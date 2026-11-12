// GameControls.cs
// The flight controls as one InputActionMap made in code, rebindable (remake: Options > Controls, OptionKind.Binding).
// Defaults: the PC version's keys (Galaxy on Fire 2 Full HD, see CLAUDE.md "UI and platforms") and the remake's controller
// buttons. Every row has three slots: two keyboard / mouse bindings and one controller binding (Steer's controller slot is a
// whole stick or the D-pad, so the stick keeps its radial dead zone). The player's changes are binding overrides kept in
// PlayerPrefs ("controls_bindings", by action name and binding index: the Input System's own override JSON finds bindings
// by their id, which code-made bindings get new every launch, so its saved overrides never applied after a restart); an
// empty override unbinds a slot. Menus keep their fixed keys (arrows, Enter, Esc,
// controller A / B / Menu) so no binding can lock the player out; the pause key (Esc / Menu) isn't rebindable either.
// A key or button can be bound to several controls at once (the controller has too few buttons for one each): a rebind
// never takes it from another row.
// Remake: two key layouts, one per flight style (Settings.FlightStyle): the original's defaults above, and the free flight's
// (FreeFlightDefaults: EVERSPACE 2's layout, W / S thrust, A / D strafe, Space / L-Ctrl up / down, Q / E roll, L-Shift
// boost, the mouse aims and fires, the menus moved off Q / E). Each layout keeps its own overrides ("controls_bindings" /
// "controls_bindings_free"); switching the style reloads them (ApplyStyle). Rows only one style reads are hidden in the
// other's bindings list (ControlRow.visible).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GoF2Remake.Flight
{
    public enum BindSlot { Key1, Key2, Pad }

    /// <summary>One line of the key bindings list: an action (or a composite's parts) and its three slots.</summary>
    public sealed class ControlRow
    {
        public string id;
        public Func<string> label;
        public InputAction action;
        /// <summary>Composite rows: one prompt label per part, in the order the slots list them; null = one binding.</summary>
        public Func<string>[] partLabels;
        /// <summary>Binding indices per slot (a composite slot: its parts); null = no such slot.</summary>
        public readonly int[][] slots = new int[3][];
        /// <summary>The control type the controller slot takes ("Button" / "Vector2").</summary>
        public string padType = "Button";
        /// <summary>Shown in the bindings list (null = always): rows only one flight style reads.</summary>
        public Func<bool> visible;

        public bool HasSlot(BindSlot s) => slots[(int)s] != null;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameControls
    {
        const string PrefsKey = "controls_bindings";
        const string KeyGroup = "Keyboard", PadGroup = "Gamepad";

        public static readonly InputActionMap Map = new InputActionMap("Flight");
        static readonly List<ControlRow> rows = new List<ControlRow>();
        public static IReadOnlyList<ControlRow> Rows => rows;

        /// <summary>A binding changed (a rebind, a reset), or the controller's button names (InputMode.StyleChanged): hints
        /// and option rows show the new keys.</summary>
        public static event Action Changed;

        public static readonly InputAction Steer, Throttle, Brake, Boost, LevelOut, Roll, StrafeLeft, StrafeRight, DodgeLeft, DodgeRight, Drill,
            FirePrimary, FireSecondary, SwitchSecondary, UnlockTarget, Action, AutopilotMenu, ActionsMenu, Wingmen, KhadorDrive, FastForward,
            Camera, CockpitView, AutoTurret, Cloak, TimeExtender, MouseSteering, Chat, ChatSend, ChatChannel, Screenshot,
            HudToggle, MultiplayerWindow, DistressCall,
            Thrust, StrafeAxis, Hover, Dampeners, FreeLookHold,
            Device1, Device2, Device3,
            Headlights;

        static string X(string key, string english) => Localization.Extra(key, english);

        static GameControls()
        {
            // ---- flying
            Steer = Composite("steer", () => X("ctlSteer", "Steer"), InputActionType.Value, "Vector2", "2DVector",
                new[] { "Up", "Down", "Left", "Right" },
                new Func<string>[] { () => X("ctlUp", "up"), () => X("ctlDown", "down"), () => X("ctlLeft", "left"), () => X("ctlRight", "right") },
                new[] { "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow" },
                padSingle: "<Gamepad>/leftStick", padType: "Vector2");
            Throttle = Composite("throttle", () => X("ctlThrottle", "Throttle"), InputActionType.Value, "Axis", "1DAxis",
                new[] { "Positive", "Negative" },
                new Func<string>[] { () => X("ctlFaster", "faster"), () => X("ctlSlower", "slower") },
                new[] { "<Keyboard>/rightBracket", "<Keyboard>/slash" },
                padParts: new[] { "<Gamepad>/rightShoulder", "<Gamepad>/leftShoulder" });
            Brake = Button("brake", () => X("ctlBrake", "Brake"), "<Keyboard>/s", null, null);
            Boost = Button("boost", () => X("ctlBoost", "Boost"), "<Keyboard>/w", null, "<Gamepad>/buttonSouth");
            Roll = Composite("roll", () => X("ctlRoll", "Roll"), InputActionType.Value, "Axis", "1DAxis",
                new[] { "Negative", "Positive" },
                new Func<string>[] { () => X("ctlLeft", "left"), () => X("ctlRight", "right") },
                new[] { "<Keyboard>/1", "<Keyboard>/3" }, padParts: new string[] { null, null });
            LevelOut = Button("levelOut", () => X("ctlLevelOut", "Level out"), "<Keyboard>/2", null, "<Gamepad>/buttonNorth");
            // The PC version's binding screen: 3350 / 3351 "Strafe left / right" (held, PlayerEgo::strafe). The dodge (the
            // phone's swipe) has no keyboard default; on the controller the right stick pushed left / right (a binding like
            // any other, rebindable: it was a fixed flick that a right-stick-press binding switched off).
            StrafeLeft = Button("strafeLeft", () => Localization.Get(3350), "<Keyboard>/a", null, null);
            StrafeRight = Button("strafeRight", () => Localization.Get(3351), "<Keyboard>/d", null, null);
            DodgeLeft = Button("dodgeLeft", () => X("ctlDodgeLeft", "Dodge left"), null, null, "<Gamepad>/rightStick/left");
            DodgeRight = Button("dodgeRight", () => X("ctlDodgeRight", "Dodge right"), null, null, "<Gamepad>/rightStick/right");
            // Remake: the mining drill on its own row (it used Steer: binding W A S D there to drill also steered the ship with
            // the boost / brake / strafe keys). Read only while the minigame runs, when the flight controls aren't, so its
            // keys may overlap them: the arrows, W A S D as the second keys, the left stick.
            Drill = Composite("drill", () => X("ctlDrill", "Mining drill"), InputActionType.Value, "Vector2", "2DVector",
                new[] { "Up", "Down", "Left", "Right" },
                new Func<string>[] { () => X("ctlUp", "up"), () => X("ctlDown", "down"), () => X("ctlLeft", "left"), () => X("ctlRight", "right") },
                new[] { "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow" },
                padSingle: "<Gamepad>/leftStick", padType: "Vector2",
                keys2: new[] { "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d" });
            // ---- weapons
            FirePrimary = Button("firePrimary", () => X("ctlFirePrimary", "Fire"), "<Keyboard>/space", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            FireSecondary = Button("fireSecondary", () => X("ctlFireSecondary", "Fire secondary"), "<Keyboard>/r", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            SwitchSecondary = Button("switchSecondary", () => X("ctlSwitchSecondary", "Switch secondary"), "<Keyboard>/g", null, "<Gamepad>/dpad/right");
            Camera = Button("camera", () => X("ctlCamera", "Camera / turret view"), "<Keyboard>/t", null, "<Gamepad>/dpad/up");
            // Remake (players' feedback, 2026-10-22): the cockpit view on its own (FreeLookCamera.ToggleCockpit), out of the
            // camera cycle; holding the camera button does the same. No controller default: D-pad up held.
            CockpitView = Button("cockpitView", () => X("ctlCockpitView", "Cockpit view on / off"), "<Keyboard>/f2", null, null);
            AutoTurret = Button("autoTurret", () => X("ctlAutoTurret", "Auto turret on / off"), "<Keyboard>/y", null, "<Gamepad>/dpad/down");
            // Remake: drops the ship lock (CombatRadar.Unlock; the original has none). U, and B: in flight B is only the
            // menus' back, and the flight's presses wait while a menu is open (Navigation.PressesBlocked).
            UnlockTarget = Button("unlockTarget", () => X("ctlUnlockTarget", "Unlock target"), "<Keyboard>/u", null, "<Gamepad>/buttonEast");
            // ---- navigation and equipment
            Action = Button("action", () => X("ctlAction", "Action (dock, autopilot, jump, mine)"), "<Keyboard>/f", "<Keyboard>/enter", "<Gamepad>/buttonWest");
            AutopilotMenu = Button("autopilotMenu", () => X("ctlAutopilotMenu", "Autopilot menu"), "<Keyboard>/q", null, "<Gamepad>/select");
            // The PC version's E "Actions": the quick menu (Hud::initHudMenu(0): secondary weapons, wingmen, cloak, Khador Drive).
            ActionsMenu = Button("actionsMenu", () => X("ctlActionsMenu", "Actions menu"), "<Keyboard>/e", null, "<Gamepad>/dpad/left");
            FastForward = Button("fastForward", () => X("ctlFastForward", "Fast-forward (hold)"), "<Keyboard>/tab", null, "<Gamepad>/buttonNorth");
            Wingmen = Button("wingmen", () => X("ctlWingmen", "Wingmen"), "<Keyboard>/v", null, null);
            KhadorDrive = Button("khadorDrive", () => X("ctlKhador", "Khador Drive"), "<Keyboard>/k", null, null);
            Cloak = Button("cloak", () => X("ctlCloak", "Cloak"), "<Keyboard>/c", null, "<Gamepad>/rightStickPress");
            TimeExtender = Button("timeExtender", () => X("ctlTimeExtender", "Time extender"), "<Keyboard>/x", null, "<Gamepad>/leftStickPress");
            // ---- other
            MouseSteering = Button("mouseSteering", () => X("ctlMouseSteering", "Mouse steering on / off"), "<Keyboard>/m", "<Mouse>/middleButton", null, padSlot: false);
            Chat = Button("chat", () => X("ctlChat", "Chat (multiplayer)"), "<Keyboard>/b", null, null);
            // Read by ChatView while a line is typed (PressedNow: the map is off meanwhile).
            ChatSend = Button("chatSend", () => X("ctlChatSend", "Chat: send message"), "<Keyboard>/enter", "<Keyboard>/numpadEnter", null, padSlot: false);
            ChatChannel = Button("chatChannel", () => X("ctlChatChannel", "Chat: switch Local / Global"), "<Keyboard>/tab", null, null, padSlot: false);
            Screenshot = Button("screenshot", () => X("ctlScreenshot", "Screenshot"), "<Keyboard>/f12", null, null);
            // Remake: the flight HUD Full -> Minimal -> Off (Settings.HudMode), F1 like many games' "hide HUD".
            HudToggle = Button("hudToggle", () => X("ctlHudToggle", "Toggle HUD"), "<Keyboard>/f1", null, null);
            // Multiplayer (remake): the multiplayer window in flight (N, no controller default) and the squad's distress call
            // (unbound: an accidental call brings the squad over, so it takes a click / tap on its button unless bound here).
            // Added last: the saved overrides are by action and binding index.
            MultiplayerWindow = Button("multiplayerWindow", () => X("ctlMultiplayerWindow", "Multiplayer window"), "<Keyboard>/n", null, null);
            DistressCall = Button("distressCall", () => X("ctlDistressCall", "Distress call (multiplayer)"), null, null, null);
            // Remake: the free flight's own rows (Settings.FlightStyle, FreeFlight; unbound in the original layout, which doesn't
            // read them), and free look while held (both styles).
            Thrust = Composite("thrust", () => X("ctlThrust", "Thrust"), InputActionType.Value, "Axis", "1DAxis",
                new[] { "Positive", "Negative" },
                new Func<string>[] { () => X("ctlForward", "forward"), () => X("ctlBackward", "backward") },
                new string[] { null, null }, padParts: new string[] { null, null });
            StrafeAxis = Composite("strafe", () => X("ctlStrafeAxis", "Strafe"), InputActionType.Value, "Axis", "1DAxis",
                new[] { "Negative", "Positive" },
                new Func<string>[] { () => X("ctlLeft", "left"), () => X("ctlRight", "right") },
                new string[] { null, null }, padParts: new string[] { null, null });
            Hover = Composite("hover", () => X("ctlHover", "Up / down"), InputActionType.Value, "Axis", "1DAxis",
                new[] { "Positive", "Negative" },
                new Func<string>[] { () => X("ctlUp", "up"), () => X("ctlDown", "down") },
                new string[] { null, null }, padParts: new string[] { null, null });
            Dampeners = Button("dampeners", () => X("ctlDampeners", "Inertial dampeners on / off"), null, null, null);
            FreeLookHold = Button("freeLookHold", () => X("ctlFreeLookHold", "Free look (hold)"), null, null, null);
            // Remake: the first three active devices (PlayerDevices, mod items; Flight.Device) on 4 / 5 / 6, free in both layouts
            // (1-3 roll and level out). No controller default (every button is taken): the quick menu (D-pad left) has them.
            Device1 = Button("device1", () => X("ctlDevice1", "Device 1"), "<Keyboard>/4", null, null);
            Device2 = Button("device2", () => X("ctlDevice2", "Device 2"), "<Keyboard>/5", null, null);
            Device3 = Button("device3", () => X("ctlDevice3", "Device 3"), "<Keyboard>/6", null, null);
            // Remake: the headlights on / off (EVERSPACE 2's; Flight.Headlights), the same key in both layouts.
            Headlights = Button("headlights", () => X("ctlHeadlights", "Headlights on / off"), "<Keyboard>/l", null, null);

            bool Free() => loadedStyle == FlightStyles.Free;
            foreach (var r in rows)
                switch (r.id)
                {
                    case "throttle": case "brake": case "strafeLeft": case "strafeRight": r.visible = () => !Free(); break;
                    case "thrust": case "strafe": case "hover": case "dampeners": r.visible = Free; break;
                }
        }

        // ---- the free flight's key layout (EVERSPACE 2's defaults; FlightStyles.Free) ----------------------------

        /// <summary>Per row id and slot: the free flight's default paths (a composite: its parts, "" = unbound). Rows and
        /// slots not listed keep the original's.</summary>
        static readonly Dictionary<string, Dictionary<BindSlot, string[]>> FreeFlightDefaults = new Dictionary<string, Dictionary<BindSlot, string[]>>
        {
            // The right stick aims (the left one moves); the keyboard's arrows still turn; the mouse aims through its reticle.
            ["steer"] = new Dictionary<BindSlot, string[]> { [BindSlot.Pad] = new[] { "<Gamepad>/rightStick" } },
            ["thrust"] = new Dictionary<BindSlot, string[]>
            {
                [BindSlot.Key1] = new[] { "<Keyboard>/w", "<Keyboard>/s" },
                [BindSlot.Pad] = new[] { "<Gamepad>/leftStick/up", "<Gamepad>/leftStick/down" },
            },
            ["strafe"] = new Dictionary<BindSlot, string[]>
            {
                [BindSlot.Key1] = new[] { "<Keyboard>/a", "<Keyboard>/d" },
                [BindSlot.Pad] = new[] { "<Gamepad>/leftStick/left", "<Gamepad>/leftStick/right" },
            },
            ["hover"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Keyboard>/space", "<Keyboard>/leftCtrl" } },
            ["roll"] = new Dictionary<BindSlot, string[]>
            {
                [BindSlot.Key1] = new[] { "<Keyboard>/q", "<Keyboard>/e" },
                [BindSlot.Pad] = new[] { "<Gamepad>/leftShoulder", "<Gamepad>/rightShoulder" },
            },
            ["boost"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Keyboard>/leftShift" } },
            ["dampeners"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Keyboard>/z" } },
            ["freeLookHold"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Keyboard>/leftAlt" } },
            // Not read in free flight: unbound so their keys don't double up.
            ["throttle"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "", "" }, [BindSlot.Pad] = new[] { "", "" } },
            ["brake"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "" } },
            ["strafeLeft"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "" } },
            ["strafeRight"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "" } },
            // The right stick aims: no dodge on it.
            ["dodgeLeft"] = new Dictionary<BindSlot, string[]> { [BindSlot.Pad] = new[] { "" } },
            ["dodgeRight"] = new Dictionary<BindSlot, string[]> { [BindSlot.Pad] = new[] { "" } },
            // Space is up: the mouse fires.
            ["firePrimary"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Mouse>/leftButton" }, [BindSlot.Key2] = new[] { "" } },
            ["fireSecondary"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Mouse>/rightButton" }, [BindSlot.Key2] = new[] { "" } },
            // Q / E roll: the menus move to R (route) and Tab, fast-forward to H.
            ["autopilotMenu"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Keyboard>/r" } },
            ["actionsMenu"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Keyboard>/tab" } },
            ["fastForward"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key1] = new[] { "<Keyboard>/h" } },
            // The mouse always aims in free flight; M still frees the cursor.
            ["mouseSteering"] = new Dictionary<BindSlot, string[]> { [BindSlot.Key2] = new[] { "" } },
        };

        /// <summary>The style whose layout is loaded (FlightStyles.Current when it was last applied).</summary>
        static int loadedStyle = -1;
        /// <summary>The flight style whose key layout is loaded (FlightStyles).</summary>
        public static int LoadedStyle => loadedStyle;
        static string StylePrefsKey => loadedStyle == FlightStyles.Free ? PrefsKey + "_free" : PrefsKey;

        /// <summary>The layout of the flight style now in use (FlightStyles.Current: the option, or a mod's / an event's):
        /// its defaults and its own overrides. Called when the option changes and every frame by the flight (ShipController)
        /// and the bindings page, for a style a mod or an event switches; a no-op when that style is loaded already.</summary>
        public static void ApplyStyle()
        {
            if (loadedStyle == FlightStyles.Current) return;
            CancelRebind();
            Load();
            Changed?.Invoke();
        }

        /// <summary>A binding's default in the loaded style (null = the action's own path).</summary>
        static string StyleDefault(InputAction action, int index)
        {
            if (loadedStyle != FlightStyles.Free) return null;
            var row = RowOf(action);
            if (row == null || !FreeFlightDefaults.TryGetValue(row.id, out var slots)) return null;
            foreach (var kv in slots)
            {
                var idx = row.slots[(int)kv.Key];
                if (idx == null) continue;
                for (int i = 0; i < idx.Length && i < kv.Value.Length; i++) if (idx[i] == index) return kv.Value[i] ?? "";
            }
            return null;
        }

        static InputAction Button(string id, Func<string> label, string key1, string key2, string pad, bool padSlot = true)
        {
            var a = Map.AddAction(id, InputActionType.Button);
            var row = new ControlRow { id = id, label = label, action = a };
            row.slots[0] = new[] { Add(a, key1, KeyGroup) };
            row.slots[1] = new[] { Add(a, key2, KeyGroup) };
            if (padSlot) row.slots[2] = new[] { Add(a, pad, PadGroup) };
            rows.Add(row);
            return a;
        }

        /// <summary>A composite row: two keyboard composites (the second unbound) and, on the controller, the same composite
        /// ('padParts') or one control ('padSingle', a stick).</summary>
        static InputAction Composite(string id, Func<string> label, InputActionType type, string controlType, string composite,
                                     string[] parts, Func<string>[] partLabels, string[] keys, string[] padParts = null,
                                     string padSingle = null, string padType = "Button", string[] keys2 = null)
        {
            var a = Map.AddAction(id, type);
            a.expectedControlType = controlType;
            var row = new ControlRow { id = id, label = label, action = a, partLabels = partLabels, padType = padType };
            row.slots[0] = AddComposite(a, composite, parts, keys, KeyGroup);
            row.slots[1] = AddComposite(a, composite, parts, keys2 ?? new string[parts.Length], KeyGroup);
            if (padSingle != null) row.slots[2] = new[] { Add(a, padSingle, PadGroup) };
            else if (padParts != null) row.slots[2] = AddComposite(a, composite, parts, padParts, PadGroup);
            rows.Add(row);
            return a;
        }

        static int Add(InputAction a, string path, string group)
        {
            a.AddBinding(path ?? "", groups: group);
            return a.bindings.Count - 1;
        }

        static int[] AddComposite(InputAction a, string composite, string[] parts, string[] paths, string group)
        {
            var syntax = a.AddCompositeBinding(composite);
            for (int i = 0; i < parts.Length; i++) syntax.With(parts[i], paths[i] ?? "", group);
            int first = a.bindings.Count - parts.Length;
            var indices = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) indices[i] = first + i;
            return indices;
        }

        // ---- lifetime ------------------------------------------------------------------------------------------

        static int suspended;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Start()
        {
            CancelRebind();
            suspended = 0;
            Load();
            Map.Enable();
            Settings.Changed -= ApplyStyle;
            Settings.Changed += ApplyStyle;   // the flight style option (and Default settings) switch the layout
            InputMode.StyleChanged -= StyleChanged;
            InputMode.StyleChanged += StyleChanged;   // another controller's button names (Xbox / PlayStation)
        }

        static void StyleChanged() => Changed?.Invoke();

        /// <summary>Chat typing (NetChat) and rebinding switch the game's controls off; nested calls count.</summary>
        public static void Suspend(bool on)
        {
            suspended = Math.Max(0, suspended + (on ? 1 : -1));
            if (suspended > 0) Map.Disable();
            else if (Application.isPlaying) Map.Enable();
        }

        public static void ResumeAll()
        {
            suspended = 0;
            if (Application.isPlaying) Map.Enable();
        }

        [Serializable] sealed class SavedOverride { public string action; public int index; public string path; }
        [Serializable] sealed class SavedOverrides { public int version = 2; public List<SavedOverride> bindings = new List<SavedOverride>(); }

        static void Load()
        {
            Map.RemoveAllBindingOverrides();
            loadedStyle = FlightStyles.Current;
            // The style's defaults first (as overrides of the original's paths), the player's own on top.
            foreach (var action in Map.actions)
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    if (action.bindings[i].isComposite) continue;
                    string d = StyleDefault(action, i);
                    if (d != null) action.ApplyBindingOverride(i, d);
                }
            string json = PlayerPrefs.GetString(StylePrefsKey, "");
            if (json.Length == 0) return;
            try
            {
                var saved = JsonUtility.FromJson<SavedOverrides>(json);
                // Version 1 was the Input System's JSON (binding ids that no longer exist): nothing to restore.
                if (saved == null || saved.version < 2 || saved.bindings == null) return;
                foreach (var o in saved.bindings)
                {
                    var action = Map.FindAction(o.action);
                    if (action == null || o.index < 0 || o.index >= action.bindings.Count || action.bindings[o.index].isComposite) continue;
                    action.ApplyBindingOverride(o.index, o.path ?? "");
                }
            }
            catch (Exception e) { Debug.LogWarning("GameControls: bindings not loaded: " + e.Message); }
        }

        static void Save()
        {
            var saved = new SavedOverrides();
            foreach (var action in Map.actions)
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var b = action.bindings[i];
                    if (b.isComposite || b.overridePath == null) continue;
                    // Only what differs from the loaded style's default (the free layout's defaults are overrides too).
                    string d = StyleDefault(action, i);
                    if (d != null && b.overridePath == d) continue;
                    saved.bindings.Add(new SavedOverride { action = action.name, index = i, path = b.overridePath });
                }
            PlayerPrefs.SetString(StylePrefsKey, JsonUtility.ToJson(saved));
            PlayerPrefs.Save();
        }

        /// <summary>Every binding back to its default (the options' "Reset key bindings" and Default settings, 497).</summary>
        public static void ResetToDefaults()
        {
            CancelRebind();
            PlayerPrefs.DeleteKey(StylePrefsKey);   // the loaded style's own; the other layout keeps its
            PlayerPrefs.Save();
            Load();
            Changed?.Invoke();
        }

        // ---- lookups --------------------------------------------------------------------------------------------

        public static ControlRow RowOf(InputAction action) => rows.Find(r => r.action == action);

        static bool IsPad(BindSlot s) => s == BindSlot.Pad;

        /// <summary>The slot's effective control paths (a composite: its parts; unbound parts are "").</summary>
        public static string[] Paths(ControlRow row, BindSlot slot)
        {
            var idx = row.slots[(int)slot];
            if (idx == null) return Array.Empty<string>();
            var paths = new string[idx.Length];
            for (int i = 0; i < idx.Length; i++) paths[i] = row.action.bindings[idx[i]].effectivePath ?? "";
            return paths;
        }

        public static bool IsBound(ControlRow row, BindSlot slot)
        {
            foreach (var p in Paths(row, slot)) if (!string.IsNullOrEmpty(p)) return true;
            return false;
        }

        /// <summary>The short name the HUD and the options show for one control path ("F", "SPACE", "LMB", "A", "RT", "LS"):
        /// the controller's buttons in the current style (InputMode.Style: "×", "R2", "L3" for a PlayStation pad).</summary>
        public static string ShortName(string path) => ShortName(path, InputMode.Style);

        /// <summary>ShortName with the controller's buttons in this style.</summary>
        public static string ShortName(string path, PadStyle style)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.ToLowerInvariant();
            string pad = PadControl(p);
            if (pad != null)
            {
                bool ps = style == PadStyle.PlayStation;
                return pad switch
                {
                    "buttonsouth" => ps ? "×" : "A", "buttoneast" => ps ? "○" : "B", "buttonwest" => ps ? "□" : "X", "buttonnorth" => ps ? "△" : "Y",
                    "leftshoulder" => ps ? "L1" : "LB", "rightshoulder" => ps ? "R1" : "RB", "lefttrigger" => ps ? "L2" : "LT", "righttrigger" => ps ? "R2" : "RT",
                    "leftstick" => ps ? "L" : "LS", "rightstick" => ps ? "R" : "RS", "leftstickpress" => ps ? "L3" : "LS", "rightstickpress" => ps ? "R3" : "RS",
                    "leftstick/up" => Stick(ps, "L", "↑"), "leftstick/down" => Stick(ps, "L", "↓"), "leftstick/left" => Stick(ps, "L", "←"), "leftstick/right" => Stick(ps, "L", "→"),
                    "rightstick/up" => Stick(ps, "R", "↑"), "rightstick/down" => Stick(ps, "R", "↓"), "rightstick/left" => Stick(ps, "R", "←"), "rightstick/right" => Stick(ps, "R", "→"),
                    "dpad" => "D-PAD", "dpad/up" => "D-PAD ↑", "dpad/down" => "D-PAD ↓", "dpad/left" => "D-PAD ←", "dpad/right" => "D-PAD →",
                    "start" => ps ? "OPTIONS" : "MENU", "select" => ps ? (InputMode.DualShock4 ? "SHARE" : "CREATE") : "VIEW",
                    "touchpadbutton" => "TOUCHPAD",
                    _ => InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant(),
                };
            }
            if (p.StartsWith("<mouse>/"))
            {
                return p.Substring("<mouse>/".Length) switch
                {
                    "leftbutton" => "LMB", "rightbutton" => "RMB", "middlebutton" => "MMB", "backbutton" => "MB4", "forwardbutton" => "MB5",
                    _ => InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant(),
                };
            }
            switch (p)
            {
                case "<keyboard>/uparrow": return "↑";
                case "<keyboard>/downarrow": return "↓";
                case "<keyboard>/leftarrow": return "←";
                case "<keyboard>/rightarrow": return "→";
                case "<keyboard>/space": return X("keySpace", "Space").ToUpperInvariant();
                case "<keyboard>/enter": return "ENTER";
                case "<keyboard>/escape": return "ESC";
                case "<keyboard>/tab": return "TAB";
                case "<keyboard>/leftshift": return "L-SHIFT";
                case "<keyboard>/rightshift": return "R-SHIFT";
                case "<keyboard>/leftctrl": return "L-CTRL";
                case "<keyboard>/rightctrl": return "R-CTRL";
                case "<keyboard>/leftalt": return "L-ALT";
                case "<keyboard>/rightalt": return "R-ALT";
            }
            // The key's name on this keyboard's layout ("]" for rightBracket on a US keyboard).
            var kb = Keyboard.current;
            var control = kb != null ? InputControlPath.TryFindControl(kb, path) : null;
            string name = control != null ? control.displayName : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(name) ? "?" : name.ToUpperInvariant();
        }

        // A stick pushed one way: "LS ↑" (Xbox) / "L ↑" (PlayStation, whose sticks are L / R).
        static string Stick(bool ps, string side, string arrow) => (ps ? side : side + "S") + " " + arrow;

        /// <summary>A controller path's control ("buttonsouth", "leftstick/up"), lower case; null for any other device. The
        /// bindings name &lt;Gamepad&gt;, a capture of a pad-only control (a DualSense's touchpad) its own layout.</summary>
        public static string PadControl(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '<') return null;
            int close = path.IndexOf(">/", StringComparison.Ordinal);
            if (close < 0) return null;
            string layout = path.Substring(1, close - 1);
            if (!layout.Equals("gamepad", StringComparison.OrdinalIgnoreCase))
            {
                bool basedOn;
                try { basedOn = InputSystem.IsFirstLayoutBasedOnSecond(layout, "Gamepad"); }
                catch (ArgumentException) { basedOn = false; }
                if (!basedOn) return null;
            }
            return path.Substring(close + 2).ToLowerInvariant();
        }

        /// <summary>The slot as text: its key, or a composite's keys ("↑ ↓ ← →"); "" = unbound.</summary>
        public static string SlotText(ControlRow row, BindSlot slot)
        {
            if (!IsBound(row, slot)) return "";
            var names = new List<string>();
            foreach (var p in Paths(row, slot)) names.Add(string.IsNullOrEmpty(p) ? "–" : ShortName(p));
            return string.Join(" ", names);
        }

        /// <summary>A control bound to 'action' went down this frame, read from the devices themselves: works while the map is
        /// off (ChatView reads the chat keys while a line is typed and the flight controls are suspended).</summary>
        public static bool PressedNow(InputAction action)
        {
            if (action == null) return false;
            foreach (var b in action.bindings)
            {
                if (b.isComposite || string.IsNullOrEmpty(b.effectivePath)) continue;
                using (var controls = InputSystem.FindControls(b.effectivePath))
                    foreach (var c in controls)
                        if (c is ButtonControl button && button.wasPressedThisFrame) return true;
            }
            return false;
        }

        /// <summary>The first bound slot's text for the keyboard or the controller ("" = none): the flight hints' #KEY_ tokens.</summary>
        public static string KeyText(InputAction action, bool pad)
        {
            var row = RowOf(action);
            if (row == null) return "";
            if (pad) return row.HasSlot(BindSlot.Pad) ? SlotText(row, BindSlot.Pad) : "";
            string k = SlotText(row, BindSlot.Key1);
            return k.Length > 0 ? k : SlotText(row, BindSlot.Key2);
        }

        // ---- rebinding -----------------------------------------------------------------------------------------

        static InputActionRebindingExtensions.RebindingOperation operation;
        static int rebindEndFrame = -10;
        static bool capturingPad;
        static Action markClear;   // the running capture's "clear instead" (CancelFromOtherDevice)

        /// <summary>A capture that takes nothing in this long ends by itself: the menus ignore every input while one waits,
        /// so a player without the device it waits for (a keyboard cell picked with a controller or a tap) had no way out.</summary>
        const float CaptureTimeoutSeconds = 10f;

        /// <summary>A key is being captured: the menus ignore their keys meanwhile (and on the frame it ends, so the
        /// captured key doesn't also act in the menu).</summary>
        public static bool Rebinding => operation != null;
        public static bool BlocksMenus => operation != null || Time.frameCount <= rebindEndFrame + 1
                                          || GoF2Remake.World.SpaceHooks.QuestionOpen   // an event's question takes the keys
                                          || TextEntryOpen || Time.frameCount <= textEntryEndFrame + 1    // the controller's keyboard (PadKeyboard)
                                          || ModalOpen || Time.frameCount <= modalEndFrame + 1;           // "Keep these settings?" (SettingsConfirm)

        /// <summary>Rework: a modal question that reads the devices itself is open (UI.SettingsConfirm): the menus ignore
        /// their keys meanwhile and on the frame it closes.</summary>
        public static bool ModalOpen
        {
            get => modalOpen;
            set { if (modalOpen && !value) modalEndFrame = Time.frameCount; modalOpen = value; }
        }
        static bool modalOpen;
        static int modalEndFrame = -10;

        /// <summary>Rework: the controller's on-screen keyboard (UI.PadKeyboard) is open: it takes the controller, the menus
        /// ignore their keys meanwhile and on the frame it closes.</summary>
        public static bool TextEntryOpen
        {
            get => textEntryOpen;
            set { if (textEntryOpen && !value) textEntryEndFrame = Time.frameCount; textEntryOpen = value; }
        }
        static bool textEntryOpen;
        static int textEntryEndFrame = -10;

        /// <summary>Captures the slot's binding (a composite slot part by part). 'prompt' gets the part being asked for
        /// ("up", or null for a single binding); 'done' runs once it ended (captured, cleared or cancelled). Esc (or the
        /// controller's Menu) cancels, Backspace / Delete unbinds the slot. Another kind of input than the slot's also
        /// cancels (a keyboard cell: the controller's Menu / B, a tap; a controller cell: a mouse click, a tap), and so does
        /// waiting CaptureTimeoutSeconds.</summary>
        public static void Rebind(ControlRow row, BindSlot slot, Action<string> prompt, Action done)
        {
            CancelRebind();
            var idx = row.slots[(int)slot];
            if (idx == null) { done?.Invoke(); return; }
            Suspend(true);
            RebindPart(row, slot, 0, prompt, done);
        }

        static void RebindPart(ControlRow row, BindSlot slot, int part, Action<string> prompt, Action done)
        {
            var idx = row.slots[(int)slot];
            int index = idx[part];
            bool single = idx.Length == 1;
            prompt?.Invoke(single ? null : row.partLabels?[part]?.Invoke());
            bool clear = false;
            markClear = () => clear = true;

            var op = row.action.PerformInteractiveRebinding(index)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithTimeout(CaptureTimeoutSeconds)
                .OnMatchWaitForAnother(0.1f);
            // A later part never takes what an earlier part of this slot just took: the stick still pushed left for "left"
            // was taken again for "right" the moment that part's capture started (#24: Roll on the controller had one way).
            for (int p = 0; p < part; p++)
            {
                string taken = row.action.bindings[idx[p]].effectivePath;
                if (!string.IsNullOrEmpty(taken)) op.WithControlsExcluding(taken);
            }
            capturingPad = IsPad(slot);
            if (IsPad(slot))
            {
                bool wholeStick = single && row.padType == "Vector2";
                op.WithControlsHavingToMatchPath("<Gamepad>")
                  .WithExpectedControlType(wholeStick ? "Vector2" : "Button");
                if (wholeStick)
                {
                    // A whole stick (or the D-pad), not one of its directions.
                    foreach (var stick in new[] { "leftStick", "rightStick" })
                        foreach (var dir in new[] { "up", "down", "left", "right" })
                            op.WithControlsExcluding($"<Gamepad>/{stick}/{dir}");
                }
                else
                {
                    // A stick pushed one way is a button too (right stick up / down for the throttle); pushed past half
                    // way, so a drifting stick isn't caught.
                    op.WithMagnitudeHavingToBeGreaterThan(0.5f);
                    foreach (var stick in new[] { "leftStick", "rightStick" })
                        foreach (var axis in new[] { "x", "y" })
                            op.WithControlsExcluding($"<Gamepad>/{stick}/{axis}");
                }
            }
            else
            {
                op.WithControlsHavingToMatchPath("<Keyboard>")
                  .WithControlsHavingToMatchPath("<Mouse>")
                  .WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta")
                  .WithControlsExcluding("<Mouse>/scroll").WithControlsExcluding("<Mouse>/press")
                  .WithExpectedControlType("Button");
            }
            op.OnPotentialMatch(o =>
            {
                var c = o.selectedControl;
                if (c is KeyControl k && (k.keyCode == Key.Backspace || k.keyCode == Key.Delete)) { clear = true; o.Cancel(); }
                else if (Gamepad.current != null && c == Gamepad.current.startButton) o.Cancel();   // the pause button stays fixed
            });
            op.OnComplete(o =>
            {
                Dispose();
                if (part + 1 < idx.Length) RebindPart(row, slot, part + 1, prompt, done);
                else Finish(done);
            });
            op.OnCancel(o =>
            {
                if (clear)
                    foreach (int i in idx) row.action.ApplyBindingOverride(i, "");
                Dispose();
                Finish(done);
            });
            operation = op;
            InputSystem.onAfterUpdate -= CancelFromOtherDevice;
            InputSystem.onAfterUpdate += CancelFromOtherDevice;
            op.Start();
        }

        /// <summary>The way out of a capture for input it doesn't take: a keyboard cell picked with a controller or a tap
        /// waited for a key that never came (on a controller or a phone: stuck in the options for good).</summary>
        static void CancelFromOtherDevice()
        {
            var op = operation;
            if (op == null) { InputSystem.onAfterUpdate -= CancelFromOtherDevice; return; }
            if (UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType == UnityEngine.InputSystem.LowLevel.InputUpdateType.Editor) return;
            // A controller cell's capture only listens to the controller: Backspace / Delete on the keyboard clears it too.
            var keys = Keyboard.current;
            if (capturingPad && keys != null && (keys.backspaceKey.wasPressedThisFrame || keys.deleteKey.wasPressedThisFrame)
                && op.started && !op.completed && !op.canceled)
            {
                markClear?.Invoke();
                op.Cancel();
                return;
            }
            var touch = Touchscreen.current;
            bool tap = touch != null && touch.primaryTouch.press.wasPressedThisFrame;
            bool other;
            if (capturingPad)
            {
                var mouse = Mouse.current;
                other = tap || mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);
            }
            else
            {
                var pad = Gamepad.current;
                other = tap || pad != null && (pad.startButton.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame);
            }
            if (other && op.started && !op.completed && !op.canceled) op.Cancel();
        }

        static void Dispose()
        {
            var op = operation;
            operation = null;
            InputSystem.onAfterUpdate -= CancelFromOtherDevice;
            op?.Dispose();
        }

        static void Finish(Action done)
        {
            rebindEndFrame = Time.frameCount;
            Suspend(false);
            Save();
            Changed?.Invoke();
            done?.Invoke();
        }

        /// <summary>Stops a capture in progress (a menu closed meanwhile), keeping what was bound so far.</summary>
        public static void CancelRebind()
        {
            if (operation == null) return;
            var op = operation;
            operation = null;
            InputSystem.onAfterUpdate -= CancelFromOtherDevice;
            op.Dispose();
            rebindEndFrame = Time.frameCount;
            Suspend(false);
            Save();
            Changed?.Invoke();
        }

        /// <summary>Unbinds one slot (a right click on it in the options).</summary>
        public static void Clear(ControlRow row, BindSlot slot)
        {
            var idx = row.slots[(int)slot];
            if (idx == null) return;
            foreach (int i in idx) row.action.ApplyBindingOverride(i, "");
            Save();
            Changed?.Invoke();
        }
    }
}
