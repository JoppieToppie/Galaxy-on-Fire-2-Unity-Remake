// gof2_streamline.cpp
// Feature: docs/notes/options.md "Frame generation": NVIDIA DLSS Frame Generation in the Windows D3D12 player through
// NVIDIA Streamline (sl.interposer.dll and its plugins, beside this DLL). The C# side is FrameGeneration (GoF2.App).
//
// Unity has no frame generation: its NVIDIA module is the DLSS upscaler alone, and DLSS-G has to own the swap chain (it
// presents the generated frames itself). This plugin is preloaded (isPreloaded), so it is in the process before Unity
// makes its D3D12 device, and uses Streamline's "manual hooking" mode (sdk/docs/ProgrammingGuideManualHooking.md): Unity
// keeps its native device, queue and factory, and only the calls Streamline has to see go through its proxies:
//   - D3D12CreateDevice (UnityPlayer.dll's delay import): the device's CreateCommandQueue is redirected,
//   - ID3D12Device::CreateCommandQueue called by Unity: slSetD3DDevice on the first one, then through the proxy device
//     (DLSS-G hooks the creation); Unity gets the native queue back,
//   - CreateDXGIFactory / CreateDXGIFactory2 (UnityPlayer.dll's imports): the factory's CreateSwapChain(ForHwnd) is
//     redirected, and a D3D12 swap chain Unity makes goes through the proxy factory: Unity then holds Streamline's swap
//     chain proxy, whose Present / GetBuffer / ResizeBuffers are DLSS-G's.
// Only calls from UnityPlayer.dll are redirected (the return address): Streamline's own queues and swap chain use the
// same vtables.
//
// Nothing of this runs unless the player turned the option on in an earlier run ("armed", HKCU\Software\GoF2Remake\
// FrameGeneration: the choice has to be known before any managed code runs). A start that never reports back
// (Gof2Sl_StartedOk) is not repeated: the next one stays unarmed and says so.
//
// Per frame (FrameGeneration.cs): the main thread's Reflex sleep and PCL markers, and two render thread events: Tag (the
// camera constants, our own unflipped copies of the depth and the motion vectors, the back buffer before the interface
// is drawn as the HUD-less colour) and FrameEnd (the DLSS-G mode, the render-submit-end marker that tells Streamline
// which frame is being presented: its Unity path, sl.reflex "Special case for Unity").

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#include <intrin.h>
#include <d3d12.h>
#include <dxgi1_6.h>

#include <algorithm>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>

#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D12.h"
#include "IUnityLog.h"

#include "sl.h"
#include "sl_consts.h"
#include "sl_dlss_g.h"
#include "sl_reflex.h"
#include "sl_pcl.h"
#include "sl_security.h"

#include "sl_nis.h"
#include "sl_deepdvc.h"

// AMD Anti-Lag 2 (docs/notes/options.md "Anti-Lag 2"): AMD's SDK is this one header (Native/AntiLag2/sdk, from
// github.com/GPUOpen-LibrariesAndSDKs/AntiLag2-SDK), which talks to the driver (amdxc64.dll) through the D3D12 device.
// Nothing of Streamline is involved: it works whether or not Streamline was started.
#include "ffx_antilag2_dx12.h"

// AMD FSR frame generation (docs/notes/options.md "FSR frame generation"): AMD's FidelityFX API headers
// (Native/FsrFrameGen/sdk, github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK at the version of the loader Unity ships).
// Its functions are taken from the loader DLL by name at run time: nothing of it is linked.
#include "framegeneration/include/ffx_framegeneration.h"
#include "framegeneration/include/dx12/ffx_api_framegeneration_dx12.h"

// ---- DLSS neural rendering ("DLSS 5", sl.dlss_nr) ------------------------------------------------------------------
// NVIDIA ships no public header for it yet (it is in the NDA SDK). These declarations are the plugin's interface as far
// as public code shows it: the one function sl.dlss_nr.dll (2.13) exports by name, slDLSSNRSetOptions; the options
// struct's id, which is in that DLL byte for byte; its members in the order Gaijin's Dagor Engine sets them
// (prog/engine/drv/drv3d_commonCode/streamline_adapter.cpp, DLSSNeuralRendering::setOptions, built against the real
// header) and a community reconstruction of the 2.13 layout lists them. Replace with NVIDIA's sl_dlss_nr.h when there is
// one. Streamline checks the struct's id and version itself: a mismatch is an error result, handled below.
namespace sl
{
enum class DLSSNRMode : uint32_t { eOff = 0, eOn = 1 };

// {29DFDFE0-273A-4E72-B492-2DC823D5B1AD}
SL_STRUCT_BEGIN(DLSSNROptions, StructType({0x29dfdfe0, 0x273a, 0x4e72, {0xb4, 0x92, 0x2d, 0xc8, 0x23, 0xd5, 0xb1, 0xad}}), kStructVersion3)
    DLSSNRMode mode = DLSSNRMode::eOff;
    float intensity = 1.0f;
    float localToneStrength = 1.0f;
    float localStructureStrength = 1.0f;
    float globalToneStrength = 1.0f;
    // Version 2
    uint32_t style = 0;
    uint32_t preset = 0;
    Boolean useAutoMask = Boolean::eFalse;
    float skinStructureStrength = 1.0f;
    // Version 3 (a DLSS mode: 3 = maximum quality)
    uint32_t performanceMode = 3;
SL_STRUCT_END()
}
using PFun_slDLSSNRSetOptions = sl::Result(const sl::ViewportHandle& viewport, const sl::DLSSNROptions& options);

namespace
{
// ---- state ----------------------------------------------------------------------------------------------------------

enum StateBits : int
{
    kArmed = 1,            // slInit succeeded, the hooks are in
    kDeviceBound = 2,      // Unity's device was given to Streamline
    kSwapChainProxied = 4, // Unity's swap chain is Streamline's proxy
    kSupported = 8,        // DLSS-G runs on this adapter
    kLatched = 16,         // the last armed start never reported back: not armed this time
    kFilesPresent = 32,    // Streamline's DLLs are beside the plugin
    kLateLoad = 64,        // the device existed before the hooks: nothing to attach to
    kNrPresent = 128,      // sl.dlss_nr.dll (DLSS neural rendering) is beside the plugin
    kNrSupported = 256,    // and this adapter runs it
    kAntiLag = 512,        // AMD Anti-Lag 2 is available (an AMD GPU and driver that have it)
    kNisPresent = 1024,    // sl.nis.dll (NVIDIA Image Scaling) is beside the plugin
    kDvcPresent = 2048,    // sl.deepdvc.dll and nvngx_deepdvc.dll (RTX Dynamic Vibrance) are
    kNisSupported = 4096,  // and this adapter runs them
    kDvcSupported = 8192,
    kFsrPresent = 1 << 14,   // AMD's FidelityFX loader and its frame generation DLL are beside the player
    kFsrArmed = 1 << 15,     // FSR frame generation is this run's provider: its hooks are in (Streamline is not started)
    kFsrSwapChain = 1 << 16, // Unity's swap chain is FSR's frame generation swap chain
    kFsrRunning = 1 << 17,   // its frame generation context exists
};

AMD::AntiLag2DX12::Context s_antiLag;

const wchar_t* kRegKey = L"Software\\GoF2Remake\\FrameGeneration";
const int kEventTag = 0x534C0001, kEventFrameEnd = 0x534C0002, kEventNeural = 0x534C0003, kEventPicture = 0x534C0004;

IUnityInterfaces* s_unity;
IUnityGraphics* s_graphics;
IUnityGraphicsD3D12v8* s_d3d12;
IUnityLog* s_log;

std::atomic<int> s_state{0};
std::mutex s_statusMutex;
std::string s_lastError;         // the last warning / error of ours or Streamline's
char s_status[1024];

std::wstring s_dir;              // this DLL's folder
BYTE* s_unityBase;
SIZE_T s_unitySize;

ID3D12Device* s_device;          // Unity's device (not owned)
ID3D12Device* s_proxyDevice;     // Streamline's proxy of it (kept for the process)
int s_supportResult = -1;        // sl::Result of slIsFeatureSupported(DLSS_G)
uint32_t s_requirementFlags;
bool s_reflexSet;

sl::ViewportHandle s_viewport(0);

// Render thread only
uint32_t s_modeSet = UINT32_MAX, s_framesSet;
bool s_swapChecked;
// Read by the main thread
std::atomic<uint32_t> s_dlssgStatus{0}, s_maxFrames{0}, s_vsyncOk{0}, s_presentedSum{0}, s_presentedFrames{0}, s_generating{0};
std::atomic<uint32_t> s_tagErrors{0}, s_dynamicOk{0}, s_reflexAvailable{0}, s_neuralErrors{0}, s_neuralRuns{0};
std::atomic<uint32_t> s_pictureErrors{0}, s_pictureRuns{0};

thread_local int t_inside;       // inside one of our redirected calls: the proxies call the same vtable slots

void Log(UnityLogType type, const char* format, ...)
{
    char text[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(text, sizeof(text), format, args);
    va_end(args);
    if (type != kUnityLogTypeLog)
    {
        std::lock_guard<std::mutex> lock(s_statusMutex);
        s_lastError = text;
    }
    char line[1100];
    snprintf(line, sizeof(line), "Frame generation: %s", text);
    if (s_log) s_log->Log(type, line, "gof2_streamline", 0);
    OutputDebugStringA(line);
    OutputDebugStringA("\n");
}

// ---- the armed flag ---------------------------------------------------------------------------------------------------

DWORD RegGet(const wchar_t* name, DWORD fallback)
{
    DWORD value = 0, size = sizeof(value);
    return RegGetValueW(HKEY_CURRENT_USER, kRegKey, name, RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS ? value : fallback;
}

void RegSet(const wchar_t* name, DWORD value)
{
    HKEY key;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, kRegKey, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS) return;
    RegSetValueExW(key, name, 0, REG_DWORD, (const BYTE*)&value, sizeof(value));
    RegCloseKey(key);
}

bool HasArgument(const wchar_t* argument)
{
    const wchar_t* line = GetCommandLineW();
    size_t n = wcslen(argument);
    for (const wchar_t* at = line; (at = wcsstr(at, argument)) != nullptr; at += n)
    {
        wchar_t after = at[n];
        if ((at == line || at[-1] == L' ' || at[-1] == L'"') && (after == 0 || after == L' ' || after == L'"')) return true;
    }
    return false;
}

bool FileExists(const std::wstring& path)
{
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY);
}

bool FilesPresent()
{
    for (const wchar_t* name : {L"sl.interposer.dll", L"sl.common.dll", L"sl.dlss_g.dll", L"sl.reflex.dll", L"sl.pcl.dll", L"nvngx_dlssg.dll"})
        if (!FileExists(s_dir + name)) return false;
    return true;
}

// ---- patching -------------------------------------------------------------------------------------------------------

bool WritePointer(void** slot, void* value)
{
    DWORD old;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &old)) return false;
    *slot = value;
    VirtualProtect(slot, sizeof(void*), old, &old);
    return true;
}

