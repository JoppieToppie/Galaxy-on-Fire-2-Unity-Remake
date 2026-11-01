// DatabaseTests.cs
// Feature: CLAUDE.md "Data" (Database.Load, items' original attributes ItemData.Attr, the statList keys, text ids) and
// "Shop" > Economy (the Default Economy overlay economy_default.json over the Android tables), Modding.ItemStats (stat names
// <-> attribute numbers), Database.ShipAssembly's special hulls, BuildFlightStats.
// Expected: CLAUDE.md "Item attribute keys" (9 damage ... 39 / 40 fire-rate / damage factor), shop.md 8 (Item::init copies
// attributes 0-8: index, type, sort, tech, lowest / highest price system, occurrence, min / max price) and 2.4 (the
// attribute table), the data files by hand (Android: Energy Cells 1400-1540, signatures 500 000, Phantom 966 667, Night Owl
// 3 secondaries, Khador Drive 200 Pyresium; Default: 560-700, 7500, 1 450 000, 2 secondaries, 10 Pyresium; CLAUDE.md quotes
// the same pairs), and the tables' sizes (233 items, 64 ships, 34 systems, 135 stations).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class DatabaseTests : EconomyTest
    {
        [Test]
        public void TablesLoad()
        {
            Assert.AreEqual(233, Db.Items.Count(i => !i.modded));
            Assert.AreEqual(64, Db.Ships.Count);
            Assert.AreEqual(34, Db.Systems.Count);
            Assert.AreEqual(135, Db.Stations.Count);
            Assert.IsNotEmpty(Db.Assemblies);
            Assert.IsNotEmpty(Db.WeaponMounts);
            Assert.AreEqual(Economy.Android, Db.Economy);
        }

        [Test]
        public void LookupsByIndex()
        {
            for (int i = 0; i < Db.Items.Count; i++) Assert.AreEqual(i, Db.Item(i).index);
            for (int i = 0; i < Db.Ships.Count; i++) Assert.AreEqual(i, Db.Ship(i).index);
            Assert.IsNull(Db.Item(-1));
            Assert.IsNull(Db.Item(Db.Items.Count));
            Assert.IsNull(Db.Ship(64));
            Assert.AreEqual(10, Db.ShipByName("Phantom").index);
            Assert.AreEqual(122, Db.ItemByName("Energy Cells").index);
            CollectionAssert.AreEquivalent(new[] { 75, 76, 77, 78, 79 }, Db.StationsIn(15).Select(s => s.index).ToArray());
        }

        [Test]
        public void AttributesZeroToEightMirrorTheItemFields()
        {
            // Item::init copies attributes 0-8 (shop.md 8)
            foreach (var it in Db.Items)
            {
                Assert.AreEqual(it.index, it.Attr(0), "index of " + it.index);
                Assert.AreEqual(it.categoryId, it.Attr(2), "sort of " + it.index);
                Assert.AreEqual(it.techLevel, it.Attr(3), "tech of " + it.index);
                Assert.AreEqual(it.lowestPriceSystem, it.Attr(4), "lowest price system of " + it.index);
                Assert.AreEqual(it.highestPriceSystem, it.Attr(5), "highest price system of " + it.index);
                Assert.AreEqual(it.occurrence, it.Attr(6), "occurrence of " + it.index);
                Assert.AreEqual(it.minPrice, it.Attr(7), "min price of " + it.index);
                Assert.AreEqual(it.maxPrice, it.Attr(8), "max price of " + it.index);
            }
        }

        [Test]
        public void TypeIdIsAttributeOne()
        {
            var names = new[] { "primary", "secondary", "turret", "equipment", "commodity" };
            foreach (var it in Db.Items) Assert.AreEqual(names[it.TypeId], it.type, "item " + it.index);
            Assert.AreEqual(0, Db.Item(2).TypeId);
            Assert.AreEqual(1, Db.Item(36).TypeId);
            Assert.AreEqual(2, Db.Item(180).TypeId);
            Assert.AreEqual(3, Db.Item(54).TypeId);
            Assert.AreEqual(4, Db.Item(122).TypeId);
            // without attribute 1 the type string decides
            var bare = new ItemData { type = "turret" };
            Assert.AreEqual(2, bare.TypeId);
            Assert.AreEqual(4, new ItemData { type = "whatever" }.TypeId);
        }

        [Test]
        public void AttrReadsTheOriginalPairs()
        {
            // Nirai Charged Pulse (2): damage 6, reload 380 ms, range (lifetime) 2000, speed 20
            var it = Db.Item(2);
            Assert.AreEqual(6, it.Attr(9));
            Assert.AreEqual(380, it.Attr(11));
            Assert.AreEqual(2000, it.Attr(12));
            Assert.AreEqual(20, it.Attr(13));
            Assert.IsTrue(it.HasAttr(9));
            Assert.IsFalse(it.HasAttr(18));
            Assert.AreEqual(0, it.Attr(18));
            Assert.AreEqual(-7, it.Attr(18, -7), "the fallback for a missing attribute");
            Assert.AreEqual(220, Db.Item(54).Attr(18), "Fluxed Matter Shield capacity");
            Assert.AreEqual(250, Db.Item(59).Attr(20), "T'yol armor");
            Assert.AreEqual(100, Db.Item(67).Attr(22), "Rhoda Blackhole compression");
            Assert.AreEqual(1, Db.Item(95).Attr(60), "the U'tool is Vossk-only");
        }

        [Test]
        public void StatListAgreesWithTheAttributes()
        {
            // items.json's statList is a readable copy of the attributes: every key that names an attribute holds its value
            foreach (var econ in new[] { Economy.Android, Economy.Default })
            {
                Session.Economy = econ;
                var db = Database.Load();
                foreach (var it in db.Items)
                    foreach (var s in it.statList)
                    {
                        int id = ItemStats.IdOf(s.key);
                        if (id < 0) continue;
                        Assert.AreEqual(it.Attr(id), s.value, $"{econ}: item {it.index} stat {s.key} = attribute {id}");
                        Assert.AreEqual(s.value, it.Stat(s.key));
                    }
            }
        }

        [Test]
        public void StatNamesAreTheAttributeNumbers()
        {
            // CLAUDE.md "Item attribute keys"
            Assert.AreEqual(9, ItemStats.IdOf("damage"));
            Assert.AreEqual(10, ItemStats.IdOf("empDamage"));
            Assert.AreEqual(11, ItemStats.IdOf("reloadMs"));
            Assert.AreEqual(11, ItemStats.IdOf("loadingTimeMs"));
            Assert.AreEqual(12, ItemStats.IdOf("range"));
            Assert.AreEqual(13, ItemStats.IdOf("projectileSpeed"));
            Assert.AreEqual(18, ItemStats.IdOf("shieldCapacity"));
            Assert.AreEqual(19, ItemStats.IdOf("shieldRegenMs"));
            Assert.AreEqual(20, ItemStats.IdOf("armor"));
            Assert.AreEqual(22, ItemStats.IdOf("cargoBonus"));
            Assert.AreEqual(25, ItemStats.IdOf("boostSpeed"));
            Assert.AreEqual(26, ItemStats.IdOf("boostRechargeMs"));
            Assert.AreEqual(27, ItemStats.IdOf("boostDurationMs"));
            Assert.AreEqual(28, ItemStats.IdOf("agility"));
            Assert.AreEqual(31, ItemStats.IdOf("radarShowsCargo"));
            Assert.AreEqual(39, ItemStats.IdOf("fireRateFactor"));
            Assert.AreEqual(40, ItemStats.IdOf("damageFactor"));
            // the remake's attributes for mods (CLAUDE.md "Mining" mining beam 100-103, "Combat equipment" phase cloak 104)
            Assert.AreEqual(100, ItemStats.IdOf("miningBeam"));
            Assert.AreEqual(101, ItemStats.IdOf("miningBeamRange"));
            Assert.AreEqual(102, ItemStats.IdOf("miningBeamLayerMs"));
            Assert.AreEqual(103, ItemStats.IdOf("miningBeamLook"));
            Assert.AreEqual(104, ItemStats.IdOf("phaseCloak"));
            // missile turrets and salvos (Modding/README.md "Stats")
            Assert.AreEqual(105, ItemStats.IdOf("missiles"));
            Assert.AreEqual(106, ItemStats.IdOf("salvoShots"));
            Assert.AreEqual(107, ItemStats.IdOf("salvoCount"));
            Assert.AreEqual(108, ItemStats.IdOf("salvoGapMs"));
            Assert.AreEqual(109, ItemStats.IdOf("turretRange"));
        }

        [Test]
        public void StatNamesIgnoreCaseAndRefuseUnknowns()
        {
            Assert.AreEqual(9, ItemStats.IdOf("DAMAGE"));
            Assert.AreEqual(-1, ItemStats.IdOf("warpFactor"));
            Assert.AreEqual(-1, ItemStats.IdOf(null));
            Assert.AreEqual(-1, ItemStats.IdOf(""));
        }

        [Test]
        public void StatKeysRoundTrip()
        {
            // every items.json statList key (bar the two guessed ones) maps to an attribute that maps back to it
            foreach (var it in Db.Items)
                foreach (var s in it.statList)
                {
                    int id = ItemStats.IdOf(s.key);
                    if (id < 0) { CollectionAssert.Contains(new[] { "raceSpecific_guess", "iconIndex_guess" }, s.key); continue; }
                    Assert.AreEqual(s.key, ItemStats.StatKeyOf(id), "attribute " + id);
                }
            Assert.IsNull(ItemStats.StatKeyOf(21));
            Assert.AreEqual("raceSpecific_guess", ItemStats.StatKeyOf(60));
            Assert.AreEqual("iconIndex_guess", ItemStats.StatKeyOf(61));
            Assert.AreEqual(-1, ItemStats.IdOf("raceSpecific_guess"), "the guessed ones aren't mod stat names");
        }

        [Test]
        public void ReadableNamesStopBeforeTheStatListAliases()
        {
            var names = ItemStats.Readable().Select(kv => kv.Key).ToList();
            CollectionAssert.Contains(names, "damage");
            CollectionAssert.Contains(names, "phaseCloak");
            CollectionAssert.DoesNotContain(names, "loadingTimeMs");
            CollectionAssert.DoesNotContain(names, "plasmaConsumption");
        }

        // ---- Android vs Default economy ------------------------------------------------------------------------------

        static Database Load(Economy e)
        {
            Session.Economy = e;
            return Database.Load();
        }

        [Test]
        public void DefaultEconomyOverlay()
        {
            var android = Load(Economy.Android);
            var pc = Load(Economy.Default);
            Assert.AreEqual(Economy.Android, android.Economy);
            Assert.AreEqual(Economy.Default, pc.Economy);
            // Energy Cells 1400-1540 / 560-700
            Assert.AreEqual((1400, 1540), (android.Item(122).minPrice, android.Item(122).maxPrice));
            Assert.AreEqual((560, 700), (pc.Item(122).minPrice, pc.Item(122).maxPrice));
            Assert.AreEqual(560, pc.Item(122).Attr(7), "the attributes follow");
            Assert.AreEqual(700, pc.Item(122).Attr(8));
            // signatures 500 000 / 7500
            for (int i = 189; i <= 192; i++)
            {
                Assert.AreEqual(500000, android.Item(i).maxPrice);
                Assert.AreEqual(7500, pc.Item(i).maxPrice);
            }
            // tractor beams ~x18: AB-1 154 500 / 8567
            Assert.AreEqual(154500, android.Item(68).maxPrice);
            Assert.AreEqual(8567, pc.Item(68).maxPrice);
            // a tech level change: D'iol 8 / 7
            Assert.AreEqual(8, android.Item(58).techLevel);
            Assert.AreEqual(7, pc.Item(58).techLevel);
        }

        [Test]
        public void DefaultEconomyRecipesAndShips()
        {
            var android = Load(Economy.Android);
            var pc = Load(Economy.Default);
            // Khador Drive: 200 / 10 Pyresium (158); the Default recipe has no Energy Cells
            Assert.AreEqual(200, android.Item(85).blueprint.Find(b => b.item == 158).amount);
            Assert.AreEqual(10, pc.Item(85).blueprint.Find(b => b.item == 158).amount);
            Assert.AreEqual(9, android.Item(85).blueprint.Count);
            Assert.AreEqual(8, pc.Item(85).blueprint.Count);
            // ships: the Phantom 966 667 / 1 450 000, the Night Owl's 3 / 2 secondary slots
            Assert.AreEqual(966667, android.Ship(10).price);
            Assert.AreEqual(1450000, pc.Ship(10).price);
            // the retail (iOS 1.1.19) ships.bin: Betty 16 200 in both, the Bloodstar 13 500 000 (KiritoJPK's table had 12 prices
            // at the in-game 0.99 race discount, 16 038 / 13 365 000, which Shop.ShipPrice then discounted again)
            Assert.AreEqual(16200, pc.Ship(0).price);
            Assert.AreEqual(13500000, pc.Ship(45).price);
            // the iOS build's exhaust anchors (weapons_hd_default.json): the VoidX has 9 mounts instead of the Android table's 11,
            // the H'Soc's side anchors sit at x -69 / 69 (Android -65 / 65; its first anchor is the centre one); the primary mounts are the same
            Assert.AreEqual(11, android.MountsOf(8, 0).Count + android.MountsOf(8, 1).Count + android.MountsOf(8, 2).Count + android.MountsOf(8, 3).Count);
            Assert.AreEqual(9, pc.MountsOf(8, 0).Count + pc.MountsOf(8, 1).Count + pc.MountsOf(8, 2).Count + pc.MountsOf(8, 3).Count);
            Assert.AreEqual(-65, android.MountsOf(9, 3)[1].position_engine[0]);
            Assert.AreEqual(-69, pc.MountsOf(9, 3)[1].position_engine[0]);
            Assert.AreEqual(android.MountsOf(8, 0).Count, pc.MountsOf(8, 0).Count);
            Assert.AreEqual(3, android.Ship(35).slots.secondary);
            Assert.AreEqual(2, pc.Ship(35).slots.secondary);
            Assert.AreEqual(pc.Ship(35).handling / 100f, pc.Ship(35).handlingMultiplier, 1e-6f);
        }

        [Test]
        public void ItemsWithoutAnOverlayRowStayTheSame()
        {
            // economy_default.json lists only what differs: Sh'gaal (14) isn't in it
            var android = Load(Economy.Android);
            var pc = Load(Economy.Default);
            foreach (int i in new[] { 14, 116, 217 })
            {
                Assert.AreEqual(android.Item(i).minPrice, pc.Item(i).minPrice, "item " + i);
                Assert.AreEqual(android.Item(i).maxPrice, pc.Item(i).maxPrice);
                CollectionAssert.AreEqual(android.Item(i).attrKeys, pc.Item(i).attrKeys);
            }
        }

        [Test]
        public void SharedDatabaseFollowsTheEconomy()
        {
            Session.Economy = Economy.Android;
            var a = Database.Shared;
            Assert.AreSame(a, Database.Shared, "kept while nothing changes");
            Assert.AreEqual(1540, a.Item(122).maxPrice);
            Session.Economy = Economy.Default;
            Assert.AreEqual(700, Database.Shared.Item(122).maxPrice);
            Session.Economy = Economy.Android;
            Assert.AreEqual(1540, Database.Shared.Item(122).maxPrice);
        }

        [Test]
        public void DefaultPricesFollowTheOverlay()
        {
            // the price rule on the Default tables: Loma charges the Default maximum x 1.05 (the iOS build's rule; float: 734, 7874)
            Session.Economy = Economy.Default;
            Assert.AreEqual(new[] { 734, 7874 }, Shop.PriceList(Database.Shared, 106, new[] { 122, 189 }));
            Session.Economy = Economy.Android;
            Assert.AreEqual(new[] { 1540, 500000 }, Shop.PriceList(Database.Shared, 106, new[] { 122, 189 }));
        }

        // ---- assemblies and flight stats --------------------------------------------------------------------------------

        [Test]
        public void SpecialHullAssemblies()
        {
            // Globals::getShipGroup: 13 the Vossk freighter, 14 the Terran battleship, 15 the freighter by race
            Assert.AreEqual("cargo_004_vossk", Db.ShipAssembly(13)?.name);
            Assert.AreEqual("battleship_terran", Db.ShipAssembly(14)?.name);
            Assert.AreEqual("cargo_002_nivelian", Db.ShipAssembly(15, "nivelian")?.name);
            Assert.AreEqual("cargo_001_midorian", Db.ShipAssembly(15, "midorian")?.name);
            Assert.AreEqual("cargo_004_vossk", Db.ShipAssembly(15, "vossk")?.name);
            Assert.AreEqual("cargo_003_terran", Db.ShipAssembly(15)?.name);
            Assert.IsNull(Db.ShipAssembly(50), "no model (a '?' icon)");
            Assert.IsNull(Db.ShipAssembly(53));
            StringAssert.StartsWith("ship_010_", Db.ShipAssembly(10).name);
            Assert.IsTrue(Db.ShipAssembly(41).name.StartsWith("v_ship_041_"), "the add-on ships' packs");
        }

        [Test]
        public void FlightStatsFromTheBoosterAndNozzle()
        {
            var ship = Db.Ship(10);
            var fs = Database.BuildFlightStats(ship, new[] { Db.Item(73), Db.Item(80) }, 2);
            Assert.AreEqual(ship.handling, fs.handling);
            Assert.AreEqual(2, fs.handlingUpgrades);
            // Synchrotron Boost (73): attributes 25 / 27 / 26 = 160 / 5600 / 12000; Pulsed Plasma Thrust (80): 28 = 130
            Assert.AreEqual(160, fs.boostSpeed);
            Assert.AreEqual(5600, fs.boostDurationMs);
            Assert.AreEqual(12000, fs.boostRechargeMs);
            Assert.AreEqual(130f, fs.agility);
            var none = Database.BuildFlightStats(ship, null);
            Assert.AreEqual(0, none.boostSpeed);
            Assert.AreEqual(0f, none.agility);
        }

        [Test]
        public void MountsOfAShip()
        {
            // weapons_hd.json: the Phantom's two primary mounts, one secondary
            Assert.AreEqual(2, Db.MountsOf(10, 0).Count);
            Assert.AreEqual(1, Db.MountsOf(10, 1).Count);
            Assert.IsEmpty(Db.MountsOf(9999, 0));
        }
    }
}
