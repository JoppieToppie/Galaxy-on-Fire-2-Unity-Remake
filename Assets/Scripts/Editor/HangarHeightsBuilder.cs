// HangarHeightsBuilder.cs
// GoF2 > Build Hangar Heights: Resources/GoF2Data/hangar_heights.json, how far a parked ship must be lifted above the
// original's height (the slot's floor y + StationTables.ShipY, DAT_00253d48) so its hull doesn't cut into the pad, per
// hangar, slot (-1 = the player's turntable) and heading (24 bins of 15 deg). Nothing is ever lowered: the table height
// stays wherever the hull clears the pad (the Terran cradles are open funnels, the Vossk pads flat discs). With these
// meshes the wide hulls cut into the Midorian pads' raised rims (the H'Soc's wings 1.9 m), and Midorian slot 2 / deep
// science slot 2 are raised pedestals the table ignores.
// Each ship's hull bottom (its lowest vertex per 0.6 m column; no additive / LOD meshes) is dropped from DropFrom above
// its table height onto the room (MeshColliders on the room's meshes, not the additive light shafts; the alpha layers hold the Midorian rims,
// the animated parts posed where the game shows them); the first contact is its rest height. In a preview scene: the
// open scene is untouched.

using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.EditorTools
{
    public static class HangarHeightsBuilder
    {
        const string OutPath = "Assets/Resources/GoF2Data/hangar_heights.json";
        const float Cell = 0.6f;       // m, the hull-bottom grid
        const float DropFrom = 12f;    // m above the table pivot where the drop starts
        const float MinLift = 0.02f;   // m; less is left at the table height

        [MenuItem("GoF2/Build Hangar Heights", priority = 16)]
        public static void Build()
        {
            var db = Database.Load();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var physics = scene.GetPhysicsScene();
                var bottoms = new Dictionary<int, Vector3[]>();
                foreach (var ship in db.Ships)   // the original 64 and the custom ships (custom_ships.json)
                {
                    int i = ship.index;
                    if (i == 14) continue;   // the debug battleship (x2) never parks and doesn't fit the rooms
                    var pts = HullBottom(db, i, scene);
                    if (pts != null && pts.Length > 0) bottoms[i] = pts;
                }
                var file = new StationTables.HangarLiftFile { entries = new List<StationTables.HangarLift>() };
                var log = new System.Text.StringBuilder("Hangar lifts (m, largest heading):\n");
                for (int h = 0; h < StationTables.HangarRoom.Length; h++)
                {
                    if (StationTables.HangarRoom[h] == null) continue;
                    var room = Spawn(db, StationTables.HangarRoom[h], OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI, 0f)), scene);
                    if (room == null) continue;
                    foreach (var mf in room.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null || mf.name.Contains("_add")) continue;   // the "_alpha" layers are solid too (the Midorian pad rims)
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    }
                    Physics.SyncTransforms();
                    var slots = new List<Vector3> { Vector3.zero };
                    if (StationTables.ParkedSlots[h] != null) slots.AddRange(StationTables.ParkedSlots[h]);
                    for (int k = 0; k < slots.Count; k++)
                    {
                        var floor = OrbitLayout.ToUnity(slots[k]);
                        var line = new System.Text.StringBuilder();
                        foreach (var kv in bottoms)
                        {
                            float table = floor.y + StationTables.ShipY(kv.Key) * OrbitLayout.MetersPerUnit;
                            var lift = new float[StationTables.LiftBins];
                            bool any = false, wall = false;
                            for (int b = 0; b < lift.Length; b++)
                            {
                                float rest = Rest(physics, floor, table, kv.Value, 360f * b / lift.Length);
                                if (rest >= table + DropFrom - 0.5f) { wall = true; break; }   // under a wall or a roof: no pad
                                lift[b] = Mathf.Max(0f, rest - table);
                                if (lift[b] >= MinLift) any = true;
                            }
                            if (wall || !any) continue;
                            file.entries.Add(new StationTables.HangarLift { hangar = h, slot = k - 1, ship = kv.Key, lift = lift });
                            line.Append($" {kv.Key}:{Mathf.Max(lift):F1}");
                        }
                        log.AppendLine($"  hangar {h} slot {k - 1}:{line}");
                    }
                    Object.DestroyImmediate(room);
                }
                File.WriteAllText(OutPath, JsonUtility.ToJson(file));
                AssetDatabase.ImportAsset(OutPath);
                StationTables.ClearHangarLifts();
                Debug.Log(log.ToString());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static GameObject Spawn(Database db, string assembly, Quaternion rot, Scene scene)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(assembly));
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, Vector3.zero, rot);
            SceneManager.MoveGameObjectToScene(go, scene);
            // The animated parts where the game shows them: StationLevel plays every room animation from its loop start
            // (OneOffStartMs); Awake, which reads the keys, doesn't run in edit mode.
            var awake = typeof(PartAnimation).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var a in go.GetComponentsInChildren<PartAnimation>(true))
            {
                awake?.Invoke(a, null);
                a.Hold(a.OneOffStartMs);
            }
            return go;
        }

        /// <summary>The ship as the hangar spawns it (StationLevel.SpawnShip: NPC variant, exhaust off), at the origin: its
        /// lowest vertex per Cell x Cell column, relative to the pivot.</summary>
        static Vector3[] HullBottom(Database db, int ship, Scene scene)
        {
            var entry = db.ShipAssembly(ship);
            if (entry == null) return null;
            var go = Spawn(db, entry.name, Quaternion.identity, scene);
            if (go == null) return null;
            var asm = go.GetComponent<AssembledObject>();
            if (asm != null)
            {
                asm.SetPlayerVariant(false);
                if (asm.npcVariantParts != null) foreach (var p in asm.npcVariantParts) if (p != null) p.SetActive(false);
            }
            var lowest = new Dictionary<(int, int), Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.gameObject.activeInHierarchy || mf.name.Contains("_add") || mf.name.Contains("_lod")) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    var key = (Mathf.FloorToInt(w.x / Cell), Mathf.FloorToInt(w.z / Cell));
                    if (!lowest.TryGetValue(key, out var cur) || w.y < cur.y) lowest[key] = w;
                }
            }
            Object.DestroyImmediate(go);
            var list = new Vector3[lowest.Count];
            lowest.Values.CopyTo(list, 0);
            return list;
        }

        /// <summary>The pivot height at which the hull at Unity yaw 'yawDeg', dropped from DropFrom above its table height,
        /// first touches the room; minus infinity when nothing is under it.</summary>
        static float Rest(PhysicsScene physics, Vector3 floor, float table, Vector3[] bottom, float yawDeg)
        {
            float from = table + DropFrom, best = float.NegativeInfinity;
            var q = Quaternion.Euler(0f, yawDeg, 0f);
            foreach (var p in bottom)
            {
                var w = q * p;
                var origin = new Vector3(floor.x + w.x, from + w.y, floor.z + w.z);
                if (physics.Raycast(origin, Vector3.down, out var hit, DropFrom + 40f)) best = Mathf.Max(best, hit.point.y - w.y);
            }
            return best;
        }
    }
}
