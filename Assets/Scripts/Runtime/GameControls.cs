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

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
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

        /// <summary>A binding changed (a rebind, a reset): hints and option rows show the new keys.</summary>
        public static event Action Changed;

        public static readonly InputAction Steer, Throttle, Brake, Boost, LevelOut, Roll, StrafeLeft, StrafeRight, DodgeLeft, DodgeRight, Drill,
            FirePrimary, FireSecondary, SwitchSecondary, Action, AutopilotMenu, ActionsMenu, Wingmen, KhadorDrive, FastForward,
            Camera, AutoTurret, Cloak, TimeExtender, MouseSteering, Chat, ChatSend, ChatChannel, Screenshot;

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
            AutoTurret = Button("autoTurret", () => X("ctlAutoTurret", "Auto turret on / off"), "<Keyboard>/y", null, "<Gamepad>/dpad/down");
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
        }

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
            string json = PlayerPrefs.GetString(PrefsKey, "");
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
                    if (b.overridePath != null) saved.bindings.Add(new SavedOverride { action = action.name, index = i, path = b.overridePath });
                }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(saved));
            PlayerPrefs.Save();
        }

        /// <summary>Every binding back to its default (the options' "Reset key bindings" and Default settings, 497).</summary>
        public static void ResetToDefaults()
        {
            CancelRebind();
            Map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
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

        /// <summary>The short name the HUD and the options show for one control path ("F", "SPACE", "LMB", "A", "RT", "LS").</summary>
        public static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.ToLowerInvariant();
            if (p.StartsWith("<gamepad>/"))
            {
                string c = p.Substring("<gamepad>/".Length);
                return c switch
                {
                    "buttonsouth" => "A", "buttoneast" => "B", "buttonwest" => "X", "buttonnorth" => "Y",
                    "leftshoulder" => "LB", "rightshoulder" => "RB", "lefttrigger" => "LT", "righttrigger" => "RT",
                    "leftstick" => "LS", "rightstick" => "RS", "leftstickpress" => "LS", "rightstickpress" => "RS",
                    "leftstick/up" => "LS ↑", "leftstick/down" => "LS ↓", "leftstick/left" => "LS ←", "leftstick/right" => "LS →",
                    "rightstick/up" => "RS ↑", "rightstick/down" => "RS ↓", "rightstick/left" => "RS ←", "rightstick/right" => "RS →",
                    "dpad" => "D-PAD", "dpad/up" => "D-PAD ↑", "dpad/down" => "D-PAD ↓", "dpad/left" => "D-PAD ←", "dpad/right" => "D-PAD →",
                    "start" => "MENU", "select" => "VIEW",
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

        /// <summary>A capture that takes nothing in this long ends by itself: the menus ignore every input while one waits,
        /// so a player without the device it waits for (a keyboard cell picked with a controller or a tap) had no way out.</summary>
        const float CaptureTimeoutSeconds = 10f;

        /// <summary>A key is being captured: the menus ignore their keys meanwhile (and on the frame it ends, so the
        /// captured key doesn't also act in the menu).</summary>
        public static bool Rebinding => operation != null;
        public static bool BlocksMenus => operation != null || Time.frameCount <= rebindEndFrame + 1
                                          || Multiplayer.NetScreen.QuestionOpen;   // an event's question takes the keys

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

            var op = row.action.PerformInteractiveRebinding(index)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithTimeout(CaptureTimeoutSeconds)
                .OnMatchWaitForAnother(0.1f);
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