/// An import of 'module' from 'dll' by name; the old pointer in 'original'.
bool PatchImport(HMODULE module, const char* dll, const char* function, void* replacement, void** original)
{
    BYTE* base = (BYTE*)module;
    auto nt = (IMAGE_NT_HEADERS*)(base + ((IMAGE_DOS_HEADER*)base)->e_lfanew);
    auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return false;
    for (auto d = (IMAGE_IMPORT_DESCRIPTOR*)(base + dir.VirtualAddress); d->Name; ++d)
    {
        if (_stricmp((const char*)(base + d->Name), dll) != 0 || !d->OriginalFirstThunk) continue;
        auto names = (IMAGE_THUNK_DATA*)(base + d->OriginalFirstThunk);
        auto slots = (IMAGE_THUNK_DATA*)(base + d->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++slots)
        {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            if (strcmp(((IMAGE_IMPORT_BY_NAME*)(base + names->u1.AddressOfData))->Name, function) != 0) continue;
            *original = (void*)slots->u1.Function;
            return WritePointer((void**)&slots->u1.Function, replacement);
        }
    }
    return false;
}

/// A delay-loaded import of 'module' from 'dll', by ordinal or name (its slot is filled before the loader's thunk runs).
bool PatchDelayImport(HMODULE module, const char* dll, WORD ordinal, const char* function, void* replacement)
{
    BYTE* base = (BYTE*)module;
    auto nt = (IMAGE_NT_HEADERS*)(base + ((IMAGE_DOS_HEADER*)base)->e_lfanew);
    auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT];
    if (!dir.VirtualAddress) return false;
    for (auto d = (IMAGE_DELAYLOAD_DESCRIPTOR*)(base + dir.VirtualAddress); d->DllNameRVA; ++d)
    {
        if (_stricmp((const char*)(base + d->DllNameRVA), dll) != 0) continue;
        auto names = (IMAGE_THUNK_DATA*)(base + d->ImportNameTableRVA);
        auto slots = (IMAGE_THUNK_DATA*)(base + d->ImportAddressTableRVA);
        for (; names->u1.AddressOfData; ++names, ++slots)
        {
            bool match = IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)
                ? IMAGE_ORDINAL(names->u1.Ordinal) == ordinal
                : strcmp(((IMAGE_IMPORT_BY_NAME*)(base + names->u1.AddressOfData))->Name, function) == 0;
            if (match) return WritePointer((void**)&slots->u1.Function, replacement);
        }
    }
    return false;
}

/// A COM object's vtable slot (the class's: every object of it); the old pointer returned, null when it is ours already.
void* PatchVtable(void* object, int slot, void* replacement)
{
    void** vtable = *(void***)object;
    void* old = vtable[slot];
    if (old == replacement) return nullptr;
    return WritePointer(&vtable[slot], replacement) ? old : nullptr;
}

bool FromUnity(void* returnAddress)
{
    return s_unityBase && (BYTE*)returnAddress >= s_unityBase && (BYTE*)returnAddress < s_unityBase + s_unitySize;
}

// ---- the device and its queues ----------------------------------------------------------------------------------------

using CreateDeviceFn = HRESULT WINAPI(IUnknown*, D3D_FEATURE_LEVEL, REFIID, void**);
using CreateCommandQueueFn = HRESULT STDMETHODCALLTYPE(ID3D12Device*, const D3D12_COMMAND_QUEUE_DESC*, REFIID, void**);
CreateDeviceFn* s_realCreateDevice;
CreateCommandQueueFn* s_realCreateCommandQueue;

void BindDevice(ID3D12Device* device)
{
    s_device = device;
    sl::Result result = slSetD3DDevice(device);
    if (result != sl::Result::eOk)
    {
        Log(kUnityLogTypeWarning, "slSetD3DDevice failed (%d)", (int)result);
        return;
    }
    ID3D12Device* proxy = device;
    result = slUpgradeInterface((void**)&proxy);
    if (result != sl::Result::eOk || proxy == device)
    {
        Log(kUnityLogTypeWarning, "no device proxy (%d)", (int)result);
        return;
    }
    s_proxyDevice = proxy;
    s_state |= kDeviceBound;

    LUID luid = device->GetAdapterLuid();
    sl::AdapterInfo adapter{};
    adapter.deviceLUID = (uint8_t*)&luid;
    adapter.deviceLUIDSizeInBytes = sizeof(luid);
    s_supportResult = (int)slIsFeatureSupported(sl::kFeatureDLSS_G, adapter);
    sl::FeatureRequirements requirements{};
    if (slGetFeatureRequirements(sl::kFeatureDLSS_G, requirements) == sl::Result::eOk) s_requirementFlags = (uint32_t)requirements.flags;
    if (s_supportResult == (int)sl::Result::eOk) s_state |= kSupported;
    Log(kUnityLogTypeLog, "device bound, DLSS-G %s (result %d, requirement flags 0x%x)",
        s_supportResult == 0 ? "supported" : "not supported", s_supportResult, s_requirementFlags);
    if (s_state & kNisPresent)
    {
        int nis = (int)slIsFeatureSupported(sl::kFeatureNIS, adapter);
        if (nis == (int)sl::Result::eOk) s_state |= kNisSupported;
        Log(kUnityLogTypeLog, "NVIDIA Image Scaling: %s (result %d)", nis == 0 ? "supported" : "not supported on this GPU", nis);
    }
    if (s_state & kDvcPresent)
    {
        int dvc = (int)slIsFeatureSupported(sl::kFeatureDeepDVC, adapter);
        if (dvc == (int)sl::Result::eOk) s_state |= kDvcSupported;
        Log(kUnityLogTypeLog, "RTX Dynamic Vibrance: %s (result %d)", dvc == 0 ? "supported" : "not supported on this GPU", dvc);
    }
    if (s_state & kNrPresent)
    {
        int nr = (int)slIsFeatureSupported(sl::kFeatureDLSS_NR, adapter);
        bool loaded = false;
        int loadedResult = (int)slIsFeatureLoaded(sl::kFeatureDLSS_NR, loaded);
        sl::FeatureRequirements nrRequirements{};
        int requirementsResult = (int)slGetFeatureRequirements(sl::kFeatureDLSS_NR, nrRequirements);
        if (nr == (int)sl::Result::eOk) s_state |= kNrSupported;
        Log(kUnityLogTypeLog, "DLSS neural rendering: %s (supported result %d; loaded %d, result %d; requirements result %d, flags 0x%x, "
            "driver %u.%u needs %u.%u)", nr == 0 ? "supported on this GPU" : "not supported on this GPU", nr, loaded ? 1 : 0, loadedResult,
            requirementsResult, (uint32_t)nrRequirements.flags, nrRequirements.driverVersionDetected.major, nrRequirements.driverVersionDetected.minor,
            nrRequirements.driverVersionRequired.major, nrRequirements.driverVersionRequired.minor);
    }
}

HRESULT STDMETHODCALLTYPE Hook_CreateCommandQueue(ID3D12Device* self, const D3D12_COMMAND_QUEUE_DESC* desc, REFIID riid, void** queue)
{
    if (t_inside || !(s_state & kArmed) || !FromUnity(_ReturnAddress())) return s_realCreateCommandQueue(self, desc, riid, queue);
    if (!s_device)
    {
        t_inside++;
        BindDevice(self);
        t_inside--;
    }
    if (self != s_device || !s_proxyDevice) return s_realCreateCommandQueue(self, desc, riid, queue);

    // Through the proxy device, so DLSS-G's hook sees the queue; Unity keeps the native one (the proxy shares its
    // reference count and is left alone for the process).
    t_inside++;
    HRESULT hr = s_proxyDevice->CreateCommandQueue(desc, riid, queue);
    t_inside--;
    if (SUCCEEDED(hr) && queue && *queue)
    {
        void* native = nullptr;
        if (slGetNativeInterface(*queue, &native) == sl::Result::eOk && native)
        {
            ((IUnknown*)native)->Release();   // slGetNativeInterface's own reference
            *queue = native;
        }
    }
    return hr;
}

HRESULT WINAPI Hook_D3D12CreateDevice(IUnknown* adapter, D3D_FEATURE_LEVEL level, REFIID riid, void** device)
{
    HRESULT hr = s_realCreateDevice(adapter, level, riid, device);
    if (SUCCEEDED(hr) && device && *device && (s_state & kArmed) && !s_realCreateCommandQueue)
    {
        ID3D12Device* d3d = nullptr;
        if (SUCCEEDED(((IUnknown*)*device)->QueryInterface(IID_PPV_ARGS(&d3d))))
        {
            // ID3D12Device's vtable: IUnknown 0-2, ID3D12Object 3-6, GetNodeCount 7, CreateCommandQueue 8
            s_realCreateCommandQueue = (CreateCommandQueueFn*)PatchVtable(d3d, 8, (void*)&Hook_CreateCommandQueue);
            d3d->Release();
            if (!s_realCreateCommandQueue) Log(kUnityLogTypeWarning, "the device's vtable could not be changed");
        }
    }
    return hr;
}

// ---- the factory and the swap chain -----------------------------------------------------------------------------------

HRESULT FsrCreateSwapChain(IDXGIFactory2* factory, IUnknown* device, HWND window, const DXGI_SWAP_CHAIN_DESC1* desc,
                           const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* fullscreen, IDXGISwapChain1** swapChain);

using CreateFactoryFn = HRESULT WINAPI(REFIID, void**);
using CreateFactory2Fn = HRESULT WINAPI(UINT, REFIID, void**);
using CreateSwapChainFn = HRESULT STDMETHODCALLTYPE(IDXGIFactory*, IUnknown*, DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**);
using CreateSwapChainForHwndFn = HRESULT STDMETHODCALLTYPE(IDXGIFactory2*, IUnknown*, HWND, const DXGI_SWAP_CHAIN_DESC1*,
                                                          const DXGI_SWAP_CHAIN_FULLSCREEN_DESC*, IDXGIOutput*, IDXGISwapChain1**);
CreateFactoryFn* s_realCreateFactory;
CreateFactory2Fn* s_realCreateFactory2;
CreateSwapChainFn* s_realCreateSwapChain;
CreateSwapChainForHwndFn* s_realCreateSwapChainForHwnd;
IDXGIFactory2* s_factory;        // the factory Unity made its swap chain with (not owned)
IDXGIFactory2* s_proxyFactory;   // Streamline's proxy of it (kept for the process)

/// True when Unity is making a swap chain for the device Streamline has: the one to hand to DLSS-G.
bool IsUnitySwapChain(IUnknown* device, void* returnAddress)
{
    if (t_inside || !(s_state & kDeviceBound) || !device || !FromUnity(returnAddress)) return false;
    ID3D12CommandQueue* queue = nullptr;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&queue)))) return false;
    ID3D12Device* owner = nullptr;
    bool ours = SUCCEEDED(queue->GetDevice(IID_PPV_ARGS(&owner))) && owner == s_device;
    if (owner) owner->Release();
    queue->Release();
    return ours;
}

