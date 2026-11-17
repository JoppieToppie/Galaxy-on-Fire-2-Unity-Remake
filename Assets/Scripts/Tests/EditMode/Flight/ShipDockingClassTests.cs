using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ShipDockingClassTests
    {
        [Test]
        public void DatabaseReturnsEveryShipAuthoredExternalDockingHardpoint()
        {
            var db = new Database
            {
                WeaponMounts = new List<WeaponMountSet>
                {
                    new WeaponMountSet
                    {
                        ship = 901,
                        mounts = new List<WeaponMount>
                        {
                            new WeaponMount { slotType = 0, position_engine = new[] { 0, 0, 100 } },
                            new WeaponMount { slotType = 8, position_engine = new[] { 100, 200, 300 }, rotation = new[] { 0f, 90f, 0f } },
                            new WeaponMount { slotType = 8, position_engine = new[] { -100, 200, 300 }, rotation = new[] { 0f, -90f, 0f } }
                        }
                    }
                }
            };

            var mounts = db.MountsOf(901, 8);

            Assert.AreEqual(2, mounts.Count);
            Assert.AreEqual(100, mounts[0].position_engine[0]);
            Assert.AreEqual(-100, mounts[1].position_engine[0]);
        }

        [Test]
        public void BaseShipClassCatalogDefinesAllFourteenClassesWithoutAssigningThemToShips()
        {
            Assert.AreEqual(14, ShipClassCatalog.All.Count);
            Assert.IsNull(ShipClassRules.ClassOf(new ShipData()));
            Assert.AreEqual("light_fighter", ShipClassCatalog.CanonicalId("LIGHT_FIGHTER"));
            Assert.AreEqual("Light Fighter", ShipClassCatalog.Get("light_fighter").DisplayName);
            Assert.AreEqual("my_mod:gunship", ShipClassCatalog.CanonicalId("my_mod:gunship"));
        }

        [TestCase("light_fighter")]
        [TestCase("heavy_fighter")]
        [TestCase("light_bomber")]
        [TestCase("heavy_bomber")]
        [TestCase("interceptor")]
        [TestCase("stealth_bomber")]
        public void BaseFighterAndBomberClassesDefaultToInternalDocking(string classId)
        {
            var ship = new ShipData { shipClass = classId };
            Assert.IsTrue(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }

        [TestCase("frigate")]
        [TestCase("cruiser")]
        [TestCase("carrier")]
        public void BaseCapitalClassesDefaultToExternalDocking(string classId)
        {
            var ship = new ShipData { shipClass = classId };
            Assert.IsFalse(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsTrue(ShipClassRules.CanDockExternally(ship));
        }

        [TestCase("hauler")]
        [TestCase("light_privateer")]
        [TestCase("privateer")]
        [TestCase("heavy_privateer")]
        [TestCase("freighter")]
        public void UtilityAndPrivateerClassesKeepSizeBasedDocking(string classId)
        {
            var ship = new ShipData { shipClass = classId };
            Assert.IsTrue(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }

        [TestCase("fighter")]
        [TestCase("interceptor")]
        [TestCase("bomber")]
        public void FighterRolesUseInternalDockingOnly(string shipClass)
        {
            var ship = new ShipData { shipClass = shipClass };

            Assert.IsTrue(ShipClassRules.CanDockInternally(ship, false));
            Assert.IsFalse(ShipClassRules.CanDockExternally(ship));
        }

        [Test]
        public void InternalRoleDoesNotOverrideThePhysicalLargeHullClearance()
        {
            var ship = new ShipData { shipClass = "fighter" };

            Assert.IsFalse(ShipClassRules.CanDockInternally(ship, true));
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
