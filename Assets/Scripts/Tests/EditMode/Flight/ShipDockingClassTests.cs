using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ShipDockingClassTests
    {
        [TestCase("fighter")]
        [TestCase("interceptor")]
        [TestCase("bomber")]
        public void FighterRolesUseInternalDockingOnly(string shipClass)
        {
            var ship = new ShipData { shipClass = shipClass };

            Assert.IsTrue(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }

        [TestCase("frigate")]
        [TestCase("cruiser")]
        [TestCase("carrier")]
        public void CapitalRolesUseExternalDockingOnly(string shipClass)
        {
            var ship = new ShipData { shipClass = shipClass };

            Assert.IsFalse(ShipClassRules.CanDockInternally(ship, true));
            Assert.IsFalse(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsTrue(ShipClassRules.CanDockExternally(ship));
        }

        [Test]
        public void ClassTagsCanDeclareDockingRoleForModSpecificClasses()
        {
            var ship = new ShipData
            {
                shipClass = "my_mod:heavy_hull",
                classTags = new[] { "cruiser", "capital" }
            };

            Assert.IsFalse(ShipClassRules.CanDockInternally(ship, true));
            Assert.IsTrue(ShipClassRules.CanDockExternally(ship));
        }

        [Test]
        public void UnclassifiedSmallShipsKeepLegacyInternalDockingButCannotDockExternally()
        {
            var ship = new ShipData();

            Assert.IsTrue(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }

        [Test]
        public void UnclassifiedLargeShipsCannotUseEitherDockingRoute()
        {
            var ship = new ShipData();

            Assert.IsFalse(ShipClassRules.CanDockInternally(ship, true));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }

        [Test]
        public void ConflictingInternalAndExternalRoleTagsAreRejected()
        {
            var ship = new ShipData
            {
                shipClass = "custom_hull",
                classTags = new[] { "fighter", "cruiser" }
            };

            Assert.IsFalse(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }

        [Test]
        public void UnknownSmallClassKeepsLegacyInternalDockingOnly()
        {
            var ship = new ShipData { shipClass = "scout" };

            Assert.IsTrue(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }
    }
}
