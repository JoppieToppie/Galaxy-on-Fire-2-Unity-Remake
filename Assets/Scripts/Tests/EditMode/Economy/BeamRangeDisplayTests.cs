using System.Linq;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class BeamRangeDisplayTests : SandboxTest
    {
        [TestCase(207, "1500 m")]
        [TestCase(208, "6000 m")]
        [TestCase(222, "2000 m")]
        [TestCase(223, "1000 m")]
        public void RangeLabelUsesOriginalDisplayUnitsWithoutChangingTargetingData(int item, string expected)
        {
            int targetingRange = Db.Item(item).Attr(53);
            Assert.IsTrue(ItemInfo.ItemStats(Db.Item(item)).Any(row => row.value == expected));
            Assert.AreEqual(targetingRange, Db.Item(item).Attr(53));
        }

        // Plasma collectors (attr 51) go through the same x 0.1 in the original (ListItemWindow::set). (Not the PE Proton:
        // its 27500 is the Android table's, the Default Economy's is 40000.)
        [TestCase(199, "5000 m")]
        [TestCase(200, "6000 m")]
        public void CollectorRangeUsesTheSameScale(int item, string expected)
        {
            Assert.IsTrue(ItemInfo.ItemStats(Db.Item(item)).Any(row => row.value == expected));
        }
    }
}
