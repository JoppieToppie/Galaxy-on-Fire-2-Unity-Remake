// CombatRadar.cs
// The ship and salvage part of Radar::draw 0x1554fc (Reference/research/ship_combat.md 5.4, 7.1-7.4), on the player:
//   gate        only with a scanner (sort 17) mounted: without one no ship or crate locks at all
//   ship lock   a living NPC on screen inside the +-w/16 box around the crosshair, when no other lock holds it: the
//               timer runs only without an asteroid lock (+0xc), a landmark lock (+0x24), a crate on the tractor beam
//               (+0x1c), the autopilot, an asteroid approach or a docking (0x1558ae / 0x1566b2). Candidates never block
//               a ship: a landmark or planet candidate and a planet lock fill their own ring beside it, and Radar::draw
//               0x1574c0 skips the asteroid loop while a ship candidate exists, not the other way round. With an
//               asteroid locked a ship in the box freezes both (the original keeps its ship candidate, the remake none:
//               the asteroid stays locked either way). The ring fills over the scanner's attr 29 from
//               t = 0; on lock sound 26 (when the target changes). The lock is sticky: kept after the ship leaves the box
//               until it dies or another lock completes. Homing missiles fly at it (WeaponSystem.LockTarget).
//               Remake, the smarter lock (default; Settings.OriginalTargetLock = the original's): among the ships in the box
//               the hostile ones come first, then the one nearest the crosshair (the original takes the first of the
//               list, any faction), and while a hostile ship is locked and alive no neutral or friendly one becomes a
//               candidate: one crossing the box no longer steals the lock (and the missiles) from the enemy aimed at. A
//               crate in the box beats the ship already locked and the non-hostile ones (#76: the locked enemy kept the
//               tractor off its own container); a new hostile ship still comes first. The hold is its own option
//               (Settings.KeepEnemyLock, on by default; players asked for the two apart): off, the priority alone, so a
//               neutral ship can be scanned while an enemy is locked. Rules in TargetLockRules.
//               Remake, Unlock target (GameControls.UnlockTarget, U; a tap on the lock plate): the lock is dropped (the
//               missiles homing on it fly straight on: WeaponSystem.HomingLock reads the lock) and that ship is no candidate
//               again until it has left the crosshair box, so the next lock can be any other ship. The original has no unlock.
//   salvage     a crate in the box: ring after 500 ms, locked after the tractor beam's attr 24 (TractorBeam::update);
//               without a tractor beam "No tractor beam." (540). The beam (projectile_068..070 / v_194) pulls the crate at
//               10 u/ms, sound 0 loops; within 400 units it is captured (sound 4): the first non-empty cargo entry, capped
//               to the free cargo (at least 1) -> "<n>t <item>" or "Cargo hold is full." (322). Remake (#176): with no
//               room the original's crate was gone, its loot lost; now nothing is taken, the crate stays in space (not
//               locked again while it stays the one in the box), and what doesn't fit of a crate stays in it (single
//               player: in a session the rest is lost as before, the other players' copies keep the spawn's loot) and the auto modes and extra beams leave crates alone
//               while the hold is full (a mod blueprint's data crate needs no room); a living ship's steal takes nothing.
//   steal       an EMP-disabled ship with cargo (KIPlayer+0x20) in the box is a salvage candidate, not a ship lock: the
//               same ring and tractor lock time; locked, TractorBeam::update makes a container of its cargo at the ship
//               (createCrate(0)) and pulls that; captureCrate takes rnd(amount) of its first entry (at least 1, capped to
//               the free cargo), Standing::applyStealCargo (delict 2), a friend's cargo sets Level::stealFriendCargo; the
//               beam lets go when the ship dies (TractorBeam: !Player::isActive). Multiplayer: another game's NPC (a NetProxy
//               in Target.NetShips) is a steal candidate the same way (Target.NetStealable: disabled with cargo, its first
//               entry published by its owner): the crate is made here and the capture's units leave the owner's ship
//               (Target.RemoteSteal); it was a ship lock candidate, so only the orbit's authority could steal (#Discord
//               2026-10-21)
//   auto modes  Radar::Radar from the beam's attr 23: 1 (AB-3 Kingfisher) the first crate on screen is salvaged at once, 2
//               (AB-4 Octopus) any crate, even off screen and on the autopilot; only dead cargo (crates), never ships
//   remake      (for mods) a tractor beam with attr 122 "tractorTargets" N >= 2 also pulls up to N - 1 more crates by itself,
//               the nearest within attr 123 "tractorRange" (default 30000 units, 1.5 km), on screen or not, each on a beam
//               of its own (like the transfusion beams' attr 55 slots): no salvage lock, no scanner needed, and the lock stays
//               free for ships (the manual salvage, a steal or an auto mode still has the first beam; with none running the
//               extra beams take all N). Not in the turret view; a crate only while the hold has room. The
//               beam's look is its item's look (a mod's tractor beam draws its base's beam).
//   readout     a scanner with attr 31 = 1 (Ecoscan / Proscan / Ultrascan, Radar+0x1a5): a new ship lock within 24000 units
//               per axis shows the ship's first cargo entry like a pickup ("<n>t <item>", Hud::catchCargo), or "Nothing to
//               salvage." (542, HUD event 0x16) when it has no cargo list; nothing when every entry is empty. Remake: the
//               readout is white (a capture is green).

