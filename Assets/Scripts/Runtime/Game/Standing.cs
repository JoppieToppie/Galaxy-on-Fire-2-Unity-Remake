// Standing.cs
// The player's reputation (Standing 0x14282a.., Reference/research/npc_traffic_ai.md 4.1, ship_combat.md 2.7): two axes
// clamped to +-100, s0 = Terran (+) / Vossk (-), s1 = Nivelian (+) / Midorian (-); a new game starts at s0 = 30, s1 = 0.
//   applyDelict(race, p) (p doubled on Extreme): 0: s0 -= p, 1: s0 += p, 2: s1 -= p, 3: s1 += p
//   isEnemy(0) s0 <= -71, (1) s0 > 70, (2) s1 <= -71, (3) s1 > 70; isFriend the mirror (>= 71 / < -70)
//   callers: kill 5, steal cargo 2, EMP-disable 2, mission completed -5; a pirate kill applyDelict(enemyRace(system), 1)
// Pirates (8) and the Void (9) are always enemies. Between NPCs (no standing, PlayerFighter::update / PlayerTurret::
// pickEnemy): pirates, the Void and race 10 fight everyone else, Terran fights Vossk, Nivelian fights Midorian.
// Signatures (items 189-192, sort 29; combat_equipment.md 3.3, Standing::setPlayerSignatureRace 0x142898): with a
// signature of race S mounted, S is a friend (+100), its rival an enemy (-100), the other two races neutral (70) whatever
// the axes say; a pirate kill then gives no +1. Friendly fire on the signature's race (33 % / 10 % hc) or on any race
// 0..3 (50 % / 25 % hc) makes it invalid: the item is removed, delict 100 toward S, "Signature invalid" (324).
// Remake mods: a mod's races (Modding.ModRaces, race numbers from ModRaces.FirstRace) have one standing each
// (Session.ModRaceStanding), hostile at -71 and below, friends at 71 and above, always hostile when the mod race says so (like
// the pirates); their relations decide NPC vs NPC (ModRaces.Relation). Signatures are the game races' only.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public static class Standing
    {
        public const int Pirate = 8, Void = 9, Specter = 10;
        static readonly int[] EnemyRace = { 1, 0, 3, 2 };   // DAT_00252020, Standing::getEnemyRace

        public static bool Independent => Modding.ModPvpPiracy.IndependentReputation;
        public static bool PiracyLocked => Independent && GameHooks.LocalPilotIsPirate?.Invoke() == true;
        public static bool CanGainFromKills(int race) => !PiracyLocked && RawToward(race) > -71;
        public const string PiracyHistoryKey = "__piracy_reputation_history";
        public static string ReputationKey(int race) => Modding.ModRaces.Get(race)?.key ?? ("__base_standing_" + race);
        public static IEnumerable<int> ReputationRaces()
        {
            for (int race = 0; race < 4; race++) yield return race;
            foreach (var d in Modding.ModRaces.All) if (!d.hostile) yield return d.race;
        }
        internal static void StoreIndependent(int race, int value) =>
            Modding.ModRaces.SetStandingKey(ReputationKey(race), value);
        public static void EnforcePiracyLock()
        {
            if (!Independent) return;
            Modding.ModRaces.SetStandingKey(PiracyHistoryKey, 1);
            foreach (int race in ReputationRaces()) StoreIndependent(race, -100);
        }
        public static int EarnedWith(int race, int axis0, int axis1, IReadOnlyDictionary<string, int> standings)
        {
            if (Independent && (race >= 0 && race < 4 || Modding.ModRaces.Is(race)) && standings != null)
            {
                if (standings.TryGetValue(ReputationKey(race), out int value)) return Mathf.Clamp(value, -100, 100);
                if (standings.TryGetValue(PiracyHistoryKey, out int history) && history > 0) return -100;
            }
            if (Modding.ModRaces.Is(race)) return Modding.ModRaces.StandingIn(race, standings);
            return race switch { 0 => axis0, 1 => -axis0, 2 => axis1, 3 => -axis1, _ => 0 };
        }

        /// <summary>The mounted signature's race (items 189-192: Terran, Vossk, Nivelian, Midorian), -1 = none.</summary>
        public static int SignatureRace
        {
            get
            {
                foreach (var e in Session.Equipment) if (e.item >= 189 && e.item <= 192) return e.item - 189;
                return -1;
            }
        }

        /// <summary>Removes the mounted signature (Player::damage friendly fire): delict 100 toward its race.</summary>
        public static bool InvalidateSignature()
        {
            int s = SignatureRace;
            if (s < 0) return false;
            Session.Equipment.RemoveAll(e => e.item == 189 + s);
            ApplyDelict(s, 100);
            return true;
        }

        /// <summary>The value toward 'race' (positive = liked), races 0..3; a signature overrides it (Standing::getStanding).</summary>
        public static int Toward(int race) => PiracyLocked && (race >= 0 && race < 4 || Modding.ModRaces.Is(race)) ? -100 : TowardWith(race, Session.Standing[0], Session.Standing[1], SignatureRace, Session.ModRaceStanding);

        /// <summary>Toward with given axes, signature and mod race standings (multiplayer: another player's, NetPlayer;
        /// mod races null = this game's, Session.ModRaceStanding).</summary>
        public static int TowardWith(int race, int axis0, int axis1, int signature, IReadOnlyDictionary<string, int> modRaces = null)
        {
            var standings = modRaces ?? Session.ModRaceStanding;
            int earned = EarnedWith(race, axis0, axis1, standings);
            if (Modding.ModRaces.Is(race)) return earned;
            if (Independent && earned <= -71) return earned;
            if (signature >= 0 && signature < EnemyRace.Length && race >= 0 && race <= 3) return race == signature ? 100 : race == EnemyRace[signature] ? -100 : 70;
            return earned;
        }

        /// <summary>IsEnemy / IsFriend with given axes, signature and mod race standings (multiplayer: another player's).</summary>
        public static bool IsEnemyWith(int race, int axis0, int axis1, int signature, IReadOnlyDictionary<string, int> modRaces = null) =>
            Modding.ModRaces.Is(race) ? ModRaceEnemy(race, modRaces ?? Session.ModRaceStanding)
            : race == Pirate || race == Void || (race >= 0 && race <= 3 && TowardWith(race, axis0, axis1, signature, modRaces) <= -71);
        public static bool IsFriendWith(int race, int axis0, int axis1, int signature, IReadOnlyDictionary<string, int> modRaces = null) =>
            Modding.ModRaces.Is(race) ? ModRaceFriend(race, modRaces ?? Session.ModRaceStanding)
            : race >= 0 && race <= 3 && TowardWith(race, axis0, axis1, signature, modRaces) >= 71;

        /// <summary>A mod race is the player's enemy: always if "hostile", else at -71 and below.</summary>
        static bool ModRaceEnemy(int race, IReadOnlyDictionary<string, int> modRaces) =>
            Modding.ModRaces.Get(race) is var d && d != null && (d.hostile || EarnedWith(race, 0, 0, modRaces) <= -71);
        static bool ModRaceFriend(int race, IReadOnlyDictionary<string, int> modRaces) =>
            Modding.ModRaces.Get(race) is var d && d != null && !d.hostile && EarnedWith(race, 0, 0, modRaces) >= 71;

        /// <summary>The standing axes themselves (the Status window, mission bonuses).</summary>
        public static int RawToward(int race) =>
            PiracyLocked && (race >= 0 && race < 4 || Modding.ModRaces.Is(race)) ? -100 :
            EarnedWith(race, Session.Standing[0], Session.Standing[1], Session.ModRaceStanding);

        /// <summary>Remake (/standing, the quests' Standing step): the value toward race 0..3 moved by 'amount' (positive =
        /// liked more) or set to it, on its axis, -100..100; no Extreme doubling (no delict). Mods' mod races:
        /// ModRaces.SetStanding.</summary>
        public static void Move(int race, int amount, bool set)
        {
            if (race < 0 || race > 3 || Session.Standing == null) return;
            if (Independent)
            {
                if (!PiracyLocked) StoreIndependent(race, set ? amount : RawToward(race) + amount);
                return;
            }
            int axis = race / 2, sign = race % 2 == 0 ? 1 : -1;   // 0 / 2 the axis's + end, 1 / 3 its - end
            int toward = set ? amount : RawToward(race) + amount;
            Session.Standing[axis] = Mathf.Clamp(sign * Mathf.Clamp(toward, -100, 100), -100, 100);
        }

        /// <summary>Restore diplomatic relations to the neutral band.</summary>
        public static void Rehabilitate(int race)
        {
            if (Independent) { StoreIndependent(race, Modding.ModRaces.Is(race) ? 0 : -35); return; }
            if (Modding.ModRaces.Is(race)) Modding.ModRaces.SetStanding(race, 0);
            else Move(race, -35, true);
        }

        /// <summary>Standing::isEnemy (plus the always-hostile pirates and Void).</summary>
        public static bool IsEnemy(int race) => Modding.ModRaces.Is(race) ? (PiracyLocked || ModRaceEnemy(race, Session.ModRaceStanding)) : race == Pirate || race == Void || (race >= 0 && race <= 3 && Toward(race) <= -71);

        /// <summary>Standing::isFriend.</summary>
        public static bool IsFriend(int race) => Modding.ModRaces.Is(race) ? (!PiracyLocked && ModRaceFriend(race, Session.ModRaceStanding)) : race >= 0 && race <= 3 && Toward(race) >= 71;

        /// <summary>Standing::applyDelict: moves the race's axis away from it by p (x2 on Extreme).</summary>
        public static void ApplyDelict(int race, int p)
        {
            if (Independent)
            {
                if (PiracyLocked || !(race >= 0 && race < 4 || Modding.ModRaces.Is(race))) return;
                StoreIndependent(race, RawToward(race) - p * (Session.IsExtreme ? 2 : 1));
                return;
            }
            if (Modding.ModRaces.Is(race))
            {
                // a mod race: its standing down by p (x2 on Extreme), like one end of a game axis
                if (Session.IsExtreme) p *= 2;
                Modding.ModRaces.SetStanding(race, Modding.ModRaces.Standing(race) - p);
                return;
            }
            if (race < 0 || race > 3) return;
            if (Session.IsExtreme) p *= 2;
            int axis = race <= 1 ? 0 : 1;
            int sign = race == 0 || race == 2 ? -1 : 1;
            Session.Standing[axis] = Mathf.Clamp(Session.Standing[axis] + sign * p, -100, 100);
        }

        /// <summary>A kill by the player: race 0..3 -> delict 5; a pirate -> 1 toward the system race.</summary>
        public static void ApplyKill(int race, int systemRace)
        {
            if (Independent)
            {
                if (PiracyLocked) return;
                bool ordinary = race >= 0 && race < 4 || Modding.ModRaces.Get(race) is Modding.ModRaces.Def d && !d.hostile;
                int multiplier = Session.IsExtreme ? 2 : 1;
                if (ordinary)
                {
                    ApplyDelict(race, 5);
                    foreach (int ally in ReputationRaces())
                        if (ally != race && RacesHostile(ally, race) && CanGainFromKills(ally))
                            StoreIndependent(ally, RawToward(ally) + 5 * multiplier);
                }
                else if ((race == Pirate || Modding.ModRaces.Get(race)?.hostile == true) && SignatureRace < 0 &&
                         (systemRace >= 0 && systemRace < 4 || Modding.ModRaces.Get(systemRace) is Modding.ModRaces.Def owner && !owner.hostile) &&
                         RacesHostile(race, systemRace) && CanGainFromKills(systemRace))
                    StoreIndependent(systemRace, RawToward(systemRace) + multiplier);
                return;
            }
            if (Modding.ModRaces.Get(race) is Modding.ModRaces.Def f)
            {
                // a hostile mod race's ship, like a pirate: +1 with the system's mod race when it is that mod race's enemy; any
                // other mod race's: delict 5
                if (f.hostile)
                {
                    if (Modding.ModRaces.Is(systemRace) && Modding.ModRaces.Relation(race, systemRace) == Modding.ModRaces.Enemy)
                        Modding.ModRaces.SetStanding(systemRace, Modding.ModRaces.Standing(systemRace) + 1);
                    return;
                }
                ApplyDelict(race, 5);
                return;
            }
            if (race == Pirate) { int e = EnemyRaceOf(systemRace); if (e >= 0 && SignatureRace < 0) ApplyDelict(e, 1); return; }
            ApplyDelict(race, 5);
        }

        /// <summary>SolarSystem::getAttackRace / Standing::getEnemyRace: [1, 0, 3, 2][race], else pirates.</summary>
        public static int EnemyRaceOf(int race)
        {
            if (Modding.ModRaces.Is(race)) { var e = Modding.ModRaces.EnemiesOf(race); return e.Count > 0 ? e[0] : Pirate; }   // remake mods
            return race >= 0 && race <= 3 ? EnemyRace[race] : Pirate;
        }

        /// <summary>NPC vs NPC: do ships of races a and b fight each other? (The player's ship counts as race 0.)</summary>
        public static bool RacesHostile(int a, int b) =>
            Modding.ModRaces.Is(a) || Modding.ModRaces.Is(b) ? Modding.ModRaces.Relation(a, b) == Modding.ModRaces.Enemy :
            (a == Pirate) != (b == Pirate) || (a == Void) != (b == Void) || (a == Specter) != (b == Specter)
            || (a == 0 && b == 1) || (a == 1 && b == 0) || (a == 2 && b == 3) || (a == 3 && b == 2);

        /// <summary>A race by its word ("vossk", "pirates", or the language's race name), -1 = none (the admin commands, mods' characters).</summary>
        public static int RaceWord(string w)
        {
            switch (w.ToLowerInvariant())
            {
                case "terran": case "terrans": return 0;
                case "vossk": return 1;
                case "nivelian": case "nivelians": return 2;
                case "midorian": case "midorians": return 3;
                case "pirate": case "pirates": return Standing.Pirate;
                case "void": return Standing.Void;
                case "specter": case "specters": return Standing.Specter;
            }
            foreach (int r in new[] { 0, 1, 2, 3, Standing.Pirate, Standing.Void, Standing.Specter })
                if (string.Equals(GoF2Remake.Data.Localization.Get(406 + r), w, System.StringComparison.OrdinalIgnoreCase)) return r;
            // remake mods: a mod race by its key, id or name
            if (Modding.ModRaces.TryResolve(w, null, out int fr) && Modding.ModRaces.Is(fr)) return fr;
            foreach (var d in Modding.ModRaces.All)
                if (string.Equals(Modding.ModRaces.Name(d.race), w, System.StringComparison.OrdinalIgnoreCase)) return d.race;
            return -1;
        }

        /// <summary>The game race whose tables a race uses (hangar, bar, visitors, music, guns): a mod race's "look".</summary>
        public static int Look(int race) => Modding.ModRaces.Look(race);

        /// <summary>A race's name: the game's (text 406 + race) or a mod race's.</summary>
        public static string RaceName(int race) =>
            Modding.ModRaces.Is(race) ? Modding.ModRaces.Name(race) : race >= 0 && race <= 10 ? GoF2Remake.Data.Localization.Get(406 + race) : "";
    }
}
