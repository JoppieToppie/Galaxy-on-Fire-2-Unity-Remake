using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModItemAvailabilityTests : SavesModsTest
    {
        [Test]
        public void NoModRestrictionKeepsOriginalItemAvailable()
        {
            Database.Load();
            Assert.IsTrue(ModContent.ItemAvailableFor(85, "shops"));
            Assert.IsTrue(ModContent.ItemAvailableFor(85, "equipment"));
            Assert.IsTrue(ModContent.ItemAvailableFor(85, "loot"));
        }

        [Test]
        public void AvailabilityOnlyOverrideDoesNotRequireARename()
        {
            WriteMod("restricted equipment", "t_restriction", "1.0", "",
                ("items.json", @"[
                    { 'override': 85, 'availability': {
                        'shops': false, 'equipment': false, 'loot': false, 'blueprints': false
                    } }
                ]"));
            ScanAndEnable("t_restriction");
            Assert.IsFalse(Installed("t_restriction").Broken, AllMessages(Installed("t_restriction")));
            Database.Load();
            Assert.IsFalse(ModContent.ItemAvailableFor(85, "shops"));
            Assert.IsFalse(ModContent.ItemAvailableFor(85, "equipment"));
            Assert.IsFalse(ModContent.ItemAvailableFor(85, "loot"));
            Assert.IsFalse(ModContent.ItemAvailableFor(85, "blueprints"));
            Assert.IsTrue(ModContent.ItemAvailableFor(85, "npcCargo"),
                "Absent flags must not unexpectedly change other acquisition rules.");
            Assert.IsTrue(ModContent.ItemAvailableFor(84, "shops"),
                "An override must not suppress other items.");
        }
    }
}
