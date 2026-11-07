// ModWantedTests.cs
// Feature: docs/notes/supernova.md "Most Wanted", Modding/README.md "Most Wanted": a mod's wanted.json adds criminals after
// the game's 25 (Modding.ModWanted), with their state by key (Session.ModWanted, saved by key), on their board in free play
// too while the game's stay off there, their kills counted apart (Session.ModBounties), their trips picked at docking.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModWantedTests : SavesModsTest
    {
        Database Load()
        {
            WriteMod("w", "t_wanted", "1.0", "", ("wanted.json",
                "[ { 'id': 'khador', 'name': 'Khador', 'description': 'The Void prophet.', 'board': 'terran', 'race': 'nivelian', " +
                "'ship': 5, 'weapon': 2, 'secondary': 36, 'hitpoints': 8000, 'reward': 250000, 'loot': 113, 'lootAmount': 3, " +
                "'wingmen': 3, 'route': [1, 20], 'escorts': [7, 8, 'nope:y'], 'taunt': 'You will not leave.', 'music': 'nope' }, " +
                "{ 'id': 'later', 'name': 'Later', 'ship': 5, 'requiredBounties': 1, 'board': 'terran' }, " +
                "{ 'id': 'broken', 'name': 'Broken', 'ship': 'nope:x' } ]"));
            ScanAndEnable("t_wanted");
            return Database.Load();
        }

        static int TerranStation(Database db) =>
            db.Stations.First(s => db.Systems.Find(x => x.index == s.system) is SystemData sys && sys.raceId == 0
                                   && sys.jumpRoutesTo != null && sys.jumpRoutesTo.Count > 0 && s.index != 108).index;

        [Test]
        public void AModsCriminalsFollowTheGames()
        {
            var db = Load();
            var w = db.Wanted.Find(x => x.key == "t_wanted:khador");
            Assert.IsNotNull(w);
            Assert.AreEqual(25, db.Wanted.FindIndex(x => x.IsMod), "after the game's 25");
            Assert.AreEqual(w.index, db.Wanted.IndexOf(w));
            Assert.AreEqual(0, w.board);
            Assert.AreEqual(2, w.race);
            Assert.AreEqual(5, w.ship);
            Assert.AreEqual(36, w.secondary);
            Assert.AreEqual(3, w.numWingmen);
            Assert.AreEqual("The Void prophet.", w.description);
            CollectionAssert.AreEqual(new[] { 7, 8 }, w.escorts, "a ship that isn't: left out of the escorts");
            Assert.AreEqual("You will not leave.", w.taunt);
            Assert.IsNull(w.music, "no such track: the game's wanted music");
            Assert.IsNull(db.Wanted.Find(x => x.key == "t_wanted:later").escorts);
            Assert.IsNull(db.Wanted.Find(x => x.key == "t_wanted:broken"), "a ship that isn't: left out");
            StringAssert.Contains("is no ship", AllMessages(Installed("t_wanted")));
        }

        [Test]
        public void InFreePlayOnlyTheModsAreOnTheBoard()
        {
            var db = Load();
            Session.FreePlay = true;
            int station = TerranStation(db);
            Assert.IsTrue(WantedBoard.Accessible(db, station));
            var list = WantedBoard.ListFor(db, station);
            Assert.IsTrue(list.All(x => x.IsMod), "the game's stay off in free play");
            Assert.IsTrue(list.Exists(x => x.key == "t_wanted:khador"));
        }

        [Test]
        public void TheStateIsByKeyAndTheBountiesApart()
        {
            var db = Load();
            Session.FreePlay = true;
            var w = db.Wanted.Find(x => x.key == "t_wanted:khador");
            int station = TerranStation(db);
            Assert.AreEqual(1, WantedBoard.ActivateNew(db, station), "khador on a trip; 'later' waits for a bounty");
            var st = WantedBoard.State(db, w.index);
            Assert.IsTrue(st.active);
            Assert.AreSame(st, Session.ModWanted["t_wanted:khador"]);
            Assert.IsTrue(st.current >= 0 && st.travelsTo >= 0);
            long credits = Session.Credits;
            Assert.AreEqual(250000, WantedBoard.OnKilled(db, w.index));
            Assert.AreEqual(credits + 250000, Session.Credits);
            Assert.AreEqual(1, Session.ModBounties[0]);
            Assert.AreEqual(0, Session.CollectedBounties[0], "the game's tally untouched");
            Assert.AreEqual(1, WantedBoard.ActivateNew(db, station), "one bounty: 'later' comes");
            var saved = ModWantedSave.To(ModWantedSave.From(Session.ModWanted));
            Assert.IsTrue(saved["t_wanted:khador"].terminated);
            Assert.IsTrue(saved["t_wanted:later"].active);
        }
    }
}
