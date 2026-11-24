// gof2_fsr.cpp
// Feature: docs/notes/options.md "FSR on Linux": AMD FSR 4 / 3 / 2 on Linux Vulkan players through ffx-vk-linux (AMD's
// Windows FidelityFX Vulkan DLLs, loaded natively). The C# side is LinuxFsrUpscaler (URP's upscaler framework).
//
// Unity asks Vulkan for API 1.1 and enables only the device features it uses; FSR's shaders need Vulkan 1.2+ and more
// features (float16 / int8, 8 / 16-bit storage, integer dot product, subgroup size control, descriptor indexing, separate
// depth / stencil layouts, storage images without a format, compute shader derivatives, AMD coherent memory). This plugin
// is preloaded (isPreloaded), so it can intercept
// Unity's Vulkan initialization (IUnityGraphicsVulkanV2::AddInterceptInitialization): vkCreateInstance asks for the
// newest of 1.3 and the loader's version, vkCreateDevice adds those features where the GPU has them (never the
// robustness ones: they cost performance). Nothing changes on a GPU / driver without Vulkan 1.2: FSR then stays off.
//
// It also sets RADV's radv_disable_dcc_stores (no DCC on storage images: ~0.3 ms of FSR 4 at 4K) before Unity creates its
// Vulkan instance, unless set already or GOF2_KEEP_DCC_STORES is. ffx-vk-linux's own RADV fixes (cached memory types,
// wave64, host-visible buffers in VRAM) apply inside the library.
//
// The AMD DLLs are the player's (Gof2Fsr_Init's directory: the SDK's two; the driver's amdvkffx64.dll is optional, there
// or where FFXVK_DRIVER_DLL says).
// Contexts are made, used and destroyed on the render thread, by plugin events with data blocks the C# side fills:
// create / dispatch / destroy. A destroyed context is released once Unity says the frames that used it are done.
// Windows (gof2_fsr.dll, a comparison build on Vulkan): the same plugin with AMD's DLLs loaded as they are (LoadLibrary)
// and the library's memory / wave64 changes done in the vkGetDeviceProcAddr it hands FSR.

#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "IUnityLog.h"

#ifdef _WIN32
// Windows (a comparison build, docs/notes/options.md "FSR on Linux"): AMD's Vulkan DLLs loaded as they are, with
// LoadLibrary, in place of ffx-vk-linux's PE loader: the same plugin without the bridge (under Wine: the DLLs run as
// Windows code, Vulkan through winevulkan to the same driver).
#include <windows.h>
#include <string>
#include <vulkan/vulkan.h>
#include "ffx_api.h"
#include "ffx_upscale.h"
#include "vk/ffx_api_vk.h"
namespace {
PfnFfxCreateContext p_ffxCreateContext; PfnFfxDestroyContext p_ffxDestroyContext; PfnFfxConfigure p_ffxConfigure;
PfnFfxQuery p_ffxQuery; PfnFfxDispatch p_ffxDispatch;
std::string s_loadError;
int ffxvkLoad(const char* directory)
{
    if (p_ffxCreateContext) return 0;
    HMODULE h = nullptr;
    for (const char* name : {"amd_fidelityfx_loader_vk.dll", "amd_fidelityfx_upscaler_vk.dll"})
        if (!h) h = LoadLibraryA((std::string(directory) + "\\" + name).c_str());
    if (!h) { s_loadError = "no amd_fidelityfx_loader_vk.dll / amd_fidelityfx_upscaler_vk.dll in " + std::string(directory); return 1; }
    p_ffxCreateContext = (PfnFfxCreateContext)GetProcAddress(h, "ffxCreateContext");
    p_ffxDestroyContext = (PfnFfxDestroyContext)GetProcAddress(h, "ffxDestroyContext");
    p_ffxConfigure = (PfnFfxConfigure)GetProcAddress(h, "ffxConfigure");
    p_ffxQuery = (PfnFfxQuery)GetProcAddress(h, "ffxQuery");
    p_ffxDispatch = (PfnFfxDispatch)GetProcAddress(h, "ffxDispatch");
    if (!p_ffxCreateContext || !p_ffxDestroyContext || !p_ffxQuery || !p_ffxDispatch) { s_loadError = "the DLL lacks the FFX API"; p_ffxCreateContext = nullptr; return 1; }
    return 0;
}
const char* ffxvkError() { return s_loadError.c_str(); }
int ffxvkFsr4Model(const char*) { s_loadError = "FSR 4 Lite needs ffx-vk-linux"; return 1; }   // (AMD's own loader: the GPU's model only)
}
#define ffxCreateContext p_ffxCreateContext
#define ffxDestroyContext p_ffxDestroyContext
#define ffxQuery p_ffxQuery
#define ffxDispatch p_ffxDispatch
#define EXPORT extern "C" __declspec(dllexport)
#else
#include "ffx_vk_linux.h"
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

#include <algorithm>
#include <chrono>
#include <cstdlib>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <new>
#include <string>
#include <vector>


