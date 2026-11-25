using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class AsteroidHullContactTests
    {
        static AsteroidHullContact.Box Box(Vector3 centre, Vector3 half, float yaw = 0f) =>
            new AsteroidHullContact.Box(centre, half, Quaternion.Euler(0f, yaw, 0f));

        [Test]
        public void CapitalNoseContactsRockBeyondLegacyProbe()
        {
            var nose = Box(new Vector3(0, 0, 180), new Vector3(12, 8, 30));
            var rock = Box(new Vector3(0, 0, 211), Vector3.one * 3);
            Assert.IsTrue(AsteroidHullContact.Sweep(nose, rock, Vector3.zero, out var normal));
            Assert.Less(normal.z, 0f);
        }

        [Test]
        public void RotatedHullDoesNotHitEmptyBoundingBoxCorner()
        {
            var hull = Box(Vector3.zero, new Vector3(2, 2, 40), 45f);
            var rock = Box(new Vector3(24, 0, -24), Vector3.one * 2);
            Assert.IsFalse(AsteroidHullContact.Sweep(hull, rock, Vector3.zero, out _));
        }

        [Test]
        public void SweptHullContactsRockBetweenFrames()
        {
            var end = Box(new Vector3(0, 0, 50), new Vector3(2, 2, 4));
            var rock = Box(Vector3.zero, Vector3.one * 3);
            Assert.IsFalse(AsteroidHullContact.Sweep(end, rock, Vector3.zero, out _));
            Assert.IsTrue(AsteroidHullContact.Sweep(end, rock, new Vector3(0, 0, 100), out var normal));
            Assert.Less(normal.z, 0f);
        }

        [Test]
        public void SweptHullMissesRockToTheSide()
        {
            var end = Box(new Vector3(20, 0, 50), new Vector3(2, 2, 4));
            var rock = Box(Vector3.zero, Vector3.one * 3);
            Assert.IsFalse(AsteroidHullContact.Sweep(end, rock, new Vector3(0, 0, 100), out _));
        }

        [Test]
        public void GridUsesFinalRadiusAndIncludesCubeCorner()
        {
            var go = new GameObject("test asteroid");
            try
            {
                go.transform.position = new Vector3(400, 400, 400);
                var rock = go.AddComponent<Target>();
                rock.isAsteroid = true;
                rock.hp = 100;
                rock.radius = 1;
                AsteroidTargetGrid.Register(rock);
                rock.radius = 405;
                AsteroidTargetGrid.Register(rock);
                var candidates = new System.Collections.Generic.List<Target>();
                AsteroidTargetGrid.Query(Vector3.zero, 0, candidates);
                Assert.Contains(rock, candidates);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
