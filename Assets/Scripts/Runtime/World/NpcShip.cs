// NpcShip.cs
// One NPC ship (KIPlayer + Player), Reference/research/npc_traffic_ai.md 4-6 and ship_combat.md 2-5. Created by
// Traffic from a SpawnSpec.
// Fighters (PlayerFighter::update 0xf0d90):
//   relations   hostile = pirates / Void / Specters, or an enemy race by the standing, or turned / alarmed; friend by the
//               standing (recomputed every frame)
//   targeting   enemy list = the player (index 0) + ships of other races; attack whatever is inside the +-50000 box; every
//               5 s: 20 % start / stop flying straight, 30 % re-roll the target else back to the player; neutral and
//               friendly ships never keep the player but attack the first race-hostile ship; no target -> the patrol
//               route (a finished route -> toward the player without firing)
//   steering    speed 2 u/ms (boost 5.5), heading += normalize(dir - fwd) * dt * 48 / 65536, snap when within L1 0.0625;
//               inside +-8000 of the target it steers along its own right vector (circles away), so it never fires
//               close up; visual bank up to 33 deg from the averaged turn, auto-level after 750 ms
//   firing      target inside the +-0.0076 cone (ship-space unit vector x / y) and +-35000 per axis; one NPC gun
//   boost       5 % per 5 s, or after losing 40 % of the hull, for 5..8 s (x1.05 / x0.95 per 30 fps frame)
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
    public class NpcShip : MonoBehaviour
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
        TurretAim turretAim;
        Transform turretBarrel;
        Target turretTarget, turretIgnored;
        float turretPickMs;
        const float TurretRangeUnits = 50000f, TurretLeadUnits = 1500f, TurretPickMs = 3000f, TurretDeathMs = 4500f;
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
        public void SetExhaust(bool on) => modelGo?.GetComponent<AssembledObject>()?.SetExhaust(on, playerEngines);

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

        /// <summary>Multiplayer: another player's hit (NetProxy). Like the player's friendly fire (OnDamaged), it turns on
        /// that player's squad once they took half its hull (a quarter on Extreme); a race that is always hostile needs none.
        /// The hull damage itself comes in as an NPC's hit (no standing or kill credit for this game's player).</summary>
        public void OnRemoteHit(ulong client, int dmg)
        {
            if (alwaysEnemy || IsWingman || Race == Standing.Pirate || Race == Standing.Void || Race == Standing.Specter) return;
            // Remake (CapitalShips): a capital ship, its turrets and escorts turn on another player's squad at the first hit.
            if (Spec.capitalEnhanced && (Spec.capital != 0 || Spec.capitalPart)) { aggressors.Add(client); return; }
            int total = (remoteHullDamage.TryGetValue(client, out int d) ? d : 0) + dmg;
            remoteHullDamage[client] = total;
            if (total >= Hp.maxHull * (Session.IsExtreme ? 0.25f : 0.50f)) aggressors.Add(client);
        }

        /// <summary>Multiplayer: another player's EMP (NetProxy): past a third of its EMP points it turns on them
        /// (OnPlayerEmp), then the EMP lands.</summary>
        public void OnRemoteEmp(ulong client, int emp)
        {
            if (Hp == null || !Target.Alive || Hp.emp <= 0) return;
            if (!alwaysEnemy && !IsWingman && Race != Standing.Void && Race != Standing.Specter)
            {
                int total = (remoteEmp.TryGetValue(client, out int e) ? e : 0) + emp;
                remoteEmp[client] = total;
                if (total > Hp.maxEmp / 3) aggressors.Add(client);
            }
            Hp.DamageEmp(emp);
        }

        /// <summary>Multiplayer: another game's ship shown here (NetProxy), hostile to the local player: a wingman's target.</summary>
        static bool HostileOther(Target t) => t != null && t.enabled && !t.untargetable && !t.isPlayer && t.isShip && t.Alive && t.hostileToPlayer
                                              && t.GetComponent<GoF2Remake.Multiplayer.NetPlayer>() == null;

        /// <summary>Multiplayer (NetOrbit): another player's ship is docked at an object (their hits x0.75), null = none.</summary>
        public static System.Func<Target, bool> RemoteDockedAtObject;

        /// <summary>What its guns can hit: its enemies, plus the other players here (every player ship blocks its shots).</summary>
        public List<Target> HitTargets
        {
            get
            {
                var remote = RemotePlayers;
                bool others = IsWingman && GoF2Remake.Multiplayer.NetGame.Active && Target.NetShips.Count > 0;
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
        /// <summary>Resources path of the assembled prefab it was built from (Traffic), for multiplayer proxies.</summary>
        public string ModelPath { get; set; }
        /// <summary>A freelance mission's ship (FreelanceOrbit): multiplayer never takes it over from its player.</summary>
        public bool MissionShip { get; set; }
        /// <summary>Multiplayer: this player's own stand-in, never shown to the others (a follower's copy of the mission's
        /// mining plant, FreelanceOrbit).</summary>
        public bool LocalOnly { get; set; }
        Route route;
        Gun gun;
        GunRig rig;
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
        GameObject wreck;
        WreckBurn wreckBurn;
        Crate crate;
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

        /// <summary>The shock blast: 'center' and 'radius' in Unity metres.</summary>
        public void InitPush(Vector3 center, float radius)
        {
            // KIPlayer::initPush is empty and PlayerTurret inherits it; only PlayerFighter / PlayerAsteroid push. The turret
            // assemblies on battleships and carriers stay mounted.
            if (IsFixed || IsFreighter || IsTurret || radius <= 0f) return;
            var away = transform.position - center;
            float d = away.magnitude;
            pushTotalMs = pushMs = (1f - Mathf.Min(d / radius, 1f)) * PushMaxMs;
            pushDir = d > 1e-3f ? away / d : Random.onUnitSphere;
            pushTumble = Random.onUnitSphere * 0.2f;
        }

        /// <summary>PlayerFighter::push 0xf3c50 (checked in the disassembly): f = the time left / T; each frame the ship moves
        /// dir x dt x its speed (+0x1e0) x 3 (2 - f) x T / 5000 (a full push 6 -> 12 u/ms) and turns by f x the tumble
        /// (a random unit vector x 0.2 rad per 30 fps frame).</summary>
        void UpdatePush(float dtMs)
        {
            if (pushMs <= 0f) return;
            pushMs -= dtMs;
            float T = Mathf.Max(1f, pushTotalMs);
            float f = Mathf.Max(0f, pushMs) / T;
            transform.position += pushDir * (dtMs * speed * 3f * (2f - f) * (T / PushMaxMs)) * M;
            transform.Rotate(pushTumble.normalized, f * pushTumble.magnitude * Mathf.Rad2Deg * dtMs / 33.3f, Space.World);
        }

        /// <summary>Game rotation identity: facing game +Z = Unity -Z.</summary>
        static readonly Quaternion GameForward = Quaternion.Euler(0f, 180f, 0f);

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
                modelGo = Instantiate(prefab, transform, false);
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
            Target.plateWanted = spec.wantedIndex >= 0;
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
            if (modelGo != null) HullCollision.Attach(modelGo, Target, obstacle);
            if (spec.deadButSelectable) ShowWreckAtEnd();
            Target.Damaged += OnDamaged;
            Target.Died += OnDied;
            lastHull = Hp.hull;
            if (!spec.freighter && spec.fixedObject == null && spec.turretAssembly == null) smoke = new ShipSmoke(transform);

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
                    gun.Hit += OnGunHit;
                }
            }
            route = (spec.route ?? Route.DefaultPatrol(spec.race)).Clone();
            loot = spec.lootItem >= 0 ? new List<ItemStack> { new ItemStack(spec.lootItem, Mathf.Max(1, spec.lootAmount)) }
                 : spec.missionCrate >= 0 ? new List<ItemStack> { new ItemStack(spec.missionCrate, 1) }
                 : spec.noLoot ? new List<ItemStack>() : RollLoot();

            sfx = gameObject.AddComponent<AudioSource>();
            Setup3D(sfx);
            engine = gameObject.AddComponent<AudioSource>();
            engine.playOnAwake = false;
            EngineVoices.Setup3D(engine);   // the engine events' rolloff (0.05 .. 500 m), at most 3 of each event at once
            engine.loop = true;
            engine.clip = GoF2Remake.Modding.ModSounds.Get(assets == null || spec.fixedObject != null || spec.turretAssembly != null || spec.ship == 14 ? null
                        : CombatAssets.Pick(spec.freighter ? assets.freighterEngines : assets.enemyEngines));
            EngineVoices.Register(engine, spec.freighter ? 47 : 46, (spec.freighter ? 0.195f : 0.0759f) * Sfx.EventGain);   // event volumes 47 / 46
            if (engine.clip != null) engine.Play();

            if (spec.startsDead) SetDead();
            if ((spec.race == Standing.Specter || spec.ship == 49) && spec.fixedObject == null && modelGo != null) cloak = new NpcCloak(model);
            // Level::assignGuns: a Most Wanted criminal fires its own weapon at x4.
            if (spec.gunItem >= 0 && gun != null) SetGun(spec.gunItem, spec.gunFactor);
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
            route = (Spec.route ?? Route.DefaultPatrol(Spec.race)).Clone();
            // PlayerFighter::revive 0xf3de0: a revived Void ship or Specter carries nothing (the Void's remains come at death).
            loot = Spec.noLoot || Race == Standing.Void || Race == Standing.Specter ? new List<ItemStack>() : RollLoot();
            crate = null;
            crateDropped = false;
            if (engine != null && engine.clip != null) engine.Play();
        }

        /// <summary>PlayerFighter::PlayerFighter: race 9 (the Void) gets no cargo list (KIPlayer+0x4c = 0), so the scanner
        /// reads "Nothing to salvage." and there is nothing to steal; its 1-3 t Alien Remains are made at death (OnDied).
        /// The others: Generator::getLootList.</summary>
        List<ItemStack> RollLoot() => Spec.race == Standing.Void ? new List<ItemStack>() : NpcTables.RollLoot(db, Spec.freighter);

        // ---- turrets (PlayerTurret::handleTurret / pickEnemy / handleRotation) ---------------------------------------

        void SetupTurret()
        {
            Target.radius = 1000f * M;   // the normal +-1000 cube (Player radius 1000), no collision volume
            Transform pivot = model != null ? model.Find("pivot") : null;
            if (pivot != null) foreach (Transform c in pivot) if (c.name.Contains("_gun")) turretBarrel = c;
            if (pivot != null && turretBarrel != null)
                turretAim = new TurretAim(pivot, turretBarrel)
                {
                    // Pitch counter -600 .. +100 ms at 2 pi / 4096 rad per ms: about 52.7 deg up, 8.8 deg down.
                    pitchMax = 600f * TurretAim.RadPerMs, pitchMin = -100f * TurretAim.RadPerMs,
                };
            gunBase = (int)(NpcTables.GunDamage(Spec, false, true, out gunSpeed) * Spec.gunFactor);   // gunFactor: the remake's CapitalShips
            // Level::assignGuns: a sentry gun object (0x49c0 / 0x49c1 / 0x49c2 = sn_sentry_gun_001..003) fires its item's
            // shot (211 / 212 / 213: attr 9 damage, 11 reload, 12 lifetime, 13 speed); at 0x9e (Harval's, always enemy)
            // x1.5 damage, x1.2 speed and x5 hull; the LevelScript ctor of 0x9e scales their damage by 0.3.
            int sentry = Spec.turretAssembly.StartsWith("sn_sentry_gun_00") ? Spec.turretAssembly[16] - '0' : 0;
            var sentryItem = sentry >= 1 && sentry <= 3 ? db.Item(210 + sentry) : null;
            if (sentryItem != null)
            {
                float dmg = sentryItem.Attr(9), speedU = sentryItem.Attr(13, 22);
                if (!Session.FreePlay && Session.CampaignMission == 0x9e && sentry == 3 && alwaysEnemy)
                {
                    dmg *= 1.5f * 0.3f;
                    speedU *= 1.2f;
                    SetHull(Hp.maxHull * 5);
                }
                int look = sentry == 1 ? 2 : sentry == 2 ? 20 : 14;   // Gun::setIndex 2 / 0x14 / 0xe
                gun = new Gun(db.Item(look) ?? sentryItem, (int)dmg, sentryItem.Attr(11, 430), NpcTables.GunPool, sentryItem.Attr(12, 1000), speedU) { owner = Target };
                rig = new GunRig(gun, WeaponFx.Load(look), fxRootRef, turretBarrel, 2);
                gun.Hit += OnGunHit;
                return;
            }
            var item = db.Item(Spec.race == 1 ? 15 : 20);
            if (item != null)
            {
                // Remake (CapitalShips): an enhanced turret's shots fly 3600 ms (57 600 units), past its 50 000-unit reach.
                gun = new Gun(item, gunBase, NpcTables.GunReloadMs, NpcTables.GunPool,
                                  Spec.capitalEnhanced ? World.CapitalShips.TurretLifetimeMs : NpcTables.GunLifetimeMs, gunSpeed) { owner = Target };
                rig = new GunRig(gun, WeaponFx.Load(item.index), fxRootRef, turretBarrel, 2);
                gun.Hit += OnGunHit;
            }
        }

        void UpdateTurret(float dtMs)
        {
            if (turretAim == null || gun == null) return;
            turretPickMs += dtMs;
            if (turretPickMs > TurretPickMs)
            {
                turretPickMs = 0f;
                turretTarget = PickTurretTarget();
            }
            if (turretTarget == null) return;
            var at = Spec.capitalEnhanced && turretTarget.boxes != null && turretTarget.boxes.Length > 0 ? TurretAimPoint(turretTarget, turretBarrel.position)
                   : turretTarget.transform.position + turretTarget.transform.forward * TurretLeadUnits * M;
            bool aligned = turretAim.Step(at, dtMs);
            if (turretAim.LimitHit) { turretIgnored = turretTarget; turretPickMs += dtMs; return; }   // out of reach: dropped
            if (!aligned || !shootingEnabled) return;
            // Bullets from the barrel frame, the usual 100 ahead in the scaled frame (about 600 units).
            int b = gun.TryFire(turretBarrel.position + turretBarrel.forward * 600f * M, Quaternion.LookRotation(turretBarrel.forward, turretBarrel.up), false);
            if (b >= 0)
            {
                rig.OnShot();
                Sfx.PlayAt(assets != null && assets.shots != null && assets.shots.Length > 0 ? assets.shots[Race == 1 ? 1 : 0] : null, transform.position, 0.6f);
            }
        }

        /// <summary>Remake debug (PlayerHull): one of the turrets on the player's own hull.</summary>
        public bool PlayerOwned { get; private set; }

        /// <summary>A turret on the player's hull (PlayerHull): it aims at whatever is hostile to the player and its
        /// shots pass through the player.</summary>
        public void MakePlayerTurret()
        {
            PlayerOwned = true;
            alwaysFriend = true;
            turretTarget = null;
            if (gun == null) return;
            var before = gun.Ignores;
            gun.Ignores = t => (t != null && t.isPlayer) || (before != null && before(t));
        }

        /// <summary>pickEnemy 0x182e90: the nearest (Euclidean) active target within 50000 that is hostile to this turret's race;
        /// the player only when the turret is hostile to it; the last unreachable one only when nothing else is there.</summary>
        Target PickTurretTarget()
        {
            Target best = null, ignored = null;
            float bestD = TurretRangeUnits * M;
            foreach (var e in enemies)
            {
                if (!Valid(e) || e.cloaked) continue;   // PlayerTurret::handleTurret: no aiming at a cloaked target
                bool candidate = PlayerOwned ? !e.isPlayer && e.isShip && e.hostileToPlayer
                               : e.isPlayer ? Target.hostileToPlayer : e.isShip && e.race >= 0 && Standing.RacesHostile(e.race, Race);
                if (!candidate) continue;
                float d = (TurretAimPoint(e, transform.position) - transform.position).magnitude;
                if (d >= bestD) continue;
                if (e == turretIgnored) { ignored = e; continue; }
                bestD = d;
                best = e;
            }
            return best ?? ignored;
        }

        /// <summary>Where a turret measures and aims: the target's position, or (remake, World.CapitalShips) for an enhanced
        /// turret the nearest point of a big ship's hull (Target.NearestPoint: a capital ship 3 km long is in reach long before
        /// its centre).</summary>
        Vector3 TurretAimPoint(Target e, Vector3 from) => Spec.capitalEnhanced ? e.NearestPoint(from) : e.transform.position;

        /// <summary>The Terran battleship died: every turret of the level takes 9 999 999 (not credited to the player).</summary>
        public void DestroyAsTurret()
        {
            if (!IsTurret || !Target.Alive) return;
            Target.Damage(9999999f, true);
        }

        /// <summary>KIPlayer::setDead: inactive until relaunched.</summary>
        void SetDead()
        {
            cloak?.Stop();
            Current = State.Dead;
            rig?.HideAll();
            empRig?.HideAll();
            secondRig?.HideAll();
            smoking = false;
            smoke?.Clear(); sparks?.Clear(); burn?.SetBurning(false);
            if (wreck != null) Destroy(wreck);
            gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            cloak?.Dispose();
            EngineVoices.Unregister(engine);
        }

        /// <summary>Player::setHitpoints(0) + KIPlayer::setDead (a level script): gone at once, without the tumble, the
        /// explosion, a crate or the kill bookkeeping.</summary>
        public void Vanish()
        {
            Hp.hull = 0;
            Target.hp = 0;
            if (engine != null) engine.Stop();
            SetDead();
        }

        // ---- per frame -------------------------------------------------------------------------------------

        /// <summary>Level::createFighterTurrets: the turret on this ship (45 / 51), null = none.</summary>
        [System.NonSerialized] public NpcShip AttachedTurret;

        /// <summary>The fighter turret shows, hides, sides and dies with its host.</summary>
        void SyncTurret()
        {
            var t = AttachedTurret;
            if (t == null) return;
            if (Current != State.Fly || Gone) { t.DestroyAsTurret(); AttachedTurret = null; return; }
            t.alwaysEnemy = alwaysEnemy;
            t.alwaysFriend = alwaysFriend;
            t.turnedEnemy = turnedEnemy;
            t.SetVisible(modelGo == null || modelGo.activeSelf);
            if (Asleep && !t.Asleep) { t.Asleep = true; } else if (!Asleep && t.Asleep) t.Wake();
            t.enemies.Clear();
            t.enemies.AddRange(enemies);
            t.Target.untargetable = true;
        }

        void Update()
        {
            EngineVoices.Tick();   // once a frame for all ships: the three loudest loops of each engine event
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            SyncTurret();
            if (gun != null)
            {
                gun.Update(dtMs, HitTargets, HomingTarget);
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
            if (Current == State.Dead) { UpdateDead(dtMs); return; }
            if (Current == State.Dying) { UpdateSmoke(); UpdateDying(dtMs); return; }
            UpdateRelations();
            Hp.Update(dtMs);
            // PlayerFighter::handleCloaking: race 10 only (Harval's Scimitar cloaks as a Specter, at 158); scripted ships too.
            if (Race == Standing.Specter) cloak?.Update(dtMs, !Asleep && !inactive, panic, Hp.empDisabled);
            UpdatePush(dtMs);
            UpdateSmoke();
            UpdateSparks();
            UpdateMissionCrate();
            if (Asleep) { UpdateSleep(); return; }
            if (IsTurret) { UpdateTurret(dtMs); return; }
            if (parked || frozen) return;   // parked: a target that neither flies nor shoots
            if (scriptedSpeed >= 0f)
            {
                // AEGeometry::moveForward never touches the fighter's speed (+0x1e0): released, the AI flies at its own
                // speed again (keeping the scripted one left Harval at ~9 u/ms in 158 and 154's fighters at 0).
                transform.position += transform.forward * scriptedSpeed * dtMs * M;
                if (scriptedFire && gun != null && gun.TryFire(transform) >= 0)
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

        /// <summary>§4.2: hostile / friend flags for the markers and the AI.</summary>
        void UpdateRelations()
        {
            int r = Race;
            bool alwaysHostile = r == Standing.Pirate || r == Standing.Void || r == Standing.Specter;
            bool hostile = alwaysHostile || Standing.IsEnemy(r);
            bool friend = !alwaysHostile && Standing.IsFriend(r);
            // PlayerFighter::update's order: always-enemy, then Loma's paid toll (pirates neither hostile nor friendly), then a
            // turned ship, and the always-friend flag last (setAlwaysEnemy doesn't clear it: a story ally stays one).
            if (alwaysEnemy) { hostile = true; friend = false; }
            if (alwaysNeutral) { hostile = false; friend = false; }
            if (r == Standing.Pirate && traffic != null && traffic.LomaTollPaid) { hostile = false; friend = false; }
            if (turnedEnemy) { hostile = true; friend = false; }
            if (HostileToLocalBySquad != null && HostileToLocalBySquad(this)) { hostile = true; friend = false; }   // multiplayer
            int territory = TerritoryToLocal != null ? TerritoryToLocal(this) : 0;   // multiplayer: a faction station's defence
            if (territory > 0 && !turnedEnemy) { hostile = false; friend = true; }
            else if (territory < 0) { hostile = true; friend = false; }
            if (alwaysFriend) { hostile = false; friend = true; }
            Target.hostileToPlayer = hostile;
            Target.friendToPlayer = friend;
        }

        bool InBox(Target t)
        {
            var d = t.transform.position - transform.position;
            float r = (detectRange > 0f ? detectRange : NpcTables.DetectRange) * M;
            return Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r;
        }

        static bool Valid(Target t) => t != null && t.Targetable;

        /// <summary>State 5 (sleeping): a hostile sleeper wakes when the player (or the steered Liberator) comes within its
        /// detect range per axis (+-50 000 by default, not while the player is cloaked) or within +-25 000 (cloaked too);
        /// other ships never wake it. A non-hostile sleeper wakes when its target is within the detect range; fixed objects
        /// and freighters on any enemy within +-50 000. Hostile sleepers stay hidden after the tutorial (PlayerFighter,
        /// index &gt; 1). An inactive ship only wakes by script.</summary>
        void UpdateSleep()
        {
            bool hide = Hidden;
            if (modelGo != null && modelGo.activeSelf == hide) modelGo.SetActive(!hide);
            Target.untargetable = true;
            if (inactive) return;
            bool Near(Vector3 p, float units) { var d = p - transform.position; float r = units * M; return Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r; }
            // Freighters and fixed objects are PlayerFixedObjects: PlayerFixedObject::update (0x17f480) state 5 wakes them on
            // the first active enemy within +-50 000 per axis (a constant: no detect range, and no cloak test, only
            // Player::isActive).
            if (IsFixed || IsFreighter)
            {
                foreach (var e in enemies)
                    if (Valid(e) && Near(e.transform.position, NpcTables.DetectRange)) { Wake(); return; }
                return;
            }
            // PlayerFighter::update: KIPlayer+0x124 is the detect range (50 000 by default; 0 = never by proximity).
            float dr = detectRange >= 0f ? detectRange : NpcTables.DetectRange;
            if (dr <= 0f) return;
            var sleepTarget = SleepTarget(dr);
            if (Target.hostileToPlayer)
            {
                // 0xf19b8: a hostile sleeper (Player+0x5c) has its target delta (+0x16c) overwritten by the player's position,
                // or the steered rocket's (PlayerEgo::isInRocketControl), whatever its target is: within +-25 000 per axis it
                // wakes at once (no cloak test), and state 5 (0xf2750) wakes it within the detect range unless its target
                // (+0x144, the player unless another ship was picked) is cloaked (+0x5e). Other ships never wake it.
                var player = traffic != null ? traffic.Player : null;
                Vector3? at = WeaponSystem.GuidedRocket ?? (player != null ? player.transform.position : (Vector3?)null);
                if (at.HasValue && (Near(at.Value, 25000f) || (sleepTarget != null && !sleepTarget.cloaked && Near(at.Value, dr)))) { Wake(); return; }
                // Multiplayer: another player it is hostile to wakes it the same way.
                var remote = RemotePlayers;
                if (remote != null && HostileToRemote != null)
                    foreach (var r in remote)
                    {
                        if (r == null || !r.Alive || !r.isActiveAndEnabled || !HostileToRemote(this, r)) continue;
                        if (Near(r.transform.position, 25000f) || (!r.cloaked && Near(r.transform.position, dr))) { Wake(); return; }
                    }
            }
            // State 5: the delta to the target itself, within the detect range (not for a cloaked target).
            else if (sleepTarget != null && !sleepTarget.cloaked && Near(sleepTarget.transform.position, dr)) { Wake(); return; }
        }

        /// <summary>PlayerFighter+0x144 while asleep: the TARGETING (§5.3) keeps running in state 5. The first enemy in the
        /// box; a non-hostile ship's pick of the player moves on to index 1, and any pick but the player becomes the first
        /// race-hostile ship at any range; with none (the sleeper is inactive, so the route branch fails Player::isActive)
        /// enemy 0, the player. The 5 s re-roll's random picks are left out.</summary>
        Target SleepTarget(float dr)
        {
            int n = enemies.Count, idx = -1;
            bool pirate = Race == Standing.Pirate;
            float r = dr * M;
            for (int i = 0; i < n; i++)
            {
                var e = enemies[i];
                if (!Valid(e)) continue;
                var d = e.transform.position - transform.position;
                if ((!pirate && turnedEnemy) || (Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r)) { idx = i; break; }
            }
            if (!Target.hostileToPlayer && idx == 0) idx = 1;
            if (idx > 0)
            {
                idx = -1;
                for (int i = 1; i < n; i++)
                    if (Valid(enemies[i]) && Standing.RacesHostile(Race, enemies[i].race)) { idx = i; break; }
            }
            return idx >= 0 ? enemies[idx] : n > 0 ? enemies[0] : null;
        }

        /// <summary>KIPlayer vtable +0x0c: awake (visible, flying, attacking).</summary>
        public void Wake()
        {
            if (Asleep && Spec.guard) traffic?.PirateStationAction(true);   // a pirate base's guard woke
            Asleep = inactive = false;
            Target.untargetable = false;
            if (modelGo != null && Current == State.Fly) modelGo.SetActive(!forcedHidden);
        }

        /// <summary>Places the ship (Unity world position) facing 'forward'.</summary>
        public void Place(Vector3 position, Vector3 forward)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        // ---- level-script controls (LevelScript, Level::createCampaignMission) ------------------------------------

        /// <summary>PlayerFixedObject::setMoving: a freighter flies game +Z at 1 u/ms, or stays parked (still a target).</summary>
        public void SetMoving(bool on) => parked = !on;
        public bool Moving => !parked;

        /// <summary>KIPlayer::setRoute: fly this route (game units) when there is nothing to attack.</summary>
        public void SetRoute(Route r)
        {
            route = r != null ? r.Clone() : Route.DefaultPatrol(Race);
            attacking = false;
            targetIdx = -1;
        }

        /// <summary>Player::setMaxHitpoints + setHitpoints.</summary>
        public void SetHull(int hull, bool alsoMax = true)
        {
            if (alsoMax) Hp.maxHull = Mathf.Max(1, hull);
            Hp.hull = Mathf.Clamp(hull, 1, Hp.maxHull);
            Target.hp = Hp.hull;
            Target.maxHp = Hp.maxHull;
            lastHull = Hp.hull;
        }

        /// <summary>KIPlayer::setRace (index 40: Errkt's freighter becomes Vossk).</summary>
        public void SetRace(int race)
        {
            Spec.race = race;
            Target.race = race;
        }

        /// <summary>setSpeed: a fixed speed (no boosts), u/ms.</summary>
        public void SetSpeed(float unitsPerMs)
        {
            Spec.speed = unitsPerMs;
            speed = baseSpeed = unitsPerMs;
        }

        /// <summary>Level::assignGuns' per-mission guns (index 70: every ship the Disruptor Laser 183 at x2.5 damage).</summary>
        public void SetGun(int itemIndex, float damageFactor)
        {
            var item = db.Item(itemIndex);
            if (item == null || IsFreighter || IsFixed) return;
            rig?.HideAll();
            if (gun != null) gun.Hit -= OnGunHit;
            gun = MakeGun(item, gunBase * damageFactor);
            rig = new GunRig(gun, WeaponFx.Load(item.index), fxRootRef, IsTurret ? turretBarrel : null, 2);
            gun.Hit += OnGunHit;
        }

        /// <summary>Level::assignGuns' second slot: a Wanted flying ship's G'liissk, Harval's Shesha (index 157 / 158).</summary>
        public void SetSecondaryGun(int itemIndex, float damageFactor)
        {
            var item = db.Item(itemIndex);
            if (item == null || IsFreighter || IsFixed || IsTurret) return;
            secondRig?.HideAll();
            secondGun = MakeGun(item, gunBase * damageFactor);
            secondRig = new GunRig(secondGun, WeaponFx.Load(item.index), fxRootRef, null, 2);
            var g = secondGun;
            var r = secondRig;
            secondGun.Hit += (b, hit, point) =>
            {
                GunHit(g, r, b, hit, point);
            };
            useSecond = false;
            slotMs = 0f;
        }

        /// <summary>Level::assignGuns: rockets / missiles (sorts 4, 5, 40) become a RocketGun (speed 8, lifetime 10 000, reload
        /// 3000, homing for 5 / 40 after 1000 ms); every other item only gives the look of the NPC gun (16 u/ms, 3000 ms).</summary>
        Gun MakeGun(ItemData item, float damage)
        {
            bool rocket = item.categoryId == (int)Gun.Kind.Rocket || item.categoryId == (int)Gun.Kind.Missile || item.categoryId == (int)Gun.Kind.ClusterMissile;
            if (!rocket)
                return new Gun(item, damage, NpcTables.GunReloadMs, NpcTables.GunPool, NpcTables.GunLifetimeMs, gunSpeed) { owner = Target, emp = item.Attr(10) };
            int pool = item.categoryId == (int)Gun.Kind.ClusterMissile ? Mathf.Max(1, item.index - 211) : NpcTables.GunPool;
            return new Gun(item, damage, RocketReloadMs, pool, RocketLifetimeMs, RocketSpeed) { owner = Target, homingDelayMs = 1000f };
        }

        const float RocketReloadMs = 3000f, RocketLifetimeMs = 10000f, RocketSpeed = 8f;
        /// <summary>Level::assignGuns' per-ship damage (gun+0x60 before an item's factor) and projectile speed.</summary>
        float gunBase = 3f, gunSpeed = NpcTables.GunSpeed;

        /// <summary>RocketGun::seekEnemy for an NPC: its current target (PlayerFighter+0x34), while it attacks.</summary>
        Target HomingTarget => attacking && target != null && target.Alive && !target.cloaked ? target : null;

        /// <summary>The target SetOnlyEnemy gave (Player::setEnemy), null when none.</summary>
        public Target ScriptEnemy { get; private set; }

        /// <summary>Only this target (the level script aims ships at Errkt's freighter / at the player).</summary>
        public void SetOnlyEnemy(Target t)
        {
            ScriptEnemy = t;
            enemies.Clear();
            if (t != null) enemies.Add(t);
            attacking = false;
            targetIdx = -1;
            reselectTimer = 0f;
        }

        /// <summary>KIPlayer::setActive(false) + parked far away (index 40: the freighter after the wormhole).</summary>
        public void Deactivate()
        {
            Asleep = inactive = true;
            Target.untargetable = true;
            if (engine != null) engine.Stop();
        }

        // ---- wingman ------------------------------------------------------------------------------------------

        /// <summary>KIPlayer::setWingman(true, slot) + the createWingmen setup; 'armed' false in a Challenge.</summary>
        public void MakeWingman(int slot, bool armed)
        {
            IsWingman = true;
            wingSlot = slot;
            wingCommand = 1;
            alwaysFriend = true;
            // Event 48 Spaceship_Engine_Wingmen: the enemy engine's waves at event volume 0.0437 (46: 0.0759).
            if (engine != null) EngineVoices.Register(engine, 48, 0.043652f * Sfx.EventGain);
            // Level::assignGuns: a wingman's laser skips the other ships' per-mission factors.
            gunBase = NpcTables.GunDamage(Spec, true, false, out gunSpeed);
            if (gun != null) gun.damage = gunBase;
            if (!armed)
            {
                rig?.HideAll();
                gun = null;
                return;
            }
            // Level::assignGuns: slot 1 = Dia EMP Mk III, damage 0, 4 bullets, reload 400, 3000 ms, speed 16.
            var emp = db.Item(18);
            if (emp == null) return;
            empGun = new Gun(emp, 0f, 400f, 4, 3000f, 16f) { owner = Target, emp = emp.Stat("empDamage", 8) };
            empRig = new GunRig(empGun, WeaponFx.Load(18), fxRootRef, null, 2);
            empGun.Hit += (b, hit, point) => { if (hit.hitpoints != null) hit.hitpoints.DamageEmp((int)empGun.emp); empRig.ShowImpact(point); };
        }

        /// <summary>PlayerFighter::setWingmanCommand 0xf096c: 1 fire at will (boosts off for good), 2 secure the next
        /// waypoint (a clone of the player's route), 3 attack the locked ship; 0 toggles laser / EMP blaster.</summary>
        public void WingmanCommand(int command, Target locked, Route playerRoute)
        {
            if (!IsWingman || Current != State.Fly) return;
            switch (command)
            {
                case 0: useEmp = !useEmp; return;
                case 1: evasionOff = true; boosting = panic = false; break;
                case 2:
                    if (playerRoute == null || playerRoute.Waypoint == null) return;
                    scout = playerRoute.Clone();
                    scout.index = playerRoute.index;
                    scoutStart = playerRoute.index;
                    reselectTimer = 5001f;
                    break;
                case 3:
                    if (locked == null || !locked.Alive) return;   // no lock: ignored
                    wingTarget = locked;
                    reselectTimer = 5001f;
                    break;
            }
            wingCommand = command;
            speed = baseSpeed;
        }

        /// <summary>The wingman branches of the target selection and the formation route.</summary>
        void UpdateWingman()
        {
            target = null;
            attacking = false;
            followingWaypoint = false;
            var player = traffic.Player != null ? traffic.Player.transform : null;
            if (wingCommand == 3)
            {
                if (wingTarget == null || !wingTarget.Alive) { wingCommand = 1; wingTarget = null; }
                else if (InBox(wingTarget)) { target = wingTarget; attacking = true; }
            }
            if (wingCommand == 2 && scout != null)
            {
                scout.Update(ToGame(transform.position));
                var wp = scout.Waypoint;
                if (wp.HasValue && scout.index <= scoutStart) { targetPos = ToUnity(wp.Value); followingWaypoint = true; return; }
                scout = null;
                wingCommand = 1;
            }
            if (!attacking && wingCommand != 3 && gun != null)
                foreach (var s in traffic.Ships)
                {
                    if (s == this || s.Gone || s.IsWingman || s.Current != State.Fly || s.Hidden) continue;
                    if (s.Target.Alive && s.Target.hostileToPlayer) { target = s.Target; attacking = true; break; }
                }
            // Multiplayer: another game's hostile ship here (the orbit's authority runs them).
            if (!attacking && wingCommand != 3 && gun != null && GoF2Remake.Multiplayer.NetGame.Active)
                foreach (var t in Target.NetShips)
                    if (HostileOther(t) && InBox(t)) { target = t; attacking = true; break; }
            if (target != null) { targetPos = target.transform.position; return; }
            if (player == null) { targetPos = transform.position + transform.forward; return; }
            // Formation point (per frame a one-point route).
            Vector3 f = player.forward, r = player.right, u = player.up;
            Vector3 off = wingSlot == 0 ? -r * 4000f - f * 3000f : wingSlot == 1 ? r * 4000f - f * 3000f : u * 2000f - f * 2000f;
            targetPos = player.position + off * M;
            followingWaypoint = true;
        }

        /// <summary>Remake option (Settings.CloakLosesPursuers, off = the original, which keeps chasing a cloaked target
        /// and only holds its fire): a cloaked target is lost. The ship flies to where it was last seen, then back to its
        /// route.</summary>
        static bool LostToCloak(Target t) => t != null && t.cloaked && Settings.CloakLosesPursuers;   // a session: the host's (NetRules)
        static bool Pursuable(Target t) => Valid(t) && !LostToCloak(t);
        const float SearchMs = 12000f, SearchReach = 3000f;
        Vector3 lastSeen;
        float searchMs;

        /// <summary>§5.3 target selection.</summary>
        void UpdateTargeting()
        {
            if (attacking && target != null && LostToCloak(target)) { lastSeen = target.transform.position; searchMs = SearchMs; }
            if (searchMs > 0f)
            {
                searchMs -= Time.deltaTime * 1000f;
                if ((lastSeen - transform.position).sqrMagnitude < SearchReach * M * SearchReach * M) searchMs = 0f;
            }
            // Multiplayer: another player it is hostile to, kept while valid, hostile and in the box.
            if (remoteTarget != null)
            {
                if (Pursuable(remoteTarget) && HostileToRemote != null && HostileToRemote(this, remoteTarget) && InBox(remoteTarget))
                {
                    target = remoteTarget;
                    targetPos = target.transform.position;
                    attacking = true;
                    followingWaypoint = false;
                    return;
                }
                remoteTarget = null;
            }
            UpdateTargetingLocal();
            if (attacking && target != null) return;
            var remote = RemotePlayers;
            if (remote == null || HostileToRemote == null) return;
            foreach (var r in remote)
            {
                if (!Pursuable(r) || !HostileToRemote(this, r) || !InBox(r)) continue;
                remoteTarget = r;
                target = r;
                targetPos = r.transform.position;
                attacking = true;
                followingWaypoint = false;
                return;
            }
        }

        void UpdateTargetingLocal()
        {
            int n = enemies.Count;
            int idx = targetIdx;
            if (idx >= n) idx = -1;
            if (!attacking) idx = -1;
            else if (idx >= 0 && !Pursuable(enemies[idx])) attacking = false;
            bool pirate = Race == Standing.Pirate;
            if (reselectTimer < 5001f)
            {
                if (!attacking)
                    for (int i = 0; i < n; i++)
                        if (Pursuable(enemies[i]) && ((!pirate && turnedEnemy) || InBox(enemies[i]))) { idx = i; attacking = true; break; }
            }
            else
            {
                drift = !drift && Random.Range(0, 100) < 20;
                reselectTimer = 0f;
                if (Random.Range(0, 100) < 30 && n > 1)
                {
                    attacking = false;
                    for (int k = 0; k < 5; k++)
                    {
                        int i = Random.Range(0, n);
                        if (Pursuable(enemies[i]) && ((!pirate && turnedEnemy) || InBox(enemies[i]))) { idx = i; attacking = true; break; }
                    }
                    if (!attacking) idx = 0;
                }
                else idx = 0;
                // The 5 s re-roll: a target outside the box is dropped (a turned ship too: it flies its route until the next one).
                if (n > 0 && Pursuable(enemies[idx])) { if (!InBox(enemies[idx])) idx = -1; }
                else { idx = -1; attacking = false; }
            }
            if (!Target.hostileToPlayer && idx == 0) { idx = 1; attacking = false; }
            if (idx > 0)
            {
                idx = -1;
                for (int i = 1; i < n; i++)
                {
                    var e = enemies[i];
                    if (!Pursuable(e)) continue;
                    if (Standing.RacesHostile(Race, e.race)) { idx = i; attacking = true; break; }
                }
            }
            targetIdx = idx;
            followingWaypoint = false;
            target = null;
            if (idx < 0 || idx >= n)
            {
                if (searchMs > 0f) { attacking = false; targetPos = lastSeen; followingWaypoint = true; return; }   // the option
                var wp = route.Waypoint;
                // A finished route: the target is the player (PlayerFighter+0x144), so the break-off circle applies, but it isn't
                // an attack: no firing.
                if (wp == null && LostToCloak(traffic.Player))   // the option: no circling a cloaked player, straight on
                { attacking = false; targetPos = transform.position + transform.forward * (20000f * M); followingWaypoint = true; }
                else if (wp == null) { target = traffic.Player; attacking = false; targetPos = traffic.Player != null ? traffic.Player.transform.position : transform.position; }
                else { route.Update(ToGame(transform.position)); wp = route.Waypoint; targetPos = wp.HasValue ? ToUnity(wp.Value) : transform.position; followingWaypoint = true; }
            }
            else { target = enemies[idx]; targetPos = target.transform.position; }
        }

        Vector3 targetPos;

        /// <summary>§5.4 fire, turn, bank, move.</summary>
        void Steer(float dtMs)
        {
            var dir = targetPos - transform.position;
            // PlayerFighter::update's fly state: the player docked at an object (PlayerEgo::isDockedToDockingPoint) is circled
            // at +-12000 instead of +-8000, and not fired at while it is above the ship (0xf2cfa).
            bool dockedPlayer = target != null && target.isPlayer && ObjectDocking.PlayerDocked;
            if (target != null && !followingWaypoint)
            {
                float r = (dockedPlayer ? 12000f : NpcTables.BreakOffRange) * M;
                if (Mathf.Abs(dir.x) < r && Mathf.Abs(dir.y) < r && Mathf.Abs(dir.z) < r) dir = transform.right;
            }
            if (dir.sqrMagnitude < 1e-8f) dir = transform.forward;
            var dirN = dir.normalized;
            var local = transform.InverseTransformDirection(dirN);

            if (attacking && !followingWaypoint && target != null)
            {
                if (target.untargetable || target.cloaked) attacking = false;   // a cloaked player: chased, never fired at
                else
                {
                    var d = targetPos - transform.position;
                    float fr = NpcTables.FireRange * M;
                    if (Mathf.Abs(local.x) < NpcTables.FireCone && Mathf.Abs(local.y) < NpcTables.FireCone
                        && Mathf.Abs(d.x) < fr && Mathf.Abs(d.y) < fr && Mathf.Abs(d.z) < fr)
                    {
                        var firing = useEmp && empGun != null ? empGun : useSecond && secondGun != null ? secondGun : gun;
                        if (firing == null || !target.Targetable) attacking = false;
                        else if (shootingEnabled && !RadarHidden && !(dockedPlayer && target.transform.position.y > transform.position.y)
                                 && firing.TryFire(transform) >= 0)
                        {
                            int shot = NpcTables.ShotSound(Race);
                            // The rigs' own fx (WeaponFx.Load was a formatted path and a Resources lookup per shot).
                            var clip = firing == empGun ? (empRig?.fx ?? WeaponFx.Load(18))?.Shot
                                     : firing == secondGun ? (secondRig?.fx ?? WeaponFx.Load(secondGun.itemIndex))?.Shot
                                     : assets != null && assets.shots != null && shot < assets.shots.Length ? assets.shots[shot] : null;
                            if (clip != null) ShotVoices.PlayAt(clip, transform.position, 0.8f);   // shared voices (FEV max_playbacks)
                        }
                    }
                }
            }

            var fwd = transform.forward;
            // LevelScript::process: during the launch / arrival camera every wingman drifts (KIPlayer+0x129, no steering).
            bool noSteer = drift || (IsWingman && traffic != null && traffic.LaunchCameraRunning != null && traffic.LaunchCameraRunning());
            if (!noSteer && !Hp.empDisabled)
            {
                var delta = dirN - fwd;
                var h = delta.sqrMagnitude > 1e-10f ? (fwd + delta.normalized * (dtMs * 48f / 65536f)).normalized : dirN;
                var diff = h - dirN;
                if (Mathf.Abs(diff.x) + Mathf.Abs(diff.y) + Mathf.Abs(diff.z) < 0.0625f) h = dirN;
                float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(fwd, h), -1f, 1f));
                if (Vector3.Dot(transform.right, h) > 0f) a = -a;
                turnRing[ringIndex] = a * 33.3f / dtMs;   // per 30 fps frame, like the original's samples
                ringIndex = (ringIndex + 1) % turnRing.Length;
                if (ringFilled < turnRing.Length) ringFilled++;
                float avg = 0f;
                for (int i = 0; i < ringFilled; i++) avg += turnRing[i];
                avg /= ringFilled;
                bankTarget = Mathf.Clamp(avg * 750f * 15.139f, -750f, 750f);
                transform.rotation = Quaternion.LookRotation(h, transform.up);
            }
            else { bankTarget = 0f; ringFilled = 0; }

            // Bank toward the target at dt * 1.25 / 3.9 per ms; after 750 ms at the target the roll levels out.
            float step = dtMs * 1.25f / 3.9f;
            float before = bank;
            bank = Mathf.MoveTowards(bank, bankTarget, step);
            if (Mathf.Approximately(bank, before)) { levelTimer -= dtMs; if (levelTimer <= 0f) levelling = true; }
            else levelTimer = 750f;
            if (model != null) model.localRotation = Quaternion.AngleAxis(bank * Mathf.PI / 4096f * Mathf.Rad2Deg, Vector3.forward);
            if (levelling) Roll(dtMs);

            if (!Hp.empDisabled) transform.position += transform.forward * speed * dtMs * M;
        }

        // ---- scripted orders (remake: the event graphs' ship nodes, NpcOrder) ------------------------------------------------

        NpcOrder order;
        float orderMs;
        Vector3 leaderLast;
        bool leaderSeen;

        /// <summary>The event order it is carrying out (null: its own AI).</summary>
        public NpcOrder Order => order;

        /// <summary>Gives the ship an order (null: back to its own AI). Wakes a sleeper, lets a parked ship fly; "hold" is
        /// SetHold, "jump" JumpOut.</summary>
        public void SetOrder(NpcOrder o)
        {
            order = o;
            orderMs = 0f;
            leaderSeen = false;
            speed = baseSpeed;
            if (o == null) return;
            if (Asleep) Wake();
            parked = frozen = false;
            scriptedSpeed = -1f;
        }

        /// <summary>Holds still where it is (a target that neither flies nor shoots) until another order or "resume".</summary>
        public void SetHold() { order = null; parked = true; }

        /// <summary>Back to its own AI (its route, its targets), flying.</summary>
        public void Resume() { order = null; parked = false; speed = baseSpeed; }

        /// <summary>State 6: accelerates away and is gone (the jumpers' exit). Nobody's kill (an event counts it as gone).</summary>
        public void JumpOut()
        {
            order = null;
            parked = false;
            Target.killedByNpc = true;
            if (Current == State.Fly) Current = State.JumpingOut;
        }

        /// <summary>Gone without a fight (an order's dock / vanish): nobody's kill.</summary>
        void LeaveQuietly()
        {
            order = null;
            Target.killedByNpc = true;
            Vanish();
        }

        void FinishOrder()
        {
            var then = order != null ? order.then : NpcOrder.Then.Resume;
            order = null;
            speed = baseSpeed;
            switch (then)
            {
                case NpcOrder.Then.Hold: parked = true; break;
                case NpcOrder.Then.Vanish: LeaveQuietly(); break;
                case NpcOrder.Then.Jump: JumpOut(); break;
            }
        }

        /// <summary>One frame of the order; false = nothing done (the order ended: the ship's own update goes on).</summary>
        bool UpdateOrder(float dtMs)
        {
            orderMs += dtMs;
            var o = order;
            var pos = transform.position;
            float own = IsFreighter ? NpcTables.FreighterSpeed : baseSpeed;
            float fly = o.speed > 0f ? o.speed : own;
            Vector3 goal;
            switch (o.kind)
            {
                case NpcOrder.Kind.Move:
                {
                    if (o.points.Count == 0) { FinishOrder(); return false; }
                    goal = o.points[Mathf.Clamp(o.index, 0, o.points.Count - 1)];
                    if ((goal - pos).sqrMagnitude < (o.radius * M) * (o.radius * M))
                    {
                        o.index++;
                        if (o.index >= o.points.Count)
                        {
                            if (!o.loop) { FinishOrder(); return order != null || parked; }
                            o.index = 0;
                        }
                        goal = o.points[o.index];
                    }
                    break;
                }
                case NpcOrder.Kind.Follow:
                {
                    var leader = o.leader;
                    if (leader == null || !leader.gameObject.activeInHierarchy) { FinishOrder(); return false; }
                    var slot = leader.position + leader.rotation * (o.offset * M);
                    // The leader's speed (units / ms) from its movement; aim ahead along its heading so the ship lines up with
                    // it, and close the gap along that heading by the speed.
                    float leaderSpeed = leaderSeen && dtMs > 0f ? (leader.position - leaderLast).magnitude / M / dtMs : own;
                    leaderLast = leader.position;
                    leaderSeen = true;
                    var lead = leader.forward;
                    goal = slot + lead * (3000f * M);
                    float behind = Vector3.Dot(slot - pos, lead) / M;   // units the ship trails its slot by
                    float far = (slot - pos).magnitude / M;
                    fly = far > 6000f ? Mathf.Max(own, NpcTables.BoostSpeed)
                        : Mathf.Clamp(leaderSpeed + behind * 0.002f, 0.2f, Mathf.Max(NpcTables.BoostSpeed, leaderSpeed * 1.5f));
                    if (far > 6000f) goal = slot;
                    break;
                }
                case NpcOrder.Kind.Attack:
                {
                    var t = o.target;
                    if (t == null || !t.Alive || !t.gameObject.activeInHierarchy) { FinishOrder(); return false; }
                    target = t;
                    targetPos = t.transform.position;
                    attacking = true;
                    followingWaypoint = false;
                    drift = false;
                    speed = fly;
                    UpdateBoost(dtMs);
                    Steer(dtMs);
                    Avoid(true, dtMs);
                    Avoid(false, dtMs);
                    return true;
                }
                case NpcOrder.Kind.Flee:
                {
                    if (orderMs >= o.seconds * 1000f) { order = null; JumpOut(); return Current == State.JumpingOut; }
                    var away = pos - o.from;
                    if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                    goal = pos + away.normalized * (100000f * M);
                    fly = Mathf.Max(fly, NpcTables.BoostSpeed);
                    break;
                }
                case NpcOrder.Kind.Dock:
                {
                    goal = Vector3.zero;   // the station sits at the orbit's origin
                    bool inside = false;
                    foreach (var ob in Obstacle.All)
                        if (ob != null && ob.landmark && ob.Active && ob.Touches(pos, out _)) { inside = true; break; }
                    if (inside || pos.sqrMagnitude < (o.radius * M) * (o.radius * M)) { LeaveQuietly(); return true; }
                    break;
                }
                default:
                    FinishOrder();
                    return false;
            }
            // An escort fights what it meets (its own targeting), then goes on with the order.
            if (o.fight && !IsFreighter && gun != null)
            {
                UpdateTargeting();
                if (attacking && target != null && !followingWaypoint)
                {
                    UpdateBoost(dtMs);
                    Steer(dtMs);
                    Avoid(true, dtMs);
                    Avoid(false, dtMs);
                    return true;
                }
            }
            target = null;
            attacking = false;
            followingWaypoint = true;
            drift = false;   // the AI's 5 s "drift" (no steering) left over from its own targeting
            targetPos = goal;
            // Remake: slower while facing away from the goal, so the turn (the original's fixed turn rate: a circle of
            // speed / 0.00073 units) stays tight; full speed once lined up.
            var toGoal = goal - pos;
            float align = toGoal.sqrMagnitude > 1e-6f ? Vector3.Dot(transform.forward, toGoal.normalized) : 1f;
            fly = Mathf.Max(Mathf.Min(fly, 0.6f), fly * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((align + 0.2f) / 0.9f)));
            speed = fly;
            Steer(dtMs);
            if (o.kind != NpcOrder.Kind.Dock) Avoid(true, dtMs);   // docking flies into the station on purpose
            Avoid(false, dtMs);
            return true;
        }

        /// <summary>PlayerFighter::roll: brings right.y to 0 with up.y > 0 at 0.00075 rad/ms (0.00025 near level).</summary>
        void Roll(float dtMs)
        {
            float rx = transform.right.y, uy = transform.up.y;
            if (Mathf.Abs(rx) < 0.015f && uy > 0f) { levelling = false; return; }
            float rate = uy < 0f || Mathf.Abs(rx) > 0.3f ? 0.00075f : 0.00025f;
            if (rx >= 0f) rate = -rate;
            transform.Rotate(0f, 0f, rate * Mathf.Min(dtMs, 60f) * Mathf.Rad2Deg, Space.Self);
        }

        /// <summary>§5.5 boost and panic.</summary>
        void UpdateBoost(float dtMs)
        {
            float frames = dtMs / 33.3f;
            if (Hp.hull < lastHull)
            {
                damageSinceBoost += lastHull - Hp.hull;
                lastHull = Hp.hull;
                if (damageSinceBoost >= 0.4f * Hp.maxHull) { damageSinceBoost = 0; boostTimer = 10000f; panic = true; }
            }
            if (Spec.speed > 0f || evasionOff) return;   // a fixed-speed ship (setSpeed) / a wingman after "Fire at will" never boosts
            if (boostTimer > 5000f && !boosting)
            {
                boostTimer = 0f;
                if (panic || Random.Range(0, 100) < 5)
                {
                    boostDuration = 5000f + Random.Range(0, 3000);
                    boosting = true;
                    targetSpeed = NpcTables.BoostSpeed;
                }
            }
            if (!boosting) return;
            if (boostTimer > boostDuration) { boostTimer = 0f; panic = false; targetSpeed = NpcTables.BaseSpeed; }
            if (targetSpeed <= 0f) return;
            speed *= Mathf.Pow(speed < targetSpeed ? 1.05f : 0.95f, frames);
            if (speed >= NpcTables.BoostSpeed || speed < NpcTables.BaseSpeed)
            {
                speed = targetSpeed;
                if (Mathf.Approximately(targetSpeed, NpcTables.BaseSpeed)) { boosting = false; targetSpeed = 0f; }
            }
        }

        /// <summary>§5.7 (PlayerFighter::update, +0x13a): the first landmark / ship whose volumes contain the fighter turns it
        /// away (direction += (away - fwd) * speed * 0.03, up = world up) and moves it one extra step.</summary>
        void Avoid(bool landmarks, float dtMs)
        {
            var all = Obstacle.All;
            var pos = transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || o.landmark != landmarks || !o.Active) continue;
                if (!o.Touches(pos, out int index)) continue;
                var p = o.ProjectionVector(pos, index);
                if (p == Vector3.zero) continue;
                var fwd = transform.forward;
                var dir = (fwd + (p - fwd) * speed * 0.03f).normalized;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                transform.position += transform.forward * speed * dtMs * M;
                return;
            }
        }

        /// <summary>State 6: x1.1 per (30 fps) frame, gone above 100 u/ms.</summary>
        void UpdateJumpOut(float dtMs)
        {
            speed *= Mathf.Pow(1.1f, dtMs / 33.3f);
            transform.position += transform.forward * speed * dtMs * M;
            if (speed > 100f) SetDead();
        }

        // ---- combat ----------------------------------------------------------------------------------------

        void OnGunHit(int bullet, Target hit, Vector3 point) => GunHit(gun, rig, bullet, hit, point);

        static bool Contains(IReadOnlyList<Target> list, Target t)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == t) return true;
            return false;
        }

        /// <summary>Gun::calcCharacterCollision 0x17e154 for an NPC bullet: a non-hostile ship's stray shot at the player does
        /// 20 %, a hostile one x0.75 while the player is docked at an object (PlayerEgo::isDockedToDockingPoint); the item's
        /// EMP (attr 10, Gun::setIndex) lands too (NPC against NPC: the player has no EMP pool).</summary>
        void GunHit(Gun g, GunRig r, int bullet, Target hit, Vector3 point)
        {
            float dmg = g.damage;
            if (hit.isPlayer && !Target.hostileToPlayer) dmg = (int)(dmg * 0.2f);   // stray fire from a non-hostile ship
            else if (!hit.isPlayer && RemotePlayers != null && HostileToRemote != null && Contains(RemotePlayers, hit) && !HostileToRemote(this, hit))
                dmg = (int)(dmg * 0.2f);   // multiplayer: the same for another player it isn't after
            else if (hit.isPlayer && ObjectDocking.PlayerDocked) dmg = (int)(dmg * 0.75f);
            else if (!hit.isPlayer && RemoteDockedAtObject != null && RemoteDockedAtObject(hit)) dmg = (int)(dmg * 0.75f);   // multiplayer
            hit.Damage(dmg, true, g.bullets[bullet].velocity);
            if (g.emp > 0f && !hit.isPlayer && hit.hitpoints != null) hit.hitpoints.DamageEmp((int)g.emp);
            r.ShowImpact(point);
        }

        /// <summary>Player::damage friendly-fire bookkeeping (§4.5), hits by the player only.</summary>
        void OnDamaged(Target t, int dmg, bool byNpc)
        {
            // Player::damage on a Most Wanted criminal (+0x3e): Level::attackWanted instead of the friendly-fire rules.
            if (!byNpc && Spec.wantedIndex >= 0) { traffic.AttackWanted(this, dmg); return; }
            // Player::damage 0xafd80: hitting a pirate in Loma arms and alarms them all and revokes the toll.
            if (!byNpc && Race == Standing.Pirate) traffic.LomaPirateHit();
            if (byNpc || alwaysEnemy || noFriendlyFire || IsWingman || Race == Standing.Void || Race == Standing.Specter) return;
            if (Target.hostileToPlayer && !turnedEnemy) return;
            if (Race != traffic.SystemRace && Race != traffic.AttackRace) return;
            damageByPlayer += dmg;
            bool hc = Session.IsExtreme;
            float max = Hp.maxHull;
            // Player::damage: a signature of this race, or any of races 0..3 at the higher threshold, becomes invalid.
            int sig = Standing.SignatureRace;
            if (sig >= 0 && Race <= 3 && ((Race == sig && damageByPlayer >= max * (hc ? 0.10f : 0.33f)) || damageByPlayer >= max * (hc ? 0.25f : 0.50f))
                && Standing.InvalidateSignature())
                traffic.Warn(Localization.Get(324));
            if (damageByPlayer > max * (hc ? 0.10f : 0.33f)) traffic.FriendTurnedEnemy(Race);
            if (damageByPlayer >= max * (hc ? 0.25f : 0.50f)) turnedEnemy = true;
            if (damageByPlayer >= max * (hc ? 0.40f : 0.66f)) traffic.AlarmAllFriends(Race, true);
        }

        int empByPlayer;

        /// <summary>Player::damageEmp by the player, before the EMP lands: a Wanted criminal counts as attacked; a ship of the
        /// system's race (not a wingman, the Void, a Specter or an always-enemy) past a third of its EMP points turns on the
        /// player ("Hold your fire!", Level::friendTurnedEnemy).</summary>
        public void OnPlayerEmp(int emp)
        {
            if (alwaysEnemy) return;
            if (Race != Standing.Void && Race != Standing.Specter && Spec.wantedIndex >= 0) { traffic.AttackWanted(this, 0); return; }
            if (IsWingman || Race == Standing.Void || Race == Standing.Specter || Race != traffic.SystemRace) return;
            empByPlayer += emp;
            if (empByPlayer > Hp.maxEmp / 3) { turnedEnemy = true; traffic.FriendTurnedEnemy(Race); }
        }

        /// <summary>Player::damageEmp's disable: the system's race all turn hostile (alarmAllFriends(race, false), no radio);
        /// Standing::applyDisable unless it is a Wanted criminal.</summary>
        public void OnPlayerDisabled()
        {
            if (!alwaysEnemy && Race != Standing.Void && Race != Standing.Specter && Race == traffic.SystemRace)
                traffic.AlarmAllFriends(Race, false);
            if (Spec.wantedIndex < 0 && Race >= 0 && Race <= 3) Standing.ApplyDelict(Race, 2);
        }

        /// <summary>Hull &lt; 1: the dying state (§5.10) / the freighter wreck (§6).</summary>
        void OnDied(Target t)
        {
            cloak?.Stop();
            // PlayerFighter's death: a hostile Void ship leaves 1-3 t Alien Remains (131).
            if (Race == Standing.Void && !Spec.noLoot && Target.hostileToPlayer && !loot.Exists(s => s.item == 131))
                loot = new List<ItemStack> { new ItemStack(131, Random.Range(0, 3) + 1) };
            traffic.OnShipDied(this, !Target.killedByNpc);
            // Remake mods: a ship the player destroys may drop a mod blueprint (blueprints.json "drops") in a data crate.
            if (!Target.killedByNpc && !IsWingman && !IsTurret)
            {
                int bp = Modding.ModBlueprints.RollDrop(Race);
                if (bp >= 0) Crate.Spawn(transform.position, new[] { new ItemStack(bp, 1) }, Race, Crate.LookJunk);
            }
            if (IsWingman) Wingmen.Died(Target.displayName);   // Level::wingmanDied: gone from the contract
            if (Spec.ship == 14 && !Target.killedByNpc) Session.BattleshipsDestroyed++;   // Status+0x118
            Current = State.Dying;
            sparks?.Clear();
            deathDir = transform.forward;
            if (engine != null) engine.Stop();
            Sfx.PlayAt(assets != null ? CombatAssets.Pick(assets.shipDestroyed) : null, transform.position);
            if (IsTurret)
            {
                // PlayerTurret::update: sound 22, an explosion with fire streaks, the turret gone at once, inactive after 4500 ms.
                dyingMs = TurretDeathMs;
                if (modelGo != null) modelGo.SetActive(false);
                rig?.HideAll();
                Explosion.Spawn(0, transform.position, transform.forward, 1f, assets != null ? CombatAssets.Pick(assets.garbageExplosion) : null, true);
                return;
            }
            if (Spec.group == NpcGroup.Outpost) traffic.PirateStationAction(false);   // a pirate base's outpost destroyed
            // PlayerFixedObject::update: every turret of the level goes too (remake, CapitalShips: an enhanced one's own only; in a
            // fleet battle the Vossk battleship's stay).
            if (Spec.ship == 14) { if (Spec.capitalEnhanced) traffic.DestroyCapitalTurrets(Spec); else traffic.DestroyTurrets(); }
            if (IsFixed || IsFreighter)
            {
                // PlayerFixedObject::update 0x17f6b4: record 22 (23 for the battleship and the Pirate Outpost) on the wreck,
                // and an explosion with fire streaks at once; the big one follows when the wreck animation ends.
                wreckBurn ??= new WreckBurn(transform, Spec.ship == 14 || Spec.fixedObject == "station_pirates");
                wreckBurn.SetEmitting(true);
                Explosion.Spawn(0, transform.position, transform.forward, 1f, null, true);
            }
            if (IsFixed)
            {
                DropCrate();
                // PlayerFixedObject::update state 3: the hull swapped for the wreck animation (plays once), smoke.
                dyingMs = Spec.deathMs > 0f ? Spec.deathMs : 20000f;
                if (Spec.wreckPrefab != null && modelGo != null)
                {
                    wreck = Instantiate(Spec.wreckPrefab, transform, false);
                    float len = PartAnimation.PlayOnce(wreck);
                    if (len > 0f) dyingMs = len;
                    modelGo.SetActive(false);
                }
                crate?.DelayExpiry(dyingMs);   // the crate's 60 s run from state 4, the end of the wreck animation
            }
            else if (IsFreighter)
            {
                dyingMs = 10000f;
                var wreckPrefab = WreckPrefab(assets, Spec.ship, Race);
                if (wreckPrefab != null && model != null)
                {
                    wreck = Instantiate(wreckPrefab, transform, false);
                    // PlayerFixedObject::update state 3: the wreck takes the hull's matrix as it is (AEGeometry::setMatrix),
                    // so it faces the same way and keeps the battleship's x2. Its collision boxes are another matter
                    // (CollisionVolume.ForWreck).
                    wreck.transform.localRotation = model.localRotation;
                    wreck.transform.localScale = model.localScale;
                    float len = PartAnimation.PlayOnce(wreck);
                    if (len > 0f) dyingMs = len;
                    modelGo.SetActive(false);
                }
                DropCrate();
            }
            else
            {
                dyingMs = 1500f + Random.Range(0, 1500);
                // PlayerFighter: the death burn (record 9, +0x19c) through the tumble.
                burn ??= new ShipBurn(transform);
                burn.SetBurning(true);
                spinAxis = new Vector3(Random.Range(0, 200) - 100, Random.Range(0, 200) - 100, Random.Range(0, 200) - 100).normalized;
            }
        }

        /// <summary>A freighter's wreck (setWreckedMeshId: cargo_00N_(race)_explosion_anim by race, the battleship's for
        /// ship 14; CombatAssets.wrecks), null when missing.</summary>
        public static GameObject WreckPrefab(CombatAssets assets, int ship, int race) =>
            assets != null && assets.wrecks != null && assets.wrecks.Length == 5 ? assets.wrecks[WreckIndex(ship, race)] : null;

        /// <summary>The CombatAssets.wrecks index: Terran 0, Vossk 1, Nivelian 2, Midorian 3, the battleship 4.</summary>
        public static int WreckIndex(int ship, int race) => ship == 14 ? 4 : race == 1 ? 1 : race == 2 ? 2 : race == 3 ? 3 : 0;

        /// <summary>PlayerFixedObject::setDeadButSelectable 0x180248 (the Supernova wrecks with a hidden blueprint): the
        /// hull's geometry is replaced by the wreck's (+0x120, setWreckedMeshId), SetAnimationRangeInTime puts its animation
        /// at the end and nothing advances it; HP 1 and setVulnerable(false). Still lockable and dockable (docking type 3),
        /// no explosion, no fire (WreckBurn is the dying state's). Its docking points (sets 11-14) fit the wreck's end pose.</summary>
        void ShowWreckAtEnd()
        {
            Target.invulnerable = true;
            var prefab = WreckPrefab(assets, Spec.ship, Race);
            if (prefab == null) return;   // keeps the intact hull
            var w = Instantiate(prefab, transform, false);
            if (model != null)
            {
                // the wreck takes the hull's matrix, like the dying freighter's (AEGeometry::setMatrix)
                w.transform.localRotation = model.localRotation;
                w.transform.localScale = model.localScale;
            }
            PartAnimation.HoldAllAtEnd(w);
            if (modelGo != null) Destroy(modelGo);
            modelGo = w;
            model = w.transform;
            // Multiplayer: the other players' proxies load the wreck by this marker (NetProxy.BuildVisual).
            ModelPath = WreckModelPrefix + WreckIndex(Spec.ship, Race);
        }

        /// <summary>NpcShip.ModelPath of a ship shown as a freighter wreck held at its end (setDeadButSelectable): the prefix
        /// and the CombatAssets.wrecks index; NetProxy shows the same wreck.</summary>
        public const string WreckModelPrefix = "wreck:";

        void UpdateDying(float dtMs)
        {
            float frames = dtMs / 33.3f;
            if (IsTurret) { dyingMs -= dtMs; if (dyingMs <= 0f) SetDead(); return; }
            if (IsFixed || IsFreighter) { }   // PlayerFixedObject: the dying clears the moving flag (0x17f6b4), the wreck stays put
            else
            {
                transform.Rotate(spinAxis, 0.05f * frames * Mathf.Rad2Deg, Space.World);
                transform.position += deathDir * speed * dtMs * M;
            }
            dyingMs -= dtMs;
            if (dyingMs > 0f) return;
            // PlayerFixedObject::update: setScaling(6), 8 for the Terran battleship (0x37e7) and the pirate outpost (0x37a3).
            explosion = Explosion.Spawn(transform.position, IsFixed ? Spec.explosionScale : IsFreighter ? (Spec.ship == 14 ? 8f : 6f) : 1f);
            wreckBurn?.SetEmitting(false);   // state 4: enableSystemEmit(false)
            Current = State.Dead;
            smoke?.SetEmitting(false);   // the end of the tumble: Explosion::start, smoke and fire off
            if (burn != null && burn.Emitting) { burn.SetBurning(false); burn.Burst(); }   // record 11 at the explosion
            deadMs = 0f;
            if (obstacle != null && !IsFixed) obstacle.volumes = CollisionVolume.ForWreck(Spec.ship, Race);   // setWreckedMeshId
            // A Pirate Outpost's wreck (0x37a3 -> wreck volume 5): in state 4 only the wreck volumes collide.
            else if (obstacle != null && Spec.fixedObject == "station_pirates") obstacle.volumes = CollisionVolume.ForWreckId(5);
            // Remake (CapitalShips): a capital ship without a wreck leaves nothing to fly into.
            else if (obstacle != null && Spec.capitalEnhanced && wreck == null) obstacle.volumes = new List<CollisionVolume>();
            if (!IsFreighter) DropCrate();
        }

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

        /// <summary>KIPlayer::captureCrate on a dying or dead ship clears +0x48: the explosion leaves no crate.</summary>
        public void LootTaken() => crateDropped = true;

        Crate stealCrate;   // the crate TractorBeam::update made at the ship, while it exists

        /// <summary>KIPlayer::createCrate(0) for a living ship (TractorBeam::update's steal): a container of its cargo at the
        /// ship; the capture takes from the ship's own list (StealFrom).</summary>
        public Crate CreateStealCrate()
        {
            if (!HasCargo) return null;
            var prefab = assets != null ? assets.Crate(Race) : null;
            var go = prefab != null ? Instantiate(prefab, transform.position, Random.rotation) : new GameObject("Crate");
            go.name = "Stolen cargo";
            var c = go.AddComponent<Crate>();
            c.Setup(loot, Race);
            c.stolenFrom = this;
            c.pulled = true;
            stealCrate = c;
            return c;
        }

        /// <summary>KIPlayer::captureCrate on a living ship: 'amount' of 'item' leaves its hold.</summary>
        public void StealFrom(int item, int amount)
        {
            var s = loot.Find(x => x.item == item && x.amount > 0);
            if (s != null) s.amount = Mathf.Max(0, s.amount - amount);
        }

        /// <summary>Remake (CapitalShips): the cargo it drops when it dies (a capital ship's crate).</summary>
        public void SetLoot(List<ItemStack> items) => loot = items ?? new List<ItemStack>();

        bool crateDropped;

        void DropCrate()
        {
            // A fixed object dropped its crate as it died (OnDied) and again at the end of its dying (UpdateDying): a pirate
            // outpost's loot came twice. One crate per death.
            if (crateDropped || loot.Count == 0 || assets == null) return;
            crateDropped = true;
            // The beam was already pulling the ship's own crate (KIPlayer+0x74): that one is its crate now.
            if (stealCrate != null)
            {
                crate = stealCrate;
                crate.stolenFrom = null;
                crate.fromFriend = Target.friendToPlayer;
                crate.missionLoot = MissionShip;
                Target.crate = crate;
                return;
            }
            var prefab = assets.Crate(Race);
            var go = prefab != null ? Instantiate(prefab, transform.position, Random.rotation) : new GameObject("Crate");
            go.name = "Crate";
            crate = go.AddComponent<Crate>();
            crate.Setup(loot, Race);
            crate.fromFriend = Target.friendToPlayer;
            crate.missionCrate = Spec.missionCrate >= 0;
            crate.missionLoot = MissionShip;   // multiplayer: only the mission's team takes it
            Target.crate = crate;
        }

        void UpdateDead(float dtMs)
        {
            deadMs += dtMs;
            if ((IsFreighter || IsFixed) && wreck != null) return;   // state 4: the wreck stays for the rest of the level
            if (deadMs > 300f)
            {
                if (modelGo != null && modelGo.activeSelf) modelGo.SetActive(false);
                if (wreck != null) Destroy(wreck);
            }
            bool exploded = explosion == null || explosion.Finished;
            if (exploded && crate == null && deadMs > 300f) SetDead();
        }
    }
}
