// ConductorNet.cs
// Remake (2026-11-06, for mods): a secure container's conductor puzzle (stations.json "orbit.containers.secure[].conductors",
// World.LootContainer), after EVERSPACE 2's conductors (UConductorComponent; BP_Conductor_Source, _Static, _Portable,
// _Receiver): sources, fixed relays, portable conductors the ship carries and drops or throws into place (World.Portable kind
// Conductor, World.CarryPuzzle) and receivers. Each frame Flight.ConductorGraph works out what the sources reach; a reached
// receiver charges for ChargeTime (1.5 s) and stays energized while reached; all energized, the container checks whether it can
// open. Energy shows as beams from each energized node to the one feeding it (ES2's ElectricityParticleSystem: a line here)
// and as the nodes' glow. Sources and receivers are radar objects (ES2's HUD markers), and so are the loose conductors.
// Containers with the same receivers (one network unlocking several, as ES2's do) share one net (Shared: by the spec's places).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ConductorNet : MonoBehaviour
    {
        static readonly Dictionary<string, ConductorNet> Shared = new Dictionary<string, ConductorNet>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Shared.Clear();
        string key;
        static readonly Color BeamColor = new Color(0.6f, 1.6f, 3f, 1f), Dark = new Color(0.3f, 0.4f, 0.6f, 0.5f), Lit = new Color(0.8f, 1.8f, 3f, 0.9f);
        static Material beamLook;

        sealed class Node
        {
            public Transform t;
            public Portable portable;
            public float radiusM;
            public bool source, receiver;
            public float chargeS;
            public Renderer body;
            public LineRenderer beam;
            public AudioSource hum;
        }

        readonly List<Node> nodes = new List<Node>();
        readonly List<(float x, float y, float z)> at = new List<(float, float, float)>();
        readonly List<float> radius = new List<float>();
        readonly List<bool> sources = new List<bool>();
        readonly List<LootContainer> containers = new List<LootContainer>();
        readonly List<Target> markers = new List<Target>();

        /// <summary>Every receiver charged.</summary>
        public bool AllEnergized { get; private set; }

        /// <summary>The spec's identity: its places and spheres.</summary>
        public static string Key(Modding.ModWorld.ConductorSpec spec)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var list in new[] { spec.sources, spec.statics, spec.portables, spec.receivers })
            {
                foreach (var p in list) sb.Append(p.x).Append(',').Append(p.y).Append(',').Append(p.z).Append(',').Append(p.w).Append(';');
                sb.Append('|');
            }
            return sb.Append(spec.radius).Append('/').Append(spec.receiverRadius).ToString();
        }

        public static ConductorNet Make(Transform parent, Modding.ModWorld.ConductorSpec spec, LootContainer container)
        {
            string key = Key(spec);
            if (Shared.TryGetValue(key, out var existing) && existing != null) { existing.containers.Add(container); return existing; }
            var go = new GameObject("Conductors");
            go.transform.SetParent(parent, false);
            var net = go.AddComponent<ConductorNet>();
            net.key = key;
            Shared[key] = net;
            net.containers.Add(container);
            float R(Vector4 p, float fallback) => (p.w > 0f ? p.w : fallback) * OrbitLayout.MetersPerUnit;
            Vector3 At(Vector4 p) => OrbitLayout.ToUnity(new Vector3(p.x, p.y, p.z));
            foreach (var p in spec.sources) net.Fixed(At(p), R(p, spec.radius), true, false, 8f, Localization.Extra("conductorSource", "Conductor source"));
            foreach (var p in spec.statics) net.Fixed(At(p), R(p, spec.radius), false, false, 5f, null);
            foreach (var p in spec.receivers) net.Fixed(At(p), R(p, spec.receiverRadius), false, true, 6f, Localization.Extra("conductorReceiver", "Conductor receiver"));
            foreach (var p in spec.portables)
            {
                var portable = Portable.Make(parent, At(p), Portable.Kind.Conductor);
                net.nodes.Add(new Node { t = portable.transform, portable = portable, radiusM = R(p, spec.radius), body = portable.GetComponent<Renderer>() });
            }
            foreach (var n in net.nodes) n.beam = net.Beam(n.t);
            return net;
        }

        void OnDestroy()
        {
            foreach (var m in markers) if (m != null) Target.RadarObjects.Remove(m);
            if (key != null && Shared.TryGetValue(key, out var n) && n == this) Shared.Remove(key);
        }

        void Fixed(Vector3 pos, float radiusM, bool source, bool receiver, float sizeM, string marker)
        {
            var go = RiftHazards.Sphere(transform, pos, sizeM, source ? Lit : Dark, source ? "Conductor source" : receiver ? "Conductor receiver" : "Conductor");
            nodes.Add(new Node { t = go.transform, radiusM = radiusM, source = source, receiver = receiver, body = go.GetComponent<Renderer>() });
            if (marker == null) return;
            var t = go.AddComponent<Target>();
            t.hp = t.maxHp = 1f;
            t.invulnerable = true;
            t.radius = sizeM;
            t.displayName = marker;
            t.plateNameOnly = t.plateNoIcon = true;
            Target.RadarObjects.Add(t);
            markers.Add(t);
        }

        LineRenderer Beam(Transform owner)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 1.5f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (beamLook == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null) beamLook = new Material(shader) { name = "Conductor beam" };
            }
            if (beamLook != null) lr.sharedMaterial = beamLook;
            lr.startColor = lr.endColor = BeamColor;
            lr.enabled = false;
            return lr;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            nodes.RemoveAll(n => n.t == null);   // (a portable gone)
            at.Clear(); radius.Clear(); sources.Clear();
            foreach (var n in nodes) { var p = n.t.position; at.Add((p.x, p.y, p.z)); radius.Add(n.radiusM); sources.Add(n.source); }
            var on = ConductorGraph.Energized(at, radius, sources, out var feeder);
            bool all = true;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n.portable != null) n.portable.Conducting = on[i];
                if (n.receiver)
                {
                    n.chargeS = on[i] ? n.chargeS + dt : 0f;
                    if (n.chargeS < ConductorGraph.ChargeS) all = false;
                    // S_SC_3D_ConductorBeam while it's energized
                    if (on[i] && n.hum == null) n.hum = PuzzleSounds.Loop("conductor", n.t.gameObject);
                    else if (n.hum != null && on[i] != n.hum.isPlaying) { if (on[i]) n.hum.Play(); else n.hum.Stop(); }
                }
                if (n.body != null && !n.source && n.portable == null) n.body.material.color = on[i] && (!n.receiver || n.chargeS >= ConductorGraph.ChargeS) ? Lit : Dark;
                if (n.beam != null)
                {
                    bool show = on[i] && feeder[i] >= 0;
                    n.beam.enabled = show;
                    if (show) { n.beam.SetPosition(0, nodes[feeder[i]].t.position); n.beam.SetPosition(1, n.t.position); }
                }
            }
            if (all && !AllEnergized) { AllEnergized = true; foreach (var c in containers) if (c != null) c.CheckUnlock(); }
            else if (!all) AllEnergized = false;
        }
    }
}