namespace {

constexpr int kEventCreate = 0x46535201, kEventDispatch = 0x46535202, kEventDestroy = 0x46535203;
constexpr int kMaxContexts = 64;

IUnityInterfaces* s_ifaces;
IUnityGraphics* s_gfx;
IUnityGraphicsVulkanV2* s_vk;
IUnityLog* s_log;

PFN_vkGetInstanceProcAddr s_gipa;   // the loader's (or the next interceptor's)
VkInstance s_instance;
uint32_t s_instanceApi, s_requestedApi;
std::string s_hook = "Vulkan not initialized by Unity (another graphics API, or the plugin loaded too late)";
bool s_featuresOn;   // the device was made with FSR's features
bool s_vulkan;       // Unity's device is Vulkan (kUnityGfxDeviceEventInitialize)

std::mutex s_lock;
std::string s_status = "not loaded";
bool s_loaded;
struct Provider { uint64_t id; std::string name; };
std::vector<Provider> s_providers;

struct Ctx { ffxContext ctx = nullptr; bool live = false; unsigned long long lastFrame = 0; };
Ctx s_ctx[kMaxContexts];
struct Doomed { ffxContext ctx; unsigned long long frame; };
std::vector<Doomed> s_doomed;
std::string s_lastError;

// Timings (Gof2Fsr_Timings): the dispatch's GPU time by timestamps around it (read back a few frames later, never waited
// for) and its CPU time on the render thread (the texture accesses and FSR's recording).
struct Timing
{
    VkQueryPool pool = VK_NULL_HANDLE;
    static constexpr uint32_t Frames = 8;
    bool pending[Frames] = {};
    uint32_t next = 0;
    double periodNs = 0, gpuMs = 0, cpuMs = 0;
    int gpuSamples = 0, cpuSamples = 0;
    PFN_vkCmdResetQueryPool reset; PFN_vkCmdWriteTimestamp write; PFN_vkGetQueryPoolResults results; PFN_vkDestroyQueryPool destroy;
} s_time;

void Log(const char* fmt, ...)
{
    char buf[1024];
    va_list ap; va_start(ap, fmt); vsnprintf(buf, sizeof buf, fmt, ap); va_end(ap);
    if (s_log) UNITY_LOG(s_log, buf); else fprintf(stderr, "gof2_fsr: %s\n", buf);
}

// ---- Vulkan initialization: the API version and the device features ------------------------------------------------

VKAPI_ATTR VkResult VKAPI_CALL HookCreateInstance(const VkInstanceCreateInfo* ci, const VkAllocationCallbacks* alloc, VkInstance* out)
{
    auto create = (PFN_vkCreateInstance)s_gipa(nullptr, "vkCreateInstance");
    auto enumerate = (PFN_vkEnumerateInstanceVersion)s_gipa(nullptr, "vkEnumerateInstanceVersion");
    uint32_t loader = VK_API_VERSION_1_0;
    if (enumerate) enumerate(&loader);
    VkApplicationInfo app = ci->pApplicationInfo ? *ci->pApplicationInfo : VkApplicationInfo{VK_STRUCTURE_TYPE_APPLICATION_INFO};
    s_requestedApi = app.apiVersion;
    uint32_t want = std::min<uint32_t>(VK_MAKE_API_VERSION(0, 1, 3, 0), VK_MAKE_API_VERSION(0, VK_API_VERSION_MAJOR(loader), VK_API_VERSION_MINOR(loader), 0));
    VkResult r;
    if (app.apiVersion < want)
    {
        app.apiVersion = want;
        VkInstanceCreateInfo copy = *ci;
        copy.pApplicationInfo = &app;
        r = create(&copy, alloc, out);
        if (r == VK_SUCCESS) s_instanceApi = want;
        else { r = create(ci, alloc, out); s_instanceApi = s_requestedApi; }
    }
    else { r = create(ci, alloc, out); s_instanceApi = app.apiVersion; }
    if (r == VK_SUCCESS) s_instance = *out;
    return r;
}

const VkBaseOutStructure* Find(const void* chain, VkStructureType t)
{
    for (auto p = (const VkBaseOutStructure*)chain; p; p = p->pNext) if (p->sType == t) return p;
    return nullptr;
}

// Unity's structs the hook changes, as they were: put back after vkCreateDevice (whether it worked or not), so Unity's
// own request is never altered and a failed attempt can be retried as Unity made it.
struct Saved { void* at; std::vector<char> bytes; };

template <typename T> void Save(std::vector<Saved>& saved, T* p)
{
    for (auto& s : saved) if (s.at == p) return;
    saved.push_back({p, std::vector<char>((char*)p, (char*)p + sizeof(T))});
}

// A feature struct of Unity's chain to set bits in (saved first), else ours (added to the chain): never two of one sType,
// and never an individual struct next to the aggregate that covers it (VkPhysicalDeviceVulkan1xFeatures).
template <typename T> T* Slot(std::vector<VkBaseOutStructure*>& chain, std::vector<void*>& owned, std::vector<Saved>& saved, VkStructureType t)
{
    for (auto* p : chain) if (p->sType == t) { Save(saved, (T*)p); return (T*)p; }
    auto* s = new (operator new(sizeof(T))) T{};   // (freed with operator delete: plain structs)
    s->sType = t;
    owned.push_back(s);
    chain.push_back((VkBaseOutStructure*)s);
    return s;
}

VKAPI_ATTR VkResult VKAPI_CALL HookCreateDevice(VkPhysicalDevice gpu, const VkDeviceCreateInfo* ci, const VkAllocationCallbacks* alloc, VkDevice* out)
{
    auto create = (PFN_vkCreateDevice)s_gipa(s_instance, "vkCreateDevice");
    auto props2 = (PFN_vkGetPhysicalDeviceProperties)s_gipa(s_instance, "vkGetPhysicalDeviceProperties");
    auto feats2 = (PFN_vkGetPhysicalDeviceFeatures2)s_gipa(s_instance, "vkGetPhysicalDeviceFeatures2");
    auto exts = (PFN_vkEnumerateDeviceExtensionProperties)s_gipa(s_instance, "vkEnumerateDeviceExtensionProperties");
    VkPhysicalDeviceProperties props{};
    props2(gpu, &props);
    uint32_t api = std::min(props.apiVersion, s_instanceApi);
    char buf[256];
    if (api < VK_MAKE_API_VERSION(0, 1, 2, 0) || !feats2 || !exts)
    {
        snprintf(buf, sizeof buf, "Vulkan %u.%u on %s: FSR needs 1.2", VK_API_VERSION_MAJOR(api), VK_API_VERSION_MINOR(api), props.deviceName);
        s_hook = buf;
        return create(gpu, ci, alloc, out);
    }
    bool v13 = api >= VK_MAKE_API_VERSION(0, 1, 3, 0);

    uint32_t n = 0;
    exts(gpu, nullptr, &n, nullptr);
    std::vector<VkExtensionProperties> available(n);
    exts(gpu, nullptr, &n, available.data());
    auto has = [&](const char* e) { return std::any_of(available.begin(), available.end(), [&](auto& x) { return !strcmp(x.extensionName, e); }); };

    // What the GPU supports.
    VkPhysicalDeviceComputeShaderDerivativesFeaturesKHR sDeriv{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_COMPUTE_SHADER_DERIVATIVES_FEATURES_KHR};
    VkPhysicalDeviceCoherentMemoryFeaturesAMD sCoherent{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_COHERENT_MEMORY_FEATURES_AMD, &sDeriv};
    VkPhysicalDeviceShaderIntegerDotProductFeatures sDot{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_INTEGER_DOT_PRODUCT_FEATURES, &sCoherent};
    VkPhysicalDeviceSubgroupSizeControlFeatures sSubgroup{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SUBGROUP_SIZE_CONTROL_FEATURES, &sDot};
    VkPhysicalDeviceVulkan12Features s12{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES, &sSubgroup};
    VkPhysicalDeviceVulkan11Features s11{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES, &s12};
    VkPhysicalDeviceFeatures2 s{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &s11};
    bool hasDeriv = has(VK_KHR_COMPUTE_SHADER_DERIVATIVES_EXTENSION_NAME), hasCoherent = has(VK_AMD_DEVICE_COHERENT_MEMORY_EXTENSION_NAME);
    bool hasDot = v13 || has(VK_KHR_SHADER_INTEGER_DOT_PRODUCT_EXTENSION_NAME), hasSubgroup = v13 || has(VK_EXT_SUBGROUP_SIZE_CONTROL_EXTENSION_NAME);
    feats2(gpu, &s);
    if (!hasDeriv) sDeriv = {};
    if (!hasCoherent) sCoherent = {};
    if (!hasDot) sDot = {};
    if (!hasSubgroup) sSubgroup = {};

    // Unity's chain, copied (the structs stay Unity's: only bits are added to them).
    VkDeviceCreateInfo copy = *ci;
    std::vector<VkBaseOutStructure*> chain;
    for (auto p = (VkBaseOutStructure*)ci->pNext; p; p = p->pNext) chain.push_back(p);
    std::vector<void*> owned;
    std::vector<Saved> saved;
    for (auto* p : chain) saved.push_back({&p->pNext, std::vector<char>((char*)&p->pNext, (char*)&p->pNext + sizeof(void*))});
    // The core features: Unity's VkPhysicalDeviceFeatures2 in the chain, else its pEnabledFeatures (copied).
    VkPhysicalDeviceFeatures core = ci->pEnabledFeatures ? *ci->pEnabledFeatures : VkPhysicalDeviceFeatures{};
    VkPhysicalDeviceFeatures* f = nullptr;
    for (auto* p : chain)
        if (p->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2) { Save(saved, (VkPhysicalDeviceFeatures2*)p); f = &((VkPhysicalDeviceFeatures2*)p)->features; }
    if (!f) { f = &core; copy.pEnabledFeatures = &core; }
    f->shaderStorageImageReadWithoutFormat |= s.features.shaderStorageImageReadWithoutFormat;
    f->shaderStorageImageWriteWithoutFormat |= s.features.shaderStorageImageWriteWithoutFormat;
    f->shaderInt16 |= s.features.shaderInt16;
    f->shaderInt64 |= s.features.shaderInt64;
    f->shaderImageGatherExtended |= s.features.shaderImageGatherExtended;

    auto* agg11 = (VkPhysicalDeviceVulkan11Features*)Find(ci->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES);
    auto* agg12 = (VkPhysicalDeviceVulkan12Features*)Find(ci->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES);
    auto* agg13 = (VkPhysicalDeviceVulkan13Features*)Find(ci->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES);
    if (agg11) Save(saved, agg11);
    if (agg12) Save(saved, agg12);
    if (agg13) Save(saved, agg13);
    // 1.1: 16-bit storage
    if (agg11)
    {
        agg11->storageBuffer16BitAccess |= s11.storageBuffer16BitAccess;
        agg11->uniformAndStorageBuffer16BitAccess |= s11.uniformAndStorageBuffer16BitAccess;
    }
    else
    {
        auto* x = Slot<VkPhysicalDevice16BitStorageFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES);
        x->storageBuffer16BitAccess |= s11.storageBuffer16BitAccess;
        x->uniformAndStorageBuffer16BitAccess |= s11.uniformAndStorageBuffer16BitAccess;
    }
    // 1.2: float16 / int8, 8-bit storage, subgroup extended types, the memory model, separate depth / stencil layouts,
    // descriptor indexing
    if (agg12)
    {
        agg12->shaderFloat16 |= s12.shaderFloat16; agg12->shaderInt8 |= s12.shaderInt8;
        agg12->storageBuffer8BitAccess |= s12.storageBuffer8BitAccess; agg12->uniformAndStorageBuffer8BitAccess |= s12.uniformAndStorageBuffer8BitAccess;
        agg12->shaderSubgroupExtendedTypes |= s12.shaderSubgroupExtendedTypes;
        agg12->vulkanMemoryModel |= s12.vulkanMemoryModel; agg12->vulkanMemoryModelDeviceScope |= s12.vulkanMemoryModelDeviceScope;
        agg12->separateDepthStencilLayouts |= s12.separateDepthStencilLayouts;
        agg12->descriptorBindingPartiallyBound |= s12.descriptorBindingPartiallyBound;
        agg12->runtimeDescriptorArray |= s12.runtimeDescriptorArray;
        agg12->descriptorBindingVariableDescriptorCount |= s12.descriptorBindingVariableDescriptorCount;
        agg12->shaderSampledImageArrayNonUniformIndexing |= s12.shaderSampledImageArrayNonUniformIndexing;
        agg12->shaderStorageImageArrayNonUniformIndexing |= s12.shaderStorageImageArrayNonUniformIndexing;
        agg12->shaderStorageBufferArrayNonUniformIndexing |= s12.shaderStorageBufferArrayNonUniformIndexing;
    }
    else
    {
        auto* a = Slot<VkPhysicalDeviceShaderFloat16Int8Features>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES);
        a->shaderFloat16 |= s12.shaderFloat16; a->shaderInt8 |= s12.shaderInt8;
        auto* b = Slot<VkPhysicalDevice8BitStorageFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_8BIT_STORAGE_FEATURES);
        b->storageBuffer8BitAccess |= s12.storageBuffer8BitAccess; b->uniformAndStorageBuffer8BitAccess |= s12.uniformAndStorageBuffer8BitAccess;
        auto* c = Slot<VkPhysicalDeviceShaderSubgroupExtendedTypesFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_SUBGROUP_EXTENDED_TYPES_FEATURES);
        c->shaderSubgroupExtendedTypes |= s12.shaderSubgroupExtendedTypes;
        auto* d = Slot<VkPhysicalDeviceVulkanMemoryModelFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_MEMORY_MODEL_FEATURES);
        d->vulkanMemoryModel |= s12.vulkanMemoryModel; d->vulkanMemoryModelDeviceScope |= s12.vulkanMemoryModelDeviceScope;
        // (FSR 4's barriers on a depth / stencil image name its depth aspect alone; its descriptor set layouts bind
        // storage buffers partially bound)
        Slot<VkPhysicalDeviceSeparateDepthStencilLayoutsFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SEPARATE_DEPTH_STENCIL_LAYOUTS_FEATURES)
            ->separateDepthStencilLayouts |= s12.separateDepthStencilLayouts;
        auto* e = Slot<VkPhysicalDeviceDescriptorIndexingFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DESCRIPTOR_INDEXING_FEATURES);
        e->descriptorBindingPartiallyBound |= s12.descriptorBindingPartiallyBound;
        e->runtimeDescriptorArray |= s12.runtimeDescriptorArray;
        e->descriptorBindingVariableDescriptorCount |= s12.descriptorBindingVariableDescriptorCount;
        e->shaderSampledImageArrayNonUniformIndexing |= s12.shaderSampledImageArrayNonUniformIndexing;
        e->shaderStorageImageArrayNonUniformIndexing |= s12.shaderStorageImageArrayNonUniformIndexing;
        e->shaderStorageBufferArrayNonUniformIndexing |= s12.shaderStorageBufferArrayNonUniformIndexing;
    }
    // 1.3 (or their extensions): integer dot product, subgroup size control
    if (agg13)
    {
        agg13->shaderIntegerDotProduct |= sDot.shaderIntegerDotProduct;
        agg13->subgroupSizeControl |= sSubgroup.subgroupSizeControl; agg13->computeFullSubgroups |= sSubgroup.computeFullSubgroups;
    }
    else
    {
        if (hasDot)
            Slot<VkPhysicalDeviceShaderIntegerDotProductFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_INTEGER_DOT_PRODUCT_FEATURES)
                ->shaderIntegerDotProduct |= sDot.shaderIntegerDotProduct;
        if (hasSubgroup)
        {
            auto* x = Slot<VkPhysicalDeviceSubgroupSizeControlFeatures>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SUBGROUP_SIZE_CONTROL_FEATURES);
            x->subgroupSizeControl |= sSubgroup.subgroupSizeControl; x->computeFullSubgroups |= sSubgroup.computeFullSubgroups;
        }
    }
    if (hasDeriv)
    {
        auto* x = Slot<VkPhysicalDeviceComputeShaderDerivativesFeaturesKHR>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_COMPUTE_SHADER_DERIVATIVES_FEATURES_KHR);
        x->computeDerivativeGroupQuads |= sDeriv.computeDerivativeGroupQuads; x->computeDerivativeGroupLinear |= sDeriv.computeDerivativeGroupLinear;
    }
    if (hasCoherent)
        Slot<VkPhysicalDeviceCoherentMemoryFeaturesAMD>(chain, owned, saved, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_COHERENT_MEMORY_FEATURES_AMD)
            ->deviceCoherentMemory |= sCoherent.deviceCoherentMemory;
    for (size_t i = 0; i < chain.size(); i++) chain[i]->pNext = i + 1 < chain.size() ? chain[i + 1] : nullptr;
    copy.pNext = chain.empty() ? nullptr : chain[0];

    // The extensions: Unity's, and those the added features belong to (core ones only below their version).
    std::vector<const char*> names(ci->ppEnabledExtensionNames, ci->ppEnabledExtensionNames + ci->enabledExtensionCount);
    auto add = [&](const char* e) { if (has(e) && std::none_of(names.begin(), names.end(), [&](const char* x) { return !strcmp(x, e); })) names.push_back(e); };
    if (!v13) { add(VK_KHR_SHADER_INTEGER_DOT_PRODUCT_EXTENSION_NAME); add(VK_EXT_SUBGROUP_SIZE_CONTROL_EXTENSION_NAME); }
    if (hasDeriv) add(VK_KHR_COMPUTE_SHADER_DERIVATIVES_EXTENSION_NAME);
    if (hasCoherent) add(VK_AMD_DEVICE_COHERENT_MEMORY_EXTENSION_NAME);
    copy.enabledExtensionCount = (uint32_t)names.size();
    copy.ppEnabledExtensionNames = names.data();

    VkResult r = create(gpu, &copy, alloc, out);
    // (the structs are only read during the call: Unity's put back as they were, ours freed)
    for (auto& sv : saved) memcpy(sv.at, sv.bytes.data(), sv.bytes.size());
    for (void* p : owned) operator delete(p);
    if (r != VK_SUCCESS)
    {
        snprintf(buf, sizeof buf, "the device with FSR's features failed (%d): Unity's own request instead", r);
        s_hook = buf;
        return create(gpu, ci, alloc, out);
    }
    s_featuresOn = true;
    snprintf(buf, sizeof buf, "Vulkan %u.%u (Unity asked for %u.%u) on %s; FSR features on (fp16 %u, int8 %u, dot %u, subgroup size %u, derivatives %u, coherent %u)",
             VK_API_VERSION_MAJOR(api), VK_API_VERSION_MINOR(api), VK_API_VERSION_MAJOR(s_requestedApi), VK_API_VERSION_MINOR(s_requestedApi),
             props.deviceName, s12.shaderFloat16, s12.shaderInt8, sDot.shaderIntegerDotProduct, sSubgroup.subgroupSizeControl,
             sDeriv.computeDerivativeGroupQuads, sCoherent.deviceCoherentMemory);
    s_hook = buf;
    return r;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL HookGetInstanceProcAddr(VkInstance instance, const char* name)
{
    if (!strcmp(name, "vkCreateInstance")) return (PFN_vkVoidFunction)HookCreateInstance;
    if (!strcmp(name, "vkCreateDevice")) return (PFN_vkVoidFunction)HookCreateDevice;
    return s_gipa(instance, name);
}

PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API OnVulkanInit(PFN_vkGetInstanceProcAddr gipa, void*)
{
    s_gipa = gipa;
    return HookGetInstanceProcAddr;
}

// ---- FSR's Vulkan functions: memory types, wave size (Windows build) ------------------------------------------------
// (On Linux ffx-vk-linux does this in its own Vulkan wrappers, for every program: FsrGdpa hands FSR the real
// vkGetDeviceProcAddr there. The Windows comparison build loads AMD's DLLs without it, so it does the same here.)
// AMD's FSR code takes RADV's "device coherent, device uncached" memory types (VK_AMD_device_coherent_memory: listed by
// RADV whether or not the feature is on) for ~300 MB of its buffers per 4K context; the GPU's caches skip them, and FSR 4's network
// passes ran 4-5x slower than the same work through vkd3d-proton (which picks its own memory types). FSR gets this
// vkGetDeviceProcAddr: its vkAllocateMemory takes the cached type with the same other properties instead.

PFN_vkGetDeviceProcAddr s_realGdpa;
PFN_vkAllocateMemory s_realAllocate;
VkPhysicalDeviceMemoryProperties s_memory;
bool s_cachedMemory = true;
uint64_t s_remapped;

VKAPI_ATTR VkResult VKAPI_CALL FsrAllocateMemory(VkDevice device, const VkMemoryAllocateInfo* info, const VkAllocationCallbacks* a, VkDeviceMemory* out)
{
    const VkMemoryPropertyFlags amd = VK_MEMORY_PROPERTY_DEVICE_COHERENT_BIT_AMD | VK_MEMORY_PROPERTY_DEVICE_UNCACHED_BIT_AMD;
    uint32_t t = info->memoryTypeIndex;
    if (s_cachedMemory && t < s_memory.memoryTypeCount && (s_memory.memoryTypes[t].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_UNCACHED_BIT_AMD))
    {
        VkMemoryPropertyFlags want = s_memory.memoryTypes[t].propertyFlags & ~amd;
        for (uint32_t i = 0; i < s_memory.memoryTypeCount; i++)
            if (s_memory.memoryTypes[i].propertyFlags == want && s_memory.memoryTypes[i].heapIndex == s_memory.memoryTypes[t].heapIndex)
            {
                VkMemoryAllocateInfo copy = *info;
                copy.memoryTypeIndex = i;
                s_remapped += info->allocationSize;
                return s_realAllocate(device, &copy, a, out);
            }
    }
    return s_realAllocate(device, info, a, out);
}

