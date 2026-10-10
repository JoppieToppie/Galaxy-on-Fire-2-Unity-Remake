// CombatView.cs
// The flight HUD's combat visuals (Reference/research/ship_combat.md 6.1, 7), driven by FlightHud:
//   ship markers (Radar::draw ship loop, only with a scanner): faction colour red enemy / green friend / yellow neutral;
//     off screen a dot (ring when locked) on the radar ellipse 657 x 491; on screen far (any axis > 24000 units from the
//     camera) the dot / ring, plus the distance for the locked ship at (-61, +61); near: the hull bar (114 x 10 frame at
//     (-59, +63), 110 x 6 fill) and, when locked, the faction bracket 81 x 81
//   crates: far a white diamond 0x4f1, near the white bracket 0x4f2, off screen the box 0x451 (0x44d Void); also a
//     fighter dying with cargo (KIPlayer+0x48) until it explodes
//   ship lock: the lock ring on the crosshair (24 frames, from t = 0; crates after 500 ms) and the top plate
//     "<race> NN%" with the race icon (Radar::drawCurrentLock)
//   player status (Hud::draw, top left): shield icon (red 500 ms after a hit) + cyan bar, hull icon (red once the armor is
//     gone) + red hull bar with the yellow armor bar over it
//   hit arcs: blue while the shield holds, red without, 300 ms, on the ellipse's left / right / top / bottom
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
        const float EllipseX = 657f, EllipseY = 491f;

        class Marker
        {
            public VisualElement dot, bar, fill, bracket, emp, empFill;
            public Label distance;
            public bool used;
        }

        readonly VisualElement layer, lockRing, lockPlate, lockClass, statusPanel, shieldRow, shieldIcon, shieldFill, hullIcon, hullFill, armorFill,
                                      gammaRow, gammaFill, speedPanel;
        readonly Label lockOre;
        readonly VisualElement[] arcs = new VisualElement[4];
        readonly Texture2D[] lockFrames = new Texture2D[24];
        readonly Dictionary<Object, Marker> markers = new Dictionary<Object, Marker>();
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly string[] Faction = { "enemy", "friend", "neutral" };

        /// <summary>A faction's marker images, looked up once: DrawShip runs per ship per frame, and building the names
        /// ("ship_bar_enemy"...) each time allocated strings.</summary>
        class MarkerTextures { public Texture2D bar, fill, bracket, ring, dot; }

        // The lock plate as last built (Update).
        Target plateTarget;
        int platePct = -1, plateRace = int.MinValue;
        string plateName, plateText;
        Texture2D plateIcon;
        readonly MarkerTextures[] factionTextures = new MarkerTextures[3];

        MarkerTextures FactionTextures(int faction)
        {
            var t = factionTextures[faction];
            if (t != null) return t;
            string f = Faction[faction];
            return factionTextures[faction] = new MarkerTextures
            {
                bar = Tex($"ship_bar_{f}"), fill = Tex($"ship_fill_{f}"), bracket = Tex($"ship_bracket_{f}"),
                ring = Tex($"ship_ring_{f}"), dot = Tex($"ship_dot_{f}"),
            };
        }

        Texture2D Tex(string name)
        {
            if (!textures.TryGetValue(name, out var t)) textures[name] = t = Resources.Load<Texture2D>("GoF2Hud/" + name);
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
                        if (s.Gone || !s.Target.Alive || s.Hidden || s.Asleep || s.RadarHidden || s.DockingType > 0) continue;
                        int f = s.Target.hostileToPlayer ? 0 : s.Target.friendToPlayer ? 1 : 2;
                        DrawShip(Get(s), s.transform.position, f, s.Target.HullFraction, radar.Locked == s.Target, cam, origin, centre);
                        DrawEmp(Get(s), s.Hp != null ? s.Hp.EmpFraction : 1f);
                    }
                foreach (var o in Target.NetShips)   // multiplayer: the other players, the host's ships on a client
                    if (o != null && o.Alive && !o.untargetable)
                        DrawShip(Get(o), o.transform.position, o.hostileToPlayer ? 0 : o.friendToPlayer ? 1 : 2, o.HullFraction, radar.Locked == o, cam, origin, centre);
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
                    // Radar::drawCurrentLock 0x158548: "<name or race> NN%"; the Hijacker / the Informer their name alone; a Most
                    // Wanted criminal in its own colour.
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
                        plateIcon = !locked.plateNoIcon && (locked.race >= 0 && locked.race <= 3 || locked.race == 8 || locked.race == 9) ? Tex($"race_{locked.race}") : null;
                    }
                    lockOre.EnableInClassList("lock-ore--wanted", locked.plateWanted);
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
                    (gone ??= new List<Object>()).Add(kv.Key);
                    continue;
                }
                Show(m.dot, false); Show(m.bar, false); Show(m.fill, false); Show(m.bracket, false); Show(m.distance, false);
                Show(m.emp, false); Show(m.empFill, false);
            }
            if (gone != null) foreach (var k in gone) markers.Remove(k);
        }

        static string RaceName(int race) => race switch
        {
            >= 0 and <= 3 => Localization.Get(406 + race),
            8 => Localization.Get(414),
            9 => Localization.Get(415),
            _ => "???",
        };

        bool Project(Camera cam, Vector3 world, Vector2 origin, Vector2 centre, out Vector2 p, out bool near)
        {
            var sp = cam.WorldToScreenPoint(world);
            bool onScreen = sp.z > 0f && sp.x >= 0f && sp.y >= 0f && sp.x <= Screen.width && sp.y <= Screen.height;
            var d = (world - cam.transform.position) / M;
            near = Mathf.Abs(d.x) <= NearUnits && Mathf.Abs(d.y) <= NearUnits && Mathf.Abs(d.z) <= NearUnits;
            if (onScreen)
            {
                // Viewport -> panel (the camera fills the screen): exact on screen and in offscreen captures.
                var v = cam.WorldToViewportPoint(world);
                var size = layer.panel.visualTree.layout.size;
                p = new Vector2(v.x * size.x, (1f - v.y) * size.y) - origin;
            }
            else p = OffScreen(cam, world, centre);
            return onScreen;
        }

        /// <summary>Radar::update for an object not on screen: its direction clamped onto the radar ellipse.</summary>
        static Vector2 OffScreen(Camera cam, Vector3 world, Vector2 centre)
        {
            var local = cam.transform.InverseTransformPoint(world);
            var d = new Vector2(local.x, -local.y);
            if (d.sqrMagnitude < 1e-6f) d = Vector2.down;
            float k = Mathf.Sqrt(d.x * d.x / (EllipseX * EllipseX) + d.y * d.y / (EllipseY * EllipseY));
            return centre + d / k;
        }

        void DrawShip(Marker m, Vector3 world, int faction, float hull, bool locked, Camera cam, Vector2 origin, Vector2 centre, bool dotOnly = false)
        {
            bool onScreen = Project(cam, world, origin, centre, out var p, out bool near);
            // VR: no dots on the radar ellipse (it would reach onto the cockpit's displays): the dashboard's scope has them.
            if (!onScreen && Vr.VrMode.Enabled)
            {
                Show(m.bar, false); Show(m.fill, false); Show(m.bracket, false); Show(m.dot, false); Show(m.distance, false);
                return;
            }
            var t = FactionTextures(faction);
            bool bar = onScreen && near && !dotOnly;
            Show(m.bar, bar);
            Show(m.fill, bar);
            Show(m.bracket, bar && locked);
            Show(m.dot, !bar);
            Show(m.distance, !bar && onScreen && locked);
            if (bar)
            {
                Image(m.bar, t.bar);
                Image(m.fill, t.fill);
                Place(m.bar, p.x - 59f, p.y + 63f);
                Place(m.fill, p.x - 58f, p.y + 65f);
                m.fill.style.width = Mathf.Clamp01(hull) * 110f;
                if (locked) { Image(m.bracket, t.bracket); Place(m.bracket, p.x - 40.5f, p.y - 40.5f); }
            }
            else
            {
                var tex = locked ? t.ring : t.dot;
                Image(m.dot, tex);
                if (tex != null) Place(m.dot, p.x - tex.width / 2f, p.y - tex.height / 2f);
                if (onScreen && locked)
                {
                    m.distance.text = Navigation.FormatDistance((world - cam.transform.position).magnitude / M);
                    Place(m.distance, p.x - 61f, p.y + 61f);
                }
            }
        }

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
                m.distance.text = Navigation.FormatDistance((world - cam.transform.position).magnitude / M);
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
                switch (i)
                {
                    case 0: PlaceFixed(a, -EllipseX - w / 2f, -h / 2f); a.style.scale = new Scale(new Vector3(-1f, 1f, 1f)); break;
                    case 1: PlaceFixed(a, EllipseX - w / 2f, -h / 2f); break;
                    case 2: PlaceFixed(a, -w / 2f, -EllipseY - h / 2f); break;
                    default: PlaceFixed(a, -w / 2f, EllipseY - h / 2f); a.style.scale = new Scale(new Vector3(1f, -1f, 1f)); break;
                }
            }
        }
    }
}
