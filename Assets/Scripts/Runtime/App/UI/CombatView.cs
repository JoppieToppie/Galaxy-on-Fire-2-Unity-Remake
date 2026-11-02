// CombatView.cs
// The flight HUD's combat visuals (Reference/research/ship_combat.md 6.1, 7), driven by FlightHud:
//   ship markers (Radar::draw ship loop, only with a scanner): faction colour red enemy / green friend / yellow neutral;
//     off screen a dot (ring when locked) on the radar ellipse 657 x 491; on screen far (any axis > 24000 units from the
//     camera) the dot / ring, plus the distance for the locked ship at (-61, +61); near: the hull bar (114 x 10 frame at
//     (-59, +63), 110 x 6 fill) and, when locked, the faction bracket 81 x 81; remake (multiplayer, players' request): another
//     player's ship has a second frame 11 px above with their shield (the status bar's cyan) and the hull bar takes the
//     status bar's red hull with their yellow armor over it (Target.netShield / netArmor from NetPlayer), like the HUD's own
//   crates: far a white diamond 0x4f1, near the white bracket 0x4f2, off screen the box 0x451 (0x44d Void); also a
//     fighter dying with cargo (KIPlayer+0x48) until it explodes
//   ship lock: the lock ring on the crosshair (24 frames, from t = 0; crates after 500 ms) and the top plate
//     "<race> NN%" with the race icon (Radar::drawCurrentLock)
//   player status (Hud::draw, top left): shield icon (red 500 ms after a hit) + cyan bar, hull icon (red once the armor is
//     gone) + red hull bar with the yellow armor bar over it
//   hit arcs: blue while the shield holds, red without, 300 ms, on the ellipse's left / right / top / bottom
//   remake options (players' request): a far ship's on-screen marker as the original's dot, a hollow ring or a small dot above
//     it, and its distance under every far marker (FarShipMarkers, Settings.FarShipMarkers / MarkerDistance)
//   remake mods: a mod's textures/<name> replaces any of these images by its GoF2Hud name (ModTextures.Hud)
// Positions are HD pixels = panel units.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class CombatView
    {
        const float M = 0.05f, NearUnits = 24000f;
        // Radar::elipsoidIntersect's ellipse, image 0x4c7's size (remake: x FlightHud.RadarScale, smaller in the phone layout).
        static float EllipseX => 657f * FlightHud.RadarScale;
        static float EllipseY => 491f * FlightHud.RadarScale;

        class Marker
        {
            public VisualElement dot, bar, fill, bracket, emp, empFill;
            public VisualElement shieldBar, shieldFill, armorFill;   // another player's pools, made on first use (DrawPools)
            public Label distance;
            public Label pilot;   // another player's name over their marker, made on first use (DrawPilotName)
            public bool used;
            public int distanceKey = int.MinValue, labelLook = -1, pilotLook = -1;   // the labels as last written (FarShipMarkers.DistanceKey)
            // Where DrawShip last put the marker: on screen, its centre x and the top of what it drew (panel px).
            public bool onScreen;
            public float x, top;
        }

        readonly VisualElement layer, lockRing, lockPlate, lockClass, statusPanel, shieldRow, shieldIcon, shieldFill, hullIcon, hullFill, armorFill,
                                      gammaRow, gammaFill, speedPanel;
        readonly Label lockOre;
        readonly VisualElement[] arcs = new VisualElement[4];
        readonly VisualElement arcBox, safeBox;   // the arcs' 0 x 0 box at the screen centre, the HUD's safe area
        readonly Texture2D[] lockFrames = new Texture2D[24];
        readonly Dictionary<Object, Marker> markers = new Dictionary<Object, Marker>();
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly string[] Faction = { "enemy", "friend", "neutral" };

        /// <summary>A faction's marker images, looked up once: DrawShip runs per ship per frame, and building the names
        /// ("ship_bar_enemy"...) each time allocated strings.</summary>
        class MarkerTextures { public Texture2D bar, fill, bracket, ring, dot, openRing; }

        // The lock plate as last built (Update).
        Target plateTarget;
        int platePct = -1, plateRace = int.MinValue;
        string plateName, plateText;
        Texture2D plateIcon;
        readonly MarkerTextures[] factionTextures = new MarkerTextures[3];
        // The far marker options, read once per frame (Update).
        int farStyle;
        bool farDistance;
        static readonly string[] LabelFaction = { "combat-label--enemy", "combat-label--friend", "combat-label--neutral" };

        MarkerTextures FactionTextures(int faction)
        {
            // Options > Accessibility "Colour-blind markers": the friendly images blue (MarkerTex), all looked up again.
            bool cb = Settings.ColorblindMarkers;
            if (cb != colorblind)
            {
                colorblind = cb;
                for (int i = 0; i < factionTextures.Length; i++) factionTextures[i] = null;
            }
            var t = factionTextures[faction];
            if (t != null) return t;
            string f = Faction[faction];
            bool blue = cb && faction == 1;
            return factionTextures[faction] = new MarkerTextures
            {
                bar = MarkerTex($"ship_bar_{f}", blue), fill = MarkerTex($"ship_fill_{f}", blue), bracket = MarkerTex($"ship_bracket_{f}", blue),
                ring = MarkerTex($"ship_ring_{f}", blue), dot = MarkerTex($"ship_dot_{f}", blue), openRing = MarkerTex($"ship_ring_open_{f}", blue),
            };
        }

        bool colorblind;

        /// <summary>A marker image; 'blue': the green friendly one turned blue (red-green colour blindness reads green and red
        /// alike, blue stands apart from both), made once from the GPU copy (the HUD images aren't readable).</summary>
        Texture2D MarkerTex(string name, bool blue)
        {
            var src = Tex(name);
            if (!blue || src == null) return src;
            string key = "cb:" + name;
            if (textures.TryGetValue(key, out var done)) return done;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = src.filterMode, wrapMode = src.wrapMode, name = key };
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                byte g = c.g;
                c.g = (byte)(g * 0.62f);          // green -> an azure blue, its brightness kept
                c.b = (byte)Mathf.Max(c.b, g);
                c.r = (byte)(c.r * 0.5f);
                px[i] = c;
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            textures[key] = t;
            return t;
        }

        Texture2D Tex(string name)
        {
            if (!textures.TryGetValue(name, out var t)) textures[name] = t = Modding.ModTextures.Hud(name) ?? Resources.Load<Texture2D>("GoF2Hud/" + name);
            return t;
        }

        public CombatView(VisualElement root)
        {
            for (int i = 0; i < 24; i++) lockFrames[i] = Tex($"lock_{i:00}");
            layer = root.Q("combatMarkers");
            lockRing = root.Q("lockRing");
            lockPlate = root.Q("lockPlate");
            lockClass = root.Q("lockClass");
            lockOre = root.Q<Label>("lockOre");
            statusPanel = root.Q("statusPanel");
            shieldRow = root.Q("shieldRow");
            shieldIcon = root.Q("shieldIcon");
            shieldFill = root.Q("shieldFill");
            hullIcon = root.Q("hullIcon");
            hullFill = root.Q("hullFill");
            armorFill = root.Q("armorFill");
            gammaRow = root.Q("gammaRow");
            gammaFill = root.Q("gammaFill");
            speedPanel = root.Q("speedPanel");
            arcs[0] = root.Q("arcLeft");
            arcs[1] = root.Q("arcRight");
            arcs[2] = root.Q("arcTop");
            arcs[3] = root.Q("arcBottom");
            arcBox = root.Q("hitArcs");
            safeBox = root.Q("safeArea");
            foreach (var plate in statusPanel.Query(className: "status-plate").ToList()) Image(plate, Tex("status_plate"), false);
            Image(root.Q("shieldFrame"), Tex("status_shield_frame"), false);
            Image(root.Q("hullFrame"), Tex("status_hull_frame"), false);
            shieldFill.style.backgroundImage = new StyleBackground(Tex("status_shield_fill"));
            hullFill.style.backgroundImage = new StyleBackground(Tex("status_hull_fill"));
            armorFill.style.backgroundImage = new StyleBackground(Tex("status_armor_fill"));
            Image(root.Q("gammaIcon"), Tex("status_gamma"));
            Image(root.Q("gammaFrame"), Tex("status_gamma_frame"), false);
            gammaFill.style.backgroundImage = new StyleBackground(Tex("status_gamma_fill"));
        }

        static void Image(VisualElement e, Texture2D tex, bool size = true)
        {
            if (e == null || tex == null) return;
            e.style.backgroundImage = new StyleBackground(tex);
            if (!size) return;
            e.style.width = tex.width;
            e.style.height = tex.height;
        }

        /// <summary>The markers move every frame: by their translate, which only changes their transform, not their left / top,
        /// which ran the panel's layout every frame (absolute elements are laid out too).</summary>
        static void Place(VisualElement e, float x, float y) => e.style.translate = new Translate(x, y);

        /// <summary>Left / top for elements placed once (the hit arcs, which also flip by their scale).</summary>
        /// <summary>The safe area around the hit arcs' centre box (panel units; the box sits at the screen's centre), or no
        /// limit before the layout.</summary>
        Rect ArcLimits()
        {
            if (arcBox == null || safeBox == null) return new Rect(-1e5f, -1e5f, 2e5f, 2e5f);
            Rect s = safeBox.worldBound, c = arcBox.worldBound;
            if (float.IsNaN(s.width) || s.width <= 0f || float.IsNaN(c.x)) return new Rect(-1e5f, -1e5f, 2e5f, 2e5f);
            return Rect.MinMaxRect(s.xMin - c.x, s.yMin - c.y, s.xMax - c.x, s.yMax - c.y);
        }

        static void PlaceFixed(VisualElement e, float x, float y)
        {
            e.style.left = x;
            e.style.top = y;
        }

        VisualElement NewImage()
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            e.AddToClassList("nav-abs");
            e.style.display = DisplayStyle.None;
            layer.Add(e);
            return e;
        }

        Marker Get(Object key)
        {
            if (!markers.TryGetValue(key, out var m))
            {
                m = new Marker { dot = NewImage(), bar = NewImage(), fill = NewImage(), bracket = NewImage(), emp = NewImage(), empFill = NewImage() };
                m.distance = new Label { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
                m.distance.AddToClassList("combat-label");
                m.distance.AddToClassList("gof-semibold");
                m.distance.style.display = DisplayStyle.None;
                layer.Add(m.distance);
                markers[key] = m;
            }
            m.used = true;
            return m;
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        /// <param name="plateFree">No landmark / asteroid owns the top plate this frame.</param>
        /// <param name="markersOff">Hide the ship / crate markers only (the launch / arrival camera).</param>
        public void Update(CombatRadar radar, Traffic traffic, PlayerHealth health, Camera cam, bool cinematic, bool plateFree, bool markersOff = false)
        {
            UpdateStatus(health, cinematic);
            farStyle = Settings.FarShipMarkers;
            farDistance = Settings.MarkerDistance;
            foreach (var m in markers.Values) m.used = false;
            bool show = radar != null && radar.HasScanner && cam != null && layer.panel != null && !cinematic && !markersOff && health != null && !health.Dead;
            layer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                var origin = layer.worldBound.position;
                var centre = layer.layout.size / 2f;
                if (traffic != null)
                    foreach (var s in traffic.Ships)
                    {
                        // Radar::draw skips inactive players: a sleeping ship (KIPlayer::setToSleep) has no marker until it
                        // wakes, e.g. a pirate outpost until an enemy comes within its +-50 000 box.
                        // A fighter dying with cargo (KIPlayer+0x48): the crate markers until it explodes; without cargo none.
                        // Remake option (Settings.DyingCargoMarkers): none at all.
                        if (s.DyingWithCargo && !s.Gone && !s.Hidden && !s.RadarHidden)
                        {
                            if (Settings.DyingCargoMarkers) DrawCrate(Get(s), s.transform.position, s.Race == Standing.Void, cam, origin, centre);
                            continue;
                        }
                        if (s.Gone || !s.Target.Alive || s.Hidden || s.Asleep || s.RadarHidden || s.OffersDocking || s.Target.untargetable) continue;
                        int f = s.Target.hostileToPlayer ? 0 : s.Target.friendToPlayer ? 1 : 2;
                        DrawShip(Get(s), s.transform.position, f, s.Target.HullFraction, radar.Locked == s.Target, cam, origin, centre);
                        DrawEmp(Get(s), s.Hp != null ? s.Hp.EmpFraction : 1f);
                    }
                foreach (var o in Target.NetShips)   // multiplayer: the other players, the host's ships on a client
                    if (o != null && o.Alive && !o.untargetable)
                    {
                        var m = Get(o);
                        int f = o.hostileToPlayer ? 0 : o.friendToPlayer ? 1 : 2;
                        DrawShip(m, o.transform.position, f, o.HullFraction, radar.Locked == o, cam, origin, centre);
                        DrawPools(m, o.netShield, o.netArmor, radar.Locked == o);
                        if (o.netPilot) DrawPilotName(m, o.displayName, f, radar.Locked == o);
                    }
                foreach (var o in Target.RadarObjects)
                    if (o != null && o.Alive && !o.untargetable)
                        DrawShip(Get(o), o.transform.position, o.hostileToPlayer ? 0 : 2, 1f, radar.Locked == o, cam, origin, centre, true);
                // Level::createSentryGuns: the player's sentries are in Level::getEnemies too, always-friend: green markers.
                foreach (var sg in SentryGun.All)
                    if (sg != null && sg.Target != null && sg.Target.Alive)
                        DrawShip(Get(sg), sg.transform.position, 1, sg.Target.HullFraction, radar.Locked == sg.Target, cam, origin, centre);
                foreach (var c in Crate.All)   // the registry: a scene search every frame before
                    DrawCrate(Get(c), c.transform.position, c.race == 9, cam, origin, centre);
                // Radar::draw 0x1570ec: a spectral filter's gas clouds (attr 57), off screen too with attr 58 (Omega).
                var clouds = radar.Clouds;
                if (radar.CloudRadar && clouds != null)
                    for (int i = 0; i < clouds.Count; i++)
                        if (clouds.IsLive(i) && clouds.ObjectOf(i) != null)
                            DrawCloud(Get(clouds.ObjectOf(i)), clouds.PositionOf(i), radar.LockedCloud == i, radar.CloudOffScreen, cam, origin, centre);

                // Lock ring and plate for ships / crates (after the navigation view, which owns them otherwise).
                if (radar.LockFrame >= 0)
                {
                    lockRing.EnableInClassList("lock-ring--shown", true);
                    Image(lockRing, lockFrames[Mathf.Clamp(radar.LockFrame, 0, 23)]);
                }
                var locked = radar.Locked;
                if (plateFree && locked != null)
                {
                    lockPlate.EnableInClassList("lock-plate--shown", true);
                    // Radar::drawCurrentLock 0x158548: "<name or race> NN%"; the Hijacker / the Informer their name alone; they and
                    // a Most Wanted criminal in red.
                    // Built again only when something on it changed (every frame allocated the text and the icon's name), or
                    // when the mining / navigation view wrote its own text on the shared plate meanwhile (an asteroid's ore or a
                    // planet's name stayed on the ship's plate).
                    int pct = Mathf.RoundToInt(locked.HullFraction * 100f);
                    if (locked != plateTarget || pct != platePct || locked.displayName != plateName || locked.race != plateRace || lockOre.text != plateText)
                    {
                        plateTarget = locked; platePct = pct; plateName = locked.displayName; plateRace = locked.race;
                        string who = string.IsNullOrEmpty(locked.displayName) ? RaceName(locked.race) : locked.displayName;
                        lockOre.text = locked.plateNameOnly && !string.IsNullOrEmpty(locked.displayName) ? locked.displayName : $"{who} {pct}%";
                        plateText = lockOre.text;
                        plateIcon = locked.plateNoIcon ? null
                            : Modding.ModRaces.Is(locked.race) ? Modding.ModRaces.Icon(locked.race) ?? RaceTex(Standing.Look(locked.race))   // remake mods: a faction's emblem
                            : RaceTex(locked.race);
                    }
                    lockOre.EnableInClassList("lock-ore--wanted", locked.plateRed);
                    lockClass.style.display = plateIcon != null ? DisplayStyle.Flex : DisplayStyle.None;
                    Image(lockClass, plateIcon);
                }
                else if (plateFree && radar.LockedCloud >= 0)
                {
                    // Radar::drawCurrentLock: a locked gas cloud's plate reads 3236 "Gas cloud", no icon.
                    lockPlate.EnableInClassList("lock-plate--shown", true);
                    plateTarget = null;
                    lockOre.text = Localization.Get(3236);
                    lockOre.EnableInClassList("lock-ore--wanted", false);
                    lockClass.style.display = DisplayStyle.None;
                }
            }
            List<Object> gone = null;
            foreach (var kv in markers)
            {
                var m = kv.Value;
                if (m.used) continue;
                if (kv.Key == null)   // destroyed (a collected or expired crate): drop its elements
                {
                    m.dot.RemoveFromHierarchy(); m.bar.RemoveFromHierarchy(); m.fill.RemoveFromHierarchy();
                    m.bracket.RemoveFromHierarchy(); m.distance.RemoveFromHierarchy(); m.emp.RemoveFromHierarchy(); m.empFill.RemoveFromHierarchy();
                    m.shieldBar?.RemoveFromHierarchy(); m.shieldFill?.RemoveFromHierarchy(); m.armorFill?.RemoveFromHierarchy();
                    m.pilot?.RemoveFromHierarchy();
                    (gone ??= new List<Object>()).Add(kv.Key);
                    continue;
                }
                Show(m.dot, false); Show(m.bar, false); Show(m.fill, false); Show(m.bracket, false); Show(m.distance, false);
                Show(m.emp, false); Show(m.empFill, false);
                HidePools(m);
                if (m.pilot != null) Show(m.pilot, false);
            }
            if (gone != null) foreach (var k in gone) markers.Remove(k);
        }

        Texture2D RaceTex(int race) => race >= 0 && race <= 3 || race == 8 || race == 9 ? Tex($"race_{race}") : null;

        static string RaceName(int race) => Modding.ModRaces.Is(race) ? Modding.ModRaces.Name(race) : race switch   // remake mods: a faction's name
        {
            >= 0 and <= 9 => Localization.Get(406 + race),   // the four races, the minor ones, pirates, the Void
            _ => Localization.Get(416),                     // the Specters: the original's own "???"
        };

        // The camera as Project needs it, read once a frame (CameraFor): the per-marker camera calls (WorldToScreenPoint,
        // WorldToViewportPoint, its transform, Screen's size, the panel's size: seven native calls a ship) were most of
        // the HUD's time with 860 ships about (20 fleet battles).
        Camera viewCam;
        int viewFrame = -1;
        Matrix4x4 viewProj, camToLocal;
        Vector3 camPos;
        Rect camPixels;
        Vector2 screenSize, panelSize;

        void CameraFor(Camera cam)
        {
            if (viewFrame == Time.frameCount && viewCam == cam) return;
            viewFrame = Time.frameCount;
            viewCam = cam;
            viewProj = cam.projectionMatrix * cam.worldToCameraMatrix;
            var tr = cam.transform;
            camToLocal = tr.worldToLocalMatrix;
            camPos = tr.position;
            camPixels = cam.pixelRect;
            screenSize = new Vector2(Screen.width, Screen.height);
            panelSize = layer.panel.visualTree.layout.size;
        }

        bool Project(Camera cam, Vector3 world, Vector2 origin, Vector2 centre, out Vector2 p, out bool near)
        {
            CameraFor(cam);
            // Camera.WorldToScreenPoint / WorldToViewportPoint: the clip position, its depth the eye distance (w).
            var c = viewProj * new Vector4(world.x, world.y, world.z, 1f);
            float w = c.w;
            float vx = w != 0f ? c.x / w * 0.5f + 0.5f : 0f, vy = w != 0f ? c.y / w * 0.5f + 0.5f : 0f;
            float sx = camPixels.x + vx * camPixels.width, sy = camPixels.y + vy * camPixels.height;
            bool onScreen = w > 0f && sx >= 0f && sy >= 0f && sx <= screenSize.x && sy <= screenSize.y;
            var d = (world - camPos) / M;
            near = Mathf.Abs(d.x) <= NearUnits && Mathf.Abs(d.y) <= NearUnits && Mathf.Abs(d.z) <= NearUnits;
            // Viewport -> panel (the camera fills the screen): exact on screen and in offscreen captures.
            if (onScreen) p = new Vector2(vx * panelSize.x, (1f - vy) * panelSize.y) - origin;
            else p = OffScreen(camToLocal, world, centre);
            return onScreen;
        }

        /// <summary>Radar::update for an object not on screen: its direction clamped onto the radar ellipse.</summary>
        static Vector2 OffScreen(Camera cam, Vector3 world, Vector2 centre) => OffScreen(cam.transform.worldToLocalMatrix, world, centre);

        static Vector2 OffScreen(Matrix4x4 camToLocal, Vector3 world, Vector2 centre)
        {
            var local = camToLocal.MultiplyPoint3x4(world);
            var d = new Vector2(local.x, -local.y);
            if (d.sqrMagnitude < 1e-6f) d = Vector2.down;
            float k = Mathf.Sqrt(d.x * d.x / (EllipseX * EllipseX) + d.y * d.y / (EllipseY * EllipseY));
            return centre + d / k;
        }

        void DrawShip(Marker m, Vector3 world, int faction, float hull, bool locked, Camera cam, Vector2 origin, Vector2 centre, bool dotOnly = false)
        {
            bool onScreen = Project(cam, world, origin, centre, out var p, out bool near);
            m.onScreen = onScreen;
            m.x = p.x;
            m.top = p.y - 40.5f;   // near: the lock bracket's top (whether locked or not, so a name over it doesn't jump)
            // VR: no dots on the radar ellipse (it would reach onto the cockpit's displays): the dashboard's scope has them.
            if (!onScreen && Vr.VrMode.Enabled)
            {
                Show(m.bar, false); Show(m.fill, false); Show(m.bracket, false); Show(m.dot, false); Show(m.distance, false);
                HidePools(m);
                return;
            }
            var t = FactionTextures(faction);
            bool bar = onScreen && near && !dotOnly;
            Show(m.bar, bar);
            Show(m.fill, bar);
            Show(m.bracket, bar && locked);
            Show(m.dot, !bar);
            bool distance = !bar && onScreen && FarShipMarkers.ShowsDistance(locked, farDistance);
            Show(m.distance, distance);
            if (bar)
            {
                Image(m.bar, t.bar);
                Image(m.fill, t.fill);
                Place(m.bar, p.x - 59f, p.y + 63f);
                Place(m.fill, p.x - 58f, p.y + 65f);
                m.fill.style.width = Mathf.Clamp01(hull) * 110f;
                if (locked) { Image(m.bracket, t.bracket); Place(m.bracket, p.x - 40.5f, p.y - 40.5f); }
                // Remake: the locked ship's bar on top of the others. 158's sentry guns appear at Harval's position (full hull);
                // with Harval EMP'd and still, a later marker's bar covered his, which looked frozen.
                if (locked && layer.IndexOf(m.bracket) != layer.childCount - 1) { m.bar.BringToFront(); m.fill.BringToFront(); m.bracket.BringToFront(); }
            }
            else
            {
                // Off screen the original's dot / ring on the radar ellipse; on screen the far marker the option picks.
                var look = FarShipMarkers.Look(onScreen ? farStyle : Settings.FarMarkerOriginal, locked);
                var tex = look.shape switch
                {
                    FarShipMarkers.Shape.Ring => t.ring,
                    FarShipMarkers.Shape.OpenRing => t.openRing,
                    FarShipMarkers.Shape.Bracket => t.bracket,
                    _ => t.dot,
                };
                if (tex != null)
                {
                    float w = tex.width * look.scale, h = tex.height * look.scale;
                    m.dot.style.backgroundImage = new StyleBackground(tex);
                    m.dot.style.width = w;
                    m.dot.style.height = h;
                    Place(m.dot, p.x - w / 2f, p.y - look.rise - h / 2f);
                    m.top = p.y - look.rise - h / 2f;
                }
                if (distance) DrawDistance(m, world, faction, locked, cam, p);
            }
        }

        /// <summary>The distance under a far on-screen marker: the locked one's as the original (white, at (-61, +61)), the
        /// others' (the "Distance on ship markers" option) small and centred in the faction's colour. The text is written
        /// only when it changes (no string per ship per frame in a fleet battle).</summary>
        void DrawDistance(Marker m, Vector3 world, int faction, bool locked, Camera cam, Vector2 p)
        {
            int look = locked ? 0 : 1 + faction;
            if (look != m.labelLook)
            {
                m.labelLook = look;
                m.distance.EnableInClassList("combat-label--far", !locked);
                for (int i = 0; i < 3; i++) m.distance.EnableInClassList(LabelFaction[i], !locked && i == faction);
            }
            float units = (world - camPos).magnitude / M;
            int key = FarShipMarkers.DistanceKey(units);
            if (key != m.distanceKey)
            {
                m.distanceKey = key;
                m.distance.text = Navigation.FormatDistance(units);
            }
            Place(m.distance, p.x - 61f, p.y + FarShipMarkers.LabelTop(farStyle, locked));
        }

        /// <summary>Remake (multiplayer, players' feedback 2026-10-22: other players were yellow like any neutral NPC): another
        /// player's name ("[TAG] Name") over their on-screen marker, near or far, in the marker's colour. Off screen the radar
        /// ellipse's dot stays as it is. The text and colour are written only when they change.</summary>
        void DrawPilotName(Marker m, string name, int faction, bool locked)
        {
            bool on = m.onScreen && !string.IsNullOrEmpty(name);
            if (m.pilot == null)
            {
                if (!on) return;
                m.pilot = new Label { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
                m.pilot.AddToClassList("combat-label");
                m.pilot.AddToClassList("combat-label--pilot");
                m.pilot.AddToClassList("gof-semibold");
                layer.Add(m.pilot);
            }
            Show(m.pilot, on);
            if (!on) return;
            if (m.pilot.text != name) m.pilot.text = name;
            if (faction != m.pilotLook)
            {
                m.pilotLook = faction;
                for (int i = 0; i < 3; i++) m.pilot.EnableInClassList(LabelFaction[i], i == faction);
            }
            // Its bottom 4 px over the marker (the label's box is PilotLabelHeight high, its width centred by the USS).
            Place(m.pilot, m.x - PilotLabelWidth / 2f, m.top - PilotLabelHeight - 4f);
            if (locked && layer.IndexOf(m.pilot) != layer.childCount - 1) m.pilot.BringToFront();
        }

        // .combat-label--pilot's box (FlightHud.uss).
        const float PilotLabelWidth = 240f, PilotLabelHeight = 22f;

        /// <summary>Radar::draw (ship_combat.md 7.2): a near ship's EMP bar 8 px under its hull bar while EMP &lt; max.</summary>
        void DrawEmp(Marker m, float emp)
        {
            bool on = m.bar.style.display.value == DisplayStyle.Flex && emp < 1f;
            Show(m.emp, on);
            Show(m.empFill, on);
            if (!on) return;
            Image(m.emp, Tex("emp_bar"));
            Image(m.empFill, Tex("emp_fill"));
            var at = m.bar.style.translate.value;
            float x = at.x.value, y = at.y.value + 18f;
            Place(m.emp, x, y);
            Place(m.empFill, x + 2f, y + 2f);
            m.empFill.style.width = Mathf.Clamp01(emp) * 110f;
        }

        /// <summary>Remake (multiplayer): another player's shield bar 11 px above their hull bar, and the hull bar in the status
        /// bar's red with their armor over it in yellow (Player::damage takes shield, then armor, then hull). A pool of -1 isn't
        /// there; with neither the marker stays as DrawShip drew it.</summary>
        void DrawPools(Marker m, float shield, float armor, bool locked)
        {
            bool bar = m.bar.style.display.value == DisplayStyle.Flex;
            if (!bar || shield < 0f && armor < 0f) { HidePools(m); return; }
            if (m.shieldBar == null)
            {
                m.shieldBar = NewImage(); m.shieldFill = NewImage(); m.armorFill = NewImage();
                m.shieldFill.style.backgroundImage = new StyleBackground(Tex("status_shield_fill"));
                m.armorFill.style.backgroundImage = new StyleBackground(Tex("status_armor_fill"));
                m.shieldFill.style.height = 6f;
                m.armorFill.style.height = 6f;
            }
            var at = m.bar.style.translate.value;
            float x = at.x.value, y = at.y.value;
            // The hull fill (DrawShip's, at the faction colour) in the status bar's red, so the yellow armor reads over it.
            m.fill.style.backgroundImage = new StyleBackground(Tex("status_hull_fill"));
            m.fill.style.height = 6f;
            bool hasShield = shield >= 0f;
            Show(m.shieldBar, hasShield);
            Show(m.shieldFill, hasShield);
            if (hasShield)
            {
                m.shieldBar.style.backgroundImage = m.bar.style.backgroundImage;   // the faction's frame
                m.shieldBar.style.width = m.bar.style.width;
                m.shieldBar.style.height = m.bar.style.height;
                Place(m.shieldBar, x, y - 11f);
                Place(m.shieldFill, x + 1f, y - 9f);
                m.shieldFill.style.width = Mathf.Clamp01(shield) * 110f;
            }
            bool hasArmor = armor >= 0f;
            Show(m.armorFill, hasArmor);
            if (hasArmor)
            {
                Place(m.armorFill, x + 1f, y + 2f);
                m.armorFill.style.width = Mathf.Clamp01(armor) * 110f;
            }
            // The armor over the hull fill; the locked ship's (which DrawShip brought to the front) its shield too, under the bracket.
            if (layer.IndexOf(m.armorFill) < layer.IndexOf(m.fill))
            {
                m.armorFill.PlaceInFront(m.fill);
                if (locked) { m.shieldBar.PlaceInFront(m.fill); m.shieldFill.PlaceInFront(m.shieldBar); }
            }
        }

        static void HidePools(Marker m)
        {
            if (m.shieldBar == null) return;
            Show(m.shieldBar, false); Show(m.shieldFill, false); Show(m.armorFill, false);
        }

        void DrawCrate(Marker m, Vector3 world, bool voidCrate, Camera cam, Vector2 origin, Vector2 centre)
        {
            bool onScreen = Project(cam, world, origin, centre, out var p, out bool near);
            Show(m.bar, false); Show(m.fill, false); Show(m.distance, false); Show(m.bracket, false);
            Show(m.emp, false); Show(m.empFill, false);
            Show(m.dot, onScreen || !Vr.VrMode.Enabled);   // VR: no off-screen boxes on the ellipse
            var tex = !onScreen ? Tex(voidCrate ? "crate_off_void" : "crate_off") : near ? Tex("bracket") : Tex("crate_dot");
            Image(m.dot, tex);
            if (tex != null) Place(m.dot, p.x - tex.width / 2f, p.y - tex.height / 2f);
        }

        /// <summary>A gas cloud (Radar::draw): on screen the white diamond 0x4f1 with the distance under it while locked; off
        /// screen, with the Omega (attr 58), 0x1f62 on the radar ellipse.</summary>
        void DrawCloud(Marker m, Vector3 world, bool locked, bool offScreen, Camera cam, Vector2 origin, Vector2 centre)
        {
            bool onScreen = Project(cam, world, origin, centre, out var p, out _);
            Show(m.bar, false); Show(m.fill, false); Show(m.bracket, false); Show(m.emp, false); Show(m.empFill, false);
            bool dot = onScreen || (offScreen && !Vr.VrMode.Enabled);
            Show(m.dot, dot);
            Show(m.distance, onScreen && locked);
            if (!dot) return;
            var tex = onScreen ? Tex("crate_dot") : Tex("cloud_off");
            Image(m.dot, tex);
            if (tex != null) Place(m.dot, p.x - tex.width / 2f, p.y - tex.height / 2f);
            if (onScreen && locked)
            {
                m.distance.text = Navigation.FormatDistance((world - camPos).magnitude / M);
                m.distanceKey = int.MinValue;
                Place(m.distance, p.x - 61f, p.y + 61f);
            }
        }

        void UpdateStatus(PlayerHealth health, bool cinematic)
        {
            bool show = health != null && health.Target != null && !cinematic && !health.Dead;
            statusPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < 4; i++) arcs[i].EnableInClassList("hit-arc--shown", show && health.ArcMs[i] > 0f);
            if (!show) return;
            var hp = health.Hp;
            shieldRow.style.display = health.HasShield ? DisplayStyle.Flex : DisplayStyle.None;
            Image(shieldIcon, Tex(health.ShieldHitMs > 0f ? "status_shield_hit" : "status_shield"));
            Image(hullIcon, Tex(hp.armor > 0 ? "status_hull" : "status_hull_red"));
            shieldFill.style.width = hp.ShieldFraction * 248f;
            hullFill.style.width = hp.HullFraction * 248f;
            armorFill.style.display = health.HasArmor ? DisplayStyle.Flex : DisplayStyle.None;
            armorFill.style.width = hp.ArmorFraction * 248f;
            // Hud::draw: the third row (icon 0x1f59, frame 0x1f5a, fill 0x1f5b, gamma / 100 * 248) where gamma rays drain the pool.
            bool gamma = health.Gamma >= 0f;
            gammaRow.style.display = gamma ? DisplayStyle.Flex : DisplayStyle.None;
            if (gamma) gammaFill.style.width = Mathf.Clamp01(health.Gamma / 100f) * 248f;
            // The secondary weapon's plate moves below the third row.
            speedPanel?.EnableInClassList("speed-panel--low", gamma && health.HasShield);

            // Hit arcs: blue with the shield up, red without; left mirrored, bottom flipped.
            bool blue = hp.shield > 0f;
            for (int i = 0; i < 4; i++)
            {
                if (health.ArcMs[i] <= 0f) continue;
                var tex = Tex(i < 2 ? (blue ? "hit_side_blue" : "hit_side_red") : (blue ? "hit_top_blue" : "hit_top_red"));
                if (tex == null) continue;
                var a = arcs[i];
                Image(a, tex);
                float w = tex.width, h = tex.height;
                // Remake: each image kept inside the safe area. Centred on the ellipse (491 units up / down) the 374-unit top
                // and bottom arcs reach 678 units out, their glow (the outer half) past a 1080-unit screen's 540 (a player's
                // report: mostly off screen on a phone); the sides' 819 of 960 fit at 16:9, not on a narrower screen.
                var lim = ArcLimits();
                switch (i)
                {
                    case 0: PlaceFixed(a, Mathf.Max(-EllipseX - w / 2f, lim.xMin), -h / 2f); a.style.scale = new Scale(new Vector3(-1f, 1f, 1f)); break;
                    case 1: PlaceFixed(a, Mathf.Min(EllipseX - w / 2f, lim.xMax - w), -h / 2f); break;
                    case 2: PlaceFixed(a, -w / 2f, Mathf.Max(-EllipseY - h / 2f, lim.yMin)); break;
                    default: PlaceFixed(a, -w / 2f, Mathf.Min(EllipseY - h / 2f, lim.yMax - h)); a.style.scale = new Scale(new Vector3(1f, -1f, 1f)); break;
                }
            }
        }
    }
}