IDXGIFactory2* ProxyFactory(IDXGIFactory2* factory)
{
    if (s_factory == factory && s_proxyFactory) return s_proxyFactory;
    IDXGIFactory2* proxy = factory;
    sl::Result result = slUpgradeInterface((void**)&proxy);
    if (result != sl::Result::eOk || proxy == factory)
    {
        Log(kUnityLogTypeWarning, "no factory proxy (%d)", (int)result);
        return nullptr;
    }
    s_factory = factory;
    s_proxyFactory = proxy;
    return proxy;
}

// The present markers (PCL: Reflex's latency reports stay empty without them). Unity presents by itself, so the swap
// chain proxy's Present / Present1 slots are wrapped: present start, the proxy's own present (DLSS-G's), present end,
// for the frame whose FrameEnd event the render thread ran last.
using PresentFn = HRESULT STDMETHODCALLTYPE(IDXGISwapChain*, UINT, UINT);
using Present1Fn = HRESULT STDMETHODCALLTYPE(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
PresentFn* s_realPresent;
Present1Fn* s_realPresent1;
std::atomic<uint32_t> s_presentFrame{0};   // the frame the next present shows (FrameEnd's)
thread_local int t_presenting;

void PresentMarker(sl::PCLMarker marker)
{
    uint32_t frame = s_presentFrame.load();
    sl::FrameToken* token = nullptr;
    if (frame && slGetNewFrameToken(token, &frame) == sl::Result::eOk && token) slPCLSetMarker(marker, *token);
}

HRESULT STDMETHODCALLTYPE Hook_Present(IDXGISwapChain* self, UINT interval, UINT flags)
{
    if (t_presenting || (flags & DXGI_PRESENT_TEST)) return s_realPresent(self, interval, flags);
    t_presenting++;
    PresentMarker(sl::PCLMarker::ePresentStart);
    HRESULT hr = s_realPresent(self, interval, flags);
    PresentMarker(sl::PCLMarker::ePresentEnd);
    t_presenting--;
    return hr;
}

HRESULT STDMETHODCALLTYPE Hook_Present1(IDXGISwapChain1* self, UINT interval, UINT flags, const DXGI_PRESENT_PARAMETERS* parameters)
{
    if (t_presenting || (flags & DXGI_PRESENT_TEST)) return s_realPresent1(self, interval, flags, parameters);
    t_presenting++;
    PresentMarker(sl::PCLMarker::ePresentStart);
    HRESULT hr = s_realPresent1(self, interval, flags, parameters);
    PresentMarker(sl::PCLMarker::ePresentEnd);
    t_presenting--;
    return hr;
}

void SwapChainMade(HRESULT hr, IUnknown* swapChain, const char* how)
{
    if (FAILED(hr) || !swapChain)
    {
        Log(kUnityLogTypeWarning, "%s through Streamline failed (0x%08x)", how, (unsigned)hr);
        return;
    }
    void* native = nullptr;
    bool proxied = slGetNativeInterface(swapChain, &native) == sl::Result::eOk && native && native != swapChain;
    if (native) ((IUnknown*)native)->Release();
    if (proxied) s_state |= kSwapChainProxied;
    if (proxied)
    {
        // IDXGISwapChain's vtable: IUnknown 0-2, IDXGIObject 3-6, GetDevice 7, Present 8; IDXGISwapChain1's Present1 22
        // (the proxy's class alone: Streamline's real swap chain underneath is DXGI's own)
        IDXGISwapChain1* chain = nullptr;
        if (SUCCEEDED(swapChain->QueryInterface(IID_PPV_ARGS(&chain))))
        {
            if (void* old = PatchVtable(chain, 8, (void*)&Hook_Present)) s_realPresent = (PresentFn*)old;
            if (void* old = PatchVtable(chain, 22, (void*)&Hook_Present1)) s_realPresent1 = (Present1Fn*)old;
            chain->Release();
        }
    }
    Log(proxied ? kUnityLogTypeLog : kUnityLogTypeWarning, "%s: %s", how, proxied ? "Unity's swap chain is Streamline's" : "no swap chain proxy");
}

HRESULT STDMETHODCALLTYPE Hook_CreateSwapChainForHwnd(IDXGIFactory2* self, IUnknown* device, HWND window, const DXGI_SWAP_CHAIN_DESC1* desc,
                                                      const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* fullscreen, IDXGIOutput* output, IDXGISwapChain1** swapChain)
{
    if ((s_state & kFsrArmed) && !t_inside && device && FromUnity(_ReturnAddress()))
    {
        // FSR's frame generation swap chain in place of Unity's (Unity's own when FSR can't make one)
        if (SUCCEEDED(FsrCreateSwapChain(self, device, window, desc, fullscreen, swapChain))) return S_OK;
        return s_realCreateSwapChainForHwnd(self, device, window, desc, fullscreen, output, swapChain);
    }
    IDXGIFactory2* proxy = IsUnitySwapChain(device, _ReturnAddress()) ? ProxyFactory(self) : nullptr;
    if (!proxy) return s_realCreateSwapChainForHwnd(self, device, window, desc, fullscreen, output, swapChain);
    t_inside++;
    HRESULT hr = proxy->CreateSwapChainForHwnd(device, window, desc, fullscreen, output, swapChain);
    t_inside--;
    SwapChainMade(hr, swapChain ? *swapChain : nullptr, "CreateSwapChainForHwnd");
    return hr;
}

HRESULT STDMETHODCALLTYPE Hook_CreateSwapChain(IDXGIFactory* self, IUnknown* device, DXGI_SWAP_CHAIN_DESC* desc, IDXGISwapChain** swapChain)
{
    IDXGIFactory2* self2 = nullptr;
    IDXGIFactory2* proxy = nullptr;
    if (IsUnitySwapChain(device, _ReturnAddress()) && SUCCEEDED(self->QueryInterface(IID_PPV_ARGS(&self2))))
    {
        proxy = ProxyFactory(self2);
        self2->Release();
    }
    if (!proxy) return s_realCreateSwapChain(self, device, desc, swapChain);
    t_inside++;
    HRESULT hr = proxy->CreateSwapChain(device, desc, swapChain);
    t_inside--;
    SwapChainMade(hr, swapChain ? *swapChain : nullptr, "CreateSwapChain");
    return hr;
}

void PatchFactory(IUnknown* made)
{
    if (!made || !(s_state & (kArmed | kFsrArmed)) || t_inside) return;
    IDXGIFactory2* factory = nullptr;
    if (FAILED(made->QueryInterface(IID_PPV_ARGS(&factory)))) return;
    // IDXGIFactory's vtable: IUnknown 0-2, IDXGIObject 3-6, EnumAdapters 7, MakeWindowAssociation 8, GetWindowAssociation 9,
    // CreateSwapChain 10, CreateSoftwareAdapter 11, IDXGIFactory1 12-13, IsWindowedStereoEnabled 14, CreateSwapChainForHwnd 15
    if (void* old = PatchVtable(factory, 10, (void*)&Hook_CreateSwapChain)) s_realCreateSwapChain = (CreateSwapChainFn*)old;
    if (void* old = PatchVtable(factory, 15, (void*)&Hook_CreateSwapChainForHwnd)) s_realCreateSwapChainForHwnd = (CreateSwapChainForHwndFn*)old;
    factory->Release();
}

HRESULT WINAPI Hook_CreateDXGIFactory(REFIID riid, void** factory)
{
    HRESULT hr = s_realCreateFactory(riid, factory);
    if (SUCCEEDED(hr) && factory) PatchFactory((IUnknown*)*factory);
    return hr;
}

HRESULT WINAPI Hook_CreateDXGIFactory2(UINT flags, REFIID riid, void** factory)
{
    HRESULT hr = s_realCreateFactory2(flags, riid, factory);
    if (SUCCEEDED(hr) && factory) PatchFactory((IUnknown*)*factory);
    return hr;
}

// ---- starting Streamline ----------------------------------------------------------------------------------------------

void SlLog(sl::LogType type, const char* message)
{
    static const bool verbose = GetEnvironmentVariableW(L"GOF2_SL_VERBOSE", nullptr, 0) != 0;
    if (type == sl::LogType::eInfo && !verbose) return;
    char text[900];
    snprintf(text, sizeof(text), "%s", message ? message : "");
    for (size_t n = strlen(text); n > 0 && (text[n - 1] == '\n' || text[n - 1] == '\r'); n--) text[n - 1] = 0;
    Log(type == sl::LogType::eError ? kUnityLogTypeWarning : type == sl::LogType::eWarn ? kUnityLogTypeWarning : kUnityLogTypeLog, "[sl] %s", text);
}

bool ShouldArm(bool fsr)
{
    if (HasArgument(L"-noframegen")) return false;
    // Never headless, under another graphics API, or in VR (the headset's compositor presents there, not this swap chain)
    for (const wchar_t* argument : {L"-batchmode", L"-nographics", L"-server", L"-force-d3d11", L"-force-vulkan", L"-force-glcore", L"-force-gles",
                                    L"-vr", L"-vrsim"})
        if (HasArgument(argument)) return false;
    if (!(s_state & (fsr ? kFsrPresent : kFilesPresent))) return false;
    if (HasArgument(fsr ? L"-fsrframegen" : L"-framegen")) return true;
    if (RegGet(L"Armed", 0) == 0) return false;
    if (RegGet(L"Starting", 0) != 0)
    {
        // The last armed start never got as far as the menu: not again until the option is turned on anew.
        RegSet(L"Starting", 0);
        RegSet(L"Armed", 0);
        s_state |= kLatched;
        return false;
    }
    return true;
}

void Arm()
{
    HMODULE unity = GetModuleHandleW(L"UnityPlayer.dll");
    MODULEINFO info{};
    if (!unity || !GetModuleInformation(GetCurrentProcess(), unity, &info, sizeof(info)))
    {
        Log(kUnityLogTypeWarning, "UnityPlayer.dll not found");
        return;
    }
    s_unityBase = (BYTE*)info.lpBaseOfDll;
    s_unitySize = info.SizeOfImage;

    std::wstring interposer = s_dir + L"sl.interposer.dll";
    if (!sl::security::verifyEmbeddedSignature(interposer.c_str()))
    {
        Log(kUnityLogTypeWarning, "sl.interposer.dll is not signed by NVIDIA: not loaded");
        return;
    }
    // Loaded by its full path first: the delay-loaded imports below then find it by name.
    if (!LoadLibraryExW(interposer.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH))
    {
        Log(kUnityLogTypeWarning, "sl.interposer.dll did not load (%lu)", GetLastError());
        return;
    }
    HMODULE d3d12 = LoadLibraryW(L"d3d12.dll");
    s_realCreateDevice = d3d12 ? (CreateDeviceFn*)GetProcAddress(d3d12, "D3D12CreateDevice") : nullptr;
    if (!s_realCreateDevice)
    {
        Log(kUnityLogTypeWarning, "no D3D12");
        return;
    }

    static const wchar_t* pluginPaths[1];
    static std::wstring pluginDir;
    pluginDir = s_dir.substr(0, s_dir.size() - 1);
    pluginPaths[0] = pluginDir.c_str();
    // DLSS neural rendering ("DLSS 5", sl.dlss_nr.dll + nvngx_dlssnr.dll): asked for only where its plugin is beside the
    // others, and only probed (BindDevice logs whether this GPU runs it): its API has no public header yet.
    static sl::Feature features[6] = {sl::kFeatureDLSS_G, sl::kFeatureReflex, sl::kFeaturePCL};
    uint32_t featureCount = 3;
    if (s_state & kNrPresent) features[featureCount++] = sl::kFeatureDLSS_NR;
    if (s_state & kNisPresent) features[featureCount++] = sl::kFeatureNIS;
    if (s_state & kDvcPresent) features[featureCount++] = sl::kFeatureDeepDVC;
    static wchar_t logDir[MAX_PATH];
    bool logToFile = GetEnvironmentVariableW(L"GOF2_SL_LOGDIR", logDir, MAX_PATH) > 0;

    sl::Preferences pref{};
    pref.showConsole = false;
    pref.logLevel = GetEnvironmentVariableW(L"GOF2_SL_VERBOSE", nullptr, 0) != 0 ? sl::LogLevel::eVerbose : sl::LogLevel::eDefault;
    pref.pathsToPlugins = pluginPaths;
    pref.numPathsToPlugins = 1;
    pref.pathToLogsAndData = logToFile ? logDir : nullptr;
    pref.logMessageCallback = &SlLog;
    // Manual hooking (see the header); tags per frame (slSetTagForFrame); no over-the-air plugin downloads.
    pref.flags = sl::PreferenceFlags::eDisableCLStateTracking | sl::PreferenceFlags::eUseManualHooking | sl::PreferenceFlags::eUseFrameBasedResourceTagging;
    pref.featuresToLoad = features;
    pref.numFeaturesToLoad = featureCount;
    pref.engine = sl::EngineType::eUnity;   // sl.reflex then takes the render-submit-end marker for the frame being presented
    pref.engineVersion = "7000.0.0";
    pref.projectId = "5cb937cc-9c05-497c-93f9-3f7c75cae17f";
    pref.renderAPI = sl::RenderAPI::eD3D12;
    sl::Result result = slInit(pref, sl::kSDKVersion);
    if (result != sl::Result::eOk)
    {
        Log(kUnityLogTypeWarning, "slInit failed (%d)", (int)result);
        return;
    }
    s_state |= kArmed;

    bool device = PatchDelayImport(unity, "d3d12.dll", 101, "D3D12CreateDevice", (void*)&Hook_D3D12CreateDevice);
    bool factory = PatchImport(unity, "dxgi.dll", "CreateDXGIFactory", (void*)&Hook_CreateDXGIFactory, (void**)&s_realCreateFactory);
    bool factory2 = PatchImport(unity, "dxgi.dll", "CreateDXGIFactory2", (void*)&Hook_CreateDXGIFactory2, (void**)&s_realCreateFactory2);
    Log(device && (factory || factory2) ? kUnityLogTypeLog : kUnityLogTypeWarning,
        "Streamline started; hooks: D3D12CreateDevice %d, CreateDXGIFactory %d, CreateDXGIFactory2 %d", device, factory, factory2);
}

// ---- AMD FSR frame generation -----------------------------------------------------------------------------------------
// docs/notes/options.md "FSR frame generation": the other provider of generated frames, on any vendor's GPU. AMD's
// FidelityFX API (the loader Unity's own AMD module ships beside the player, plus amd_fidelityfx_framegeneration_dx12.dll
// from AMD's SDK of the same version) replaces Unity's swap chain with its frame generation swap chain, in the same
// CreateSwapChainForHwnd hook the Streamline path uses; Streamline is not started in this mode (two frame generators
// can't share a swap chain), so there is no Reflex, Image Scaling or neural rendering beside it.
// Per frame, on the render thread: the Tag event configures frame generation (with our copy of the back buffer before
// the interface as the HUD-less colour) and dispatches its "prepare" with the depth and motion vector copies; the swap
// chain's Present then generates and paces the frames itself. FrameEnd turns it off on a frame that tagged nothing.

struct TagData;
struct FrameEndData;

PfnFfxCreateContext s_ffxCreate;
PfnFfxDestroyContext s_ffxDestroy;
PfnFfxConfigure s_ffxConfigure;
PfnFfxQuery s_ffxQuery;
PfnFfxDispatch s_ffxDispatch;

std::wstring s_exeDir;                  // the player's folder: where Unity puts AMD's loader
ffxContext s_fsrSwapContext;            // the frame generation swap chain's context
ffxContext s_fsrContext;                // the frame generation context (render thread)
IDXGISwapChain4* s_fsrSwapChain;        // Unity's swap chain, FSR's (not owned)
ID3D12Resource* s_fsrHudless;           // our copy of the back buffer before the interface
uint32_t s_fsrWidth, s_fsrHeight, s_fsrMaxWidth, s_fsrMaxHeight;
DXGI_FORMAT s_fsrFormat = DXGI_FORMAT_UNKNOWN;
bool s_fsrEnabled;
std::atomic<uint32_t> s_fsrErrors{0};

bool FsrFilesPresent()
{
    return FileExists(s_exeDir + L"amd_fidelityfx_loader_dx12.dll") && FileExists(s_exeDir + L"amd_fidelityfx_framegeneration_dx12.dll");
}

/// The frame generation callback the swap chain calls at Present: the generation itself, on our context.
ffxReturnCode_t FsrGenerate(ffxDispatchDescFrameGeneration* params, void* context)
{
    return s_ffxDispatch((ffxContext*)context, &params->header);
}

void FsrConfigure(bool enabled, uint64_t frame)
{
    ffxConfigureDescFrameGeneration config{};
    config.header.type = FFX_API_CONFIGURE_DESC_TYPE_FRAMEGENERATION;
    config.swapChain = s_fsrSwapChain;
    config.frameGenerationCallback = &FsrGenerate;
    config.frameGenerationCallbackUserContext = &s_fsrContext;
    config.frameGenerationEnabled = enabled;
    config.allowAsyncWorkloads = false;
    if (enabled && s_fsrHudless)
        config.HUDLessColor = ffxApiGetResourceDX12(s_fsrHudless, FFX_API_RESOURCE_STATE_PIXEL_COMPUTE_READ);
    // (testing: $GOF2_FRAMEGEN_ONLY_GENERATED shows the generated frames alone)
    static const bool onlyGenerated = GetEnvironmentVariableW(L"GOF2_FRAMEGEN_ONLY_GENERATED", nullptr, 0) != 0;
    config.onlyPresentGenerated = onlyGenerated;
    // (testing: $GOF2_FSR_DEBUG draws FSR's own debug view and tear lines into what it presents)
    static const bool debugView = GetEnvironmentVariableW(L"GOF2_FSR_DEBUG", nullptr, 0) != 0;
    if (debugView) config.flags = FFX_FRAMEGENERATION_FLAG_DRAW_DEBUG_VIEW | FFX_FRAMEGENERATION_FLAG_DRAW_DEBUG_TEAR_LINES;
    config.generationRect.left = 0;
    config.generationRect.top = 0;
    config.generationRect.width = (int32_t)s_fsrWidth;
    config.generationRect.height = (int32_t)s_fsrHeight;
    config.frameID = frame;
    ffxReturnCode_t result = s_ffxConfigure(&s_fsrContext, &config.header);
    if (result != FFX_API_RETURN_OK && s_fsrErrors++ < 5) Log(kUnityLogTypeWarning, "FSR frame generation: configure failed (%u)", result);
    s_fsrEnabled = enabled && result == FFX_API_RETURN_OK;
}

void FsrDestroyContext()
{
    if (!s_fsrContext) return;
    // Off first, and the presents in flight done: they use the context
    if (s_fsrSwapContext && s_fsrSwapChain)
    {
        FsrConfigure(false, 0);
        ffxDispatchDescFrameGenerationSwapChainWaitForPresentsDX12 wait{};
        wait.header.type = FFX_API_DISPATCH_DESC_TYPE_FRAMEGENERATIONSWAPCHAIN_WAIT_FOR_PRESENTS_DX12;
        s_ffxDispatch(&s_fsrSwapContext, &wait.header);
    }
    s_ffxDestroy(&s_fsrContext, nullptr);
    s_fsrContext = nullptr;
    s_fsrEnabled = false;
    s_state &= ~kFsrRunning;
}

/// The frame generation context for this display size and back buffer format (a new one when they change).
bool FsrEnsureContext(ID3D12Device* device, uint32_t width, uint32_t height, DXGI_FORMAT format, uint32_t renderWidth, uint32_t renderHeight)
{
    if (s_fsrContext && s_fsrWidth == width && s_fsrHeight == height && s_fsrFormat == format && renderWidth <= s_fsrMaxWidth && renderHeight <= s_fsrMaxHeight)
        return true;
    FsrDestroyContext();
    if (s_fsrHudless) { s_fsrHudless->Release(); s_fsrHudless = nullptr; }
    if (s_fsrErrors.load() >= 5) return false;

    s_fsrWidth = width;
    s_fsrHeight = height;
    s_fsrFormat = format;
    s_fsrMaxWidth = std::max(width, renderWidth);
    s_fsrMaxHeight = std::max(height, renderHeight);
    bool hdr = format == DXGI_FORMAT_R10G10B10A2_UNORM || format == DXGI_FORMAT_R16G16B16A16_FLOAT;
    ffxCreateBackendDX12Desc backend{};
    backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
    backend.device = device;
    ffxCreateContextDescFrameGeneration create{};
    create.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATION;
    create.header.pNext = &backend.header;
    create.flags = FFX_FRAMEGENERATION_ENABLE_DEPTH_INVERTED | (hdr ? FFX_FRAMEGENERATION_ENABLE_HIGH_DYNAMIC_RANGE : 0);
    create.displaySize.width = width;
    create.displaySize.height = height;
    create.maxRenderSize.width = s_fsrMaxWidth;
    create.maxRenderSize.height = s_fsrMaxHeight;
    create.backBufferFormat = ffxApiGetSurfaceFormatDX12(format);
    ffxReturnCode_t result = s_ffxCreate(&s_fsrContext, &create.header, nullptr);
    if (result != FFX_API_RETURN_OK || !s_fsrContext)
    {
        s_fsrContext = nullptr;
        s_fsrErrors++;
        Log(kUnityLogTypeWarning, "FSR frame generation: no context for %u x %u, format %d (%u)", width, height, (int)format, result);
        return false;
    }

    // The HUD-less copy: a texture like the back buffer, ours
    D3D12_RESOURCE_DESC desc{};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Width = width;
    desc.Height = height;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    D3D12_HEAP_PROPERTIES heap{};
    heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    const D3D12_RESOURCE_STATES read = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    if (FAILED(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc, read, nullptr, IID_PPV_ARGS(&s_fsrHudless)))) s_fsrHudless = nullptr;
    s_state |= kFsrRunning;
    Log(kUnityLogTypeLog, "FSR frame generation: context for %u x %u, format %d%s", width, height, (int)format, s_fsrHudless ? "" : " (no HUD-less copy)");
    return true;
}

