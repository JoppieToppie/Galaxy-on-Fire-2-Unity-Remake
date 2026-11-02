// NetCommandsTests.cs
// Feature: CLAUDE.md "Multiplayer" > Chat commands: players are named whole (any case, the longest name the arguments
// start with), by a client id ("5" or "#5") or by a Minecraft-style selector: @a everyone, @s yourself (not for the
// console), @p the nearest other player, @r a random other player, @alive / @space / @docked / @dead / @survivors / @team,
// each narrowed by [orbit=<station>] (a number, a name or "void"); "/" lists the matching commands and Tab completes them
// and, after a command that takes a player, the selectors and players; /tp's destination is a player or a station
// [x y z | dock]; chat lines are one line, tag-free, at most 160 characters (commands 500).
// Without a session the event system's players are the single player's one LocalPilot (id 0), docked or flying at
// Session.StationIndex: that is the population these tests select from.
// Expected values: the documented selector rules and command forms (CLAUDE.md), the station numbers (78 Var Hastra,
// the Void = -1), the respawn spot 3 km in front of the station (60000 game units x 0.05 m).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Multiplayer;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class NetCommandsTests : SandboxTest
    {
        static IPilot Me => LocalPilot.Instance;

        [SetUp]
        public void NoScope() => NetCommands.Scope = null;

        [TearDown]
        public void ClearScope() => NetCommands.Scope = null;

        static void Finds(string args, IPilot issuer, int count, string rest = "")
        {
            var list = NetCommands.FindTargets(args, issuer, out string r, out string error);
            Assert.AreEqual(count, list.Count, args + ": " + error);
            if (count > 0) Assert.AreEqual(rest, r, args + ": the rest of the line");
        }

        static string Refused(string args, IPilot issuer)
        {
            var list = NetCommands.FindTargets(args, issuer, out _, out string error);
            Assert.IsEmpty(list, args);
            Assert.IsNotNull(error, args);
            return error;
        }

        // ---- selectors ----

        [Test]
        public void EveryoneIsTheOnePilot()
        {
            var list = NetCommands.FindTargets("@a hello there", null, out string rest, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(1, list.Count);
            Assert.AreSame(Me, list[0]);
            Assert.AreEqual("hello there", rest);
        }

        [Test]
        public void SelfNeedsAPlayer()
        {
            Finds("@s", Me, 1);
            Refused("@s", null);
        }

        [Test]
        public void NearestAndRandomAreOtherPlayers()
        {
            // Alone, there is no other player: "nobody matches".
            Assert.AreEqual(NetCommands.NobodyMatches, Refused("@p", Me));
            Assert.AreEqual(NetCommands.NobodyMatches, Refused("@r", Me));
            Refused("@p", null);   // the console has no nearest player
        }

        [Test]
        public void UnknownSelectors()
        {
            Assert.AreNotEqual(NetCommands.NobodyMatches, Refused("@q", Me));
            Assert.AreNotEqual(NetCommands.NobodyMatches, Refused("@everyone", Me));
        }

        [Test]
        public void TheTeamIsEveryoneOutsideAMission()
        {
            Finds("@team", null, 1);
            Assert.AreEqual(NetCommands.NobodyMatches, Refused("@survivors", null), "no event runs: nobody survived one");
        }

        [Test]
        public void TheOrbitFilter()
        {
            Session.StationIndex = 78;
            Finds("@a[orbit=78] rest", null, 1, "rest");
            Assert.AreEqual(NetCommands.NobodyMatches, Refused("@a[orbit=void]", null));
            Assert.AreEqual(NetCommands.NobodyMatches, Refused("@a[orbit=70]", null));
            Assert.AreNotEqual(NetCommands.NobodyMatches, Refused("@a[orbit=99999]", null), "no such station");
            Assert.AreNotEqual(NetCommands.NobodyMatches, Refused("@a[colour=red]", null), "no such filter");
            Assert.AreNotEqual(NetCommands.NobodyMatches, Refused("@a[orbit=78", null), "an open bracket");
            Session.StationIndex = 70;
            Finds("@a[orbit=70]", null, 1);
        }

        [Test]
        public void TheOrbitFilterTakesAStationName()
        {
            EventsTestUtil.RequireEnglish();
            Session.StationIndex = 78;
            Finds("@a[orbit=Var Hastra] go", null, 1, "go");
        }

        [Test]
        public void AMissionsScopeLimitsTheSelectorsOnly()
        {
            NetCommands.Scope = p => false;
            Assert.AreEqual(NetCommands.NobodyMatches, Refused("@a", null));
            Finds("0", null, 1);   // names and ids aren't selectors
        }

        // ---- names and ids ----

        [Test]
        public void APlayerByName()
        {
            string me = Me.DisplayName;
            Assert.IsNotEmpty(me);
            Assert.AreSame(Me, NetCommands.MatchPlayer(me + " some text", out string rest));
            Assert.AreEqual("some text", rest);
            Assert.AreSame(Me, NetCommands.MatchPlayer(me.ToUpperInvariant(), out rest), "any case");
            Assert.AreEqual("", rest);
            Assert.IsNull(NetCommands.MatchPlayer(me + "x", out _), "whole names only");
            Assert.IsNull(NetCommands.MatchPlayer("", out _));
        }

        [Test]
        public void APlayerById()
        {
            Finds("0 hi", null, 1, "hi");
            Finds("#0 hi", null, 1, "hi");
            Refused("7", null);
            Refused("Nobody By That Name", null);
        }

        [Test]
        public void FindTargetWantsOne()
        {
            Assert.AreSame(Me, NetCommands.FindTarget("@s", Me, out _, out string error));
            Assert.IsNull(error);
            Assert.IsNull(NetCommands.FindTarget("@p", Me, out _, out error));
            Assert.IsNotNull(error);
        }

        // ---- completion ----

        [Test]
        public void CompletionsOfACommandName()
        {
            var help = NetCommands.Completions("/he");
            Assert.AreEqual(1, help.Count);
            Assert.AreEqual("/help", help[0].line, "a command without arguments gets no space");
            var w = NetCommands.Completions("/w");
            Assert.IsTrue(w.Any(c => c.line == "/w "), "a command with arguments gets a space");
            var all = NetCommands.Completions("/");
            Assert.IsTrue(all.Any(c => c.line == "/help"));
            Assert.IsTrue(all.Any(c => c.line == "/players"));
            Assert.IsTrue(all.All(c => c.line.StartsWith("/") && !string.IsNullOrEmpty(c.description)));
        }

        [Test]
        public void TheAdminCommandsArentOfferedToAPlayer()
        {
            var all = NetCommands.Completions("/").Select(c => c.line.Trim()).ToList();
            foreach (var admin in new[] { "/kill", "/tp", "/give", "/spawn", "/event" })
                Assert.IsFalse(all.Contains(admin), admin);
        }

        [Test]
        public void NothingToComplete()
        {
            Assert.IsEmpty(NetCommands.Completions(""));
            Assert.IsEmpty(NetCommands.Completions(null));
            Assert.IsEmpty(NetCommands.Completions("hello"));
            Assert.IsEmpty(NetCommands.Completions("/nosuchcommand"));
            Assert.IsEmpty(NetCommands.Completions("/nosuchcommand x"));
            Assert.IsEmpty(NetCommands.Completions("/help x"), "help takes no player");
        }

        [Test]
        public void TheSelectorsAfterAPlayerCommand()
        {
            var sel = NetCommands.Completions("/w @");
            var labels = sel.Select(c => c.label).ToList();
            foreach (var s in new[] { "@a", "@s", "@p", "@r", "@alive", "@survivors", "@team", "@space", "@docked", "@dead" })
                Assert.Contains(s, labels);
            Assert.AreEqual(10, sel.Count);
            Assert.IsTrue(sel.Any(c => c.line == "/w @a "), "a player and text: a space after the selector");
            var invite = NetCommands.Completions("/invite @a");
            Assert.IsTrue(invite.Any(c => c.line == "/invite @a"), "a player only: no space");
            Assert.AreEqual(new[] { "@alive" }, NetCommands.Completions("/w @al").Select(c => c.label).ToArray());
            Assert.IsEmpty(NetCommands.Completions("/w @a x"), "past the player");
        }

        [Test]
        public void YourOwnNameIsntOfferedForAWhisper()
        {
            string me = Me.DisplayName;
            Assert.IsFalse(NetCommands.Completions("/w " + me.Substring(0, 1)).Any(c => c.label == me));
        }

        [Test]
        public void TheServerCommandsHaveNames()
        {
            var names = NetCommands.ServerCommandNames.ToList();
            foreach (var n in new[] { "players", "g", "l", "w", "kick", "tp", "kill", "heal", "give", "spawn", "event", "dialog" })
                Assert.Contains(n, names);
            Assert.IsFalse(names.Contains("help"), "help runs on this game");
            Assert.IsFalse(names.Contains("netstats"));
        }

        [Test]
        public void AnUnknownServerCommandIsNotOne()
        {
            Assert.IsNull(NetCommands.RunOnServer("nosuchcommand", "", null));
            Assert.IsNull(NetCommands.RunOnServer("help", "", null), "a local command");
        }

        [Test]
        public void ThePlayerOnlyCommandsRefuseTheConsole()
        {
            string answer = NetCommands.RunOnServer("invite", "Bob", null);
            Assert.IsNotNull(answer);
            Assert.IsNotEmpty(answer);
        }

        // ---- /tp destinations ----

        [Test]
        public void ADestinationStation()
        {
            Assert.IsTrue(NetTeleport.Parse("78", Me, out var d, out string error), error);
            Assert.AreEqual(NetTeleport.Kind.Orbit, d.kind);
            Assert.AreEqual(78, d.station);
            Assert.IsFalse(d.hasPos);
        }

        [Test]
        public void ADestinationWithCoordinates()
        {
            Assert.IsTrue(NetTeleport.Parse("78 100 -200 30000", Me, out var d, out string error), error);
            Assert.AreEqual(NetTeleport.Kind.Orbit, d.kind);
            Assert.IsTrue(d.hasPos);
            Assert.AreEqual(new Vector3(100, -200, 30000), d.pos);
            Assert.IsFalse(NetTeleport.Parse("78 100 200", Me, out _, out error), "x y z or nothing");
            Assert.IsFalse(NetTeleport.Parse("78 1e9 0 0", Me, out _, out error), "beyond 1e7");
        }

        [Test]
        public void ADestinationHangar()
        {
            Assert.IsTrue(NetTeleport.Parse("78 dock", Me, out var d, out _));
            Assert.AreEqual(NetTeleport.Kind.Hangar, d.kind);
            Assert.IsTrue(NetTeleport.Parse("78 HANGAR", Me, out d, out _));
            Assert.AreEqual(NetTeleport.Kind.Hangar, d.kind);
            Assert.IsFalse(NetTeleport.Parse("void dock", Me, out _, out string error), "the Void has no hangar");
            Assert.IsNotNull(error);
        }

        [Test]
        public void TheVoidIsAnOrbit()
        {
            Assert.IsTrue(NetTeleport.Parse("void", Me, out var d, out _));
            Assert.AreEqual(Session.VoidOrbit, d.station);
            Assert.AreEqual(-1, Session.VoidOrbit);
        }

        [Test]
        public void ADestinationPlayer()
        {
            Assert.IsTrue(NetTeleport.Parse("@s", Me, out var d, out _));
            Assert.AreEqual(NetTeleport.Kind.Player, d.kind);
            Assert.AreEqual(0UL, d.player);
            Assert.IsTrue(NetTeleport.Parse(Me.DisplayName, Me, out d, out _));
            Assert.AreEqual(NetTeleport.Kind.Player, d.kind);
            Assert.IsFalse(NetTeleport.Parse("@s 78", Me, out _, out _), "one destination");
            Assert.IsFalse(NetTeleport.Parse("99999", Me, out _, out _));
            Assert.IsFalse(NetTeleport.Parse("", Me, out _, out _));
        }

        [Test]
        public void StationsByNumberNameOrVoid()
        {
            Assert.IsTrue(NetTeleport.ParseStation("78 1 2 3", out int st, out string rest));
            Assert.AreEqual(78, st);
            Assert.AreEqual("1 2 3", rest);
            Assert.IsTrue(NetTeleport.ParseStation("VOID", out st, out rest));
            Assert.AreEqual(-1, st);
            Assert.IsFalse(NetTeleport.ParseStation("99999", out _, out _));
            Assert.IsFalse(NetTeleport.ParseStation("Nowhere", out _, out _));
        }

        [Test]
        public void StationsByName()
        {
            EventsTestUtil.RequireEnglish();
            Assert.IsTrue(NetTeleport.ParseStation("var hastra dock", out int st, out string rest));
            Assert.AreEqual(78, st);
            Assert.AreEqual("dock", rest);
            Assert.IsFalse(NetTeleport.ParseStation("Var Hastrax", out _, out _), "whole words");
        }

        [Test]
        public void TheRespawnSpotIs3KmInFront()
        {
            Assert.AreEqual(new Vector3(0f, 0f, 60000f), NetTeleport.RespawnSpot);
            Assert.AreEqual(3000f, NetTeleport.RespawnSpot.z * 0.05f, 1e-3f, "metres");
        }

        // ---- chat lines ----

        [Test]
        public void ChatLinesAreCleaned()
        {
            Assert.AreEqual(160, NetChat.MaxLength);
            Assert.AreEqual(500, NetChat.MaxCommandLength);
            Assert.AreEqual("bhi/b", NetChat.Clean("<b>hi</b>"));
            Assert.AreEqual("one two", NetChat.Clean(" one\ntwo "));
            Assert.AreEqual(160, NetChat.Clean(new string('a', 300)).Length);
            Assert.AreEqual("", NetChat.Clean(null));
        }

        // /wormhole (NetAdmin.TryParseWormhole -> EventWormhole's order text "station x y z destination seconds").
        [Test]
        public void AWormholeOrder()
        {
            Assert.IsTrue(NetAdmin.TryParseWormhole("78 100 -200 30000 to 70 for 45", out string p));
            Assert.AreEqual("78 100 -200 30000 70 45", p);
            Assert.IsTrue(EventWormhole.TryParse(p, out int st, out var at, out int to, out float s));
            Assert.AreEqual(78, st);
            Assert.AreEqual(new Vector3(100, -200, 30000), at);
            Assert.AreEqual(70, to);
            Assert.AreEqual(45f, s);
            Assert.IsTrue(NetAdmin.TryParseWormhole("Var Hastra to void", out p), "names, the default spot, the Void");
            var def = NetAdmin.WormholeDefaultAt;
            Assert.AreEqual($"78 {def.x} {def.y} {def.z} {Session.VoidOrbit} 0", p);
            Assert.IsTrue(NetAdmin.TryParseWormhole("void 0 0 0 to -1", out p), "in the Void, a variable's -1 = the Void");
            Assert.AreEqual($"{Session.VoidOrbit} 0 0 0 {Session.VoidOrbit} 0", p);
            Assert.IsFalse(NetAdmin.TryParseWormhole("78", out _), "no destination");
            Assert.IsFalse(NetAdmin.TryParseWormhole("78 to nowhere", out _));
            Assert.IsFalse(NetAdmin.TryParseWormhole("78 to 70 for", out _));
            Assert.IsFalse(NetAdmin.TryParseWormhole("999 to 70", out _), "no such station");
        }

        // Remake (a modder's suggestions): /take hands items in (single player: applied at once); /restrict khador;
        // /spawn's hull / guns factors.
        [Test]
        public void TakeAndKhadorOnly()
        {
            Shop.AddToCargo(22, 10);
            NetAdmin.Take("@a 22 4", Me);
            Assert.AreEqual(6, Shop.CargoOf(22));
            NetAdmin.Take("@a 22 all", Me);
            Assert.AreEqual(0, Shop.CargoOf(22));
            StringAssert.StartsWith("/take", NetAdmin.Take("@a", Me), "no item: the usage");
            Assert.AreEqual("", NetAdmin.Restrict("@a khador", Me));
            Assert.AreEqual("", NetAdmin.Restrict("@a off", Me));
        }

        [Test]
        public void SpawnFactors()
        {
            var take = typeof(NetAdmin).GetMethod("TakeFactor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object[] Run(string rest, string word) { var a = new object[] { rest, word, 0f }; a = new object[] { a[0], a[1], a[2] }; bool ok = (bool)take.Invoke(null, a); return new object[] { ok, a[0], a[2] }; }
            var r = Run("Hiro 3 enemy hull 2.5 guns x1.5", "hull");
            Assert.IsTrue((bool)r[0]);
            Assert.AreEqual("Hiro 3 enemy guns x1.5", r[1]);
            Assert.AreEqual(2.5f, (float)r[2]);
            r = Run((string)r[1], "guns");
            Assert.AreEqual("Hiro 3 enemy", r[1]);
            Assert.AreEqual(1.5f, (float)r[2], "an x before the number is fine");
            r = Run("Hiro", "hull");
            Assert.IsTrue((bool)r[0]);
            Assert.AreEqual(1f, (float)r[2], "none: as usual");
            Assert.IsFalse((bool)Run("Hiro hull", "hull")[0], "no number");
            Assert.IsFalse((bool)Run("Hiro hull -2", "hull")[0]);
            Assert.AreEqual(50f, (float)Run("Hiro hull 900", "hull")[2], "at most x50");
        }
    }
}
