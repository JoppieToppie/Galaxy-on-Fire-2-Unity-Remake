// ChorusWorldCheck.cs
// A check against a real mod, not part of the suite: the Chorus importer's systems (docs/notes/mods-world.md "Chorus skies"
// and "Chorus planets"), with the built mod's folder named in Temp/chorus_world_mod.txt (ignored without it). Each Chorus
// station's orbit in turn: its planet is GoF2/ChorusPlanet (and its shell GoF2/ChorusAtmosphere where it has one), the sky
// is GoF2/SpaceSky with _CHORUS_SKY; frames through the game's camera go into Temp/chorus_world/<station>_*.png: the planet,
// its lit edge, the sky live and the same view with "Live mod skies" off (the baked panorama).
//   python3 -I Reference/tools/tests/run_tests.py --mode PlayMode --filter GoF2Remake.Tests.ChorusWorldCheck

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
    public class ChorusWorldCheck : PlayModeTest
    {
        const string ModId = "chorus_ships";

        string Root => Directory.GetParent(Application.dataPath).FullName;

        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        [UnityTest, Timeout(1800000)]
        public IEnumerator EachChorusOrbitDrawsItsSkyAndPlanet()
        {
            string named = Path.Combine(Root, "Temp", "chorus_world_mod.txt");
            if (!File.Exists(named)) Assert.Ignore("No Temp/chorus_world_mod.txt: no mod to check");
            string source = File.ReadAllText(named).Trim();
            Directory.CreateDirectory(ModManager.UserFolder);
            using (var ln = System.Diagnostics.Process.Start("ln", $"-s \"{source}\" \"{Path.Combine(ModManager.UserFolder, ModId)}\"")) ln.WaitForExit();
            ModManager.Scan();
            ModManager.SetEnabled(ModId, true);
            Database.Load();
            var stations = Database.Shared.Stations.Where(st => ModWorld.PlanetSphereOf(st.index)?.look == "chorus").ToList();
            Assert.IsNotEmpty(stations, "the Chorus stations");
            yield return WaitFor(() => ModStations.Ready, 600f, "the mods' models and backdrops");
            string folder = Path.Combine(Root, "Temp", "chorus_world");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            bool live = Settings.LiveSkies;
            try
            {
                foreach (var st in stations)
                {
                    Settings.LiveSkies = true;
                    FreePlayAt(st.index);
                    Session.LaunchedFromStation = true;
                    yield return Load("Space");
                    yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null && Find<SpaceLevel>().Backdrop != null, 120f, "the level");
                    var level = Find<SpaceLevel>();
                    level.Player.GetComponent<Flight.Target>().invulnerable = true;
                    yield return Run(1f);
                    Assert.IsTrue(RenderSettings.skybox.IsKeywordEnabled("_CHORUS_SKY"), $"{st.name}: the live Chorus sky");
                    var target = level.Backdrop.PlanetTargets.FirstOrDefault(p => p.orbitPlanet);
                    Assert.IsNotNull(target.transform, $"{st.name}: the orbit planet");
                    Assert.AreEqual("GoF2/ChorusPlanet", target.transform.GetComponent<MeshRenderer>().sharedMaterial.shader.name);
                    bool shell = ModWorld.PlanetSphereOf(st.index).chorusAtmosphere != null;
                    Assert.AreEqual(shell, target.transform.Find("Atmosphere") != null, $"{st.name}: its atmosphere shell");
                    string name = st.name.Replace(' ', '_').ToLowerInvariant();
                    var cam = Camera.main.transform.position;
                    var to = (target.transform.position - cam).normalized;
                    yield return StationLifeCheck.Shot(folder, name + "_planet", cam, cam + to);
                    var sun = level.Backdrop.SunDirection;
                    var edge = Vector3.Slerp(to, Vector3.ProjectOnPlane(sun, to).normalized, 0.25f);
                    yield return StationLifeCheck.Shot(folder, name + "_edge", cam, cam + edge);
                    yield return StationLifeCheck.Shot(folder, name + "_sky", cam, cam - to);
                    // the same sky baked ("Live mod skies" off: the panorama)
                    Settings.LiveSkies = false;
                    level.RestoreOrbitSky();
                    Assert.IsFalse(RenderSettings.skybox.IsKeywordEnabled("_CHORUS_SKY"));
                    yield return StationLifeCheck.Shot(folder, name + "_sky_baked", cam, cam - to);
                    Debug.Log($"Chorus world: {st.name} ({st.index}) shots taken");
                }
            }
            finally { Settings.LiveSkies = live; }
            Debug.Log($"Chorus world: shots in {folder}");
        }
    }
}
