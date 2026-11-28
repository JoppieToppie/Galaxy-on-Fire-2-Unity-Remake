// NpcMountedTurrets.cs
// Remake (generic turrets): turret items on an NPC ship's own turret mounts (weapons_hd.json slot 2, a mod ship's
// "mounts"), the way the player's ship carries them (Flight.TurretCore.ForItem: the item's *_ship_mounted model or a mod's
// own, its turn rates, pitch range, muzzle, gun, missiles and salvos), aimed and fired by the NPC turrets' rules
// (TurretTargeting.Rule.Timed, PlayerTurret::pickEnemy 0x182e90: every 3000 ms the nearest race-hostile target, the player
// when the ship is hostile to it, nothing cloaked; its lead point; fire when aligned). The original's NPC ships carry no
// turret items: they come from SpawnSpec.turretItems or a mod's ships.json "npcTurrets" (Database.NpcTurrets). Manual
// turret items (47-49, 224) aim by themselves too (an NPC has no turret view); plasma collectors (198-200) only sit there.
// Range: the item's attr 109 (mods), else the NPC turrets' 50000 units. The shots use the ship's hit rules (NpcShip.GunHit:
// the stray-fire 20 %, EMP, multiplayer) and its guns list (multiplayer's shot mirrors, NpcShip.ExtraGuns).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class NpcMountedTurrets
    {
        const float RangeUnits = 50000f;

        class Mounted
        {
            public TurretCore core;
            public TurretTargeting picking;
            public AudioClip shot;
            public bool collector;
        }

        readonly List<Mounted> turrets = new List<Mounted>();
        readonly NpcShip ship;

        /// <summary>The turret items it carries, in mount order.</summary>
        public readonly List<int> Items = new List<int>();
        public int Count => turrets.Count;
        /// <summary>The n-th turret's moving parts (tests, the debug tools).</summary>
        public TurretCore this[int i] => turrets[i].core;

        NpcMountedTurrets(NpcShip ship) { this.ship = ship; }

        /// <summary>The turrets of 'items' on ship 'shipIndex's turret mounts under 'model' (the n-th item on the n-th mount);
        /// null when none fits.</summary>
        public static NpcMountedTurrets Build(NpcShip ship, Database db, int shipIndex, IList<int> items, Transform model, Transform fxRoot,
                                              System.Action<Gun, GunRig, int, Target, Vector3> onHit)
        {
            if (db == null || items == null || items.Count == 0 || model == null) return null;
            var mounts = db.MountsOf(shipIndex, 2);
            NpcMountedTurrets set = null;
            for (int m = 0; m < items.Count && m < mounts.Count; m++)
            {
                var item = db.Item(items[m]);
                if (item == null || (item.categoryId != 8 && item.categoryId != 35)) continue;
                var fx = WeaponFx.Load(item.index);
                var core = TurretCore.ForItem(item, fx, model, mounts[m], fxRoot, m == 0 ? "Turret" : "Turret " + (m + 1), ship.transform.forward);
                if (core == null) continue;
                core.Host = ship.transform;   // no shots through its own ship (LineOfFire)
                set ??= new NpcMountedTurrets(ship);
                var t = new Mounted { core = core, collector = item.categoryId == 35, shot = fx != null && fx.shot != null ? fx.Shot : null };
                set.turrets.Add(t);
                set.Items.Add(item.index);
                if (!core.Valid || core.Gun == null) continue;
                var gun = core.Gun;
                var rig = core.Rig;
                gun.owner = ship.Target;
                gun.Hit += (b, hit, point) => onHit(gun, rig, b, hit, point);
                if (gun.echo != null) gun.AreaHit += (target, dmg, emp, center) => ship.HitByBlast(gun, target, dmg, emp, center);   // an echo's blasts (Gun.EchoBlast)
                ship.ExtraGuns.Add(gun);   // multiplayer: the other players see its shots
                t.picking = new TurretTargeting(TurretTargeting.Rule.Timed, item.Attr(109) > 0 ? item.Attr(109) : RangeUnits)
                {
                    accelerationLead = true,
                    Sensor = ship.Detects,
                    Candidate = e => e != null && e.Targetable && !e.cloaked   // no aiming at a cloaked target
                        && (e.isPlayer ? ship.Target.hostileToPlayer : e.isShip && e.race >= 0 && Standing.RacesHostile(e.race, ship.Race)),
                };
            }
            return set;
        }

        /// <summary>One frame: aim and fire while 'armed' (flying, awake, shooting allowed), the bullets and visuals always.</summary>
        public void Update(float dtMs, IReadOnlyList<Target> enemies, IReadOnlyList<Target> hitTargets, bool armed)
        {
            foreach (var t in turrets)
            {
                var core = t.core;
                if (!core.Valid || t.collector || core.Gun == null) continue;
                core.SinceShotMs += dtMs;
                Target target = null;
                if (armed)
                {
                    target = t.picking.Update(dtMs, enemies, ship.transform.position);
                    if (target != null && !target.cloaked)
                    {
                        bool aligned = core.StepToward(Settings.TurretLeadAim && core.Gun.isBeam ? TurretTargeting.CombatAimPoint(target, core.GunPosition) :
                            t.picking.LeadPoint(target, core.GunPosition, core.Gun.speedUnitsPerMs, core.Gun.lifetimeMs), dtMs);
                        if (core.Aim.LimitHit) t.picking.LimitHit(dtMs);
                        else if (aligned && core.Fire() >= 0) Sfx.PlayAt(t.shot, core.GunPosition, 0.6f);
                        else if (aligned && core.Blocked) t.picking.LimitHit(dtMs);   // its own hull in the way: another target
                    }
                }
                core.SetAnimating(core.RecentlyFired);
                var homing = core.Gun.missiles && target != null && target.Alive && !target.cloaked ? target : null;
                core.Tick(dtMs, hitTargets, homing);
            }
        }

        /// <summary>Its shots and effects go (the ship died or was put away).</summary>
        public void HideAll()
        {
            foreach (var t in turrets) t.core.Rig?.HideAll();
        }

        /// <summary>A multiplayer copy (NetProxy) shows the turret items: "ship:item,item" (empty = none).</summary>
        public static string Pack(int shipIndex, IList<int> items)
        {
            if (items == null || items.Count == 0) return "";
            return shipIndex + ":" + string.Join(",", items);
        }

        /// <summary>Pack's ship and items back; false for anything else.</summary>
        public static bool Unpack(string packed, out int shipIndex, out List<int> items)
        {
            shipIndex = -1;
            items = new List<int>();
            if (string.IsNullOrEmpty(packed)) return false;
            int colon = packed.IndexOf(':');
            if (colon <= 0 || !int.TryParse(packed.Substring(0, colon), out shipIndex)) return false;
            foreach (var part in packed.Substring(colon + 1).Split(','))
                if (int.TryParse(part, out int i) && i >= 0) items.Add(i);
            return items.Count > 0 && items.Count <= 8;
        }
    }
}
