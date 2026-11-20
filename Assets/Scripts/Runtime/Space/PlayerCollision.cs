// PlayerCollision.cs
// PlayerEgo::calcCollision 0xab550 (Reference/research/ship_combat.md 2.9), on the player ship. Runs after the ship has
// moved each frame, like PlayerEgo::update, over the landmarks, then the NPC ships, then the asteroids:
//   station, jumpgate, freighters (Obstacle)   the ship is put on the surface of the volumes it is inside
//                                              (projectCollisionOnSurface) and keeps flying, so it slides along them;
//                                              camera hit() (shake 1000 ms, +-6 units); no damage
//   NPC fighters                               none: the player flies through them (they have no volumes)
//   asteroids                                  the asteroid is destroyed (damage 9999, normal death), the player takes
//                                              20 (shield -> armor -> hull), camera hit(); no push
//   the wormhole (Wormhole)                    visible and not shrinking, within 40000 units: its loop sound, the ship
//                                              pulled toward it by (40000 - d) / 256 units per original frame, camera
//                                              hit(); within 1000 units the ship is inside (PlayerEgo+0x25)
// Collision is off during the launch / arrival camera and the jump scenes (PlayerEgo+0x144, set by the level), while
// mining (PlayerEgo+0x356 with a mining phase 1-3) and once dead.
// The 1000 ms jitter of the ship model after a hit (PlayerEgo+0x328 / +0x32c, +-0.006 units) is too small to see and
// isn't reproduced.
// Remake: a phase cloak (PlayerCloak.Phasing, a mod item's attribute 104) lets the ship pass through the landmarks, the
// freighters, other players' ships and the asteroids while it is cloaked; the wormhole still pulls.
// Remake haptics: running into a landmark is a knock, then a scraping rumble while the ship slides along it; an asteroid a
// knock; the wormhole's pull a rumble that grows toward it.
// Remake (2026-11-02, Options > Gameplay "Collision damage", Settings.CollisionDamage, off by default): EVERSPACE 2's
// collision damage (decompiled: BP_CollisionDamageComponent's ComponentHit graph, on every ship; the player's
// CollisionDamageFactor attribute 1). Per hit: strength = |impulse| / mass / DamagePercentageDivisor 50 000, i.e. the speed
// lost into the surface in cm/s over 50 000; from MinDamagePercent 0.01 (5 m/s) and outside the GracePeriodDuration 0.5 s
// after a hit, damage = strength ^ 1.5 x the ship's maximum hit points x DamageMultiplier 1 x the factor (the whole hull at
// 500 m/s, a quarter at 200 m/s, 3 % at 50 m/s, 0.1 % at 5 m/s), dealt as a point hit (its Kinetic half and Energy third the remake's one
// number through the shield first). Here the speed into the surface is the ship's velocity against the push-out normal
// (a landmark's volume, a hull), or toward the asteroid's centre (which the original's rule destroys; with the option on
// the asteroid costs this instead of 20), and the maximum hit points are the hull's and the armour's.


