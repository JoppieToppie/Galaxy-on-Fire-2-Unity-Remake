// StationLife.cs
// Remake (2026-11-04, for mods): a mod station's life (stations.json "life", Modding.ModWorld's LifeSpec), after EVERSPACE 2's
// worker drones (BP_SplineTraffic_WorkerDrones, decompiled): drones on loops round the station's structures (Flight.TrafficLane:
// spread evenly, every other one the other way on two lanes, each keeping laneOffset to its right), and every ScanIntervalS /
// the drones not welding, one of them at random, not welding and its cooldown over, looks for a weld spot within ReachM and
// ConeDeg of its heading (ES2 sphere-traces for one: the mod's "spots" are such traces made beforehand). It leaves the loop
// for the spot WeldOffsetM off the hull, facing it (ES2's PhysicsApproachTransform: a spring of Stiffness, at most
// ApproachMaxMs, turning at most TurnDegS), welds there WeldMinS..WeldMaxS with sparks (P_WorkerDrone_WeldSparks) and goes
// back to its place on the loop, which ran on meanwhile, with a cooldown of up to CooldownMaxS.
// All in the station model's own frame: the drones are children of its hull and turn, scale and centre with it (the model's
// glTF metres; glTFast's Unity frame mirrors x). Only looks: no target, no collision.

