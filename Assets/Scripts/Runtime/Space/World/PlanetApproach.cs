// PlanetApproach.cs
// Remake: another station's orbit built ahead of a cruise to its planet (Navigation.RunPlanet), the way InterstellarField does
// for a system: its sky blends in, SystemApproach brings the rest, and this orbit's planets, sun, fog and flare give way to it.
// The arrival faces the orbit's own planet (OrbitLayout.PlanetArrivalPose), which starts at the size the planet has here, so
// the planet the ship flies at is the one that grows. This orbit's station, gate, field and ships draw off as it comes (ArrivalScale,
// in place: nothing of them is left to vanish at the swap). Turning back fades it all out again.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class PlanetApproach : MonoBehaviour
    {
        /// <summary>How fast the next orbit may come in (per ms).</summary>
        const float BlendPerMs = 1f / 1500f, M = 0.05f;

        SpaceLevel level;
        Navigation nav;
        Transform ship;
        Navigation.Target blended;
        float blend, startSize;
        OrbitLayout orbit;
        Vector3 arrivalAt, sunFrom;
        Quaternion arrivalFacing = Quaternion.identity;
        Matrix4x4 blendedSky = Matrix4x4.identity;
        Material sky;
        SystemApproach approach;
        ArrivalScale leaving;

        public void Setup(SpaceLevel level)
        {
            this.level = level;
            nav = level.Navigation;
            ship = level.Player.transform;
        }

        void LateUpdate()
        {
            if (ship == null) return;
            var want = nav.RunPlanet;
            float goal = 0f;
            if (want != null)
            {
                float units = (nav.RunGoal - ship.position).magnitude / M;
                goal = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(InterstellarSpace.ArriveUnits, InterstellarSpace.ApproachUnits, units));
            }
            if (want != blended)
            {
                if (blend > 0.001f) goal = 0f;   // the last one out first
                else
                {
                    Clear();
                    if (want != null) Load(want);
                }
            }
            blend = Mathf.MoveTowards(blend, goal, BlendPerMs * Time.deltaTime * 1000f);
            Shader.SetGlobalFloat("_SkyBlend", blended != null ? blend : 0f);
            if (blended == null) return;
            level.Backdrop.Fade(1f - blend, 1f - blend);
            level.Backdrop.flareShown = 1f - blend;
            level.Fog?.Fade(1f - blend);
            leaving?.Apply(1f - blend * blend);
            if (blend <= 0.001f && goal <= 0f) { approach?.Remove(); approach = null; return; }
            var way = nav.RunGoal - ship.position;
            if (way.sqrMagnitude < 1f) return;
            var turn = arrivalFacing * Quaternion.Inverse(Quaternion.LookRotation(way.normalized, Vector3.up));
            Shader.SetGlobalMatrix("_BlendRotation", blendedSky * Matrix4x4.Rotate(turn));
            if (approach == null && goal > 0f && want == blended) approach = new SystemApproach(level, orbit, arrivalAt, arrivalFacing, transform, startSize);
            approach?.Update(ship.position, way, turn, blend, sunFrom, blend);
        }

        void Load(Navigation.Target planet)
        {
            blended = planet;
            orbit = OrbitLayout.Build(level.Database, planet.station);
            orbit.PlanetArrivalPose(Session.StationIndex, out arrivalAt, out arrivalFacing);
            var own = orbit.planets.Find(p => p.isOrbitPlanet);
            startSize = own != null && own.scale > 0f
                ? Mathf.Clamp(level.Backdrop.PlanetScale(planet.transform) / own.scale, 0.01f, InterstellarSpace.PlanetEdgeSize)
                : InterstellarSpace.PlanetFarSize;
            sunFrom = level.Backdrop.SunDirection;
            leaving = new ArrivalScale();
            leaving.Add(level.Station != null ? level.Station.transform : null);
            leaving.Add(level.Jumpgate != null ? level.Jumpgate.transform : null);
            leaving.AddChildren(level.Asteroids);
            if (level.Traffic != null) foreach (var ship in level.Traffic.Ships) leaving.Add(ship.Model);
            if (sky != null) Destroy(sky);
            sky = OrbitBuilder.MakeSky(orbit);
            if (sky == null) return;
            blendedSky = sky.GetMatrix("_SkyRotation");
            InterstellarField.LoadBlendSky(sky);
        }

        /// <summary>This orbit as it was.</summary>
        void Clear()
        {
            if (blended == null) return;
            blended = null;
            approach?.Remove();
            approach = null;
            level.Backdrop.Fade(1f, 1f);
            level.Backdrop.flareShown = 1f;
            level.Fog?.Fade(1f);
            leaving?.Restore();
            leaving = null;
        }

        void OnDisable() => Shader.SetGlobalFloat("_SkyBlend", 0f);

        void OnDestroy()
        {
            approach?.Remove();
            if (sky != null) Destroy(sky);
        }
    }
}
