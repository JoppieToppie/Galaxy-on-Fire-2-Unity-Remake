// RiftHazardsFlightTests.cs
// The rift's newer mutators (World.RiftEncounter, World/RiftHazards.cs, Flight.RiftRun's ES2 numbers) in the real Space scene:
// a stage with Mines on Death, Corrosive Death, Overheated Systems and Burst Bulbs, and a boss stage with the Phase Barrier,
// Stasis Snares and the Apocalypse. The mutators are set by hand and their timers run short, so each shows up within seconds;
// the player's ship can't be harmed, so the stage runs on without it dying.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class RiftHazardsFlightTests : PlayModeTest
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, Any);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{field} not found");
            f.SetValue(target, value);
        }

        IEnumerator EnterSpace()
        {
            FreePlayAt(78);
            Session.Difficulty = Session.DifficultyNormal;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null && Find<SpaceLevel>().Traffic != null, 60f, "the level");
            Find<SpaceLevel>().Player.GetComponent<Target>().invulnerable = true;
        }

        static RiftEncounter StartAtPlayer(SpaceLevel level, int stages, params string[] mutators)
        {
            var p = level.Player.transform.position;
            var r = RiftEncounter.Start(level, new Vector3(p.x, p.y, -p.z) / 0.05f, stages);
            Assert.IsNotNull(r, "the rift");
            r.Run.active.Clear();
            r.Run.active.AddRange(mutators);
            return r;
        }

        [UnityTest]
        public IEnumerator AStagesHazardsComeFromTheDeadAndTheTimers()
        {
            yield return EnterSpace();
            var level = Find<SpaceLevel>();
            var rift = StartAtPlayer(level, 3, RiftRun.MineDrop, RiftRun.CorrosionField, RiftRun.Overheat, RiftRun.BurstBulbs);
            Assert.IsFalse(rift.Run.IsBossStage);
            Set(rift, "overheatS", Overheat.IntervalS - 0.2f);
            Set(rift, "bulbS", 0.2f);
            yield return Run(0.6f);
            Assert.IsTrue(Overheat.AnyHot, "a slot overheated");
            var bulb = Find<RiftBulb>();
            Assert.IsNotNull(bulb, "a Burst Bulb");
            Assert.IsTrue(Target.RadarObjects.Contains(bulb.Target), "the bulb on the radar");

            // an enemy dies: its mines and its corrosion field
            var enemies = Get<List<NpcShip>>(rift, "enemies");
            yield return WaitFor(() => enemies.Any(e => e != null && e.Target.Alive), 10f, "the stage's enemies");
            var victim = enemies.First(e => e != null && e.Target.Alive);
            victim.Target.Damage(victim.Target.maxHp * 100f);
            yield return Run(0.5f);
            int mines = Object.FindObjectsByType<RiftMine>(FindObjectsSortMode.None).Length;
            Assert.That(mines, Is.InRange(RiftRun.MinesMin, RiftRun.MinesMax), "the mines dropped");
            Assert.AreEqual(1, Object.FindObjectsByType<RiftField>(FindObjectsSortMode.None).Length, "the corrosion field");

            // the bulb shot down: gone from the radar
            bulb.Target.Damage(bulb.Target.maxHp * 100f);
            yield return Run(0.3f);
            Assert.IsFalse(Target.RadarObjects.Contains(bulb.Target));
            Assert.IsNull(Find<RiftBulb>());

            // the run left: its hazards go with it, the overheats cool
            Object.Destroy(rift.gameObject);
            yield return Run(0.2f);
            Assert.IsFalse(Overheat.AnyHot);
            Assert.AreEqual(0, Object.FindObjectsByType<RiftMine>(FindObjectsSortMode.None).Length);
        }

        [UnityTest]
        public IEnumerator ABossStagesBarrierSnaresAndApocalypse()
        {
            yield return EnterSpace();
            var level = Find<SpaceLevel>();
            var rift = StartAtPlayer(level, 2);
            rift.Run.EndStage();   // (a run has two stages at least: on to the boss's)
            Invoke(rift, "BeginStage");
            Assert.IsTrue(rift.Run.IsBossStage);
            rift.Run.active.Clear();
            rift.Run.active.AddRange(new[] { RiftRun.BossEnergyShield, RiftRun.BossWebFields, RiftRun.BossApocalypse });
            var boss = Get<NpcShip>(rift, "boss");
            Assert.IsNotNull(boss, "the boss");
            boss.Target.invulnerable = true;   // (the stage stays up)
            if (Get<GameObject>(rift, "barrier") == null) Invoke(rift, "RaiseBarrier");
            Set(rift, "apocalypseS", 0.1f);
            yield return Run(1f);

            // the barrier: the boss proof against the pilot's shots exactly while the pilot is outside it
            var player = level.Player.transform.position;
            bool inside = (bool)Invoke(rift, "InsideBarrier", player);
            Assert.AreEqual(!inside, boss.Target.playerProof);

            // the snares around the pilot
            var fields = Object.FindObjectsByType<RiftField>(FindObjectsSortMode.None);
            Assert.AreEqual(RiftRun.SnaresMax, fields.Count(f => f.name == "Rift Web"), "the web bubbles");

            // the safe fields form, then the countdown runs
            yield return WaitFor(() => Get<float>(rift, "countdownS") > 0f, 10f, "the Apocalypse countdown");
            int safe = Object.FindObjectsByType<RiftField>(FindObjectsSortMode.None).Count(f => f.name == "Rift Safe");
            Assert.That(safe, Is.InRange(RiftRun.SafeFieldsMin, RiftRun.SafeFieldsMax), "the safe fields");

            Object.Destroy(rift.gameObject);
            yield return Run(0.2f);
            Assert.IsFalse(boss.Target.playerProof, "the barrier gone with the run");
        }
    }
}
