// ValkyrieLevelsMission80.cs
// ValkyrieLevels: mission 80's frames (LevelScript::process) (part of the partial ValkyrieLevels; its fields are in ValkyrieLevels.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class ValkyrieLevels
    {
        void Tick80(float dtMs)
        {
            TurretGunsByPitch();   // Modified
            var host = battlestation != null ? battlestation.transform.position : At(Host80);
            // OrbitScale: the cameras and blasts off the battlestation / on Kothar's hull (both grown) scale with them.
            float k = StructureScale;
            switch (Step)
            {
                case 0:
                    if (Over(0))
                    {
                        EnterCutscene();
                        // Modified: the shot is on the battlestation itself (the laser only appears LaserDelay80 ms in, step 1).
                        var shot = new Vector3(-20000, 5000, -35000);
                        cam.LookAtUnity(host + ToUnity(shot) * k, battlestation != null ? battlestation.transform : battleLaser?.transform);
                        camStartZ80 = cam.Camera != null ? cam.Camera.position.z : 0f;
                        // OrbitScale: the pull back ends where the scaled orbit has the original's end (toward Kothar), at least
                        // CamPullBack80 on (at 1x: exactly that).
                        camEndZ80 = Mathf.Max(camStartZ80 + CamPullBack80, OrbitScale.PlaceUnity(ToUnity(Host80 + shot) + new Vector3(0f, 0f, CamPullBack80)).z);
                        PlayAtCamera(assets?.deepScienceAttacked);   // 0x461
                        Step = 1;
                    }
                    break;
                case 1:
                    // Modified: the camera pulls back from the battlestation, still looking at it (was translate(-1, 1, -12) x dt,
                    // 0x167790).
                    PullBack80(stepMs);
                    if (stepMs >= LaserDelay80)
                    {
                        // Modified: the laser starts LaserDelay80 ms into the shot (it played from the level's start), its
                        // firing animation from the beginning.
                        if (battleLaser != null)
                        {
                            battleLaser.SetActive(true);
                            // the charge-up lives in the parts' material channels (like 89's supernova front)
                            laserAnims80 = battleLaser.GetComponentsInChildren<PartAnimation>(true);
                            laserFullOn80 = new float[laserAnims80.Length];
                            foreach (var a in laserAnims80) { a.applyMaterialChannels = true; a.speed = 1f; }
                            PartAnimation.PlayOnce(battleLaser);
                            // Modified: each part plays from its start to LaserHoldAt80 of its length in LaserChargeMs80, then is
                            // held there (step 2) until the cut: its tail (fading out) no longer shows, nor a second hit.
                            for (int i = 0; i < laserAnims80.Length; i++)
                            {
                                var a = laserAnims80[i];
                                float start = Mathf.Clamp(a.loopStartMs, 0f, a.LengthMs);
                                laserFullOn80[i] = Mathf.Lerp(start, a.LengthMs, Mathf.Clamp01(LaserHoldAt80));
                                a.speed = Mathf.Max(0.01f, (laserFullOn80[i] - start) / LaserChargeMs80);
                            }
                        }
                        laserLooping80 = false;
                        Step = 2;
                    }
                    break;
                case 2:
                    PullBack80(LaserDelay80 + stepMs);   // Modified: still pulling back
                    // Modified: once charged the laser stays fully on (held at its LaserHoldAt80 pose) until the
                    // camera cuts to the Void.
                    if (!laserLooping80 && stepMs >= LaserChargeMs80 && laserAnims80 != null)
                    {
                        for (int i = 0; i < laserAnims80.Length; i++) laserAnims80[i].Hold(laserFullOn80[i]);
                        laserLooping80 = true;
                    }
                    if (stepMs >= LaserWindow80)
                    {
                        if (battleLaser != null) battleLaser.SetActive(false);
                        Step = 6;
                    }
                    break;
                case 6:
                {
                    // The hit: the camera on the impact point, the station's laser visible.
                    var hit = new Vector3(12487, -11451, 5958);
                    cam.LookAtUnity(At(new Vector3(-16974, -17691, 37541)), null, At(hit));   // OrbitScale: absolute (by Kothar's hull)
                    if (stationLaser != null)
                    {
                        stationLaser.SetActive(true);
                        // Modified: held at its first frame until step 7 fires it (it looped on its own for these 800 ms, then
                        // PlayOnce started it over: it vanished and fired again).
                        foreach (var a in stationLaser.GetComponentsInChildren<PartAnimation>(true))
                        {
                            a.applyMaterialChannels = true;
                            a.Hold(Mathf.Clamp(a.loopStartMs, 0f, a.LengthMs));
                        }
                    }
                    Step = 7;
                    break;
                }
                case 7:
                    // LevelScript::process 0x16d74c, sub-steps 7-9 (the dolly runs on through 8 and 9): translate(0.5, 0.2, -0.3)
                    // x dt, read from the disassembly (the decompile lists the arguments out of order and drops the 0.3).
                    cam.SetDolly(new Vector3(0.5f, 0.2f, -0.3f));
                    if (stepMs >= HitWait80)
                    {
                        if (stationLaser != null)
                        {
                            foreach (var a in stationLaser.GetComponentsInChildren<PartAnimation>(true)) a.applyMaterialChannels = true;   // Modified: its fades
                            PartAnimation.PlayOnce(stationLaser);
                        }
                        if (level.Station != null)
                        {
                            PartAnimation.PlayOnce(level.Station);   // the station's explosion animation
                            LoopEmitters80(level.Station);   // Modified: not the flames, they go on looping
                        }
                        Step = 8;
                    }
                    break;
                case 8:
                    if (stepMs >= HitWait80)
                    {
                        var hitPoint = new Vector3(12487, -11451, 5958);
                        // Modified: the blast HitBlastOffset80 from the hit point (was the hit point itself).
                        // OrbitScale: the hit point where the scaled orbit has it, the offsets on Kothar's hull grown with it.
                        Explosion.Spawn(0, At(hitPoint) + ToUnity(HitBlastOffset80) * k, Vector3.forward, 3f, CombatAssets.Pick(combat?.explosionBig), true);   // sound 18
                        // Level+0x58 at the hit point and Level+0x5c at hit + (4500, 0, 1000) (record 24, both emitting for
                        // the rest of the level; the second point drifts +2 u/ms along x in state 9).
                        burnA = new GameObject("Deep science burn A").transform;
                        burnA.position = At(hitPoint);
                        new WreckBurn(burnA, WreckBurn.DeepScience).SetEmitting(true);
                        burnB = new GameObject("Deep science burn B").transform;
                        burnB.position = At(hitPoint) + ToUnity(new Vector3(4500, 0, 1000)) * k;
                        new WreckBurn(burnB, WreckBurn.DeepScience).SetEmitting(true);
                        // Modified: the fires' own pose under each burn point, matched to the original (Inspector values).
                        PlaceBurn80(burnA, BurnPosA80 * k, BurnRotA80);
                        PlaceBurn80(burnB, BurnPosB80 * k, BurnRotB80);
                        // Modified: fire B rides the blown-off part 4 of the station (it drifted +2 u/ms along x on its own).
                        var part4 = FindStationPart80(BurnBFollows80);
                        if (part4 != null) burnB.SetParent(part4, true);
                        else Debug.LogWarning($"[80] {BurnBFollows80} not found: fire B drifts on its own");
                        Step = 9;
                    }
                    break;
                case 9:
                    if (burnB != null && burnB.parent == null) burnB.position += ToUnity(new Vector3(2f * dtMs, 0, 0)) - ToUnity(Vector3.zero);   // Modified: only without part 4
                    if (stepMs >= 8000f)
                    {
                        LeaveCutscene();
                        if (stationLaser != null) stationLaser.SetActive(false);
                        Step = 10;
                    }
                    break;
                case 10:
                    if (Over(9))
                    {
                        EnterCutscene();
                        var shot = new Vector3(20000, 5000, -20000);
                        cam.LookAtUnity(host + ToUnity(shot) * k, battlestation != null ? battlestation.transform : null);
                        jumpShotMs80 = 0f;
                        jumpShotStartZ80 = cam.Camera != null ? cam.Camera.position.z : 0f;
                        // OrbitScale: the shot ends where the scaled orbit has the original's end (past Kothar), at least
                        // JumpShotTravel80 on (at 1x: exactly that).
                        jumpShotEndZ80 = Mathf.Max(jumpShotStartZ80 + JumpShotTravel80, OrbitScale.PlaceUnity(ToUnity(Host80 + shot) + new Vector3(0f, 0f, JumpShotTravel80)).z);
                        Step = 11;
                    }
                    break;
                case 11:
                    // Modified: the camera moves along Unity Z only, JumpShotTravel80 further by the end of the cutscene (steps 11-13;
                    // was translate(0.5, 0.2, -4) x dt, 0x16caa6 / 0x16d63e / 0x16d492).
                    JumpShot80(dtMs);
                    if (stepMs >= 4000f)
                    {
                        Explosion.Spawn(0, host + ToUnity(new Vector3(-4000, 5000, -6000)) * k, Vector3.forward, 3f, CombatAssets.Pick(combat?.explosionBig), true);
                        Step = 12;
                    }
                    break;
                case 12:
                    JumpShot80(dtMs);   // Modified
                    cam.Rumble = Mathf.Clamp01(stepMs / 8000f);
                    if (stepMs >= 8000f)
                    {
                        // Modified: like 78's jump: aligned with the battlestation instead of facing the camera, and 78's custom
                        // texture (Resources/GoF2Custom/hyperdrive_78) when there is one.
                        SpawnHyperDrive(ToGame(host) + new Vector3(0, 0, -10000), 20f, texture78: true);
                        if (fx != null)
                        {
                            fx.transform.SetPositionAndRotation(host + HyperDrivePos80 * k, Quaternion.Euler(HyperDriveRot80));   // OrbitScale
                            fx.transform.localScale = Vector3.one * (HyperDriveScale80 * k);
                            fxFixed = true;
                        }
                        Step = 13;
                    }
                    break;
                case 13:
                    JumpShot80(dtMs);   // Modified
                    cam.Rumble = Mathf.Clamp01(1f - stepMs / 10000f);
                    if (stepMs >= 2151f && battlestation != null && battlestation.activeSelf) battlestation.SetActive(false);
                    if (stepMs >= 10001f) { LeaveCutscene(); Step = 14; }
                    break;
            }
        }
        void JumpShot80(float dtMs)
        {
            cam.SetDolly(Vector3.zero);
            jumpShotMs80 += dtMs;
            if (cam.Camera == null) return;
            var p = cam.Camera.position;
            cam.Camera.position = new Vector3(p.x, p.y, Mathf.Lerp(jumpShotStartZ80, jumpShotEndZ80, Mathf.Clamp01(jumpShotMs80 / JumpShotMs80)));
        }
        void MakeFlamePieces80()
        {
            flamePieces80.Clear();
            flameSource80 = null;
            var source = FindStationPart80(FlameSource80);
            var mf = source != null ? source.GetComponent<MeshFilter>() : null;
            var sr = source != null ? source.GetComponent<Renderer>() : null;
            if (mf == null || sr == null || mf.sharedMesh == null) { Debug.LogWarning($"[80] flames: {FlameSource80} not found"); return; }
            var mesh = mf.sharedMesh;
            if (!mesh.isReadable) { Debug.LogWarning($"[80] flames: turn on Read/Write for {mesh.name}'s model to split them"); return; }

            var glows = new List<(int part, Renderer r, Bounds bounds)>();
            foreach (int k in FlameTargets80)
            {
                var t = FindStationPart80(FlameTargetPrefix80 + k);
                var r = t != null ? t.GetComponent<Renderer>() : null;
                if (r != null) glows.Add((k, r, r.bounds));
                else Debug.LogWarning($"[80] flames: {FlameTargetPrefix80}{k} not found");
            }
            if (glows.Count == 0) return;
            int largest = 0;   // part 0's glow (the whole station)
            for (int g = 1; g < glows.Count; g++)
                if (glows[g].bounds.size.sqrMagnitude > glows[largest].bounds.size.sqrMagnitude) largest = g;

            var verts = mesh.vertices;
            int subs = mesh.subMeshCount;
            var subTris = new int[subs][];
            for (int m = 0; m < subs; m++) subTris[m] = mesh.GetTriangles(m);

            // The flames, each one whole: triangles joined by shared vertex positions, then the pieces that overlap (a flame's
            // layers, one per material) merged. By single triangles, a flame's outer layer had gone to another glow.
            var key = new Dictionary<Vector3Int, int>();
            var parent = new List<int>();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[x] = y; }
            var vertexNode = new int[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                var k = Vector3Int.RoundToInt(verts[v] * 100f);
                if (!key.TryGetValue(k, out int node)) { node = parent.Count; parent.Add(node); key[k] = node; }
                vertexNode[v] = node;
            }
            for (int m = 0; m < subs; m++)
                for (int i = 0; i + 2 < subTris[m].Length; i += 3)
                {
                    Union(vertexNode[subTris[m][i]], vertexNode[subTris[m][i + 1]]);
                    Union(vertexNode[subTris[m][i]], vertexNode[subTris[m][i + 2]]);
                }
            var comp = new Dictionary<int, int>();   // root -> cluster
            var clusterBounds = new List<Bounds>();
            var clusterTris = new List<List<(int m, int i)>>();
            for (int m = 0; m < subs; m++)
                for (int i = 0; i + 2 < subTris[m].Length; i += 3)
                {
                    int root = Find(vertexNode[subTris[m][i]]);
                    if (!comp.TryGetValue(root, out int c))
                    {
                        c = clusterBounds.Count; comp[root] = c;
                        clusterBounds.Add(new Bounds(verts[subTris[m][i]], Vector3.zero));
                        clusterTris.Add(new List<(int, int)>());
                    }
                    var cb = clusterBounds[c];
                    cb.Encapsulate(verts[subTris[m][i]]); cb.Encapsulate(verts[subTris[m][i + 1]]); cb.Encapsulate(verts[subTris[m][i + 2]]);
                    clusterBounds[c] = cb;
                    clusterTris[c].Add((m, i));
                }
            for (bool merged = true; merged;)   // overlapping pieces are one flame
            {
                merged = false;
                for (int x = 0; x < clusterBounds.Count && !merged; x++)
                    for (int y = x + 1; y < clusterBounds.Count && !merged; y++)
                        if (clusterBounds[x].Intersects(clusterBounds[y]))
                        {
                            var bx = clusterBounds[x]; bx.Encapsulate(clusterBounds[y]); clusterBounds[x] = bx;
                            clusterTris[x].AddRange(clusterTris[y]);
                            clusterBounds.RemoveAt(y); clusterTris.RemoveAt(y);
                            merged = true;
                        }
            }

            // each flame to the nearest engine glow (part 2 gets part 4's, below; part 0 only what no engine glow is near)
            var perGlow = new List<int>[glows.Count][];
            var glowFlameBounds = new Bounds?[glows.Count];
            for (int g = 0; g < glows.Count; g++) { perGlow[g] = new List<int>[subs]; for (int m = 0; m < subs; m++) perGlow[g][m] = new List<int>(); }
            for (int c = 0; c < clusterBounds.Count; c++)
            {
                var centre = source.TransformPoint(clusterBounds[c].center);
                int best = -1; float bestD = float.MaxValue, bestSize = float.MaxValue;
                for (int g = 0; g < glows.Count; g++)
                {
                    if (g == largest || glows[g].part == FlameCopyTo80) continue;
                    float d = glows[g].bounds.SqrDistance(centre);
                    float size = glows[g].bounds.size.sqrMagnitude;
                    if (d < bestD - 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && size < bestSize)) { bestD = d; bestSize = size; best = g; }
                }
                if (best < 0 || bestD > FlameReach80 * FlameReach80) best = largest;
                foreach (var (m, i) in clusterTris[c]) { perGlow[best][m].Add(subTris[m][i]); perGlow[best][m].Add(subTris[m][i + 1]); perGlow[best][m].Add(subTris[m][i + 2]); }
                if (glowFlameBounds[best] is Bounds fb) { fb.Encapsulate(clusterBounds[c]); glowFlameBounds[best] = fb; } else glowFlameBounds[best] = clusterBounds[c];
            }

            // Part 2's engine has no flame in the mesh: part 4's flame, moved to where part 7's (part 2's twin across the
            // station) would be mirrored, then FlameCopyPos80 / Rot80 / Scale80 in part 2's frame.
            int from = glows.FindIndex(x => x.part == FlameCopyFrom80), twin = glows.FindIndex(x => x.part == FlameCopyTwin80);
            Vector3 copyShift = Vector3.zero;
            if (from >= 0 && glowFlameBounds[from] is Bounds fromB)
            {
                if (twin >= 0 && glowFlameBounds[twin] is Bounds twinB) { var t = twinB.center; t.x = -t.x; copyShift = t - fromB.center; }
                else Debug.LogWarning("[80] flames: part " + FlameCopyTwin80 + " has no flame to place part 2's by");
            }

            for (int g = 0; g < glows.Count; g++)
            {
                bool copied = glows[g].part == FlameCopyTo80;
                var lists = copied ? (from >= 0 ? perGlow[from] : null) : perGlow[g];
                if (lists == null) continue;
                int count = 0; foreach (var l in lists) count += l.Count / 3;
                if (count == 0) continue;
                var pieceMesh = BuildFlamePiece80(mesh, lists, copied ? copyShift : Vector3.zero);
                var go = new GameObject("Flame (" + glows[g].r.name + ")");
                // where the flames are (the source's pose), then onto its glow: 0, 0, 0 there when both sit in the station's frame
                go.transform.SetParent(source.parent, false);
                go.transform.localPosition = source.localPosition;
                go.transform.localRotation = source.localRotation;
                go.transform.localScale = source.localScale;
                go.transform.SetParent(glows[g].r.transform, true);
                if (copied && (FlameCopyPos80 != Vector3.zero || FlameCopyRot80 != Vector3.zero || FlameCopyScale80 != Vector3.one))
                {
                    go.transform.localPosition = FlameCopyPos80;
                    go.transform.localEulerAngles = FlameCopyRot80;
                    go.transform.localScale = FlameCopyScale80;
                }
                go.AddComponent<MeshFilter>().sharedMesh = pieceMesh;
                var pr = go.AddComponent<MeshRenderer>();
                pr.sharedMaterials = sr.sharedMaterials;   // every material of the flames (one per triangle list)
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pr.receiveShadows = false;
                flamePieces80.Add((pr, glows[g].r));
            }
            sr.enabled = false;   // the pieces stand in for it (its PartAnimation still scrolls the texture, copied below)
            flameSource80 = sr;
            flameBlock80 = new MaterialPropertyBlock();
            glowBlock80 = new MaterialPropertyBlock();
        }
        /// <summary>A mesh of the given triangles (per material) of 'mesh', every vertex channel kept, moved by 'shift'.</summary>
        static Mesh BuildFlamePiece80(Mesh mesh, List<int>[] lists, Vector3 shift)
        {
            var verts = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents; var colors = mesh.colors;
            var uv = new List<Vector2>[4];
            for (int c = 0; c < 4; c++) { uv[c] = new List<Vector2>(); mesh.GetUVs(c, uv[c]); }
            var map = new Dictionary<int, int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pt = new List<Vector4>(); var pc = new List<Color>();
            var pu = new List<Vector2>[4]; for (int c = 0; c < 4; c++) pu[c] = new List<Vector2>();
            var subTris = new List<int[]>();
            foreach (var list in lists)
            {
                var t = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    int v = list[i];
                    if (!map.TryGetValue(v, out int nv))
                    {
                        nv = pv.Count; map[v] = nv;
                        pv.Add(verts[v] + shift);
                        if (normals.Length == verts.Length) pn.Add(normals[v]);
                        if (tangents.Length == verts.Length) pt.Add(tangents[v]);
                        if (colors.Length == verts.Length) pc.Add(colors[v]);
                        for (int c = 0; c < 4; c++) if (uv[c].Count == verts.Length) pu[c].Add(uv[c][v]);
                    }
                    t[i] = nv;
                }
                subTris.Add(t);
            }
            var piece = new Mesh { name = mesh.name + " (flame)" };
            if (pv.Count > 65535) piece.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            piece.SetVertices(pv);
            if (pn.Count == pv.Count) piece.SetNormals(pn);
            if (pt.Count == pv.Count) piece.SetTangents(pt);
            if (pc.Count == pv.Count) piece.SetColors(pc);
            for (int c = 0; c < 4; c++) if (pu[c].Count == pv.Count && pv.Count > 0) piece.SetUVs(c, pu[c]);
            piece.subMeshCount = subTris.Count;
            for (int m = 0; m < subTris.Count; m++) piece.SetTriangles(subTris[m], m);
            piece.RecalculateBounds();
            return piece;
        }
        void UpdateFlamePieces80()
        {
            if (flameSource80 == null || flamePieces80.Count == 0) return;
            flameSource80.GetPropertyBlock(flameBlock80);
            foreach (var (piece, glow) in flamePieces80)
            {
                if (piece == null) continue;
                piece.SetPropertyBlock(flameBlock80);
                piece.enabled = GlowShown80(glow);
            }
        }
        /// <summary>The glow is drawn: active, enabled, and its `extra` fade (PartAnimation's _Fade / _Color) not at 0.</summary>
        bool GlowShown80(Renderer glow)
        {
            if (glow == null || !glow.enabled || !glow.gameObject.activeInHierarchy) return false;
            glow.GetPropertyBlock(glowBlock80);
            if (glowBlock80.HasFloat("_Fade") && glowBlock80.GetFloat("_Fade") < 0.01f) return false;
            if (glowBlock80.HasColor("_Color"))
            {
                var c = glowBlock80.GetColor("_Color");
                if (c.a < 0.01f || c.maxColorComponent < 0.01f) return false;
            }
            return true;
        }
        Transform FindStationPart80(string name)
        {
            if (level.Station == null) return null;
            foreach (var t in level.Station.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(name)) return t;
            return null;
        }
        static void PlaceBurn80(Transform burn, Vector3 localPos, Vector3 localEuler)
        {
            if (burn == null) return;
            bool found = false;
            foreach (var ps in burn.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform == burn || ps.transform.parent != burn) continue;   // the fire itself, right under the point
                ps.transform.localPosition = localPos;
                ps.transform.localEulerAngles = localEuler;
                found = true;
            }
            if (!found) Debug.LogWarning($"[80] {burn.name}: no fire found under it, its position was not changed");
        }
        // Modified: the battlestation's turrets (StationTurrets index -> position relative to the battlestation, Unity units,
        // and rotation, degrees), measured in 80 from the Inspector with the battlestation at (0, 0, -8000), unrotated
        // (OrbitLayout.RotationToUnity(0)). The shield generators keep the table's pose. Read through StationTurretPose
        // (80 and PlayerHull's Valkyrie hull).
        /// <summary>Modified: the deep science station's thruster flames (its *_emitters_anim_add meshes: a texture scroll over
        /// 11 s) keep looping. Holding the whole station until the hit froze them, and its PlayOnce would stop them after
        /// one scroll.</summary>
        static void LoopEmitters80(GameObject station)
        {
            foreach (var a in station.GetComponentsInChildren<PartAnimation>(true))
            {
                if (a.gameObject.name.IndexOf("_emitters", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                a.loop = true;
                a.play = true;
            }
        }
        /// <summary>Modified: 80's first shot pulls straight back along Unity Z, from where it starts by CamPullBack80 over the
        /// whole shot (LaserDelay80 + LaserWindow80), X and Y unchanged, still looking at the battlestation.</summary>
        void PullBack80(float shotMs)
        {
            cam.SetDolly(Vector3.zero);
            if (cam.Camera == null) return;
            float k = Mathf.Clamp01(shotMs / (LaserDelay80 + LaserWindow80));
            var p = cam.Camera.position;
            cam.Camera.position = new Vector3(p.x, p.y, Mathf.Lerp(camStartZ80, camEndZ80, k));   // OrbitScale: camEndZ80 (see step 0)
        }
    }
}