// Wave64 for FSR's compute shaders whose workgroup size is a multiple of 64 and that use no subgroup operations (their
// results can't depend on the wave size): RADV compiles them as wave32 unless asked; vkd3d-proton asks for 64 for the same
// passes of the D3D12 version; an explicit wave32 request on such a shader is replaced too. Checked: ffx-vk-linux's probe
// gives byte-identical FSR 4 output with both this and the cached memory; at 4K on an RX 7900 XTX, FSR 4 went from 4.3 to
// 3.2 ms (memory) to 2.7 ms (wave64). $GOF2_FSR_WAVE64=0 / $GOF2_FSR_UNCACHED=1 put AMD's choices back.
PFN_vkCreateShaderModule s_realCreateModule;
PFN_vkCreateComputePipelines s_realCreatePipelines;
bool s_wave64 = true;
std::mutex s_modulesLock;
std::vector<std::pair<VkShaderModule, bool>> s_wave64Modules;   // module, may run as wave64
uint32_t s_wave64Pipelines, s_pipelinesSeen, s_pipelinesAsked32, s_pipelinesUnsafe, s_pipelinesUnknownModule;

bool Wave64Safe(const uint32_t* w, size_t n)
{
    uint32_t total = 0;
    for (size_t i = 5; i < n;)
    {
        uint32_t op = w[i] & 0xffff, c = w[i] >> 16;
        if (!c || i + c > n) return false;
        if (op == 17 && w[i + 1] >= 61 && w[i + 1] <= 69) return false;                 // GroupNonUniform*
        if (op == 17 && (w[i + 1] == 4423 || w[i + 1] == 5568 || w[i + 1] == 5569)) return false;   // SubgroupBallotKHR / shuffle INTEL
        if (op == 16 && c >= 6 && w[i + 2] == 17) total = w[i + 3] * w[i + 4] * w[i + 5];   // ExecutionMode LocalSize
        if (op == 331) return false;                                                     // LocalSizeId: unknown here
        i += c;
    }
    return total && total % 64 == 0;
}

