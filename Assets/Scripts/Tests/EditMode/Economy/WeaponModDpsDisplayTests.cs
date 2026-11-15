using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    /// <summary>A weapon mod's damage per second change (docs/notes/shop.md "Weapon mod damage per second").</summary>
    public class WeaponModDpsDisplayTests : SandboxTest
    {
        [TestCase(0, 20, "+20 %")]
        [TestCase(20, 0, "+25 %")]
        [TestCase(20, 10, "+37.5 %")]
        [TestCase(-25, -10, "-28 %")]
        [TestCase(0, 0, "+0 %")]
        public void DpsCombinesDamageAndReducedReloadTime(int reloadBonus, int damageBonus, string expected)
        {
            var item = Db.Item(91);
            int category = item.categoryId;
            var keys = item.attrKeys;
            var values = item.attrValues;
            try
            {
                item.categoryId = 28;
                item.attrKeys = new[] { 39, 40 };
                item.attrValues = new[] { reloadBonus, damageBonus };
                Assert.AreEqual(expected, ItemInfo.ItemStats(item).Single(row => row.label == Localization.Get(188)).value);
            }
            finally { item.categoryId = category; item.attrKeys = keys; item.attrValues = values; }
        }
    }
}
