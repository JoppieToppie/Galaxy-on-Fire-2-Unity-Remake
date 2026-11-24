// NetGuardTests.cs
// Feature: CLAUDE.md "Multiplayer" > Server checks: every client request is checked before the server acts on it:
// finite numbers in range (positions within 1e6 m per axis, damage positive and up to 1e6, 1e8 only with the debug menu
// allowed), stations / items / ships that exist, the Void's orbit (-1) as an orbit; command names 32 and arguments 512
// characters, mission JSON and hangar snapshots 4096; and a token bucket per client and request kind drops a flood, and
// 1500 weighted drops in a minute (shots 0.1, trades 0.2) kick the client.
// Expected values: the limits CLAUDE.md states (1e6, 1e8, 32 / 512, 4096, 1500, the weights 0.1 / 0.2), the table sizes
// of the game's data (items 0..232, ships, stations), hand-picked out-of-range values. Within one edit-mode test
// Time.unscaledTime doesn't move, so a bucket doesn't refill while it is emptied.

using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class NetGuardTests : SandboxTest
    {
        // ---- NetGuard ----

        [Test]
        public void TheLimits()
        {
            Assert.AreEqual(1e6f, NetGuard.MaxCoordinate);
            Assert.AreEqual(32, NetGuard.MaxCommandName);
            Assert.AreEqual(512, NetGuard.MaxCommandArgs);
            Assert.AreEqual(4096, NetGuard.MaxMissionJson);
            Assert.AreEqual(4096, NetGuard.MaxSnapshot);
            Assert.AreEqual(1e6f, NetGuard.MaxDamage, "no session: the debug menu isn't allowed");
        }

        [Test]
        public void FiniteNumbers()
        {
            Assert.IsTrue(NetGuard.Finite(0f));
            Assert.IsTrue(NetGuard.Finite(-1e30f));
            Assert.IsFalse(NetGuard.Finite(float.NaN));
            Assert.IsFalse(NetGuard.Finite(float.PositiveInfinity));
            Assert.IsFalse(NetGuard.Finite(float.NegativeInfinity));
            Assert.IsTrue(NetGuard.Finite(new Vector3(1, 2, 3)));
            Assert.IsFalse(NetGuard.Finite(new Vector3(1, float.NaN, 3)));
        }

        [Test]
        public void PositionsWithinAMillionMetres()
        {
            Assert.IsTrue(NetGuard.Position(Vector3.zero));
            Assert.IsTrue(NetGuard.Position(new Vector3(1e6f, -1e6f, 1e6f)));
            Assert.IsFalse(NetGuard.Position(new Vector3(1.01e6f, 0, 0)));
            Assert.IsFalse(NetGuard.Position(new Vector3(0, -2e6f, 0)));
            Assert.IsFalse(NetGuard.Position(new Vector3(0, 0, float.NaN)));
        }

        [Test]
        public void ScanDistanceRejectsInvalidAndDistantPositions()
        {
            Assert.IsTrue(NetState.ScanDistanceValid(Vector3.zero, Vector3.forward * 5000f));
            Assert.IsTrue(NetState.ScanDistanceValid(Vector3.zero, Vector3.forward * NetState.ScanMaxDistanceMeters));
            Assert.IsFalse(NetState.ScanDistanceValid(Vector3.zero, Vector3.forward * (NetState.ScanMaxDistanceMeters + 100f)));
            Assert.IsFalse(NetState.ScanDistanceValid(Vector3.zero, new Vector3(float.NaN, 0, 0)));
            Assert.IsFalse(NetState.ScanDistanceValid(new Vector3(2e6f, 0, 0), new Vector3(2e6f, 0, 0)));
        }

        [Test]
        public void Rotations()
        {
            Assert.IsTrue(NetGuard.Rotation(Quaternion.identity));
            Assert.IsFalse(NetGuard.Rotation(new Quaternion(float.NaN, 0, 0, 1)));
        }

        [Test]
        public void Damage()
        {
            Assert.IsTrue(NetGuard.Damage(0.01f));
            Assert.IsTrue(NetGuard.Damage(1e6f));
            Assert.IsFalse(NetGuard.Damage(1.5e6f));
            Assert.IsFalse(NetGuard.Damage(0f));
            Assert.IsFalse(NetGuard.Damage(-5f));
            Assert.IsFalse(NetGuard.Damage(float.NaN));
            Assert.IsFalse(NetGuard.Damage(float.PositiveInfinity));
        }

        [Test]
        public void NpcDamageRequiresTheServerSelectedOrbitRunner()
        {
            Assert.IsTrue(NetGuard.NpcDamageRunner(true, true, 42, 42), "the current orbit runner may relay NPC damage");
            Assert.IsFalse(NetGuard.NpcDamageRunner(false, true, 42, 42), "a docked player cannot relay NPC damage");
            Assert.IsFalse(NetGuard.NpcDamageRunner(true, false, 42, 42), "a non-runner cannot relay NPC damage");
            Assert.IsFalse(NetGuard.NpcDamageRunner(true, true, 41, 42), "a player running another orbit cannot relay it");
            Assert.IsFalse(NetGuard.NpcDamageRunner(true, false, -1, 42), "a player who runs no orbit cannot relay it");
            Assert.IsTrue(NetGuard.NpcDamageRunner(true, true, -1, -1), "the Void orbit has a valid -1 id");
        }

        [Test]
        public void ItemsShipsStations()
        {
            Assert.IsTrue(NetGuard.Item(0));
            Assert.IsTrue(NetGuard.Item(85), "the Khador Drive");
            Assert.IsTrue(NetGuard.Item(232), "the last original item");
            Assert.IsFalse(NetGuard.Item(-1));
            Assert.IsFalse(NetGuard.Item(100000));
            Assert.IsTrue(NetGuard.Ship(10), "the Phantom");
            Assert.IsFalse(NetGuard.Ship(-1));
            Assert.IsFalse(NetGuard.Ship(100000));
            Assert.IsTrue(NetGuard.Station(78), "Var Hastra");
            Assert.IsTrue(NetGuard.Station(70), "Dis");
            Assert.IsFalse(NetGuard.Station(-1), "the Void is no station");
            Assert.IsFalse(NetGuard.Station(100000));
            Assert.IsTrue(NetGuard.Orbit(-1), "but an orbit");
            Assert.IsTrue(NetGuard.Orbit(78));
            Assert.IsFalse(NetGuard.Orbit(-2));
        }

        [Test]
        public void Shots()
        {
            var up = Vector3.up;
            var v = new Vector3(0, 0, 320f);   // 16 u/ms
            Assert.IsTrue(NetGuard.Shot(0, Vector3.zero, v, up, 3000f, 0f));
            Assert.IsTrue(NetGuard.Shot(0, Vector3.zero, v, up, -5000f, 0f), "a coasting rocket's timer below 0");
            Assert.IsFalse(NetGuard.Shot(0, Vector3.zero, v, up, -5001f, 0f));
            Assert.IsTrue(NetGuard.Shot(0, Vector3.zero, v, up, NetGuard.MaxLifetimeMs, NetGuard.MaxLifetimeMs));
            Assert.IsFalse(NetGuard.Shot(0, Vector3.zero, v, up, NetGuard.MaxLifetimeMs + 1f, 0f));
            Assert.IsFalse(NetGuard.Shot(0, Vector3.zero, v, up, 3000f, -1f), "a homing delay below 0");
            Assert.IsFalse(NetGuard.Shot(-1, Vector3.zero, v, up, 3000f, 0f), "no such item");
            Assert.IsFalse(NetGuard.Shot(0, new Vector3(2e6f, 0, 0), v, up, 3000f, 0f));
            Assert.IsFalse(NetGuard.Shot(0, Vector3.zero, new Vector3(1e7f, 0, 0), up, 3000f, 0f), "too fast");
            Assert.IsFalse(NetGuard.Shot(0, Vector3.zero, v, new Vector3(float.NaN, 0, 0), 3000f, 0f));
            Assert.IsFalse(NetGuard.Shot(0, Vector3.zero, v, up, float.NaN, 0f));
        }

        [Test]
        public void StructuralWeaponStatDoesNotAliasEnergyResistance()
        {
            Assert.AreEqual(189, GoF2Remake.Modding.ItemStats.IdOf("structuralDamage"));
            Assert.AreEqual(183, GoF2Remake.Modding.ItemStats.IdOf("energyResistance"));
        }

        // ---- Player-built jumpgate shot validation ----

        [Test]
        public void RebuiltGateSecondDestructionIsPermanent()
        {
            var transition = typeof(NetPlayerStations).GetMethod("TransitionDestroyedGate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(transition);
            var gate = new PlayerJumpgateRecord
            {
                orbitStationIndex = 78,
                structuralIntegrity = 0f,
                combatStateInitialized = true
            };
            transition.Invoke(null, new object[] { gate });
            Assert.IsTrue(gate.scrapped);
            Assert.IsFalse(gate.permanentlyDestroyed);
            Assert.AreEqual(1, gate.destructionCount);
            Assert.IsTrue(gate.explosionPending);
            Assert.IsFalse(gate.salvageDropPending);

            // Reconstruction re-enables the same record after a successful reclaim.
            gate.scrapped = false;
            gate.building = false;
            gate.explosionPending = false;
            gate.structuralIntegrity = 10000f;
            transition.Invoke(null, new object[] { gate });
            Assert.IsFalse(gate.scrapped);
            Assert.IsTrue(gate.permanentlyDestroyed);
            Assert.AreEqual(2, gate.destructionCount);
            Assert.IsTrue(gate.salvageDropPending);
            Assert.IsTrue(gate.explosionPending);
            Assert.IsNotEmpty(gate.salvageMaterials);
            foreach (var material in gate.salvageMaterials)
                Assert.Greater(material.required, 0);
        }

        [Test]
        public void RegisteredGateShotCanHitOnlyOnce()
        {
            const ulong source = 100001;
            const uint shot = 101;
            var impact = new Vector3(0f, 0f, 100f);
            NetShotRegistry.Register(source, shot, 55, NetShots.NoTarget, 10000f,
                Vector3.zero, Vector3.forward * 200f);
            Assert.IsTrue(NetShotRegistry.TryConsumeGateHit(source, shot, 55,
                impact, impact, 20f));
            Assert.IsFalse(NetShotRegistry.TryConsumeGateHit(source, shot, 55,
                impact, impact, 20f), "one shot cannot destroy a gate through repeated RPCs");
        }

        [Test]
        public void GateHitRejectsWrongWeaponAndImpossibleTrajectory()
        {
            const ulong source = 100002;
            const uint shot = 102;
            var impact = new Vector3(0f, 0f, 100f);
            NetShotRegistry.Register(source, shot, 55, NetShots.NoTarget, 10000f,
                Vector3.zero, Vector3.forward * 200f);
            Assert.IsFalse(NetShotRegistry.TryConsumeGateHit(source, shot, 56,
                impact, impact, 20f), "shot cannot be retagged as another equipped weapon");
            Assert.IsFalse(NetShotRegistry.TryConsumeGateHit(source, shot, 55,
                new Vector3(100f, 0f, 100f), new Vector3(100f, 0f, 100f), 20f),
                "a shot cannot abruptly move sideways to another mesh");
            Assert.IsTrue(NetShotRegistry.TryConsumeGateHit(source, shot, 55,
                impact, impact, 20f), "a rejected report must not consume a valid shot");
        }

        [Test]
        public void GateBlastMustReachItsReportedHullSurface()
        {
            const ulong source = 100003;
            const uint shot = 103;
            var detonation = new Vector3(0f, 0f, 100f);
            var surface = new Vector3(0f, 0f, 120f);
            NetShotRegistry.Register(source, shot, 55, NetShots.NoTarget, 10000f,
                Vector3.zero, Vector3.forward * 200f);
            Assert.IsFalse(NetShotRegistry.TryConsumeGateBlast(source, shot, 55,
                detonation, surface, surface, 20f, 10f));
            Assert.IsTrue(NetShotRegistry.TryConsumeGateBlast(source, shot, 55,
                detonation, surface, surface, 20f, 40f));
            Assert.IsFalse(NetShotRegistry.TryConsumeGateBlast(source, shot, 55,
                detonation, surface, surface, 20f, 40f), "the detonation applies only once");
        }

        // ---- NetRateLimit ----

        [SetUp]
        public void ResetLimits() => NetRateLimit.Reset();

        [TearDown]
        public void ClearLimits() => NetRateLimit.Reset();

        /// <summary>Requests until the first drop: the burst a client may send at once.</summary>
        static int Burst(ulong client, NetRateLimit.Kind kind)
        {
            int n = 0;
            while (NetRateLimit.Allow(client, kind) && n < 100000) n++;
            return n;
        }

        [Test]
        public void ABurstThenDrops()
        {
            int chat = Burst(1001, NetRateLimit.Kind.Chat);
            Assert.Greater(chat, 1, "a few lines at once are fine");
            Assert.Less(chat, 100, "a flood isn't");
            Assert.IsFalse(NetRateLimit.Allow(1001, NetRateLimit.Kind.Chat), "still empty");
        }

        [Test]
        public void EachKindHasItsOwnBucket()
        {
            Burst(1002, NetRateLimit.Kind.Chat);
            Assert.IsTrue(NetRateLimit.Allow(1002, NetRateLimit.Kind.Command));
            Assert.IsTrue(NetRateLimit.Allow(1002, NetRateLimit.Kind.Shot));
        }

        [Test]
        public void EachClientHasItsOwnBuckets()
        {
            Burst(1003, NetRateLimit.Kind.Chat);
            Assert.IsTrue(NetRateLimit.Allow(1004, NetRateLimit.Kind.Chat));
        }

        [Test]
        public void ForgetGivesAFreshBucket()
        {
            int first = Burst(1005, NetRateLimit.Kind.Invite);
            NetRateLimit.Forget(1005);
            Assert.AreEqual(first, Burst(1005, NetRateLimit.Kind.Invite));
        }

        [Test]
        public void BusyPlayIsWellBelowTheLimits()
        {
            // CLAUDE.md: the limits are above real play: a held shop arrow's units, a battle's shots, a busy orbit's ships.
            Assert.GreaterOrEqual(Burst(1006, NetRateLimit.Kind.Trade), 100);
            Assert.GreaterOrEqual(Burst(1006, NetRateLimit.Kind.Shot), 200);
            Assert.GreaterOrEqual(Burst(1006, NetRateLimit.Kind.Hit), 200);
            Assert.GreaterOrEqual(Burst(1006, NetRateLimit.Kind.ProxySpawn), 40, "a busy orbit's 40 ships at once");
        }

        /// <summary>Drops of one kind until the client is kicked (then every request is refused, a kind with a full bucket
        /// too), probed every 10 drops with a kind whose burst outlasts the probes; -1 = never within 'max'.</summary>
        static int DropsUntilKicked(ulong client, NetRateLimit.Kind kind, NetRateLimit.Kind probe, int max)
        {
            Burst(client, kind);
            for (int drops = 1; drops <= max; drops++)
            {
                Assert.IsFalse(NetRateLimit.Allow(client, kind));
                if (drops % 10 == 0 && !NetRateLimit.Allow(client, probe)) return drops;
            }
            return -1;
        }

        [Test]
        public void AFloodOf1500DropsKicks()
        {
            // Chat weighs 1 per drop: kicked at the 1500th (not yet at the 1490th probe).
            int drops = DropsUntilKicked(2001, NetRateLimit.Kind.Chat, NetRateLimit.Kind.Shot, 2000);
            Assert.AreEqual(1500, drops);
        }

        [Test]
        public void TradeDropsWeighAFifth()
        {
            // 0.2 per drop: about 7500 drops (a float sum; probed every 100).
            Burst(2002, NetRateLimit.Kind.Trade);
            int drops = 0;
            while (drops < 9000)
            {
                drops++;
                NetRateLimit.Allow(2002, NetRateLimit.Kind.Trade);
                if (drops % 100 == 0 && !NetRateLimit.Allow(2002, NetRateLimit.Kind.Shot)) break;
            }
            Assert.That(drops, Is.InRange(7400, 7600));
        }

        [Test]
        public void AKickedClientIsRefusedEverything()
        {
            DropsUntilKicked(2003, NetRateLimit.Kind.Chat, NetRateLimit.Kind.Shot, 2000);
            foreach (NetRateLimit.Kind k in System.Enum.GetValues(typeof(NetRateLimit.Kind)))
                if (k != NetRateLimit.Kind.Count) Assert.IsFalse(NetRateLimit.Allow(2003, k), k.ToString());
            Assert.IsTrue(NetRateLimit.Allow(2004, NetRateLimit.Kind.Chat), "another client isn't");
        }

        [Test]
        public void ResetForgetsTheKick()
        {
            DropsUntilKicked(2005, NetRateLimit.Kind.Chat, NetRateLimit.Kind.Shot, 2000);
            NetRateLimit.Reset();
            Assert.IsTrue(NetRateLimit.Allow(2005, NetRateLimit.Kind.Chat));
        }
    }
}