VKAPI_ATTR VkResult VKAPI_CALL FsrCreateShaderModule(VkDevice device, const VkShaderModuleCreateInfo* ci, const VkAllocationCallbacks* a, VkShaderModule* out)
{
    VkResult r = s_realCreateModule(device, ci, a, out);
    if (r == VK_SUCCESS && s_wave64)
    {
        std::lock_guard<std::mutex> l(s_modulesLock);
        s_wave64Modules.push_back({*out, Wave64Safe(ci->pCode, ci->codeSize / 4)});
    }
    return r;
}

VKAPI_ATTR VkResult VKAPI_CALL FsrCreateComputePipelines(VkDevice device, VkPipelineCache cache, uint32_t n, const VkComputePipelineCreateInfo* ci,
                                                         const VkAllocationCallbacks* a, VkPipeline* out)
{
    if (!s_wave64) return s_realCreatePipelines(device, cache, n, ci, a, out);
    std::vector<VkComputePipelineCreateInfo> copy(ci, ci + n);
    std::vector<VkPipelineShaderStageRequiredSubgroupSizeCreateInfo> sizes(n);
    for (uint32_t i = 0; i < n; i++)
    {
        s_pipelinesSeen++;
        bool safe = false, known = false;
        {
            std::lock_guard<std::mutex> l(s_modulesLock);
            for (auto& [m, ok] : s_wave64Modules) if (m == ci[i].stage.module) { safe = ok; known = true; }
        }
        if (!known && !ci[i].stage.module)   // (the code inline, VK_KHR_maintenance5)
            for (auto* x = (const VkBaseInStructure*)ci[i].stage.pNext; x; x = x->pNext)
                if (x->sType == VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO) { auto* m = (const VkShaderModuleCreateInfo*)x; safe = Wave64Safe(m->pCode, m->codeSize / 4); known = true; }
        if (!known) { s_pipelinesUnknownModule++; continue; }
        if (!safe) { s_pipelinesUnsafe++; continue; }
        // an explicit wave32 request is replaced (the shader can't tell); anything else asked for stays
        auto* asked = (VkPipelineShaderStageRequiredSubgroupSizeCreateInfo*)nullptr;
        for (auto* x = (const VkBaseInStructure*)ci[i].stage.pNext; x; x = x->pNext)
            if (x->sType == VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_REQUIRED_SUBGROUP_SIZE_CREATE_INFO) asked = (VkPipelineShaderStageRequiredSubgroupSizeCreateInfo*)x;
        if (asked && asked->requiredSubgroupSize != 32) continue;
        if (asked) s_pipelinesAsked32++;
        copy[i].stage.flags &= ~VK_PIPELINE_SHADER_STAGE_CREATE_ALLOW_VARYING_SUBGROUP_SIZE_BIT;
        sizes[i] = {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_REQUIRED_SUBGROUP_SIZE_CREATE_INFO, (void*)ci[i].stage.pNext, 64};
        if (asked)   // (its own struct stays in the chain: ours goes in its place, in a copy of the chain head only)
        {
            // rebuild the stage's pNext without the asked struct
            std::vector<const VkBaseInStructure*> rest;
            for (auto* x = (const VkBaseInStructure*)ci[i].stage.pNext; x; x = x->pNext) if ((const void*)x != (const void*)asked) rest.push_back(x);
            // only a chain of the size struct alone is rewritten safely here (no copies of unknown structs)
            if (!rest.empty()) continue;
            sizes[i].pNext = nullptr;
        }
        copy[i].stage.pNext = &sizes[i];
        s_wave64Pipelines++;
    }
    return s_realCreatePipelines(device, cache, n, copy.data(), a, out);
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FsrGetDeviceProcAddr(VkDevice device, const char* name)
{
    if (!strcmp(name, "vkAllocateMemory")) return (PFN_vkVoidFunction)FsrAllocateMemory;
    if (!strcmp(name, "vkCreateShaderModule")) return (PFN_vkVoidFunction)FsrCreateShaderModule;
    if (!strcmp(name, "vkCreateComputePipelines")) return (PFN_vkVoidFunction)FsrCreateComputePipelines;
    return s_realGdpa(device, name);
}

/// The vkGetDeviceProcAddr FSR gets (FsrGetDeviceProcAddr, set up once).
PFN_vkGetDeviceProcAddr FsrGdpa()
{
#ifndef _WIN32
    UnityVulkanInstance vi = s_vk->Instance();
    return (PFN_vkGetDeviceProcAddr)vi.getInstanceProcAddr(vi.instance, "vkGetDeviceProcAddr");
#endif
    if (!s_realGdpa)
    {
        UnityVulkanInstance vi = s_vk->Instance();
        s_realGdpa = (PFN_vkGetDeviceProcAddr)vi.getInstanceProcAddr(vi.instance, "vkGetDeviceProcAddr");
        s_realAllocate = (PFN_vkAllocateMemory)s_realGdpa(vi.device, "vkAllocateMemory");
        s_realCreateModule = (PFN_vkCreateShaderModule)s_realGdpa(vi.device, "vkCreateShaderModule");
        s_realCreatePipelines = (PFN_vkCreateComputePipelines)s_realGdpa(vi.device, "vkCreateComputePipelines");
        if (const char* e = getenv("GOF2_FSR_WAVE64")) s_wave64 = e[0] != '0';   // (on unless "0")
        auto props = (PFN_vkGetPhysicalDeviceMemoryProperties)vi.getInstanceProcAddr(vi.instance, "vkGetPhysicalDeviceMemoryProperties");
        if (props) props(vi.physicalDevice, &s_memory);
        if (const char* e = getenv("GOF2_FSR_UNCACHED")) s_cachedMemory = e[0] == '0';   // (1: AMD's choice kept, as ffx-vk-linux's FFXVK_UNCACHED)
    }
    return FsrGetDeviceProcAddr;
}

// ---- contexts and dispatches (render thread) -------------------------------------------------------------------------

#pragma pack(push, 8)
struct CreateData   // LinuxFsrUpscaler.CreateData
{
    int32_t handle;
    int32_t major;   // 4 / 3 / 2: the provider
    uint32_t maxRenderWidth, maxRenderHeight, displayWidth, displayHeight;
    uint32_t flags;  // FFX_UPSCALE_ENABLE_*
    int32_t lite;    // FSR 4: 1 = FSR 4 Lite (ffx-vk-linux's ffxvkFsr4Model), 0 the GPU's own model
};
struct DispatchData   // LinuxFsrUpscaler.DispatchData
{
    int32_t handle;
    int32_t reset;
    void* color;
    void* depth;
    void* depthBuffer;   // the depth render buffer (a depth-only render texture: its texture pointer may be the color)
    void* motion;
    void* output;
    float jitterX, jitterY, motionScaleX, motionScaleY;
    uint32_t renderWidth, renderHeight, upscaleWidth, upscaleHeight;
    int32_t sharpen;
    float sharpness, frameTimeMs, preExposure, cameraNear, cameraFar, fovVertical;
};
struct DestroyData { int32_t handle; };
#pragma pack(pop)

bool s_liteMade;   // the model of the last FSR 4 context (what ffx-vk-linux is set to)

const Provider* ProviderFor(int major)
{
    for (auto& p : s_providers) if (!p.name.empty() && p.name[0] == '0' + major && p.name.size() > 1 && p.name[1] == '.') return &p;
    return nullptr;
}

void ReleaseDoomed(unsigned long long safe, bool all)
{
    for (size_t i = 0; i < s_doomed.size();)
        if (all || s_doomed[i].frame <= safe) { ffxDestroyContext(&s_doomed[i].ctx, nullptr); s_doomed.erase(s_doomed.begin() + i); }
        else i++;
}

void Message(uint32_t type, const wchar_t* message)
{
    char buf[512];
    snprintf(buf, sizeof buf, "FSR %s: %ls", type == FFX_API_MESSAGE_TYPE_ERROR ? "error" : "warning", message);
    s_lastError = buf;
    Log("%s", buf);
}

void Create(const CreateData& d)
{
    if (d.handle < 0 || d.handle >= kMaxContexts || !s_loaded) return;
    auto& c = s_ctx[d.handle];
    UnityVulkanRecordingState rs{};
    s_vk->CommandRecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare);
    if (c.live) { s_doomed.push_back({c.ctx, c.lastFrame}); c = {}; }
    const Provider* p = ProviderFor(d.major);
    if (!p) { Log("FSR %d: no such provider", d.major); return; }
    UnityVulkanInstance vi = s_vk->Instance();
    auto gdpa = FsrGdpa();
    // FSR 4's model for this context (the option "FSR 4 Lite"): set for the contexts made from here on, on the thread
    // that makes them; one that can't be had (the driver DLL not loadable a second time) leaves the model as it was
    bool lite = false;
    if (d.major == 4)
    {
        lite = d.lite != 0;
        if (ffxvkFsr4Model(lite ? "lite" : nullptr) != 0)
        {
            Log("FSR 4%s: %s", lite ? " Lite" : "", ffxvkError());
            lite = s_liteMade;
        }
        s_liteMade = lite;
    }
    ffxCreateContextDescUpscaleVersion version{};
    version.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE_VERSION;
    version.version = FFX_UPSCALER_VERSION;
    ffxOverrideVersion override{};
    override.header.type = FFX_API_DESC_TYPE_OVERRIDE_VERSION;
    override.header.pNext = &version.header;
    override.versionId = p->id;
    ffxCreateBackendVKDesc backend{};
    backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_VK;
    backend.header.pNext = &override.header;
    backend.deviceContext = {vi.device, vi.physicalDevice, gdpa};
    ffxCreateContextDescUpscale up{};
    up.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
    up.header.pNext = &backend.header;
    up.maxRenderSize = {d.maxRenderWidth, d.maxRenderHeight};
    up.maxUpscaleSize = {d.displayWidth, d.displayHeight};
    up.flags = d.flags;
    up.fpMessage = Message;
    ffxContext ctx = nullptr;
    ffxReturnCode_t r = ffxCreateContext(&ctx, &up.header, nullptr);
    if (r != FFX_API_RETURN_OK) { Log("FSR %s: context %ux%u -> %ux%u failed (%u)", p->name.c_str(), d.maxRenderWidth, d.maxRenderHeight, d.displayWidth, d.displayHeight, r); return; }
    c.ctx = ctx; c.live = true; c.lastFrame = rs.currentFrameNumber;
    Log("FSR %s%s context %d: %ux%u -> %ux%u, flags 0x%x", p->name.c_str(), lite ? " Lite" : "", d.handle, d.maxRenderWidth, d.maxRenderHeight, d.displayWidth, d.displayHeight, d.flags);
}

