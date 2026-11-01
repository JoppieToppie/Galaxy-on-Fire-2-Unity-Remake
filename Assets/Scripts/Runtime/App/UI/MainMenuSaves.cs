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
                var row = SaveSlotRow.Build(db, i, save, Localization.Extra("autosaveHint", "Saved automatically when you dock"), DeleteSlot);
                row.clicked += () =>
                {
                    if (SaveSlotRow.HoldUsed(row)) return;
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
                HookFocusSound(row);
                list.Add(row);
            }
            SaveSlotRow.AttachHint(list);
        }

        /// <summary>The Load panel as itself, or as the co-op host's save picker (coopSlotPick, OpenCoopSlots): its own title,
        /// none of the import / export rows.</summary>
        void ApplyLoadPanelMode()
        {
            if (root == null) return;
            if (root.Q<Label>("loadTitle") is Label title)
                title.text = (coopSlotPick ? Localization.Extra("coopPickTitle", "Host a save") : Localization.Get(29)).ToUpperInvariant();
            void Show(string name, bool on) { if (root.Q(name) is VisualElement e) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }
            Show("saveTransferRow", !coopSlotPick);
            Show("originalSaveRow", !coopSlotPick && Modding.ModImport.Available);
            Show("saveTransferNote", !coopSlotPick && !SaveTransfer.HasFileDialog);
        }

        /// <summary>Remake: a slot held down (SaveSlotRow): deleted, the list rebuilt with the same row selected.</summary>
        void DeleteSlot(int slot)
        {
            if (!SaveGame.Delete(slot)) return;
            Play(buttonRelease);
            root.schedule.Execute(() =>
            {
                BuildSlots();
                var list = root.Q<ScrollView>("slotList");
                if (slot < list.contentContainer.childCount) Select(list.contentContainer.ElementAt(slot));
                resumeButton.EnableInClassList("menu-button--gone", SaveGame.MostRecentSlot() < 0);
                UpdateColumnFit();
            });
        }

        string SaveTransferTitle(bool import) => (import ? Localization.Extra("importSaves", "Import saves") : Localization.Extra("exportSaves", "Export saves"));

        void ExportSaves()
        {
            if (dialog.ClassListContains("dialog-backdrop--shown")) return;
            var r = SaveTransfer.Export();
            switch (r.outcome)
            {
                case SaveTransfer.Outcome.Done:
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
            if (dialog.ClassListContains("dialog-backdrop--shown")) return;
            string path = SaveTransfer.PickImportFile();
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
                        System.IO.Path.GetFileName(path), r.problem));
                    return;
            }
            // Step 2: the warning; nothing is written before Yes.
            ShowDialog(SaveTransferTitle(true),
                string.Format(Localization.Extra("importWarning",
                    "WARNING: importing overwrites ALL your existing saves, the auto-save too, with the {0} saves in \"{1}\". Export them first if you want to keep them. Import anyway?"),
                    r.slots, System.IO.Path.GetFileName(path)),
                () => root.schedule.Execute(() => ImportSaves(path)).ExecuteLater(1));
        }

        void ImportSaves(string path)
        {
            var r = SaveTransfer.Import(path);
            BuildSlots();   // the Load game list shows the imported saves
            resumeButton.EnableInClassList("menu-button--gone", SaveGame.MostRecentSlot() < 0);
            UpdateColumnFit();
            if (r.outcome == SaveTransfer.Outcome.Done)
                ShowNotice(SaveTransferTitle(true), string.Format(Localization.Extra("importDone", "{0} saves imported."), r.slots));
            else
                ShowNotice(SaveTransferTitle(true), string.Format(Localization.Extra("importInvalid", "This file can't be imported, your saves are unchanged.\n{0}: {1}"),
                    System.IO.Path.GetFileName(path), r.problem));
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
                ShowNotice(OriginalImportTitle, Localization.Extra("originalImportNoSlot", "No empty save slot: hold a save in the list to delete it."));
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
