// NpcShip.cs
// One NPC ship (KIPlayer + Player), Reference/research/npc_traffic_ai.md 4-6 and ship_combat.md 2-5. Created by
// Traffic from a SpawnSpec.
// Fighters (PlayerFighter::update 0xf0d90):
//   relations   hostile = pirates / Void / Specters, or an enemy race by the standing, or turned / alarmed; friend by the
//               standing (recomputed every frame)
//   targeting   enemy list = the player (index 0) + ships of other races; attack whatever is inside the +-50000 box; every
//               5 s: 20 % start / stop flying straight, 30 % re-roll the target else back to the player; neutral and
//               friendly ships never keep the player but attack the first race-hostile ship; no target -> the patrol
//               route (a finished route -> toward the player without firing); remake option (Settings.SmarterNpcTargeting,
//               on by default): the nearest hostile target in the box instead, the player's distance x0.7, at most
//               2 + difficulty attackers per target while another is free (NpcTargetRules, UpdateSmartTargeting)
//   steering    speed 2 u/ms (boost 5.5), heading += normalize(dir - fwd) * dt * 48 / 65536, snap when within L1 0.0625;
//               inside +-8000 of the target it steers along its own right vector (circles away), so it never fires
//               close up; visual bank up to 33 deg from the averaged turn, auto-level after 750 ms
//   firing      target inside the +-0.0076 cone (ship-space unit vector x / y) and +-35000 per axis; one NPC gun
//   boost       5 % per 5 s, or after losing 40 % of the hull, for 5..8 s (x1.05 / x0.95 per original frame)
//   avoidance   inside the first landmark's volumes (station, gate), then the first ship's (freighters): heading +=
//               (away - fwd) * speed * 0.03 plus an extra step (fighters fly through asteroids and each other)
//   death       sound 20, 1.5..3 s tumbling along the death direction, then Explosion type 0, the hull 300 ms more,
//               a crate with the cargo; gone once the explosion ended and the crate is gone (60 s)
// Wingmen (Level::createWingmen 0xcb338, PlayerFighter::update; Reference/research/wingmen_wanted.md 1): always friend,
//   formation points (slot 0 -right*4000 -fwd*3000, 1 +right*4000 -fwd*3000, 2 +up*2000 -fwd*2000 around the player);
//   command 1 "Fire at will" attacks the first hostile ship of the orbit at any distance (and ends boosts for good),
//   2 scouts the player's next waypoint, 3 sticks to the player's locked ship; a laser (the race's NPC gun) and a Dia EMP
//   Mk III (item 18) to switch between; unarmed in a Challenge; a dead one leaves the contract; no friendly-fire reaction.
// Damage smoke (PlayerFighter::update 0xf1b0e): below 33 % of the hull a fighter trails the prologue's smoke and fire
//   (ShipSmoke), off again when repaired to 33 %; they keep running through the death tumble and stop at the explosion.
//   The tumble also burns (ShipBurn, record 9) and the explosion bursts record 11.
// Freighters (PlayerFixedObject): unarmed, fly game +Z at 1 u/ms, never turn, x5 hull; death: their wreck animation
// (cargo_*_explosion_anim, ~10 s, still moving), then a x6 explosion; the crate appears at once; the wreck then stays
// where it is (state 4) with its wreck volumes. Their boxes (Obstacle, Level::createShip) are what bullets hit, the
// player slides along and fighters steer out of.
// Static objects (PlayerFixedObject 0x17ece0, the Kaamo siege's Pirate Outposts, kaamo_club.md 3.2): an assembled object
// that never moves, no gun, engine or loot, a +-hitRadius hit cube, the level's static volumes; death: the wreck animation
// (20 s), then an x8 explosion, and the wreck stays.
// Turrets (PlayerTurret 0x182640, npc_combat_specials.md 1): the capital ships' satellites, turret_002_static /
// turret_003_static at scale 6, 1000 HP, +-1000 hit cube, no collision; every 3000 ms the nearest race-hostile target
// within 50000 (the player when hostile to it), yaw / pitch at 2 pi / 4096 rad per ms toward its position + heading *
// 1500, pitch -8.8 .. +52.7 deg (a target out of reach is dropped), fire when aligned (+-0.05) with the fighters' gun
// in the look of item 20 (Vossk 15); death: sound 22, an explosion, gone after 4500 ms, no crate. Killing the Terran
// battleship destroys every turret; its wreck explodes x6.
// Sleeping fixed objects (the pirate bases' outposts) stay visible but can't be hit, locked or counted until any enemy
// comes within +-50000; guards call Level::pirateStationAction(true) when they wake.
// Friendly fire (Player::damage): hits by the player on system-race / attack-race ships add up: > 33 % of the hull ->
// radio "Hold your fire!", >= 50 % -> this ship turns hostile, >= 66 % -> the whole race turns hostile (10 / 25 / 40 % on
// Extreme). NPC bullets never hit their own race; a non-hostile NPC's stray hit on the player does 20 %.
// Directions: "right" is Unity's transform.right (the original's mirrored model space may circle the other way).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public partial class NpcShip : MonoBehaviour
    {
        const float M = 0.05f;
        public enum State { Fly = 1, Dying = 3, Dead = 4, JumpingOut = 6 }

        public SpawnSpec Spec { get; private set; }
        public int Race => Spec.race;
        public bool IsFreighter => Spec.freighter;
        /// <summary>Level::createStaticObject: a Pirate Outpost (never moves, never shoots).</summary>
        public bool IsFixed => Spec.fixedObject != null;
        /// <summary>A capital ship's turret (PlayerTurret).</summary>
        public bool IsTurret => Spec.turretAssembly != null;
        /// <summary>Remake: the turret items on its turret mounts (SpawnSpec.turretItems, Database.NpcTurrets), null = none.</summary>
        NpcMountedTurrets mounted;
        public NpcMountedTurrets MountedTurrets => mounted;
        /// <summary>A turret object's aim, gun and shots (TurretCore) and its pick (TurretTargeting.Rule.Timed).</summary>
        TurretCore turretCore;
        TurretTargeting turretPicking;
        bool turretTargetFixed;   // the target is a turret: aimed at where it is, no lead
        AudioClip turretShot;     // a turret item's own shot sound (SpawnSpec.turretItem), null = the race's
        const float TurretRangeUnits = 50000f, TurretDeathMs = 4500f;
        public Target Target { get; private set; }
        public Hitpoints Hp => Target.hitpoints;
        public State Current { get; private set; } = State.Fly;
        /// <summary>Dead and cleaned up (inactive): the level may relaunch it.</summary>
        public bool Gone => !gameObject.activeSelf;
        public bool IsJumper => Spec.group == NpcGroup.Jumper;

        [System.NonSerialized] public bool alwaysEnemy, turnedEnemy, alwaysFriend;
        /// <summary>Remake: a friend the level expects the player to destroy (step 42's Errkt): no friendly-fire rules, so no
        /// "We need backup!" chatter or a turned race for it (#46).</summary>
        [System.NonSerialized] public bool noFriendlyFire;
        /// <summary>Remake (/spawn, the debug spawner): neutral to every player whatever the standings, until it is turned
        /// (friendly fire) or a player shoots it in multiplayer (aggressors).</summary>
        [System.NonSerialized] public bool alwaysNeutral;
        /// <summary>Player::setShootingEnabled / removeAllGuns (level scripts): false = it still hunts, never fires.</summary>
        [System.NonSerialized] public bool shootingEnabled = true;
        /// <summary>KIPlayer+0x20 set by a level script (index 64: Khador sits still): no steering, no flying, no firing.</summary>
        [System.NonSerialized] public bool frozen;
        /// <summary>PlayerFighter+0x124, the detection box's half size (units); -1 = NpcTables.DetectRange.</summary>
        [System.NonSerialized] public float detectRange = -1f;
        /// <summary>setToSleep / setInitActive(false): no flying or shooting until woken (Wake, or the player close by).</summary>
        public bool Asleep { get; private set; }
        /// <summary>PlayerFighter: a sleeping hostile ship is invisible after the tutorial (index &gt; 1): no model, no marker,
        /// no lock. Freighters are PlayerFixedObjects like the fixed objects, whose sleepers stay visible.</summary>
        public bool Hidden => forcedHidden || (Asleep && !IsFixed && !IsFreighter && Target.hostileToPlayer && Session.CampaignMission > 1);
        bool forcedHidden;

        /// <summary>KIPlayer::setVisible (cutscenes): hidden ships have no model, marker or lock.</summary>
        public void SetVisible(bool visible)
        {
            forcedHidden = !visible;
            if (modelGo != null && Current == State.Fly) modelGo.SetActive(!Hidden);
        }

        /// <summary>The engine exhaust meshes (cutscenes: "exhaust hidden" while the pirates wait).</summary>
        public void SetExhaust(bool on)
        {
            exhaustWanted = on;
            ApplyExhaust();
        }

        // Remake: an EMP-disabled ship's engines go dark (the glow meshes, and with them a mod ship's flames, EngineDrive; the
        // exhaust particles already stopped), back when the EMP wears off; the scripts' own on / off stays theirs.
        bool exhaustWanted = true, exhaustEmpOff;

        void ApplyExhaust() => modelGo?.GetComponent<AssembledObject>()?.SetExhaust(exhaustWanted && !exhaustEmpOff, playerEngines);

        void UpdateEmpEngines()
        {
            bool off = Hp.empDisabled && Current == State.Fly;
            if (off == exhaustEmpOff) return;
            exhaustEmpOff = off;
            ApplyExhaust();
        }

        /// <summary>Remake option (Settings.NpcPlayerEngines): the player's engine glow mesh and exhaust particles instead of
        /// the *_engine_add mesh; ships without a glow mesh (39 / 41, the battleship, static objects) keep theirs.</summary>
        bool playerEngines;

        /// <summary>The engine loop (PlayerFighter: no NPC engine sound in index 1).</summary>
        public void SetEngineSound(bool on)
        {
            if (engine == null || engine.clip == null) return;
            if (on && !engine.isPlaying) engine.Play(); else if (!on) engine.Stop();
        }
        bool inactive, parked;
        /// <summary>setInitActive(false) (Player::isActive 0xb0022 false): waits for its script; not on the radar's hostile
        /// counter.</summary>
        public bool Inactive => inactive;
        [System.NonSerialized] public List<Target> enemies = new List<Target>();

        // ---- multiplayer (NetOrbit sets the hooks on the orbit's authority; all null in single player) ----------
        /// <summary>The other players whose shots turned this ship on them (client ids): hostile to their squads.</summary>
        [System.NonSerialized] public readonly HashSet<ulong> aggressors = new HashSet<ulong>();
        /// <summary>The other players' ships in this orbit (their NetPlayer Targets).</summary>
        public static IReadOnlyList<Target> RemotePlayers;
        /// <summary>This ship is hostile to that other player (an always-hostile race, their squad shot it, or it is hostile to
        /// the local player and they are in its squad).</summary>
        public static System.Func<NpcShip, Target, bool> HostileToRemote;
        /// <summary>Another member of the local player's squad shot this ship: hostile to the local player too.</summary>
        public static System.Func<NpcShip, bool> HostileToLocalBySquad;
        /// <summary>Multiplayer: a faction's held station (NetFactions): +1 the ship is the local player's friend, -1 an enemy, 0 as
        /// always (NetOrbit).</summary>
        public static System.Func<NpcShip, int> TerritoryToLocal;
        Target remoteTarget;
        readonly List<Target> hitList = new List<Target>();
        readonly Dictionary<ulong, int> remoteHullDamage = new Dictionary<ulong, int>(), remoteEmp = new Dictionary<ulong, int>();

        /// <summary>Multiplayer: another game's ship shown here (NetProxy), hostile to the local player: a wingman's target.</summary>
        static bool HostileOther(Target t) => t != null && t.enabled && !t.untargetable && !t.isPlayer && t.isShip && t.Alive && t.hostileToPlayer
                                              && t.GetComponent<GoF2Remake.World.IOrbitPeer>() == null;

        /// <summary>Multiplayer (NetOrbit): another player's ship is docked at an object (their hits x0.75), null = none.</summary>
        public static System.Func<Target, bool> RemoteDockedAtObject;

        /// <summary>What its guns can hit: its enemies, plus the other players here (every player ship blocks its shots).</summary>
        public List<Target> HitTargets
        {
            get
            {
                var remote = RemotePlayers;
                bool others = IsWingman && GoF2Remake.Data.NetSession.Active && Target.NetShips.Count > 0;
                if ((remote == null || remote.Count == 0 || HostileToRemote == null) && !others) return enemies;
                hitList.Clear();
                hitList.AddRange(enemies);
                if (remote != null) foreach (var r in remote) if (r != null) hitList.Add(r);   // in the line of fire like the local player
                // Multiplayer: a wingman's shots also hit the other players' hostile ships here (their game applies it).
                if (others) foreach (var t in Target.NetShips) if (HostileOther(t)) hitList.Add(t);
                return hitList;
            }
        }

        Traffic traffic;
        bool turretHostLooked;   // UpdateTurret: its host for LineOfFire looked up
        Database db;
        CombatAssets assets;
        Transform model;
        GameObject modelGo;

        /// <summary>The ship it attacks (its rockets home on it), null = none.</summary>
        public Target CurrentTarget => target;
        /// <summary>Its guns (the main gun, the second slot, the wingmen's EMP gun), for multiplayer's shot mirrors.</summary>
        public IEnumerable<Gun> Guns
        {
            get
            {
                if (gun != null) yield return gun;
                if (secondGun != null) yield return secondGun;
                if (empGun != null) yield return empGun;
                foreach (var g in ExtraGuns) yield return g;
            }
        }
        /// <summary>Remake: guns another component fires for this ship (a capital ship's missiles, CapitalShip), for the
        /// multiplayer shot mirrors.</summary>
        public readonly List<Gun> ExtraGuns = new List<Gun>();
        /// <summary>The ship's model (its pose is what a multiplayer proxy follows), else the ship itself.</summary>
        public Transform Model => model != null ? model : transform;
        /// <summary>Remake: the warp holds every ship's fire (Traffic.HoldFire).</summary>
        bool HoldFire => traffic != null && traffic.HoldFire;
        /// <summary>Resources path of the assembled prefab it was built from (Traffic), for multiplayer proxies.</summary>
        public string ModelPath { get; set; }
        /// <summary>A freelance mission's ship (FreelanceOrbit): multiplayer never takes it over from its player.</summary>
        public bool MissionShip { get; set; }
        /// <summary>Remake co-op: a story level's ship of the leader's (CampaignLevel): shown to the crew, never handed over
        /// (it goes when the leader leaves the orbit), never dropped with the orbit's authority.</summary>
        public bool StoryShip { get; set; }
        /// <summary>Multiplayer: this player's own stand-in, never shown to the others (a follower's copy of the mission's
        /// mining plant, FreelanceOrbit).</summary>
        public bool LocalOnly { get; set; }
        Route route;
        Gun gun;
        GunRig rig;
        /// <summary>Remake: this ship's (or turret's) gun visuals (ValkyrieLevels: the battlestation turrets' twin muzzles).</summary>
        public GunRig Rig => rig;
        /// <summary>Remake: a turret object's moving parts and aim (null for a ship).</summary>
        public TurretCore Turret => turretCore;
        AudioSource engine, sfx;
        List<ItemStack> loot = new List<ItemStack>();
        int damageByPlayer;

        // flight
        float speed = NpcTables.BaseSpeed, targetSpeed, baseSpeed = NpcTables.BaseSpeed;
        bool empWasDisabled;
        bool boosting, panic;
        float boostTimer, boostDuration, reselectTimer = 0f, jumpMs;
        int lastHull, damageSinceBoost;
        bool drift, attacking, followingWaypoint;
        int targetIdx = -1;
        Target target;
        float bank, bankTarget, levelTimer = 750f;
        bool levelling;
        readonly float[] turnRing = new float[5];
        int ringIndex, ringFilled;

        // death
        float dyingMs, deadMs;
        Vector3 spinAxis, deathDir;
        Explosion explosion;
        int explosionSerial;   // the explosions are pooled: this one's Spawn (Explosion.Done)
        GameObject wreck;
        float wreckDamageMs;   // until the wreck takes its damaged material (CombatAssets.ApplyWreckDamage)
        WreckBurn wreckBurn;
        Crate crate;
        readonly List<Crate> moreCrates = new List<Crate>();   // remake: a crate per kind of loot (DropCrate)
        Obstacle obstacle;
        ShipSmoke smoke;
        ShipBurn burn;
        EmpSparks sparks;
        bool smoking;   // PlayerFighter +0x1f4

        // wingman (KIPlayer+0xd8 / +0xdc / +0xe0 / +0xe4)
        public bool IsWingman { get; private set; }
        /// <summary>PlayerFixedObject::getDockingType (ObjectDocking.DropOff / Pickup / Hackable, 0 = not dockable); level
        /// scripts change it.</summary>
        public int DockingType { get; set; }
        /// <summary>Offered as a docking target now (Navigation's landmark, no ship marker or lock): any docking type, but a
        /// capital ship's resupply deck only while it is open (CapitalShips.DeckOpen). Remake: closed (hostile, in a fleet
        /// battle, coasting after one) the carrier and the Vossk battleship are ships to mark, lock and scan; before, the
        /// docking type kept them off the radar for good (a player's report 2026-10-16).</summary>
        public bool OffersDocking => DockingType > 0 && (DockingType != ObjectDocking.Resupply || CapitalShips.DeckOpen(this));
        public int SpacePointSet => Spec.spacePoints;
        /// <summary>Level::getDockingTarget's index (the hacking game's dock index).</summary>
        public int DockIndex { get; set; } = -1;
        /// <summary>KIPlayer+0x70: skipped by the radar and the HUD markers (Radar::draw); also while cloaked.</summary>
        public bool RadarHidden { get => radarHidden || (cloak != null && cloak.Hidden); set => radarHidden = value; }
        bool radarHidden;
        NpcCloak cloak;

        /// <summary>The Specters (race 10) and Harval's Scimitar (ship 49) can cloak (NpcCloak).</summary>
        public bool CanCloak => cloak != null;
        public bool Cloaked => cloak != null && cloak.Cloaked;
        /// <summary>Its cloak's fade, 0..100 (multiplayer: NetProxy draws it in the other players' games).</summary>
        public float CloakPercent => cloak != null ? cloak.Percentage : 0f;
        /// <summary>RadarHidden without the cloak (a level's or the spec's): NetProxy hides those copies, a cloaked one shows its cloak.</summary>
        public bool ScriptRadarHidden => radarHidden;
        /// <summary>setCloakingPossible: may cloak on its own.</summary>
        public bool CloakingPossible { get => cloak != null && cloak.Possible; set { if (cloak != null) cloak.Possible = value; } }
        /// <summary>PlayerFighter::cloak(ms) from a level script; 0 uncloaks.</summary>
        public void Cloak(float ms, bool instant = false) => cloak?.Cloak(ms, instant);

        /// <summary>A level script flies it (AI off): straight ahead at this speed (u/ms), no targeting, no firing; -1 = off.
        /// The script may also turn it (transform).</summary>
        [System.NonSerialized] public float scriptedSpeed = -1f;
        /// <summary>LevelScript's Player::shoot on a scripted ship: the primary fires straight ahead whenever it is loaded.</summary>
        [System.NonSerialized] public bool scriptedFire;
        int wingSlot, wingCommand = 1, scoutStart;
        Target wingTarget;
        Route scout;
        Gun empGun;
        // Player::shoot slot 1 (Level::assignGuns): a second gun; the Wanted pilots toggle the fired slot every 20 000 ms
        // (PlayerFighter+0x2e0), Harval's cluster missiles too.
        Gun secondGun;
        GunRig secondRig;
        bool useSecond;
        float slotMs;
        const float SlotToggleMs = 20000f;
        GunRig empRig;
        bool useEmp, evasionOff;
        Transform fxRootRef;

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;
        // PlayerFighter::initPush 0xf3b00 / push 0xf3c50 (the shock blast): pushed away from the blast centre for
        // T = (1 - min(d / radius, 1)) * 5000 ms, speeding up as it runs out, tumbling (UpdatePush).
        const float PushMaxMs = 5000f;
        Vector3 pushDir, pushTumble;
        float pushMs, pushTotalMs;

        /// <summary>Game rotation identity: facing game +Z = Unity -Z.</summary>
        static readonly Quaternion GameForward = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>A fighter's explosion size: 1 (Explosion::setScaling's default), or a mod ship's "explosionScale"
        /// (ModShipView).</summary>
        public float FighterExplosionScale => db?.Ship(Spec.ship)?.explosionScale ?? 1f;

        /// <summary>A freighter's: PlayerFixedObject::update's setScaling(6), 8 for the Terran battleship; a mod's capital ship
        /// or freighter its own "explosionScale" when it has one (else the battleship's / a freighter's).</summary>
        public float FreighterExplosionScale => Spec.ship == 14 ? 8f
            : Spec.modCapital && db?.Ship(Spec.ship)?.explosionScale is float own && own != 1f ? own
            : Spec.modCapital && Spec.stationary ? 8f : 6f;

        public void Setup(Traffic owner, Database database, SpawnSpec spec, GameObject prefab, Transform fxRoot)
        {
            traffic = owner;
            db = database;
            Spec = spec;
            fxRootRef = fxRoot;
            assets = CombatAssets.Load();
            transform.SetPositionAndRotation(ToUnity(spec.position),
                spec.turretAssembly != null || spec.fixedObject != null ? OrbitLayout.RotationToUnity(spec.rotation) : GameForward);
            DockingType = spec.dockingType;
            RadarHidden = spec.radarHidden;
            if (prefab != null)
            {
                modelGo = spec.standIn != null ? spec.standIn : Instantiate(prefab, transform, false);
                if (spec.standIn != null)
                {
                    modelGo.transform.SetParent(transform, false);
                    SystemApproach.Wake(spec);
                    spec.standIn = null;
                }
                var asm = modelGo.GetComponent<AssembledObject>();
                // The option, or a model with no NPC engine part at all (the Kaamo Club's 55-63: only the engine glow).
                playerEngines = (Settings.NpcPlayerEngines || (asm != null && !asm.HasNpcExhaust)) && spec.fixedObject == null && spec.turretAssembly == null
                                && asm != null && asm.playerVariantParts != null && asm.playerVariantParts.Length > 0 && asm.playerVariantParts[0] != null;
                asm?.SetPlayerVariant(playerEngines);
                model = modelGo.transform;
                if (spec.scale != 1f) model.localScale *= spec.scale;
                if (playerEngines)
                {
                    var glow = asm.playerVariantParts[0];
                    Flight.ShipExhaust.AttachRemote(gameObject, database, model, spec.ship,
                        () => glow != null && glow.activeInHierarchy && Current == State.Fly && !Hp.empDisabled,
                        () => Mathf.Clamp01(((scriptedSpeed >= 0f ? scriptedSpeed : speed) - baseSpeed) / Mathf.Max(0.01f, NpcTables.BoostSpeed - baseSpeed)),
                        () => cloak != null ? cloak.Percentage : 0f);
                }
            }

            int kind = spec.freighter ? 1 : 0;
            Target = gameObject.AddComponent<Target>();
            Target.isShip = true;
            Target.race = spec.race;
            Target.customDeath = true;
            Target.mineProof = spec.fixedObject == "station_pirates";   // KIPlayer+0x3d
            Target.plateNameOnly = spec.nameText == 1611 || spec.nameText == 1663;
            // Radar::drawCurrentLock compares the name with 0x64b "Hijacker" / 0x67f "Informer" (and the Wanted flag): those are
            // red (a player's report: only the Most Wanted criminals were).
            Target.plateRed = spec.wantedIndex >= 0 || spec.nameText == 1611 || spec.nameText == 1663;
            Target.radius = (spec.hitRadius > 0f ? spec.hitRadius : NpcTables.HitRadiusUnits) * M;
            Target.hitpoints = new Hitpoints(spec.hitpoints > 0 ? spec.hitpoints : NpcTables.Hull(kind, spec.ship));
            Target.hitpoints.SetEmp(spec.empPoints > 0 ? spec.empPoints : NpcTables.Emp(kind), NpcTables.EmpRecoveryMs(kind));   // also with a hull override
            Target.hp = Target.maxHp = Target.hitpoints.maxHull;
            Target.displayName = !string.IsNullOrEmpty(spec.name) ? spec.name : spec.nameText >= 0 ? Localization.Get(spec.nameText) : null;
            if (spec.speed > 0f) speed = baseSpeed = spec.speed;
            alwaysEnemy = spec.alwaysEnemy;
            alwaysFriend = spec.alwaysFriend;
            alwaysNeutral = spec.alwaysNeutral;
            Asleep = spec.asleep || spec.inactive;
            inactive = spec.inactive;
            parked = spec.stationary;
            if (spec.freighter)
            {
                obstacle = gameObject.AddComponent<Obstacle>();
                obstacle.projectFromVolume = false;
                obstacle.volumes = CollisionVolume.ForFreighter(spec.ship, spec.race);
                Target.boxes = LocalBoxes(obstacle.volumes);
            }
            else if (spec.fixedObject != null && spec.collisionId >= 0)
            {
                obstacle = gameObject.AddComponent<Obstacle>();
                obstacle.projectFromVolume = false;
                obstacle.volumes = CollisionVolume.ForStaticObject(spec.collisionId);
                // Remake (CapitalShips): a killable capital ship is hit on its collision boxes, not the +-1000 cube at its centre.
                if (spec.capitalEnhanced) Target.boxes = LocalBoxes(obstacle.volumes);
            }
            // Remake: shots hit its real shape and the player slides along it (HullCollision); NPCs still steer by the volumes.
            // A fighter (not a freighter, a fixed object such as the battleship or a capital ship, nor a turret) is a flyable
            // ship: Original ship collision hits it on its cube (HullBody.flyableShip).
            if (modelGo != null) HullCollision.Attach(modelGo, Target, obstacle, !IsFreighter && !IsFixed && !IsTurret && HullCollision.FlyableShip(spec.ship));
            if (spec.deadButSelectable) ShowWreckAtEnd();
            Target.Damaged += OnDamaged;
            Target.Died += OnDied;
            lastHull = Hp.hull;
            if (!spec.freighter && spec.fixedObject == null && spec.turretAssembly == null) smoke = new ShipSmoke(transform, db?.Ship(spec.ship)?.smokePoints);

            if (spec.turretAssembly != null) SetupTurret();
            else if (!spec.freighter && spec.fixedObject == null && spec.ship != 51)
            {
                gunBase = NpcTables.GunDamage(spec, false, false, out gunSpeed);
                var item = db.Item(NpcTables.GunItemFor(spec.race, false));
                if (item != null)
                {
                    gun = new Gun(item, gunBase, NpcTables.GunReloadMs, NpcTables.GunPool,
                                      NpcTables.GunLifetimeMs, gunSpeed) { owner = Target };
                    rig = new GunRig(gun, WeaponFx.Load(item.index), fxRoot, null, 2);
                    WireGun(gun);
                }
            }
            route = (spec.route ?? Route.DefaultPatrol(spec.race)).Clone();
            loot = spec.lootItem >= 0 ? new List<ItemStack> { new ItemStack(spec.lootItem, Mathf.Max(1, spec.lootAmount)) }
                 : spec.missionCrate >= 0 ? new List<ItemStack> { new ItemStack(spec.missionCrate, 1) }
                 : spec.noLoot ? new List<ItemStack>() : RollLoot();
            if (spec.lootItem >= 0 && spec.extraLoot != null)   // remake: a pirate outpost's crate by its tier
            {
                foreach (var st in spec.extraLoot) if (st != null && st.amount > 0) loot.Add(new ItemStack(st.item, st.amount));
                multiKindLoot = true;
            }

            sfx = gameObject.AddComponent<AudioSource>();
            Setup3D(sfx);
            engine = gameObject.AddComponent<AudioSource>();
            engine.playOnAwake = false;
            EngineVoices.Setup3D(engine);   // the engine events' rolloff (0.05 .. 500 m), at most 3 of each event at once
            engine.loop = true;
            engine.clip = GoF2Remake.Modding.ModSounds.Get(assets == null || spec.fixedObject != null || spec.turretAssembly != null || spec.ship == 14 ? null
                        : CombatAssets.Pick(spec.freighter ? assets.freighterEngines : assets.enemyEngines));
            float engineVolume = (spec.freighter ? 0.195f : 0.0759f) * Sfx.EventGain;   // event volumes 47 / 46
            // Remake mods: a mod ship's own engine loop (ModShipSounds), at its volume near top speed.
            if (engine.clip != null && !spec.freighter && GoF2Remake.Modding.ModShipSounds.EngineClip(spec.ship, 0.8f, out float ownVolume) is AudioClip ownEngine)
            {
                engine.clip = ownEngine;
                engineVolume = ownVolume;
            }
            EngineVoices.Register(engine, spec.freighter ? 47 : 46, engineVolume);
            if (engine.clip != null) engine.Play();

            if (spec.startsDead) SetDead();
            if ((spec.race == Standing.Specter || spec.ship == 49) && spec.fixedObject == null && modelGo != null) cloak = new NpcCloak(model);
            // Level::assignGuns: a Most Wanted criminal fires its own weapon at x4.
            if (spec.gunItem >= 0 && gun != null) SetGun(spec.gunItem, spec.gunFactor);
            // Remake (generic turrets): turret items on its turret mounts, its own or its type's (a mod's "npcTurrets").
            if (spec.turretAssembly == null && spec.fixedObject == null && modelGo != null)
                mounted = NpcMountedTurrets.Build(this, db, spec.ship, spec.turretItems ?? db.NpcTurretsOf(spec.ship), model, fxRoot, GunHit);
            if (spec.secondaryItem >= 0 && gun != null) SetSecondaryGun(spec.secondaryItem, spec.secondaryFactor);
        }

        static void Setup3D(AudioSource s)
        {
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 20f;
            s.maxDistance = 1500f;
            s.dopplerLevel = 0f;
        }

        /// <summary>The world-axis boxes as local hit boxes (Target.Contains) for the ship's fixed rotation.</summary>
        public static Bounds[] LocalBoxes(List<CollisionVolume> volumes)
        {
            var inv = Quaternion.Inverse(GameForward);
            var list = new List<Bounds>();
            foreach (var v in volumes)
            {
                if (v.sphere) continue;
                var size = inv * (v.half * 2f);
                list.Add(new Bounds(inv * v.centre, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z))));
            }
            return list.ToArray();
        }

        /// <summary>KIPlayer::revive: full hull, new cargo, base speed, back on its route, at 'position' (Unity).</summary>
        public void Revive(Vector3 position)
        {
            gameObject.SetActive(true);
            transform.SetPositionAndRotation(position, GameForward);
            if (modelGo != null) { modelGo.SetActive(true); model.localRotation = Quaternion.identity; }
            if (wreck != null) Destroy(wreck);
            if (obstacle != null) obstacle.volumes = CollisionVolume.ForFreighter(Spec.ship, Spec.race);
            Target.Revive();
            lastHull = Hp.hull;
            damageSinceBoost = 0;
            damageByPlayer = 0;
            smoking = false;
            smoke?.Clear(); sparks?.Clear(); burn?.SetBurning(false);
            Current = State.Fly;
            speed = baseSpeed;
            boosting = panic = false;
            targetSpeed = 0f;
            jumpMs = 0f;
            attacking = false;
            targetIdx = -1;
            // Modified (review of #179): back to its own AI: an order of its last life (the Supernova Challenge's first-wave run
            // to its point at run speed, an event's) doesn't carry over.
            order = null;
            orderMs = 0f;
            route = (Spec.route ?? Route.DefaultPatrol(Spec.race)).Clone();
            // PlayerFighter::revive 0xf3de0: a revived Void ship or Specter carries nothing (the Void's remains come at death).
            loot = Spec.noLoot || Race == Standing.Void || Race == Standing.Specter ? new List<ItemStack>() : RollLoot();
            crate = null;
            crateDropped = false;
            if (engine != null && engine.clip != null) engine.Play();
        }

        /// <summary>A turret object's homing target: a turret item firing missiles homes on what it aims at.</summary>
        Target TurretHoming => turretPicking != null && turretPicking.Current is Target t && t.Alive && !t.cloaked ? t : null;

        /// <summary>Remake debug (PlayerHull): one of the turrets on the player's own hull.</summary>
        public bool PlayerOwned { get; private set; }

        void OnDestroy()
        {
            cloak?.Dispose();
            EngineVoices.Unregister(engine);
        }

        // ---- per frame -------------------------------------------------------------------------------------

        /// <summary>Level::createFighterTurrets: the turret on this ship (45 / 51), null = none.</summary>
        [System.NonSerialized] public NpcShip AttachedTurret;

        void Update()
        {
            EngineVoices.Tick();   // once a frame for all ships: the three loudest loops of each engine event
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            SyncTurret();
            if (gun != null)
            {
                gun.Update(dtMs, HitTargets, IsTurret ? TurretHoming : HomingTarget);
                rig.UpdateVisuals(dtMs, Camera.main, transform.forward);
            }
            if (secondGun != null)
            {
                secondGun.Update(dtMs, HitTargets, HomingTarget);
                secondRig.UpdateVisuals(dtMs, Camera.main, transform.forward);
                slotMs += dtMs;
                if (slotMs >= SlotToggleMs) { slotMs = 0f; useSecond = !useSecond; }
            }
            if (empGun != null)
            {
                empGun.Update(dtMs, HitTargets, null);
                empRig.UpdateVisuals(dtMs, Camera.main, transform.forward);
            }
            // Remake: turret items on its turret mounts (NpcMountedTurrets) aim and fire while it flies, awake and armed.
            mounted?.Update(dtMs, enemies, HitTargets, Current == State.Fly && !Asleep && !inactive && !Hidden && (!parked || Spec.modCapital) && !frozen
                                                     && shootingEnabled && !Hp.empDisabled && Target.Alive);
            if (Current == State.Dead) { UpdateDead(dtMs); return; }
            if (Current == State.Dying) { UpdateSmoke(); UpdateDying(dtMs); return; }
            UpdateRelations();
            Hp.Update(dtMs);
            // PlayerFighter::handleCloaking: race 10 only (Harval's Scimitar cloaks as a Specter, at 158); scripted ships too.
            if (Race == Standing.Specter) cloak?.Update(dtMs, !Asleep && !inactive, panic, Hp.empDisabled);
            // Remake: a cloaked ship can't be targeted, whoever flies it. The original's cloaked Specter only leaves the
            // radar (KIPlayer+0x70); turrets (PlayerTurret::handleTurret) and the fighters' fire block read Player+0x5e,
            // which only the player's cloak sets, so the Rhinos' turrets and the locals kept shooting at it. Target.cloaked
            // gives it the player's rules: turrets don't aim, fighters chase but hold fire, missiles lose their lock.
            Target.cloaked = cloak != null && cloak.Hidden;
            UpdatePush(dtMs);
            UpdateSmoke();
            UpdateSparks();
            UpdateEmpEngines();
            UpdateMissionCrate();
            if (Asleep) { UpdateSleep(); return; }
            if (IsTurret) { UpdateTurret(dtMs); return; }
            if (parked || frozen) return;   // parked: a target that neither flies nor shoots
            if (scriptedSpeed >= 0f)
            {
                // AEGeometry::moveForward never touches the fighter's speed (+0x1e0): released, the AI flies at its own
                // speed again (keeping the scripted one left Harval at ~9 u/ms in 158 and 154's fighters at 0).
                transform.position += transform.forward * scriptedSpeed * dtMs * M;
                if (scriptedFire && !HoldFire && gun != null && gun.TryFire(transform) >= 0)
                {
                    int shot = NpcTables.ShotSound(Race);
                    var clip = assets != null && assets.shots != null && shot < assets.shots.Length ? assets.shots[shot] : null;
                    if (clip != null) ShotVoices.PlayAt(clip, transform.position, 0.8f);
                }
                return;
            }
            if (Current == State.JumpingOut) { UpdateJumpOut(dtMs); return; }
            if (order != null && UpdateOrder(dtMs)) return;   // an event graph's order (NpcOrder)
            if (IsFreighter)
            {
                if (!Hp.empDisabled) transform.position += transform.forward * NpcTables.FreighterSpeed * dtMs * M;
                return;
            }
            reselectTimer += dtMs;
            boostTimer += dtMs;
            if (IsWingman) UpdateWingman(); else UpdateTargeting();
            if (IsJumper && followingWaypoint) { jumpMs += dtMs; if (jumpMs >= 20000f) { jumpMs = 0f; Current = State.JumpingOut; } }
            else jumpMs = 0f;
            UpdateBoost(dtMs);
            Steer(dtMs);
            Avoid(true, dtMs);
            Avoid(false, dtMs);
        }

        /// <summary>PlayerFighter::update 0xf1b0e..0xf1bc0: smoke and fire switch on when the hull drops below a third of its
        /// maximum and off when it is back at a third; only the crossings act.</summary>
        void UpdateSmoke()
        {
            if (smoke == null) return;
            bool low = Hp.hull < 0.33f * Hp.maxHull;
            if (low == smoking) return;
            smoking = low;
            smoke.SetEmitting(low);
        }

        /// <summary>PlayerFighter::update 0xf3138: the EMP spark systems emit while the ship is disabled.</summary>
        void UpdateSparks()
        {
            bool on = Hp.empDisabled;
            if (on && sparks == null) sparks = new EmpSparks(transform);
            sparks?.SetEmitting(on);
        }

        /// <summary>Freelance Recovery / Salvage: the Hijacker's container comes loose once its EMP is down (the original
        /// tractors it out of the disabled ship; the remake drops it as a crate for the tractor beam).</summary>
        void UpdateMissionCrate()
        {
            bool disabled = Hp.empDisabled;
            if (disabled && !empWasDisabled && Spec.missionCrate >= 0 && loot.Count > 0 && crate == null)
            {
                DropCrate();
                loot = new List<ItemStack>();
            }
            // Player::damageEmp / Player::update: Status+0x134 counts the ships disabled right now (medal 42 Jammer); a ship
            // destroyed while disabled stays counted until the next level, like the original.
            if (disabled && !empWasDisabled && !Achievements.Has(42)) Achievements.Elite(42, ++Session.EmpDisabledNow);
            else if (!disabled && empWasDisabled) Session.EmpDisabledNow--;
            empWasDisabled = disabled;
        }

        /// <summary>The mission container was taken from this ship (dropped by the EMP or collected).</summary>
        public bool MissionCrateTaken => Spec.missionCrate >= 0 && loot.Count == 0;

        public bool Moving => !parked;

        const float RocketReloadMs = 3000f, RocketLifetimeMs = 10000f, RocketSpeed = 8f;
        /// <summary>Level::assignGuns' per-ship damage (gun+0x60 before an item's factor) and projectile speed.</summary>
        float gunBase = 3f, gunSpeed = NpcTables.GunSpeed;

        /// <summary>RocketGun::seekEnemy for an NPC: its current target (PlayerFighter+0x34), while it attacks.</summary>
        Target HomingTarget => attacking && target != null && target.Alive && !target.cloaked ? target : null;

        /// <summary>The target SetOnlyEnemy gave (Player::setEnemy), null when none.</summary>
        public Target ScriptEnemy { get; private set; }

        const float SearchMs = 12000f, SearchReach = 3000f;
        Vector3 lastSeen;
        float searchMs;

        /// <summary>A fleet battle fighter's nearest pick (UpdateTargetingLocal) is made again after this long (ms, randomised down
        /// to half, so the ships' scans spread over the frames).</summary>
        const float NearestHoldMs = 800f;
        float nearestHoldMs;
        Target heldNearest;

        /// <summary>Remake option (Settings.SmarterNpcTargeting, a session rule; NpcTargetRules): the fighter takes the nearest
        /// hostile target in its box instead of the first of the list (the player), spread over the targets. Not for the ships
        /// whose targets a script, a mission or the original's special rules decide: story and mission orbits, a script's only
        /// enemy, mission ships, a turned ship (it goes for the player who shot it, box or not), the Specters (player-only in
        /// free flight), the fleet battles' fighters (their own nearest pick). Wingmen and turrets never get here.</summary>
        bool SmartTargeting =>
            Settings.SmarterNpcTargeting && !NpcTables.InCampaignLevel && NpcTables.LevelFreelanceType < 0 && ScriptEnemy == null
            && !MissionShip && !turnedEnemy && Race != Standing.Specter && traffic != null
            && !(Spec.capitalPart && Spec.capitalHost != null && Spec.capitalHost.fleetBattle);

        static readonly List<NpcTargetRules.Candidate> smartCandidates = new List<NpcTargetRules.Candidate>();
        static readonly List<int> smartIndices = new List<int>();

        Vector3 targetPos;

        // Remake (the user's request 2026-10-22): where on the target's model this ship aims (Target.AimSpot), a new spot
        // every 1.5-3.5 s and with every new target; the original aims at the centre.
        Target aimFor;
        int aimSpot;
        float aimMs;

        // ---- scripted orders (remake: the event graphs' ship nodes, NpcOrder) ------------------------------------------------

        NpcOrder order;
        float orderMs;
        Vector3 leaderLast;
        bool leaderSeen;

        /// <summary>The event order it is carrying out (null: its own AI).</summary>
        public NpcOrder Order => order;

        /// <summary>Modified: its own flying speed (u/ms), what an order without a speed of its own flies at.</summary>
        public float OwnSpeed => baseSpeed;

        int empByPlayer;

        /// <summary>NpcShip.ModelPath of a ship shown as a freighter wreck held at its end (setDeadButSelectable): the prefix
        /// and the CombatAssets.wrecks index; NetProxy shows the same wreck.</summary>
        public const string WreckModelPrefix = "wreck:";
        /// <summary>ModelPath of a turret object built from a turret item (SpawnSpec.turretItem): "turret:" + the item, whose
        /// turret model the multiplayer copies load (NetProxy).</summary>
        public const string TurretModelPrefix = "turret:";

        /// <summary>KIPlayer+0x4c: the cargo list (the scanner's readout).</summary>
        public IReadOnlyList<ItemStack> CargoList => loot;

        /// <summary>KIPlayer::cargoAvailable: something aboard to steal (the Hijacker's mission container drops by itself).</summary>
        public bool HasCargo => Spec.missionCrate < 0 && loot.Exists(s => s.amount > 0);

        /// <summary>PlayerFighter::update 0xf1... sets KIPlayer+0x48 = cargoAvailable() as a fighter starts dying: until it
        /// explodes Radar::draw gives it the crate markers (0x4f2 near, 0x4f1 far, 0x451 / 0x44d off screen) and it is a
        /// salvage target (TractorBeam::update makes its crate at the ship, KIPlayer::createCrate(0)); a dying ship without
        /// cargo has no marker at all. Freighters and fixed objects drop their crate as they die.</summary>
        public bool DyingWithCargo => Current == State.Dying && !IsFixed && !IsFreighter && !IsTurret && HasCargo && !crateDropped
                                      && stealCrate == null;
        public bool Dying => Current == State.Dying;

        Crate stealCrate;   // the crate TractorBeam::update made at the ship, while it exists

        /// <summary>Remake: loot of several kinds given on purpose (a pirate outpost's tier loot, a capital ship's crate) drops a
        /// crate per kind. An ordinary fighter's roll can hold two stacks too (NpcTables.RollLoot): it drops one crate, as the
        /// original (#Discord 2026-10-20: ships killed by NPCs, wingmen or turrets dropped two crates).</summary>
        bool multiKindLoot;

        bool crateDropped;

        const float CrateBurst = 3f;   // several crates fly apart at 3x a crate's usual push
    }
}
