// ModDriftFxTests.cs
// Feature: docs/notes/mods-models.md "Drift energy field": ships.json "driftFx" (CustomDriftFx) puts Chorus's drift effects
// on nodes of the player's ship model while it drifts (World.ModDriftFx): looping, local-space emitters with a start
// point, a light that flashes again until the drift ends, and energy-field shells (a JSON mesh on GoF2/ChorusDriftField).

using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModDriftFxTests
    {
        const string Ship = "{ \"id\": \"forsaken\", \"driftFx\": { \"parts\": [ { \"node\": \"SCK_VFX_Drift_L2\", \"fx\": { \"emitters\": [ " +
            "{ \"image\": \"fx/glow.png\", \"bursts\": [0, 1], \"duration\": 1, \"life\": [1.2, 1.2], \"size\": [72, 72], \"loop\": true, " +
            "\"local\": true, \"at\": [-2.16, 0, 0], \"inherit\": 0 }, " +
            "{ \"image\": \"fx/spark.png\", \"bursts\": [0, 100], \"duration\": 2, \"life\": [0.15, 0.35], \"local\": true, \"inherit\": 0 }, " +
            "{ \"light\": 288, \"bursts\": [0, 1], \"duration\": 1, \"life\": [1, 1], \"loop\": true, \"color\": [0.4, 1, 0.9] } ] }, " +
            "\"fields\": [ { \"mesh\": \"fx/drift/field_l_0.json\", \"band\": \"fx/drift/band.png\", \"color\": [0.4, 1, 0.9, 1], \"exposure\": 3 } ] } ] } }";

        [Test]
        public void TheEntryIsRead()
        {
            var s = JsonUtility.FromJson<CustomShipData>(Ship);
            var part = s.driftFx.parts[0];
            Assert.AreEqual("SCK_VFX_Drift_L2", part.node);
            Assert.AreEqual(3, part.fx.emitters.Length);
            Assert.IsTrue(part.fx.emitters[0].loop);
            Assert.IsTrue(part.fx.emitters[0].local);
            Assert.AreEqual(new[] { -2.16f, 0f, 0f }, part.fx.emitters[0].at);
            Assert.IsFalse(part.fx.emitters[1].loop, "once by default");
            var f = part.fields[0];
            Assert.AreEqual(3f, f.exposure);
            Assert.AreEqual(0.5f, f.offset, "Chorus's Upositon by default");
            Assert.AreEqual(new[] { 1f, 0.12f }, f.tiling);
            Assert.AreEqual(new[] { 2f, 1f, 1f, 12f }, f.coreLine);
            Assert.AreEqual(1.6f, f.fresnel);
            var none = JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\" }").driftFx;
            Assert.IsTrue(none == null || none.parts == null || none.parts.Length == 0, "none: no drift look");
        }

        [Test]
        public void ALoopingLocalEmitterStartsAtItsPoint()
        {
            var e = JsonUtility.FromJson<CustomShipData>(Ship).driftFx.parts[0].fx.emitters[0];
            var root = new GameObject("fx");
            try
            {
                var ps = ModDeathFx.Build(null, e, root.transform, Vector3.zero, 1f, out _);
                Assert.IsTrue(ps.main.loop, "again every duration");
                Assert.AreEqual(ParticleSystemSimulationSpace.Local, ps.main.simulationSpace, "it moves with the ship");
                Assert.AreEqual(new Vector3(-2.16f, 0f, 0f) * 0.05f, ps.shape.position);
                Assert.GreaterOrEqual(ps.main.maxParticles, 3, "the last cycle's still alive (life 1.2 over a 1 s loop)");
                var once = ModDeathFx.Build(null, JsonUtility.FromJson<CustomFxEmitter>("{ \"bursts\": [0, 1] }"), root.transform, Vector3.zero, 1f, out _);
                Assert.IsFalse(once.main.loop);
                Assert.AreEqual(ParticleSystemSimulationSpace.World, once.main.simulationSpace, "the world's by default");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ALoopingLightFlashesAgainUntilStopped()
        {
            var e = JsonUtility.FromJson<CustomFxEmitter>("{ \"light\": 288, \"bursts\": [0, 1], \"duration\": 1, \"life\": [0.5, 0.5], \"loop\": true }");
            var root = new GameObject("fx");
            try
            {
                var f = FxFlash.Spawn(e, root.transform, 1f);
                Assert.IsTrue(f.Looping);
                f.Step(0.2f);
                Assert.IsTrue(f.Light.enabled);
                f.Step(0.5f);
                Assert.IsFalse(f.Light.enabled, "dark between its flashes");
                f.Step(0.5f);
                Assert.IsTrue(f.Light.enabled, "the next flash");
                f.StopLooping();
                f.Step(0.3f);
                Assert.IsFalse(f.Light.enabled, "no flash after the one it was in");
                f.Step(1f);
                Assert.IsFalse(f.Light.enabled);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ItSitsOnItsNodeAndStops()
        {
            var spec = JsonUtility.FromJson<CustomShipData>(Ship).driftFx;
            var model = new GameObject("model");
            try
            {
                var wing = new GameObject("BONE_SM_Phoenix_wingLow_l1").transform;
                wing.SetParent(model.transform, false);
                wing.localPosition = new Vector3(1f, 2f, 3f);
                var socket = new GameObject("SCK_VFX_Drift_L2").transform;
                socket.SetParent(wing, false);
                socket.localPosition = new Vector3(0.5f, 0f, 0f);
                socket.localRotation = Quaternion.Euler(0f, 90f, 0f);
                model.transform.localScale = Vector3.one * 3.6f;
                var d = ModDriftFx.Start(null, spec, model.transform);
                Assert.AreEqual(1, d.Roots.Count);
                var root = d.Roots[0].transform;
                Assert.AreSame(socket, root.parent, "on its node (it rides the wing)");
                Assert.AreEqual(socket.position, root.position);
                Assert.AreEqual(1f, root.lossyScale.x, 1e-4f, "the emitters' game units hold");
                Assert.AreEqual(2, d.Systems.Count, "the dots and the sparks");
                Assert.AreEqual(1, d.Lights.Count);
                Assert.AreEqual(0, d.Fields.Count, "no mod: no shell mesh to read");
                d.Stop();
                foreach (var ps in d.Systems) Assert.IsFalse(ps.isEmitting, "stopped: the particles live out");
                Assert.IsFalse(d.Lights[0].Looping);
                var missing = new CustomDriftFx { parts = new[] { new CustomDriftPart { node = "no such node" } } };
                var d2 = ModDriftFx.Start(null, missing, model.transform);
                Assert.AreSame(model.transform, d2.Roots[0].transform.parent, "a missing node: the model itself");
                d2.Clear();
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void AShellMeshIsBuiltFromJson()
        {
            var m = JsonUtility.FromJson<CustomFxMesh>("{ \"positions\": [0, 0, 0, 20, 0, 0, 0, 20, 0], \"normals\": [0, 0, -2, 0, 0, -1, 0, 0, -1], " +
                "\"uvs\": [0, 0, 1, 0, 0, 1], \"triangles\": [0, 1, 2] }");
            var mesh = ModDriftFx.BuildMesh(m);
            try
            {
                Assert.AreEqual(3, mesh.vertexCount);
                Assert.AreEqual(new Vector3(1f, 0f, 0f), mesh.vertices[1], "game units to metres");
                Assert.AreEqual(new Vector3(0f, 0f, -1f), mesh.normals[0], "normalised");
                Assert.AreEqual(new Vector2(0f, 1f), mesh.uv[2]);
                Assert.IsNull(ModDriftFx.BuildMesh(JsonUtility.FromJson<CustomFxMesh>("{ \"positions\": [0, 0, 0, 1, 0, 0, 0, 1, 0], \"triangles\": [0, 1, 3] }")),
                    "an index past its vertices");
                Assert.IsNull(ModDriftFx.BuildMesh(new CustomFxMesh()));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void AShellsMaterialCarriesItsValues()
        {
            var f = JsonUtility.FromJson<CustomShipData>(Ship).driftFx.parts[0].fields[0];
            var m = ModDriftFx.BuildMaterial(null, f);
            try
            {
                Assert.AreEqual("GoF2/ChorusDriftField", m.shader.name);
                Assert.AreEqual(new Vector4(0.4f, 1f, 0.9f, 1f), m.GetVector("_DriftTint"));
                Assert.AreEqual(new Vector4(0.5f, 0.2f, 1f, 0.12f), m.GetVector("_DriftA"), "Upositon, Panning V, Tiling U / V");
                Assert.AreEqual(new Vector4(1f, 5f, 2f, 0.06f), m.GetVector("_DriftB"));
                Assert.AreEqual(new Vector4(1f, 1f, 12f, 1.6f), m.GetVector("_DriftC"));
                Assert.AreEqual(new Vector4(1.5f, 0.45f, 1.5f, 2f), m.GetVector("_DriftD"));
                Assert.AreEqual(3f, m.GetVector("_DriftE").z, "its exposure");
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
