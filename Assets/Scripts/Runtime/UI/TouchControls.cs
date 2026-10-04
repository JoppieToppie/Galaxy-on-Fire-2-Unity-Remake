// TouchControls.cs
// The original's touchscreen flight controls (Reference/research/touch_hud.md) as UI Toolkit elements with the original
// images (Resources/GoF2Hud/touch_*, "GoF2 > Build > HUD Images"), laid out like Globals::setCoordsSteer / setCoordsFire
// (panel units = the original's HD canvas pixels) and driven by their own pointer events:
//   left group, from S (415)   fast-forward / time extender (40, S - 180) and autopilot (40, S) on their pill (NavigationView
//                              draws and handles them; placed here), the fixed stick: base 0x4c1 at (20, S + 132), centre
//                              (165, S + 277), knob 0x4b6 / 0x4b7, travel 94, no dead zone, its touch area +-112 x +-216
//                              around the centre (Hud::touchedElement), a double tap (<= 499 ms) levels out; boost 0x4b2 /
//                              0x4b3 at (40, S + 446), sliding right along the bottom
//   right cluster, from F (365) background 0x6aa (secondary socket at the bottom) or 0x4c6 (on the left, F > h - 401) at
//                              (w - 321, F); fire 0x4b4 / 0x4b5 centred at bg + (200, 200) with the action arrow 0x536;
//                              quick menu 0x4ba / 0x4bb at bg + (152, 8); camera (the next mode's icon) at bg + (28, 27);
//                              secondary 0x4bc / 0x4bd at bg + (144, 298) or bg + (-2, 189); the auto-turret toggle 0x547 /
//                              0x546 at bg + (160, -134)
//   pause                      0x4b8 / 0x4b9 at (w - 121, 24), also during cutscenes (sounds 124 / 123)
//   secondary plate            0x4c2 at the bottom centre, "<name> (<n>)" (Hud::updateSecondaryWeaponString; remake: a tap
//                              switches to the next secondary), in every input mode (Hud::draw redraws it opaque while the PC
//                              version's mouse steers)
//   empty screen               an element behind the controls: a vertical drag sets the throttle after 160 px, 400 px =
//                              0 -> 100 % (up = faster; the gauge 0x548 under the crosshair fills from the bottom with the
//                              number, 2 s fade), a sideways flick dodges (MGame::maneuverTouchEnd), in free look it orbits
// Fire is also the action button (FlightHud.OnTouchFire); a second press within 249 ms latches autofire, the next press
// releases it. The secondary, boost, camera, menu, turret and pause act on release over the button (sliding off cancels,
// like Hud::touchEnd); every control keeps its finger (pointer capture). Positions follow the safe area; the original's
// Configure screen (dragging S and F) isn't built.
// The PC version's cursor mode (Frame.cursor: keys and mouse, the mouse not steering; Hud::draw's Globals::iPad == 0 path and
// the Full HD manual's screenshot): the same buttons clicked with the mouse, but no stick and no gestures; the cluster in the
// bottom-right corner, the autopilot / fast-forward pill and the boost at the bottom left.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class TouchControls
    {
        /// <summary>What reacts this frame: nothing, only the pause button (cutscenes, the launch camera), nothing but the
        /// stick drawn (a HUD menu is open), or everything.</summary>
        public enum Mode { Off, PauseOnly, MenuOpen, Full }

        /// <summary>What the flight HUD knows this frame (FlightHud fills it before Update).</summary>
        public struct Frame
        {
            public Mode mode;
            public bool hasBooster, boosting, boostReady;
            public float boostRate;              // 0..1 recharge
            public bool secondary;               // a secondary selected with ammo, not in the turret view
            public string secondaryText;         // "<name> (<n>)"
            public bool menu;                    // the quick menu has entries
            public int nextCamera;               // FreeLookCamera.Mode of the camera button's icon (0 / 1 / 3), -1 = hidden
            public bool turret, turretOn;        // the auto-turret toggle
            public bool actionArrow;             // something is locked that fire acts on
            public bool dimStick, dimNav;        // tilt / autopilot / approach / hacking; mining / hacking / docked at a point
            public bool mining;                  // the boost's mining alpha
            public bool freeLook, standardCamera;
            public bool steeringMissile;         // Liberator: only the stick and the secondary
            public Vector2 crosshair;            // safe-area units
            public bool crosshairVisible;
            public bool gauge;                   // keyboard / controller flight: the throttle gauge without the touch controls
            public bool cursor;                  // the PC cursor mode: the buttons without the stick and the gestures
        }

        // Actions (FlightHud).
        public Func<bool> FirePressed;             // true = the press was an action (no shooting)
        public Action SecondaryReleased, BoostReleased, CameraReleased, MenuReleased, TurretReleased;
        public Action PausePressed, PauseReleased, LevelOut, CycleSecondary;
        public Action<int> Dodge;                  // 1 left, 2 right
        public Func<float> GetThrust;
        public Action<float> SetThrust;
        public Action<Vector2, bool> FreeLookDrag; // screen-pixel delta (y up), still held
        public Action<float> FreeLookPinch;        // two-finger span change in screen pixels

        /// <summary>The stick, offset / 94 per axis (x right, y up), not squared.</summary>
        public Vector2 Stick { get; private set; }
        /// <summary>The fire button is held (or autofire is latched) and the press wasn't an action.</summary>
        public bool FireHeld => autofire || (firePointer >= 0 && !fireSwallowed);

        const float S0 = 415f, F0 = 365f;   // Globals::Globals: the Android defaults
        const float StickTravel = 94f, StickHitX = 112f, StickHitY = 216f;
        const float ThrottleDeadTravel = 160f, ThrottlePixels = 400f;

        class Gesture
        {
            public Vector2 start, last;
            public float startTime;
            public bool dodge, throttle, dragging;
            public float anchorY, thrust0;
        }

        readonly VisualElement layer, pauseHost, navButtons;
        readonly VisualElement gestureZone, stickArea, stickBase, knob, boost, cluster, fire, arrow, secondary, menu, camera, turret, pause,
                               plate, gauge, gaugeFill;
        readonly Label plateText, gaugeText;
        readonly Dictionary<int, Gesture> gestures = new Dictionary<int, Gesture>();
        readonly HashSet<VisualElement> held = new HashSet<VisualElement>();
        readonly Texture2D knobOff, knobOn, fireOff, fireOn, boostOff, boostOn, pauseOff, pauseOn, menuOff, menuOn, secOff, secOn,
                           turretOff, turretOn, clusterBottom, clusterLeft;
        readonly Texture2D[] camOff = new Texture2D[4], camOn = new Texture2D[4];
        int stickPointer = -1, firePointer = -1;
        bool fireSwallowed, autofire, boostWasReady;
        float lastFirePress = -10f, lastStickPress = -10f, boostFlashMs, gaugeMs = -1f;
        Vector2 stickCentre;
        Frame frame;

        static Texture2D Tex(string name) => Resources.Load<Texture2D>("GoF2Hud/" + name);

        /// <param name="layer">the touch layer inside the safe area</param>
        /// <param name="pauseHost">where the pause button goes (the HUD root, so it shows during cutscenes)</param>
        /// <param name="navButtons">NavigationView's autopilot / fast-forward block (placed here)</param>
        public TouchControls(VisualElement layer, VisualElement pauseHost, VisualElement navButtons)
        {
            this.layer = layer;
            this.pauseHost = pauseHost;
            this.navButtons = navButtons;
            layer.Clear();
            knobOff = Tex("touch_stick_knob"); knobOn = Tex("touch_stick_knob_on");
            fireOff = Tex("touch_fire"); fireOn = Tex("touch_fire_on");
            boostOff = Tex("touch_boost"); boostOn = Tex("touch_boost_on");
            pauseOff = Tex("touch_pause"); pauseOn = Tex("touch_pause_on");
            menuOff = Tex("touch_menu"); menuOn = Tex("touch_menu_on");
            secOff = Tex("touch_secondary"); secOn = Tex("touch_secondary_on");
            turretOff = Tex("touch_turret"); turretOn = Tex("touch_turret_on");
            clusterBottom = Tex("touch_cluster_bottom"); clusterLeft = Tex("touch_cluster_left");
            foreach (int m in new[] { 0, 1, 3 }) { camOff[m] = Tex($"touch_cam_{m}"); camOn[m] = Tex($"touch_cam_{m}_on"); }

            // The empty screen behind everything: throttle drag, dodge, free look.
            gestureZone = new VisualElement();
            gestureZone.AddToClassList("touch-gesture-zone");
            layer.Add(gestureZone);
            HookGestures();

            // Hud::draw's order: the stick, the cluster background, the camera, turret, menu, secondary, boost, fire, arrow.
            stickArea = new VisualElement();
            stickArea.AddToClassList("touch-abs");
            stickArea.style.width = StickHitX * 2f;
            stickArea.style.height = StickHitY * 2f;
            layer.Add(stickArea);
            stickBase = Image(layer, Tex("touch_stick_base"), false);
            knob = Image(layer, knobOff, false);
            HookStick();
            cluster = Image(layer, clusterBottom, false);
            camera = Button(Image(layer, camOff[3]), null, () => CameraReleased?.Invoke());
            turret = Button(Image(layer, turretOff), null, () => TurretReleased?.Invoke());
            menu = Button(Image(layer, menuOff), null, () => MenuReleased?.Invoke());
            secondary = Button(Image(layer, secOff), null, () => SecondaryReleased?.Invoke());
            boost = Button(Image(layer, boostOff), null, () => BoostReleased?.Invoke());
            fire = Image(layer, fireOff);
            HookFire();
            arrow = Image(layer, Tex("touch_action"), false);

            // PlayerEgo::drawThrottle: the half ring 0x548 filling from the bottom, the number under the crosshair.
            // Its own layer beside the touch controls' (the same box), shown in every input mode: the keyboard / controller
            // throttle shows it too (the iPhone version's gauge; the remake's PC readout).
            var gaugeLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            gaugeLayer.AddToClassList("touch-gauge-layer");
            if (layer.parent != null) layer.parent.Insert(layer.parent.IndexOf(layer) + 1, gaugeLayer); else layer.Add(gaugeLayer);
            gauge = new VisualElement { pickingMode = PickingMode.Ignore };
            gauge.AddToClassList("touch-abs");
            gauge.style.overflow = Overflow.Hidden;
            gauge.style.width = 162;
            gaugeFill = Image(gauge, Tex("throttle_gauge"), false);
            gaugeLayer.Add(gauge);
            gaugeText = new Label { pickingMode = PickingMode.Ignore };
            gaugeText.AddToClassList("touch-gauge-text");
            gaugeText.AddToClassList("gof-semibold");
            gaugeLayer.Add(gaugeText);
            // The secondary's plate: the gauge layer's too, so keyboard and controller flight show it.
            plate = Image(gaugeLayer, Tex("touch_secondary_plate"));
            plate.RegisterCallback<PointerDownEvent>(e => { CycleSecondary?.Invoke(); e.StopPropagation(); });
            plateText = new Label { pickingMode = PickingMode.Ignore };
            plateText.AddToClassList("touch-plate-text");
            plateText.AddToClassList("gof-semibold");
            plate.Add(plateText);

            pause = Button(Image(pauseHost, pauseOff), () => PausePressed?.Invoke(), () => PauseReleased?.Invoke());
            pause.AddToClassList("touch-pause");
            Show(pause, false);
        }

        static VisualElement Image(VisualElement parent, Texture2D tex, bool pickable = true)
        {
            var e = new VisualElement { pickingMode = pickable ? PickingMode.Position : PickingMode.Ignore };
            e.AddToClassList("touch-abs");
            SetImage(e, tex);
            parent.Add(e);
            return e;
        }

        static void SetImage(VisualElement e, Texture2D tex)
        {
            if (tex == null || e.style.backgroundImage.value.texture == tex) return;
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.width = tex.width;
            e.style.height = tex.height;
        }

        static void Place(VisualElement e, Vector2 topLeft)
        {
            e.style.left = topLeft.x;
            e.style.top = topLeft.y;
        }

        static void Show(VisualElement e, bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        /// <summary>A button acting on press / on release over it (MGame::OnTouchEnd acts on the element under the finger:
        /// releasing after sliding off does nothing); lit while held.</summary>
        VisualElement Button(VisualElement e, Action down, Action up)
        {
            int pointer = -1;
            e.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (pointer >= 0) return;
                pointer = ev.pointerId;
                e.CapturePointer(pointer);
                held.Add(e);
                down?.Invoke();
                ev.StopPropagation();
            });
            e.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (ev.pointerId != pointer) return;
                bool over = e.ContainsPoint(ev.localPosition);
                Release();
                if (over) up?.Invoke();
                ev.StopPropagation();
            });
            e.RegisterCallback<PointerCancelEvent>(ev => { if (ev.pointerId == pointer) Release(); });
            e.RegisterCallback<PointerCaptureOutEvent>(_ => { if (pointer >= 0) { pointer = -1; held.Remove(e); } });
            void Release()
            {
                if (pointer >= 0 && e.HasPointerCapture(pointer)) e.ReleasePointer(pointer);
                pointer = -1;
                held.Remove(e);
            }
            return e;
        }

        /// <summary>MGame::OnTouchBegin: the action first; else shooting while held, with the autofire latch (0x1a898a).</summary>
        void HookFire()
        {
            fire.RegisterCallback<PointerDownEvent>(e =>
            {
                if (firePointer >= 0) return;
                firePointer = e.pointerId;
                fire.CapturePointer(firePointer);
                fireSwallowed = false;
                e.StopPropagation();
                if (FirePressed != null && FirePressed()) { fireSwallowed = true; return; }
                float now = Time.unscaledTime;
                if (now - lastFirePress <= 0.249f) autofire = true;
                else if (autofire) { autofire = false; fireSwallowed = true; }   // the press that unlatches doesn't fire
                lastFirePress = now;
            });
            void Up(int id)
            {
                if (id != firePointer) return;
                if (fire.HasPointerCapture(firePointer)) fire.ReleasePointer(firePointer);
                firePointer = -1;
            }
            fire.RegisterCallback<PointerUpEvent>(e => Up(e.pointerId));
            fire.RegisterCallback<PointerCancelEvent>(e => Up(e.pointerId));
            fire.RegisterCallback<PointerCaptureOutEvent>(_ => firePointer = -1);
        }

        /// <summary>Hud::touchBegin / touchMove on the stick: the knob follows the finger up to 94 px, the value is the offset
        /// / 94 (no dead zone); a second press within 499 ms levels out (MGame::OnTouchBegin 0x1a8558).</summary>
        void HookStick()
        {
            stickArea.RegisterCallback<PointerDownEvent>(e =>
            {
                if (stickPointer >= 0) return;
                stickPointer = e.pointerId;
                stickArea.CapturePointer(stickPointer);
                if (Time.unscaledTime - lastStickPress <= 0.499f) LevelOut?.Invoke();
                lastStickPress = Time.unscaledTime;
                MoveKnob(e.position);
                e.StopPropagation();
            });
            stickArea.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == stickPointer) MoveKnob(e.position); });
            void Up(int id)
            {
                if (id != stickPointer) return;
                if (stickArea.HasPointerCapture(stickPointer)) stickArea.ReleasePointer(stickPointer);
                ReleaseStick();
            }
            stickArea.RegisterCallback<PointerUpEvent>(e => Up(e.pointerId));
            stickArea.RegisterCallback<PointerCancelEvent>(e => Up(e.pointerId));
            stickArea.RegisterCallback<PointerCaptureOutEvent>(_ => ReleaseStick());
        }

        void MoveKnob(Vector3 panelPosition)
        {
            var p = layer.WorldToLocal(panelPosition);
            var d = Vector2.ClampMagnitude((Vector2)p - stickCentre, StickTravel);
            Stick = new Vector2(d.x, -d.y) / StickTravel;
            Place(knob, stickCentre + d - new Vector2(90.5f, 90.5f));
        }

        void ReleaseStick()
        {
            stickPointer = -1;
            Stick = Vector2.zero;
            Place(knob, stickCentre - new Vector2(90.5f, 90.5f));
        }

        /// <summary>Touches on no control (MGame::OnTouchBegin with no HUD element, camera 0 / 1): the throttle drag and the
        /// dodge share the finger (a mostly vertical drag throttles, a quick sideways flick dodges); in free look the
        /// drag orbits the camera (MGame::freeCamTouch*).</summary>
        void HookGestures()
        {
            gestureZone.RegisterCallback<PointerDownEvent>(e =>
            {
                if (frame.mode != Mode.Full || gestures.ContainsKey(e.pointerId)) return;
                gestures[e.pointerId] = new Gesture
                {
                    start = e.localPosition, last = e.localPosition, startTime = Time.unscaledTime,
                    dodge = frame.standardCamera, throttle = !frame.freeLook,
                };
                gestureZone.CapturePointer(e.pointerId);
            });
            gestureZone.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!gestures.TryGetValue(e.pointerId, out var g)) return;
                var p = (Vector2)e.localPosition;
                // Free look: two fingers pinch-zoom (as in PhotoMode), one orbits.
                if (frame.freeLook && gestures.Count >= 2)
                {
                    float span0 = Span();
                    g.last = p;
                    FreeLookPinch?.Invoke(ToScreenDelta(new Vector2(Span() - span0, 0f)).x);
                    return;
                }
                if (frame.freeLook) FreeLookDrag?.Invoke(ToScreenDelta(p - g.last), true);
                g.last = p;
                if (frame.freeLook) return;
                // MGame::maneuverTouchMove: more than h/320 * 90 up or down cancels the dodge.
                if (g.dodge && Mathf.Abs(p.y - g.start.y) > Height / 320f * 90f) g.dodge = false;
                if (!g.throttle || GetThrust == null || SetThrust == null) return;
                // MGame::OnTouchMove 0x1a9088: 160 px of travel before it takes over, then 400 px per 100 %.
                if (!g.dragging && Mathf.Abs(p.y - g.start.y) > ThrottleDeadTravel)
                {
                    g.dragging = true;
                    g.anchorY = p.y;
                    g.thrust0 = GetThrust();
                }
                if (!g.dragging) return;
                float thrust = Mathf.Clamp01(g.thrust0 + (g.anchorY - p.y) / ThrottlePixels);
                if (!Mathf.Approximately(thrust, GetThrust())) { SetThrust(thrust); ThrottleChanged(); }
            });
            gestureZone.RegisterCallback<PointerUpEvent>(e => EndGesture(e.pointerId, e.localPosition, true));
            gestureZone.RegisterCallback<PointerCancelEvent>(e => EndGesture(e.pointerId, e.localPosition, false));
            gestureZone.RegisterCallback<PointerCaptureOutEvent>(e => gestures.Remove(e.pointerId));
        }

        float Span()
        {
            Vector2 a = Vector2.zero;
            int n = 0;
            foreach (var o in gestures.Values) { if (n++ == 0) a = o.last; else return (o.last - a).magnitude; }
            return 0f;
        }

        float Width => layer.contentRect.width > 0f ? layer.contentRect.width : 1920f;
        float Height => layer.contentRect.height > 0f ? layer.contentRect.height : 1080f;

        Vector2 ToScreenDelta(Vector2 panelDelta)
        {
            float k = Screen.height / Mathf.Max(1f, layer.panel != null ? layer.panel.visualTree.layout.height : Height);
            return new Vector2(panelDelta.x, -panelDelta.y) * k;
        }

        void EndGesture(int pointerId, Vector2 p, bool released)
        {
            if (!gestures.TryGetValue(pointerId, out var g)) return;
            gestures.Remove(pointerId);
            if (gestureZone.HasPointerCapture(pointerId)) gestureZone.ReleasePointer(pointerId);
            if (frame.freeLook) { FreeLookDrag?.Invoke(Vector2.zero, false); return; }
            // MGame::maneuverTouchEnd 0x1a7eac: within 600 ms, more than w/480 * 70 sideways, less than h/320 * 90 up or down.
            if (!released || !g.dodge || g.dragging || frame.mode != Mode.Full) return;
            float dx = p.x - g.start.x;
            if (Time.unscaledTime - g.startTime > 0.6f || Mathf.Abs(dx) <= Width / 480f * 70f) return;
            if (Mathf.Abs(p.y - g.start.y) > Height / 320f * 90f) return;
            Dodge?.Invoke(dx < 0f ? 1 : 2);
        }

        /// <summary>Hud::releaseAllKeys: the stick back to the centre, fire and the buttons let go, the gestures dropped.</summary>
        public void ReleaseAll()
        {
            foreach (var e in new[] { stickArea, fire, gestureZone, camera, turret, menu, secondary, boost, pause })
                ReleaseCaptures(e);
            ReleaseStick();
            firePointer = -1;
            gestures.Clear();
            held.Clear();
        }

        /// <summary>Hud::releaseAllKeys and the autofire latch (a cutscene start, a planet jump, a level change).</summary>
        public void Reset()
        {
            ReleaseAll();
            autofire = false;
        }

        static void ReleaseCaptures(VisualElement e)
        {
            for (int id = 0; id < PointerId.maxPointers; id++)
                if (e.HasPointerCapture(id)) e.ReleasePointer(id);
        }

        /// <summary>PlayerEgo::throttleChanged 0xae874: the gauge's 2 s timer restarts (0 while hidden, into the hold when
        /// showing, mirrored while fading out).</summary>
        /// <summary>The throttle changed by keys, the wheel or a controller (FlightHud): the gauge shows as on touch.</summary>
        public void NotifyThrottle() => ThrottleChanged();

        void ThrottleChanged()
        {
            if (gaugeMs < 0f) gaugeMs = 0f;
            else if (gaugeMs > 1500f) gaugeMs = 2000f - gaugeMs;
            else if (gaugeMs > 500f) gaugeMs = 500f;
        }

        // ---- per frame ------------------------------------------------------------------------------------------

        public void Update(Frame f, float dtMs)
        {
            var was = frame.mode;
            frame = f;
            if (f.mode != Mode.Full && was == Mode.Full) ReleaseAll();
            if (f.mode != Mode.Full) autofire = false;
            Layout();
            Draw(dtMs);
        }

        void Layout()
        {
            float w = Width, h = Height;
            // Globals::setCoordsSteer: the left group from S.
            float S = Mathf.Clamp(S0, 300f, Mathf.Max(300f, h - 425f));
            stickCentre = new Vector2(165f, S + 277f);
            var boostPos = new Vector2(40f, S + 446f);
            if (boostPos.y > h - 113f)
            {
                float over = boostPos.y - (h - 113f);
                boostPos = new Vector2(266f + 80f * Mathf.Min(over / 54f, 1f), h - 113f);
            }
            // Globals::setCoordsFire: the right cluster from F.
            float F = Mathf.Clamp(F0, 300f, Mathf.Max(300f, h - 311f));
            var bg = new Vector2(w - 321f, F);
            bool left = h - 401f < F;
            if (frame.cursor)
            {
                // The PC cursor mode: the cluster's background drawn from the bottom-right corner (0x6aa, 397 x 406), the pill
                // and the boost along the bottom left.
                bg = new Vector2(w - 397f, h - 406f);
                left = false;
                boostPos = new Vector2(174f, h - 125f);
            }
            SetImage(cluster, left ? clusterLeft : clusterBottom);
            var firePos = bg + new Vector2(200f, 200f);

            Place(stickArea, stickCentre - new Vector2(StickHitX, StickHitY));
            Place(stickBase, new Vector2(20f, S + 132f));
            if (stickPointer < 0) Place(knob, stickCentre - new Vector2(90.5f, 90.5f));
            Place(cluster, bg);
            Place(fire, firePos - new Vector2(99f, 99f));
            Place(arrow, firePos - new Vector2(67f, 64f));
            Place(menu, bg + new Vector2(152f, 8f));
            Place(camera, bg + new Vector2(28f, 27f));
            Place(turret, bg + new Vector2(160f, -134f));
            Place(secondary, bg + (left ? new Vector2(-2f, 189f) : new Vector2(144f, 298f)));
            Place(boost, boostPos);
            Place(plate, new Vector2(w / 2f - 187f, h - 37f));
            if (navButtons != null)   // FF at (40, S - 180), autopilot (40, S); the cursor mode's at the bottom left
                Place(navButtons, frame.cursor ? new Vector2(32f, h - 309f) : new Vector2(32f, Mathf.Max(0f, S - 180f)));
            // The pause button sits in the HUD root, so it also shows while the safe area is hidden: follow the safe area.
            var lb = layer.worldBound;
            var hb = pauseHost.worldBound;
            if (!float.IsNaN(lb.x) && !float.IsNaN(hb.x)) Place(pause, new Vector2(lb.x - hb.x + w - 121f, lb.y - hb.y + 24f));
        }

        void Draw(float dtMs)
        {
            var f = frame;
            bool full = f.mode == Mode.Full;
            bool menuOpen = f.mode == Mode.MenuOpen;
            Show(pause, f.mode != Mode.Off);
            SetImage(pause, held.Contains(pause) ? pauseOn : pauseOff);

            // Hud::draw 4: the stick (also under a HUD menu); dim (alpha 50) in tilt, autopilot, approaches, hacking.
            bool stickShown = (full || menuOpen) && !f.cursor;
            Show(stickArea, full && !f.cursor);
            Show(stickBase, stickShown);
            Show(knob, stickShown);
            SetImage(knob, stickPointer >= 0 ? knobOn : knobOff);
            float stickAlpha = f.dimStick ? 50f / 255f : 1f;
            stickBase.style.opacity = stickAlpha;
            knob.style.opacity = stickAlpha;
            Show(gestureZone, full && !f.cursor);

            // With a HUD menu open everything after the stick is invisible (alpha 0).
            bool rest = full && !f.steeringMissile;
            Show(cluster, rest);
            Show(fire, rest);
            SetImage(fire, FireHeld ? fireOn : fireOff);
            Show(arrow, rest && f.actionArrow);
            Show(camera, rest && f.nextCamera >= 0);
            if (f.nextCamera >= 0 && f.nextCamera < 4 && camOff[f.nextCamera] != null)
                SetImage(camera, held.Contains(camera) ? camOn[f.nextCamera] : camOff[f.nextCamera]);
            Show(turret, rest && f.turret);
            SetImage(turret, f.turretOn || held.Contains(turret) ? turretOn : turretOff);
            Show(menu, rest && f.menu);
            SetImage(menu, held.Contains(menu) ? menuOn : menuOff);
            Show(secondary, full && f.secondary);
            SetImage(secondary, held.Contains(secondary) ? secOn : secOff);
            secondary.style.width = 109;   // 0x4bd is 77 px (only in the phone atlas): scaled to the button
            secondary.style.height = 109;
            bool plateShown = (full || f.gauge) && f.secondary && f.secondaryText != null;
            Show(plate, plateShown);
            if (plateShown && plateText.text != f.secondaryText) plateText.text = f.secondaryText;

            // Hud::draw 18: the boost's alpha shows the charge; a 2 s blink (one lit frame per 80 ms) once it is ready again.
            if (f.boostReady && !boostWasReady && f.hasBooster) boostFlashMs = 2000f;
            boostWasReady = f.boostReady;
            bool lit = false;
            if (boostFlashMs > 0f)
            {
                float before = boostFlashMs;
                boostFlashMs -= dtMs;
                lit = Mathf.FloorToInt(before / 80f) != Mathf.FloorToInt(Mathf.Max(0f, boostFlashMs) / 80f);
            }
            Show(boost, rest && f.hasBooster && !(f.mining && f.boostReady));
            float alpha = f.boosting ? 55f : f.boostReady ? 255f : 55f + (int)(75f * Mathf.Clamp01(f.boostRate));
            if (f.mining) alpha = 47f;
            boost.style.opacity = alpha / 255f;
            SetImage(boost, held.Contains(boost) || lit ? boostOn : boostOff);
            if (navButtons != null) navButtons.style.opacity = f.dimNav ? 47f / 255f : 1f;

            // PlayerEgo::drawThrottle: fade in 500 ms, hold, fade out 500 ms over 2 s.
            if (gaugeMs >= 0f)
            {
                gaugeMs += dtMs;
                if (gaugeMs > 2000f) gaugeMs = -1f;
            }
            bool gaugeShown = gaugeMs >= 0f && (full || f.gauge) && f.crosshairVisible && GetThrust != null;
            Show(gauge, gaugeShown);
            Show(gaugeText, gaugeShown);
            if (!gaugeShown) return;
            float a = Mathf.Min(1f, Mathf.Min(gaugeMs, 2000f - gaugeMs) / 500f);
            float thrust = Mathf.Clamp01(GetThrust());
            float fh = Mathf.Round(83f * thrust);
            Place(gauge, new Vector2(f.crosshair.x - 81f, f.crosshair.y + 83f - fh));
            gauge.style.height = fh;
            gaugeFill.style.top = fh - 83f;
            gauge.style.opacity = a;
            gaugeText.text = ((int)(thrust * 100f)).ToString();
            Place(gaugeText, new Vector2(f.crosshair.x - 61f, f.crosshair.y + 83f / 1.6f));
            gaugeText.style.opacity = a;
        }
    }
}