bool Image(void* tex, void* buffer, bool write, uint32_t extraUsage, FfxApiResource& out)
{
    UnityVulkanImage img{};
    VkImageLayout layout = write ? VK_IMAGE_LAYOUT_GENERAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    VkAccessFlags access = write ? (VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT) : VK_ACCESS_SHADER_READ_BIT;
    bool ok = tex && s_vk->AccessTexture(tex, UnityVulkanWholeImage, layout, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, access, kUnityVulkanResourceAccess_PipelineBarrier, &img);
    if ((!ok || (buffer && !(img.aspect & VK_IMAGE_ASPECT_DEPTH_BIT))) && buffer)
        ok = s_vk->AccessRenderBufferTexture((UnityRenderBuffer)buffer, UnityVulkanWholeImage, layout, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, access,
                                             kUnityVulkanResourceAccess_PipelineBarrier, &img);
    if (!ok || !img.image) return false;
    VkImageCreateInfo info{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
    info.imageType = img.type; info.format = img.format; info.extent = img.extent; info.mipLevels = (uint32_t)img.mipCount;
    info.arrayLayers = (uint32_t)img.layers; info.samples = img.samples; info.tiling = img.tiling; info.usage = img.usage;
    out = ffxApiGetResourceVK((void*)img.image, ffxApiGetImageResourceDescriptionVK(img.image, info, extraUsage),
                              write ? FFX_API_RESOURCE_STATE_UNORDERED_ACCESS : FFX_API_RESOURCE_STATE_PIXEL_COMPUTE_READ);
    return true;
}

bool TimingInit()
{
    if (s_time.pool) return true;
    UnityVulkanInstance vi = s_vk->Instance();
    auto gdpa = (PFN_vkGetDeviceProcAddr)vi.getInstanceProcAddr(vi.instance, "vkGetDeviceProcAddr");
    auto props = (PFN_vkGetPhysicalDeviceProperties)vi.getInstanceProcAddr(vi.instance, "vkGetPhysicalDeviceProperties");
    auto create = (PFN_vkCreateQueryPool)gdpa(vi.device, "vkCreateQueryPool");
    s_time.reset = (PFN_vkCmdResetQueryPool)gdpa(vi.device, "vkCmdResetQueryPool");
    s_time.write = (PFN_vkCmdWriteTimestamp)gdpa(vi.device, "vkCmdWriteTimestamp");
    s_time.results = (PFN_vkGetQueryPoolResults)gdpa(vi.device, "vkGetQueryPoolResults");
    s_time.destroy = (PFN_vkDestroyQueryPool)gdpa(vi.device, "vkDestroyQueryPool");
    VkPhysicalDeviceProperties p{};
    props(vi.physicalDevice, &p);
    s_time.periodNs = p.limits.timestampPeriod;
    VkQueryPoolCreateInfo qi{VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO};
    qi.queryType = VK_QUERY_TYPE_TIMESTAMP;
    qi.queryCount = Timing::Frames * 2;
    return create && s_time.reset && s_time.write && s_time.results && create(vi.device, &qi, nullptr, &s_time.pool) == VK_SUCCESS;
}

void Dispatch(const DispatchData& d)
{
    if (d.handle < 0 || d.handle >= kMaxContexts || !s_ctx[d.handle].live) return;
    auto cpuStart = std::chrono::steady_clock::now();
    auto& c = s_ctx[d.handle];
    ffxDispatchDescUpscale up{};
    up.header.type = FFX_API_DISPATCH_DESC_TYPE_UPSCALE;
    if (!Image(d.color, nullptr, false, 0, up.color) || !Image(d.depth, d.depthBuffer, false, 0, up.depth) ||
        !Image(d.motion, nullptr, false, 0, up.motionVectors) || !Image(d.output, nullptr, true, FFX_API_RESOURCE_USAGE_UAV, up.output))
    {
        static int warned;
        if (warned++ < 5) Log("FSR dispatch: a texture couldn't be accessed (color %p depth %p/%p motion %p output %p)", d.color, d.depth, d.depthBuffer, d.motion, d.output);
        return;
    }
    UnityVulkanRecordingState rs{};   // (after the accesses: they may record barriers)
    if (!s_vk->CommandRecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare) || !rs.commandBuffer) return;
    ReleaseDoomed(rs.safeFrameNumber, false);
    up.commandList = rs.commandBuffer;
    up.jitterOffset = {d.jitterX, d.jitterY};
    up.motionVectorScale = {d.motionScaleX, d.motionScaleY};
    up.renderSize = {d.renderWidth, d.renderHeight};
    up.upscaleSize = {d.upscaleWidth, d.upscaleHeight};
    up.enableSharpening = d.sharpen != 0;
    up.sharpness = d.sharpness;
    up.frameTimeDelta = d.frameTimeMs;
    up.preExposure = d.preExposure > 0 ? d.preExposure : 1.0f;
    up.reset = d.reset != 0;
    up.cameraNear = d.cameraNear;
    up.cameraFar = d.cameraFar;
    up.cameraFovAngleVertical = d.fovVertical;
    up.viewSpaceToMetersFactor = 1.0f;
    // GPU time: the slot's earlier result (if it's there yet), then timestamps around this dispatch
    uint32_t slot = UINT32_MAX;
    if (TimingInit())
    {
        slot = s_time.next;
        s_time.next = (s_time.next + 1) % Timing::Frames;
        UnityVulkanInstance vi = s_vk->Instance();
        if (s_time.pending[slot])
        {
            uint64_t t[2];
            if (s_time.results(vi.device, s_time.pool, slot * 2, 2, sizeof t, t, sizeof(uint64_t), VK_QUERY_RESULT_64_BIT) == VK_SUCCESS)
            {
                s_time.gpuMs += (t[1] - t[0]) * s_time.periodNs / 1e6;
                s_time.gpuSamples++;
            }
            s_time.pending[slot] = false;
        }
        s_time.reset((VkCommandBuffer)rs.commandBuffer, s_time.pool, slot * 2, 2);
        s_time.write((VkCommandBuffer)rs.commandBuffer, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, s_time.pool, slot * 2);
    }
    ffxReturnCode_t r = ffxDispatch(&c.ctx, &up.header);
    if (slot != UINT32_MAX)
    {
        s_time.write((VkCommandBuffer)rs.commandBuffer, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, s_time.pool, slot * 2 + 1);
        s_time.pending[slot] = true;
    }
    s_time.cpuMs += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - cpuStart).count();
    s_time.cpuSamples++;
    c.lastFrame = rs.currentFrameNumber;
    if (r != FFX_API_RETURN_OK)
    {
        static int warned;
        if (warned++ < 5) Log("FSR dispatch failed (%u)", r);
    }
}

