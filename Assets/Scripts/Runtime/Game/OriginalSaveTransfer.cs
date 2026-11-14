// OriginalSaveTransfer.cs
// Remake: saves of the original game in and out (the main menu's Load panel). Import: one gof2_save_game_N file of the
// Android or PC version (OriginalSave, OriginalSaveConvert.Read finds which and its economy) into a free save slot, after
// SaveGame.TryParse checked the converted save like an imported one. Export: every save slot as the Android versions' files,
// gof2_save_game_N and gof2_save_game_preview_N for slot N (the original's 12 slots, Globals::recordSlots, the same
// numbers), signed with the community builds' key, into a new folder in the Transfer folder; copied into
// Android/data/net.fishlabs.gof2hdallandroid2012/files they load in those builds (Fishlabs' own build signs with the
// phone's device ID and refuses them).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class OriginalSaveTransfer
    {
        const long MaxFileBytes = 4L * 1024 * 1024;

        public class Imported
        {
            public SaveData save;
            public string summary;              // "Var Hastra · step 20 · 12 345$" (the confirmation)
            public string version;              // which game wrote it (English)
            public List<string> notes = new List<string>();
            public string problem;              // why not (English); null = fine
        }

        public class Exported
        {
            public string folder, problem;
            public int slots;
            public List<string> notes = new List<string>();
        }

        /// <summary>The blueprint recipe lengths of an economy (OriginalSave.IngredientCounts needs that economy's tables).</summary>
        static int[] Recipes(Economy economy)
        {
            var saved = Session.Economy;
            try { Session.Economy = economy; return OriginalSave.IngredientCounts(Database.Load()); }
            finally { Session.Economy = saved; }
        }

        /// <summary>Reads and converts a file without writing anything.</summary>
        public static Imported Inspect(string path)
        {
            var res = new Imported();
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) { res.problem = "the file is gone"; return res; }
                if (info.Length > MaxFileBytes) { res.problem = "the file is too large to be a Galaxy on Fire 2 save"; return res; }
                var bytes = File.ReadAllBytes(path);
                var read = OriginalSaveConvert.Read(bytes, Recipes(Economy.Android), Recipes(Economy.Default));
                if (read.record == null) { res.problem = "not a save of the original game (" + read.problem + ")"; return res; }
                res.version = !read.signedFile ? "the PC Full HD version"
                            : read.signatureValid ? "the Android version (community build)" : "the Android version";
                var saved = Session.Economy;
                Database db;
                try { Session.Economy = read.economy; db = Database.Load(); }
                finally { Session.Economy = saved; }
                var save = OriginalSaveConvert.ToSave(read.record, db, read.economy, res.notes);
                save.mods = new List<Modding.SaveMod>();
                save.modKeys = new List<Modding.SaveModKey>();
                // The same checks as any imported save (an index out of range would throw wherever the game looks it up).
                if (!SaveGame.TryParse(JsonUtility.ToJson(save), db, out _, out string why)) { res.problem = "it doesn't convert into a valid save (" + why + ")"; return res; }
                res.save = save;
                var st = db.Stations.Find(x => x.index == save.station);
                res.summary = $"{st?.name ?? "?"} · {Localization.Extra("originalSaveStep", "step")} {save.campaignMission} · {save.credits:N0}$";
                if (read.economy == Economy.Default) res.notes.Add("the PC / iOS economy (Default)");
            }
            catch (Exception e) { res.problem = "reading the file failed: " + e.Message; }
            return res;
        }

        /// <summary>The first empty manual slot (1..11), -1 = none.</summary>
        public static int FreeSlot()
        {
            for (int i = 1; i < SaveGame.SlotCount; i++) if (!SaveGame.Exists(i)) return i;
            return -1;
        }

        /// <summary>Writes an inspected save into 'slot' (empty); the problem or null.</summary>
        public static string Import(Imported imported, int slot)
        {
            if (NetSession.Active) return "not during a multiplayer session";
            if (imported?.save == null) return imported?.problem ?? "nothing to import";
            if (SaveGame.SelectedMode != GameMode.Standard) return "original saves belong to the standard game";
            if (slot < 0 || slot >= SaveGame.SlotCount) return "no free save slot";
            if (SaveGame.Exists(slot)) return $"slot {slot} isn't empty";
            try
            {
                Directory.CreateDirectory(SaveGame.Folder);
                string tmp = SaveGame.SlotPath(slot) + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(imported.save, true));
                File.Move(tmp, SaveGame.SlotPath(slot));
            }
            catch (Exception e) { return "writing the save failed: " + e.Message; }
            Debug.Log($"OriginalSaveTransfer: {imported.summary} imported into slot {slot}" + (imported.notes.Count > 0 ? " (" + string.Join("; ", imported.notes) + ")" : ""));
            SaveFolder.AfterLocalChange();   // remake: into the chosen save folder too
            return null;
        }

        /// <summary>Every save slot as the original's files in a new folder (in 'parent', default the Transfer folder:
        /// "Original saves yyyy-MM-dd HHmm"); key null = unsigned (the PC Full HD version's files).</summary>
        public static Exported Export(string key = OriginalSave.ModdedKey, string parent = null)
        {
            var res = new Exported();
            res.folder = Path.Combine(parent ?? SaveTransfer.TransferFolder, $"Original saves {DateTime.Now:yyyy-MM-dd HHmm}");
            try
            {
                for (int slot = 0; slot < SaveGame.SlotCount; slot++)
                {
                    var s = SaveGame.Preview(slot);
                    if (s == null || s.gameMode != 0) continue;
                    var economy = SaveGame.SaveEconomy(s);
                    var saved = Session.Economy;
                    Database db;
                    try { Session.Economy = economy; db = Database.Load(); }
                    finally { Session.Economy = saved; }
                    var notes = new List<string>();
                    var record = OriginalSaveConvert.ToRecord(s, db, notes);
                    var recipes = OriginalSave.IngredientCounts(db);
                    Directory.CreateDirectory(res.folder);
                    File.WriteAllBytes(Path.Combine(res.folder, "gof2_save_game_" + slot), OriginalSave.Write(record, recipes, key));
                    File.WriteAllBytes(Path.Combine(res.folder, "gof2_save_game_preview_" + slot), OriginalSave.WritePreview(OriginalSaveConvert.Preview(record, db)));
                    res.slots++;
                    foreach (var n in notes) if (!res.notes.Contains(n)) res.notes.Add(n);
                }
            }
            catch (Exception e) { res.problem = "writing the files failed: " + e.Message; return res; }
            if (res.slots == 0) { res.problem = "there are no saves to export"; return res; }
            Debug.Log($"OriginalSaveTransfer: {res.slots} saves exported to {res.folder}" + (res.notes.Count > 0 ? " (" + string.Join("; ", res.notes) + ")" : ""));
            return res;
        }
    }
}
