// MultiplayerHandOverTests.cs
// Feature: docs/notes/multiplayer-sync.md "Orbit authority": when the game running an orbit leaves it, the next runner
// rebuilds every object from its proxy (NetOrbit.AdoptHandedOver, NetCrate's takeover) where it was, so the players still
// there see nothing vanish, appear or reset. A hosted session flies at Dis with its traffic plus one of each harder kind:
// an enhanced carrier with its turrets and escorts (capital parts, a capital ship's waves), a pirate outpost (a fixed
// object that leaves a wreck), Qyrr Myfft's ship 45 (a fighter turret its creation makes again), a freighter destroyed into
// a wreck, a fighter destroyed (dying), an EMP-disabled ship and a crate. The game then hands its own orbit to itself as
// another game would (NetOrbit.SimulateHandOverForTest: the ships let go, the proxies kept as copies) and each proxy must be
// driven again: the same model where it was with its hull, the destroyed ones kept or finished, no extra ship (the fighter
// turret and the carrier's turrets made again take the old parts' proxies), the crate still there with its age.
// Expected values: positions within 10 m the frame they are rebuilt (a ship flies on meanwhile), the hull within 1 %, counts unchanged.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Multiplayer;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class MultiplayerHandOverTests : PlayModeTest
    {
        string savedName;

        [SetUp]
        public void LocalHostSettings()
        {
            savedName = PlayerPrefs.GetString("mp_name", "");
            PlayerPrefs.SetString("mp_name", "Tester");
        }

        [TearDown]
        public void RestoreSettings() => PlayerPrefs.SetString("mp_name", savedName);

        struct Before
        {
            public string model;
            public Vector3 position;
            public float hull;
            public byte life;
        }

        [UnityTest]
        public IEnumerator EveryOrbitObjectSurvivesAHandOver()
        {
            yield return Load("MainMenu");
            Assert.IsTrue(NetGame.CanHost(), "the port is free");
            Assert.IsTrue(NetGame.StartHost(), NetGame.Status);
            yield return WaitFor(() => Find<StationLevel>() != null, 60f, "docked at the session's station");
            yield return Run(2f);
            Find<StationLevel>().Launch();
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null && Find<SpaceLevel>().LaunchCameraOver, 60f, "the orbit");
            var level = Find<SpaceLevel>();
            Assert.IsTrue(level.NetAuthority, "the only player runs the orbit");
            var traffic = level.Traffic;

            // One of each harder kind, ahead of the player.
            var ahead = level.Player.transform.position + level.Player.transform.forward * 400f;
            var at = new Vector3(ahead.x, ahead.y, -ahead.z) / OrbitLayout.MetersPerUnit;
            var list = new List<SpawnSpec>();
            TrafficPlan.AddCapitalShipAt(level.Database, list, SpawnSpec.CapitalCarrier, at + new Vector3(0f, 0f, 60000f), true);
            foreach (var s in list) traffic.SpawnShip(s);
            var assets = CombatAssets.Load();
            var outpost = traffic.SpawnShip(new SpawnSpec
            {
                group = NpcGroup.Outpost, race = Standing.Pirate, ship = -1, position = at + new Vector3(30000f, 0f, 0f),
                fixedObject = "station_pirates", collisionId = 1002, hitRadius = 7500f, hitpoints = 5000,
                wreckPrefab = assets != null ? assets.outpostWreck : null, explosionScale = 8f, stationary = true, nameText = 441,
            });
            var myfft = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = Standing.Pirate, ship = 45, position = at + new Vector3(-20000f, 0f, 0f) });
            var freighter = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Freighter, race = 0, ship = 12, freighter = true, position = at + new Vector3(0f, 5000f, 20000f) });
            var doomed = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = 0, ship = 2, position = at + new Vector3(0f, -5000f, 10000f) });
            var stunned = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = 1, ship = 6, position = at + new Vector3(5000f, 0f, 10000f) });
            traffic.ConnectPlayers();
            var crate = Crate.Spawn(level.Player.transform.position + level.Player.transform.forward * 200f, new[] { new ItemStack(1, 5) }, 0, 0);
            yield return Run(3f);   // their proxies spawned (NetOrbit's scan) and their first status written

            freighter.Target.Damage(freighter.Hp.hull + 1000, true, Vector3.forward);
            doomed.Target.Damage(doomed.Hp.hull + 1000, true, Vector3.forward);
            stunned.OnRemoteEmp(12345, 100000);
            Assert.IsTrue(stunned.Hp.empDisabled, "the EMP-disabled ship");
            yield return Run(1f);
            crate.AgeMs = 20000f;
            yield return Run(1.5f);   // the crate's data written again with its age (NetCrate, every second)

            var proxies = Object.FindObjectsByType<NetProxy>().Where(p => p.IsSpawned && p.IsOwner && p.BoundShip != null).ToList();
            Assert.Greater(proxies.Count, 10, "the traffic and the extras have proxies");
            var before = new Dictionary<NetProxy, Before>();
            foreach (var p in proxies)
                before[p] = new Before { model = p.BoundShip.ModelPath, position = p.BoundShip.Model.position, hull = p.HullFraction, life = p.Life };
            float crateAge = crate.AgeMs;
            int livingBefore = traffic.Ships.Count(s => s != null && !s.Gone && s.Current == NpcShip.State.Fly && !string.IsNullOrEmpty(s.ModelPath));
            var stunnedProxy = proxies.First(p => p.BoundShip == stunned);
            var netCrate = Object.FindObjectsByType<NetCrate>().FirstOrDefault(c => c.IsSpawned);
            Assert.IsNotNull(netCrate, "the crate is shared");
            Assert.IsTrue(before.Values.Any(b => b.life == NetProxy.Dying || b.life == NetProxy.Dead), "a ship being destroyed");

            NetOrbit.Current.SimulateHandOverForTest();
            // Checked the frame they are rebuilt (they fly on from there).
            yield return WaitFor(() => before.All(x => x.Value.life != NetProxy.Flying || (x.Key != null && x.Key.BoundShip != null)), 5f, "every flying object rebuilt");

            foreach (var pair in before)
            {
                var p = pair.Key;
                var b = pair.Value;
                if (b.life != NetProxy.Flying) continue;   // below
                Assert.IsTrue(p != null && p.IsSpawned, $"{b.model} still shown");
                var ship = p.BoundShip;
                Assert.IsNotNull(ship, $"{b.model} rebuilt and driving its proxy");
                Assert.IsFalse(ship.Gone, $"{b.model} flying");
                Assert.AreEqual(b.model, ship.ModelPath, "the same model");
                Assert.Less((ship.Model.position - b.position).magnitude, 10f, $"{b.model} where it was");
                Assert.AreEqual(b.hull, p.HullFraction, 0.01f, $"{b.model}'s hull");
            }
            // The destroyed ones: kept as they are (a wreck stays, a dying ship finishes), never rebuilt alive.
            foreach (var pair in before.Where(x => x.Value.life != NetProxy.Flying))
                Assert.IsTrue(pair.Key == null || !pair.Key.IsSpawned || pair.Key.Kept, $"{pair.Value.model} kept as it was");
            int livingAfter = traffic.Ships.Count(s => s != null && !s.Gone && s.Current == NpcShip.State.Fly && !string.IsNullOrEmpty(s.ModelPath));
            Assert.AreEqual(livingBefore, livingAfter, "no ship more or less (the parts made again took the old proxies)");
            Assert.IsTrue(stunnedProxy.BoundShip != null && stunnedProxy.BoundShip.Hp.empDisabled, "still EMP-disabled");
            yield return Run(1f);   // the crate's takeover (NetCrate's update)
            Assert.IsTrue(netCrate.IsSpawned, "the crate is still there");
            Assert.IsTrue(crate != null && !crate.remote, "and is this game's own again");
            Assert.GreaterOrEqual(crate.AgeMs, crateAge - 500f, "with its age (it doesn't start its 60 s over)");

            NetGame.ShutdownNow();
            yield return Run(1f);
        }
    }
}