void Destroy(const DestroyData& d)
{
    if (d.handle < 0 || d.handle >= kMaxContexts || !s_ctx[d.handle].live) return;
    UnityVulkanRecordingState rs{};
    s_vk->CommandRecordingState(&rs, kUnityVulkanGraphicsQueueAccess_DontCare);
    // (used up to its last frame, plus this one's commands recorded so far)
    s_doomed.push_back({s_ctx[d.handle].ctx, std::max(s_ctx[d.handle].lastFrame, rs.currentFrameNumber)});
    s_ctx[d.handle] = {};
    ReleaseDoomed(rs.safeFrameNumber, false);
}

void UNITY_INTERFACE_API OnEvent(int id, void* data)
{
    if (!s_vulkan || !data) return;
    std::lock_guard<std::mutex> g(s_lock);
    switch (id)
    {
    case kEventCreate: Create(*(const CreateData*)data); break;
    case kEventDispatch: Dispatch(*(const DispatchData*)data); break;
    case kEventDestroy: Destroy(*(const DestroyData*)data); break;
    }
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType type)
{
    if (type == kUnityGfxDeviceEventInitialize)
    {
        s_vulkan = s_vk && s_gfx && s_gfx->GetRenderer() == kUnityGfxRendererVulkan;
        if (!s_vulkan) return;
        // (no EnsurePreviousFrameSubmission: the events don't depend on the previous frame's submission; with it or
        // without, FSR's cost measured the same)
        const uint32_t flags = kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;
        UnityVulkanPluginEventConfig cfg{kUnityVulkanRenderPass_EnsureOutside, kUnityVulkanGraphicsQueueAccess_DontCare, flags};
        s_vk->ConfigureEvent(kEventCreate, &cfg);
        s_vk->ConfigureEvent(kEventDispatch, &cfg);
        s_vk->ConfigureEvent(kEventDestroy, &cfg);
        return;
    }
    if (type != kUnityGfxDeviceEventShutdown || !s_vulkan) return;
    std::lock_guard<std::mutex> g(s_lock);
    UnityVulkanInstance vi = s_vk->Instance();
    if (vi.device)
    {
        auto wait = (PFN_vkDeviceWaitIdle)((PFN_vkGetDeviceProcAddr)vi.getInstanceProcAddr(vi.instance, "vkGetDeviceProcAddr"))(vi.device, "vkDeviceWaitIdle");
        if (wait) wait(vi.device);
    }
    for (auto& c : s_ctx) if (c.live) { ffxDestroyContext(&c.ctx, nullptr); c = {}; }
    ReleaseDoomed(0, true);
    if (s_time.pool && s_time.destroy) { s_time.destroy(vi.device, s_time.pool, nullptr); s_time.pool = VK_NULL_HANDLE; }
}

} // namespace

