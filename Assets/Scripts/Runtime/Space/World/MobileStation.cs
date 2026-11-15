// MobileStation.cs
// Remake mods: a mobile station (stations.json "mobile", Modding.MobileStations) in the flight level: its model cruising
// along its course (game time: fast-forward speeds it up like everything else), its collision turning with it (the hull cut
// into slices like a mod capital ship's, Obstacle.rotates; the player slides along the real hull, HullCollision), a lockable
// landmark the autopilot flies to (Navigation: a Station target that follows it), and docking within its range (its bounding
// radius + 6000 units, like a mod station model's) into its own hangar and bar (SpaceLevel.Dock). A launch from it comes out
// beside it, flying along its course (SpaceLevel.SpawnPlayer). Its "engines" glow at its stern like a mod ship's.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class MobileStation : MonoBehaviour
    {
        const float M = OrbitLayout.MetersPerUnit;
        const float CruiseDrive = 0.8f;   // the engine effect's thrust while it cruises (EngineDrive: 1 = full throttle)

        /// <summary>The station number (stations.json's mobile station).</summary>
        public int Station { get; private set; }
        public string Label { get; private set; }
        public int TechLevel { get; private set; }
        /// <summary>Its dock range in game units (bounding radius + 6000).</summary>
        public float DockRangeUnits { get; private set; } = 16000f;
        /// <summary>The model's half extents in its own frame (metres).</summary>
        public Vector3 HalfSize { get; private set; }
        public Obstacle Obstacle { get; private set; }
        /// <summary>The player has been outside its dock range since the level began (a launch starts inside it).</summary>
        public bool LeftRange;

        Vector3 velocity;   // metres per second, world

        /// <summary>The mobile stations in this orbit now, built under 'parent' (null when none).</summary>
        public static List<MobileStation> SpawnAll(Database db, OrbitLayout layout, Transform parent = null)
        {
            var list = new List<MobileStation>();
            if (layout == null || layout.alienOrbit || layout.systemIndex < 0) return list;
            double now = Modding.MobileStations.Clock;
            foreach (int mobile in Modding.MobileStations.AtOrbit(layout.stationIndex, now))
            {
                var ms = Spawn(db, mobile, now, parent);
                if (ms != null) list.Add(ms);
            }
            return list;
        }

        static MobileStation Spawn(Database db, int mobile, double seconds, Transform parent)
        {
            var st = db.Stations.Find(s => s.index == mobile);
            var own = Modding.ModWorld.ModelOf(mobile);
            string assembly = own != null && Modding.ModStations.Built(own.station) ? own.Assembly
                            : OrbitBuilder.StationAssembly(db, OrbitLayout.Build(db, mobile));
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(assembly));
            if (prefab == null) { Debug.LogWarning($"MobileStation: no model for {mobile}"); return null; }
            var root = new GameObject($"Mobile station {st?.name}");
            root.transform.SetParent(parent, false);
            var model = Instantiate(prefab, root.transform, false);
            model.name = "Station";
            OrbitScale.Structure(model.transform);   // OrbitScale: as big as the orbit's stations (its bounds below measure it so)
            foreach (var pa in model.GetComponentsInChildren<PartAnimation>(true)) pa.applyMaterialChannels = true;
            var ms = root.AddComponent<MobileStation>();
            ms.Station = mobile;
            ms.Label = st != null ? st.name : "Station";   // a ship: no "Station" after its name
            ms.TechLevel = st?.techLevel ?? 0;

            // Collision measured in its own frame (identity), turned with it from there.
            var volumes = OrbitScale.StructureVolumes(Modding.ModCapitalShips.Measure(model, Quaternion.identity));   // (measured unscaled)
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            ms.HalfSize = bounds.extents;
            float radius = 0f;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) != 0 ? bounds.max.x : bounds.min.x, (c & 2) != 0 ? bounds.max.y : bounds.min.y, (c & 4) != 0 ? bounds.max.z : bounds.min.z);
                radius = Mathf.Max(radius, corner.magnitude);
            }
            var o = root.AddComponent<Obstacle>();
            o.landmark = true;
            o.volumes = volumes ?? new List<CollisionVolume> { CollisionVolume.Box(bounds.center, bounds.extents) };
            o.cubeHalf = radius + 5000f * M;
            o.rotates = true;
            o.baseRotation = Quaternion.identity;
            ms.Obstacle = o;
            ms.DockRangeUnits = radius / M + 6000f;
            HullCollision.Attach(model, null, o);
            // Its exhausts ("mobile" "engines": mounts like a ship's, from its centre), lit while it cruises; the ship
            // glow's discs and rings (no collision: HullCollision skips the glow layers).
            var spec = Modding.ModWorld.MobileOf(mobile);
            // With an "engineFx" Galaxy on Fire 3's engine effect instead of the discs (EngineFx, each engine sized by its
            // fxScale and shaped by its outline), run at a cruise's thrust (EnginePreview): measured, its 0.15 u/ms would read as idle.
            if (spec?.engines != null && spec.engines.Count > 0)
            {
                var data = new CustomShipData { mounts = spec.engines, engineGlowColor = spec.engineColor, assembly = $"mobile_{mobile:000}" };
                var glow = Modding.ModShipBuilder.BuildEngineGlow(data, spec.engineFx == null) ?? new GameObject("engine_glow");
                glow.transform.SetParent(model.transform, false);
                if (spec.engineFx != null)
                {
                    root.AddComponent<EnginePreview>().drive = CruiseDrive;
                    Modding.EngineFx.CreateShaped(model, glow, spec.engineFx, spec.engines, spec.mod, data.assembly);
                }
            }
            ms.Place(seconds);
            return ms;
        }

        double seconds;

        void Place(double at)
        {
            seconds = at;
            Modding.MobileStations.CourseAt(Station, at, out var pos, out var heading);
            var forward = OrbitLayout.ToUnity(heading).normalized;
            transform.SetPositionAndRotation(OrbitScale.ToUnity(pos), Quaternion.LookRotation(forward, Vector3.up));   // (OrbitScale: in the scaled orbit)
            velocity = forward * Modding.MobileStations.SpeedUnitsPerSecond(Station) * M;
        }

        void Update()
        {
            // Along its course at its speed (game time), on past the stay's end while the level lasts.
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            seconds += dt;
            transform.position += velocity * dt;
        }

        /// <summary>The player at 'world' is inside its dock range.</summary>
        public bool InRange(Vector3 world) => (world - transform.position).sqrMagnitude < DockRangeUnits * M * DockRangeUnits * M;

        /// <summary>Where a launch from it comes out: beside it (its right), clear of its hull, facing along its course.</summary>
        public Pose LaunchPose()
        {
            var side = transform.right * (HalfSize.x + 3000f * M);
            return new Pose(transform.position + side + Vector3.up * (HalfSize.y * 0.25f), transform.rotation);
        }
    }
}
