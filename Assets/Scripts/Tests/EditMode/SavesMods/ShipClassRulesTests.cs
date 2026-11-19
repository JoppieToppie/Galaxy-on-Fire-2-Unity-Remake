// ShipClassRulesTests.cs
// Feature: Modding/README.md "Ship classes" (ships.json "shipClass" / "classTags" / "className", items.json "allowedClasses" /
// "excludedClasses", stats "scanClass"; AI_PROJECT_CONTEXT.md 6.1 / 6.2). Optional everywhere: an unclassified ship (no class, no
// tags: every original one) takes every item, even one with an allow-list, and an item without a list fits every ship. On a
// classified ship an exclude-list blocks what it names, an allow-list refuses what it doesn't name; both together need a match
// in the allow-list and none in the exclude-list; words compare without regard to case.

using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ShipClassRulesTests
    {
        [Test]
        public void CanonicalTaxonomyContainsExactlySixteenClasses()
        {
            Assert.AreEqual(16, ShipClassRules.CanonicalClassIds.Length);
            Assert.IsTrue(ShipClassRules.IsCanonicalClass("stealth_fighter"));
            Assert.IsTrue(ShipClassRules.IsCanonicalClass("scout"));
            Assert.IsTrue(ShipClassRules.IsCanonicalClass("cruiser"));
            Assert.IsFalse(ShipClassRules.IsCanonicalClass("gunship"));
        }

        static ShipData Ship(string cls = null, params string[] tags) =>
            new ShipData { shipClass = cls, classTags = tags.Length > 0 ? tags : null };

        static ItemData Item(string[] allowed = null, string[] excluded = null) =>
            new ItemData { allowedClasses = allowed, excludedClasses = excluded };

        // ---- nothing set: the original game ---------------------------------------------------------------------------

        [Test]
        public void AnItemWithoutListsFitsEveryShip()
        {
            var item = Item();
            Assert.IsTrue(ShipClassRules.Fits(item, new ShipData()));
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("frigate", "large")));
            Assert.IsTrue(ShipClassRules.Fits(item, null));
        }

        [Test]
        public void EmptyListsAreNoRestriction()
        {
            var item = Item(new string[0], new string[0]);
            Assert.IsTrue(ShipClassRules.Fits(item, new ShipData()));
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("fighter")));
        }

        [Test]
        public void NoItemFitsAnything() => Assert.IsTrue(ShipClassRules.Fits(null, Ship("fighter")));

        // ---- exclude-list ---------------------------------------------------------------------------------------------

        [Test]
        public void AnExcludeListBlocksAShipByItsClass()
        {
            var item = Item(excluded: new[] { "light_fighter" });
            Assert.IsFalse(ShipClassRules.Fits(item, Ship("light_fighter")));
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("frigate")));
        }

        [Test]
        public void AnExcludeListBlocksAShipByATag()
        {
            // another mod's class carrying the shared tag is excluded without that mod being named
            var item = Item(excluded: new[] { "small" });
            Assert.IsFalse(ShipClassRules.Fits(item, Ship("drone", "small")));
            Assert.IsFalse(ShipClassRules.Fits(item, Ship("light_fighter", "agile", "small")));
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("destroyer", "large")));
        }

        [Test]
        public void AnExcludeListLeavesAShipWithoutAClassAlone()
        {
            var item = Item(excluded: new[] { "small" });
            Assert.IsTrue(ShipClassRules.Fits(item, new ShipData()));
            Assert.IsTrue(ShipClassRules.Fits(item, null));
        }

        // ---- allow-list -----------------------------------------------------------------------------------------------

        [Test]
        public void AnAllowListTakesAShipByClassOrTag()
        {
            var item = Item(new[] { "frigate", "capital" });
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("frigate")));
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("cruiser", "capital")));
            Assert.IsFalse(ShipClassRules.Fits(item, Ship("fighter")));
        }

        [Test]
        public void AnAllowListTakesAnUnclassifiedShip()
        {
            // the lists engage only on ships a mod classified: every original loadout stays valid
            var item = Item(new[] { "capital" });
            Assert.IsTrue(ShipClassRules.Fits(item, new ShipData()));
            Assert.IsTrue(ShipClassRules.Fits(item, null));
            Assert.IsTrue(ShipClassRules.Fits(Item(new[] { "capital" }, new[] { "small" }), new ShipData()));
        }

        [Test]
        public void AShipWithOnlyTagsIsClassified()
        {
            var item = Item(new[] { "capital" });
            Assert.IsTrue(ShipClassRules.IsClassified(Ship(null, "small")));
            Assert.IsFalse(ShipClassRules.Fits(item, Ship(null, "small")));   // a tag that isn't on the allow-list
            Assert.IsTrue(ShipClassRules.Fits(item, Ship(null, "capital")));
            Assert.IsFalse(ShipClassRules.IsClassified(new ShipData()));
            Assert.IsFalse(ShipClassRules.IsClassified(null));
        }

        [Test]
        public void WithBothListsAShipNeedsTheAllowAndNotTheExclude()
        {
            var item = Item(new[] { "frigate" }, new[] { "stealth" });
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("frigate")));
            Assert.IsFalse(ShipClassRules.Fits(item, Ship("frigate", "stealth")));
            Assert.IsFalse(ShipClassRules.Fits(item, Ship("fighter")));
        }

        [Test]
        public void WordsCompareWithoutRegardToCase()
        {
            Assert.IsTrue(ShipClassRules.Fits(Item(new[] { "Frigate" }), Ship("frigate")));
            Assert.IsFalse(ShipClassRules.Fits(Item(excluded: new[] { "SMALL" }), Ship("drone", "small")));
        }

        [Test]
        public void ModPrefixedTagsAreJustWords()
        {
            var item = Item(excluded: new[] { "mymod:agile" });
            Assert.IsFalse(ShipClassRules.Fits(item, Ship("fighter", "mymod:agile")));
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("fighter", "agile")));
        }

        // ---- the scanner's view ---------------------------------------------------------------------------------------

        [Test]
        public void TheClassOfAShipIsItsClassWord()
        {
            Assert.AreEqual("light_fighter", ShipClassRules.ClassOf(Ship("light_fighter", "small")));
            Assert.IsNull(ShipClassRules.ClassOf(Ship(null, "small")));   // tags alone are not a class
            Assert.IsNull(ShipClassRules.ClassOf(new ShipData()));
            Assert.IsNull(ShipClassRules.ClassOf(null));
        }

        [Test]
        public void TheScanAttributeNameMapsTo143()
        {
            Assert.AreEqual(143, ItemStats.IdOf("scanClass"));
            Assert.AreEqual(143, ShipClassRules.ScanClassAttr);
        }

        [Test]
        public void TheClassLabelIsTheModsTextElseTheCapitalisedId()
        {
            Assert.AreEqual("Light Fighter", ShipClassRules.ClassLabel(Ship("light_fighter")));
            Assert.AreEqual("Light Fighter", ShipClassRules.ClassLabel(Ship("mymod:light-fighter")));   // a mod prefix is left off
            Assert.AreEqual("Leichter Jaeger", ShipClassRules.ClassLabel(new ShipData { shipClass = "light_fighter", shipClassLabel = "Leichter Jaeger" }));
            Assert.IsNull(ShipClassRules.ClassLabel(Ship(null, "small")));
            Assert.IsNull(ShipClassRules.ClassLabel(null));
        }

        [Test]
        public void AClassNameInTheJsonBecomesTheLabelWhileTheIdStaysTheIdentity()
        {
            var s = new ShipData();
            ShipClassRules.ApplyShip(s, JObject.Parse("{ 'shipClass': 'light_fighter', 'className': 'Light fighter' }"));
            Assert.AreEqual("light_fighter", s.shipClass);
            Assert.AreEqual("Light fighter", ShipClassRules.ClassLabel(s));
            Assert.IsTrue(ShipClassRules.Matches(s, new[] { "LIGHT_FIGHTER" }));   // restrictions match the id, never the label
            Assert.IsFalse(ShipClassRules.Matches(s, new[] { "Light fighter" }));
        }

        // ---- ships.json / items.json ----------------------------------------------------------------------------------

        [Test]
        public void AShipReadsItsClassAndTags()
        {
            var s = new ShipData();
            ShipClassRules.ApplyShip(s, JObject.Parse("{ 'shipClass': ' light_fighter ', 'classTags': ['small', '', ' agile '] }"));
            Assert.AreEqual("light_fighter", s.shipClass);
            CollectionAssert.AreEqual(new[] { "small", "agile" }, s.classTags);
        }

        [Test]
        public void AnOverrideWithoutTheFieldsKeepsWhatAnEarlierModGave()
        {
            var s = Ship("fighter", "small");
            ShipClassRules.ApplyShip(s, JObject.Parse("{ 'armor': 50 }"));
            Assert.AreEqual("fighter", s.shipClass);
            CollectionAssert.AreEqual(new[] { "small" }, s.classTags);
        }

        [Test]
        public void AnEmptyClassAndEmptyTagsClearThem()
        {
            var s = Ship("fighter", "small");
            ShipClassRules.ApplyShip(s, JObject.Parse("{ 'shipClass': '', 'classTags': [] }"));
            Assert.IsNull(s.shipClass);
            Assert.IsNull(s.classTags);
        }

        [Test]
        public void AnItemReadsItsListsAsAWordOrAList()
        {
            var it = new ItemData();
            ShipClassRules.ApplyItem(it, JObject.Parse("{ 'allowedClasses': 'capital', 'excludedClasses': ['small', 'drone'] }"));
            CollectionAssert.AreEqual(new[] { "capital" }, it.allowedClasses);
            CollectionAssert.AreEqual(new[] { "small", "drone" }, it.excludedClasses);
        }

        [Test]
        public void AnItemOverrideWithoutListsKeepsTheirs_AndAnEmptyListLiftsTheRestriction()
        {
            var it = Item(new[] { "capital" }, new[] { "small" });
            ShipClassRules.ApplyItem(it, JObject.Parse("{ 'techLevel': 3 }"));
            CollectionAssert.AreEqual(new[] { "capital" }, it.allowedClasses);
            ShipClassRules.ApplyItem(it, JObject.Parse("{ 'allowedClasses': [], 'excludedClasses': [] }"));
            Assert.IsNull(it.allowedClasses);
            Assert.IsNull(it.excludedClasses);
        }

        [Test]
        public void AWrongShapeIsAnError()
        {
            Assert.Throws<ModJsonException>(() => ShipClassRules.CheckShip(JObject.Parse("{ 'shipClass': 5 }"), "ships.json", null, "ships.json"));
            Assert.Throws<ModJsonException>(() => ShipClassRules.CheckShip(JObject.Parse("{ 'classTags': [1, 2] }"), "ships.json", null, "ships.json"));
            Assert.Throws<ModJsonException>(() => ShipClassRules.CheckItem(JObject.Parse("{ 'allowedClasses': { 'a': 1 } }"), "items.json", null, "items.json"));
            Assert.DoesNotThrow(() => ShipClassRules.CheckItem(JObject.Parse("{ 'allowedClasses': ['a'], 'excludedClasses': 'b' }"), "items.json", null, "items.json"));
        }
    }
}
