// UpscalerFramework.cs
// Remake: NVIDIA DLSS and AMD FSR 2 / 3 / 4 through URP's upscaler framework (Unity 6.7, URP 17.7). The framework and its
// DLSS / FSR upscalers are in the render pipeline packages behind the undocumented ENABLE_UPSCALER_FRAMEWORK define (set for
// the Standalone platforms in the Player settings, and for iOS by IosUpscalerDefine), on the built-in modules com.unity.modules.nvidia / com.unity.modules.amd
// (ENABLE_NVIDIA / ENABLE_AMD on Windows). With the framework compiled in, URP ignores the asset's old upscaling filter: the
// camera uses the framework's active upscaler (UniversalRenderPipeline.SetUpscaler), so every choice of the Upscaler option
// goes through here: Off = unity.auto (bilinear / point by the render scale), FSR 1 = amd.fsr1, STP = unity.stp, DLSS =
// nvidia.dlss4, FSR = the newest the device runs of amd.fsr4 / fsr3 / fsr2. DLSS and FSR 2+ pick their render resolution
// from the quality mode (the Upscaler quality option), not the render scale, and are temporal (anti-aliasing included,
// MSAA off like STP). URP resolves the active upscaler once per pipeline instance, so Bootstrap applies it again whenever
// URP creates its pipeline (RenderPipelineManager.activeRenderPipelineCreated). Which upscalers run on this device comes from the
// framework's own checks (IUpscaler.isSupportedOnDevice: the NVIDIA / AMD plugin, the GPU and the graphics API: DLSS and
// FSR 2 on Direct3D 11 / 12 or Vulkan, FSR 3 / 4 on Direct3D 12 only, FSR 4 on AMD's newest GPUs; on Linux the AMD module
// isn't there: LinuxFsrUpscaler registers FSR 4 / 3 / 2 under the same ids, on Vulkan with the player's AMD DLLs), read from the pipeline's
// framework instance (UniversalRenderPipeline.upscaling, internal: one reflection read). On iOS the framework offers Auto, FSR 1,
// STP and MetalFX (no NVIDIA / AMD plugins there). Without the define (Android) all of
// this compiles to nothing and Bootstrap keeps the asset's upscaling filter.
// Apple MetalFX (apple.metalfx-spatial / apple.metalfx-temporal): the render pipeline core's own MetalFX upscalers, compiled
// in with the built-in module com.unity.modules.metalfx (ENABLE_METALFX_MODULE) for macOS / iOS players (and the Editor with
// a macOS target) on Metal. Unlike DLSS / FSR 2+ they have no quality modes: the render scale sets their input resolution
// (MetalFX Temporal's GetResolutionInfo returns the device's supported range and URP clamps the render scale into it).
// Spatial is a single-frame upscaler after post-processing (like FSR 1); Temporal is jittered TAA + upscaling (like STP:
// MSAA off). Device support: MTLFXSpatialScalerDescriptor / MTLFXTemporalScalerDescriptor.supportsDevice (macOS 13+,
// Apple silicon / recent AMD GPUs).

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class UpscalerFramework
    {
        public const string Auto = "unity.auto", Fsr1 = "amd.fsr1", Stp = "unity.stp", Dlss = "nvidia.dlss4",
            Fsr4 = "amd.fsr4", Fsr3 = "amd.fsr3", Fsr2 = "amd.fsr2",
            MetalFxSpatial = "apple.metalfx-spatial", MetalFxTemporal = "apple.metalfx-temporal",
            // LinuxFsrUpscaler on Windows Vulkan (its plugin with AMD's Vulkan DLLs as they are: a comparison build)
            VkFsr4 = "ffxvk.fsr4", VkFsr3 = "ffxvk.fsr3", VkFsr2 = "ffxvk.fsr2";

        /// <summary>The upscaler quality option: 0 native (DLAA / FSR native AA), 1 quality, 2 balanced, 3 performance, 4 ultra
        /// performance.</summary>
        public const int QualityNative = 0, QualityQuality = 1, QualityBalanced = 2, QualityPerformance = 3, QualityUltra = 4;

#if ENABLE_UPSCALER_FRAMEWORK
        /// <summary>The framework is compiled in (desktop and iOS builds).</summary>
        public static bool Compiled => true;

        static UniversalRenderPipeline Pipeline => RenderPipelineManager.currentPipeline as UniversalRenderPipeline;

        static System.Reflection.FieldInfo upscalingField;

        /// <summary>The pipeline's framework instance (UniversalRenderPipeline.upscaling, internal static).</summary>
        static Upscaling Instance
        {
            get
            {
                try
                {
                    upscalingField ??= typeof(UniversalRenderPipeline).GetField("upscaling",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    return upscalingField?.GetValue(null) as Upscaling;
                }
                catch (System.Exception) { return null; }
            }
        }

        /// <summary>The upscaler is registered and runs on this device (false until URP has made its pipeline).</summary>
        public static bool Supports(string id)
        {
            var u = Instance?.GetIUpscalerById(id);
            if (u == null) return false;
            try { return u.isSupportedOnDevice; }
            catch (System.Exception) { return false; }
        }

        public static bool DlssSupported => Supports(Dlss);

        /// <summary>MetalFX Spatial / Temporal on this device (macOS / iOS builds on Metal with the MetalFX module; iOS 16+,
        /// A13 or newer for Temporal per Apple's supportsDevice).</summary>
        public static bool MetalFxSpatialSupported => Supports(MetalFxSpatial);
        public static bool MetalFxTemporalSupported => Supports(MetalFxTemporal);

        /// <summary>The newest FSR (4, 3, 2) this device runs, or null.</summary>
        public static string BestFsr
        {
            get
            {
                // (test switch: $GOF2_FSR_VERSION=3 / 2 takes that one where the device runs it, to compare FSR 3 with 4)
                string v = System.Environment.GetEnvironmentVariable("GOF2_FSR_VERSION");
                foreach (var id in v == "3" ? new[] { Fsr3, VkFsr3 } : v == "2" ? new[] { Fsr2, VkFsr2 } : System.Array.Empty<string>())
                    if (Supports(id)) return id;
                foreach (var id in new[] { Fsr4, VkFsr4, Fsr3, VkFsr3, Fsr2, VkFsr2 })   // (the newest FSR first, whoever runs it)
                    if (Supports(id)) return id;
                return null;
            }
        }

        /// <summary>"FSR 4" / "FSR 3" / "FSR 2" for the option, or null.</summary>
        public static string BestFsrLabel => BestFsr switch { Fsr4 or VkFsr4 => "FSR 4", Fsr3 or VkFsr3 => "FSR 3", Fsr2 or VkFsr2 => "FSR 2", _ => null };

        /// <summary>The upscaler the framework renders with now (its id; empty before the pipeline exists).</summary>
        public static string ActiveId => Instance?.activeUpscaler?.upscalerId ?? "";

        /// <summary>The pipeline exists (the upscalers' device checks have run).</summary>
        public static bool Ready => Pipeline != null && Instance != null;

        /// <summary>Activates the upscaler 'id' (one this device doesn't run falls back to Auto) with the DLSS / FSR quality
        /// mode 'quality'; false before URP has made its pipeline (Bootstrap applies it again then).</summary>
        public static bool Apply(string id, int quality, bool fsr4Lite = false)
        {
            var pipeline = Pipeline;
            var instance = Instance;
            if (pipeline == null || instance == null) return false;
            if (string.IsNullOrEmpty(id) || !Supports(id)) id = Auto;
            SetQuality(instance, id, Mathf.Clamp(quality, QualityNative, QualityUltra), fsr4Lite);
            if (instance.activeUpscaler?.upscalerId != id && !pipeline.SetUpscaler(id)) pipeline.SetUpscaler(Auto);
            return true;
        }

        static CommandBuffer cleanupCmd;

        /// <summary>Destroys the upscaler contexts URP has given up on, after every camera. The framework keeps one DLSS / FSR
        /// context per camera (~190 MB of VRAM at 3440 x 1440 for DLSS: its feature and history) and drops it once the camera
        /// hasn't rendered for 400 frames, or when the resolution or quality changes; UniversalRenderPipeline.Render records
        /// those DestroyFeature calls into a command buffer after the frame's last Submit and never submits again, so they
        /// never ran: every new camera (the star map, the item window, each scene's) and every camera switched off for 400
        /// frames (the level's behind the map) left its context in VRAM for good (a player build reached 7 GB in 20 map
        /// visits; reported on 50-series cards, measured on a 4080). Run here, the cleanup finds them first and its
        /// commands are submitted at once; URP's own call then has nothing left to do. Bootstrap hooks it.</summary>
        public static void FlushContexts(ScriptableRenderContext context, Camera camera)
        {
            var instance = Instance;
            if (instance == null) return;
            cleanupCmd ??= new CommandBuffer { name = "Upscaler context cleanup (remake)" };
            instance.CleanupExpiredContexts(cleanupCmd);
            if (cleanupCmd.sizeInBytes > 0)
            {
                context.ExecuteCommandBuffer(cleanupCmd);
                context.Submit();
            }
            cleanupCmd.Clear();
        }

        /// <summary>The quality mode on the upscaler's options (the framework's global options; a changed mode recreates the
        /// upscaler's context).</summary>
        static void SetQuality(Upscaling instance, string id, int q, bool fsr4Lite)
        {
            var upscaler = instance.GetIUpscalerById(id);
            var options = upscaler != null ? instance.GetGlobalOptions(upscaler) : null;
            if (options == null) return;
#if ENABLE_NVIDIA && ENABLE_NVIDIA_MODULE
            if (options is DLSSOptions dlss)
                dlss.dlssQualityMode = q switch
                {
                    QualityNative => UnityEngine.NVIDIA.DLSSQuality.DLAA,
                    QualityQuality => UnityEngine.NVIDIA.DLSSQuality.MaximumQuality,
                    QualityBalanced => UnityEngine.NVIDIA.DLSSQuality.Balanced,
                    QualityPerformance => UnityEngine.NVIDIA.DLSSQuality.MaximumPerformance,
                    _ => UnityEngine.NVIDIA.DLSSQuality.UltraPerformance,
                };
#endif
#if ENABLE_AMD && ENABLE_AMD_MODULE
            // FSR 3 / 4: native AA, quality, balanced, performance, ultra performance (the option's order); FSR 2 has no native
            // mode: its best is quality.
            if (options is FSR4Options fsr4) fsr4.fsr4QualityMode = (UnityEngine.AMD.FSR4Quality)q;
            else if (options is FSR3Options fsr3) fsr3.fsr3QualityMode = (UnityEngine.AMD.FSR3Quality)q;
            else if (options is FSR2Options fsr2) fsr2.fsr2QualityMode = (UnityEngine.AMD.FSR2Quality)Mathf.Max(0, q - 1);
#endif
            // Linux (LinuxFsrUpscaler: the same ids, its own options; FSR 2 through the SDK takes native AA too)
            if (options is LinuxFsrOptions linux)
            {
                linux.qualityMode = q;
                linux.lite = fsr4Lite;
            }
        }

        /// <summary>The option "FSR 4 Lite" applies: FSR 4 is the FSR this device runs, through ffx-vk-linux (Linux).</summary>
        public static bool Fsr4LiteAvailable
        {
            get
            {
#if UNITY_STANDALONE_LINUX
                return BestFsr == Fsr4 && LinuxFsr.LiteAvailable;
#else
                return false;
#endif
            }
        }
#else
        public static bool Compiled => false;
        public static bool Supports(string id) => false;
        public static bool DlssSupported => false;
        public static bool MetalFxSpatialSupported => false;
        public static bool MetalFxTemporalSupported => false;
        public static string BestFsr => null;
        public static string BestFsrLabel => null;
        public static string ActiveId => "";
        public static bool Ready => false;
        public static bool Apply(string id, int quality, bool fsr4Lite = false) => false;
        public static bool Fsr4LiteAvailable => false;
        public static void FlushContexts(ScriptableRenderContext context, Camera camera) { }
#endif
    }
}
