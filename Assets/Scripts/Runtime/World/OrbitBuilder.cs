// OrbitBuilder.cs
// Turns a OrbitLayout into scene objects. Shared by the flight level (SpaceLevel) and the main menu
// background (MenuBackground), like the original, whose menu backdrop is a normal orbit level (Level type 2 in
// Level::createScene 0xc2910: createPlayer + an empty mission) built by the same Level::init code.
//   Level::createSpace      sky (GoF2/SpaceSky), station at the origin, jumpgate, sun/planets (Backdrop)
//   StarSystem::initLight   LIGHT0 toward the sun, LIGHT1 from the orbit planet (Unity +Z), skybox ambient, fog
//   Level::createAsteroids  asteroids around the seeded centre (per-visit placement with UnityEngine.Random), each
//                           with its ore (Galaxy::getAsteroidProbabilities) and quality A..D for mining
//   initParticleSystems     space dust + fog sprites around the camera

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace GoF2Remake.World
{
    public static class OrbitBuilder
    {
        const float M = OrbitLayout.MetersPerUnit;

        // ---- sky, light, fog ---------------------------------------------------------------------------------

        public static void SetupSky(OrbitLayout layout, float ambientIntensity = 1f, int nebulaOverride = -1)
        {
            var template = Resources.Load<Material>("GoF2Sky/SpaceSky");
            if (template == null) { Debug.LogWarning("OrbitBuilder: run GoF2 > Build > Space Skies"); return; }
            var sky = new Material(template) { name = "SpaceSky (runtime)" };
            int stars = layout.systemIndex >= 0 ? layout.systemIndex % 3 : 2;   // alien/void: stars_002
            sky.SetTexture("_Stars", Resources.Load<Cubemap>($"GoF2Sky/stars_{stars:000}"));
            int nebula = nebulaOverride >= 0 ? nebulaOverride : layout.systemTexture;   // the prologue's belt: nebula 3
            sky.SetTexture("_Nebula", Resources.Load<Cubemap>($"GoF2Sky/nebula_{nebula:000}"));
            // The shader maps world directions into the baked cube: the inverse of the sky's Unity rotation.
            sky.SetMatrix("_SkyRotation", Matrix4x4.Rotate(Quaternion.Inverse(SkyRotation(layout))));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();

            Bootstrap.SetSceneFog(layout.fog);   // off below Quality High
            if (layout.fog)
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = layout.fogEnd * M;
                RenderSettings.fogColor = layout.fogColor;
            }
        }

        /// <summary>R_sky in Unity (the baked cubemaps are the sky meshes at identity, after the import's 180 deg yaw).</summary>
        public static Quaternion SkyRotation(OrbitLayout layout)
        {
            if (!layout.skySunAligned) return OrbitLayout.RotationToUnity(layout.skyEuler);
            // System 27: X = a x b, Y = a (toward the sun), Z = b with b = normalize((1,0,0) x a). Mirrored to Unity
            // (S * R * S) the Z column flips sign: Y' = S a, Z' = -S b.
            var a = OrbitLayout.DirToUnity(layout.lightDirection).normalized;
            var b = OrbitLayout.DirToUnity(Vector3.Cross(Vector3.right, layout.lightDirection)).normalized;
            return Quaternion.LookRotation(-b, a) * Quaternion.Euler(0f, 180f, 0f);
        }

        /// <param name="sunIntensityAt2">URP intensity for the original's LIGHT0 diffuse of 2.0 (tuned, not recovered).</param>
        public static void SetupLights(OrbitLayout layout, Light sun, Light planet, float sunIntensityAt2 = 1.6f, float planetIntensity = 1f)
        {
            if (sun != null)
            {
                var toSun = OrbitLayout.DirToUnity(layout.lightDirection).normalized;
                sun.transform.rotation = Quaternion.LookRotation(-toSun);
                var c = layout.SunLightColor;   // 0..2 per channel, 2 for most systems
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 1e-3f);
                sun.color = c / max;
                sun.intensity = sunIntensityAt2 * max / 2f;
            }
            if (planet != null)
            {
                // LIGHT1 direction (0, 0, -1) game = toward the orbit planet (Unity +Z); light travels toward -Z.
                planet.transform.rotation = Quaternion.LookRotation(Vector3.back);
                planet.color = layout.planetLightColor;
                planet.intensity = planetIntensity;
            }
        }

        // ---- objects -----------------------------------------------------------------------------------------

        public static GameObject Spawn(Database db, string assembly, Vector3 gamePos, Quaternion rot, string label, Transform parent)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(assembly));
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, OrbitLayout.ToUnity(gamePos), rot, parent);
            go.name = label;
            return go;
        }

        /// <summary>Assembly name of a station (PlayerStation::PlayerStation, assemblies_stations_notes.md), null if none.</summary>
        public static string StationAssembly(Database db, OrbitLayout layout)
        {
            // Special cases, as they are before the campaign changes them.
            // PlayerStation: in the alien orbit the Void station (16443, collision 1001), after the Valkyrie add-on the battlestation.
            if (layout.alienOrbit) return Story.Dlc1Won ? "v_station_battlestation_anim" : "station_void";
            switch (layout.stationIndex)
            {
                // Kothar: exploding while Alice attacks (index 80), damaged after the add-on.
                case 100: return !Session.FreePlay && Story.Index == 80 ? "v_station_deep_science_explosion_anim"
                               : Story.Dlc1Won ? "v_station_deep_science_damaged" : "v_station_deep_science";
                case 101: return "v_station_battlestation_anim";
                case 108: return "station_kaamo_club";
                case 109: case 110: return "sn_station_midorian_wrecked";
                // Luur (PlayerStation::PlayerStation): burning up to campaign 0x5d, the bare hull at 0x5e (its level adds the
                // burning platform), the wreck after.
                case 111: return Session.CampaignMission <= 0x5d ? "sn_burning_station_luur"
                               : Session.CampaignMission == 0x5e ? "station_111_luur_mission_94" : "sn_station_midorian_wrecked";
            }
            string prefix = $"station_{layout.stationIndex:000}_";
            var entry = db.Assemblies.Find(a => a.category == "stations" && (a.name.StartsWith(prefix)
                                                  || a.name.StartsWith("v_" + prefix) || a.name.StartsWith("sn_" + prefix)));
            return entry != null ? entry.name : layout.raceId == 1 ? "station_vossk" : null;   // Vossk: no collision entry
        }

        /// <summary>Level::createSpace / PlayerStation: at the origin, rotation (0, pi, 0) (= identity in Unity).</summary>
        public static GameObject SpawnStation(Database db, OrbitLayout layout, Transform parent = null)
        {
            if (!layout.stationObject) return null;   // Level::createSpace: the empty orbits 27 / 110 / 111 keep their PlayerStation
            string name = StationAssembly(db, layout);
            if (name == null) { Debug.LogWarning($"OrbitBuilder: no station assembly for {layout.stationIndex}"); return null; }
            var go = Spawn(db, name, Vector3.zero, OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI, 0f)), "Station", parent);
            // PlayerStation::update advances the station's animation every frame except at 101 and in the alien orbit: the
            // battlestation's arms hold their first frame there, or their last once step 78 unfolded them (the ctor's
            // Transform::Update(the length): station 0x65 from campaign 0x50 0x1473c2, the alien orbit after the Valkyrie
            // add-on 0x146e90). The Void station holds the pose after its one-off first
            // key: at t 0 every part is at scale 1, from 50 ms the hull is x10.065 (about 10 km across), the size its
            // collision volumes (1001: spheres out to +-100 000 units) and the arrival 170 000-220 000 units out are made for.
            if (name == "station_void") PartAnimation.HoldAllAfterOneOff(go);
            else if ((layout.stationIndex == 101 && Session.CampaignMission >= 0x50) || (layout.alienOrbit && Story.Dlc1Won)) PartAnimation.HoldAllAtEnd(go);
            else if (layout.stationIndex == 101 || layout.alienOrbit) PartAnimation.HoldAll(go);
            return go;
        }

        /// <summary>The station's volumes (collision.json) and the visible jumpgate's sphere (Obstacle): the player slides
        /// along them (PlayerEgo::calcCollision) and NPC fighters turn away from them (PlayerFighter::update, the first
        /// landmark's volumes). The flight level and the menu backdrop.</summary>
        public static void AddObstacles(OrbitLayout layout, GameObject station, GameObject jumpgate)
        {
            const float M = OrbitLayout.MetersPerUnit;
            if (station != null)
            {
                var o = station.AddComponent<GoF2Remake.Flight.Obstacle>();
                o.landmark = o.isStation = true;
                o.volumes = GoF2Remake.Flight.CollisionVolume.ForStation(layout.stationIndex, layout.systemIndex < 0);
                // PlayerStation+0x150: the transform's bounding radius (Transform+0xe0, about the station's own origin)
                // + 5000 units. A radius from the origin, not the bounds' half size: a lopsided station (Tornard, 57,
                // towers 3.4 km out on one side) had its far tower outside the cube, so nothing collided there.
                var pos = station.transform.position;
                var b = new Bounds(pos, Vector3.zero);
                foreach (var r in station.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                float radius = 0f;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) != 0 ? b.max.x : b.min.x, (c & 2) != 0 ? b.max.y : b.min.y, (c & 4) != 0 ? b.max.z : b.min.z);
                    radius = Mathf.Max(radius, (corner - pos).magnitude);
                }
                o.cubeHalf = radius + 5000f * M;
            }
            if (jumpgate != null)
            {
                var o = jumpgate.AddComponent<GoF2Remake.Flight.Obstacle>();
                o.landmark = o.cubeIsContact = true;
                o.cubeHalf = layout.JumpgateRadius * M;
                o.volumes.Add(GoF2Remake.Flight.CollisionVolume.Sphere(Vector3.zero, layout.JumpgateRadius * M));
            }
        }

        public static GameObject SpawnJumpgate(Database db, OrbitLayout layout, Transform parent = null)
        {
            if (!layout.hasJumpgate) return null;
            return Spawn(db, layout.JumpgateAssembly, layout.jumpgate, OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI, 0f)), "Jumpgate", parent);
        }

        /// <summary>
        /// Level::createAsteroids / PlayerAsteroid. The first 2..9 are big (cube +-30000, scale 1.2..2.19, no spin), the
        /// rest small (cube +-50000, scale 0.3..0.99, 0.1 rad/s on random axes). 'reject' (Unity position) re-rolls a
        /// spot, e.g. to keep the menu camera's orbit clear.
        /// </summary>
        public static Transform SpawnAsteroids(Database db, OrbitLayout layout, Transform parent = null, Func<Vector3, bool> reject = null)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(layout.AsteroidAssembly));
            var explosion = AssembledObject.LoadPrefab(db.AssemblyByName(layout.AsteroidAssembly + "_explosion_anim"));
            var destroyedSound = GoF2Remake.Flight.CombatAudio.Load()?.asteroidDestroyed;
            var root = new GameObject("Asteroids").transform;
            root.SetParent(parent, false);
            if (prefab == null) return root;
            int big = Random.Range(2, 10);
            var bigPositions = new Vector3[big];
            var ores = OreProbabilities(db, layout);
            int oreCursor = 0;
            for (int i = 0; i < layout.asteroidCount; i++)
            {
                bool isBig = i < big;
                float side = isBig ? 60000f : 100000f;
                Vector3 pos;
                int tries = 0;
                bool bad;
                do
                {
                    pos = layout.asteroidCentre + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f)) * side;
                    bad = (isBig && TooClose(pos, bigPositions, i))   // intended rule: big ones 8000 apart
                          || (reject != null && reject(OrbitLayout.ToUnity(pos)));
                } while (bad && ++tries < 40);
                if (bad && reject != null) continue;   // no valid spot: leave it out rather than block the view
                if (isBig) bigPositions[i] = pos;

                float scale = isBig ? Random.Range(120, 220) * 0.01f : Random.Range(30, 100) * 0.01f;
                int ore = PickOre(ores, ref oreCursor);
                // Quality from the scale; the big ones (and the largest small ones) are 50 % A, else D..B.
                int quality = scale < 0.4f ? 4 : scale < 0.7f ? 5 : scale < 0.92f ? 6 : Random.Range(0, 2) == 0 ? 7 : 4 + Random.Range(0, 3);
                var euler = new Vector3(Random.Range(0, 100), Random.Range(0, 100), Random.Range(0, 100)) * 0.01f * 2f * Mathf.PI;
                var go = Object.Instantiate(prefab, OrbitLayout.ToUnity(pos), OrbitLayout.RotationToUnity(euler), root);
                go.name = $"Asteroid {i}";
                go.transform.localScale = prefab.transform.localScale * scale;
                // PlayerAsteroid: hit radius = meshRadius * scale * 0.7, HP = scale * 100 + 30.
                var target = go.AddComponent<GoF2Remake.Flight.Target>();
                target.isAsteroid = true;
                target.radius = layout.AsteroidMeshRadius * scale * 0.7f * M;
                target.maxHp = target.hp = scale * 100f + 30f;
                target.explosionPrefab = explosion;
                target.explosionScale = scale;
                target.destroyedSound = destroyedSound;
                target.oreItem = ore;
                target.quality = quality;
                target.scale = scale;
                float spin = 1f - Mathf.Clamp(scale, 0.9f, 1f);   // 0.1 rad/s for small, none for big
                if (spin > 0f)
                {
                    var axes = new Vector3(Random.Range(-1, 2), Random.Range(-1, 2), Random.Range(-1, 2));
                    go.AddComponent<Spin>().degreesPerSecond = new Vector3(-axes.x, -axes.y, axes.z) * spin * Mathf.Rad2Deg;
                }
            }
            return root;
        }

        /// <summary>Galaxy::getAsteroidProbabilities 0x1a4fb0: per ore 154..163, p = 100 - distance(system, the ore's cheapest
        /// system), 0 below 50; Void Crystals (164) appended with 0 (100 in an alien orbit); sorted descending (stable),
        /// then p[k] -= 2k for the positive ones.</summary>
        public static List<(int item, int p)> OreProbabilities(Database db, OrbitLayout layout)
        {
            var list = new List<(int item, int p)>();
            bool alien = layout.systemIndex < 0;
            for (int item = 154; item <= 163; item++)
            {
                var it = db.Item(item);
                int p = alien || it == null ? 0 : 100 - Shop.Distance(db, layout.systemIndex, it.lowestPriceSystem);
                list.Add((item, p < 50 ? 0 : p));
            }
            list.Add((164, alien ? 100 : 0));
            var sorted = list.Select((e, i) => (e, i)).OrderByDescending(x => x.e.p).ThenBy(x => x.i).Select(x => x.e).ToList();
            for (int k = 0; k < sorted.Count; k++) if (sorted[k].p > 0) sorted[k] = (sorted[k].item, sorted[k].p - 2 * k);
            return sorted;
        }

        /// <summary>Level::createAsteroids 0xbd34a: a cursor walks the pairs; a roll under p takes that ore and moves on
        /// (wrapping after pair 5), a miss starts over at the top ore.</summary>
        public static int PickOre(List<(int item, int p)> ores, ref int k)
        {
            for (int guard = 0; guard < 10000; guard++)
            {
                if (Random.Range(0, 100) < ores[k].p)
                {
                    int item = ores[k].item;
                    k = k + 1 > 5 ? 0 : k + 1;
                    if (item < 164 || item == 217 || ores[0].item == 164) return item;
                }
                else k = 0;
            }
            return ores[0].item;
        }

        static bool TooClose(Vector3 p, Vector3[] others, int count)
        {
            for (int j = 0; j < count; j++) if ((others[j] - p).sqrMagnitude < 8000f * 8000f) return true;
            return false;
        }

        /// <summary>SET_STARS + SET_FOG around the camera (Level::initParticleSystems 0xcc990).</summary>
        public static void SpawnDust(OrbitLayout layout, Transform parent = null)
        {
            var stars = new GameObject("SpaceDust").AddComponent<SpaceDust>();
            stars.transform.SetParent(parent, false);
            stars.Build(Resources.Load<Material>($"{Backdrop.MaterialFolder}/space_particle"), 500, 20f, 60f, Color.white, 10000f, 5000f, 2000f);
            var fog = new GameObject("SpaceFog").AddComponent<SpaceDust>();
            fog.transform.SetParent(parent, false);
            string fogTex = layout.systemTexture == 12 ? "v_fog_ice" : "fog";
            fog.Build(Resources.Load<Material>($"{Backdrop.MaterialFolder}/{fogTex}"), 15, 10000f, 10000f, layout.dustFogTint, 10000f, 5000f, 1000f);
        }

        public static Backdrop SpawnBackdrop(OrbitLayout layout, Camera camera, Transform parent = null)
        {
            var backdrop = new GameObject("Backdrop").AddComponent<Backdrop>();
            backdrop.transform.SetParent(parent, false);
            backdrop.Build(layout, camera);
            return backdrop;
        }
    }
}