using System.Collections.Generic;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.World
{
    public class StationLife : MonoBehaviour
    {
        /// <summary>ES2's BP_SplineTraffic_WorkerDrones: WeldScanIntervalPerDrone, MaxWeldPointDistance, the trace's cone,
        /// WeldPointPositionOffset, the weld's RandomFloatInRange(10, 20), CooldownDurationMax, PhysicsApproachTransform's
        /// MaxLinearVelocity 8000 cm/s, FollowStiffness and MaxAngularVelocity (model metres, seconds).</summary>
        public const float ScanIntervalS = 2f, ReachM = 100f, ConeDeg = 90f, WeldOffsetM = 5f, WeldMinS = 10f, WeldMaxS = 20f,
                           CooldownMaxS = 20f, ApproachMaxMs = 80f, Stiffness = 2f, TurnDegS = 120f, ReachedM = 1f, RejoinM = 3f;
        const int SparkCell = 61;

        public enum State { Loop, Going, Welding, Back }

        public class Drone
        {
            public Transform transform;
            public float start, sign, cooldownS, weldS;
            public State state;
            public Vector3 target, spotAt, spotNormal;
            public Quaternion targetRotation;
        }

        class Loop
        {
            public TrafficLane path;
            public ModWorld.LifeLoop spec;
            public readonly List<Drone> drones = new List<Drone>();
            public readonly List<(Vector3 at, Vector3 normal)> spots = new List<(Vector3, Vector3)>();
            public float scanS;
        }

        readonly List<Loop> loops = new List<Loop>();
        readonly List<(Vector3, Vector3)> near = new List<(Vector3, Vector3)>();
        ParticleSystem sparks;
        float timeS;

        /// <summary>Every drone, for the tests.</summary>
        public IEnumerable<Drone> Drones { get { foreach (var l in loops) foreach (var d in l.drones) yield return d; } }

        /// <summary>The model's glTF frame into its Unity one (glTFast mirrors x).</summary>
        public static Vector3 FromGltf(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        public static StationLife Attach(SpaceLevel level)
        {
            if (level == null || level.Station == null || level.Layout == null || level.Layout.interstellar || level.Layout.alienOrbit) return null;
            int station = level.Layout.stationIndex;
            var life = ModWorld.ModelOf(station)?.life;
            var unit = ModStations.LifeUnit(station);
            var hull = level.Station.transform.Find("hull");
            if (life == null || unit == null || hull == null) return null;
            var go = new GameObject("Station life");
            go.transform.SetParent(hull, false);
            var s = go.AddComponent<StationLife>();
            foreach (var spec in life.loops) s.Build(spec, unit);
            s.MakeSparks();
            return s;
        }

        void Build(ModWorld.LifeLoop spec, GameObject unit)
        {
            var pts = new List<Vector3>();
            foreach (var p in spec.points) pts.Add(FromGltf(p));
            var loop = new Loop { path = new TrafficLane(pts, spec.loop), spec = spec, scanS = Random.Range(0f, ScanIntervalS / spec.count) };
            if (loop.path.Length < 0.1f) return;
            foreach (var (at, n) in spec.spots) loop.spots.Add((FromGltf(at), FromGltf(n)));
            for (int i = 0; i < spec.count; i++)
            {
                var go = Instantiate(unit, transform, false);
                go.name = "Worker drone";
                // ES2's drones start with a random cooldown (the construction's RandomFloat x CooldownDurationMax)
                var d = new Drone { transform = go.transform, start = loop.path.Length * i / spec.count, sign = spec.twoLanes && i % 2 == 1 ? -1f : 1f,
                                    cooldownS = Random.value * CooldownMaxS };
                loop.drones.Add(d);
                Place(loop, d, out var pos, out var rot);
                d.transform.localPosition = pos; d.transform.localRotation = rot;
            }
            loops.Add(loop);
        }

        /// <summary>A drone's place and heading on its loop now (the model's Unity frame).</summary>
        void Place(Loop loop, Drone d, out Vector3 pos, out Quaternion rot)
        {
            float along = loop.path.Position(d.start, d.sign * loop.spec.speed * timeS, out bool backwards);
            loop.path.Sample(along, out pos, out var fwd);
            if (d.sign < 0f != backwards) fwd = -fwd;
            var right = Vector3.Cross(Vector3.up, fwd);
            if (loop.spec.twoLanes && right.sqrMagnitude > 1e-6f) pos += right.normalized * loop.spec.laneOffset;
            rot = Quaternion.LookRotation(fwd, Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
        }

        /// <summary>CheckForWeldPoint: 'd' off to a spot within reach ahead of it, if it is free to and there is one.</summary>
        public bool TryWeld(Drone d)
        {
            var loop = loops.Find(l => l.drones.Contains(d));
            if (loop == null || d.state != State.Loop || d.cooldownS > 0f) return false;
            var pos = d.transform.localPosition;
            var fwd = d.transform.localRotation * Vector3.forward;
            near.Clear();
            foreach (var s in loop.spots)
            {
                var to = s.at - pos;
                if (to.sqrMagnitude <= ReachM * ReachM && Vector3.Angle(fwd, to) <= ConeDeg) near.Add(s);
            }
            if (near.Count == 0) return false;
            var (at, normal) = near[Random.Range(0, near.Count)];
            d.spotAt = at; d.spotNormal = normal;
            d.target = at + normal * WeldOffsetM;
            d.targetRotation = Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.99f ? Vector3.forward : Vector3.up);
            d.state = State.Going;
            return true;
        }

        /// <summary>PhysicsApproachTransform: a spring towards 'to' (plus 'carry', the place's own speed), at most ApproachMaxMs;
        /// true when there.</summary>
        static bool Approach(Drone d, Vector3 to, Quaternion facing, Vector3 carry, float dt, float within)
        {
            var t = d.transform;
            var v = carry + (to - t.localPosition) * Stiffness;
            if (v.sqrMagnitude > ApproachMaxMs * ApproachMaxMs) v = v.normalized * ApproachMaxMs;
            t.localPosition += v * dt;
            t.localRotation = Quaternion.RotateTowards(t.localRotation, facing, TurnDegS * dt);
            return (to - t.localPosition).sqrMagnitude <= within * within && Quaternion.Angle(t.localRotation, facing) < 10f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            timeS += dt;
            foreach (var loop in loops)
            {
                // a scan every ScanIntervalS / the drones not welding (its timer re-set after each), one drone at random
                if ((loop.scanS -= dt) <= 0f)
                {
                    int busy = 0;
                    foreach (var d in loop.drones) if (d.state != State.Loop) busy++;
                    loop.scanS = ScanIntervalS / Mathf.Max(1, loop.drones.Count - busy);
                    if (loop.drones.Count > 0) TryWeld(loop.drones[Random.Range(0, loop.drones.Count)]);
                }
                foreach (var d in loop.drones)
                {
                    Place(loop, d, out var pos, out var rot);
                    switch (d.state)
                    {
                        case State.Loop:
                            d.transform.localPosition = pos;
                            d.transform.localRotation = rot;
                            d.cooldownS -= dt;
                            break;
                        case State.Going:
                            if (Approach(d, d.target, d.targetRotation, Vector3.zero, dt, ReachedM)) { d.state = State.Welding; d.weldS = Random.Range(WeldMinS, WeldMaxS); }
                            break;
                        case State.Welding:
                            if (Random.value < dt * 30f) Spark(d);
                            if ((d.weldS -= dt) <= 0f) { d.state = State.Back; d.cooldownS = Random.value * CooldownMaxS; }
                            break;
                        case State.Back:
                            var carry = rot * Vector3.forward * loop.spec.speed;
                            if (Approach(d, pos, rot, carry, dt, RejoinM)) d.state = State.Loop;
                            break;
                    }
                }
            }
        }

        void MakeSparks()
        {
            var assets = CombatAssets.Load();
            if (assets == null || assets.particlesMaterial == null) return;
            var go = new GameObject("Weld sparks");
            go.transform.SetParent(transform, false);
            var ps = sparks = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.startSpeed = 0f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var sh = ps.shape;
            sh.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            // additive: fading to black fades it out
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = ShipExhaust.CellQuad(SparkCell);
            r.alignment = ParticleSystemRenderSpace.View;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = assets.particlesMaterial;
            ps.Play();
        }

        /// <summary>A few sparks off the weld spot (in the world: the model's scale on them).</summary>
        void Spark(Drone d)
        {
            if (sparks == null) return;
            var parent = transform;
            var at = parent.TransformPoint(d.spotAt);
            var normal = parent.TransformDirection(d.spotNormal).normalized;
            float scale = parent.lossyScale.x;
            for (int i = 0; i < 5; i++)
            {
                var dir = (normal + Random.insideUnitSphere * 0.9f).normalized;
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = dir * Random.Range(4f, 12f) * scale,
                    startSize = Random.Range(0.6f, 1.6f) * scale,
                    startLifetime = Random.Range(0.2f, 0.5f),
                    startColor = Color.Lerp(new Color(1f, 0.55f, 0.1f), Color.white, Random.value * 0.6f),
                }, 1);
            }
        }
    }
}
