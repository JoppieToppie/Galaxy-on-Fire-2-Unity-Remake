// ItemInfoWindow.cs
// The full-screen item / ship details (ListItemWindow: set 0x159468, draw 0x15b620, update 0x15bee8, render 0x15c054,
// OnTouch* 0x15c100..; Reference/research/shop.md 2.4). Opened by the hangar list's info button on the selected row
// (HangarWindow::OnTouchEnd case 0: sound 97 Button_Info) and by the lounge's "Let me see it" (776, no price).
//   header 390 "Info", help 643; a 1300 x 800 box (full screen on phones) over the dimmed hangar
//   left   the name box (icon + 1274 + item / 913 + ship), the stat rows (label left, value right)
//   ships  every row but the price with a comparison arrow against the current ship: 0x512 worse, 0x513 better, 0x514
//          equal; the right half the 3D ship (Globals::getShipGroup(idx, race, true): the player variant) in a 634 x 318
//          box over the floor glow 0x50b (drawn once and mirrored), its own camera (0.92 rad, (1362, 1690, -5257) at
//          1920 x 1080, turned (pi/8, pi, -0.1)), light (-5, 1, -5), then 280 "Description" and 977 + ship
//   items  280 "Description", 1041 + item and the known price range (ItemInfo.ItemText); 132 Price only with showPrice
//   turning yaw only, 120 px = 1 rad, starting at 260 px; a drag starts on the right half above the 3D box's bottom; a
//          release over 3 px keeps turning x0.9 per frame, stops at 1 px
//   close  the footer's Back (170)
// Remake input: mouse / touch drag, A / D or the right stick turn; Esc / B back. Plain class owned by StationMenu.

