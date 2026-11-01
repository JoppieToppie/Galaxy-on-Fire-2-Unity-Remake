// EventCutscene.cs
// Remake events: cutscenes made in an event graph (or typed as commands), on this player's game (NetAdmin's Cutscene /
// Camera / Fade / Letterbox orders; the server only checks the text, TryParseShot):
//   - "cutscene start [nobars] [freeze] [invulnerable]" / "cutscene end": cinematic mode in flight like a LevelScript cutscene
//     (SpaceLevel.Cutscene: the HUD hides but for the radio and the conversations, no controls, no weapons, no autopilot;
//     the ship flies on, or holds still with "freeze"; "invulnerable": no damage meanwhile), the letterbox bars slide in
//     unless "nobars". Kept across scenes (a teleport mid-scene keeps it); the camera shot is per orbit.
//   - "camera <place> [to <place>] [over <s>] [look <place>] [follow] [fov <deg>] [shake <0..1>]" / "camera chase": the scene
//     camera at a place (moving to the second one over the seconds, eased), looking at another (default the player's ship)
//     every frame. Places: "player [right up forward]" (game units in the ship's own frame: 0 600 -1338 is the chase spot),
//     "station [x y z]" / "x y z" (game coordinates of the orbit, the station at the origin), "ship <name> [x y z]" (a ship
//     spawned "named <name>", "quoted" when the name has spaces; world axes). "follow": the places that move (the player, a
//     ship) are read every frame, else once when the shot starts. A shot works with or without "cutscene start" (without it
//     the HUD and controls stay); "camera chase" (and "cutscene end") hand the camera back to the chase camera.
//   - "fade out|in [seconds] [rrggbb]" (a "#" before it is allowed in typed commands, not in event lines, where it starts a comment) / "fade clear": the screen to a colour (held) or from it to clear; "letterbox on|off".
// The fade and the bars are drawn by a panel of their own under the HUD (sorting -1), so the radio box and a conversation
// stay readable over a black screen; across scenes too (docked as well). Everything ends with the event (EventRunner' End
// sends "cutscene end" and "fade clear" to whoever got one) and when no event screens are active (EventHost.ScreensActive).

