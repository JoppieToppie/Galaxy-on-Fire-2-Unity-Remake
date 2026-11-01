// WarpSky.cs
// Remake: the warp of the cruise drives and of the space between the systems (Data.InterstellarSpace), EVERSPACE 2's travel
// mode: its M_TravelMode_WarpTunnel is a refraction, the sky bent into streaks round the flight, with no colour of its own.
// So this is no object: GoF2/SpaceSky stretches the sky itself by the globals set here, and at level 0 it is the plain sky,
// which makes the start and the end seamless; ships, planets and suns draw over it untouched. Between the systems (and easing
// out of them) it takes after the Khador Drive's animation: that fx's vein web runs through it, and it leans to its ice blue;
// a cruise in an orbit is the plain stretch. The web is a layer of its own (GoF2/WarpWeb), so the planets stay under it. Always
// full between the systems; in an orbit it follows the cruise drive's ramp, and an arrival from between the systems eases out
// of it, the orbit's fog easing in and its planets growing to full size as it goes. The band images are the game's own
// (Data.WarpBands) unless a mod's warp.json (Modding.ModWarp) replaces them; it plays a sound only if that mod gives one.

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
                            Noise = Shader.PropertyToID("_WarpNoise"), Detail = Shader.PropertyToID("_WarpDetail"), Arcs = Shader.PropertyToID("_WarpArcs"),
                            Khador = Shader.PropertyToID("_WarpKhador");

        Navigation nav;
        ShipController ship;
        bool interstellar, local;
        float arrivalMs, lightMs, lightStartMs, lightStrength;
        Color lightColour;
        /// <summary>The sky runs on across a load, and the loop plays through it (CarrySound).</summary>
        static float flow;
        static AudioSource carriedLoop;
        SystemJump jump;
        SpaceDust fog;
        Backdrop backdrop;
        Traffic traffic;
        ArrivalScale objects;
        MeshRenderer web;
        Material webLook;
        Vector3 axis;
        /// <summary>Data.WarpBands' images: made once, so a level's load doesn't pay for them again.</summary>
        static Texture2D ownNoise, ownDetail;
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
            local = level.LocalArrival;
            arrivalMs = arriving ? ArrivalCarryMs : 0f;
            if (interstellar) Light(EntryLight, EntryFadeMs, 1f);
            else if (arriving && unseen) Light(ArrivalLight, ArrivalDissolveMs, ArrivalDissolve);
            axis = ship.transform.forward;
            fog = arriving ? level.Fog : null;
            if (fog != null) fog.Fade(0f);
            backdrop = arriving ? level.Backdrop : null;
            traffic = arriving ? level.Traffic : null;
            if (arriving)
            {
                objects = new ArrivalScale();
                objects.Add(level.Station != null ? level.Station.transform : null);
                objects.Add(level.Jumpgate != null ? level.Jumpgate.transform : null);
                objects.AddChildren(level.Asteroids);
                objects.Apply(InterstellarSpace.ArrivalSize(1f));
                traffic?.SetArrivalSize(InterstellarSpace.ArrivalSize(1f));
            }
            if (backdrop != null)
            {
                backdrop.planetSize = backdrop.sunScaleFactor = InterstellarSpace.ArrivalSize(1f);
                backdrop.flareShown = 0f;
            }
            var warp = ModWarp.Current;
            if (ownNoise == null) ownNoise = OwnLayer("WarpNoise", WarpBands.TunnelSize, WarpBands.Tunnel());
            if (ownDetail == null) ownDetail = OwnLayer("WarpDetail", WarpBands.DetailSize, WarpBands.Detail());
            Shader.SetGlobalTexture(Noise, Layer(warp, warp?.tunnel, ownNoise));
            Shader.SetGlobalTexture(Detail, Layer(warp, warp?.detail, ownDetail));
            Shader.SetGlobalTexture(Arcs, KhadorSheet() ?? Texture2D.blackTexture);
            MakeWeb();
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

        /// <summary>The vein web, a layer over the planets (GoF2/WarpWeb): the shader places it about the camera.</summary>
        void MakeWeb()
        {
            var shader = Resources.Load<Shader>("GoF2Sky/WarpWeb");
            if (shader == null || local || (!interstellar && arrivalMs <= 0f)) return;
            var go = new GameObject("Warp web");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            web = go.AddComponent<MeshRenderer>();
            web.sharedMaterial = webLook = new Material(shader);
            web.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            web.receiveShadows = false;
            web.localBounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
        }

        /// <summary>An arrival's warp is still easing out (SpaceLevel.WarpHolds).</summary>
        public bool Arriving => arrivalMs > 0f;

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

        static Texture2D Layer(ModWarp.Def warp, string path, Texture2D own)
        {
            var t = warp != null && !string.IsNullOrEmpty(path) ? ModWarp.Texture(warp, path) : null;
            if (t == null) return own;
            t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        /// <summary>A band image of the game's own (Data.WarpBands).</summary>
        static Texture2D OwnLayer(string name, int size, byte[] pixels)
        {
            var t = new Texture2D(size, size, TextureFormat.R8, false, true) { name = name, wrapMode = TextureWrapMode.Repeat };
            t.SetPixelData(pixels, 0);
            t.Apply(false, true);
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
            Shader.SetGlobalFloat(Khador, interstellar || (arrivalMs > 0f && !local) ? 1f : 0f);
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
            if (fog != null)
            {
                fog.Fade(1f - arrivalMs / ArrivalCarryMs);
                if (arrivalMs <= 0f) fog = null;
            }
            if (backdrop != null)
            {
                backdrop.planetSize = backdrop.sunScaleFactor = InterstellarSpace.ArrivalSize(arrivalMs / ArrivalCarryMs);
                backdrop.flareShown = 1f - arrivalMs / ArrivalCarryMs;
                if (arrivalMs <= 0f) backdrop = null;
            }
            if (objects != null)
            {
                float size = InterstellarSpace.ArrivalSize(arrivalMs / ArrivalCarryMs);
                objects.Apply(size);
                traffic?.SetArrivalSize(size);
                if (arrivalMs <= 0f)
                {
                    objects.Restore();
                    objects = null;
                }
            }
            if (traffic != null && arrivalMs <= 0f)
            {
                traffic.ReleaseEnemies();
                traffic = null;
            }
            if (web != null && !interstellar && arrivalMs <= 0f) { Destroy(web.gameObject); web = null; }
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
            if (webLook != null) Destroy(webLook);
        }
    }
}
