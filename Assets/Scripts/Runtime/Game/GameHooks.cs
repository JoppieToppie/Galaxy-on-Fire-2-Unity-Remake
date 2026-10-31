// GameHooks.cs
// What the game's rules (GoF2.Game) need from the layers above them: the squad missions, the shared stock, the faction
// prices, the profile upload, the session rules and the event graphs of GoF2.Net, the live ship, the controls and the
// hangar shadows' cache of GoF2.Space. Those layers fill these in at startup (their hook installers: NetHooksInstaller,
// SpaceHooksInstaller, from RuntimeInitializeOnLoadMethod and, for the Editor, EditorHookInstaller). Unset, each falls
// back to what single player does.

using System;
using System.Collections.Generic;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameHooks
    {
        // ---- squad missions (NetMissions) ---------------------------------------------------------------------------

        /// <summary>Why the squad can't take a mission now (the whole squad docked here), null = it can.</summary>
        public static Func<string> SquadAcceptRefusal;
        /// <summary>A mission was accepted: the squad gets it too.</summary>
        public static Action<FreelanceMission> MissionAccepted;
        /// <summary>The mission changed (Recovery's return trip): the squad's copies follow.</summary>
        public static Action<FreelanceMission, bool> MissionShared;
        /// <summary>Whether this player can deliver it (the containers / passengers aboard); unset: yes.</summary>
        public static Func<FreelanceMission, bool> CanDeliver;
        /// <summary>This player's share of the pay; unset: all of it.</summary>
        public static Func<FreelanceMission, int, int> SplitReward;
        /// <summary>The mission failed: this player's share of a lost wager; unset: all of it.</summary>
        public static Func<FreelanceMission, int, int> MissionFailed;
        /// <summary>Remake: starts a quest graph by name (single player; EventRunner.StartQuest: the reason it can't, null =
        /// started; 'quiet' = one under way or done is no error): a cruise drive encounter of kind "quest" (CruiseEncounters).</summary>
        public static Func<string, bool, string> StartQuest;

        // ---- event graphs (EventMissions, EventRunner) --------------------------------------------------------------

        /// <summary>The title of the event graph bar mission this player is on, null = none.</summary>
        public static Func<string> ActiveEventMissionTitle;

        public delegate bool ConditionCheck(string expression, out string error);
        /// <summary>An event graph expression on this game's state (EventRunner.Condition); unset: true.</summary>
        public static ConditionCheck Condition;

        /// <summary>A new game: single player's graph runs end (EventRunner.ResetLocal).</summary>
        public static Action ResetLocalEvents;

        // ---- shared stock, faction prices, profiles, rules, the pirate board (multiplayer) ------------------------------

        /// <summary>The station's stock is the session's shared one (NetStock.Shared).</summary>
        public static Func<int, bool> StockShared;
        /// <summary>Docking: the shared stock replaces the rolled one (NetStock.OnEnterStation).</summary>
        public static Action<StationStock> StockEntered;
        /// <summary>A unit bought / sold at the station: station, item, delta, price (NetStock.ItemChanged).</summary>
        public static Action<int, int, int, int> StockItemChanged;
        /// <summary>A dealer ship swapped: station, removed, added (NetStock.ShipChanged).</summary>
        public static Action<int, int, int> StockShipChanged;
        /// <summary>A faction station's buying / selling price (NetFactionsClient); unset: the price.</summary>
        public static Func<int, int, int> FactionBuyPrice, FactionSellPrice, FactionShipBuyPrice;
        /// <summary>Save point in a session: the profile goes to the server instead (NetProfileClient.Upload).</summary>
        public static Action ProfileUpload;
        /// <summary>The session's pirate board as JSON (NetState.PirateBoardJson), null = none.</summary>
        public static Func<string> SharedPirateBoard;

        public delegate bool RuleQuery(int bit, out bool on);
        /// <summary>The session's value of a gameplay rule (NetRules.TryGet); false = no session, the player's own option.</summary>
        public static RuleQuery SessionRule;
        /// <summary>A player host changed a rule's option (NetRules.HostChanged).</summary>
        public static Action<int, bool> RuleChangedByHost;
        public delegate bool NumberQuery(out int value);
        /// <summary>The session's Kaamo upgrade limit (NetRules.TryGetKaamoLevel); false = no session, the player's own option.</summary>
        public static NumberQuery SessionKaamoLevel;
        /// <summary>A player host changed the Kaamo upgrade limit (NetRules.HostKaamoLevelChanged).</summary>
        public static Action<int> KaamoLevelChangedByHost;
        /// <summary>The session's flight model (NetRules.TryGetFlight: FlightStyles.Original / Free, SessionRules.PlayerFlight);
        /// false = no session, the player's own option.</summary>
        public static NumberQuery SessionFlightStyle;

        // ---- remake mods: customizable ships and flight styles (PR #87; ModShipKits / ModShipSounds in GoF2.Space) ----------
        /// <summary>A customizable ship's build effect (ModShipKits.BuildEffect), null = not a kit ship.</summary>
        public static Func<int, Modding.KitEffect> KitBuildEffect;
        /// <summary>A kit ship's mounts of a slot type in its build (ModShipKits.BuildMounts), null = not a kit ship.</summary>
        public static Func<int, int, List<WeaponMount>> KitMounts;
        /// <summary>A ships.json entry's "sounds" parsed (ModShipSounds.ParseShip), null without one.</summary>
        public static Func<Newtonsoft.Json.Linq.JObject, Modding.ModInfo, string, object> ParseShipSounds;
        /// <summary>A ships.json "build" object as build text (ModShipKits.Build).</summary>
        public static Func<Newtonsoft.Json.Linq.JObject, string> KitBuildText;
        /// <summary>The flight style an event sets (Events.EventFlight.Forced), -1 = none.</summary>
        public static Func<int> EventFlightStyle;

        // ---- the flight (GoF2.Space) ---------------------------------------------------------------------------------

        /// <summary>Cheats.Repair: the live ship in flight too (hull, armor, shield, gamma).</summary>
        public static Action RepairLiveShip;
        /// <summary>Settings' "default settings": the key bindings too (GameControls.ResetToDefaults).</summary>
        public static Action ResetControls;
        /// <summary>The mods found by a scan (every id, on or off): the caches of the others go (ShipShadowBaker.PruneCache).</summary>
        public static Action<IEnumerable<string>> ModsScanned;
        /// <summary>A line on the flight HUD's message plate (Achievements.Progress, Hud::hudEventMedal); unset: not in flight.</summary>
        public static Action<string> FlightMessage;
    }
}
