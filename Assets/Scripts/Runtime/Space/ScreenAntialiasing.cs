// ScreenAntialiasing.cs
// Remake: the "Post-process AA" option (Settings.PostAntialiasing) on the cameras that draw the game to the screen: SMAA
// (the scenes' cameras as built: SpaceSceneBuilder, MainMenuBuilder, StationSceneBuilder; the star map's), FXAA, TAA (URP's
// temporal AA; SMAA while MSAA or a temporal upscaler is on) or none.
// A camera is taken in once (Register: each loaded scene's screen cameras that have anti-aliasing of their own, the star
// map's, the VR eye) and follows every options change (Bootstrap.ApplyUpscaling -> ApplyAll, after the MSAA it decides
// on). The station camera prefers URP's TAA (StationLevel.ApplyAntialiasing: the rooms' thin glossy parts shimmered under
// SMAA as the camera swayed) while SMAA is picked and neither a temporal upscaler nor MSAA rules TAA out.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ScreenAntialiasing
    {
        // The cameras that follow the option; true = it prefers TAA where SMAA is picked.
        static readonly Dictionary<Camera, bool> cameras = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cameras.Clear();

        /// <summary>The camera mode for the option ('option': Settings.PostAa*): FXAA, none, TAA, or SMAA, which a camera
        /// that prefers TAA swaps for it. TAA only where neither a temporal upscaler (its own anti-aliasing) nor MSAA (URP's
        /// TAA needs it off: UniversalCameraData.IsTemporalAAEnabled, else no anti-aliasing at all) is on; SMAA then.</summary>
        public static AntialiasingMode Mode(int option, bool prefersTemporal, bool temporalUpscaler, bool msaa)
        {
            bool taa = !temporalUpscaler && !msaa;
            return option switch
            {
                Settings.PostAaFxaa => AntialiasingMode.FastApproximateAntialiasing,
                Settings.PostAaOff => AntialiasingMode.None,
                Settings.PostAaTaa when taa => AntialiasingMode.TemporalAntiAliasing,
                Settings.PostAaSmaa when taa && prefersTemporal => AntialiasingMode.TemporalAntiAliasing,
                _ => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            };
        }

        /// <summary>'cam' follows the option from now on (applied at once). Once a camera prefers TAA it keeps that.</summary>
        public static void Register(Camera cam, bool prefersTemporal = false)
        {
            if (cam == null) return;
            prefersTemporal |= cameras.TryGetValue(cam, out bool before) && before;
            cameras[cam] = prefersTemporal;
            Apply(cam, prefersTemporal);
        }

        /// <summary>'eye' draws what 'source' would (the VR eye): it follows the option as the source does, if it does.</summary>
        public static void Follow(Camera eye, Camera source)
        {
            if (source != null && cameras.TryGetValue(source, out bool temporal)) Register(eye, temporal);
        }

        /// <summary>A loaded scene's cameras that draw to the screen (base cameras without a target texture) with
        /// anti-aliasing of their own: registered (one already on TAA prefers it).</summary>
        public static void RegisterScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cameras.ContainsKey(cam) || cam.targetTexture != null || !cam.TryGetComponent(out UniversalAdditionalCameraData data)) continue;
                    if (data.renderType != CameraRenderType.Base || data.antialiasing == AntialiasingMode.None) continue;
                    Register(cam, data.antialiasing == AntialiasingMode.TemporalAntiAliasing);
                }
        }

        /// <summary>The option on every registered camera (destroyed ones dropped).</summary>
        public static void ApplyAll()
        {
            List<Camera> gone = null;
            foreach (var kv in cameras)
                if (kv.Key == null) (gone ??= new List<Camera>()).Add(kv.Key);
                else Apply(kv.Key, kv.Value);
            if (gone != null) foreach (var cam in gone) cameras.Remove(cam);
        }

        static void Apply(Camera cam, bool prefersTemporal)
        {
            if (!cam.TryGetComponent(out UniversalAdditionalCameraData data)) return;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool temporalUpscaler = World.SpaceHooks.TemporalUpscaler != null && World.SpaceHooks.TemporalUpscaler();
            var mode = Mode(Settings.PostAntialiasing, prefersTemporal, temporalUpscaler, urp != null && urp.msaaSampleCount > 1);
            if (data.antialiasing != mode) data.antialiasing = mode;
        }
    }
}
