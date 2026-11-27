using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    internal static class PlayerIdentity
    {
        // raceOrigins.json: opt-in, works for stock and mod races. No ongoing racial bonuses.
        // {"enabled":true,"origins":{"mod_id:grey":{"startingStation":"mod_id:hantra",
        // "discoverOwnModSystems":true,"discoveredSystems":[],"standing":{"terran":0},
        // "completedMissions":["i_have_some_information_you_migh"]}}}
        public const string OriginsFile = "raceOrigins.json";

        public static void ValidateOrigins(JObject options)
        {
            if (ModJson.Get(options, "enabled") is JToken enabled && enabled.Type != JTokenType.Boolean)
                throw new ModJsonException($"{OriginsFile}: enabled must be boolean.");
            ValidateNonnegativeInteger(options, "defaultStartingCredits");
            var entries = ModJson.Get(options, "origins");
            if (entries == null) return;
            if (!(entries is JObject origins)) throw new ModJsonException($"{OriginsFile}: origins must be an object.");
            foreach (var entry in origins.Properties())
            {
                if (!(entry.Value is JObject origin)) throw new ModJsonException($"{OriginsFile}: {entry.Name} must be an object.");
                ValidateNonnegativeInteger(origin, "startingShip");
                ValidateNonnegativeInteger(origin, "startingCredits");
                if (ModJson.Get(origin, "startingEquipment") is JToken equipment)
                {
                    if (!(equipment is JArray stacks)) throw new ModJsonException($"{OriginsFile}: startingEquipment must be an array.");
                    foreach (var stack in stacks)
                    {
                        if (!(stack is JObject item) || ModJson.Get(item, "item") == null)
                            throw new ModJsonException($"{OriginsFile}: invalid starting equipment.");
                        ValidateNonnegativeInteger(item, "item");
                        ValidateNonnegativeInteger(item, "amount", 1);
                    }
                }
                if (ModJson.Get(origin, "startingStation") is JToken station &&
                    station.Type != JTokenType.String && station.Type != JTokenType.Integer)
                    throw new ModJsonException($"{OriginsFile}: startingStation must be a station reference.");
                if (ModJson.Get(origin, "startAsPirate") is JToken pirate && pirate.Type != JTokenType.Boolean)
                    throw new ModJsonException($"{OriginsFile}: startAsPirate must be boolean.");
                if (ModJson.Get(origin, "discoverOwnModSystems") is JToken own && own.Type != JTokenType.Boolean)
                    throw new ModJsonException($"{OriginsFile}: discoverOwnModSystems must be boolean.");
                foreach (string field in new[] { "discoveredSystems", "completedMissions" })
                    if (ModJson.Get(origin, field) is JToken list)
                    {
                        if (!(list is JArray array)) throw new ModJsonException($"{OriginsFile}: {field} must be an array.");
                        foreach (var value in array)
                            if (value.Type != JTokenType.String && (field != "discoveredSystems" || value.Type != JTokenType.Integer))
                                throw new ModJsonException($"{OriginsFile}: invalid {field} reference.");
                    }
                if (ModJson.Get(origin, "standing") is JToken standing)
                {
                    if (!(standing is JObject reputations)) throw new ModJsonException($"{OriginsFile}: standing must be an object.");
                    foreach (var reputation in reputations.Properties())
                        if (reputation.Value.Type != JTokenType.Integer || (long)reputation.Value < -100 || (long)reputation.Value > 100)
                            throw new ModJsonException($"{OriginsFile}: standing must be an integer from -100 to 100.");
                }
            }
        }

        static void ValidateNonnegativeInteger(JObject obj, string key, int minimum = 0)
        {
            if (ModJson.Get(obj, key) is JToken value &&
                (value.Type != JTokenType.Integer || (long)value < minimum || (long)value > int.MaxValue))
                throw new ModJsonException($"{OriginsFile}: {key} must be an integer from {minimum} to {int.MaxValue}.");
        }

        static JObject FindOrigin(int race, out ModInfo source, out int defaultCredits)
        {
            JObject selected = null;
            source = null;
            defaultCredits = NetGame.StartCredits;
            foreach (var mod in ModManager.Active)
            {
                var token = ModJson.Read(mod.Source, OriginsFile);
                if (token == null) continue;
                if (!(token is JObject options)) throw new ModJsonException($"{OriginsFile}: expected an object.");
                ValidateOrigins(options);
                if (!ModJson.Bool(options, "enabled", false, OriginsFile) || !(ModJson.Get(options, "origins") is JObject entries)) continue;
                foreach (var entry in entries.Properties())
                    if (ModRaces.TryResolve(entry.Name, mod, out int resolved) && resolved == race)
                    {
                        selected = (JObject)entry.Value;
                        source = mod;
                        defaultCredits = ModJson.Int(options, "defaultStartingCredits", NetGame.StartCredits, OriginsFile);
                    }
            }
            return selected;
        }

        internal static int EmergencyShip(Database db, string raceKey)
        {
            if (db != null && TryResolveRace(raceKey, out int race))
            {
                var origin = FindOrigin(race, out _, out _);
                if (origin != null && ModJson.Get(origin, "startingShip") is JToken hull &&
                    hull.Type == JTokenType.Integer && NetGuard.Ship((int)hull))
                    return (int)hull;
            }
            return ModPvpPiracy.EmergencyShip(db);
        }

        // The server builds the initial profile rather than trusting the client's chosen origin.
        // Return null for an unconfigured race, preserving the normal multiplayer start.
        internal static SaveData CreateOriginProfile(Database db, int race, string portrait)
        {
            if (db == null) return null;
            var selected = FindOrigin(race, out var source, out int defaultCredits);
            if (selected == null) return null;

            string Reference(JToken value)
            {
                string reference = value.ToString();
                return int.TryParse(reference, out _) || reference.Contains(":") ? reference : source.Id + ":" + reference;
            }
            int station = NetGame.Station;
            if (ModJson.Get(selected, "startingStation") is JToken start &&
                (!ModWorld.TryResolveStation(Reference(start), out station) || ModWorld.IsMissingStation(station)))
                throw new ModJsonException($"{OriginsFile}: unknown starting station {start}.");
            var dock = db.Stations.Find(x => x.index == station);
            if (dock == null) throw new ModJsonException($"{OriginsFile}: starting station is unavailable.");
            bool startAsPirate = ModJson.Bool(selected, "startAsPirate", false, OriginsFile);
            if (startAsPirate && !ModPvpPiracy.Enabled)
                throw new ModJsonException($"{OriginsFile}: startAsPirate requires enabled piracy.json.");
            int ship = ModJson.Int(selected, "startingShip", NetGame.StartShip, OriginsFile);
            if (!NetGuard.Ship(ship)) throw new ModJsonException($"{OriginsFile}: invalid startingShip {ship}.");
            int credits = ModJson.Int(selected, "startingCredits", defaultCredits, OriginsFile);
            if (credits < 0) throw new ModJsonException($"{OriginsFile}: startingCredits must be nonnegative.");
            var equipment = NetGame.StartEquipment();
            if (ModJson.Get(selected, "startingEquipment") is JArray loadout)
            {
                equipment = new List<ItemStack>();
                foreach (var stack in loadout)
                {
                    if (!(stack is JObject entry)) throw new ModJsonException($"{OriginsFile}: invalid starting equipment.");
                    int item = ModJson.Int(entry, "item", -1, OriginsFile);
                    int amount = ModJson.Int(entry, "amount", 1, OriginsFile);
                    if (item < 0 || db.Item(item) == null || amount <= 0)
                        throw new ModJsonException($"{OriginsFile}: invalid starting equipment item or amount.");
                    equipment.Add(new ItemStack(item, amount));
                }
            }
            var save = new SaveData
            {
                originStartAsPirate = startAsPirate,
                campaign = (int)Campaign.GalaxyOnFire2, campaignMission = Story.LastIndex, freePlay = true,
                difficulty = Session.DifficultyNormal, economy = (int)NetSession.Economy,
                station = station, lastDockedStation = station, previousStation = -1,
                ship = ship, credits = credits,
                equipment = equipment, cargo = new List<ItemStack>(),
                standing = new[] { 30, 0 }, modRaceStanding = new List<string>(),
                playerIdentityRace = RaceKey(race), playerIdentityPortrait = portrait,
                runId = Guid.NewGuid().ToString("N"), graphQuestsDone = new List<string>(),
                visitedStations = new List<int> { station }, systemShown = new List<string>()
            };
            int size = 0;
            foreach (var system in db.Systems) size = Math.Max(size, system.index + 1);
            save.systemVisible = new bool[size];
            bool own = ModJson.Bool(selected, "discoverOwnModSystems", false, OriginsFile);
            foreach (var system in db.Systems)
                if (!ModWorld.IsMissingSystem(system.index))
                    save.systemVisible[system.index] = system.initiallyVisible ||
                        (own && system.raceId == race && ModWorld.SystemKey(system.index) != null);
            save.systemVisible[dock.system] = true;
            if (ModJson.Get(selected, "discoveredSystems") is JArray discovered)
                foreach (var entry in discovered)
                {
                    if (!ModWorld.TryResolveSystem(Reference(entry), out int id) || id < 0 || id >= size || ModWorld.IsMissingSystem(id))
                        throw new ModJsonException($"{OriginsFile}: unknown discovered system {entry}.");
                    save.systemVisible[id] = true;
                }
            // The base game has two paired reputation axes; mod races have independent standings.
            var assignedAxes = new Dictionary<int, int>();
            if (ModJson.Get(selected, "standing") is JObject standings)
                foreach (var entry in standings.Properties())
                {
                    if (!ModRaces.TryResolve(entry.Name, source, out int target))
                        throw new ModJsonException($"{OriginsFile}: unknown reputation race {entry.Name}.");
                    int value = (int)entry.Value;
                    if (ModRaces.Is(target)) save.modRaceStanding.Add(RaceKey(target) + "|" + value.ToString(CultureInfo.InvariantCulture));
                    else if (target >= 0 && target <= 3)
                    {
                        int axis = target / 2, signed = target % 2 == 0 ? value : -value;
                        if (assignedAxes.TryGetValue(axis, out int prior) && prior != signed)
                            throw new ModJsonException($"{OriginsFile}: conflicting paired reputation values.");
                        assignedAxes[axis] = save.standing[axis] = signed;
                    }
                    else throw new ModJsonException($"{OriginsFile}: {entry.Name} has no adjustable reputation.");
                }
            if (ModJson.Get(selected, "completedMissions") is JArray missions)
                foreach (var mission in missions)
                    if (!string.IsNullOrWhiteSpace((string)mission)) save.graphQuestsDone.Add((string)mission);
            return save;
        }

        internal static bool IsComplete(string race, string portrait) => !string.IsNullOrEmpty(race) && !string.IsNullOrEmpty(portrait);

        internal static bool CanChoose(string savedRace, string savedPortrait, string race) =>
            !IsComplete(savedRace, savedPortrait) && (string.IsNullOrEmpty(savedRace) || string.Equals(savedRace, race, StringComparison.Ordinal));

        // Void and Specters remain valid world/policy races, but cannot be chosen by players.
        internal static bool IsSelectableRace(int race)
        {
            if (race == 9 || race == 10) return false;
            if (!ModRaces.Is(race)) return true;
            foreach (var character in ModCharacters.All())
                if (OwnPortrait(character, race)) return true;
            return false;
        }

        static bool OwnPortrait(ModCharacters.Def character, int race) =>
            character != null && character.playerPortrait && character.hasRace &&
            character.race == race && !string.IsNullOrEmpty(character.portrait);

        internal static bool TryResolveSelectableRace(string key, out int race) =>
            TryResolveRace(key, out race) && IsSelectableRace(race);

        internal static string RaceKey(int race) => ModRaces.Is(race)
            ? ModRaces.Get(race)?.key : race.ToString(CultureInfo.InvariantCulture);

        internal static bool TryResolveRace(string key, out int race) =>
            ModRaces.TryResolve(key, null, out race) && string.Equals(key, RaceKey(race), StringComparison.Ordinal);

        // The game uses Terran/Nivelian bodies for Midorians and a Terran body for Cyborgs.
        // Pirates use Terran portraits and the Specter uses the game's alien portrait body.
        static IEnumerable<int> Bodies(int race)
        {
            int look = ModRaces.Look(race);
            switch (look)
            {
                case 0:
                case 8: yield return 0; yield return 10; break;
                case 3: yield return 2; yield return 0; yield return 10; break;
                case 5: yield return 0; break;
                case 10: yield return 12; break;
                default: yield return look; break;
            }
        }

        static int BuiltInCount(int body) => body == 9 || body == 12 ? 1 : 4;

        static string BuiltInKey(int race, int body, int variant) =>
            FormattableString.Invariant($"stock/{RaceKey(race)}/{body}-{variant}");

        internal static IEnumerable<(string Key, string Name)> Portraits(int race)
        {
            int label = 1;
            if (!ModRaces.Is(race))
                foreach (int body in Bodies(race))
                    for (int variant = 0; variant < BuiltInCount(body); variant++)
                        yield return (BuiltInKey(race, body, variant), $"Portrait {label++}");

            foreach (var character in ModCharacters.All())
                if (OwnPortrait(character, race))
                    yield return (character.key, character.Name);
        }

        // A new choice must come from this race's own selection pool. Older saved inherited portraits
        // remain renderable by TryResolvePortrait so completed identities are not silently changed.
        internal static bool TryResolveSelectionPortrait(int race, string key, out int[] descriptor, out ModCharacters.Def character)
        {
            descriptor = null;
            character = null;
            if (!IsSelectableRace(race) || !TryResolvePortrait(race, key, out descriptor, out character)) return false;
            return !ModRaces.Is(race) || OwnPortrait(character, race);
        }

        internal static bool TryResolvePortrait(int race, string key, out int[] descriptor, out ModCharacters.Def character)
        {
            descriptor = null;
            character = null;
            foreach (int body in Bodies(race))
                for (int variant = 0; variant < BuiltInCount(body); variant++)
                    if (string.Equals(key, BuiltInKey(race, body, variant), StringComparison.Ordinal))
                    {
                        descriptor = BuiltInDescriptor(body, variant);
                        return true;
                    }

            var modPortrait = ModCharacters.Find(key);
            if (modPortrait == null || !modPortrait.playerPortrait || !modPortrait.hasRace ||
                modPortrait.race != race || !string.Equals(key, modPortrait.key, StringComparison.Ordinal)) return false;
            character = modPortrait;
            return true;
        }

        static int[] BuiltInDescriptor(int body, int variant)
        {
            if (body == 12) return new[] { body, 0, -1, -1, -1 };
            if (body == 9) return new[] { body, 0, 0, 0, 0 };
            int[] counts = AgentData.PortraitPartCounts[body];
            var parts = new int[5];
            parts[0] = body;
            // Evenly spaced choices from the game's available combinations, with stable IDs.
            int combinations = 1;
            foreach (int count in counts) combinations *= Math.Max(1, count);
            int combination = variant * (combinations - 1) / (BuiltInCount(body) - 1);
            for (int part = 0; part < counts.Length; part++)
            {
                int choices = Math.Max(1, counts[part]);
                parts[part + 1] = counts[part] == 0 ? -1 : combination % choices;
                combination /= choices;
            }
            return parts;
        }
    }
}