// ---- exports ---------------------------------------------------------------------------------------------------------

EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* ifaces)
{
    // RADV: no DCC for storage images (driconf radv_disable_dcc_stores, read when Unity creates its Vulkan instance, after
    // this). FSR 4's compressed storage writes cost ~0.3 ms a frame at 4K on an RX 7900 XTX (2.60 -> 2.28 ms; FSR 3 and the
    // game's own rendering measured the same either way). Not when the player set it already, or GOF2_KEEP_DCC_STORES=1;
    // other drivers don't read it.
#ifndef _WIN32
    if (!getenv("radv_disable_dcc_stores") && !getenv("GOF2_KEEP_DCC_STORES")) setenv("radv_disable_dcc_stores", "true", 0);
#endif

    s_ifaces = ifaces;
    s_log = ifaces->Get<IUnityLog>();
    s_gfx = ifaces->Get<IUnityGraphics>();
    s_vk = ifaces->Get<IUnityGraphicsVulkanV2>();
    if (!s_vk) { s_hook = "no IUnityGraphicsVulkanV2"; return; }
    // (a low priority: called first, right after Unity, so any other interceptor sees the raised version too)
    if (!s_vk->AddInterceptInitialization(OnVulkanInit, nullptr, 0)) s_hook = "too late to intercept Vulkan's initialization (not preloaded?)";
    // (the events are configured once the device exists, and only a Vulkan one: ConfigureEvent without one crashed the
    // batchmode Editor)
    if (s_gfx) s_gfx->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
}

EXPORT void UNITY_INTERFACE_API UnityPluginUnload()
{
    if (s_gfx) s_gfx->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
}

/// Loads AMD's DLLs from 'directory' and lists the FSR providers this device runs (main thread, once Unity's Vulkan device
/// exists): 1 when at least one is there. Again with another directory: tries again if nothing loaded yet.
EXPORT int Gof2Fsr_Init(const char* directory)
{
    std::lock_guard<std::mutex> g(s_lock);
    if (s_loaded) return s_providers.empty() ? 0 : 1;
    if (!s_vulkan) { s_status = "not Vulkan"; return 0; }
    if (!s_featuresOn) { s_status = "FSR's device features aren't on"; return 0; }
    if (ffxvkLoad(directory) != 0) { s_status = std::string("AMD's DLLs: ") + ffxvkError(); return 0; }
    UnityVulkanInstance vi = s_vk->Instance();
    VkDeviceContext dc{vi.device, vi.physicalDevice, FsrGdpa()};
    uint64_t count = 0;
    ffxQueryDescGetVersions v{};
    v.header.type = FFX_API_QUERY_DESC_TYPE_GET_VERSIONS;
    v.createDescType = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
    v.device = &dc;
    v.outputCount = &count;
    if (ffxQuery(nullptr, &v.header) != FFX_API_RETURN_OK || !count || count > 64) { s_status = "no FSR provider runs on this device"; s_loaded = true; return 0; }
    std::vector<uint64_t> ids(count);
    std::vector<const char*> names(count);
    v.versionIds = ids.data();
    v.versionNames = names.data();
    if (ffxQuery(nullptr, &v.header) != FFX_API_RETURN_OK) { s_status = "the FSR provider query failed"; s_loaded = true; return 0; }
    s_loaded = true;
    s_status = "providers:";
    for (uint64_t i = 0; i < count; i++)
    {
        s_providers.push_back({ids[i], names[i] ? names[i] : "?"});
        s_status += " " + s_providers.back().name;
    }
    return 1;
}

/// The newest provider of major version 'major' (4 / 3 / 2), e.g. "4.1.2"; "" when there is none.
EXPORT const char* Gof2Fsr_Provider(int major)
{
    std::lock_guard<std::mutex> g(s_lock);
    const Provider* p = ProviderFor(major);
    return p ? p->name.c_str() : "";
}

/// What happened: the Vulkan hook, then the DLLs and providers.
EXPORT const char* Gof2Fsr_Status()
{
    static std::string text;
    std::lock_guard<std::mutex> g(s_lock);
    text = s_hook + "; " + s_status + (s_lastError.empty() ? "" : "; last: " + s_lastError);
    if (s_remapped) text += "; " + std::to_string(s_remapped >> 20) + " MB of FSR memory moved from uncached to cached types";
    if (s_pipelinesSeen) text += "; wave64: " + std::to_string(s_wave64Pipelines) + " of " + std::to_string(s_pipelinesSeen) + " FSR pipelines (" +
        std::to_string(s_pipelinesAsked32) + " asked for 32, " + std::to_string(s_pipelinesUnsafe) + " unsuitable, " + std::to_string(s_pipelinesUnknownModule) + " modules not seen)";
    return text.c_str();
}

EXPORT UnityRenderingEventAndData Gof2Fsr_EventFunc() { return OnEvent; }

/// 1 where FSR 4 Lite can be asked for (CreateData.lite): FSR 4 runs here through ffx-vk-linux, which holds the two
/// models apart (the SDK DLL's, or the driver DLL's). Not on Windows (AMD's own loader gives the GPU's model).
EXPORT int Gof2Fsr_LiteAvailable()
{
#ifdef _WIN32
    return 0;
#else
    std::lock_guard<std::mutex> g(s_lock);
    return ProviderFor(4) ? 1 : 0;
#endif
}

/// The dispatches' average GPU and render-thread CPU milliseconds since the last call with 'reset'.
EXPORT void Gof2Fsr_Timings(double* gpuMs, double* cpuMs, int* samples, int reset)
{
    std::lock_guard<std::mutex> g(s_lock);
    *gpuMs = s_time.gpuSamples ? s_time.gpuMs / s_time.gpuSamples : 0;
    *cpuMs = s_time.cpuSamples ? s_time.cpuMs / s_time.cpuSamples : 0;
    *samples = s_time.gpuSamples;
    if (reset) { s_time.gpuMs = s_time.cpuMs = 0; s_time.gpuSamples = s_time.cpuSamples = 0; }
}
