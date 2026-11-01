// NpcShipTurrets.cs
// Part of NpcShip: turret mode (the capital ships' and pirate outposts' turret objects on the shared TurretCore /
// TurretTargeting), the turrets on the player's own hull, and the fighter turret that follows its host (SyncTurret).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        // ---- turrets (PlayerTurret::handleTurret / pickEnemy / handleRotation) ---------------------------------------
        // The aim, shots and pick are the shared turret's (Flight.TurretCore, TurretTargeting.Rule.Timed); this object keeps
        // its own Target, hull, death and the host rules.

        void SetupTurret()
        {
            Target.radius = 1000f * M;   // the normal +-1000 cube (Player radius 1000), no collision volume
            // Remake: a turret object built from a turret item (SpawnSpec.turretItem: its *_ship_mounted model, which
            // Traffic.Create loaded) aims and fires like that item on a ship (its rates, pitch range and gun).
            var turretItem = Spec.turretItem >= 0 ? db.Item(Spec.turretItem) : null;
            var turretFx = turretItem != null ? WeaponFx.Load(turretItem.index) : null;
            if (turretItem != null && turretFx != null && turretFx.turretMounted != null && modelGo != null)
            {
                turretCore = TurretCore.ForItemModel(turretItem, turretFx, modelGo, fxRootRef, name, transform.forward);
                MakeTurretPicking();
                if (turretCore.Gun == null) return;
                gun = turretCore.Gun;
                rig = turretCore.Rig;
                gun.owner = Target;
                gun.damage *= Spec.gunFactor;   // gunFactor: the remake's CapitalShips
                WireGun(gun);
                turretShot = turretFx.shot != null ? turretFx.Shot : null;
                return;
            }
            Transform pivot = null, barrel = null;
            if (model != null) TurretCore.FindNodes(model, out pivot, out barrel);
            // Pitch counter -600 .. +100 ms at 2 pi / 4096 rad per ms: about 52.7 deg up, 8.8 deg down.
            turretCore = TurretCore.Wrap(model, pivot, barrel, pivot != null && barrel != null ? TurretCore.StaticAim(pivot, barrel) : null,
                                         TurretCore.ShotFrom.AheadOfBarrel);
            MakeTurretPicking();
            gunBase = (int)(NpcTables.GunDamage(Spec, false, true, out gunSpeed) * Spec.gunFactor);   // gunFactor: the remake's CapitalShips
            // Level::assignGuns: a sentry gun object (0x49c0 / 0x49c1 / 0x49c2 = sn_sentry_gun_001..003) fires its item's
            // shot (211 / 212 / 213: attr 9 damage, 11 reload, 12 lifetime, 13 speed); at 0x9e (Harval's, always enemy)
            // x1.5 damage, x1.2 speed and x5 hull; the LevelScript ctor of 0x9e scales their damage by 0.3.
            int sentry = Spec.turretAssembly.StartsWith("sn_sentry_gun_00") ? Spec.turretAssembly[16] - '0' : 0;
            var sentryItem = sentry >= 1 && sentry <= 3 ? db.Item(210 + sentry) : null;
            if (sentryItem != null)
            {
                float dmg = sentryItem.Attr(9), speedU = sentryItem.Attr(13, 22);
                if (!Session.FreePlay && Session.CampaignMission == 0x9e && sentry == 3 && alwaysEnemy)
                {
                    dmg *= 1.5f * 0.3f;
                    speedU *= 1.2f;
                    SetHull(Hp.maxHull * 5);
                }
                int look = sentry == 1 ? 2 : sentry == 2 ? 20 : 14;   // Gun::setIndex 2 / 0x14 / 0xe
                gun = new Gun(db.Item(look) ?? sentryItem, (int)dmg, sentryItem.Attr(11, 430), NpcTables.GunPool, sentryItem.Attr(12, 1000), speedU) { owner = Target };
                rig = new GunRig(gun, WeaponFx.Load(look), fxRootRef, turretCore.GunNode, 2);
                WireGun(gun);
                turretCore.SetGun(gun, rig);
                return;
            }
            var item = db.Item(Spec.race == 1 ? 15 : 20);
            if (item != null)
            {
                // Remake (CapitalShips): an enhanced turret's shots fly 3600 ms (57 600 units), past its 50 000-unit reach.
                gun = new Gun(item, gunBase, NpcTables.GunReloadMs, NpcTables.GunPool,
                                  Spec.capitalEnhanced ? World.CapitalShips.TurretLifetimeMs : NpcTables.GunLifetimeMs, gunSpeed) { owner = Target };
                rig = new GunRig(gun, WeaponFx.Load(item.index), fxRootRef, turretCore.GunNode, 2);
                WireGun(gun);
                turretCore.SetGun(gun, rig);
            }
        }

        /// <summary>pickEnemy 0x182e90: the nearest (Euclidean) active target within 50000 that is hostile to this turret's race;
        /// the player only when the turret is hostile to it; the last unreachable one only when nothing else is there.</summary>
        void MakeTurretPicking()
        {
            turretPicking = new TurretTargeting(TurretTargeting.Rule.Timed, TurretRangeUnits)
            {
                boundFactor = 0.3f,   // Measure: at least the distance to the target's sphere, x0.3 for a turret hunter
                Candidate = e => Valid(e) && !e.cloaked   // PlayerTurret::handleTurret: no aiming at a cloaked target
                    && (PlayerOwned ? !e.isPlayer && e.isShip && e.hostileToPlayer
                        : e.isPlayer ? Target.hostileToPlayer : e.isShip && e.race >= 0 && Standing.RacesHostile(e.race, Race)),
                Measure = (e, from, bestD) =>
                {
                    float d = (TurretAimPoint(e, from) - from).magnitude;
                    // Remake (CapitalShips): a turret hunter takes an enemy capital ship's turret in reach before its hull (the
                    // hull's nearest point is always nearer, so no turret was ever shot at in a fleet battle).
                    if (d < bestD && Spec.turretHunter && !e.isPlayer && (e.boxes == null || e.boxes.Length == 0) && IsTurretTarget(e)) d *= 0.3f;
                    return d;
                },
            };
        }

        void UpdateTurret(float dtMs)
        {
            if (turretCore == null || !turretCore.Valid || gun == null) return;
            if (!turretHostLooked)
            {
                // Remake (LineOfFire): the ship it sits on blocks its fire: its capital ship, else the ship it rides on.
                turretHostLooked = true;
                var hostShip = Spec.capitalHost != null && traffic != null ? traffic.Ships.Find(s => s != null && s.Spec == Spec.capitalHost) : null;
                if (hostShip == null && transform.parent != null) hostShip = transform.parent.GetComponentInParent<NpcShip>();
                if (hostShip != null && hostShip != this) turretCore.Host = hostShip.transform;
            }
            var target = turretPicking.Update(dtMs, enemies, transform.position);
            if (turretPicking.JustPicked) turretTargetFixed = target != null && IsTurretTarget(target);
            if (target == null || target.cloaked) return;   // handleTurret: no turning toward a cloaked target
            var at = Spec.capitalEnhanced && target.boxes != null && target.boxes.Length > 0 ? TurretAimPoint(target, turretCore.GunPosition)
                   : turretTargetFixed ? AimAt(target, dtMs)
                   : TurretTargeting.LeadPoint(target) + (AimAt(target, dtMs) - target.transform.position);   // led, at a spot on it
            bool aligned = turretCore.StepToward(at, dtMs);
            if (turretCore.Aim.LimitHit) { turretPicking.LimitHit(dtMs); return; }   // out of reach: dropped at the next pick
            if (!aligned || !shootingEnabled || HoldFire) return;
            // Bullets from the barrel frame, the usual 100 ahead in the scaled frame (about 600 units); a turret item's from
            // its gun like on a ship.
            int shot = turretCore.Fire();
            if (shot < 0 && turretCore.Blocked) { turretPicking.LimitHit(dtMs); return; }   // its own hull in the way: another target
            if (shot >= 0)
                Sfx.PlayAt(turretShot != null ? turretShot
                           : assets != null && assets.shots != null && assets.shots.Length > 0 ? assets.shots[Race == 1 ? 1 : 0] : null, transform.position, 0.6f);
        }

        /// <summary>A turret on the player's hull (PlayerHull): it aims at whatever is hostile to the player and its
        /// shots pass through the player.</summary>
        public void MakePlayerTurret()
        {
            PlayerOwned = true;
            alwaysFriend = true;
            turretPicking?.Clear();
            if (gun == null) return;
            var before = gun.Ignores;
            gun.Ignores = t => (t != null && t.isPlayer) || (before != null && before(t));
        }

        static bool IsTurretTarget(Target t)
        {
            var s = t != null ? t.GetComponent<NpcShip>() : null;
            return s != null && s.IsTurret;
        }

        /// <summary>Where a turret measures and aims: the target's position, or (remake, World.CapitalShips) for an enhanced
        /// turret the nearest point of a big ship's hull (Target.NearestPoint: a capital ship 3 km long is in reach long before
        /// its centre).</summary>
        Vector3 TurretAimPoint(Target e, Vector3 from) => Spec.capitalEnhanced ? e.NearestPoint(from) : e.transform.position;

        /// <summary>The Terran battleship died: every turret of the level takes 9 999 999 (not credited to the player).</summary>
        public void DestroyAsTurret()
        {
            if (!IsTurret || !Target.Alive) return;
            Target.invulnerable = false;   // a fighter's turret is invulnerable: it stayed in space after its host died
            Target.Damage(9999999f, true);
        }

        /// <summary>The fighter turret shows, hides, sides and dies with its host.</summary>
        void SyncTurret()
        {
            var t = AttachedTurret;
            if (t == null) return;
            if (Current != State.Fly || Gone) { t.DestroyAsTurret(); AttachedTurret = null; return; }
            t.alwaysEnemy = alwaysEnemy;
            t.alwaysFriend = alwaysFriend;
            t.turnedEnemy = turnedEnemy;
            t.SetVisible(modelGo == null || modelGo.activeSelf);
            if (Asleep && !t.Asleep) { t.Asleep = true; } else if (!Asleep && t.Asleep) t.Wake();
            t.enemies.Clear();
            t.enemies.AddRange(enemies);
            t.Target.untargetable = true;
        }
    }
}
