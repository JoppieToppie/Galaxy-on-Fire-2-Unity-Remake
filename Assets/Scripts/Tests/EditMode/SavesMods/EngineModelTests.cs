// EngineModelTests.cs
// Feature: CLAUDE.md "Mods" > Engine effect, GoF3's engines as GoF3 draws them: an "engineFx" with "meshes" (EngineModel,
// the GoF3 importer's file of an engine scene's parts) is read once per set of mods, and EngineFx then draws each part's own
// mesh at every exhaust in GoF3's engine frame (+z backward: the holder's z scale negative), x "meshScale" x the mount's
// fxScale, with GoF2/ModEngineMesh, an exhaust style's colours on each renderer (the helix the second); a sprite keeps its corner offsets (uv1); an
// animation channel samples its keys linearly (EngineChannel.Sample). GoF3's Fork emitters ("trailParticles" /
// "contrailParticles"): particles born at the rate along the path the exhaust moved, a quad each in the wake, none past "max",
// none while the ship stands still; a part's "intensity" is its material's _Intensity (4 without one). The steering bend
// (EVERSPACE 2's, on GoF3's engines too: EngineFlame.SteerBend): the tip against the velocity, x the thrust's forward
// share, at most EngineFlame.SteerShare of the length; each exhaust gets its nozzle, its way back and its parts' length.
// Expected values: the format in Modding/README.md "Engine effect".

