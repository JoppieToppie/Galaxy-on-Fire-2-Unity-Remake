// PlayerCapitalMissiles.cs
// Remake (the Debug page's Ships tab, players' suggestion): a capital ship hull flown by the player (the Terran battleship,
// the carrier, the Vossk battleship, the Valkyrie: PlayerHull) launches the enhanced capital ships' missile salvo
// (CapitalShip.UpdateMissiles) on the fire button: 4 homing missiles of its race (the Terrans' Intelli Jet 37, the Vossk
// S'koonn 38), 250 ms apart, up out of the turret mounts, at the locked ship when it is hostile, else the nearest hostile
// ship (a red marker) within 40 000 units (2 km); never a neutral or friendly one. One salvo per 4 s while the button is
// held. These hulls fly with the battleship's stats (no weapon slots), so this is their own weapon.
// Damage: the missile item's (attr 9), x3 on a big ship's hull (hit boxes), like the capital ships' factors against them.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class PlayerCapitalMissiles : MonoBehaviour
    {
        const float M = 0.05f;
        const float SalvoCooldownMs = 4000f, BigFactor = 3f;

        SpaceLevel level;
        WeaponSystem weapons;
        Gun missiles;
        GunRig rig;
        WeaponFx fx;
        Transform root;
        readonly List<Transform> launchers = new List<Transform>();
        Target salvoTarget;
        int salvoLeft, launcherIndex;
        float gapMs, cooldownMs;

        /// <summary>The salvo for the player's capital hull: 'item' its missile, 'launchers' the turrets on the model.</summary>
        public static PlayerCapitalMissiles Attach(SpaceLevel level, int item, List<Transform> launchers)
        {
            var it = level.Database.Item(item);
            if (it == null || level.Player == null) return null;
            var c = level.Player.gameObject.AddComponent<PlayerCapitalMissiles>();
            c.level = level;
            c.weapons = level.Weapons;
            c.launchers.AddRange(launchers);
            float speed = it.Attr(13, 0) > 0 ? it.Attr(13, 0) : CapitalShips.MissileSpeed;
            c.missiles = new Gun(it, Mathf.Max(1, it.Attr(9, 100)), CapitalShips.MissileGapMs, CapitalShips.MissilesPerSalvo * 2,
                                 CapitalShips.MissileLifetimeMs, speed)
            {
                owner = level.Health != null ? level.Health.Target : null, homingDelayMs = 1000f,
            };
            // Never the player's own hull or turrets (untargetable, on the hull) or a friend in the way.
            c.missiles.Ignores = t => t != null && (t.isPlayer || t.untargetable || t.friendToPlayer);
            c.fx = WeaponFx.Load(it.index);
            c.root = new GameObject("Player capital missiles").transform;
            c.rig = new GunRig(c.missiles, c.fx, c.root, null, 2);
            c.rig.EnableTrails();
            c.missiles.Hit += c.OnHit;
            return c;
        }

        void OnDestroy()
        {
            rig?.HideAll();
            if (root != null) Destroy(root.gameObject);
        }

        void OnHit(int b, Target hit, Vector3 point)
        {
            bool big = hit.boxes != null && hit.boxes.Length > 0;
            hit.lastPlayerWeapon = missiles.itemIndex;
            hit.Damage(missiles.damage * (big ? BigFactor : 1f), false, missiles.bullets[b].velocity, missiles.damageType == DamageType.None ? 1f : missiles.shieldFactor, missiles.damageType);
            rig.ShowImpact(point);
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;
            if (missiles == null || dtMs <= 0f) return;
            var lockTarget = salvoTarget != null && salvoTarget.Alive && !salvoTarget.cloaked ? salvoTarget : null;
            missiles.Update(dtMs, Target.All, lockTarget);
            rig.UpdateVisuals(dtMs, Camera.main, transform.up);
            cooldownMs -= dtMs;
            bool dead = level.Health != null && level.Health.Dead;
            if (dead || level.Cutscene || weapons == null || weapons.Blocked || weapons.TurretView) { salvoLeft = 0; return; }
            if (salvoLeft > 0)
            {
                gapMs -= dtMs;
                if (gapMs > 0f) return;
                gapMs = CapitalShips.MissileGapMs;
                if (lockTarget == null) { salvoLeft = 0; return; }
                if (Launch(lockTarget)) salvoLeft--;
                else gapMs = 0f;
                return;
            }
            if (!weapons.FireHeld || cooldownMs > 0f) return;
            salvoTarget = PickTarget();
            if (salvoTarget == null) return;
            salvoLeft = CapitalShips.MissilesPerSalvo;
            gapMs = 0f;
            cooldownMs = SalvoCooldownMs;
        }

        bool Launch(Target target)
        {
            launchers.RemoveAll(l => l == null);
            var from = launchers.Count > 0 ? launchers[launcherIndex % launchers.Count].position + transform.up * 400f * M
                                           : transform.position + transform.up * 3000f * M;
            var toward = (target.transform.position - from).normalized;
            var dir = (transform.up * 0.35f + toward).normalized;   // a short climb out of the launcher, then homing
            int b = missiles.TryFire(from, Quaternion.LookRotation(dir, transform.forward), false);
            if (b < 0) return false;
            if (CapitalShips.SpreadAim(target, null, out var aim)) missiles.SetAim(b, aim);   // a big hull: its own spot
            launcherIndex++;
            rig.OnShot();
            Sfx.PlayAt(fx != null ? fx.Shot : null, from, 0.8f);
            return true;
        }

        /// <summary>The radar's lock when it is hostile, else the nearest hostile ship in reach; never a neutral or friend.</summary>
        Target PickTarget()
        {
            var radar = GetComponent<CombatRadar>();
            var locked = radar != null ? radar.Locked : null;
            if (Hostile(locked)) return locked;
            Target best = null;
            float bestD = CapitalShips.MissileRangeUnits * M + HullReach;
            foreach (var t in Target.All)
            {
                if (!Hostile(t)) continue;
                float d = (t.transform.position - transform.position).magnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        static bool Hostile(Target t) => t != null && t.isShip && !t.isPlayer && t.Alive && t.hostileToPlayer && !t.cloaked && !t.untargetable
                                         && t.isActiveAndEnabled;

        /// <summary>The range counts from the hull's surface, roughly: half the model's largest extent.</summary>
        float HullReach
        {
            get
            {
                if (hullReach < 0f)
                {
                    var b = new Bounds(transform.position, Vector3.zero);
                    var model = level.Player.visualModel != null ? level.Player.visualModel : transform;
                    foreach (var r in model.GetComponentsInChildren<MeshRenderer>()) b.Encapsulate(r.bounds);
                    hullReach = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
                }
                return hullReach;
            }
        }
        float hullReach = -1f;
    }
}
