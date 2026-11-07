// OrbDispenser.cs
// Remake (2026-11-06, for mods): an energy sphere dispenser (a secure container's orb socket's "orb" place, World.LootContainer),
// after EVERSPACE 2's BP_EnergyOrb_Dispenser ("Energy Sphere Dispenser"): the action key within reach hands the ship a new
// energy sphere (World.Portable, gone after 40 s unless socketed) while none of its own is about. Marked on the radar.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class OrbDispenser : MonoBehaviour
    {
        public const float RadiusM = 10f;
        public static readonly System.Collections.Generic.List<OrbDispenser> All = new System.Collections.Generic.List<OrbDispenser>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        Portable current;
        Target marker;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void OnDestroy() { if (marker != null) Target.RadarObjects.Remove(marker); }

        public static OrbDispenser Make(Transform parent, Vector3 at)
        {
            var go = RiftHazards.Sphere(parent, at, RadiusM, new Color(0.3f, 0.5f, 1f, 0.5f), "Energy sphere dispenser");
            var d = go.AddComponent<OrbDispenser>();
            var t = d.marker = go.AddComponent<Target>();
            t.hp = t.maxHp = 1f;
            t.invulnerable = true;
            t.radius = RadiusM;
            t.displayName = Localization.Extra("energySphereDispenser", "Energy sphere dispenser");
            t.plateNameOnly = t.plateNoIcon = true;
            Target.RadarObjects.Add(t);
            PuzzleSounds.Loop("dispenser", go);   // (S_SC_OrbDispenser_Loop)
            return d;
        }

        /// <summary>It can hand out a sphere: none of its own loose or carried.</summary>
        public bool Ready => current == null || current.Socketed;

        /// <summary>A new sphere, at once on 'ship'.</summary>
        public Portable Dispense(Transform ship)
        {
            if (!Ready) return null;
            current = Portable.Make(transform.parent, transform.position, false, this);
            current.Grab(ship);
            return current;
        }
    }
}
