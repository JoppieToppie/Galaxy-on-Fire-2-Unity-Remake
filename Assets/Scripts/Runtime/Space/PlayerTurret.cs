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
// ones, FreeLookCamera). A mount's 'turretUp' (or 'upsideDown') turns its turret to face any way from the hull: the sides,
// the nose, under it (WeaponMount.TurretTurn). The turret view's camera sits on the turret's outer side with the turret's up
// as its up, so the stick turns it as seen; a turret facing below the ship's horizon has the camera the other way up (as
// the old under-hull mounts: upright, under it) and the stick mirrored.
// Remake, for mods: a turret item with its own model (items.json fx "turret", WeaponFx.modTurret) shows it in flight and in
// the hangar; "missiles" (attr 105) make its shots homing missiles on the target it aims at (an auto turret's pick, else
// the radar's lock), with the smoke trail; "turretRange" (attr 109) is how far an auto turret looks for a target.
// The turret's model, aim, gun and shots are TurretCore's, its auto pick TurretTargeting's (Retarget), shared with the NPC
// turrets and the sentry guns; this keeps the player's own: the turret view, the auto switch, collectors, the loop sound.
// Remake mods: a mount marked 'builtIn' (a customizable ship's turret socket, Modding.ModShipKits) takes no turret model of
// the game: the turret item keeps its gun, stats, turn rates, auto or manual aiming and turret view, with an invisible pivot
// at the mount that the shots and the muzzle flash come from (the hangar and other players' ships show no model either).
// A plasma collector there keeps only its stream (sn_plasma_stream_anim_add, lifted out of the item's model onto the
// stand-in gun at the mount, aimed and shown in the turret view like the model's). A kit with a turret model (ModHardpoints)
// shows that instead of the stand-in: its pivot turns, its gun tilts, the shots and the stream leave from its muzzle
// (TurretCore.ForItemModel on it).
// Remake, for mods: with missile defense mounted (equipment with "missileDefense", PointDefense) an auto turret turns to a
// homing missile flying at the ship within that reach before its ship target, and its shots destroy it.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    public class PlayerTurret : MonoBehaviour
    {
        const float IdleStopMs = 500f;
        /// <summary>How far an auto turret looks for a target (units) unless the item's attr 109 says otherwise (mods).</summary>
        public const float AutoRangeUnits = 60000f;

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
        public Vector3 GunPosition => core != null && core.Valid ? core.GunPosition : transform.position;
        /// <summary>The turret's aim (world), for the collector's scope.</summary>
        public Vector3 AimForward => core != null && core.Valid ? core.BarrelForward : transform.forward;
        public bool AutoEnabled { get; private set; } = true;
        float defenseReachMeters;   // PointDefense: the mounted missile defense's reach, 0 = none
        bool defending;             // this frame's aim is at a missile ...
        IncomingMissiles.Missile missile;   // ... this one
        float missileFlightMs;      // ... its shot's flight time
        public bool InTurretView { get; private set; }
        public int Item { get; private set; } = -1;
        /// <summary>Remake: which slot-2 mount it sits on (0 = the first) and whether its base faces below the ship's horizon
        /// (WeaponMount.TurretHangs: the turret view's camera hangs under it, upright).</summary>
        public int MountIndex { get; private set; }
        public bool Hangs { get; private set; }
        /// <summary>The turret model's root on its mount: its up is the turret's yaw axis.</summary>
        Transform mountRoot;
        bool removed;
        /// <summary>A toggle message for the HUD (text 37 / remake strings).</summary>
        public event Action<string> Message;
        /// <summary>One of this turret's shots hit (the HUD's orange crosshair, Level+0x30, aimed by hand or auto fire).</summary>
        public event Action Hit;

        ShipController ship;
        WeaponSystem weapons;
        ChaseCamera chase;
        TurretCore core;
        /// <summary>The turret's moving parts (TurretCore), null before it is set up.</summary>
        public TurretCore Core => core;
        /// <summary>The turret's gun (multiplayer's shot mirrors), null before it is set up.</summary>
        public Gun Gun => core?.Gun;
        Gun gun => core.Gun;
        TurretAim aim => core.Aim;
        Transform camAnchor;
        AudioSource loop;
        /// <summary>The auto turret's pick (PlayerEgo::handleAutoTurret): attr 109 (mods), else the original's 60000 units.</summary>
        TurretTargeting picking;
        Target target => picking?.Current;
        /// <summary>Remake mods: the kit hardpoint's sound for this turret (Modding.ModShipSounds), null = the item's own.</summary>
        Modding.ModShipSounds.WeaponSound modSound;
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
                if (fx == null || (fx.turretMounted == null && !mounts[m].builtIn)) continue;
                var t = player.AddComponent<PlayerTurret>();
                t.Setup(it, fx, mounts[m], m, chase, shipIndex);
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

        /// <summary>CutScene::checkForTurret 0xa4594 (the hangar): the item's hangar_turret_item_N assembly (base + gun, the gun
        /// raised by its per-item offset) on the ship's slot-2 mount, turned (0, pi, 0) against the ship except the plasma
        /// collectors 198-200; still. Re-run after equipment changes. Null without a mount or turret. Remake: every turret
        /// item on its own mount (the n-th on the n-th, upside down where the mount says so), under one "Turrets" object.
        /// Shared with the other players' ships and the NPC ships in the hangar (TurretCore.Display). Remake mods: 'shown' is
        /// the build the model shows (another player's kit ship; null = Database.MountsOf, the local player's); a built-in
        /// mount shows its kit's turret model (ModHardpoints), else nothing.</summary>
        public static GameObject BuildStatic(Database db, int shipIndex, IList<ItemStack> equipment, Transform shipModel, Modding.ModShipKits.Build shown = null)
        {
            var mounts = shown != null ? Modding.ModShipKits.MountsShown(db, shipIndex, 2, shown) : db.MountsOf(shipIndex, 2);
            bool builtIn = false;
            foreach (var m in mounts) if (m.builtIn) builtIn = true;
            if (shown == null && !builtIn) return TurretCore.DisplayAll(db, shipIndex, TurretItems(db, equipment), shipModel);
            var items = TurretItems(db, equipment);
            if (mounts.Count == 0 || items.Count == 0) return null;
            GameObject root = null;
            for (int m = 0; m < items.Count && m < mounts.Count; m++)
            {
                int item = items[m];
                var kitTurret = mounts[m].builtIn ? Modding.ModHardpoints.TurretModel(shipIndex, db.Item(item), shown) : null;
                if (mounts[m].builtIn && kitTurret == null) continue;
                if (!mounts[m].builtIn && TurretCore.DisplayPrefab(db, item, out _) == null) continue;
                if (root == null)
                {
                    root = new GameObject("Turrets");
                    root.transform.SetParent(shipModel, false);
                }
                if (kitTurret != null)
                {
                    kitTurret.transform.SetParent(root.transform, false);
                    TurretCore.PlaceOnMount(kitTurret.transform, mounts[m], Quaternion.identity);
                    foreach (var t in kitTurret.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = shipModel.gameObject.layer;
                    continue;
                }
                TurretCore.Display(db, item, mounts[m], root.transform);
            }
            return root;
        }

        void Setup(ItemData item, WeaponFx fx, WeaponMount mount, int mountIndex, ChaseCamera chaseCamera, int shipIndex)
        {
            ship = GetComponent<ShipController>();
            weapons = GetComponent<WeaponSystem>();
            chase = chaseCamera;
            Item = item.index;
            MountIndex = mountIndex;
            Hangs = mount.TurretHangs;
            IsAuto = item.Attr(16) == 1;
            defenseReachMeters = PointDefense.ReachUnits(Database.Shared) * Gun.MetersPerUnit;
            IsCollector = item.categoryId == 35;
            PullSpeed = item.Attr(49);
            CollectRange = item.Attr(51);
            picking = new TurretTargeting(TurretTargeting.Rule.Retarget, item.Attr(109) > 0 ? item.Attr(109) : AutoRangeUnits, true)
            {
                Candidate = t => t.isShip && t.hostileToPlayer && t.Alive && !t.untargetable && t.isActiveAndEnabled
                                 && !t.cloaked   // a cloaked ship (Target.cloaked, NpcShip: a Specter past a quarter of its fade)
                                 && !(t.GetComponent<World.NpcShip>() is World.NpcShip npc && npc.RadarHidden),   // KIPlayer+0x70
            };
            // On the ship's model, so it banks and tumbles (death) with the hull. The gun faces backwards in the pivot (the
            // assembly's (0, pi, 0)): it starts turned to the ship's nose (TurretCore.ForItem).
            var parent = ship != null && ship.visualModel != null ? ship.visualModel : transform;
            string name = mountIndex == 0 ? "Turret" : "Turret " + (mountIndex + 1);
            // A built-in mount (a kit's turret socket): the kit's turret model (ModHardpoints), else an invisible stand-in.
            var kitModel = mount.builtIn ? Modding.ModHardpoints.TurretModel(shipIndex, item) : null;
            if (mount.builtIn)
            {
                var model = kitModel != null ? kitModel : BuiltInTurret(IsCollector ? fx.turretMounted : null);
                model.transform.SetParent(parent, false);
                model.name = name;
                TurretCore.PlaceOnMount(model.transform, mount, model.transform.localRotation);
                core = TurretCore.ForItemModel(item, fx, model, null, name, transform.forward);
            }
            else core = TurretCore.ForItem(item, fx, parent, mount, null, name, transform.forward);
            mountRoot = core.Root;
            if (IsCollector)
                foreach (Transform t in core.Root.GetComponentsInChildren<Transform>(true))
                    // The stream node itself, not its mesh parts (their names contain it too): hiding every match and keeping
                    // the last one switched only "_part1" back on in the turret view, under a parent still hidden.
                    if (stream == null && t.name.Contains("plasma_stream")) { stream = t.gameObject; stream.SetActive(false); }
            if (!core.Valid) { enabled = false; return; }
            core.Host = transform;   // no shots into its own ship (LineOfFire)
            gun.owner = GetComponent<Target>();
            gun.Ignores = t => t.playerProof;   // multiplayer: through squadmates
            core.Rig?.Tint(WeaponTint.For(item, WeaponTint.PlayerColor()));   // an energy turret in the build's weapon colour
            gun.Hit += OnHit;
            if (gun.echo != null) gun.AreaHit += (t, dmg, emp, c) => { if (t != null && dmg > 0) t.Damage(dmg, false, (t.transform.position - c).normalized, gun.shieldFactor); };   // an echo's blast (GunEcho)
            if (fx.shot != null)
            {
                loop = gameObject.AddComponent<AudioSource>();
                loop.clip = GoF2Remake.Modding.ModSounds.Get(fx.shot);
                loop.loop = fx.shotLoops;
                loop.playOnAwake = false;
                loop.spatialBlend = 0f;
            }
            modSound = Modding.ModShipSounds.Weapon(shipIndex, item);
            if (modSound != null)
            {
                // A kit turret's own sound: its loop while it fires, else its shots one by one (ShotVoices).
                if (modSound.loop?.Clip is AudioClip ownLoop)
                {
                    if (loop == null)
                    {
                        loop = gameObject.AddComponent<AudioSource>();
                        loop.playOnAwake = false;
                        loop.spatialBlend = 0f;
                    }
                    loop.clip = ownLoop;
                    loop.loop = true;
                }
                else if (loop != null) { Destroy(loop); loop = null; }
            }
            camAnchor = new GameObject(name + " camera").transform;
            camAnchor.SetParent(core.Pivot, false);
            camAnchor.localPosition = mount.builtIn && kitModel == null ? new Vector3(0f, BuiltInCameraRise, 0f) * M : core.GunNode.localPosition;
            if (mount.builtIn)
            {
                // The kit turret's muzzle (its weapon's tip), else the gun's origin; the shots a little ahead of it.
                var kitMuzzle = kitModel != null ? core.GunNode.Find("muzzle") : null;
                core.Muzzle.localPosition = kitMuzzle != null ? kitMuzzle.localPosition : Vector3.zero;
                var at = core.Muzzle.localPosition / M;
                core.shotOffsetUnits = new Vector3(-at.x, at.y, at.z + (kitMuzzle != null ? KitBulletAhead : BuiltInBulletAhead));
                if (kitModel != null && IsCollector)
                {
                    MoveStream(fx.turretMounted, core.GunNode, core.Muzzle.localPosition);
                    foreach (Transform t in core.GunNode) if (t.name.Contains("plasma_stream")) { stream = t.gameObject; break; }
                }
            }
        }

        const float M = Gun.MetersPerUnit;
        const float BuiltInCameraRise = 60f, BuiltInBulletAhead = 120f;   // game units: the view just above the socket, shots a little ahead
        const float KitBulletAhead = 40f;   // game units past a kit turret's muzzle

        /// <summary>A built-in turret's stand-in for the model: the "pivot" -> "_gun" hierarchy the aiming turns, the gun facing
        /// the ship's nose (TurretAim starts it there). Empty, except for a plasma collector ('streamSource', its ship-mounted
        /// model): its stream (a child of the model's gun node) is moved from a copy of the model under the stand-in gun with the
        /// same local pose (its origin at the gun's, running along the gun's +Z), the rest of the copy thrown away.</summary>
        static GameObject BuiltInTurret(GameObject streamSource)
        {
            var root = new GameObject("Turret");
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(root.transform, false);
            var gun = new GameObject("builtin_gun").transform;
            gun.SetParent(pivot, false);
            if (streamSource != null) MoveStream(streamSource, gun, Vector3.zero);
            return root;
        }

        /// <summary>A plasma collector's stream lifted out of a copy of its model onto another gun ('at': the stream's origin in
        /// the gun's space; the collectors' streams sit at their gun's origin), the rest of the copy thrown away. Setup finds it
        /// there as the turret's stream.</summary>
        static void MoveStream(GameObject source, Transform gun, Vector3 at)
        {
            if (source == null) return;
            var copy = Instantiate(source);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("plasma_stream"))
                {
                    t.SetParent(gun, false);   // its rotation under the gun kept
                    t.localPosition += at;
                    t.gameObject.SetActive(false);
                    break;
                }
            Destroy(copy);
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

        ObjectDocking docking;

        /// <summary>The guns are blocked, except by docking at an object: PlayerEgo::setTurretMode 0xa6a48 refuses only while
        /// mining (+0x1e4), steering the Liberator (+0x194) or for an auto turret (+0x180), so docked (the evacuations, the
        /// hacks) the turret view and the auto turrets work; the remake had them off with the docked ship's guns. Not during
        /// the hacking game, which takes the keys.</summary>
        bool GunsBlocked
        {
            get
            {
                if (weapons == null || !weapons.Blocked) return false;
                if (docking == null) docking = GetComponent<ObjectDocking>();
                return docking == null || !docking.IsDocked || docking.Hacking != null;
            }
        }

        /// <summary>Some turret on this ship is in its view (ObjectDocking leaves the camera to it).</summary>
        public static bool ViewActive(GameObject player) => On(player).Exists(t => t.InTurretView);

        /// <summary>PlayerEgo::setTurretMode: refused for auto turrets, while mining or while the guns are blocked. Remake:
        /// one turret view at a time (entering this one leaves another's).</summary>
        public void SetTurretView(bool on)
        {
            if (on && (IsAuto || weapons == null || GunsBlocked || Time.timeScale <= 0f || Navigation.ConversationRuns)) return;
            if (InTurretView == on) return;
            if (on) foreach (var other in On(gameObject)) if (other != this && other.InTurretView) other.SetTurretView(false);
            InTurretView = on;
            if (weapons != null) weapons.TurretView = on;
            if (ship != null) { ship.steeringLocked = on; ship.CenterMouse(); }   // mouse steering's stick starts centred
            if (chase != null)
            {
                // Docked at an object the chase camera is off (ObjectDocking's look-at camera): on for the turret's view.
                if (docking == null) docking = GetComponent<ObjectDocking>();
                if (docking != null && (docking.State == ObjectDocking.Phase.Entering || docking.State == ObjectDocking.Phase.Docked
                                        || docking.State == ObjectDocking.Phase.Leaving)) chase.enabled = on;
                chase.follow = on ? camAnchor : null;
                // The camera sits on the turret's outer side: a hanging turret's anchor has the other up (still upright).
                float side = Hangs ? -1f : 1f;
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
                    collectLoop.clip = GoF2Remake.Modding.ModSounds.Get(SupernovaAssets.Load()?.extractorLoop);
                }
                collectLoop.volume = 0.047f * Sfx.EventGain * Settings.SfxVolume;
                if (on && collectLoop.clip != null) collectLoop.Play(); else collectLoop.Stop();
            }
        }

        void Update()
        {
            if (core == null || !core.Valid) return;
            float dtMs = Time.deltaTime * 1000f;
            if (gun.owner == null) gun.owner = GetComponent<Target>();   // the player's Target comes after the turret
            bool halted = Time.timeScale <= 0f || GunsBlocked || Navigation.ConversationRuns;
            if (!halted && !Navigation.PressesBlocked)   // not a menu's D-pad (multiplayer doesn't pause)
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
            if (InTurretView && (GunsBlocked || dead)) SetTurretView(false);
            if (dead) { StopShooting(); core.SetAnimating(false); core.Tick(dtMs, Target.All, null); return; }

            bool fire = false;
            if (!halted && InTurretView)
            {
                // A hanging turret's own axes point the other way round than its upright view's: mirror the stick.
                var stick = ship != null ? ship.SteerInput : Vector2.zero;
                // #103: the flight's invert options apply to the turret too (like the Liberator, WeaponSystem).
                if (ship != null) stick = new Vector2(ship.invertYaw ? -stick.x : stick.x, ship.invertPitch ? -stick.y : stick.y);
                aim.Drive(Hangs ? -stick : stick, dtMs);
                // The camera turns with the turret, at half the gun's pitch; its up is the turret's (upright when hanging).
                var yawAxis = mountRoot != null ? mountRoot.up : transform.up;
                camAnchor.rotation = Quaternion.LookRotation(Vector3.Slerp(Vector3.ProjectOnPlane(aim.BarrelForward, yawAxis), aim.BarrelForward, 0.5f),
                                                             Hangs ? -yawAxis : yawAxis);
                fire = weapons != null && weapons.FireHeld && !IsCollector;   // a collector collects by aiming (GasCloudField)
                if (IsCollector) core.SetAnimating(true);
            }
            else if (!halted && IsAuto && AutoEnabled && GetComponent<Target>() is Target self && self.Alive)
                fire = DefenseAim(dtMs) ?? AutoAim(dtMs);
            else defending = false;

            // Bullets from the gun + R * (0, 0, 300) (48: x +-80 alternating, 181: z -300), TurretCore.Fire.
            if (fire && core.Fire() >= 0)
            {
                if (defending) PointDefense.Claim(missile, missileFlightMs);   // this shot destroys it when it arrives
                bool starting = loop != null && (!loop.isPlaying || !loop.loop);
                if (starting) { loop.volume = (modSound?.loop != null ? modSound.loop.vol0 : 0.7f) * Settings.SfxVolume; loop.Play(); }
                // A mod sound's shot: every shot, or with a loop once as it starts.
                if (modSound?.shot is Modding.ModShipSounds.Sound own && (loop == null || starting) && own.Clip is AudioClip shotClip)
                    ShotVoices.Play(shotClip, own.vol0 * Settings.SfxVolume, own.RandomPitch);
            }
            core.SinceShotMs += dtMs;
            if (core.SinceShotMs > IdleStopMs && loop != null && loop.loop && loop.isPlaying) loop.Stop();
            // The animations run only while shots are going out (within one reload of the last shot).
            core.SetAnimating(core.RecentlyFired || (IsCollector && InTurretView));
            core.Tick(dtMs, Target.All, gun.missiles ? MissileLock() : null);
            PointDefense.Tick();
        }

        /// <summary>Remake (PointDefense): with missile defense mounted, the nearest missile flying at the ship within its
        /// reach, led by the shot's flight time; aligned = fire. Null when there is none to shoot at (the ship target then).</summary>
        bool? DefenseAim(float dtMs)
        {
            defending = false;
            if (defenseReachMeters <= 0f || !PointDefense.Pick(GunPosition, defenseReachMeters, out missile)) return null;
            float shotSpeed = gun.speedUnitsPerMs * Gun.MetersPerUnit;
            var at = PointDefense.Lead(missile, GunPosition, shotSpeed, out missileFlightMs);
            bool aligned = core.StepToward(at, dtMs);
            if (aim.LimitHit || aligned && core.Blocked) return null;   // out of its arc or behind its own hull: the ship target
            defending = true;
            return aligned;
        }

        /// <summary>Remake (mod turrets firing missiles): what they home on: the auto turret's target, else the radar's lock.</summary>
        Target MissileLock()
        {
            var t = IsAuto && !InTurretView ? target : weapons != null ? weapons.LockTarget : null;
            return t != null && t.Alive && !t.cloaked ? t : null;
        }

        /// <summary>handleAutoTurret: re-pick every 3 s (TurretTargeting.Rule.Retarget), turn toward the lead point, fire when
        /// aligned; a target out of the turret's pitch range is skipped at once.</summary>
        bool AutoAim(float dtMs)
        {
            var t = picking.Update(dtMs, Target.All, transform.position);
            if (t == null) return false;
            bool aligned = core.StepToward(TurretTargeting.LeadPoint(t), dtMs);
            if (aim.LimitHit || aligned && core.Blocked) { picking.LimitHit(dtMs); return false; }   // (its own hull in the way: another target)
            return aligned;
        }


        void OnHit(int bullet, Target t, Vector3 point)
        {
            t.Damage(gun.damage, false, gun.bullets[bullet].velocity, gun.shieldFactor);
            core.Rig.ShowImpact(point);
            Hit?.Invoke();
        }

        void StopShooting()
        {
            if (loop != null && loop.isPlaying) loop.Stop();
        }
    }
}