using System;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class CombatRadar : MonoBehaviour
    {
        const float M = 0.05f, CrosshairDistanceMeters = 22000f * M, PullSpeed = 10f, CaptureUnits = 400f, SalvageRingDelay = 500f;
        /// <summary>Remake-only tractor beam attributes (Modding.ItemStats "tractorTargets" / "tractorRange").</summary>
        public const int TractorTargetsAttr = 122, TractorRangeAttr = 123, MaxTractorTargets = 8;
        public const float DefaultTractorRangeUnits = 30000f;

        public bool HasScanner { get; private set; }
        /// <summary>The lock candidate: a Target (ship) or null.</summary>
        public Target Candidate { get; private set; }
        public Crate CrateCandidate { get; private set; }
        /// <summary>An EMP-disabled ship with cargo in the box (the steal): a traffic ship's Target or, in multiplayer, another
        /// game's NPC (NetProxy, Target.NetStealable).</summary>
        public Target StealCandidate { get; private set; }
        /// <summary>The sticky ship lock.</summary>
        public Target Locked { get; private set; }
        public Crate Salvaging { get; private set; }
        /// <summary>Lock ring frame 0..23, -1 = none.</summary>
        public int LockFrame { get; private set; } = -1;
        /// <summary>A ship or crate candidate exists (blocks the asteroid lock, Navigation.ShipLockActive).</summary>
        public bool Busy => Candidate != null || CrateCandidate != null || StealCandidate != null || cloudCandidate >= 0;
        /// <summary>The ship let go by Unlock: no candidate until it leaves the crosshair box (TargetLockRules).</summary>
        public Target Released { get; private set; }

        /// <summary>Remake: Unlock target. Drops the ship lock; that ship isn't locked again until it has left the crosshair
        /// box. False when no ship was locked.</summary>
        public bool Unlock()
        {
            if (Locked == null) return false;
            Released = Locked;
            Locked = null;
            if (Candidate == Released) { Candidate = null; timer = 0f; }
            if (weapons != null) weapons.LockTarget = null;
            return true;
        }
        public event Action<string, int, HudMessageKind> Message;   // text, colour (0 white, 1 red, 2 green), kind

        /// <summary>A HUD message of the level's own (SpaceLevel), through the same plate.</summary>
        public void Say(string text, int colour = 0) => Message?.Invoke(text, colour, HudMessageKind.Normal);

        Database db;
        ShipController ship;
        Navigation nav;
        Mining mining;
        WeaponSystem weapons;
        PlayerHealth health;
        Traffic traffic;
        CombatAssets assets;
        AudioSource sfx, beamLoop;
        int lockTimeMs = 8000, tractorItem = -1, tractorLockMs, tractorMode;
        bool cargoScan;
        float timer;
        bool noTractorShown;
        // Remake (#45, like Mining's "No drill installed."): salvage refused for want of a tractor beam; ignored until it leaves
        // the crosshair box.
        Crate refusedCrate;
        Target refusedSteal;
        Transform beam;
        GameObject beamPrefab;
        float beamLength = M;
        // Remake (tractorTargets): the crates the extra beams pull, each with its own beam.
        sealed class Pull { public Crate crate; public Transform beam; }
        readonly System.Collections.Generic.List<Pull> extra = new System.Collections.Generic.List<Pull>();
        int tractorTargets = 1;
        float tractorRangeUnits = DefaultTractorRangeUnits;
        /// <summary>The crates the extra beams pull now (tractorTargets).</summary>
        public int ExtraPulls => extra.Count;

        /// <summary>The orbit's gas clouds (SpaceLevel; null without): the spectral filters' lock and markers.</summary>
        [System.NonSerialized] public GasCloudField Clouds;
        /// <summary>Spectral filter (sort 33) attr 57: live gas clouds are lock targets with markers (ST-X, Omega); attr 58:
        /// also their marker off screen on the radar ellipse (Omega).</summary>
        public bool CloudRadar { get; private set; }
        public bool CloudOffScreen { get; private set; }
        /// <summary>The gas cloud locked (GasCloudField index, -1 = none): Radar+0x38, held only while it is in the box.</summary>
        public int LockedCloud { get; private set; } = -1;
        int cloudCandidate = -1;
        float cloudTimer;

        public void Setup(Database database, ShipController controller, Navigation navigation, Mining miningSystem,
                          WeaponSystem weaponSystem, PlayerHealth playerHealth, Traffic trafficManager)
        {
            db = database;
            ship = controller;
            nav = navigation;
            mining = miningSystem;
            weapons = weaponSystem;
            health = playerHealth;
            traffic = trafficManager;
            assets = CombatAssets.Load();
            var scanner = Shop.FirstMounted(db, 17);
            HasScanner = scanner != null;
            lockTimeMs = scanner != null && scanner.HasAttr(29) ? scanner.Attr(29) : 8000;
            lockTimeMs = Cheats.LockMs(lockTimeMs);
            cargoScan = scanner != null && scanner.Attr(31) == 1;
            var filter = Shop.FirstMounted(db, 33);
            CloudRadar = filter != null && filter.Attr(57) == 1;
            CloudOffScreen = filter != null && filter.Attr(58) == 1;
            var tractor = Shop.FirstMounted(db, 13);
            if (tractor != null)
            {
                tractorItem = tractor.index;
                tractorLockMs = tractor.Attr(24);
                tractorMode = tractor.Attr(23);
                tractorTargets = TractorTargets(tractor);
                tractorRangeUnits = tractor.Attr(TractorRangeAttr, (int)DefaultTractorRangeUnits);
            }
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            beamLoop = gameObject.AddComponent<AudioSource>();
            beamLoop.playOnAwake = false;
            beamLoop.loop = true;
            beamLoop.clip = GoF2Remake.Modding.ModSounds.Get(assets != null ? assets.tractorLoop : null);
            beamPrefab = assets != null && tractor != null ? assets.Tractor(tractor.Look) : null;   // a mod's beam: its base's look
            beam = MakeBeam();
            if (beam != null)
            {
                // The mesh is +-250 units wide and 1 unit (0.05 m) long.
                beamLength = 0f;
                foreach (var mf in beam.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) beamLength = Mathf.Max(beamLength, mf.sharedMesh.bounds.size.z);
                if (beamLength <= 0f) beamLength = M;
            }
        }

        /// <summary>The tractor beam's crate count (attr 122, 1..MaxTractorTargets; 1 = the original's one beam).</summary>
        public static int TractorTargets(ItemData tractor) =>
            tractor == null ? 0 : Mathf.Clamp(tractor.Attr(TractorTargetsAttr, 1), 1, MaxTractorTargets);

        Transform MakeBeam()
        {
            if (beamPrefab == null) return null;
            var go = Instantiate(beamPrefab);
            go.name = "Tractor beam";
            GunRig.StripForFx(go);
            GunRig.EnableFades(go);   // its keys pulse the width 1..1.5 and the `extra` opacity 25..50 %
            go.SetActive(false);
            return go.transform;
        }

        void OnDestroy()
        {
            if (beam != null) Destroy(beam.gameObject);
            for (int i = extra.Count - 1; i >= 0; i--) ReleaseExtra(i);
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            // KIPlayer::isDying / isDead clear the ship lock (+4); a jumper flying off (KIPlayer::setDead) counts too: the
            // remake only switches it off, hull intact, and its "<race> 100%" plate stayed with no ship (#28).
            // Remake: a ship gone off the lock (another player cloaking, a hidden proxy) loses a lock already held too; the
            // missiles homing on it lose theirs for good (Gun).
            if (Locked != null && (!Locked.Alive || !Locked.gameObject.activeInHierarchy || Locked.untargetable || Locked.cloaked)) Locked = null;
            if (weapons != null) weapons.LockTarget = Locked;
            // PlayerEgo::isInTurretMode: the turret view neither locks ships nor keeps the tractor beam pulling.
            bool turretView = weapons != null && weapons.TurretView;
            if (turretView && Salvaging != null) { Salvaging.pulled = false; Salvaging = null; }
            UpdateSalvage(dtMs);
            UpdateExtraBeams(dtMs, !turretView && health != null && !health.Dead && nav != null && !nav.Jumping,
                             nav != null && !nav.Paused && !nav.Conversation && !nav.MenuOpen && !nav.RadarOff);
            // MGame::OnRender2D: no Radar::draw in a cutscene or the fly-in, so no ship / crate / cloud lock (nor its sound).
            if (!HasScanner || health == null || health.Dead || nav == null || nav.Paused || nav.Conversation || nav.MenuOpen || turretView || nav.RadarOff) { Candidate = null; CrateCandidate = null; StealCandidate = null; LockFrame = -1; UpdateCloudLock(-1, 0f); Publish(); return; }
            // Radar+0x1ab (AB-4): any crate, wherever it is, even on the autopilot.
            if (tractorMode == 2 && Salvaging == null && !nav.Jumping) AutoSalvage(Camera.main, false);

            // Radar::draw 0x156900: the ship lock runs unless docking to an asteroid (the approach and mining); a locked
            // asteroid (+0xc) doesn't stop it: a ship in the box wins and the asteroid lock waits (Navigation.ShipLockActive).
            bool blocked = nav.Autopilot || nav.Jumping || nav.LandmarkLocked || Salvaging != null
                           || (mining != null && mining.State != Mining.Phase.Idle);
            Target best = null;
            Crate bestCrate = null;
            Target bestSteal = null;
            int bestCloud = -1;
            var cam = Camera.main;
            // Radar+0x1aa (AB-3): the first crate on screen, no box and no lock time.
            if (!blocked && cam != null && tractorMode == 1 && Salvaging == null) AutoSalvage(cam, true);
            if (!blocked && cam != null)
            {
                var c = cam.WorldToScreenPoint(ChaseCamera.AimOrigin(transform) + transform.forward * CrosshairDistanceMeters);
                float box = Screen.width / 16f, bestD = float.MaxValue, stealD = float.MaxValue;
                if (c.z > 0f)
                {
                    // Remake: the smarter lock (see the header): hostile first, then the nearest to the crosshair; no
                    // non-hostile candidate while a hostile ship is locked.
                    bool smart = !Settings.OriginalTargetLock;
                    bool keepHostile = TargetLockRules.HoldsEnemy(smart, Settings.KeepEnemyLock, Locked != null && Locked.Alive && Locked.hostileToPlayer);
                    // Unlock target: the ship let go stays out until it leaves the box.
                    if (Released != null && !TargetLockRules.StillReleased(Released.Alive && Released.isActiveAndEnabled, InBox(cam, c, box, Released.transform.position, out _))) Released = null;
                    float bestScore = float.MaxValue;
                    bool StealingFrom(Target t) => Salvaging != null && (Salvaging.stolenFromRemote == t || (Salvaging.stolenFrom != null && Salvaging.stolenFrom.Target == t));
                    bool Better(Target t)
                    {
                        if (!TargetLockRules.Eligible(keepHostile, t.hostileToPlayer, t == Released)) return false;
                        var p = cam.WorldToScreenPoint(t.transform.position);
                        float score = TargetLockRules.Score(p.x - c.x, p.y - c.y, t.hostileToPlayer);
                        if (score >= bestScore) return false;
                        bestScore = score;
                        return true;
                    }
                    // PlayerJunk objects: Level::createMission (Junk removal) puts them in the ship list before the pirates,
                    // so the first in the box is the junk, not a ship behind it (#28).
                    foreach (var o in Target.RadarObjects)
                        if (o != null && o.Alive && !o.untargetable && o != Released && InBox(cam, c, box, o.transform.position, out float dj))
                        {
                            if (!smart) { best = o; break; }
                            if (Better(o)) best = o;
                        }
                    // The ship loop skips inactive players (Player::isActive): no lock on a sleeper, which has no marker.
                    if (traffic != null)
                        foreach (var s in traffic.Ships)
                        {
                            // Radar::draw: a fighter dying with cargo (KIPlayer+0x48) is salvage until it explodes.
                            if (s.DyingWithCargo && !s.Gone && !s.Hidden && !s.RadarHidden)
                            {
                                if (InBox(cam, c, box, s.transform.position, out float dd) && dd < stealD) { stealD = dd; bestSteal = s.Target; }
                                continue;
                            }
                            // KIPlayer untargetable (a Rhino's / Myfft's turret, Level::createFighterTurrets): no lock; it was
                            // locked, dropped and locked again every frame, so the Rhino under it couldn't be scanned.
                            if (s.Gone || !s.Target.Alive || s.Hidden || s.RadarHidden || s.OffersDocking || s.Asleep || s.Target.untargetable) continue;
                            if (!InBox(cam, c, box, s.transform.position, out float d)) continue;
                            // KIPlayer+0x20: a disabled ship with cargo is salvage (it wins over the ship locks).
                            if (s.Hp.empDisabled && s.HasCargo) { if (d < stealD && !StealingFrom(s.Target)) { stealD = d; bestSteal = s.Target; } }
                            else if (smart) { if (Better(s.Target)) best = s.Target; }
                            else if (best == null && s.Target != Released) best = s.Target;   // Radar::draw: the first ship of the list in the box
                        }
                    // Multiplayer: the other players and the host's ships (on a client), after the traffic's. Another game's
                    // EMP-disabled NPC with cargo is a steal like a traffic ship (it was a ship lock candidate here, so only
                    // the player whose game ran the orbit's NPCs could steal: Discord report 2026-10-21).
                    foreach (var o in Target.NetShips)
                    {
                        if (o == null || !o.Alive || o.untargetable || !InBox(cam, c, box, o.transform.position, out float dn)) continue;
                        if (o.NetStealable) { if (dn < stealD && !StealingFrom(o)) { stealD = dn; bestSteal = o; } continue; }
                        if (bestSteal != null || o == Released) continue;
                        if (!smart) { if (best == null) best = o; continue; }   // the first in the list, after the traffic's
                        if (Better(o)) best = o;
                    }
                    if (bestSteal != null) best = null;
                    // Remake, the smarter lock (#76): a crate in the box also beats the ship already locked (that lock stays,
                    // there is nothing to lock) and a non-hostile one; only a new hostile ship still comes first. Before, the
                    // locked enemy, usually in the box beside the container it dropped, kept the tractor beam off it.
                    bool crateFirst = smart && (best == null || best == Locked || !best.hostileToPlayer);
                    if ((best == null || crateFirst) && bestSteal == null)
                    {
                        bestD = float.MaxValue;
                        foreach (var cr in Crate.All)
                            if (cr != Salvaging && !cr.pulled && !cr.claimedByOther && InBox(cam, c, box, cr.transform.position, out float d) && d < bestD) { bestD = d; bestCrate = cr; }
                        if (bestCrate != null) best = null;
                    }
                    // Radar::draw 0x1570ec, a spectral filter with attr 57: a live gas cloud in the box is a lock candidate
                    // (Radar+0x3c) when nothing else is.
                    if (CloudRadar && Clouds != null && best == null && bestCrate == null && bestSteal == null)
                    {
                        float cd = float.MaxValue;
                        for (int i = 0; i < Clouds.Count; i++)
                            if (Clouds.IsLive(i) && InBox(cam, c, box, Clouds.PositionOf(i), out float d) && d < cd) { cd = d; bestCloud = i; }
                    }
                }
            }
            if (bestCrate != null && bestCrate == refusedCrate) bestCrate = null; else refusedCrate = null;
            if (bestSteal != null && bestSteal == refusedSteal) bestSteal = null; else refusedSteal = null;
            if (best != Candidate || bestCrate != CrateCandidate || bestSteal != StealCandidate)
            {
                Candidate = best; CrateCandidate = bestCrate; StealCandidate = bestSteal; timer = 0f; noTractorShown = false;
            }
            LockFrame = -1;
            if (Candidate != null)
            {
                timer += dtMs;
                // Radar::draw: the ring only while the candidate isn't the lock already.
                if (Candidate != Locked) LockFrame = Mathf.Min(23, (int)(23f * timer / Mathf.Max(1, lockTimeMs)));
                if (timer > lockTimeMs && Locked != Candidate)
                {
                    Locked = Candidate;
                    if (assets != null && assets.targetLock != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(assets.targetLock), Settings.SfxVolume);
                    Haptics.Play(Haptics.TargetLock);   // remake
                    if (cargoScan) ReadCargo(Locked);
                }
            }
            else if (CrateCandidate != null || StealCandidate != null)
            {
                timer += dtMs;
                int lt = Mathf.Max(tractorLockMs, (int)SalvageRingDelay + 1);
                if (timer > SalvageRingDelay) LockFrame = Mathf.Min(23, (int)(23f * (timer - SalvageRingDelay) / (lt - SalvageRingDelay)));
                if (timer > lt)
                {
                    if (tractorItem < 0)
                    {
                        // Radar::draw: 540 "No tractor beam."; remake: once, and the salvage lock is dropped (the ring goes)
                        // instead of staying full.
                        if (!noTractorShown) { noTractorShown = true; Message?.Invoke(Localization.Get(540), 0, HudMessageKind.Normal); }
                        refusedCrate = CrateCandidate;
                        refusedSteal = StealCandidate;
                        CrateCandidate = null;
                        StealCandidate = null;
                        LockFrame = -1;
                    }
                    else if (Salvaging == null)
                    {
                        var crate = CrateCandidate != null ? CrateCandidate : CreateStealCrate(StealCandidate);
                        if (crate != null) StartSalvage(crate);
                        CrateCandidate = null;
                        StealCandidate = null;
                    }
                }
            }
            UpdateCloudLock(bestCloud, dtMs);
            Publish();
        }

        /// <summary>Radar::draw's gas cloud lock: the candidate's timer runs while it stays in the box; past the scanner's lock
        /// time - 200 ms it is locked (sound 0x1a once), the ring filling from 500 ms and full while locked; it lets go as the
        /// cloud leaves the box or bursts. Only a display aid (Radar::getLockedGasCloud has no caller): the plate's "Gas cloud"
        /// (3236) and the distance under its marker (CombatView).</summary>
        void UpdateCloudLock(int cloud, float dtMs)
        {
            if (cloud < 0 || Candidate != null || CrateCandidate != null || StealCandidate != null)
            {
                cloudCandidate = -1; cloudTimer = 0f; LockedCloud = -1;
                return;
            }
            if (cloud != cloudCandidate) { cloudCandidate = cloud; cloudTimer = 0f; LockedCloud = -1; }
            cloudTimer += dtMs;
            int lt = Mathf.Max(501, lockTimeMs - 200);
            if (cloudTimer > lt && LockedCloud != cloud)
            {
                LockedCloud = cloud;
                if (assets != null && assets.targetLock != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(assets.targetLock), Settings.SfxVolume);
                Haptics.Play(Haptics.TargetLock);
            }
            if (cloudTimer > SalvageRingDelay)
                LockFrame = LockedCloud == cloud ? 23 : Mathf.Min(23, (int)(23f * (cloudTimer - SalvageRingDelay) / (lt - SalvageRingDelay)));
        }

        /// <summary>Radar::draw on a new lock with Radar+0x1a5: the ship's first cargo entry. Its 24000-unit test (0x156916)
        /// is dx &lt; 24000 || dx &gt; -24000 || ..., always true, so it reads at any distance (the remake kept the box: a lock
        /// completed farther out, and sticky, never read the ship at all; "intermittently nothing").</summary>
        void ReadCargo(Target locked)
        {
            if (locked == null || traffic == null) return;
            var ship = traffic.Ships.Find(s => s != null && s.Target == locked);
            if (ship == null) return;
            var cargo = ship.CargoList;
            if (cargo == null || cargo.Count == 0) { Message?.Invoke(Localization.Get(542), 0, HudMessageKind.Scan); return; }
            for (int i = 0; i < cargo.Count; i++)
                if (cargo[i].amount > 0) { Message?.Invoke($"{cargo[i].amount}t {GameNames.Item(cargo[i].item)}", 0, HudMessageKind.Scan); return; }
        }

        /// <summary>TractorBeam::update's steal: KIPlayer::createCrate(0) at the ship, a container of its cargo, pulled at once:
        /// a traffic ship makes its own (NpcShip.CreateStealCrate); another game's NPC (multiplayer) one here holding its first
        /// cargo entry as published (NetProxy), the capture taking the stolen units off the owner's ship (Target.RemoteSteal).</summary>
        Crate CreateStealCrate(Target t)
        {
            if (t == null) return null;
            var npc = t.GetComponent<NpcShip>();
            if (npc != null) return npc.CreateStealCrate();
            if (!t.NetStealable) return null;
            var c = Crate.Spawn(t.transform.position, new[] { new ItemStack(t.netStealItem, t.netStealAmount) }, t.race, Crate.LookContainer);
            c.name = "Stolen cargo";
            c.stolenFromRemote = t;
            c.fromFriend = t.friendToPlayer;
            c.pulled = true;
            return c;
        }

        void StartSalvage(Crate crate)
        {
            crate.PullStarted?.Invoke();   // multiplayer: the claim
            if (crate.pulled) ReleaseExtra(extra.FindIndex(p => p.crate == crate));   // an extra beam had it: the lock's beam takes over
            Salvaging = crate;
            Salvaging.pulled = true;
            if (beamLoop.clip != null && !beamLoop.isPlaying) beamLoop.Play();
        }

        /// <summary>Remake (tractorTargets): the extra beams. Each pulls its crate in like the main beam and captures it; free
        /// beams take the nearest crates within reach ('pick': not paused, in a menu or a cutscene). 'keep' false (the turret
        /// view, death, a jump) lets them all go.</summary>
        void UpdateExtraBeams(float dtMs, bool keep, bool pick)
        {
            for (int i = extra.Count - 1; i >= 0; i--)
            {
                var p = extra[i];
                var c = p.crate;
                if (!keep || c == null || c.claimedByOther || c == Salvaging || !c.pulled) { ReleaseExtra(i); continue; }
                if (!PullIn(c, p.beam, dtMs) || c.captureBlocked) continue;
                extra.RemoveAt(i);
                if (p.beam != null) Destroy(p.beam.gameObject);
                Capture(c);
            }
            if (keep && pick && tractorItem >= 0 && tractorTargets > 1)
            {
                int free = tractorTargets - (Salvaging != null ? 1 : 0) - extra.Count;
                bool room = Shop.FreeCargo(db) > 0;
                float reach2 = tractorRangeUnits * M * tractorRangeUnits * M;
                while (free-- > 0)
                {
                    Crate pickCrate = null;
                    float best = reach2;
                    foreach (var cr in Crate.All)
                    {
                        if (cr == null || cr.pulled || cr == Salvaging || cr.Stolen || cr.claimedByOther || !cr.HasLoot) continue;
                        if (!room && NeedsRoom(cr)) continue;   // it would only come back: the hold has no room for it
                        float d = (cr.transform.position - transform.position).sqrMagnitude;
                        if (d < best) { best = d; pickCrate = cr; }
                    }
                    if (pickCrate == null) break;
                    pickCrate.PullStarted?.Invoke();   // multiplayer: the claim
                    pickCrate.pulled = true;
                    extra.Add(new Pull { crate = pickCrate, beam = MakeBeam() });
                }
            }
            UpdateBeamLoop();
        }

        void ReleaseExtra(int i)
        {
            if (i < 0 || i >= extra.Count) return;
            var p = extra[i];
            extra.RemoveAt(i);
            if (p.crate != null && p.crate != Salvaging) p.crate.pulled = false;
            if (p.beam != null) Destroy(p.beam.gameObject);
        }

        void UpdateBeamLoop()
        {
            bool on = Salvaging != null || extra.Count > 0;
            if (on && beamLoop.clip != null && !beamLoop.isPlaying) beamLoop.Play();
            else if (!on && beamLoop.isPlaying) beamLoop.Stop();
        }

        /// <summary>TractorBeam::update's pull: the crate toward the ship at 10 u/ms, the beam from the ship to it; true once it
        /// is within the capture distance (400 units).</summary>
        bool PullIn(Crate crate, Transform beamTransform, float dtMs)
        {
            var to = transform.position - crate.transform.position;
            float dist = to.magnitude;
            if (dist / M < CaptureUnits) return true;
            crate.transform.position += to / dist * Mathf.Min(dist, PullSpeed * dtMs * M);
            if (beamTransform != null)
            {
                beamTransform.gameObject.SetActive(true);
                beamTransform.position = transform.position;
                beamTransform.rotation = Quaternion.LookRotation(-to, transform.up);
                // TractorBeam::update 0x17c16c: setScaling(beam, 0.5, 0.5, distance): 250 units wide, exactly as long as the distance.
                beamTransform.localScale = new Vector3(0.5f, 0.5f, dist / beamLength);
            }
            return false;
        }

        /// <summary>Radar::draw's auto modes: the nearest crate on screen (mode 1) or anywhere (mode 2) is locked at once.</summary>
        void AutoSalvage(Camera cam, bool onScreenOnly)
        {
            Crate pick = null;
            float best = float.MaxValue;
            bool room = Shop.FreeCargo(db) > 0;
            foreach (var cr in Crate.All)
            {
                if (cr.Stolen || !cr.HasLoot || cr.claimedByOther || (!room && NeedsRoom(cr))) continue;
                if (onScreenOnly)
                {
                    if (cam == null) return;
                    var p = cam.WorldToScreenPoint(cr.transform.position);
                    if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height) continue;
                }
                float d = (cr.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; pick = cr; }
            }
            if (pick != null) StartSalvage(pick);
        }

        void Publish()
        {
            if (nav != null) nav.ShipLockActive = Busy || Salvaging != null;   // Radar+8 / +0x1c
        }

        static bool InBox(Camera cam, Vector3 crosshair, float box, Vector3 world, out float dist)
        {
            dist = 0f;
            var p = cam.WorldToScreenPoint(world);
            if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height) return false;
            if (Mathf.Abs(p.x - crosshair.x) >= box || Mathf.Abs(p.y - crosshair.y) >= box) return false;
            dist = p.z;
            return true;
        }

        /// <summary>TractorBeam::update: pull the crate in, capture it within 400 units.</summary>
        void UpdateSalvage(float dtMs)
        {
            if (Salvaging == null)
            {
                if (beam != null && beam.gameObject.activeSelf) beam.gameObject.SetActive(false);
                if (beamLoop.isPlaying && extra.Count == 0) beamLoop.Stop();
                return;
            }
            // TractorBeam::update lets go when the ship is gone (Player::isActive); a dying one keeps its crate coming, which
            // becomes the ship's crate when it explodes (NpcShip.DropCrate).
            var remote = Salvaging.stolenFromRemote;
            if ((Salvaging.stolenFrom != null && ((!Salvaging.stolenFrom.Target.Alive && !Salvaging.stolenFrom.Dying) || Salvaging.stolenFrom.Gone))
                || (remote != null && (!remote.Alive || !remote.isActiveAndEnabled || !remote.NetStealable)))
            {
                Destroy(Salvaging.gameObject);
                Salvaging = null;
                return;
            }
            // Multiplayer: another player got it first (NetCrate's claim).
            if (Salvaging.claimedByOther) { Salvaging.pulled = false; Salvaging = null; return; }
            if (PullIn(Salvaging, beam, dtMs) && !Salvaging.captureBlocked) Capture(Salvaging);   // blocked: waits for the claim
        }

        /// <summary>KIPlayer::captureCrate 0xb2a00: the crate is gone once captured (KIPlayer+0x74 = 0) and a dead ship leaves no
        /// other (+0x48 = 0). Its first non-empty entry: all of it from a wreck, rnd(amount) from a living ship, at least 1,
        /// capped to the free cargo. Every ship's crate costs standing (Standing::applyStealCargo, delict 2) and a friend's is a
        /// stolen cargo (Level::stealFriendCargo). No room: the unit is lost ("Cargo hold full"); remake (#176): nothing is
        /// taken, the crate stays in space for another try (a living ship's steal: its crate goes, the ship keeps its
        /// cargo), and no standing is lost for it; what doesn't fit stays in the crate (keepRest). Missiles of a mounted launcher's type reload it (Item::changeAmount), the rest is cargo.</summary>
        /// <summary>Its loot goes in the hold (not a mod blueprint's data crate, which is only learnt).</summary>
        static bool NeedsRoom(Crate crate)
        {
            var entry = crate.loot.Find(s => s.amount > 0);
            return entry == null || GoF2Remake.Modding.ModBlueprints.Of(entry.item) == null;
        }

        void Capture(Crate crate)
        {
            if (crate == Salvaging) Salvaging = null;
            UpdateBeamLoop();
            if (assets != null && assets.tractorClose != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(assets.tractorClose), Settings.SfxVolume);
            var entry = crate.loot.Find(s => s.amount > 0);
            if (entry == null) { Destroy(crate.gameObject); return; }
            if (GoF2Remake.Modding.ModBlueprints.Of(entry.item) != null)
            {
                // Remake mods: a data crate (a mod blueprint's drop or derelict): the blueprint is learnt, nothing goes in
                // the hold.
                bool known = Blueprints.IsUnlocked(entry.item);
                Blueprints.Unlock(entry.item);
                Message?.Invoke(string.Format(known ? Localization.Extra("bpKnown", "Blueprint already known: {0}")
                                                    : Localization.Extra("bpFound", "Blueprint found: {0}"), GameNames.Item(entry.item)), 2, HudMessageKind.Normal);
                crate.CapturedHere?.Invoke();
                Destroy(crate.gameObject);
                return;
            }
            int free = Shop.FreeCargo(db);
            var ship = crate.stolenFrom;
            // KIPlayer::captureCrate: rnd(amount) only from a living ship; a dying one gives it all and leaves no crate.
            var remoteShip = crate.stolenFromRemote;
            if (free <= 0)
            {
                Message?.Invoke(Localization.Get(322), 1, HudMessageKind.Normal);
                if ((ship != null && ship.Target.Alive) || remoteShip != null)
                {
                    refusedSteal = ship != null ? ship.Target : remoteShip;
                    Destroy(crate.gameObject);   // made for the steal (CreateStealCrate): the cargo stays on the ship
                }
                else
                {
                    crate.pulled = false;   // multiplayer: the claim goes back (NetCrate.TrackPull)
                    refusedCrate = crate;
                }
                return;
            }
            // Remake (#176): what doesn't fit stays in a crate in space (the original's crate was gone with it). Not a steal's
            // (the rest stays on the ship) and not in a session: the other players' copies hold the loot the crate spawned
            // with (NetCrate), so a part left here could be taken there in full.
            bool keepRest = !crate.Stolen && !NetSession.Active;
            int want = (ship != null && ship.Target.Alive) || remoteShip != null ? UnityEngine.Random.Range(0, entry.amount) : entry.amount;
            if (ship != null && !ship.Target.Alive) ship.LootTaken();
            int n = Mathf.Max(1, Mathf.Min(want, free));
            n = Mathf.Min(n, entry.amount);
            bool friend = ship != null ? ship.Target.friendToPlayer : crate.fromFriend;
            if (friend && traffic != null) traffic.FriendCargoStolen = true;   // Level::stealFriendCargo
            Standing.ApplyDelict(crate.race, 2);                               // Standing::applyStealCargo
            entry.amount -= n;
            ship?.StealFrom(entry.item, n);
            remoteShip?.RemoteSteal?.Invoke(entry.item, n);   // multiplayer: off the owner's ship (NetProxy)
            var mounted = db.Item(entry.item)?.TypeId == 1 ? Session.Equipment.Find(e => e.item == entry.item) : null;
            if (mounted != null) mounted.amount += n; else Shop.AddToCargo(entry.item, n);
            Session.CratesSalvaged += n;   // Status::crateCaptured
            if (crate.missionCrate && Freelance.Active)
            {
                Session.Unsaleable.Add(entry.item);   // the client's container (also a squadmate's capture, multiplayer)
                GoF2Remake.World.SpaceHooks.MissionStatus(Freelance.Mission, 1);   // multiplayer: the squad has it
            }
            // A Void crate counts for Alien Hunter (Status+0xcc), another race's booze for Barkeeper.
            if (crate.race == Standing.Void) Session.AlienRemainsCollected += n;
            else if (Session.IsBooze(entry.item)) Session.BoozeTypes.Add(entry.item);
            Message?.Invoke($"{n}t {GameNames.Item(entry.item)}", 2, HudMessageKind.Gain);
            // Remake: a crate of several stacks (the remake's own: a capital ship's, a pirate outpost's by tier; the
            // original's crates hold one) gives every one of them, as far as the hold has room (it used to give the first).
            if (!crate.Stolen && !crate.missionCrate)
                foreach (var more in crate.loot)
                {
                    if (more == entry || more.amount <= 0) continue;
                    int room = Shop.FreeCargo(db);
                    if (room <= 0) { if (!keepRest) Message?.Invoke(Localization.Get(322), 1, HudMessageKind.Normal); break; }
                    int k = Mathf.Min(more.amount, room);
                    more.amount -= k;
                    var mountedMore = db.Item(more.item)?.TypeId == 1 ? Session.Equipment.Find(e => e.item == more.item) : null;
                    if (mountedMore != null) mountedMore.amount += k; else Shop.AddToCargo(more.item, k);
                    Session.CratesSalvaged += k;
                    if (crate.race == Standing.Void) Session.AlienRemainsCollected += k;
                    else if (Session.IsBooze(more.item)) Session.BoozeTypes.Add(more.item);
                    Message?.Invoke($"{k}t {GameNames.Item(more.item)}", 2, HudMessageKind.Gain);
                }
            if (keepRest && crate.HasLoot)
            {
                Message?.Invoke(Localization.Get(322), 1, HudMessageKind.Normal);
                crate.pulled = false;
                refusedCrate = crate;
                return;
            }
            crate.CapturedHere?.Invoke();   // multiplayer: the capture reaches the crate's owner (NetCrate)
            Destroy(crate.gameObject);
        }
    }
}
