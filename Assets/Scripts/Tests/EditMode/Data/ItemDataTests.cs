// ItemDataTests.cs
// CLAUDE.md "Data" (text ids: item name 1274 + index, description 1041 + index, category 221 + categoryId), "Shop" (icons
// Resources/GoF2Icons item_XXX, 180 x 88: frame + icon) and "Weapons" (one WeaponFx per weapon item in
// Resources/GoF2Weapons). Expected names and category words: items.json's own name / category fields (exported from
// items.bin, independent of the text table that GameNames / ItemInfo read). Also GameNames.Credits (Layout::formatCredits:
// thousands separated, "$" after, "," in English and "." in the other languages).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ItemDataTests : SandboxTest
    {
        DataUiTestKit.TextLanguage english;

        [SetUp] public void English() => english = new DataUiTestKit.TextLanguage("en");
        [TearDown] public void Back() => english?.Dispose();

        /// <summary>The item categories the original has (the sort, Item+0x8): 0..29 and 33..43 (30-32 unused).</summary>
        static bool KnownCategory(int c) => c >= 0 && c <= 29 || c >= 33 && c <= 43;

        [Test]
        public void EveryItemHasItsNameAndDescription()
        {
            for (int i = 0; i < GameNames.OriginalItems; i++)
            {
                var it = Db.Items[i];
                string name = GameNames.Item(i);
                Assert.IsFalse(string.IsNullOrWhiteSpace(name) || name.StartsWith("#"), $"item {i} name");
                Assert.AreEqual(it.name, name, $"item {i}: text 1274 + {i} against items.json");
                Assert.AreEqual(name, ItemInfo.ItemName(i));
                string desc = GameNames.ItemDescription(i);
                Assert.IsFalse(string.IsNullOrWhiteSpace(desc) || desc.StartsWith("#"), $"item {i} description");
                // (Localization.Load turns the table's \r\n into \n)
                Assert.AreEqual(it.description.Replace("\r\n", "\n"), desc, $"item {i}: text 1041 + {i} against items.json");
            }
        }

        [Test]
        public void ItemTextsOutsideTheTablesAreNotBorrowed()
        {
            // The text blocks sit back to back: a number past the table would read another block's (GameNames).
            Assert.AreEqual("#item300", GameNames.Item(300));
            Assert.AreEqual("#item-1", GameNames.Item(-1));
            Assert.AreEqual("", GameNames.ItemDescription(300));
            Assert.AreEqual("#ship64", GameNames.Ship(64));
            Assert.AreEqual("", GameNames.ShipDescription(64));
        }

        [Test]
        public void EveryItemHasAValidCategory()
        {
            for (int i = 0; i < GameNames.OriginalItems; i++)
            {
                var it = Db.Items[i];
                Assert.IsTrue(KnownCategory(it.categoryId), $"item {i}: category {it.categoryId}");
                Assert.AreEqual(it.category, Localization.Get(221 + it.categoryId), $"item {i}: text 221 + {it.categoryId}");
                Assert.AreEqual(it.category, ItemInfo.Category(it), $"item {i}: ItemInfo.Category");
            }
            Assert.AreEqual("", ItemInfo.Category(null));
        }

        [Test]
        public void CategoriesBelongToOneItemType()
        {
            // A category is one kind of slot: every Laser is a primary, every Turret a turret... (Shop / Hangar mount by it).
            var typeOf = new Dictionary<int, string>();
            for (int i = 0; i < GameNames.OriginalItems; i++)
            {
                var it = Db.Items[i];
                if (typeOf.TryGetValue(it.categoryId, out var t)) Assert.AreEqual(t, it.type, $"item {i} ({it.category})");
                else typeOf[it.categoryId] = it.type;
            }
            Assert.AreEqual("primary", typeOf[0]);     // Laser
            Assert.AreEqual("secondary", typeOf[4]);   // Rocket
            Assert.AreEqual("turret", typeOf[8]);      // Turret
            Assert.AreEqual("equipment", typeOf[9]);   // Shield
            Assert.AreEqual("commodity", typeOf[22]);  // Commodity
            Assert.AreEqual("equipment", typeOf[10]);  // Armor ("sort 10 = armor", CLAUDE.md "Story")
            Assert.AreEqual("equipment", typeOf[19]);  // Mining laser (the drill, CLAUDE.md "Mining": category 19)
            Assert.AreEqual("equipment", typeOf[14]);  // Booster (FlightModel: category 14)
        }

        [Test]
        public void EveryItemHasAShopIcon()
        {
            for (int i = 0; i < GameNames.OriginalItems; i++)
            {
                var icon = ItemInfo.ItemIcon(i);
                Assert.IsNotNull(icon, $"GoF2Icons/item_{i:000}");
                Assert.AreEqual(180, icon.width, $"item {i} icon width");
                Assert.AreEqual(88, icon.height, $"item {i} icon height");
            }
        }

        static bool IsWeapon(ItemData it) => it.type == "primary" || it.type == "secondary" || it.type == "turret";

        [Test]
        public void EveryWeaponHasItsFx()
        {
            int weapons = 0;
            for (int i = 0; i < GameNames.OriginalItems; i++)
            {
                var it = Db.Items[i];
                if (!IsWeapon(it)) continue;
                weapons++;
                var fx = WeaponFx.Load(i);
                Assert.IsNotNull(fx, $"GoF2Weapons/item_{i:000} ({it.name})");
                Assert.AreEqual(i, fx.item, $"item {i}'s fx names its item");
                if (it.type == "turret") Assert.IsNotNull(fx.turretMounted, $"turret {i} ({it.name}): the ship-mounted model");
                if (it.categoryId == 39) Assert.IsNotNull(fx.sentry, $"sentry gun {i}: the deployed object");
                if (it.type == "primary")
                {
                    Assert.IsNotNull(fx.projectile, $"primary {i} ({it.name}): projectile");
                    Assert.IsNotNull(fx.Shot, $"primary {i} ({it.name}): shot sound");
                }
            }
            Assert.AreEqual(80, weapons, "40 primaries, 30 secondaries, 10 turrets");
        }

        [Test]
        public void OnlyWeaponsHaveFx()
        {
            for (int i = 0; i < GameNames.OriginalItems; i++)
                if (!IsWeapon(Db.Items[i]))
                    Assert.IsNull(UnityEngine.Resources.Load<WeaponFx>($"{WeaponFx.ResourcesFolder}/item_{i:000}"), $"item {i} isn't a weapon");
        }

        [Test]
        public void CreditsAreFormattedLikeTheOriginal()
        {
            Assert.AreEqual("0$", GameNames.Credits(0));
            Assert.AreEqual("999$", GameNames.Credits(999));
            Assert.AreEqual("1,800$", GameNames.Credits(1800));
            Assert.AreEqual("30,000,001$", GameNames.Credits(30000001));
            Assert.AreEqual("-20,000$", GameNames.Credits(-20000));
            Assert.AreEqual("1,800$", ItemInfo.Credits(1800));
        }

        [Test]
        public void CreditsUseDotsOutsideEnglish()
        {
            using (new DataUiTestKit.TextLanguage("de"))
            {
                Assert.AreEqual("de", Localization.Language);
                Assert.AreEqual("1.800$", GameNames.Credits(1800));
                Assert.AreEqual("2.500.000$", GameNames.Credits(2500000));
            }
            Assert.AreEqual("en", Localization.Language);
        }
    }
}
