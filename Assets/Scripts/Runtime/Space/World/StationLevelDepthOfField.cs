// StationLevelDepthOfField.cs
// StationLevel: depth of field (Settings.HangarDepthOfField) (part of the partial StationLevel; its fields are in StationLevel.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace GoF2Remake.World
{
    public partial class StationLevel
    {
        void SetupDepthOfField()
        {
            if (mainCamera == null) return;
            var go = new GameObject("Hangar depth of field") { layer = DofLayer };
            go.transform.SetParent(transform, false);
            dofVolume = go.AddComponent<Volume>();
            dofVolume.isGlobal = true;
            dofVolume.priority = 50f;
            // A profile made in code: URP keeps the depth of field shaders in a build only because
            // Assets/Settings/HangarDepthOfFieldShaders.asset has it on (ShaderBuildPreprocessor scans the profile assets).
            dofProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            dof = dofProfile.Add<UnityEngine.Rendering.Universal.DepthOfField>(true);
            bokehDof = !Application.isMobilePlatform;
            dof.mode.Override(bokehDof ? UnityEngine.Rendering.Universal.DepthOfFieldMode.Bokeh : UnityEngine.Rendering.Universal.DepthOfFieldMode.Gaussian);
            dof.focalLength.Override(300f);
            dof.aperture.Override(2f);
            dof.gaussianMaxRadius.Override(1f);
            dof.highQualitySampling.Override(false);
            dofVolume.profile = dofProfile;   // an instance, not a shared profile: Bootstrap.ApplyPostProcessing leaves it alone
            dofVolume.enabled = false;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(mainCamera);
            if (data != null) data.volumeLayerMask |= 1 << DofLayer;
        }
        /// <summary>Remake: under the hangar's depth of field the room's specular highlights are left out. The key light's
        /// highlights on the room's glossy parts (the Terran forcefield's top beam) are tiny HDR glints that pop in and out as
        /// the camera drifts a few centimetres; sharp they are a pixel's sparkle, but the blur spreads each into a bright
        /// blob, which flickered, worst where the beam meets the screen's top edge (measured with the camera stepped like its
        /// drift: 0.7-1 % of the top strip's pixels flickering, Bokeh or Gaussian, with or without DLSS; 0 with the
        /// highlights off, as with the depth of field off). Under the blur the room looks the same without them.</summary>
        void CollectRoomHighlights(GameObject room)
        {
            roomHighlights.Clear();
            roomMatte = false;
            if (room == null) return;
            var copies = new Dictionary<Material, Material>();
            foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                Material[] matte = null;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty("_SpecularHighlights") || m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF")) continue;
                    if (!copies.TryGetValue(m, out var c))
                    {
                        c = new Material(m) { name = m.name + " (no highlights)" };
                        c.SetFloat("_SpecularHighlights", 0f);
                        c.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                        copies[m] = c;
                    }
                    matte ??= (Material[])mats.Clone();
                    matte[i] = c;
                }
                if (matte != null) roomHighlights.Add((r, mats, matte));
            }
        }
        void SetRoomMatte(bool matte)
        {
            if (roomMatte == matte) return;
            roomMatte = matte;
            foreach (var (r, lit, m) in roomHighlights)
                if (r != null) r.sharedMaterials = matte ? m : lit;
        }
        void UpdateDepthOfField()
        {
            if (dofVolume == null) return;
            bool on = Settings.HangarDepthOfField && View == StationView.Hangar && playerShip != null && mainCamera != null
                      && !GoF2Remake.Data.VrFlags.Enabled && !GoF2Remake.World.SpaceHooks.StarMapOpen;
            if (dofVolume.enabled != on) dofVolume.enabled = on;
            SetRoomMatte(on);
            if (!on) return;
            float focus = Mathf.Max(1f, Vector3.Distance(mainCamera.transform.position, playerShip.position));
            // Inspect ship / the Mod Builder: focused on the ship's centre, through a wider lens (300 mm at f/2 kept only ~6 m
            // sharp up close).
            float inspect = Mathf.SmoothStep(0f, 1f, Inspect.Blend);
            if (inspect > 0f) focus = Mathf.Lerp(focus, Mathf.Max(1f, Vector3.Distance(mainCamera.transform.position, Inspect.Centre)), inspect);
            dof.focalLength.value = Mathf.Lerp(300f, 50f, inspect);
            dof.focusDistance.value = focus;
            dof.gaussianStart.value = focus + Mathf.Lerp(25f, Inspect.Radius * 1.2f + 10f, inspect);
            dof.gaussianEnd.value = focus + Mathf.Lerp(140f, Inspect.Radius * 3f + 60f, inspect);
            // Orbiting, a physical lens can't both keep the whole hull sharp and blur the room: at ~80 m a 50 mm lens blurred
            // nothing (the option seemed off in the Mod Builder), a longer one the hull's own far side. The orbit takes the
            // Gaussian blur instead, which starts past the hull (gaussianStart above) and blurs the room behind it.
            var mode = inspect > 0.5f || !bokehDof ? UnityEngine.Rendering.Universal.DepthOfFieldMode.Gaussian
                                                   : UnityEngine.Rendering.Universal.DepthOfFieldMode.Bokeh;
            if (dof.mode.value != mode) dof.mode.value = mode;
            dof.gaussianMaxRadius.value = Mathf.Lerp(1f, 1.5f, inspect);
        }
        /// <summary>Remake: the rooms' thin, glossy parts (the Nivelian bar stools, the Midorian window frames) shimmered as the
        /// camera swayed: SMAA works within one frame. The station camera takes URP's temporal AA instead (its slow camera
        /// and still rooms are where TAA has nothing to smear) while the Post-process AA option is SMAA, unless a temporal
        /// upscaler (DLSS, FSR 2+, STP) already anti-aliases or MSAA is on (URP's TAA needs it off); then SMAA as before.
        /// FXAA / off as picked. ScreenAntialiasing follows the options from here on.</summary>
        void ApplyAntialiasing()
        {
            if (mainCamera == null) return;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(mainCamera);
            if (data == null) return;
            data.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            ScreenAntialiasing.Register(mainCamera, prefersTemporal: true);
        }
        /// <summary>Game camera rotation, order 2 (Ry * Rx * Rz, looking down local -Z) -> Unity (looking down +Z).</summary>
        static Quaternion CameraRotation(float x, float y, float z) =>
            Quaternion.Euler(-x * Mathf.Rad2Deg, -y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);
        /// <summary>Remake (the Mod Builder's rooms on a game room, "gof2:hangar_terran"): the markers that room has in the
        /// game's tables, room metres as interiors.json's "markers": the pad, the camera and where it looks, the parked pads,
        /// the flights' gate; a bar's resting and first-visit cameras and its visitor spots. Null: not one of the game's rooms.</summary>
        public static Dictionary<string, Vector3> GameRoomMarkers(string assembly)
        {
            var m = new Dictionary<string, Vector3>();
            int h = System.Array.IndexOf(StationTables.HangarRoom, assembly);
            if (h >= 0)
            {
                const int probe = 10;   // the Phantom: the pads' floor is the pivot less its height
                float FloorY(int slot) => StationTables.PadPivotY(h, slot, probe, Quaternion.identity) - StationTables.ShipY(probe) * M;
                var pad = new Vector3(0f, FloorY(-1), 0f);
                m["pad"] = pad;
                var pivot = pad + Vector3.up * (StationTables.ShipY(probe) * M);
                var cam = pivot + OrbitLayout.ToUnity(StationTables.HangarCameraPos[h]);
                m["camera"] = cam;
                var r = StationTables.HangarCameraRot[h];
                m["camera_target"] = cam + CameraRotation(r.x, r.y, StationTables.HangarCameraRoll) * Vector3.forward * Vector3.Distance(cam, pivot);
                var slots = StationTables.ParkedSlots[h];
                if (slots != null)
                    for (int i = 0; i < slots.Length; i++)
                        m["parked_" + (i + 1)] = Vector3.Scale(OrbitLayout.ToUnity(slots[i]), new Vector3(1f, 0f, 1f)) + Vector3.up * FloorY(i);
                var lane = StationTables.HangarLanes[h];
                if (lane != null && !lane.bays)
                {
                    m["gate"] = lane.gate;
                    m["gate_out"] = lane.gate + lane.outward * 300f;
                }
                return m;
            }
            int b = System.Array.IndexOf(StationTables.BarRoom, assembly);
            if (b < 0) return null;
            var rest = OrbitLayout.ToUnity(StationTables.BarCameraRest[b]);
            m["camera"] = rest;
            m["camera_target"] = rest + CameraRotation(0f, StationTables.BarCameraRestYaw[b], 0f) * Vector3.forward * 60f;
            var start = OrbitLayout.ToUnity(StationTables.BarCameraStart[b]);
            m["camera_start"] = start;
            m["camera_start_target"] = start + CameraRotation(0f, StationTables.BarCameraStartYaw[b], 0f) * Vector3.forward * 60f;
            var spots = StationTables.VisitorSlots[b];
            for (int i = 0; i < spots.Length; i++) m["visitor_" + (i + 1)] = OrbitLayout.ToUnity(spots[i]);
            return m;
        }
        /// <summary>Remake: the Vossk bar's bartender arms (VosskBarBots). The first time the bar shows, they take their rest
        /// pose (the models sit at the room's origin until their animation is applied, and that is parsed only once the bar
        /// is active); then each replays when its slot comes round, the end arm as bot_a or bot_c.</summary>
        void UpdateVosskBots(float dtMs)
        {
            if (!vosskBotsPosed)
            {
                vosskBotsPosed = true;
                vosskBotA.Hold();
                vosskBotB.Hold();
            }
            var start = vosskBots.Tick(dtMs, () => Random.value);
            if ((start & VosskBarBots.Start.EndArm) != 0)
            {
                var play = vosskBots.EndArmPlaysC ? vosskBotC : vosskBotA;
                var rest = vosskBots.EndArmPlaysC ? vosskBotA : vosskBotC;
                rest.gameObject.SetActive(false);
                play.gameObject.SetActive(true);
                play.loop = false;
                play.Restart();
            }
            if ((start & VosskBarBots.Start.SecondArm) != 0) vosskBotB.Restart();
        }
        /// <summary>Modified: the Terran hangar's bots (hangar_terran_bot_a..e_anim + _anim_add) and the Nivelian hangar's
        /// (hangar_nivelian_bot_a..c) become one-shots, hidden in their first pose until TerranHangarBots picks them.</summary>
        void SetupTerranBots(GameObject roomGo)
        {
            terranBots = null;
            terranBotParts.Clear();
            if (roomGo == null) return;
            // Modified: the Nivelian hangar's bots too (hangar_nivelian_bot_a..c, BuildHangar), the same way.
            var byBot = new SortedDictionary<char, List<PartAnimation>>();
            foreach (var a in roomGo.GetComponentsInChildren<PartAnimation>(true))
            {
                string n = a.gameObject.name;
                string prefix = n.StartsWith("hangar_terran_bot_") ? "hangar_terran_bot_" : n.StartsWith("hangar_nivelian_bot_") ? "hangar_nivelian_bot_" : null;
                if (prefix == null || n.Length <= prefix.Length) continue;
                if (!byBot.TryGetValue(n[prefix.Length], out var parts)) byBot[n[prefix.Length]] = parts = new List<PartAnimation>();
                parts.Add(a);
            }
            foreach (var parts in byBot.Values)
            {
                foreach (var a in parts)
                {
                    a.loop = false;
                    a.Hold();
                    a.gameObject.SetActive(false);
                }
                terranBotParts.Add(parts);
            }
            if (terranBotParts.Count > 0) terranBots = new TerranHangarBots(terranBotParts.Count, () => Random.value);
            hangarRobotsShown = true;
            ApplyRobotOptions();
        }
        /// <summary>Remake (the user's request, 2026-11-05): Options > Graphics "Hangar robots" / "Bar robots"
        /// (Settings.HangarRobots / BarRobots), read every frame so a change from the station's Options page shows at once.
        /// Off hides the bots and drops their schedulers; on again starts them afresh (the first flight within 1.5 s, the
        /// arms back in their rest poses, the first plays within 4 s).</summary>
        void ApplyRobotOptions()
        {
            bool hangar = Settings.HangarRobots;
            if (hangar != hangarRobotsShown)
            {
                hangarRobotsShown = hangar;
                foreach (var parts in terranBotParts)
                    foreach (var a in parts) if (a != null) a.gameObject.SetActive(false);   // a bot shows only while it flies
                terranBots = hangar && terranBotParts.Count > 0 ? new TerranHangarBots(terranBotParts.Count, () => Random.value) : null;
            }
            bool bar = Settings.BarRobots;
            if (bar != barRobotsShown)
            {
                barRobotsShown = bar;
                foreach (var a in barBotParts) if (a != null) a.gameObject.SetActive(bar);
                if (vosskBotA != null && vosskBotB != null && vosskBotC != null)
                {
                    vosskBotC.gameObject.SetActive(false);   // one end-arm animation shows at a time (BuildBar)
                    vosskBots = bar ? new VosskBarBots(() => Random.value) : null;
                    vosskBotsPosed = false;                  // UpdateVosskBots puts them in their rest poses again
                }
            }
        }
        /// <summary>Modified: one Terran bot flies at a time (TerranHangarBots); it shows while it flies.</summary>
        void UpdateTerranBots(float dtMs)
        {
            int prev = terranBots.Current;
            bool playing = false;
            if (prev >= 0)
                foreach (var a in terranBotParts[prev]) if (a != null && a.play) playing = true;
            int next = terranBots.Tick(dtMs, playing, () => Random.value);
            if (prev >= 0 && terranBots.Current != prev)
                foreach (var a in terranBotParts[prev]) if (a != null) a.gameObject.SetActive(false);
            if (next < 0) return;
            foreach (var a in terranBotParts[next])
            {
                if (a == null) continue;
                a.gameObject.SetActive(true);
                a.loop = false;
                a.Restart();
            }
        }
    }
}
