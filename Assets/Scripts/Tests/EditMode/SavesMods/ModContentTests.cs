// ModContentTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Items, Ships, The look, Numbers, and Modding/README.md / the example
// Modding/Examples/plasma_arsenal (ModContent.cs): items.json { "id", "base" } adds a copy of the base (type, category and
// look kept, its own number from 233 on) with the fields given; { "override" } changes one; "stats" by readable name set
// the original attribute and its items.json statList key (ItemStats: 9 damage, 11 loading time, 12 range, 13 speed,
// 18 shield capacity...; attributes 3 tech level, 6 occurrence, 7 / 8 min / max price, 60 Vossk only, 61 always sold at);
// "defaultEconomy" only under the Default economy; ships.json adds ships from 64 (assembly ship_NNN_mod, pack "mod") or
// overrides one; a mod turned off leaves placeholders at its numbers. Base values from Resources/GoF2Data (Android):
// item 12 N'saan (primary, damage 8, reload 600, range 1500, speed 14, attr 60 = 1), 36 Edo, 50 Targe Shield (capacity 50,
// prices 3750-4125), ship 22 Groza (armor 160, cargo 130, slots 3/3/0/8).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModContentTests : SavesModsTest
    {
        const string ItemsMod = "t_items";

        const string ItemsJson = @"[
            // a new blaster from the N'saan
            { 'id': 'lance', 'base': 12, 'name': 'Test Lance', 'description': 'A test.', 'techLevel': 3, 'occurrence': 40, 'price': [24000, 26500],
              'vosskOnly': false, 'alwaysSoldAt': 78, 'stats': { 'damage': 14, 'reloadMs': 700, 'lifetimeMs': 1800, 'speed': 16 }, 'attributes': { '40': 120 } },
            { 'id': 'copy', 'base': 't_items:lance', 'name': 'Copy', 'price': 500 },
            { 'override': 50, 'stats': { 'SHIELDCAPACITY': 65 }, 'maxPrice': 4600 },
            { 'id': 'swarm', 'base': 36, 'name': 'Swarm', 'price': [260, 300], 'defaultEconomy': { 'price': [520, 600] } }
        ]";

        void ItemsModOn()
        {
            WriteMod("items", ItemsMod, "1.0", "", ("items.json", ItemsJson));
            ScanAndEnable(ItemsMod);
            Assert.IsFalse(Installed(ItemsMod).Broken, AllMessages(Installed(ItemsMod)));
        }

        // ---- items ------------------------------------------------------------------------------------------------------

        [Test]
        public void ANewItemIsACopyOfItsBaseWithItsOwnNumber()
        {
            ItemsModOn();
            var db = Database.Load();
            var baseItem = db.Item(12);
            var lance = db.Item(233);
            Assert.IsNotNull(lance);
            Assert.AreEqual(233, lance.index);
            Assert.AreEqual("Test Lance", lance.name);
            Assert.AreEqual("A test.", lance.description);
            Assert.AreEqual(baseItem.type, lance.type);
            Assert.AreEqual("primary", lance.type);
            Assert.AreEqual(baseItem.category, lance.category);
            Assert.AreEqual(baseItem.categoryId, lance.categoryId);
            Assert.IsTrue(lance.modded);
            Assert.AreEqual("t_items:lance", lance.modKey);
            Assert.AreEqual(12, lance.Look, "it looks and behaves like its base");
            Assert.AreEqual(233, lance.Attr(0), "attribute 0 is the index");
            Assert.AreEqual(baseItem.Attr(1), lance.Attr(1));
            Assert.AreEqual(0, lance.blueprint.Count, "a copy is no blueprint product");
            Assert.AreEqual(233, ModContent.ItemIndexOf("t_items:lance"));
            Assert.AreEqual("t_items:lance", ModContent.ItemKey(233));
            Assert.AreEqual(12, ModContent.ItemLook(233));
            Assert.AreEqual(12, ModContent.ItemLook(12), "an original item is its own look");
        }

        [Test]
        public void TheFieldsSetAttributesAndStats()
        {
            ItemsModOn();
            var lance = Database.Load().Item(233);
            Assert.AreEqual(3, lance.techLevel);
            Assert.AreEqual(3, lance.Attr(3));
            Assert.AreEqual(40, lance.occurrence);
            Assert.AreEqual(40, lance.Attr(6));
            Assert.AreEqual(24000, lance.minPrice);
            Assert.AreEqual(26500, lance.maxPrice);
            Assert.AreEqual(24000, lance.Attr(7));
            Assert.AreEqual(26500, lance.Attr(8));
            Assert.AreEqual(0, lance.Attr(60), "vosskOnly false (the N'saan's is 1)");
            Assert.AreEqual(78, lance.Attr(61));
            Assert.AreEqual(14, lance.Attr(9));
            Assert.AreEqual(14, lance.Stat("damage"));
            Assert.AreEqual(700, lance.Attr(11));
            Assert.AreEqual(700, lance.Stat("loadingTimeMs"));
            Assert.AreEqual(1800, lance.Attr(12));
            Assert.AreEqual(1800, lance.Stat("range"));
            Assert.AreEqual(16, lance.Attr(13));
            Assert.AreEqual(16, lance.Stat("projectileSpeed"));
            Assert.AreEqual(120, lance.Attr(40));
            Assert.AreEqual(120, lance.Stat("damageFactor"));
        }

        [Test]
        public void TheBaseItemIsntChanged()
        {
            ItemsModOn();
            var db = Database.Load();
            var nsaan = db.Item(12);
            Assert.AreEqual(8, nsaan.Attr(9));
            Assert.AreEqual(600, nsaan.Stat("loadingTimeMs"));
            Assert.AreEqual(1, nsaan.Attr(60));
            Assert.IsFalse(nsaan.modded);
            Assert.AreEqual(10580, nsaan.minPrice);
        }

        [Test]
        public void ACopyOfACopyLooksLikeTheOriginal()
        {
            ItemsModOn();
            var copy = Database.Load().Item(234);
            Assert.AreEqual("Copy", copy.name);
            Assert.AreEqual(12, copy.Look);
            Assert.AreEqual(14, copy.Attr(9), "it copies the modded base's stats");
            Assert.AreEqual(500, copy.minPrice);
            Assert.AreEqual(500, copy.maxPrice, "'price': n sets both");
        }

        [Test]
        public void AnOverrideChangesTheOriginalItem()
        {
            ItemsModOn();
            var targe = Database.Load().Item(50);
            Assert.AreEqual(65, targe.Attr(18), "stat names are read without case");
            Assert.AreEqual(65, targe.Stat("shieldCapacity"));
            Assert.AreEqual(4600, targe.maxPrice);
            Assert.AreEqual(4600, targe.Attr(8));
            Assert.AreEqual(3750, targe.minPrice);
            Assert.AreEqual(20000, targe.Attr(19), "what the override doesn't name stays");
            Assert.IsFalse(targe.modded);
            Assert.AreEqual(50, targe.index);
        }

        [Test]
        public void DefaultEconomyFieldsApplyOnlyUnderTheDefaultEconomy()
        {
            ItemsModOn();
            var swarm = Database.Load().Item(235);
            Assert.AreEqual(260, swarm.minPrice);
            Assert.AreEqual(300, swarm.maxPrice);
            Session.Economy = Economy.Default;
            swarm = Database.Load().Item(235);
            Assert.AreEqual(520, swarm.minPrice);
            Assert.AreEqual(600, swarm.maxPrice);
        }

        [Test]
        public void TheItemTableStaysNumberedInOrder()
        {
            ItemsModOn();
            var db = Database.Load();
            Assert.AreEqual(237, db.Items.Count, "the mod's three and the base content's one");
            for (int i = 0; i < db.Items.Count; i++) Assert.AreEqual(i, db.Items[i].index);
            Assert.AreEqual(233, ModContent.OriginalItemCount);
            CollectionAssert.AreEquivalent(new[] { 233, 234, 235, 236 }, ModContent.ActiveItemKeys().Select(k => k.Key).ToArray());
        }

        [Test]
        public void WithoutModsTheTablesAreTheOriginals()
        {
            ModManager.Scan();
            var db = Database.Load();
            Assert.AreEqual(OriginalItems, db.Items.Count(i => !i.modded));
            Assert.AreEqual(OriginalShips, db.Ships.Count);
            Assert.AreEqual(OriginalSystems, db.Systems.Count);
            Assert.AreEqual(OriginalStations, db.Stations.Count);
            Assert.AreEqual(1, db.Items.Count(i => i.modded), "only the base content");
        }

        [Test]
        public void AModTurnedOffLeavesAPlaceholder()
        {
            ItemsModOn();
            Database.Load();
            ModManager.SetEnabled(ItemsMod, false);
            var db = Database.Load();
            Assert.AreEqual(237, db.Items.Count, "the numbers stay taken");
            var p = db.Item(233);
            Assert.IsTrue(p.modded);
            Assert.AreEqual("t_items:lance", p.modKey);
            Assert.AreEqual("commodity", p.type);
            Assert.AreEqual(22, p.categoryId);
            Assert.AreEqual(0, p.minPrice);
            Assert.AreEqual(0, p.maxPrice);
            StringAssert.Contains("t_items:lance", p.name);
            Assert.IsTrue(ModContent.IsMissingItem(233));
            Assert.AreEqual(-1, ModContent.ItemIndexOf("t_items:lance"));
            Assert.AreEqual(50, db.Item(50).Attr(18), "the override is gone with its mod");
        }

        [Test]
        public void TurnedOnAgainAModKeepsItsNumbers()
        {
            ItemsModOn();
            Database.Load();
            ModManager.SetEnabled(ItemsMod, false);
            Database.Load();
            ModManager.SetEnabled(ItemsMod, true);
            var db = Database.Load();
            Assert.AreEqual("Test Lance", db.Item(233).name);
            Assert.AreEqual(237, db.Items.Count);
        }

        [Test]
        public void AnotherModsItemIsReachedByItsKey()
        {
            WriteMod("items", ItemsMod, "1.0", "", ("items.json", ItemsJson));
            WriteMod("addon", "t_addon", "1.0", ", 'dependencies': ['t_items']", ("items.json",
                "[ { 'id': 'heavy', 'base': 't_items:lance', 'name': 'Heavy Lance', 'stats': { 'damage': 30 } }, { 'override': 't_items:swarm', 'maxPrice': 999 } ]"));
            ScanAndEnable("t_addon");
            var db = Database.Load();
            var heavy = db.Item(ModContent.ItemIndexOf("t_addon:heavy"));
            Assert.AreEqual(236, heavy.index, "numbered after the mod it depends on");
            Assert.AreEqual(30, heavy.Attr(9));
            Assert.AreEqual(700, heavy.Attr(11));
            Assert.AreEqual(12, heavy.Look);
            Assert.AreEqual(999, db.Item(235).maxPrice);
        }

        [Test]
        public void ABadReferenceIsAWarningNotACrash()
        {
            WriteMod("bad", "t_badref", "1.0", "", ("items.json",
                "[ { 'override': 9999, 'maxPrice': 1 }, { 'id': 'orphan', 'base': 'nobody:thing' } ]"));
            ScanAndEnable("t_badref");
            var db = Database.Load();
            var m = Installed("t_badref");
            Assert.IsFalse(m.Broken);
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("\"override\": 9999 is no item")), AllMessages(m));
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("\"base\": nobody:thing is no item")), AllMessages(m));
            Assert.IsTrue(db.Items.All(i => i.name != "orphan"));
        }

        [Test]
        public void TranslationsComeFromTheModsTextFiles()
        {
            WriteMod("tr", "t_tr", "1.0", "", ("items.json", "[ { 'id': 'gun', 'base': 2 } ]"),
                ("text/en.json", "{ 'items.gun.name': 'Translated Gun', 'items.gun.description': 'From the text file.' }"),
                ("text/" + Localization.Language + ".json", "{ 'items.gun.name': 'Translated Gun', 'items.gun.description': 'From the text file.' }"));
            ScanAndEnable("t_tr");
            var gun = Database.Load().Item(233);
            Assert.AreEqual("Translated Gun", gun.name);
            Assert.AreEqual("From the text file.", gun.description);
        }

        [Test]
        public void AnItemWithoutANameIsCalledByItsKey()
        {
            WriteMod("nn", "t_noname", "1.0", "", ("items.json", "[ { 'id': 'gun', 'base': 2 } ]"));
            ScanAndEnable("t_noname");
            Assert.AreEqual("t_noname:gun", Database.Load().Item(233).name);
        }

        // ---- parsing errors -----------------------------------------------------------------------------------------------

        string ParseError(string itemsJson, string file = "items.json")
        {
            string dir = WriteMod("p", "t_parse", "1.0", "", (file, itemsJson));
            var m = OpenParsed(dir);
            Assert.IsTrue(m.Broken, "accepted: " + itemsJson);
            return m.Errors[0];
        }

        [Test] public void ItemsMustBeAList() => StringAssert.Contains("must be a list", ParseError("{ 'id': 'x' }"));
        [Test] public void AnItemNeedsIdOrOverride() => StringAssert.Contains("either \"id\"", ParseError("[ { 'base': 2 } ]"));
        [Test] public void AnItemCantHaveBothIdAndOverride() => StringAssert.Contains("either \"id\"", ParseError("[ { 'id': 'x', 'override': 2, 'base': 2 } ]"));
        [Test] public void ANewItemNeedsABase() => StringAssert.Contains("needs \"base\"", ParseError("[ { 'id': 'x' } ]"));
        [Test] public void AnItemIdMustBeValid() => StringAssert.Contains("may only use", ParseError("[ { 'id': 'Big Gun', 'base': 2 } ]"));
        [Test] public void AnItemIdIsUsedOnce() => StringAssert.Contains("used twice", ParseError("[ { 'id': 'x', 'base': 2 }, { 'id': 'x', 'base': 3 } ]"));
        [Test] public void AnUnknownStatIsAnError() => StringAssert.Contains("unknown stat \"dmg\"", ParseError("[ { 'override': 2, 'stats': { 'dmg': 5 } } ]"));
        [Test] public void StatsMustBeAnObject() => StringAssert.Contains("\"stats\" must be an object", ParseError("[ { 'override': 2, 'stats': [5] } ]"));
        [Test] public void AStatMustBeANumber() => StringAssert.Contains("must be a whole number", ParseError("[ { 'override': 2, 'stats': { 'damage': 'lots' } } ]"));
        [Test] public void AttributesAre0To99() => StringAssert.Contains("must be a number 0-99", ParseError("[ { 'override': 2, 'attributes': { '100': 5 } } ]"));
        [Test] public void APriceMustBeANumber() => StringAssert.Contains("\"minPrice\" must be a whole number", ParseError("[ { 'override': 2, 'minPrice': 'cheap' } ]"));

        [Test]
        public void AnErrorNamesTheEntrysLine()
        {
            StringAssert.Contains("items.json line 3", ParseError("[\n { 'override': 2 },\n { 'base': 2 }\n]"));
        }

        [Test]
        public void UnknownFieldsAndLowAttributesAreWarnings()
        {
            string dir = WriteMod("w", "t_warn", "1.0", "", ("items.json", "[ { 'override': 2, 'colour': 'red', 'attributes': { '7': 10 }, 'icon': 'missing.png' } ]"));
            var m = OpenParsed(dir);
            Assert.IsFalse(m.Broken, AllMessages(m));
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("unknown field \"colour\"")), AllMessages(m));
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("attributes 0-8 follow the item's fields")), AllMessages(m));
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("missing.png")), AllMessages(m));
        }

        [Test]
        public void MaxPriceIsNeverBelowMinPrice()
        {
            WriteMod("mp", "t_maxp", "1.0", "", ("items.json", "[ { 'override': 2, 'minPrice': 50000, 'maxPrice': 100 } ]"));
            ScanAndEnable("t_maxp");
            var it = Database.Load().Item(2);
            Assert.AreEqual(50000, it.minPrice);
            Assert.AreEqual(50000, it.maxPrice);
        }

        [Test]
        public void VosskOnlyAndAlwaysSoldAtAreAttributes60And61()
        {
            WriteMod("vo", "t_vossk", "1.0", "", ("items.json", "[ { 'override': 2, 'vosskOnly': true, 'alwaysSoldAt': 107 } ]"));
            ScanAndEnable("t_vossk");
            var it = Database.Load().Item(2);
            Assert.AreEqual(1, it.Attr(60));
            Assert.AreEqual(107, it.Attr(61));
        }

        // ---- the example mod ----------------------------------------------------------------------------------------------

        [Test]
        public void ThePlasmaArsenalExampleApplies()
        {
            // Modding/Examples/plasma_arsenal/items.json: the Plasma Lance (base 12, damage 14, reload 700, lifetime 1800,
            // speed 16, prices 24000-26500, sold at 78), the Hornet Swarm (base 36, damage 95, reload 1400, 260-300, Default
            // 520-600) and the Targe Shield (50) at capacity 65, max price 4600.
            CopyExample("plasma_arsenal");
            ScanAndEnable("plasma_arsenal");
            var m = Installed("plasma_arsenal");
            Assert.IsFalse(m.Broken, AllMessages(m));
            Assert.AreEqual("1.0", m.Version);
            var db = Database.Load();
            var lance = db.Item(ModContent.ItemIndexOf("plasma_arsenal:plasma_lance"));
            var hornet = db.Item(ModContent.ItemIndexOf("plasma_arsenal:hornet_swarm"));
            Assert.AreEqual(233, lance.index);
            Assert.AreEqual(234, hornet.index);
            Assert.AreEqual(12, lance.Look);
            Assert.AreEqual(14, lance.Attr(9));
            Assert.AreEqual(700, lance.Attr(11));
            Assert.AreEqual(1800, lance.Attr(12));
            Assert.AreEqual(16, lance.Attr(13));
            Assert.AreEqual(24000, lance.minPrice);
            Assert.AreEqual(26500, lance.maxPrice);
            Assert.AreEqual(78, lance.Attr(61));
            Assert.AreEqual(36, hornet.Look);
            Assert.AreEqual(95, hornet.Attr(9));
            Assert.AreEqual(1400, hornet.Attr(11));
            Assert.AreEqual(260, hornet.minPrice);
            Assert.AreEqual(300, hornet.maxPrice);
            Assert.AreEqual(65, db.Item(50).Attr(18));
            Assert.AreEqual(4600, db.Item(50).maxPrice);
        }

        // ---- ships --------------------------------------------------------------------------------------------------------

        const string ShipsJson = @"[
            { 'id': 'falcon', 'model': 'models/falcon.glb', 'name': 'Test Falcon', 'race': 0, 'armor': 300, 'cargo': 80, 'price': 500000,
              'priceDefault': 400000, 'handling': 120, 'slots': { 'primary': 3, 'secondary': 2, 'turret': 1, 'equipment': 7 },
              'mounts': [ { 'slotType': 0, 'position_engine': [10, 0, 200] }, { 'slotType': 3, 'position_engine': [0, 0, -300] } ] },
            { 'override': 22, 'armor': 999, 'cargo': 150, 'slots': { 'primary': 4 } }
        ]";

        void ShipsModOn()
        {
            string dir = WriteMod("ships", "t_ships", "1.0", "", ("ships.json", ShipsJson));
            WriteFile(dir, "models/falcon.glb", Dummy);
            ScanAndEnable("t_ships");
            Assert.IsFalse(Installed("t_ships").Broken, AllMessages(Installed("t_ships")));
        }

        [Test]
        public void ANewShipIsNumberedAfterTheOriginal64()
        {
            ShipsModOn();
            var db = Database.Load();
            Assert.AreEqual(65, db.Ships.Count);
            var c = db.Ship(64) as CustomShipData;
            Assert.IsNotNull(c, "a mod ship is a CustomShipData");
            Assert.AreEqual(64, c.index);
            Assert.AreEqual("Test Falcon", c.name);
            Assert.AreEqual(300, c.armor);
            Assert.AreEqual(80, c.cargo);
            Assert.AreEqual(500000, c.price);
            Assert.AreEqual(120, c.handling);
            Assert.AreEqual(1.2f, c.handlingMultiplier, 1e-6f);
            Assert.AreEqual(3, c.slots.primary);
            Assert.AreEqual(2, c.slots.secondary);
            Assert.AreEqual(1, c.slots.turret);
            Assert.AreEqual(7, c.slots.equipment);
            Assert.AreEqual(0, c.race);
            Assert.AreEqual("ship_064_mod", c.assembly);
            Assert.IsNull(c.lounge, "no seller without a 'lounge' entry");
            Assert.IsNull(c.dealer, "no dealer without a 'dealer' entry");
            var asm = db.AssemblyByName("ship_064_mod");
            Assert.IsNotNull(asm);
            Assert.AreEqual("mod", asm.pack);
            Assert.AreEqual("ships", asm.category);
            Assert.AreSame(asm, db.ShipAssembly(64));
            var mounts = db.WeaponMounts.Find(w => w.ship == 64);
            Assert.AreEqual(2, mounts.mounts.Count);
            Assert.AreEqual(3, mounts.mounts[1].slotType);
            Assert.AreEqual("t_ships:falcon", ModContent.ShipKey(64));
            Assert.IsTrue(ModContent.IsModShip(64));
            Assert.AreEqual(64, ModContent.ShipIndexOf("t_ships:falcon"));
            Assert.IsTrue(CustomShips.IsCustom(64));
            Assert.AreEqual(64, ModContent.OriginalShipCount);
        }

        [Test]
        public void AShipsDefaultEconomyPrice()
        {
            ShipsModOn();
            Session.Economy = Economy.Default;
            Assert.AreEqual(400000, Database.Load().Ship(64).price);
        }

        [Test]
        public void AShipOverrideChangesTheOriginal()
        {
            ShipsModOn();
            var groza = Database.Load().Ship(22);
            Assert.AreEqual(999, groza.armor);
            Assert.AreEqual(150, groza.cargo);
            Assert.AreEqual(4, groza.slots.primary);
            Assert.AreEqual(3, groza.slots.secondary, "slots not named stay");
            Assert.AreEqual(8, groza.slots.equipment);
            Assert.AreEqual(201280, groza.price);
            Assert.IsFalse(groza is CustomShipData);
        }

        [Test]
        public void AShipTurnedOffLeavesAPlaceholder()
        {
            ShipsModOn();
            Database.Load();
            ModManager.SetEnabled("t_ships", false);
            var db = Database.Load();
            Assert.AreEqual(65, db.Ships.Count);
            var p = db.Ship(64);
            Assert.AreEqual(0, p.price);
            Assert.AreEqual(100, p.armor);
            Assert.AreEqual(0, p.cargo);
            Assert.IsTrue(ModContent.IsMissingShip(64));
            Assert.IsFalse(ModContent.IsModShip(64));
            Assert.AreEqual("t_ships:falcon", ModContent.ShipKey(64));
            Assert.AreEqual(160, db.Ship(22).armor, "the override is gone");
        }

        [Test]
        public void AnOverrideWithAModelReplacesTheOriginalsModel()
        {
            string dir = WriteMod("groza", "t_groza", "1.0", "", ("ships.json", "[ { 'override': 22, 'model': 'manticore.glb', 'modelLength': 1200 } ]"));
            WriteFile(dir, "manticore.glb", Dummy);
            ScanAndEnable("t_groza");
            var db = Database.Load();
            Assert.AreEqual(64, db.Ships.Count, "no new ship");
            Assert.AreEqual("ship_022_mod", ModContent.ModelOverrideAssembly(22));
            Assert.AreEqual("ship_022_mod", db.ShipAssembly(22).name);
            Assert.AreNotEqual("ship_022_mod", db.ShipAssembly(22, null, true).name, "the original's stands in while it loads");
            var o = ModContent.ModelOverrides();
            Assert.AreEqual(1, o.Count);
            Assert.AreEqual(22, o[0].ship.index);
            Assert.AreEqual(1200f, o[0].ship.modelLength);
            Assert.IsTrue(ModContent.AnyShipModels());
        }

        // ---- npcTurrets (remake, generic turrets: World.NpcMountedTurrets, the hangar's parked ships) ---------------------

        [Test]
        public void NpcTurretsGiveAShipTypesNpcsTurretItems()
        {
            Assert.IsEmpty(Database.Load().NpcTurretsOf(28), "the original's NPC ships carry none");
            WriteMod("turrets", "t_turrets", "1.0", "", ("ships.json", "[ { 'override': 28, 'npcTurrets': [ 48 ] }, { 'override': 51, 'npcTurrets': [ '181' ] } ]"));
            ScanAndEnable("t_turrets");
            Assert.IsFalse(Installed("t_turrets").Broken, AllMessages(Installed("t_turrets")));
            var db = Database.Load();
            CollectionAssert.AreEqual(new[] { 48 }, db.NpcTurretsOf(28), "the Veteran's NPCs: a Hammerhead on its turret mount");
            CollectionAssert.AreEqual(new[] { 181 }, db.NpcTurretsOf(51));
            Assert.IsEmpty(db.NpcTurretsOf(22), "other ships none");
        }

        [Test]
        public void NpcTurretsMustBeTurrets()
        {
            WriteMod("turrets", "t_turrets2", "1.0", "", ("ships.json", "[ { 'override': 28, 'npcTurrets': [ 12, 48, 47 ] } ]"));
            ScanAndEnable("t_turrets2");
            var db = Database.Load();
            CollectionAssert.AreEqual(new[] { 48, 47 }, db.NpcTurretsOf(28), "the N'saan (a primary) is left out");
            StringAssert.Contains("is no turret", AllMessages(Installed("t_turrets2")));
            StringAssert.Contains("1 turret mounts", AllMessages(Installed("t_turrets2")), "more turrets than mounts: a warning");
        }

        [Test] public void NpcTurretsMustBeAList() => StringAssert.Contains("\"npcTurrets\" must be a list", ParseError("[ { 'override': 28, 'npcTurrets': 48 } ]", "ships.json"));

        // ---- integrated (a modder's request: a built-in Khador Drive / cloak like the Kothar ships' and the Specter's) ------

        [Test]
        public void IntegratedItemsAreTheOriginalsWithoutMods()
        {
            var db = Database.Load();
            foreach (int ship in new[] { 37, 38, 40 }) Assert.IsTrue(db.HasIntegratedDrive(ship), "ship " + ship);
            Assert.IsFalse(db.HasIntegratedDrive(10));
            Assert.AreEqual(95, db.IntegratedCloak(44));
            Assert.AreEqual(95, db.IntegratedCloak(49));
            Assert.AreEqual(-1, db.IntegratedCloak(10));
        }

        [Test]
        public void IntegratedGivesAndTakesADriveAndACloak()
        {
            WriteMod("integrated", "t_integrated", "1.0", "", ("ships.json",
                "[ { 'override': 22, 'integrated': { 'khadorDrive': true, 'cloak': 96 } }, { 'override': 28, 'integrated': { 'cloak': true } }," +
                "  { 'override': 37, 'integrated': { 'khadorDrive': false } }, { 'override': 44, 'integrated': { 'cloak': false } } ]"));
            ScanAndEnable("t_integrated");
            Assert.IsFalse(Installed("t_integrated").Broken, AllMessages(Installed("t_integrated")));
            var db = Database.Load();
            Assert.IsTrue(db.HasIntegratedDrive(22), "the Groza: a Khador Drive built in");
            Assert.AreEqual(96, db.IntegratedCloak(22), "and the cloak it names");
            Assert.AreEqual(95, db.IntegratedCloak(28), "true: the U'tool's stats");
            Assert.IsFalse(db.HasIntegratedDrive(37), "the Cronus' taken away");
            Assert.AreEqual(-1, db.IntegratedCloak(44), "the Specter's taken away");
            Assert.AreEqual(95, db.IntegratedCloak(49), "the rest stay");
            Assert.IsTrue(GalaxyMap.HasIntegratedDrive(22), "the game's checks read it (Database.Shared)");
        }

        [Test]
        public void AnIntegratedCloakMustBeACloak()
        {
            WriteMod("integrated", "t_integrated2", "1.0", "", ("ships.json",
                "[ { 'override': 22, 'integrated': { 'cloak': 12, 'shield': true } } ]"));
            ScanAndEnable("t_integrated2");
            Assert.AreEqual(-1, Database.Load().IntegratedCloak(22));
            StringAssert.Contains("is no cloak", AllMessages(Installed("t_integrated2")));
            StringAssert.Contains("unknown field \"shield\"", AllMessages(Installed("t_integrated2")));
        }

        [Test] public void IntegratedMustBeAnObject() => StringAssert.Contains("\"integrated\" must be an object", ParseError("[ { 'override': 22, 'integrated': true } ]", "ships.json"));

        // ---- camera / cockpit / smoke / explosionScale (ModShipView: how a ship is seen, from GoF3's ship files) ---------

        [Test]
        public void ShipsSeenTheOriginalsWayWithoutMods()
        {
            var s = Database.Load().Ship(22);
            Assert.IsNull(s.chaseCamera);
            Assert.IsNull(s.cockpit);
            Assert.IsNull(s.smokePoints);
            Assert.AreEqual(1f, s.explosionScale);
            ModShipView.CameraOffsets(s, 0.05f, out var offset, out var look);
            Assert.AreEqual(new UnityEngine.Vector3(0f, 600f, -1338f) * 0.05f, offset, "TargetFollowCamera's offsets");
            Assert.AreEqual(new UnityEngine.Vector3(0f, 600f, -650f) * 0.05f, look);
        }

        [Test]
        public void AShipHasItsOwnCameraCockpitSmokeAndExplosion()
        {
            WriteMod("view", "t_view", "1.0", "", ("ships.json",
                "[ { 'override': 22, 'camera': { 'distance': 2400, 'height': 900 }, 'cockpit': [ 10, 80, 500 ]," +
                "    'smoke': [ [ 40, 60, -100 ], [ -40, 60, -100 ] ], 'explosionScale': 1.6 } ]"));
            ScanAndEnable("t_view");
            Assert.IsFalse(Installed("t_view").Broken, AllMessages(Installed("t_view")));
            var s = Database.Load().Ship(22);
            CollectionAssert.AreEqual(new[] { 2400f, 900f, 600f, -650f }, s.chaseCamera, "the fields left out keep the original's");
            Assert.AreEqual(new UnityEngine.Vector3(10, 80, 500), s.cockpit.Value);
            Assert.AreEqual(2, s.smokePoints.Count);
            Assert.AreEqual(1.6f, s.explosionScale, 1e-4f);
            ModShipView.CameraOffsets(s, 0.05f, out var offset, out _);
            Assert.AreEqual(new UnityEngine.Vector3(0f, 900f, -2400f) * 0.05f, offset);
            Assert.AreEqual(new UnityEngine.Vector3(-0.5f, 4f, 25f), ModShipView.ToLocal(s.cockpit.Value, 0.05f), "like a mount: x mirrored");
            Assert.IsNull(Database.Load().Ship(28).chaseCamera, "other ships keep the original's");
        }

        [Test]
        public void AShipsViewWarnsAndClamps()
        {
            WriteMod("view", "t_view2", "1.0", "", ("ships.json",
                "[ { 'override': 22, 'camera': { 'distance': 2000, 'zoom': 2 }, 'explosionScale': 50," +
                "    'smoke': [ [0,0,0], [0,0,1], [0,0,2], [0,0,3], [0,0,4] ] } ]"));
            ScanAndEnable("t_view2");
            var s = Database.Load().Ship(22);
            Assert.AreEqual(ModShipView.MaxExplosion, s.explosionScale);
            Assert.AreEqual(ModShipView.MaxSmokePoints, s.smokePoints.Count);
            string all = AllMessages(Installed("t_view2"));
            StringAssert.Contains("unknown camera field \"zoom\"", all);
            StringAssert.Contains("(clamped)", all);
            StringAssert.Contains("only the first 4", all);
        }

        [Test] public void ACockpitIsAPoint() => StringAssert.Contains("\"cockpit\" must be a point", ParseError("[ { 'override': 22, 'cockpit': [ 1, 2 ] } ]", "ships.json"));
        [Test] public void SmokeIsAListOfPoints() => StringAssert.Contains("each smoke point must be", ParseError("[ { 'override': 22, 'smoke': [ 1, 2, 3 ] } ]", "ships.json"));
        [Test] public void ACameraIsAnObject() => StringAssert.Contains("\"camera\" must be an object", ParseError("[ { 'override': 22, 'camera': 2000 } ]", "ships.json"));
        [Test] public void ACamerasDistanceIsAboveZero() => StringAssert.Contains("\"distance\" must be above 0", ParseError("[ { 'override': 22, 'camera': { 'distance': 0 } } ]", "ships.json"));

        // ---- a steady engine glow (a modder's request: dark parked, lit through the take-off and all flight) -------------

        [Test]
        public void EngineMaterialsTakeAnIdleShare()
        {
            Assert.AreEqual(0f, Modding.EngineMaterialGlow.Target(0f, 0f, false), "default: dark at throttle 0");
            Assert.AreEqual(0.5f, Modding.EngineMaterialGlow.Target(0.5f, 0f, false), 1e-5f, "following the throttle");
            Assert.AreEqual(1f, Modding.EngineMaterialGlow.Target(0f, 1f, false), "engineIdle 1: steady at throttle 0");
            Assert.AreEqual(1f, Modding.EngineMaterialGlow.Target(2f, 1f, false), "and while boosting");
            Assert.AreEqual(0f, Modding.EngineMaterialGlow.Target(1f, 1f, true), "dark while the engines are off (parked)");
            Assert.AreEqual(0.625f, Modding.EngineMaterialGlow.Target(0.5f, 0.25f, false), 1e-5f);
        }

        [Test]
        public void ShipsJsonReadsTheSteadyGlowFields()
        {
            var c = UnityEngine.JsonUtility.FromJson<CustomShipData>("{ \"materials\": [ { \"engine\": true, \"engineIdle\": 1 } ], \"throttleGlow\": { \"idle\": 4, \"full\": 4, \"off\": 0 } }");
            Assert.AreEqual(1f, c.materials[0].engineIdle);
            Assert.AreEqual(0f, c.throttleGlow.off);
            Assert.AreEqual(-1f, new CustomThrottleGlow().off, "default: parked at 'idle' as before");
            Assert.AreEqual(0f, new CustomShipMaterial().engineIdle, "default: follows the throttle as before");
        }

        [Test] public void ANewShipNeedsAModel() => StringAssert.Contains("needs \"model\"", ParseError("[ { 'id': 'x' } ]", "ships.json"));
        [Test] public void AShipsModelMustBeInTheMod() => StringAssert.Contains("isn't in the mod", ParseError("[ { 'id': 'x', 'model': 'nope.glb' } ]", "ships.json"));
        [Test] public void AShipNeedsIdOrOverride() => StringAssert.Contains("either \"id\"", ParseError("[ { 'armor': 5 } ]", "ships.json"));
        [Test] public void AShipsNumbersMustBeNumbers() => StringAssert.Contains("\"armor\" must be a whole number", ParseError("[ { 'override': 5, 'armor': 'thick' } ]", "ships.json"));
        [Test] public void ShipsMustBeAList() => StringAssert.Contains("must be a list", ParseError("{ }", "ships.json"));

        // ---- ItemStats ----------------------------------------------------------------------------------------------------

        [TestCase("damage", 9)]
        [TestCase("empDamage", 10)]
        [TestCase("reloadMs", 11)]
        [TestCase("lifetimeMs", 12)]
        [TestCase("speed", 13)]
        [TestCase("shieldCapacity", 18)]
        [TestCase("shieldRegenMs", 19)]
        [TestCase("armor", 20)]
        [TestCase("cargoBonus", 22)]
        [TestCase("boostSpeed", 25)]
        [TestCase("boostRechargeMs", 26)]
        [TestCase("boostDurationMs", 27)]
        [TestCase("agility", 28)]
        [TestCase("lockTimeMs", 29)]
        [TestCase("radarShowsCargo", 31)]
        [TestCase("fireRateFactor", 39)]
        [TestCase("damageFactor", 40)]
        [TestCase("miningBeam", 100)]
        [TestCase("miningBeamRange", 101)]
        [TestCase("miningBeamLayerMs", 102)]
        [TestCase("miningBeamLook", 103)]
        [TestCase("phaseCloak", 104)]
        [TestCase("loadingTimeMs", 11)]   // items.json's statList names work too
        [TestCase("projectileSpeed", 13)]
        [TestCase("DAMAGE", 9)]
        [TestCase("dmg", -1)]
        [TestCase(null, -1)]
        public void StatNamesAreTheOriginalsAttributes(string name, int id)
        {
            Assert.AreEqual(id, ItemStats.IdOf(name));
        }

        [TestCase(9, "damage")]
        [TestCase(11, "loadingTimeMs")]
        [TestCase(12, "range")]
        [TestCase(13, "projectileSpeed")]
        [TestCase(18, "shieldCapacity")]
        [TestCase(19, "shieldRegenTime")]
        [TestCase(100, null)]
        [TestCase(5, null)]
        public void StatKeysAreItemsJsonsNames(int id, string key)
        {
            Assert.AreEqual(key, ItemStats.StatKeyOf(id));
        }

        [Test]
        public void TheReadableNamesAreTheFirstOnes()
        {
            var names = ItemStats.Readable().Select(kv => kv.Key).ToList();
            CollectionAssert.Contains(names, "damage");
            CollectionAssert.Contains(names, "phaseCloak");
            CollectionAssert.DoesNotContain(names, "loadingTimeMs");
        }

        [Test]
        public void EveryItemsStatListAgreesWithItsAttributes()
        {
            // ModContent keeps a stat and its attribute in step; the original tables already agree (items.json's statList
            // and item_attributes.json, Android economy), so StatKeyOf maps them the same way.
            var db = Database.Load();
            int checkedStats = 0;
            foreach (var it in db.Items)
                foreach (var s in it.statList)
                {
                    int id = ItemStats.IdOf(s.key);
                    if (id < 0 || ItemStats.StatKeyOf(id) != s.key) continue;
                    Assert.AreEqual(s.value, it.Attr(id), $"item {it.index} {s.key}");
                    checkedStats++;
                }
            Assert.Greater(checkedStats, 400);
        }
    }
}
