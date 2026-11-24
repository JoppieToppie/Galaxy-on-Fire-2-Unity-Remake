// gof2_xess.cpp
// Intel XeSS Super Resolution in the Windows Direct3D 12 player (docs/notes/options.md "XeSS"): the Unity native plugin
// behind XessUpscaler (URP's upscaler framework, like LinuxFsrUpscaler's gof2_fsr). XeSS-SR runs on any GPU with Shader
// Model 6.4, so this is the one temporal upscaler every vendor's cards share.
//   - Intel's libxess.dll (the XeSS SDK's, github.com/intel/xess, beside this plugin in the build) is delay-loaded: the
//     plugin loads without it, and Gof2Xess_Init then says "no".
//   - Contexts are made, run and destroyed by plugin events on Unity's render thread (IssuePluginEventAndData), with
//     Unity's own command list (IUnityGraphicsD3D12v8::CommandRecordingState); Unity binds its state again after
//     (kUnityD3D12EventConfigFlag_ModifiesCommandBuffersState).
//   - A destroyed context is kept a moment first: the GPU may still be running its last frame.
// Build: Native/XeSS/CMakeLists.txt.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D12.h"
#include "IUnityLog.h"

#include "xess/xess.h"
#include "xess/xess_d3d12.h"

namespace
{
const int kEventCreate = 0x58455301, kEventDispatch = 0x58455302, kEventDestroy = 0x58455303;
const int kSlots = 64;

IUnityInterfaces* s_unity;
IUnityGraphics* s_graphics;
IUnityGraphicsD3D12v8* s_d3d12;
IUnityLog* s_log;
std::wstring s_dir;
bool s_tried, s_supported;
char s_status[256] = "not started";
xess_context_handle_t s_query;   // a context for the main thread's questions (the input resolution of a quality mode)
xess_context_handle_t s_contexts[kSlots];

struct Pending { xess_context_handle_t context; ULONGLONG at; };
std::vector<Pending> s_pending;   // render thread only

void Log(UnityLogType type, const char* format, ...)
{
    char text[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(text, sizeof(text), format, args);
    va_end(args);
    char line[1100];
    snprintf(line, sizeof(line), "XeSS: %s", text);
    if (s_log) s_log->Log(type, line, __FILE__, __LINE__);
}

/// libxess.dll from beside this plugin, before the first call into it (the delay-load helper then finds it loaded).
bool LoadXess()
{
    static int loaded = -1;
    if (loaded < 0) loaded = LoadLibraryExW((s_dir + L"libxess.dll").c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH) ? 1 : 0;
    return loaded == 1;
}

// The option's quality modes (UpscalerFramework.Quality*): 0 native, 1 quality, 2 balanced, 3 performance, 4 ultra performance
xess_quality_settings_t Quality(int quality)
{
    switch (quality)
    {
    case 0: return XESS_QUALITY_SETTING_AA;
    case 1: return XESS_QUALITY_SETTING_QUALITY;
    case 2: return XESS_QUALITY_SETTING_BALANCED;
    case 3: return XESS_QUALITY_SETTING_PERFORMANCE;
    default: return XESS_QUALITY_SETTING_ULTRA_PERFORMANCE;
    }
}

struct CreateData
{
    int32_t handle, quality;
    uint32_t displayWidth, displayHeight, flags;   // flags: XESS_INIT_FLAG_*
};

struct DispatchData
{
    int32_t handle, reset;
    void* color;         // ID3D12Resource: the camera's colour at the render resolution
    void* depth;         // the depth texture, and
    void* depthBuffer;   // the depth render buffer (a depth-only render texture's texture pointer may be something else)
    void* motion;        // R16G16F motion vectors
    void* output;        // the result at the display resolution (unordered access)
    float jitterX, jitterY, motionScaleX, motionScaleY;
    uint32_t renderWidth, renderHeight;
};

bool IsDepth(ID3D12Resource* resource)
{
    if (!resource) return false;
    switch (resource->GetDesc().Format)
    {
    case DXGI_FORMAT_R32_TYPELESS: case DXGI_FORMAT_D32_FLOAT: case DXGI_FORMAT_R32_FLOAT:
    case DXGI_FORMAT_R32G8X24_TYPELESS: case DXGI_FORMAT_D32_FLOAT_S8X24_UINT: case DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS:
    case DXGI_FORMAT_R24G8_TYPELESS: case DXGI_FORMAT_D24_UNORM_S8_UINT: case DXGI_FORMAT_R24_UNORM_X8_TYPELESS:
    case DXGI_FORMAT_R16_TYPELESS: case DXGI_FORMAT_D16_UNORM: case DXGI_FORMAT_R16_UNORM:
        return true;
    default:
        return false;
    }
}

/// Contexts given up a while ago: the GPU is done with them.
void Reap(bool all)
{
    ULONGLONG now = GetTickCount64();
    for (size_t i = 0; i < s_pending.size();)
    {
        if (!all && now - s_pending[i].at < 2000) { i++; continue; }
        xessDestroyContext(s_pending[i].context);
        s_pending.erase(s_pending.begin() + i);
    }
}

void Release(int handle)
{
    if (handle < 0 || handle >= kSlots || !s_contexts[handle]) return;
    s_pending.push_back({s_contexts[handle], GetTickCount64()});
    s_contexts[handle] = nullptr;
}

void OnCreate(const CreateData* d)
{
    if (!d || d->handle < 0 || d->handle >= kSlots || !s_supported) return;
    Release(d->handle);
    ID3D12Device* device = s_d3d12->GetDevice();
    xess_context_handle_t context = nullptr;
    xess_result_t result = xessD3D12CreateContext(device, &context);
    if (result == XESS_RESULT_SUCCESS)
    {
        xess_d3d12_init_params_t init{};
        init.outputResolution.x = d->displayWidth;
        init.outputResolution.y = d->displayHeight;
        init.qualitySetting = Quality(d->quality);
        init.initFlags = d->flags;
        result = xessD3D12Init(context, &init);   // (builds its pipelines here: a moment's wait, once per context)
    }
    if (result != XESS_RESULT_SUCCESS)
    {
        Log(kUnityLogTypeWarning, "no context for %u x %u, quality %d, flags 0x%x (%d)", d->displayWidth, d->displayHeight, d->quality, d->flags, (int)result);
        if (context) xessDestroyContext(context);
        return;
    }
    s_contexts[d->handle] = context;
    Log(kUnityLogTypeLog, "context %d: %u x %u, quality %d, flags 0x%x", d->handle, d->displayWidth, d->displayHeight, d->quality, d->flags);
}

void OnDispatch(const DispatchData* d)
{
    if (!d || d->handle < 0 || d->handle >= kSlots) return;
    xess_context_handle_t context = s_contexts[d->handle];
    auto color = (ID3D12Resource*)d->color;
    auto motion = (ID3D12Resource*)d->motion;
    auto output = (ID3D12Resource*)d->output;
    // The depth: whichever of the two pointers is a depth texture
    auto depth = IsDepth((ID3D12Resource*)d->depth) ? (ID3D12Resource*)d->depth : (ID3D12Resource*)d->depthBuffer;
    UnityGraphicsD3D12RecordingState recording{};
    if (!color || !output || !s_d3d12->CommandRecordingState(&recording) || !recording.commandList) return;

    static int warned;
    bool ready = context && motion && depth;
    if (!ready && warned++ < 5)
        Log(kUnityLogTypeWarning, "dispatch without %s (colour %p depth %p / %p motion %p)", context ? "a texture" : "a context", d->color, d->depth, d->depthBuffer, d->motion);
    xess_result_t result = XESS_RESULT_ERROR_UNINITIALIZED;
    if (ready)
    {
        s_d3d12->RequestResourceState(color, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        s_d3d12->RequestResourceState(motion, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        s_d3d12->RequestResourceState(depth, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        s_d3d12->RequestResourceState(output, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        xessSetVelocityScale(context, d->motionScaleX, d->motionScaleY);
        xess_d3d12_execute_params_t params{};
        params.pColorTexture = color;
        params.pVelocityTexture = motion;
        params.pDepthTexture = depth;
        params.pOutputTexture = output;
        params.jitterOffsetX = d->jitterX;
        params.jitterOffsetY = d->jitterY;
        params.exposureScale = 1.f;
        params.resetHistory = d->reset ? 1u : 0u;
        params.inputWidth = d->renderWidth;
        params.inputHeight = d->renderHeight;
        result = xessD3D12Execute(context, recording.commandList, &params);
        if (result != XESS_RESULT_SUCCESS && warned++ < 5) Log(kUnityLogTypeWarning, "execute failed (%d)", (int)result);
    }
    if (result != XESS_RESULT_SUCCESS)
    {
        // Nothing upscaled: at least a picture where the sizes allow a plain copy (the native quality mode)
        D3D12_RESOURCE_DESC a = color->GetDesc(), b = output->GetDesc();
        if (a.Width == b.Width && a.Height == b.Height && a.Format == b.Format)
        {
            s_d3d12->RequestResourceState(color, D3D12_RESOURCE_STATE_COPY_SOURCE);
            s_d3d12->RequestResourceState(output, D3D12_RESOURCE_STATE_COPY_DEST);
            recording.commandList->CopyResource(output, color);
        }
    }
}

void UNITY_INTERFACE_API OnRenderEvent(int eventId, void* data)
{
    if (!s_d3d12 || !s_supported) return;
    Reap(false);
    if (eventId == kEventCreate) OnCreate((const CreateData*)data);
    else if (eventId == kEventDispatch) OnDispatch((const DispatchData*)data);
    else if (eventId == kEventDestroy && data) Release(((const CreateData*)data)->handle);
}

void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType type)
{
    if (type == kUnityGfxDeviceEventInitialize)
    {
        if (!s_graphics || s_graphics->GetRenderer() != kUnityGfxRendererD3D12) return;
        s_d3d12 = s_unity->Get<IUnityGraphicsD3D12v8>();
        if (!s_d3d12) return;
        UnityD3D12PluginEventConfig config{};
        config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
        config.flags = kUnityD3D12EventConfigFlag_ModifiesCommandBuffersState;
        config.ensureActiveRenderTextureIsBound = false;
        s_d3d12->ConfigureEvent(kEventCreate, &config);
        s_d3d12->ConfigureEvent(kEventDispatch, &config);
        s_d3d12->ConfigureEvent(kEventDestroy, &config);
    }
    else if (type == kUnityGfxDeviceEventShutdown)
    {
        if (s_supported)
        {
            for (int i = 0; i < kSlots; i++) Release(i);
            Reap(true);
            if (s_query) xessDestroyContext(s_query);
            s_query = nullptr;
        }
        s_supported = false;
        s_d3d12 = nullptr;
    }
}
} // namespace

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

    if (s_graphics) s_graphics->RegisterDeviceEventCallback(OnDeviceEvent);
    // (a plugin loaded after the device exists gets no Initialize event)
    if (s_graphics && s_graphics->GetRenderer() != kUnityGfxRendererNull) OnDeviceEvent(kUnityGfxDeviceEventInitialize);
}

void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    if (s_graphics) s_graphics->UnregisterDeviceEventCallback(OnDeviceEvent);
}

/// Main thread, once: loads libxess.dll and asks XeSS whether it runs on Unity's device. 1 when it does.
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Xess_Init()
{
    if (s_tried) return s_supported ? 1 : 0;
    s_tried = true;
    if (!s_d3d12) { snprintf(s_status, sizeof(s_status), "not Direct3D 12"); return 0; }
    if (!LoadXess()) { snprintf(s_status, sizeof(s_status), "libxess.dll is not in the build"); return 0; }
    xess_version_t version{};
    xessGetVersion(&version);
    xess_result_t result = xessD3D12CreateContext(s_d3d12->GetDevice(), &s_query);
    s_supported = result == XESS_RESULT_SUCCESS && s_query;
    snprintf(s_status, sizeof(s_status), "XeSS %u.%u.%u, %s (%d)", version.major, version.minor, version.patch,
        s_supported ? "runs on this GPU" : "not supported on this GPU", (int)result);
    return s_supported ? 1 : 0;
}

UNITY_INTERFACE_EXPORT const char* UNITY_INTERFACE_API Gof2Xess_Status() { return s_status; }

/// Main thread: the render resolution XeSS wants for a quality mode at this display resolution. 0 when it can't say.
int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Xess_InputResolution(int quality, uint32_t displayWidth, uint32_t displayHeight, uint32_t* width, uint32_t* height)
{
    if (!s_supported || !s_query || !width || !height) return 0;
    xess_2d_t output{displayWidth, displayHeight}, optimal{}, lowest{}, highest{};
    if (xessGetOptimalInputResolution(s_query, &output, Quality(quality), &optimal, &lowest, &highest) != XESS_RESULT_SUCCESS) return 0;
    if (!optimal.x || !optimal.y) return 0;
    *width = optimal.x;
    *height = optimal.y;
    return 1;
}

UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API Gof2Xess_EventFunc() { return OnRenderEvent; }
}
