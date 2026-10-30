// ModBuilderViewFrame.cs
// ModBuilderView: per frame and the pointer over the 3D view (part of the partial ModBuilderView; its fields are in ModBuilderView.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using GoF2Remake.Flight;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        void UpdateUiAnimation(bool flying)
        {
            float target = flying ? 0f : 1f;
            if (!uiStateTaken) { uiStateTaken = true; uiShown = target; ApplyUiAnimation(); return; }
            if (uiShown == target) return;
            uiShown = GoF2Remake.Data.Settings.ReduceMotion ? target : Mathf.MoveTowards(uiShown, target, Time.unscaledDeltaTime * 1000f / UiAnimMs);   // "Reduce motion": at once
            ApplyUiAnimation();
        }
        void ApplyUiAnimation()
        {
            bool away = uiShown <= 0f, still = uiShown >= 1f;
            root.EnableInClassList("mbv-root--flying", away);
            uiBlocker.style.display = still ? DisplayStyle.None : DisplayStyle.Flex;
            // Each part, its delay and where it goes (panel px).
            var parts = new (VisualElement el, float delay, Vector2 off)[]
            {
                (uiTop, 0f, new Vector2(0f, -90f)),
                (uiLeft, 50f, new Vector2(-160f, 0f)),
                (uiRight, 100f, new Vector2(200f, 0f)),
                (uiEngines, 125f, new Vector2(0f, -60f)),
                (testFly, 150f, new Vector2(220f, 0f)),
            };
            float ms = Mathf.Clamp01(uiShown) * UiAnimMs;
            foreach (var (el, delay, off) in parts)
            {
                if (el == null) continue;
                if (still) { el.style.translate = StyleKeyword.Null; el.style.opacity = StyleKeyword.Null; continue; }
                float p = Mathf.Clamp01((ms - delay) / UiPartMs);
                float r = (1f - p) * (1f - p) * (1f - p);   // 1 - easeOutCubic: the share of the way still to go
                el.style.translate = new Translate(off.x * r, off.y * r);
                el.style.opacity = 1f - r;
            }
        }
        void ApplyEngines()
        {
            bool on = engineLevel > 0.001f;
            float throttle = Mathf.Clamp01(engineLevel / BoostFrom);
            float boost = Mathf.Clamp01((engineLevel - BoostFrom) / (1f - BoostFrom));
            engineValue.text = !on ? T("mbEnginesOff", "Off").ToUpperInvariant()
                             : boost > 0f ? T("mbEnginesBoost", "Boost").ToUpperInvariant()
                             : $"{Mathf.RoundToInt(throttle * 100f)} %";
            engineValue.EnableInClassList("mbv-engines-value--boost", boost > 0f);
            var asm = boundTransform != null ? boundTransform.GetComponent<Visuals.AssembledObject>() : null;
            if (asm != null) asm.SetExhaust(on, HangarFlight.UsesPlayerEngine(asm));
            // The exhaust particles follow the glow but come per distance flown: standing on the pad they need the
            // stationary rate.
            if (boundTransform != null)
                foreach (var e in boundTransform.GetComponentsInChildren<Flight.ShipExhaust>(true))
                {
                    e.stationary = on ? throttle : 0f;
                    e.stationaryBoost = boost;
                }
        }
        /// <summary>The turntable shows the current ship: rebuilt, the gizmo on it, the camera orbiting it.</summary>
        void BindShip()
        {
            int index = Session.ShipIndex;
            level.ReplacePlayerShip(index);
            boundShip = index;
            boundTransform = level.PlayerShip;
            if (boundTransform == null)
            {
                gizmo.Bind(null, null);
                Status(T("mbNoPreview", "The ship couldn't be built for the preview: check its model."), true);
                ShowProblems();
                return;
            }
            var s = Current;
            gizmo.Bind(boundTransform, EditedMounts ?? new List<WeaponMount>());
            if (InHangar && ShipKind(kind)) level.BeginInspect(0.55f);
            ApplyEngines();   // the rebuilt ship comes with its engines off
            // A new ship's first hardpoints (just imported, or never given any).
            // Not on a game model: it came with that ship's own hardpoints (or, a model that is no ship's, none on purpose).
            if (s != null && !s.IsOverride && !s.HasGameModel && !s.IsKitShip && (s.NeedsLayout || s.Mounts.Count == 0) && ModBuilder.IndexOf(s) == index) AutoLayout(s);
            else if (autoHeightAfterReload && s != null && ModBuilder.IndexOf(s) == index)
            {
                autoHeightAfterReload = false;
                int before = s.HangarHeight;
                AutoHangarHeight();
                if (s.HangarHeight == before) reloadPending = false;   // nothing moved: no second rebuild
                BuildShipPage();
            }
            RefreshPoints();
            MaybeMakeIcon(s, index);
        }
        /// <summary>The station rebuilt the turntable ship by itself (a customizable ship's parts arrived, ModShips.ModelsChanged):
        /// the gizmo and the engines on the new one.</summary>
        void RebindShip()
        {
            boundTransform = level.PlayerShip;
            gizmo.Bind(boundTransform, EditedMounts ?? new List<WeaponMount>());
            ApplyEngines();
            MaybeMakeIcon(Current, Session.ShipIndex);
        }
        /// <summary>Whether the builder draws this ship's shop icon from its model: a new ship with a model file or a kit
        /// (a game model has the game's icon, a changed game ship keeps its own).</summary>
        static bool MakesIcon(ProjectShip s) => s != null && !s.IsOverride && !s.HasGameModel && (!string.IsNullOrEmpty(s.Model) || s.IsKitShip);
        /// <summary>The Ship tab's shop icon: what it is now, and Recreate icon to draw it again from the model (also over an
        /// icon the modder set by hand, which ships.json keeps until then; Undo puts it back).</summary>
        void BuildShipIcon(ProjectShip s)
        {
            if (!MakesIcon(s)) return;
            int index = ModBuilder.IndexOf(s);
            Heading(ship, T("mbShopIcon", "Shop icon"));
            var row = Add(ship, new VisualElement(), "mbv-row");
            var image = Add(row, new VisualElement(), "mbv-ship-icon");
            var tex = index >= 0 ? ItemInfo.ShipIcon(index) : null;
            if (tex != null) image.style.backgroundImage = new StyleBackground(tex);
            void Redraw()
            {
                if (index < 0 || ModBuilder.IndexOf(s) != Session.ShipIndex || !MaybeMakeIcon(s, index, true))
                    Status(T("mbIconWait", "The icon can be drawn once the ship's model is on the turntable."), true);
                else BuildShipPage();
            }
            Act(row, T("mbRecreateIcon", "Recreate icon"), Redraw);
            // A model whose nose comes out elsewhere: the icon's own turn in quarter turns, drawn again at once.
            Act(row, "−90°", () => { Project.SetIconTurn(s, Project.IconTurn(s) - 90); Redraw(); });
            Act(row, "+90°", () => { Project.SetIconTurn(s, Project.IconTurn(s) + 90); Redraw(); });
            int turn = Project.IconTurn(s);
            if (turn != 0) Add(row, new Label($"{turn}°"), "mbv-inline-label");
            var made = Project.GeneratedIcon(s);
            Note(ship, !string.IsNullOrEmpty(s.Icon) && (made == null || made.Value.file != s.Icon)
                ? T("mbIconOwn", "Your own icon: Recreate icon draws one from the model instead.")
                : T("mbIconMade", "Drawn from the model, nose to the bottom left, whenever the model, its turn or its textures change."));
        }
        /// <summary>A new ship's shop icon, rendered from its model once (ShipIconMaker) into icons/&lt;id&gt;.png: when it has
        /// none, or the builder's own no longer matches its look (another model file, a turn, other hull textures). An icon
        /// the modder set by hand is left alone, unless 'force' (Recreate icon) draws it again whatever it is. True when an
        /// icon was drawn.</summary>
        bool MaybeMakeIcon(ProjectShip s, int index, bool force = false)
        {
            if (!MakesIcon(s) || ModBuilder.IndexOf(s) != index) return false;
            if (s.IsKitShip && kind == ModBuilder.Kind.Kits) return false;   // a preview build, not the default the icon shows
            var made = Project.GeneratedIcon(s);
            string key = IconKey(s);
            if (!force)
            {
                if (!string.IsNullOrEmpty(s.Icon) && (made == null || made.Value.file != s.Icon)) return false;   // the modder's own
                if (made != null && made.Value.key == key && System.IO.File.Exists(System.IO.Path.Combine(Project.Folder, made.Value.file))) return false;
            }
            var db = Database.Shared;
            var entry = db.ShipAssembly(index);
            if (entry == null || !ModShips.Built.Any(b => b.ship == index)) return false;   // not built: the Phantom would stand in
            var icon = ShipIconMaker.Make(ModShips.Template(entry.name), Project.IconTurn(s));
            if (icon == null) return false;
            ModBuilder.History?.Commit();   // the edits so far are their own step: the icon joins it, not a step of its own
            Project.SetGeneratedIcon(s, icon.EncodeToPNG(), key);
            Project.Save();
            ModBuilder.History?.Absorb();
            ModShips.SetIcon(index, icon);
            RebuildShips();
            return true;
        }
        /// <summary>What the icon is drawn from: the model file, its turn and the hull textures. The leading version goes up
        /// when ShipIconMaker draws differently, so the icons it drew before are drawn again (2: wide hulls no longer turned).</summary>
        string IconKey(ProjectShip s)
        {
            if (s.IsKitShip)
            {
                // A kit ship: its default build, the kit's parts and their files.
                var kit = Project.FindKit(s.Kit);
                var sb = new System.Text.StringBuilder("kit2|" + Project.IconTurn(s) + "|" + s.Kit + "|" + s.KitType + "|" + s.BuildText());
                if (kit != null)
                {
                    kit.Flush();
                    sb.Append('|').Append(kit.Scale).Append('|').Append(kit.Yaw);
                    foreach (var p in kit.Parts)
                    {
                        var fi = new System.IO.FileInfo(System.IO.Path.Combine(Project.Folder, p.Model ?? ""));
                        sb.Append('|').Append(p.Id).Append('@').Append(fi.Exists ? fi.Length + ":" + fi.LastWriteTimeUtc.Ticks : "missing");
                    }
                    sb.Append('|').Append(kit.Json["colorSlots"]?.ToString(Newtonsoft.Json.Formatting.None));
                }
                return sb.ToString();
            }
            var file = new System.IO.FileInfo(System.IO.Path.Combine(Project.Folder, s.Model));
            string stamp = file.Exists ? file.Length + "@" + file.LastWriteTimeUtc.Ticks : "missing";
            var materials = s.ToJson()["materials"];
            return $"2|{Project.IconTurn(s)}|{s.Model}|{stamp}|{s.ModelYaw}|{s.ModelPitch}|{s.ModelRoll}|{materials?.ToString(Newtonsoft.Json.Formatting.None)}";
        }
        void ReadKeys(float dt)
        {
            if (ReadCommonKeys(EscapeInKind)) return;
            var kb = Keyboard.current;
            if (EditsMounts && (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) DeleteSelected();
            if (EditsMounts && kb.mKey.wasPressedThisFrame) Mirror();
            if (!InHangar || !ShipKind(kind)) return;   // the Space Lounge, the map, a room: no ship to fly or turn around
            if (kb.lKey.wasPressedThisFrame && Flies(kind)) StartTestFlight();
            float frames = dt / OriginalFrame.Ms;
            float left = kb.leftArrowKey.isPressed || kb.aKey.isPressed ? 1f : 0f, right = kb.rightArrowKey.isPressed || kb.dKey.isPressed ? 1f : 0f;
            float up = kb.upArrowKey.isPressed || kb.wKey.isPressed ? 1f : 0f, dn = kb.downArrowKey.isPressed || kb.sKey.isPressed ? 1f : 0f;
            var d = new Vector2(left - right, up - dn) * 4f * frames;
            if (d != Vector2.zero) TurnOrbit(d);
            float zoom = (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed ? 1f : 0f) - (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed ? 1f : 0f);
            if (zoom != 0f) level.Inspect.Zoom(Mathf.Pow(1.03f, zoom * frames));
        }
        /// <summary>Esc in the screen: a hardpoint being placed or selected, then the kind's own (a marker being placed).</summary>
        bool EscapeInKind()
        {
            if (placing >= 0) { placing = -1; BuildHardpointsPage(); return true; }
            if (gizmo.Selected >= 0) { gizmo.Selected = -1; gizmo.Refresh(); BuildHardpointsPage(); return true; }
            return EscapeKind();
        }
        /// <summary>The orbit turned by 'pixels' of drag (PhotoMode's 0.005 rad per pixel), the camera views' own inversion
        /// applied (#121).</summary>
        void TurnOrbit(Vector2 pixels)
        {
            var d = Settings.OrbitTurn(pixels);
            level.Inspect.Turn(d.x * 0.005f, d.y * 0.005f);
        }
        /// <summary>The orbit camera looking at the ship from 'angle' (its own frame), the distance kept, the pan centred.</summary>
        void ShowAngle(string angle)
        {
            if (boundTransform == null || !level.Inspect.Active) return;
            var from = ModelViewer.Angles.First(a => a.name == angle).from;
            // Top: from a little behind, so the orbit's turn puts the nose at the top of the screen (straight down left the
            // turn to whatever the overhead direction gave: the ship lay at an angle).
            if (angle == "top") from = new Vector3(0f, 1f, -0.1f);
            pendingTurn = fling = Vector2.zero;
            level.Inspect.ResetPan();
            level.Inspect.SetView(boundTransform.TransformDirection(from.normalized), 0f);
        }
        /// <summary>The orbit back around the ship's centre (the pan undone), the angle and distance kept.</summary>
        void CentreOrbit()
        {
            if (!level.Inspect.Active) return;
            pendingTurn = fling = Vector2.zero;
            level.Inspect.ResetPan();
        }
        void UpdateCamera(float dt)
        {
            if (!InHangar || !ShipKind(kind)) return;
            if (!level.Inspect.Active && boundTransform != null && !level.PlayerFlying) level.BeginInspect(0.55f);
            // Touch: two fingers pinch to zoom and move the view (the middle button's pan; TwoFingerTouch).
            if (touch2.Read(out float zoom, out var pan))
            {
                if (zoom != 1f) level.Inspect.Zoom(zoom);
                if (pan != Vector2.zero) level.Inspect.Pan(pan);
                if (drag == Drag.Camera) drag = Drag.None;
                pendingTurn = fling = Vector2.zero;
                return;
            }
            if (pendingTurn != Vector2.zero)
            {
                var step = pendingTurn * (1f - Mathf.Exp(-dt / OrbitEaseMs));
                if (pendingTurn.sqrMagnitude < 0.01f) step = pendingTurn;
                pendingTurn -= step;
                TurnOrbit(step);
            }
            if (drag == Drag.None && fling != Vector2.zero)
            {
                float frames = dt / OriginalFrame.Ms;
                TurnOrbit(fling * frames);
                fling *= Mathf.Pow(0.9f, frames);
                if (fling.magnitude <= 0.5f) fling = Vector2.zero;
            }
            if (drag == Drag.None && Pointer.current != null && !ModalOpen && EditsMounts) gizmo.Hover(Pointer.current.position.ReadValue());
        }
        /// <summary>A short tag by every hardpoint ("P1", "E2"...), where it shows on screen.</summary>
        void UpdateLabels()
        {
            var s = EditedMounts;
            var cam = level.MainCamera;
            int n = s != null && boundTransform != null && cam != null && EditsMounts ? s.Count : 0;
            while (labels.childCount < n) Add(labels, new Label { pickingMode = PickingMode.Ignore }, "mbv-point-tag");
            while (labels.childCount > n) labels.RemoveAt(labels.childCount - 1);
            if (n == 0) return;
            float w = root.layout.width, h = root.layout.height;
            if (float.IsNaN(w) || w <= 0f) return;
            var counts = new int[4];
            for (int i = 0; i < n; i++)
            {
                var m = s[i];
                int type = Mathf.Clamp(m.slotType, 0, 3);
                counts[type]++;
                var tag = (Label)labels[i];
                var sp = cam.WorldToScreenPoint(gizmo.WorldOf(m));
                if (sp.z <= 0f) { tag.style.display = DisplayStyle.None; continue; }
                tag.style.display = DisplayStyle.Flex;
                tag.text = TypeShort[type] + counts[type];
                // translate, not left / top: moving a tag every frame of an orbit then needs no layout pass
                tag.style.translate = new Translate(sp.x / Screen.width * w + 10f, (Screen.height - sp.y) / Screen.height * h - 24f);
                tag.style.color = ModBuilderGizmo.TypeColours[type];
                tag.EnableInClassList("mbv-point-tag--on", i == gizmo.Selected);
            }
        }
        // ---- the pointer over the 3D view -----------------------------------------------------------------------
        static Vector2 ScreenPos() => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
        void OnPointerDown(PointerDownEvent e)
        {
            if (ModalOpen) return;
            if (KindPointerDown(e)) return;   // the kinds with a view of their own (a room)
            if (!InHangar || !ShipKind(kind)) return;
            // While the preview rebuilds (reloading) the camera still turns and pans; only the hardpoints wait for the ship.
            // (It refused every drag then, and a rebuild that never finished left the camera stuck.)
            bool shipReady = boundTransform != null && !reloading;
            if (!shipReady && !level.Inspect.Active) return;
            var p = ScreenPos();
            downAt = last = p;
            travel = 0f;
            fling = Vector2.zero;
            viewport.CapturePointer(e.pointerId);
            if (e.button == 2) { drag = Drag.Pan; return; }   // the middle button pans the view (a click centres it again)
            if (!EditsMounts || EditedMounts == null || !shipReady) { drag = Drag.Camera; return; }   // the hardpoints: a ship's, a kit part's
            if (e.button == 0 && placing >= 0)
            {
                if (gizmo.RaycastHull(p, out var hit, out var normal)) { int type = placing; placing = -1; AddMount(type, hit, normal); }
                else Status(T("mbMissedHull", "Click on the ship's hull."), false);
                drag = Drag.None;
                return;
            }
            if (e.button == 0 && gizmo.BeginDrag(p))
            {
                drag = Drag.Gizmo;
                gizmo.Refresh();
                RefreshPoints();
                if (kind == ModBuilder.Kind.Ships && tab != 1) SelectTab(1);
                return;
            }
            drag = Drag.Camera;
        }
        void OnPointerMove(PointerMoveEvent e)
        {
            if (KindPointerMove(e)) return;
            if (drag == Drag.None || !viewport.HasPointerCapture(e.pointerId)) return;
            if (!UiPointerButtons.AnyHeld(e)) { viewport.ReleasePointer(e.pointerId); EndDrag(); return; }   // released where we didn't hear it
            var p = ScreenPos();
            float scale = 1080f / Mathf.Max(1, Screen.height);
            var delta = (p - last) * scale;
            delta.y = -delta.y;   // a drag up takes the camera down (the ship tips up with the finger), as in Inspect ship
            travel += delta.magnitude;
            last = p;
            if (drag == Drag.Pan)
            {
                level.Inspect.Pan(new Vector2(delta.x, -delta.y));   // the screen's own up (delta's y is the orbit's, flipped)
                return;
            }
            if (drag == Drag.Gizmo)
            {
                var kb = Keyboard.current;
                gizmo.Drag(p, kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed));
                MarkDirty();
            }
            else
            {
                pendingTurn += delta;   // applied frame by frame (UpdateCamera)
                fling = delta;
            }
        }
        void OnPointerUp(PointerUpEvent e)
        {
            if (KindPointerUp(e)) return;
            if (!viewport.HasPointerCapture(e.pointerId)) return;
            viewport.ReleasePointer(e.pointerId);
            EndDrag();
        }
        void EndDrag()
        {
            if (drag == Drag.Gizmo)
            {
                gizmo.EndDrag();
                var s = EditedMounts;
                int i = gizmo.Selected;
                if (s != null && i >= 0 && i < s.Count) OnMountEdited(s[i].slotType);
                RefreshPoints();
            }
            else if (drag == Drag.Pan)
            {
                if (travel < 6f) level.Inspect.ResetPan();   // a middle click: back on the ship's centre
            }
            else if (drag == Drag.Camera)
            {
                if (fling.magnitude <= 3f) fling = Vector2.zero;
                // A click (no drag) on empty space: nothing selected.
                if (travel < 6f && gizmo.Selected >= 0) { fling = Vector2.zero; gizmo.Selected = -1; gizmo.Refresh(); BuildHardpointsPage(); }
            }
            drag = Drag.None;
        }
        /// <summary>The scene goes (StationMenu's OnDestroy): the gizmo with it.</summary>
        public void Dispose()
        {
            if (gizmo != null) UnityEngine.Object.Destroy(gizmo.gameObject);
            DisposeKinds();
            DisposeAi();
            SaveNow();
        }
    }
}
