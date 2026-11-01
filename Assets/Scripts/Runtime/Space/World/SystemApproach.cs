// SystemApproach.cs
// Remake: a system's orbit built ahead of the ship as it flies in from between the systems (InterstellarField), so that the
// swap to the real orbit changes nothing on the screen. The orbit's station and gate stand on a root that maps the orbit onto
// this space, the point of the arrival (OrbitLayout.ArrivalPose, as far out as the speed carries it) onto the system's edge
// and the arrival's facing onto the way to the system: they come into view where and when they will be there. Its planets and
// sun (a Backdrop of its own, turned the same way) fade in, the planets specks that grow on through the arrival
// (InterstellarSpace.ApproachSize, Visuals.WarpSky); the sun starts as the star ahead and draws off the nose to its place in the
// orbit, and the scene's lights turn and change to the orbit's. All of it starts small and grows as the ship closes in, each
// thing where it stands (ArrivalScale), its normal traffic too, whose models the arrival's Traffic takes over (Carried,
// NpcShip.Setup); the real ones grow on from there to their own size as the warp ends (Visuals.WarpSky). The dear parts are built a frame apart, and the arrival's
// tables are read here too (SpaceLevel.PrepareArrival), each a short step in the flight in place of a long one at the swap.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class SystemApproach
    {
        /// <summary>The arrival's whole traffic plan, its normal traffic with stand-ins, handed to its Traffic with the models as they
        /// stand: the same plan, as ships in it name each other (an escort its capital ship).</summary>
        static List<SpawnSpec> arrivalShips;
        static Transform shipRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { arrivalShips = null; shipRoot = null; }

        /// <summary>The arrival takes the traffic: the orbit's own coordinates are the world's from here on. Null: none.</summary>
        public static List<SpawnSpec> TakeShips()
        {
            var ships = arrivalShips;
            arrivalShips = null;
            if (shipRoot != null) shipRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return ships;
        }

        /// <summary>The stand-ins' root, emptied by NpcShip.Setup; with what is left on it when the traffic has no use for them.</summary>
        public static void DropShips()
        {
            if (shipRoot != null) Object.Destroy(shipRoot.gameObject);
            shipRoot = null;
            arrivalShips = null;
        }

        /// <summary>How soon the sun takes over from the star ahead (of the blend).</summary>
        const float SunBy = 0.25f;

        struct Lights
        {
            public Quaternion sun, planet;
            public Color sunColor, planetColor;
            public float sunIntensity, planetIntensity;
            public Vector4 ambient, gamma, planetGamma, planetLinear, planetWay;
        }

        readonly SpaceLevel level;
        readonly OrbitLayout orbit;
        readonly Transform root;
        readonly Backdrop backdrop;
        readonly Vector3 carriedFrom;   // where the arrival starts
        readonly float startSize;
        readonly Quaternion facing;
        readonly Lights left, reached;
        readonly int field = Random.Range(1, int.MaxValue);
        readonly ArrivalScale objects = new ArrivalScale();
        int step, nextShip;

        /// <summary>The sun's share of the blend: the star ahead's is what is left of it.</summary>
        public static float SunShown(float blend) => Mathf.Clamp01(blend / SunBy);

        public SystemApproach(SpaceLevel level, OrbitLayout orbit, Vector3 arrivalAt, Quaternion facing, Transform parent, float startSize = InterstellarSpace.PlanetFarSize)
        {
            this.startSize = startSize;
            this.level = level;
            this.orbit = orbit;
            this.facing = facing;
            var drive = Shop.MountedCruiseDrive(level.Database);
            carriedFrom = arrivalAt - facing * Vector3.forward * (drive != null ? InterstellarSpace.CarryMeters(drive.Attr(CruiseDrive.SpeedAttr), WarpSky.ArrivalCarryMs) : 0f);
            root = new GameObject("Approach").transform;
            root.SetParent(parent, false);
            backdrop = OrbitBuilder.SpawnBackdrop(orbit, Camera.main, parent);
            backdrop.Fade(0f, 0f);
            left = Read();
            level.LightFor(orbit);
            reached = Read();
            level.LightFor(level.Layout);
        }

        Lights Read() => new Lights
        {
            sun = level.sunLight.transform.rotation, planet = level.planetLight.transform.rotation,
            sunColor = level.sunLight.color, planetColor = level.planetLight.color,
            sunIntensity = level.sunLight.intensity, planetIntensity = level.planetLight.intensity,
            ambient = Shader.GetGlobalVector("_GoF2SunAmbient"), gamma = Shader.GetGlobalVector("_GoF2SunGamma"),
            planetGamma = Shader.GetGlobalVector("_GoF2PlanetColor"), planetLinear = Shader.GetGlobalVector("_GoF2PlanetLinear"),
            planetWay = Shader.GetGlobalVector("_GoF2PlanetLight"),
        };

        /// <summary>The next dear part, one a frame. Their objects are placed by the orbit's coordinates, with 'root' at rest.</summary>
        void BuildStep()
        {
            step++;
            if (step == 1) objects.Add(AddObject(OrbitBuilder.SpawnStation(level.Database, orbit, root)));
            else if (step == 2) objects.Add(AddObject(OrbitBuilder.SpawnJumpgate(level.Database, orbit, root)));
            else if (step == 3 && !NetSession.Active) objects.AddChildren(AddObject(OrbitBuilder.SpawnAsteroids(level.Database, orbit, field, root).gameObject));
            else if (step == 4) SpaceLevel.PrepareArrival(field);
            else if (step == 5 && !NetSession.Active) PlanShips();
        }

        void PlanShips()
        {
            var system = level.Database.Systems.Find(s => s.index == orbit.systemIndex);
            var from = carriedFrom / OrbitLayout.MetersPerUnit;
            arrivalShips = TrafficPlan.Build(level.Database, orbit.stationIndex, system, new Vector3(from.x, from.y, -from.z));
            shipRoot = new GameObject("Arrival ships").transform;
            Object.DontDestroyOnLoad(shipRoot.gameObject);
        }

        /// <summary>The next ship ahead, one a frame: its model where the orbit's traffic will start it.</summary>
        void BuildShip()
        {
            if (arrivalShips == null || nextShip >= arrivalShips.Count) return;
            var spec = arrivalShips[nextShip++];
            if (!Traffic.Carried(spec)) return;
            var prefab = AssembledObject.LoadPrefab(Traffic.AssemblyOf(level.Database, spec));
            if (prefab == null) return;
            var at = new GameObject("Ship ahead").transform;
            at.SetParent(shipRoot, false);
            at.SetLocalPositionAndRotation(OrbitLayout.ToUnity(spec.position), OrbitLayout.RotationToUnity(spec.fixedObject != null ? spec.rotation : Vector3.zero));
            at.localScale = Vector3.one * spec.scale;   // on the pivot: the ship applies it to the model itself
            spec.standIn = Object.Instantiate(prefab, at, false);
            Sleep(spec);
            objects.Add(at);
        }

        /// <summary>A stand-in is a model and nothing more: whatever runs on it, a mod's most of all, is off until the ship takes it
        /// over. It stands on a root that sweeps through the world at the warp's speed and jumps at the swap, which a thing that
        /// goes by world positions takes for a flight: EVERSPACE 2's engine trails (Es2EngineVfx: a ribbon dropped every 10 m flown,
        /// seen only nose first) drew kilometres of it, on the runs whose course lay along the ship's heading.</summary>
        static void Sleep(SpawnSpec spec)
        {
            spec.standInAsleep = new List<Behaviour>();
            foreach (var b in spec.standIn.GetComponentsInChildren<MonoBehaviour>(true))
                if (b.enabled) { b.enabled = false; spec.standInAsleep.Add(b); }
            foreach (var t in spec.standIn.GetComponentsInChildren<TrailRenderer>(true)) t.emitting = false;
            foreach (var p in spec.standIn.GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>The ship has its model: all of it runs from here, in the orbit's own coordinates, with nothing from the flight in.</summary>
        public static void Wake(SpawnSpec spec)
        {
            foreach (var t in spec.standIn.GetComponentsInChildren<TrailRenderer>(true)) { t.Clear(); t.emitting = true; }
            foreach (var p in spec.standIn.GetComponentsInChildren<ParticleSystem>(true)) if (p.main.playOnAwake) p.Play(false);
            if (spec.standInAsleep != null) foreach (var b in spec.standInAsleep) if (b != null) b.enabled = true;
            spec.standInAsleep = null;
        }

        /// <summary>Only to look at: this space flies through it.</summary>
        static Transform AddObject(GameObject go)
        {
            if (go == null) return null;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var t in go.GetComponentsInChildren<Target>(true)) Object.DestroyImmediate(t);
            return go.transform;
        }

        /// <summary>A frame's place: the ship at 'at', the system's edge ahead along 'way' (to its middle), 'turn' this space's
        /// directions to the orbit's. The sun comes from 'sunFrom' (else the star ahead), the orbit's planets and sun 'shown'
        /// (else as the star ahead gives way).</summary>
        public void Update(Vector3 at, Vector3 way, Quaternion turn, float blend, Vector3? sunFrom = null, float? shown = null)
        {
            if (step < 5)
            {
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                BuildStep();
            }
            else BuildShip();
            float ahead = Mathf.Max(0f, way.magnitude - InterstellarSpace.ArriveUnits * OrbitLayout.MetersPerUnit);
            var here = carriedFrom - facing * Vector3.forward * ahead;
            var back = Quaternion.Inverse(turn);
            root.SetPositionAndRotation(at - back * here, back);
            float size = InterstellarSpace.ApproachSize(way.magnitude / OrbitLayout.MetersPerUnit, startSize);
            objects.Apply(size);
            if (shipRoot != null) shipRoot.SetPositionAndRotation(root.position, root.rotation);

            backdrop.Turn(back, sunFrom ?? way.normalized, blend);
            backdrop.Fade(shown ?? SunShown(blend), shown ?? SunShown(blend));
            backdrop.planetSize = backdrop.sunScaleFactor = size;
            backdrop.swell = blend;
            backdrop.orbitZ = here.z;

            level.sunLight.transform.rotation = Quaternion.Slerp(left.sun, back * reached.sun, blend);
            level.planetLight.transform.rotation = Quaternion.Slerp(left.planet, back * reached.planet, blend);
            level.sunLight.color = Color.Lerp(left.sunColor, reached.sunColor, blend);
            level.planetLight.color = Color.Lerp(left.planetColor, reached.planetColor, blend);
            level.sunLight.intensity = Mathf.Lerp(left.sunIntensity, reached.sunIntensity, blend);
            level.planetLight.intensity = Mathf.Lerp(left.planetIntensity, reached.planetIntensity, blend);
            Shader.SetGlobalVector("_GoF2SunAmbient", Vector4.Lerp(left.ambient, reached.ambient, blend));
            Shader.SetGlobalVector("_GoF2SunGamma", Vector4.Lerp(left.gamma, reached.gamma, blend));
            Shader.SetGlobalVector("_GoF2PlanetColor", Vector4.Lerp(left.planetGamma, reached.planetGamma, blend));
            Shader.SetGlobalVector("_GoF2PlanetLinear", Vector4.Lerp(left.planetLinear, reached.planetLinear, blend));
            Shader.SetGlobalVector("_GoF2PlanetLight", Vector3.Slerp(left.planetWay, back * reached.planetWay, blend));
        }

        /// <summary>The ship turned away: back as it was.</summary>
        public void Remove()
        {
            if (root != null) Object.Destroy(root.gameObject);
            if (backdrop != null) Object.Destroy(backdrop.gameObject);
            if (level != null) level.LightFor(level.Layout);
            if (!Session.WarpArrival) DropShips();
        }
    }
}