using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class EngineModelTests : SavesModsTest
    {
        // a coloured tube's ring (no texture: its DiffuseColor) that flickers out, and a sprite at the nozzle (named as GoF3's helix)
        internal const string EngineJson = @"{ 'source': 'test', 'parts': [
            { 'name': 'body', 'pos': [0, 0, 0.5], 'rot': [0, 0, 0, 1], 'scale': [1, 1, 1],
              'v': [1, 0, 0,  0, 1, 0,  -1, 0, 4], 'n': [1, 0, 0,  0, 1, 0,  -1, 0, 0], 'uv': [0, 0,  1, 0,  0, 1],
              'c': [1, 0.5, 0, 1,  1, 0.5, 0, 1,  0, 0, 0, 1], 'i': [0, 1, 2],
              'useTex': false, 'angle': 1, 'pulsate': true, 'pulseMin': 0.95, 'pulseMax': 1, 'pulseFreq': 25, 'opacity': 0.7,
              'diffuse': [1, 1, 1, 1], 'blend': 2, 'intensity': 1.5,
              'anims': [ { 'kind': 'off', 'length': 1000, 'channels': [ { 'name': 'alpha', 'keys': [0, 1, 500, 0.2, 1000, 0] } ] } ] },
            { 'name': 'engine_spiral', 'pos': [0, 0, 0], 'rot': [0, 0, 0, 1], 'scale': [1, 1, 1],
              'v': [0, 0, 0,  0, 0, 0,  0, 0, 0,  0, 0, 0], 'n': [0, 0, 1,  0, 0, 1,  0, 0, 1,  0, 0, 1],
              'uv': [0, 0,  1, 0,  1, 1,  0, 1], 'o': [-4.5, -4.5,  4.5, -4.5,  4.5, 4.5,  -4.5, 4.5],
              'c': [1, 1, 1, 1,  1, 1, 1, 1,  1, 1, 1, 1,  1, 1, 1, 1], 'i': [0, 1, 2, 0, 2, 3],
              'sprite': true, 'useTex': false, 'angle': 2, 'angleMin': 0.4, 'angleMax': 0.7, 'diffuse': [0, 0.8, 1, 1] }
        ] }";

        ModInfo EngineModOn()
        {
            string dir = WriteMod("eng", "t_eng", "1.0", "", ("fx/engines/e.json", EngineJson));
            ScanAndEnable("t_eng");
            var mod = Installed("t_eng");
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            return mod;
        }

        [Test]
        public void AnEngineFileIsReadWithItsPartsAndAnimations()
        {
            var mod = EngineModOn();
            var f = EngineModels.Get(mod, "fx/engines/e.json");
            Assert.IsNotNull(f, AllMessages(mod));
            Assert.AreEqual(2, f.parts.Length);
            Assert.IsTrue(f.parts[1].sprite);
            Assert.AreEqual(2, f.parts[1].angle);
            var alpha = f.parts[0].anims[0].channels[0];
            Assert.AreEqual(1f, alpha.Sample(-10f), 1e-5f, "held before its first key");
            Assert.AreEqual(0.6f, alpha.Sample(250f), 1e-5f, "linear between keys");
            Assert.AreEqual(0f, alpha.Sample(5000f), 1e-5f, "held after its last key");
            CollectionAssert.IsEmpty(EngineModels.Textures(mod, "fx/engines/e.json"), "no textures to preload");
            Assert.IsNull(EngineModels.Get(mod, "fx/engines/missing.json"));
            Assert.IsTrue(mod.Warnings.Exists(w => w.Contains("missing.json")), "a missing file is a warning");
        }

        [Test]
        public void TheEnginesPartsAreDrawnAtEachExhaustInGoF3sFrame()
        {
            var mod = EngineModOn();
            var ship = new GameObject("ship");
            var glow = new GameObject("engine_glow");
            glow.transform.SetParent(ship.transform, false);
            try
            {
                var fx = new CustomEngineFx { meshes = "fx/engines/e.json", meshScale = 50f };
                var mount = new WeaponMount { slotType = 3, position_engine = new[] { 0, 0, -400 }, fxScale = 2f };
                var e = EngineFx.Create(ship, glow, fx, new[] { mount }, mod, "test", m => m.fxScale, main: Color.red, second: new Color(0f, 0.5f, 1f));
                Assert.IsNotNull(e);
                typeof(EngineFx).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, null);
                var holder = glow.transform.Find("engine_fx_mesh_0");
                Assert.IsNotNull(holder, "the parts' holder at the exhaust");
                Assert.AreEqual(50f * 0.05f * 2f, holder.localScale.x, 1e-4f, "meshScale x metres x fxScale");
                Assert.Less(holder.localScale.z, 0f, "GoF3's +z is backward: the mount's -z");
                Assert.AreEqual(-400f * 0.05f, holder.localPosition.z, 1e-3f, "at the mount");
                Assert.AreEqual(2, holder.childCount);
                var sprite = holder.GetChild(1).GetComponent<MeshFilter>().sharedMesh;
                var offsets = new System.Collections.Generic.List<Vector2>();
                sprite.GetUVs(1, offsets);
                Assert.AreEqual(new Vector2(4.5f, 4.5f), offsets[2], "the sprite's corner offsets");
                Assert.Greater(sprite.bounds.extents.x, 4f, "its bounds reach its corners");
                var mat = holder.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreEqual("GoF2/ModEngineMesh", mat.shader.name);
                Assert.AreEqual(0.7f, mat.GetFloat("_Opacity"), 1e-5f);
                Assert.AreEqual(1f, mat.GetFloat("_Pulsate"));
                Assert.AreEqual(1.5f, mat.GetFloat("_Intensity"), 1e-5f, "the part's own intensity");
                Assert.AreEqual(4f, holder.GetChild(1).GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Intensity"), 1e-5f, "4 without one");
                // an exhaust style's colours on each renderer: the helix the second, the rest the first
                var block = new MaterialPropertyBlock();
                holder.GetChild(0).GetComponent<MeshRenderer>().GetPropertyBlock(block);
                Assert.AreEqual(Color.red, block.GetColor("_Tint"), "the first colour on the body");
                holder.GetChild(1).GetComponent<MeshRenderer>().GetPropertyBlock(block);
                Assert.AreEqual(new Color(0f, 0.5f, 1f, 1f), block.GetColor("_Tint"), "the second on the helix");            }
            finally { Object.DestroyImmediate(ship); }
        }

        [Test]
        public void TheSteeringBendLeansTheTipAgainstTheVelocityWhileThrustingForward()
        {
            var v = new Vector3(30f, 0f, 0f);
            var bend = EngineFlame.SteerBend(v, new Vector3(0f, 0f, 1f));
            Assert.AreEqual(-EngineFlame.SteerShare * 30f / EngineFlame.SteerTopSpeed, bend.x, 1e-5f, "against the velocity, by its share of the top speed");
            Assert.AreEqual(0f, bend.y, 1e-6f); Assert.AreEqual(0f, bend.z, 1e-6f);
            Assert.AreEqual(EngineFlame.SteerShare, EngineFlame.SteerBend(new Vector3(0f, 500f, 0f), Vector3.forward).magnitude, 1e-5f,
                            "at most SteerShare of the length (the top speed's)");
            Assert.AreEqual(Vector3.zero, EngineFlame.SteerBend(v, Vector3.zero), "no thrust, no bend");
            Assert.AreEqual(Vector3.zero, EngineFlame.SteerBend(v, new Vector3(0f, 0f, -1f)), "nor thrusting backward");
            Assert.AreEqual(bend.x * Mathf.Sqrt(0.5f), EngineFlame.SteerBend(v, new Vector3(1f, 0f, 1f)).x, 1e-5f, "x the thrust's forward share");
        }

        [Test]
        public void EachExhaustGivesTheBendItsNozzleAndLength()
        {
            var mod = EngineModOn();
            var ship = new GameObject("ship");
            var glow = new GameObject("engine_glow");
            glow.transform.SetParent(ship.transform, false);
            try
            {
                var fx = new CustomEngineFx { meshes = "fx/engines/e.json", meshScale = 50f };
                var mount = new WeaponMount { slotType = 3, position_engine = new[] { 0, 0, -400 }, fxScale = 2f };
                var e = EngineFx.Create(ship, glow, fx, new[] { mount }, mod, "test", m => m.fxScale);
                typeof(EngineFx).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(e, null);
                var holder = glow.transform.Find("engine_fx_mesh_0");
                var block = new MaterialPropertyBlock();
                holder.GetChild(0).GetComponent<MeshRenderer>().GetPropertyBlock(block);
                Vector4 origin = block.GetVector("_BendOrigin"), axis = block.GetVector("_BendAxis");
                Assert.Less(Vector3.Distance(holder.position, origin), 1e-4f, "from the nozzle");
                Assert.Less(Vector3.Distance(Vector3.back, axis), 1e-4f, "backward along the exhaust");
                // the body's far end: 0.5 + 4 GoF3 units along it, x the holder's length this frame
                Assert.AreEqual(4.5f * Mathf.Abs(holder.lossyScale.z), axis.w, 1e-3f, "the parts' length");
                Assert.AreEqual(Vector4.zero, block.GetVector("_SteerBend"), "no bend without the player's ship");
            }
            finally { Object.DestroyImmediate(ship); }
        }

        [Test]
        public void ATrailOfForkParticlesFollowsTheExhaustsWake()
        {
            string dir = WriteMod("fork", "t_fork", "1.0", "");
            var tex = new Texture2D(4, 4);
            WriteFile(dir, "fx/particles/beam.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            ScanAndEnable("t_fork");
            var mod = Installed("t_fork");
            var ship = new GameObject("ship");
            var glow = new GameObject("engine_glow");
            glow.transform.SetParent(ship.transform, false);
            try
            {
                var trail = new CustomForkEmitter
                {
                    texture = "fx/particles/beam.png", life = new[] { 1.8f, 1.8f }, sizeStart = new[] { 5f, 5f },
                    sizeMid = new[] { 5f, 5f }, sizeEnd = new[] { 5f, 5f }, sizeMidTime = 0.62f, rate = 30f, max = 60,
                    colors = new[] { 0.16f, 1f, 0.96f, 1f, 0f, 0.33f, 0.93f, 0.22f, 0.06f, 0.11f, 0.45f, 0.1f, 0f, 0f, 0f, 0f },
                    colorTimes = new[] { 0f, 0.0145f, 0.73f, 1f },
                };
                var fx = new CustomEngineFx { meshScale = 50f, trailParticles = trail };
                var mount = new WeaponMount { slotType = 3, position_engine = new[] { 0, 0, -400 } };
                var e = EngineFx.Create(ship, glow, fx, new[] { mount }, mod, "test", m => 1f);
                Assert.IsNotNull(e, AllMessages(mod));
                var late = typeof(EngineFx).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                late.Invoke(e, null);
                var run = glow.transform.Find("engine_fx_fork_0");
                Assert.IsNotNull(run, "a Fork run at the exhaust");
                var update = typeof(EngineFx).GetMethod("UpdateFork", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int k = 0; k < 5; k++) update.Invoke(e, new object[] { 0.1f, null, true });
                Assert.AreEqual(0, run.GetComponent<MeshFilter>().sharedMesh.vertexCount, "no wake behind a ship standing still");
                for (int k = 0; k < 10; k++)   // a second, flying forward
                {
                    ship.transform.position += Vector3.forward * 10f;
                    update.Invoke(e, new object[] { 0.1f, null, true });
                }
                var mesh = run.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(30 * 4, mesh.vertexCount, 16, "30 a second, a quad each (and the first frame's few)");
                Assert.Greater(mesh.bounds.size.z, 80f, "spread along the 100 m flown");
                for (int k = 0; k < 30; k++) update.Invoke(e, new object[] { 0.1f, null, true });
                Assert.LessOrEqual(mesh.vertexCount, 60 * 4, "never past max");
                var colour = typeof(EngineFx).GetMethod("ForkColour", BindingFlags.Static | BindingFlags.NonPublic);
                var mid = (Color)colour.Invoke(null, new object[] { trail, 0.73f });
                Assert.AreEqual(0.45f, mid.b, 1e-3f, "the third key at its time");
            }
            finally { Object.DestroyImmediate(ship); }
        }
    }
}
