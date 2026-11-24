using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.Modding;

namespace GoF2Remake.Multiplayer
{
    internal static class PlayerIdentity
    {
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
            foreach (int body in Bodies(race))
                for (int variant = 0; variant < BuiltInCount(body); variant++)
                    yield return (BuiltInKey(race, body, variant), $"Portrait {label++}");

            foreach (var character in ModCharacters.All())
                if (character.playerPortrait && character.hasRace && character.race == race)
                    yield return (character.key, character.Name);
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