/// Unity makes its swap chain: FSR's frame generation swap chain in its place.
HRESULT FsrCreateSwapChain(IDXGIFactory2* factory, IUnknown* device, HWND window, const DXGI_SWAP_CHAIN_DESC1* desc,
                           const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* fullscreen, IDXGISwapChain1** swapChain)
{
    ID3D12CommandQueue* queue = nullptr;
    if (!s_ffxCreate || !desc || !swapChain || !device || FAILED(device->QueryInterface(IID_PPV_ARGS(&queue)))) return E_FAIL;
    if (s_fsrSwapContext)
    {
        // Unity makes its swap chain anew (it let go of the one before): that one's contexts first
        FsrDestroyContext();
        s_ffxDestroy(&s_fsrSwapContext, nullptr);
        s_fsrSwapContext = nullptr;
        s_fsrSwapChain = nullptr;
        s_state &= ~kFsrSwapChain;
    }
    DXGI_SWAP_CHAIN_DESC1 desc1 = *desc;
    DXGI_SWAP_CHAIN_FULLSCREEN_DESC fullscreen1{};
    if (fullscreen) fullscreen1 = *fullscreen;
    IDXGISwapChain4* made = nullptr;
    ffxCreateContextDescFrameGenerationSwapChainForHwndDX12 create{};
    create.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATIONSWAPCHAIN_FOR_HWND_DX12;
    create.swapchain = &made;
    create.hwnd = window;
    create.desc = &desc1;
    create.fullscreenDesc = fullscreen ? &fullscreen1 : nullptr;
    create.dxgiFactory = factory;
    create.gameQueue = queue;
    t_inside++;   // (FSR makes the real swap chain through the same factory slot)
    ffxReturnCode_t result = s_ffxCreate(&s_fsrSwapContext, &create.header, nullptr);
    t_inside--;
    queue->Release();
    if (result != FFX_API_RETURN_OK || !made)
    {
        Log(kUnityLogTypeWarning, "FSR frame generation: no swap chain (%u): Unity's own is used", result);
        s_fsrSwapContext = nullptr;
        return E_FAIL;
    }
    *swapChain = made;
    s_fsrSwapChain = made;
    s_state |= kFsrSwapChain;
    Log(kUnityLogTypeLog, "FSR frame generation: Unity's swap chain is FSR's (%u x %u, format %d)", desc->Width, desc->Height, (int)desc->Format);
    return S_OK;
}

