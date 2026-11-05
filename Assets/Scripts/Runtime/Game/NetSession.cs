// NetSession.cs
// The multiplayer session as the game's rules see it. GoF2.Game sits below the networking (GoF2.Net), so the rules can't
// ask NetGame themselves: NetHooksInstaller fills these providers when the game starts, and until then (the
// Editor's edit mode, a test) there is no session.

using System;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetSession
    {
        /// <summary>The station a session starts docked at (Dis), NetGame.Station.</summary>
        public const int StartStation = 70;

        public static Func<bool> ActiveProvider, SessionGameProvider, DebugAllowedProvider;
        public static Func<Database> DbProvider;
        public static Func<double> ServerTimeProvider;
        public static Func<ulong> LocalIdProvider;
        public static Func<int, int> OrbitSeedProvider;

        /// <summary>NetGame.Active: a session runs (host, client or dedicated server).</summary>
        public static bool Active => ActiveProvider != null && ActiveProvider();

        /// <summary>NetGame.SessionGame: this game belongs to a session, from its preparation until the menu after it, even
        /// once the connection went (never saved).</summary>
        public static bool SessionGame => SessionGameProvider != null && SessionGameProvider();

        /// <summary>NetGame.SessionLost: this game belonged to a session that has ended (a level loaded after it goes to the
        /// menu).</summary>
        public static bool SessionLost => SessionGame && !Active;

        /// <summary>NetGame.LocalId: this game's client id in the session, 0 outside one.</summary>
        public static ulong LocalId => LocalIdProvider != null && Active ? LocalIdProvider() : 0;

        /// <summary>NetGame.OrbitSeed: the seed of a station's shared asteroid field (the same for every player).</summary>
        public static int OrbitSeed(int station) => OrbitSeedProvider != null ? OrbitSeedProvider(station) : station;

        /// <summary>NetGame.DebugAllowed: the session allows the Debug menu.</summary>
        public static bool DebugAllowed => DebugAllowedProvider != null && DebugAllowedProvider();

        /// <summary>The session's clock in seconds (Netcode's server time), -1 = not connected.</summary>
        public static double ServerTime => ServerTimeProvider != null ? ServerTimeProvider() : -1;

        /// <summary>The economy the session's games play on (Session.Economy under the session's rules, SaveGame.ApplyProfile):
        /// the Android tables in every free-roam session and on a dedicated server (NetGame.PrepareSession); a co-op campaign
        /// the host's (the save it continues, or its new game's pick: NetCoop.PrepareHost), which NetState's spawn data
        /// carries to every guest before its world or profile is made (NetState.OnNetworkSpawn), so prices, stats and the
        /// shared stock are the same on every machine.</summary>
        public static Economy Economy = Economy.Android;

        /// <summary>The economy as NetState carries it: 0 Android (also a value it doesn't know), 1 Default.</summary>
        public static byte EconomyCode(Economy e) => (byte)(e == Economy.Default ? 1 : 0);

        /// <summary>NetState's economy byte as the economy: 1 Default, anything else Android.</summary>
        public static Economy EconomyFromCode(byte code) => code == 1 ? Economy.Default : Economy.Android;

        /// <summary>NetGame.Db: the session's tables (in its economy, Economy); outside a session the shared ones.</summary>
        public static Database Db => DbProvider != null ? DbProvider() : Database.Shared;
    }
}
