// WeaponSystem.cs
// The player's weapons (weapons.md, weapons_special.md): one Gun per equipped weapon item on its ship mount
// (weapons_hd.json, Gun::setOffset(weaponPos[type][slot])), their projectiles, muzzle flashes, impacts and sounds.
//   Primary (MGame::OnUpdate -> PlayerEgo::shoot -> Player::shoot): while fire is held every primary gun fires on
//     its own reload; all together on the first frame, no alternation between mounts. Weapon mods (sort 28,
//     Ship::refreshValue, the last one wins): reload x (1 - attr39/100), damage x (1 + attr40/100), primaries only.
//   Beams (items 9-11, 228): hitscan on the nearest target inside the crosshair box, on screen and < 60000 units
//     (Radar::draw's KIPlayer+0x6f; other objects inside a +-24000 cube).
//   Secondary (MGame::OnTouchEnd, on release): Player::shoot first ignites every bomb in flight (EMP bombs, nukes,
//     ionizing), whatever is selected; otherwise the selected item's first ready gun fires. The selection
//     (PlayerEgo+0x10c, saved as Status+0xf4) keeps the item while it is mounted; no automatic switch when it runs empty.
//     Remake: a cycle key (G / controller D-pad right / the touch caption) instead of the HUD quick menu.
//     Ammo = the mounted stack's amount: 1 per missile / bomb / mine / blast, 1 per cluster salvo.
//   Hits: primary damage (attr 9) to the target; rockets / missiles / bombs kill asteroids instantly (9999). Area hits
//     (Gun::ignite) go the same way (hull damage + EMP); the shock blast pushes NPC ships away (PlayerFighter::initPush).
//   BombGun::update: in hardcore mode the player takes damage * clamp((mag/2 - d)/(mag/2) * 0.5, 0, 1) (shock blast x0.2).
//   The Liberator (179, attr 15 = 1; weapons_special.md 3.7): while it flies the stick steers the missile instead of the
//     ship (PlayerEgo::setRocketControl; the ship flies straight on its throttle), the camera follows it (camOffset
//     (0, 450, -1400), targetOffset (0, 0, 1700) around a point 350 ahead of it, world up, constant rumble 0.2) and its
//     engine loop (1116) plays; detonation (the next press, contact, 20 s) resets the camera. The turn rate was lost
//     (a: 1.5 rad/s at full stick).
// Input (Input System, editable in the inspector): fire = Left Ctrl / left mouse / gamepad right trigger,
// missile = F / right mouse / gamepad left trigger. Touch: FlightHud calls SetPrimaryHeld / FireSecondary.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    [RequireComponent(typeof(ShipController))]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class WeaponSystem : MonoBehaviour
    {
        const float M = Gun.MetersPerUnit;
        const float BeamRangeUnits = 60000f, BeamObjectCubeUnits = 24000f;

        public bool useBuiltInInput = true;
        /// <summary>The flight HUD's PC cursor mode: true while the mouse is over one of its buttons, so a click there (the
        /// fire button's own, the boost, the menus...) isn't also the mouse's fire / missile binding.</summary>
        [System.NonSerialized] public System.Func<bool> mouseOverControls;
        // The controls are GameControls' (rebindable): FirePrimary, FireSecondary, SwitchSecondary.
        static InputAction firePrimaryAction => GameControls.FirePrimary;
        static InputAction fireSecondaryAction => GameControls.FireSecondary;
        static InputAction cycleSecondaryAction => GameControls.SwitchSecondary;
        [Range(0f, 1f)] public float shotVolume = 0.7f;

        class Rig
        {
            public GunRig visuals;
            public Gun gun => visuals.gun;
            public WeaponFx fx => visuals.fx;
            public AudioSource loop;
            /// <summary>The loop is ending after a release (LoopRelease): ms left of its fade-out, or -1 while it plays out.</summary>
            public bool releasing;
            public float fadeLeft, fadeTotal;
            /// <summary>Secondaries: the mounted stack this gun draws its ammo from (Gun+0xf4).</summary>
            public ItemStack stack;
        }

        readonly List<Rig> rigs = new List<Rig>();
        /// <summary>Every mounted gun (primaries and secondaries), for multiplayer's shot mirrors.</summary>
        public IEnumerable<Gun> Guns { get { foreach (var r in rigs) yield return r.gun; } }

        /// <summary>How the looping shot events end on Player::stopShooting (the FEV's LGCY data, fev_lgcy.py): "loop and play
        /// to end" (cannons, thermo guns, Hammerhead turrets: the shot in progress finishes, then the event's fade-out) or
        /// "loop and cutoff" (the DLC turrets, Sunfire, Matador, MD-12: the fade-out at once). By item: (play to end,
        /// fade-out ms). Stopping them dead cut a quick tap's shot short.</summary>
        static readonly Dictionary<int, (bool playToEnd, float fadeMs)> LoopRelease = new Dictionary<int, (bool, float)>
        {
            { 22, (true, 100f) }, { 23, (true, 80f) }, { 24, (true, 70f) }, { 25, (true, 60f) }, { 26, (true, 70f) },
            { 27, (true, 60f) }, { 28, (true, 150f) }, { 29, (true, 100f) }, { 30, (true, 100f) },
            { 47, (true, 80f) }, { 48, (true, 100f) }, { 49, (true, 100f) },
            { 180, (false, 100f) }, { 181, (false, 100f) }, { 182, (false, 100f) }, { 193, (false, 100f) },
            { 224, (false, 50f) }, { 230, (false, 0f) },
        };

        /// <summary>The shot loop after a release: plays its current shot to the end and / or fades out, then stops.</summary>
        void UpdateLoopRelease(Rig r, float dtMs)
        {
            var src = r.loop;
            if (!src.isPlaying) { r.releasing = false; src.loop = true; return; }
            if (!r.releasing)
            {
                r.releasing = true;
                var (playToEnd, fadeMs) = LoopRelease.TryGetValue(r.gun.lookIndex, out var rule) ? rule : (true, 100f);
                src.loop = !playToEnd;   // play to end: this pass is the last one
                r.fadeLeft = playToEnd ? -1f : fadeMs;
                r.fadeTotal = Mathf.Max(1f, fadeMs);
                if (!playToEnd && fadeMs <= 0f) { src.Stop(); r.releasing = false; src.loop = true; }
                return;
            }
            if (r.fadeLeft < 0f) return;   // playing out; the fade-out itself is the clip's own tail
            r.fadeLeft -= dtMs;
            src.volume = shotVolume * Settings.SfxVolume * Mathf.Clamp01(r.fadeLeft / r.fadeTotal);
            if (r.fadeLeft <= 0f) { src.Stop(); src.loop = true; r.releasing = false; }
        }

        Database db;
        const float LiberatorTurnRadPerMs = 0.0015f;
        Rig liberator;
        Transform liberatorAnchor;
        AudioSource liberatorLoop, liberatorExtra;
        float liberatorBank;
        /// <summary>The Liberator is being steered (PlayerEgo+0x194): the HUD's hints and the ship's steering follow it.</summary>
        public bool SteeringMissile => liberator != null;
        /// <summary>The steered Liberator's position (Unity world), null when none: it wakes sleepers like the player.</summary>
        public static Vector3? GuidedRocket { get; private set; }
        Transform fxRoot;
        AudioSource shotSource;
        bool touchPrimary;
        /// <summary>A fire button pressed while the game was paused or the guns blocked (e.g. the mouse click on a dialogue's
        /// Next): ignored until it is released, so it neither fires when the game resumes nor launches a missile on release.</summary>
        bool primaryLatched, secondaryLatched;
        // Remake (Settings.KeyAutofire): the touch fire button's double-press autofire latch on the fire binding.
        bool keyAutofire;
        float lastKeyFirePress = -10f;

        /// <summary>The selected secondary item (-1 = none); the HUD shows it with its ammo.</summary>
        public int SelectedSecondary { get; private set; } = -1;
        /// <summary>Remaining missiles/rockets of the selected secondary weapon (-1 = none equipped).</summary>
        public int SecondaryAmmo
        {
            get
            {
                if (SelectedSecondary < 0) return -1;
                int n = 0;
                foreach (var r in rigs) if (r.gun.isSecondary && r.gun.itemIndex == SelectedSecondary && r.stack != null) n += Mathf.Max(0, r.stack.amount);
                return n;
            }
        }
        public string SecondaryName => SelectedSecondary >= 0 ? UI.ItemInfo.ItemName(SelectedSecondary) : "";
        /// <summary>The mounted secondary items in mount order, each once (Hud::initHudMenu(1)'s list).</summary>
        public List<int> SecondaryItems() => SecondaryItems(new List<int>());
        /// <summary>The same into a list of the caller's (cleared first; the HUD's weapon strip asks every frame).</summary>
        public List<int> SecondaryItems(List<int> items)
        {
            items.Clear();
            foreach (var r in rigs) if (r.gun.isSecondary && !items.Contains(r.gun.itemIndex)) items.Add(r.gun.itemIndex);
            return items;
        }
        /// <summary>The remaining ammo of one mounted secondary item.</summary>
        public int AmmoOf(int item)
        {
            int n = 0;
            foreach (var r in rigs) if (r.gun.isSecondary && r.gun.itemIndex == item && r.stack != null) n += Mathf.Max(0, r.stack.amount);
            return n;
        }
        /// <summary>The flight menu's pick (MGame::OnTouchEnd, keys 0x2000 / 0x4000 / 0x8000 / 0x10000).</summary>
        public void SelectSecondary(int item)
        {
            if (!SecondaryItems().Contains(item)) return;
            SelectedSecondary = item;
            Session.SelectedSecondary = item;
        }
        /// <summary>More than one secondary item is mounted (the HUD offers the switch).</summary>
        public bool CanCycleSecondary { get { int n = 0, last = -1; foreach (var r in rigs) if (r.gun.isSecondary && r.gun.itemIndex != last) { last = r.gun.itemIndex; n++; } return n > 1; } }
        /// <summary>No firing (the mining minigame blocks the guns, MGame::OnTouchBegin / OnTouchEnd).</summary>
        public bool Blocked { get; set; }
        /// <summary>The primary guns stay silent, the secondaries fire (the approach to an asteroid and the landing: the fire
        /// button aborts instead, Mining; missiles still go, MGame::OnTouchEnd).</summary>
        public bool PrimaryBlocked { get; set; }
        /// <summary>Raised when a player bullet hits something (the crosshair turns orange for 200 ms).</summary>
        public event Action Hit;
        /// <summary>A bomb, mine or blast went off (the explosion's world position).</summary>
        public event Action<Vector3> Detonated;
        /// <summary>Locked target for homing missiles (the radar's ship lock, CombatRadar).</summary>
        public Target LockTarget { get; set; }
        /// <summary>The player's own hittable object: never hit by its own guns.</summary>
        public Target Owner
        {
            get => owner;
            set { owner = value; foreach (var r in rigs) r.gun.owner = value; }
        }
        Target owner;

        public bool HasPrimary => rigs.Exists(r => !r.gun.isSecondary);
        /// <summary>PlayerEgo::setTurretMode: the fire button fires the turret; primaries and secondaries are silent.</summary>
        [NonSerialized] public bool TurretView;
        /// <summary>The fire button is held this frame (not paused, not latched): the turret view fires with it.</summary>
        public bool FireHeld { get; private set; }
        /// <summary>Remake: asked every frame the fire button is held; true = it fires something else and the primary guns
        /// stay silent (Mining's beam mode: a mining beam on a locked asteroid).</summary>
        [NonSerialized] public Func<bool> FireClaimed;

        void Awake()
        {
            shotSource = gameObject.AddComponent<AudioSource>();
            shotSource.playOnAwake = false;
            shotSource.spatialBlend = 0f;
        }

        void OnDisable() => StopLoops();

        void OnDestroy()
        {
            if (fxRoot != null) Destroy(fxRoot.gameObject);
            GuidedRocket = null;
        }

        /// <summary>Remake debug (SpaceLevel.SwapPlayerShip): the guns again on another hull's mounts, the old ones' visuals,
        /// loops and pools gone (a swapped hull kept firing from the previous ship's mounts).</summary>
        public void Rebuild(Database db, int shipIndex, IList<ItemStack> equipment)
        {
            StopLoops();
            foreach (var r in rigs) if (r.loop != null) Destroy(r.loop);
            rigs.Clear();
            if (fxRoot != null) Destroy(fxRoot.gameObject);
            Setup(db, shipIndex, equipment);
            Owner = owner;
        }

        /// <summary>Level::createPlayer: one gun per equipped primary/secondary item on the ship's mounts.</summary>
        public void Setup(Database db, int shipIndex, IList<ItemStack> equipment)
        {
            this.db = db;
            fxRoot = new GameObject("Player weapon fx").transform;
            var primaryMounts = db.MountsOf(shipIndex, 0);
            var secondaryMounts = db.MountsOf(shipIndex, 1);
            // Ship::refreshValue, sort 28: the last mounted weapon mod sets both factors.
            float fireRate = 1f, damageFactor = 1f;
            foreach (var e in equipment)
            {
                var it = db.Item(e.item);
                if (it != null && it.categoryId == 28) { fireRate = 1f - it.Attr(39) / 100f; damageFactor = 1f + it.Attr(40) / 100f; }
            }
            // MGame::OnInitialize 0x1a6ebc -> PlayerEgo::pitchAllPrimaryGuns(1 - getFireRateFactor()) when it is >= 0: Gun+0xb0,
            // the raw FMOD pitch Player::playShootSound passes to FModSound::play (set only when != 0; raw x 4 = octaves,
            // EventI::getPitch). The Nirai Overdrive (+20 % fire rate) raises the primaries' shots by 0.8 octaves; the
            // Overcharge (-10 %) gives a negative value the original skips: no change (#76).
            primaryPitch = 1f - fireRate > 0f ? Mathf.Pow(2f, 4f * (1f - fireRate)) : 1f;
            int p = 0, s = 0;
            for (int e = 0; e < equipment.Count; e++)
            {
                var item = db.Item(equipment[e].item);
                if (item == null) continue;
                bool secondary = item.type == "secondary";
                if (item.type != "primary" && !secondary) continue;
                var mounts = secondary ? secondaryMounts : primaryMounts;
                int slot = secondary ? s++ : p++;
                // Remake: more weapons than the model has mounts (a mod's or a Kaamo upgrade's extra slots) reuse the mounts in
                // turn, the ship's centre without any; they were skipped, so an extra secondary could be neither selected nor fired.
                var at = mounts.Count > 0 ? MountToLocal(mounts[slot % mounts.Count]) : Vector3.zero;
                var gun = new Gun(item, at, secondary);
                gun.Ignores = t => t.playerProof;   // multiplayer: through squadmates
                if (!secondary)
                {
                    // Level::createPlayer: damage = int(attr9 * damageFactor), reload = int(attr11 * fireRate), type-0 items only.
                    gun.damage = (int)(gun.damage * damageFactor);
                    gun.reloadMs = Mathf.Max(1f, (int)(gun.reloadMs * fireRate));
                }
                if (gun.isBeam) gun.AutoAim = BeamTarget;
                gun.Guided = item.Attr(15) == 1;
                var rig = BuildRig(gun);
                if (secondary) rig.stack = equipment[e];
                rigs.Add(rig);
            }
            // MGame::OnInitialize: keep the saved selection while that item is mounted, else the first secondary slot.
            int saved = Session.SelectedSecondary;
            SelectedSecondary = rigs.Exists(r => r.gun.isSecondary && r.gun.itemIndex == saved) ? saved
                              : rigs.Find(r => r.gun.isSecondary)?.gun.itemIndex ?? -1;
            Session.SelectedSecondary = SelectedSecondary;
        }

        /// <summary>Writes the remaining ammo back to the mounted stacks (they are the stacks themselves: nothing to copy).</summary>
        public void StoreAmmo() { }

        /// <summary>Mount position (game space, ship-relative) -> Unity ship space: (-x, y, z) * 0.05 (the models are
        /// imported mirrored and turned 180 degrees, so ship-local offsets flip x, not z).</summary>
        public static Vector3 MountToLocal(WeaponMount m) =>
            m.position_engine != null && m.position_engine.Length >= 3
                ? new Vector3(-m.position_engine[0], m.position_engine[1], m.position_engine[2]) * M
                : Vector3.zero;

        Rig BuildRig(Gun gun)
        {
            var rig = new Rig { visuals = new GunRig(gun, WeaponFx.Load(gun.itemIndex), fxRoot, FirePose) };
            rig.visuals.EnableTrails();   // RocketGun::setRadar: the player's rockets / missiles / thermo shots trail smoke
            var fx = rig.fx;
            if (fx != null && fx.shotLoops && fx.shot != null)
            {
                rig.loop = gameObject.AddComponent<AudioSource>();
                rig.loop.clip = GoF2Remake.Modding.ModSounds.Get(fx.Shot);
                rig.loop.loop = true;
                rig.loop.playOnAwake = false;
                rig.loop.spatialBlend = 0f;
                if (!gun.isSecondary) rig.loop.pitch = primaryPitch;   // pitchAllPrimaryGuns
            }
            gun.owner = owner;
            gun.Hit += (i, target, point) => OnHit(rig, i, target, point);
            gun.AreaHit += (target, dmg, emp, center) => OnAreaHit(rig, target, dmg, emp, center);
            gun.Ignited += point => OnIgnited(rig, point);
            gun.Expired += _ => Session.RocketAsteroids = 0;   // Gun::update: a rocket / missile ran out (medal 41)
            return rig;
        }

        // ---- input ------------------------------------------------------------------------------------------

        /// <summary>Touch: hold to fire the primary guns.</summary>
        public void SetPrimaryHeld(bool held)
        {
            if (held && !touchPrimary) touchPressedFrame = Time.frameCount;
            touchPrimary = held;
        }

        int touchPressedFrame = -1;

        /// <summary>The fire button went down this frame (keys, mouse, controller or touch), whether or not the guns may
        /// fire: the original's fire button is also the mining one (MGame::OnTouchBegin, Mining). A touch counts for two
        /// frames (the UI event may come after the reader's Update).</summary>
        public bool PrimaryPressedThisFrame =>
            (useBuiltInInput && firePrimaryAction.WasPressedThisFrame()) || Time.frameCount - touchPressedFrame <= 1;

        /// <summary>The press that stopped something (the asteroid approach) doesn't also fire: ignored until released
        /// (Hud::releaseAllKeys).</summary>
        public void SwallowPrimaryPress()
        {
            primaryLatched = true;
            keyAutofire = false;
            touchPrimary = false;
        }

        /// <summary>A held fire button fires at once (stopping the mining minigame keeps the fire key down: the guns shoot).</summary>
        public void ReleasePrimaryLatch() => primaryLatched = false;

        /// <summary>Touch / release of the missile button (MGame::OnTouchEnd -> Player::shoot(1)): detonate the bombs in
        /// flight, else fire the selected secondary.</summary>
        public bool FireSecondary()
        {
            if (Blocked || TurretView) return false;
            bool detonated = false;
            foreach (var r in rigs) if (r.gun.isSecondary && r.gun.BombInFlight) { r.gun.Detonate(); detonated = true; }
            if (detonated) return true;
            if (SelectedSecondary < 0) return false;
            foreach (var r in rigs)
            {
                if (!r.gun.isSecondary || r.gun.itemIndex != SelectedSecondary || r.stack == null || r.stack.amount <= 0) continue;
                if (r.gun.kind == Gun.Kind.Sentry && !SentryGun.CanDeploy) return false;   // Level+0x6c > 2: refused, no cost
                if (Cheats.NoSecondaryCooldown) r.gun.reloadAcc = r.gun.reloadMs + 1f;   // remake debug: reloaded at once
                int b = Fire(r.gun);
                if (b < 0) continue;
                if (r.gun.kind == Gun.Kind.Sentry)
                {
                    // SentryGun::update: the deploy "bullet" places the next free sentry object; it isn't drawn.
                    SentryGun.Deploy(db, r.gun.itemIndex, r.gun.bullets[b].position, transform.rotation, FindAnyObjectByType<World.Traffic>());
                    r.gun.bullets[b].timer = -1e9f;
                }
                if (!Cheats.InfiniteAmmo) r.stack.amount--;
                PlayShot(r);
                r.visuals.OnShot();
                AfterShot();
                Haptics.Play(Haptics.SecondaryFire);   // remake
                if (r.gun.Guided) StartLiberator(r);
                return true;
            }
            return false;
        }

        /// <summary>Remake: the next mounted secondary item (the original picks it in the HUD quick menu).</summary>
        public void CycleSecondary()
        {
            var items = new List<int>();
            foreach (var r in rigs) if (r.gun.isSecondary && !items.Contains(r.gun.itemIndex)) items.Add(r.gun.itemIndex);
            if (items.Count == 0) return;
            int i = items.IndexOf(SelectedSecondary);
            SelectedSecondary = items[(i + 1) % items.Count];
            Session.SelectedSecondary = SelectedSecondary;
        }

        // ---- per frame ----------------------------------------------------------------------------------------

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;
            // The game is paused (Time.timeScale 0: dialogues, the autopilot menu, the star map): no firing at all.
            bool halted = Blocked || Navigation.InputHalted;
            bool primaryPressed = useBuiltInInput && firePrimaryAction.IsPressed();
            bool secondaryPressed = useBuiltInInput && fireSecondaryAction.IsPressed();
            if (halted) { primaryLatched |= primaryPressed; secondaryLatched |= secondaryPressed; }
            var mouse = Mouse.current;
            if (mouse != null && mouseOverControls != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                && mouseOverControls())
            {
                if (mouse.leftButton.wasPressedThisFrame) primaryLatched |= primaryPressed;
                if (mouse.rightButton.wasPressedThisFrame) secondaryLatched |= secondaryPressed;
            }
            if (PrimaryBlocked) primaryLatched |= primaryPressed;
            // Remake option: a double press latches autofire, the next press releases it (and doesn't fire); a pause,
            // a dialogue, mining or a cutscene ends it. A click on a HUD button (latched above) doesn't count.
            if (halted || PrimaryBlocked || !Settings.KeyAutofire) keyAutofire = false;
            else if (useBuiltInInput && firePrimaryAction.WasPressedThisFrame() && !primaryLatched
                     && !AutofireLatch.Press(ref keyAutofire, ref lastKeyFirePress, Time.unscaledTime))
                primaryLatched = true;   // the releasing press is ignored until the button goes up
            if (!primaryPressed) primaryLatched = false;
            bool primaryHeld = !halted && !PrimaryBlocked && (touchPrimary || keyAutofire || (primaryPressed && !primaryLatched));
            FireHeld = primaryHeld;
            if (TurretView) primaryHeld = false;
            if (primaryHeld && FireClaimed != null && FireClaimed()) primaryHeld = false;   // the mining beam
            if (!halted && !TurretView && useBuiltInInput && fireSecondaryAction.WasReleasedThisFrame() && !secondaryLatched) FireSecondary();
            if (!secondaryPressed) secondaryLatched = false;
            if (!halted && useBuiltInInput && cycleSecondaryAction.WasPressedThisFrame()) CycleSecondary();

            if (liberator != null) UpdateLiberator(dtMs);
            var cam = Camera.main;
            var homing = HomingLock(cam);
            // Player::calcWeaponSounds 0xb00b0: only the first primary gun makes the shot sound (Player+0x10c). Remake option
            // (Settings.EachWeaponSound): the first gun of every primary weapon item, so a mixed loadout sounds each weapon.
            Rig soundRig = rigs.Find(x => !x.gun.isSecondary);
            bool each = Settings.EachWeaponSound;
            // A loop, not rigs.Find(y => ... x ...): that lambda captured 'x', a closure on every call (per shot, and every frame
            // while a looping gun is held).
            Rig FirstOfItem(int item)
            {
                foreach (var y in rigs) if (!y.gun.isSecondary && y.gun.itemIndex == item) return y;
                return null;
            }
            bool Sounds(Rig x) => x == soundRig || (each && !x.gun.isSecondary && FirstOfItem(x.gun.itemIndex) == x);
            foreach (var r in rigs)
            {
                var gun = r.gun;
                if (!gun.isSecondary && primaryHeld)
                {
                    if (Cheats.NoPrimaryCooldown) gun.reloadAcc = gun.reloadMs + 1f;   // remake debug: reloaded at once
                    int b = Fire(gun);
                    if (b >= 0) OnShot(r, Sounds(r));
                }
                gun.Update(dtMs, Target.All, homing);
                r.visuals.UpdateVisuals(dtMs, cam, transform.forward);
                if (r.loop != null)
                {
                    bool firing = primaryHeld && !gun.isSecondary && Sounds(r);
                    if (firing)
                    {
                        // Held again while ending: it just carries on.
                        r.releasing = false;
                        r.loop.loop = true;
                        r.loop.volume = shotVolume * Settings.SfxVolume;
                        if (!r.loop.isPlaying) r.loop.Play();
                    }
                    else if (r.loop.isPlaying || r.releasing) UpdateLoopRelease(r, Time.unscaledDeltaTime * 1000f);
                }
            }
        }

        /// <summary>Player::resetGunDelay(0) (PlayerEgo::dockToAsteroid / approachDockingPoint / dockToDockingPoint /
        /// dockToPlanet): the primaries start their reload over.</summary>
        public void ResetGunDelay()
        {
            foreach (var r in rigs) if (!r.gun.isSecondary) r.gun.reloadAcc = 0f;
        }

        /// <summary>RocketGun::seekEnemy 0x18bd70: missiles steer only toward a lock that is on screen (KIPlayer+0x72), not
        /// cloaked (+0x70), and not while the camera looks around freely.</summary>
        Target HomingLock(Camera cam)
        {
            var t = LockTarget;
            if (t == null || !t.Alive || t.cloaked || cam == null) return null;
            if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
            if (freeLook != null && freeLook.FreeLookActive) return null;
            var v = cam.WorldToViewportPoint(t.transform.position);
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f ? t : null;
        }
        FreeLookCamera freeLook;
        ShipController shipController;
        /// <summary>Where the shots leave: the banked, tilted model (ShipController.visualModel) when the ship has one, so
        /// bullets and muzzle flashes follow the wings through a turn (the root never rolls); else the ship itself.</summary>
        Transform FirePose
        {
            get
            {
                if (shipController == null) shipController = GetComponent<ShipController>();
                return shipController != null && shipController.visualModel != null ? shipController.visualModel : transform;
            }
        }

        /// <summary>A shot from the mount on the banked model (FirePose: the wings' muzzles through a turn), flying along the
        /// ship's own heading: PlayerEgo::shoot -> Player::shoot passes the unbanked Player matrix to Gun::shootAt, so the
        /// bullets keep to the crosshair (taking the tilted model's heading put them 8-16 deg off the nose while pitching).</summary>
        int Fire(Gun g)
        {
            var pose = FirePose;
            if (pose == transform) return g.TryFire(transform);
            var origin = pose.position + pose.rotation * g.mountLocal - transform.rotation * g.mountLocal;
            return g.TryFire(origin, transform.rotation);   // TryFire adds rotation * (mountLocal + the forward offset) again
        }

        void OnShot(Rig r, bool sound = true)
        {
            if (r.loop == null && sound) PlayShot(r);
            r.visuals.OnShot();
            AfterShot();
            Haptics.Play(Haptics.PrimaryShot);   // remake: a light tick on the controller
        }

        VolatileCargo volatileCargo;
        ChaseCamera chaseCam;
        /// <summary>The primaries' shot pitch (Gun+0xb0, pitchAllPrimaryGuns): a weapon mod speeding up the fire rate raises it.</summary>
        float primaryPitch = 1f;

        /// <summary>Player::shoot: every shot + 0.008 on the volatile meter; Gun::shootAt: TargetFollowCamera::hitSmall
        /// (50 ms, +-2 units).</summary>
        void AfterShot()
        {
            if (volatileCargo == null) volatileCargo = GetComponent<VolatileCargo>();
            volatileCargo?.Add(0.008f);
            if (chaseCam == null && Camera.main != null) chaseCam = Camera.main.GetComponent<ChaseCamera>();
            if (chaseCam != null && chaseCam.enabled) chaseCam.Shake(50f, 2f);
        }

        void PlayShot(Rig r)
        {
            var clip = r.fx != null ? r.fx.Shot : null;
            // Two voices per shot sound (FEV max_playbacks); the primaries at the weapon mod's pitch (pitchAllPrimaryGuns).
            if (clip != null) ShotVoices.Play(clip, shotVolume * Settings.SfxVolume, r.gun.isSecondary ? 1f : primaryPitch);
        }

        /// <summary>Radar::draw's auto-aim flag (KIPlayer+0x6f) for the beams: the nearest target (to the player) on screen,
        /// inside the +-w/16 box around the crosshair, within 60000 units (ships) or a +-24000 cube (other objects).</summary>
        Target BeamTarget()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var aim = cam.WorldToScreenPoint(transform.position + transform.forward * 22000f * M);
            float box = Screen.width / 16f, best = float.MaxValue;
            Target found = null;
            foreach (var t in Target.All)
            {
                if (t == null || t == owner || !t.Alive || t.untargetable || !t.isActiveAndEnabled || t.playerProof) continue;   // multiplayer: squadmates
                var d = t.transform.position - transform.position;
                if (t.isShip ? d.magnitude >= BeamRangeUnits * M
                             : Mathf.Abs(d.x) >= BeamObjectCubeUnits * M || Mathf.Abs(d.y) >= BeamObjectCubeUnits * M || Mathf.Abs(d.z) >= BeamObjectCubeUnits * M) continue;
                var p = cam.WorldToScreenPoint(t.transform.position);
                if (p.z <= 0f || Mathf.Abs(p.x - aim.x) >= box || Mathf.Abs(p.y - aim.y) >= box) continue;
                float dist = d.sqrMagnitude;
                if (dist < best) { best = dist; found = t; }
            }
            return found;
        }

        void OnHit(Rig r, int bullet, Target target, Vector3 point)
        {
            // Rockets, missiles and bombs destroy asteroids outright; everything else deals its damage (attr 9).
            bool missile = r.gun.isSecondary;
            bool wasAlive = target.Alive;
            target.lastPlayerWeapon = r.gun.itemIndex;
            float damage = missile && target.isAsteroid ? 9999f : r.gun.damage;
            if (Cheats.OneHitKills && !target.isPlayer) damage = 99999999f;
            target.Damage(damage, false, r.gun.bullets[bullet].velocity);
            if (wasAlive && !target.Alive && target.isAsteroid) { Session.AsteroidsDestroyed++; CountAsteroidMedals(r.gun); }   // Status+0xd8
            ApplyEmp(target, (int)r.gun.emp);
            r.visuals.ShowImpact(point);
            Hit?.Invoke();
        }

        /// <summary>Gun::calcCharacterCollision on an asteroid: a rocket / missile / cluster missile / ionizing missile
        /// (sorts 4, 5, 40, 34) counts toward 41 Asteroid Hazard (Status+0x12c, reset when a rocket runs out), the Liberator
        /// (0xb3) toward 44 Hot Shot (+0x144, reset when one is fired).</summary>
        static void CountAsteroidMedals(Gun g)
        {
            bool rocket = g.kind == Gun.Kind.Rocket || g.kind == Gun.Kind.Missile || g.kind == Gun.Kind.ClusterMissile || g.kind == Gun.Kind.Ionizing;
            if (rocket && !Achievements.Has(41)) Achievements.Elite(41, ++Session.RocketAsteroids);
            else if (g.itemIndex == 179 && !Achievements.Has(44)) Achievements.Elite(44, ++Session.LiberatorAsteroids);
        }

        /// <summary>Gun::ignite on one target: hull damage and EMP, the shock blast's push.</summary>
        void OnAreaHit(Rig r, Target target, int dmg, int emp, Vector3 center)
        {
            bool wasAlive = target.Alive;
            target.lastPlayerWeapon = r.gun.itemIndex;
            if (Cheats.OneHitKills && !target.isPlayer && dmg > 0) dmg = 99999999;
            if (dmg > 0) target.Damage(dmg, false, (target.transform.position - center).normalized);
            if (wasAlive && !target.Alive && target.isAsteroid)
            {
                Session.AsteroidsDestroyed++;
                // Gun::ignite: an asteroid in the Liberator's blast (medal 44 Hot Shot).
                if (r.gun.itemIndex == 179 && !Achievements.Has(44)) Achievements.Elite(44, ++Session.LiberatorAsteroids);
            }
            ApplyEmp(target, emp);
            if (r.gun.kind == Gun.Kind.ShockBlast && target.isShip)
                target.GetComponent<World.NpcShip>()?.InitPush(center, r.gun.magnitude * M);
            if (dmg > 0) Hit?.Invoke();
        }

        /// <summary>Player::damageEmp (EMP weapons): disabling a ship of races 0..3 costs standing 2 (applyDelict).</summary>
        /// <summary>Player::damageEmp 0xaf834 from the player's weapons: the friendly-fire rules first (NpcShip.OnPlayerEmp),
        /// then the EMP; a disabled ship costs standing (Standing::applyDisable, not for asteroids / Wanted criminals).</summary>
        static void ApplyEmp(Target target, int emp)
        {
            if (emp <= 0 || target.playerProof || target.phased) return;   // phased: a phase cloak
            if (target.RemoteEmp != null) { if (target.Alive) target.RemoteEmp(emp); return; }   // multiplayer: its owner's game
            if (target.hitpoints == null || !target.isShip || !target.Alive) return;
            if (target.hitpoints.emp <= 0 || target.hitpoints.hull <= 0) return;   // already disabled
            var npc = target.GetComponent<World.NpcShip>();
            npc?.OnPlayerEmp(emp);
            if (!target.hitpoints.DamageEmp(emp)) return;
            if (npc != null) npc.OnPlayerDisabled();
            else if (target.race >= 0 && target.race <= 3) Standing.ApplyDelict(target.race, 2);
        }

        /// <summary>BombGun / MineGun / ObjectGun: the explosion, the counter, hardcore self-damage.</summary>
        void OnIgnited(Rig r, Vector3 point)
        {
            var gun = r.gun;
            int type = r.fx != null ? r.fx.explosionType : 0;
            if (type >= 0) Explosion.Spawn(type, point, transform.forward, 1f, r.fx != null ? r.fx.ExplosionSound : null, false);
            if (gun.kind == Gun.Kind.ScatterGun) return;
            if (gun.kind == Gun.Kind.Nuke) Session.BombsDetonated++;   // Status+200
            if (owner != null && (gun.IsBomb || gun.kind == Gun.Kind.ShockBlast))
            {
                // BombGun::update 0x17116c at ignition: f by the player's distance (the shock blast at the ship itself).
                float half = gun.magnitude * 0.5f;
                float d = gun.kind == Gun.Kind.ShockBlast ? 0f : (point - transform.position).magnitude / M;
                float f = Mathf.Clamp01((half - d) / half * 0.5f) * (gun.kind == Gun.Kind.ShockBlast ? 0.2f : 1f);
                if (Session.IsExtreme && f > 0f && gun.damage > 0f) owner.Damage((int)(f * gun.damage), false, (transform.position - point).normalized);
                // PlayerEgo::addNukeVolatileForce: the volatile goods' meter + 3 f (a nuke at the ship: + 1.5).
                if (f > 0f) GetComponent<VolatileCargo>()?.Add(3f * f);
            }
            Detonated?.Invoke(point);
        }

        // ---- the Liberator (PlayerEgo::setRocketControl, BombGun guided) ------------------------------------------

        void StartLiberator(Rig r)
        {
            liberator = r;
            Session.LiberatorAsteroids = 0;   // Gun::shootAt 0xb3: a new Liberator starts medal 44's count
            var ship = GetComponent<ShipController>();
            if (ship != null) ship.steeringLocked = true;
            if (liberatorAnchor == null) liberatorAnchor = new GameObject("Liberator camera target").transform;
            PlaceLiberatorAnchor();
            var chase = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            if (chase != null)
            {
                chase.follow = liberatorAnchor;
                chase.followOffset = new Vector3(0f, 450f, -1400f) * M;
                chase.followLookOffset = new Vector3(0f, 0f, 1700f) * M;
                chase.followRigid = false;
                chase.followUsesUp = false;
                chase.constantRumble = 0.2f;
            }
            if (r.fx != null && r.fx.engineLoop != null)
            {
                if (liberatorLoop == null)
                {
                    liberatorLoop = gameObject.AddComponent<AudioSource>();
                    liberatorLoop.loop = true;
                    liberatorLoop.playOnAwake = false;
                    liberatorLoop.spatialBlend = 0f;
                }
                liberatorLoop.clip = GoF2Remake.Modding.ModSounds.Get(r.fx.engineLoop);
                liberatorLoop.volume = LiberatorVolume;
                liberatorLoop.Play();
                if (liberatorExtra == null)
                {
                    liberatorExtra = gameObject.AddComponent<AudioSource>();
                    liberatorExtra.loop = true;
                    liberatorExtra.playOnAwake = false;
                    liberatorExtra.spatialBlend = 0f;
                }
                liberatorExtra.clip = GoF2Remake.Modding.ModSounds.Get(r.fx.engineLoopExtra);
                liberatorBank = 0f;   // PlayerEgo::setRocketControl: +0x198 = 0
                UpdateLiberatorSound(0f);
            }
        }

        void UpdateLiberator(float dtMs)
        {
            if (!liberator.gun.BombInFlight || (owner != null && !owner.Alive)) { EndLiberator(); return; }
            var ship = GetComponent<ShipController>();
            if (!Navigation.InputHalted && !Blocked) liberator.gun.SteerBullet(0, ship != null ? ship.SteerInput : Vector2.zero, dtMs, LiberatorTurnRadPerMs);
            PlaceLiberatorAnchor();
            UpdateLiberatorSound(dtMs);
        }

        // 1116 (the FEV's LGCY data), event volume 0.121, "load" never set (gain 0.7).
        static float LiberatorVolume => 0.121f * 0.7f * Sfx.EventGain * Settings.SfxVolume;

        /// <summary>BombGun::update 0x17116c: parameter 0 "Vertical" = PlayerEgo::getRocketBanking * 0.2, the banking growing
        /// by dt * stick x * 0.01 (PlayerEgo::right / left, +0x198), clamped 0..1 by FMOD: it pitches the Liberator engine
        /// x0.891 -> x1.122 and adds EngineDLC_06 from 0.336.</summary>
        void UpdateLiberatorSound(float dtMs)
        {
            if (liberatorLoop == null) return;
            var ship = GetComponent<ShipController>();
            if (ship != null && !Navigation.InputHalted && !Blocked) liberatorBank += dtMs * ship.SteerInput.x * 0.01f;
            float v = Mathf.Clamp01(liberatorBank * 0.2f);
            liberatorLoop.pitch = Mathf.Pow(2f, 8f * Mathf.Lerp(0.479167f, 0.520833f, v) - 4f) * TimeExtender.SoundPitch;
            liberatorLoop.volume = LiberatorVolume;
            if (liberatorExtra == null || liberatorExtra.clip == null) return;
            liberatorExtra.volume = LiberatorVolume;
            bool on = liberatorLoop.isPlaying && v >= 0.336146f && v <= 0.99578f;
            if (on && !liberatorExtra.isPlaying) liberatorExtra.Play();
            else if (!on && liberatorExtra.isPlaying) liberatorExtra.Stop();
        }

        /// <summary>BombGun+0xe8: a helper at the missile + its direction * 350, carrying the missile's orientation.</summary>
        void PlaceLiberatorAnchor()
        {
            ref var b = ref liberator.gun.bullets[0];
            GuidedRocket = b.position;
            var dir = b.velocity.sqrMagnitude > 1e-9f ? b.velocity.normalized : transform.forward;
            liberatorAnchor.SetPositionAndRotation(b.position + dir * 350f * M, Quaternion.LookRotation(dir, b.up));
        }

        /// <summary>LevelScript: isInRocketControl -> setRocketControl(null) + PlayerEgo::killLiberator (a cutscene takes over).</summary>
        public void KillLiberator()
        {
            if (liberator == null) return;
            liberator.gun.RemoveAll();
            EndLiberator();
        }

        /// <summary>Detonation: LevelScript::resetCamera, setRocketControl(null), the loop stops.</summary>
        void EndLiberator()
        {
            liberator = null;
            GuidedRocket = null;
            var ship = GetComponent<ShipController>();
            if (ship != null) ship.steeringLocked = false;
            var chase = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            if (chase != null && chase.follow == liberatorAnchor)
            {
                chase.follow = null;
                chase.constantRumble = 0f;
                chase.Snap();
            }
            if (liberatorLoop != null) liberatorLoop.Stop();
            if (liberatorExtra != null) liberatorExtra.Stop();
        }

        void StopLoops()
        {
            foreach (var r in rigs) if (r.loop != null) r.loop.Stop();
        }
    }
}
