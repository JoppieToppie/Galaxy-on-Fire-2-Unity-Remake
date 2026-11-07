// ModFlight.cs
// Remake mods and the two flight styles (Settings.FlightStyle: the original, or free flight after EVERSPACE 2;
// FlightModel.Step / StepFree). A mod decides for itself whether its content touches one style, the other or both:
//   items.json "stats":  flightScope 0 both (default) | 1 the original only | 2 free flight only: the item's booster /
//                        Steering Nozzle attributes (boostSpeed, boostRechargeMs, boostDurationMs, agility) count only in
//                        that style;
//                        topSpeed / turnRate / strafeSpeed: percentages for both styles (20 = +20 %, -10 = 10 % less);
//                        originalTopSpeed / originalTurnRate / originalStrafeSpeed: the original only;
//                        freeTopSpeed / freeTurnRate / freeStrafeSpeed / freeAcceleration: free flight only;
//                        freeBoostFactor: free flight's boost top speed in hundredths of the top speed (350 = x3.5)
//                        instead of the booster's (its original boost speed x 3.2 / 3);
//                        fullStopDamage / lowSpeedResistance (138 / 139, both styles): EVERSPACE 2's ship passives, the
//                        primary guns' damage in % more while the ship stands still, and damage taken in % less once it
//                        has flown slower than 30 m/s for a second (FlightBonus, FlightModel.WeaponDamageFactor /
//                        DamageTakenFactor).
//   ships.json:          "flightStyle": "free" | "original" (this ship always flies so; default the player's), and
//                        "flight": { "topSpeed", "turnRate", "strafeSpeed", "acceleration", "boostFactor", "fullStopDamage",
//                                    "lowSpeedResistance" (both styles), "original": { ... }, "free": { ... } } with the same
//                                    meanings (new ships and overrides).
//   campaign.json:       "flightStyle": the mod campaign's games fly so.
//   gameoptions.json:    "flightStyle": a new-game option that, while on, sets the style.
//   event graphs:        /flightstyle [players] <free | original | player> (the Set Flight Model node; undone when the event
//                        ends), and the expression "freeflight" (1 in free flight).
// Who decides, first match: an event, the ship flown, the mod campaign, a new-game option, then the player's option.
// The bonuses add up (the ship's, then every mounted item's; a total never below -90 %); an item's freeBoostFactor wins over
// the ship's. The original has no acceleration (the throttle is at once), so acceleration is free flight's alone; a top
// speed bonus in the original scales the boost with it.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModFlight
    {
        public const int ScopeAttr = 110, ScopeBoth = 0, ScopeOriginal = 1, ScopeFree = 2;
        const int TopSpeed = 111, TurnRate = 112, StrafeSpeed = 113, OrigTopSpeed = 114, OrigTurnRate = 115, OrigStrafeSpeed = 116,
                  FreeTopSpeed = 117, FreeTurnRate = 118, FreeStrafeSpeed = 119, FreeAcceleration = 120, FreeBoostFactor = 121,
                  FullStopDamage = 138, LowSpeedResistance = 139;

        /// <summary>ships.json's "flight" fields (also inside its "original" / "free" objects).</summary>
        static readonly HashSet<string> BonusFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "topSpeed", "turnRate", "strafeSpeed", "acceleration", "boostFactor", "fullStopDamage", "lowSpeedResistance" };

        static int cachedFrame = -1, cachedStyle = -1;
        static string cachedReason;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cachedFrame = -1; cachedStyle = -1; cachedReason = null; }

        // ---- the style -----------------------------------------------------------------------------------------------

        /// <summary>The flight style a mod or an event sets now (FlightStyles.Original / Free), -1 = the player's option.</summary>
        public static int ForcedStyle { get { Refresh(); return cachedStyle; } }

        /// <summary>Who sets it (for the Options page), null = nobody.</summary>
        public static string ForcedBy { get { Refresh(); return cachedReason; } }

        static void Refresh()
        {
            if (cachedFrame == Time.frameCount) return;
            cachedFrame = Time.frameCount;
            cachedStyle = Decide(out cachedReason);
        }

        static int Decide(out string reason)
        {
            reason = null;
            int e = GameHooks.EventFlightStyle?.Invoke() ?? -1;
            if (e >= 0) { reason = "event"; return e; }
            // A multiplayer session's rule (NetRules.HostFlight): one flight model for everyone, so PvP is fair; it wins over a
            // mod ship's or campaign's style. SessionRules.PlayerFlight leaves it to the rest.
            if (GameHooks.SessionFlightStyle != null && GameHooks.SessionFlightStyle(out int sf) && sf >= 0) { reason = "session"; return sf; }
            if (ModManager.Active.Count == 0) return -1;   // no mods: nothing else can set it
            try
            {
                var ship = Database.Shared.Ship(Session.ShipIndex);
                if (ship != null && ship.flightStyle >= 0) { reason = "ship"; return ship.flightStyle; }
                var c = ModCampaigns.Current;
                if (c != null && c.flightStyle >= 0) { reason = "campaign"; return c.flightStyle; }
                foreach (var o in ModGameOptions.All())
                    if (o.flightStyle >= 0 && ModGameOptions.IsOn(o.Key)) { reason = "option"; return o.flightStyle; }
            }
            catch (Exception ex) { Debug.LogWarning($"Mods: flight style: {ex.Message}"); }
            return -1;
        }

        /// <summary>"free" / "original" / "player" (also "" and "any": the player's) -> FlightStyles, -1; false = unknown.</summary>
        public static bool TryParseStyle(string text, out int style)
        {
            style = -1;
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "": case "player": case "any": case "default": return true;
                case "free": case "freeflight": case "free flight": case "everspace": case "es2": style = FlightStyles.Free; return true;
                case "original": case "classic": case "gof2": style = FlightStyles.Original; return true;
            }
            return false;
        }

        /// <summary>A JSON file's "flightStyle" (campaign.json, gameoptions.json, ships.json): -1 when absent.</summary>
        public static int ReadStyle(JObject o, string where)
        {
            if (!ModJson.Has(o, "flightStyle")) return -1;
            string text = ModJson.Str(o, "flightStyle");
            if (!TryParseStyle(text, out int style))
                throw new ModJsonException($"{where}: \"flightStyle\": \"{text}\" is no flight style (\"free\", \"original\" or \"player\")");
            return style;
        }

        // ---- items ---------------------------------------------------------------------------------------------------

        /// <summary>Database.BuildFlightStats: a mounted item's flight bonuses into the original's and free flight's.</summary>
        public static void AddItemBonuses(ItemData item, FlightBonus original, FlightBonus free)
        {
            if (item?.attrKeys == null || item.attrKeys.Length == 0) return;
            float both = item.Attr(TopSpeed), turn = item.Attr(TurnRate), strafe = item.Attr(StrafeSpeed);
            original.topSpeed += both + item.Attr(OrigTopSpeed);
            original.turnRate += turn + item.Attr(OrigTurnRate);
            original.strafeSpeed += strafe + item.Attr(OrigStrafeSpeed);
            free.topSpeed += both + item.Attr(FreeTopSpeed);
            free.turnRate += turn + item.Attr(FreeTurnRate);
            free.strafeSpeed += strafe + item.Attr(FreeStrafeSpeed);
            free.acceleration += item.Attr(FreeAcceleration);
            int boost = item.Attr(FreeBoostFactor);
            if (boost > 0) free.boostFactor = boost;
            float stop = item.Attr(FullStopDamage), slow = item.Attr(LowSpeedResistance);   // EVERSPACE 2's passives: both styles
            original.fullStopDamage += stop; free.fullStopDamage += stop;
            original.lowSpeedResistance += slow; free.lowSpeedResistance += slow;
        }

        // ---- ships ---------------------------------------------------------------------------------------------------

        /// <summary>ModContent.ParseShips: "flightStyle" and "flight" are readable (errors with file and line).</summary>
        public static void CheckShip(JObject o, string where, ModInfo mod, string file)
        {
            ReadStyle(o, where);
            if (!(ModJson.Get(o, "flight") is JToken t)) return;
            if (!(t is JObject f)) throw new ModJsonException($"{where}: \"flight\" must be an object {{ \"turnRate\": 10, \"free\": {{ ... }} }}");
            CheckBonus(f, where, mod, file, true);
        }

        static void CheckBonus(JObject f, string where, ModInfo mod, string file, bool top)
        {
            foreach (var prop in f.Properties())
            {
                if (top && (string.Equals(prop.Name, "original", StringComparison.OrdinalIgnoreCase) || string.Equals(prop.Name, "free", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!(prop.Value is JObject sub)) throw new ModJsonException($"{ModJson.Where(prop, file)}: \"{prop.Name}\" must be an object {{ \"turnRate\": 10 }}");
                    CheckBonus(sub, where, mod, file, false);
                    continue;
                }
                if (!BonusFields.Contains(prop.Name))
                {
                    string warning = $"{ModJson.Where(prop, file)}: unknown flight field \"{prop.Name}\" (ignored)";
                    if (!mod.Warnings.Contains(warning)) mod.Warnings.Add(warning);
                    continue;
                }
                if (prop.Value.Type != JTokenType.Integer && prop.Value.Type != JTokenType.Float)
                    throw new ModJsonException($"{ModJson.Where(prop, file)}: \"{prop.Name}\" must be a number");
            }
        }

        /// <summary>ModContent.ApplyShips: a ship's "flightStyle" and "flight" (a new ship or an override; an override
        /// without them keeps what an earlier mod gave).</summary>
        public static void ApplyShip(ShipData s, JObject o, string where)
        {
            if (ModJson.Has(o, "flightStyle")) s.flightStyle = ReadStyle(o, where);
            if (!(ModJson.Get(o, "flight") is JObject f)) return;
            s.flightBoth = ReadBonus(f);
            s.flightOriginal = ModJson.Get(f, "original") is JObject og ? ReadBonus(og) : null;
            s.flightFree = ModJson.Get(f, "free") is JObject fr ? ReadBonus(fr) : null;
        }

        static FlightBonus ReadBonus(JObject f) => new FlightBonus
        {
            topSpeed = Num(f, "topSpeed"), turnRate = Num(f, "turnRate"), strafeSpeed = Num(f, "strafeSpeed"),
            acceleration = Num(f, "acceleration"), boostFactor = Mathf.Max(0, Mathf.RoundToInt(Num(f, "boostFactor"))),
            fullStopDamage = Num(f, "fullStopDamage"), lowSpeedResistance = Num(f, "lowSpeedResistance"),
        };

        static float Num(JObject f, string key) => ModJson.Get(f, key) is JToken v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : 0f;
    }
}
