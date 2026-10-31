// SystemApproach.cs
// Remake: a system's orbit built ahead of the ship as it flies in from between the systems (InterstellarField), so that the
// swap to the real orbit changes nothing on the screen. The orbit's station and gate stand on a root that maps the orbit onto
// this space, the point of the arrival (OrbitLayout.ArrivalPose, as far out as the speed carries it) onto the system's edge
// and the arrival's facing onto the way to the system: they come into view where and when they will be there. Its planets and
// sun (a Backdrop of its own, turned the same way) fade in; the sun starts as the star ahead and draws off the nose to its place
// in the orbit, and the scene's lights turn and change to the orbit's. The dear parts are built a frame apart, and the arrival's
// tables are read here too (SpaceLevel.PrepareArrival), each a short step in the flight in place of a long one at the swap.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public sealed class SystemApproach
    {
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
        readonly Quaternion facing;
        readonly Lights left, reached;
        readonly int field = Random.Range(1, int.MaxValue);
        int step;

        /// <summary>The sun's share of the blend: the star ahead's is what is left of it.</summary>
        public static float SunShown(float blend) => Mathf.Clamp01(blend / SunBy);

        public SystemApproach(SpaceLevel level, OrbitLayout orbit, Vector3 arrivalAt, Quaternion facing, Transform parent)
        {
            this.level = level;
            this.orbit = orbit;
            this.facing = facing;
            var drive = InterstellarSpace.Drive(level.Database);
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
            if (step == 1) AddObject(OrbitBuilder.SpawnStation(level.Database, orbit, root));
            else if (step == 2) AddObject(OrbitBuilder.SpawnJumpgate(level.Database, orbit, root));
            else if (step == 3 && !NetSession.Active) AddObject(OrbitBuilder.SpawnAsteroids(level.Database, orbit, field, root).gameObject);
            else if (step == 4) SpaceLevel.PrepareArrival(field);
        }

        /// <summary>Only to look at: this space flies through it.</summary>
        static void AddObject(GameObject go)
        {
            if (go == null) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var t in go.GetComponentsInChildren<Target>(true)) Object.DestroyImmediate(t);
        }

        /// <summary>A frame's place: the ship at 'at', the system's edge ahead along 'way' (to its middle), 'turn' this space's
        /// directions to the orbit's.</summary>
        public void Update(Vector3 at, Vector3 way, Quaternion turn, float blend)
        {
            if (step < 4)
            {
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                BuildStep();
            }
            float ahead = Mathf.Max(0f, way.magnitude - InterstellarSpace.ArriveUnits * OrbitLayout.MetersPerUnit);
            var here = carriedFrom - facing * Vector3.forward * ahead;
            var back = Quaternion.Inverse(turn);
            root.SetPositionAndRotation(at - back * here, back);

            backdrop.Turn(back, way.normalized, blend);
            backdrop.Fade(blend, SunShown(blend));
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
        }
    }
}
