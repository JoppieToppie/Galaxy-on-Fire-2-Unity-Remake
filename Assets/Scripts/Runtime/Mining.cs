// Mining.cs
// Asteroid mining on the player's ship, from lock-on to ore in the cargo hold (Reference/research/mining.md):
//   Radar::draw 0x1554fc          lock: keep an asteroid inside a +-w/16 box around the crosshair for (scanner attr 29,
//                                 else 8000) - 200 ms; nearest in 3D wins; sound 26; no drill -> 541 "No drill installed."
//                                 (the original keeps the full ring and repeats 541 every frame (0x157b00); remake: the
//                                 lock is dropped and that asteroid isn't tried again until it has left the crosshair box)
//   MGame::OnTouchBegin 0x1a838c  action while locked: cargo full -> 322, else "Target: Asteroid" + sound 28 and the
//                                 autopilot takes the ship in; again = cancel ("Autopilot Off", sound 29); while mining = stop
//   PlayerEgo::approachAsteroid   steer dir += (to - dir) * dt * min(H + 2.7, 4) / 4096 at full throttle; the last 2000
//                                 units before scale * 2500: exhaust off, sound 2, the chase camera freezes, the model
//                                 pitches up (cumulative); then stop, asteroid spin off, a short settle, the minigame
//   MiningGame (MiningGame)   drill loop sound 1 (DrillSound: its four layers by drill_speed), sound 3 while off target
//   PlayerEgo::stopMining 0xade94 ore (capped to free cargo, hardcore halves an unfinished run) and on a full class-A run a
//                                 core (ore + 11) into the cargo, "12t Gold" messages, then the asteroid explodes (no crate)
// The HUD shows the lock ring, the ore plate, the minigame and the prompt; it forwards touch input and the action button.
// Not yet: stats / medals (Geologist, Miner, Ore Athlete), the Ultrascan class-A markers, the mining plant.
// Remake, beam mode: a mounted drill item (sort 19) with attribute 100 = 1 is a mining beam (a mod's equipment; Modding/
// README.md). The lock is the same; there is no approach, landing or minigame and the ship keeps flying: holding fire on
// the locked asteroid (the guns stay silent meanwhile, WeaponSystem.FireClaimed / BeamClaimsFire) fires the beam straight along the ship's
// heading, fixed to it: it cuts where that line meets the rock (aim the nose at it), with the minigame's rules
// (MiningBeamExtraction: layer by layer, attr 33 yield, attr 102 ms per layer) within attr 101 units of its surface
// (default 24000, the beam lasers' reach on objects, WeaponSystem.BeamTarget); the progress stays with the asteroid when
// the beam lets go. Every ton flies into the ship (MiningBeamFx) and into the hold as it arrives; a depleted asteroid
// gives its core (class A) and explodes. While the beam holds, the lock follows the asteroid in a wider box (+-w/5).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Mining : MonoBehaviour
    {
        public enum Phase { Idle, Approaching, Landing, Docked, Mining }

        const float M = 0.05f;
        const float CrosshairDistanceMeters = 22000f * M;   // same aim point as the HUD crosshair

        public Phase State { get; private set; } = Phase.Idle;
        /// <summary>PlayerEgo::lostMiningGame (+0x39b): the last minigame failed (its energy ran out).</summary>
        public bool LostGame { get; private set; }
        public Target Candidate { get; private set; }
        public Target Locked { get; private set; }
        public Target Target { get; private set; }
        /// <summary>A scanner with attr 30 = 1 marks the class-A asteroids (Radar::draw 0x1577de).</summary>
        public bool Ultrascan { get; private set; }
        /// <summary>Level::isInAsteroidCenterRange: within 100 000 units of the field's centre.</summary>
        public const float FieldRangeMeters = 100000f * 0.05f;

        /// <summary>Radar::draw 0x1577de: with the Ultrascan, not in a docking procedure and inside the asteroid field's
        /// sphere, every living quality-7 (class A) asteroid gets the class letter "A" (0x44e frame 0) on screen.</summary>
        public void ClassAMarkers(Vector3? fieldCentre, List<Target> into)
        {
            into.Clear();
            if (!Ultrascan || State != Phase.Idle || fieldCentre == null || ship == null) return;
            if ((ship.transform.position - fieldCentre.Value).sqrMagnitude > FieldRangeMeters * FieldRangeMeters) return;
            foreach (var t in Target.All) if (t != null && t.isAsteroid && t.Alive && t.quality == 7) into.Add(t);
        }
        public MiningGame Game { get; private set; }
        /// <summary>Lock ring frame 0..23 (image 0x456), -1 = not shown.</summary>
        public int LockFrame { get; private set; } = -1;
        public bool HasDrill => drill != null;
        public int FreeCargo => Shop.FreeCargo(db);
        /// <summary>A HUD message (text id 546, 541, 322, "12t Gold" ...).</summary>
        public event Action<string> Message;
        /// <summary>Multiplayer (NetOrbit): the asteroid exploding now was mined out here (not shot or rammed).</summary>
        public static bool MiningOut { get; private set; }

        /// <summary>What the action button does right now (null = nothing to do with mining).</summary>
        public string PromptText =>
            BeamMode && State == Phase.Idle ? null   // the fire button mines
            : State == Phase.Mining ? Localization.Extra("hudMiningStop", "STOP MINING")
            : State != Phase.Idle ? Localization.Extra("hudMiningAbort", "ABORT")
            : Locked != null ? Localization.Extra("hudMine", "MINE") : null;

        Database db;
        ShipController ship;
        WeaponSystem weapons;
        ChaseCamera chase;
        CombatAudio sounds;
        AudioSource sfx;
        /// <summary>Event 3 Mining_Drill_Broken: a loop (Mining_Drill_Broken.wav, 10 s, loop and cutoff), event volume 0.245.</summary>
        AudioSource brokenLoop;
        DrillSound drillSound;
        // The FEV's event volumes (fev_lgcy.py): 2 Mining_Landing, 3 Mining_Drill_Broken (the drill's own, 0.244, in DrillSound).
        const float LandingVolume = 0.183f, BrokenVolume = 0.2455f;
        ItemData drill;
        int lockTimeMs = 8000;
        float lockTimer;
        bool wasLocked;
        Target refusedNoDrill;   // remake: its lock ran out without a drill; ignored until it leaves the crosshair box
        float dockDistance, pitchAccumulator;
        bool ready;
        Vector3 capturedUp;
        Vector2 touchInput;

        // ---- beam mode (remake) ----
        /// <summary>The mounted drill is a mining beam (attr 100 = 1): free flight, the fire button mines.</summary>
        public bool BeamMode { get; private set; }
        /// <summary>The asteroid the mining beam is cutting this frame (null = the beam is off or out of range).</summary>
        public Target Beaming { get; private set; }
        MiningBeamFx beamFx;
        readonly Dictionary<Target, MiningBeamExtraction> extractions = new Dictionary<Target, MiningBeamExtraction>();
        float beamRangeUnits, beamLayerMs;
        int beamYield;
        /// <summary>The beam is on (fire held on a locked asteroid, in range or not).</summary>
        bool beamOn;
        /// <summary>Refused for this press (a full hold): off until the fire button is let go.</summary>
        bool beamRefused;
        bool beamRangeSaid;
        /// <summary>Tons cut since the beam started, by item (the "12t Gold" message when it stops).</summary>
        readonly Dictionary<int, int> beamCut = new Dictionary<int, int>();
        Target beamTarget;
        const float BeamDefaultRangeUnits = 24000f;   // WeaponSystem.BeamObjectCubeUnits

        /// <summary>Set by SpaceLevel: station / planet locks and the autopilot come first (Radar::draw order).</summary>
        [NonSerialized] public Navigation navigation;

        public void Setup(Database database, ShipController controller, WeaponSystem weaponSystem, ChaseCamera chaseCamera)
        {
            db = database;
            ship = controller;
            weapons = weaponSystem;
            chase = chaseCamera;
            sounds = CombatAudio.Load();
            drill = Shop.FirstMounted(db, 19);
            var scanner = Shop.FirstMounted(db, 17);
            lockTimeMs = scanner != null && scanner.HasAttr(29) ? scanner.Attr(29) : 8000;   // Radar::Radar
            lockTimeMs = Cheats.LockMs(lockTimeMs);
            Ultrascan = scanner != null && scanner.Attr(30) == 1;                             // Radar+0x1a6 (Hiroto Ultrascan)
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            brokenLoop = gameObject.AddComponent<AudioSource>();
            brokenLoop.playOnAwake = false;
            brokenLoop.loop = true;
            brokenLoop.clip = GoF2Remake.Modding.ModSounds.Get(sounds?.miningDrillBroken);
            drillSound = new DrillSound(gameObject.AddComponent<AudioSource>(), gameObject.AddComponent<AudioSource>(),
                                        gameObject.AddComponent<AudioSource>(), gameObject.AddComponent<AudioSource>(),
                                        sounds?.miningDrillSlow, sounds?.miningDrill, sounds?.miningDrillAdd2, sounds?.miningDrillSwitch);
            BeamMode = drill != null && drill.Attr(100) == 1;
            if (BeamMode)
            {
                beamYield = drill.Attr(33, 100);
                beamRangeUnits = Mathf.Max(1000f, drill.Attr(101, (int)BeamDefaultRangeUnits));
                beamLayerMs = Mathf.Max(500f, drill.Attr(102, (int)MiningGame.LayerMs));
                beamFx = gameObject.AddComponent<MiningBeamFx>();
                beamFx.Setup(ship, db, drill.Attr(103, 228));
                beamFx.Arrived += OreLanded;
                if (weapons != null) weapons.FireClaimed = BeamClaimsFire;
            }
        }

        /// <summary>Touch stick for the drill (+y = up on the stick).</summary>
        public void SetTouchInput(Vector2 v) => touchInput = v;

        // ---- per frame ---------------------------------------------------------------------------------------

        void Update()
        {
            if (db == null) return;
            float dtMs = Time.deltaTime * 1000f;
            // MGame::OnTouchBegin 0x1a838c: the fire button is the mining button too. During the approach, the landing and
            // the settle it turns the autopilot off (hudEvent 6, dockToAsteroid(null), Hud::releaseAllKeys: that press
            // fires nothing); in the minigame it stops mining with the ore so far (PlayerEgo::stopMining) and the button
            // stays down, so the guns fire at once.
            if (State != Phase.Idle && weapons != null && !Navigation.InputHalted && weapons.PrimaryPressedThisFrame)
            {
                if (State == Phase.Mining) { FinishMining(); weapons.ReleasePrimaryLatch(); }
                else { weapons.SwallowPrimaryPress(); Interact(); }
                return;
            }
            switch (State)
            {
                case Phase.Idle:
                    if (navigation != null && navigation.BlocksAsteroidLock) { Candidate = Locked = refusedNoDrill = null; LockFrame = -1; lockTimer = 0f; wasLocked = false; }
                    else UpdateLock(dtMs);
                    if (BeamMode) UpdateBeam(dtMs);
                    break;
                case Phase.Approaching:
                case Phase.Landing:
                case Phase.Docked:
                    if (Target == null || !Target.Alive)
                    {
                        // Multiplayer: another player mined it out before this one landed.
                        Say(GoneMessage() ?? Localization.Get(571) + " " + Localization.Get(39));
                        Undock();
                        break;
                    }
                    // MGame::OnTouchBegin key 2: the booster works until the minigame exists (PlayerEgo::isMining is the
                    // MiningGame, +0x1e4), so on the way in and the landing too; the ship's own input is off meanwhile.
                    if (!Navigation.PressesBlocked && GameControls.Boost.WasPressedThisFrame()) ship.Boost();
                    Approach(dtMs);
                    break;
                case Phase.Mining: UpdateMining(dtMs); break;
            }
        }

        /// <summary>Radar::draw, asteroid part: box around the crosshair, nearest in 3D, lock timer.</summary>
        void UpdateLock(float dtMs)
        {
            if (GoF2Remake.Multiplayer.NetArenaClient.InMatch) return;   // no mining in an arena match
            var cam = Camera.main;
            // Remake, beam mode: while the beam holds an asteroid the lock follows it in a wider box (the ship turns and the
            // rock spins; the crosshair box alone dropped it at every wobble).
            if (beamOn && beamTarget != null && cam != null && InBox(cam, beamTarget, Screen.width / 5f))
            {
                Candidate = Locked = beamTarget;
                LockFrame = 23;
                wasLocked = true;
                return;
            }
            Target best = null;
            if (cam != null)
            {
                var c = cam.WorldToScreenPoint(ship.transform.position + ship.transform.forward * CrosshairDistanceMeters);
                float r = Screen.width / 16f, bestDist = float.MaxValue;
                if (c.z > 0f)
                    foreach (var t in Target.All)
                    {
                        if (t == null || !t.Alive || t.oreItem < 0) continue;
                        var p = cam.WorldToScreenPoint(t.transform.position);
                        if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height) continue;
                        if (Mathf.Abs(p.x - c.x) >= r || Mathf.Abs(p.y - c.y) >= r) continue;
                        float d = (t.transform.position - ship.transform.position).sqrMagnitude;
                        if (d < bestDist) { bestDist = d; best = t; }
                    }
            }
            // Remake: an asteroid refused for want of a drill stays ignored while it remains the pick (looking away resets it).
            if (best != null && best == refusedNoDrill) best = null;
            else refusedNoDrill = null;
            if (best != Candidate) { Candidate = best; lockTimer = 0f; }
            if (Candidate == null) { Locked = null; LockFrame = -1; wasLocked = false; return; }

            lockTimer += dtMs;
            LockFrame = lockTimer > 500f ? Mathf.Min(23, (int)(23f * (lockTimer - 500f) / Mathf.Max(1f, lockTimeMs - 500f))) : -1;
            if (lockTimer > lockTimeMs - 200f)
            {
                if (drill == null)
                {
                    // Radar::draw 0x157b00: hudEvent(0x14) = 541 "No drill installed."; remake: the lock is cancelled (the
                    // ring and the ore plate go) instead of staying full and repeating the message.
                    Say(Localization.Get(541));
                    refusedNoDrill = Candidate;
                    Candidate = Locked = null;
                    LockFrame = -1;
                    lockTimer = 0f;
                    wasLocked = false;
                    return;
                }
                else
                {
                    Locked = Candidate;
                    LockFrame = 23;
                    if (!wasLocked) Play(sounds?.targetLock);
                }
            }
            else Locked = null;
            wasLocked = Locked != null;
        }

        /// <summary>The HUD action button / Enter / controller X (the original's fire button while locked or mining); false
        /// when nothing happened (no lock, a full hold), so the touch fire button shoots instead.</summary>
        public bool Interact()
        {
            switch (State)
            {
                case Phase.Idle:
                    if (Locked == null || BeamMode) return false;   // beam mode: the fire button mines
                    if (Shop.FreeCargo(db) < 1) { Say(Localization.Get(322)); return false; }   // Cargo hold is full.
                    Say(Localization.Get(546) + ": " + Localization.Get(550));            // Target: Asteroid
                    Play(sounds?.autopilotOn);
                    StartApproach(Locked);
                    break;
                case Phase.Mining:
                    FinishMining();   // stop early: keeps the ore so far
                    break;
                default:
                    Say(Localization.Get(571) + " " + Localization.Get(39));              // Autopilot Off
                    Play(sounds?.autopilotOff);
                    Undock();
                    break;
            }
            return true;
        }

        // ---- approach (PlayerEgo::dockToAsteroid 0xab9b4 / approachAsteroid 0xaba90) --------------------------

        void StartApproach(Target asteroid)
        {
            Target = asteroid;
            State = Phase.Approaching;
            dockDistance = asteroid.scale * 2500f;
            pitchAccumulator = 0f;
            ready = false;
            ship.externalControl = true;   // player steering off
            ship.SetThrottle(1f);          // MGame::OnUpdate forces full throttle while docking
            if (weapons != null) weapons.PrimaryBlocked = true;   // the fire button aborts; missiles still go (MGame::OnTouchEnd)
        }

        void Approach(float dtMs)
        {
            var tr = ship.transform;
            Vector3 toTarget = Target.transform.position - tr.position;
            float d = toTarget.magnitude / M;
            if (d >= dockDistance)
            {
                // moveToPosition 0xa8720: turn = min(handling + 2.7, 4), dir += (to - dir) * dt * turn / 4096.
                float turn = Mathf.Min(ship.stats.handling / 100f + 2.7f, 4f);
                var dir = (tr.forward + (toTarget.normalized - tr.forward) * (dtMs * turn / 4096f)).normalized;
                tr.rotation = Quaternion.LookRotation(dir, Vector3.up);
                // moveToPosition: dt * throttle (+0xbc, full while docking) * speed (+0xb8: a boost raises it).
                float step = Mathf.Min(dtMs * ship.Model.Throttle * ship.Model.CurrentSpeed, d - dockDistance + 1f) * M;
                tr.position += tr.forward * step;
                ship.ExternalSpeedMetersPerSecond = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;
            }
            else ship.ExternalSpeedMetersPerSecond = 0f;

            if (State == Phase.Approaching && d < dockDistance + 2000f) BeginLanding();

            if (State == Phase.Landing)
            {
                // Pitch the model up until its nose is within 0.2 rad of its old up vector (cumulative, like the original,
                // normalised to its ~20 ms frames).
                var model = ship.visualModel;
                if (model != null && Vector3.Angle(model.forward, capturedUp) * Mathf.Deg2Rad > 0.2f && pitchAccumulator > -1024f)
                {
                    pitchAccumulator -= 0.35f * dtMs;
                    model.localRotation *= Quaternion.Euler(pitchAccumulator / 65536f * 360f * (dtMs / 20f), 0f, 0f);
                }
                else ready = true;
                if (ready && d < dockDistance)
                {
                    State = Phase.Docked;
                    var spin = Target.GetComponent<Spin>();
                    if (spin != null) spin.enabled = false;
                }
            }
            else if (State == Phase.Docked)
            {
                if (pitchAccumulator > -1024f) { pitchAccumulator -= dtMs / 2f; return; }   // settle
                StartMinigame();
            }
        }

        void BeginLanding()
        {
            State = Phase.Landing;
            weapons?.ResetGunDelay();   // PlayerEgo::dockToAsteroid
            SetExhaust(false);
            Play(sounds?.miningLanding, LandingVolume);
            if (chase != null) chase.enabled = false;   // TargetFollowCamera::setActive(false): the camera stays put
            Haptics.Play(Haptics.MiningLanding);   // remake
            capturedUp = ship.visualModel != null ? ship.visualModel.up : ship.transform.up;
            ship.modelHeld = true;   // the pitch-up below is cumulative; ShipController would level the model again every frame
        }

        void StartMinigame()
        {
            Game = new MiningGame(Target.quality, Target.oreItem, drill.Attr(32), drill.Attr(33), Session.CampaignMission <= 4);
            LostGame = false;
            mouseOffset = Vector2.zero;   // MGame::OnUpdate zeroes the mouse deltas while docking: the drill starts centred
            Game.InsideChanged += inside =>
            {
                // MiningGame::update: off target stop(1) + play(3), back on play(1) + stop(3): one loop at a time.
                if (inside) { StopBroken(); if (!drillSound.IsPlaying) drillSound.Start(DrillSpeed); }
                else { drillSound.Stop(); StartBroken(); }
            };
            Game.NewTon += () => Haptics.Play(Haptics.DrillTon);   // remake
            stutterMs = 0f;
            Target.radius = 0f;   // Player radius 0: can't be hit or collided while mined
            State = Phase.Mining;
            miners = 1;
            if (weapons != null) weapons.Blocked = true;   // PlayerEgo::isMining: no guns, no missiles, no boost
            drillSound.Start(DrillSpeed);
        }

        // ---- minigame -------------------------------------------------------------------------------------------

        /// <summary>Multiplayer: another player's game destroyed the asteroid: "Mined out by X." (they finished drilling it) or
        /// "X destroyed the asteroid." (shot, rammed, a blast); null = not another player.</summary>
        string GoneMessage() => GoneMessage(Target);

        string GoneMessage(Target asteroid)
        {
            var orbit = GoF2Remake.Multiplayer.NetGame.Active ? GoF2Remake.Multiplayer.NetOrbit.Current : null;
            if (orbit == null || !orbit.DestroyedBy(asteroid, out string by, out bool mined)) return null;
            if (by.Length == 0) by = Localization.Extra("mpAnotherPilot", "another pilot");
            return mined ? string.Format(Localization.Extra("mpMinedOutBy", "Mined out by {0}."), by)
                         : string.Format(Localization.Extra("mpAsteroidDestroyedBy", "{0} destroyed the asteroid."), by);
        }

        /// <summary>Multiplayer: the most players drilling this asteroid at once during this session (the ore is split
        /// between them); 1 in single player.</summary>
        int miners = 1;

        void UpdateMining(float dtMs)
        {
            if (Target == null || !Target.Alive)
            {
                // Multiplayer: another player mined it out (or shot it): the drilled ore is still this player's share.
                string gone = GoneMessage();
                if (gone != null) Say(gone);
                else { Session.OreStreak = 0; Say(Localization.Get(539)); }
                FinishMining();
                return;
            }
            if (GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetOrbit.Current != null)
                miners = Mathf.Max(miners, 1 + GoF2Remake.Multiplayer.NetOrbit.Current.OtherMiners(Target));
            Game.SetInput(ReadDrillInput());
            drillSound.Set(DrillSpeed);
            int layer = Game.Layer;
            bool running = Game.Update(dtMs);
            DrillHaptics(dtMs, layer);
            if (running) return;
            if (Game.Lost) { Session.OreStreak = 0; LostGame = true; Say(Localization.Get(539)); }   // Mining failed.
            Haptics.Play(Game.Won ? Haptics.MiningWon : Haptics.MiningLost);   // remake
            // MiningGame::update: every layer drilled -> Status+0x124 + 1 (medal 38 Ore Athlete).
            if (Game.Won && !Achievements.Has(38)) Achievements.Elite(38, ++Session.OreStreak);
            FinishMining();
        }

        float stutterMs;

        /// <summary>Remake haptics while drilling: a light rumble on target that grows with the depth (the drill sound's
        /// speed), a knock per layer drilled; off target the broken drill's stutter, every 180 ms.</summary>
        void DrillHaptics(float dtMs, int layerBefore)
        {
            if (Game.Layer != layerBefore && !Game.Won) Haptics.Play(Haptics.DrillLayer);
            if (Game.Inside)
            {
                stutterMs = 0f;
                Haptics.Rumble(0.06f + 0.02f * Game.Layer);
            }
            else if ((stutterMs -= dtMs) <= 0f)
            {
                stutterMs = 180f;
                Haptics.Play(Haptics.DrillStutter);
            }
        }

        /// <summary>PlayerEgo::update with the mouse cursor on (FlightHud: mouse steering in use) feeds the drill the cursor's
        /// offset, a virtual stick: the PC version drills with the mouse.</summary>
        [System.NonSerialized] public bool mouseDrill;
        Vector2 mouseOffset;

        /// <summary>The touch stick (squared by FlightHud), the Drill controls (GameControls: the arrows, W A S D, the left
        /// stick; squared per axis like Hud::getAnalog) or the mouse (linear, PlayerEgo::update's offset / limit); the stronger
        /// one wins. +y = down on screen for the minigame.</summary>
        Vector2 ReadDrillInput()
        {
            var v = new Vector2(touchInput.x, -touchInput.y);
            var s = GameControls.Drill.ReadValue<Vector2>();
            s = new Vector2(Mathf.Sign(s.x) * s.x * s.x, Mathf.Sign(s.y) * s.y * s.y);
            if (s.sqrMagnitude > 0.0004f && s.sqrMagnitude > v.sqrMagnitude) v = new Vector2(s.x, -s.y);
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouseDrill && mouse != null)
            {
                // Clamped to +-0.7 of half the screen like the ship's mouse steering; screen down = +y.
                var lim = new Vector2(Screen.width * 0.35f, Screen.height * 0.35f);
                var o = mouseOffset + mouse.delta.ReadValue();
                mouseOffset = new Vector2(Mathf.Clamp(o.x, -lim.x, lim.x), Mathf.Clamp(o.y, -lim.y, lim.y));
                var m = new Vector2(mouseOffset.x / Mathf.Max(1f, lim.x), -mouseOffset.y / Mathf.Max(1f, lim.y));
                if (m.sqrMagnitude > v.sqrMagnitude) v = m;
            }
            else mouseOffset = Vector2.zero;
            // Remake: the drill's own invert options (flight's don't apply here).
            if (Settings.InvertDrillX) v.x = -v.x;
            if (Settings.InvertDrillY) v.y = -v.y;
            return Vector2.ClampMagnitude(v, 1f);
        }

        /// <summary>PlayerEgo::stopMining: ore (and a core) into the cargo, messages, the asteroid explodes, undock.</summary>
        void FinishMining()
        {
            if (Game != null && Target != null)
            {
                int ore = Target.oreItem, core = Target.CoreItem;
                int free = Shop.FreeCargo(db);
                int n = Game.Lost ? 0 : Game.OreAmount;
                if (Session.IsExtreme && !Game.Won) n /= 2;
                // Multiplayer: one asteroid's ore split between everyone who drilled it at once (at least 1 t for some ore).
                if (miners > 1 && n > 0) n = Mathf.Max(1, n / miners);
                n = Mathf.Min(n, free);
                if (free < 1) Say(Localization.Get(322));
                else
                {
                    if (Game.GotCore)
                    {
                        Shop.AddToCargo(core, 1);
                        Session.CoresMined++;
                        Session.CoreTypesMined.Add(core);
                        Say($"1t {GameNames.Item(core)}");
                        n = Mathf.Min(n, Shop.FreeCargo(db));
                    }
                    if (n > 0)
                    {
                        Shop.AddToCargo(ore, n);
                        Session.OreMined += n;
                        Session.OreTypesMined.Add(ore);
                        Say($"{n}t {GameNames.Item(ore)}");
                    }
                    if (Shop.FreeCargo(db) <= 0) Say(Localization.Get(322));
                }
                // HP -1: the asteroid's explosion and sound 21, no crate (not twice: mined out by someone else).
                if (Target.Alive) { MiningOut = true; Target.Explode(); MiningOut = false; }
            }
            Undock();
        }

        /// <summary>dockToAsteroid(null): spin back on, camera and controls back, the model upright, sounds off.</summary>
        void Undock()
        {
            if (Target != null && Target.Alive)
            {
                var spin = Target.GetComponent<Spin>();
                // Multiplayer: not while another player is still on it (NetOrbit starts it once they have left).
                bool othersOn = GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetOrbit.Current != null
                                && GoF2Remake.Multiplayer.NetOrbit.Current.OthersOn(Target);
                if (spin != null && !othersOn) spin.enabled = true;
            }
            State = Phase.Idle;
            Target = null;
            Game = null;
            Locked = Candidate = null;
            LockFrame = -1;
            lockTimer = 0f;
            ship.externalControl = false;
            ship.modelHeld = false;
            ship.ExternalSpeedMetersPerSecond = 0f;
            if (ship.visualModel != null) ship.visualModel.localRotation = Quaternion.identity;
            if (weapons != null) weapons.Blocked = weapons.PrimaryBlocked = false;
            if (chase != null) chase.enabled = true;   // damped catch-up from where it stopped
            SetExhaust(true);
            drillSound.Stop();
            StopBroken();
        }

        // ---- beam mode (remake) ----------------------------------------------------------------------------------

        /// <summary>The asteroid's centre on screen within +-'half' px of the crosshair.</summary>
        bool InBox(Camera cam, Target t, float half)
        {
            if (t == null || !t.Alive) return false;
            var c = cam.WorldToScreenPoint(ship.transform.position + ship.transform.forward * CrosshairDistanceMeters);
            var p = cam.WorldToScreenPoint(t.transform.position);
            return c.z > 0f && p.z > 0f && Mathf.Abs(p.x - c.x) < half && Mathf.Abs(p.y - c.y) < half;
        }

        /// <summary>0..1 of the asteroid the beam has cut, -1 = none (the lock plate).</summary>
        public float BeamProgress(Target t) => t != null && extractions.TryGetValue(t, out var e) ? e.Progress01 : -1f;

        /// <summary>The fire button is the beam's (WeaponSystem.FireClaimed): while it cuts, or when it could start on the
        /// locked asteroid (the nose on it, in reach, room in the hold). Otherwise the press stays the guns': a far or missed
        /// asteroid or a full hold no longer silences them (a locked asteroid behind an enemy did, mid-fight).</summary>
        bool BeamClaimsFire() =>
            BeamMode && State == Phase.Idle && (beamOn || (Locked != null && Locked.Alive && !beamRefused && StartRefusal(Locked) == null));

        /// <summary>Metres along the ship's heading to the rock's visible surface (-1 = the heading misses it). The rock's
        /// surface is about 0.85 of the mesh's bounding radius (the hit radius is 0.7 of it).</summary>
        float AlongToRock(Target t, out float surface)
        {
            surface = t.radius / 0.7f * 0.85f;
            var oc = ship.transform.position - t.transform.position;
            float b = Vector3.Dot(oc, ship.transform.forward), disc = b * b - (oc.sqrMagnitude - surface * surface);
            return disc >= 0f ? Mathf.Max(0f, -b - Mathf.Sqrt(disc)) : -1f;
        }

        /// <summary>Why the beam can't start on 't' (null = it can; "" = the heading misses the rock: no message).</summary>
        string StartRefusal(Target t)
        {
            float along = AlongToRock(t, out _);
            if (along < 0f) return "";
            if (along / M > beamRangeUnits) return Localization.Extra("miningBeamRange", "Asteroid out of range.");
            if (BeamRoom(t) < 1) return Localization.Get(322);   // Cargo hold is full.
            return null;
        }

        /// <summary>Tons the beam may still cut into the hold from 't': a class-A rock keeps one free for its core (the minigame
        /// pays the core first; the beam pays it last).</summary>
        int BeamRoom(Target t) => Shop.FreeCargo(db) - (t.quality >= 7 ? 1 : 0);

        readonly List<Target> deadExtractions = new List<Target>();

        /// <summary>Partly cut rocks that went another way (shot, rammed, mined out by another player) leave the table.</summary>
        void PruneExtractions()
        {
            foreach (var t in extractions.Keys) if (t == null || !t.Alive) deadExtractions.Add(t);
            foreach (var t in deadExtractions) extractions.Remove(t);
            deadExtractions.Clear();
        }

        void UpdateBeam(float dtMs)
        {
            bool fire = weapons != null && weapons.FireHeld && !weapons.TurretView && !Navigation.InputHalted;
            if (!fire) { beamRefused = false; beamRangeSaid = false; }
            var target = fire && !beamRefused ? Locked : null;
            if (beamTarget != null && target != beamTarget && !beamTarget.Alive)
            {
                // Shot, rammed, or (multiplayer) mined out by another player while the beam was on it.
                string gone = GoneMessage(beamTarget);
                if (gone != null) Say(gone);
                extractions.Remove(beamTarget);
            }
            if (target == null || !target.Alive) { StopBeam(); return; }
            if (!beamOn)
            {
                // Only where it can cut (BeamClaimsFire): otherwise the guns have the press; the reason once per press.
                string refusal = StartRefusal(target);
                if (refusal != null)
                {
                    if (refusal.Length > 0 && !beamRangeSaid) { beamRangeSaid = true; Say(refusal); }
                    return;
                }
                beamOn = true;
                beamCut.Clear();
            }
            beamTarget = target;

            // The beam is fixed to the ship: straight along its heading (like the guns, the unbanked root), from the wing
            // mounts to the point ahead where that line meets the rock; it moves over the rock only as the ship turns. The
            // rock's visible surface is about 0.85 of the mesh's bounding radius (the hit radius is 0.7 of it: inside the
            // rock, where the impact, sparks and chunks were hidden). The beams end a little deeper, so they always touch it.
            var shipPos = ship.transform.position;
            var fwd = ship.transform.forward;
            var centre = target.transform.position;
            float along = AlongToRock(target, out float surface);   // metres to the rock along the nose, -1 = off it
            bool inRange = along >= 0f && along / M <= beamRangeUnits;
            if (!inRange)
            {
                // Off the rock or out of reach: the beam runs straight ahead to its full length and cuts nothing.
                if (along >= 0f && !beamRangeSaid) { beamRangeSaid = true; Say(Localization.Extra("miningBeamRange", "Asteroid out of range.")); }
                Beaming = null;
                drillSound.Stop();
                var end = shipPos + fwd * beamRangeUnits * M;
                beamFx.Aim(end, end, -fwd, false);
                return;
            }
            var contact = shipPos + fwd * along;
            var normal = (contact - centre).normalized;
            int room = BeamRoom(target);
            if (room < 1)
            {
                Say(Localization.Get(322));   // Cargo hold is full.
                beamRefused = true;
                StopBeam();
                return;
            }
            Beaming = target;
            beamFx.Aim(contact + fwd * surface * 0.2f, contact, normal, true);

            if (!extractions.TryGetValue(target, out var ex))
                extractions[target] = ex = new MiningBeamExtraction(target.quality, beamYield, beamLayerMs, Session.IsExtreme);
            int layer = ex.Layer;
            int tons = Mathf.Min(ex.Update(dtMs), room);
            for (int i = 0; i < tons; i++) Launch(target, contact, normal, target.oreItem, false);
            if (tons > 0) Session.OreTypesMined.Add(target.oreItem);
            float speed = (MiningGame.LayerSpeeds[Mathf.Clamp(ex.Layer, 0, 6)] - 5f) / 33f * 3f;
            if (!drillSound.IsPlaying) drillSound.Start(speed); else drillSound.Set(speed);
            Haptics.Rumble(0.06f + 0.02f * ex.Layer);   // like the drill on target
            if (ex.Layer != layer && !ex.Depleted) Haptics.Play(Haptics.DrillLayer);
            if (!ex.Depleted) return;

            // Every layer cut: a class-A asteroid's core comes last (if the hold has room), then the rock goes.
            if (ex.GotCore && Shop.FreeCargo(db) >= 1)   // the slot BeamRoom kept free
            {
                Launch(target, contact, normal, target.CoreItem, true);
                Session.CoreTypesMined.Add(target.CoreItem);
            }
            extractions.Remove(target);
            Haptics.Play(Haptics.MiningWon);
            StopBeam();
            // HP -1: the asteroid's explosion and sound 21, no crate; multiplayer: "Mined out by X." for the others.
            MiningOut = true;
            target.Explode();
            MiningOut = false;
        }

        /// <summary>A ton (or the core) cut: into the hold at once (PlayerEgo::stopMining's bookkeeping), the chunk flying into
        /// the ship is the look only (MiningBeamFx).</summary>
        void Launch(Target from, Vector3 contact, Vector3 normal, int item, bool core)
        {
            Shop.AddToCargo(item, 1);
            if (core) Session.CoresMined++;
            else Session.OreMined++;
            beamCut[item] = (beamCut.TryGetValue(item, out int n) ? n : 0) + 1;
            beamFx.Launch(from, contact, normal, item, core);
        }

        /// <summary>A chunk reached the ship (its ore is in the hold already).</summary>
        void OreLanded(int item, bool core) => Haptics.Play(Haptics.DrillTon);

        /// <summary>The beam off: the messages for what it cut ("12t Gold", "1t Gold Core"), the sounds off.</summary>
        void StopBeam()
        {
            Beaming = null;
            if (!beamOn) return;
            beamOn = false;
            // Remake: a fire button still held when the beam stops (mined out, lock lost, hold full) doesn't turn into
            // gunfire at the next rock: ignored until it is let go (WeaponSystem.SwallowPrimaryPress, as the approach's).
            if (weapons != null && weapons.FireHeld) weapons.SwallowPrimaryPress();
            beamTarget = null;
            beamFx?.Off();
            drillSound.Stop();
            foreach (var kv in beamCut) Say($"{kv.Value}t {GameNames.Item(kv.Key)}");
            beamCut.Clear();
            PruneExtractions();
        }

        void OnDestroy()
        {
            if (weapons != null) weapons.FireClaimed = null;
        }

        void StartBroken()
        {
            if (brokenLoop == null || brokenLoop.clip == null) return;
            brokenLoop.volume = BrokenVolume * Settings.SfxVolume;
            if (!brokenLoop.isPlaying) brokenLoop.Play();
        }

        void StopBroken()
        {
            if (brokenLoop != null && brokenLoop.isPlaying) brokenLoop.Stop();
        }

        /// <summary>MiningGame::update: FMOD event 1's parameter drill_speed = (LAYER_SPEEDS[layer] - 5) / 33 * 3.</summary>
        float DrillSpeed => Game != null ? (MiningGame.LayerSpeeds[Game.Layer] - 5f) / 33f * 3f : 0f;

        void SetExhaust(bool on)
        {
            var asm = ship.visualModel != null ? ship.visualModel.GetComponent<AssembledObject>() : null;
            if (asm?.playerVariantParts != null) foreach (var p in asm.playerVariantParts) if (p != null) p.SetActive(on);
        }

        void Say(string text) => Message?.Invoke(text);

        void Play(AudioClip clip, float volume = 1f)
        {
            if (clip != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), volume * Settings.SfxVolume);
        }
    }
}
