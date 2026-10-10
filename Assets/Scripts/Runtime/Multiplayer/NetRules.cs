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
        public const int CloakLosesPursuers = 1, PirateEvents = 2, CapitalShips = 4, InformerOriginalRule = 8,
                         KaamoStacking = 16, KaamoKeepsEquipment = 32;

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

        /// <summary>The options' rows: hidden for a player in someone else's session (the host decides).</summary>
        public static bool ClientLocked => NetGame.Active && !NetGame.IsServer;

        /// <summary>The player's own options as rules (a player host's start).</summary>
        public static int FromOptions() => Settings.LocalRules();
    }
}
