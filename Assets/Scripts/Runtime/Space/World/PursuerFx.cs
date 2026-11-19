// PursuerFx.cs
// Remake mods (2026-11-11, Chorus's Guilt: PP_Guilt, BP_NPC_Guilt's ProximityIntensityMax 0.4): the screen darkening
// toward a pursuer (Flight.NpcRoles' "pursuer" role, World.NpcShip) as it nears: each pursuer reports its proximity each
// frame (Near, the strongest kept); a global URP volume (made on first use) lays a dark red vignette and drains the colour
// by it, easing in and out. No pursuer, no volume weight.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class PursuerFx : MonoBehaviour
    {
        static PursuerFx instance;
        static float wanted;
        Volume volume;
        VolumeProfile profile;
        Vignette vignette;
        ColorAdjustments colour;
        float shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; wanted = 0f; }

        /// <summary>A pursuer this close now (0..NpcRoles.ProximityMax): the effect follows the strongest this frame.</summary>
        public static void Near(float intensity)
        {
            if (intensity <= 0f && instance == null) return;
            if (instance == null && Application.isPlaying)
            {
                var go = new GameObject("Pursuer proximity");
                instance = go.AddComponent<PursuerFx>();
            }
            wanted = Mathf.Max(wanted, intensity);
        }

        /// <summary>The effect's strength from 'shown' (0..1) as the volume reads it: the vignette's intensity and the colour drained.</summary>
        public static (float vignette, float saturation) Strength(float shown) => (Mathf.Clamp01(shown) * 1.2f, -100f * Mathf.Clamp01(shown));

        void Awake()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vignette = profile.Add<Vignette>(true);
            vignette.color.Override(new Color(0.25f, 0f, 0f));
            vignette.smoothness.Override(0.6f);
            vignette.intensity.Override(0f);
            colour = profile.Add<ColorAdjustments>(true);
            colour.saturation.Override(0f);
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.sharedProfile = profile;
            volume.weight = 0f;
        }

        void LateUpdate()
        {
            shown = Mathf.MoveTowards(shown, wanted, Time.deltaTime);   // (a second from none to full)
            wanted = 0f;
            var (v, sat) = Strength(shown);
            vignette.intensity.Override(v);
            colour.saturation.Override(sat);
            volume.weight = shown > 0f ? 1f : 0f;
        }

        void OnDestroy()
        {
            if (profile != null) Destroy(profile);
            if (instance == this) instance = null;
        }
    }
}
