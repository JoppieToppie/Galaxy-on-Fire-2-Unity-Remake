// RebuiltPlanetsCheck.cs
// A check to look at, not part of the suite: Options > Graphics "3D planets" (World.RebuiltPlanets) in the orbit's backdrop. For a
// few stations whose orbit planet is one of the kinds (plain, banded, lava, ringed, the Supernova planet with its moon), the orbit
// is loaded with the setting on and off and the orbit planet shot through the game's camera both ways: Temp/rebuilt_planets/
// <planet>_3d.png and <planet>_png.png. The rebuilt sphere is lit as its png, so the two should look the same; the log gives each
// pair's mean difference over the frame.
//   python3 -I Reference/tools/tests/run_tests.py --mode PlayMode --filter GoF2Remake.Tests.RebuiltPlanetsCheck

using System.Collections;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Recording")]
    public class RebuiltPlanetsCheck : PlayModeTest
    {
        static readonly int[] Planets = { 0, 1, 5, 9, 13, 16, 21, 25 };
        string Root => Directory.GetParent(Application.dataPath).FullName;

        [UnityTest, Timeout(1800000)]
        public IEnumerator TheOrbitPlanetLooksAsItsPng()
        {
            string folder = Path.Combine(Root, "Temp", "rebuilt_planets");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            bool was = Settings.RebuiltPlanets;
            try
            {
                foreach (int t in Planets)
                {
                    var st = Database.Shared.Stations.FirstOrDefault(s => s.textureIndex == t);
                    if (st == null) { Debug.Log($"Rebuilt planets: no station with planet {t}"); continue; }
                    Vector3? look = null;
                    foreach (bool on in new[] { true, false })
                    {
                        Settings.RebuiltPlanets = on;
                        FreePlayAt(st.index);
                        Session.LaunchedFromStation = true;
                        yield return Load("Space");
                        yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null && Find<SpaceLevel>().Backdrop != null, 120f, "the level");
                        var level = Find<SpaceLevel>();
                        level.Player.GetComponent<Flight.Target>().invulnerable = true;
                        yield return Run(1f);
                        var target = level.Backdrop.PlanetTargets.FirstOrDefault(p => p.orbitPlanet);
                        Assert.IsNotNull(target.transform, $"planet {t}: the orbit planet");
                        var shader = target.transform.GetComponent<MeshRenderer>().sharedMaterial.shader.name;
                        if (on) Assert.AreEqual("GoF2/RebuiltPlanetBackdrop", shader, $"planet {t} at station {st.index}: the rebuilt sphere");
                        else Assert.AreNotEqual("GoF2/RebuiltPlanetBackdrop", shader, $"planet {t}: the png with the setting off");
                        if (on)
                            foreach (var mr in target.transform.GetComponentsInChildren<MeshRenderer>(true))
                                Debug.Log($"Rebuilt planets: {t:000} part {mr.name} active {mr.gameObject.activeInHierarchy} enabled {mr.enabled} at {mr.transform.position} scale {mr.transform.lossyScale.x:0} visible {mr.isVisible} shader {mr.sharedMaterial?.shader.name} queue {mr.sharedMaterial?.renderQueue} layer {mr.gameObject.layer}");
                        var cam = Camera.main.transform.position;
                        look ??= (target.transform.position - cam).normalized;   // (the same way both times)
                        // (the backdrop alone: the station and the ships in front hid the planet)
                        var hidden = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Where(r => r.enabled && !r.transform.IsChildOf(level.Backdrop.transform)).ToList();
                        foreach (var r in hidden) r.enabled = false;
                        yield return StationLifeCheck.Shot(folder, $"{t:000}_{(on ? "3d" : "png")}", cam, cam + look.Value);
                        if (on && target.transform.GetComponentsInChildren<MeshRenderer>().Length > 1)   // (a companion: shot alone too)
                        {
                            var own = target.transform.GetComponent<MeshRenderer>(); own.enabled = false;
                            yield return StationLifeCheck.Shot(folder, $"{t:000}_3d_companions", cam, cam + look.Value);
                            own.enabled = true;
                        }
                        foreach (var r in hidden) if (r != null) r.enabled = true;
                    }
                    var a = LoadPng(Path.Combine(folder, $"{t:000}_3d.png")); var b = LoadPng(Path.Combine(folder, $"{t:000}_png.png"));
                    double sum = 0; var pa = a.GetPixels32(); var pb = b.GetPixels32();
                    for (int i = 0; i < pa.Length; i++) sum += (System.Math.Abs(pa[i].r - pb[i].r) + System.Math.Abs(pa[i].g - pb[i].g) + System.Math.Abs(pa[i].b - pb[i].b)) / 3.0;
                    Debug.Log($"Rebuilt planets: planet {t:000} (station {st.index} {st.name}): 3D against the png, mean difference {sum / pa.Length:0.0}/255");
                    Object.Destroy(a); Object.Destroy(b);
                }
            }
            finally { Settings.RebuiltPlanets = was; }
            Debug.Log($"Rebuilt planets: shots in {folder}");
        }

        // the star map's system view of the stations' systems (009's with its ring, 025's with its moon, 005's), "3D planets" on and
        // off: Temp/rebuilt_planets/map_<station>_3d.png and _png.png; on, each game planet's model has its rebuilt sphere
        static readonly int[] MapStations = { 10, 114, 18 };

        [UnityTest, Timeout(900000)]
        public IEnumerator TheStarMapsPlanetsAreTheSpheres()
        {
            string folder = Path.Combine(Root, "Temp", "rebuilt_planets");
            Directory.CreateDirectory(folder);
            bool was = Settings.RebuiltPlanets;
            try
            {
                foreach (int stIdx in MapStations)
                    foreach (bool on in new[] { true, false })
                    {
                        Settings.RebuiltPlanets = on;
                        FreePlayAt(stIdx);
                        yield return Load("Station");
                        yield return Run(2f);
                        var st = Db.Stations.Find(s => s.index == stIdx);
                        var map = GoF2Remake.UI.StarMap.Open(Db, GoF2Remake.UI.StarMapMode.Mission, false, _ => { });
                        for (int i = 0; i < 10; i++) yield return null;
                        map.GetType().GetField("selected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(map, st.system);
                        Invoke(map, "TryZoomIn");
                        yield return Run(4f);
                        int spheres = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude).Count(r => r.sharedMaterial != null && r.sharedMaterial.shader.name == "GoF2/RebuiltPlanet");
                        Debug.Log($"Rebuilt planets: star map of {st.systemName} ({(on ? "3D" : "png")}): {spheres} rebuilt sphere parts");
                        foreach (var rr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude).Where(r => r.sharedMaterial != null && r.sharedMaterial.shader.name == "GoF2/RebuiltPlanet"))
                        {
                            var mm = rr.sharedMaterial;
                            Debug.Log($"Rebuilt planets: map part {rr.name} pole {mm.GetVector("_RingPole")} ringR {mm.GetVector("_RingR")} profile {(mm.GetTexture("_RingProfile") != null)} profileR {mm.GetVector("_RingProfileR")} scale {rr.transform.lossyScale} rot {rr.transform.rotation.eulerAngles} parent {rr.transform.parent?.name} visible {rr.isVisible}");
                        }
                        if (on) Assert.Greater(spheres, 0, $"{st.systemName}: the map's planets as rebuilt spheres");
                        else Assert.AreEqual(0, spheres, "none with the setting off");
                        var cam = Get<Camera>(map, "cam");
                        yield return new WaitForEndOfFrame();
                        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                        var keep = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = keep;
                        var prev = RenderTexture.active; RenderTexture.active = rt;
                        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                        RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(folder, $"map_{stIdx}_{(on ? "3d" : "png")}.png"), tex.EncodeToPNG());
                        Object.Destroy(rt); Object.Destroy(tex);
                        Invoke(map, "Close", new GoF2Remake.UI.StarMapResult { station = -1 });
                        yield return null;
                    }
            }
            finally { Settings.RebuiltPlanets = was; }
        }

        static Texture2D LoadPng(string file)
        {
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(file));
            return t;
        }
    }
}
