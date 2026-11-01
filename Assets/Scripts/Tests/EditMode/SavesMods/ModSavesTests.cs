// ModSavesTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Saves (ModSaves.cs, save version 12): a save records the mods that were on
// (id, name, version) and every modded entry's key, number and price ((min + max) / 2 for an item); loading warns about
// mods that aren't on (MissingMods); Fix moves a key to its number now (a save from another device), and removes what a
// mod that is off brought: equipment, cargo, Kaamo storage and the parked ship refunded at the saved price x amount, the
// flown mod ship replaced by a Phantom (ship 10, its equipment into the hold, refunded), a station of the mod docked at Var
// Hastra (78) instead; a key this device doesn't know keeps the save's number when it is free (ModRegistry.Adopt).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModSavesTests : SavesModsTest
    {
        const string Mod = "t_saves";

        void ModOn()
        {
            string dir = WriteMod("saves", Mod, "2.5", "", ("items.json",
                    "[ { 'id': 'gun', 'base': 2, 'name': 'Gun', 'price': [1000, 3000] }, { 'id': 'ore', 'base': 155, 'name': 'Ore', 'price': 50 } ]"),
                ("ships.json", "[ { 'id': 'yacht', 'model': 'y.glb', 'name': 'Yacht', 'price': 80000, 'armor': 100, 'cargo': 30, 'slots': { 'primary': 2, 'equipment': 3 } } ]"),
                ("systems.json", "[ { 'id': 'home', 'name': 'Home', 'race': 0, 'position': [5, 5, 5] } ]"),
                ("stations.json", "[ { 'id': 'port', 'system': 't_saves:home', 'name': 'Port' } ]"));
            WriteFile(dir, "y.glb", Dummy);
            ScanAndEnable(Mod);
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
        }

        [Test]
        public void RecordWritesTheModsAndTheirKeys()
        {
            ModOn();
            var db = Database.Load();
            var s = new SaveData();
            ModSaves.Record(s, db);
            Assert.AreEqual(1, s.mods.Count);
            Assert.AreEqual(Mod, s.mods[0].id);
            Assert.AreEqual("Test " + Mod, s.mods[0].name);
            Assert.AreEqual("2.5", s.mods[0].version);
            var gun = s.modKeys.Single(k => k.key == "t_saves:gun");
            Assert.AreEqual("item", gun.kind);
            Assert.AreEqual(233, gun.index);
            Assert.AreEqual(2000, gun.price, "(min + max) / 2");
            Assert.AreEqual(50, s.modKeys.Single(k => k.key == "t_saves:ore").price);
            var yacht = s.modKeys.Single(k => k.key == "t_saves:yacht");
            Assert.AreEqual("ship", yacht.kind);
            Assert.AreEqual(64, yacht.index);
            Assert.AreEqual(80000, yacht.price);
            Assert.AreEqual(34, s.modKeys.Single(k => k.kind == "system").index);
            Assert.AreEqual(135, s.modKeys.Single(k => k.kind == "station").index);
        }

        [Test]
        public void ASaveRecordsItsMods()
        {
            ModOn();
            Assert.IsTrue(SaveGame.Save(1));
            var p = SaveGame.Preview(1);
            Assert.AreEqual(Mod, p.mods.Single().id);
            Assert.AreEqual(5, p.modKeys.Count(k => !k.key.StartsWith(ModManager.BaseId + ":")));
        }

        [Test]
        public void MissingModsNamesTheModsThatArentOn()
        {
            ModOn();
            var s = new SaveData
            {
                mods = new List<SaveMod>
                {
                    new SaveMod { id = Mod, name = "Saves", version = "2.5" },
                    new SaveMod { id = "gone_mod", name = "Gone Mod", version = "1" },
                    new SaveMod { id = "nameless", name = "", version = "1" },
                },
            };
            CollectionAssert.AreEqual(new[] { "Gone Mod", "nameless" }, ModSaves.MissingMods(s));
            Assert.AreEqual(0, ModSaves.MissingMods(new SaveData()).Count);
            Assert.AreEqual(0, ModSaves.MissingMods(null).Count);
        }

        /// <summary>A save made with the mod on (its keys recorded), holding its items, ship and station.</summary>
        SaveData ModdedSave()
        {
            var db = Database.Load();
            var s = new SaveData
            {
                station = 135, previousStation = 135, ship = 64, credits = 100,
                equipment = Stacks((233, 1), (2, 1)),
                cargo = Stacks((234, 10), (155, 5)),
                kaamoItems = Stacks((233, 2)),
                hasParkedShip = true,
                parkedShip = new ParkedShip { ship = 64, equipment = Stacks((233, 1)), cargo = Stacks((234, 1)) },
                kaamoShips = new List<StoredShip> { new StoredShip(64, 0, null), new StoredShip(5, 0, null) },
                seenItems = new List<int> { 233, 12 },
                visitedStations = new List<int> { 78, 135 },
                selectedSecondary = 233,
                shipMods = new List<int> { 1 },
                recentStations = new List<StationStock>
                {
                    new StationStock { station = 135, items = Stacks((233, 4)) },
                    new StationStock { station = 70, items = Stacks((234, 4), (155, 3)), ships = new List<int> { 64, 5 },
                        agents = new List<Agent> { new Agent { name = "Seller", sellItem = 233 }, new Agent { name = "Dealer", sellShip = 64 }, new Agent { name = "Other", sellItem = 12 } } },
                },
                blueprints = new List<BlueprintState> { new BlueprintState { item = 233 }, new BlueprintState { item = 178 } },
                pendingProducts = new List<PendingProduct> { new PendingProduct { item = 178, quantity = 1, station = 135 } },
                freelanceMission = new FreelanceMission { type = MissionType.Purchase, good = 233, clientStation = 70 },
            };
            ModSaves.Record(s, db);
            return s;
        }

        [Test]
        public void FixRemovesAndRefundsWhatAModThatIsOffBrought()
        {
            ModOn();
            var s = ModdedSave();
            ModManager.SetEnabled(Mod, false);
            int refund = ModSaves.Fix(s);
            // items: equipment 1 x 2000, cargo 10 x 50, Kaamo 2 x 2000, parked 1 x 2000 + 1 x 50; ships: the flown yacht and
            // the stored one 80000 each (the parked ship's hull too: 80000).
            int items = 2000 + 10 * 50 + 2 * 2000 + 2000 + 50;
            int ships = 80000 + 80000 + 80000;
            Assert.AreEqual(items + ships, refund);
            Assert.AreEqual(100 + items + ships, s.credits);
            Assert.AreEqual(ModSaves.FallbackShip, s.ship);
            Assert.AreEqual(10, ModSaves.FallbackShip, "the Phantom");
            Assert.AreEqual(0, s.equipment.Count, "a Phantom without equipment...");
            Assert.AreEqual("155x5 2x1", Describe(s.cargo), "...the equipment that is still in the game is in the hold");
            Assert.AreEqual(0, s.shipMods.Count);
            Assert.AreEqual(0, s.kaamoItems.Count);
            CollectionAssert.AreEqual(new[] { 5 }, s.kaamoShips.Select(k => k.ship).ToArray());
            Assert.AreEqual(ModSaves.FallbackShip, s.parkedShip.ship);
            Assert.AreEqual(0, s.parkedShip.equipment.Count);
            Assert.AreEqual(0, s.parkedShip.cargo.Count);
            CollectionAssert.AreEqual(new[] { 12 }, s.seenItems);
            Assert.AreEqual(-1, s.selectedSecondary);
            Assert.AreEqual(ModWorld.RefugeStation, s.station);
            Assert.AreEqual(78, ModWorld.RefugeStation);
            Assert.AreEqual(-1, s.previousStation);
            CollectionAssert.AreEqual(new[] { 78 }, s.visitedStations);
            Assert.AreEqual(1, s.recentStations.Count, "the mod's station's shop memory goes");
            var rs = s.recentStations[0];
            Assert.AreEqual("155x3", Describe(rs.items));
            CollectionAssert.AreEqual(new[] { 5 }, rs.ships);
            CollectionAssert.AreEqual(new[] { "Other" }, rs.agents.Select(a => a.name).ToArray());
            CollectionAssert.AreEqual(new[] { 178 }, s.blueprints.Select(b => b.item).ToArray());
            Assert.AreEqual(78, s.pendingProducts[0].station, "production waiting at a station that is gone waits at Var Hastra");
            Assert.AreEqual(MissionType.Empty, s.freelanceMission.type, "a Purchase of goods that are gone is dropped");
        }

        [Test]
        public void FixKeepsEverythingWhileTheModIsOn()
        {
            ModOn();
            var s = ModdedSave();
            Assert.AreEqual(0, ModSaves.Fix(s));
            Assert.AreEqual(100, s.credits);
            Assert.AreEqual(64, s.ship);
            Assert.AreEqual(135, s.station);
            Assert.AreEqual("233x1 2x1", Describe(s.equipment));
            Assert.AreEqual("234x10 155x5", Describe(s.cargo));
            Assert.AreEqual(233, s.selectedSecondary);
            Assert.AreEqual(MissionType.Purchase, s.freelanceMission.type);
        }

        [Test]
        public void FixMovesKeysToTheirNumbersHere()
        {
            // A save from another device, where the gun was 300 and the ore 301 (here: 233 / 234).
            ModOn();
            Database.Load();
            var s = new SaveData
            {
                station = 78, ship = 10, credits = 0,
                cargo = Stacks((300, 3), (301, 7)),
                equipment = Stacks((300, 1)),
                seenItems = new List<int> { 300 },
                selectedSecondary = 300,
                blueprints = new List<BlueprintState> { new BlueprintState { item = 301 } },
                lowestPrices = new List<SaveData.KnownPrice> { new SaveData.KnownPrice { item = 301, price = 40, system = 3 } },
                modKeys = new List<SaveModKey>
                {
                    new SaveModKey { kind = "item", key = "t_saves:gun", index = 300, price = 2000 },
                    new SaveModKey { kind = "item", key = "t_saves:ore", index = 301, price = 50 },
                },
            };
            Assert.AreEqual(0, ModSaves.Fix(s));
            Assert.AreEqual("233x3 234x7", Describe(s.cargo));
            Assert.AreEqual("233x1", Describe(s.equipment));
            CollectionAssert.AreEqual(new[] { 233 }, s.seenItems);
            Assert.AreEqual(233, s.selectedSecondary);
            Assert.AreEqual(234, s.blueprints[0].item);
            Assert.AreEqual(234, s.lowestPrices[0].item);
            Assert.AreEqual(3, s.lowestPrices[0].system);
        }

        [Test]
        public void FixMovesAShipToItsNumberHere()
        {
            ModOn();
            Database.Load();
            var s = new SaveData
            {
                station = 78, ship = 70, credits = 0,
                modKeys = new List<SaveModKey> { new SaveModKey { kind = "ship", key = "t_saves:yacht", index = 70, price = 80000 } },
            };
            Assert.AreEqual(0, ModSaves.Fix(s));
            Assert.AreEqual(64, s.ship);
        }

        [Test]
        public void AnUnknownKeyKeepsTheSavesNumberWhenItIsFree()
        {
            // The mod is installed but this device never numbered it: the save's numbers are adopted (and its items, the mod
            // being off, refunded); turned on, the mod's items take those numbers.
            string dir = WriteMod("saves", Mod, "1.0", "", ("items.json", "[ { 'id': 'gun', 'base': 2, 'name': 'Gun' } ]"));
            ModManager.Scan();
            Database.Load();
            var s = new SaveData
            {
                station = 78, ship = 10, credits = 0, cargo = Stacks((240, 2)),
                modKeys = new List<SaveModKey> { new SaveModKey { kind = "item", key = "t_saves:gun", index = 240, price = 100 } },
            };
            Assert.AreEqual(200, ModSaves.Fix(s));
            Assert.AreEqual(0, s.cargo.Count);
            Assert.IsTrue(ModRegistry.TryGet(ModRegistry.Kind.Item, "t_saves:gun", out int n));
            Assert.AreEqual(240, n);
            ModManager.SetEnabled(Mod, true);
            var db = Database.Load();
            Assert.AreEqual("t_saves:gun", db.Item(240).modKey);
            Assert.AreEqual("Gun", db.Item(240).name);
            Assert.AreEqual(241, db.Items.Count, "the numbers below it are placeholders, the table stays 0..N-1");
            Assert.IsTrue(db.Item(235).modded);
        }

        [Test]
        public void ANumberNoModAccountsForIsRemovedWithoutARefund()
        {
            ModManager.Scan();
            Database.Load();
            var s = new SaveData { station = 78, ship = 10, credits = 5, cargo = Stacks((250, 4), (155, 1)) };
            Assert.AreEqual(0, ModSaves.Fix(s));
            Assert.AreEqual("155x1", Describe(s.cargo));
            Assert.AreEqual(5, s.credits);
        }

        [Test]
        public void LoadingASaveWithTheModOffRefundsIt()
        {
            ModOn();
            Session.Credits = 1000;
            Session.Cargo = Stacks((233, 2), (155, 1));
            Assert.IsTrue(SaveGame.Save(1));
            Assert.AreEqual(0, ModSaves.MissingMods(SaveGame.Preview(1)).Count, "nothing missing while it is on");
            ModManager.SetEnabled(Mod, false);
            Assert.AreEqual(new[] { "Test " + Mod }, ModSaves.MissingMods(SaveGame.Preview(1)).ToArray());
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual("155x1", Describe(Session.Cargo));
            Assert.AreEqual(1000 + 2 * 2000, Session.Credits);
        }

        [Test]
        public void LoadingWithTheModOnKeepsItsItems()
        {
            ModOn();
            Session.Cargo = Stacks((233, 2));
            Session.ShipIndex = 64;
            SaveGame.Save(1);
            Session.ResetNewGame();
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual("233x2", Describe(Session.Cargo));
            Assert.AreEqual(64, Session.ShipIndex);
        }
    }
}
