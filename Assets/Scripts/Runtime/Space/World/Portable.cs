// Portable.cs
// Remake (2026-11-06, for mods): a thing the player's ship carries to a socket (World.EnergySocket), after EVERSPACE 2's
// portable objects (BP_Portable_Component: the action key grabs it within reach, it hangs behind the ship in a magnetic
// field, the action key lets it go): an energy sphere from a dispenser (BP_Portable_EnergyOrb, gone after the dispenser's
// EnergyOrbLifetime 40 s unless socketed) or an energy coil lying in the orbit (BP_Portable_EnergyCoil, stays). Grabbing and
// letting go: World.CarryPuzzle. Let go it keeps its momentum (PortableComponent::Detach bKeepMomentum); thrown (Detach bFling:
// bAllowFlinging, FlingStrength 20 000 cm/s, BP_Portable_EnergyCoil's and the common value) it flies on ahead of the ship at
// FlingMs more, slowing with the rift mines' damping (UE linear damping 0.2). A portable conductor (BP_Conductor_Portable,
// World.ConductorNet) is carried the same way; loose, it slows with its NormalDamping 0.1, or ConductingDamping 3 while it
// carries energy (so it stops where it links). An acid pouch (BP_AcidSack, World.AcidSackBase) thrown bursts on the first
// NPC ship it comes within BurstM of (its ImpactEvent: BP_Explosion_Corrosion and a corrosion field), or after BurstAfterS. Drawn as the rift's spheres (RiftHazards.Sphere): a white-blue glowing sphere, an orange coil.
// Rotation (2026-11-07, UPortableComponent::Attach / Detach, decompiled): ES2 holds a carried thing on a physics constraint
// whose linear drive pulls its centre of mass to the hold point, so the pull never turns it. With bLockObjectRotation the
// constraint's three angular axes are locked (ACM_Locked): it keeps the turn it had against the ship when grabbed and turns
// with the ship (BP_Portable_EnergyCoil and its kin set it); without (the default: the sphere, the conductors) they are free
// and it keeps whatever spin it had. Let go with its momentum (bKeepMomentum, flung too) it keeps its spin, so a coil dropped
// mid-turn tumbles on at the ship's turn rate, slowing with its AngularDamping 0.1 (BP_Portable_EnergyCoil, BP_Conductor_Portable).
// A socket keeps the turn it arrives with (the snap only moves it: no bSameOrientationAsActor on these).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Portable : MonoBehaviour
    {
        /// <summary>BP_EnergyOrb_Dispenser's EnergyOrbLifetime (s); how far behind the ship it hangs (m); its size (m).</summary>
        public const float OrbLifeS = 40f, CarryBehindM = 30f, RadiusM = 4f, FlingMs = 200f * 3.6f, Damping = 0.2f;
        static readonly Color OrbColor = new Color(1.6f, 2.2f, 3f, 0.9f), CoilColor = new Color(3f, 1.4f, 0.3f, 0.9f), ConductorColor = new Color(0.4f, 0.8f, 1.6f, 0.8f);
        public enum Kind { Orb, Coil, Conductor, AcidSack }
        /// <summary>A thrown acid pouch bursts within this of a ship (m), or after this long (s).</summary>
        public const float BurstM = 30f, BurstAfterS = 8f;
        static readonly Color AcidColor = new Color(0.9f, 2.2f, 0.3f, 0.9f);
        /// <summary>BP_Conductor_Portable's NormalDamping and ConductingDamping.</summary>
        public const float ConductorDamping = 0.1f, ConductingDamping = 3f;
        /// <summary>BP_Portable_EnergyCoil's and BP_Conductor_Portable's BodyInstance AngularDamping (per second).</summary>
        public const float AngularDamping = 0.1f;

        public static readonly List<Portable> All = new List<Portable>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public Kind Type { get; private set; }
        public bool Coil => Type == Kind.Coil;
        /// <summary>A conductor carrying energy (World.ConductorNet sets it).</summary>
        public bool Conducting { get; set; }
        /// <summary>An acid pouch thrown (it bursts on a ship); the level it bursts in.</summary>
        public bool Thrown { get; private set; }
        public SpaceLevel Level { get; set; }
        float thrownS;
        /// <summary>The ship carrying it, null = loose.</summary>
        public Transform Carrier { get; private set; }
        /// <summary>In a socket for good.</summary>
        public bool Socketed { get; private set; }
        public OrbDispenser From { get; private set; }
        float lifeS = -1f;
        Target marker;
        /// <summary>Its motion (m/s): followed while carried, its own while loose.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>Its spin (world axis x rad/s): turned with the ship while a locked one is carried, its own otherwise.</summary>
        public Vector3 Spin { get; private set; }
        /// <summary>bLockObjectRotation: carried, it turns with the ship (BP_Portable_EnergyCoil sets it; the sphere, the
        /// conductors and the acid pouch keep the default, free).</summary>
        public bool LockRotation => Type == Kind.Coil;
        Quaternion heldTurn = Quaternion.identity;   // its turn against the ship's, taken when grabbed (the locked constraint's)

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void OnDestroy() { if (marker != null) Target.RadarObjects.Remove(marker); }

        public static Portable Make(Transform parent, Vector3 at, bool coil, OrbDispenser from = null) => Make(parent, at, coil ? Kind.Coil : Kind.Orb, from);

        public static Portable Make(Transform parent, Vector3 at, Kind kind, OrbDispenser from = null)
        {
            bool coil = kind == Kind.Coil;
            var go = RiftHazards.Sphere(parent, at, coil ? RadiusM * 0.8f : RadiusM,
                                        kind == Kind.Coil ? CoilColor : kind == Kind.Conductor ? ConductorColor : kind == Kind.AcidSack ? AcidColor : OrbColor,
                                        kind == Kind.Coil ? "Energy coil" : kind == Kind.Conductor ? "Conductor" : kind == Kind.AcidSack ? "Acid pouch" : "Energy sphere");
            if (coil) go.transform.localScale = new Vector3(RadiusM * 1.2f, RadiusM * 2.4f, RadiusM * 1.2f);
            var p = go.AddComponent<Portable>();
            p.Type = kind;
            p.From = from;
            if (kind == Kind.Orb) p.lifeS = OrbLifeS;
            var t = p.marker = go.AddComponent<Target>();
            t.hp = t.maxHp = 1f;
            t.invulnerable = true;
            t.radius = RadiusM;
            t.displayName = kind == Kind.Coil ? Localization.Extra("energyCoil", "Energy coil")
                          : kind == Kind.Conductor ? Localization.Extra("conductor", "Conductor")
                          : kind == Kind.AcidSack ? Localization.Extra("acidPouch", "Acid pouch") : Localization.Extra("energySphere", "Energy sphere");
            t.plateNameOnly = t.plateNoIcon = true;
            Target.RadarObjects.Add(t);
            return p;
        }

        AudioSource holdLoop;

        public void Grab(Transform ship)
        {
            if (Socketed || Carrier == ship) return;
            Carrier = ship;
            heldTurn = HeldTurn(ship.rotation, transform.rotation);
            PuzzleSounds.PlayAt("grab", transform.position);   // (S_SC_Grab_Hold, then its loop while carried)
            if (holdLoop == null) holdLoop = PuzzleSounds.Loop("hold", gameObject);
            else holdLoop.Play();
        }

        void StopHold() { if (holdLoop != null) holdLoop.Stop(); }

        /// <summary>Let go with its momentum; 'fling' throws it on along the ship's nose at FlingMs more.</summary>
        public void Release(bool fling = false)
        {
            if (Carrier == null) return;
            Velocity = Fling(Velocity, Carrier.forward, fling);
            Carrier = null;
            if (fling && Type == Kind.AcidSack) { this.Thrown = true; thrownS = 0f; }
            StopHold();
            PuzzleSounds.PlayAt(fling ? "throw" : "drop", transform.position);   // (S_SC_Grab_Throw / _Leggo)
        }

        /// <summary>Its velocity once let go: kept, plus FlingMs along 'forward' when flung.</summary>
        public static Vector3 Fling(Vector3 velocity, Vector3 forward, bool fling) => fling ? velocity + forward.normalized * FlingMs : velocity;

        /// <summary>Into 'socket' for good: it sits there, off the radar.</summary>
        public void Seat(Transform socket)
        {
            Carrier = null;
            StopHold();
            Socketed = true;
            Velocity = Vector3.zero;
            Spin = Vector3.zero;
            lifeS = -1f;
            transform.position = socket.position;
            if (marker != null) { Target.RadarObjects.Remove(marker); Destroy(marker); marker = null; }
        }

        static bool NearShip(Vector3 at)
        {
            foreach (var t in Target.All) if (ShipCorrosion.Corrodes(t, at, BurstM)) return true;
            return false;
        }

        /// <summary>A thrown acid pouch bursts: BP_Explosion_Corrosion and its corrosion field.</summary>
        void Burst()
        {
            Explosion.Spawn(transform.position, 0.6f);
            AcidSackBase.Corrosion(Level, transform.parent, transform.position);
            Destroy(gameObject);
        }

        /// <summary>A locked one's turn against the ship's, taken when grabbed.</summary>
        public static Quaternion HeldTurn(Quaternion ship, Quaternion own) => Quaternion.Inverse(ship) * own;

        /// <summary>'rotation' turned on by 'spin' (world axis x rad/s) for 'dt' seconds.</summary>
        public static Quaternion Turned(Quaternion rotation, Vector3 spin, float dt)
        {
            float rate = spin.magnitude;
            if (rate * dt < 1e-7f) return rotation;
            return Quaternion.AngleAxis(rate * dt * Mathf.Rad2Deg, spin / rate) * rotation;
        }

        /// <summary>The spin that turns 'from' into 'to' in 'dt' seconds (world axis x rad/s).</summary>
        public static Vector3 SpinBetween(Quaternion from, Quaternion to, float dt)
        {
            if (dt <= 0f) return Vector3.zero;
            (to * Quaternion.Inverse(from)).ToAngleAxis(out float deg, out Vector3 axis);
            if (deg > 180f) deg -= 360f;
            if (Mathf.Abs(deg) < 1e-5f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return Vector3.zero;
            return axis.normalized * (deg * Mathf.Deg2Rad / dt);
        }

        /// <summary>A loose spin after 'dt' seconds of AngularDamping (UE's: x 1 / (1 + c dt) a step, near exp(-c dt)).</summary>
        public static Vector3 Damped(Vector3 spin, float dt) => spin * Mathf.Exp(-AngularDamping * dt);

        /// <summary>Where a carried one hangs: CarryBehindM behind the ship and a little below.</summary>
        public static Vector3 CarryPoint(Vector3 shipPos, Vector3 forward, Vector3 up) => shipPos - forward * CarryBehindM - up * (CarryBehindM * 0.2f);

        void Tumble(float dt)
        {
            if (Spin.sqrMagnitude < 1e-8f) { Spin = Vector3.zero; return; }
            transform.rotation = Turned(transform.rotation, Spin, dt);
            Spin = Damped(Spin, dt);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (lifeS >= 0f && (lifeS -= dt) <= 0f)
            {
                Explosion.Spawn(transform.position, 0.3f, false);   // (the sphere fizzles out)
                Destroy(gameObject);
                return;
            }
            if (Socketed || dt <= 0f) return;
            if (Carrier == null)
            {
                if (Thrown && ((thrownS += dt) >= BurstAfterS || NearShip(transform.position))) { Burst(); return; }
                transform.position += Velocity * dt;
                float damping = Type != Kind.Conductor ? Damping : Conducting ? ConductingDamping : ConductorDamping;
                Velocity *= Mathf.Exp(-damping * dt);
                Tumble(dt);
                return;
            }
            var was = transform.position;
            var to = CarryPoint(Carrier.position, Carrier.forward, Carrier.up);
            transform.position = Vector3.Lerp(was, to, 1f - Mathf.Exp(-4f * dt));   // (the magnetic field's pull)
            Velocity = (transform.position - was) / dt;
            if (LockRotation)
            {
                // The locked constraint: it turns with the ship, and its spin is the ship's (kept when let go).
                var wasTurn = transform.rotation;
                transform.rotation = Carrier.rotation * heldTurn;
                Spin = SpinBetween(wasTurn, transform.rotation, dt);
            }
            else Tumble(dt);   // free axes: the pull is at its centre of mass, so it spins on as it did
        }
    }
}
