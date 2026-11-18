// ShipDataTests.cs
// CLAUDE.md "Data" (ship name 913 + index), "Assembled prefabs" (one prefab per game object; Globals::getShipGroup:
// 13 the Vossk freighter, 14 the Terran battleship, 15 the freighter by race; the add-on ships v_ship_NNN_* /
// sn_ship_NNN_*), "Debug panel" (ships 0-63 with a model: 50 / 53 have none; 13 / 14 / 15 "not normally flyable"),
// weapons_hd.json (slotType 0 primary, 1 secondary, 2 turret, 3 engine exhaust) and the shop icons (180 x 88).
// Ship heights: Reference/research/station_interior.md 1.2 (DAT_00253d48, the table typed in below), the remake's lifts
// in hangar_heights.json (24 headings, never lowering a ship: CLAUDE.md "Station scene", Parking heights).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ShipDataTests : SandboxTest
    {
        /// <summary>The ships the player can own: every original ship but the freighters / battleship (13-15) and the two
        /// unused entries without a model (50, 53).</summary>
        static IEnumerable<int> OwnableShips() => Enumerable.Range(0, 64).Where(i => i != 13 && i != 14 && i != 15 && i != 50 && i != 53);

        static bool PrefabExists(AssemblyData a) => DataUiTestKit.AssembledExists(a);

        [Test]
        public void ShipNamesComeFromTheTextTable()
        {
            using (new DataUiTestKit.TextLanguage("en"))
            {
                foreach (int i in OwnableShips())
                {
                    Assert.AreEqual(Db.Ships[i].name, GameNames.Ship(i), $"ship {i}: text 913 + {i}");
                    Assert.AreNotEqual("-", GameNames.Ship(i), $"ship {i} is a real ship");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(GameNames.ShipDescription(i)), $"ship {i}: description 977 + {i}");
                }
                foreach (int i in new[] { 13, 14, 15, 50, 53 }) Assert.AreEqual("-", GameNames.Ship(i), $"ship {i} has no name");
                Assert.AreEqual("Betty", GameNames.Ship(0));
                Assert.AreEqual("Phantom", GameNames.Ship(10));
            }
        }

        [TestCaseSource(nameof(OwnableShips))]
        public void OwnableShipHasAnAssembledPrefab(int ship)
        {
            var a = Db.ShipAssembly(ship);
            Assert.IsNotNull(a, $"ship {ship}: no assembly");
            Assert.AreEqual("ships", a.category);
            string p = $"ship_{ship:000}_";
            Assert.IsTrue(a.name.StartsWith(p) || a.name.StartsWith("v_" + p) || a.name.StartsWith("sn_" + p), a.name);
            Assert.IsTrue(PrefabExists(a), "Resources/" + Visuals.AssembledObject.PrefabPath(a));
        }

        [Test]
        public void AddOnShipsUseTheirPacks()
        {
            foreach (int ship in new[] { 37, 38, 39, 40, 41 })
                StringAssert.StartsWith($"v_ship_{ship:000}_", Db.ShipAssembly(ship).name, "Valkyrie add-on ship");
            foreach (int ship in new[] { 44, 45, 46, 47, 48, 49, 51, 52, 54, 55, 56, 57, 58, 59, 60 })
                StringAssert.StartsWith($"sn_ship_{ship:000}_", Db.ShipAssembly(ship).name, "Supernova add-on ship");
        }

        [Test]
        public void FreightersAndTheBattleshipResolveLikeGetShipGroup()
        {
            Assert.AreEqual("cargo_004_vossk", Db.ShipAssembly(13)?.name, "13: the Vossk freighter");
            Assert.AreEqual("battleship_terran", Db.ShipAssembly(14)?.name, "14: the Terran battleship");
            Assert.AreEqual("cargo_003_terran", Db.ShipAssembly(15)?.name, "15: the Terran freighter");
            Assert.AreEqual("cargo_003_terran", Db.ShipAssembly(15, "terran")?.name);
            Assert.AreEqual("cargo_002_nivelian", Db.ShipAssembly(15, "nivelian")?.name);
            Assert.AreEqual("cargo_001_midorian", Db.ShipAssembly(15, "midorian")?.name);
            Assert.AreEqual("cargo_004_vossk", Db.ShipAssembly(15, "vossk")?.name, "15 of the Vossk = 13's");
            foreach (int ship in new[] { 13, 14, 15 }) Assert.IsTrue(PrefabExists(Db.ShipAssembly(ship)), "ship " + ship);
            Assert.IsNull(Db.ShipAssembly(50), "50 has no model");
            Assert.IsNull(Db.ShipAssembly(53), "53 has no model");
        }

        [TestCaseSource(nameof(OwnableShips))]
        public void OwnableShipHasWeaponMountsForItsSlots(int ship)
        {
            var s = Db.Ships[ship];
            Assert.IsNotNull(s.slots, $"ship {ship} slots");
            Assert.AreEqual(1, Db.WeaponMounts.Count(m => m.ship == ship), $"ship {ship}: one weapons_hd.json set");
            Assert.Greater(Db.MountsOf(ship, 3).Count, 0, $"ship {ship}: engine exhaust points (slot type 3)");
            if (s.slots.primary > 0) Assert.Greater(Db.MountsOf(ship, 0).Count, 0, $"ship {ship}: primary mounts");
            if (s.slots.secondary > 0) Assert.Greater(Db.MountsOf(ship, 1).Count, 0, $"ship {ship}: secondary mounts");
            if (s.slots.turret > 0) Assert.Greater(Db.MountsOf(ship, 2).Count, 0, $"ship {ship}: turret mount");
            foreach (var m in Db.WeaponMounts.First(w => w.ship == ship).mounts)
            {
                Assert.That(m.slotType, Is.InRange(0, 3), $"ship {ship} mount type");
                Assert.AreEqual(3, m.position_engine?.Length ?? 0, $"ship {ship}: mount position (x, y, z)");
            }
        }

        [Test]
        public void WeaponMountSetsNameRealShips()
        {
            var seen = new HashSet<int>();
            foreach (var set in Db.WeaponMounts)
            {
                Assert.That(set.ship, Is.InRange(0, 63), set.shipName);
                Assert.IsTrue(seen.Add(set.ship), $"ship {set.ship} twice in weapons_hd.json");
            }
        }

        [TestCaseSource(nameof(OwnableShips))]
        public void OwnableShipHasAShopIcon(int ship)
        {
            var icon = ItemInfo.ShipIcon(ship);
            Assert.IsNotNull(icon, $"GoF2Icons/ship_{ship:000}");
            Assert.AreEqual(180, icon.width);
            Assert.AreEqual(88, icon.height);
        }

        [Test]
        public void SlotsAndStatsAreSane()
        {
            foreach (int i in OwnableShips())
            {
                var s = Db.Ships[i];
                Assert.Greater(s.armor, 0, $"ship {i} hull");
                Assert.Greater(s.cargo, 0, $"ship {i} cargo");
                Assert.Greater(s.handling, 0f, $"ship {i} handling");
                Assert.AreEqual(s.handling / 100f, s.handlingMultiplier, 1e-4f, $"ship {i}: handlingMultiplier = handling / 100");
                Assert.Greater(s.slots.primary + s.slots.secondary + s.slots.turret, 0, $"ship {i} weapons");
                Assert.Greater(s.slots.equipment, 0, $"ship {i} equipment");
                Assert.That(s.price, Is.GreaterThanOrEqualTo(0), $"ship {i} price");
            }
            var economy = Session.Economy;
            try
            {
                Session.Economy = Economy.Android;   // the Default economy prices it (1 200 000)
                Assert.AreEqual(0, Database.Load().Ships[37].price, "the Cronus has no price in the Android economy (Khador's gift, CLAUDE.md \"Shop\")");
            }
            finally { Session.Economy = economy; }
        }

        /// <summary>station_interior.md 1.2: DAT_00253d48, the ship pivot's height above the hangar floor (game units).</summary>
        static readonly int[] ResearchShipY =
        {
            160, 150, 220, 310, 160, 180, 140, 170, 170, 110, 250, 205, 240, 250, 250, 250,
            170, 170, 190, 460, 170, 210, 140, 170, 160, 190, 170, 240, 220, 280, 200, 200,
            140, 160, 180, 150, 310, 390, 480, 200, 300, 300, 170, 200, 170, 370, 370, 300,
            440, 340, 170, 470, 170, 170, 170, 170, 200, 170, 250, 150, 170, 250, 270, 170,
        };

        [Test]
        public void EveryShipHasItsHangarHeight()
        {
            for (int i = 0; i < 64; i++) Assert.AreEqual(ResearchShipY[i], StationTables.ShipY(i), $"ship {i}");
        }

        [System.Serializable] class LiftFile { public List<StationTables.HangarLift> entries; }

        [Test]
        public void HangarLiftsAreWellFormed()
        {
            var file = JsonUtility.FromJson<LiftFile>(GameContent.Text("GoF2Data/hangar_heights"));
            Assert.IsNotNull(file?.entries);
            Assert.Greater(file.entries.Count, 0);
            var seen = new HashSet<(int, int, int)>();
            foreach (var e in file.entries)
            {
                string what = $"hangar {e.hangar} slot {e.slot} ship {e.ship}";
                Assert.IsTrue(seen.Add((e.hangar, e.slot, e.ship)), what + " twice");
                Assert.That(e.hangar, Is.InRange(0, StationTables.ParkedSlots.Length - 1), what);
                Assert.IsNotNull(StationTables.HangarRoom[e.hangar], what + ": a hangar room");
                if (e.slot >= 0)
                {
                    Assert.IsNotNull(StationTables.ParkedSlots[e.hangar], what);
                    Assert.Less(e.slot, StationTables.ParkedSlots[e.hangar].Length, what);
                }
                else Assert.AreEqual(-1, e.slot, what + ": -1 = the turntable");
                Assert.That(e.ship, Is.InRange(0, 63), what);
                Assert.AreEqual(StationTables.LiftBins, e.lift?.Length ?? 0, what + ": one lift per heading");
                Assert.AreEqual(24, StationTables.LiftBins, "15 degrees apart");
                foreach (float l in e.lift) Assert.IsTrue(l >= 0f && !float.IsNaN(l) && !float.IsInfinity(l), what + ": never lowered");
            }
        }

        [Test]
        public void PadHeightsNeverGoBelowTheOriginal()
        {
            // PadPivotY = (floor + ShipY) x 0.05, raised by the lift where the hull would cut into the pad, never lowered.
            int[] hangars = { 0, 1, 2, 3, 7, 8 };
            foreach (int h in hangars)
                foreach (int ship in OwnableShips())
                {
                    var slots = StationTables.ParkedSlots[h];
                    int slotCount = slots?.Length ?? 0;
                    for (int slot = -1; slot < slotCount; slot++)
                    {
                        float floor = slot >= 0 ? slots[slot].y : 0f;
                        float original = (floor + StationTables.ShipY(ship)) * 0.05f;
                        for (int k = 0; k < 24; k += 5)
                            Assert.GreaterOrEqual(StationTables.PadPivotY(h, slot, ship, Quaternion.Euler(0f, k * 15f, 0f)), original - 1e-4f,
                                $"hangar {h} slot {slot} ship {ship}");
                    }
                }
        }
    }
}
