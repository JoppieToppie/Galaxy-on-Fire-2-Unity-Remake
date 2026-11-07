// HazardTests.cs
// Feature: Modding/README.md orbit "hazards", docs/notes/navigation.md "Hazard volumes": Flight.Hazards, after EVERSPACE 2's
// BP_HeatDamageVolume (200 dps x (1 - d / r)^2 each 0.25 s) and the radiated condition (the hull's maximum x 0.85 a token,
// at most 10, cleared by docking). Expected values: those sums by hand.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class HazardTests
    {
        [TearDown] public void Clear() => Hazards.ClearRadiation();

        [Test]
        public void HeatFallsOffWithTheSquareOfTheDistance()
        {
            Assert.AreEqual(50f, Hazards.HeatDamage(200f, 0f, 1000f), 1e-3f, "the centre: 200 a second over a quarter second");
            Assert.AreEqual(12.5f, Hazards.HeatDamage(200f, 500f, 1000f), 1e-3f, "halfway: a quarter of it");
            Assert.AreEqual(0f, Hazards.HeatDamage(200f, 1000f, 1000f), "the edge");
            Assert.AreEqual(0f, Hazards.HeatDamage(200f, 1500f, 1000f), "outside");
        }

        [Test]
        public void RadiationLowersTheHullsLimitUntilDocking()
        {
            Assert.AreEqual(1f, Hazards.RadiationFactor(0));
            Assert.IsTrue(Hazards.AddRadiation());
            Assert.AreEqual(0.85f, Hazards.RadiationFactor(Hazards.RadiationTokens), 1e-5f);
            for (int i = 0; i < 20; i++) Hazards.AddRadiation();
            Assert.AreEqual(Hazards.MaxRadiationTokens, Hazards.RadiationTokens, "at most 10");
            Assert.IsFalse(Hazards.AddRadiation());
            Assert.AreEqual(UnityEngine.Mathf.Pow(0.85f, 10), Hazards.RadiationFactor(Hazards.RadiationTokens), 1e-5f);
            Hazards.ClearRadiation();
            Assert.AreEqual(0, Hazards.RadiationTokens, "docked");
        }

        [Test]
        public void FrostBuildsForFiveSecondsAndDrainsOverSixty()
        {
            float b = Hazards.FrostStep(0f, 4000f, true, out bool frozen);
            Assert.IsFalse(frozen);
            Assert.AreEqual(4000f, b);
            b = Hazards.FrostStep(b, 12000f, false, out frozen);
            Assert.AreEqual(3000f, b, 1e-3f, "12 s outside drain a fifth of the 5 s (60 s for all)");
            b = Hazards.FrostStep(b, 2000f, true, out frozen);
            Assert.IsTrue(frozen, "5 s built: frozen");
            Assert.AreEqual(0f, b, "and it builds again");
            Assert.AreEqual(0f, Hazards.FrostStep(100f, 60000f, false, out frozen), "never below none");
        }

        [Test]
        public void TheHeatsBloomFollowsTheDepthInside()
        {
            var heat = new[] { new GoF2Remake.Modding.ModWorld.HazardSpec { kind = GoF2Remake.Modding.ModWorld.HazardKind.Heat, centre = UnityEngine.Vector3.zero, radius = 10000f } };
            float r = 10000f * 0.05f;   // 500 m
            Assert.AreEqual(1f, GoF2Remake.World.HazardLooks.HeatDepth(heat, UnityEngine.Vector3.zero), "the centre");
            Assert.AreEqual(1f, GoF2Remake.World.HazardLooks.HeatDepth(heat, new UnityEngine.Vector3(r * 0.85f, 0f, 0f)), 1e-4f, "85 % in");
            Assert.AreEqual(0.5f, GoF2Remake.World.HazardLooks.HeatDepth(heat, new UnityEngine.Vector3(r * 0.925f, 0f, 0f)), 1e-3f, "half way through the edge");
            Assert.AreEqual(0f, GoF2Remake.World.HazardLooks.HeatDepth(heat, new UnityEngine.Vector3(r * 1.2f, 0f, 0f)), "outside");
        }

        [Test]
        public void StormFlashesLieOnTheCylinder()
        {
            var o = GoF2Remake.World.OrbitStorm.FlashOffset(4000f, 4000f, 0.25f, 1f);
            Assert.AreEqual(4000f, new UnityEngine.Vector2(o.x, o.z).magnitude, 1e-2f, "on the cylinder's surface (ES2's VolumeSurfaceOnly)");
            Assert.AreEqual(2000f, o.y, 1e-3f, "at the top: half the height up");
            float total = 0f;
            UnityEngine.Random.InitState(7);
            for (int i = 0; i < 2000; i++) total += GoF2Remake.World.OrbitStorm.NextFlashMs(1f);
            Assert.AreEqual(1000f, total / 2000f, 100f, "a flash a second on average at frequency 1");
        }

        [Test]
        public void ShotTriggersUnlockOnceAllAreLit()
        {
            var c = new ShotTriggerChain(3, false, 0f);
            Assert.IsFalse(c.Hit(2));
            Assert.IsFalse(c.Hit(0));
            Assert.IsTrue(c.Hit(1), "any order: the last one unlocks it");
            Assert.IsTrue(c.Unlocked);
            Assert.IsFalse(c.Hit(1), "once only");
        }

        [Test]
        public void OrderedShotTriggersResetOnAWrongHit()
        {
            var c = new ShotTriggerChain(3, true, 0f);
            Assert.IsFalse(c.Hit(0));
            Assert.IsFalse(c.Hit(2), "the one before is dark");
            Assert.IsFalse(c.IsLit(0), "OnReset walks the chain back");
            c.Hit(0); c.Hit(1);
            Assert.IsTrue(c.Hit(2));
        }

        [Test]
        public void RetriggerableShotTriggersGoDarkAgain()
        {
            var c = new ShotTriggerChain(2, false, ShotTriggerChain.DefaultRetriggerS);
            c.Hit(0);
            c.Update(2999f);
            Assert.IsTrue(c.IsLit(0));
            c.Update(2f);
            Assert.IsFalse(c.IsLit(0), "BP_ShotTrigger's RegtriggerDelay 3 s");
            c.Hit(0); c.Update(1000f);
            Assert.IsTrue(c.Hit(1), "both lit within the 3 s");
            c.Update(10000f);
            Assert.IsTrue(c.IsLit(0), "unlocked for good");
        }

        [Test]
        public void ConductorsCarryEnergyAlongLinkedChains()
        {
            // source 0 -> relay at 150 m -> receiver at 340 m (within the relay's 200 m) ; a lone receiver far off
            var at = new System.Collections.Generic.List<(float, float, float)> { (0f, 0f, 0f), (150f, 0f, 0f), (340f, 0f, 0f), (1000f, 0f, 0f) };
            var r = new System.Collections.Generic.List<float> { ConductorGraph.ConductorRadiusM, ConductorGraph.ConductorRadiusM, ConductorGraph.ReceiverRadiusM, ConductorGraph.ReceiverRadiusM };
            var src = new System.Collections.Generic.List<bool> { true, false, false, false };
            var on = ConductorGraph.Energized(at, r, src, out var feeder);
            Assert.AreEqual(new[] { true, true, true, false }, on);
            Assert.AreEqual(new[] { -1, 0, 1, -1 }, feeder, "each fed by the node before it");
            at[1] = (250f, 0f, 0f);
            on = ConductorGraph.Energized(at, r, src, out _);
            Assert.IsFalse(on[1], "beyond both 200 m spheres: no link");
            Assert.IsTrue(ConductorGraph.Linked(199f, 2f, 200f), "within the larger sphere");
        }

        [Test]
        public void ASocketTakesItsKindWithinReach()
        {
            float reach = GoF2Remake.World.EnergySocket.ConnectM;
            Assert.AreEqual(36f, reach, 1e-4f, "BP_EnergyOrb_Socket's ConnectionDistance 1000 cm x 3.6");
            Assert.IsTrue(GoF2Remake.World.EnergySocket.Takes(false, false, reach));
            Assert.IsFalse(GoF2Remake.World.EnergySocket.Takes(false, true, 1f), "a coil doesn't fit a sphere socket");
            Assert.IsFalse(GoF2Remake.World.EnergySocket.Takes(true, true, reach + 1f));
            var p = GoF2Remake.World.Portable.CarryPoint(UnityEngine.Vector3.zero, UnityEngine.Vector3.forward, UnityEngine.Vector3.up);
            Assert.AreEqual(-30f, p.z, 1e-4f, "behind the ship");
            Assert.Less(p.y, 0f, "a little below");
        }

        [Test]
        public void AcidCorrodesNpcShipsNotThePlayersOrProps()
        {
            var go = new UnityEngine.GameObject("t");
            try
            {
                var t = go.AddComponent<Target>();
                t.hp = t.maxHp = 10f;
                t.isShip = true;
                Assert.IsTrue(GoF2Remake.World.ShipCorrosion.Corrodes(t, UnityEngine.Vector3.zero, 10f));
                Assert.IsFalse(GoF2Remake.World.ShipCorrosion.Corrodes(t, new UnityEngine.Vector3(20f, 0f, 0f), 10f), "outside");
                t.isPlayer = true;
                Assert.IsFalse(GoF2Remake.World.ShipCorrosion.Corrodes(t, UnityEngine.Vector3.zero, 10f), "the player's: RiftField's part");
                t.isPlayer = false; t.isShip = false;
                Assert.IsFalse(GoF2Remake.World.ShipCorrosion.Corrodes(t, UnityEngine.Vector3.zero, 10f), "a prop");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            Assert.AreEqual(8f, GoF2Remake.World.AcidSackBase.SpitMinS, "SpitIntervalMin");
            Assert.AreEqual(13f, GoF2Remake.World.AcidSackBase.SpitMaxS, "SpitIntervalMax");
        }

        [Test]
        public void AThrownSphereKeepsItsMomentumAndFliesOn()
        {
            var v = new UnityEngine.Vector3(0f, 0f, 50f);
            Assert.AreEqual(v, GoF2Remake.World.Portable.Fling(v, UnityEngine.Vector3.right, false), "let go: Detach's bKeepMomentum");
            var t = GoF2Remake.World.Portable.Fling(v, UnityEngine.Vector3.right * 3f, true);
            Assert.AreEqual(720f, t.x, 1e-3f, "FlingStrength 20 000 cm/s x 3.6 along the nose");
            Assert.AreEqual(50f, t.z, 1e-4f, "on top of its momentum");
        }

        [Test]
        public void ACloakedContainerShowsItselfOnlyUpClose()
        {
            Assert.AreEqual(2, GoF2Remake.World.CloakedContainers.Stage(100f), "within ES2's InteractRadius 40 m x 3.6: the marker comes on");
            Assert.AreEqual(1, GoF2Remake.World.CloakedContainers.Stage(1500f), "within the activity's 2 km: CONTAINER NEARBY");
            Assert.AreEqual(0, GoF2Remake.World.CloakedContainers.Stage(2500f));
            Assert.Less(GoF2Remake.World.CloakedContainers.RevealM, GoF2Remake.World.LootContainer.OpenRangeM, "revealed before it can be opened");
        }

        [Test]
        public void MinefieldsAreLaidOnlyWhereTheirSpecAllows()
        {
            var spec = new GoF2Remake.Modding.ModWorld.MinefieldSpec();   // defaults: security 1 or lower, rank 4 or more, half the visits
            Assert.IsTrue(GoF2Remake.World.OrbitMinefield.Laid(spec, 1, 4, 0.49f));
            Assert.IsFalse(GoF2Remake.World.OrbitMinefield.Laid(spec, 2, 4, 0f), "an average system");
            Assert.IsFalse(GoF2Remake.World.OrbitMinefield.Laid(spec, 0, 3, 0f), "a low rank");
            Assert.IsFalse(GoF2Remake.World.OrbitMinefield.Laid(spec, 0, 10, 0.5f), "the roll");
        }

        [Test]
        public void AMinesBlastFollowsEverspace2sBaseDamage()
        {
            var spec = new GoF2Remake.Modding.ModWorld.MinefieldSpec();
            Assert.AreEqual(120f / 55f, GoF2Remake.World.OrbitMinefield.RaiderSeconds(spec), 1e-5f, "BP_Minefield 60 + 60 over a raider's 55");
            spec.kind = GoF2Remake.Modding.ModWorld.MinefieldSpec.Kind.Web;
            Assert.AreEqual(15f / 55f, GoF2Remake.World.OrbitMinefield.RaiderSeconds(spec), 1e-5f, "_Web 10 + 5");
            spec.raiderSeconds = 3f;
            Assert.AreEqual(3f, GoF2Remake.World.OrbitMinefield.RaiderSeconds(spec), "the mod's own");
            Assert.AreEqual(RiftRun.MineBlastM * 1.5f, GoF2Remake.World.OrbitMine.BlastM(GoF2Remake.Modding.ModWorld.MinefieldSpec.Kind.Thermal), 1e-3f);
        }

        [Test]
        public void AnEncountersMusicLastsWhileItsFightDoes()
        {
            Assert.IsTrue(GoF2Remake.World.EncounterMusic.StillOn(5f, 5f, 100f), "the grace: the fight hasn't started yet");
            Assert.IsTrue(GoF2Remake.World.EncounterMusic.StillOn(60f, 10f, 100f), "calm a little while");
            Assert.IsFalse(GoF2Remake.World.EncounterMusic.StillOn(60f, 16f, 100f), "calm 15 s after the grace");
            Assert.IsFalse(GoF2Remake.World.EncounterMusic.StillOn(5f, 0f, 16000f), "flown off");
        }

        [Test]
        public void AShipInNeedAsksForWhatThePilotCarries()
        {
            var items = GoF2Remake.World.ShipInNeed.Items;
            Assert.AreEqual(98, GoF2Remake.World.ShipInNeed.Pick(items, i => i == 98 ? 5 : 1, 3), "enough Medical Supplies aboard");
            Assert.AreEqual(121, GoF2Remake.World.ShipInNeed.Pick(items, i => i == 121 ? 1 : 0, 3), "some Nanotech aboard");
            CollectionAssert.Contains(items, GoF2Remake.World.ShipInNeed.Pick(items, i => 0, 3), "else either");
            Assert.AreEqual(75f, GoF2Remake.World.ShipInNeed.RepairPerS, "EnqueueHullRepair's RepairRatePerSecond");
        }

        [Test]
        public void TheShootingChallengeGoesToTheBetterShot()
        {
            Assert.AreEqual(1, GoF2Remake.World.ChallengeWatch.Outcome(5, 4));
            Assert.AreEqual(-1, GoF2Remake.World.ChallengeWatch.Outcome(3, 4));
            Assert.AreEqual(0, GoF2Remake.World.ChallengeWatch.Outcome(4, 4), "a tie: ES2's AdditionalEnemy");
            Assert.AreEqual(3f, GoF2Remake.World.CruiseEncounterSpawner.ChallengerDamageFactor, "ChallengerDealDamageFactor");
        }

        [Test]
        public void AMinesweepersFieldKeepsItsMinesApart()
        {
            float radiusM = GoF2Remake.World.CruiseEncounterSpawner.MinesweeperRadius * 0.05f;
            Assert.AreEqual(1080f, radiusM, 1e-2f, "BP_Minefield_BiggerRadius 30 000 cm x 3.6");
            var list = GoF2Remake.World.OrbitMinefield.Places(UnityEngine.Vector3.zero, radiusM, 40, radiusM * 0.48f, new System.Random(5));
            int close = 0;
            for (int i = 0; i < list.Count; i++)
                for (int j = 0; j < i; j++) if ((list[i] - list[j]).magnitude < RiftRun.MineTriggerM * 0.5f) close++;
            Assert.Less(close, 4, "next to none within half a mine's trigger of another: they don't go off together");
            Assert.AreEqual(18000f, new GoF2Remake.Modding.ModWorld.MinefieldSpec().radius, "ES2's 25 000 cm x 0.2 x 3.6");
        }

        [Test]
        public void MinesLieInTheFieldSpreadOut()
        {
            var c = new UnityEngine.Vector3(10f, 20f, 30f);
            var list = GoF2Remake.World.OrbitMinefield.Places(c, 1000f, 20, 300f, new System.Random(3));
            Assert.AreEqual(20, list.Count);
            float closest = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                Assert.LessOrEqual((list[i] - c).magnitude, 1000.01f, "inside the sphere");
                for (int j = 0; j < i; j++) closest = UnityEngine.Mathf.Min(closest, (list[i] - list[j]).magnitude);
            }
            Assert.Greater(closest, 100f, "kept apart (the best of a few tries each)");
        }

        [Test]
        public void TheFieldsTakeEverspace2sColours()
        {
            Assert.AreEqual(new UnityEngine.Color(0.84f, 1f, 0.09f), GoF2Remake.World.HazardLooks.KindColor(GoF2Remake.Modding.ModWorld.HazardKind.Corrosion), "P_CorrosionField's StartColor");
            Assert.AreEqual(new UnityEngine.Color(0.19f, 0.77f, 1f), GoF2Remake.World.HazardLooks.KindColor(GoF2Remake.Modding.ModWorld.HazardKind.Frost), "NS_FrostField's Initial.Color");
            Assert.AreEqual(new UnityEngine.Color(1f, 1f, 0.39f), GoF2Remake.World.HazardLooks.KindColor(GoF2Remake.Modding.ModWorld.HazardKind.Radiation), "RadiationColor");
        }
    }
}
