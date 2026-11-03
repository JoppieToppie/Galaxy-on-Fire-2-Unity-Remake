// TurretCore.cs
// One turret's moving parts, shared by every turret of the game (remake: the original has PlayerTurret for the NPC
// turrets and sentries and PlayerEgo's turret code for the player's, which the remake had copied three times):
// the model with its yaw node ("pivot") and gun node ("*_gun", its barrel along local +Z), the stepwise aim (TurretAim),
// the muzzle, the gun (Gun + GunRig) and where its shots start. Plain C#; the owners keep what is theirs:
//   PlayerTurret   the player's turret items: the turret view, the auto switch, collectors, the loop sound
//   NpcShip        the NPC turret objects (the capital ships' satellites, the outposts', the fighter turrets of 45 / 51,
//                  the NPC sentry guns): their own Target, hull and death; and (remake) turret items on NPC ships' turret
//                  mounts (World.NpcTurretMounts)
//   SentryGun      the player's deployed sentries
//   the hangar     Display: the still copy on a docked ship (CutScene::checkForTurret 0xa4594), for the player's ship,
//                  the other players' and the NPC ships parked there
// The original's numbers, by owner:
//   PlayerEgo::checkForTurret 0xa722c    an item turret: yaw rate = attr17 * 1.5 / 200 * 2 pi / 4096 rad per ms, pitch
//                                        2 pi / 4096 rad per ms, the pitch accumulator within [-500, +70]: 44 deg up, 6 down
//   PlayerTurret::handleRotation         an NPC turret: both at 2 pi / 4096 rad per ms, pitch counter -600 .. +100 ms
//                                        (52.7 deg up, 8.8 down)
//   ObjectGun::update (turret branch)    an item turret's bullets from the gun + R * (0, 0, 300) (48: x +-80 alternating,
//                                        181: z -300); the muzzle flash at the item's muzzle offset
//   PlayerTurret::handleTurret           an NPC turret's bullets 600 units ahead of its barrel
//   SentryGun::update                    a sentry's from its muzzle (0, 0, 250) (213: 300)
// Remake, for mods: an item turret's own model (WeaponFx.modTurret, a GLB: its muzzle / shot start), on a mount facing any
// way (WeaponMount.TurretTurn).

