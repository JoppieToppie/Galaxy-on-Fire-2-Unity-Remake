// TrafficLanes.cs
// Remake (2026-11-03, for mods): the orbit's traffic lanes (stations.json "orbit.lanes", Modding.ModWorld's LaneSpec), after
// EVERSPACE 2's BP_SplineTraffic: ships streaming along a path (Flight.TrafficLane), spread evenly over it, at the lane's
// speed with their engines lit; with two lanes every other one runs the other way, each keeping to its right. Not the
// traffic's ships: no AI, no marker or lock, no radar dot. A hit can destroy one (its hit points: ES2's 50 against the
// remake's fighters' hulls, HitPoints): an explosion, and it is back at its place after RespawnMs; destroying one is a crime
// against the system's race (ES2's TryTriggerWantedSystem(1): Standing.ApplyDelict 1). Their looks: the lane's "ships"
// (ship references), else the system race's fighters as the traffic picks them.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class TrafficLanes : MonoBehaviour
    {
        const float M = 0.05f;
        public const float HitPoints = 30f;

        class Unit
        {
            public GameObject go;
            public Target target;
            public float start, sign, deadMs;
        }

        class Lane
        {
            public TrafficLane path;
            public float speed;
            public bool twoLanes;
            public readonly List<Unit> units = new List<Unit>();
        }

        readonly List<Lane> lanes = new List<Lane>();
        float timeS;
        int systemRace;

        public static TrafficLanes Attach(SpaceLevel level, Database db)
        {
            var spec = level != null && level.Layout != null && !level.Layout.interstellar && !level.Layout.alienOrbit
                ? Modding.ModWorld.OrbitOf(level.Layout.stationIndex) : null;
            if (spec?.lanes == null || spec.lanes.Count == 0 || db == null) return null;
            var t = new GameObject("Traffic lanes").AddComponent<TrafficLanes>();
            t.transform.SetParent(level.transform, false);
            t.systemRace = db.Systems.Find(s => s.index == level.Layout.systemIndex)?.raceId ?? 0;
            foreach (var l in spec.lanes) t.Build(db, l);
            return t;
        }

        void Build(Database db, Modding.ModWorld.LaneSpec spec)
        {
            if (spec.points == null || spec.points.Count < 2) return;
            var pts = new List<Vector3>();
            foreach (var p in spec.points) pts.Add(OrbitLayout.ToUnity(p));
            var lane = new Lane { path = new TrafficLane(pts, spec.loop), speed = spec.speed, twoLanes = spec.twoLanes };
            if (lane.path.Length < 1f) return;
            for (int i = 0; i < spec.count; i++)
            {
                var u = new Unit { start = lane.path.Length * i / spec.count, sign = spec.twoLanes && i % 2 == 1 ? -1f : 1f };
                u.go = Model(db, PickShip(spec), u);
                if (u.go == null) continue;
                u.go.transform.SetParent(transform, true);
                lane.units.Add(u);
            }
            lanes.Add(lane);
        }

        int PickShip(Modding.ModWorld.LaneSpec spec)
        {
            if (spec.ships != null && spec.ships.Count > 0)
                for (int tries = 0; tries < 8; tries++)
                    if (Modding.ModContent.TryResolveShip(spec.ships[Random.Range(0, spec.ships.Count)], out int ship) && Database.Shared.Ship(ship) != null) return ship;
            return NpcTables.RandomFighter(systemRace);
        }

        GameObject Model(Database db, int ship, Unit u)
        {
            var prefab = AssembledObject.LoadPrefab(Traffic.AssemblyOf(db, new SpawnSpec { ship = ship, race = systemRace }));
            if (prefab == null) return null;
            var go = Instantiate(prefab);
            go.name = "Lane ship";
            var asm = go.GetComponent<AssembledObject>();
            if (asm != null) asm.SetExhaust(true, false);
            var t = u.target = go.AddComponent<Target>();
            t.hp = t.maxHp = HitPoints;
            t.radius = 300f * M;
            t.race = systemRace;
            t.customDeath = true;   // (its own: hidden, then back)
            t.Died += dead => Destroyed(u);
            return go;
        }

        void Destroyed(Unit u)
        {
            var pos = u.go.transform.position;
            Explosion.Spawn(pos, 0.6f);
            if (!u.target.killedByNpc) Standing.ApplyDelict(systemRace, 1);
            u.deadMs = TrafficLane.RespawnMs;
            u.go.SetActive(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            timeS += dt;
            foreach (var lane in lanes)
                foreach (var u in lane.units)
                {
                    if (u.deadMs > 0f)
                    {
                        u.deadMs -= dt * 1000f;
                        if (u.deadMs > 0f) continue;
                        u.target.Revive();
                        u.target.hp = u.target.maxHp = HitPoints;
                        u.go.SetActive(true);
                    }
                    float d = lane.path.Position(u.start, u.sign * lane.speed * timeS, out bool backwards);
                    lane.path.Sample(d, out var pos, out var fwd);
                    if (u.sign < 0f != backwards) fwd = -fwd;
                    var right = Vector3.Cross(Vector3.up, fwd);
                    if (lane.twoLanes && right.sqrMagnitude > 1e-6f) pos += right.normalized * TrafficLane.LaneOffsetM;
                    u.go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
                }
        }
    }
}
