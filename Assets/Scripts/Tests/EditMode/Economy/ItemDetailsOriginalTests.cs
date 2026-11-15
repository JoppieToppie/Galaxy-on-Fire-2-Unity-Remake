using System.Linq;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    /// <summary>The item details window's numbers as the original's ListItemWindow::set computes them, read from the
    /// disassembly where the decompile drops the float math (docs/notes/shop.md "Item details numbers").</summary>
    public class ItemDetailsOriginalTests : SandboxTest
    {
        string Row(int item, int labelText) =>
            ItemInfo.ItemStats(Db.Item(item)).First(r => r.label == Data.Localization.Get(labelText)).value;

        [TestCase(33.333f, "33.33")]
        [TestCase(12f, "12.0")]
        [TestCase(12.05f, "12.5")]    // the original's quirk: the fraction's digits are not zero-padded
        [TestCase(122.22f, "122.22")]
        [TestCase(0.5f, "0.50")]
        public void DecimalsAreTheWholePartAndTwoDigitsOfTheFraction(float value, string expected)
        {
            Assert.AreEqual(expected, ItemInfo.OriginalDecimals(value));
        }

        [Test]
        public void MagnitudeIsHalved()
        {
            Assert.AreEqual(22000, Db.Item(41).Attr(14));   // EMP GL I
            Assert.AreEqual("11000 m", Row(41, 139));
        }

        [Test]
        public void RangeEndingInFiftyGoesUpToTheNextHundred()
        {
            Assert.AreEqual("1300 m", Row(20, 151));   // Ridil Blaster: 1250 before the rounding
        }

        [Test]
        public void DamagePerSecondHasTheOriginalDecimals()
        {
            Assert.AreEqual("48.0", Row(20, 188));
        }
    }
}
