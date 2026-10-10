// ShipPresets.cs
// Remake debug (the Debug page's Presets tab, CheatsCatalog.Presets): eight slots of saved ships, each the hull flown (its
// PlayerHull key, so the Ships tab's debug hulls too), the mounted equipment with its amounts (a secondary's ammo) and
// the ship mods. Kept in persistentDataPath/ship_presets.json, outside the save games: a preset belongs to the player, not
// to one game. Loading one mounts its equipment and flies its hull like the Ships tab (PlayerHull.Fly: in flight where
// the player is, docked only a ship the hangar takes); in flight the shield, armor and hull follow the new loadout, full
// (PlayerHealth.RefreshLoadout). Items no longer in the game (a mod that is off) are left out.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ShipPresets
    {
        public const int Slots = 8;

        [Serializable]
        public class Stack { public int item, amount; }

        [Serializable]
        public class Preset
        {
            public string hull;         // PlayerHull.Hull.key ("ship_10", or a debug hull's assembly)
            public int ship = -1;       // its stats ship (-1 = an empty slot)
            public List<Stack> equipment = new List<Stack>();
            public List<int> mods = new List<int>();
            public string saved;        // when, for the slot's line
        }

        [Serializable]
        class PresetFile { public List<Preset> slots = new List<Preset>(); }

        static PresetFile file;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => file = null;

        static string FilePath => Path.Combine(Application.persistentDataPath, "ship_presets.json");

        static PresetFile Data
        {
            get
            {
                if (file != null) return file;
                try { if (File.Exists(FilePath)) file = JsonUtility.FromJson<PresetFile>(File.ReadAllText(FilePath)); }
                catch (Exception e) { Debug.LogWarning($"Ship presets: {e.Message}"); }
                file ??= new PresetFile();
                while (file.slots.Count < Slots) file.slots.Add(new Preset());
                return file;
            }
        }

        static void Write()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true)); }
            catch (Exception e) { Debug.LogWarning($"Ship presets: {e.Message}"); }
        }

        /// <summary>The slot's preset, or null when it is empty.</summary>
        public static Preset Get(int slot) => slot >= 0 && slot < Slots && Data.slots[slot].ship >= 0 ? Data.slots[slot] : null;

        /// <summary>The hull a slot's preset flies (null: an empty slot, or a hull no longer in the game).</summary>
        public static World.PlayerHull.Hull HullOf(int slot, Database db) { var p = Get(slot); return p != null ? HullOf(db, p) : null; }

        static World.PlayerHull.Hull HullOf(Database db, Preset p)
        {
            var all = World.PlayerHull.All(db);
            return all.Find(h => h.key == p.hull) ?? all.Find(h => h.own && h.stats == p.ship);
        }

        /// <summary>The slot's line in the picker: "1 · 10 · Phantom (6 items)" or "1 · empty".</summary>
        public static string Label(int slot, Database db)
        {
            var p = Get(slot);
            if (p == null) return $"{slot + 1} · {Localization.Extra("presetEmpty", "empty")}";
            var hull = HullOf(db, p);
            string name = hull != null ? hull.label : GameNames.Ship(p.ship);
            return $"{slot + 1} · {name} ({p.equipment.Count} {Localization.Extra("presetItems", "items")})";
        }

        /// <summary>What the slot holds, for the line under the picker.</summary>
        public static string Details(int slot, Database db)
        {
            var p = Get(slot);
            if (p == null) return Localization.Extra("presetEmptyHelp", "An empty slot: Save puts the ship you fly now and its equipment here.");
            var names = new List<string>();
            foreach (var s in p.equipment)
            {
                if (db.Item(s.item) == null || Modding.ModContent.IsMissingItem(s.item)) continue;
                string n = GameNames.Item(s.item);
                names.Add(s.amount > 1 ? $"{n} ×{s.amount}" : n);
            }
            string mods = p.mods.Count > 0 ? $"  ·  {p.mods.Count} {Localization.Extra("presetMods", "ship mods")}" : "";
            return (names.Count > 0 ? string.Join(", ", names) : Localization.Extra("presetNoEquipment", "no equipment")) + mods
                   + (string.IsNullOrEmpty(p.saved) ? "" : $"  ·  {p.saved}");
        }

        /// <summary>The ship flown now, its equipment and mods into the slot; the result text.</summary>
        public static string Save(int slot, Database db)
        {
            if (slot < 0 || slot >= Slots) return "";
            var hull = World.PlayerHull.Current(db);
            var p = new Preset
            {
                hull = hull?.key ?? "ship_" + Session.ShipIndex,
                ship = Session.ShipIndex,
                saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };
            foreach (var e in Session.Equipment) p.equipment.Add(new Stack { item = e.item, amount = e.amount });
            p.mods.AddRange(Session.ShipMods);
            Data.slots[slot] = p;
            Write();
            return string.Format(Localization.Extra("presetSaved", "Saved to preset {0}: {1}."), slot + 1, hull != null ? hull.label : GameNames.Ship(p.ship));
        }

        public static string Delete(int slot)
        {
            if (Get(slot) == null) return Localization.Extra("presetNothing", "That slot is empty.");
            Data.slots[slot] = new Preset();
            Write();
            return string.Format(Localization.Extra("presetDeleted", "Preset {0} deleted."), slot + 1);
        }

        /// <summary>Flies the slot's hull with its equipment and mods ('flight' in space, 'docked' at a station); the result text.</summary>
        public static string Load(int slot, Database db, World.SpaceLevel flight, World.StationLevel docked)
        {
            var p = Get(slot);
            if (p == null) return Localization.Extra("presetNothing", "That slot is empty.");
            var hull = HullOf(db, p);
            if (hull == null) return Localization.Extra("presetNoHull", "That preset's ship isn't in the game now (a mod that is off?).");
            // Checked before anything changes: docked, the hangar takes only the ships the player can normally own.
            if (flight == null && !hull.playerShip)
                return string.Format(Localization.Extra("debugHullLaunchFirst", "The {0} doesn't fit in the hangar: launch first."), hull.label);

            int dropped = 0;
            var equipment = new List<ItemStack>();
            foreach (var s in p.equipment)
            {
                if (db.Item(s.item) == null || Modding.ModContent.IsMissingItem(s.item)) { dropped++; continue; }
                equipment.Add(new ItemStack(s.item, Mathf.Max(1, s.amount)));
            }
            Session.Equipment = equipment;
            Session.ShipMods = new List<int>(p.mods);
            Session.SelectedSecondary = -1;

            if (World.PlayerHull.Current(db) == hull)
            {
                // The same hull: rebuilt in place with the new equipment (its guns, turrets, stats).
                if (flight != null) flight.SwapPlayerShip(Session.ShipIndex);
                else docked?.ReplacePlayerShip(Session.ShipIndex);
            }
            else World.PlayerHull.Fly(hull, flight, docked);

            if (flight != null && flight.Health != null) flight.Health.RefreshLoadout(db);
            else { Session.PlayerHull = -1; Session.PlayerShield = -1f; Session.PlayerArmor = -1; }   // docked: full at the next launch
            string text = string.Format(Localization.Extra("presetLoaded", "Preset {0} loaded: {1}."), slot + 1, hull.label);
            if (dropped > 0) text += " " + string.Format(Localization.Extra("presetDropped", "{0} item(s) no longer in the game were left out."), dropped);
            return text;
        }
    }
}