using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [DefaultExecutionOrder(50)]   // after ShipController has moved the ship
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // 'overlaps' is a scratch buffer: nothing to reset
    public class PlayerCollision : MonoBehaviour
    {
        /// <summary>Set by the level each frame: launch / arrival camera, jump scenes (collision off).</summary>
        [NonSerialized] public bool off;
        /// <summary>Set by the level: the autopilot flies into the jumpgate's sphere to use it (SystemJump).</summary>
        [NonSerialized] public bool ignoreGate;
        /// <summary>This frame the ship touched the station (MGame::dockEvent: with the autopilot to it, that docks).</summary>
        public bool TouchingStation { get; private set; }
        /// <summary>The orbit's wormhole (landmark 3), null = none.</summary>
        [NonSerialized] public GoF2Remake.World.Wormhole wormhole;
        /// <summary>Set by the level: the wormhole neither pulls nor takes the player now (SpaceLevel.WormholeHeld).</summary>
        [NonSerialized] public Func<bool> wormholeHeld;
        /// <summary>Set by the level: the wormhole doesn't pull, but entering it still counts (SpaceLevel.WormholeNoPull).</summary>
        [NonSerialized] public Func<bool> wormholeNoPull;
        /// <summary>PlayerEgo::isInWormhole: pulled within 1000 units (and alive).</summary>
        public bool InWormhole { get; private set; }
        /// <summary>Remake events: an event's wormhole (SpaceLevel.OpenEventWormhole), null = none; it pulls like the story's.</summary>
        [NonSerialized] public GoF2Remake.World.Wormhole eventWormhole;
        /// <summary>Within 1000 units of the event's wormhole: SpaceLevel rides it to its destination.</summary>
        public bool InEventWormhole { get; private set; }

        PlayerHealth health;
        ChaseCamera chase;
        Mining mining;
        ShipController ship;
        // EVERSPACE 2's collision damage (Settings.CollisionDamage)
        public const float DamageDivisorMps = 500f, MinDamageStrength = 0.01f, DamageExponent = 1.5f, GraceSeconds = 0.5f;
        float graceLeft;
        bool scraping, scrapedLastFrame;   // haptics: touching a landmark this frame / the frame before
        const float ScrapeRumble = 0.35f;
        /// <summary>Docking at a story object: no collision while easing in, docked or leaving (set by the level).</summary>
        [System.NonSerialized] public ObjectDocking docking;
        /// <summary>The player's cloak (set by the level), for the phase cloak.</summary>
        [NonSerialized] public PlayerCloak cloak;

        public void Setup(PlayerHealth playerHealth, ChaseCamera chaseCamera, Mining miningSystem)
        {
            health = playerHealth;
            chase = chaseCamera;
            mining = miningSystem;
        }

        // Remake: where the ship was after last frame's collision, for the hull sweep (none after a skipped frame)
        Vector3 lastPosition;
        Quaternion lastRotation;
        bool hasLastPosition;
        const float MaxSweepMeters = 300f;   // a longer step is a teleport (a jump, a launch, an event's move), not flight

        /// <summary>EVERSPACE 2's collision damage for a hit at 'intoSpeedMps' (the speed lost into the surface, m/s) on a ship of
        /// 'maxHealth' hit points: strength = the speed over 500 m/s (50 000 cm/s), nothing below 0.01 (5 m/s), else
        /// strength ^ 1.5 x maxHealth.</summary>
        public static float CollisionDamage(float intoSpeedMps, float maxHealth)
        {
            float strength = Mathf.Max(0f, intoSpeedMps) / DamageDivisorMps;
            if (strength < MinDamageStrength) return 0f;
            return Mathf.Pow(strength, DamageExponent) * Mathf.Max(0f, maxHealth);
        }

        /// <summary>The ship's velocity (m/s, world): free flight's own, the original's along the nose.</summary>
        Vector3 Velocity()
        {
            if (ship == null) ship = GetComponent<ShipController>();
            if (ship == null) return Vector3.zero;
            return ship.Model.FreeFlight ? ship.Model.FreeVelocity * (ship.metersPerUnit * 1000f) : transform.forward * ship.SpeedMetersPerSecond;
        }

        /// <summary>Remake: EVERSPACE 2's collision damage for a hit whose surface pushed the ship along 'normal' (an asteroid:
        /// the way to it, negated), within the grace period none.</summary>
        void Ram(Vector3 normal)
        {
            if (!GoF2Remake.Data.Settings.CollisionDamage || graceLeft > 0f || health == null || health.Target == null || normal.sqrMagnitude < 1e-8f) return;
            float into = Mathf.Max(0f, -Vector3.Dot(Velocity(), normal.normalized));
            float maxHealth = health.Hp != null ? health.Hp.maxHull + health.Hp.maxArmor : health.Target.maxHp;
            float dmg = CollisionDamage(into, maxHealth);
            if (dmg <= 0f) return;
            graceLeft = GraceSeconds;
            if (!health.invulnerable) health.Target.Damage(dmg);
        }

        void Update()
        {
            TouchingStation = false;
            scraping = false;
            if (graceLeft > 0f) graceLeft -= Time.deltaTime;
            // Remake: the enemies' shots hit the collision probe too, not only the hull (Target.hitSphereRadius).
            if (health != null && health.Target != null) health.Target.hitSphereRadius = ProbeRadius();
            if (off || health == null || health.Dead) { wormhole?.SetSound(false); eventWormhole?.SetSound(false); scrapedLastFrame = false; hasLastPosition = false; return; }
            if (mining != null && mining.State != Mining.Phase.Idle) { scrapedLastFrame = false; hasLastPosition = false; return; }
            if (docking != null && docking.Busy && docking.State != ObjectDocking.Phase.Approach) { scrapedLastFrame = false; hasLastPosition = false; return; }   // easing onto a docking point
            CheckWormhole();
            if (cloak != null && cloak.Phasing) { scrapedLastFrame = false; hasLastPosition = false; return; }   // phased out: nothing solid
            SweepHulls();
            CheckObstacles(true);
            CheckHulls(true);
            CheckObstacles(false);
            CheckHulls(false);
            CheckAsteroids();
            if (scraping)
            {
                if (!scrapedLastFrame) Haptics.Play(Haptics.Impact);
                Haptics.Rumble(ScrapeRumble);
            }
            scrapedLastFrame = scraping;
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            hasLastPosition = true;
        }

        void Hit()
        {
            if (chase != null && chase.enabled) chase.Shake(1000f, 6f);   // TargetFollowCamera::hit
        }

        void CheckObstacles(bool landmarks)
        {
            var all = Obstacle.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || o.landmark != landmarks || !o.Active || o.UsesHull) continue;   // a hull: CheckHulls
                if (ignoreGate && o.cubeIsContact) continue;
                // Remake: the object being docked at doesn't block its own approach (the original's autopilot could pin the
                // ship on a hull face when it started beside the object; from afar it comes in over the top anyway).
                if (docking != null && docking.State == ObjectDocking.Phase.Approach && docking.Target != null
                    && (o.gameObject == docking.Target.gameObject
                        // ... nor the station a docking target stands in for (102: Tadram's pads)
                        || (o.isStation && (o.transform.position - docking.Target.transform.position).sqrMagnitude < 1f))) continue;
                var pos = transform.position;
                EnsureHullProbes();
                if (hullProbes.Count == 0)
                {
                    if (!o.Touches(pos, out _)) continue;
                    transform.position = o.PushOut(pos);
                    Ram(transform.position - pos);
                    if (o.isStation) TouchingStation = true;
                    scraping = true;
                    Hit();
                    continue;
                }
                bool touched = false;
                foreach (var p in hullProbes)
                {
                    var centre = transform.position + HullProbeWorldOffset(p.centre);
                    if (!o.TryPushOutSphere(centre, p.radius, out var pushed)) continue;
                    var correction = pushed - centre;
                    if (correction.sqrMagnitude < 1e-8f) { touched = true; continue; }
                    transform.position += correction;
                    Ram(correction);
                    touched = true;
                }
                if (touched)
                {
                    if (o.isStation) TouchingStation = true;
                    scraping = true;
                    Hit();
                }
            }
        }

        // Remake (HullCollision): the landmarks' / freighters' real shapes. The ship is a sphere (ProbeRadius) pushed out of
        // every hull it overlaps (Physics.ComputePenetration), so it slides along the hull as on the original's volumes,
        // flies through its gaps and doesn't stop short of it in a box's empty corner.
        static readonly Collider[] overlaps = new Collider[32];
        SphereCollider probe;
        readonly List<HullProbe> hullProbes = new List<HullProbe>();
        struct HullProbe { public Vector3 centre; public float radius; public HullProbe(Vector3 centre, float radius) { this.centre = centre; this.radius = radius; } }

        /// <summary>A hull the ship collides with: an obstacle's (Active: it has volumes too, so a mod station's "collision":
        /// "none" has no hull contact either), not the gate the autopilot flies into, not the object being docked at (nor the
        /// station a docking target stands in for).</summary>
        bool Solid(HullBody body)
        {
            var o = body != null ? body.obstacle : null;
            if (o == null || !o.Active || !body.Collides) return false;   // not taken by the ship collision mode: the volumes (CheckLandmarks)
            if (ignoreGate && o.cubeIsContact) return false;
            if (docking != null && docking.State == ObjectDocking.Phase.Approach && docking.Target != null
                && (o.gameObject == docking.Target.gameObject
                    || (o.isStation && (o.transform.position - docking.Target.transform.position).sqrMagnitude < 1f))) return false;
            return true;
        }

        /// <summary>Remake (after a contributor's precise-collision version, 2026-10-11): the probe sphere swept from last
        /// frame's place to this one; a hull it runs into on the way stops the ship short of it, so a fast ship (boost,
        /// fast-forward: up to ~25 m a frame) can't pass a thin part between two frames. The push below then slides it.</summary>
        void SweepHulls()
        {
            if (!HullCollision.Any || !hasLastPosition) return;
            var pos = transform.position;
            var step = pos - lastPosition;
            float len = step.magnitude;
            if (len < 0.01f || len > MaxSweepMeters) return;   // a longer step is a teleport, not flight
            EnsureHullProbes();
            float stop = float.MaxValue;
            if (hullProbes.Count == 0)
            {
                float r = ProbeRadius(), half = r * 0.5f;
                if (len >= half && HullCollision.Sweep(lastPosition, pos, half, Solid, out float hit)) stop = hit - half;
                if (HullCollision.Sweep(lastPosition, pos, 0.01f, Solid, out float centreHit)) stop = Mathf.Min(stop, centreHit - r);
            }
            else foreach (var p in hullProbes)
            {
                Vector3 offset = HullProbeWorldOffset(p.centre);
                float half = p.radius * 0.5f;
                var localOffset = HullProbeLocalOffset(p.centre);
                var from = lastPosition + lastRotation * localOffset;
                var to = pos + transform.rotation * localOffset;
                if (HullCollision.Sweep(from, to, half, Solid, out float hit)) stop = Mathf.Min(stop, hit - half);
                if (HullCollision.Sweep(from, to, 0.01f, Solid, out float centreHit)) stop = Mathf.Min(stop, centreHit - p.radius);
            }
            if (stop != float.MaxValue) transform.position = lastPosition + step / len * Mathf.Max(0f, stop);
        }

        void CheckHulls(bool landmarks)
        {
            if (!HullCollision.Any) return;
            var pos = transform.position;
            EnsureHullProbes();
            if (hullProbes.Count == 0)
            {
                ResolveHullProbe(ref pos, pos, ProbeRadius(), landmarks);
                return;
            }
            foreach (var p in hullProbes)
                ResolveHullProbe(ref pos, pos + HullProbeWorldOffset(p.centre), p.radius, landmarks);
        }

        void ResolveHullProbe(ref Vector3 shipPosition, Vector3 centre, float radius, bool landmarks)
        {
            int n = HullCollision.Overlap(centre, radius, overlaps);
            for (int k = 0; k < n; k++)
            {
                var c = overlaps[k];
                var body = HullCollision.BodyOf(c);
                if (!Solid(body) || body.obstacle.landmark != landmarks || !HullCollision.Shown(c)) continue;
                var o = body.obstacle;
                probe.radius = radius;
                probe.transform.position = centre;
                if (!Physics.ComputePenetration(probe, centre, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out float dist)) continue;
                shipPosition += dir * dist;
                transform.position = shipPosition;
                centre += dir * dist;
                Ram(dir);
                if (o.isStation) TouchingStation = true;
                scraping = true;
                Hit();
            }
        }

        void EnsureHullProbes()
        {
            var current = GetComponent<ShipController>();
            var model = current != null ? current.visualModel : null;
            if (model == probeModel) return;
            probeModel = model;
            hullProbes.Clear();
            if (model == null) return;
            var volumes = GoF2Remake.Modding.ModCapitalShips.Measure(model.gameObject, Quaternion.identity);
            if (volumes == null || volumes.Count == 0) return;
            var min = volumes[0].centre;
            var max = min;
            foreach (var volume in volumes)
            {
                if (volume == null || volume.sphere) continue;
                min = Vector3.Min(min, volume.centre);
                max = Vector3.Max(max, volume.centre);
            }
            var spread = max - min;
            int longAxis = spread.x >= spread.y && spread.x >= spread.z ? 0 : spread.y >= spread.z ? 1 : 2;
            int crossA = longAxis == 0 ? 1 : 0;
            int crossB = longAxis == 2 ? 1 : 2;
            var scale = model.lossyScale;
            float scaleA = Mathf.Abs(scale[crossA]), scaleB = Mathf.Abs(scale[crossB]);
            foreach (var volume in volumes)
            {
                if (volume == null || volume.sphere) continue;
                float a = Mathf.Abs(volume.half[crossA]) * scaleA;
                float b = Mathf.Abs(volume.half[crossB]) * scaleB;
                float radius = Mathf.Sqrt(a * a + b * b);
                if (radius > 0.1f) hullProbes.Add(new HullProbe(volume.centre, Mathf.Max(3f, radius)));
            }
        }

        Vector3 HullProbeLocalOffset(Vector3 localCentre)
        {
            return probeModel != null ? transform.InverseTransformPoint(probeModel.TransformPoint(localCentre)) : Vector3.zero;
        }

        Vector3 HullProbeWorldOffset(Vector3 localCentre)
        {
            return transform.TransformVector(HullProbeLocalOffset(localCentre));
        }

        /// <summary>Metres: the probe sphere round the ship's centre, a third of the model's largest extent (the original
        /// tests the ship's centre point, so half the hull sank into a station). Made once per model.</summary>
        float ProbeRadius()
        {
            var ship = GetComponent<ShipController>();
            var model = ship != null ? ship.visualModel : null;
            if (probe == null)
            {
                var go = new GameObject("CollisionProbe") { layer = HullCollision.ProbeLayer };
                go.transform.SetParent(transform, false);
                probe = go.AddComponent<SphereCollider>();
                probe.isTrigger = true;
            }
            if (model != probeModel)
            {
                probeModel = model;
                float size = 20f;
                if (model != null)
                {
                    var b = new Bounds(model.position, Vector3.zero);
                    foreach (var mr in model.GetComponentsInChildren<MeshRenderer>()) b.Encapsulate(mr.bounds);
                    size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                }
                probe.radius = Mathf.Clamp(size / 3f, 3f, 60f);
            }
            return probe.radius;
        }

        Transform probeModel;

        void CheckWormhole()
        {
            if (Pull(wormhole, true)) InWormhole = true;
            if (Pull(eventWormhole, false)) InEventWormhole = true;
        }

        /// <summary>One wormhole's pull (the story's rules only for 'story'); true: the ship is inside.</summary>
        bool Pull(GoF2Remake.World.Wormhole w, bool story)
        {
            if (w == null) return false;
            if (!w.Visible || w.Shrinking) { w.SetSound(false); return false; }
            if (story && wormholeHeld != null && wormholeHeld()) return false;
            var d = w.transform.position - transform.position;
            float units = d.magnitude / GoF2Remake.World.OrbitLayout.MetersPerUnit;
            float pull = GoF2Remake.World.Wormhole.RadiusUnits - units;
            if (pull < 1f) { w.SetSound(false); return false; }
            w.SetSound(true);
            if (!story || wormholeNoPull == null || !wormholeNoPull())
            {
                transform.position += d.normalized * ((int)pull >> 8) * (Time.deltaTime * 1000f / OriginalFrame.Ms) * GoF2Remake.World.OrbitLayout.MetersPerUnit;
                Hit();
                Haptics.Rumble(0.1f + 0.6f * pull / GoF2Remake.World.Wormhole.RadiusUnits);   // remake: stronger closer in
            }
            return units < GoF2Remake.World.Wormhole.InsideUnits;
        }

        /// <summary>The asteroid's cube (+-radius) grown by 'probe'; its own hit boxes (if any) as they are.</summary>
        static bool TouchesAsteroid(Target t, Vector3 pos, float probe)
        {
            if (t.boxes != null && t.boxes.Length > 0) return t.Contains(pos);
            var d = t.transform.position - pos;
            float r = t.radius + probe;
            return Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r;
        }

        /// <summary>PlayerEgo::calcCollision: an asteroid touched (the volatile goods' +0.2).</summary>
        public event System.Action AsteroidHit;

        /// <summary>The asteroid part: the asteroid is destroyed, the player takes 20. Remake (after a contributor's version,
        /// 2026-10-11): its cube grown by the ship's probe sphere, so the ship touches it with its hull, not only its centre.</summary>
        void CheckAsteroids()
        {
            var pos = transform.position;
            float probeR = ProbeRadius();
            var all = Target.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (t == null || !t.isAsteroid || !t.Alive) continue;
                if (!TouchesAsteroid(t, pos, probeR)) continue;
                t.Damage(9999f);
                if (GoF2Remake.Data.Settings.CollisionDamage) Ram(pos - t.transform.position);
                else if (!health.invulnerable) health.Target.Damage(20f);
                AsteroidHit?.Invoke();   // volatile goods: +0.2 (VolatileCargo)
                Hit();
                Haptics.Play(Haptics.Impact);   // remake
            }
        }
    }
}
