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
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
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
            var rotation = SkyRotation(layout);
            // Remake mods: a system's own sky (systems.json "skybox": panorama / cube-strip images, the star layer, a turn).
            var mod = layout.systemIndex >= 0 && nebulaOverride < 0 ? Modding.ModWorld.SkyOf(layout.systemIndex) : null;
            if (mod != null)
            {
                SetModLayer(sky, mod.nebula, "_NebulaMap", "_NEBULA_PANORAMA", "_NEBULA_STRIP");
                if (mod.stars != null) SetModLayer(sky, mod.stars, "_StarsMap", "_STARS_PANORAMA", "_STARS_STRIP");
                else if (mod.starsLayer >= 0) sky.SetTexture("_Stars", Resources.Load<Cubemap>($"GoF2Sky/stars_{mod.starsLayer:000}"));
                sky.SetFloat("_NebulaGain", mod.nebulaBrightness);
                sky.SetFloat("_StarsGain", mod.starsLayer == -2 ? 0f : mod.starsBrightness);
                // "sunAligned": +Y toward this orbit's sun like the supernova system (a fixed "rotation" turns the image first).
                if (mod.sunAligned) rotation = SunAlignedSky(layout) * (mod.rotation.HasValue ? Quaternion.Euler(mod.rotation.Value) : Quaternion.identity);
                else if (mod.rotation.HasValue) rotation = Quaternion.Euler(mod.rotation.Value);
            }
            // The shader maps world directions into the baked cube: the inverse of the sky's Unity rotation.
            sky.SetMatrix("_SkyRotation", Matrix4x4.Rotate(Quaternion.Inverse(rotation)));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            SkyReflection.Update();   // the ambient light
            SkyReflection.SetSystem(layout);   // the hulls' reflection: the system's cubemap (ChangeCubeTexture)

            GoF2Remake.World.SpaceHooks.SceneFog(layout.fog);   // off below Quality High
            if (layout.fog)
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = layout.fogEnd * M;
                RenderSettings.fogColor = layout.fogColor;
            }
        }

        /// <summary>A mod's sky image on a GoF2/SpaceSky layer: 2:1 = a panorama, 6:1 = a strip of cube faces (else a warning
        /// and the game's layer stays).</summary>
        static void SetModLayer(Material sky, string name, string property, string panorama, string strip)
        {
            if (name == null) return;
            var tex = Modding.ModBackdrop.Texture(name);
            if (tex == null) return;
            float aspect = tex.width / (float)Mathf.Max(1, tex.height);
            bool isStrip = Mathf.Abs(aspect - 6f) < 0.05f, isPanorama = Mathf.Abs(aspect - 2f) < 0.05f;
            if (!isStrip && !isPanorama)
            {
                Debug.LogWarning($"OrbitBuilder: the skybox image {name} is {tex.width} x {tex.height}: a panorama is 2:1, a strip of cube faces 6:1");
                return;
            }
            tex.wrapMode = isPanorama ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            tex.wrapModeV = TextureWrapMode.Clamp;
            sky.SetTexture(property, tex);
            sky.EnableKeyword(isPanorama ? panorama : strip);
        }

        /// <summary>R_sky in Unity (the baked cubemaps are the sky meshes at identity, after the import's 180 deg yaw).</summary>
        public static Quaternion SkyRotation(OrbitLayout layout)
        {
            if (!layout.skySunAligned) return OrbitLayout.RotationToUnity(layout.skyEuler);
            return SunAlignedSky(layout);
        }

        /// <summary>The sky turned so its +Y points at the orbit's sun (StarSystem's supernova sky; remake mods' "sunAligned").</summary>
        public static Quaternion SunAlignedSky(OrbitLayout layout)
        {
            // System 27: X = a x b, Y = a (toward the sun), Z = b with b = normalize((1,0,0) x a). Mirrored to Unity
            // (S * R * S) the Z column flips sign: Y' = S a, Z' = -S b.
            var a = OrbitLayout.DirToUnity(layout.lightDirection).normalized;
            var side = Vector3.Cross(Vector3.right, layout.lightDirection);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(Vector3.forward, layout.lightDirection);   // a sun on game x (a mod's)
            var b = OrbitLayout.DirToUnity(side).normalized;
            return Quaternion.LookRotation(-b, a) * Quaternion.Euler(0f, 180f, 0f);
        }

        /// <param name="sunIntensityAt2">URP intensity for the original's LIGHT0 diffuse of 2.0 (tuned, not recovered).</param>
        /// <param name="sunShadows">URP's real-time shadows from the sun: none in space (the original draws none; within URP's
        /// 50 m shadow distance the player's own hull shaded itself as the camera came close, Action Freeze, a player's report
        /// 2026-10-17), the Space Lounge keeps its room's.</param>
        public static void SetupLights(OrbitLayout layout, Light sun, Light planet, float sunIntensityAt2 = 1.6f, float planetIntensity = 1f,
                                       LightShadows sunShadows = LightShadows.None, bool gammaSun = true)
        {
            Shader.SetGlobalVector("_GoF2SunAmbient", gammaSun && sun != null ? (Vector4)SceneAmbient(layout) : Vector4.zero);   // gamma values: not SetGlobalColor, which linearises
            if (planet != null) planet.shadows = LightShadows.None;
            if (sun != null)
            {
                sun.shadows = sunShadows;
                var toSun = OrbitLayout.DirToUnity(layout.lightDirection).normalized;
                sun.transform.rotation = Quaternion.LookRotation(-toSun);
                var c = layout.SunLightColor;   // 0..2 per channel, 2 for most systems
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 1e-3f);
                sun.color = c / max;
                sun.intensity = sunIntensityAt2 * max / 2f;
            }
            // Modified: the hulls' sunlit side as the original's gamma-space LIGHT0 (GoF2/Hull, _GoF2SunGamma): per channel,
            // the gamma colour over URP's linear one (colour.linear x intensity), so a tinted sun keeps its tint.
            if (gammaSun && sun != null)
            {
                Color c = layout.SunLightColor, lin = sun.color.linear * sun.intensity;
                // Modified: the sun table's own hue blended in by SunTint: clamped at 2 a red sun's LIGHT0 is nearly white
                // (Magnetar, system 5: 2 / 1.95 / 1.65), while the original's Dis is lit a faint red (measured on its screenshots:
                // red over green 1.5 in the half tones, 1.2 here; 30 % of (1, 0.34, 0.29) gives 2 / 1.56 / 1.30, tuned by eye).
                var raw = layout.sunColor;
                float rawMax = Mathf.Max(raw.r, Mathf.Max(raw.g, raw.b), 1e-4f);
                c = new Color(c.r * Mathf.Lerp(1f, raw.r / rawMax, SunTint), c.g * Mathf.Lerp(1f, raw.g / rawMax, SunTint),
                              c.b * Mathf.Lerp(1f, raw.b / rawMax, SunTint));
                static float Ratio(float gamma, float linear) => linear > 1e-4f ? gamma / linear : 0f;
                SetSunGamma(new Vector3(Ratio(c.r, lin.r), Ratio(c.g, lin.g), Ratio(c.b, lin.b)));
            }
            else SetSunGamma(Vector3.zero);
            if (planet != null)
            {
                // LIGHT1 direction (0, 0, -1) game = toward the orbit planet (Unity +Z); light travels toward -Z.
                planet.transform.rotation = Quaternion.LookRotation(Vector3.back);
                planet.color = layout.planetLightColor;
                planet.intensity = planetIntensity;
            }
            // Modified: LIGHT1 in GoF2/Hull's gamma-space sum (see _GoF2PlanetLight)
            bool gammaPlanet = gammaSun && sun != null && planet != null && planet.enabled;
            Shader.SetGlobalVector("_GoF2PlanetLight", gammaPlanet ? (Vector4)Vector3.forward : Vector4.zero);
            Shader.SetGlobalVector("_GoF2PlanetColor", gammaPlanet ? (Vector4)layout.planetLightColor : Vector4.zero);   // gamma values
            Shader.SetGlobalVector("_GoF2PlanetLinear", gammaPlanet ? (Vector4)(planet.color.linear * planet.intensity) : Vector4.zero);
        }

        /// <summary>Modified: the sunlit side's HDR glow (_GoF2SunGlow), for the bloom; matched by eye to the original's
        /// screenshots (Kaamo).</summary>
        const float SunGlow = 1.3f;
        /// <summary>Modified: the brightness (gamma, texture x LIGHT0) where that glow starts.</summary>
        const float SunGlowFrom = 0.4f;
        /// <summary>Modified: how much of the sun table's hue tints the sunlit side (0 = LIGHT0 as clamped).</summary>
        const float SunTint = 0.30f;

        /// <summary>Modified: StarSystem::initLight's LightSetGlobalSceneColorAmbient, kA x the sun colour (gamma): kA 0.15, 0.7
        /// in the supernova system (texture 15) but at mission 89 and from 158, where the sun colour is halved first.</summary>
        public static Color SceneAmbient(OrbitLayout layout)
        {
            var c = layout.sunColor;
            float k = 0.15f;
            if (layout.systemTexture == 15)
            {
                int m = Session.WorldIndex;
                if (m != 89 && m < 158) k = 0.7f;
                else c *= 0.5f;
            }
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        /// <summary>Modified: GoF2/Hull's gamma-space sunlight (_GoF2SunGamma): per channel, the original's LIGHT0 per unit of
        /// the main light's (linear) colour; zero = URP's own linear diffuse (the hangars, the lounge).</summary>
        public static void SetSunGamma(Vector3 level)
        {
            bool on = level.x > 0f || level.y > 0f || level.z > 0f;
            Shader.SetGlobalVector("_GoF2SunGamma", level);
            Shader.SetGlobalFloat("_GoF2SunGlow", on ? SunGlow : 0f);
            Shader.SetGlobalFloat("_GoF2SunGlowFrom", SunGlowFrom);
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

        /// <summary>The station whose model and volumes a station uses: a mod's "looksLike" (Modding.ModWorld.StationLook);
        /// remake multiplayer's intact Naneroh / Valpatro (no model of their own) borrow Midantha's / Tergalon's, the
        /// Supernova add-on's Midorian stations next door (OrbitLayout.IsRebuiltGinoya).</summary>
        public static int StationLook(int station)
        {
            if (Session.CompletedWorld && station == 109) return 114;
            if (Session.CompletedWorld && station == 110) return 115;
            return Modding.ModWorld.StationLook(station);
        }

        /// <summary>Assembly name of a station (PlayerStation::PlayerStation, assemblies_stations_notes.md), null if none.</summary>
        /// <summary>PlayerStation::PlayerStation: Kothar's station exploding (index 0x50) or damaged (the Valkyrie add-on won)
        /// collides by its own table entry, 1005 (the blown-off module's box gone, its debris spheres), not the intact
        /// station's 100 (the remake used 100 until 2026-10-11; a contributor's precise-collision version found it).</summary>
        const int KotharDamagedCollision = 1005;
        static bool KotharDamaged(OrbitLayout layout) =>
            layout.stationIndex == 100 && !layout.alienOrbit && ((!Session.FreePlay && Story.Index == 80) || Story.Dlc1Won);

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
                case 109: case 110:
                    if (Session.CompletedWorld) break;   // remake multiplayer: intact (StationLook)
                    return "sn_station_midorian_wrecked";
                // Luur (PlayerStation::PlayerStation): burning up to campaign 0x5d, the bare hull at 0x5e (its level adds the
                // burning platform), the wreck after. Remake multiplayer: intact (its model before the supernova, step 89).
                case 111: return Session.CompletedWorld ? "station_111_luur_intact_mission_89"
                               : Session.CampaignMission <= 0x5d ? "sn_burning_station_luur"
                               : Session.CampaignMission == 0x5e ? "station_111_luur_mission_94" : "sn_station_midorian_wrecked";
            }
            // A mod's own station model (ModStations), once built; else (and for the rest) the original it names ("looksLike",
            // Modding.ModWorld.StationLook).
            var own = Modding.ModWorld.ModelOf(layout.stationIndex);
            if (own != null && Modding.ModStations.Built(own.station)) return own.Assembly;
            string prefix = $"station_{StationLook(layout.stationIndex):000}_";
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
            // Remake: the station's opacity keys play like the engine's (the blinking lights of the Nivelian, Midorian, pirate,
            // Loma and Supernova-era stations, the Kaamo Club's lit panels and glows); they were left out, the lights stayed on.
            if (go != null)
                foreach (var pa in go.GetComponentsInChildren<PartAnimation>(true))
                {
                    pa.applyMaterialChannels = true;
                    // Remake: the light layers (station_0xx_nivelian / _midorian_lights_add, station_pirates_lights_add, the Loma
                    // stations' lights, sn_station_1xx_add) are no "_anim" models, so the import left them stopped (play off):
                    // their blink keys never ran and the lights stayed on. PlayerStation::update advances them with the rest.
                    string n = pa.gameObject.name;
                    if (n.Contains("_lights_add") || n.EndsWith("_add") || n.EndsWith("_add(Clone)")) pa.play = true;
                }
            // PlayerStation::update advances the station's animation every frame except at 101 and in the alien orbit: the
            // battlestation's arms hold their first frame there, or their last once step 78 unfolded them (the ctor's
            // Transform::Update(the length): station 0x65 from campaign 0x50 0x1473c2, the alien orbit after the Valkyrie
            // add-on 0x146e90). The Void station holds the pose after its one-off first
            // key: at t 0 every part is at scale 1, from 50 ms the hull is x10.065 (about 10 km across), the size its
            // collision volumes (1001: spheres out to +-100 000 units) and the arrival 170 000-220 000 units out are made for.
            if (name == "station_void") PartAnimation.HoldAllAfterOneOff(go);
            else if ((layout.stationIndex == 101 && Session.CampaignMission >= 0x50) || (layout.alienOrbit && Story.Dlc1Won)) PartAnimation.HoldAllAtEnd(go);
            else if (layout.stationIndex == 101 || layout.alienOrbit) PartAnimation.HoldAll(go);
            if (name == KaamoClub) PlaceKaamoClubLights(go);   // Remake: its ring of blinking lights on the hull
            return go;
        }

        // Remake: the Kaamo Club's blinking lights (station_kaamo_club_fx_anim_add's parts) moved onto its hull: in the converted
        // model they float off it. Local positions as the Inspector shows them; parts sharing a place are the light's two layers.
        const string KaamoClub = "station_kaamo_club";
        static readonly Dictionary<string, Vector3> KaamoClubLights = new Dictionary<string, Vector3>
        {
            { "station_kaamo_club_fx_anim_add_part27", new Vector3(4.5f, 1f, 3f) },
            { "station_kaamo_club_fx_anim_add_part28", new Vector3(4.5f, 1f, 3f) },
            { "station_kaamo_club_fx_anim_add_part29", new Vector3(5.7f, 2f, 4.7f) },
            { "station_kaamo_club_fx_anim_add_part30", new Vector3(0f, 1f, 6f) },
            { "station_kaamo_club_fx_anim_add_part31", new Vector3(-1f, 1f, 6f) },
            { "station_kaamo_club_fx_anim_add_part32", new Vector3(-6.4f, 0.4f, 0f) },
            { "station_kaamo_club_fx_anim_add_part33", new Vector3(-6.4f, 0.4f, 0f) },
            { "station_kaamo_club_fx_anim_add_part34", new Vector3(-7.6f, 2f, 2.5f) },
            { "station_kaamo_club_fx_anim_add_part35", new Vector3(-5.8f, 0f, 2.8f) },
            { "station_kaamo_club_fx_anim_add_part36", new Vector3(-5.8f, 0f, 2.8f) },
            { "station_kaamo_club_fx_anim_add_part37", new Vector3(-3.7f, -1f, 6.5f) },
            { "station_kaamo_club_fx_anim_add_part38", new Vector3(1f, 0f, 6.4f) },
            { "station_kaamo_club_fx_anim_add_part39", new Vector3(2.8f, 0f, 6.4f) },
            { "station_kaamo_club_fx_anim_add_part40", new Vector3(7.5f, -1f, 2f) },
            { "station_kaamo_club_fx_anim_add_part41", new Vector3(4.8f, 0f, 0f) },
            { "station_kaamo_club_fx_anim_add_part42", new Vector3(4.8f, 0f, 0f) },
        };

        static void PlaceKaamoClubLights(GameObject station)
        {
            foreach (var t in station.GetComponentsInChildren<Transform>(true))
            {
                if (!KaamoClubLights.TryGetValue(t.name, out var pos)) continue;
                var anim = t.GetComponentInParent<PartAnimation>(true);
                if (anim != null) anim.SetBasePosition(t, pos);   // its blinking keeps the new place
                else t.localPosition = pos;
            }
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
                var own = Modding.ModWorld.ModelOf(layout.stationIndex);
                o.volumes = own != null && Modding.ModStations.Built(own.station) ? Modding.ModStations.Volumes(own, station)   // a mod's model
                          : GoF2Remake.Flight.CollisionVolume.ForStation(KotharDamaged(layout) ? KotharDamagedCollision
                                                                         : StationLook(layout.stationIndex), layout.systemIndex < 0,
                                                                         layout.alienOrbit && Story.Dlc1Won);
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
            // A mod system's own gate (systems.json "jumpgate") once ModStations has built it; else the race's.
            GameObject go = null;
            if (layout.ModJumpgateAssembly is string modGate)
                go = Spawn(db, modGate, layout.jumpgate, OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI, 0f)), "Jumpgate", parent);
            if (go == null) go = Spawn(db, layout.JumpgateAssembly, layout.jumpgate, OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI, 0f)), "Jumpgate", parent);
            // Remake: the idle light layer's blink (`extra` keys) on every gate: jumpgate_nivelian_anim_add parts 0 / 2, the
            // Midorian ring's 1-9, the Terran lamps (the jump flash, _jump_anim_add, gets its fades in SystemJump.PlayGateJump).
            if (go != null)
                foreach (var pa in go.GetComponentsInChildren<PartAnimation>(true))
                    if (pa.gameObject.name.EndsWith("_anim_add") && !pa.gameObject.name.EndsWith("_jump_anim_add")) pa.applyMaterialChannels = true;
            if (go != null && layout.JumpgateAssembly == TerranGate) PlayTerranGateRing(go);
            return go;
        }

        const string TerranGate = "jumpgate_terran";

        /// <summary>Remake: the Terran gate. Its inner ring (jumpgate_terran_part1, and its LODs) has its own keys, a turn opposite
        /// to the light ring's (jumpgate_terran_anim_add_part1: rotY +0.39 vs -0.77 rad per loop), but the import only plays the
        /// "_anim" models, so it stood still; both rings now turn steadily at their keys' rate (SpinContinuously: no wobble or
        /// snap back on each loop). The light ring's lamps fade with their `extra` keys like the stations' (they only shrank
        /// and stayed lit, seen jumping between their two places).</summary>
        static void PlayTerranGateRing(GameObject gate)
        {
            foreach (var anim in gate.GetComponentsInChildren<PartAnimation>(true))
            {
                string n = anim.gameObject.name;
                if (n == TerranGate + "_anim_add") anim.applyMaterialChannels = true;
                if (n != TerranGate && !n.StartsWith(TerranGate + "_lod") && n != TerranGate + "_anim_add") continue;
                foreach (var t in anim.GetComponentsInChildren<Transform>(true))
                    if (t.name == n + "_part1") anim.SpinContinuously(t);
            }
        }

        /// <summary>
        /// Level::createAsteroids / PlayerAsteroid. The first 2..9 are big (cube +-30000, scale 1.2..2.19, no spin), the
        /// rest small (cube +-50000, scale 0.3..0.99, 0.1 rad/s on random axes). 'reject' (Unity position) re-rolls a
        /// spot, e.g. to keep the menu camera's orbit clear.
        /// </summary>
        public static Transform SpawnAsteroids(Database db, OrbitLayout layout, Transform parent = null, Func<Vector3, bool> reject = null)
            => SpawnField(db, layout, layout.asteroidCentre, layout.asteroidCount, -1, layout.asteroidSpread * 0.6f, layout.asteroidSpread, -1, parent, reject, "Asteroids");

        /// <summary>
        /// A field of 'count' asteroids around 'centre' (game units) of the orbit's kind: the first 'big' (-1 = roll 2..9
        /// like Level::createAsteroids) big in a cube of 'bigSide', the rest small in 'smallSide'. 'ore' >= 0 gives every
        /// one that ore (a remake Ore Mining site, MiningSite), else the orbit's ores. 'reject' takes Unity positions.
        /// </summary>
        public static Transform SpawnField(Database db, OrbitLayout layout, Vector3 centre, int count, int big, float bigSide, float smallSide,
                                           int ore, Transform parent, Func<Vector3, bool> reject, string name)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(layout.AsteroidAssembly));
            var explosion = AssembledObject.LoadPrefab(db.AssemblyByName(layout.AsteroidAssembly + "_explosion_anim"));
            var destroyedSound = GoF2Remake.Flight.CombatAudio.Load()?.asteroidDestroyed;
            // Remake: Explosion types 2 and 3 share the billboard 0x4213 and its purple fragments (Void Crystals); the ordinary
            // asteroids get a rock-coloured copy (Reference/tools/combat/make_rock_explosion.py).
            var explosionTexture = layout.asteroidType == 0 ? Resources.Load<Texture2D>("GoF2Combat/asteroid_explosion_rock") : null;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            if (prefab == null) return root;
            if (big < 0) big = Mathf.Min(count, Random.Range(2, 10));
            var bigPositions = new Vector3[big];
            var ores = ore >= 0 ? new List<(int item, int p)> { (ore, 100) } : OreProbabilities(db, layout);
            int oreCursor = 0;
            // Level::createAsteroids: a Novanium asteroid is type 3, mesh 18836 sn_asteroid_magma with its own explosion
            // (space_props.md, table UNK_00251fb0), whatever the orbit's own type; the same mesh radius as asteroid_01.
            GameObject magma = null, magmaExplosion = null;
            if (ores.Exists(o => o.item == Novanium))
            {
                magma = AssembledObject.LoadPrefab(db.AssemblyByName("sn_asteroid_magma"));
                magmaExplosion = AssembledObject.LoadPrefab(db.AssemblyByName("sn_asteroid_magma_explosion_anim"));
            }
            for (int i = 0; i < count; i++)
            {
                bool isBig = i < big;
                float side = isBig ? bigSide : smallSide;
                Vector3 pos;
                int tries = 0;
                bool bad;
                do
                {
                    pos = centre + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f)) * side;
                    bad = (isBig && TooClose(pos, bigPositions, i))   // intended rule: big ones 8000 apart
                          || (reject != null && reject(OrbitLayout.ToUnity(pos)));
                } while (bad && ++tries < 40);
                if (bad && reject != null) continue;   // no valid spot: leave it out rather than block the view
                if (isBig) bigPositions[i] = pos;

                float scale = isBig ? Random.Range(120, 220) * 0.01f : Random.Range(30, 100) * 0.01f;
                int rockOre = ore >= 0 ? ore : layout.modOres != null ? PickWeighted(layout.modOres) : PickOre(ores, ref oreCursor);
                // Quality from the scale; the big ones (and the largest small ones) are 50 % A, else D..B.
                int quality = scale < 0.4f ? 4 : scale < 0.7f ? 5 : scale < 0.92f ? 6 : Random.Range(0, 2) == 0 ? 7 : 4 + Random.Range(0, 3);
                var euler = new Vector3(Random.Range(0, 100), Random.Range(0, 100), Random.Range(0, 100)) * 0.01f * 2f * Mathf.PI;
                bool isMagma = rockOre == Novanium && magma != null;
                var model = isMagma ? magma : prefab;
                var go = Object.Instantiate(model, OrbitLayout.ToUnity(pos), OrbitLayout.RotationToUnity(euler), root);
                go.name = $"Asteroid {i}";
                go.transform.localScale = model.transform.localScale * scale;
                // PlayerAsteroid: hit radius = meshRadius * scale * 0.7, HP = scale * 100 + 30.
                var target = go.AddComponent<GoF2Remake.Flight.Target>();
                target.isAsteroid = true;
                target.radius = (isMagma ? MagmaMeshRadius : layout.AsteroidMeshRadius) * scale * 0.7f * M;
                target.maxHp = target.hp = scale * 100f + 30f;
                target.explosionPrefab = isMagma ? magmaExplosion : explosion;
                target.explosionScale = scale;
                target.explosionTexture = isMagma ? null : explosionTexture;
                target.destroyedSound = destroyedSound;
                target.oreItem = rockOre;
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

        /// <summary>The asteroid ores (OrbitLayout.OreProbabilities, in GoF2.Game for the bar's Ore Mining offers).</summary>
        public static List<(int item, int p)> OreProbabilities(Database db, OrbitLayout layout) => OrbitLayout.OreProbabilities(db, layout);
        public const int Novanium = OrbitLayout.Novanium;
        const float MagmaMeshRadius = 3547f;   // sn_asteroid_magma's bounding sphere, as asteroid_01's
        public static bool NovaniumOrbit(OrbitLayout layout) => OrbitLayout.NovaniumOrbit(layout);

        /// <summary>Level::createAsteroids 0xbd34a: a cursor walks the pairs; a roll under p takes that ore and moves on
        /// (wrapping after pair 5), a miss starts over at the top ore.</summary>
        /// <summary>Remake mods: an ore of the orbit's own list, by its chance weight.</summary>
        static int PickWeighted(List<(int item, int chance)> ores)
        {
            int total = 0;
            foreach (var o in ores) total += Mathf.Max(1, o.chance);
            int r = Random.Range(0, total);
            foreach (var o in ores) { r -= Mathf.Max(1, o.chance); if (r < 0) return o.item; }
            return ores[0].item;
        }

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
