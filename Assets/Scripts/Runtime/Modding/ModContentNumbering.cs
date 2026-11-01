// ModContentNumbering.cs
// ModContent: numbering (part of the partial ModContent; its fields are in ModContent.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static partial class ModContent
    {
        /// <summary>The numbers of the active mods' content (again after ModManager.Revision changes).</summary>
        static void EnsureMapping()
        {
            if (mappedRevision == ModManager.Revision && originalItems >= 0 && originalShips >= 0) return;
            if (originalItems < 0 || originalShips < 0) { Database.Load(); if (originalItems < 0 || originalShips < 0) return; }   // Load sets them
            _ = ModManager.Active;   // read first: the first read may scan the mods, a new revision, and map all this again inside the loops
            mappedRevision = ModManager.Revision;
            itemIndex.Clear(); itemDefAt.Clear(); placeholderItems.Clear(); itemLook.Clear(); itemRenames.Clear(); deedAt.Clear();
            bool session = ModManager.InSession;
            int next = originalItems;
            // A session numbers afresh: its mods first, so they keep the numbers they had before there was any base content.
            foreach (var mod in ModManager.Active.OrderBy(m => m.IsBase))
                foreach (var d in Parse(mod).items)
                {
                    if (d.key == null) continue;
                    int i = session ? next++ : ModRegistry.Assign(ModRegistry.Kind.Item, d.key, originalItems);
                    itemIndex[d.key] = i;
                    itemDefAt[i] = d;
                }
            // A ship blueprint's item (ModBlueprints): numbered like an item, so saves and crates keep it.
            foreach (var mod in ModManager.Active)
                foreach (var b in Parse(mod).blueprints)
                {
                    if (!b.IsShip) continue;
                    int i = session ? next++ : ModRegistry.Assign(ModRegistry.Kind.Item, b.DeedKey, originalItems);
                    itemIndex[b.DeedKey] = i;
                    deedAt[i] = b;
                }
            if (!session)
                foreach (var kv in ModRegistry.All(ModRegistry.Kind.Item))
                    if (!itemIndex.ContainsKey(kv.Key) && kv.Value >= originalItems) placeholderItems[kv.Value] = kv.Key;
            // Looks: a new item looks like its base (recursively: a copy of a copy looks like the original).
            foreach (var kv in itemDefAt) itemLook[kv.Key] = LookOf(kv.Key, 0);
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).items)
                    if (d.overrideRef != null && (d.name != null || d.description != null) && TryResolveItem(d.overrideRef, out int t)) itemRenames[t] = d;
            // Shop icons ("icon"): a new item's own, or an override's for the item it changes (the later mod wins).
            itemIcons.Clear();
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).items)
                {
                    string icon = ModJson.Str(d.json, "icon");
                    if (string.IsNullOrEmpty(icon) || !GameModels.Is(icon) && !mod.Source.Exists(icon)) continue;
                    if (d.key != null ? itemIndex.TryGetValue(d.key, out int i) : TryResolveItem(d.overrideRef, out i)) itemIcons[i] = d;
                }
            // A new item without an icon of its own takes the nearest base's that has one (a copy of a mod item, or of an
            // original item an override gave an icon); without any, the original look's (ItemLook).
            foreach (var kv in itemDefAt)
            {
                if (itemIcons.ContainsKey(kv.Key)) continue;
                int at = kv.Key;
                for (int depth = 0; depth < 16 && itemDefAt.TryGetValue(at, out var def) && TryResolveItem(def.baseRef, out int b) && b != at; depth++)
                {
                    at = b;
                    if (itemIcons.TryGetValue(at, out var src)) { inheritedIcons[kv.Key] = src; break; }
                }
            }
            foreach (var kv in inheritedIcons) itemIcons[kv.Key] = kv.Value;
            inheritedIcons.Clear();

            // Ships: the same, after the original 64.
            shipIndex.Clear(); shipDefAt.Clear(); placeholderShips.Clear(); shipRenames.Clear(); shipRetags.Clear();
            int nextShip = originalShips;
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).ships)
                {
                    if (d.key == null) continue;
                    int i = session ? nextShip++ : ModRegistry.Assign(ModRegistry.Kind.Ship, d.key, originalShips);
                    shipIndex[d.key] = i;
                    shipDefAt[i] = d;
                }
            if (!session)
                foreach (var kv in ModRegistry.All(ModRegistry.Kind.Ship))
                    if (!shipIndex.ContainsKey(kv.Key) && kv.Value >= originalShips) placeholderShips[kv.Value] = kv.Key;
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).ships)
                    if (d.overrideRef != null && TryResolveShip(d.overrideRef, out int t))
                    {
                        if (d.name != null || d.description != null) shipRenames[t] = d;
                        if (d.hasTag) shipRetags[t] = d;
                    }
        }
        /// <summary>A ship reference ("12" or "mod:id") to its number in the current numbering.</summary>
        public static bool TryResolveShip(string reference, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(reference)) return false;
            if (int.TryParse(reference, out index)) return index >= 0 && (index < originalShips || shipDefAt.ContainsKey(index));
            return shipIndex.TryGetValue(reference, out index);
        }
        /// <summary>A mod ship's entry as CustomShipData (a fresh copy: Database.Load sets prices by economy), null = none.</summary>
        static CustomShipData ShipData(int index, ShipDef d)
        {
            var c = JsonUtility.FromJson<CustomShipData>(d.data);
            c.index = index;
            // remake mods: a mod race as the ship's race or among its traffic's races (ModRaces)
            if (d.raceRef != null) c.race = ModRaces.TryResolve(d.raceRef, d.mod, out int fr) ? fr : -1;
            if (d.trafficRaceRefs != null && c.traffic != null)
                c.traffic.races = d.trafficRaceRefs.Select(r => ModRaces.TryResolve(r, d.mod, out int tr) ? tr : -1).Where(r => r >= 0).ToArray();
            if (!d.refsReported)
            {
                // Remake: a misspelt mod race was dropped without a word (systems.json reports its own: ModWorld.RaceOf)
                d.refsReported = true;
                if (d.raceRef != null && c.race < 0) d.mod.Warnings.Add($"{d.where}: \"race\": \"{d.raceRef}\" is no race (ignored)");
                if (d.trafficRaceRefs != null)
                    foreach (var r in d.trafficRaceRefs)
                        if (!ModRaces.TryResolve(r, d.mod, out _)) d.mod.Warnings.Add($"{d.where}: traffic races: \"{r}\" is no race (ignored)");
            }
            c.assembly = $"ship_{index:000}_mod";
            if (GameModels.Is(c.model, out string gameModel))
            {
                // One of the game's own models (no file to build): the ship's hangar height and icon follow it unless given.
                c.assembly = gameModel;
                c.gameModelShip = GameModels.ShipOf(gameModel);
                if (!ModJson.Has(d.json, "hangarHeight") && c.gameModelShip >= 0) c.hangarHeight = -1;
            }
            if (!d.hasLounge) c.lounge = null;   // JsonUtility makes one with its defaults
            if (!d.hasDealer) c.dealer = null;
            if (c.throttleGlow != null && string.IsNullOrEmpty(c.throttleGlow.mask)) c.throttleGlow = null;
            if (c.engineFx != null && !c.engineFx.Any) c.engineFx = null;   // (JsonUtility makes one either way)
            ShipText(index, false, out c.name);
            ShipText(index, true, out c.description);
            c.handlingMultiplier = c.handling / 100f;
            c.slots ??= new ShipSlots();
            if (!string.IsNullOrEmpty(c.kit))
            {
                // A customizable ship (ModShipKits): the kit as "mod_id:kit_id", the default build as build text.
                if (c.kit.IndexOf(':') < 0) c.kit = d.mod.Id + ":" + c.kit;
                c.defaultBuild = GameHooks.KitBuildText?.Invoke(ModJson.Get(d.json, "build") as JObject) ?? "";
            }
            else c.kit = null;
            return c;
        }
        /// <summary>Original ships whose model a mod replaces (an "override" with "model", e.g. the Groza from Manticore): their
        /// entries as CustomShipData for ModShips to build (assembly "ship_NNN_mod", which Database.ShipAssembly then hands out
        /// instead of the original's); the original's weapon and exhaust mounts unless the entry has its own "mounts". The
        /// later mod in the load order wins. Not new ships: CustomShips doesn't see them (no lounge sellers, the stats stay the
        /// original's plus the override's).</summary>
        public static List<(ModInfo mod, CustomShipData ship)> ModelOverrides()
        {
            EnsureMapping();
            var l = new List<(ModInfo, CustomShipData)>();
            if (originalShips < 0) return l;
            var byShip = new SortedDictionary<int, ShipDef>();
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).ships)
                    if (d.overrideRef != null && ModJson.Has(d.json, "model") && TryResolveShip(d.overrideRef, out int t) && t < originalShips)
                    {
                        if (GameModels.Is(ModJson.Str(d.json, "model"))) byShip.Remove(t);   // the game's model: nothing to build
                        else byShip[t] = d;
                    }
            if (byShip.Count == 0) return l;
            var db = Database.Load();
            foreach (var kv in byShip)
            {
                var c = JsonUtility.FromJson<CustomShipData>(kv.Value.data);
                c.index = kv.Key;
                c.assembly = ModelOverrideAssembly(kv.Key);
                c.name = db.Ship(kv.Key)?.name ?? c.assembly;
                c.lounge = null;
                if (c.throttleGlow != null && string.IsNullOrEmpty(c.throttleGlow.mask)) c.throttleGlow = null;
                if (c.engineFx != null && !c.engineFx.Any) c.engineFx = null;
                if (c.mounts == null || c.mounts.Count == 0)
                    c.mounts = db.WeaponMounts.Find(w => w.ship == kv.Key)?.mounts ?? new List<WeaponMount>();
                l.Add((kv.Value.mod, c));
            }
            return l;
        }
        /// <summary>The ships' own sounds (ships.json "sounds", ModShipSounds) by ship number: new ships' and overrides', the
        /// later mod in the load order winning.</summary>
        public static Dictionary<int, object> ShipSounds()
        {
            EnsureMapping();
            var r = new Dictionary<int, object>();
            foreach (var kv in shipDefAt) if (kv.Value.sounds != null) r[kv.Key] = kv.Value.sounds;
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).ships)
                    if (d.overrideRef != null && d.sounds != null && TryResolveShip(d.overrideRef, out int t)) r[t] = d.sounds;
            return r;
        }
        /// <summary>The assembly of a mod's model for an original ship (ModelOverrides).</summary>
        public static string ModelOverrideAssembly(int ship) => $"ship_{ship:000}_mod";
        /// <summary>Some active mod brings a ship model (a new ship, or an override's model; ActiveShips / ModelOverrides not
        /// empty), without building their entries: ModShips.Ready asks every frame while the loading screen shows, and
        /// ActiveShips parses every ship's entry again (~200 KB of garbage and 2 ms for a mod of 79 ships, three times a frame).</summary>
        public static bool AnyShipModels()
        {
            EnsureMapping();
            if (shipDefAt.Count > 0) return true;   // a new ship on a game model still has its icon to load (ModShips)
            if (originalShips < 0) return false;
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).ships)
                    if (d.overrideRef != null && ModJson.Has(d.json, "model") && !GameModels.Is(ModJson.Str(d.json, "model"))
                        && TryResolveShip(d.overrideRef, out int t) && t < originalShips)
                        return true;
            return false;
        }
        /// <summary>The active mods' new ships with their numbers (ModShips builds their models, CustomShips serves them).</summary>
        public static List<(ModInfo mod, CustomShipData ship)> ActiveShips()
        {
            EnsureMapping();
            var l = new List<(ModInfo, CustomShipData)>();
            foreach (var kv in shipDefAt.OrderBy(k => k.Key)) l.Add((kv.Value.mod, ShipData(kv.Key, kv.Value)));
            return l;
        }
        public static string ShipKey(int index)
        {
            EnsureMapping();
            return shipDefAt.TryGetValue(index, out var d) ? d.key : placeholderShips.TryGetValue(index, out var k) ? k : null;
        }
        public static bool IsMissingShip(int index) { EnsureMapping(); return placeholderShips.ContainsKey(index); }
        public static bool IsModShip(int index) { EnsureMapping(); return shipDefAt.ContainsKey(index); }
        public static int ShipIndexOf(string key) { EnsureMapping(); return shipIndex.TryGetValue(key, out int i) ? i : -1; }
        public static IEnumerable<KeyValuePair<int, string>> ActiveShipKeys()
        {
            EnsureMapping();
            foreach (var kv in shipDefAt) yield return new KeyValuePair<int, string>(kv.Key, kv.Value.key);
        }
        public static int OriginalShipCount => originalShips;
        /// <summary>A modded (or renamed) ship's name / description in the current language; false = the original text.</summary>
        public static bool ShipText(int index, bool description, out string text)
        {
            EnsureMapping();
            text = null;
            if (placeholderShips.TryGetValue(index, out var missing))
            {
                text = description ? string.Format(Localization.Extra("modMissingShipDesc", "This ship came from the mod \"{0}\", which isn't on."), missing.Split(':')[0])
                    : string.Format(Localization.Extra("modMissingShip", "Missing ship ({0})"), missing);
                return true;
            }
            ShipDef d = shipDefAt.TryGetValue(index, out var nd) ? nd : shipRenames.TryGetValue(index, out var rd) ? rd : null;
            if (d == null) return false;
            string id = d.localId ?? d.overrideRef;
            text = Translated(d.mod, $"ships.{id}.{(description ? "description" : "name")}", description ? d.description : d.name);
            if (text == null && d.key == null) return false;
            text ??= description ? "" : d.key;
            return true;
        }
        /// <summary>Remake (TorchOP's request, 2026-10-23): the tag the lists show dim after a ship's name ("GoF3", "HD",
        /// "Star Wars"), null = none. An override's "tag" (a later mod's winning) first, else a new ship's own "tag", else
        /// its mod's "shipTag" (mod.json); "" clears it. Translated like the name (text/&lt;lang&gt;.json "ships.&lt;id&gt;.tag").</summary>
        public static string ShipTag(int index)
        {
            EnsureMapping();
            if (shipRetags.TryGetValue(index, out var d) || shipDefAt.TryGetValue(index, out d) && d.hasTag)
                return NonEmpty(Translated(d.mod, $"ships.{d.localId ?? d.overrideRef}.tag", d.tag));
            return shipDefAt.TryGetValue(index, out d) ? NonEmpty(ModManifest.Pick(d.mod.Manifest?.shipTag)) : null;
        }
        static string NonEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static int LookOf(int index, int depth)
        {
            if (!itemDefAt.TryGetValue(index, out var d) || depth > 16) return index;
            return TryResolveItem(d.baseRef, out int b) && b != index ? LookOf(b, depth + 1) : index;
        }
        /// <summary>An item reference ("12" or "mod:id") to its number in the current numbering.</summary>
        public static bool TryResolveItem(string reference, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(reference)) return false;
            // The table's size and the mod numbers first (ModWeapons reads the overrides' fx on the loading screen, before
            // anything else loaded the database: originalItems was still -1 and every override's fx was dropped).
            EnsureMapping();
            if (int.TryParse(reference, out index)) return index >= 0 && (index < originalItems || itemDefAt.ContainsKey(index));
            return itemIndex.TryGetValue(reference, out index);
        }
        /// <summary>The original item a modded one looks and behaves like (icon, weapon fx, sounds, special rules); an
        /// original item is its own.</summary>
        public static int ItemLook(int index)
        {
            EnsureMapping();
            return itemLook.TryGetValue(index, out int l) ? l : index;
        }
        /// <summary>An item's shop icon from a mod (items.json "icon": a PNG like the originals' 180 x 88, frame included,
        /// kept uncompressed), null = none (the item's own, or its base's: ItemLook).</summary>
        public static Texture2D ItemIcon(int index)
        {
            EnsureMapping();
            if (!itemIcons.TryGetValue(index, out var d)) return null;
            string icon = ModJson.Str(d.json, "icon");
            if (GameModels.Is(icon)) return GameModels.Icon(icon);   // one of the game's own icons ("gof2:item_046")
            var t = ModMaterials.Texture(d.mod, icon, false, true);
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;   // a UI image: no bleeding in from the opposite edge
            return t;
        }
        /// <summary>The "mod:id" key of a modded item (also a placeholder's), null for an original one.</summary>
        public static string ItemKey(int index)
        {
            EnsureMapping();
            return itemDefAt.TryGetValue(index, out var d) ? d.key : placeholderItems.TryGetValue(index, out var k) ? k : null;
        }
        /// <summary>A modded item whose mod isn't on (a placeholder: never sold, worth nothing).</summary>
        public static bool IsMissingItem(int index) { EnsureMapping(); return placeholderItems.ContainsKey(index); }
        /// <summary>The number the key has now, -1 = not active.</summary>
        public static int ItemIndexOf(string key) { EnsureMapping(); return itemIndex.TryGetValue(key, out int i) ? i : -1; }
        /// <summary>Every modded item number in use now (active ones), with its key.</summary>
        public static IEnumerable<KeyValuePair<int, string>> ActiveItemKeys()
        {
            EnsureMapping();
            foreach (var kv in itemDefAt) yield return new KeyValuePair<int, string>(kv.Key, kv.Value.key);
        }
    }
}
