// SettingsConfirm.cs
// Rework (docs/UI-DESIGN.md "Options"): after a change of the display mode, the resolution or the interface / text size
// (OptionDef.confirm) the screen asks "Keep these settings?" with a 15 s countdown; Revert, Back or the countdown running
// out put every value changed since back, so a mode the monitor can't show or a size that makes the menus unreadable
// undoes itself. The question comes 0.6 s after the last change, so stepping through the values with left / right asks
// once. It reads the devices itself like PadKeyboard (left / right / Tab pick, A / Enter / Space press, B / Esc revert)
// and the menus ignore their keys meanwhile (GameControls.ModalOpen). Styles: GoF2Common.uss .settings-confirm.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class SettingsConfirm
    {
        const float CountdownSeconds = 15f, SettleSeconds = 0.6f;

        /// <summary>The question on screen, if any.</summary>
        public static SettingsConfirm Current { get; private set; }

        static readonly List<(OptionControl row, int before)> pending = new List<(OptionControl, int)>();
        static VisualElement waitingTree;
        static IVisualElementScheduledItem waiting;
        static float askAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            pending.Clear();
            waitingTree = null;
            waiting = null;
        }

        readonly VisualElement overlay;
        readonly Label text;
        readonly Button keep, revert;
        readonly float endsAt;
        bool revertSelected;
        IVisualElementScheduledItem tick;

        /// <summary>The row's value changed from 'before' (its index): ask to keep it once the changes settle.</summary>
        public static void Changed(OptionControl row, int before)
        {
            if (Current != null || row?.Root?.panel == null) return;
            if (!pending.Exists(p => p.row.def == row.def)) pending.Add((row, before));
            askAt = Time.unscaledTime + SettleSeconds;
            var tree = row.Root.panel.visualTree;
            if (waiting != null && waitingTree == tree) return;
            waiting?.Pause();
            waitingTree = tree;
            waiting = tree.schedule.Execute(Wait).Every(50);
        }

        static void Wait()
        {
            if (Time.unscaledTime < askAt) return;
            waiting?.Pause();
            waiting = null;
            pending.RemoveAll(p => p.row.def.getIndex() == p.before);   // stepped back to where it was: nothing to keep
            if (pending.Count == 0) return;
            var host = HostOf(pending[pending.Count - 1].row.Root) ?? HostOf(waitingTree) ?? waitingTree;
            if (host == null || host.panel == null) { pending.Clear(); return; }
            Current = new SettingsConfirm(host);
        }

        /// <summary>The screen's root (UiLayout.Configure's ui-root, which carries the text size and input classes) above
        /// or under 'e'.</summary>
        static VisualElement HostOf(VisualElement e)
        {
            if (e == null || e.panel == null) return null;
            for (var v = e; v != null; v = v.parent) if (v.ClassListContains("ui-root")) return v;
            return e.Q(className: "ui-root");
        }

        SettingsConfirm(VisualElement host)
        {
            string X(string key, string english) => Localization.Extra(key, english);
            overlay = new VisualElement();
            overlay.AddToClassList("settings-confirm");
            var box = new VisualElement();
            box.AddToClassList("settings-confirm__box");
            overlay.Add(box);
            var title = new Label(X("keepSettingsTitle", "Keep these settings?").UiCaps()) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("settings-confirm__title");
            title.AddToClassList("gof-semibold");
            box.Add(title);
            text = new Label { pickingMode = PickingMode.Ignore };
            text.AddToClassList("settings-confirm__text");
            box.Add(text);
            var buttons = new VisualElement();
            buttons.AddToClassList("settings-confirm__buttons");
            box.Add(buttons);
            keep = new Button(() => Close(true)) { focusable = false };
            keep.AddToClassList("gof-button");
            keep.AddToClassList("gof-button--primary");
            keep.AddToClassList("gof-semibold");
            revert = new Button(() => Close(false)) { focusable = false };
            revert.AddToClassList("gof-button");
            revert.AddToClassList("gof-semibold");
            InputGlyph.PadPrompt(keep, PadButton.A, true);
            InputGlyph.PadPrompt(revert, PadButton.B);
            InputGlyph.SetText(keep, X("keepSettings", "Keep").UiCaps());
            InputGlyph.SetText(revert, X("revertSettings", "Revert").UiCaps());
            keep.RegisterCallback<PointerEnterEvent>(_ => Select(false));
            revert.RegisterCallback<PointerEnterEvent>(_ => Select(true));
            buttons.Add(keep);
            buttons.Add(revert);
            host.Add(overlay);
            var tree = overlay.panel.visualTree;
            tree.RegisterCallback<NavigationMoveEvent>(Swallow, TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationSubmitEvent>(Swallow, TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationCancelEvent>(Swallow, TrickleDown.TrickleDown);
            GameControls.ModalOpen = true;
            endsAt = Time.unscaledTime + CountdownSeconds;
            Select(false);
            UiSounds.Move();
            tick = overlay.schedule.Execute(Tick).Every(0);
            Tick();
        }

        void Swallow(EventBase e)
        {
            e.StopImmediatePropagation();
            overlay.focusController?.IgnoreEvent(e);
        }

        void Select(bool toRevert)
        {
            if (revertSelected != toRevert) UiSounds.Move();
            revertSelected = toRevert;
            keep.EnableInClassList("options-selected", !toRevert);
            revert.EnableInClassList("options-selected", toRevert);
        }

        void Tick()
        {
            if (overlay.panel == null) { Close(true); return; }   // the screen went (a scene change): the values stay
            float left = endsAt - Time.unscaledTime;
            if (left <= 0f) { Close(false); return; }
            text.text = string.Format(Localization.Extra("keepSettingsText", "The previous settings come back in {0} s."), Mathf.CeilToInt(left));
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) { Close(false); return; }
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) { Close(!revertSelected); return; }
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Select(false);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Select(true);
                if (kb.tabKey.wasPressedThisFrame) Select(!revertSelected);
            }
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame) { Close(false); return; }
                if (pad.buttonSouth.wasPressedThisFrame) { Close(!revertSelected); return; }
                var v = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
                if (pad.dpad.left.wasPressedThisFrame || v.x < -0.6f && revertSelected) Select(false);
                if (pad.dpad.right.wasPressedThisFrame || v.x > 0.6f && !revertSelected) Select(true);
            }
        }

        void Close(bool keepValues)
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
            GameControls.ModalOpen = false;
            if (keepValues) UiSounds.Confirm();
            else
            {
                UiSounds.Back();
                // In the order changed, last first: a resolution picked after a display mode goes back before the mode.
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var (row, before) = pending[i];
                    row.def.setIndex(before);
                    if (row.Root.panel != null) row.Refresh();
                }
            }
            pending.Clear();
        }
    }
}
