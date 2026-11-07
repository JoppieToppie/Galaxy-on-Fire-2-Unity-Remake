// NetRules.cs
// Remake: the Gameplay options that change how the game plays are one rule for the whole session. In a session every
// orbit's NPCs, loot and missions run in one player's game (the orbit authority, the mission runner) and the others see
// that game's result, so a rule each player picks for themselves would change with whoever runs the orbit. The host's
// (or a dedicated server's) values go to everyone in NetState (a bit mask); Settings' getters return the session's value
// while a session runs. A player host picks them with its own Gameplay options (and may change them while hosting, at
// once for everyone); a dedicated server with its command line and server settings (NetServerSettings). The options'
// rows are hidden for the other players. Options that only change what one player sees or how they steer (target lock,
// markers, cameras, dialogue, hints) stay each player's own.

using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetRules
    {
        public const int CloakLosesPursuers = SessionRules.CloakLosesPursuers, PirateEvents = SessionRules.PirateEvents,
                         CapitalShips = SessionRules.CapitalShips, InformerOriginalRule = SessionRules.InformerOriginalRule,
                         KaamoStacking = SessionRules.KaamoStacking, KaamoKeepsEquipment = SessionRules.KaamoKeepsEquipment,
                         CloakBay = SessionRules.CloakBay, EmpBreaksCloak = SessionRules.EmpBreaksCloak,
                         SmarterNpcTargeting = SessionRules.SmarterNpcTargeting, PreciseShipCollision = SessionRules.PreciseShipCollision,
                         OriginalCollision = SessionRules.OriginalCollision, Es2Aiming = SessionRules.Es2Aiming,
                         Es2NpcFlight = SessionRules.Es2NpcFlight;

        /// <summary>One rule: its bit, the server setting's key, the command-line flag that sets it the other way from
        /// its default, the server window's label, and the option's default.</summary>
        public struct Rule
        {
            public int bit;
            public string key, flag, label;
            public bool fallback;
        }

        public static readonly List<Rule> All = new List<Rule>
        {
            new Rule { bit = CloakLosesPursuers, key = "cloakhides", flag = "-cloakhides", label = "Cloaked players shake off NPC pursuers", fallback = false },
            new Rule { bit = PirateEvents, key = "pirateevents", flag = "-nopirateevents", label = "Pirate outposts and bosses", fallback = true },
            new Rule { bit = CapitalShips, key = "capitalships", flag = "-capitalships", label = "Capital ship enhancements", fallback = false },
            new Rule { bit = InformerOriginalRule, key = "informeroriginal", flag = "-informeroriginal", label = "Informer missions: the original's rule", fallback = false },
            new Rule { bit = KaamoStacking, key = "kaamostacking", flag = "-nokaamostacking", label = "Stackable Kaamo Club upgrades", fallback = true },
            new Rule { bit = KaamoKeepsEquipment, key = "kaamoequipment", flag = "-nokaamoequipment", label = "Stored ships keep their equipment", fallback = true },
            new Rule { bit = CloakBay, key = "cloakbay", flag = "-nocloakbay", label = "Cloak bay on cloaking ships", fallback = true },
            new Rule { bit = EmpBreaksCloak, key = "empcloak", flag = "-noempcloak", label = "EMP breaks cloaks", fallback = true },
            new Rule { bit = SmarterNpcTargeting, key = "smarttargeting", flag = "-smarttargeting", label = "Smarter NPC targeting", fallback = false },
            new Rule { bit = Es2Aiming, key = "es2aiming", flag = "-es2aiming", label = "EVERSPACE 2 NPC aiming", fallback = false },
            new Rule { bit = Es2NpcFlight, key = "es2flight", flag = "-es2flight", label = "EVERSPACE 2 NPC flight", fallback = false },
        };

        /// <summary>Every rule at its option's default (a dedicated server's start).</summary>
        public static int Defaults
        {
            get
            {
                int m = 0;
                foreach (var r in All) if (r.fallback) m |= r.bit;
                return m;
            }
        }

        /// <summary>The server's rules (NetGame.StartHost: the host's options; DedicatedServer.Boot: the defaults and the
        /// command line; NetServerSettings). NetState carries them.</summary>
        public static int Host { get; set; }

        /// <summary>The session's value of a rule; false outside a session or before NetState has arrived (the player's own
        /// option then).</summary>
        public static bool TryGet(int bit, out bool on)
        {
            on = false;
            if (!NetGame.Active) return false;
            if (NetGame.IsServer) { on = (Host & bit) != 0; return true; }
            var s = NetState.Instance;
            if (s == null || !s.IsSpawned) return false;
            on = (s.Rules & bit) != 0;
            return true;
        }

        /// <summary>A player host changed its option while hosting: for everyone at once.</summary>
        public static void HostChanged(int bit, bool on)
        {
            if (!NetGame.IsServer) return;
            Set(bit, on);
        }

        /// <summary>Server: one rule on or off (NetServerSettings, HostChanged).</summary>
        public static void Set(int bit, bool on)
        {
            Host = on ? Host | bit : Host & ~bit;
            NetState.Instance?.SetRules(Host);
        }

        // ---- the Kaamo upgrade limit: a number beside the bits ----

        public const string KaamoLevelKey = "kaamomaxlevel", KaamoLevelFlag = "-kaamomaxlevel",
                            KaamoLevelLabel = "Kaamo Club upgrade levels per ship";

        /// <summary>The server's Kaamo upgrade limit (Settings.KaamoMaxLevel; NetGame.StartHost: the host's option;
        /// DedicatedServer.Boot: -kaamomaxlevel, else the default). NetState carries it.</summary>
        public static int HostKaamoLevel { get; set; } = SessionRules.DefaultKaamoLevel;

        /// <summary>The session's Kaamo upgrade limit; false outside a session or before NetState has arrived.</summary>
        public static bool TryGetKaamoLevel(out int level)
        {
            level = 0;
            if (!NetGame.Active) return false;
            if (NetGame.IsServer) { level = HostKaamoLevel; return true; }
            var s = NetState.Instance;
            if (s == null || !s.IsSpawned || s.KaamoLevel <= 0) return false;
            level = s.KaamoLevel;
            return true;
        }

        /// <summary>Server: the Kaamo upgrade limit (NetServerSettings, HostKaamoLevelChanged).</summary>
        public static void SetKaamoLevel(int level)
        {
            HostKaamoLevel = SessionRules.ClampKaamoLevel(level);
            NetState.Instance?.SetKaamoLevel(HostKaamoLevel);
        }

        /// <summary>A player host changed its Kaamo upgrade limit while hosting: for everyone at once.</summary>
        public static void HostKaamoLevelChanged(int level)
        {
            if (NetGame.IsServer) SetKaamoLevel(level);
        }

        // ---- the flight model: a number beside the bits ----

        public const string FlightKey = "flightmodel", FlightFlag = "-flightmodel",
                            FlightLabel = "Flight model: gof2 (the game's), es2 (EVERSPACE 2 style) or player (each player's own)";

        /// <summary>The server's flight model (SessionRules.ParseFlight's values; NetGame.StartHost: SessionRules.DefaultFlight,
        /// each player's own; DedicatedServer.Boot: -flightmodel, else the default; an admin's /flightmodel or /set flightmodel). NetState carries
        /// it.</summary>
        public static int HostFlight { get; set; } = SessionRules.DefaultFlight;

        /// <summary>The session's flight model (SessionRules.PlayerFlight = each player's own); false outside a session or
        /// before NetState has arrived.</summary>
        public static bool TryGetFlight(out int style)
        {
            style = SessionRules.PlayerFlight;
            if (!NetGame.Active) return false;
            if (NetGame.IsServer) { style = HostFlight; return true; }
            var s = NetState.Instance;
            if (s == null || !s.IsSpawned || !s.FlightKnown) return false;
            style = s.FlightModel;
            return true;
        }

        /// <summary>Server: the flight model (NetServerSettings).</summary>
        public static void SetFlight(int style)
        {
            HostFlight = style == FlightStyles.Free || style == SessionRules.PlayerFlight ? style : FlightStyles.Original;
            NetState.Instance?.SetFlightModel(HostFlight);
        }

        // ---- the ship collision mode: two bits, set as one setting ----

        public const string CollisionKey = "shipcollision", CollisionFlag = "-shipcollision", LegacyCollisionFlag = "-precisecollision",
                            CollisionLabel = "Ship collision: exact (every hull's real shape), mixed (fighters and players' ships on the original's boxes, the rest exact) or original (the original's boxes everywhere)";

        /// <summary>The server's ship collision mode (SessionRules.CollisionExact / Mixed / Original), in Host's bits
        /// PreciseShipCollision / OriginalCollision.</summary>
        public static int HostCollision => SessionRules.CollisionOf(Host);

        /// <summary>Server: the ship collision mode (NetServerSettings, DedicatedServer.Boot).</summary>
        public static void SetCollision(int mode)
        {
            Host = SessionRules.WithCollision(Host, mode);
            NetState.Instance?.SetRules(Host);
        }

        /// <summary>The options' rows: hidden for a player in someone else's session (the host decides).</summary>
        public static bool ClientLocked => NetGame.Active && !NetGame.IsServer;

        /// <summary>The player's own options as rules (a player host's start).</summary>
        public static int FromOptions() => Settings.LocalRules();
    }
}