using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    [DefaultExecutionOrder(5000)]   // after the chase camera, before the VR rig (10000)
    public sealed class EventCutscene : MonoBehaviour
    {
        const float M = 0.05f, BarsSeconds = 0.6f, BarHeight = 0.11f;

        public enum PlaceKind { Point, Player, Station, Ship }

        public struct Place
        {
            public PlaceKind kind;
            public Vector3 offset;   // game units
            public string name;
        }

        public sealed class Shot
        {
            public Place from, look;
            public Place? to;
            public float seconds, fov, shake;
            public bool follow;
        }

        static EventCutscene instance;
        bool cinematic, freeze, invulnerable, cameraOwned;
        float bars, barsTarget;
        float fadeFrom, fadeTo, fadeAlpha, fadeSeconds, fadeT = 1f;
        Color fadeColour = Color.black;
        Shot shot;
        float shotT;
        Vector3 fromAt, toAt;   // Unity, read when the shot starts (a shot without "follow")
        readonly Dictionary<string, Transform> ships = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        SpaceLevel appliedTo;
        bool lockedInput, froze, blockedWeapons;
        UIDocumentLike ui;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        /// <summary>An event's cutscene runs here (SpaceLevel.Cutscene: the HUD and the controls rest).</summary>
        public static bool Cinematic => instance != null && instance.cinematic && EventHost.ScreensActive;
        /// <summary>"cutscene start invulnerable": the player takes no damage meanwhile.</summary>
        public static bool Invulnerable => Cinematic && instance.invulnerable;
        /// <summary>An event's camera shot owns the scene camera.</summary>
        public static bool CameraOwned => instance != null && instance.cameraOwned;

        static EventCutscene Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("EventCutscene");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<EventCutscene>();
            return instance;
        }

        // ---- the orders --------------------------------------------------------------------------------------------

        internal static void ApplyCutscene(string text)
        {
            var words = Words(text);
            var s = Get();
            if (words.Count > 0 && words[0].Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                s.cinematic = true;
                s.freeze = words.Exists(w => w.Equals("freeze", StringComparison.OrdinalIgnoreCase));
                s.invulnerable = words.Exists(w => w.Equals("invulnerable", StringComparison.OrdinalIgnoreCase));
                s.barsTarget = words.Exists(w => w.Equals("nobars", StringComparison.OrdinalIgnoreCase)) ? s.barsTarget : 1f;
            }
            else s.End();
        }

        internal static void ApplyCamera(string text)
        {
            var s = Get();
            if (text != null && text.Trim().Equals("chase", StringComparison.OrdinalIgnoreCase)) { s.ReleaseCamera(); return; }
            if (!TryParseShot(text, out var shot, out _)) return;
            s.shot = shot;
            s.shotScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();   // the shot belongs to this orbit
            s.shotT = 0f;
            s.ships.Clear();
            s.fromAt = s.toAt = Vector3.zero;
            s.appliedShot = false;
        }

        internal static void ApplyFade(string text)
        {
            var words = Words(text);
            var s = Get();
            if (words.Count == 0 || words[0].Equals("clear", StringComparison.OrdinalIgnoreCase)) { s.fadeAlpha = s.fadeTo = 0f; s.fadeT = 1f; return; }
            bool fadeOut = words[0].Equals("out", StringComparison.OrdinalIgnoreCase);
            float seconds = 1f;
            if (words.Count > 1) float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            if (words.Count > 2 && ColorUtility.TryParseHtmlString(words[2].StartsWith("#") ? words[2] : "#" + words[2], out var c)) s.fadeColour = c;
            else if (fadeOut) s.fadeColour = Color.black;
            s.fadeFrom = fadeOut ? s.fadeAlpha : Mathf.Max(s.fadeAlpha, 1f);
            s.fadeTo = fadeOut ? 1f : 0f;
            s.fadeSeconds = Mathf.Max(0f, seconds);
            s.fadeT = s.fadeSeconds <= 0f ? 1f : 0f;
            if (s.fadeT >= 1f) s.fadeAlpha = s.fadeTo;
        }

        internal static void ApplyLetterbox(string text) =>
            Get().barsTarget = text != null && text.Trim().Equals("off", StringComparison.OrdinalIgnoreCase) ? 0f : 1f;

        void End()
        {
            cinematic = false;
            freeze = invulnerable = false;
            barsTarget = 0f;
            ReleaseCamera();
            Unapply();
        }

        // ---- the shot's text ----------------------------------------------------------------------------------------

        /// <summary>The words of an order: spaces between, "double quotes" keep one together.</summary>
        internal static List<string> Words(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;
                if (text[i] == '"')
                {
                    int end = text.IndexOf('"', i + 1);
                    if (end < 0) end = text.Length;
                    list.Add(text.Substring(i + 1, end - i - 1));
                    i = end + 1;
                    continue;
                }
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                list.Add(text.Substring(start, i - start));
            }
            return list;
        }

        static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "to", "over", "look", "follow", "fov", "shake", "then", "speed", "radius", "fight", "loop", "seconds", "offset" };

        internal static bool Number(List<string> w, int i, out float v)
        {
            v = 0f;
            return i < w.Count && float.TryParse(w[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && Mathf.Abs(v) < 1e8f;
        }

        internal static bool ParsePlace(List<string> w, ref int i, out Place place, out string error)
        {
            place = new Place();
            error = null;
            if (i >= w.Count) { error = "a place is missing"; return false; }
            string k = w[i].ToLowerInvariant();
            if (k == "player" || k == "station" || k == "ship")
            {
                place.kind = k == "player" ? PlaceKind.Player : k == "station" ? PlaceKind.Station : PlaceKind.Ship;
                i++;
                if (place.kind == PlaceKind.Ship)
                {
                    if (i >= w.Count || Keywords.Contains(w[i])) { error = "ship needs the ship's name"; return false; }
                    place.name = w[i++];
                }
            }
            else place.kind = PlaceKind.Point;
            if (Number(w, i, out float x))
            {
                if (!Number(w, i + 1, out float y) || !Number(w, i + 2, out float z)) { error = "three numbers: x y z"; return false; }
                place.offset = new Vector3(x, y, z);
                i += 3;
            }
            else if (place.kind == PlaceKind.Point) { error = $"\"{w[i]}\" is no place (player, station, ship <name> or x y z)"; return false; }
            return true;
        }

        /// <summary>"&lt;place&gt; [to &lt;place&gt;] [over s] [look &lt;place&gt;] [follow] [fov deg] [shake 0..1]".</summary>
        public static bool TryParseShot(string text, out Shot shot, out string error)
        {
            shot = new Shot { look = new Place { kind = PlaceKind.Player } };
            var w = Words(text);
            int i = 0;
            if (!ParsePlace(w, ref i, out shot.from, out error)) return false;
            while (i < w.Count)
            {
                string k = w[i++].ToLowerInvariant();
                switch (k)
                {
                    case "to":
                        if (!ParsePlace(w, ref i, out var to, out error)) return false;
                        shot.to = to;
                        break;
                    case "look":
                        if (!ParsePlace(w, ref i, out shot.look, out error)) return false;
                        break;
                    case "over":
                        if (!Number(w, i++, out shot.seconds) || shot.seconds < 0f) { error = "over needs seconds"; return false; }
                        break;
                    case "fov":
                        if (!Number(w, i++, out shot.fov) || shot.fov < 5f || shot.fov > 150f) { error = "fov needs degrees (5 to 150)"; return false; }
                        break;
                    case "shake":
                        if (!Number(w, i++, out shot.shake) || shot.shake < 0f || shot.shake > 1f) { error = "shake needs 0 to 1"; return false; }
                        break;
                    case "follow":
                        shot.follow = true;
                        break;
                    default:
                        error = $"\"{w[i - 1]}\" isn't understood here";
                        return false;
                }
            }
            return true;
        }

        // ---- running -----------------------------------------------------------------------------------------------

        bool appliedShot;
        UnityEngine.SceneManagement.Scene shotScene;

        /// <summary>A place in Unity space now; false when it is gone (its ship destroyed or never spawned).</summary>
        bool Resolve(SpaceLevel level, Place p, out Vector3 at)
        {
            at = Vector3.zero;
            switch (p.kind)
            {
                case PlaceKind.Point:
                case PlaceKind.Station:
                    at = new Vector3(p.offset.x, p.offset.y, -p.offset.z) * M;
                    return true;
                case PlaceKind.Player:
                {
                    var ship = level.Player != null ? level.Player.transform : null;
                    if (ship == null) return false;
                    // The ship's own frame: right, up, forward (the models face Unity +Z), unscaled.
                    at = ship.position + ship.rotation * (p.offset * M);
                    return true;
                }
                case PlaceKind.Ship:
                {
                    if (!ships.TryGetValue(p.name, out var t) || t == null)
                    {
                        t = null;
                        foreach (var target in FindObjectsByType<Target>())
                            if (target != null && target.Alive && string.Equals(target.displayName, p.name, StringComparison.OrdinalIgnoreCase)) { t = target.transform; break; }
                        if (t == null)   // numbered by the spawn ("Name 1"): the first of them
                            foreach (var target in FindObjectsByType<Target>())
                                if (target != null && target.Alive && target.displayName != null && target.displayName.StartsWith(p.name + " ", StringComparison.OrdinalIgnoreCase)) { t = target.transform; break; }
                        ships[p.name] = t;
                    }
                    if (t == null) return false;
                    at = t.position + new Vector3(p.offset.x, p.offset.y, -p.offset.z) * M;
                    return true;
                }
            }
            return false;
        }

        /// <summary>A living ship by its name (Spawn's "named"; "Name 1" numbering: the first of them), null = none.</summary>
        internal static Target FindShip(string name, Target except = null)
        {
            Target first = null;
            foreach (var t in FindObjectsByType<Target>())
            {
                if (t == null || t == except || !t.Alive || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.displayName)) continue;
                if (string.Equals(t.displayName, name, StringComparison.OrdinalIgnoreCase)) return t;
                if (first == null && t.displayName.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)) first = t;
            }
            return first;
        }

        /// <summary>A place in Unity space now, outside a shot (EventShipOrders); false when its ship is gone.</summary>
        internal static bool ResolvePlace(SpaceLevel level, Place p, out Vector3 at)
        {
            at = Vector3.zero;
            switch (p.kind)
            {
                case PlaceKind.Point:
                case PlaceKind.Station:
                    at = new Vector3(p.offset.x, p.offset.y, -p.offset.z) * M;
                    return true;
                case PlaceKind.Player:
                {
                    var ship = level != null && level.Player != null ? level.Player.transform : null;
                    if (ship == null) return false;
                    at = ship.position + ship.rotation * (p.offset * M);
                    return true;
                }
                case PlaceKind.Ship:
                {
                    var t = FindShip(p.name);
                    if (t == null) return false;
                    at = t.transform.position + new Vector3(p.offset.x, p.offset.y, -p.offset.z) * M;
                    return true;
                }
            }
            return false;
        }

        static bool Moves(Place p) => p.kind == PlaceKind.Player || p.kind == PlaceKind.Ship;

        void ReleaseCamera()
        {
            shot = null;
            if (!cameraOwned) return;
            cameraOwned = false;
            var cam = appliedTo != null ? appliedTo.mainCamera : Camera.main;
            var chase = cam != null ? cam.GetComponent<ChaseCamera>() : null;
            if (chase != null && (appliedTo == null || appliedTo.LaunchCameraOver)) { chase.scriptCamera = false; chase.enabled = true; chase.Snap(); }
        }

        /// <summary>The cinematic mode on the level's player: input, weapons, the ship held.</summary>
        void ApplyTo(SpaceLevel level)
        {
            var player = level.Player;
            if (player == null) return;
            if (!player.inputLocked)
            {
                player.inputLocked = true;
                lockedInput = true;
                level.Navigation?.SetAutopilot(null);
            }
            if (level.Weapons != null && !level.Weapons.BlockSet) { level.Weapons.Blocked = true; blockedWeapons = true; }
            if (freeze && !player.externalControl) { player.externalControl = true; player.ExternalSpeedMetersPerSecond = 0f; froze = true; }
            else if (!freeze && froze) { player.externalControl = false; froze = false; }
        }

        void Unapply()
        {
            var level = appliedTo;
            if (level != null && level.Player != null)
            {
                if (lockedInput && level.LaunchCameraOver) level.Player.inputLocked = false;
                if (froze) level.Player.externalControl = false;
                if (blockedWeapons && level.Weapons != null && level.LaunchCameraOver) level.Weapons.Blocked = false;
            }
            lockedInput = froze = blockedWeapons = false;
        }

        void Update()
        {
            if (!EventHost.ScreensActive)
            {
                if (cinematic || shot != null || cameraOwned) End();
                fadeAlpha = fadeTo = 0f;
                fadeT = 1f;
                bars = barsTarget = 0f;
                DrawOverlay();
                return;
            }
            float dt = Time.deltaTime;
            if (fadeT < 1f)
            {
                fadeT = fadeSeconds <= 0f ? 1f : Mathf.Min(1f, fadeT + dt / fadeSeconds);
                fadeAlpha = Mathf.Lerp(fadeFrom, fadeTo, fadeT);
            }
            bars = Mathf.MoveTowards(bars, barsTarget, dt / BarsSeconds);
            DrawOverlay();

            var level = FindLevel();
            if (level != appliedTo)
            {
                // A new orbit (or docked): the old level's player is gone; the mode carries over.
                lockedInput = froze = blockedWeapons = false;
                cameraOwned = false;
                appliedTo = level;
            }
            // A shot is for the orbit it was sent in.
            if (shot != null && shotScene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) shot = null;
            if (level == null) return;
            if (cinematic) ApplyTo(level);
            else if (lockedInput || froze || blockedWeapons) Unapply();
        }

        SpaceLevel cachedLevel;

        SpaceLevel FindLevel()
        {
            if (cachedLevel == null) cachedLevel = FindAnyObjectByType<SpaceLevel>();
            return cachedLevel;
        }

        void LateUpdate()
        {
            var level = appliedTo;
            if (shot == null || level == null || level.mainCamera == null) return;
            var cam = level.mainCamera;
            if (!cameraOwned)
            {
                var chase = cam.GetComponent<ChaseCamera>();
                if (chase != null) { chase.enabled = false; chase.scriptCamera = true; }
                cameraOwned = true;
            }
            if (!appliedShot)
            {
                Resolve(level, shot.from, out fromAt);
                toAt = fromAt;
                if (shot.to.HasValue) Resolve(level, shot.to.Value, out toAt);
                appliedShot = true;
            }
            shotT = shot.seconds <= 0f ? 1f : Mathf.Min(1f, shotT + Time.deltaTime / shot.seconds);
            float e = shotT * shotT * (3f - 2f * shotT);   // eased in and out
            Vector3 a = fromAt, b = toAt;
            if (shot.follow)
            {
                if (Moves(shot.from) && Resolve(level, shot.from, out var fa)) a = fa;
                if (shot.to.HasValue && Moves(shot.to.Value) && Resolve(level, shot.to.Value, out var tb)) b = tb;
                else if (!shot.to.HasValue) b = a;
            }
            cam.transform.position = Vector3.Lerp(a, b, e);
            cam.fieldOfView = Aspect.VerticalFov(shot.fov > 0f ? shot.fov : 1.22f * Mathf.Rad2Deg, cam.aspect);
            if (Resolve(level, shot.look, out var look))
            {
                if (shot.shake > 0f && Time.deltaTime > 0f)
                {
                    float j = shot.shake * 100f * M * Settings.CameraShake;
                    look += new Vector3(UnityEngine.Random.Range(-j, j), UnityEngine.Random.Range(-j, j), UnityEngine.Random.Range(-j, j));
                    Haptics.Rumble(shot.shake);
                }
                var d = look - cam.transform.position;
                if (d.sqrMagnitude > 1e-6f) cam.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }

        // ---- the overlay (fade, bars) ------------------------------------------------------------------------------

        /// <summary>The panel of the fade and the bars, under the HUD.</summary>
        sealed class UIDocumentLike
        {
            public VisualElement fade, top, bottom;
        }

        GameObject overlay;

        void DrawOverlay()
        {
            bool visible = fadeAlpha > 0.001f || bars > 0.001f;
            if (ui == null)
            {
                if (visible && overlay == null) BuildOverlay();
                return;
            }
            ui.fade.style.opacity = fadeAlpha;
            ui.fade.style.backgroundColor = new Color(fadeColour.r, fadeColour.g, fadeColour.b, 1f);
            ui.fade.style.display = fadeAlpha > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
            float e = bars * bars * (3f - 2f * bars);
            var h = Length.Percent(BarHeight * 100f * e);
            ui.top.style.height = h;
            ui.bottom.style.height = h;
        }

        void BuildOverlay()
        {
            var assets = StarMapAssets.Load();
            if (assets == null || assets.panelSettings == null) return;
            overlay = new GameObject("EventCutscene panel");
            overlay.SetActive(false);
            overlay.transform.SetParent(transform, false);
            var settings = Instantiate(assets.panelSettings);
            settings.sortingOrder = assets.panelSettings.sortingOrder - 1;   // under the HUD: its radio box and conversations stay on top
            var pr = overlay.AddComponent<PanelRenderer>();
            pr.panelSettings = settings;
            pr.visualTreeAsset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            pr.RegisterUIReloadCallback(OnUIReload);
            overlay.SetActive(true);
        }

        void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            var o = new UIDocumentLike
            {
                fade = new VisualElement { pickingMode = PickingMode.Ignore },
                top = new VisualElement { pickingMode = PickingMode.Ignore },
                bottom = new VisualElement { pickingMode = PickingMode.Ignore },
            };
            foreach (var e in new[] { o.fade, o.top, o.bottom })
            {
                e.style.position = Position.Absolute;
                e.style.left = 0;
                e.style.right = 0;
                root.Add(e);
            }
            o.fade.style.top = 0;
            o.fade.style.bottom = 0;
            o.top.style.top = 0;
            o.bottom.style.bottom = 0;
            o.top.style.backgroundColor = o.bottom.style.backgroundColor = Color.black;
            o.top.style.height = o.bottom.style.height = 0;
            o.fade.style.display = DisplayStyle.None;
            ui = o;
        }
    }
}
