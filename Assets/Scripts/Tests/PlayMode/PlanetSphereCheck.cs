// PlanetSphereCheck.cs
// A check against a real mod, not part of the suite: an ES2 Ships orbit whose planet is a lit sphere (stations.json
// "planetSphere", World.Backdrop, GoF2/Es2Planet; the first such station), with the mod Temp/station_life_mod.txt names (as StationLifeCheck; the
// test is ignored without it): the orbit planet is a sphere body, and frames of it through the game's camera go into
// Temp/planet_sphere/*.png to look at (the planet ahead, and turned toward its lit edge).
//   python3 -I Reference/tools/tests/run_tests.py --mode PlayMode --filter GoF2Remake.Tests.PlanetSphereCheck

using System.Collections;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Recording")]
    public class PlanetSphereCheck : PlayModeTest
    {
        const string ModId = "es2_ships";

        string Root => Directory.GetParent(Application.dataPath).FullName;

        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        [UnityTest, Timeout(900000)]
        public IEnumerator TheOrbitPlanetIsALitSphere()
        {
            string named = Path.Combine(Root, "Temp", "station_life_mod.txt");
            if (!File.Exists(named)) Assert.Ignore("No Temp/station_life_mod.txt: no mod to check");
            string source = File.ReadAllText(named).Trim();
            Directory.CreateDirectory(ModManager.UserFolder);
            using (var ln = System.Diagnostics.Process.Start("ln", $"-s \"{source}\" \"{Path.Combine(ModManager.UserFolder, ModId)}\"")) ln.WaitForExit();
            ModManager.Scan();
            ModManager.SetEnabled(ModId, true);
            Database.Load();
            var withSphere = Database.Shared.Stations.FirstOrDefault(st => ModWorld.PlanetSphereOf(st.index) != null);
            Assert.IsNotNull(withSphere, "a mod station with a planetSphere");
            int station = withSphere.index;
            Debug.Log($"Planet sphere: station {station} {withSphere.name}");
            yield return WaitFor(() => ModStations.Ready, 600f, "the mods' models and backdrops");

            FreePlayAt(station);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null && Find<SpaceLevel>().Backdrop != null, 120f, "the level");
            var level = Find<SpaceLevel>();
            level.Player.GetComponent<Flight.Target>().invulnerable = true;
            yield return Run(1f);
            var target = level.Backdrop.PlanetTargets.FirstOrDefault(p => p.orbitPlanet);
            Assert.IsNotNull(target.transform, "the orbit planet");
            var mf = target.transform.GetComponent<MeshFilter>();
            Assert.AreEqual("BackdropSphere", mf.sharedMesh.name, "a sphere, not the quad");
            Assert.AreEqual("GoF2/Es2Planet", target.transform.GetComponent<MeshRenderer>().sharedMaterial.shader.name);

            string folder = Path.Combine(Root, "Temp", "planet_sphere");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var cam = Camera.main.transform.position;
            var to = (target.transform.position - cam).normalized;
            yield return StationLifeCheck.Shot(folder, "ahead", cam, cam + to);
            // toward its lit edge: the planet's centre turned a little toward the sun
            var sun = level.Backdrop.SunDirection;
            var edge = Vector3.Slerp(to, Vector3.ProjectOnPlane(sun, to).normalized, 0.25f);
            yield return StationLifeCheck.Shot(folder, "edge", cam, cam + edge);
            // the point under the sun (when it faces the camera's side): its lit face
            float radius = target.transform.lossyScale.x;
            var lit = target.transform.position + sun * radius;
            yield return StationLifeCheck.Shot(folder, "sunlit", cam, lit);
            var mat = target.transform.GetComponent<MeshRenderer>().sharedMaterial;
            for (int k = 1; k <= 3; k++)
            {
                mat.SetFloat("_Debug", k);
                yield return StationLifeCheck.Shot(folder, "debug" + k, cam, lit);
            }
            mat.SetFloat("_Debug", 0f);
            Debug.Log($"Planet sphere: shots in {folder}; sun . toward camera {Vector3.Dot(sun, -to):0.00}");
        }
    }
}
