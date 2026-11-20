// ModCampaignsTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Mod campaigns and New-game options (ModCampaigns.cs, ModGameOptions.cs):
// campaign.json { name, description, image, showTitle, startStation, startShip, credits, equipment [item | { item,
// amount }], cargo, standing [-100..100] x 2, quest, galaxy / items / ships "mod" | "all", trafficShips per race } starts a
// free-play game (index 20, Session.ModCampaign = the mod's id) docked at its station with that loadout, the quest pending;
// "galaxy": "mod" hides every system but the mod's (and its dependencies'). gameoptions.json [ { id, name, default } ]:
// options found by "mod:id" or by the id alone when only one mod has it, on in a game when Session.ModGameOptions holds the
// key; a new game takes the remembered choice (PlayerPrefs newgame_modopt_<key>) else the default.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModCampaignsTests : SavesModsTest
    {
        const string CampaignJson = @"{
            'name': 'Test Campaign', 'description': 'Somewhere else.', 'image': 'card.png', 'showTitle': false,
            'startStation': 70, 'startShip': 5, 'credits': 5000,
            'equipment': [2, { 'item': 36, 'amount': 6 }, 9999], 'cargo': [ { 'item': 155, 'amount': 12 } ],
            'standing': [150, -20], 'quest': 'intro_quest',
            'trafficShips': { 'pirate': [5], 'klingon': [1] }, 'oops': 1
        }";

        ModCampaigns.Def CampaignOn(string json = CampaignJson, string id = "t_camp")
        {
            WriteMod("c_" + id, id, "1.0", "", ("campaign.json", json));
            ScanAndEnable(id);
            return ModCampaigns.All().SingleOrDefault(c => c.Id == id);
        }

        [Test]
        public void TheCampaignIsRead()
        {
            var c = CampaignOn();
            Assert.IsNotNull(c);
            Assert.AreEqual("Test Campaign", c.Name);
            Assert.AreEqual("Somewhere else.", c.Description);
            Assert.AreEqual("card.png", c.image);
            Assert.IsFalse(c.showTitle);
            Assert.AreEqual("70", c.startStation);
            Assert.AreEqual("5", c.startShip);
            Assert.AreEqual(5000, c.credits);
            CollectionAssert.AreEqual(new[] { ("2", 1), ("36", 6), ("9999", 1) }, c.equipment);
            CollectionAssert.AreEqual(new[] { ("155", 12) }, c.cargo);
            CollectionAssert.AreEqual(new[] { 100, -20 }, c.standing, "clamped to -100..100");
            Assert.AreEqual("intro_quest", c.quest);
            Assert.IsFalse(c.modGalaxy);
            Assert.IsFalse(c.modItems);
            Assert.IsFalse(c.modShips);
            CollectionAssert.AreEqual(new[] { "5" }, c.trafficShips[8]);
            Assert.AreEqual(1, c.trafficShips.Count, "an unknown race is skipped");
            var m = Installed("t_camp");
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("unknown field \"oops\"")), AllMessages(m));
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("unknown race \"klingon\"")), AllMessages(m));
        }

        [Test]
        public void StartingSetsTheGameUp()
        {
            var c = CampaignOn();
            var db = Database.Load();
            Session.ResetNewGame();
            Assert.AreEqual("Station", ModCampaigns.Start(db, c, out string error), error);
            Assert.IsNull(error);
            Assert.AreEqual("t_camp", Session.ModCampaign);
            Assert.IsTrue(Session.FreePlay);
            Assert.AreEqual(20, Session.CampaignMission);
            Assert.AreEqual(70, Session.StationIndex);
            Assert.AreEqual(-1, Session.PreviousStationIndex);
            Assert.AreEqual(5, Session.ShipIndex);
            Assert.AreEqual(5000, Session.Credits);
            Assert.AreEqual("2x1 36x6", Describe(Session.Equipment), "an unknown item is skipped");
            Assert.AreEqual("155x12", Describe(Session.Cargo));
            CollectionAssert.AreEqual(new[] { 100, -20 }, Session.Standing);
            CollectionAssert.AreEquivalent(new[] { 70 }, Session.VisitedStations);
            Assert.IsNotNull(Session.SystemVisible);
            Assert.IsTrue(Session.SystemVisible[db.Stations[70].system]);
            Assert.AreEqual(1, Session.GraphQuests.Count);
            Assert.AreEqual("intro_quest", Session.GraphQuests[0].name);
            Assert.AreEqual(GraphQuestState.KindQuest, Session.GraphQuests[0].kind);
            Assert.AreEqual("Test Campaign", Session.GraphQuests[0].title);
            Assert.IsTrue(GraphQuestState.RestorePending);
            Assert.AreSame(c, ModCampaigns.Current);
            Assert.IsTrue(Installed("t_camp").Warnings.Any(w => w.Contains("equipment \"9999\" is no item")));
        }

        [Test]
        public void ACampaignGameIsSavedAndLoaded()
        {
            var c = CampaignOn();
            ModCampaigns.Start(Database.Load(), c, out _);
            SaveGame.Save(1);
            Session.ResetNewGame();
            Assert.IsNull(ModCampaigns.Current);
            SaveGame.Load(1);
            Assert.AreEqual("t_camp", Session.ModCampaign);
            Assert.AreSame(c, ModCampaigns.Current);
            Assert.AreEqual(5, Session.ShipIndex);
        }

        [Test]
        public void ABadStartIsRefused()
        {
            var c = CampaignOn(Q("{ 'name': 'Bad', 'startStation': 999, 'startShip': 5 }"));
            Session.ResetNewGame();
            Assert.IsNull(ModCampaigns.Start(Database.Load(), c, out string error));
            StringAssert.Contains("startStation", error);
            Assert.AreEqual("", Session.ModCampaign, "nothing changed");
            c.startStation = "70";
            c.startShip = "200";
            Assert.IsNull(ModCampaigns.Start(Database.Load(), c, out error));
            StringAssert.Contains("startShip", error);
        }

        [Test]
        public void AStartAndAShipAreRequired()
        {
            Assert.IsNull(CampaignOn(Q("{ 'name': 'No start', 'startShip': 5 }")));
            StringAssert.Contains("\"startStation\" is missing", AllMessages(Installed("t_camp")));
        }

        [Test]
        public void WithoutACampaignEverythingIsAllowed()
        {
            CampaignOn();
            var db = Database.Load();
            Session.ModCampaign = "";
            Assert.IsNull(ModCampaigns.Current);
            Assert.IsFalse(ModCampaigns.ModGalaxy);
            Assert.IsTrue(ModCampaigns.SystemAllowed(0));
            Assert.IsTrue(ModCampaigns.StationAllowed(db, 78));
            Assert.IsTrue(ModCampaigns.ItemAllowed(db, 2));
            Assert.IsTrue(ModCampaigns.ShipAllowed(5));
            Assert.AreEqual(7, ModCampaigns.TrafficShip(8, 7));
        }

        // #313 (a game mode as a mod): the campaign's economy and medals, and its id known before its tables load.
        const string ModeJson = "{ 'startStation': 70, 'startShip': 5, 'economy': 'original', 'medals': [3, 'champion', 'other:m'] }";

        [Test]
        public void ACampaignCanFixItsEconomyAndMedals()
        {
            var c = CampaignOn(ModeJson);
            Assert.AreEqual(Economy.Android, c.economy, "\"original\": the original game's tables");
            CollectionAssert.AreEqual(new[] { "3", "t_camp:champion", "other:m" }, c.medals, "a bare id is the campaign mod's");
            Assert.IsNull(CampaignOn(CampaignJson, "t_plain").economy, "none: the player picks");
            Assert.IsNull(ModCampaigns.All().Find(x => x.Id == "t_plain").medals, "none: every medal");
        }

        [Test]
        public void OnlyTheCampaignsMedalsAreAwarded()
        {
            var c = CampaignOn(ModeJson);
            var db = Database.Load();
            Session.ResetNewGame();
            Assert.IsTrue(ModCampaigns.MedalAllowed(0), "a GoF2 game: every medal");
            Session.ModCampaign = c.Id;
            Assert.IsTrue(ModCampaigns.MedalAllowed(3));
            Assert.IsFalse(ModCampaigns.MedalAllowed(0));
            Assert.IsFalse(ModCampaigns.MedalAllowed(35));
            Achievements.Check(db);
            Assert.AreEqual(0, Achievements.Grade(0), "no preset Veteran");
            Session.Medals[12] = 1;   // earned elsewhere (a carried-over save): not counted here
            Assert.AreEqual(0, Achievements.Earned().earned);
            Session.Medals[3] = 2;
            Assert.AreEqual(1, Achievements.Earned().earned);
        }

        [Test]
        public void TheSharedTablesFollowTheCampaign()
        {
            var c = CampaignOn(ModeJson);
            Session.ModCampaign = "";
            var gof2 = Database.Shared;
            Assert.AreSame(gof2, Database.Shared);
            Session.ModCampaign = c.Id;
            Assert.AreNotSame(gof2, Database.Shared, "a campaign's scripts may change its tables: loaded again for it");
        }

        [Test]
        public void TrafficShipsComeFromTheCampaign()
        {
            var c = CampaignOn();
            Session.ModCampaign = c.Id;
            Assert.AreEqual(5, ModCampaigns.TrafficShip(8, 7));
            Assert.AreEqual(5, ModCampaigns.TrafficShip(9, 7), "the Void and the other races fly the pirates' list");
            Assert.AreEqual(7, ModCampaigns.TrafficShip(0, 7), "no list for the Terrans: the game's pick");
        }

        [Test]
        public void ACampaignOfItsOwnGalaxyHidesTheRest()
        {
            string dir = WriteMod("gal", "t_gal", "1.0", "",
                ("systems.json", "[ { 'id': 'sys', 'name': 'Own', 'race': 0, 'position': [10, 10, 10] } ]"),
                ("stations.json", "[ { 'id': 'home', 'system': 't_gal:sys', 'name': 'Home' } ]"),
                ("items.json", "[ { 'id': 'gun', 'base': 2, 'name': 'Gun' } ]"),
                ("ships.json", "[ { 'id': 'yacht', 'model': 'y.glb', 'name': 'Yacht', 'price': 1000 } ]"),
                ("campaign.json", "{ 'name': 'Own galaxy', 'startStation': 't_gal:home', 'startShip': 't_gal:yacht', 'equipment': ['t_gal:gun'], 'galaxy': 'mod', 'items': 'mod', 'ships': 'mod' }"));
            WriteFile(dir, "y.glb", Dummy);
            ScanAndEnable("t_gal");
            var db = Database.Load();
            var c = ModCampaigns.All().Single();
            Session.ResetNewGame();
            Assert.AreEqual("Station", ModCampaigns.Start(db, c, out string error), error);
            Assert.AreEqual(135, Session.StationIndex);
            Assert.AreEqual(64, Session.ShipIndex);
            Assert.AreEqual("233x1", Describe(Session.Equipment));
            Assert.IsTrue(ModCampaigns.ModGalaxy);
            for (int s = 0; s < OriginalSystems; s++)
            {
                Assert.IsFalse(Session.SystemVisible[s], "system " + s);
                Assert.IsFalse(ModCampaigns.SystemAllowed(s), "system " + s);
            }
            Assert.IsTrue(Session.SystemVisible[34]);
            Assert.IsTrue(ModCampaigns.SystemAllowed(34));
            Assert.IsFalse(ModCampaigns.StationAllowed(db, 78));
            Assert.IsTrue(ModCampaigns.StationAllowed(db, 135));
            Assert.AreEqual(135, ModCampaigns.RandomStation(db));
            Assert.IsFalse(ModCampaigns.ItemAllowed(db, 2));
            Assert.IsTrue(ModCampaigns.ItemAllowed(db, 233));
            Assert.IsFalse(ModCampaigns.ShipAllowed(5));
            Assert.IsTrue(ModCampaigns.ShipAllowed(64));
        }

        [Test]
        public void ACampaignWhoseModIsOffIsntCurrent()
        {
            var c = CampaignOn();
            Session.ModCampaign = c.Id;
            ModManager.SetEnabled("t_camp", false);
            Assert.IsNull(ModCampaigns.Current);
            Assert.AreEqual(0, ModCampaigns.All().Count);
        }

        // ---- new-game options -------------------------------------------------------------------------------------------

        void OptionsOn()
        {
            WriteMod("o1", "t_opts", "1.0", "", ("gameoptions.json",
                "[ { 'id': 'extra', 'name': 'Extra', 'description': 'More.', 'default': true }, { 'id': 'hard', 'name': { 'en': 'Hard' } } ]"));
            WriteMod("o2", "t_opts2", "1.0", "", ("gameoptions.json", "[ { 'id': 'hard', 'name': 'Harder', 'image': 'missing.png' } ]"));
            ScanAndEnable("t_opts", "t_opts2");
            foreach (var d in ModGameOptions.All()) Assume.That(PlayerPrefs.HasKey("newgame_modopt_" + d.Key), Is.False, "a remembered choice exists for " + d.Key);
        }

        [Test]
        public void OptionsAreReadInLoadOrder()
        {
            OptionsOn();
            var all = ModGameOptions.All();
            CollectionAssert.AreEqual(new[] { "t_opts:extra", "t_opts:hard", "t_opts2:hard" }, all.Select(d => d.Key).ToArray());
            Assert.IsTrue(all[0].defaultOn);
            Assert.IsFalse(all[1].defaultOn);
            Assert.AreEqual("Extra", all[0].Name);
            Assert.AreEqual("More.", all[0].Description);
            Assert.AreEqual("Hard", all[1].Name);
            Assert.IsTrue(all[0].showTitle);
            Assert.IsTrue(Installed("t_opts2").Warnings.Any(w => w.Contains("missing.png")));
        }

        [Test]
        public void ASettingIsAGameplayToggleNotACard()
        {
            WriteMod("o3", "t_set", "1.0", "", ("gameoptions.json",
                "[ { 'id': 'raids', 'name': 'Raids', 'setting': true, 'default': true }, { 'id': 'card', 'name': 'Card' } ]"));
            ScanAndEnable("t_set");
            var raids = ModGameOptions.Find("t_set:raids");
            PlayerPrefs.DeleteKey("modsetting_t_set:raids");
            try
            {
                CollectionAssert.AreEqual(new[] { "t_set:card" }, ModGameOptions.Cards().Select(d => d.Key).ToArray(), "no card for a setting");
                CollectionAssert.AreEqual(new[] { "t_set:raids" }, ModGameOptions.GameplaySettings().Select(d => d.Key).ToArray());
                Assert.IsTrue(ModGameOptions.IsOn("t_set:raids"), "its default, whatever the game holds");
                ModGameOptions.SetSetting(raids, false);
                Assert.IsFalse(ModGameOptions.IsOn("t_set:raids"), "the player's choice");
                ModGameOptions.ApplyChoices();
                Assert.IsFalse(Session.ModGameOptions.Contains("t_set:raids"), "a setting isn't the new game's");
                ModGameOptions.ResetSettings();
                Assert.IsTrue(ModGameOptions.IsOn("t_set:raids"), "Default settings: back to its default");
            }
            finally { PlayerPrefs.DeleteKey("modsetting_t_set:raids"); PlayerPrefs.DeleteKey("newgame_modopt_t_set:card"); }
        }

        [Test]
        public void OptionsAreFoundByKeyOrUniqueId()
        {
            OptionsOn();
            Assert.AreEqual("t_opts:hard", ModGameOptions.Find("t_opts:hard").Key);
            Assert.AreEqual("t_opts2:hard", ModGameOptions.Find(" T_OPTS2:HARD ").Key);
            Assert.AreEqual("t_opts:extra", ModGameOptions.Find("extra").Key, "the id alone when only one mod has it");
            Assert.IsNull(ModGameOptions.Find("hard"), "two mods have it");
            Assert.IsNull(ModGameOptions.Find("nothing"));
            Assert.IsNull(ModGameOptions.Find(""));
        }

        [Test]
        public void AnOptionIsOnWhenTheGameHasIt()
        {
            OptionsOn();
            Assert.IsFalse(ModGameOptions.IsOn("t_opts:extra"));
            Session.ModGameOptions.Add("t_opts:extra");
            Assert.IsTrue(ModGameOptions.IsOn("t_opts:extra"));
            Assert.IsTrue(ModGameOptions.IsOn("extra"));
            Assert.IsFalse(ModGameOptions.IsOn("nothing"));
        }

        [Test]
        public void ASessionPlaysEveryOptionAtItsDefault()
        {
            OptionsOn();
            var old = NetSession.SessionGameProvider;
            try
            {
                NetSession.SessionGameProvider = () => true;
                Assert.IsTrue(ModGameOptions.IsOn("t_opts:extra"));
                Session.ModGameOptions.Add("t_opts:hard");
                Assert.IsFalse(ModGameOptions.IsOn("t_opts:hard"));
            }
            finally { NetSession.SessionGameProvider = old; }
        }

        [Test]
        public void ANewGameTakesTheDefaultsOrTheRememberedChoice()
        {
            OptionsOn();
            ModGameOptions.ApplyChoices();
            CollectionAssert.AreEquivalent(new[] { "t_opts:extra" }, Session.ModGameOptions);
            var hard = ModGameOptions.Find("t_opts:hard");
            var extra = ModGameOptions.Find("t_opts:extra");
            try
            {
                ModGameOptions.SetChosen(hard, true);
                ModGameOptions.SetChosen(extra, false);
                Assert.IsTrue(ModGameOptions.Chosen(hard));
                Assert.IsFalse(ModGameOptions.Chosen(extra));
                ModGameOptions.ApplyChoices();
                CollectionAssert.AreEquivalent(new[] { "t_opts:hard" }, Session.ModGameOptions);
            }
            finally
            {
                PlayerPrefs.DeleteKey("newgame_modopt_t_opts:hard");
                PlayerPrefs.DeleteKey("newgame_modopt_t_opts:extra");
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void TheGameOptionsAreSaved()
        {
            OptionsOn();
            Session.ModGameOptions = new HashSet<string> { "t_opts:hard" };
            SaveGame.Save(1);
            Session.ResetNewGame();
            SaveGame.Load(1);
            Assert.IsTrue(ModGameOptions.IsOn("t_opts:hard"));
        }

        [Test]
        public void ABrokenOptionsFileWarns()
        {
            WriteMod("o", "t_badopt", "1.0", "", ("gameoptions.json", "[ { 'id': 'a' }, { 'id': 'a' } ]"));
            ScanAndEnable("t_badopt");
            var all = ModGameOptions.All();
            Assert.AreEqual(1, all.Count, "the entries before the error stay");
            Assert.IsTrue(Installed("t_badopt").Warnings.Any(w => w.Contains("used twice")));
        }

        [Test]
        public void AnOptionNeedsAValidId()
        {
            WriteMod("o", "t_badid", "1.0", "", ("gameoptions.json", "[ { 'name': 'Nameless' } ]"));
            ScanAndEnable("t_badid");
            Assert.AreEqual(0, ModGameOptions.All().Count);
            Assert.IsTrue(Installed("t_badid").Warnings.Any(w => w.Contains("\"id\" is missing")));
        }
    }
}
