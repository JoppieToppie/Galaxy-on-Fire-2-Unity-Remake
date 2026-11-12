// StarMapScene.cs
// StarMap: the 3D map (part of the partial StarMap; its fields are in StarMap.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class StarMap
    {
        // ---- 3D --------------------------------------------------------------------------------------------
        static Vector3 U(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        /// <summary>A game rotation (Rx * Ry * Rz style matrix) to the Unity rotation of an imported model: S * R * M with the
        /// z mirror S and the import's model flip M = diag(-1, 1, 1) (see OrbitLayout.RotationToUnity).</summary>
        static Quaternion ModelRotation(Quaternion game) =>
            (Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.Rotate(game) * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))).rotation;
        static void SetLayer(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }
        GameObject Spawn(string assembly, Transform parent)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(assembly));
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent, false);
            SetLayer(go);
            return go;
        }
        void BuildWorld()
        {
            // StarMap::initLights / the owner's scene: only the map camera renders; no fog, flat ambient.
            foreach (var c in Camera.allCameras) if (c.enabled) { c.enabled = false; disabledCameras.Add(c); }
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.enabled) { l.enabled = false; disabledLights.Add(l); }
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbient = RenderSettings.ambientLight;
            savedFog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.2f);
            RenderSettings.fog = false;

            world = new GameObject("Star Map 3D") { layer = Layer };
            var camGo = new GameObject("Star Map Camera") { layer = Layer };
            camGo.transform.SetParent(world.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 200f * M;
            cam.farClipPlane = 64000f * M;
            cam.depth = 50f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Visuals.ScreenAntialiasing.Register(cam);   // SMAA, FXAA or none by the Post-process AA option
            camGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // game rotation (0, pi, 0): looks along game +Z

            var bg = Spawn("galaxymap_background", world.transform);
            if (bg != null) bg.transform.localPosition = U(new Vector3(-3000f, -2500f, 0f));

            int systemCount = 0;
            foreach (var s in db.Systems) systemCount = Mathf.Max(systemCount, s.index + 1);
            if (suns.Length < systemCount) suns = new Transform[systemCount];
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || !visible[s.index]) continue;
                var sun = Spawn("galaxymap_sun", world.transform);
                if (sun == null) continue;
                sun.name = "Sun " + s.name;
                sun.transform.localPosition = U(GalaxyMap.SunPosition(s));
                // Modified: one constant world size for every sun (SunSize), like the original's: they never change size with the zoom.
                sunScale = sun.transform.localScale.x * SunSize;
                sun.transform.localScale = Vector3.one * sunScale;
                var mat = s.textureIndex >= 0 && s.textureIndex < assets.sunMaterials.Length ? assets.sunMaterials[s.textureIndex] : null;
                if (s.index == SupernovaSystem && SupernovaContained) mat = ContainedSunMaterial(mat) ?? mat;   // remake
                mat = GammaSun(mat);   // Modified: the glow's dim tail faded out like the original's
                if (mat != null) foreach (var r in sun.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
                suns[s.index] = sun.transform;
            }
            // The wormhole (galaxymap_wormhole, looping animation, rotation (0, pi, 0)) at the attacked system's sun, turned to
            // the camera every frame; scale 0.02 - 0.014 * zoom; in that system's view it moves to the attacked station's planet.
            int wsys = Session.VoidInvasionSystem;
            if (!Session.FreePlay && Session.CampaignMission > 0x1f && wsys >= 0 && wsys < suns.Length && suns[wsys] != null)
            {
                var w = Spawn("galaxymap_wormhole", world.transform);
                if (w != null)
                {
                    w.name = "Wormhole";
                    wormhole = w.transform;
                    wormholeScale = w.transform.localScale;
                    foreach (var a in w.GetComponentsInChildren<GoF2Remake.Visuals.PartAnimation>(true)) a.loop = true;
                }
            }
            // The overview's frame: every visible system's sun (game units).
            overviewMin = new Vector2(float.MaxValue, float.MaxValue);
            overviewMax = new Vector2(float.MinValue, float.MinValue);
            float zSum = 0f; int zCount = 0;
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || suns[s.index] == null) continue;
                var g = GalaxyMap.SunPosition(s);
                overviewMin = Vector2.Min(overviewMin, new Vector2(g.x, g.y));
                overviewMax = Vector2.Max(overviewMax, new Vector2(g.x, g.y));
                zSum += g.z; zCount++;
            }
            if (zCount == 0) overviewMin = overviewMax = Vector2.zero;
            overviewZ = zCount > 0 ? zSum / zCount : 3000f;
            start = current != null ? new Vector2(GalaxyMap.SunPosition(current).x, GalaxyMap.SunPosition(current).y) / 20f : Vector2.zero;
            pan = Vector2.zero;
            UpdateCamera();
        }
        void UpdateWormhole()
        {
            if (wormhole == null || cam == null) return;
            int wsys = Session.VoidInvasionSystem;
            var pos = SunGame(wsys);
            float t = 0f;
            if (zoomSystem == wsys && (systemView || zoomDir != 0))
            {
                t = systemView && zoomDir == 0 ? 1f : ZoomEase;
                int k = planets.FindIndex(p => p.station == Session.VoidInvasionStation);
                if (k >= 0) pos = Vector3.Lerp(pos, PlanetGame(k), t);
            }
            wormhole.localPosition = U(pos);
            wormhole.localScale = wormholeScale * (0.02f - 0.014f * t);   // the prefab is unscaled
            var d = cam.transform.position - wormhole.position;
            if (d.sqrMagnitude > 1e-8f) wormhole.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
        void RestoreScene()
        {
            foreach (var c in disabledCameras) if (c != null) c.enabled = true;
            foreach (var l in disabledLights) if (l != null) l.enabled = true;
            if (disabledCameras.Count > 0 || disabledLights.Count > 0)
            {
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientLight = savedAmbient;
                RenderSettings.fog = savedFog;
            }
            disabledCameras.Clear();
            disabledLights.Clear();
        }
        Vector3 SunGame(int system)
        {
            var s = db.Systems.Find(x => x.index == system);
            return s != null ? GalaxyMap.SunPosition(s) : Vector3.zero;
        }
        /// <summary>The galaxy view's pan limits (camera / 20): the original's [-500, 120] x [-400, 140], widened to every
        /// visible sun (remake, see Update).</summary>
        Vector2 PanMin => Vector2.Min(new Vector2(-500f, -400f), overviewMin / 20f);
        Vector2 PanMax => Vector2.Max(new Vector2(120f, 140f), overviewMax / 20f);
        Vector3 GalaxyCameraGame => new Vector3((start.x + pan.x) * 20f, (start.y + pan.y) * 20f, 0f);
        Vector3 SystemCameraGame(int system) => SunGame(system) - new Vector3(0f, 0f, 500f);
        float ZoomEase => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * Mathf.Clamp01(zoomMs / ZoomMs));
        /// <summary>Remake: the overview camera, back along game -Z until every visible sun (plus a margin for the labels) fits.</summary>
        Vector3 OverviewCameraGame()
        {
            var centre = (overviewMin + overviewMax) / 2f;
            var half = (overviewMax - overviewMin) / 2f + new Vector2(1500f, 1500f);
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * Mathf.Max(0.1f, cam.aspect);
            // The header and the footer take about a fifth of the height.
            float d = Mathf.Max(half.y / (tanV * 0.8f), half.x / tanH);
            return new Vector3(centre.x, centre.y, Mathf.Min(0f, overviewZ - d));
        }
        Vector3 GalaxyViewGame => overviewT > 0f ? Vector3.Lerp(GalaxyCameraGame, OverviewCameraGame(), OverviewEase) : GalaxyCameraGame;
        void UpdateCamera()
        {
            if (cam == null) return;
            cam.fieldOfView = Aspect.VerticalFov(1.1504f * Mathf.Rad2Deg, cam.aspect);
            Vector3 pos;
            if (zoomDir != 0 || systemView) pos = Vector3.Lerp(GalaxyViewGame, SystemCameraGame(zoomSystem), systemView && zoomDir == 0 ? 1f : ZoomEase);
            else pos = GalaxyViewGame;
            cam.transform.localPosition = U(pos);
            cam.farClipPlane = Mathf.Max(64000f, overviewZ - pos.z + 12000f) * M;
        }
        /// <summary>Remake: in or out of the full-map overview (only in the galaxy view).</summary>
        void SetOverview(bool on)
        {
            if (on == overview || systemView || zoomDir != 0 || revealing || DialogOpen || cam == null || OverviewMoving) return;
            overview = on;
            vel = Vector2.zero;
            if (on) { autoCentre = false; Play(assets.zoomOut); }
            else Play(assets.zoomIn);
            BuildHints(InputMode.Current);
        }
        /// <summary>Remake: from the overview back to the normal view, centred on 'system' (a tap / Enter on it there).</summary>
        void FocusFromOverview(int system)
        {
            if (system < 0) return;
            Select(system);
            var g = SunGame(system);
            pan = new Vector2(g.x, g.y) / 20f - start;
            centred = -1;
            autoCentre = true;   // confirmed once the camera is back (the spring waits, as for any centring)
            SetOverview(false);
        }
        /// <summary>Remake: two fingers pinching in open the overview, spreading close it.</summary>
        void PinchTrack(int id, Vector3 at, bool down)
        {
            if (!down && !pinch.ContainsKey(id)) return;
            pinch[id] = at;
            if (pinch.Count != 2) { pinchStart = 0f; return; }
            Vector2 a = default, b = default;
            int n = 0;
            foreach (var v in pinch.Values) { if (n++ == 0) a = v; else b = v; }
            float d = Vector2.Distance(a, b);
            if (pinchStart <= 0f || down) { pinchStart = d; return; }
            if (d < pinchStart * 0.75f) { SetOverview(true); pinchStart = d; }
            else if (d > pinchStart * 1.33f) { SetOverview(false); pinchStart = d; }
        }
        /// <summary>StarMap::initStarSystem: planets, orbit rings and a light at the sun.</summary>
        void BuildSystem(int system)
        {
            DestroySystem();
            var sys = db.Systems.Find(s => s.index == system);
            systemRoot = new GameObject("System " + sys?.name) { layer = Layer };
            systemRoot.transform.SetParent(world.transform, false);
            planets.AddRange(GalaxyMap.SystemLayout(db, sys));
            foreach (var p in planets)
            {
                var planet = Spawn($"galaxymap_planet_{p.textureIndex:000}", systemRoot.transform) ?? Spawn("galaxymap_planet_000", systemRoot.transform);
                if (planet != null && (World.SkyLayers.InPlanetRingOrbit(p.station) || Modding.ModWorld.MapRing(p.station))) AddPlanetRing(planet);
                if (planet != null) ApplyModPlanet(planet, p.station);
                if (planet != null) LightBySun(planet);
                planetObjects.Add(planet != null ? planet.transform : null);
                planetScales.Add(planet != null ? planet.transform.localScale / 128f : Vector3.zero);
                var orbit = Spawn("galaxymap_orbit", systemRoot.transform);
                if (orbit != null) orbit.transform.localScale = Vector3.one * (2f * p.radius / 65536f / 128f);
                orbitObjects.Add(orbit != null ? orbit.transform : null);
            }
            // The light at the sun (LightSetLightPosition, ambient 0.2, diffuse 2.0): GoF2/MapPlanet (LightBySun) with the
            // sun's position as a global (UpdateSystemTransforms).
            yaw = 4096f;
            pitch = -4096f;
            yawVel = pitchVel = 0f;
            selectedPlanet = frontPlanet = -1;
            autoRotate = false;
            BuildPlanetItems();
            UpdateSystemTransforms();
        }
        /// <summary>Remake: StarMap::initStarSystem's ring (map_planet_ring 16853, on the planet's matrix) for the stations whose
        /// orbit has the planet ring sky (Status::orbitHasPlanetRing: 120, 126, 130, 132). The planets with texture 9, 16
        /// or 21 have it in their prefab; for these stations it was left out, so their planet showed no ring on the map.
        /// A copy of the ring in galaxymap_planet_009 (its local transform: the planet's own).</summary>
        void AddPlanetRing(GameObject planet)
        {
            foreach (var t in planet.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("map_planet_ring")) return;   // already in the prefab
            var donor = AssembledObject.LoadPrefab(db.AssemblyByName("galaxymap_planet_009"));
            if (donor == null) return;
            Transform ring = null;
            foreach (var t in donor.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("map_planet_ring")) { ring = t; break; }
            if (ring == null) return;
            var go = Instantiate(ring.gameObject, planet.transform, false);
            go.name = ring.name;
            SetLayer(go);
        }
        /// <summary>StarMap::initStarSystem's light at the sun: the planet's materials swapped for GoF2/MapPlanet copies (its
        /// texture and tint), lit toward _StarMapSunPos per pixel without the distance fade of URP's point lights.</summary>
        static void LightBySun(GameObject planet)
        {
            if (planetShader == null) planetShader = Resources.Load<Shader>("GoF2StarMap/MapPlanet");
            if (planetShader == null) return;
            foreach (var r in planet.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || !(src.shader.name.Contains("Lit") || src.shader.name == "GoF2/Hull")) continue;   // the planet's lit hull only (not a glow)
                    if (!planetMaterials.TryGetValue(src, out var m) || m == null)
                    {
                        m = new Material(planetShader) { name = src.name + " (map planet)" };
                        if (src.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", src.GetTexture("_BaseMap")); m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap")); }
                        if (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                        planetMaterials[src] = m;
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }
        void DestroySystem()
        {
            if (systemRoot != null) Destroy(systemRoot);
            systemRoot = null;
            planets.Clear();
            planetObjects.Clear();
            orbitObjects.Clear();
            planetScales.Clear();
            planetDiscs.Clear();
            foreach (var it in planetItems) it.root.RemoveFromHierarchy();
            planetItems.Clear();
        }
        Quaternion ContainerRotation() =>
            Quaternion.AngleAxis(pitch / 65536f * 360f, Vector3.right) * Quaternion.AngleAxis(yaw / 65536f * 360f, Vector3.up)
            * Quaternion.AngleAxis(-180f / 32f, Vector3.forward);
        Vector3 PlanetGame(int k)
        {
            var p = planets[k];
            var local = Quaternion.AngleAxis(p.angle / 65536f * 360f, Vector3.up) * new Vector3(0f, 0f, p.radius);
            return SunGame(zoomSystem) + ContainerRotation() * local / 128f;
        }
        void UpdateSystemTransforms()
        {
            if (systemRoot == null) return;
            var rc = ContainerRotation();
            var sun = SunGame(zoomSystem);
            Shader.SetGlobalVector(SunPosId, systemRoot.transform.TransformPoint(U(sun)));   // GoF2/MapPlanet's light
            for (int k = 0; k < planets.Count; k++)
            {
                var pl = planetObjects[k];
                if (pl != null)
                {
                    pl.localPosition = U(PlanetGame(k));
                    pl.localRotation = ModelRotation(rc * Quaternion.AngleAxis(spin * Mathf.Rad2Deg, Vector3.up));
                    pl.localScale = planetScales[k];
                }
                var orbit = orbitObjects[k];
                if (orbit != null)
                {
                    orbit.localPosition = U(sun);
                    orbit.localRotation = ModelRotation(rc * Quaternion.AngleAxis(planets[k].orbitYaw * Mathf.Rad2Deg, Vector3.up));
                }
            }
            FaceDiscs();
        }
        /// <summary>Remake mods: the planetTexture discs face the map's camera (like the planet billboards in orbit).</summary>
        void FaceDiscs()
        {
            if (cam == null) return;
            foreach (var d in planetDiscs)
                if (d != null) d.rotation = Quaternion.LookRotation(d.position - cam.transform.position, cam.transform.up);
        }
        /// <summary>Remake mods: a station's own planet on the map. "mapTexture": the image wrapped round the map's sphere (a
        /// sphere with 0..1 UVs takes the place of the game's mesh, whose UVs are one cell of the planet atlas; still lit by
        /// GoF2/MapPlanet). Else "planetTexture": the orbit's disc, facing the camera, unlit like the orbit's billboard. Else
        /// the game's planet for its "planet" number.</summary>
        void ApplyModPlanet(GameObject planet, int station)
        {
            if (modPlanetRevision != Modding.ModManager.Revision)
            {
                foreach (var m in modPlanetMaterials.Values) if (m != null) Destroy(m);
                modPlanetMaterials.Clear();
                modPlanetRevision = Modding.ModManager.Revision;
            }
            string wrap = Modding.ModWorld.MapTexture(station), disc = wrap == null ? Modding.ModWorld.PlanetTexture(station) : null;
            if (wrap == null && disc == null) return;
            var hull = new List<MeshRenderer>();
            foreach (var r in planet.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("map_planet_") && !r.name.StartsWith("map_planet_ring")) hull.Add(r);
            if (hull.Count == 0) return;
            if (wrap != null)
            {
                var mat = ModPlanetMaterial(wrap, false, hull[0].sharedMaterial);
                if (mat == null) return;
                foreach (var r in hull)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mf.sharedMesh = WrappedSphere(mf.sharedMesh.bounds.extents.x);
                    r.sharedMaterial = mat;
                }
                return;
            }
            var discMat = ModPlanetMaterial(disc, true, null);
            if (discMat == null) return;
            var mesh = hull[0].GetComponent<MeshFilter>()?.sharedMesh;
            float radius = mesh != null ? mesh.bounds.extents.x : 1600f;
            foreach (var r in hull) r.enabled = false;
            var go = new GameObject("map_planet_disc") { layer = Layer };
            go.transform.SetParent(planet.transform, false);
            go.transform.localScale = Vector3.one * radius * 2.1f;   // the orbit images keep a little room round the disc
            go.AddComponent<MeshFilter>().sharedMesh = DiscQuad();
            go.AddComponent<MeshRenderer>().sharedMaterial = discMat;
            planetDiscs.Add(go.transform);
        }
        /// <summary>A mod planet's map material: the wrapped one a copy of the game's map planet material (LightBySun makes it
        /// GoF2/MapPlanet), the disc one a copy of the mods' alpha-blended fx material.</summary>
        static Material ModPlanetMaterial(string name, bool disc, Material planetSource)
        {
            string key = (disc ? "disc|" : "wrap|") + name;
            if (modPlanetMaterials.TryGetValue(key, out var m)) return m;
            var tex = Modding.ModBackdrop.Texture(name);
            Material made = null;
            if (tex != null && disc)
            {
                var template = Modding.ModAssets.Get()?.fxAlpha;
                if (template != null)
                {
                    made = new Material(template) { name = "map disc " + name };
                    made.SetTexture("_MainTex", tex);
                    made.SetTextureScale("_MainTex", Vector2.one);
                    made.SetTextureOffset("_MainTex", Vector2.zero);
                    made.SetColor("_Color", Color.white);
                }
            }
            else if (tex != null && planetSource != null)
            {
                tex.wrapModeU = TextureWrapMode.Repeat;
                made = new Material(planetSource) { name = "map planet " + name };
                if (made.HasProperty("_BaseMap")) { made.SetTexture("_BaseMap", tex); made.SetTextureScale("_BaseMap", Vector2.one); made.SetTextureOffset("_BaseMap", Vector2.zero); }
                if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", Color.white);
            }
            return modPlanetMaterials[key] = made;
        }
        static Mesh WrappedSphere(float radius) => PlanetMeshes.WrappedSphere(radius);
        static Mesh DiscQuad() => PlanetMeshes.DiscQuad();
    }
}
