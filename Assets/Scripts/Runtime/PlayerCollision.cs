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
//                                              pulled toward it by (40000 - d) / 256 units per 30 fps frame, camera
//                                              hit(); within 1000 units the ship is inside (PlayerEgo+0x25)
// Collision is off during the launch / arrival camera and the jump scenes (PlayerEgo+0x144, set by the level), while
// mining (PlayerEgo+0x356 with a mining phase 1-3) and once dead.
// The 1000 ms jitter of the ship model after a hit (PlayerEgo+0x328 / +0x32c, +-0.006 units) is too small to see and
// isn't reproduced.
// Remake: a phase cloak (PlayerCloak.Phasing, a mod item's attribute 104) lets the ship pass through the landmarks, the
// freighters, other players' ships and the asteroids while it is cloaked; the wormhole still pulls.
// Remake haptics: running into a landmark is a knock, then a scraping rumble while the ship slides along it; an asteroid a
// knock; the wormhole's pull a rumble that grows toward it.

using System;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [DefaultExecutionOrder(50)]   // after ShipController has moved the ship
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

        PlayerHealth health;
        ChaseCamera chase;
        Mining mining;
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

        void Update()
        {
            TouchingStation = false;
            scraping = false;
            if (off || health == null || health.Dead) { wormhole?.SetSound(false); scrapedLastFrame = false; return; }
            if (mining != null && mining.State != Mining.Phase.Idle) { scrapedLastFrame = false; return; }
            if (docking != null && docking.Busy && docking.State != ObjectDocking.Phase.Approach) { scrapedLastFrame = false; return; }   // easing onto a docking point
            CheckWormhole();
            if (cloak != null && cloak.Phasing) { scrapedLastFrame = false; return; }   // phased out: nothing solid
            CheckObstacles(true);
            CheckObstacles(false);
            CheckAsteroids();
            if (scraping)
            {
                if (!scrapedLastFrame) Haptics.Play(Haptics.Impact);
                Haptics.Rumble(ScrapeRumble);
            }
            scrapedLastFrame = scraping;
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
                if (o == null || o.landmark != landmarks || !o.Active) continue;
                if (ignoreGate && o.cubeIsContact) continue;
                // Remake: the object being docked at doesn't block its own approach (the original's autopilot could pin the
                // ship on a hull face when it started beside the object; from afar it comes in over the top anyway).
                if (docking != null && docking.State == ObjectDocking.Phase.Approach && docking.Target != null
                    && (o.gameObject == docking.Target.gameObject
                        // ... nor the station a docking target stands in for (102: Tadram's pads)
                        || (o.isStation && (o.transform.position - docking.Target.transform.position).sqrMagnitude < 1f))) continue;
                var pos = transform.position;
                if (!o.Touches(pos, out _)) continue;
                transform.position = o.PushOut(pos);
                if (o.isStation) TouchingStation = true;
                scraping = true;
                Hit();
            }
        }

        void CheckWormhole()
        {
            var w = wormhole;
            if (w == null) return;
            if (!w.Visible || w.Shrinking) { w.SetSound(false); return; }
            if (wormholeHeld != null && wormholeHeld()) return;
            var d = w.transform.position - transform.position;
            float units = d.magnitude / GoF2Remake.World.OrbitLayout.MetersPerUnit;
            float pull = GoF2Remake.World.Wormhole.RadiusUnits - units;
            if (pull < 1f) { w.SetSound(false); return; }
            w.SetSound(true);
            if (wormholeNoPull == null || !wormholeNoPull())
            {
                transform.position += d.normalized * ((int)pull >> 8) * (Time.deltaTime * 1000f / 33.3f) * GoF2Remake.World.OrbitLayout.MetersPerUnit;
                Hit();
                Haptics.Rumble(0.1f + 0.6f * pull / GoF2Remake.World.Wormhole.RadiusUnits);   // remake: stronger closer in
            }
            if (units < GoF2Remake.World.Wormhole.InsideUnits) InWormhole = true;
        }

        /// <summary>PlayerEgo::calcCollision: an asteroid touched (the volatile goods' +0.2).</summary>
        public event System.Action AsteroidHit;

        /// <summary>The asteroid part: the asteroid is destroyed, the player takes 20.</summary>
        void CheckAsteroids()
        {
            var pos = transform.position;
            var all = Target.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (t == null || !t.isAsteroid || !t.Alive) continue;
                if (!t.Contains(pos)) continue;
                t.Damage(9999f);
                if (!health.invulnerable) health.Target.Damage(20f);
                AsteroidHit?.Invoke();   // volatile goods: +0.2 (VolatileCargo)
                Hit();
                Haptics.Play(Haptics.Impact);   // remake
            }
        }
    }
}
