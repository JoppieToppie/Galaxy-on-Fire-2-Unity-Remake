// NetRulesTests.cs
// Feature: CLAUDE.md "Multiplayer" > Gameplay rules (NetRules: the Gameplay options that change how the game plays are
// one rule for the session, a bit each; a dedicated server starts at the options' defaults and each flag turns one the
// other way) and Server settings while running (NetServerSettings: /set <key> <value>, numbers clamped to their range,
// on / off toggles, saved to server_settings.json beside the profiles, an option given on the command line wins).
// Expected values: CLAUDE.md's rule list and defaults (cloak shakes off pursuers off, pirate outposts and bosses on,
// capital ship enhancements off, the informer missions' original rule off, stackable Kaamo upgrades on, stored ships keep
// their equipment on, the cloak bay on, EMP breaks cloaks on, smarter NPC targeting on; the ship collision mode, mixed by default, a setting of its own) and flags (-cloakhides,
// -nopirateevents, -capitalships, -informeroriginal, -nokaamostacking, -nokaamoequipment, -nocloakbay, -noempcloak,
// -smarttargeting, -shipcollision exact|mixed|original, the old -precisecollision = exact); the setting ranges (2..100 players,
// 0..100 claims per faction, 1..3650 lapse days).

using System.IO;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class NetRulesTests : SandboxTest
    {
        static readonly (string key, string flag, bool on)[] Documented =
        {
            ("cloakhides", "-cloakhides", false),
            ("pirateevents", "-nopirateevents", true),
            ("capitalships", "-capitalships", false),
            ("informeroriginal", "-informeroriginal", false),
            ("kaamostacking", "-nokaamostacking", true),
            ("kaamoequipment", "-nokaamoequipment", true),
            ("cloakbay", "-nocloakbay", true),
            ("empcloak", "-noempcloak", true),
            ("smarttargeting", "-smarttargeting", false),
            ("es2aiming", "-es2aiming", false),
            ("es2flight", "-es2flight", false),
        };

        [Test]
        public void ElevenRulesWithTheirFlagsAndDefaults()
        {
            Assert.AreEqual(Documented.Length, NetRules.All.Count);
            foreach (var (key, flag, on) in Documented)
            {
                var r = NetRules.All.Single(x => x.key == key);
                Assert.AreEqual(flag, r.flag, key);
                Assert.AreEqual(on, r.fallback, key + "'s default");
                Assert.AreEqual(on, flag.StartsWith("-no") || flag.StartsWith("-original"), key + ": a rule on by default is turned off by its flag");
                Assert.IsNotEmpty(r.label);
            }
        }

        [Test]
        public void EachRuleIsItsOwnBit()
        {
            int all = 0;
            foreach (var r in NetRules.All)
            {
                Assert.AreEqual(1, CountBits(r.bit), r.key + ": one bit");
                Assert.AreEqual(0, all & r.bit, r.key + ": a bit of its own");
                all |= r.bit;
            }
        }

        static int CountBits(int v) { int n = 0; while (v != 0) { n += v & 1; v >>= 1; } return n; }

        [Test]
        public void TheBitsAreTheSessionRules()
        {
            Assert.AreEqual(SessionRules.CloakLosesPursuers, NetRules.CloakLosesPursuers);
            Assert.AreEqual(SessionRules.PirateEvents, NetRules.PirateEvents);
            Assert.AreEqual(SessionRules.CapitalShips, NetRules.CapitalShips);
            Assert.AreEqual(SessionRules.InformerOriginalRule, NetRules.InformerOriginalRule);
            Assert.AreEqual(SessionRules.KaamoStacking, NetRules.KaamoStacking);
            Assert.AreEqual(SessionRules.KaamoKeepsEquipment, NetRules.KaamoKeepsEquipment);
            Assert.AreEqual(SessionRules.CloakBay, NetRules.CloakBay);
            Assert.AreEqual(SessionRules.EmpBreaksCloak, NetRules.EmpBreaksCloak);
            Assert.AreEqual(SessionRules.SmarterNpcTargeting, NetRules.SmarterNpcTargeting);
            Assert.AreEqual(SessionRules.PreciseShipCollision, NetRules.PreciseShipCollision);
            Assert.AreEqual(SessionRules.OriginalCollision, NetRules.OriginalCollision);
        }

        [Test]
        public void TheDefaultsAreTheRulesOnByDefault()
        {
            int expected = NetRules.PirateEvents | NetRules.KaamoStacking | NetRules.KaamoKeepsEquipment | NetRules.CloakBay | NetRules.EmpBreaksCloak;
            Assert.AreEqual(expected, NetRules.Defaults);
        }

        [Test]
        public void ThePlayersOwnOptionsAsRules()
        {
            int m = NetRules.FromOptions();
            Assert.AreEqual(Settings.CloakLosesPursuers, (m & NetRules.CloakLosesPursuers) != 0);
            Assert.AreEqual(Settings.PirateEvents, (m & NetRules.PirateEvents) != 0);
            Assert.AreEqual(Settings.CapitalShips, (m & NetRules.CapitalShips) != 0);
            Assert.AreEqual(Settings.KaamoStacking, (m & NetRules.KaamoStacking) != 0);
        }

        [Test]
        public void OutsideASessionNoRuleIsTheSessions()
        {
            foreach (var r in NetRules.All)
                Assert.IsFalse(NetRules.TryGet(r.bit, out _), r.key);
        }

        [Test]
        public void SetTurnsOneBit()
        {
            int saved = NetRules.Host;
            try
            {
                NetRules.Host = 0;
                NetRules.Set(NetRules.CapitalShips, true);
                Assert.AreEqual(NetRules.CapitalShips, NetRules.Host);
                NetRules.Set(NetRules.CloakBay, true);
                NetRules.Set(NetRules.CapitalShips, false);
                Assert.AreEqual(NetRules.CloakBay, NetRules.Host);
                NetRules.HostChanged(NetRules.EmpBreaksCloak, true);
                Assert.AreEqual(NetRules.CloakBay, NetRules.Host, "only a server takes a host's change");
            }
            finally { NetRules.Host = saved; }
        }

        // ---- NetServerSettings ----

        int savedMaxPlayers, savedClaims, savedLapse, savedRules, savedKaamoLevel, savedFlight;

        void BeginSettings()
        {
            savedMaxPlayers = NetGame.MaxPlayers;
            savedClaims = NetFactions.MaxClaims;
            savedLapse = NetFactions.LapseDays;
            savedRules = NetRules.Host;
            savedKaamoLevel = NetRules.HostKaamoLevel;
            savedFlight = NetRules.HostFlight;
            NetProfiles.Configure(false, NetProfiles.DefaultEarnPerMinute, Path.Combine(Sandbox, "ServerProfiles"));
            Directory.CreateDirectory(Path.Combine(Sandbox, "ServerProfiles"));
            NetServerSettings.Load(_ => false);
        }

        void EndSettings()
        {
            NetGame.MaxPlayers = savedMaxPlayers;
            NetFactions.MaxClaims = savedClaims;
            NetFactions.LapseDays = savedLapse;
            NetRules.Host = savedRules;
            NetRules.HostKaamoLevel = savedKaamoLevel;
            NetRules.HostFlight = savedFlight;
            ResetStatics(typeof(NetServerSettings));
            ResetStatics(typeof(NetProfiles));
        }

        internal static void ResetStatics(System.Type t)
        {
            var m = t.GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, t.Name + ".ResetStatics");
            m.Invoke(null, null);
        }

        [Test]
        public void EverySettingHasAKeyAndAnOption()
        {
            var keys = NetServerSettings.All.Select(s => s.key).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
            foreach (var k in new[] { "name", "password", "maxplayers", "allowdebug", "freepvp", "maxearn", "claimcost", "maxclaims", "claimdays", "siegecost", "toll", "kaamomaxlevel" })
                Assert.Contains(k, keys);
            foreach (var r in NetRules.All) Assert.Contains(r.key, keys, "the rules are settings too");
            foreach (var s in NetServerSettings.All) StringAssert.StartsWith("-", s.option, s.key);
        }

        [Test]
        public void NumbersAreClampedToTheirRange()
        {
            BeginSettings();
            try
            {
                NetServerSettings.Set("maxplayers", "500", "test");
                Assert.AreEqual(100, NetGame.MaxPlayers);
                NetServerSettings.Set("MAXPLAYERS", "1", "test");
                Assert.AreEqual(2, NetGame.MaxPlayers, "keys in any case");
                NetServerSettings.Set("maxclaims", "-5", "test");
                Assert.AreEqual(0, NetFactions.MaxClaims);
                NetServerSettings.Set("claimdays", "0", "test");
                Assert.AreEqual(1, NetFactions.LapseDays);
                NetServerSettings.Set("maxplayers", "1,000", "test");
                Assert.AreEqual(100, NetGame.MaxPlayers, "thousands separators are fine");
            }
            finally { EndSettings(); }
        }

        [Test]
        public void ShipCollisionIsASessionSetting()
        {
            BeginSettings();
            try
            {
                Assert.AreEqual(SessionRules.CollisionMixed, SessionRules.CollisionOf(NetRules.Defaults), "mixed by default");
                Assert.AreEqual(SessionRules.CollisionExact, SessionRules.ParseCollision("precise"), "the old name");
                Assert.AreEqual(-1, SessionRules.ParseCollision("sometimes"));
                Assert.That(NetServerSettings.Set("shipcollision", "sometimes", "test"), Does.Contain("exact, mixed or original"));
                NetServerSettings.Set("shipcollision", "Original", "test");
                Assert.AreEqual(SessionRules.CollisionOriginal, NetRules.HostCollision);
                Assert.AreNotEqual(0, NetRules.Host & NetRules.OriginalCollision);
                NetServerSettings.Set("shipcollision", "exact", "test");
                Assert.AreEqual(SessionRules.CollisionExact, NetRules.HostCollision);
                Assert.AreEqual(0, NetRules.Host & NetRules.OriginalCollision, "one mode at a time");
                Assert.AreEqual("exact", NetServerSettings.All.Single(s => s.key == "shipcollision").get());
                NetServerSettings.Set("shipcollision", "mixed", "test");
                Assert.AreEqual(0, NetRules.Host & (NetRules.PreciseShipCollision | NetRules.OriginalCollision));
            }
            finally { EndSettings(); }
        }

        [Test]
        public void FlightModelIsASessionSetting()
        {
            BeginSettings();
            try
            {
                Assert.AreEqual(SessionRules.PlayerFlight, SessionRules.DefaultFlight, "each player's own flight model by default");
                Assert.AreEqual(SessionRules.PlayerFlight, SessionRules.ParseFlight("any"));
                Assert.AreEqual(SessionRules.PlayerFlight, SessionRules.ParseFlight("Unrestricted"));
                var s = NetServerSettings.All.Single(x => x.key == "flightmodel");
                Assert.AreEqual("-flightmodel", s.option);
                NetServerSettings.Set("flightmodel", "es2", "test");
                Assert.AreEqual(FlightStyles.Free, NetRules.HostFlight);
                Assert.AreEqual("es2", NetServerSettings.All.Single(x => x.key == "flightmodel").get());
                NetServerSettings.Set("flightmodel", "free", "test");
                Assert.AreEqual(FlightStyles.Free, NetRules.HostFlight, "free is read as es2 too");
                NetServerSettings.Set("flightmodel", "player", "test");
                Assert.AreEqual(SessionRules.PlayerFlight, NetRules.HostFlight);
                NetServerSettings.Set("flightmodel", "GoF2", "test");
                Assert.AreEqual(FlightStyles.Original, NetRules.HostFlight);
                Assert.IsNotEmpty(NetServerSettings.Set("flightmodel", "fast", "test"), "an unknown model is refused");
                Assert.AreEqual(FlightStyles.Original, NetRules.HostFlight);
                Assert.AreEqual("gof2", SessionRules.FlightText(FlightStyles.Original));
                Assert.AreEqual(FlightStyles.Free, SessionRules.ParseFlight(SessionRules.FlightText(FlightStyles.Free)));
                Assert.AreEqual(SessionRules.PlayerFlight, SessionRules.ParseFlight(SessionRules.FlightText(SessionRules.PlayerFlight)));
                Assert.IsFalse(NetRules.TryGetFlight(out _), "outside a session the player's own option");
                // The admin command: no argument shows it, a value sets it like /set.
                StringAssert.Contains("gof2", NetCommands.RunOnServer("flightmodel", "", null));
                NetCommands.RunOnServer("flightmodel", "es2", null);
                Assert.AreEqual(FlightStyles.Free, NetRules.HostFlight);
                StringAssert.StartsWith("/flightmodel", NetCommands.RunOnServer("flightmodel", "fast", null));
                Assert.AreEqual(FlightStyles.Free, NetRules.HostFlight);
            }
            finally { EndSettings(); }
        }

        [Test]
        public void ASessionsFlightModelBeatsThePlayersOption()
        {
            var hook = GameHooks.SessionFlightStyle;
            int option = Settings.FlightStyle;
            try
            {
                Settings.FlightStyle = FlightStyles.Free;
                GameHooks.SessionFlightStyle = (out int v) => { v = FlightStyles.Original; return true; };

                Assert.AreEqual(FlightStyles.Original, ForcedNow(), "the session's GoF2 flight");
                GameHooks.SessionFlightStyle = (out int v) => { v = SessionRules.PlayerFlight; return true; };
                Assert.AreEqual(-1, ForcedNow(), "each player's own: nothing forced");
                GameHooks.SessionFlightStyle = (out int v) => { v = 0; return false; };
                Assert.AreEqual(-1, ForcedNow(), "no session");
            }
            finally { GameHooks.SessionFlightStyle = hook; Settings.FlightStyle = option; }
        }

        /// <summary>ModFlight's decision now (its per-frame cache skipped).</summary>
        static int ForcedNow()
        {
            var m = typeof(Modding.ModFlight).GetMethod("Decide", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "ModFlight.Decide");
            var args = new object[] { null };
            return (int)m.Invoke(null, args);
        }

        [Test]
        public void KaamoUpgradeLimitIsANumberSetting()
        {
            BeginSettings();
            try
            {
                var s = NetServerSettings.All.Single(x => x.key == "kaamomaxlevel");
                Assert.AreEqual("-kaamomaxlevel", s.option);
                Assert.AreEqual(NetServerSettings.All.FindIndex(x => x.key == "kaamostacking") + 1, NetServerSettings.All.IndexOf(s),
                    "right after the stacking rule");
                NetServerSettings.Set("kaamomaxlevel", "3", "test");
                Assert.AreEqual(3, NetRules.HostKaamoLevel);
                NetServerSettings.Set("kaamomaxlevel", "500", "test");
                Assert.AreEqual(SessionRules.MaxKaamoLevel, NetRules.HostKaamoLevel);
                NetServerSettings.Set("kaamomaxlevel", "1", "test");
                Assert.AreEqual(SessionRules.MinKaamoLevel, NetRules.HostKaamoLevel);
                Assert.IsFalse(NetRules.TryGetKaamoLevel(out _), "outside a session the player's own option");
            }
            finally { EndSettings(); }
        }

        [Test]
        public void TogglesTakeOnOffAndFriends()
        {
            BeginSettings();
            try
            {
                NetRules.Host = 0;
                NetServerSettings.Set("capitalships", "yes", "test");
                Assert.AreNotEqual(0, NetRules.Host & NetRules.CapitalShips);
                NetServerSettings.Set("capitalships", "0", "test");
                Assert.AreEqual(0, NetRules.Host & NetRules.CapitalShips);
                NetServerSettings.Set("cloakbay", "TRUE", "test");
                Assert.AreNotEqual(0, NetRules.Host & NetRules.CloakBay);
                NetServerSettings.Set("cloakbay", "off", "test");
                Assert.AreEqual(0, NetRules.Host & NetRules.CloakBay);
            }
            finally { EndSettings(); }
        }

        [Test]
        public void BadValuesChangeNothing()
        {
            BeginSettings();
            try
            {
                NetGame.MaxPlayers = 20;
                NetRules.Host = 0;
                Assert.IsNotEmpty(NetServerSettings.Set("maxplayers", "lots", "test"));
                Assert.AreEqual(20, NetGame.MaxPlayers);
                Assert.IsNotEmpty(NetServerSettings.Set("capitalships", "maybe", "test"));
                Assert.AreEqual(0, NetRules.Host);
                Assert.AreEqual(Localization.Extra("mpSetUnknown", "Unknown setting. /settings lists them."), NetServerSettings.Set("nosuchsetting", "1", "test"));
                Assert.AreEqual(Localization.Extra("mpSetUnknown", "Unknown setting. /settings lists them."), NetServerSettings.Set(null, "1", "test"));
            }
            finally { EndSettings(); }
        }

        [Test]
        public void ChangesAreSavedAndLoadedAgain()
        {
            BeginSettings();
            try
            {
                NetServerSettings.Set("maxplayers", "40", "test");
                Assert.IsTrue(File.Exists(Path.Combine(Sandbox, "ServerProfiles", "server_settings.json")));
                NetGame.MaxPlayers = 16;
                NetServerSettings.Load(_ => false);
                Assert.AreEqual(40, NetGame.MaxPlayers, "the saved value at the next start");
            }
            finally { EndSettings(); }
        }

        [Test]
        public void TheCommandLineWinsOverTheFile()
        {
            BeginSettings();
            try
            {
                NetServerSettings.Set("maxplayers", "40", "test");
                NetGame.MaxPlayers = 24;   // as the command line set it
                NetServerSettings.Load(option => option == "-maxplayers");
                Assert.AreEqual(24, NetGame.MaxPlayers);
                Assert.IsTrue(NetServerSettings.FromCommandLine(NetServerSettings.All.Single(s => s.key == "maxplayers")));
                Assert.IsFalse(NetServerSettings.FromCommandLine(NetServerSettings.All.Single(s => s.key == "maxclaims")));
            }
            finally { EndSettings(); }
        }
    }
}
