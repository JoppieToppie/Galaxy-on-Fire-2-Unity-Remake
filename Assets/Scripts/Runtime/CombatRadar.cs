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
//   salvage     a crate in the box: ring after 500 ms, locked after the tractor beam's attr 24 (TractorBeam::update);
//               without a tractor beam "No tractor beam." (540). The beam (projectile_068..070 / v_194) pulls the crate at
//               10 u/ms, sound 0 loops; within 400 units it is captured (sound 4): the first non-empty cargo entry, capped
//               to the free cargo (at least 1) -> "<n>t <item>" or "Cargo hold is full." (322).
//   steal       an EMP-disabled ship with cargo (KIPlayer+0x20) in the box is a salvage candidate, not a ship lock: the
//               same ring and tractor lock time; locked, TractorBeam::update makes a container of its cargo at the ship
//               (createCrate(0)) and pulls that; captureCrate takes rnd(amount) of its first entry (at least 1, capped to
//               the free cargo), Standing::applyStealCargo (delict 2), a friend's cargo sets Level::stealFriendCargo; the
//               beam lets go when the ship dies (TractorBeam: !Player::isActive)
//   auto modes  Radar::Radar from the beam's attr 23: 1 (AB-3 Kingfisher) the first crate on screen is salvaged at once, 2
//               (AB-4 Octopus) any crate, even off screen and on the autopilot; only dead cargo (crates), never ships
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

        public bool HasScanner { get; private set; }
        /// <summary>The lock candidate: a Target (ship) or null.</summary>
        public Target Candidate { get; private set; }
        public Crate CrateCandidate { get; private set; }
        /// <summary>An EMP-disabled ship with cargo in the box (the steal).</summary>
        public NpcShip StealCandidate { get; private set; }
        /// <summary>The sticky ship lock.</summary>
        public Target Locked { get; private set; }
        public Crate Salvaging { get; private set; }
        /// <summary>Lock ring frame 0..23, -1 = none.</summary>
        public int LockFrame { get; private set; } = -1;
        /// <summary>A ship or crate candidate exists (blocks the asteroid lock, Navigation.ShipLockActive).</summary>
        public bool Busy => Candidate != null || CrateCandidate != null || StealCandidate != null;
        public event Action<string, int> Message;   // text, colour (0 white, 1 red, 2 green)

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
        Transform beam;
        float beamLength = M;

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
            var tractor = Shop.FirstMounted(db, 13);
            if (tractor != null) { tractorItem = tractor.index; tractorLockMs = tractor.Attr(24); tractorMode = tractor.Attr(23); }
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            beamLoop = gameObject.AddComponent<AudioSource>();
            beamLoop.playOnAwake = false;
            beamLoop.loop = true;
            beamLoop.clip = assets != null ? assets.tractorLoop : null;
            var beamPrefab = assets != null && tractorItem >= 0 ? assets.Tractor(tractorItem) : null;
            if (beamPrefab != null)
            {
                var go = Instantiate(beamPrefab);
                go.name = "Tractor beam";
                GunRig.StripForFx(go);
                GunRig.EnableFades(go);   // its keys pulse the width 1..1.5 and the `extra` opacity 25..50 %
                // The mesh is +-250 units wide and 1 unit (0.05 m) long.
                beamLength = 0f;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) beamLength = Mathf.Max(beamLength, mf.sharedMesh.bounds.size.z);
                if (beamLength <= 0f) beamLength = M;
                beam = go.transform;
                go.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (beam != null) Destroy(beam.gameObject);
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            if (Locked != null && !Locked.Alive) Locked = null;
            if (weapons != null) weapons.LockTarget = Locked;
            // PlayerEgo::isInTurretMode: the turret view neither locks ships nor keeps the tractor beam pulling.
            bool turretView = weapons != null && weapons.TurretView;
            if (turretView && Salvaging != null) { Salvaging.pulled = false; Salvaging = null; }
            UpdateSalvage(dtMs);
            if (!HasScanner || health == null || health.Dead || nav == null || nav.Paused || nav.MenuOpen || turretView) { Candidate = null; CrateCandidate = null; StealCandidate = null; LockFrame = -1; Publish(); return; }
            // Radar+0x1ab (AB-4): any crate, wherever it is, even on the autopilot.
            if (tractorMode == 2 && Salvaging == null && !nav.Jumping) AutoSalvage(Camera.main, false);

            bool blocked = nav.Autopilot || nav.Jumping || nav.LandmarkLocked || Salvaging != null
                           || (mining != null && (mining.State != Mining.Phase.Idle || mining.Locked != null));
            Target best = null;
            Crate bestCrate = null;
            NpcShip bestSteal = null;
            var cam = Camera.main;
            // Radar+0x1aa (AB-3): the first crate on screen, no box and no lock time.
            if (!blocked && cam != null && tractorMode == 1 && Salvaging == null) AutoSalvage(cam, true);
            if (!blocked && cam != null)
            {
                var c = cam.WorldToScreenPoint(transform.position + transform.forward * CrosshairDistanceMeters);
                float box = Screen.width / 16f, bestD = float.MaxValue, stealD = float.MaxValue;
                if (c.z > 0f)
                {
                    if (traffic != null)
                        foreach (var s in traffic.Ships)
                        {
                            if (s.Gone || !s.Target.Alive || s.Hidden || s.RadarHidden || s.DockingType > 0) continue;
                            if (!InBox(cam, c, box, s.transform.position, out float d)) continue;
                            // KIPlayer+0x20: a disabled ship with cargo is salvage (it wins over the ship locks).
                            if (s.Hp.empDisabled && s.HasCargo) { if (d < stealD && (Salvaging == null || Salvaging.stolenFrom != s)) { stealD = d; bestSteal = s; } }
                            else if (best == null) best = s.Target;   // Radar::draw: the first ship of the list in the box
                        }
                    // Multiplayer: the other players and the host's ships (on a client), after the traffic's.
                    if (best == null && bestSteal == null)
                        foreach (var o in Target.NetShips)
                            if (o != null && o.Alive && !o.untargetable && InBox(cam, c, box, o.transform.position, out float dn)) { best = o; break; }
                    // PlayerJunk objects are in the original's ship list too: lockable after the ships.
                    if (best == null && bestSteal == null)
                        foreach (var o in Target.RadarObjects)
                            if (o != null && o.Alive && !o.untargetable && InBox(cam, c, box, o.transform.position, out float dj)) { best = o; break; }
                    if (bestSteal != null) best = null;
                    if (best == null && bestSteal == null)
                    {
                        bestD = float.MaxValue;
                        foreach (var cr in FindObjectsByType<Crate>(FindObjectsInactive.Exclude))
                            if (cr != Salvaging && !cr.claimedByOther && InBox(cam, c, box, cr.transform.position, out float d) && d < bestD) { bestD = d; bestCrate = cr; }
                    }
                }
            }
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
                    if (assets != null && assets.targetLock != null) sfx.PlayOneShot(assets.targetLock, Settings.SfxVolume);
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
                    if (tractorItem < 0) { if (!noTractorShown) { noTractorShown = true; Message?.Invoke(Localization.Get(540), 0); } }
                    else if (Salvaging == null)
                    {
                        var crate = CrateCandidate != null ? CrateCandidate : StealCandidate.CreateStealCrate();
                        if (crate != null) StartSalvage(crate);
                        CrateCandidate = null;
                        StealCandidate = null;
                    }
                }
            }
            Publish();
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
            if (cargo == null || cargo.Count == 0) { Message?.Invoke(Localization.Get(542), 0); return; }
            for (int i = 0; i < cargo.Count; i++)
                if (cargo[i].amount > 0) { Message?.Invoke($"{cargo[i].amount}t {Localization.Get(1274 + cargo[i].item)}", 0); return; }
        }

        void StartSalvage(Crate crate)
        {
            crate.PullStarted?.Invoke();   // multiplayer: the claim
            Salvaging = crate;
            Salvaging.pulled = true;
            if (beamLoop.clip != null && !beamLoop.isPlaying) beamLoop.Play();
        }

        /// <summary>Radar::draw's auto modes: the nearest crate on screen (mode 1) or anywhere (mode 2) is locked at once.</summary>
        void AutoSalvage(Camera cam, bool onScreenOnly)
        {
            Crate pick = null;
            float best = float.MaxValue;
            foreach (var cr in FindObjectsByType<Crate>(FindObjectsInactive.Exclude))
            {
                if (cr.stolenFrom != null || !cr.HasLoot || cr.claimedByOther) continue;
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
                if (beamLoop.isPlaying) beamLoop.Stop();
                return;
            }
            // TractorBeam::update: a living ship's cargo is let go when the ship dies.
            if (Salvaging.stolenFrom != null && (!Salvaging.stolenFrom.Target.Alive || Salvaging.stolenFrom.Gone))
            {
                Destroy(Salvaging.gameObject);
                Salvaging = null;
                return;
            }
            // Multiplayer: another player got it first (NetCrate's claim).
            if (Salvaging.claimedByOther) { Salvaging.pulled = false; Salvaging = null; return; }
            var to = transform.position - Salvaging.transform.position;
            float dist = to.magnitude;
            if (dist / M < CaptureUnits) { if (!Salvaging.captureBlocked) Capture(Salvaging); return; }   // blocked: waits for the claim
            Salvaging.transform.position += to / dist * Mathf.Min(dist, PullSpeed * dtMs * M);
            if (beam != null)
            {
                beam.gameObject.SetActive(true);
                beam.position = transform.position;
                beam.rotation = Quaternion.LookRotation(-to, transform.up);
                // TractorBeam::update 0x17c16c: setScaling(beam, 0.5, 0.5, distance): 250 units wide, exactly as long as the distance.
                beam.localScale = new Vector3(0.5f, 0.5f, dist / beamLength);
            }
        }

        /// <summary>KIPlayer::captureCrate 0xb2a00: the crate is gone once captured (KIPlayer+0x74 = 0) and a dead ship leaves no
        /// other (+0x48 = 0). Its first non-empty entry: all of it from a wreck, rnd(amount) from a living ship, at least 1,
        /// capped to the free cargo. Every ship's crate costs standing (Standing::applyStealCargo, delict 2) and a friend's is a
        /// stolen cargo (Level::stealFriendCargo). No room: the unit is lost ("Cargo hold full"); remake: a mission container
        /// stays for another try. Missiles of a mounted launcher's type reload it (Item::changeAmount), the rest is cargo.</summary>
        void Capture(Crate crate)
        {
            Salvaging = null;
            if (beamLoop.isPlaying) beamLoop.Stop();
            if (assets != null && assets.tractorClose != null) sfx.PlayOneShot(assets.tractorClose, Settings.SfxVolume);
            var entry = crate.loot.Find(s => s.amount > 0);
            if (entry == null) { Destroy(crate.gameObject); return; }
            int free = Shop.FreeCargo(db);
            var ship = crate.stolenFrom;
            int want = ship != null ? UnityEngine.Random.Range(0, entry.amount) : entry.amount;
            int n = Mathf.Max(1, Mathf.Min(want, free));
            n = Mathf.Min(n, entry.amount);
            bool friend = ship != null ? ship.Target.friendToPlayer : crate.fromFriend;
            if (friend && traffic != null) traffic.FriendCargoStolen = true;   // Level::stealFriendCargo
            Standing.ApplyDelict(crate.race, 2);                               // Standing::applyStealCargo
            if (free < n)
            {
                Message?.Invoke(Localization.Get(322), 1);
                if (crate.missionCrate && ship == null) { crate.pulled = false; return; }
                entry.amount -= n;
                ship?.StealFrom(entry.item, n);
                crate.CapturedHere?.Invoke();
                Destroy(crate.gameObject);
                return;
            }
            entry.amount -= n;
            ship?.StealFrom(entry.item, n);
            var mounted = db.Item(entry.item)?.TypeId == 1 ? Session.Equipment.Find(e => e.item == entry.item) : null;
            if (mounted != null) mounted.amount += n; else Shop.AddToCargo(entry.item, n);
            Session.CratesSalvaged += n;   // Status::crateCaptured
            if (crate.missionCrate && Freelance.Active)
            {
                Session.Unsaleable.Add(entry.item);   // the client's container (also a squadmate's capture, multiplayer)
                GoF2Remake.Multiplayer.NetMissions.AddStatus(Freelance.Mission, 1);   // multiplayer: the squad has it
            }
            // A Void crate counts for Alien Hunter (Status+0xcc), another race's booze for Barkeeper.
            if (crate.race == Standing.Void) Session.AlienRemainsCollected += n;
            else if (Session.IsBooze(entry.item)) Session.BoozeTypes.Add(entry.item);
            Message?.Invoke($"{n}t {Localization.Get(1274 + entry.item)}", 2);
            crate.CapturedHere?.Invoke();   // multiplayer: the capture reaches the crate's owner (NetCrate)
            Destroy(crate.gameObject);
        }
    }
}
