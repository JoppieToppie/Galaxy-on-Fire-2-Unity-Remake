// ItemStats.cs
// The item attribute numbers (Item+0x30, Reference/research/shop.md 2.4) by name, for mods' "stats": the readable names
// (Modding/README.md lists them), and items.json's own statList keys, which ItemData.Stat reads and which
// ModContent keeps in step with the attribute.

using System;
using System.Collections.Generic;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ItemStats
    {
        /// <summary>The names a mod may use (the readable ones first; items.json's statList names work too).</summary>
        static readonly Dictionary<string, int> Ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["damage"] = 9, ["empDamage"] = 10, ["reloadMs"] = 11, ["lifetimeMs"] = 12, ["speed"] = 13, ["blastRadius"] = 14,
            ["guided"] = 15, ["automatic"] = 16, ["turretTurnSpeed"] = 17, ["shieldCapacity"] = 18, ["shieldRegenMs"] = 19, ["armor"] = 20,
            ["cargoBonus"] = 22, ["tractorAuto"] = 23, ["tractorLockMs"] = 24, ["boostSpeed"] = 25, ["boostRechargeMs"] = 26,
            ["boostDurationMs"] = 27, ["agility"] = 28, ["lockTimeMs"] = 29, ["showClassAAsteroids"] = 30, ["radarShowsCargo"] = 31,
            ["drillSpeed"] = 32, ["miningYield"] = 33, ["cabins"] = 34, ["cloakDurationMs"] = 35, ["cloakChargeMs"] = 36,
            ["energyCells"] = 38, ["fireRateFactor"] = 39, ["damageFactor"] = 40, ["emergencyMs"] = 41, ["timeExtenderMs"] = 42,
            ["timeExtenderCooldownMs"] = 43, ["collectorSpeed"] = 49, ["collectorMagnitude"] = 50, ["collectorRange"] = 51,
            ["gammaShielding"] = 52, ["beamRange"] = 53, ["beamStrength"] = 54, ["beamTargets"] = 55,
            // Remake-only attributes (100+; the original's end at 61): a drill (sort 19) with miningBeam 1 is a mining beam
            // (Flight.Mining's beam mode, MiningBeamExtraction, MiningBeamFx).
            ["miningBeam"] = 100, ["miningBeamRange"] = 101, ["miningBeamLayerMs"] = 102, ["miningBeamLook"] = 103,
            // A cloak (sort 21) with phaseCloak 1 also phases the ship through objects while cloaked (PlayerCloak.Phasing).
            ["phaseCloak"] = 104,
            // A gun or turret with missiles 1 fires homing missiles (Flight.Gun.missiles); salvoShots / salvoCount /
            // salvoGapMs fire several shots at once and several salvos per reload (Gun.TryFire); turretRange is how far an
            // auto turret looks for a target (PlayerTurret.PickTarget).
            ["missiles"] = 105, ["salvoShots"] = 106, ["salvoCount"] = 107, ["salvoGapMs"] = 108, ["turretRange"] = 109,
            // The flight (Modding.ModFlight, Database.BuildFlightStats): flightScope keeps a booster's / Steering Nozzle's
            // attributes (25-28) to one flight style (0 both, 1 the original only, 2 free flight only); the bonuses are
            // percentages (20 = +20 %) for both styles, the original only or free flight only; freeBoostFactor = free
            // flight's boost top speed in hundredths of the top speed (350 = x3.5) instead of the booster's.
            ["flightScope"] = 110, ["topSpeed"] = 111, ["turnRate"] = 112, ["strafeSpeed"] = 113,
            ["originalTopSpeed"] = 114, ["originalTurnRate"] = 115, ["originalStrafeSpeed"] = 116,
            ["freeTopSpeed"] = 117, ["freeTurnRate"] = 118, ["freeStrafeSpeed"] = 119, ["freeAcceleration"] = 120,
            ["freeBoostFactor"] = 121,
            // A tractor beam (sort 13) with tractorTargets 2+ pulls that many crates at once, by itself, within tractorRange
            // game units, without the salvage lock (Flight.CombatRadar's extra beams; like the transfusion beams' attr 55).
            ["tractorTargets"] = 122, ["tractorRange"] = 123,
            // A weapon's damage to a shield in percent (Flight.Gun.ShieldFactor; default 100, the Disruptor Laser and items
            // based on it 200): the part the shield doesn't absorb hits the armor and hull at its plain value.
            ["shieldDamage"] = 124,
            // A weapon's echo (Flight.GunEcho): a second, weaker blast at each hit, echoDelayMs after it (0 = none), at
            // echoPercent of the damage, EMP and size; echoRadius is that blast's reach in game units (0 = blastRadius, 14);
            // echoExplosion is the explosion type it shows (0 ship, 8-10 scatter, 11 shock blast, 13 fireworks; -1 none).
            ["echoDelayMs"] = 127, ["echoPercent"] = 128, ["echoRadius"] = 129, ["echoExplosion"] = 130,
            // Equipment with missileDefense (a reach in game units) lets the ship's auto turrets shoot down the homing
            // missiles flying at it within that reach (Flight.PointDefense; EVERSPACE 2's Missile Defense System).
            ["missileDefense"] = 131,
            // An ore's mining time in percent of the game's (Flight.MiningGame / MiningBeamExtraction; default 100): 200 takes
            // each layer twice as long for the same ore (EVERSPACE 2's DT_Mining_Yield: its crystals 10 s against the ores' 5 s).
            ["miningTime"] = 132,
            // A cruise drive (an item based on the Khador Drive 85) with cruiseSpeed > 0: the speed factor x 100, cruiseRange
            // the units flown per leg of energy cells, cruiseChargeMs its charge (attr 37), energyCells (38) the cells a leg
            // costs (Flight.CruiseDrive, Navigation; Modding/README.md "Cruise drives").
            // interstellar 1: it also flies the space between the systems (InterstellarSpace).
            ["cruiseSpeed"] = 133, ["cruiseRange"] = 134, ["cruiseChargeMs"] = 37, ["interstellar"] = 135,
            // items.json's statList names
            ["steerable"] = 15, ["handling"] = 17,
            ["loadingTimeMs"] = 11, ["range"] = 12, ["projectileSpeed"] = 13, ["magnitude"] = 14, ["shieldRegenTime"] = 19,
            ["automatic_2"] = 23, ["timeToLock"] = 24, ["timeToLock_2"] = 29, ["handling_2"] = 32, ["cabinSize"] = 34,
            ["effect_35"] = 35, ["loadingSpeed_36"] = 36, ["loadingSpeed_37"] = 37, ["energyConsumption"] = 38, ["effect_41"] = 41,
            ["effect_42"] = 42, ["loadingSpeed_43"] = 43, ["speed_49"] = 49, ["magnitude_50"] = 50, ["range_51"] = 51,
            ["range_53"] = 53, ["effect_54"] = 54, ["count"] = 55, ["effect_56"] = 56, ["showInfo"] = 57, ["showOnRadar_58"] = 58,
            ["plasmaConsumption"] = 59,
        };

        /// <summary>items.json's statList key for an attribute (the one ItemData.Stat reads), null = none.</summary>
        static readonly Dictionary<int, string> StatKeys = new Dictionary<int, string>
        {
            [9] = "damage", [10] = "empDamage", [11] = "loadingTimeMs", [12] = "range", [13] = "projectileSpeed", [14] = "magnitude",
            [15] = "steerable", [16] = "automatic", [17] = "handling", [18] = "shieldCapacity", [19] = "shieldRegenTime", [20] = "armor",
            [22] = "cargoBonus", [23] = "automatic_2", [24] = "timeToLock", [25] = "boostSpeed", [26] = "boostRechargeMs",
            [27] = "boostDurationMs", [28] = "agility", [29] = "timeToLock_2", [30] = "showClassAAsteroids", [31] = "radarShowsCargo",
            [32] = "handling_2", [33] = "miningYield", [34] = "cabinSize", [35] = "effect_35", [36] = "loadingSpeed_36",
            [37] = "loadingSpeed_37", [38] = "energyConsumption", [39] = "fireRateFactor", [40] = "damageFactor", [41] = "effect_41",
            [42] = "effect_42", [43] = "loadingSpeed_43", [49] = "speed_49", [50] = "magnitude_50", [51] = "range_51",
            [52] = "gammaShielding", [53] = "range_53", [54] = "effect_54", [55] = "count", [56] = "effect_56", [57] = "showInfo",
            [58] = "showOnRadar_58", [59] = "plasmaConsumption", [60] = "raceSpecific_guess", [61] = "iconIndex_guess",
        };

        /// <summary>The attribute number of a stat name, -1 = unknown.</summary>
        public static int IdOf(string name) => name != null && Ids.TryGetValue(name, out int id) ? id : -1;

        public static string StatKeyOf(int id) => StatKeys.TryGetValue(id, out var k) ? k : null;

        /// <summary>The readable names, for the docs and error messages.</summary>
        public static IEnumerable<KeyValuePair<string, int>> Readable()
        {
            foreach (var kv in Ids) { if (kv.Key == "loadingTimeMs") yield break; yield return kv; }
        }
    }
}
