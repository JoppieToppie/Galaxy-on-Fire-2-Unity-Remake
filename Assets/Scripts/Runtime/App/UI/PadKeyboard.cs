// PadKeyboard.cs
// Rework (docs/UI-DESIGN.md "Text entry"): an on-screen keyboard for a controller. Steam Deck's guidelines ask that text
// entry opens a keyboard when a controller is used; a desktop or Linux build has no system one (Xbox / UWP and phones
// keep theirs: TouchScreenKeyboard.isSupported). Attach(root) once per screen root: A (a submit) on a text field while a
// controller is the input opens it over the screen. The D-pad / left stick move over the keys (held: repeats), A types
// the key, B deletes, X a space, Y shift, Menu done (the text into the field), View cancels; the keys take clicks and
// taps too. While it is open the menus ignore their keys (GameControls.TextEntryOpen) and the panel's navigation events
// stop here. Styles: GoF2Common.uss .pad-keyboard.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class PadKeyboard
    {
        static readonly string[] Lower = { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.-" };
        static readonly string[] Upper = { "!@#$%&*()?", "QWERTYUIOP", "ASDFGHJKL\"", "ZXCVBNM;:_" };
        const int Cols = 10;
        // The bottom row: shift, space, delete, done (in key columns: 2, 4, 2, 2).
        enum Special { Shift, Space, Delete, Done }
        static readonly (Special what, int from, int span)[] Bottom = { (Special.Shift, 0, 2), (Special.Space, 2, 4), (Special.Delete, 6, 2), (Special.Done, 8, 2) };

        /// <summary>The keyboard open on some screen, if any.</summary>
        public static PadKeyboard Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        readonly VisualElement root, overlay;
        readonly TextField field;
        readonly Label preview;
        readonly List<Button[]> rows = new List<Button[]>();
        readonly Button[] bottom = new Button[Bottom.Length];
        string text;
        bool shift;
        int row, col;
        float repeatAt;
        Vector2Int heldDir;
        IVisualElementScheduledItem tick;

        /// <summary>Lets every text field under 'root' open the keyboard with a controller (once per root).</summary>
        public static void Attach(VisualElement root)
        {
            if (root == null || root.ClassListContains("pad-keyboard-host")) return;
            root.AddToClassList("pad-keyboard-host");
            root.RegisterCallback<NavigationSubmitEvent>(e =>
            {
                if (Current != null || !Wanted()) return;
                var f = FieldOf(e.target as VisualElement);
                if (f == null || f.isReadOnly || !f.enabledInHierarchy) return;
                e.StopImmediatePropagation();
                root.focusController?.IgnoreEvent(e);
                Open(root, f);
            }, TrickleDown.TrickleDown);
        }

        /// <summary>A controller is the input and the platform has no keyboard of its own.</summary>
        static bool Wanted() => InputMode.Current == InputKind.Gamepad && !TouchScreenKeyboard.isSupported;

        static TextField FieldOf(VisualElement e)
        {
            for (; e != null; e = e.parent) if (e is TextField f) return f;
            return null;
        }

        public static void Open(VisualElement root, TextField field)
        {
            Current?.Close(false);
            Current = new PadKeyboard(root, field);
        }

        PadKeyboard(VisualElement root, TextField field)
        {
            this.root = root;
            this.field = field;
            text = field.value ?? "";
            overlay = new VisualElement();
            overlay.AddToClassList("pad-keyboard");
            var box = new VisualElement();
            box.AddToClassList("pad-keyboard__box");
            overlay.Add(box);
            preview = new Label { pickingMode = PickingMode.Ignore };
            preview.AddToClassList("pad-keyboard__preview");
            box.Add(preview);
            for (int r = 0; r < Lower.Length; r++)
            {
                var line = new VisualElement();
                line.AddToClassList("pad-keyboard__row");
                var keys = new Button[Cols];
                for (int c = 0; c < Cols; c++)
                {
                    int rr = r, cc = c;
                    var b = new Button(() => { row = rr; col = cc; Press(); }) { focusable = false };
                    b.AddToClassList("pad-keyboard__key");
                    b.AddToClassList("gof-semibold");
                    line.Add(b);
                    keys[c] = b;
                }
                rows.Add(keys);
                box.Add(line);
            }
            var last = new VisualElement();
            last.AddToClassList("pad-keyboard__row");
            for (int i = 0; i < Bottom.Length; i++)
            {
                int index = i;
                var b = new Button(() => { row = Lower.Length; col = Bottom[index].from; Press(); }) { focusable = false };
                b.AddToClassList("pad-keyboard__key");
                b.AddToClassList("pad-keyboard__key--wide");
                b.AddToClassList("gof-semibold");
                b.style.flexGrow = Bottom[i].span;
                last.Add(b);
                bottom[i] = b;
            }
            box.Add(last);
            var prompts = new VisualElement { pickingMode = PickingMode.Ignore };
            prompts.AddToClassList("prompt-bar");
            prompts.AddToClassList("pad-keyboard__prompts");
            string X(string key, string english) => Localization.Extra(key, english).UiCaps();
            prompts.Add(MenuKeys.Hint(X("kbType", "Type"), InputGlyph.Pad(PadButton.A)));
            prompts.Add(MenuKeys.Hint(X("kbDelete", "Delete"), InputGlyph.Pad(PadButton.B)));
            prompts.Add(MenuKeys.Hint(X("kbSpace", "Space"), InputGlyph.Pad(PadButton.X)));
            prompts.Add(MenuKeys.Hint(X("kbShift", "Shift"), InputGlyph.Pad(PadButton.Y)));
            prompts.Add(MenuKeys.Hint(X("kbDone", "Done"), InputGlyph.Pad(PadButton.Menu)));
            prompts.Add(MenuKeys.Hint(X("kbCancel", "Cancel"), InputGlyph.Pad(PadButton.View)));
            box.Add(prompts);
            root.Add(overlay);
            // The panel's navigation, submit and cancel events stop here meanwhile (the menus' own handlers would move the focus).
            overlay.panel?.visualTree.RegisterCallback<NavigationMoveEvent>(Swallow, TrickleDown.TrickleDown);
            overlay.panel?.visualTree.RegisterCallback<NavigationSubmitEvent>(Swallow, TrickleDown.TrickleDown);
            overlay.panel?.visualTree.RegisterCallback<NavigationCancelEvent>(Swallow, TrickleDown.TrickleDown);
            GameControls.TextEntryOpen = true;
            Refresh();
            tick = overlay.schedule.Execute(Tick).Every(0);
        }

        void Swallow(EventBase e)
        {
            e.StopImmediatePropagation();
            overlay.focusController?.IgnoreEvent(e);
        }

        void Refresh()
        {
            var keys = shift ? Upper : Lower;
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < Cols; c++)
                {
                    rows[r][c].text = keys[r][c].ToString();
                    rows[r][c].EnableInClassList("pad-keyboard__key--sel", row == r && col == c);
                }
            string X(string key, string english) => Localization.Extra(key, english).UiCaps();
            for (int i = 0; i < Bottom.Length; i++)
            {
                bottom[i].text = Bottom[i].what switch
                {
                    Special.Shift => shift ? "⇧ abc" : "⇧ ABC",
                    Special.Space => X("kbSpace", "Space"),
                    Special.Delete => "⌫",
                    _ => X("kbDone", "Done"),
                };
                bottom[i].EnableInClassList("pad-keyboard__key--sel", row == Lower.Length && col >= Bottom[i].from && col < Bottom[i].from + Bottom[i].span);
            }
            preview.text = (field.isPasswordField ? new string('•', text.Length) : text) + "|";
        }

        void Press()
        {
            UiSounds.Confirm();
            if (row < Lower.Length)
            {
                Type((shift ? Upper : Lower)[row][col].ToString());
                return;
            }
            foreach (var b in Bottom)
            {
                if (col < b.from || col >= b.from + b.span) continue;
                switch (b.what)
                {
                    case Special.Shift: shift = !shift; break;
                    case Special.Space: Type(" "); break;
                    case Special.Delete: Delete(); break;
                    case Special.Done: Close(true); return;
                }
            }
            Refresh();
        }

        void Type(string s)
        {
            if (field.maxLength > 0 && text.Length >= field.maxLength) return;
            text += s;
            Refresh();
        }

        void Delete()
        {
            if (text.Length > 0) text = text.Substring(0, text.Length - 1);
            Refresh();
        }

        void Move(Vector2Int d)
        {
            UiSounds.Move();
            int rowsAll = Lower.Length + 1;
            if (d.y != 0) row = (row + d.y + rowsAll) % rowsAll;
            if (d.x != 0)
            {
                if (row == Lower.Length)
                {
                    // The bottom row moves key by key, whatever their widths.
                    int at = 0;
                    for (int i = 0; i < Bottom.Length; i++) if (col >= Bottom[i].from && col < Bottom[i].from + Bottom[i].span) at = i;
                    at = (at + d.x + Bottom.Length) % Bottom.Length;
                    col = Bottom[at].from;
                }
                else col = (col + d.x + Cols) % Cols;
            }
            Refresh();
        }

        void Tick()
        {
            if (overlay.panel == null) { Close(false); return; }
            var pad = Gamepad.current;
            if (pad == null) return;
            if (pad.buttonSouth.wasPressedThisFrame) Press();
            if (pad.buttonEast.wasPressedThisFrame) Delete();
            if (pad.buttonWest.wasPressedThisFrame) Type(" ");
            if (pad.buttonNorth.wasPressedThisFrame) { shift = !shift; Refresh(); }
            if (pad.startButton.wasPressedThisFrame) { Close(true); return; }
            if (pad.selectButton.wasPressedThisFrame) { Close(false); return; }
            // The D-pad or the left stick past half way, repeating while held (0.35 s, then every 0.08 s).
            var v = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
            var dir = new Vector2Int(Mathf.Abs(v.x) > 0.5f ? (int)Mathf.Sign(v.x) : 0, Mathf.Abs(v.y) > 0.5f ? -(int)Mathf.Sign(v.y) : 0);
            if (dir == Vector2Int.zero) { heldDir = dir; return; }
            float now = Time.unscaledTime;
            if (dir != heldDir) { heldDir = dir; repeatAt = now + 0.35f; Move(dir); }
            else if (now >= repeatAt) { repeatAt = now + 0.08f; Move(dir); }
        }

        /// <summary>Closes it; 'keep' writes the text into the field.</summary>
        public void Close(bool keep)
        {
            if (Current != this) return;
            Current = null;
            tick?.Pause();
            var tree = overlay.panel?.visualTree;
            if (tree != null)
            {
                tree.UnregisterCallback<NavigationMoveEvent>(Swallow, TrickleDown.TrickleDown);
                tree.UnregisterCallback<NavigationSubmitEvent>(Swallow, TrickleDown.TrickleDown);
                tree.UnregisterCallback<NavigationCancelEvent>(Swallow, TrickleDown.TrickleDown);
            }
            overlay.RemoveFromHierarchy();
            GameControls.TextEntryOpen = false;
            if (keep) field.value = text;
            if (field.panel != null) field.Focus();
        }
    }
}
