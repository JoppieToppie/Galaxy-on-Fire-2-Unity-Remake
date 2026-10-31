// WarpSky.cs
// Remake: the warp of the cruise drives and of the space between the systems (Data.InterstellarSpace), EVERSPACE 2's travel
// mode: its M_TravelMode_WarpTunnel is a refraction, the sky bent into streaks round the flight, with no colour of its own.
// So this is no object: GoF2/SpaceSky stretches the sky itself by the globals set here, and at level 0 it is the plain sky,
// which makes the start and the end seamless; ships, planets and suns draw over it untouched. Between the systems (and easing
// out of them) it takes after the Khador Drive's animation: that fx's vein web runs through it, and it leans to its ice blue;
// a cruise in an orbit is the plain stretch. Always full between the
// systems; in an orbit it follows the cruise drive's ramp, and an arrival from between the systems eases out of it. The
// images and sounds are a mod's warp.json (Modding.ModWarp), else made here, so it works with no mod's art.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class WarpSky : MonoBehaviour
    {
        /// <summary>How long the speed takes to ease out into interstellar space, and the speed and warp out of it: an arrival
        /// is a fly-in, the orbit growing out of the warp (InterstellarSpace.ArriveUnits).</summary>
        public const float EntryCarryMs = 2000f, ArrivalCarryMs = 7000f;
        /// <summary>The light a load is hidden in: going in, the Khador fx's white, carried on and faded out here; a fall out of
        /// the slipstream's short ice-blue dissolve. An arrival flown needs none: the orbit was there through the approach
        /// (SystemApproach).</summary>
        const float EntryFadeMs = 800f, ArrivalDissolveMs = 550f, ArrivalDissolve = 0.8f;
        static readonly Color EntryLight = new Color(0.86f, 0.91f, 1f), ArrivalLight = new Color(0.55f, 0.72f, 1f);
        /// <summary>How fast the sky runs past (its own units a second): at rest, and added at full cruise.</summary>
        const float IdleFlow = 0.45f, CruiseFlow = 0.9f;
        /// <summary>Between the systems, how much of the warp's sound plays with the drive off.</summary>
        const float IdleVolume = 0.4f;
        /// <summary>How fast the warp's centre follows the nose (per second).</summary>
        const float AxisFollow = 6f;

        static readonly int Level = Shader.PropertyToID("_WarpLevel"), Time_ = Shader.PropertyToID("_WarpTime"), Axis = Shader.PropertyToID("_WarpAxis"),
                            Tint = Shader.PropertyToID("_WarpTint"), Noise = Shader.PropertyToID("_WarpNoise"), Detail = Shader.PropertyToID("_WarpDetail"),
                            Sparks = Shader.PropertyToID("_WarpSparks"), Arcs = Shader.PropertyToID("_WarpArcs"),
                            Khador = Shader.PropertyToID("_WarpKhador");

        Navigation nav;
        ShipController ship;
        bool interstellar;
        float arrivalMs, lightMs, lightStartMs, lightStrength;
        Color lightColour;
        /// <summary>The sky runs on across a load, and the loop plays through it (CarrySound).</summary>
        static float flow;
        static AudioSource carriedLoop;
        SystemJump jump;
        Vector3 axis;
        Texture2D ownNoise, ownDetail, ownSparks;
        AudioSource loop, shot;
        AudioClip enterClip;
        bool wasCruising;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { flow = 0f; carriedLoop = null; }

        /// <summary>The level's warp. 'arriving': the ship came in from between the systems, at speed; 'unseen': with no
        /// approach to the orbit (it fell out on the way).</summary>
        public void Setup(SpaceLevel level, bool arriving, bool unseen, SystemJump systemJump)
        {
            jump = systemJump;
            nav = level.Navigation;
            ship = level.Player;
            interstellar = level.Layout.interstellar;
            arrivalMs = arriving ? ArrivalCarryMs : 0f;
            if (interstellar) Light(EntryLight, EntryFadeMs, 1f);
            else if (arriving && unseen) Light(ArrivalLight, ArrivalDissolveMs, ArrivalDissolve);
            axis = ship.transform.forward;
            var warp = ModWarp.Current;
            Shader.SetGlobalTexture(Noise, Layer(warp, warp?.tunnel, ref ownNoise, 16, 16, 1));
            Shader.SetGlobalTexture(Detail, Layer(warp, warp?.detail, ref ownDetail, 32, 8, 2));
            Shader.SetGlobalTexture(Sparks, Layer(warp, warp?.streaks, ref ownSparks, 64, 4, 3));
            Shader.SetGlobalColor(Tint, warp != null && warp.tint.HasValue ? warp.tint.Value : new Color(0.55f, 0.82f, 1f));
            Shader.SetGlobalTexture(Arcs, KhadorSheet() ?? Texture2D.blackTexture);
            Apply(Strength(0f, 0f));
            if (warp == null) return;
            shot = gameObject.AddComponent<AudioSource>();
            shot.playOnAwake = false;
            if (!string.IsNullOrEmpty(warp.enter)) ModAudio.Load(warp.mod, warp.enter, c => enterClip = c, false);
            if (carriedLoop != null)
            {
                loop = carriedLoop;
                carriedLoop = null;
                loop.transform.SetParent(transform, false);
                return;
            }
            loop = new GameObject("Warp sound").AddComponent<AudioSource>();
            loop.transform.SetParent(transform, false);
            loop.playOnAwake = false;
            loop.loop = true;
            if (!string.IsNullOrEmpty(warp.loop)) ModAudio.Load(warp.mod, warp.loop, c => { if (this != null && loop != null) { loop.clip = c; loop.volume = 0f; if (c != null) loop.Play(); } }, false);
        }

        /// <summary>The loop plays on through the swap to the next level's warp.</summary>
        public void CarrySound()
        {
            if (loop == null) return;
            loop.transform.SetParent(null, false);
            DontDestroyOnLoad(loop.gameObject);
            carriedLoop = loop;
        }

        /// <summary>The Khador fx's sheet: the warp's vein web is that animation's.</summary>
        static Texture KhadorSheet()
        {
            var fx = GoF2Remake.UI.StarMapAssets.Load()?.khadorJump;
            var look = fx != null ? fx.GetComponentInChildren<Renderer>(true)?.sharedMaterial : null;
            return look != null && look.HasProperty("_MainTex") ? look.GetTexture("_MainTex") : null;
        }

        void Light(Color colour, float ms, float strength)
        {
            lightColour = colour;
            lightMs = lightStartMs = ms;
            lightStrength = strength;
        }

        Texture2D Layer(ModWarp.Def warp, string path, ref Texture2D own, int cellsAcross, int cellsAlong, int seed)
        {
            var t = warp != null && !string.IsNullOrEmpty(path) ? ModWarp.Texture(warp, path) : null;
            if (t == null) t = own = MakeNoise(cellsAcross, cellsAlong, seed);
            t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        /// <summary>Value noise that tiles both ways, for a space with no mod's textures.</summary>
        static Texture2D MakeNoise(int cellsAcross, int cellsAlong, int seed)
        {
            const int Size = 128;
            var random = new System.Random(seed);
            var cells = new float[cellsAcross, cellsAlong];
            for (int x = 0; x < cellsAcross; x++) for (int y = 0; y < cellsAlong; y++) cells[x, y] = (float)random.NextDouble();
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x * cellsAcross / (float)Size, v = y * cellsAlong / (float)Size;
                    int cx = (int)u, cy = (int)v;
                    float fx = Mathf.SmoothStep(0f, 1f, u - cx), fy = Mathf.SmoothStep(0f, 1f, v - cy);
                    float a = Mathf.Lerp(cells[cx, cy], cells[(cx + 1) % cellsAcross, cy], fx);
                    float b = Mathf.Lerp(cells[cx, (cy + 1) % cellsAlong], cells[(cx + 1) % cellsAcross, (cy + 1) % cellsAlong], fx);
                    byte c = (byte)(Mathf.Lerp(a, b, fy) * 255f);
                    pixels[y * Size + x] = new Color32(c, c, c, 255);
                }
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = "WarpNoise", wrapMode = TextureWrapMode.Repeat };
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        /// <summary>The warp's strength 0..1: always full between the systems, else the drive's ramp or the arrival's fade.</summary>
        float Strength(float drive, float arriving)
        {
            if (interstellar) return 1f;
            float s = Mathf.Max(drive, arriving);
            return s * s * (3f - 2f * s);
        }

        void Apply(float level)
        {
            Shader.SetGlobalFloat(Level, level);
            Shader.SetGlobalFloat(Time_, flow);
            Shader.SetGlobalFloat(Khador, interstellar || arrivalMs > 0f ? 1f : 0f);
            Shader.SetGlobalVector(Axis, axis);
        }

        void LateUpdate()
        {
            if (ship == null) return;
            float dtMs = Time.deltaTime * 1000f;
            arrivalMs = Mathf.Max(0f, arrivalMs - dtMs);
            lightMs = Mathf.Max(0f, lightMs - dtMs);
            float drive = nav != null && nav.Drive != null ? nav.Drive.Level : 0f;
            axis = Vector3.Slerp(axis, ship.transform.forward, 1f - Mathf.Exp(-AxisFollow * dtMs / 1000f)).normalized;
            flow = Mathf.Repeat(flow + (IdleFlow + CruiseFlow * drive) * dtMs / 1000f, 1000f);
            float level = Strength(drive, arrivalMs / ArrivalCarryMs);
            Apply(level);
            bool cruising = nav != null && nav.DriveCruising;
            if (cruising && !wasCruising && enterClip != null && shot != null) shot.PlayOneShot(enterClip, Settings.SfxVolume);
            wasCruising = cruising;
            if (loop != null) loop.volume = (interstellar ? Mathf.Lerp(IdleVolume, 1f, drive) : level) * Settings.SfxVolume;
        }

        /// <summary>The light over everything, the one place that hides a load: a screen's worth of it from here, not the HUD's.</summary>
        void OnGUI()
        {
            float alpha = lightMs > 0f ? lightStrength * lightMs / lightStartMs : 0f;
            var colour = lightColour;
            if (jump != null && jump.GoingInterstellar && jump.SceneMs > 0f)
            {
                alpha = Mathf.InverseLerp(SystemJump.KhadorWhiteoutStartMs, SystemJump.KhadorWhiteoutMs, jump.SceneMs);
                colour = EntryLight;
            }
            if (alpha <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.color = new Color(colour.r, colour.g, colour.b, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>The next sky is a plain one again (the menu's, an orbit without a drive).</summary>
        void OnDisable() => Shader.SetGlobalFloat(Level, 0f);

        void OnDestroy()
        {
            if (ownNoise != null) Destroy(ownNoise);
            if (ownDetail != null) Destroy(ownDetail);
            if (ownSparks != null) Destroy(ownSparks);
        }
    }
}
