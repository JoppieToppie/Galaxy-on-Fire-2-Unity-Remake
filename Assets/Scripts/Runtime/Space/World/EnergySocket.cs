// EnergySocket.cs
// Remake (2026-11-06, for mods): a secure container's energy socket (stations.json "orbit.containers.secure[].sockets",
// World.LootContainer), after EVERSPACE 2's BP_EnergyOrb_Socket / BP_EnergyCoil_Socket: it takes the first energy sphere or
// coil (its kind) that comes within ConnectionDistance (1000 cm x the ES2 ships' 3.6: ConnectM), carried or drifting, and
// keeps it; then its container checks whether it can open. Marked on the radar with its kind's name ("Energy sphere socket"
// / "Energy coil socket"), drawn dim until filled, then bright.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class EnergySocket : MonoBehaviour
    {
        public const float ConnectM = 10f * 3.6f, RadiusM = 6f;

        public bool Coil { get; private set; }
        public bool Filled { get; private set; }
        LootContainer container;
        Renderer body;
        Target marker;
        Color color;

        void OnDestroy() { if (marker != null) Target.RadarObjects.Remove(marker); }

        public static EnergySocket Make(Transform parent, Vector3 at, bool coil, LootContainer container)
        {
            var color = coil ? new Color(1f, 0.5f, 0.1f, 0.35f) : new Color(0.5f, 0.75f, 1f, 0.35f);
            var go = RiftHazards.Sphere(parent, at, RadiusM, color, coil ? "Energy coil socket" : "Energy sphere socket");
            var s = go.AddComponent<EnergySocket>();
            s.Coil = coil; s.container = container; s.color = color;
            s.body = go.GetComponent<Renderer>();
            var t = s.marker = go.AddComponent<Target>();
            t.hp = t.maxHp = 1f;
            t.invulnerable = true;
            t.radius = RadiusM;
            t.displayName = coil ? Localization.Extra("energyCoilSocket", "Energy coil socket") : Localization.Extra("energySphereSocket", "Energy sphere socket");
            t.plateNameOnly = t.plateNoIcon = true;
            Target.RadarObjects.Add(t);
            return s;
        }

        /// <summary>Whether a portable of 'coil' kind at 'distance' metres goes in.</summary>
        public static bool Takes(bool socketCoil, bool portableCoil, float distance) => socketCoil == portableCoil && distance <= ConnectM;

        void Update()
        {
            if (Filled) return;
            foreach (var p in Portable.All)
            {
                if (p == null || p.Socketed || p.Type == Portable.Kind.Conductor || p.Type == Portable.Kind.AcidSack || !Takes(Coil, p.Coil, (p.transform.position - transform.position).magnitude)) continue;
                p.Seat(transform);
                Filled = true;
                if (body != null) body.material.color = new Color(color.r * 3f, color.g * 3f, color.b * 3f, 0.8f);
                if (marker != null) { Target.RadarObjects.Remove(marker); Destroy(marker); marker = null; }
                container?.CheckUnlock();
                return;
            }
        }
    }
}
