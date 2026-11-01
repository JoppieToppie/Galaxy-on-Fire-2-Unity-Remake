// ModBlueprintsTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Mod blueprints (ModBlueprints.cs): blueprints.json entries make a "ship" or an
// "item" from ingredients (amount at least 1, default 1), "autocomplete" true / false / a price, "requiresShip" (a skin,
// built only while flying that ship), "unlocked", "available", and three sources: "lounge" { chance, price, systemRace },
// "derelict" { chance, race: a Terran / Vossk / Nivelian / Midorian freighter }, "drops" [ { race, chance } ] (races
// terran 0, vossk 1, nivelian 2, midorian 3, pirate 8, void 9, specter 10, any -1). An item product gets the recipe; a ship
// product is a hidden "blueprint item" ("<mod>:blueprint:<id>", numbered like a mod item after the items) priced like the
// ship. Item 155 = Titanium, 156 = its neighbour in items.json (Resources/GoF2Data).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModBlueprintsTests : SavesModsTest
    {
        const string BlueprintsJson = @"[
            { 'id': 'gun_bp', 'item': 't_bp:gun', 'ingredients': [ { 'item': 155, 'amount': 50 }, { 'item': 156, 'amount': 2 } ],
              'autocomplete': 150000, 'lounge': { 'chance': 25, 'price': 9000, 'systemRace': 1 },
              'drops': [ { 'race': 'pirate', 'chance': 2.5 }, { 'race': 'Void' } ], 'derelict': { 'chance': 7, 'race': 'vossk' }, 'unlocked': true },
            { 'id': 'yacht_bp', 'ship': 't_bp:yacht', 'ingredients': [ { 'item': 155 } ], 'autocomplete': false, 'requiresShip': 10 }
        ]";

        void ModOn()
        {
            string dir = WriteMod("bp", "t_bp", "1.0", "",
                ("items.json", "[ { 'id': 'gun', 'base': 2, 'name': 'Gun' } ]"),
                ("ships.json", "[ { 'id': 'yacht', 'model': 'y.glb', 'name': 'Yacht', 'price': 777000 } ]"),
                ("blueprints.json", BlueprintsJson));
            WriteFile(dir, "y.glb", Dummy);
            ScanAndEnable("t_bp");
            Assert.IsFalse(Installed("t_bp").Broken, AllMessages(Installed("t_bp")));
        }

        [Test]
        public void TheEntriesAreParsed()
        {
            ModOn();
            var all = ModBlueprints.All().Where(b => !b.mod.IsBase).ToList();
            Assert.AreEqual(2, all.Count);
            var gun = all[0];
            Assert.AreEqual("gun_bp", gun.localId);
            Assert.IsFalse(gun.IsShip);
            Assert.AreEqual("t_bp:gun", gun.itemRef);
            CollectionAssert.AreEqual(new[] { ("155", 50), ("156", 2) }, gun.ingredients);
            Assert.IsTrue(gun.autocomplete);
            Assert.AreEqual(150000, gun.autocompletePrice);
            Assert.AreEqual(25f, gun.loungeChance);
            Assert.AreEqual(9000, gun.loungePrice);
            Assert.AreEqual(1, gun.loungeRace);
            Assert.AreEqual(7f, gun.derelictChance);
            Assert.AreEqual(1, gun.derelictRace);
            CollectionAssert.AreEqual(new[] { (8, 2.5f), (9, 1f) }, gun.drops, "a drop's chance defaults to 1");
            Assert.IsTrue(gun.unlocked);
            var yacht = all[1];
            Assert.IsTrue(yacht.IsShip);
            Assert.AreEqual("t_bp:blueprint:yacht_bp", yacht.DeedKey);
            Assert.AreEqual(1, yacht.ingredients[0].amount, "an amount defaults to 1");
            Assert.IsFalse(yacht.autocomplete);
            Assert.AreEqual(-1, yacht.autocompletePrice);
            Assert.AreEqual(0f, yacht.loungeChance, "no lounge entry, no seller");
            Assert.AreEqual(0f, yacht.derelictChance);
            Assert.AreEqual(0, yacht.drops.Count);
            Assert.IsFalse(yacht.unlocked);
        }

        [Test]
        public void AnItemProductGetsTheRecipe()
        {
            ModOn();
            var db = Database.Load();
            var gun = db.Item(233);
            Assert.AreEqual(2, gun.blueprint.Count);
            Assert.AreEqual(155, gun.blueprint[0].item);
            Assert.AreEqual(50, gun.blueprint[0].amount);
            Assert.AreEqual(db.Item(155).name, gun.blueprint[0].name);
            Assert.AreEqual(156, gun.blueprint[1].item);
            Assert.AreEqual(2, gun.blueprint[1].amount);
            Assert.AreEqual("gun_bp", ModBlueprints.Of(233).localId);
            Assert.IsNull(ModBlueprints.Of(2), "an original product has none");
            Assert.IsTrue(ModBlueprints.AutocompleteOverride(233, out bool allowed, out int price));
            Assert.IsTrue(allowed);
            Assert.AreEqual(150000, price);
            Assert.IsFalse(ModBlueprints.AutocompleteOverride(2, out _, out _));
        }

        [Test]
        public void AShipProductIsAHiddenBlueprintItemPricedLikeTheShip()
        {
            ModOn();
            var db = Database.Load();
            int deed = ModContent.ItemIndexOf("t_bp:blueprint:yacht_bp");
            Assert.AreEqual(235, deed, "numbered after the mod's items and the base content's");
            var item = db.Item(deed);
            Assert.IsNotNull(item);
            Assert.IsTrue(item.modded);
            Assert.AreEqual("t_bp:blueprint:yacht_bp", item.modKey);
            Assert.AreEqual("commodity", item.type);
            Assert.AreEqual(22, item.categoryId);
            Assert.AreEqual(777000, item.minPrice);
            Assert.AreEqual(777000, item.maxPrice);
            Assert.AreEqual(1, item.blueprint.Count);
            Assert.AreEqual(155, item.blueprint[0].item);
            Assert.AreEqual(1, item.blueprint[0].amount);
            Assert.AreEqual(64, ModBlueprints.ShipOf(deed));
            Assert.AreEqual(-1, ModBlueprints.ShipOf(233));
            Assert.IsTrue(ModBlueprints.AutocompleteOverride(deed, out bool allowed, out _));
            Assert.IsFalse(allowed);
        }

        [Test]
        public void ASkinIsBuiltOnlyWhileFlyingItsShip()
        {
            ModOn();
            Database.Load();
            int deed = ModContent.ItemIndexOf("t_bp:blueprint:yacht_bp");
            Assert.AreEqual(10, ModBlueprints.Of(deed).requiresShip);
            Session.ShipIndex = 10;
            Assert.IsNull(ModBlueprints.RequiresShipRefusal(deed));
            Session.ShipIndex = 5;
            Assert.IsNotNull(ModBlueprints.RequiresShipRefusal(deed));
            Assert.IsNull(ModBlueprints.RequiresShipRefusal(233), "no ship needed");
        }

        [Test]
        public void UnlockedBlueprintsAreKnownOnceAvailable()
        {
            ModOn();
            Database.Load();
            Assert.IsFalse(Blueprints.IsUnlocked(233));
            Assert.AreEqual(2, ModBlueprints.Offerable(d => true).Count);
            ModBlueprints.UnlockAvailable();
            Assert.IsTrue(Blueprints.IsUnlocked(233));
            Assert.IsFalse(Blueprints.IsUnlocked(ModContent.ItemIndexOf("t_bp:blueprint:yacht_bp")));
            Assert.AreEqual(1, ModBlueprints.Offerable(d => true).Count, "a known blueprint isn't offered any more");
            Assert.AreEqual(0, ModBlueprints.Offerable(d => d.loungeChance > 0f).Count);
        }

        [Test]
        public void ABadProductIsAWarning()
        {
            WriteMod("bp", "t_bp2", "1.0", "", ("blueprints.json",
                "[ { 'id': 'x', 'item': 9999, 'ingredients': [ { 'item': 155 } ] }, { 'id': 'y', 'item': 2, 'ingredients': [ { 'item': 'nobody:thing' } ] } ]"));
            ScanAndEnable("t_bp2");
            Database.Load();
            var m = Installed("t_bp2");
            Assert.IsFalse(m.Broken);
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("\"item\": 9999 is no item")), AllMessages(m));
            Assert.IsTrue(m.Warnings.Any(w => w.Contains("ingredient nobody:thing is no item")), AllMessages(m));
            Assert.IsNull(ModBlueprints.Of(2));
        }

        string ParseError(string json)
        {
            string dir = WriteMod("p", "t_bperr", "1.0", "", ("blueprints.json", json));
            var m = OpenParsed(dir);
            Assert.IsTrue(m.Broken, "accepted: " + json);
            return m.Errors[0];
        }

        [Test] public void BlueprintsMustBeAList() => StringAssert.Contains("must be a list", ParseError("{ }"));
        [Test] public void AnIdIsNeeded() => StringAssert.Contains("\"id\" is missing", ParseError("[ { 'item': 2, 'ingredients': [ { 'item': 155 } ] } ]"));
        [Test] public void AnIdIsUsedOnce() => StringAssert.Contains("used twice", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ] }, { 'id': 'a', 'item': 3, 'ingredients': [ { 'item': 155 } ] } ]"));
        [Test] public void AShipOrAnItemNotBoth() => StringAssert.Contains("either a \"ship\" or an \"item\"", ParseError("[ { 'id': 'a', 'item': 2, 'ship': 5, 'ingredients': [ { 'item': 155 } ] } ]"));
        [Test] public void AShipOrAnItemNotNeither() => StringAssert.Contains("either a \"ship\" or an \"item\"", ParseError("[ { 'id': 'a', 'ingredients': [ { 'item': 155 } ] } ]"));
        [Test] public void IngredientsAreNeeded() => StringAssert.Contains("\"ingredients\" is missing", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [] } ]"));
        [Test] public void AnIngredientNamesItsItem() => StringAssert.Contains("an ingredient is", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'amount': 5 } ] } ]"));
        [Test] public void AnAmountIsAtLeast1() => StringAssert.Contains("at least 1", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155, 'amount': 0 } ] } ]"));
        [Test] public void AutocompleteIsTrueFalseOrAPrice() => StringAssert.Contains("\"autocomplete\" is true, false or a price", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'autocomplete': 'yes' } ]"));
        [Test] public void AnUnknownDropRace() => StringAssert.Contains("unknown race \"klingon\"", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'drops': [ { 'race': 'klingon' } ] } ]"));
        [Test] public void ADerelictIsAFreighterOfTheFourRaces() => StringAssert.Contains("Terran, Vossk, Nivelian or Midorian freighter", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'derelict': { 'race': 'pirate' } } ]"));

        [TestCase("terran", 0)]
        [TestCase("vossk", 1)]
        [TestCase("nivelian", 2)]
        [TestCase("midorian", 3)]
        [TestCase("pirates", 8)]
        [TestCase("void", 9)]
        [TestCase("specters", 10)]
        [TestCase("any", -1)]
        public void DropRaces(string race, int id)
        {
            string dir = WriteMod("d", "t_drop", "1.0", "", ("blueprints.json",
                "[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'drops': [ { 'race': '" + race + "', 'chance': 3 } ] } ]"));
            var m = OpenParsed(dir);
            Assert.IsFalse(m.Broken, AllMessages(m));
            Assert.AreEqual((id, 3f), ModContent.Parse(m).blueprints[0].drops[0]);
        }

        [Test]
        public void ADerelictCanBePlaced()
        {
            WriteMod("d", "t_place", "1.0", "", ("blueprints.json",
                "[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'derelict': { 'race': 'nivelian', 'stations': [ 78, '79' ], 'position': [ 1000, -2000, 30000.5 ] } }," +
                "  { 'id': 'b', 'item': 3, 'ingredients': [ { 'item': 155 } ], 'derelict': { } } ]"));
            ScanAndEnable("t_place");
            Assert.IsFalse(Installed("t_place").Broken, AllMessages(Installed("t_place")));
            Database.Load();
            var a = ModBlueprints.Of(2);
            Assert.AreEqual(100f, a.derelictChance, "in named orbits the chance defaults to always");
            CollectionAssert.AreEqual(new[] { 78, 79 }, a.derelictStations);
            Assert.AreEqual(new UnityEngine.Vector3(1000f, -2000f, 30000.5f), a.derelictPosition);
            Assert.IsTrue(ModBlueprints.DerelictAllowedAt(a, 78));
            Assert.IsFalse(ModBlueprints.DerelictAllowedAt(a, 80));
            var b = ModBlueprints.Of(3);
            Assert.AreEqual(5f, b.derelictChance, "anywhere: 5 % per visit");
            Assert.IsNull(b.derelictPosition);
            Assert.IsTrue(ModBlueprints.DerelictAllowedAt(b, 80));
            Assert.AreEqual("a", ModBlueprints.RollDerelict(78)?.localId, "pinned at 100 %: always there");
            for (int i = 0; i < 50; i++) Assert.AreNotEqual("a", ModBlueprints.RollDerelict(80)?.localId, "never outside its orbits");
        }

        [Test]
        public void ADerelictInUnknownOrbitsIsNowhere()
        {
            WriteMod("d", "t_nowhere", "1.0", "", ("blueprints.json",
                "[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'derelict': { 'stations': [ 'nobody:here' ], 'chance': 50 } } ]"));
            ScanAndEnable("t_nowhere");
            Database.Load();
            var m = Installed("t_nowhere");
            Assert.IsTrue(m.Warnings.Exists(w => w.Contains("nobody:here is no station")), AllMessages(m));
            Assert.AreEqual(0f, ModBlueprints.Of(2).derelictChance);
        }

        [Test]
        public void ALoungeSellerCanStandAtFixedStations()
        {
            WriteMod("d", "t_seller", "1.0", "", ("blueprints.json",
                "[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'lounge': { 'stations': [ 78, 'nobody:here' ], 'price': 49999 } }," +
                "  { 'id': 'b', 'item': 3, 'ingredients': [ { 'item': 155 } ], 'lounge': { 'systemRace': 1 } }," +
                "  { 'id': 'c', 'item': 4, 'ingredients': [ { 'item': 155 } ], 'lounge': { 'stations': [ 'nobody:here' ] } } ]"));
            ScanAndEnable("t_seller");
            Database.Load();
            var m = Installed("t_seller");
            Assert.IsTrue(m.Warnings.Exists(w => w.Contains("nobody:here is no station")), AllMessages(m));
            var a = ModBlueprints.Of(2);
            Assert.AreEqual(100f, a.loungeChance, "at named stations always there, like the game's own sellers");
            CollectionAssert.AreEqual(new[] { 78 }, a.loungeStations);
            Assert.IsTrue(ModBlueprints.LoungeAllowedAt(a, 78, 3));
            Assert.IsFalse(ModBlueprints.LoungeAllowedAt(a, 79, 3), "not at another station");
            var b = ModBlueprints.Of(3);
            Assert.AreEqual(10f, b.loungeChance, "anywhere: 10 % per bar");
            Assert.IsTrue(ModBlueprints.LoungeAllowedAt(b, 5, 1));
            Assert.IsFalse(ModBlueprints.LoungeAllowedAt(b, 5, 0), "its systemRace only");
            Assert.AreEqual(0f, ModBlueprints.Of(4).loungeChance, "every station unknown: nowhere");
        }

        [Test]
        public void ANamedSellerStandsInTheirBarWithTheirOwnFaceAndLines()
        {
            WriteMod("d", "t_named", "1.0", "", ("blueprints.json",
                "[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'available': 'campaign >= 84', 'lounge': { 'stations': [ 10 ], 'price': 49999," +
                "    'seller': { 'name': 'Khador', 'speaker': 20, 'race': 7, 'offer': 'For you, #C: #N.', 'thanks': 'Most kind.', 'known': 'Hello again.' } } } ]"));
            ScanAndEnable("t_named");
            var db = Database.Load();
            var seller = ModBlueprints.Of(2).seller;
            Assert.AreEqual("Khador", seller.name);

            Session.CampaignMission = 80;
            Assert.IsFalse(AgentGenerator.CreateAgents(db, 10).Exists(x => x.offer == AgentOffer.SellBlueprint && x.sellBlueprint == 2), "before the step");
            var kept = AgentGenerator.CreateAgents(db, 10);

            Session.CampaignMission = 90;
            AgentGenerator.AddNamedSellers(db, 78, kept);
            Assert.IsFalse(kept.Exists(x => x.sellBlueprint == 2 && x.offer == AgentOffer.SellBlueprint), "only in their own bar");
            AgentGenerator.AddNamedSellers(db, 10, kept);
            var khador = kept.Find(x => x.offer == AgentOffer.SellBlueprint && x.sellBlueprint == 2);
            Assert.IsNotNull(khador, "joins the visitors a bar kept");
            Assert.AreEqual("Khador", khador.name);
            Assert.AreEqual(7, khador.race);
            CollectionAssert.AreEqual(StoryTable.Portrait(20), khador.portrait);
            Assert.AreEqual(49999, khador.sellPrice);
            AgentGenerator.AddNamedSellers(db, 10, kept);
            Assert.AreEqual(1, kept.Count(x => x.offer == AgentOffer.SellBlueprint && x.sellBlueprint == 2), "once");

            Assert.AreEqual("Khador", LoungeChat.Plate(khador).name, "known on sight");
            var chat = new LoungeChat(db, khador, 10, null);
            chat.Start();
            StringAssert.StartsWith("For you, ", chat.Text);
            StringAssert.DoesNotContain("#C", chat.Text);
            StringAssert.DoesNotContain("#N", chat.Text);

            Session.UnlockedBlueprints.Add(2);
            chat = new LoungeChat(db, khador, 10, null);
            chat.Start();
            Assert.AreEqual("Hello again.", chat.Text, "the blueprint learnt: his own line, not 858");
        }

        [Test]
        public void AShipTypeDropsItsOwnBlueprint()
        {
            WriteMod("d", "t_boss", "1.0", "", ("blueprints.json",
                "[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'drops': [ { 'ship': 28, 'chance': 100 }, { 'ship': 'nobody:here' } ] } ]"));
            ScanAndEnable("t_boss");
            Database.Load();
            var m = Installed("t_boss");
            Assert.IsFalse(m.Broken, AllMessages(m));
            Assert.IsTrue(m.Warnings.Exists(w => w.Contains("nobody:here is no ship")), AllMessages(m));
            var a = ModBlueprints.Of(2);
            CollectionAssert.AreEqual(new[] { (28, 100f) }, a.shipDrops);
            Assert.AreEqual(0, a.drops.Count, "a ship drop isn't a race drop");
            Assert.AreEqual(2, ModBlueprints.RollDrop(0, 28), "the Veteran always drops it");
            for (int i = 0; i < 50; i++) Assert.AreEqual(-1, ModBlueprints.RollDrop(0, 27), "another ship of its race never does");
            Blueprints.Unlock(2);
            Assert.AreEqual(-1, ModBlueprints.RollDrop(0, 28), "known: no more drops");
        }

        [Test] public void ADerelictPositionIsThreeNumbers() => StringAssert.Contains("\"position\" is [ x, y, z ]", ParseError("[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'derelict': { 'position': [ 1, 2 ] } } ]"));

        [Test]
        public void AnAutocompletePriceIsNeverNegative()
        {
            string dir = WriteMod("d", "t_neg", "1.0", "", ("blueprints.json", "[ { 'id': 'a', 'item': 2, 'ingredients': [ { 'item': 155 } ], 'autocomplete': -5 } ]"));
            var bp = ModContent.Parse(OpenParsed(dir)).blueprints[0];
            Assert.AreEqual(0, bp.autocompletePrice);
            Assert.IsTrue(bp.autocomplete);
        }
    }
}
