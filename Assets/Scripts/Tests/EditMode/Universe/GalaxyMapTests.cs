// GalaxyMapTests.cs
// Feature: CLAUDE.md "Star map and system travel" (visibility incl. Loma from the start and the finished world's reveals,
// gate routes, SystemPathFinder's BFS through visible systems, energy cells for a Khador jump: gate jumps on the route, 4
// without one, x2 on Extreme; the Khador Drive item 85 / ships 37 / 38 / 40 and the remake's free-play rule for gateless
// systems; volatile goods 209 / 204; the galaxy-view sun positions; the system view's planets, java.util.Random(system *
// 1000)), plus the security / owner rules of the map's labels and the remake's jumpgate route planning (GateJumps, NextHop).
// Expected numbers: Reference/tools/starmap/starmap_tables.py (gate_path, energy_cells, sun_position, system_map; run with
// python -I, generator gen/starmap_expected.py in the session scratchpad) and Reference/research/starmap_travel.md
// ("No owner" systems 23 / 24 / 26 / 32 / 33, 7.2 energy cells, Ship::hasJumpDrive); the initially visible systems from
// systems.json's initiallyVisible as listed by that tool.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class GalaxyMapTests : SandboxTest
    {
        // systems.json initiallyVisible, plus Loma (25)
        static readonly int[] InitiallyVisible = { 2, 3, 4, 5, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 25, 26 };

        float savedDifficulty;
        bool freeJumpsWasOn;

        [SetUp]
        public void NoFreeJumps()
        {
            savedDifficulty = Session.Difficulty;
            Session.Difficulty = Session.DifficultyNormal;
            freeJumpsWasOn = Cheats.FreeJumps;
            if (freeJumpsWasOn) Cheats.FreeJumps = false;   // the Debug panel's cheat (PlayerPrefs), put back below
        }

        [TearDown]
        public void PutBack()
        {
            Session.Difficulty = savedDifficulty;
            if (freeJumpsWasOn) Cheats.FreeJumps = true;
        }

        static int SystemCount => Db.Systems.Max(s => s.index) + 1;

        static void AllVisible() => Session.SystemVisible = Enumerable.Repeat(true, SystemCount).ToArray();

        static SystemData Sys(int i) => Db.Systems.First(s => s.index == i);

        // ---- visibility -----------------------------------------------------------------------------------------

        [Test]
        public void NewGameMapShowsTheInitialSystemsAndLoma()
        {
            var v = GalaxyMap.Visibility(Db);
            for (int s = 0; s < 34; s++)
                Assert.AreEqual(InitiallyVisible.Contains(s), v[s], $"system {s}");
            Assert.IsTrue(GalaxyMap.IsVisible(Db, GalaxyMap.LomaSystem));
            Assert.IsFalse(GalaxyMap.IsVisible(Db, -1));
            Assert.IsFalse(GalaxyMap.IsVisible(Db, 1000));
        }

        [Test]
        public void ASaveWithFewerSystemsKeepsItsReveals()
        {
            // only the new systems start from initiallyVisible
            var old = new bool[10];
            old[6] = true;     // Wolf-Reiser revealed by the story
            old[2] = false;    // hidden in that save although initially visible
            Session.SystemVisible = old;
            var v = GalaxyMap.Visibility(Db);
            Assert.AreEqual(SystemCount, v.Length);
            Assert.IsTrue(v[6]);
            Assert.IsFalse(v[2]);
            Assert.IsTrue(v[20], "new entries from initiallyVisible");
            Assert.IsFalse(v[27]);
            Assert.IsTrue(v[25], "Loma always");
        }

        [Test]
        public void FinishedWorldRevealsTheAddOnSystems()
        {
            // CLAUDE.md "Multiplayer": the add-ons' revealed systems (Valkyrie 22 / 23 / 24, Supernova 27-31) on the map
            Session.UseCompletedWorld();
            var v = GalaxyMap.Visibility(Db);
            foreach (int s in InitiallyVisible.Concat(new[] { 22, 23, 24, 27, 28, 29, 30, 31 }))
                Assert.IsTrue(v[s], $"system {s}");
        }

        [Test]
        public void BoughtCoordinatesRevealTheSystemWhileTheMapIsOverridden()
        {
            // Discord report "Bug with buying a hidden systems": Suteo (0) and Y'mirr (10) bought at a bar stayed hidden. An
            // event's "system show" (or a mod's condition, a missing mod system) makes Visibility return a copy; the purchase
            // wrote the reveal into that copy. GalaxyMap.Reveal writes the saved map.
            GalaxyMap.SetShown(32, true);
            try
            {
                Assert.AreNotSame(Session.SystemVisible, GalaxyMap.Visibility(Db), "the map is a copy while overridden");
                GalaxyMap.Reveal(Db, 0);
                GalaxyMap.Reveal(Db, 10);
                Assert.IsTrue(Session.SystemVisible[0] && Session.SystemVisible[10], "saved");
                Assert.IsTrue(GalaxyMap.IsVisible(Db, 0) && GalaxyMap.IsVisible(Db, 10), "on the map");
                GalaxyMap.SetShown(32, null);
                Assert.IsTrue(GalaxyMap.IsVisible(Db, 0), "kept after the override ends");
                Assert.IsFalse(GalaxyMap.IsVisible(Db, 32), "the event's system goes back to hidden");
                GalaxyMap.Reveal(Db, -1);
                GalaxyMap.Reveal(Db, 1000);   // out of range: nothing
            }
            finally { Session.SystemShown.Clear(); }
        }

        // ---- gate routes -------------------------------------------------------------------------------------------

        [Test]
        public void IsInRoutes()
        {
            Assert.IsTrue(GalaxyMap.IsInRoutes(Sys(0), 0), "the current system");
            Assert.IsTrue(GalaxyMap.IsInRoutes(Sys(0), 2));
            Assert.IsTrue(GalaxyMap.IsInRoutes(Sys(0), 8));
            Assert.IsFalse(GalaxyMap.IsInRoutes(Sys(0), 3));
            Assert.IsFalse(GalaxyMap.IsInRoutes(Sys(15), 3), "Mido has no gate");
            Assert.IsFalse(GalaxyMap.IsInRoutes(null, 3));
        }

        [TestCase(0, 31, new[] { 0, 8, 3, 5, 20, 31 })]
        [TestCase(19, 27, new[] { 19, 9, 17, 4, 27 })]
        [TestCase(6, 26, new[] { 6, 7, 25, 26 })]
        [TestCase(4, 28, new[] { 4, 27, 28 })]
        [TestCase(1, 30, new[] { 1, 8, 3, 5, 20, 30 })]
        [TestCase(26, 20, new[] { 26, 9, 19, 3, 5, 20 })]
        [TestCase(18, 29, new[] { 18, 8, 2, 17, 29 })]
        public void ShortestGatePathAllVisible(int a, int b, int[] path)
        {
            AllVisible();
            Assert.AreEqual(path, GalaxyMap.SystemPath(Db, a, b).ToArray());
        }

        [TestCase(15, 3)]   // from gateless Mido
        [TestCase(28, 22)]  // to gateless Beidan
        [TestCase(10, 3)]
        [TestCase(2, 2)]    // a == b: no path
        public void NoGatePath(int a, int b)
        {
            AllVisible();
            Assert.IsNull(GalaxyMap.SystemPath(Db, a, b));
        }

        [Test]
        public void PathsOnlyThroughVisibleSystems()
        {
            // new game: Ginoya (27) and Ni'mrrod (21) are hidden, Suteo (0) too
            Assert.AreEqual(new[] { 2, 8, 3 }, GalaxyMap.SystemPath(Db, 2, 3).ToArray());
            Assert.AreEqual(new[] { 7, 25, 26 }, GalaxyMap.SystemPath(Db, 7, 26).ToArray());
            Assert.AreEqual(new[] { 11, 9, 26 }, GalaxyMap.SystemPath(Db, 11, 26).ToArray());
            Assert.IsNull(GalaxyMap.SystemPath(Db, 3, 21));
            Assert.IsNull(GalaxyMap.SystemPath(Db, 3, 0));
            Assert.IsNull(GalaxyMap.SystemPath(Db, 19, 27));
        }

        [Test]
        public void PathsAreShortestAndUseRealGates()
        {
            AllVisible();
            for (int a = 0; a < 34; a++)
                for (int b = 0; b < 34; b++)
                {
                    var p = GalaxyMap.SystemPath(Db, a, b);
                    if (p == null) continue;
                    Assert.AreEqual(a, p[0]);
                    Assert.AreEqual(b, p[p.Count - 1]);
                    for (int k = 1; k < p.Count; k++) Assert.IsTrue(Sys(p[k - 1]).jumpRoutesTo.Contains(p[k]), $"{a}->{b}");
                    Assert.AreEqual(p.Count, p.Distinct().Count(), "no system twice");
                    var back = GalaxyMap.SystemPath(Db, b, a);
                    Assert.IsNotNull(back, "gates work both ways");
                    Assert.AreEqual(p.Count, back.Count, $"{a}<->{b} same length both ways");
                }
        }

        // ---- jumpgate route planning (remake) -------------------------------------------------------------------

        static int StationIn(int system, int not = -1) => Db.Stations.First(s => s.system == system && s.index != not).index;
        static int GateStation(int system) => Sys(system).jumpgateStation;

        [Test]
        public void GateJumpsCountTheRoute()
        {
            AllVisible();
            Assert.AreEqual(0, GalaxyMap.GateJumps(Db, 2, 2));
            Assert.AreEqual(1, GalaxyMap.GateJumps(Db, 0, 8), "a gate neighbour");
            Assert.AreEqual(5, GalaxyMap.GateJumps(Db, 0, 31));
            Assert.AreEqual(0, GalaxyMap.GateJumps(Db, 15, 3), "gateless Mido");
            Assert.IsTrue(GalaxyMap.GateReachable(Db, 0, 31));
            Assert.IsTrue(GalaxyMap.GateReachable(Db, 3, 3));
            Assert.IsFalse(GalaxyMap.GateReachable(Db, 28, 22), "to gateless Beidan");
        }

        [Test]
        public void GateRoutesOnlyThroughVisibleSystems()
        {
            // new game: 3 is two jumps from 2 (through 8), 21 can't be reached
            Assert.AreEqual(2, GalaxyMap.GateJumps(Db, 2, 3));
            Assert.IsFalse(GalaxyMap.GateReachable(Db, 3, 21));
            Assert.AreEqual(0, GalaxyMap.GateJumps(Db, 3, 21));
        }

        [Test]
        public void NextHopIsTheNextSystemsGateOrbit()
        {
            AllVisible();
            // 0 -> 31: 0, 8, 3, 5, 20, 31: the first hop lands in 8's gate orbit, then 3's, 5's, 20's, then the station itself
            int target = StationIn(31);
            Assert.AreEqual(GateStation(8), GalaxyMap.NextHop(Db, StationIn(0), target));
            Assert.AreEqual(GateStation(3), GalaxyMap.NextHop(Db, GateStation(8), target));
            Assert.AreEqual(GateStation(20), GalaxyMap.NextHop(Db, GateStation(5), target));
            Assert.AreEqual(target, GalaxyMap.NextHop(Db, GateStation(20), target), "the last gate jumps to the destination");
        }

        // Expected routes: Dijkstra over every system (scratchpad safe_routes.py), the pairs with a single cheapest route
        // either way. Security: 2 Behon 1, 9 Weymire 2, 11 Union 2, 8 Buntta 2, 19 Augmenta 3, 14 Magnetar 3, 16 Prospero 1,
        // 12 Oom'bak 2.
        [TestCase(0, 7, new[] { 0, 2, 9, 11, 7 }, new[] { 0, 8, 19, 14, 11, 7 })]
        [TestCase(9, 13, new[] { 9, 11, 16, 13 }, new[] { 9, 19, 14, 12, 13 })]
        [TestCase(7, 0, new[] { 7, 11, 9, 2, 0 }, new[] { 7, 11, 14, 19, 8, 0 })]
        public void SafestRouteGoesRoundTheDangerousSystems(int a, int b, int[] fastest, int[] safest)
        {
            AllVisible();
            Assert.AreEqual(fastest, GalaxyMap.RoutePath(Db, a, b, false).ToArray());
            Assert.AreEqual(fastest, GalaxyMap.SystemPath(Db, a, b).ToArray(), "SystemPath is the fewest jumps");
            Assert.AreEqual(safest, GalaxyMap.RoutePath(Db, a, b, true).ToArray());
            Assert.Less(GalaxyMap.RouteDanger(Db, safest.ToList()), GalaxyMap.RouteDanger(Db, fastest.ToList()));
            Assert.AreEqual(safest.Length - 1, GalaxyMap.GateJumps(Db, a, b, true));
        }

        [Test]
        public void SafestNeverTakesMoreDangerOrFewerJumps()
        {
            AllVisible();
            for (int a = 0; a < 34; a++)
                for (int b = 0; b < 34; b++)
                {
                    var fast = GalaxyMap.RoutePath(Db, a, b, false);
                    var safe = GalaxyMap.RoutePath(Db, a, b, true);
                    Assert.AreEqual(fast == null, safe == null, $"{a}->{b} reachable either way");
                    if (fast == null) continue;
                    Assert.GreaterOrEqual(safe.Count, fast.Count, $"{a}->{b}");
                    Assert.LessOrEqual(GalaxyMap.RouteDanger(Db, safe), GalaxyMap.RouteDanger(Db, fast), $"{a}->{b}");
                    for (int k = 1; k < safe.Count; k++) Assert.IsTrue(Sys(safe[k - 1]).jumpRoutesTo.Contains(safe[k]), $"{a}->{b}");
                }
        }

        [Test]
        public void TheSafestRoutesHopsFollowIt()
        {
            AllVisible();
            int target = StationIn(7);
            Assert.AreEqual(GateStation(2), GalaxyMap.NextHop(Db, StationIn(0), target), "fastest: through Behon");
            Assert.AreEqual(GateStation(8), GalaxyMap.NextHop(Db, StationIn(0), target, true), "safest: through Buntta");
            Assert.AreEqual(GateStation(19), GalaxyMap.NextHop(Db, GateStation(8), target, true));
            Assert.AreEqual(GateStation(14), GalaxyMap.NextHop(Db, GateStation(19), target, true));
        }

        [Test]
        public void NextHopIsTheDestinationWhereTheOriginalsGateWent()
        {
            AllVisible();
            int neighbour = StationIn(8);
            Assert.AreEqual(neighbour, GalaxyMap.NextHop(Db, StationIn(0), neighbour), "a gate neighbour");
            int here = StationIn(0);
            Assert.AreEqual(here, GalaxyMap.NextHop(Db, StationIn(0, here), here), "this system");
            int beidan = StationIn(22);
            Assert.AreEqual(beidan, GalaxyMap.NextHop(Db, StationIn(28), beidan), "no gate route: straight there");
        }

        // ---- energy cells (StarMap::OnTouchBegin) ---------------------------------------------------------------

        [TestCase(0, 31, 5, false)]
        [TestCase(19, 27, 4, false)]
        [TestCase(6, 26, 3, false)]
        [TestCase(4, 28, 2, false)]
        [TestCase(15, 3, 4, false)]   // no route from Mido: 4, the target has gates
        [TestCase(28, 22, 4, true)]   // Beidan has no gate: warning 581
        [TestCase(3, 10, 4, true)]    // Y'mirr has no gate
        [TestCase(2, 2, 0, false)]
        public void EnergyCellsAllVisible(int a, int b, int cells, bool noGate)
        {
            AllVisible();
            Assert.AreEqual(cells, GalaxyMap.EnergyCells(Db, a, b, out bool ng));
            Assert.AreEqual(noGate, ng);
        }

        [TestCase(0, 31, 10)]
        [TestCase(4, 28, 4)]
        [TestCase(28, 22, 8)]
        [TestCase(2, 2, 0)]
        public void EnergyCellsDoubleOnExtreme(int a, int b, int cells)
        {
            AllVisible();
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.AreEqual(cells, GalaxyMap.EnergyCells(Db, a, b, out _));
        }

        [Test]
        public void HardIsNotExtreme()
        {
            AllVisible();
            Session.Difficulty = Session.DifficultyHard;
            Assert.AreEqual(5, GalaxyMap.EnergyCells(Db, 0, 31, out _));
        }

        [Test]
        public void HiddenRouteCostsFour()
        {
            // new game map: 19 -> 27 has no visible route
            Assert.AreEqual(4, GalaxyMap.EnergyCells(Db, 19, 27, out bool ng));
            Assert.IsFalse(ng);
            Assert.AreEqual(2, GalaxyMap.EnergyCells(Db, 2, 3, out _));
        }

        [Test]
        public void FreeJumpsCheatCostsNothing()
        {
            AllVisible();
            try
            {
                Cheats.FreeJumps = true;
                Assert.AreEqual(0, GalaxyMap.EnergyCells(Db, 0, 31, out _));
            }
            finally { Cheats.FreeJumps = false; }
        }

        [Test]
        public void CellsInTheHold()
        {
            Assert.AreEqual(0, GalaxyMap.CellsInCargo());
            Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, 7));
            Assert.AreEqual(7, GalaxyMap.CellsInCargo());
            GalaxyMap.RemoveCells(3);
            Assert.AreEqual(4, GalaxyMap.CellsInCargo());
            GalaxyMap.RemoveCells(4);
            Assert.AreEqual(0, GalaxyMap.CellsInCargo());
            Assert.IsFalse(Session.Cargo.Exists(s => s.item == GalaxyMap.EnergyCellItem), "an empty stack leaves the hold");
            GalaxyMap.RemoveCells(1);   // nothing to remove: no error
            Assert.AreEqual(122, GalaxyMap.EnergyCellItem);
        }

        // ---- the Khador Drive and volatile goods ------------------------------------------------------------------

        [TestCase(37, true), TestCase(38, true), TestCase(40, true), TestCase(39, false), TestCase(10, false), TestCase(41, false)]
        public void IntegratedDrive(int ship, bool drive)
        {
            Assert.AreEqual(drive, GalaxyMap.HasIntegratedDrive(ship));
        }

        [Test]
        public void JumpDriveFromItemOrShip()
        {
            Session.FreePlay = false;
            Session.StationIndex = 0;
            Session.ShipIndex = 10;
            Session.Equipment.Clear();
            Assert.IsFalse(GalaxyMap.HasJumpDrive(Db));
            Session.Equipment.Add(new ItemStack(GalaxyMap.KhadorDriveItem, 1));
            Assert.IsTrue(GalaxyMap.HasJumpDrive(Db), "item 85 fitted");
            Session.Equipment.Clear();
            Session.ShipIndex = 38;
            Assert.IsTrue(GalaxyMap.HasJumpDrive(Db), "the Typhon's own drive");
            Assert.AreEqual(85, GalaxyMap.KhadorDriveItem);
        }

        [Test]
        public void FreePlayCountsADriveOnlyInGatelessSystems()
        {
            Session.ShipIndex = 10;
            Session.Equipment.Clear();
            Session.FreePlay = true;
            Session.StationIndex = 78;   // Var Hastra, Mido: no jumpgate
            Assert.IsTrue(GalaxyMap.HasJumpDrive(Db));
            Session.StationIndex = 0;    // Suteo has a gate
            Assert.IsFalse(GalaxyMap.HasJumpDrive(Db));
            Session.FreePlay = false;
            Session.StationIndex = 78;   // the campaign takes the player out of Mido
            Assert.IsFalse(GalaxyMap.HasJumpDrive(Db));
        }

        [TestCase(209, true), TestCase(204, true), TestCase(122, false), TestCase(154, false)]
        public void VolatileGoods(int item, bool volatileGoods)
        {
            Session.Cargo.Clear();
            Assert.IsFalse(GalaxyMap.HasVolatileGoods);
            Session.Cargo.Add(new ItemStack(item, 1));
            Assert.AreEqual(volatileGoods, GalaxyMap.HasVolatileGoods);
            Session.Cargo[0].amount = 0;
            Assert.IsFalse(GalaxyMap.HasVolatileGoods, "an empty stack is nothing");
        }

        // ---- the galaxy view ----------------------------------------------------------------------------------

        [TestCase(0, 920, -3150, 3700)]
        [TestCase(15, -60, 2700, 1600)]
        [TestCase(25, -2021, 3090, 2800)]   // (100 - 43) / 100 * 14000 = 7979.999.. truncated
        [TestCase(27, 1480, 2960, 1600)]
        [TestCase(33, -3420, -7570, 2800)]
        public void SunPositions(int system, int x, int y, int z)
        {
            Assert.AreEqual(new Vector3(x, y, z), GalaxyMap.SunPosition(Sys(system)));
        }

        [Test]
        public void NoOwnerSystems()
        {
            var noOwner = new[] { 23, 24, 26, 32, 33 };
            for (int s = 0; s < 34; s++) Assert.AreEqual(!noOwner.Contains(s), GalaxyMap.HasOwner(s), $"system {s}");
        }

        [Test]
        public void SecurityLevels()
        {
            Assert.AreEqual(0, GalaxyMap.SecurityOf(null));
            Assert.AreEqual(Sys(2).securityLevel, GalaxyMap.SecurityOf(Sys(2)));
            Assert.AreEqual(0, GalaxyMap.SecurityOf(Sys(26)), "Shima (Kaamo) during the siege");
            Session.KaamoState = 1;
            Assert.AreEqual(0, GalaxyMap.SecurityOf(Sys(26)));
            Session.KaamoState = 2;
            Assert.AreEqual(3, GalaxyMap.SecurityOf(Sys(26)), "Secure once the siege is over");
            Assert.AreEqual(Sys(25).securityLevel, GalaxyMap.SecurityOf(Sys(25)));
            Assert.AreEqual(4, GalaxyMap.SecurityColours.Length);
        }

        // ---- the system view (StarMap::initStarSystem) ----------------------------------------------------------

        public readonly struct M
        {
            public readonly int system, station, slot, angle, radius; public readonly float scale, orbitYaw; public readonly bool ring, gate;
            public M(int system, int station, int slot, int angle, int radius, float scale, bool ring, bool gate, float orbitYaw)
            { this.system = system; this.station = station; this.slot = slot; this.angle = angle; this.radius = radius; this.scale = scale; this.ring = ring; this.gate = gate; this.orbitYaw = orbitYaw; }
        }

        static readonly M[] Layouts =
        {
            new M(0, 0, 0, 0, 9948, 0.078125f, false, true, 1.916f),
            new M(0, 1, 1, 10922, 15995, 0.0625f, false, false, 1.982f),
            new M(0, 2, 5, 54610, 20248, 0.046875f, false, false, 0.073f),
            new M(0, 3, 3, 32766, 22967, 0.046875f, false, false, 1.565f),
            new M(0, 4, 2, 21844, 26444, 0.078125f, false, false, 0.176f),
            new M(6, 10, 1, 32768, 10163, 0.0625f, true, true, 1.326f),
            new M(15, 75, 1, 10922, 8715, 0.046875f, false, false, 2.65f),
            new M(15, 76, 0, 0, 15832, 0.046875f, false, false, 1.938f),
            new M(15, 77, 3, 32766, 22820, 0.078125f, false, false, 0.76f),
            new M(15, 78, 2, 21844, 26235, 0.078125f, false, false, 0.755f),
            new M(15, 79, 4, 43688, 31495, 0.046875f, false, false, 0.059f),
            new M(20, 62, 0, 0, 11384, 0.078125f, false, true, 1.121f),
            new M(20, 59, 5, 54610, 16366, 0.046875f, false, false, 0.572f),
            new M(20, 58, 1, 10922, 21920, 0.046875f, false, false, 2.053f),
            new M(20, 63, 2, 21844, 28254, 0.0625f, false, false, 0.935f),
            new M(20, 64, 4, 43688, 32948, 0.078125f, false, false, 1.696f),
            new M(26, 108, 0, 0, 9595, 0.046875f, false, true, 1.587f),
            new M(27, 109, 0, 0, 13205, 0.078125f, false, false, 2.443f),
            new M(27, 110, 2, 21844, 19916, 0.078125f, false, false, 1.464f),
            new M(27, 111, 3, 32766, 25973, 0.078125f, false, false, 0.098f),
            new M(27, 112, 4, 43688, 29247, 0.0625f, false, true, 1.753f),
            new M(27, 113, 5, 54610, 34751, 0.0625f, true, false, 0.159f),
            new M(31, 128, 4, 52428, 10538, 0.078125f, false, false, 2.647f),
            new M(31, 129, 1, 13107, 12971, 0.078125f, false, false, 2.291f),
            new M(31, 130, 0, 0, 14851, 0.0625f, true, true, 3.037f),
            new M(31, 131, 2, 26214, 21945, 0.078125f, false, false, 0.939f),
        };

        [TestCase(0), TestCase(6), TestCase(15), TestCase(20), TestCase(26), TestCase(27), TestCase(31)]
        public void SystemViewLayout(int system)
        {
            var expected = Layouts.Where(m => m.system == system).ToArray();
            var actual = GalaxyMap.SystemLayout(Db, Sys(system));
            Assert.AreEqual(expected.Length, actual.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var e = expected[i]; var a = actual[i];
                string at = $"system {system} station {e.station}";
                Assert.AreEqual(e.station, a.station, at);
                Assert.AreEqual(e.slot, a.slot, at + " slot");
                Assert.AreEqual(e.angle, a.angle, at + " angle");
                Assert.AreEqual(e.radius, a.radius, at + " radius");
                Assert.AreEqual(e.scale, a.scale, 1e-7f, at + " scale");
                Assert.AreEqual(e.ring, a.ring, at + " ring");
                Assert.AreEqual(e.gate, a.gate, at + " gate");
                Assert.AreEqual(e.orbitYaw, a.orbitYaw, 1e-6f, at + " orbit yaw");
                Assert.AreEqual(Db.Stations.First(s => s.index == e.station).textureIndex, a.textureIndex);
            }
        }

        [Test]
        public void SystemViewInvariants()
        {
            foreach (var sys in Db.Systems.Where(s => s.index < 34))
            {
                var list = GalaxyMap.SystemLayout(Db, sys);
                Assert.AreEqual(sys.stations, list.Select(p => p.station).ToList(), $"system {sys.index}: station order");
                Assert.AreEqual(list.Count, list.Select(p => p.slot).Distinct().Count(), "one planet per slot");
                Assert.IsTrue(list.All(p => p.slot >= 0 && p.slot <= list.Count));
                for (int k = 0; k < list.Count; k++)
                {
                    int previous = k == 0 ? 6400 : list[k - 1].radius;
                    Assert.That(list[k].radius - previous, Is.InRange(1600, 7199), "radius grows by rand(5600) + 1600");
                    Assert.That(list[k].orbitYaw, Is.InRange(0f, 3.140f));
                }
                Assert.AreEqual(sys.jumpgateStation >= 0 ? 1 : 0, list.Count(p => p.gate), $"system {sys.index}");
                Assert.AreEqual(GalaxyMap.SystemLayout(Db, sys).Select(p => p.radius), list.Select(p => p.radius), "same on every visit");
            }
            Assert.AreEqual(0, GalaxyMap.SystemLayout(Db, null).Count);
        }
    }
}
