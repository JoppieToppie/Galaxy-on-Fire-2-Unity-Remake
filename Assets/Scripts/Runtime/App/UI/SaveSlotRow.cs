// SaveSlotRow.cs
// One save slot row (MenuTouchWindow::drawLoadSaveMenu 0x14bfbc), shared by the main menu's Load list and the station
// menu's Save game list: index, "Auto-save" (486) / "Slot N", the preview (RecordHandler::recordStoreWritePreview:
// station, system, ship, difficulty and economy, credits, playing time) or "Empty slot" (the original's 174 "-BLANK-"). Styles: .slot-* in GoF2Common.uss.
// Remake (players' suggestion): a used row is deleted by holding it (a tap / click held, or Delete / the controller's X
// held while it is selected) for HoldSeconds; the row fills red meanwhile (SaveGame.Delete keeps a copy in Saves/Deleted).

using System;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SaveSlotRow
    {
        public const float HoldSeconds = 3f;
        /// <summary>Modified: the hold shows (the red fill, "Hold to delete...") only after this long held: a plain click / tap to
        /// load or save flashed it for a moment. The fill then runs from empty to full over the rest of HoldSeconds.</summary>
        public const float ShowAfterSeconds = 0.35f;

        /// <summary>'onDelete': a used slot can be held to delete it; called with the slot once the hold completes (the caller
        /// deletes it and rebuilds the list).</summary>
        public static Button Build(Database db, int slot, SaveData save, string emptyAutoSaveHint, Action<int> onDelete = null)
        {
            var row = new Button { focusable = true };
            row.AddToClassList("slot-row");
            row.EnableInClassList("slot-row--empty", save == null);

            var index = new Label(slot.ToString("00"));
            index.AddToClassList("slot-index");
            index.AddToClassList("gof-semibold");
            var info = new VisualElement();
            info.AddToClassList("slot-info");
            var name = new Label(slot == SaveGame.AutoSaveSlot ? Localization.Get(486) : $"{Localization.Extra("slot", "Slot")} {slot}");
            name.AddToClassList("slot-name");
            info.Add(name);

            string subText = null;
            if (save != null)
            {
                var st = db.Stations.Find(x => x.index == save.station);
                var ship = db.Ship(save.ship);
                subText = $"{st?.name} · {st?.systemName}  ·  {ship?.name}  ·  {Session.DifficultyName(save.difficulty)}, {Session.EconomyName(SaveGame.SaveEconomy(save))}"
                          + (save.hardcore ? "  ·  " + Localization.Extra("hardcoreTag", "HARDCORE").OgCase() : "")
                          + (save.noMedals ? "  ·  " + Localization.Extra("noMedalsTag", "NO MEDALS").OgCase() : "");
            }
            else if (slot == SaveGame.AutoSaveSlot) subText = emptyAutoSaveHint;
            if (subText != null)
            {
                var sub = new Label(subText);
                sub.AddToClassList("slot-sub");
                info.Add(sub);
            }
            var state = new Label(save == null ? Localization.Extra("slotEmpty", "Empty slot")   // the original: 174 "-BLANK-"
                : $"{save.credits:N0} Cr  ·  {(int)(save.playSeconds / 3600)}:{(int)(save.playSeconds / 60) % 60:00} h");
            state.AddToClassList("slot-state");

            foreach (var e in new VisualElement[] { index, info, state }) e.pickingMode = PickingMode.Ignore;
            row.Add(index);
            row.Add(info);
            row.Add(state);
            if (save != null && onDelete != null) row.userData = new DeleteHold(row, slot, state, onDelete);
            return row;
        }

        /// <summary>The row's press ended a delete hold: its click must not load / save the slot.</summary>
        public static bool HoldUsed(Button row) => row.userData is DeleteHold h && h.used;

        /// <summary>The line under a slot list saying how to delete (by the current input mode); made once, kept current.</summary>
        public static void AttachHint(ScrollView list)
        {
            var parent = list.parent;
            if (parent == null || parent.Q<Label>(className: "slot-delete-hint") != null) return;
            var hint = new Label { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("slot-delete-hint");
            parent.Insert(parent.IndexOf(list) + 1, hint);
            void Refresh() => hint.text = InputMode.Current == InputKind.Gamepad ? Localization.Extra("slotDeleteHintPad", "Hold X on a save to delete it")
                : InputMode.Current == InputKind.Touch ? Localization.Extra("slotDeleteHintTouch", "Hold a save to delete it")
                : Localization.Extra("slotDeleteHintKeys", "Hold Delete or click and hold a save to delete it");
            Refresh();
            hint.schedule.Execute(Refresh).Every(500);
        }

        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
        sealed class DeleteHold
        {
            enum Source { None, Pointer, Key }

            /// <summary>A key hold that deleted a row: the key must be let go before the next row (focused in its place) can
            /// start one.</summary>
            static bool keyLatch;

            readonly Button row;
            readonly int slot;
            readonly Label state;
            readonly string stateText;
            readonly Action<int> onDelete;
            readonly VisualElement fill;
            Source source;
            float startTime;
            Vector2 pointerStart;
            public bool used;

            public DeleteHold(Button row, int slot, Label state, Action<int> onDelete)
            {
                this.row = row;
                this.slot = slot;
                this.state = state;
                stateText = state.text;
                this.onDelete = onDelete;
                fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("slot-delete-fill");
                row.Insert(0, fill);
                row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) Begin(Source.Pointer, e.position); }, TrickleDown.TrickleDown);
                row.RegisterCallback<PointerMoveEvent>(e =>
                {
                    // A drag (the list scrolling) is no hold.
                    if (source == Source.Pointer && ((Vector2)e.position - pointerStart).sqrMagnitude > 15f * 15f) Cancel();
                }, TrickleDown.TrickleDown);
                row.RegisterCallback<PointerUpEvent>(_ => { if (source == Source.Pointer) Cancel(); }, TrickleDown.TrickleDown);
                row.RegisterCallback<PointerCancelEvent>(_ => { if (source == Source.Pointer) Cancel(); });
                row.RegisterCallback<PointerLeaveEvent>(_ => { if (source == Source.Pointer) Cancel(); });
                row.RegisterCallback<PointerCaptureOutEvent>(_ => { if (source == Source.Pointer) Cancel(); });
                row.schedule.Execute(Tick).Every(16);
            }

            void Begin(Source s, Vector2 at)
            {
                if (used) return;
                source = s;
                startTime = Time.realtimeSinceStartup;
                pointerStart = at;
            }

            void Cancel()
            {
                source = Source.None;
                fill.style.width = Length.Percent(0);
                row.RemoveFromClassList("slot-row--deleting");
                state.text = stateText;
            }

            void Tick()
            {
                if (used || row.panel == null) return;
                bool key = (Keyboard.current != null && Keyboard.current.deleteKey.isPressed)
                           || (Gamepad.current != null && Gamepad.current.buttonWest.isPressed);
                if (!key) keyLatch = false;
                bool focused = row.focusController?.focusedElement == row;
                if (source == Source.None && key && focused && !keyLatch) Begin(Source.Key, default);
                if (source == Source.Key && (!key || !focused)) Cancel();
                if (source == Source.None) return;

                float held = Time.realtimeSinceStartup - startTime;
                if (held < ShowAfterSeconds) return;   // Modified: a click, so far (ShowAfterSeconds)
                float t = (held - ShowAfterSeconds) / (HoldSeconds - ShowAfterSeconds);
                fill.style.width = Length.Percent(Mathf.Clamp01(t) * 100f);
                row.AddToClassList("slot-row--deleting");
                state.text = Localization.Extra("slotHoldDelete", "Hold to delete...");
                if (t < 1f) return;
                if (source == Source.Key) keyLatch = true;
                used = true;
                source = Source.None;
                onDelete(slot);
            }
        }
    }
}