using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class ItemInfoWindow
    {
        const int PreviewLayer = 30;
        const float M = 0.05f, FrameMs = 1000f / 30f, PixelsPerRadian = 120f;
        static readonly Vector3 StagePosition = new Vector3(0f, -20000f, 0f);

        readonly StationMenu menu;
        readonly VisualElement backdrop, icon, stats, preview, previewImage, floorLeft, floorRight;
        readonly Label header, help, nameLabel, subLabel, descTitle, text;
        readonly ScrollView textScroll;

        GameObject stage;
        Transform model;
        Camera cam;
        RenderTexture rt;
        float yawPx, flingPx, lastX;
        bool dragging;

        public bool IsOpen { get; private set; }

        static string T(int id) => Localization.Get(id);
        static Texture2D Hud(string n) => Resources.Load<Texture2D>("GoF2Hud/" + n);

        public ItemInfoWindow(StationMenu owner, VisualElement root)
        {
            menu = owner;
            backdrop = new VisualElement();
            backdrop.AddToClassList("info-backdrop");
            var box = new VisualElement();
            box.AddToClassList("info-box");
            header = new Label { pickingMode = PickingMode.Ignore };
            header.AddToClassList("info-header");
            header.AddToClassList("gof-semibold");
            help = new Label { pickingMode = PickingMode.Ignore };
            help.AddToClassList("info-help");
            var columns = new VisualElement();
            columns.AddToClassList("info-columns");

            var left = new VisualElement();
            left.AddToClassList("info-left");
            var nameBox = new VisualElement();
            nameBox.AddToClassList("info-name-box");
            icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("info-icon");
            var names = new VisualElement { pickingMode = PickingMode.Ignore };
            nameLabel = new Label { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("info-name");
            nameLabel.AddToClassList("gof-semibold");
            subLabel = new Label { pickingMode = PickingMode.Ignore };
            subLabel.AddToClassList("info-sub");
            names.Add(nameLabel);
            names.Add(subLabel);
            nameBox.Add(icon);
            nameBox.Add(names);
            stats = new ScrollView();
            stats.AddToClassList("info-stats");
            left.Add(nameBox);
            left.Add(stats);

            var right = new VisualElement();
            right.AddToClassList("info-right");
            preview = new VisualElement();
            preview.AddToClassList("info-preview");
            floorLeft = new VisualElement { pickingMode = PickingMode.Ignore };
            floorLeft.AddToClassList("info-floor");
            floorRight = new VisualElement { pickingMode = PickingMode.Ignore };
            floorRight.AddToClassList("info-floor");
            floorRight.AddToClassList("info-floor--mirrored");
            var floor = Hud("info_floor");
            if (floor != null) { floorLeft.style.backgroundImage = new StyleBackground(floor); floorRight.style.backgroundImage = new StyleBackground(floor); }
            previewImage = new VisualElement { pickingMode = PickingMode.Ignore };
            previewImage.AddToClassList("info-preview-image");
            preview.Add(floorLeft);
            preview.Add(floorRight);
            preview.Add(previewImage);
            descTitle = new Label { pickingMode = PickingMode.Ignore };
            descTitle.AddToClassList("info-desc-title");
            descTitle.AddToClassList("gof-semibold");
            textScroll = new ScrollView();
            textScroll.AddToClassList("info-text-scroll");
            text = new Label();
            text.AddToClassList("info-text");
            textScroll.Add(text);
            right.Add(preview);
            right.Add(descTitle);
            right.Add(textScroll);
            columns.Add(left);
            columns.Add(right);

            var footer = new VisualElement();
            footer.AddToClassList("info-footer");
            var back = new Button(() => { menu.PlayRelease(); Close(); }) { focusable = false };
            back.AddToClassList("info-back");
            back.AddToClassList("gof-semibold");
            back.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            footer.Add(back);
            box.Add(header);
            box.Add(help);
            box.Add(columns);
            box.Add(footer);
            backdrop.Add(box);
            root.Add(backdrop);

            preview.RegisterCallback<PointerDownEvent>(e => { dragging = true; lastX = e.position.x; flingPx = 0f; preview.CapturePointer(e.pointerId); });
            preview.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                float dx = e.position.x - lastX;
                lastX = e.position.x;
                // The ship follows the finger: dragging right turns its near side right.
                yawPx -= dx;
                flingPx = -dx;
            });
            preview.RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                if (preview.HasPointerCapture(e.pointerId)) preview.ReleasePointer(e.pointerId);
                if (Mathf.Abs(flingPx) <= 3f) flingPx = 0f;
            });
            back.text = T(170).ToUpperInvariant();
        }

        // ---- content --------------------------------------------------------------------------------------

        void Begin()
        {
            header.text = T(390).ToUpperInvariant();
            help.text = T(643);
            descTitle.text = T(280).ToUpperInvariant();
            stats.Clear();
            textScroll.scrollOffset = Vector2.zero;
            IsOpen = true;
            backdrop.AddToClassList("info-backdrop--shown");
        }

        void Row(string label, string value, int compare = 2)
        {
            var r = new VisualElement();
            r.AddToClassList("info-row");
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("info-row-label");
            var v = new Label(value) { pickingMode = PickingMode.Ignore };
            v.AddToClassList("info-row-value");
            v.AddToClassList("gof-semibold");
            r.Add(l);
            r.Add(v);
            if (compare != 2)
            {
                var a = new VisualElement { pickingMode = PickingMode.Ignore };
                a.AddToClassList("info-row-arrow");
                var tex = Hud(compare < 0 ? "compare_worse" : compare > 0 ? "compare_better" : "compare_equal");
                if (tex != null) a.style.backgroundImage = new StyleBackground(tex);
                r.Add(a);
            }
            stats.Add(r);
        }

        /// <summary>ListItemWindow::set(item): the attribute table, the price (showPrice) and the description with the
        /// known price range.</summary>
        public void ShowItem(Database db, int item, int currentSystem, bool showPrice, int price)
        {
            var it = db.Item(item);
            if (it == null) return;
            Begin();
            HidePreview();
            icon.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item));
            nameLabel.text = ItemInfo.ItemName(item);
            subLabel.text = $"{ItemInfo.Category(it)}  ·  {T(133)} {it.techLevel}";
            foreach (var (label, value) in ItemInfo.ItemStats(it)) Row(label, value);
            if (showPrice && price > 0) Row(T(132), ItemInfo.Credits(price));
            text.text = ItemInfo.ItemText(db, it, currentSystem);
        }

        /// <summary>ListItemWindow::set(ship): the ship rows with comparison arrows, the 3D model, the description.</summary>
        public void ShowShip(Database db, int ship, int price, bool showPrice = true)
        {
            var s = db.Ship(ship);
            if (s == null) return;
            Begin();
            icon.style.backgroundImage = new StyleBackground(ItemInfo.ShipIcon(ship));
            nameLabel.text = ItemInfo.ShipName(ship);
            subLabel.text = ItemInfo.ShipRaceText(ship);
            var cur = db.Ship(Session.ShipIndex);
            int Cmp(float v, float c) => cur == null ? 2 : v < c ? -1 : v > c ? 1 : 0;
            bool mine = ship == Session.ShipIndex;
            string Plus(int mod) => mine && Session.HasMod(mod) ? " (+)" : "";
            Row(T(165), (s.armor + (mine && Session.HasMod(0) ? 40 : 0)) + Plus(0), Cmp(s.armor, cur?.armor ?? 0));
            Row(T(166), $"{s.cargo + (mine && Session.HasMod(1) ? 30 : 0)} t" + Plus(1), Cmp(s.cargo, cur?.cargo ?? 0));
            Row(T(265), s.slots.primary.ToString(), Cmp(s.slots.primary, cur?.slots.primary ?? 0));
            Row(T(266), s.slots.secondary.ToString(), Cmp(s.slots.secondary, cur?.slots.secondary ?? 0));
            Row(T(267), s.slots.turret.ToString(), Cmp(s.slots.turret, cur?.slots.turret ?? 0));
            Row(T(269), (s.slots.equipment + (mine && Session.HasMod(2) ? 1 : 0)) + Plus(2), Cmp(s.slots.equipment, cur?.slots.equipment ?? 0));
            Row(T(164), (Mathf.RoundToInt(s.handling) + (mine && Session.HasMod(3) ? 20 : 0)) + Plus(3), Cmp(s.handling, cur?.handling ?? 0));
            if (showPrice) Row(T(132), ItemInfo.Credits(price));
            text.text = ItemInfo.ShipDescription(ship);
            ShowPreview(db, ship, Mathf.Max(0, Shop.RaceOfShip(ship)));   // the model by the original's race (freighters)
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            backdrop.RemoveFromClassList("info-backdrop--shown");
            HidePreview();
        }

        // ---- 3D preview (its own camera on layer 30, rendered into the box) ---------------------------------------

        void ShowPreview(Database db, int ship, int race)
        {
            HidePreview();
            preview.style.display = DisplayStyle.Flex;
            var entry = db.ShipAssembly(ship);
            var prefab = entry != null ? AssembledObject.LoadPrefab(entry) : null;
            if (prefab == null) return;
            stage = new GameObject("Info window stage");
            stage.transform.position = StagePosition;
            var go = Object.Instantiate(prefab, stage.transform, false);
            go.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
            model = go.transform;
            // The camera: (1362, 1690, -5257) game units at 1920 x 1080 -> Unity (x, y, -z) * 0.05, looking at the ship.
            var camGo = new GameObject("Info window camera");
            camGo.transform.SetParent(stage.transform, false);
            camGo.transform.localPosition = new Vector3(1362f, 1690f, 5257f) * M;
            cam = camGo.AddComponent<Camera>();
            cam.cullingMask = 1 << PreviewLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 0.9203f * Mathf.Rad2Deg;
            cam.nearClipPlane = 200f * M;
            cam.farClipPlane = 30000f * M;
            var bounds = new Bounds(stage.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            // Remake: the original's direction, at the distance that frames this ship (its offset was made for the
            // original's model scale, the remake's ships sat tiny in the box).
            float radius = Mathf.Max(bounds.extents.magnitude, 1f);
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.9f;
            camGo.transform.position = bounds.center + camGo.transform.localPosition.normalized * dist;
            cam.farClipPlane = dist + radius * 4f;
            cam.nearClipPlane = Mathf.Max(0.1f, dist - radius * 2f);
            camGo.transform.LookAt(bounds.center);
            camGo.transform.Rotate(0f, 0f, -0.1f * Mathf.Rad2Deg, Space.Self);
            rt = new RenderTexture(1268, 636, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var lightGo = new GameObject("Info window light");
            lightGo.transform.SetParent(stage.transform, false);
            lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(-5f, -1f, -5f) * -1f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << PreviewLayer;
            light.intensity = 1f;
            previewImage.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            yawPx = 260f;   // ListItemWindow: starts at 260 px
            flingPx = 0f;
            ApplyYaw();
        }

        void HidePreview()
        {
            preview.style.display = DisplayStyle.None;
            previewImage.style.backgroundImage = StyleKeyword.None;
            if (stage != null) Object.Destroy(stage);
            stage = null;
            model = null;
            cam = null;
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
        }

        void ApplyYaw()
        {
            if (model != null) model.localRotation = Quaternion.Euler(0f, yawPx / PixelsPerRadian * Mathf.Rad2Deg, 0f);
        }

        /// <summary>Every frame while open: the fling and the keys / stick turning.</summary>
        public void Tick()
        {
            if (!IsOpen || model == null) return;
            float frames = Time.unscaledDeltaTime * 1000f / FrameMs;
            if (!dragging && flingPx != 0f)
            {
                yawPx += flingPx * frames;
                flingPx *= Mathf.Pow(0.9f, frames);
                if (Mathf.Abs(flingPx) <= 1f) flingPx = 0f;
            }
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            float turn = 0f;
            if (kb != null) turn += (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            if (pad != null) turn += pad.rightStick.ReadValue().x + pad.leftStick.ReadValue().x;
            yawPx -= Mathf.Clamp(turn, -1f, 1f) * 8f * frames;   // like dragging that way
            ApplyYaw();
        }
    }
}
