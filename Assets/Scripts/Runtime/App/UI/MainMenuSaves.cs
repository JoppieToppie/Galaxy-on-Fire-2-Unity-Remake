// MainMenuSaves.cs
// Part of MainMenu: the save slots (load, list, delete), the save export / import, the original game's saves and the
// save folder (pick, sync).

using System;
using System.Collections;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        /// <summary>GameRecord::load: the saved (docked) state, then the station.</summary>
        void LoadSlot(int slot)
        {
            if (slot < 0) return;
            // A save made with mods that aren't on now: say what happens to their items first (Modding.ModSaves).
            var missing = Modding.ModSaves.MissingMods(SaveGame.Preview(slot));
            if (missing.Count > 0)
            {
                ShowDialog(Localization.Extra("modsMissingTitle", "Mods missing"), string.Format(Localization.Extra("modsMissingText",
                    "This game was saved with mods that aren't on: {0}.\nTheir items will be removed and refunded. Turn the mods on in Mods to keep them.\n\nLoad anyway?"),
                    string.Join(", ", missing)), () => { if (SaveGame.Load(slot)) StartCoroutine(Leave("Station")); });
                return;
            }
            if (!SaveGame.Load(slot)) return;
            StartCoroutine(Leave("Station"));
        }

        void BuildSlots()
        {
            var list = root.Q<ScrollView>("slotList");
            list.Clear();
            var db = Database.Load();
            for (int i = 0; i < SaveGame.SlotCount; i++)
            {
                int slot = i;
                var save = SaveGame.Preview(i);
                var row = SaveSlotRow.Build(db, i, save, Localization.Extra("autosaveHint", "Saved automatically when you dock"));
                row.clicked += () =>
                {
                    if (save == null) { Play(buttonPush); return; }
                    if (slotChosen) return;
                    // Modified: a save chosen to load sounds and flashes like the menu buttons (their click sound; the pressed
                    // amber kept a moment: slot-row--chosen, MainMenu.uss) before the game loads.
                    Play(buttonRelease);
                    slotChosen = true;
                    row.AddToClassList("slot-row--chosen");
                    row.schedule.Execute(() => row.RemoveFromClassList("slot-row--chosen")).ExecuteLater(SlotFlashMs);
                    row.schedule.Execute(() => { slotChosen = false; if (coopSlotPick) PickCoopSlot(slot); else LoadSlot(slot); }).ExecuteLater(SlotFlashMs * 2);
                };
                row.RegisterCallback<PointerUpEvent>(ev =>
                {
                    if (ev.button != 1 || save == null || coopSlotPick || slotChosen
                        || openPanel != panels["loadPanel"] || dialog.ClassListContains("dialog-backdrop--shown")) return;
                    ev.StopPropagation();
                    EditCampaignRules(slot);
                });
                HookFocusSound(row);
                list.Add(row);
            }
            var editHint = root.Q<Label>("campaignEditHint");
            editHint.text = X("editCampaignHint", "Right-click a save to edit its settings.");
            editHint.style.display = !coopSlotPick && InputMode.Current == InputKind.KeyboardMouse ? DisplayStyle.Flex : DisplayStyle.None;
        }

        int editingCampaignSlot;
        string campaignSnapshot;
        SaveGame.CampaignRules campaignEdit;

        static string CampaignEditorTitle => X("editCampaignSettings", "Edit save settings");

        void EditCampaignRules(int slot)
        {
            if (!SaveGame.TryReadCampaignRules(slot, out campaignSnapshot, out campaignEdit))
            {
                CampaignEditFailed();
                return;
            }
            editingCampaignSlot = slot;
            BuildCampaignEditor();
            OpenPanel("editCampaignPanel");
        }

        void CampaignEditFailed() => ShowNotice(CampaignEditorTitle,
            X("editCampaignFailed", "The save could not be updated. Reopen it and try again."));

        void ApplyCampaignEdit()
        {
            if (!SaveGame.TryWriteCampaignRules(editingCampaignSlot, campaignSnapshot, campaignEdit))
            {
                CampaignEditFailed();
                return;
            }
            BuildSlots();
            Back();
        }

        void BuildCampaignEditor()
        {
            root.Q<Label>("campaignRulesTitle").text = CampaignEditorTitle.UiCaps();
            root.Q<Button>("campaignRulesDelete").text = DeleteSaveLabel.UiCaps();
            root.Q<Button>("campaignRulesApply").text = OptionsCatalog.ApplyLabel.UiCaps();
            root.Q<Button>("campaignRulesCancel").text = X("kbCancel", "Cancel").UiCaps();
            var list = root.Q<ScrollView>("campaignRuleList");
            list.Clear();
            string off = X("kaamoVipOff", "Off");
            string on = X("on", "On");
            var vip = new ChoiceRow(false) { LabelText = X("kaamoVip", "Kaamo Club VIP Card") };
            var vipValues = new[] { off, X("kaamoVipCard", "Buy at the club") };
            void RefreshVip()
            {
                vip.SetEnabled(campaignEdit.UpgradeLimit > 0);
                vip.SetWithoutNotify(vipValues, campaignEdit.UpgradeLimit > 0 && campaignEdit.VipAvailable ? 1 : 0);
            }
            void Add(ChoiceRow row)
            {
                HookFocusSound(row);
                row.RegisterCallback<FocusInEvent>(_ => list.ScrollTo(row));
                list.Add(row);
            }
            void Choice(string title, string[] values, int index, Action<int> change)
            {
                var row = new ChoiceRow(false) { LabelText = title };
                row.SetWithoutNotify(values, index);
                row.Changed += change;
                Add(row);
            }
            void Toggle(string title, SavedGameRules rule)
            {
                Choice(title, new[] { off, on }, (campaignEdit.Rules & rule) != 0 ? 1 : 0, i =>
                {
                    if (i == 0) campaignEdit.Rules &= ~rule;
                    else campaignEdit.Rules |= rule;
                });
            }
            float[] difficulties = { Session.DifficultyEasy, Session.DifficultyNormal, Session.DifficultyHard, Session.DifficultyExtreme };
            Choice(X("difficulty", "Difficulty"), Array.ConvertAll(difficulties, Session.DifficultyName),
                Array.IndexOf(difficulties, campaignEdit.Difficulty), i => campaignEdit.Difficulty = difficulties[i]);
            vip.Changed += i => campaignEdit.VipAvailable = i == 1;
            RefreshVip();
            Add(vip);
            string[] limits = campaignEdit.UpgradeLimit > 5
                ? new[] { off, "1", "2", "3", "4", "5", campaignEdit.UpgradeLimit.ToString() }
                : new[] { off, "1", "2", "3", "4", "5" };
            int legacyLimit = campaignEdit.UpgradeLimit;
            Choice(X("kaamoUpgradeRules", "Kaamo Club upgrades"), limits, Math.Min(6, legacyLimit), i =>
            {
                campaignEdit.UpgradeLimit = i == 6 ? legacyLimit : i;
                RefreshVip();
            });
            Toggle(X("blueprintAutocomplete", "Blueprint autocomplete"), SavedGameRules.BlueprintAutocomplete);
            Toggle(X("cloakBay", "Cloak bay"), SavedGameRules.CloakBay);
            Toggle(X("pirateEvents", "Pirate outposts and bosses"), SavedGameRules.PirateEvents);
            Toggle(X("capitalShips", "Capital ship enhancements"), SavedGameRules.CapitalShips);
            Toggle(X("derelictWrecks", "Derelict freighters"), SavedGameRules.DerelictWrecks);
            Toggle(X("khadorSlipstream", "Khador Slipstream"), SavedGameRules.KhadorSlipstream);
        }

        /// <summary>The Load panel as itself, or as the co-op host's save picker (coopSlotPick, OpenCoopSlots): its own title,
        /// none of the import / export rows.</summary>
        void ApplyLoadPanelMode()
        {
            if (root == null) return;
            if (root.Q<Label>("loadTitle") is Label title)
                title.text = (coopSlotPick ? Localization.Extra("coopPickTitle", "Host a save") : Localization.Get(29)).UiCaps();
            void Show(string name, bool on) { if (root.Q(name) is VisualElement e) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }
            Show("saveTransferRow", !coopSlotPick);
            Show("originalSaveRow", !coopSlotPick && Modding.ModImport.Available);
            Show("saveTransferNote", !coopSlotPick && !SaveTransfer.HasFileDialog && !SavesPicker);
        }

        static string DeleteSaveLabel => X("modsDelete", "Delete");
        Action pendingSaveDeletion;
        int deleteHoldPointer = -1;
        int deleteHoldAttempt;

        void AskDeleteCampaignSave()
        {
            int slot = editingCampaignSlot;
            string snapshot = campaignSnapshot;
            pendingSaveDeletion = () =>
            {
                if (!SaveGame.TryReadCampaignRules(slot, out var current, out _) || current != snapshot || !SaveGame.Delete(slot))
                {
                    ShowNotice(DeleteSaveLabel, X("deleteSaveFailed", "The save could not be deleted. Reopen it and try again."));
                    return;
                }
                BuildSlots();
                resumeButton.EnableInClassList("menu-button--gone", SaveGame.MostRecentSlot() < 0);
                UpdateColumnFit();
                Back();
            };
            ShowDialog(DeleteSaveLabel, X("deleteSaveHoldHint", "Press and hold DELETE to confirm."), pendingSaveDeletion);
            dialogCheck = () => false;
            dialogRelabelled = true;
            InputGlyph.SetText(root.Q<Button>("dialogYes"), DeleteSaveLabel.UiCaps());
            InputGlyph.SetText(root.Q<Button>("dialogNo"), X("kbCancel", "Cancel").UiCaps());
        }

        void RegisterDeleteHold(Button button)
        {
            button.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (ev.button != 0 || deleteHoldPointer >= 0 || pendingSaveDeletion == null || dialogYes != pendingSaveDeletion) return;
                deleteHoldPointer = ev.pointerId;
                StartCoroutine(HoldDeleteConfirmation(pendingSaveDeletion, ++deleteHoldAttempt));
            }, TrickleDown.TrickleDown);
            void EndHold(int pointerId) { if (deleteHoldPointer == pointerId) deleteHoldPointer = -1; }
            button.RegisterCallback<PointerMoveEvent>(ev =>
            {
                if (!button.ContainsPoint(ev.localPosition)) EndHold(ev.pointerId);
            });
            button.RegisterCallback<PointerUpEvent>(ev => EndHold(ev.pointerId), TrickleDown.TrickleDown);
            button.RegisterCallback<PointerCancelEvent>(ev => EndHold(ev.pointerId));
            button.RegisterCallback<PointerCaptureOutEvent>(ev => EndHold(ev.pointerId));
        }

        IEnumerator HoldDeleteConfirmation(Action delete, int attempt)
        {
            var bar = root.Q("dialogProgress");
            var fill = root.Q("dialogProgressFill");
            float start = Time.unscaledTime;
            bar.AddToClassList("dialog-progress--shown");
            while (attempt == deleteHoldAttempt && dialogYes == delete && deleteHoldPointer >= 0 && Application.isFocused)
            {
                float progress = (Time.unscaledTime - start) / SaveSlotRow.HoldSeconds;
                fill.style.width = Length.Percent(Mathf.Min(1f, progress) * 100f);
                if (progress >= 1f)
                {
                    deleteHoldPointer = -1;
                    dialogCheck = null;
                    ConfirmDialog();
                    yield break;
                }
                yield return null;
            }
            if (attempt != deleteHoldAttempt) yield break;
            deleteHoldPointer = -1;
            if (dialogYes == delete) bar.RemoveFromClassList("dialog-progress--shown");
        }

        /// <summary>Android: Export / Import saves go through the system's document picker (ModImportActivity), as the
        /// Transfer folder in Android/data is out of other apps' reach; the export still writes its copy there first.</summary>
        static bool SavesPicker => Application.platform == RuntimePlatform.Android;

        /// <summary>Calls 'poll' (a ModImport picker) every 200 ms until it answers, then 'done'.</summary>
        void PollPicker(Func<Modding.ModImport.Picked> poll, Action<Modding.ModImport.Picked> done)
        {
            Modding.ModImport.Picked picked = null;
            root.schedule.Execute(() =>
            {
                picked = poll();
                if (picked != null) done(picked);
            }).Every(200).Until(() => picked != null);
        }

        string SaveTransferTitle(bool import) => (import ? Localization.Extra("importSaves", "Import saves") : Localization.Extra("exportSaves", "Export saves"));

        void ExportSaves()
        {
            if (pickingSaves || dialog.ClassListContains("dialog-backdrop--shown")) return;
            var r = SaveTransfer.Export();
            switch (r.outcome)
            {
                case SaveTransfer.Outcome.Done when SavesPicker:
                    var saveAs = Modding.ModImport.SaveAs(r.path, System.IO.Path.GetFileName(r.path));
                    if (saveAs == null) goto default;
                    pickingSaves = true;
                    PollPicker(saveAs, p =>
                    {
                        pickingSaves = false;
                        if (p.cancelled) return;
                        ShowNotice(SaveTransferTitle(false), p.error != null
                            ? Localization.Extra("exportFailed", "Export failed:") + " " + p.error
                            : string.Format(Localization.Extra("exportDone", "{0} saves exported to:\n{1}"), r.slots, p.name));
                    });
                    break;
                case SaveTransfer.Outcome.Done:
                default:
                    ShowNotice(SaveTransferTitle(false), string.Format(Localization.Extra("exportDone", "{0} saves exported to:\n{1}"), r.slots, r.path));
                    break;
                case SaveTransfer.Outcome.NothingToExport:
                    ShowNotice(SaveTransferTitle(false), Localization.Extra("exportNothing", "There are no saves to export."));
                    break;
                case SaveTransfer.Outcome.Failed:
                    ShowNotice(SaveTransferTitle(false), Localization.Extra("exportFailed", "Export failed:") + " " + r.problem);
                    break;
            }
        }

        /// <summary>Import, step 1: the file (picked, or the newest in the Transfer folder), checked whole; then the warning.</summary>
        void PickImport()
        {
            if (pickingSaves || dialog.ClassListContains("dialog-backdrop--shown")) return;
            if (SavesPicker)
            {
                pickingSaves = true;
                PollPicker(Modding.ModImport.Pick(SaveTransferTitle(true), "Galaxy on Fire 2 saves", new[] { SaveTransfer.Extension }), p =>
                {
                    pickingSaves = false;
                    if (p.cancelled) return;
                    if (p.error != null) ShowNotice(SaveTransferTitle(true), string.Format(Localization.Extra("importInvalid",
                        "This file can't be imported, your saves are unchanged.\n{0}: {1}"), p.name ?? "", p.error));
                    else CheckImport(p.path, p.name);
                });
                return;
            }
            string path = SaveTransfer.PickImportFile();
            CheckImport(path, System.IO.Path.GetFileName(path));
        }

        /// <summary>Import, step 1b: 'path' checked whole ('name' the file's own, the picker's copy is renamed); the warning.</summary>
        void CheckImport(string path, string name)
        {
            var r = SaveTransfer.Check(path);
            switch (r.outcome)
            {
                case SaveTransfer.Outcome.Cancelled:
                    return;
                case SaveTransfer.Outcome.NoFile:
                    ShowNotice(SaveTransferTitle(true), string.Format(Localization.Extra("importNoFile", "No exported saves (.{0}) found in:\n{1}"),
                        SaveTransfer.Extension, SaveTransfer.TransferFolder));
                    return;
                case SaveTransfer.Outcome.Invalid:
                case SaveTransfer.Outcome.Failed:
                    ShowNotice(SaveTransferTitle(true), string.Format(Localization.Extra("importInvalid", "This file can't be imported, your saves are unchanged.\n{0}: {1}"),
                        name, r.problem));
                    return;
            }
            // Step 2: the warning; nothing is written before Yes.
            ShowDialog(SaveTransferTitle(true),
                string.Format(Localization.Extra("importWarning",
                    "WARNING: importing overwrites ALL your existing saves, the auto-save too, with the {0} saves in \"{1}\". Export them first if you want to keep them. Import anyway?"),
                    r.slots, name),
                () => root.schedule.Execute(() => ImportSaves(path, name)).ExecuteLater(1));
        }

        void ImportSaves(string path, string name)
        {
            var r = SaveTransfer.Import(path);
            BuildSlots();   // the Load game list shows the imported saves
            resumeButton.EnableInClassList("menu-button--gone", SaveGame.MostRecentSlot() < 0);
            UpdateColumnFit();
            if (r.outcome == SaveTransfer.Outcome.Done)
                ShowNotice(SaveTransferTitle(true), string.Format(Localization.Extra("importDone", "{0} saves imported."), r.slots));
            else
                ShowNotice(SaveTransferTitle(true), string.Format(Localization.Extra("importInvalid", "This file can't be imported, your saves are unchanged.\n{0}: {1}"),
                    name, r.problem));
        }

        /// <summary>Import, step 1: the system's file picker (polled until it answers, like the mod browser's).</summary>
        void PickOriginalImport()
        {
            if (pickingOriginal || dialog.ClassListContains("dialog-backdrop--shown")) return;
            pickingOriginal = true;
            var poll = Modding.ModImport.Pick(OriginalImportTitle, "Galaxy on Fire 2 saves (gof2_save_game_N)", new[] { "*" });
            Modding.ModImport.Picked picked = null;
            root.schedule.Execute(() =>
            {
                picked = poll();
                if (picked != null) { pickingOriginal = false; OriginalPicked(picked); }
            }).Every(200).Until(() => picked != null);
        }

        /// <summary>Step 2: the file read and converted (nothing written yet), then the question with what it is.</summary>
        void OriginalPicked(Modding.ModImport.Picked p)
        {
            if (p.cancelled) return;
            if (p.error != null) { ShowNotice(OriginalImportTitle, Localization.Extra("originalImportFailed", "The save couldn't be imported:") + " " + p.error); return; }
            var imported = OriginalSaveTransfer.Inspect(p.path);
            Modding.ModImport.Discard(p.path);
            if (imported.problem != null)
            {
                ShowNotice(OriginalImportTitle, Localization.Extra("originalImportFailed", "The save couldn't be imported:") + "\n" + p.name + ": " + imported.problem
                    + "\n" + Localization.Extra("originalImportHint", "Pick a gof2_save_game_N file from the original game's save folder."));
                return;
            }
            int slot = OriginalSaveTransfer.FreeSlot();
            if (slot < 0)
            {
                ShowNotice(OriginalImportTitle, Localization.Extra("originalImportNoSlot", "No empty save slot. Right-click a save and choose Delete to free a slot."));
                return;
            }
            string notes = imported.notes.Count > 0 ? "\n(" + string.Join("; ", imported.notes) + ")" : "";
            ShowDialog(OriginalImportTitle,
                string.Format(Localization.Extra("originalImportAsk", "{0}\nfrom {1}.\nImport it into save slot {2}?"), imported.summary, imported.version, slot) + notes,
                () => root.schedule.Execute(() =>
                {
                    string problem = OriginalSaveTransfer.Import(imported, slot);
                    BuildSlots();
                    resumeButton.EnableInClassList("menu-button--gone", SaveGame.MostRecentSlot() < 0);
                    UpdateColumnFit();
                    ShowNotice(OriginalImportTitle, problem == null
                        ? string.Format(Localization.Extra("originalImportDone", "Imported into save slot {0}. Load it from the list."), slot)
                        : Localization.Extra("originalImportFailed", "The save couldn't be imported:") + " " + problem);
                }).ExecuteLater(1));
        }

        /// <summary>Export: what it does and which game takes the files, then every slot written.</summary>
        void AskOriginalExport()
        {
            if (dialog.ClassListContains("dialog-backdrop--shown")) return;
            ShowDialog(OriginalExportTitle, Localization.Extra("originalExportAsk",
                "Write every slot as original Android save files into a new folder? They load in the community builds; mod items are left out."),
                () => root.schedule.Execute(() =>
                {
                    var r = OriginalSaveTransfer.Export();
                    if (r.problem != null) { ShowNotice(OriginalExportTitle, Localization.Extra("exportFailed", "Export failed:") + " " + r.problem); return; }
                    string notes = r.notes.Count > 0 ? "\n(" + string.Join("; ", r.notes) + ")" : "";
                    ShowNotice(OriginalExportTitle, string.Format(Localization.Extra("exportDone", "{0} saves exported to:\n{1}"), r.slots, r.folder) + notes);
                }).ExecuteLater(1));
        }

        static string X(string key, string english) => Localization.Extra(key, english);

        /// <summary>On entering the menu and before a save list: the folder's newer saves in, this device's out.</summary>
        void SyncSaveFolder(bool tellIfUnreachable)
        {
            if (!SaveFolder.Chosen) return;
            var r = SaveFolder.Sync();
            if (r.outcome == SaveFolder.Outcome.Unreachable && tellIfUnreachable && !saveFolderNoticeShown)
            {
                saveFolderNoticeShown = true;
                root.schedule.Execute(() =>
                {
                    if (dialog.ClassListContains("dialog-backdrop--shown")) return;
                    ShowNotice(SaveFolderTitle, string.Format(X("saveFolderUnreachableNotice",
                        "Can't reach the save folder {0} ({1}). Saves stay on this device and sync when it's back."),
                        SaveFolder.DisplayName, r.problem));
                }).ExecuteLater(300);
            }
            else if (r.outcome == SaveFolder.Outcome.Done) saveFolderNoticeShown = false;
            if (r.Changed) RefreshAfterSaveFolder();
        }

        void RefreshAfterSaveFolder()
        {
            resumeButton?.EnableInClassList("menu-button--gone", SaveGame.MostRecentSlot() < 0);
            UpdateColumnFit();
            foreach (var o in optionControls) o.Refresh();
            if (openPanel != null && openPanel.name == "loadPanel") BuildSlots();
        }

        /// <summary>The row's press: pick a folder, or (one is chosen) pick another or go back to this device only.</summary>
        void ChooseSaveFolder()
        {
            if (pickingSaveFolder || dialog.ClassListContains("dialog-backdrop--shown")) return;
            if (!SaveFolder.Chosen) { root.schedule.Execute(PickSaveFolder).ExecuteLater(1); return; }
            ShowChoice(SaveFolderTitle,
                string.Format(X("saveFolderCurrent", "Your saves are also kept in:\n{0}"), SaveFolder.DisplayName),
                X("saveFolderOther", "Choose another"), () => root.schedule.Execute(PickSaveFolder).ExecuteLater(1),
                X("saveFolderUseDefault", "This device only"), () =>
                {
                    SaveFolder.UseDefault();
                    ShowNotice(SaveFolderTitle, X("saveFolderDefaultDone", "Saves are kept on this device only again."));
                    RefreshAfterSaveFolder();
                });
        }

        void PickSaveFolder()
        {
            if (pickingSaveFolder) return;
            pickingSaveFolder = true;
            var poll = SaveFolderPicker.Pick();
            SaveFolderPicker.Picked picked = null;
            root.schedule.Execute(() =>
            {
                picked = poll();
                if (picked != null) { pickingSaveFolder = false; SaveFolderPicked(picked); }
            }).Every(200).Until(() => picked != null);
        }

        void SaveFolderPicked(SaveFolderPicker.Picked p)
        {
            if (p.cancelled) return;
            if (p.error != null) { ShowNotice(SaveFolderTitle, X("saveFolderFailed", "The folder can't be used:") + " " + p.error); return; }
            if (p.target == SaveFolder.Target) { SyncSaveFolder(false); return; }
            if (SamePlace(p.target, SaveGame.Folder))
            {
                ShowNotice(SaveFolderTitle, X("saveFolderOwn", "That is the game's own save folder. Choose another one."));
                return;
            }
            var s = SaveFolder.Inspect(p.target);
            if (s.problem != null) { ShowNotice(SaveFolderTitle, X("saveFolderFailed", "The folder can't be used:") + " " + s.problem); return; }
            void Take(SaveFolder.Start start)
            {
                var r = SaveFolder.Choose(p.target, p.name, start);
                RefreshAfterSaveFolder();
                ShowNotice(SaveFolderTitle, r.outcome == SaveFolder.Outcome.Done
                    ? string.Format(X("saveFolderDone", "Your saves are now also kept in:\n{0}"), SaveFolder.DisplayName)
                    : X("saveFolderFailed", "The folder can't be used:") + " " + r.problem);
            }
            if (s.localSaves == 0) { Take(SaveFolder.Start.Merge); return; }
            if (s.folderSaves == 0)
                ShowChoice(SaveFolderTitle,
                    string.Format(X("saveFolderCopyQuestion", "Copy your {0} saves into {1}?"), s.localSaves, p.name),
                    X("saveFolderCopy", "Copy them"), () => Take(SaveFolder.Start.Merge),
                    X("saveFolderStartEmpty", "Start empty"), () => Take(SaveFolder.Start.FolderOnly));
            else
                ShowChoice(SaveFolderTitle,
                    string.Format(X("saveFolderMergeQuestion",
                        "{0} holds {1} saves, this device {2}. Keep the newer save of each slot, or only the folder's?"),
                        p.name, s.folderSaves, s.localSaves),
                    X("saveFolderMerge", "Keep the newer"), () => Take(SaveFolder.Start.Merge),
                    X("saveFolderUseFolder", "Only the folder's"), () => Take(SaveFolder.Start.FolderOnly));
        }

        static bool SamePlace(string a, string b)
        {
            try
            {
                if (a.StartsWith("content://", StringComparison.Ordinal)) return false;
                return string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('/', '\\'), System.IO.Path.GetFullPath(b).TrimEnd('/', '\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }
}
