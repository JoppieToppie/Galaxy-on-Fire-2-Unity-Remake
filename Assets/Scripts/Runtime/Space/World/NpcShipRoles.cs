// NpcShipRoles.cs
// NpcShip, part: a mod ship's role in a fight (Flight.NpcRoles; ships.json "traffic": { "role" }, EVERSPACE 2's drone types).
// The steering reads RoleFires / RoleRangeFactor / RoleBreaksOff, GunHit the webber's slow; UpdateRole runs the rest each
// frame of flight: the detonator's blast, the shield charger's repairs, the armour link's damage share, the anti-missile
// drone's shots at the player's missiles, the loot drone's run (its pieces of loot, its flight and jump; Steer reads
// RoleFlees). JumpSuppressed is the Khador Drive's check (SystemJump).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        public NpcRoles.Role Role { get; private set; }
        float roleTickMs;
        readonly List<Target> linked = new List<Target>();
        static readonly List<NpcShip> suppressors = new List<NpcShip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRoleStatics() => suppressors.Clear();

        /// <summary>A jump suppressor flies, hostile to the player, in this orbit: the Khador Drive won't charge.</summary>
        public static bool JumpSuppressed
        {
            get
            {
                for (int i = suppressors.Count - 1; i >= 0; i--)
                {
                    var s = suppressors[i];
                    if (s == null) { suppressors.RemoveAt(i); continue; }
                    if (s.isActiveAndEnabled && s.Current == State.Fly && s.Target != null && s.Target.Alive && s.Target.hostileToPlayer) return true;
                }
                return false;
            }
        }

        bool RoleFires => NpcRoles.Fires(Role);
        float RoleRangeFactor => Role == NpcRoles.Role.Sniper ? NpcRoles.FireRangeFactor : 1f;
        /// <summary>The detonator flies into its target instead of circling away at the break-off range.</summary>
        bool RoleBreaksOff => Role != NpcRoles.Role.Detonator && !RoleFlees;
        /// <summary>A loot drone that was found: it flies away from the player.</summary>
        bool RoleFlees => Role == NpcRoles.Role.LootDrone && lootAwake && FleeFrom != null;
        Target FleeFrom => traffic != null && traffic.Player != null && traffic.Player.Alive ? traffic.Player : null;

        // The loot drone: the hull shares still to drop a piece at (NpcRoles.DropThresholds, NaN once dropped), whether it was
        // found, its flight's delay and its jump's charge (ms, -1 = not yet).
        float[] dropAt;
        bool lootAwake;
        float fleeMs = -1f, chargeMs = -1f;

        /// <summary>Remake (World.RiftEncounter): a rift mutator's speed on this ship (Evasive, Strength in Death); 1 = none.</summary>
        [System.NonSerialized] public float roleSpeedFactor = 1f;

        /// <summary>Its gun's damage a second (0 without one): what the rift's blasts are measured in.</summary>
        public float GunDps => gun != null ? gun.damage * 1000f / Mathf.Max(1f, NpcTables.GunReloadMs) : 0f;

        /// <summary>Remake (a rift boss's Evasive Drift): a panic boost now, as after heavy damage.</summary>
        public void BoostAway() { boostTimer = 10000f; panic = true; }

        void SetupRole(SpawnSpec spec)
        {
            roleSpeedFactor = 1f;
            Role = NpcRoles.Parse(spec.role ?? (db?.Ship(spec.ship) as CustomShipData)?.traffic?.role);
            if (Role == NpcRoles.Role.JumpSuppressor && !suppressors.Contains(this)) suppressors.Add(this);
        }

        /// <summary>After its cargo is rolled (Setup, Revive): a loot drone's LootMin..LootMax pieces, each a stack of its own,
        /// its cargo first, then freighter rolls (NpcTables.RollLoot), energy cells when those come up empty.</summary>
        void SetupRoleLoot()
        {
            dropAt = null;
            lootAwake = false;
            fleeMs = chargeMs = -1f;
            if (Role != NpcRoles.Role.LootDrone || Spec.noLoot || db == null) return;
            int n = Random.Range(NpcRoles.LootMin, NpcRoles.LootMax + 1);
            var pieces = new List<ItemStack>();
            foreach (var s in loot) if (s != null && s.amount > 0 && pieces.Count < n) pieces.Add(s);
            for (int tries = 0; pieces.Count < n && tries < n * 2; tries++)
                foreach (var s in NpcTables.RollLoot(db, true)) if (pieces.Count < n) pieces.Add(s);
            while (pieces.Count < n) pieces.Add(new ItemStack(GalaxyMap.EnergyCellItem, Random.Range(1, 5)));
            loot = pieces;
            multiKindLoot = true;   // what is left drops a crate a piece (DropCrate)
            dropAt = NpcRoles.DropThresholds(n, () => Random.value);
        }

        void TeardownRole()
        {
            Unlink();
            suppressors.Remove(this);
        }

        void OnDisable() => Unlink();

        void Unlink()
        {
            foreach (var t in linked) if (t != null) t.damageFactor = 1f;
            linked.Clear();
        }

        /// <summary>A ship of the same race (its wing), alive and in flight.</summary>
        bool Ally(Target t, out NpcShip ship)
        {
            ship = null;
            if (t == null || t == Target || t.isPlayer || !t.isShip || !t.Alive) return false;
            ship = t.GetComponent<NpcShip>();
            return ship != null && ship.Race == Race && ship.Current == State.Fly;
        }

        void UpdateRole(float dtMs)
        {
            if (Role == NpcRoles.Role.LootDrone) { if (Target.Alive) UpdateLootDrone(dtMs); return; }
            if (Role == NpcRoles.Role.None || !Target.Alive || Hp.empDisabled) { Unlink(); return; }
            if (Role == NpcRoles.Role.Detonator) { UpdateDetonator(); return; }
            roleTickMs += dtMs;
            if (Role == NpcRoles.Role.AntiMissile)
            {
                if (roleTickMs < NpcRoles.AntiMissileReloadMs) return;
                roleTickMs = 0f;
                if (!Target.hostileToPlayer) return;
                foreach (var t in Target.All)
                    if (t != null && t.isPlayer && t.isActiveAndEnabled)
                        t.GetComponent<WeaponSystem>()?.ShootDownMissile(transform.position, NpcRoles.AntiMissileM);
                return;
            }
            if (roleTickMs < 250f) return;
            float step = roleTickMs;
            roleTickMs = 0f;
            float r2 = NpcRoles.SupportM * NpcRoles.SupportM;
            if (Role == NpcRoles.Role.ShieldCharger)
            {
                foreach (var t in Target.All)
                {
                    if (!Ally(t, out _) || (t.transform.position - transform.position).sqrMagnitude > r2) continue;
                    var hp = t.hitpoints;
                    if (hp == null || hp.hull >= hp.maxHull) continue;
                    hp.hull = Mathf.Min(hp.maxHull, hp.hull + Mathf.Max(1, Mathf.RoundToInt(NpcRoles.Repair(hp.maxHull, step))));
                    t.hp = hp.hull;
                }
            }
            else if (Role == NpcRoles.Role.ArmorLink)
            {
                // ES2's link drone holds one leader: here the nearest ally in reach, kept while it stays in reach
                Target lead = linked.Count > 0 && linked[0] != null && Ally(linked[0], out _)
                              && (linked[0].transform.position - transform.position).sqrMagnitude <= r2 ? linked[0] : null;
                if (lead == null)
                {
                    Unlink();
                    float best = r2;
                    foreach (var t in Target.All)
                    {
                        if (!Ally(t, out _) || t.damageFactor != 1f) continue;   // (another rule's factor, a capital ship's shield, stays)
                        float d = (t.transform.position - transform.position).sqrMagnitude;
                        if (d <= best) { best = d; lead = t; }
                    }
                    if (lead == null) return;
                    lead.damageFactor = NpcRoles.LinkDamageFactor;
                    linked.Add(lead);
                }
                var hp = lead.hitpoints;
                if (hp != null && hp.hull < hp.maxHull)
                {
                    hp.hull = Mathf.Min(hp.maxHull, hp.hull + Mathf.Max(1, Mathf.RoundToInt(hp.maxHull * NpcRoles.LinkRepairShare * step / 1000f)));
                    lead.hp = hp.hull;
                }
            }
        }

        /// <summary>The loot drone: a piece out at each share of its hull passed; found (hit, or the player within its detection
        /// box), it runs and then jumps.</summary>
        void UpdateLootDrone(float dtMs)
        {
            if (dropAt != null && Hp.hull < Hp.maxHull)   // (ES2 checks on a change of its health: none at full hull)
            {
                float share = (float)Hp.hull / Hp.maxHull;
                for (int i = 0; i < dropAt.Length; i++)
                    if (!float.IsNaN(dropAt[i]) && share <= dropAt[i]) { dropAt[i] = float.NaN; DropPiece(); }
            }
            if (!lootAwake)
            {
                if (Hp.hull >= Hp.maxHull && !(FleeFrom != null && InBox(FleeFrom))) return;
                lootAwake = true;
                fleeMs = Random.Range(NpcRoles.FleeDelayMinMs, NpcRoles.FleeDelayMaxMs);
            }
            if (fleeMs > 0f) { fleeMs -= dtMs; if (fleeMs <= 0f) chargeMs = 0f; return; }
            if (Hp.empDisabled) { chargeMs = 0f; return; }   // the EMP stops the charge; it starts over afterwards
            chargeMs += dtMs;
            if (chargeMs >= NpcRoles.JumpChargeMs) JumpOut();   // gone with the rest
        }

        /// <summary>One of its pieces thrown off in a crate of its own, with a small blast.</summary>
        void DropPiece()
        {
            int i = loot.FindIndex(s => s != null && s.amount > 0);
            if (i < 0 || assets == null) return;
            var piece = loot[i];
            loot.RemoveAt(i);
            MakeCrate(new List<ItemStack> { piece }).Launch(Random.onUnitSphere, CrateBurst);
            Explosion.Spawn(transform.position, 0.5f);
        }

        /// <summary>Within the trigger of its target: the blast on every ship around, then itself.</summary>
        void UpdateDetonator()
        {
            var t = target;
            if (t == null || !t.Alive || !attacking) return;
            if ((t.transform.position - transform.position).sqrMagnitude > NpcRoles.TriggerM * NpcRoles.TriggerM) return;
            float dmg = NpcRoles.BlastDamage(GunDps > 0f ? GunDps : 10f);
            float b2 = NpcRoles.BlastM * NpcRoles.BlastM;
            var pos = transform.position;
            var all = Target.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var o = all[i];
                if (o == null || o == Target || !o.Alive || !(o.isShip || o.isPlayer)) continue;
                var d = o.transform.position - pos;
                if (d.sqrMagnitude > b2) continue;
                o.Damage(dmg, true, d.normalized);
            }
            Target.lastPlayerWeapon = -1;   // its own blast: nobody's kill
            Target.Damage(Hp.Combined + 1f, true, transform.forward, 0f);
        }
    }
}
