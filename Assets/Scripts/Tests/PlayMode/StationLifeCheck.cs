// StationLifeCheck.cs
// A check against a real mod, not part of the suite: an ES2 Ships station's life (World.StationLife) in its orbit, with the
// mod the file Temp/station_life_mod.txt names (its folder, linked into the test's sandbox; the test is ignored without it):
//   every loop's drones there; every weld spot on the station's hull where the remake shows it (a ray from WeldOffsetM off
//   the spot back along its normal meets the hull within Tolerance of the spot: the spots are the importer's traces in the
//   model's glTF frame, so this checks that frame, glTFast's mirrored x and the model's turn, scale and centring); then a
//   drone welding, captured with its own camera into Temp/station_life/*.png to look at.
//   python3 -I Reference/tools/tests/run_tests.py --mode PlayMode --filter GoF2Remake.Tests.StationLifeCheck

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Recording")]
    public class StationLifeCheck : PlayModeTest
    {
        const string ModId = "es2_ships", Station = "es2_ships:es2_alcyone_station";
        /// <summary>How far (the model's metres) a spot may lie off the hull: the importer's spots are on its triangles.</summary>
        const float Tolerance = 0.5f;

        string Root => Directory.GetParent(Application.dataPath).FullName;

        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        [UnityTest, Timeout(900000)]
        public IEnumerator TheDronesFlyAndWeldOnTheHull()
        {
            string named = Path.Combine(Root, "Temp", "station_life_mod.txt");
            if (!File.Exists(named)) Assert.Ignore("No Temp/station_life_mod.txt: no mod to check");
            string source = File.ReadAllText(named).Trim();
            Directory.CreateDirectory(ModManager.UserFolder);
            using (var ln = System.Diagnostics.Process.Start("ln", $"-s \"{source}\" \"{Path.Combine(ModManager.UserFolder, ModId)}\"")) ln.WaitForExit();
            ModManager.Scan();
            ModManager.SetEnabled(ModId, true);
            Assert.IsTrue(ModWorld.TryResolveStation(Station, out int station), "the mod's station");
            yield return WaitFor(() => ModStations.Ready, 600f, "the mods' station models");
            Assert.IsNotNull(ModStations.LifeUnit(station), "the station's drone (its model's scene \"life\")");

            FreePlayAt(station);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null && Find<StationLife>() != null, 120f, "the station's life");
            var level = Find<SpaceLevel>();
            level.Player.GetComponent<Target>().invulnerable = true;
            var life = Find<StationLife>();
            var spec = ModWorld.ModelOf(station).life;
            Assert.AreEqual(spec.loops.Sum(l => l.count), life.Drones.Count(), "every loop's drones");

            // the spots on the hull: the station's meshes as colliders for the check
            var hull = life.transform.parent;
            var colliders = new List<MeshCollider>();
            foreach (var mf in hull.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.GetComponentInParent<StationLife>() == null) { var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; colliders.Add(c); }
            Physics.SyncTransforms();
            float scale = life.transform.lossyScale.x;
            var errors = new List<float>();
            int missed = 0;
            foreach (var loop in spec.loops)
                foreach (var (at, n) in loop.spots)
                {
                    var p = life.transform.TransformPoint(StationLife.FromGltf(at));
                    var dir = life.transform.TransformDirection(StationLife.FromGltf(n)).normalized;
                    var from = p + dir * StationLife.WeldOffsetM * scale;
                    if (Physics.Raycast(from, -dir, out var hit, 4f * StationLife.WeldOffsetM * scale)) errors.Add(Mathf.Abs(hit.distance / scale - StationLife.WeldOffsetM));
                    else missed++;
                }
            foreach (var c in colliders) Object.Destroy(c);
            errors.Sort();
            Debug.Log($"Station life: {life.Drones.Count()} drones, {errors.Count + missed} spots, {missed} missed, error median {(errors.Count > 0 ? errors[errors.Count / 2] : -1f):0.000} m, max {(errors.Count > 0 ? errors[errors.Count - 1] : -1f):0.000} m (scale {scale:0.00})");
            Assert.Greater(errors.Count, 0, "spots to check");
            Assert.LessOrEqual(missed, errors.Count / 10, "the spots' rays meet the hull");
            Assert.Less(errors[errors.Count / 2], Tolerance, "the spots lie on the hull");

            // a drone welding, to look at
            string folder = Path.Combine(Root, "Temp", "station_life");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            yield return Shot(folder, "overview", hull.position + (hull.rotation * new Vector3(0.6f, 0.35f, -1f)).normalized * 600f * scale, hull.position);
            StationLife.Drone welder = null;
            foreach (var d in life.Drones) if (life.TryWeld(d)) { welder = d; break; }
            if (welder == null)
                foreach (var d in life.Drones) { d.cooldownS = 0f; if (life.TryWeld(d)) { welder = d; break; } }
            Assert.IsNotNull(welder, "a drone with a weld spot ahead");
            yield return WaitFor(() => welder.state == StationLife.State.Welding, 60f, "the drone at its spot");
            yield return Run(0.5f);
            var t = welder.transform;
            // from the side, between the drone and its spot (the sparks fly off the hull there)
            var mid = t.position + t.forward * StationLife.WeldOffsetM * 0.5f * scale;
            var eye = mid + (t.rotation * new Vector3(1f, 0.4f, 0.2f)).normalized * 20f * scale;
            for (int i = 0; i < 3; i++)
            {
                yield return Shot(folder, "weld" + i, eye, mid);
                yield return Run(0.4f);
            }
            Debug.Log("Station life: shots in " + folder);
        }

        /// <summary>A frame from 'eye' looking at 'at', through the game's own camera (moved for the frame and put back): its
        /// post-processing, and the backdrop (sky, sun, planet) placed round it as the game places it (Backdrop re-places them
        /// as the camera begins rendering). A camera of the check's own saw the planet's quad off its axis and no tonemapping.</summary>
        internal static IEnumerator Shot(string folder, string name, Vector3 eye, Vector3 at)
        {
            yield return new WaitForEndOfFrame();
            var cam = Camera.main;
            Assert.IsNotNull(cam, "the game's camera");
            var t = cam.transform;
            var (pos, rot, target) = (t.position, t.rotation, cam.targetTexture);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            t.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = target;
            t.SetPositionAndRotation(pos, rot);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(rt); Object.Destroy(tex);
        }
    }
}
