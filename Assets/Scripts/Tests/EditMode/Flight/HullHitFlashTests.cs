// HullHitFlashTests.cs
// Feature: Modding/README.md materials "hit" (Chorus's M_Phoenix hit flash; GoF2/Hull HitFlash, World.HullHitFlash):
// expected, a material's hit block sets the hull shader's flash (radius in metres, duration, its pattern in the wave map's
// alpha or none); a hit on the hull puts the flash where the shot came in on the hull's box and ages it 0..1 over its
// duration; ships without the block are left alone.

using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class HullHitFlashTests
    {
        [Test]
        public void AHitPointIsOnTheBoxFacingTheShooter()
        {
            var box = new Bounds(Vector3.zero, new Vector3(10f, 4f, 20f));
            Assert.AreEqual(new Vector3(0f, 0f, 10f), HullHitFlash.PointOn(box, Vector3.forward), "from ahead: the nose");
            Assert.AreEqual(new Vector3(5f, 0f, 0f), HullHitFlash.PointOn(box, Vector3.right));
            var slant = HullHitFlash.PointOn(box, new Vector3(1f, 1f, 0f).normalized);
            Assert.AreEqual(2f, slant.y, 1e-4f, "the nearer face");
        }

        [Test]
        public void TheFlashAgesOverItsDurationOnHullMaterialsWithTheBlock()
        {
            var shader = Shader.Find("GoF2/Hull");
            Assert.IsNotNull(shader);
            var flashMat = new Material(shader);
            flashMat.SetVector("_HitFx", new Vector4(1f, 32f, 0.04f, 2f));
            var plainMat = new Material(shader);
            var ship = new GameObject("ship");
            try
            {
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube); hull.transform.SetParent(ship.transform);
                hull.GetComponent<Renderer>().sharedMaterial = flashMat;
                var other = GameObject.CreatePrimitive(PrimitiveType.Cube); other.transform.SetParent(ship.transform);
                other.GetComponent<Renderer>().sharedMaterial = plainMat;
                var f = HullHitFlash.For(ship);
                Assert.AreSame(f, HullHitFlash.For(ship), "one a ship");
                Assert.AreEqual(1, f.Rescan(), "only the renderer with a hit block");
                f.Flash(Vector3.back);   // (a shot flying backward: it came from ahead)
                Assert.AreEqual(0f, f.AgeOf(0), "lit at once");
                var block = new MaterialPropertyBlock();
                hull.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(0.5f, block.GetVector("_HitPos").z, 1e-4f, "on the cube's front face");
                f.Step(0.02f);
                Assert.AreEqual(0.5f, f.AgeOf(0), 1e-4f, "halfway through its 0.04 s");
                f.Step(0.05f);
                Assert.AreEqual(1f, f.AgeOf(0), "over");
                other.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.IsTrue(block.isEmpty, "the plain one untouched");
            }
            finally { Object.DestroyImmediate(ship); Object.DestroyImmediate(flashMat); Object.DestroyImmediate(plainMat); }
        }

        [Test]
        public void TheHitBlockIsRead()
        {
            var e = JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"x\", \"hit\": { \"color\": [750, 0, 0], \"radius\": 648, \"pattern\": true } }");
            Assert.AreEqual(648f, e.hit.radius);
            Assert.AreEqual(0.04f, e.hit.duration, 1e-6f, "M_Phoenix's HitEffectDuration");
            Assert.IsTrue(e.hit.pattern);
            Assert.IsNull(JsonUtility.FromJson<CustomShipMaterial>("{ \"material\": \"x\" }").hit?.color);
        }
    }
}
