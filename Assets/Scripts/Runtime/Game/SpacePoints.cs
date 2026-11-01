// SpacePoints.cs
// The docking points of the story's static objects (FileRead::loadSpacePoints(n), KIPlayer::setSpacePoints; data
// Resources/GoF2Data/docks_hd.json, whose "station" field is the point set n, not a station): per point a type (1 = the
// approach point KIPlayer::getNearestNavigationPoint picks, 2 = the docking point getNearestDockingPoint picks near it,
// 3 / 4 = unused by the player's docking), an offset in the object's engine space and a direction: FileRead::
// loadSpacePoints 0x146a30 turns the record's angles (degrees) into MatrixSetRotation(-r0, -r2, r1) (Rx*Ry*Rz) and keeps
// MatrixGetDir (column 2): (-sin r2, sin r0 cos r2, cos r0 cos r2); a docked ship's nose points along it.
// Set 10: 102's damaged Tadram station.
// Sets (Level::createStaticObject 0xcda54): 1 mining plant, 2 containers (container_003, the secure containers), 3 the
// damaged Midorian freighter, 4 cargo_001_midorian, 5 the Terran carrier, 6 the burning station, 7 the battlestation,
// 8 the Vossk battleship, 11-14 the Supernova wrecks with hidden blueprints (Level::createMission, table DAT_002537a4).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SpacePoints
    {
        public const int Approach = 1, Dock = 2;

        /// <summary>SpacePoint::take / giveFree: the approach points in use, per object (its transform, the point's index in
        /// its set). The player's docking (ObjectDocking) and the story's shuttles (SupernovaLevels) share them, so two ships
        /// never dock at one port (the evacuations: a shuttle docked onto the player's port).</summary>
        static readonly HashSet<(Transform, int)> taken = new HashSet<(Transform, int)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTaken() => taken.Clear();

        public static bool IsTaken(Transform obj, int point) => obj != null && taken.Contains((obj, point));
        public static void Take(Transform obj, int point) { if (obj != null) taken.Add((obj, point)); }
        public static void Free(Transform obj, int point) { if (obj != null) taken.Remove((obj, point)); }

        [Serializable] class PointJson { public int id; public float[] position_engine; public float[] rotationDeg_file; }
        [Serializable] class SetJson { public int station; public PointJson[] points; }
        [Serializable] class FileJson { public SetJson[] sets; }

        public struct Point { public int type; public Vector3 engine; public Vector3 dir; }   // dir: engine object space

        /// <summary>FileRead::loadSpacePoints: MatrixSetRotation(-r0, -r2, r1), MatrixGetDir = column 2.</summary>
        static Vector3 EngineDir(float[] r)
        {
            if (r == null || r.Length < 3) return Vector3.forward;
            float a = -r[0] * Mathf.Deg2Rad, b = -r[2] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(b), -Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a) * Mathf.Cos(b));
        }

        /// <summary>An engine-space offset in the object's Unity space: (-x, y, z) * 0.05 (the models are mirrored and turned).</summary>
        public static Vector3 ToLocal(Vector3 engine) => new Vector3(-engine.x, engine.y, engine.z) * OrbitLayoutScale;
        /// <summary>An engine-space direction in the object's Unity space.</summary>
        public static Vector3 DirToLocal(Vector3 engine) => new Vector3(-engine.x, engine.y, engine.z);
        const float OrbitLayoutScale = 0.05f;

        static Dictionary<int, List<Point>> sets;

        /// <summary>Remake mods: sets from here on are an NPC ship type's own docking pads (npcships.json "docking.pads",
        /// Modding.ModNpcShips.Pads: set NpcPadBase + the type's index), in the same engine frame as the file's.</summary>
        public const int NpcPadBase = 100000;

        /// <summary>The points of set n (empty when unknown). The first set of a number wins (the file lists 6 twice).</summary>
        public static List<Point> Set(int n)
        {
            if (n >= NpcPadBase) return Modding.ModNpcShips.Pads(n - NpcPadBase);
            if (sets == null) Load();
            return sets.TryGetValue(n, out var list) ? list : new List<Point>();
        }

        static void Load()
        {
            sets = new Dictionary<int, List<Point>>();
            var text = Resources.Load<TextAsset>("GoF2Data/docks_hd");
            if (text == null) return;
            var file = JsonUtility.FromJson<FileJson>("{\"sets\":" + text.text + "}");
            if (file?.sets == null) return;
            foreach (var s in file.sets)
            {
                if (s?.points == null || sets.ContainsKey(s.station)) continue;
                var list = new List<Point>();
                foreach (var p in s.points)
                    if (p?.position_engine != null && p.position_engine.Length == 3)
                        list.Add(new Point { type = p.id, engine = new Vector3(p.position_engine[0], p.position_engine[1], p.position_engine[2]),
                                             dir = EngineDir(p.rotationDeg_file) });
                sets[s.station] = list;
            }
        }
    }
}
