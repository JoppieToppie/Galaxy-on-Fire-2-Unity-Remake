// What an arrival from a warp shows as it comes in (Traffic.Carried, ArrivalScale): by what a ship is, so a mod's ships are
// covered whatever they are, and nothing small is culled into popping in later.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ArrivalTrafficTests
    {
        static SpawnSpec Friendly(NpcGroup group) => new SpawnSpec { group = group, race = 0, alwaysFriend = true };

        [Test]
        public void AnythingFriendlyInTheOrbitIsCarriedWhateverItIs()
        {
            Assert.IsTrue(Traffic.Carried(Friendly(NpcGroup.Local)));
            Assert.IsTrue(Traffic.Carried(Friendly(NpcGroup.Freighter)));
            var mod = Friendly(NpcGroup.Freighter);
            mod.modCapital = true;
            mod.scale = 2.5f;
            Assert.IsTrue(Traffic.Carried(mod), "a mod's capital-class freighter, at its own scale");
            var capital = Friendly(NpcGroup.Local);
            capital.capital = 1;
            Assert.IsTrue(Traffic.Carried(capital));
        }

        [Test]
        public void WhatIsNotThereYetOrIsHostileIsNot()
        {
            var jumper = Friendly(NpcGroup.Jumper);
            jumper.startsDead = true;
            Assert.IsFalse(Traffic.Carried(jumper));
            var part = Friendly(NpcGroup.Local);
            part.capitalHost = new SpawnSpec();
            Assert.IsFalse(Traffic.Carried(part));
            var enemy = Friendly(NpcGroup.Local);
            enemy.alwaysFriend = false;
            enemy.alwaysEnemy = true;
            Assert.IsFalse(Traffic.Carried(enemy));
        }

        [Test]
        public void AScaledThingIsNotCulledBySizeUntilRestored()
        {
            var go = new GameObject("lod");
            try
            {
                var group = go.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.5f, new Renderer[0]), new LOD(0.05f, new Renderer[0]) });
                var first = new ArrivalScale();
                first.Add(go.transform);
                var levels = group.GetLODs();
                Assert.AreEqual(0f, levels[1].screenRelativeTransitionHeight, "the last level stays to the smallest size");
                Assert.AreEqual(0.5f, levels[0].screenRelativeTransitionHeight, 0.001f);
                var second = new ArrivalScale();   // a stand-in's model, scaled again by the arrival's traffic
                second.Add(go.transform);
                second.Restore();
                Assert.AreEqual(0.05f, group.GetLODs()[1].screenRelativeTransitionHeight, 0.001f, "culled as before once restored");
                first.Restore();
                Assert.AreEqual(0.05f, group.GetLODs()[1].screenRelativeTransitionHeight, 0.001f, "and only once");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AStandInIsAModelUntilTheShipTakesItOver()
        {
            var go = new GameObject("stand-in");
            try
            {
                var effect = go.AddComponent<GoF2Remake.Modding.Es2EngineVfx>();
                var trail = go.AddComponent<TrailRenderer>();
                var spec = new SpawnSpec { standIn = go };
                typeof(SystemApproach).GetMethod("Sleep", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { spec });
                Assert.IsFalse(effect.enabled, "a mod's effect doesn't run on the way in");
                Assert.IsFalse(trail.emitting);
                SystemApproach.Wake(spec);
                Assert.IsTrue(effect.enabled, "all of it runs on the ship");
                Assert.IsTrue(trail.emitting);
                Assert.IsNull(spec.standInAsleep);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void WhatComesInLateGrowsOutOfNothing()
        {
            var go = new GameObject("late");
            try
            {
                var scale = new ArrivalScale();
                scale.Apply(0.5f);
                scale.Add(go.transform, true);
                Assert.AreEqual(0f, go.transform.localScale.x, "not there at once");
                var other = new GameObject("there");
                scale.Add(other.transform);
                Assert.AreEqual(0.5f, other.transform.localScale.x, "built ahead: already the size all else is");
                Object.DestroyImmediate(other);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
