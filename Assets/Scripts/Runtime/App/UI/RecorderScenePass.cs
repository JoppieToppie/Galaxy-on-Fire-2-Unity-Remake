// RecorderScenePass.cs
// Remake-only: the screen recorder's picture without the interface (Settings.RecordInterface off; ScreenRecorder): every
// game camera's renderer copies its colour after the post-processing, before the screen-space UI is drawn, into the
// recorder's texture (the last camera of the frame wins, the stack shares one colour target). That colour is linear,
// so the recorder's packer encodes it (RecorderYuv's _Linear). A camera drawing straight to the back buffer has nothing
// to copy: the recorder then takes the screen as it is.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class RecorderScenePass : ScriptableRenderPass
    {
        static RecorderScenePass instance;
        static RTHandle handle;
        static bool installed;

        /// <summary>The frame whose scene is in the texture (-1: none yet).</summary>
        public static int CopiedFrame { get; private set; } = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { handle = null; CopiedFrame = -1; }

        /// <summary>Copies into 'target' from now on; null stops it.</summary>
        public static void Set(RenderTexture target)
        {
            handle?.Release();
            handle = target != null ? RTHandles.Alloc(target) : null;
            CopiedFrame = -1;
            if (installed || target == null) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (handle == null || cam.cameraType != CameraType.Game || cam.targetTexture != null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            instance ??= new RecorderScenePass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            data.scriptableRenderer.EnqueuePass(instance);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (handle == null) return;
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            var source = resources.activeColorTexture;
            if (!source.IsValid()) return;
            var target = renderGraph.ImportTexture(handle);
            renderGraph.AddBlitPass(source, target, Vector2.one, Vector2.zero, passName: "Recorder Scene");
            CopiedFrame = Time.frameCount;
        }
    }
}
