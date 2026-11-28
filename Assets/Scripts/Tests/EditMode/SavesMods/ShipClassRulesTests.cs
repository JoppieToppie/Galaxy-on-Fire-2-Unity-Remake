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
        public void CrosshairOffsetsArePerShipAndPreserveUnspecifiedAxes()
        {
            var a = new ShipData();
            var b = new ShipData();
            ModShipView.ApplyShip(a, JObject.Parse("{\"crosshairOffsetX\":125,\"crosshairOffsetY\":-80}"));
            ModShipView.ApplyShip(a, JObject.Parse("{\"crosshairOffsetX\":2000}"));
            Assert.AreEqual(new UnityEngine.Vector2(1000f, -80f), ModShipView.CrosshairOffset(a));
            Assert.AreEqual(UnityEngine.Vector2.zero, ModShipView.CrosshairOffset(b));
            Assert.AreEqual(UnityEngine.Vector2.zero, ModShipView.CrosshairOffset(a, true));
            ModShipView.ApplyShip(a, JObject.Parse("{\"crosshairOffsetX\":0}"));
            Assert.AreEqual(new UnityEngine.Vector2(0f, -80f), ModShipView.CrosshairOffset(a));
        }

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

        [Test]
        public void ReservedSecondarySlotsAreExclusiveInBothDirections()
        {
            var hull = Ship("frigate", "secondarySlot:7:test:salvo", "secondarySlot:8:test:salvo");
            var special = new ItemData { type = "secondary", modKey = "test:salvo" };
            var ordinary = new ItemData { type = "secondary", modKey = "test:missile" };
            Assert.IsTrue(ShipClassRules.HasSecondaryReservations(hull));
            Assert.IsTrue(ShipClassRules.SecondarySlotFits(special, hull, 7));
            Assert.IsTrue(ShipClassRules.SecondarySlotFits(special, hull, 8));
            Assert.IsFalse(ShipClassRules.SecondarySlotFits(special, hull, 6));
            Assert.IsFalse(ShipClassRules.SecondarySlotFits(ordinary, hull, 7));
            Assert.IsTrue(ShipClassRules.SecondarySlotFits(ordinary, hull, 6));
            Assert.IsTrue(ShipClassRules.SecondarySlotFits(ordinary, Ship("frigate"), 7));
        }

        [Test]
        public void MultiplePrimaryLocksAreExplicitAlternatives()
        {
            var hull = Ship("cruiser", "primaryOnly:test:beam", "primaryOnly:test:cannon");
            Assert.IsTrue(ShipClassRules.Fits(new ItemData { type = "primary", modKey = "test:beam" }, hull));
            Assert.IsTrue(ShipClassRules.Fits(new ItemData { type = "primary", modKey = "test:cannon" }, hull));
            Assert.IsFalse(ShipClassRules.Fits(new ItemData { type = "primary", modKey = "test:other" }, hull));
        }

        [Test]
        public void PrimaryLockDoesNotRestrictSecondaries()
        {
            var hull = Ship("cruiser", "primaryOnly:test:beam");
            Assert.IsTrue(ShipClassRules.Fits(new ItemData { type = "primary", modKey = "test:beam" }, hull));
            Assert.IsFalse(ShipClassRules.Fits(new ItemData { type = "primary", modKey = "test:gun" }, hull));
            Assert.IsTrue(ShipClassRules.Fits(new ItemData { type = "secondary", modKey = "test:missile" }, hull));
        }

        [Test]
        public void StrictAllowListAlsoChecksUnclassifiedShips()
        {
            var item = Item(new[] { "cruiser" });
            item.attrKeys = new[] { ItemStats.IdOf("strictShipClass") };
            item.attrValues = new[] { 1 };
            Assert.IsFalse(ShipClassRules.Fits(item, new ShipData()));
            Assert.IsTrue(ShipClassRules.Fits(item, Ship("cruiser")));
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
        [Test]
        public void ClassDockingAndCameraPolicyDoesNotChangeVanillaShips()
        {
            foreach (string id in ShipClassRules.CanonicalClassIds)
            {
                var ship = new ShipData { shipClass = id, dockingRole = "external" };
                bool large = id == ShipClassRules.Frigate || id == ShipClassRules.Freighter ||
                    id == ShipClassRules.Cruiser || id == ShipClassRules.Carrier;
                Assert.AreEqual(large, ExternalDockingRules.CanDockExternally(ship), id);
                Assert.AreEqual(!large, ExternalDockingRules.CanDockInternally(ship, false), id);
                Assert.AreEqual(!large, ExternalDockingRules.CanDockInternally(ship, true), id);
                Assert.AreEqual(!large, ExternalDockingRules.CanDockInternally(ship), id);
                Assert.AreEqual(large, ModShipView.FreeLookTargeting(ship, !large), id);
                if (large) Assert.GreaterOrEqual(ModShipView.OrbitDistance(ship), 50000f, id);
            }
            var vanilla = new ShipData { dockingRole = "external" };
            Assert.IsFalse(ExternalDockingRules.CanDockExternally(vanilla));
            Assert.IsTrue(ExternalDockingRules.CanDockInternally(vanilla, false));
            Assert.IsTrue(ExternalDockingRules.CanDockInternally(vanilla, true));
            Assert.IsTrue(ExternalDockingRules.CanDockInternally(null));
            Assert.IsFalse(ExternalDockingRules.CanDockInternally(new ShipData { shipClass = ShipClassRules.Cruiser }));
            Assert.IsTrue(ExternalDockingRules.CanDockInternally(new ShipData { dockingRole = "internal" }));
            Assert.IsTrue(ModShipView.FreeLookTargeting(vanilla, true));
            Assert.IsFalse(ModShipView.FreeLookTargeting(vanilla, false));
        }

    }
}
