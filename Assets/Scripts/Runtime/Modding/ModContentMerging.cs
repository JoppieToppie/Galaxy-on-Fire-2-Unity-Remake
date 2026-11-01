// ModContentMerging.cs
// ModContent: texts and merging (part of the partial ModContent; its fields are in ModContent.cs).

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
        // ---- texts ---------------------------------------------------------------------------------------------------
        /// <summary>A modded (or renamed) item's name / description in the current language; false = the original text.</summary>
        public static bool ItemText(int index, bool description, out string text)
        {
            EnsureMapping();
            text = null;
            if (deedAt.TryGetValue(index, out var bp))
            {
                // A ship blueprint's item reads as its ship.
                text = TryResolveShip(bp.shipRef, out int ship) ? description ? GameNames.ShipDescription(ship) : GameNames.Ship(ship) : bp.DeedKey;
                return true;
            }
            if (placeholderItems.TryGetValue(index, out var missing))
            {
                text = description ? string.Format(Localization.Extra("modMissingItemDesc", "This item came from the mod \"{0}\", which isn't on."), missing.Split(':')[0])
                    : string.Format(Localization.Extra("modMissingItem", "Missing item ({0})"), missing);
                return true;
            }
            ItemDef d = itemDefAt.TryGetValue(index, out var nd) ? nd : itemRenames.TryGetValue(index, out var rd) ? rd : null;
            if (d == null) return false;
            var t = description ? d.description : d.name;
            string id = d.localId ?? d.overrideRef;
            text = Translated(d.mod, $"items.{id}.{(description ? "description" : "name")}", t);
            if (text == null && d.key == null) return false;   // an override that doesn't change this text
            text ??= description ? "" : d.key;
            return true;
        }
        /// <summary>Translated, for the other mod tables (ModEncounters' signal names).</summary>
        public static string TranslatedText(ModInfo mod, string key, Dictionary<string, string> inline) => Translated(mod, key, inline);

        /// <summary>The mod's text file for the language, else the entry's own text in the language / English / plain.</summary>
        static string Translated(ModInfo mod, string key, Dictionary<string, string> inline)
        {
            string lang = Localization.Language;
            var p = Parse(mod);
            if (inline != null && inline.TryGetValue(lang, out var s)) return s;
            if (p.text.TryGetValue(lang, out var table) && table.TryGetValue(key, out s)) return s;
            if (inline != null && (inline.TryGetValue("en", out s) || inline.TryGetValue("", out s))) return s;
            if (p.text.TryGetValue("en", out table) && table.TryGetValue(key, out s)) return s;
            return inline?.Values.FirstOrDefault();
        }
        // ---- merging -------------------------------------------------------------------------------------------------
        /// <summary>Database.Load: the active mods' changes and new entries into the freshly read tables.</summary>
        public static void Apply(Database db)
        {
            NoteOriginalCounts(db);
            EnsureMapping();
            bool dflt = db.Economy == Economy.Default;
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).items)
                {
                    try
                    {
                        if (d.overrideRef != null)
                        {
                            if (!TryResolveItem(d.overrideRef, out int t) || db.Item(t) == null)
                            { Report(mod, $"{d.where}: \"override\": {d.overrideRef} is no item (a modded one must come from an earlier mod)"); continue; }
                            ApplyFields(db.Item(t), d.json, dflt);
                            continue;
                        }
                        int index = itemIndex[d.key];
                        if (!TryResolveItem(d.baseRef, out int b) || db.Item(b) == null || b == index)
                        { Report(mod, $"{d.where}: \"base\": {d.baseRef} is no item (a modded base must be listed earlier or come from an earlier mod)"); continue; }
                        var item = Clone(db.Item(b));
                        item.index = index;
                        item.modKey = d.key;
                        item.modded = true;
                        item.lookIndex = ItemLook(index);
                        SetAttr(item, 0, index);
                        if (ItemText(index, false, out var n)) item.name = n;
                        if (ItemText(index, true, out var ds)) item.description = ds;
                        ApplyFields(item, d.json, dflt);
                        db.Items.Add(item);
                    }
                    catch (ModJsonException e) { Report(mod, e.Message); }
                }
            foreach (var kv in deedAt)
                if (db.Item(kv.Key) == null) db.Items.Add(Deed(kv.Key, kv.Value));
            foreach (var kv in placeholderItems)
                if (db.Item(kv.Key) == null) db.Items.Add(Placeholder(kv.Key, kv.Value));
            // Gaps (a number given out to a key the registry later lost): placeholders too, so the table stays 0..N-1.
            int max = db.Items.Max(i => i.index);
            for (int i = originalItems; i <= max; i++) if (db.Item(i) == null) db.Items.Add(Placeholder(i, "?:" + i));
            db.Items.Sort((a, c) => a.index.CompareTo(c.index));
            ApplyShips(db, dflt);
            ModBlueprints.Apply(db);   // the recipes, and the ship blueprints' prices (after the ships)
            ModWorld.Apply(db);
        }
        /// <summary>A ship on one of the game's ship models (GameModels) takes that ship's hardpoints (weapons, turrets, exhausts)
        /// unless its entry has its own "mounts"; a model that is no ship's (a capital ship's) brings none.</summary>
        static void CopyModelMounts(Database db, ShipDef d, int ship, string shipName, string model)
        {
            if (ModJson.Has(d.json, "mounts")) return;
            int source = GameModels.ShipOf(model);
            var mounts = source >= 0 ? Database.GameMounts(source) : null;   // the model's own, whatever other mods did to that ship
            if (mounts == null || mounts.Count == 0) return;
            db.WeaponMounts.RemoveAll(w => w.ship == ship);
            db.WeaponMounts.Add(new WeaponMountSet { ship = ship, shipName = shipName, mounts = mounts.ConvertAll(Copy) });
        }
        static WeaponMount Copy(WeaponMount m) => new WeaponMount
        {
            slotType = m.slotType, upsideDown = m.upsideDown, position_engine = (int[])m.position_engine?.Clone(),
            turretAngles = (float[])m.turretAngles?.Clone(), glowColor = (float[])m.glowColor?.Clone(),
            glowSize = (float[])m.glowSize?.Clone(), glowShape = m.glowShape, turretUp = (float[])m.turretUp?.Clone(),
        };
        static void ApplyShips(Database db, bool dflt)
        {
            db.NpcTurrets.Clear();
            db.ShipSubsystems.Clear();
            db.ShipModels.Clear();
            db.ShipOwnTextures.Clear();
            db.IntegratedDrives = new HashSet<int>(Database.DefaultIntegratedDrives);
            db.IntegratedCloaks = new Dictionary<int, int>(Database.DefaultIntegratedCloaks);
            db.IntegratedItems = new Dictionary<int, List<ItemStack>>();
            foreach (var mod in ModManager.Active)
                foreach (var d in Parse(mod).ships)
                {
                    try
                    {
                        if (d.overrideRef != null)
                        {
                            if (!TryResolveShip(d.overrideRef, out int t) || db.Ship(t) == null)
                            { Report(mod, $"{d.where}: \"override\": {d.overrideRef} is no ship (a modded one must come from an earlier mod)"); continue; }
                            var s = db.Ship(t);
                            var o = d.json;
                            s.armor = ModJson.Int(o, "armor", s.armor, ShipsFile);
                            s.cargo = ModJson.Int(o, "cargo", s.cargo, ShipsFile);
                            s.price = ModJson.Int(o, dflt && ModJson.Has(o, "priceDefault") ? "priceDefault" : "price", s.price, ShipsFile);
                            if (ModJson.Has(o, "handling")) { s.handling = ModJson.Int(o, "handling", 0, ShipsFile); s.handlingMultiplier = s.handling / 100f; }
                            if (ModJson.Get(o, "slots") is JObject sl)
                                s.slots = new ShipSlots
                                {
                                    primary = ModJson.Int(sl, "primary", s.slots?.primary ?? 0, ShipsFile), secondary = ModJson.Int(sl, "secondary", s.slots?.secondary ?? 0, ShipsFile),
                                    turret = ModJson.Int(sl, "turret", s.slots?.turret ?? 0, ShipsFile), equipment = ModJson.Int(sl, "equipment", s.slots?.equipment ?? 0, ShipsFile),
                                };
                            ModFlight.ApplyShip(s, o, d.where);
                            ModShipView.ApplyShip(s, o);
                            if (s is CustomShipData cs)
                            {
                                cs.race = d.raceRef != null ? (ModRaces.TryResolve(d.raceRef, d.mod, out int ofr) ? ofr : cs.race) : ModJson.Int(o, "race", cs.race, ShipsFile);
                                if (d.raceRef != null && !d.refsReported && !ModRaces.TryResolve(d.raceRef, d.mod, out _))
                                    d.mod.Warnings.Add($"{d.where}: \"race\": \"{d.raceRef}\" is no race (the ship keeps its own)");
                                if (d.raceRef != null) d.refsReported = true;
                                cs.hangarHeight = ModJson.Int(o, "hangarHeight", cs.hangarHeight, ShipsFile);
                            }
                            if (ShipText(t, false, out var n)) s.name = n;
                            if (GameModels.Is(ModJson.Str(o, "model"), out string gameModel))
                            {
                                // One of the game's own models for this ship (Database.ShipModels; no file to build): its
                                // hardpoints come with it unless the entry has its own.
                                if (GameModels.Find(db, gameModel) == null) { Report(mod, $"{d.where}: \"model\": the game has no model \"{gameModel}\""); }
                                else
                                {
                                    db.ShipModels[t] = gameModel;
                                    CopyModelMounts(db, d, t, s.name, gameModel);
                                }
                            }
                            else if (ModJson.Has(o, "model") && t < originalShips)
                            {
                                // A new model for an original ship (ModelOverrides; Database.ShipAssembly takes this first).
                                string asm = ModelOverrideAssembly(t);
                                if (db.AssemblyByName(asm) == null)
                                    db.Assemblies.Add(new AssemblyData { name = asm, pack = ModContent.Pack, category = "ships", origin = "mod " + mod.Id });
                            }
                            // Its own hardpoints ("mounts": weapon / turret / exhaust points), with a new model or on the
                            // ship's own (more primaries or secondaries need points to fire from; extra ones share them).
                            if (ModJson.Has(o, "mounts"))
                            {
                                var own = JsonUtility.FromJson<CustomShipData>(d.data);
                                if (own.mounts != null && own.mounts.Count > 0)
                                {
                                    db.WeaponMounts.RemoveAll(w => w.ship == t);
                                    db.WeaponMounts.Add(new WeaponMountSet { ship = t, shipName = s.name, mounts = own.mounts });
                                }
                            }
                            ApplyNpcTurrets(db, mod, d, t);
                            ApplySubsystems(db, mod, d, t);
                            ApplyIntegrated(db, mod, d, t);
                            continue;
                        }
                        int index = shipIndex[d.key];
                        var c = ShipData(index, d);
                        if (dflt && c.priceDefault > 0) c.price = c.priceDefault;
                        ModFlight.ApplyShip(c, d.json, d.where);
                        ModShipView.ApplyShip(c, d.json);
                        db.Ships.Add(c);
                        if (GameModels.Is(c.model, out string shipModel))
                        {
                            // One of the game's own models: nothing to build, ShipAssembly hands the game's out.
                            if (GameModels.Find(db, shipModel) == null) Report(mod, $"{d.where}: \"model\": the game has no model \"{shipModel}\"");
                            else
                            {
                                db.ShipModels[index] = shipModel;
                                if (HasShipTextures(mod, d.localId)) db.ShipOwnTextures.Add(index);
                            }
                        }
                        else db.Assemblies.Add(new AssemblyData { name = c.assembly, pack = ModContent.Pack, category = "ships", origin = "mod " + mod.Id });
                        if (c.mounts != null && c.mounts.Count > 0)
                            db.WeaponMounts.Add(new WeaponMountSet { ship = index, shipName = c.name, mounts = c.mounts });
                        else if (GameModels.Is(c.model, out string mountsModel)) CopyModelMounts(db, d, index, c.name, mountsModel);
                        ApplyNpcTurrets(db, mod, d, index);
                        ApplySubsystems(db, mod, d, index);
                        ApplyIntegrated(db, mod, d, index);
                    }
                    catch (ModJsonException e) { Report(mod, e.Message); }
                }
            foreach (var kv in placeholderShips)
                if (db.Ship(kv.Key) == null) db.Ships.Add(PlaceholderShip(kv.Key));
            int max = db.Ships.Max(s => s.index);
            for (int i = originalShips; i <= max; i++) if (db.Ship(i) == null) db.Ships.Add(PlaceholderShip(i));
            db.Ships.Sort((a, c) => a.index.CompareTo(c.index));
        }
        /// <summary>Remake (generic turrets): ships.json "npcTurrets", the turret items (category 8 / 35, numbers or "mod:id") the
        /// NPC ships of this type carry on its turret mounts in space (World.NpcMountedTurrets) and parked in the hangar; the
        /// later mod wins, an empty list takes them off.</summary>
        static void ApplyNpcTurrets(Database db, ModInfo mod, ShipDef d, int ship)
        {
            if (!(ModJson.Get(d.json, "npcTurrets") is JArray list)) return;
            var items = new List<int>();
            foreach (var e in list)
            {
                string reference = e.Type == JTokenType.Integer ? ((int)e).ToString() : e.Type == JTokenType.String ? (string)e : null;
                if (reference == null || !TryResolveItem(reference, out int i) || db.Item(i) == null)
                { Report(mod, $"{d.where}: \"npcTurrets\": {e} is no item"); continue; }
                var it = db.Item(i);
                if (it.categoryId != 8 && it.categoryId != 35) { Report(mod, $"{d.where}: \"npcTurrets\": {it.name} ({i}) is no turret"); continue; }
                items.Add(i);
            }
            if (db.MountsOf(ship, 2).Count < items.Count)
                Report(mod, $"{d.where}: \"npcTurrets\" lists {items.Count} turrets, the ship has {db.MountsOf(ship, 2).Count} turret mounts (the rest is left off)");
            db.NpcTurrets[ship] = items;
        }
        /// <summary>Remake mods: ships.json "subsystems", a capital ship's parts on its hull (World.CapitalSubsystems): each
        /// { "kind": "turret" | "object" | "hangar", "name", "position_engine": [x, y, z], "turretUp" / "upsideDown", "item"
        /// (a turret's turret item), "model" ("gof2:<assembly>" or a ship), "hull", "scale", "radius", "shields", "weakspot",
        /// "bonus", "stage", and for a hangar "ships", "every" (seconds), "perWave", "maxAlive", "total" }. A turret without a
        /// turret item, an unknown kind or a wrong shape is reported and left out.</summary>
        static void ApplySubsystems(Database db, ModInfo mod, ShipDef d, int ship)
        {
            if (!(ModJson.Get(d.json, "subsystems") is JArray list)) return;
            var parts = new List<CapitalSubsystem>();
            foreach (var e in list)
            {
                string at = ModJson.Where(e, ShipsFile);
                if (!(e is JObject o)) { Report(mod, $"{at}: each subsystem must be an object {{ \"kind\", \"position_engine\" }}"); continue; }
                var p = new CapitalSubsystem { kind = (ModJson.Str(o, "kind") ?? "object").ToLowerInvariant(), name = ModJson.Str(o, "name"), model = ModJson.Str(o, "model") };
                if (p.kind != "turret" && p.kind != "object" && p.kind != "hangar") { Report(mod, $"{at}: \"kind\" must be \"turret\", \"object\" or \"hangar\""); continue; }
                if (!(ModJson.Get(o, "position_engine") is JArray pos) || pos.Count < 3) { Report(mod, $"{at}: a subsystem needs \"position_engine\": [x, y, z]"); continue; }
                p.position = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]);
                if (ModJson.Get(o, "turretUp") is JArray up && up.Count >= 3) p.up = new Vector3((float)up[0], (float)up[1], (float)up[2]);
                else if (ModJson.Get(o, "upsideDown") is JValue ud && ud.Type == JTokenType.Boolean && (bool)ud) p.up = Vector3.down;
                if (p.up.sqrMagnitude < 1e-6f) p.up = Vector3.up;
                if (ModJson.Get(o, "item") is JToken it)
                {
                    string reference = it.Type == JTokenType.Integer ? ((int)it).ToString() : it.Type == JTokenType.String ? (string)it : null;
                    if (reference == null || !TryResolveItem(reference, out int i) || db.Item(i) == null) { Report(mod, $"{at}: \"item\": {it} is no item"); continue; }
                    if (db.Item(i).categoryId != 8 && db.Item(i).categoryId != 35) { Report(mod, $"{at}: \"item\": {db.Item(i).name} ({i}) is no turret"); continue; }
                    p.item = i;
                }
                if (p.kind == "turret" && p.item < 0) { Report(mod, $"{at}: a turret subsystem needs \"item\", its turret item"); continue; }
                p.hull = ModJson.Int(o, "hull", -1, ShipsFile);
                p.stage = ModJson.Int(o, "stage", 0, ShipsFile);
                p.shields = ModJson.Get(o, "shields") is JValue sh && sh.Type == JTokenType.Boolean && (bool)sh;
                float F(string k, float def) => ModJson.Get(o, k) is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : def;
                p.weakspot = F("weakspot", 0f); p.bonus = F("bonus", 0f); p.scale = F("scale", 1f); p.radius = F("radius", -1f);
                p.everyMs = Mathf.Max(3f, F("every", 25f)) * 1000f;
                p.perWave = Mathf.Max(1, ModJson.Int(o, "perWave", 2, ShipsFile));
                p.maxAlive = Mathf.Max(1, ModJson.Int(o, "maxAlive", 6, ShipsFile));
                p.total = ModJson.Int(o, "total", -1, ShipsFile);
                if (ModJson.Get(o, "ships") is JArray ships)
                    foreach (var sh2 in ships) if (sh2.Type == JTokenType.Integer || sh2.Type == JTokenType.String) p.ships.Add(sh2.ToString());
                if (p.kind == "hangar" && p.ships.Count == 0) { Report(mod, $"{at}: a hangar subsystem needs \"ships\", the ships it launches"); continue; }
                parts.Add(p);
            }
            db.ShipSubsystems[ship] = parts;
        }
        /// <summary>Remake (a modder's request): ships.json "integrated", what a ship has built in like the Kothar ships' Khador
        /// Drive (37 / 38 / 40) and the Specter's / Scimitar's cloak (44 / 49): "khadorDrive" true / false (Database.IntegratedDrives:
        /// GalaxyMap.HasJumpDrive, the Khador entry and its hints), "cloak" true (the U'tool's stats, 95), a cloak item (a number or
        /// "mod:id", category 21) or false (Database.IntegratedCloaks: PlayerCloak, the cloak bay). An override can take an
        /// original ship's away; the later mod wins. "items": any items built in (Database.IntegratedItems, Hangar.SyncBuiltIn):
        /// [n | "mod:id" | { "item", "amount" }] (amount: a secondary's starting ammo, default 10; else 1 each); an empty list
        /// takes them away.</summary>
        static void ApplyIntegrated(Database db, ModInfo mod, ShipDef d, int ship)
        {
            if (!(ModJson.Get(d.json, "integrated") is JObject o)) return;
            foreach (var prop in o.Properties())
            {
                var v = prop.Value;
                if (string.Equals(prop.Name, "khadorDrive", StringComparison.OrdinalIgnoreCase))
                {
                    if (v.Type != JTokenType.Boolean) { Report(mod, $"{d.where}: \"integrated\": \"khadorDrive\" must be true or false"); continue; }
                    if ((bool)v) db.IntegratedDrives.Add(ship); else db.IntegratedDrives.Remove(ship);
                }
                else if (string.Equals(prop.Name, "cloak", StringComparison.OrdinalIgnoreCase))
                {
                    if (v.Type == JTokenType.Boolean)
                    {
                        if ((bool)v) db.IntegratedCloaks[ship] = Database.IntegratedCloakItem; else db.IntegratedCloaks.Remove(ship);
                        continue;
                    }
                    string reference = v.Type == JTokenType.Integer ? ((int)v).ToString() : v.Type == JTokenType.String ? (string)v : null;
                    if (reference == null || !TryResolveItem(reference, out int item) || db.Item(item) == null)
                    { Report(mod, $"{d.where}: \"integrated\": \"cloak\": {v} is no item (true, false or a cloak item)"); continue; }
                    if (db.Item(item).categoryId != 21) { Report(mod, $"{d.where}: \"integrated\": \"cloak\": {db.Item(item).name} ({item}) is no cloak"); continue; }
                    db.IntegratedCloaks[ship] = item;
                }
                else if (string.Equals(prop.Name, "items", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(v is JArray list)) { Report(mod, $"{d.where}: \"integrated\": \"items\" must be a list"); continue; }
                    var items = new List<ItemStack>();
                    foreach (var entry in list)
                    {
                        var itemToken = entry is JObject eo ? eo["item"] : entry;
                        string reference = itemToken?.Type == JTokenType.Integer ? ((int)itemToken).ToString() : itemToken?.Type == JTokenType.String ? (string)itemToken : null;
                        if (reference == null || !TryResolveItem(reference, out int item) || db.Item(item) == null)
                        { Report(mod, $"{d.where}: \"integrated\": \"items\": {entry.ToString(Newtonsoft.Json.Formatting.None)} is no item"); continue; }
                        int type = db.Item(item).TypeId;
                        if (type > 3) { Report(mod, $"{d.where}: \"integrated\": \"items\": {db.Item(item).name} ({item}) is no equipment (a commodity can't be built in)"); continue; }
                        int amount = type == 1 ? 10 : 1;
                        if (entry is JObject e2 && e2["amount"] is JToken a)
                        {
                            if (a.Type != JTokenType.Integer || (int)a < 0) { Report(mod, $"{d.where}: \"integrated\": \"items\": \"amount\" must be 0 or more"); continue; }
                            if (type == 1) amount = (int)a;
                        }
                        items.Add(new ItemStack(item, amount) { builtIn = true });
                    }
                    if (items.Count > 0) db.IntegratedItems[ship] = items; else db.IntegratedItems.Remove(ship);
                }
                else mod.Warnings.Add($"{d.where}: \"integrated\": unknown field \"{prop.Name}\" (ignored; \"khadorDrive\", \"cloak\", \"items\")");
            }
        }
        /// <summary>A mod ship whose mod isn't on: never sold, can't be flown (ModSaves moves the player out of it).</summary>
        static ShipData PlaceholderShip(int index)
        {
            var s = new ShipData { index = index, slots = new ShipSlots(), armor = 100, cargo = 0, price = 0, handling = 100, handlingMultiplier = 1f };
            ShipText(index, false, out s.name);
            ShipText(index, true, out s.description);
            return s;
        }
        static void Report(ModInfo mod, string message)
        {
            if (mod.Warnings.Contains(message)) return;
            mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
        static ItemData Clone(ItemData s) => new ItemData
        {
            index = s.index, name = s.name, description = s.description, type = s.type, category = s.category, categoryId = s.categoryId,
            techLevel = s.techLevel, occurrence = s.occurrence, minPrice = s.minPrice, maxPrice = s.maxPrice,
            lowestPriceSystem = s.lowestPriceSystem, highestPriceSystem = s.highestPriceSystem,
            statList = s.statList?.Select(e => new StatEntry { key = e.key, value = e.value }).ToList() ?? new List<StatEntry>(),
            blueprint = new List<BlueprintPart>(),   // a copy is no blueprint product
            attrKeys = (int[])s.attrKeys.Clone(), attrValues = (int[])s.attrValues.Clone(),
            modded = s.modded, lookIndex = s.lookIndex, modKey = s.modKey,
        };
        /// <summary>A ship blueprint's item (ModBlueprints): a commodity no shop or loot ever offers (it is a blueprint
        /// product), named and drawn as its ship.</summary>
        static ItemData Deed(int index, ModBlueprints.Def b) => new ItemData
        {
            index = index, name = b.DeedKey, description = "", type = "commodity", category = "Commodity", categoryId = 22, techLevel = 1,
            modded = true, modKey = b.DeedKey, lookIndex = index, blueprint = new List<BlueprintPart>(),
            statList = new List<StatEntry>(), attrKeys = new[] { 0, 1, 2 }, attrValues = new[] { index, 4, 22 },
        };
        static ItemData Placeholder(int index, string key)
        {
            var p = new ItemData
            {
                index = index, type = "commodity", category = "Commodity", categoryId = 22, modded = true, modKey = key, lookIndex = index,
                attrKeys = new[] { 0, 1, 2 }, attrValues = new[] { index, 4, 22 },
            };
            ItemText(index, false, out p.name);
            ItemText(index, true, out p.description);
            return p;
        }
        static void ApplyFields(ItemData it, JObject o, bool defaultEconomy)
        {
            const string F = ItemsFile;
            it.techLevel = Mathf.Clamp(ModJson.Int(o, "techLevel", it.techLevel, F), 0, Shop.MaxItemTech);
            it.occurrence = ModJson.Int(o, "occurrence", it.occurrence, F);
            if (ModJson.Get(o, "price") is JToken pr)
            {
                if (pr is JArray a && a.Count == 2) { it.minPrice = (int)a[0]; it.maxPrice = (int)a[1]; }
                else it.minPrice = it.maxPrice = ModJson.Int(o, "price", it.minPrice, F);
            }
            it.minPrice = ModJson.Int(o, "minPrice", it.minPrice, F);
            it.maxPrice = Math.Max(it.minPrice, ModJson.Int(o, "maxPrice", it.maxPrice, F));
            it.lowestPriceSystem = ModJson.Int(o, "lowestPriceSystem", it.lowestPriceSystem, F);
            it.highestPriceSystem = ModJson.Int(o, "highestPriceSystem", it.highestPriceSystem, F);
            SetAttr(it, 3, it.techLevel); SetAttr(it, 4, it.lowestPriceSystem); SetAttr(it, 5, it.highestPriceSystem);
            SetAttr(it, 6, it.occurrence); SetAttr(it, 7, it.minPrice); SetAttr(it, 8, it.maxPrice);
            // "stock": the units a shop holds, in place of the amount by item type (Shop.ModStock).
            if (Range(o, "stock", out int stockMin, out int stockMax))
            {
                stockMin = Mathf.Clamp(stockMin, 1, Shop.MaxStock);
                stockMax = Mathf.Clamp(stockMax, stockMin, Shop.MaxStock);
                SetAttr(it, Shop.StockMinAttr, stockMin); SetAttr(it, Shop.StockMaxAttr, stockMax);
            }
            if (ModJson.Has(o, "vosskOnly")) SetAttr(it, 60, ModJson.Bool(o, "vosskOnly", false, F) ? 1 : 0);
            if (ModJson.Has(o, "alwaysSoldAt")) SetAttr(it, 61, ModJson.Int(o, "alwaysSoldAt", -1, F));
            if (ModJson.Get(o, "stats") is JObject st)
                foreach (var prop in st.Properties()) SetAttr(it, ItemStats.IdOf(prop.Name), ModJson.Int(st, prop.Name, 0, F));
            if (ModJson.Get(o, "attributes") is JObject at)
                foreach (var prop in at.Properties()) if (int.TryParse(prop.Name, out int id) && id > 8) SetAttr(it, id, ModJson.Int(at, prop.Name, 0, F));
            if (defaultEconomy && ModJson.Get(o, "defaultEconomy") is JObject de) ApplyFields(it, de, false);
        }
        /// <summary>Sets an original attribute and the named stat that mirrors it (items.json's statList).</summary>
        static void SetAttr(ItemData it, int id, int value)
        {
            if (id < 0) return;
            int i = Array.IndexOf(it.attrKeys, id);
            if (i >= 0) it.attrValues[i] = value;
            else
            {
                it.attrKeys = it.attrKeys.Append(id).ToArray();
                it.attrValues = it.attrValues.Append(value).ToArray();
            }
            string stat = ItemStats.StatKeyOf(id);
            if (stat == null) return;
            it.statList ??= new List<StatEntry>();
            var e = it.statList.Find(s => s.key == stat);
            if (e != null) e.value = value;
            else it.statList.Add(new StatEntry { key = stat, value = value });
        }
        /// <summary>Database.Load calls this first: the original tables' sizes (the first modded number). An empty table is a load
        /// that found nothing (Resources not ready in a fresh Editor), never the size: kept, it numbered every mod from 0.</summary>
        public static void NoteOriginalCounts(Database db)
        {
            if (originalItems < 0 && db.Items.Count > 0) originalItems = db.Items.Count;
            if (originalShips < 0 && db.Ships.Count > 0) originalShips = db.Ships.Count;
            ModWorld.NoteOriginalCounts(db);
        }
        public static int OriginalItemCount => originalItems;
    }
}
