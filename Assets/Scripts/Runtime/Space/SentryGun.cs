// SentryGun.cs
// A deployed sentry gun (items 211-213, sort 39; Reference/research/weapons_special.md 5.2):
//   Level::createSentryGuns 0xcb0d8   the objects: sn_sentry_gun_00X (scale 0.5), Player(radius 1000 -> 800, 100 HP),
//                                     always friend, name 1666 "Turret"; at most 3 deployed (all types together)
//   SentryGun::update 0x188942        placed where the deploy shot spawned (ship + R * (mount + (0, 0, 100)))
//   PlayerTurret::update (sentry)     invulnerable for 3000 ms, no lifetime; every 3000 ms (first after 3 s) the nearest
//                                     ship hostile to the player within 50000 units; yaw and pitch at 2 pi / 4096 rad per
//                                     ms toward its position + heading * 1500 (a), no pitch limit; fire when aligned
//   Level::assignGuns 0xcb638         its gun: 4 bullets with the sentry item's stats (attr 9 / 11 / 12 / 13), the look of
//                                     item 2 / 20 / 14 (projectile_002 / _021 / _014), muzzle (0, 0, 250) (213: 300);
//                                     kills are "by NPC" (no kill credit)
//   death                             sound 22, a type-0 explosion with fire streaks, gone after 4500 ms, the slot freed
// Hostile NPCs add it to their target list (it is a KIPlayer of the level).
// Its aim, shots and pick are the shared turret's (TurretCore, TurretTargeting.Rule.Retarget).
// Remake, multiplayer: the others in the orbit see it (NetPlayer: NetId, Item, Aim, the hull, Dead), its shots are mirrored,
// and the NPCs another player's game runs attack it like its player's ship; their hits come back here (TakeRemoteHit).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class SentryGun : MonoBehaviour
    {
        const float M = Gun.MetersPerUnit;
        const float InvulnerableMs = 3000f, RangeUnits = 50000f, GoneMs = 4500f;
        public const int MaxActive = 3;

        /// <summary>Level+0x6c: sentries deployed and not yet gone.</summary>
        public static int ActiveCount { get; private set; }

        Target self;
        /// <summary>The sentry's hittable object (its HUD marker: CombatView).</summary>
        public Target Target => self;
        TurretCore core;
        TurretTargeting picking;
        Gun gun => core.Gun;
        GunRig rig => core.Rig;
        Transform fxRoot;
        WeaponFx lookFx;
        float ageMs, deadMs = -1f;
        Transform aimNode;
        static int nextNetId;

        /// <summary>Multiplayer: this sentry among its player's (NetPlayer's list for the others).</summary>
        public int NetId { get; private set; }
        /// <summary>The sentry item (211-213 or a mod's).</summary>
        public int Item { get; private set; }
        /// <summary>The turning model's world rotation (its aim).</summary>
        public Quaternion Aim => aimNode != null ? aimNode.rotation : transform.rotation;
        /// <summary>Destroyed (its explosion; gone 4500 ms later).</summary>
        public bool Dead => deadMs >= 0f;

        public static bool CanDeploy => ActiveCount < MaxActive;
        /// <summary>The deployed sentries (multiplayer: their shots are shown to the others, NetPlayer).</summary>
        public static readonly List<SentryGun> All = new List<SentryGun>();
        public Gun Gun => gun;

        /// <summary>Deploys a sentry of 'item' at 'position' (world), facing 'forward'.</summary>
        public static SentryGun Deploy(Database db, int item, Vector3 position, Quaternion rotation, Traffic traffic)
        {
            var it = db.Item(item);
            var fx = WeaponFx.Load(item);
            if (it == null || !CanDeploy) return null;
            var go = new GameObject($"Sentry gun {item}");
            go.transform.SetPositionAndRotation(position, rotation);
            var s = go.AddComponent<SentryGun>();
            s.NetId = ++nextNetId;
            s.Item = item;
            s.Setup(db, it, fx, traffic);
            ActiveCount++;
            All.Add(s);
            return s;
        }

        void Setup(Database db, ItemData item, WeaponFx fx, Traffic traffic)
        {
            // The deployed object and its turning parts: the whole model yaws and pitches (no separate base).
            var yawNode = new GameObject("yaw").transform;
            yawNode.SetParent(transform, false);
            var pitchNode = new GameObject("gun").transform;
            pitchNode.SetParent(yawNode, false);
            aimNode = pitchNode;
            var anims = new Visuals.PartAnimation[0];
            if (fx != null && fx.sentry != null)
            {
                var model = Instantiate(fx.sentry, pitchNode, false);
                GunRig.StripForFx(model);
                anims = model.GetComponentsInChildren<Visuals.PartAnimation>(true);
                foreach (var a in anims) { a.loop = true; a.speed = 0f; }   // animated only while it fires
            }
            // No pitch limit (TurretAim's NaN), both at 2 pi / 4096 rad per ms.
            var aim = new TurretAim(yawNode, pitchNode) { yawRatePerMs = TurretAim.RadPerMs, pitchRatePerMs = TurretAim.RadPerMs };
            core = TurretCore.Wrap(transform, yawNode, pitchNode, aim, TurretCore.ShotFrom.Muzzle);
            core.SetAnimations(anims);
            picking = new TurretTargeting(TurretTargeting.Rule.Retarget, RangeUnits)
            {
                accelerationLead = true,
                Sensor = t => !CombatRadar.NpcSensorsEnabled(db) || CombatRadar.DetectsFrom(db, transform.position, RangeUnits, t),
                Candidate = t => t.isShip && t.hostileToPlayer && t.Alive && !t.untargetable && !t.cloaked && t.isActiveAndEnabled,
            };

            self = gameObject.AddComponent<Target>();
            self.isShip = true;
            self.race = 0;
            self.friendToPlayer = true;
            self.radius = 800f * M;
            self.hitpoints = new Hitpoints(100);
            self.hp = self.maxHp = 100;
            self.customDeath = true;
            self.invulnerable = true;
            self.displayName = Localization.Get(1666);
            self.Died += OnDied;

            int look = item.Look == 211 ? 2 : item.Look == 212 ? 20 : 14;
            var lookItem = db.Item(look);
            lookFx = WeaponFx.Load(look);
            var g = new Gun(lookItem, item.Attr(9), item.Attr(11, 430), 4, item.Attr(12, 1000), item.Attr(13, 22)) { owner = self, Ignores = t => t.playerProof };
            var muzzle = new GameObject("muzzle").transform;
            muzzle.SetParent(pitchNode, false);
            muzzle.localPosition = new Vector3(0f, 0f, item.Look == 213 ? 300f : 250f) * M;
            core.SetMuzzle(muzzle);
            fxRoot = new GameObject("Sentry fx").transform;
            core.SetGun(g, new GunRig(g, lookFx, fxRoot, muzzle));
            g.Hit += (b, t, p) => { t.Damage(g.damage, true, g.bullets[b].velocity); rig.ShowImpact(p); };
            if (g.echo != null) g.AreaHit += (t, dmg, emp, c) => { if (t != null && dmg > 0) t.Damage(dmg, true, (t.transform.position - c).normalized); };   // an echo's blast (GunEcho)

            // A KIPlayer of the level: the hostile ships may shoot at it.
            if (traffic != null) foreach (var s in traffic.Ships) if (s.Target.hostileToPlayer && !s.enemies.Contains(self)) s.enemies.Add(self);
            if (fx != null && fx.shot != null) Sfx.PlayAt(fx.Shot, transform.position);   // deploy sound 2263 SentryGun_SG400 (all three)
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            if (deadMs >= 0f)
            {
                deadMs += dtMs;
                if (deadMs >= GoneMs) Destroy(gameObject);
                return;
            }
            ageMs += dtMs;
            core.SinceShotMs += dtMs;
            core.SetAnimating(core.RecentlyFired);
            if (ageMs >= InvulnerableMs) self.invulnerable = false;
            core.Tick(dtMs, Target.All, null);
            if (ageMs < InvulnerableMs) return;
            var target = picking.Update(dtMs, Target.All, transform.position);
            if (target == null) return;
            if (core.StepToward(Settings.TurretLeadAim && core.Gun.isBeam ? TurretTargeting.CombatAimPoint(target, core.GunPosition) :
                picking.LeadPoint(target, core.GunPosition, core.Gun.speedUnitsPerMs, core.Gun.lifetimeMs), dtMs) && core.Fire() >= 0
                && lookFx != null && lookFx.shot != null) Sfx.PlayAt(lookFx.Shot, transform.position, 0.6f);
        }

        /// <summary>Multiplayer: another game's hit (an NPC that game runs, or a player where players may fight), checked by
        /// the server and NetPlayer.</summary>
        public void TakeRemoteHit(float amount, bool byNpc, Vector3 hitVector)
        {
            if (self != null && !Dead) self.Damage(amount, byNpc, hitVector);
        }

        void OnDied(Target t)
        {
            deadMs = 0f;
            rig.HideAll();
            var assets = CombatAssets.Load();
            Explosion.Spawn(0, transform.position, transform.forward, 1f, assets != null ? CombatAssets.Pick(assets.explosionMid) : null, true);
            for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            All.Remove(this);
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            if (fxRoot != null) Destroy(fxRoot.gameObject);
        }

        /// <summary>A new level: nothing deployed (no domain reload between Play sessions).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCount() { ActiveCount = 0; All.Clear(); }
    }
}
