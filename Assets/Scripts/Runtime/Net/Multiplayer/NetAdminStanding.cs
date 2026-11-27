// NetAdminStanding.cs
// Remake-only: /standing [players] <race> <amount> [set] (admins, the event graphs' Standing node, the Mod
// Builder quests' "Standing" step): moves each player's standing toward a game race (terran, vossk, nivelian, midorian:
// its axis, Standing's two) or a mod's race (Modding.ModRaces, "id" or "mod_id:id") by the amount, or sets it with
// "set"; -100..100, no Extreme doubling (it is no delict). Applied in the player's own game (Order.Standing), like
// /credits. A mod race travels by its key (the order's text): race numbers can differ between games.
// The game's always-hostile sides (pirates, the Void, Specters, a "hostile" mod race) have no standing to move.

using System;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetAdmin
    {
        public static string StandingCommand(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            var words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool set = words.Length == 3 && words[2].Equals("set", StringComparison.OrdinalIgnoreCase);
            if (words.Length != 2 && !set) return Usage("standing");
            if (!int.TryParse(words[1], out int amount)) return Usage("standing");
            amount = Mathf.Clamp(amount, -200, 200);
            if (!ParseStandingSide(words[0], out int race, out string modRace, out string error)) return error;
            if (Standing.Independent && t is NetPlayer pilot && NetProfiles.Controls(pilot.OwnerClientId) && !NetProfiles.IsHostClient(pilot.OwnerClientId))
            {
                int sideRace = modRace != null ? Modding.ModRaces.Get(modRace).race : race;
                if (!NetFactions.RecordReputationMove(pilot.OwnerClientId, sideRace, amount, set))
                    return "Standing change was not applied (piracy lock or pending profile recovery).";
            }
            else Send(t, Order.Standing, race, amount, set ? 1 : 0, by, $"standing {words[0]} {(set ? "set to " : "")}{amount}", modRace);
            string side = modRace != null ? Modding.ModRaces.Name(Modding.ModRaces.Get(modRace).race) : Standing.RaceName(race);
            return set ? string.Format(X("mpAdmStandingSet", "{0}: standing toward {1} set to {2}."), t.DisplayName, side, Mathf.Clamp(amount, -100, 100))
                       : string.Format(X("mpAdmStanding", "{0}: standing toward {1} {2:+0;-0}."), t.DisplayName, side, amount);
        });

        /// <summary>A side whose standing can move: a game race 0-3 (race), or a mod race that isn't always hostile (modRace =
        /// its key, race -1).</summary>
        internal static bool ParseStandingSide(string word, out int race, out string modRace, out string error)
        {
            modRace = null;
            error = null;
            race = RaceWord(word);   // a game race, or a mod race by its key, id or name (Standing.RaceWord)
            if (race >= 0 && race <= 3) return true;
            if (race >= 0 && !Modding.ModRaces.Is(race))
            {
                error = X("mpAdmStandingFixed", "Pirates, the Void and the Specters are always enemies: they have no standing.");
                return false;
            }
            var d = race >= 0 ? Modding.ModRaces.Get(race) : null;
            if (d == null)
            {
                error = string.Format(X("mpAdmNoSide", "No race \"{0}\" (terran, vossk, nivelian, midorian or a mod's race)."), word);
                return false;
            }
            if (d.hostile)
            {
                error = string.Format(X("mpAdmRaceHostile", "{0} is always an enemy: it has no standing."), Modding.ModRaces.Name(d.race));
                return false;
            }
            race = -1;
            modRace = d.key;
            return true;
        }

        /// <summary>This game's side of /standing (Order.Standing): the game race's axis or the mod race's standing.</summary>
        static void ApplyStanding(int race, int amount, bool set, string modRace, string by)
        {
            string side;
            int now;
            if (!string.IsNullOrEmpty(modRace))
            {
                var d = Modding.ModRaces.Get(modRace);
                if (d == null || d.hostile) return;   // a mod this game doesn't have
                Modding.ModRaces.SetStanding(d.race, set ? amount : Modding.ModRaces.Standing(d.race) + amount);
                side = Modding.ModRaces.Name(d.race);
                now = Modding.ModRaces.Standing(d.race);
            }
            else
            {
                if (race < 0 || race > 3) return;
                Standing.Move(race, amount, set);
                side = Standing.RaceName(race);
                now = Standing.RawToward(race);
            }
            NetChat.Notice(string.Format(X("mpAdmStandingYou", "{0}: your standing toward {1} is now {2}."), by, side, now));
        }
    }
}
