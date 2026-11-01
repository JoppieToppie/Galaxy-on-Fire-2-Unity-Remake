// ModDlcsTests.cs
// Feature: docs/notes/mods.md "Mod DLCs" (ModDlcs.cs): dlc.json { name, description, image, showTitle, quest (required),
// index, intro, introSpeaker, startStation, startShip, credits, equipment, cargo }; the DLCs' play order (the player's
// order, PlayerPrefs "dlc_order", then "index", then load order; Move swaps a DLC with its neighbour and saves the order);
// in a game whose story is finished (step 162, not free play, not a mod campaign) the first DLC not played yet comes next,
// one at a time; a DLC's card starts a game at step 162 docked at Maissa (or its start) with its quest pending and the
// DLCs before it played.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModDlcsTests : SavesModsTest
    {
        string savedOrder;
        bool hadOrder;

        [SetUp]
        public void SaveOrder()
        {
            hadOrder = PlayerPrefs.HasKey(ModDlcs.OrderKey);
            savedOrder = PlayerPrefs.GetString(ModDlcs.OrderKey, "");
            PlayerPrefs.DeleteKey(ModDlcs.OrderKey);
        }

        [TearDown]
        public void RestoreOrder()
        {
            if (hadOrder) PlayerPrefs.SetString(ModDlcs.OrderKey, savedOrder);
            else PlayerPrefs.DeleteKey(ModDlcs.OrderKey);
            PlayerPrefs.Save();
        }

        static string Dlc(string quest, string extra = "") => "{ 'name': 'DLC " + quest + "', 'quest': '" + quest + "'" + extra + " }";

        void DlcsOn(params (string id, string json)[] dlcs)
        {
            foreach (var (id, json) in dlcs) WriteMod("d_" + id, id, "1.0", "", ("dlc.json", json));
            ScanAndEnable(dlcs.Select(d => d.id).ToArray());
        }

        static string Ids() => string.Join(" ", ModDlcs.All().Select(d => d.Id));

        void FinishStory()
        {
            Session.ResetNewGame();
            Session.FreePlay = false;
            Session.ModCampaign = "";
            Session.CampaignMission = Story.LastIndex;
            Session.StoryMission = new StoryMission();
        }

        [Test]
        public void TheDlcIsRead()
        {
            DlcsOn(("t_dlc", @"{ 'name': 'Kepler Incident', 'description': 'After the supernova.', 'image': 'dlc.png', 'showTitle': false,
                'quest': 'kepler_story', 'index': 2, 'intro': 'Keith, a new signal.', 'introSpeaker': 6, 'startShip': 5, 'credits': 7000,
                'equipment': [2, { 'item': 36, 'amount': 4 }], 'oops': 1 }"));
            var d = ModDlcs.Of("t_dlc");
            Assert.IsNotNull(d);
            Assert.AreEqual("Kepler Incident", d.Name);
            Assert.AreEqual("After the supernova.", d.Description);
            Assert.IsFalse(d.showTitle);
            Assert.AreEqual("kepler_story", d.quest);
            Assert.AreEqual(2, d.index);
            Assert.AreEqual("Keith, a new signal.", d.Intro);
            Assert.AreEqual(6, d.introSpeaker);
            Assert.AreEqual(7000, d.credits);
            CollectionAssert.AreEqual(new[] { ("2", 1), ("36", 4) }, d.equipment);
            Assert.IsNull(d.cargo, "none given: the finished game's");
            Assert.IsTrue(Installed("t_dlc").Warnings.Any(w => w.Contains("unknown field \"oops\"")), AllMessages(Installed("t_dlc")));
        }

        [Test]
        public void ADlcNeedsAQuest()
        {
            DlcsOn(("t_noquest", "{ 'name': 'Nothing' }"));
            Assert.IsNull(ModDlcs.Of("t_noquest"));
            Assert.IsTrue(Installed("t_noquest").Warnings.Any(w => w.Contains("\"quest\" is missing")), AllMessages(Installed("t_noquest")));
        }

        [Test]
        public void TheOrderIsTheIndexThenTheLoadOrderThenThePlayers()
        {
            DlcsOn(("t_a", Dlc("qa")), ("t_b", Dlc("qb", ", 'index': 2")), ("t_c", Dlc("qc", ", 'index': 1")));
            Assert.AreEqual("t_c t_b t_a", Ids(), "by index, those without one after them");
            Assert.AreEqual(1, ModDlcs.Place("t_c"));
            Assert.AreEqual(3, ModDlcs.Place("t_a"));
            ModDlcs.Move("t_a", -1);
            Assert.AreEqual("t_c t_a t_b", Ids(), "the player's order wins");
            ModDlcs.Move("t_a", -1);
            ModDlcs.Move("t_a", -1);   // already first: nothing
            Assert.AreEqual("t_a t_c t_b", Ids());
            Assert.AreEqual("t_a\nt_c\nt_b", PlayerPrefs.GetString(ModDlcs.OrderKey));
        }

        [Test]
        public void APlayersOrderKeepsNewDlcsAfterIt()
        {
            // A DLC turned on after the player ordered the others: after the ordered ones, by its index.
            DlcsOn(("t_x", Dlc("qx", ", 'index': 1")), ("t_y", Dlc("qy", ", 'index': 2")), ("t_z", Dlc("qz", ", 'index': 3")));
            var ordered = ModDlcs.Order(ModDlcs.All().ToList(), new List<string> { "t_z", "t_gone" });
            Assert.AreEqual("t_z t_x t_y", string.Join(" ", ordered.Select(d => d.Id)), "the ordered one first, the others by index");
        }

        [Test]
        public void TheNextDlcComesWhenTheStoryIsOverOneAtATime()
        {
            DlcsOn(("t_a", Dlc("qa", ", 'index': 1")), ("t_b", Dlc("qb", ", 'index': 2")));
            Session.ResetNewGame();
            Session.CampaignMission = 120;
            Assert.IsNull(ModDlcs.Next(), "the story isn't over");
            FinishStory();
            Assert.AreEqual("t_a", ModDlcs.Next()?.Id);
            Session.GraphQuests.Add(new GraphQuestState { name = "qa", kind = GraphQuestState.KindQuest });
            Assert.IsNull(ModDlcs.Next(), "one runs");
            Session.GraphQuests.Clear();
            Session.GraphQuestsDone.Add("qa");
            Assert.AreEqual("t_b", ModDlcs.Next()?.Id, "then the next");
            Session.GraphQuestsDone.Add("qb");
            Assert.IsNull(ModDlcs.Next(), "all played");
            Session.GraphQuestsDone.Clear();
            Session.FreePlay = true;
            Assert.IsNull(ModDlcs.Next(), "not in free play");
            Session.FreePlay = false;
            Session.ModCampaign = "someone";
            Assert.IsNull(ModDlcs.Next(), "not in a mod campaign");
        }

        [Test]
        public void TheCardStartsAtTheEndWithTheEarlierDlcsPlayed()
        {
            DlcsOn(("t_a", Dlc("qa", ", 'index': 1")), ("t_b", Dlc("qb", ", 'index': 2, 'credits': 1234")));
            var db = Database.Load();
            Session.ResetNewGame();
            Assert.AreEqual("Station", ModDlcs.Start(db, ModDlcs.Of("t_b"), out string error), error);
            Assert.AreEqual(Story.LastIndex, Story.Index, "the whole story played");
            Assert.IsFalse(Session.FreePlay);
            Assert.AreEqual(ModDlcs.EndStation, Session.StationIndex, "docked at Maissa, where it ends");
            Assert.AreEqual(1234, Session.Credits);
            Assert.IsTrue(Session.GraphQuestsDone.Contains("qa"), "the DLC before it counts as played");
            Assert.IsTrue(Session.GraphQuests.Exists(q => q.name == "qb"), "its quest pending");
            Assert.IsTrue(GraphQuestState.RestorePending);
            GraphQuestState.RestorePending = false;
            Assert.IsNull(ModDlcs.Next(), "its own runs");
        }

        [Test]
        public void AWrongStartIsRefused()
        {
            DlcsOn(("t_bad", Dlc("qbad", ", 'startStation': 'nowhere:nothing'")));
            Session.ResetNewGame();
            Assert.IsNull(ModDlcs.Start(Database.Load(), ModDlcs.Of("t_bad"), out string error));
            StringAssert.Contains("is no station", error);
        }
    }
}
