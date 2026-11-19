// NetGameTests.cs
// Feature: CLAUDE.md "Multiplayer" > Menu / Online / Version check / Dedicated server, and "UI and platforms" > Version:
// the address field takes a join code (6 letters / digits) or "host" / "host:port" (default port 7777); a command-line
// option's value is the next argument unless that is another option (Unity drops empty arguments: "-password -maxplayers"
// means no password); pilot names are trimmed, tag-free and at most 20 characters; a session holds 2..100 players
// (default 16); the Editor's version and fingerprint are "editor"; release versions are yyyy.MM.dd.HHmm; arena matches
// are orbits from 100000; the server browser names a Relay region shortly ("europe-west4" EU West) and reads a ping
// as good up to 80 ms, fair up to 160, poor above (NetPing).
// Expected values: CLAUDE.md's numbers (7777, 16, 2..100, 20 characters, 100000, "editor"), hand-written addresses.

using System;
using System.Globalization;
using GoF2Remake.Multiplayer;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class NetGameTests
    {
        // ---- addresses and join codes ----

        [Test]
        public void TheDefaultPort()
        {
            Assert.AreEqual(7777, NetGame.DefaultPort);
        }

        [Test]
        public void AnAddressWithoutAPortTakesTheDefault()
        {
            Assert.IsTrue(NetGame.ParseAddress("192.168.1.10", out string ip, out ushort port));
            Assert.AreEqual("192.168.1.10", ip);
            Assert.AreEqual(7777, port);
        }

        [Test]
        public void AnAddressWithAPort()
        {
            Assert.IsTrue(NetGame.ParseAddress("  10.0.0.5:8000 ", out string ip, out ushort port));
            Assert.AreEqual("10.0.0.5", ip);
            Assert.AreEqual(8000, port);
        }

        [Test]
        public void BadPortsAreRefused()
        {
            Assert.IsFalse(NetGame.ParseAddress("10.0.0.5:0", out _, out _));
            Assert.IsFalse(NetGame.ParseAddress("10.0.0.5:70000", out _, out _));
            Assert.IsFalse(NetGame.ParseAddress("10.0.0.5:port", out _, out _));
        }

        [Test]
        public void AnIpv6AddressHasNoPortPart()
        {
            Assert.IsTrue(NetGame.ParseAddress("::1", out string ip, out ushort port));
            Assert.AreEqual("::1", ip);
            Assert.AreEqual(7777, port);
        }

        [Test]
        public void LocalhostIsLookedUp()
        {
            Assert.IsTrue(NetGame.ParseAddress("localhost:9000", out string ip, out ushort port));
            Assert.AreEqual("127.0.0.1", ip, "the first IPv4 address of the name");
            Assert.AreEqual(9000, port);
        }

        [Test]
        public void NothingIsNoAddress()
        {
            Assert.IsFalse(NetGame.ParseAddress("", out _, out _));
            Assert.IsFalse(NetGame.ParseAddress(null, out _, out _));
            Assert.IsFalse(NetGame.ParseAddress("   ", out _, out _));
        }

        [Test]
        public void JoinCodesAreSixLettersOrDigits()
        {
            Assert.IsTrue(NetGame.IsJoinCode("ABC123"));
            Assert.IsTrue(NetGame.IsJoinCode("abc123"));
            Assert.IsTrue(NetGame.IsJoinCode(" XYZ789 "), "trimmed");
            Assert.IsFalse(NetGame.IsJoinCode("ABC12"));
            Assert.IsFalse(NetGame.IsJoinCode("ABC1234"));
            Assert.IsFalse(NetGame.IsJoinCode("1.2.34"), "an address");
            Assert.IsFalse(NetGame.IsJoinCode("ab:123"));
            Assert.IsFalse(NetGame.IsJoinCode("ABÇ123"), "letters of the code are ASCII");
            Assert.IsFalse(NetGame.IsJoinCode(""));
            Assert.IsFalse(NetGame.IsJoinCode(null));
        }

        // ---- the command line ----

        [Test]
        public void AnOptionNotGivenHasNoValue()
        {
            Assert.IsNull(NetGame.CommandLineValue("-gof2-test-option-nobody-passes"));
        }

        [Test]
        public void AnOptionFollowedByAnotherOptionHasNoValue()
        {
            // Property of the real command line: every flag directly followed by another option ("-x -y") reads as no value,
            // every other flag as its next argument (a lone "-" is a value). The Editor's own line is used: the method reads
            // Environment.GetCommandLineArgs and can't be given another.
            var args = Environment.GetCommandLineArgs();
            int checkedFlags = 0;
            for (int i = 1; i < args.Length - 1; i++)
            {
                string flag = args[i];
                if (flag.Length < 2 || flag[0] != '-' || !char.IsLetter(flag[1])) continue;
                if (Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) != i) continue;   // the first only
                string next = args[i + 1];
                bool option = next.Length > 1 && next[0] == '-' && char.IsLetter(next[1]);
                Assert.AreEqual(option ? null : next, NetGame.CommandLineValue(flag), flag);
                checkedFlags++;
            }
            if (checkedFlags == 0) Assert.Ignore("the Editor was started without options");
        }

        // ---- names, passwords, player limit ----

        [Test]
        public void PilotNamesAreCleaned()
        {
            Assert.AreEqual(20, NetGame.MaxNameLength);
            Assert.AreEqual("bBob/b", NetGame.Clean("<b>Bob</b>"), "no rich-text tags");
            Assert.AreEqual("Ann", NetGame.Clean("  Ann  "));
            Assert.AreEqual(new string('x', 20), NetGame.Clean(new string('x', 25)));
            Assert.AreEqual("", NetGame.Clean(null));
        }

        [Test]
        public void PasswordsAreTrimmedAndCut()
        {
            Assert.AreEqual("secret", NetGame.CleanPassword("  secret "));
            Assert.AreEqual(NetGame.MaxPasswordLength, NetGame.CleanPassword(new string('p', 100)).Length);
            Assert.AreEqual("", NetGame.CleanPassword(null));
        }

        [Test]
        public void ThePasswordNeverTravelsAsTyped()
        {
            string proof = NetGame.PasswordProof(" secret ");
            StringAssert.StartsWith("p1:", proof);
            StringAssert.DoesNotContain("secret", proof);
            Assert.AreEqual(proof, NetGame.PasswordProof("secret"), "trimmed like the password itself");
            Assert.AreNotEqual(proof, NetGame.PasswordProof("Secret"));
            Assert.AreEqual("", NetGame.PasswordProof("  "), "no password: nothing");
            Assert.IsTrue(NetGame.SameProof(proof, NetGame.PasswordProof("secret")));
            Assert.IsFalse(NetGame.SameProof(proof, proof.Substring(0, proof.Length - 1)));
            Assert.IsFalse(NetGame.SameProof(proof, "secret"));
        }

        [Test]
        public void ThePlayerLimitIs2To100()
        {
            Assert.AreEqual(16, NetGame.DefaultMaxPlayers);
            Assert.AreEqual(100, NetGame.MaxPlayersLimit);
            int saved = NetGame.MaxPlayers;
            try
            {
                NetGame.MaxPlayers = 1;
                Assert.AreEqual(2, NetGame.MaxPlayers);
                NetGame.MaxPlayers = 500;
                Assert.AreEqual(100, NetGame.MaxPlayers);
                NetGame.MaxPlayers = 30;
                Assert.AreEqual(30, NetGame.MaxPlayers);
            }
            finally { NetGame.MaxPlayers = saved; }
        }

        [Test]
        public void NoSessionRunsInEditMode()
        {
            Assert.IsFalse(NetGame.Active);
            Assert.IsFalse(NetGame.IsServer);
            Assert.IsFalse(NetGame.Connected);
            Assert.IsFalse(NetGame.DebugAllowed);
            Assert.IsFalse(NetRules.ClientLocked);
        }

        [Test]
        public void OrbitSeedsDifferPerStationAndRepeat()
        {
            Assert.AreEqual(NetGame.OrbitSeed(78), NetGame.OrbitSeed(78));
            Assert.AreNotEqual(NetGame.OrbitSeed(78), NetGame.OrbitSeed(79));
            Assert.AreNotEqual(NetGame.OrbitSeed(-1), NetGame.OrbitSeed(0), "the Void's orbit too");
            Assert.AreEqual(unchecked(NetGame.OrbitSeed(11) - NetGame.OrbitSeed(10)), unchecked(NetGame.OrbitSeed(101) - NetGame.OrbitSeed(100)),
                            "a fixed step per station");
        }

        // ---- the version ----

        [Test]
        public void TheEditorsVersionIsEditor()
        {
            Assert.AreEqual("editor", BuildVersion.Text);
            Assert.AreEqual("editor", BuildVersion.Fingerprint);
            Assert.AreEqual("editor", NetGame.Version);
            Assert.AreEqual("editor", NetGame.Protocol, "sessions compare the fingerprint");
        }

        [Test]
        public void TheVersionFormatIsTheCommitsDateAndTime()
        {
            Assert.AreEqual("yyyy.MM.dd.HHmm", BuildVersion.Format);
            var t = DateTime.ParseExact("2026.10.14.0930", BuildVersion.Format, CultureInfo.InvariantCulture);
            Assert.AreEqual(new DateTime(2026, 10, 14, 9, 30, 0), t);
            Assert.AreEqual("2026.10.14.0930", t.ToString(BuildVersion.Format, CultureInfo.InvariantCulture));
            Assert.AreEqual("GoF2Build/BuildFingerprint", BuildVersion.FingerprintResource);
        }

        // ---- arena orbits ----

        [Test]
        public void ArenaOrbitsStartAt100000()
        {
            Assert.AreEqual(100000, NetArena.OrbitBase);
            Assert.IsTrue(NetArena.IsArenaOrbit(100000));
            Assert.IsTrue(NetArena.IsArenaOrbit(100007));
            Assert.IsFalse(NetArena.IsArenaOrbit(99999));
            Assert.IsFalse(NetArena.IsArenaOrbit(78));
            Assert.IsFalse(NetArena.IsArenaOrbit(-1), "the Void's orbit");
        }

        // ---- the server browser's region and ping (NetPing) ----

        [TestCase("europe-west4", "EU West")]
        [TestCase("europe-north1", "EU North")]
        [TestCase("us-east4", "US East")]
        [TestCase("us-central1", "US Central")]
        [TestCase("asia-southeast1", "Asia Southeast")]
        [TestCase("asia-northeast1", "Asia Northeast")]
        [TestCase("australia-southeast1", "Australia Southeast")]
        [TestCase("northamerica-northeast1", "N. America Northeast")]
        [TestCase("southamerica-east1", "S. America East")]
        [TestCase("asia", "Asia")]
        [TestCase("mars-dome2", "Mars Dome")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void ARelayRegionReadsAsAShortName(string id, string name)
        {
            Assert.AreEqual(name, NetPing.RegionName(id));
        }

        [Test]
        public void ThePingDotReadsGoodFairAndPoor()
        {
            Assert.AreEqual(-1, NetPing.Quality(-1), "not measured");
            Assert.AreEqual(0, NetPing.Quality(0));
            Assert.AreEqual(0, NetPing.Quality(80));
            Assert.AreEqual(1, NetPing.Quality(81));
            Assert.AreEqual(1, NetPing.Quality(160));
            Assert.AreEqual(2, NetPing.Quality(161));
            Assert.AreEqual(-1, NetPing.Of(null));
            Assert.AreEqual(-1, NetPing.Of("nowhere-west9"), "a region never measured");
        }
    }
}
