// PlayerTurret.cs
// The turret on the player's ship (Reference/research/weapons_special.md 6): the turret-slot item (category 8) on the
// ship's slot-2 mount (weapons_hd.json), as its assembled *_ship_mounted prefab (pivot -> base + gun).
//   PlayerEgo::checkForTurret 0xa722c   yaw rate = attr17 * 1.5 / 200 * 2 pi / 4096 rad per ms, pitch 2 pi / 4096 rad per ms
//                                       limited to [-500, +70] * 2 pi / 4096 on the gun's pitch accumulator, whose positive
//                                       turn about X dips the barrel: 44 deg up, 6 deg down (#9, #17: it was the reverse)
//   PlayerEgo::handleAutoTurret 0xa8ae0 180-182 (attr 16 = 1) aim and fire by themselves: every 3000 ms the nearest hostile,
//                                       active ship without KIPlayer+0x70 (radar-hidden, as a cloak makes it) within 60000
//                                       units (the last unreachable one skipped), aim point = its
//                                       position + its heading * 1500 (a), fire when aligned (all turret guns on their own
//                                       reload); 500 ms without a shot stops the loop sound. Independent of the primaries.
//                                       Toggled with the HUD's auto-turret button (on at the start).
//   MGame::switchCamera / PlayerEgo::setTurretMode  the manual turrets (47-49, 224) work only in the turret view: the
//                                       camera button cycles chase -> turret view; the ship flies straight, the stick
//                                       aims the turret (360 deg yaw), the fire button fires it, primaries and secondaries
//                                       are silent. Auto turrets can't enter the view.
//   ObjectGun::update (turret branch)   bullets from the turret gun + R * (0, 0, 300) (48: x +-80 alternating, 181: z -300),
//                                       the muzzle flash at the gun's muzzle offset; the shot sound loops while firing.
//                                       The turret's own animations (the auto turrets' spinning barrels) only advance
//                                       while it fires and stand still otherwise.
// Remake: the turret view key is V / controller D-pad up / the touch turret button; auto-fire toggles with T / D-pad
// down / the same touch button. The turret camera's offsets were lost in the decompile (a: above and behind the turret).
// Plasma collectors (198-200, sort 35; weapons_special.md 6.5): the same turret on the same mount and view, but no gun: in
// the turret view the plasma stream (sn_plasma_stream_anim_add under the gun) shows and GasCloudField pulls the sparks in
// sight toward the turret (attr 49 u/ms, within attr 51 units).
// Remake: several turrets (custom ships with more turret slots and slot-2 mounts): one PlayerTurret per mounted turret item,
// the n-th turret item of the equipment on the n-th slot-2 mount (AttachAll). They share one switch for auto fire (SetAuto,
// the HUD's announcement on the first turret) and one turret view at a time (the camera button steps through the manual
// ones, FreeLookCamera). A mount with 'upsideDown' hangs its turret under the hull (turned 180 deg about the ship's length):
// its turret camera stays upright and sits under it, and the stick is mirrored so it aims as seen in that view.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    public class PlayerTurret : MonoBehaviour
    {
        const float M = Gun.MetersPerUnit;
        const float PickMs = 3000f, IdleStopMs = 500f, AutoRangeUnits = 60000f, LeadUnits = 1500f;
        const float PitchUpUnits = 500f, PitchDownUnits = -70f;

        // The controls are GameControls' (rebindable): Camera (the turret view, T / D-pad up), AutoTurret (Y / D-pad down).
        static InputAction viewAction => GameControls.Camera;
        static InputAction autoAction => GameControls.AutoTurret;

        /// <summary>180-182: aims and fires by itself.</summary>
        public bool IsAuto { get; private set; }
        /// <summary>A plasma collector (sort 35): collects, never fires.</summary>
        public bool IsCollector { get; private set; }
        /// <summary>Collectors: attr 49 (pull speed, u/ms) and attr 51 (range, units).</summary>
        public int PullSpeed { get; private set; }
        public int CollectRange { get; private set; }
        /// <summary>PlayerEgo::getTurretPosition: the gun's world position (Unity).</summary>
        public Vector3 GunPosition => muzzle != null && muzzle.parent != null ? muzzle.parent.position : transform.position;
        /// <summary>The turret's aim (world), for the collector's scope.</summary>
        public Vector3 AimForward => aim != null ? aim.BarrelForward : transform.forward;
        public bool AutoEnabled { get; private set; } = true;
        public bool InTurretView { get; private set; }
        public int Item { get; private set; } = -1;
        /// <summary>Remake: which slot-2 mount it sits on (0 = the first) and whether that mount hangs it under the hull.</summary>
        public int MountIndex { get; private set; }
        public bool UpsideDown { get; private set; }
        bool removed;
        /// <summary>A toggle message for the HUD (text 37 / remake strings).</summary>
        public event Action<string> Message;

        ShipController ship;
        WeaponSystem weapons;
        ChaseCamera chase;
        Gun gun;
        /// <summary>The turret's gun (multiplayer's shot mirrors), null before it is set up.</summary>
        public Gun Gun => gun;
        GunRig rig;
        TurretAim aim;
        Transform muzzle, camAnchor;
        AudioSource loop;
        Target target, unreachable;
        float pickMs = PickMs, idleMs;
        bool alternate;
        Vector3 bulletOffset;
        Visuals.PartAnimation[] anims;
        GameObject stream;
        AudioSource collectLoop;

        /// <summary>Level::createPlayer: the turret items of the current equipment on the ship's turret mounts, in order (the
        /// original has one; remake: as many as the ship has slot-2 mounts). Empty without a mount or turret.</summary>
        public static List<PlayerTurret> AttachAll(GameObject player, Database db, int shipIndex, IList<ItemStack> equipment, ChaseCamera chase)
        {
            var list = new List<PlayerTurret>();
            var mounts = db.MountsOf(shipIndex, 2);
            int m = 0;
            foreach (int item in TurretItems(db, equipment))
            {
                if (m >= mounts.Count) break;
                var it = db.Item(item);
                var fx = WeaponFx.Load(it.index);
                if (fx == null || fx.turretMounted == null) continue;
                var t = player.AddComponent<PlayerTurret>();
                t.Setup(it, fx, mounts[m], m, chase);
                list.Add(t);
                m++;
            }
            return list;
        }

        /// <summary>Removes the turrets from the player (a hull swap); they leave the group at once, before Unity destroys them.</summary>
        public static void RemoveAll(IList<PlayerTurret> turrets)
        {
            if (turrets == null) return;
            foreach (var t in turrets)
                if (t != null)
                {
                    if (t.InTurretView) t.SetTurretView(false);
                    t.removed = true;
                    Destroy(t);
                }
        }

        /// <summary>The turrets on this player's ship, in mount order.</summary>
        public static List<PlayerTurret> On(GameObject player)
        {
            var list = new List<PlayerTurret>();
            if (player != null) foreach (var t in player.GetComponents<PlayerTurret>()) if (t != null && !t.removed) list.Add(t);
            return list;
        }

        /// <summary>The turret the HUD and the camera talk about: the one in the turret view, else the first.</summary>
        public static PlayerTurret Current(GameObject player)
        {
            var all = On(player);
            foreach (var t in all) if (t.InTurretView) return t;
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>The turret-slot item (category 8 / 35) of this equipment, or -1 (the first one).</summary>
        public static int TurretItem(Database db, IList<ItemStack> equipment)
        {
            var items = TurretItems(db, equipment);
            return items.Count > 0 ? items[0] : -1;
        }

        /// <summary>Every turret-slot item (category 8 / 35) of this equipment, in equipment (= mount) order.</summary>
        public static List<int> TurretItems(Database db, IList<ItemStack> equipment)
        {
            var list = new List<int>();
            if (db == null || equipment == null) return list;
            foreach (var e in equipment)
            {
                if (e == null) continue;
                var it = db.Item(e.item);
                if (it != null && (it.categoryId == 8 || it.categoryId == 35)) list.Add(it.index);
            }
            return list;
        }

        /// <summary>The turret's model on its mount: the mount's position, and under the hull a half turn about the ship's
        /// length (local z) on top of the model's own turn.</summary>
        static void PlaceOnMount(Transform model, WeaponMount mount, Quaternion turn)
        {
            model.localPosition = WeaponSystem.MountToLocal(mount);
            model.localRotation = mount.upsideDown ? Quaternion.Euler(0f, 0f, 180f) * turn : turn;
        }

        /// <summary>CutScene::checkForTurret 0xa4594 (the hangar): the item's hangar_turret_item_N assembly (base + gun, the gun
        /// raised by its per-item offset) on the ship's slot-2 mount, turned (0, pi, 0) against the ship except the plasma
        /// collectors 198-200; still. Re-run after equipment changes. Null without a mount or turret. Remake: every turret
        /// item on its own mount (the n-th on the n-th, upside down where the mount says so), under one "Turrets" object.</summary>
        public static GameObject BuildStatic(Database db, int shipIndex, IList<ItemStack> equipment, Transform shipModel)
        {
            var mounts = db.MountsOf(shipIndex, 2);
            var items = TurretItems(db, equipment);
            if (mounts.Count == 0 || items.Count == 0) return null;
            GameObject root = null;
            for (int m = 0; m < items.Count && m < mounts.Count; m++)
            {
                int item = items[m];
                var prefab = Visuals.AssembledObject.LoadPrefab(db.AssemblyByName("hangar_turret_item_" + item));
                if (prefab == null) continue;
                if (root == null)
                {
                    root = new GameObject("Turrets");
                    root.transform.SetParent(shipModel, false);
                }
                var model = Instantiate(prefab, root.transform, false);
                model.name = "Turret";
                // A game-space turn relative to the ship: the import's 180 deg yaw cancels, (0, pi, 0) stays a half turn.
                PlaceOnMount(model.transform, mounts[m], item >= 198 && item <= 200 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f));
                foreach (var a in model.GetComponentsInChildren<Visuals.PartAnimation>(true)) a.speed = 0f;
            }
            return root;
        }

        void Setup(ItemData item, WeaponFx fx, WeaponMount mount, int mountIndex, ChaseCamera chaseCamera)
        {
            ship = GetComponent<ShipController>();
            weapons = GetComponent<WeaponSystem>();
            chase = chaseCamera;
            Item = item.index;
            MountIndex = mountIndex;
            UpsideDown = mount.upsideDown;
            IsAuto = item.Attr(16) == 1;
            IsCollector = item.categoryId == 35;
            PullSpeed = item.Attr(49);
            CollectRange = item.Attr(51);
            // On the ship's model, so it banks and tumbles (death) with the hull.
            var parent = ship != null && ship.visualModel != null ? ship.visualModel : transform;
            var model = Instantiate(fx.turretMounted, parent, false);
            model.name = mountIndex == 0 ? "Turret" : "Turret " + (mountIndex + 1);
            PlaceOnMount(model.transform, mount, model.transform.localRotation);
            GunRig.StripForFx(model);
            anims = model.GetComponentsInChildren<Visuals.PartAnimation>(true);
            foreach (var a in anims) a.speed = 0f;   // still until the first shot
            if (IsCollector)
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                    if (t.name.Contains("plasma_stream")) { stream = t.gameObject; stream.SetActive(false); }
            var pivot = model.transform.Find("pivot");
            Transform gunNode = null;
            if (pivot != null) foreach (Transform c in pivot) if (c.name.Contains("_gun")) gunNode = c;
            if (pivot == null || gunNode == null) { Debug.LogWarning("PlayerTurret: no pivot / gun in " + fx.turretMounted.name); enabled = false; return; }
            // The gun faces backwards in the pivot (the assembly's (0, pi, 0)); start turned to the ship's nose.
            bool backwards = Vector3.Dot(gunNode.forward, transform.forward) < 0f;
            aim = new TurretAim(pivot, gunNode, backwards ? Mathf.PI : 0f)
            {
                yawRatePerMs = item.Attr(17, 100) * 1.5f / 200f * TurretAim.RadPerMs,
                pitchRatePerMs = TurretAim.RadPerMs,
                pitchMin = PitchDownUnits * TurretAim.RadPerMs,
                pitchMax = PitchUpUnits * TurretAim.RadPerMs,
            };
            // Muzzle flash at the gun's muzzle offset; bullets from the gun + (0, 0, 300) (48: x 80, 181: z -300).
            float muzzleZ = item.index switch { 47 => 260f, 48 => 170f, 49 => 200f, 180 => 172f, 181 => 190f, 182 => 150f, 224 => 147f, _ => 200f };
            muzzle = new GameObject(model.name + " muzzle").transform;
            muzzle.SetParent(gunNode, false);
            muzzle.localPosition = new Vector3(0f, 0f, muzzleZ) * M;
            bulletOffset = item.index == 48 ? new Vector3(80f, 0f, 300f) : item.index == 181 ? new Vector3(0f, 0f, -300f) : new Vector3(0f, 0f, 300f);
            gun = new Gun(item, Vector3.zero, false) { owner = GetComponent<Target>(), Ignores = t => t.playerProof };   // multiplayer: through squadmates
            var fxRoot = new GameObject(model.name + " fx").transform;
            rig = new GunRig(gun, fx, fxRoot, muzzle);
            gun.Hit += OnHit;
            if (fx.shot != null)
            {
                loop = gameObject.AddComponent<AudioSource>();
                loop.clip = fx.shot;
                loop.loop = fx.shotLoops;
                loop.playOnAwake = false;
                loop.spatialBlend = 0f;
            }
            camAnchor = new GameObject(model.name + " camera").transform;
            camAnchor.SetParent(pivot, false);
            camAnchor.localPosition = gunNode.localPosition;
        }

        void OnDestroy()
        {
            if (InTurretView) SetTurretView(false);
        }

        /// <summary>The touch turret button: toggles auto-fire, or the turret view for a manual turret. Remake, with several
        /// turrets: any auto turret on the ship -> auto fire for all of them; else the view steps to the next manual turret
        /// and then back out.</summary>
        public void Toggle()
        {
            var group = On(gameObject);
            var firstAuto = group.Find(t => t.IsAuto);
            if (firstAuto != null) { firstAuto.SetAuto(!firstAuto.AutoEnabled); return; }
            var manual = group.FindAll(t => !t.IsAuto);
            int at = manual.FindIndex(t => t.InTurretView);
            if (at < 0) { if (manual.Count > 0) manual[0].SetTurretView(true); }
            else if (at + 1 < manual.Count) manual[at + 1].SetTurretView(true);
            else manual[at].SetTurretView(false);
        }

        /// <summary>MGame::OnTouchEnd auto-turret button: HUD event 0x20 / 0x21. Remake: one switch for every auto turret on the
        /// ship, announced once (on the first turret, the one the HUD listens to).</summary>
        public void SetAuto(bool on, bool announce = true)
        {
            if (!IsAuto) return;
            var group = On(gameObject);
            bool changed = false;
            foreach (var t in group)
            {
                if (!t.IsAuto || t.AutoEnabled == on) continue;
                t.AutoEnabled = on;
                if (!on) t.StopShooting();
                changed = true;
            }
            if (!changed) return;
            var herald = group.Count > 0 ? group[0] : this;
            if (announce) herald.Message?.Invoke(Localization.Get(37) + (on ? ": " + Localization.Extra("on", "On") : ": " + Localization.Extra("off", "Off")));
        }

        /// <summary>PlayerEgo::setTurretMode: refused for auto turrets, while mining or while the guns are blocked. Remake:
        /// one turret view at a time (entering this one leaves another's).</summary>
        public void SetTurretView(bool on)
        {
            if (on && (IsAuto || weapons == null || weapons.Blocked || Time.timeScale <= 0f)) return;
            if (InTurretView == on) return;
            if (on) foreach (var other in On(gameObject)) if (other != this && other.InTurretView) other.SetTurretView(false);
            InTurretView = on;
            if (weapons != null) weapons.TurretView = on;
            if (ship != null) ship.steeringLocked = on;
            if (chase != null)
            {
                chase.follow = on ? camAnchor : null;
                // Under the hull the camera hangs below the turret (still upright: the anchor keeps the ship's up).
                float side = UpsideDown ? -1f : 1f;
                chase.followOffset = new Vector3(0f, 6f * side, -14f);
                chase.followLookOffset = new Vector3(0f, 2f * side, 40f);
                chase.followRigid = true;
                chase.followUsesUp = true;
                if (!on) chase.Snap();
            }
            if (!on) StopShooting();
            if (stream != null) stream.SetActive(on);
            if (IsCollector)
            {
                // PlayerEgo::setTurretMode: 2255 (Extractor_Loop_01, event volume 0.047) loops while collecting.
                if (collectLoop == null)
                {
                    collectLoop = gameObject.AddComponent<AudioSource>();
                    collectLoop.playOnAwake = false; collectLoop.loop = true; collectLoop.spatialBlend = 0f;
                    collectLoop.clip = SupernovaAssets.Load()?.extractorLoop;
                }
                collectLoop.volume = 0.047f * Sfx.EventGain * Settings.SfxVolume;
                if (on && collectLoop.clip != null) collectLoop.Play(); else collectLoop.Stop();
            }
        }

        void Update()
        {
            if (aim == null) return;
            float dtMs = Time.deltaTime * 1000f;
            if (gun.owner == null) gun.owner = GetComponent<Target>();   // the player's Target comes after the turret
            bool halted = Time.timeScale <= 0f || (weapons != null && weapons.Blocked);
            if (!halted)
            {
                // The view key (V / D-pad up) is the camera button now: FreeLookCamera cycles standard / turret / free look.
                // With several turrets the first manual / auto one answers the keys for all of them (Toggle / SetAuto).
                var group = On(gameObject);
                if (viewAction.WasPressedThisFrame() && !IsAuto && GetComponent<FreeLookCamera>() == null && group.Find(t => !t.IsAuto) == this)
                {
                    if (group.FindAll(t => !t.IsAuto).Count > 1) Toggle(); else SetTurretView(!InTurretView);
                }
                if (autoAction.WasPressedThisFrame() && IsAuto && group.Find(t => t.IsAuto) == this) SetAuto(!AutoEnabled);
            }
            bool dead = GetComponent<Target>() is Target me && !me.Alive;
            if (InTurretView && ((weapons != null && weapons.Blocked) || dead)) SetTurretView(false);
            if (dead) { StopShooting(); foreach (var a in anims) if (a != null) a.speed = 0f; gun.Update(dtMs, Target.All, null); rig.UpdateVisuals(dtMs, Camera.main, aim.BarrelForward); return; }

            bool fire = false;
            if (!halted && InTurretView)
            {
                // Upside down the turret's own axes point the other way round than the upright view's: mirror the stick.
                var stick = ship != null ? ship.SteerInput : Vector2.zero;
                aim.Drive(UpsideDown ? -stick : stick, dtMs);
                // The camera turns with the turret, at half the gun's pitch.
                camAnchor.rotation = Quaternion.LookRotation(Vector3.Slerp(Vector3.ProjectOnPlane(aim.BarrelForward, transform.up), aim.BarrelForward, 0.5f), transform.up);
                fire = weapons != null && weapons.FireHeld && !IsCollector;   // a collector collects by aiming (GasCloudField)
                if (IsCollector) foreach (var a in anims) if (a != null) a.speed = 1f;
            }
            else if (!halted && IsAuto && AutoEnabled && GetComponent<Target>() is Target self && self.Alive)
                fire = AutoAim(dtMs);

            if (fire)
            {
                var off = bulletOffset;
                if (Item == 48) { off.x = alternate ? -80f : 80f; alternate = !alternate; }
                int b = gun.TryFire(muzzle.parent.TransformPoint(new Vector3(-off.x, off.y, off.z) * M), Quaternion.LookRotation(aim.BarrelForward, aim.BarrelUp), false);
                if (b >= 0)
                {
                    rig.OnShot();
                    idleMs = 0f;
                    if (loop != null && (!loop.isPlaying || !loop.loop)) { loop.volume = 0.7f * Settings.SfxVolume; loop.Play(); }
                }
            }
            idleMs += dtMs;
            if (idleMs > IdleStopMs && loop != null && loop.loop && loop.isPlaying) loop.Stop();
            // The animations run only while shots are going out (within one reload of the last shot).
            float animSpeed = idleMs <= gun.reloadMs + 50f || (IsCollector && InTurretView) ? 1f : 0f;
            foreach (var a in anims) if (a != null) a.speed = animSpeed;
            gun.Update(dtMs, Target.All, null);
            rig.UpdateVisuals(dtMs, Camera.main, aim.BarrelForward);
        }

        /// <summary>handleAutoTurret: re-pick every 3 s, turn toward the lead point, fire when aligned.</summary>
        bool AutoAim(float dtMs)
        {
            pickMs += dtMs;
            if (pickMs >= PickMs || target == null || !target.Alive || !target.isActiveAndEnabled)
            {
                pickMs = 0f;
                target = PickTarget();
            }
            if (target == null) return false;
            var at = target.transform.position + target.transform.forward * LeadUnits * M;
            bool aligned = aim.Step(at, dtMs);
            if (aim.LimitHit) { unreachable = target; target = null; pickMs = PickMs; return false; }
            return aligned;
        }

        Target PickTarget()
        {
            Target best = null;
            float bestD = AutoRangeUnits * M;
            foreach (var t in Target.All)
            {
                if (t == null || !t.isShip || !t.hostileToPlayer || !t.Alive || t == unreachable || t.untargetable || !t.isActiveAndEnabled) continue;
                if (t.GetComponent<World.NpcShip>() is World.NpcShip npc && npc.RadarHidden) continue;   // KIPlayer+0x70: cloaked too
                float d = (t.transform.position - transform.position).magnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            if (best == null) unreachable = null;
            return best;
        }

        void OnHit(int bullet, Target t, Vector3 point)
        {
            t.Damage(gun.damage, false, gun.bullets[bullet].velocity);
            rig.ShowImpact(point);
        }

        void StopShooting()
        {
            if (loop != null && loop.isPlaying) loop.Stop();
        }
    }
}