using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class TurretCore
    {
        const float M = Gun.MetersPerUnit;
        /// <summary>An item turret's pitch accumulator limits (units of 2 pi / 4096): 44 deg up, 6 deg down.</summary>
        public const float ItemPitchUp = 500f, ItemPitchDown = -70f;
        /// <summary>An NPC turret object's pitch counter limits: 52.7 deg up, 8.8 deg down.</summary>
        public const float StaticPitchUp = 600f, StaticPitchDown = -100f;
        /// <summary>An NPC turret's bullets start this far ahead of its barrel (units).</summary>
        public const float StaticShotAheadUnits = 600f;

        /// <summary>Where a shot starts: an item turret's gun-frame offset (ShotOffset), ahead of the barrel (NPC turret
        /// objects), or at the muzzle (sentries).</summary>
        public enum ShotFrom { GunFrame, AheadOfBarrel, Muzzle }

        /// <summary>The turret's model root (an item turret: on its mount).</summary>
        public Transform Root { get; private set; }
        public Transform Pivot { get; private set; }
        public Transform GunNode { get; private set; }
        /// <summary>The muzzle flash's parent (null for an NPC turret object, whose flash sits on the barrel).</summary>
        public Transform Muzzle { get; private set; }
        public TurretAim Aim { get; private set; }
        public Gun Gun { get; private set; }
        public GunRig Rig { get; private set; }
        /// <summary>The turret item (-1 = an NPC turret object or a sentry without one).</summary>
        public int Item { get; private set; } = -1;
        public ShotFrom shotFrom = ShotFrom.GunFrame;
        /// <summary>GunFrame: the shot's offset in the gun's frame, game units (x toward the pilot's left).</summary>
        public Vector3 shotOffsetUnits = new Vector3(0f, 0f, 300f);
        /// <summary>Item 48 (Hammerhead): the shots alternate between x +80 and -80.</summary>
        public bool alternateSides;
        bool alternate;
        // Remake: the muzzles shots leave from in turn (SetShotMuzzles), and the next one's index.
        Transform[] shotMuzzles;
        int nextShotMuzzle;
        /// <summary>Since the last shot that left the gun (ms); the owner adds the time (its own order).</summary>
        public float SinceShotMs = 1e9f;
        PartAnimation[] anims = new PartAnimation[0];
        /// <summary>The projectiles' and impacts' parent ForItem made (destroyed with the turret).</summary>
        Transform fxRoot;

        public Vector3 BarrelForward => Aim != null ? Aim.BarrelForward : Root != null ? Root.forward : Vector3.forward;
        public Vector3 BarrelUp => Aim != null ? Aim.BarrelUp : Root != null ? Root.up : Vector3.up;
        /// <summary>PlayerEgo::getTurretPosition: the gun's world position.</summary>
        public Vector3 GunPosition => GunNode != null ? GunNode.position : Root != null ? Root.position : Vector3.zero;
        /// <summary>The model's turning parts were found (else it can't aim or fire).</summary>
        public bool Valid => Aim != null;
        /// <summary>Shots went out within one reload (+50 ms) of now: the turret's own animations run only then.</summary>
        public bool RecentlyFired => Gun != null && SinceShotMs <= Gun.reloadMs + 50f;

        // ---- building ---------------------------------------------------------------------------------------------

        /// <summary>The yaw node ("pivot") and the gun node (the pivot's child named "*_gun", the last one) of a turret model.</summary>
        public static bool FindNodes(Transform model, out Transform pivot, out Transform gun)
        {
            gun = null;
            pivot = model != null ? model.Find("pivot") : null;
            if (pivot != null) foreach (Transform c in pivot) if (c.name.Contains("_gun")) gun = c;
            return pivot != null && gun != null;
        }

        /// <summary>The turret's model on its mount: the mount's position, and the mount's turn (WeaponMount.TurretTurn: its
        /// base facing turretUp, under the hull a half turn about the ship's length) on top of the model's own turn.</summary>
        public static void PlaceOnMount(Transform model, WeaponMount mount, Quaternion turn)
        {
            model.localPosition = WeaponSystem.MountToLocal(mount);
            model.localRotation = mount.TurretTurn * turn;
        }

        /// <summary>An item turret's aim (PlayerEgo::checkForTurret): yaw by attr 17, pitch -6 .. +44 deg; 'backwards' = the
        /// gun faces backwards in the pivot (the assemblies' (0, pi, 0)), so it starts turned to the nose.</summary>
        public static TurretAim ItemAim(Transform pivot, Transform gun, ItemData item, bool backwards) =>
            new TurretAim(pivot, gun, backwards ? Mathf.PI : 0f)
            {
                yawRatePerMs = item.Attr(17, 100) * 1.5f / 200f * TurretAim.RadPerMs,
                pitchRatePerMs = TurretAim.RadPerMs,
                pitchMin = ItemPitchDown * TurretAim.RadPerMs,
                pitchMax = ItemPitchUp * TurretAim.RadPerMs,
            };

        /// <summary>An NPC turret object's aim (PlayerTurret::handleRotation): -8.8 .. +52.7 deg.</summary>
        public static TurretAim StaticAim(Transform pivot, Transform gun) =>
            new TurretAim(pivot, gun) { pitchMax = StaticPitchUp * TurretAim.RadPerMs, pitchMin = StaticPitchDown * TurretAim.RadPerMs };

        /// <summary>The muzzle flash's distance along an item turret's gun (units): per item, a mod model's own.</summary>
        public static float MuzzleUnits(ItemData item, WeaponFx fx)
        {
            if (fx != null && fx.turretMuzzleUnits >= 0f) return fx.turretMuzzleUnits;
            return item.Look switch { 47 => 260f, 48 => 170f, 49 => 200f, 180 => 172f, 181 => 190f, 182 => 150f, 224 => 147f, _ => 200f };
        }

        /// <summary>Where an item turret's shot starts in its gun's frame (units): (0, 0, 300), 48 (80, 0, 300), 181 (0, 0,
        /// -300), a mod model's own z.</summary>
        public static Vector3 ShotOffsetUnits(ItemData item, WeaponFx fx)
        {
            if (fx != null && fx.turretShotUnits >= 0f) return new Vector3(0f, 0f, fx.turretShotUnits);
            return item.Look == 48 ? new Vector3(80f, 0f, 300f) : item.Look == 181 ? new Vector3(0f, 0f, -300f) : new Vector3(0f, 0f, 300f);
        }

        /// <summary>A turret item's model, aim, muzzle and gun on 'mount' under 'parent' (a ship's model): the item's
        /// *_ship_mounted assembly (a mod's own model), still until it fires. 'shipForward' (world) decides which way the gun
        /// starts. The gun has no owner, hit handler or ignore rule yet: the owner sets them. Null without the item's turret
        /// model; a model without pivot / gun returns a core that isn't Valid (logged).</summary>
        public static TurretCore ForItem(ItemData item, WeaponFx fx, Transform parent, WeaponMount mount, Transform fxRoot, string name, Vector3 shipForward)
        {
            if (item == null || fx == null || fx.turretMounted == null) return null;
            var model = Object.Instantiate(fx.turretMounted, parent, false);
            model.name = name;
            PlaceOnMount(model.transform, mount, model.transform.localRotation);
            return ForItemModel(item, fx, model, fxRoot, name, shipForward);
        }

        /// <summary>ForItem on a turret model already in place ('model', an instance of fx.turretMounted: an NPC turret object
        /// built from a turret item, World.NpcShip).</summary>
        public static TurretCore ForItemModel(ItemData item, WeaponFx fx, GameObject model, Transform fxRoot, string name, Vector3 shipForward)
        {
            GunRig.StripForFx(model);
            var core = new TurretCore { Root = model.transform, Item = item.index };
            core.anims = model.GetComponentsInChildren<PartAnimation>(true);
            core.SetAnimating(false);   // still until the first shot
            if (!FindNodes(model.transform, out var pivot, out var gunNode))
            {
                Debug.LogWarning("TurretCore: no pivot / gun in " + model.name);
                return core;
            }
            core.Pivot = pivot;
            core.GunNode = gunNode;
            core.Aim = ItemAim(pivot, gunNode, item, Vector3.Dot(gunNode.forward, shipForward) < 0f);
            core.Muzzle = new GameObject(name + " muzzle").transform;
            core.Muzzle.SetParent(gunNode, false);
            core.Muzzle.localPosition = new Vector3(0f, 0f, MuzzleUnits(item, fx)) * M;
            core.shotOffsetUnits = ShotOffsetUnits(item, fx);
            core.alternateSides = item.index == 48;
            var gun = new Gun(item, Vector3.zero, false);
            core.fxRoot = new GameObject(name + " fx").transform;
            if (fxRoot != null) core.fxRoot.SetParent(fxRoot, false);
            core.SetGun(gun, new GunRig(gun, fx, core.fxRoot, core.Muzzle));
            core.Rig.EnableTrails();   // only a mod turret firing missiles has one (RocketTrail.For)
            return core;
        }

        /// <summary>An existing model's turning parts (an NPC turret object, a sentry): 'aim' already made on them.</summary>
        public static TurretCore Wrap(Transform root, Transform pivot, Transform gunNode, TurretAim aim, ShotFrom from)
        {
            var core = new TurretCore { Root = root, Pivot = pivot, GunNode = gunNode, Aim = aim, shotFrom = from };
            if (root != null) core.anims = root.GetComponentsInChildren<PartAnimation>(true);
            return core;
        }

        /// <summary>The gun it fires (and its visuals); an NPC turret's SetGun swaps them.</summary>
        public void SetGun(Gun gun, GunRig rig)
        {
            Gun = gun;
            Rig = rig;
        }

        /// <summary>Remake: shots leave from these points in turn (ShotFrom.Muzzle): a twin-barrelled turret object fires from
        /// each barrel's tip (the Valkyrie battlestation's turrets, whose shots started inside the turret).</summary>
        public void SetShotMuzzles(params Transform[] muzzles)
        {
            shotMuzzles = muzzles != null && muzzles.Length > 0 ? muzzles : null;
            nextShotMuzzle = 0;
            if (shotMuzzles != null) shotFrom = ShotFrom.Muzzle;
        }

        /// <summary>Its muzzle (a sentry's: placed by the owner).</summary>
        public void SetMuzzle(Transform muzzle) => Muzzle = muzzle;

        /// <summary>The animations it takes part in (a sentry's model under its own nodes), looped or not.</summary>
        public void SetAnimations(PartAnimation[] list) => anims = list ?? new PartAnimation[0];

        /// <summary>The hangar's model of turret 'item' (CutScene::checkForTurret): the item's hangar_turret_item_N assembly
        /// (base + gun, the gun raised by its per-item offset), or a mod turret's own model ('own'). Null = none.</summary>
        public static GameObject DisplayPrefab(Database db, int item, out bool own)
        {
            var fx = WeaponFx.Load(item);
            own = fx != null && fx.modTurret && fx.turretMounted != null;
            return own ? fx.turretMounted : AssembledObject.LoadPrefab(db.AssemblyByName("hangar_turret_item_" + Modding.ModContent.ItemLook(item)));
        }

        /// <summary>CutScene::checkForTurret 0xa4594 (the hangar): turret 'item' on 'mount' under 'parent', still, turned
        /// (0, pi, 0) against the ship except the plasma collectors 198-200; a mod turret with its own model shows that model
        /// (as in flight). Null without a model.</summary>
        public static GameObject Display(Database db, int item, WeaponMount mount, Transform parent)
        {
            var prefab = DisplayPrefab(db, item, out bool own);
            if (prefab == null) return null;
            var model = Object.Instantiate(prefab, parent, false);
            model.name = "Turret";
            // A game-space turn relative to the ship: the import's 180 deg yaw cancels, (0, pi, 0) stays a half turn.
            PlaceOnMount(model.transform, mount, own ? prefab.transform.localRotation
                : Modding.ModContent.ItemLook(item) is >= 198 and <= 200 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f));
            if (own) GunRig.StripForFx(model);
            foreach (var a in model.GetComponentsInChildren<PartAnimation>(true)) a.speed = 0f;
            return model;
        }

        /// <summary>Every turret item of 'items' on its own turret mount of ship 'shipIndex' (the n-th on the n-th), still,
        /// under one "Turrets" object on 'shipModel'. Null without a mount or turret.</summary>
        public static GameObject DisplayAll(Database db, int shipIndex, System.Collections.Generic.IList<int> items, Transform shipModel)
        {
            if (db == null || items == null || items.Count == 0 || shipModel == null) return null;
            var mounts = db.MountsOf(shipIndex, 2);
            GameObject root = null;
            for (int m = 0; m < items.Count && m < mounts.Count; m++)
            {
                if (items[m] < 0 || DisplayPrefab(db, items[m], out _) == null) continue;   // < 0: an empty slot (PlayerTurret.TurretItems)
                if (root == null)
                {
                    root = new GameObject("Turrets");
                    root.transform.SetParent(shipModel, false);
                }
                Display(db, items[m], mounts[m], root.transform);
            }
            return root;
        }

        // ---- running ----------------------------------------------------------------------------------------------

        /// <summary>One aiming step toward 'world' (TurretAim.Step); true when aligned.</summary>
        public bool StepToward(Vector3 world, float dtMs)
        {
            blockedStale = true;   // the barrel turned: Blocked looks again
            return Aim.Step(world, dtMs);
        }

        /// <summary>Where a shot with gun-frame offset 'off' (units) starts (world).</summary>
        public Vector3 ShotOrigin(Vector3 off)
        {
            switch (shotFrom)
            {
                case ShotFrom.AheadOfBarrel: return GunNode.position + GunNode.forward * StaticShotAheadUnits * M;
                case ShotFrom.Muzzle:
                    if (shotMuzzles != null && shotMuzzles[nextShotMuzzle] != null) return shotMuzzles[nextShotMuzzle].position;
                    return Muzzle != null ? Muzzle.position : GunNode.position;
                // The gun frame carries the turret's scale (a turret item as a capital turret object at x6): so does the offset.
                default: return GunNode.TransformPoint(new Vector3(-off.x, off.y, off.z) * M);
            }
        }

        /// <summary>Remake (LineOfFire): the ship this turret sits on; its hull (or, without hull meshes, its collision volumes)
        /// blocks the turret's fire. Null = nothing to block (a sentry gun).</summary>
        public Transform Host
        {
            get => host;
            set { host = value; hostBody = null; hostObstacle = null; hostLooked = false; blockedStale = true; }
        }
        Transform host;
        HullBody hostBody;
        Obstacle hostObstacle;
        bool hostLooked;

        /// <summary>The barrel points into its own ship (the owners drop such a target like an unreachable one; Fire holds
        /// its shot). Modified (review of #168): worked out when asked, once a frame and again after an aiming step; it was
        /// only set by a Fire, which the owners stopped calling once it was set, so a turret blocked once stayed silent.</summary>
        public bool Blocked
        {
            get
            {
                if (blockedFrame != Time.frameCount || blockedStale)
                {
                    blocked = IntoOwnHull();
                    blockedFrame = Time.frameCount;
                    blockedStale = false;
                }
                return blocked;
            }
        }
        bool blocked, blockedStale = true;
        int blockedFrame = -1;

        bool IntoOwnHull()
        {
            if (host == null || GunNode == null) return false;
            if (!hostLooked || hostBody == null && hostObstacle == null)
            {
                hostLooked = true;
                hostBody = host.GetComponentInChildren<HullBody>();
                hostObstacle = host.GetComponentInChildren<Obstacle>();
            }
            var from = GunNode.position;
            var dir = BarrelForward;
            if (hostBody != null && hostBody.Active) return HullCollision.RayHits(from, dir, 30000f, hostBody, Root);
            return hostObstacle != null && hostObstacle.RayInto(from, dir);
        }

        /// <summary>Fires along the barrel when the gun is ready: the bullet's index, else -1. Starts the animations' clock.
        /// Item 48 switches sides at every try (ObjectGun::update), shot or not.</summary>
        public int Fire()
        {
            if (Gun == null || GunNode == null) return -1;
            // Remake (LineOfFire): no shot into its own ship's hull (a turret on a capital ship's far side shot the player
            // straight through it: shots pass their own side's hulls).
            if (Gun.Ready && Blocked) return -1;
            var off = shotOffsetUnits;
            if (alternateSides) { off.x = alternate ? -80f : 80f; alternate = !alternate; }
            int b = Gun.TryFire(ShotOrigin(off), Quaternion.LookRotation(BarrelForward, BarrelUp), false);
            if (b >= 0)
            {
                Rig?.OnShot(shotMuzzles != null ? nextShotMuzzle : -1);   // remake: the flash at the barrel that fired
                SinceShotMs = 0f;
                if (shotMuzzles != null) nextShotMuzzle = (nextShotMuzzle + 1) % shotMuzzles.Length;   // the other barrel next
            }
            return b;
        }

        /// <summary>The gun's bullets fly and its visuals follow (Gun.Update + GunRig.UpdateVisuals).</summary>
        public void Tick(float dtMs, System.Collections.Generic.IReadOnlyList<Target> targets, Target homing)
        {
            if (Gun == null) return;
            Gun.Update(dtMs, targets, homing);
            Rig?.UpdateVisuals(dtMs, Camera.main, BarrelForward);
        }

        /// <summary>The model's own animations (the auto turrets' spinning barrels) run or stand still.</summary>
        public void SetAnimating(bool on)
        {
            foreach (var a in anims) if (a != null) a.speed = on ? 1f : 0f;
        }

        /// <summary>The model and its effects go (a hull swap, a turret item taken off).</summary>
        public void Destroy()
        {
            Rig?.HideAll();
            if (fxRoot != null) Object.Destroy(fxRoot.gameObject);
            if (Root != null) Object.Destroy(Root.gameObject);
        }
    }
}
