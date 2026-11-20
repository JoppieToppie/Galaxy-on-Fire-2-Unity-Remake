// ScavengerExampleTests.cs
// Feature: Modding/Examples/scavenger: a whole game mode as a mod (after AntonTolochko's Scavenger mode, #285 / #313), made
// of campaign.json (the start, "economy", "medals"), medals.json and one Lua script that patches the game's methods
// (game.patch; docs/notes/mods.md "Patches"). Expected, from the mode's rules (the contributor's scavenger-mode.md):
// a Betty at Dis with the Luna EMP Mk I (16), AB-1 Retractor (68), Telta Ecoscan (82) and 10 EMP GL I (41), no credits,
// the Default economy, the finished world, the Kaamo Club purchasable; only the Chromo Plasma (210, the game's recipe)
// and the Fireworks (232: 42 rows, 28 weapons / turrets / equipment, the Chromo Plasma, the mutagen, 12 cores x5) can be
// built, neither autocompleted; fresh shop stock has no primary weapon, turret or equipment above tech level 5, no
// Fireworks, ore or core (Bak S'ondorr, 126, still stocks the mutagen); NPC cargo is drawn from the original items 0-232
// without documents (115), the secure container / cabin (116 / 117), Alien Remains (131), ores and cores, the plasmas
// (201-204), the mutagen (209), the Chromo Plasma and the Fireworks; no blueprint seller or Kaamo unique-item dealer in
// the bars; only the mod's Champion medal, earned by building the Fireworks once. An ordinary game with the mod on is
// the game as it was.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.Patching;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ScavengerExampleTests : SavesModsTest
    {
        const string Id = "scavenger";
        static readonly HashSet<int> NotLoot = new HashSet<int> { 115, 116, 117, 131, 201, 202, 203, 204, 209, 210, 232 };

        [SetUp]
        public void OwnApprovals()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PatchRuntime.RemoveAll();
        }

        [TearDown]
        public void StopScripts()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = null;
            PatchRuntime.RemoveAll();
        }

        static string Errors() => string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text));

        ModCampaigns.Def ModOn()
        {
            CopyExample(Id);
            ScanAndEnable(Id);
            var mod = Installed(Id);
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            Assert.IsEmpty(mod.Warnings, AllMessages(mod));
            ModScripts.SetApproved(mod, true);
            ModScripts.Sync(true);
            Assert.AreEqual("", Errors());
            return ModCampaigns.All().Single(c => c.Id == Id);
        }

        /// <summary>A new Scavenger game, as the main menu starts it (MainMenu.BeginGame).</summary>
        Database StartGame()
        {
            var c = ModOn();
            Session.ResetNewGame();
            Session.Economy = c.economy.Value;
            Session.ModCampaign = c.Id;
            var db = Database.Load();
            Assert.AreEqual("Station", ModCampaigns.Start(db, c, out string error), error);
            Assert.AreEqual("", Errors());
            return Database.Load();
        }

        static bool Sold(Database db, int item)
        {
            var it = db.Item(item);
            if (item == 232 || it.categoryId == 23 || it.categoryId == 24) return false;
            return it.TypeId == 1 || it.TypeId == 4 || it.techLevel <= 5;
        }

        [Test]
        public void TheStart()
        {
            var c = ModOn();
            Assert.AreEqual(Economy.Default, c.economy);
            CollectionAssert.AreEqual(new[] { "scavenger:champion" }, c.medals);
            StartGame();
            Assert.AreEqual(70, Session.StationIndex, "Dis");
            Assert.AreEqual(0, Session.ShipIndex, "a Betty");
            Assert.AreEqual(0, Session.Credits);
            Assert.AreEqual("16x1 68x1 82x1 41x10", Describe(Session.Equipment));
            Assert.IsTrue(Session.FreePlay && Session.CompletedWorld, "the finished world, no story");
            Assert.AreEqual(2, Session.KaamoState, "the Kaamo Club can be bought");
            Assert.IsTrue(Blueprints.IsUnlocked(210) && Blueprints.IsUnlocked(232));
            Assert.AreEqual(2, Session.UnlockedBlueprints.Count);
        }

        [Test]
        public void OnlyTheChromoPlasmaAndTheFireworksCanBeBuilt()
        {
            var db = StartGame();
            CollectionAssert.AreEquivalent(new[] { 210, 232 }, Blueprints.Products(db).Select(i => i.index));
            var recipe = db.Item(232).blueprint;
            Assert.AreEqual(42, recipe.Count);
            Assert.AreEqual(228, recipe[0].item);
            Assert.AreEqual(1, recipe[0].amount);
            Assert.AreEqual(db.Item(228).name, recipe[0].name);
            Assert.AreEqual(218, recipe[41].item);
            Assert.AreEqual(5, recipe[41].amount);
            Assert.AreEqual(28 + 2 + 12 * 5, recipe.Sum(p => p.amount));
            Session.ModCampaign = "";
            int chromo = Database.Load().Item(210).blueprint.Count;
            Session.ModCampaign = Id;
            Assert.AreEqual(chromo, db.Item(210).blueprint.Count, "the game's own recipe");
            Assert.IsTrue(ModBlueprints.AutocompleteOverride(232, out bool allowed, out _));
            Assert.IsFalse(allowed, "no autocomplete");
            Assert.IsTrue(ModBlueprints.AutocompleteOverride(210, out allowed, out _));
            Assert.IsFalse(allowed);
            Assert.IsFalse(ModBlueprints.AutocompleteOverride(85, out _, out _), "nothing said about the others");
        }

        [Test]
        public void ShopsSellGearOnlyUpToTechLevel5()
        {
            var db = StartGame();
            int stacks = 0;
            bool lateSecondary = false;
            foreach (var st in db.Stations.Where(s => s.index < 100 && s.techLevel >= 7).Take(12))
                for (int i = 0; i < 4; i++)
                    foreach (var s in Shop.GenerateItems(db, st.index))
                    {
                        Assert.IsTrue(Sold(db, s.item), $"{db.Item(s.item).name} (tech {db.Item(s.item).techLevel}) at station {st.index}");
                        lateSecondary |= db.Item(s.item).TypeId == 1 && db.Item(s.item).techLevel > 5;
                        stacks++;
                    }
            Assert.Greater(stacks, 100);
            Assert.IsTrue(lateSecondary, "secondary weapons keep the stations' own tech levels");
            Assert.IsTrue(Shop.GenerateItems(db, 126).Exists(s => s.item == 209), "Bak S'ondorr has the mutagen");
            Assert.AreEqual("", Errors());
        }

        [Test]
        public void CargoIsDrawnFromThePool()
        {
            var db = StartGame();
            var seen = new HashSet<int>();
            int empty = 0;
            for (int i = 0; i < 400; i++)
            {
                var hold = NpcTables.RollLoot(db, i % 2 == 0);
                if (hold.Count == 0) empty++;
                foreach (var s in hold)
                {
                    var it = db.Item(s.item);
                    Assert.IsTrue(s.item <= 232 && !NotLoot.Contains(s.item) && it.categoryId != 23 && it.categoryId != 24, it.name);
                    Assert.Greater(s.amount, 0);
                    if (i % 2 == 0) Assert.GreaterOrEqual(s.amount, 2, "a freighter's stack");
                    seen.Add(s.item);
                }
            }
            Assert.Greater(empty, 0, "the game's own chance of an empty hold");
            Assert.Greater(seen.Count, 100, "any item, whatever its rarity");
            Assert.IsTrue(seen.Any(i => db.Item(i).techLevel >= 8 && db.Item(i).TypeId != 4), "the best gear too");
            for (int i = 0; i < 20; i++)
                foreach (var s in GoF2Remake.World.CapitalShips.RollLoot(db))
                    Assert.IsFalse(NotLoot.Contains(s.item), db.Item(s.item).name);
            Assert.AreEqual("", Errors());
        }

        [Test]
        public void NoBlueprintOrUniqueItemSellers()
        {
            var db = StartGame();
            int agents = 0;
            foreach (int station in new[] { 105, 106, 107, 10, 54, 70, 22 })
                for (int i = 0; i < 6; i++)
                    foreach (var a in AgentGenerator.CreateAgents(db, station))
                    {
                        agents++;
                        Assert.AreNotEqual(AgentOffer.SellBlueprint, a.offer, $"station {station}");
                        Assert.AreNotEqual(AgentOffer.KaamoSpecial, a.offer, $"station {station}");
                        if (a.offer == AgentOffer.SellItem) Assert.IsTrue(Sold(db, a.sellItem), db.Item(a.sellItem).name);
                    }
            Assert.Greater(agents, 50);
            Assert.AreEqual("", Errors());
        }

        [Test]
        public void TheOnlyMedalIsChampionForTheFireworks()
        {
            var db = StartGame();
            var champion = ModMedals.Find("scavenger:champion");
            Assert.IsNotNull(champion);
            Assert.IsTrue(ModMedals.Shown(champion));
            Assert.IsFalse(ModCampaigns.MedalAllowed(0));
            Assert.IsFalse(ModCampaigns.MedalAllowed(35), "not the game's own Champion");
            Assert.IsEmpty(Achievements.Check(db), "nothing built yet");
            Session.Blueprints.Add(new BlueprintState { item = 232, timesProduced = 1 });
            CollectionAssert.AreEqual(new[] { ModMedals.IndexOf(champion) }, Achievements.Check(db));
            Assert.IsNotNull(ModMedals.Earned(champion));
            Assert.AreEqual("", Errors());
        }

        [Test]
        public void AnOrdinaryGameIsTheGameAsItWas()
        {
            ModOn();
            Session.ResetNewGame();
            Session.CampaignMission = 100;
            var db = Database.Load();
            Assert.Greater(Blueprints.Products(db).Count, 10, "the game's blueprints");
            Assert.AreNotEqual(42, db.Item(232).blueprint.Count);
            Assert.IsFalse(ModBlueprints.AutocompleteOverride(232, out _, out _));
            Assert.IsFalse(ModMedals.Shown(ModMedals.Find("scavenger:champion")), "the mode's medal isn't in other games");
            Assert.IsTrue(ModCampaigns.MedalAllowed(0));
            bool gear = false;
            foreach (var st in db.Stations.Where(s => s.index < 100 && s.techLevel >= 8).Take(8))
                for (int i = 0; i < 6 && !gear; i++)
                    gear = Shop.GenerateItems(db, st.index).Exists(s => db.Item(s.item).TypeId != 1 && db.Item(s.item).TypeId != 4 && db.Item(s.item).techLevel > 5);
            Assert.IsTrue(gear, "shops sell gear above tech level 5");
            Assert.AreEqual("", Errors());
        }
    }
}