void ArmFsr()
{
    HMODULE unity = GetModuleHandleW(L"UnityPlayer.dll");
    MODULEINFO info{};
    if (!unity || !GetModuleInformation(GetCurrentProcess(), unity, &info, sizeof(info)))
    {
        Log(kUnityLogTypeWarning, "UnityPlayer.dll not found");
        return;
    }
    s_unityBase = (BYTE*)info.lpBaseOfDll;
    s_unitySize = info.SizeOfImage;
    HMODULE loader = LoadLibraryExW((s_exeDir + L"amd_fidelityfx_loader_dx12.dll").c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (loader)
    {
        s_ffxCreate = (PfnFfxCreateContext)GetProcAddress(loader, "ffxCreateContext");
        s_ffxDestroy = (PfnFfxDestroyContext)GetProcAddress(loader, "ffxDestroyContext");
        s_ffxConfigure = (PfnFfxConfigure)GetProcAddress(loader, "ffxConfigure");
        s_ffxQuery = (PfnFfxQuery)GetProcAddress(loader, "ffxQuery");
        s_ffxDispatch = (PfnFfxDispatch)GetProcAddress(loader, "ffxDispatch");
    }
    if (!s_ffxCreate || !s_ffxDestroy || !s_ffxConfigure || !s_ffxDispatch)
    {
        Log(kUnityLogTypeWarning, "FSR frame generation: AMD's FidelityFX loader did not load (%lu)", GetLastError());
        return;
    }
    bool factory = PatchImport(unity, "dxgi.dll", "CreateDXGIFactory", (void*)&Hook_CreateDXGIFactory, (void**)&s_realCreateFactory);
    bool factory2 = PatchImport(unity, "dxgi.dll", "CreateDXGIFactory2", (void*)&Hook_CreateDXGIFactory2, (void**)&s_realCreateFactory2);
    if (!factory && !factory2)
    {
        Log(kUnityLogTypeWarning, "FSR frame generation: no factory hook");
        return;
    }
    s_state |= kFsrArmed;
    Log(kUnityLogTypeLog, "FSR frame generation started; hooks: CreateDXGIFactory %d, CreateDXGIFactory2 %d", factory, factory2);
}

// ---- per frame ----------------------------------------------------------------------------------------------------------

/// Streamline has Unity's device: Reflex and the markers work.
bool Bound() { return (s_state & (kArmed | kDeviceBound)) == (kArmed | kDeviceBound); }
/// And DLSS-G has Unity's swap chain on a GPU that runs it.
bool Running() { return (s_state & (kArmed | kDeviceBound | kSwapChainProxied | kSupported)) == (kArmed | kDeviceBound | kSwapChainProxied | kSupported); }

sl::FrameToken* Token(uint32_t frame)
{
    sl::FrameToken* token = nullptr;
    return slGetNewFrameToken(token, &frame) == sl::Result::eOk ? token : nullptr;
}

struct TagData
{
    void* depth;       // ID3D12Resource: view depth as rendered (reversed Z), unflipped, R32F
    void* motion;      // ID3D12Resource: motion vectors, unflipped, RG16F
    // flags: 1 reset, 2 tag the back buffer as HUD-less colour, 4 the constants and the depth / motion tags (once a frame)
    uint32_t frame, renderWidth, renderHeight, flags;
    // Row-major, row-vector matrices (Streamline's): view to clip, clip to view, clip to previous clip, previous clip to clip;
    // then the jitter (pixels), the motion vector scale, the camera's position / up / right / forward, near, far, the
    // vertical field of view (radians), the aspect ratio
    float values[84];
    void* uiAlpha;     // ID3D12Resource or null: the interface's alpha (R8, the back buffer's size), with the HUD-less colour
};

struct FrameEndData
{
    uint32_t frame, mode, framesToGenerate, tagged;   // mode: 0 off, 1 that many frames, 2 dynamic (the driver picks)
    float dynamicTarget;                              // dynamic: the frame rate to reach; 0 = the display's refresh rate
    uint32_t recompose;                               // the interface's alpha was tagged: interpolate it apart from the scene
};

sl::float4x4 Matrix(const float* m)
{
    sl::float4x4 r;
    for (int i = 0; i < 4; i++) r.setRow(i, sl::float4(m[i * 4], m[i * 4 + 1], m[i * 4 + 2], m[i * 4 + 3]));
    return r;
}

void TagBackBuffer(sl::FrameToken* token, void* uiAlpha);

void OnTag(const TagData* d)
{
    if (!Bound() || !d) return;
    sl::FrameToken* token = Token(d->frame);
    if (!token) return;
    if (!(d->flags & 4))
    {
        if ((d->flags & 2) && Running()) TagBackBuffer(token, d->uiAlpha);
        return;
    }
    if (!d->depth || !d->motion) return;

    const float* v = d->values;
    sl::Constants c{};
    c.cameraViewToClip = Matrix(v);
    c.clipToCameraView = Matrix(v + 16);
    static const float identity[16] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    c.clipToLensClip = Matrix(identity);
    c.clipToPrevClip = Matrix(v + 32);
    c.prevClipToClip = Matrix(v + 48);
    c.jitterOffset = sl::float2(v[64], v[65]);
    c.mvecScale = sl::float2(v[66], v[67]);
    c.cameraPinholeOffset = sl::float2(0, 0);
    c.cameraPos = sl::float3(v[68], v[69], v[70]);
    c.cameraUp = sl::float3(v[71], v[72], v[73]);
    c.cameraRight = sl::float3(v[74], v[75], v[76]);
    c.cameraFwd = sl::float3(v[77], v[78], v[79]);
    c.cameraNear = v[80];
    c.cameraFar = v[81];
    c.cameraFOV = v[82];
    c.cameraAspectRatio = v[83];
    c.motionVectorsInvalidValue = sl::INVALID_FLOAT;
    c.depthInverted = sl::Boolean::eTrue;
    c.cameraMotionIncluded = sl::Boolean::eTrue;
    c.motionVectors3D = sl::Boolean::eFalse;
    c.reset = (d->flags & 1) ? sl::Boolean::eTrue : sl::Boolean::eFalse;
    c.orthographicProjection = sl::Boolean::eFalse;
    c.motionVectorsDilated = sl::Boolean::eFalse;
    c.motionVectorsJittered = sl::Boolean::eFalse;
    sl::Result result = slSetConstants(c, *token, s_viewport);
    if (result != sl::Result::eOk) { s_tagErrors++; return; }

    // Our own textures, written once a frame before this event and not touched again until the next: valid until the
    // present, in the state asked for here.
    const uint32_t readState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    auto depth = (ID3D12Resource*)d->depth;
    auto motion = (ID3D12Resource*)d->motion;
    s_d3d12->RequestResourceState(depth, (D3D12_RESOURCE_STATES)readState);
    s_d3d12->RequestResourceState(motion, (D3D12_RESOURCE_STATES)readState);
    sl::Extent renderExtent{0, 0, d->renderWidth, d->renderHeight};
    sl::Resource depthResource(sl::ResourceType::eTex2d, depth, readState);
    sl::Resource motionResource(sl::ResourceType::eTex2d, motion, readState);
    sl::ResourceTag tags[2] = {
        sl::ResourceTag(&depthResource, sl::kBufferTypeDepth, sl::ResourceLifecycle::eValidUntilPresent, &renderExtent),
        sl::ResourceTag(&motionResource, sl::kBufferTypeMotionVectors, sl::ResourceLifecycle::eValidUntilPresent, &renderExtent),
    };
    result = slSetTagForFrame(*token, s_viewport, tags, 2, nullptr);
    if (result != sl::Result::eOk) s_tagErrors++;
    if ((d->flags & 2) && Running()) TagBackBuffer(token, d->uiAlpha);
}

/// The back buffer as it is now, before the interface is drawn: the HUD-less colour for DLSS-G. The interface comes
/// after, so Streamline copies it here, on Unity's command list. With it the interface's alpha where the game drew one
/// (a texture of ours, written before this event and left alone until the next frame: valid until the present).
void TagBackBuffer(sl::FrameToken* token, void* uiAlpha)
{
    UnityGraphicsD3D12RecordingState recording{};
    IDXGISwapChain* swapChain = s_d3d12->GetSwapChain();
    IDXGISwapChain3* swapChain3 = nullptr;
    if (!swapChain || !s_d3d12->CommandRecordingState(&recording) || !recording.commandList
        || FAILED(swapChain->QueryInterface(IID_PPV_ARGS(&swapChain3)))) return;
    ID3D12Resource* backBuffer = nullptr;
    if (SUCCEEDED(swapChain3->GetBuffer(swapChain3->GetCurrentBackBufferIndex(), IID_PPV_ARGS(&backBuffer))))
    {
        s_d3d12->RequestResourceState(backBuffer, D3D12_RESOURCE_STATE_COPY_SOURCE);
        sl::Resource hudless(sl::ResourceType::eTex2d, backBuffer, (uint32_t)D3D12_RESOURCE_STATE_COPY_SOURCE);
        const uint32_t readState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        auto ui = (ID3D12Resource*)uiAlpha;
        if (ui) s_d3d12->RequestResourceState(ui, (D3D12_RESOURCE_STATES)readState);
        sl::Resource uiResource(sl::ResourceType::eTex2d, ui, readState);
        sl::ResourceTag tags[2] = {
            sl::ResourceTag(&hudless, sl::kBufferTypeHUDLessColor, sl::ResourceLifecycle::eOnlyValidNow, nullptr),
            sl::ResourceTag(&uiResource, sl::kBufferTypeUIAlpha, sl::ResourceLifecycle::eValidUntilPresent, nullptr),
        };
        if (slSetTagForFrame(*token, s_viewport, tags, ui ? 2 : 1, recording.commandList) != sl::Result::eOk) s_tagErrors++;
        backBuffer->Release();
    }
    swapChain3->Release();
}

struct NeuralData
{
    void* colorIn;    // ID3D12Resource: the finished picture, tonemapped SDR (sRGB-encoded), the way up the back buffer is
    void* colorOut;   // ID3D12Resource: the same size and format, with unordered access: DLSS-NR writes the picture here
    uint32_t frame, width, height, flags;
    // The options (DLSSNROptions): 0..1 each; quality 1 performance, 2 balanced, 3 quality (a DLSS mode)
    float intensity, toneStrength, structureStrength, skinStrength;
    uint32_t quality;
};

/// DLSS neural rendering on the finished picture (the frame's constants, depth and motion vectors were tagged by OnTag
/// before). As Dagor Engine evaluates it: the colour pair tagged valid until the evaluate, then slEvaluateFeature. Any
/// failure copies the picture through unchanged, and after a few the managed side stops asking.
void OnNeural(const NeuralData* d)
{
    if (!d || !d->colorIn || !d->colorOut) return;
    auto in = (ID3D12Resource*)d->colorIn;
    auto out = (ID3D12Resource*)d->colorOut;
    UnityGraphicsD3D12RecordingState recording{};
    if (!s_d3d12->CommandRecordingState(&recording) || !recording.commandList) return;

    bool done = false;
    sl::FrameToken* token = Bound() && (s_state & kNrSupported) && s_neuralErrors.load() < 8 ? Token(d->frame) : nullptr;
    if (token)
    {
        static PFun_slDLSSNRSetOptions* setOptions;
        static float set[4] = {-1.f, -1.f, -1.f, -1.f};
        static uint32_t qualitySet = UINT32_MAX;
        void* function = setOptions;
        if (!setOptions && slGetFeatureFunction(sl::kFeatureDLSS_NR, "slDLSSNRSetOptions", function) == sl::Result::eOk)
            setOptions = (PFun_slDLSSNRSetOptions*)function;
        bool ready = setOptions != nullptr;
        const float want[4] = {d->intensity, d->toneStrength, d->structureStrength, d->skinStrength};
        uint32_t quality = d->quality >= 1 && d->quality <= 3 ? d->quality : 3;
        if (ready && (memcmp(set, want, sizeof(want)) != 0 || qualitySet != quality))
        {
            sl::DLSSNROptions options{};
            options.mode = sl::DLSSNRMode::eOn;
            options.intensity = d->intensity;
            options.localToneStrength = d->toneStrength;
            options.localStructureStrength = d->structureStrength;
            options.skinStructureStrength = d->skinStrength;
            options.performanceMode = quality;
            options.useAutoMask = sl::Boolean::eTrue;   // no control mask of ours: DLSS-NR masks on its own
            sl::Result result = setOptions(s_viewport, options);
            if (result == sl::Result::eOk) { memcpy(set, want, sizeof(want)); qualitySet = quality; }
            else { ready = false; Log(kUnityLogTypeWarning, "slDLSSNRSetOptions failed (%d)", (int)result); }
        }
        if (ready)
        {
            const uint32_t readState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
            s_d3d12->RequestResourceState(in, (D3D12_RESOURCE_STATES)readState);
            s_d3d12->RequestResourceState(out, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            sl::Resource inResource(sl::ResourceType::eTex2d, in, readState);
            sl::Resource outResource(sl::ResourceType::eTex2d, out, (uint32_t)D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            sl::ResourceTag tags[2] = {
                sl::ResourceTag(&inResource, sl::kBufferTypeUpliftInputColor, sl::ResourceLifecycle::eValidUntilEvaluate, nullptr),
                sl::ResourceTag(&outResource, sl::kBufferTypeUpliftOutputColor, sl::ResourceLifecycle::eValidUntilEvaluate, nullptr),
            };
            sl::Result result = slSetTagForFrame(*token, s_viewport, tags, 2, recording.commandList);
            if (result == sl::Result::eOk)
            {
                const sl::BaseStructure* inputs[] = {&s_viewport};
                result = slEvaluateFeature(sl::kFeatureDLSS_NR, *token, inputs, 1, recording.commandList);
            }
            done = result == sl::Result::eOk || result == sl::Result::eWarnOutOfVRAM;
            if (!done) Log(kUnityLogTypeWarning, "DLSS neural rendering failed (%d)", (int)result);
        }
        if (done) s_neuralRuns++; else s_neuralErrors++;
    }
    if (!done)
    {
        // The picture as it was
        s_d3d12->RequestResourceState(in, D3D12_RESOURCE_STATE_COPY_SOURCE);
        s_d3d12->RequestResourceState(out, D3D12_RESOURCE_STATE_COPY_DEST);
        recording.commandList->CopyResource(out, in);
    }
}

struct PictureData
{
    void* colorIn;    // ID3D12Resource: the finished picture, tonemapped SDR (sRGB-encoded), the way up the back buffer is
    void* colorOut;   // ID3D12Resource: the same size and format, with unordered access: the result
    uint32_t frame, width, height, flags;   // flags: 1 NVIDIA Image Scaling's sharpening, 2 RTX Dynamic Vibrance (DeepDVC)
    float sharpness, vibrance, saturation;  // 0..1 each
};

/// The finished picture's filters (docs/notes/options.md "Image Scaling and Dynamic Vibrance"): NIS as a sharpener
/// (NISMode::eSharpen: the picture is at its final size already) from 'colorIn' into 'colorOut', then DeepDVC on
/// 'colorOut' in place. Neither needs the frame's constants. A filter that is off or fails leaves the picture as it was;
/// after a few failures the managed side stops asking.
void OnPicture(const PictureData* d)
{
    if (!d || !d->colorIn || !d->colorOut) return;
    auto in = (ID3D12Resource*)d->colorIn;
    auto out = (ID3D12Resource*)d->colorOut;
    UnityGraphicsD3D12RecordingState recording{};
    if (!s_d3d12->CommandRecordingState(&recording) || !recording.commandList) return;
    const uint32_t readState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    sl::FrameToken* token = Bound() && s_pictureErrors.load() < 8 ? Token(d->frame) : nullptr;
    const sl::BaseStructure* inputs[] = {&s_viewport};

    bool sharpened = false;
    if (token && (d->flags & 1) && (s_state & kNisSupported))
    {
        static float sharpnessSet = -1.f;
        sl::Result result = sl::Result::eOk;
        if (sharpnessSet != d->sharpness)
        {
            sl::NISOptions options{};
            options.mode = sl::NISMode::eSharpen;
            options.hdrMode = sl::NISHDR::eNone;
            options.sharpness = d->sharpness;
            result = slNISSetOptions(s_viewport, options);
            if (result == sl::Result::eOk) sharpnessSet = d->sharpness;
        }
        if (result == sl::Result::eOk)
        {
            s_d3d12->RequestResourceState(in, (D3D12_RESOURCE_STATES)readState);
            s_d3d12->RequestResourceState(out, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            sl::Resource inResource(sl::ResourceType::eTex2d, in, readState);
            sl::Resource outResource(sl::ResourceType::eTex2d, out, (uint32_t)D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            sl::ResourceTag tags[2] = {
                sl::ResourceTag(&inResource, sl::kBufferTypeScalingInputColor, sl::ResourceLifecycle::eValidUntilEvaluate, nullptr),
                sl::ResourceTag(&outResource, sl::kBufferTypeScalingOutputColor, sl::ResourceLifecycle::eValidUntilEvaluate, nullptr),
            };
            result = slSetTagForFrame(*token, s_viewport, tags, 2, recording.commandList);
            if (result == sl::Result::eOk) result = slEvaluateFeature(sl::kFeatureNIS, *token, inputs, 1, recording.commandList);
        }
        sharpened = result == sl::Result::eOk;
        if (sharpened) s_pictureRuns++;
        else { s_pictureErrors++; Log(kUnityLogTypeWarning, "NVIDIA Image Scaling failed (%d)", (int)result); }
    }
    if (!sharpened)
    {
        // The picture as it was
        s_d3d12->RequestResourceState(in, D3D12_RESOURCE_STATE_COPY_SOURCE);
        s_d3d12->RequestResourceState(out, D3D12_RESOURCE_STATE_COPY_DEST);
        recording.commandList->CopyResource(out, in);
    }

    if (token && (d->flags & 2) && (s_state & kDvcSupported))
    {
        static float set[2] = {-1.f, -1.f};
        sl::Result result = sl::Result::eOk;
        if (set[0] != d->vibrance || set[1] != d->saturation)
        {
            sl::DeepDVCOptions options{};
            options.mode = sl::DeepDVCMode::eOn;
            options.intensity = d->vibrance;
            options.saturationBoost = d->saturation;
            result = slDeepDVCSetOptions(s_viewport, options);
            if (result == sl::Result::eOk) { set[0] = d->vibrance; set[1] = d->saturation; }
        }
        if (result == sl::Result::eOk)
        {
            s_d3d12->RequestResourceState(out, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            sl::Resource outResource(sl::ResourceType::eTex2d, out, (uint32_t)D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            sl::ResourceTag tag(&outResource, sl::kBufferTypeScalingOutputColor, sl::ResourceLifecycle::eValidUntilEvaluate, nullptr);
            result = slSetTagForFrame(*token, s_viewport, &tag, 1, recording.commandList);
            if (result == sl::Result::eOk) result = slEvaluateFeature(sl::kFeatureDeepDVC, *token, inputs, 1, recording.commandList);
        }
        if (result == sl::Result::eOk) s_pictureRuns++;
        else { s_pictureErrors++; Log(kUnityLogTypeWarning, "RTX Dynamic Vibrance failed (%d)", (int)result); }
    }
}

void OnFrameEnd(const FrameEndData* d)
{
    if (!Bound() || !d) return;
    sl::FrameToken* token = Token(d->frame);
    if (!token) return;
    // The last thing of the frame on the render thread: under EngineType::eUnity this marker is also what tells
    // Streamline which frame the coming Present shows.
    slPCLSetMarker(sl::PCLMarker::eRenderSubmitEnd, *token);
    s_presentFrame = d->frame;
    if (!Running()) return;

    // Off on a frame without tags (a loading screen, a camera of another kind): DLSS-G must not use stale buffers.
    uint32_t mode = d->tagged ? d->mode : 0u;
    if (mode == 2 && !s_dynamicOk.load()) mode = 1;
    uint32_t frames = d->framesToGenerate < 1 ? 1 : d->framesToGenerate;
    uint32_t max = s_maxFrames.load();
    if (max > 0 && frames > max) frames = max;
    static float targetSet = -1.f;
    static uint32_t recomposeSet = UINT32_MAX;
    float target = mode == 2 && d->dynamicTarget > 0.f ? d->dynamicTarget : 0.f;
    uint32_t recompose = mode && d->recompose ? 1u : 0u;
    if (mode != s_modeSet || frames != s_framesSet || target != targetSet || (mode && recompose != recomposeSet))
    {
        targetSet = target;
        recomposeSet = recompose;
        sl::DLSSGOptions options{};
        options.mode = mode == 2 ? sl::DLSSGMode::eDynamic : mode == 1 ? sl::DLSSGMode::eOn : sl::DLSSGMode::eOff;
        options.numFramesToGenerate = frames;
        options.dynamicTargetFrameRate = target;
        // The interface's alpha is tagged beside the HUD-less colour: DLSS-G interpolates the two apart
        options.enableUserInterfaceRecomposition = recompose ? sl::Boolean::eTrue : sl::Boolean::eFalse;
        options.flags = sl::DLSSGFlags::eRetainResourcesWhenOff;
        // (testing: $GOF2_FRAMEGEN_ONLY_GENERATED shows the generated frames alone, to look at what DLSS-G makes of the inputs)
        static const bool onlyGenerated = GetEnvironmentVariableW(L"GOF2_FRAMEGEN_ONLY_GENERATED", nullptr, 0) != 0;
        if (onlyGenerated) options.flags |= sl::DLSSGFlags::eShowOnlyInterpolatedFrame;
        sl::Result result = slDLSSGSetOptions(s_viewport, options);
        if (result != sl::Result::eOk) Log(kUnityLogTypeWarning, "slDLSSGSetOptions failed (%d)", (int)result);
        s_modeSet = mode;
        s_framesSet = frames;
    }

    sl::DLSSGState state{};
    if (slDLSSGGetState(s_viewport, state, nullptr) == sl::Result::eOk)
    {
        s_dlssgStatus = (uint32_t)state.status;
        s_maxFrames = state.numFramesToGenerateMax;
        s_vsyncOk = state.bIsVsyncSupportAvailable == sl::Boolean::eTrue ? 1u : 0u;
        s_dynamicOk = state.bIsDynamicMFGSupported == sl::Boolean::eTrue ? 1u : 0u;
        s_presentedSum += state.numFramesActuallyPresented;
        s_presentedFrames++;
        s_generating = mode && state.status == sl::DLSSGStatus::eOk && state.numFramesActuallyPresented > 1 ? 1u : 0u;
    }
}

// ---- FSR frame generation, per frame ----------------------------------------------------------------------------------

void FsrOnTag(const TagData* d)
{
    if (!d || !(d->flags & 4) || !(d->flags & 2) || !d->depth || !d->motion || !s_fsrSwapChain) return;
    UnityGraphicsD3D12RecordingState recording{};
    ID3D12Device* device = s_d3d12->GetDevice();
    if (!device || !s_d3d12->CommandRecordingState(&recording) || !recording.commandList) return;
    ID3D12Resource* backBuffer = nullptr;
    if (FAILED(s_fsrSwapChain->GetBuffer(s_fsrSwapChain->GetCurrentBackBufferIndex(), IID_PPV_ARGS(&backBuffer)))) return;
    D3D12_RESOURCE_DESC desc = backBuffer->GetDesc();
    if (!FsrEnsureContext(device, (uint32_t)desc.Width, desc.Height, desc.Format, d->renderWidth, d->renderHeight))
    {
        backBuffer->Release();
        return;
    }

    // The back buffer as it is now, before the interface: the HUD-less colour (kept until the present)
    const D3D12_RESOURCE_STATES read = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    if (s_fsrHudless)
    {
        s_d3d12->RequestResourceState(backBuffer, D3D12_RESOURCE_STATE_COPY_SOURCE);
        D3D12_RESOURCE_BARRIER barrier{};
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Transition.pResource = s_fsrHudless;
        barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        barrier.Transition.StateBefore = read;
        barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
        recording.commandList->ResourceBarrier(1, &barrier);
        recording.commandList->CopyResource(s_fsrHudless, backBuffer);
        std::swap(barrier.Transition.StateBefore, barrier.Transition.StateAfter);
        recording.commandList->ResourceBarrier(1, &barrier);
    }
    backBuffer->Release();
    FsrConfigure(true, d->frame);
    if (!s_fsrEnabled) return;

    static LARGE_INTEGER last, frequency;
    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    if (!frequency.QuadPart) QueryPerformanceFrequency(&frequency);
    float delta = last.QuadPart ? (float)((double)(now.QuadPart - last.QuadPart) * 1000.0 / (double)frequency.QuadPart) : 16.7f;
    last = now;

    const float* v = d->values;
    auto depth = (ID3D12Resource*)d->depth;
    auto motion = (ID3D12Resource*)d->motion;
    s_d3d12->RequestResourceState(depth, read);
    s_d3d12->RequestResourceState(motion, read);
    ffxDispatchDescFrameGenerationPrepareV2 prepare{};
    prepare.header.type = FFX_API_DISPATCH_DESC_TYPE_FRAMEGENERATION_PREPARE_V2;
    prepare.frameID = d->frame;
    prepare.commandList = recording.commandList;
    prepare.renderSize.width = d->renderWidth;
    prepare.renderSize.height = d->renderHeight;
    prepare.jitterOffset.x = v[64];
    prepare.jitterOffset.y = v[65];
    // Our motion vectors are this frame minus the one before in texture coordinates; FSR wants the way back, in pixels
    prepare.motionVectorScale.x = -(float)d->renderWidth;
    prepare.motionVectorScale.y = -(float)d->renderHeight;
    prepare.frameTimeDelta = delta;
    prepare.reset = (d->flags & 1) != 0;
    prepare.cameraNear = v[80];
    prepare.cameraFar = v[81];
    prepare.cameraFovAngleVertical = v[82];
    prepare.viewSpaceToMetersFactor = 1.f;
    prepare.depth = ffxApiGetResourceDX12(depth, FFX_API_RESOURCE_STATE_PIXEL_COMPUTE_READ);
    prepare.motionVectors = ffxApiGetResourceDX12(motion, FFX_API_RESOURCE_STATE_PIXEL_COMPUTE_READ);
    for (int i = 0; i < 3; i++)
    {
        prepare.cameraPosition[i] = v[68 + i];
        prepare.cameraUp[i] = v[71 + i];
        prepare.cameraRight[i] = v[74 + i];
        prepare.cameraForward[i] = v[77 + i];
    }
    ffxReturnCode_t result = s_ffxDispatch(&s_fsrContext, &prepare.header);
    if (result != FFX_API_RETURN_OK)
    {
        if (s_fsrErrors++ < 5) Log(kUnityLogTypeWarning, "FSR frame generation: prepare failed (%u)", result);
        FsrConfigure(false, d->frame);
    }
}

void FsrOnFrameEnd(const FrameEndData* d)
{
    if (!d) return;
    bool on = d->mode != 0 && d->tagged && s_fsrEnabled;
    // Off on a frame without tags (a loading screen, a menu, the option): FSR must not use stale buffers
    if (!on && s_fsrEnabled && s_fsrContext) FsrConfigure(false, d->frame);
    s_presentedSum += on ? 2u : 1u;
    s_presentedFrames++;
    s_generating = on ? 1u : 0u;
}

void UNITY_INTERFACE_API OnRenderEvent(int eventId, void* data)
{
    if (!s_d3d12) return;
    if (s_state & kFsrSwapChain)
    {
        if (eventId == kEventTag) FsrOnTag((const TagData*)data);
        else if (eventId == kEventFrameEnd) FsrOnFrameEnd((const FrameEndData*)data);
        return;
    }
    if (eventId == kEventTag) OnTag((const TagData*)data);
    else if (eventId == kEventFrameEnd) OnFrameEnd((const FrameEndData*)data);
    else if (eventId == kEventNeural) OnNeural((const NeuralData*)data);
    else if (eventId == kEventPicture) OnPicture((const PictureData*)data);
}

void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType type)
{
    if (type == kUnityGfxDeviceEventInitialize)
    {
        if (!s_graphics || s_graphics->GetRenderer() != kUnityGfxRendererD3D12) return;
        s_d3d12 = s_unity->Get<IUnityGraphicsD3D12v8>();
        if (!s_d3d12)
        {
            if (s_state & kArmed) Log(kUnityLogTypeWarning, "this Unity has no IUnityGraphicsD3D12v8");
            return;
        }
        // The tag event records on Unity's command list (Streamline's copy of the back buffer): Unity binds its state again after.
        UnityD3D12PluginEventConfig config{};
        config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
        config.flags = kUnityD3D12EventConfigFlag_ModifiesCommandBuffersState;
        config.ensureActiveRenderTextureIsBound = false;
        s_d3d12->ConfigureEvent(kEventTag, &config);
        s_d3d12->ConfigureEvent(kEventNeural, &config);
        s_d3d12->ConfigureEvent(kEventPicture, &config);
        config.flags = 0;
        s_d3d12->ConfigureEvent(kEventFrameEnd, &config);
        if ((s_state & kArmed) && !(s_state & kDeviceBound))
        {
            s_state |= kLateLoad;
            Log(kUnityLogTypeWarning, "Unity made its device without the hooks: nothing to attach to");
        }
        // AMD Anti-Lag 2: S_OK where the GPU and its driver have it (the driver's amdxc64.dll is in the process then).
        if (ID3D12Device* device = s_d3d12->GetDevice())
        {
            HRESULT hr = AMD::AntiLag2DX12::Initialize(&s_antiLag, device);
            if (hr == S_OK) s_state |= kAntiLag;
            if (hr == S_OK || GetModuleHandleA("amdxc64.dll"))
                Log(kUnityLogTypeLog, "AMD Anti-Lag 2 %s (0x%08x)", hr == S_OK ? "available" : "not available", (unsigned)hr);
        }
    }
    else if (type == kUnityGfxDeviceEventShutdown)
    {
        if (s_state & kAntiLag) AMD::AntiLag2DX12::DeInitialize(&s_antiLag);
        s_state &= ~kAntiLag;
        s_d3d12 = nullptr;
    }
}
} // namespace

// ---- exports ------------------------------------------------------------------------------------------------------------

extern "C"
{
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    s_unity = unityInterfaces;
    s_log = unityInterfaces->Get<IUnityLog>();
    s_graphics = unityInterfaces->Get<IUnityGraphics>();

    wchar_t path[MAX_PATH * 2];
    HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCWSTR)&UnityPluginLoad, &self);
    DWORD n = GetModuleFileNameW(self, path, MAX_PATH * 2);
    s_dir.assign(path, n);
    size_t slash = s_dir.find_last_of(L"\\/");
    s_dir = slash == std::wstring::npos ? L"" : s_dir.substr(0, slash + 1);
    if (FilesPresent()) s_state |= kFilesPresent;
    if (FileExists(s_dir + L"sl.dlss_nr.dll") && FileExists(s_dir + L"nvngx_dlssnr.dll")) s_state |= kNrPresent;
    if (FileExists(s_dir + L"sl.nis.dll")) s_state |= kNisPresent;
    if (FileExists(s_dir + L"sl.deepdvc.dll") && FileExists(s_dir + L"nvngx_deepdvc.dll")) s_state |= kDvcPresent;

    wchar_t exe[MAX_PATH * 2];
    s_exeDir.assign(exe, GetModuleFileNameW(nullptr, exe, MAX_PATH * 2));
    slash = s_exeDir.find_last_of(L"\\/");
    s_exeDir = slash == std::wstring::npos ? L"" : s_exeDir.substr(0, slash + 1);
    if (FsrFilesPresent()) s_state |= kFsrPresent;

    // This run's provider of generated frames: FSR where the option (or -fsrframegen) says so, else Streamline
    bool fsr = (s_state & kFsrPresent) && (HasArgument(L"-fsrframegen") || (!HasArgument(L"-framegen") && RegGet(L"Provider", 0) == 1));
    if (fsr)
    {
        if (ShouldArm(true))
        {
            RegSet(L"Starting", 1);
            ArmFsr();
            if (!(s_state & kFsrArmed)) RegSet(L"Starting", 0);
        }
    }
    else if (ShouldArm(false))
    {
        RegSet(L"Starting", 1);
        Arm();
        if (!(s_state & kArmed)) RegSet(L"Starting", 0);   // it never started: nothing to latch
    }
    if (s_graphics) s_graphics->RegisterDeviceEventCallback(OnDeviceEvent);
    // (a plugin loaded after the device exists gets no Initialize event: the samples call it by hand)
    if (s_graphics && s_graphics->GetRenderer() != kUnityGfxRendererNull) OnDeviceEvent(kUnityGfxDeviceEventInitialize);
}

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    if (s_graphics) s_graphics->UnregisterDeviceEventCallback(OnDeviceEvent);
}

/// StateBits.
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_State() { return s_state.load(); }

/// The option for the next start (the registry: read before any managed code runs).
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_SetArmed(int on) { RegSet(L"Armed", on ? 1 : 0); }
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_GetArmed() { return (int)RegGet(L"Armed", 0); }

/// The next start's provider of generated frames: 0 Streamline (DLSS-G, and Reflex and the rest of it), 1 AMD FSR.
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_SetProvider(int provider) { RegSet(L"Provider", provider == 1 ? 1 : 0); }

/// The game reached its first frames with Streamline in: the start is not held against it.
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_StartedOk() { RegSet(L"Starting", 0); }

/// Main thread, first thing in a frame: Reflex's sleep, then the simulation start marker.
/// 'reflexMode': 0 off, 1 low latency, 2 low latency + boost (the sleep and the markers are sent in every mode).
/// 'frameLimitUs': Reflex's own frame rate cap, the frame's time in microseconds (0 = none): the driver paces to it in
/// slReflexSleep, in step with frame generation. Returns 1 while that cap is the one in force (the game then drops its own).
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_FrameStart(uint32_t frame, int reflexMode, uint32_t frameLimitUs)
{
    if (!Bound()) return 0;
    static int modeSet = -1;
    static uint32_t limitSet = 0;
    if (modeSet != reflexMode || limitSet != frameLimitUs)
    {
        sl::ReflexOptions options{};
        options.mode = reflexMode >= 2 ? sl::ReflexMode::eLowLatencyWithBoost : reflexMode == 1 ? sl::ReflexMode::eLowLatency : sl::ReflexMode::eOff;
        options.frameLimitUs = frameLimitUs;
        if (slReflexSetOptions(options) == sl::Result::eOk)
        {
            limitSet = frameLimitUs;
            if (modeSet < 0)
            {
                static sl::ReflexState state;   // (64 frame reports: not on the stack)
                state = sl::ReflexState{};
                if (slReflexGetState(state) == sl::Result::eOk) s_reflexAvailable = state.lowLatencyAvailable ? 1u : 0u;
                Log(kUnityLogTypeLog, "Reflex low latency %s", s_reflexAvailable.load() ? "available" : "not available");
            }
            modeSet = reflexMode;
        }
    }
    if (sl::FrameToken* token = Token(frame))
    {
        slReflexSleep(*token);
        slPCLSetMarker(sl::PCLMarker::eSimulationStart, *token);
    }
    return frameLimitUs != 0 && limitSet == frameLimitUs ? 1 : 0;
}

/// Main thread, first thing in a frame (before the input is read): AMD Anti-Lag 2's delay, and its own frame rate cap
/// ('maxFps', 0 = none). Returns 1 while it is on with that cap in force.
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_AntiLag(int enable, uint32_t maxFps)
{
    if (!(s_state & kAntiLag)) return 0;
    HRESULT hr = AMD::AntiLag2DX12::Update(&s_antiLag, enable != 0, enable ? maxFps : 0);
    return hr == S_OK && enable && maxFps != 0 ? 1 : 0;
}

/// Main thread: the PC latency Reflex measured over its last frame reports, in milliseconds (simulation start to the end
/// of the GPU's work on the frame); 0 when it has no reports (another vendor's GPU, an old driver).
float LatencyMs()
{
    if (!Bound()) return 0.f;
    static sl::ReflexState state;   // (64 frame reports: not on the stack)
    state = sl::ReflexState{};
    if (slReflexGetState(state) != sl::Result::eOk || !state.latencyReportAvailable) return 0.f;
    // The median of the reports: a hitch or a loading frame among them is not the latency
    double samples[64];
    int count = 0;
    for (const sl::ReflexReport& report : state.frameReport)
    {
        // (a whole report only: the driver's part is there once the frame was presented)
        if (report.frameID == 0 || report.simStartTime == 0 || report.presentStartTime == 0 || report.gpuRenderEndTime <= report.simStartTime) continue;
        if (report.gpuRenderEndTime - report.simStartTime > 1000000) continue;
        if (count < 64) samples[count++] = (double)(report.gpuRenderEndTime - report.simStartTime);
    }
    // ($GOF2_SL_VERBOSE: the newest report's times after its simulation start, once in a while)
    static const bool verbose = GetEnvironmentVariableW(L"GOF2_SL_VERBOSE", nullptr, 0) != 0;
    static int calls;
    if (verbose && (++calls % 60) == 20)
    {
        // (sorted by frame: the newest is still being filled in, so a few older ones)
        const sl::ReflexReport* sorted[64];
        int n = 0;
        for (const sl::ReflexReport& report : state.frameReport)
            if (report.frameID != 0 && n < 64) sorted[n++] = &report;
        std::sort(sorted, sorted + n, [](const sl::ReflexReport* a, const sl::ReflexReport* b) { return a->frameID > b->frameID; });
        for (int k : {0, 3, 12, 40})
        {
            if (k >= n) break;
            const sl::ReflexReport* r = sorted[k];
            auto after = [&](uint64_t t) { return t ? (long long)t - (long long)r->simStartTime : -1LL; };
            Log(kUnityLogTypeLog, "Reflex report %d of %d: frame %llu, us after sim start: input %lld, sim end %lld, submit %lld..%lld, present %lld..%lld, "
                "driver %lld..%lld, queue %lld..%lld, gpu %lld..%lld, gpu active %u, gpu frame %u",
                k, n, (unsigned long long)r->frameID, after(r->inputSampleTime), after(r->simEndTime), after(r->renderSubmitStartTime),
                after(r->renderSubmitEndTime), after(r->presentStartTime), after(r->presentEndTime), after(r->driverStartTime),
                after(r->driverEndTime), after(r->osRenderQueueStartTime), after(r->osRenderQueueEndTime),
                after(r->gpuRenderStartTime), after(r->gpuRenderEndTime), r->gpuActiveRenderTimeUs, r->gpuFrameTimeUs);
        }
    }
    if (count == 0) return 0.f;
    std::sort(samples, samples + count);
    return (float)(samples[count / 2] / 1000.0);
}

/// Main thread, the scripts done and the rendering about to be recorded.
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_SimulationEnd(uint32_t frame)
{
    if (!Bound()) return;
    if (sl::FrameToken* token = Token(frame))
    {
        slPCLSetMarker(sl::PCLMarker::eSimulationEnd, *token);
        slPCLSetMarker(sl::PCLMarker::eRenderSubmitStart, *token);
    }
}

UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_EventFunc() { return OnRenderEvent; }

struct Gof2SlInfo
{
    int32_t state, supportResult;
    uint32_t requirementFlags, dlssgStatus, maxFramesToGenerate, vsyncSupported, generating, tagErrors, dynamicSupported, reflexAvailable,
        neuralErrors, neuralRuns, pictureErrors, pictureRuns;
    float presentedPerFrame;   // frames shown per rendered frame since the last call (2 = one generated frame each)
    float latencyMs;           // Reflex's PC latency lately; 0 without reports
};

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Sl_GetInfo(Gof2SlInfo* info)
{
    if (!info) return;
    info->state = s_state.load();
    info->supportResult = s_supportResult;
    info->requirementFlags = s_requirementFlags;
    info->dlssgStatus = s_dlssgStatus.load();
    info->maxFramesToGenerate = s_maxFrames.load();
    info->vsyncSupported = s_vsyncOk.load();
    info->generating = s_generating.load();
    info->tagErrors = s_tagErrors.load();
    info->dynamicSupported = s_dynamicOk.load();
    info->reflexAvailable = s_reflexAvailable.load();
    info->neuralErrors = s_neuralErrors.load();
    info->neuralRuns = s_neuralRuns.load();
    info->pictureErrors = s_pictureErrors.load();
    info->pictureRuns = s_pictureRuns.load();
    uint32_t frames = s_presentedFrames.exchange(0), sum = s_presentedSum.exchange(0);
    info->presentedPerFrame = frames ? (float)sum / (float)frames : 0.f;
    info->latencyMs = LatencyMs();
}

/// The last warning (ours or Streamline's); "" when none.
UNITY_INTERFACE_EXPORT const char* UNITY_INTERFACE_API Gof2Sl_LastError()
{
    std::lock_guard<std::mutex> lock(s_statusMutex);
    snprintf(s_status, sizeof(s_status), "%s", s_lastError.c_str());
    return s_status;
}
}
