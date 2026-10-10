// NetPlayer.cs
// One per player in a multiplayer session (NetGame spawns it as that player's player object when they connect; it lives
// in DontDestroyOnLoad, through all their scene changes). The owner writes where they are (the station, and in space /
// in the hangar / taking off from it, from the scene they are in) and, in space, their ship's pose and hull fraction
// (owner-written, sent at the tick rate), their ship, name and whether they run their orbit's NPCs (NetOrbit).
// Every other player in the same orbit sees the ship model there (player variant), smoothed (NetSmoothing), as:
//   a Target in Target.NetShips: lockable, the player's name (NetGame.PlayerName, else "Player N") on the lock plate; a
//     yellow (neutral) marker whose hits (guns, missiles, blasts) go to the owner's game, which applies them to its own ship
//     (players can destroy each other: a notice for everyone, the destroyed player respawns docked); a squadmate is a
//     green marker that the local player's weapons don't affect (Target.playerProof; NetSquad);
//   an Obstacle: a sphere around the model that the local ship slides along (PlayerCollision, camera shake, no damage),
//     sized to cover both ships (the local ship is tested as a point). Each player's own device pushes their own ship,
//     so two players bump off each other;
//   a shot mirror: the owner's shots (every gun and the turret, missiles homing on its lock, blasts) drawn here
//     (NetShotSender -> NetShotMirror); the hits stay the owner's (on NPC proxies through NetProxy).
// Hits, EMP, shots, blasts and jump effects go through the server, which checks and limits them (HitUpRpc, ShotUpRpc...:
// NetGuard, NetRateLimit) before passing them on; only the server may send the passed-on ones (InvokePermission.Server).
// The ship looks and sounds like the owner's: its engine glow and exhaust (ShipExhaust) while their engine shows, bigger
// with their boost, the engine loop (3D), their cloak (NpcCloak's look, off the radar from 25 %, NPCs don't fire at it;
// a phase cloak: shots, blasts and the local ship pass through, Target.phased),
// and an EMP's lightning. Their jumps too: the Khador Drive's charge sound and khador_jump fx, a jumpgate's jump animation and
// sound (SystemJump's events, JumpFxRpc), the ship gone when theirs vanishes (and an explosion when it was destroyed). EMP on a player (the remake's pick: players have no EMP pool) drains that much shield and
// shows the lightning for 1.5 s. The owner also shares their standings (Standing.*With), so the NPCs of an orbit another
// player runs treat them by their own standing.
// Elsewhere (another orbit, docked) all of it is hidden; the players docked at the same station see them in their hangar
// instead (NetHangar).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class NetPlayer : NetworkBehaviour, IPilot
    {
        public enum Place : byte { None = 0, Space = 1, Hangar = 2, Departing = 3 }

        /// <summary>The collision sphere per unit of the model's bounding radius (about both ships' size).</summary>
        const float CollisionScale = 1.6f;

        static readonly NetworkVariableReadPermission Read = NetworkVariableReadPermission.Everyone;
        static readonly NetworkVariableWritePermission Write = NetworkVariableWritePermission.Owner;

        readonly NetworkVariable<Vector3> position = new NetworkVariable<Vector3>(default, Read, Write);
        readonly NetworkVariable<Quaternion> rotation = new NetworkVariable<Quaternion>(Quaternion.identity, Read, Write);
        readonly NetworkVariable<int> ship = new NetworkVariable<int>(-1, Read, Write);
        readonly NetworkVariable<float> hull = new NetworkVariable<float>(1f, Read, Write);
        readonly NetworkVariable<float> shield = new NetworkVariable<float>(-1f, Read, Write);   // fractions, -1 = no such pool
        readonly NetworkVariable<float> armor = new NetworkVariable<float>(-1f, Read, Write);
        readonly NetworkVariable<FixedString128Bytes> pilot = new NetworkVariable<FixedString128Bytes>(default, Read, Write);
        readonly NetworkVariable<int> station = new NetworkVariable<int>(-1, Read, Write);
        readonly NetworkVariable<byte> place = new NetworkVariable<byte>((byte)Place.None, Read, Write);
        readonly NetworkVariable<bool> authority = new NetworkVariable<bool>(false, Read, Write);
        readonly NetworkVariable<int> squad = new NetworkVariable<int>(0);   // the host's (NetSquad), 0 = none
        readonly NetworkVariable<bool> observer = new NetworkVariable<bool>(false);   // the server's: another device controls the profile
        readonly NetworkVariable<FixedString32Bytes> factionTag = new NetworkVariable<FixedString32Bytes>();   // the server's: NetFactions, "" = none
        readonly NetworkVariable<int> factionHome = new NetworkVariable<int>(-1);   // the server's: the faction's home station, -1 = none
        readonly NetworkVariable<int> tollStation = new NetworkVariable<int>(-1, Read, Write);   // NetFactionsClient.TollStation
        readonly NetworkVariable<bool> distress = new NetworkVariable<bool>(false, Read, Write);  // NetDistress: calls for help
        readonly NetworkVariable<bool> admin = new NetworkVariable<bool>(false);   // the server's: admin commands (NetCommands)
        readonly NetworkVariable<byte> staffRole = new NetworkVariable<byte>(0);   // the server's: the profile's role (NetModeration)
        readonly NetworkVariable<bool> engine = new NetworkVariable<bool>(true, Read, Write);   // the engine glow shows
        readonly NetworkVariable<float> boost = new NetworkVariable<float>(0f, Read, Write);    // 0..1 (FlightModel.BoostVisualPercent)
        readonly NetworkVariable<float> cloak = new NetworkVariable<float>(0f, Read, Write);    // 0..100
        readonly NetworkVariable<bool> phase = new NetworkVariable<bool>(false, Read, Write);   // a phase cloak is up (PlayerCloak.Phasing)
        readonly NetworkVariable<bool> empShock = new NetworkVariable<bool>(false, Read, Write);
        readonly NetworkVariable<bool> visible = new NetworkVariable<bool>(true, Read, Write);   // the ship's model shows (gone in a jump, exploded)
        readonly NetworkVariable<int> standing0 = new NetworkVariable<int>(0, Read, Write);    // Session.Standing, the signature
        readonly NetworkVariable<int> standing1 = new NetworkVariable<int>(0, Read, Write);
        readonly NetworkVariable<int> signature = new NetworkVariable<int>(-1, Read, Write);
        readonly NetworkVariable<long> missionRun = new NetworkVariable<long>(0, Read, Write);   // NetMissions: the mission its level runs
        readonly NetworkVariable<long> missionHeld = new NetworkVariable<long>(0, Read, Write);  // the mission this player holds (netId)
        readonly NetworkVariable<int> missionCargo = new NetworkVariable<int>(0, Read, Write);   // containers 116 | 117 << 10 | passengers << 20
        // The mission orbit's runner: its route ("x,y,z;..."), clock (ms) and Challenge score (player << 16 | others), for the
        // squadmates there (FreelanceOrbit's follower mode).
        readonly NetworkVariable<FixedString512Bytes> missionRoute = new NetworkVariable<FixedString512Bytes>(default, Read, Write);
        readonly NetworkVariable<float> missionClock = new NetworkVariable<float>(0f, Read, Write);
        readonly NetworkVariable<int> missionScore = new NetworkVariable<int>(0, Read, Write);
        readonly NetworkVariable<bool> siegeRun = new NetworkVariable<bool>(false, Read, Write);   // KaamoSiege: builds the siege here
        readonly NetworkVariable<bool> atObject = new NetworkVariable<bool>(false, Read, Write);   // docked at an object (ObjectDocking)
        readonly NetworkVariable<int> turretItem = new NetworkVariable<int>(-1, Read, Write);   // the mounted turrets (PackTurrets)
        readonly NetworkVariable<bool> hangarRun = new NetworkVariable<bool>(false, Read, Write);   // runs its hangar's NPC ships (NetHangar)
        readonly NetworkVariable<bool> arrivedFlying = new NetworkVariable<bool>(false, Read, Write);   // docked by flying in (StationLevel)
        readonly NetworkVariable<int> miningAsteroid = new NetworkVariable<int>(-1, Read, Write);   // drilling this asteroid (NetOrbit index)
        readonly NetworkVariable<int> landedAsteroid = new NetworkVariable<int>(-1, Read, Write);   // landing on / docked at / drilling it

        /// <summary>Every player in the session, the local one included.</summary>
        public static readonly List<NetPlayer> All = new List<NetPlayer>();
        /// <summary>The local player's, null outside a session.</summary>
        public static NetPlayer Local { get; private set; }

        public int Station => station.Value;
        /// <summary>The ship's pose in its orbit (Unity space; the owner's ship), for NetTeleport.</summary>
        public Vector3 Position => position.Value;
        public Quaternion Rotation => rotation.Value;
        public Place Where => (Place)place.Value;
        public bool InSpace => Where == Place.Space;
        /// <summary>Docked (taking off included).</summary>
        public bool InHangar => Where == Place.Hangar || Where == Place.Departing;
        public bool OrbitAuthority => authority.Value;
        public int ShipIndex => ship.Value;
        /// <summary>Server: the ship this player flew before ShipIndex (a dealer trade-in puts it on the dealer's list,
        /// NetState.StockShipRpc: the trade may arrive before or after the new ship), -1 = none.</summary>
        public int PreviousShip { get; private set; } = -1;
        public int SquadId => squad.Value;
        /// <summary>This player's standing axes and signature (their Session): Standing.IsEnemyWith / IsFriendWith.</summary>
        public int Standing0 => standing0.Value;
        public int Standing1 => standing1.Value;
        public int Signature => signature.Value;
        public float CloakPercent => cloak.Value;
        /// <summary>The squad mission (FreelanceMission.netId) this player's level runs here, 0 = none.</summary>
        public long MissionRun => missionRun.Value;
        /// <summary>The squad mission (netId) this player holds, 0 = none (the reward split, NetMissions).</summary>
        public long MissionHeld => missionHeld.Value;
        /// <summary>What this player carries for the mission (NetMissions.PackCargo), handed over if they disconnect.</summary>
        public int MissionCargo => missionCargo.Value;
        public string MissionRoute => missionRoute.Value.ToString();
        public float MissionClock => missionClock.Value;
        public int MissionScore => missionScore.Value;
        /// <summary>This player's level runs the Kaamo siege in its orbit (KaamoSiege's one-per-orbit rule).</summary>
        public bool SiegeRun => siegeRun.Value;
        /// <summary>Docked at an object in space (ObjectDocking.PlayerDocked): NPC hits x0.75, like the local player's.</summary>
        public bool DockedAtObject => atObject.Value;
        /// <summary>The turret item on the ship (-1 = none): shown on its model in space and in the hangar (NetHangar).</summary>
        public int TurretItem => turretItem.Value < 0 ? -1 : turretItem.Value & 0xffff;
        /// <summary>Every turret item on the ship, in mount order (remake: a custom ship can carry two).</summary>
        public int[] TurretItems => UnpackTurrets(turretItem.Value);

        /// <summary>The turret items in one int: -1 none, else the first in the low 16 bits and the second + 1 in the high 16
        /// (0 = none), so a single turret is its item index, as before.</summary>
        public static int PackTurrets(System.Collections.Generic.IList<int> items)
        {
            if (items == null || items.Count == 0 || items[0] < 0) return -1;
            int second = items.Count > 1 && items[1] >= 0 ? items[1] + 1 : 0;
            return (items[0] & 0xffff) | ((second & 0x7fff) << 16);
        }

        public static int[] UnpackTurrets(int packed)
        {
            if (packed < 0) return new int[0];
            int first = packed & 0xffff, second = ((packed >> 16) & 0x7fff) - 1;
            return second >= 0 ? new[] { first, second } : new[] { first };
        }

        /// <summary>The packed turret items as equipment stacks (PlayerTurret.BuildStatic).</summary>
        public static ItemStack[] TurretStacks(int packed)
        {
            var items = UnpackTurrets(packed);
            var stacks = new ItemStack[items.Length];
            for (int i = 0; i < items.Length; i++) stacks[i] = new ItemStack(items[i], 1);
            return stacks;
        }
        /// <summary>Docked and running the hangar's NPC ships for everyone docked there (NetHangar).</summary>
        public bool HangarRun => hangarRun.Value;
        /// <summary>Docked by flying in from the orbit (not a session start, a respawn, a load): the others see it land.</summary>
        public bool ArrivedFlying => arrivedFlying.Value;
        /// <summary>The asteroid this player is drilling (its index in the orbit's seeded field), -1 = none: the ore is split
        /// between everyone drilling the same one (Mining).</summary>
        public int MiningAsteroid => miningAsteroid.Value;
        /// <summary>The asteroid this player's ship is landing on, sits on or drills (-1 = none): its spin stops for everyone
        /// (NetOrbit), like it does for the miner (Mining).</summary>
        public int LandedAsteroid => landedAsteroid.Value;
        public float Hull => hull.Value;
        /// <summary>Shield / armor fractions, -1 = the ship has none.</summary>
        public float Shield => shield.Value;
        public float Armor => armor.Value;
        /// <summary>Made an admin for the session by the host or the server console (the host's own player is one anyway,
        /// NetCommands.IsAdmin).</summary>
        public bool IsAdmin => admin.Value;
        /// <summary>Server: admin rights on / off.</summary>
        public void SetAdmin(bool on) { if (IsServer && admin.Value != on) admin.Value = on; }
        /// <summary>The profile's server role on a dedicated server with profiles (NetModeration: 0 player, 1 op, 2 admin,
        /// 3 master); admins and masters get the admin commands too (NetCommands.IsAdmin).</summary>
        public int StaffRole => staffRole.Value;
        /// <summary>Server: NetModeration's role for this player.</summary>
        public void SetStaffRole(int role) { if (IsServer && staffRole.Value != role) staffRole.Value = (byte)Mathf.Clamp(role, 0, 3); }
        /// <summary>Host: into squad 'id' (0 = none).</summary>
        public void SetSquad(int id) { if (IsServer && squad.Value != id) squad.Value = id; }
        /// <summary>Another device of this player's profile controls it: this one stays docked (NetProfiles).</summary>
        public bool Observer => observer.Value;
        /// <summary>Server: NetProfiles' role for this device.</summary>
        public void SetObserver(bool on) { if (IsServer && observer.Value != on) observer.Value = on; }
        /// <summary>The player's faction tag (NetFactions), "" = no faction.</summary>
        public string FactionTag => factionTag.Value.ToString();
        /// <summary>Server: NetFactions' tag for this player.</summary>
        public void SetFactionTag(string tag) { if (IsServer && factionTag.Value.ToString() != (tag ?? "")) factionTag.Value = tag ?? ""; }
        /// <summary>This pilot calls their squad for help (NetDistress).</summary>
        public bool Distress => distress.Value;
        /// <summary>Where this pilot paid the toll for the current visit (-1 = none): a held station's defence spares them.</summary>
        public int TollStation => tollStation.Value;
        /// <summary>The faction's home station (NetFactions; a destroyed member respawns there), -1 = none.</summary>
        public int FactionHome => factionHome.Value;
        /// <summary>Server: NetFactions' home for this player.</summary>
        public void SetFactionHome(int station) { if (IsServer && factionHome.Value != station) factionHome.Value = station; }
        /// <summary>The name with the faction's tag before it ("[TAG] Name"): the lock plate and the chat.</summary>
        public string TaggedName => FactionTag.Length > 0 ? $"[{FactionTag}] {DisplayName}" : DisplayName;
        public string DisplayName
        {
            get
            {
                string n = NetGame.Clean(pilot.Value.ToString());
                return n.Length > 0 ? n : string.Format(Localization.Extra("mpPlayerName", "Player {0}"),
                    OwnerClientId + (NetState.Instance != null && NetState.Instance.Dedicated ? 0ul : 1ul));   // a server's players start at 1
            }
        }
        /// <summary>A remote player in the local player's orbit: their ship is shown here.</summary>
        public bool SharesOrbit => !IsOwner && Local != null && Local.InSpace && InSpace && Station == Local.Station;

        readonly NetSmoothing smoothing = new NetSmoothing();
        SpaceLevel level;
        StationLevel dock;
        Transform localShip;
        GameObject model;
        Target target;
        Obstacle obstacle;
        NetShotSender sender;
        NetShotMirror mirror;
        bool shown, joinNoticePending;
        ShipExhaust exhaust;
        World.NpcCloak cloakLook;
        AudioSource engineLoop;
        float engineVolume, empShockMs;
        EmpSparks sparks, ownSparks;
        AssembledObject asm;
        float spawnedAt;
        SystemJump hookedJump;
        GameObject jumpedGate;   // a gate this player went through here: back to idle after its animation
        float gateResetMs;

        /// <summary>This player's ship as a Target here: the local player's own where it is theirs, else the proxy Target.</summary>
        public Target LocalTarget => IsOwner ? (level != null && level.Health != null ? level.Health.Target : null) : target;

        public override void OnNetworkSpawn()
        {
            DontDestroyOnLoad(gameObject);
            All.Add(this);
            name = $"NetPlayer {OwnerClientId}";
            if (IsServer) ship.OnValueChanged += (old, _) => PreviousShip = old;
            if (IsServer) SetStaffRole(NetModeration.RoleOfClient(OwnerClientId));   // signed in before the ship spawned
            if (IsServer) NetNews.SendAll(OwnerClientId);   // the sector's recent news for their ticker
            if (IsOwner)
            {
                Local = this;
                spawnedAt = Time.unscaledTime;
                ship.Value = Session.ShipIndex;
                pilot.Value = NetGame.Clean(NetGame.PlayerName);
                sender = new NetShotSender(ShotUpRpc, BlastUpRpc, () => level != null && level.Weapons != null ? level.Weapons.LockTarget : null);
                SceneManager.sceneLoaded += OnSceneLoaded;
                admin.OnValueChanged += (_, on) => NetChat.Notice(on
                    ? Localization.Extra("mpYouAdmin", "You are now an admin: /kick is available (/help).")
                    : Localization.Extra("mpYouNotAdmin", "You are no longer an admin."));
                FindLevel();
                return;
            }
            position.OnValueChanged += (_, p) => smoothing.Push(p);
            ship.OnValueChanged += (_, s) => BuildModel(s);
            turretItem.OnValueChanged += (_, _) => BuildTurret();
            smoothing.Push(position.Value);

            target = gameObject.AddComponent<Target>();
            target.isShip = true;
            target.customDeath = true;
            target.maxHp = 100f;
            // This player's weapons hitting them count toward them being this player's enemy too (NetAggression, both ways); an NPC's shot
            // relayed by this orbit's authority isn't.
            target.RemoteDamage = (amount, hitVector, byNpc) => { if (!byNpc) NetAggression.Hit(OwnerClientId, amount); HitUpRpc(amount, hitVector, byNpc); };
            target.RemoteEmp = emp => { NetAggression.Hit(OwnerClientId, emp); EmpUpRpc(emp); };
            ApplyName();
            pilot.OnValueChanged += (_, _) => ApplyName();
            factionTag.OnValueChanged += (_, _) => ApplyName();
            Target.NetShips.Add(target);
            obstacle = gameObject.AddComponent<Obstacle>();
            obstacle.projectFromVolume = false;
            mirror = new NetShotMirror(transform, () => target, ThroughSquad);
            BuildModel(ship.Value);
            // A mod's ship: this player can arrive before the session's mods are on and their models built (NetMods); the
            // model is built again then.
            Modding.ModShips.ModelsChanged += OnModShipModels;
            SetShown(false);
            // The join notice: once their name is here, and not for the players already in the session when this one joined.
            joinNoticePending = Local != null && Time.unscaledTime - Local.spawnedAt > 3f;
            spawnedAt = Time.unscaledTime;
        }

        public override void OnNetworkDespawn()
        {
            // Host: a player going (disconnected: Netcode despawns the player object before OnClientDisconnectCallback) hands
            // what they carried for the squad's mission to a squadmate (not when the whole session closes).
            if (IsServer && !IsOwner && NetState.Instance != null && !NetworkManager.ShutdownInProgress) NetState.Instance.HandOverMission(this);
            All.Remove(this);
            if (Local == this) Local = null;
            else if (!IsOwner && NetGame.Active && !NetGame.Dedicated) NetChat.Notice(NetChat.LeftText(DisplayName));
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Modding.ModShips.ModelsChanged -= OnModShipModels;
            if (target != null) Target.NetShips.Remove(target);
            sender?.Unhook();
            mirror?.Clear();
            cloakLook?.Dispose();
            sparks?.Clear();
            ownSparks?.Clear();
        }

        GameObject turretModel;
        static bool testDocked;

        /// <summary>The mounted turret on the model (CutScene::checkForTurret's static turret: the shots are mirrored).</summary>
        void BuildTurret()
        {
            if (turretModel != null) Destroy(turretModel);
            turretModel = null;
            if (model == null || turretItem.Value < 0) return;
            turretModel = PlayerTurret.BuildStatic(NetGame.Db, ship.Value, TurretStacks(turretItem.Value), model.transform);
        }

        /// <summary>The local player and 'other' (in the same orbit) may shoot each other: in an arena match (its own orbit
        /// id), during a siege between their two factions there (NetFactions), anywhere on a server started with -freepvp
        /// (NetState.FreePvp), or while an event's free for all runs (NetState.FreeForAll: /pvp on, the Free For All node; the
        /// Free For All / King of the Hill templates had stopped working). Squadmates never (NetSquad).</summary>
        static bool PvpWith(NetPlayer other) =>
            other != null && ((NetState.Instance != null && NetState.Instance.FreePvp) || NetState.FreeForAll || NetArena.IsArenaOrbit(other.Station)
                              || (Local != null && NetFactionsClient.SiegePvp(other.Station, Local.FactionTag, other.FactionTag)));   // a faction siege

        /// <summary>This player's shots pass through their squadmates (the local player's ship included).</summary>
        bool ThroughSquad(Target t)
        {
            if (SquadId == 0 || t == null) return false;
            var p = t.isPlayer ? Local : t.GetComponent<NetPlayer>();
            return p != null && p != this && NetSquad.Same(this, p);
        }

        void ApplyName()
        {
            target.displayName = TaggedName;
            name = $"NetPlayer {OwnerClientId} ({target.displayName})";
        }

        /// <summary>Another game's hit on this player's ship (a player's weapon, or an NPC that orbit's authority runs), through
        /// the server: only a game flying in this player's orbit, never a squadmate's weapon (squadmates are out of each
        /// other's line of fire), a finite damage up to NetGuard.MaxDamage, at its rate. (A direct RPC to the owner let a
        /// modified client destroy any player anywhere, or send NaN damage.)</summary>
        [Rpc(SendTo.Server)]
        void HitUpRpc(float amount, Vector3 hitVector, bool byNpc, RpcParams rpc = default)
        {
            ulong shooter = rpc.Receive.SenderClientId;
            if (shooter == OwnerClientId || !NetRateLimit.Allow(shooter, NetRateLimit.Kind.Hit)) return;
            if (!NetGuard.Damage(amount) || !NetGuard.Finite(hitVector)) { NetRateLimit.Reject(shooter, $"a hit of {amount}"); return; }
            var by = NetSquad.Find(shooter);
            if (!NetGuard.SameOrbit(by, this)) return;   // gone from the orbit a moment ago, or a hit from elsewhere
            if (!byNpc && NetSquad.Same(by, this)) return;
            NetState.Instance?.NoteHit(NetworkObjectId, shooter);
            HitRpc(amount, hitVector, byNpc, shooter);
        }

        /// <summary>The checked hit (HitUpRpc): this player's own ship takes it; destroyed by a player = a notice for everyone.</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void HitRpc(float amount, Vector3 hitVector, bool byNpc, ulong shooter)
        {
            var own = level != null && level.Health != null ? level.Health.Target : null;
            if (own == null) return;
            // A player's hit counts only where players may fight (an arena match, or a -freepvp server) and from the same
            // orbit: a modified game can't hurt anyone in free roam.
            if (!byNpc)
            {
                var from = NetSquad.Find(shooter);
                if (from == null || from.Station != Station || !PvpWith(from)) return;
            }
            bool alive = own.Alive;
            if (alive && !byNpc) NetAggression.Hit(shooter, amount);   // an attack makes them an enemy here (NetAggression)
            own.Damage(amount, byNpc, hitVector);
            if (alive && !own.Alive && !byNpc && NetState.Instance != null) NetState.Instance.DestroyedByRpc(shooter);
        }

        /// <summary>The owner's shot, through the server (only the owner sends this player's shots): checked (a weapon item,
        /// finite values) and limited, then drawn by the others' mirrors. (Unchecked, any client could put shots on any
        /// player's ship for everyone, or flood them.)</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void ShotUpRpc(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, ulong targetId)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Shot(item, position, velocity, up, lifetimeMs, homingDelayMs)) { NetRateLimit.Reject(OwnerClientId, $"a shot of item {item}"); return; }
            ShotRpc(item, position, velocity, up, lifetimeMs, homingDelayMs, targetId);
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        void ShotRpc(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, ulong targetId)
        {
            if (shown) mirror?.Shot(item, position, velocity, up, lifetimeMs, homingDelayMs, NetShots.Resolve(targetId));
        }

        /// <summary>The owner's blast (a bomb's ignition), through the server like a shot.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BlastUpRpc(int item, Vector3 point)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Item(item) || !NetGuard.Position(point)) { NetRateLimit.Reject(OwnerClientId, $"a blast of item {item}"); return; }
            BlastRpc(item, point);
        }

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Server)]
        void BlastRpc(int item, Vector3 point)
        {
            if (shown) mirror?.Blast(item, point);
        }

        void OnModShipModels()
        {
            if (this != null && !IsOwner && IsSpawned) BuildModel(ship.Value);
        }

        void BuildModel(int index)
        {
            if (model != null) Destroy(model);
            if (index < 0 || NetGame.Dedicated) return;   // a dedicated server shows nobody
            var prefab = AssembledObject.LoadPrefab(Database.Load().ShipAssembly(index));
            if (prefab == null) return;
            model = Instantiate(prefab, transform, false);
            model.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
            // The hit cube and the collision sphere from the model's size (its renderers' bounds, at the origin pose).
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) if (r is MeshRenderer || r is SkinnedMeshRenderer) bounds.Encapsulate(r.bounds);   // not the trails (empty at the origin)
            float size = Mathf.Max(5f, bounds.extents.magnitude);
            if (target != null) target.radius = size * 0.7f;
            if (obstacle != null)
            {
                obstacle.volumes.Clear();
                obstacle.volumes.Add(CollisionVolume.Sphere(Vector3.zero, size * CollisionScale));
            }
            model.SetActive(shown);
            HullCollision.Attach(model, target, obstacle);   // shots hit and the local ship slides along its real shape
            BuildTurret();
            // Its look and sound: the exhaust, the cloak, the engine loop (3D, at space distances).
            asm = model.GetComponent<AssembledObject>();
            if (exhaust != null) Destroy(exhaust);
            exhaust = ShipExhaust.AttachRemote(gameObject, Database.Load(), model.transform, index, () => shown && engine.Value && cloak.Value < 25f,
                                               () => boost.Value, () => cloak.Value);
            cloakLook?.Dispose();
            cloakLook = new World.NpcCloak(model.transform, keepEngine: true);
            if (engineLoop != null) Destroy(engineLoop);
            engineLoop = World.HangarFlight.AddEngine(model, true, Database.Load(), index, out engineVolume);
            if (engineLoop != null) EngineVoices.Setup3D(engineLoop);   // the engine events' rolloff (0.05 .. 500 m) in space
        }

        /// <summary>Remote: the ship, its marker, lock and collision only in the local player's orbit.</summary>
        void SetShown(bool on)
        {
            shown = on;
            if (model != null) model.SetActive(on);
            if (target != null) { target.enabled = on; target.untargetable = !on; }
            if (obstacle != null) obstacle.enabled = on;
            if (!on) { mirror?.Clear(); sparks?.SetEmitting(false); if (engineLoop != null) engineLoop.Stop(); }
            else smoothing.Snap();
        }

        /// <summary>Remote, shown: the glow, the engine loop, the cloak and the EMP lightning from the owner's state.</summary>
        void ApplyLook(float dtMs)
        {
            bool cloaked = cloak.Value > 0f, hidden = cloak.Value >= 25f;
            asm?.SetExhaust(engine.Value && !hidden, true);
            cloakLook?.Show(cloak.Value, dtMs);
            if (target != null)
            {
                target.cloaked = cloaked;          // NPCs keep chasing but hold their fire (Target.cloaked)
                // Off the radar, its markers and the lock (a lock held is dropped, CombatRadar) as soon as the cloak engages,
                // like the original's Player+0x5e; the auto turrets and sentries ignore it too.
                target.untargetable = cloaked;
                target.boosting = boost.Value > 0f;   // remake: a boost (or the cloak) shakes off the missiles homing on them (Gun)
                target.phased = phase.Value;          // remake: a phase cloak: shots and blasts pass through
            }
            if (obstacle != null) obstacle.enabled = !phase.Value;   // and the local ship flies through it
            if (engineLoop != null)
            {
                bool run = engine.Value && !hidden;
                engineLoop.volume = engineVolume;
                engineLoop.pitch = 1f + 0.12f * boost.Value;
                if (run && !engineLoop.isPlaying) engineLoop.Play();
                else if (!run && engineLoop.isPlaying) engineLoop.Stop();
            }
            if (empShock.Value && sparks == null) sparks = new EmpSparks(transform);
            sparks?.SetEmitting(empShock.Value);
        }

        /// <summary>Another game's EMP on this player's ship, through the server like a hit (the same orbit, no squadmate, a
        /// sane amount, its rate).</summary>
        [Rpc(SendTo.Server)]
        void EmpUpRpc(int emp, RpcParams rpc = default)
        {
            ulong shooter = rpc.Receive.SenderClientId;
            if (shooter == OwnerClientId || !NetRateLimit.Allow(shooter, NetRateLimit.Kind.Hit)) return;
            if (emp <= 0 || emp > NetGuard.MaxDamage) { NetRateLimit.Reject(shooter, $"an EMP of {emp}"); return; }
            var by = NetSquad.Find(shooter);
            if (!NetGuard.SameOrbit(by, this) || NetSquad.Same(by, this)) return;
            EmpRpc(emp, shooter);
        }

        /// <summary>The checked EMP (EmpUpRpc): players have no EMP pool, so it drains that much shield and shows the
        /// lightning for 1.5 s (a remake pick).</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void EmpRpc(int emp, ulong shooter)
        {
            var from = NetSquad.Find(shooter);
            if (from == null || from.Station != Station || !PvpWith(from)) return;   // only where players may fight
            var hp = level != null && level.Health != null && level.Health.Target != null ? level.Health.Target.hitpoints : null;
            if (hp == null || !hp.Alive || level.Health.Target.phased) return;   // a phase cloak: it passes through
            NetAggression.Hit(shooter, emp);
            hp.shield = Mathf.Max(0f, hp.shield - emp);
            empShockMs = 1500f;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => FindLevel();

        void OnJumpFx(bool viaGate, Vector3 at, Quaternion facing) => JumpFxUpRpc(viaGate, at, facing);
        void OnCharge() => ChargeUpRpc();

        /// <summary>The owner's jump effect, through the server (only the owner announces it; finite, a few a minute: a flood
        /// of khador_jump effects would bog the others' games down).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void JumpFxUpRpc(bool viaGate, Vector3 at, Quaternion facing)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Fx)) return;
            if (!NetGuard.Position(at) || !NetGuard.Rotation(facing)) { NetRateLimit.Reject(OwnerClientId, "a jump effect"); return; }
            JumpFxRpc(viaGate, at, facing);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ChargeUpRpc()
        {
            if (NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Fx)) ChargeRpc();
        }

        /// <summary>This player's jump effect, where they are shown: the jumpgate's jump animation and sound 31, or the
        /// khador_jump fx and sound 32 (SystemJump: their ship vanishes 1000 / 1700 ms in, `visible`).</summary>
        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Server)]
        void JumpFxRpc(bool viaGate, Vector3 at, Quaternion facing)
        {
            if (!SharesOrbit) return;
            var assets = StarMapAssets.Load();
            if (viaGate)
            {
                var gate = Local != null && Local.level != null ? Local.level.Jumpgate : null;
                float len = SystemJump.PlayGateJump(gate);
                if (len > 0f) { jumpedGate = gate; gateResetMs = len; }
                if (assets != null && assets.jumpgate != null && assets.jumpgate.Length > 0)
                    Sfx.PlayAt(assets.jumpgate[Random.Range(0, assets.jumpgate.Length)], gate != null ? gate.transform.position : at);
                return;
            }
            var fx = SystemJump.SpawnKhadorFx(assets, at, facing, out float lengthMs);
            if (fx != null) Destroy(fx, lengthMs / 1000f + 0.5f);
            if (assets != null) Sfx.PlayAt(assets.khadorDrive, at);
        }

        /// <summary>This player's Khador Drive charging (sound 33), where they are shown.</summary>
        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Server)]
        void ChargeRpc()
        {
            var assets = StarMapAssets.Load();
            if (SharesOrbit && assets != null) Sfx.PlayAt(assets.jumpgateCharge, transform.position);
        }

        void WritePools(float s, float a)
        {
            if (Mathf.Abs(shield.Value - s) > 0.004f) shield.Value = s;
            if (Mathf.Abs(armor.Value - a) > 0.004f) armor.Value = a;
        }

        void FindLevel()
        {
            level = FindAnyObjectByType<SpaceLevel>();
            dock = level == null ? FindAnyObjectByType<StationLevel>() : null;
            localShip = null;
            ownSparks = null;   // went with the last level's ship
            sender?.Unhook();   // the last level's guns
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (!IsOwner)
            {
                if (joinNoticePending && (pilot.Value.Length > 0 || Time.unscaledTime - spawnedAt > 2f))
                {
                    joinNoticePending = false;
                    NetChat.Notice(NetChat.JoinedText(DisplayName));
                }
                if (jumpedGate != null && (gateResetMs -= Time.deltaTime * 1000f) <= 0f) { SystemJump.ResetGate(jumpedGate); jumpedGate = null; }
                bool show = SharesOrbit && visible.Value;
                // Their ship vanished after its death tumble: the explosion (PlayerEgo::explode, PlayerHealth at 3000 ms).
                if (shown && !show && SharesOrbit && hull.Value <= 0f) Explosion.Spawn(transform.position);
                if (show != shown) SetShown(show);
                if (InSpace && hull.Value <= 0f) NetAggression.Forget(OwnerClientId);   // destroyed: no longer this player's enemy
                if (!shown) return;
                smoothing.Apply(transform, rotation.Value);
                ApplyLook(Time.deltaTime * 1000f);
                if (target != null)
                {
                    target.hp = hull.Value * target.maxHp;   // 0 = destroyed: no marker, no lock, no NPC after it
                    bool mate = NetSquad.Same(this, Local);   // squadmates: green, out of each other's line of fire
                    target.friendToPlayer = mate;
                    target.playerProof = mate || !PvpWith(this);   // free roam: no player hurts another (NetArena)
                    // An enemy after attacking this player, until one of them is destroyed (NetAggression): a red marker, the
                    // turrets and sentries fire at them (only where players may fight).
                    target.hostileToPlayer = !mate && PvpWith(this) && NetAggression.IsHostile(OwnerClientId);
                }
                mirror?.Update(Time.deltaTime * 1000f);
                return;
            }
            NetProfileClient.Tick();   // the server profile's periodic upload
            if (tollStation.Value != NetFactionsClient.TollStation) tollStation.Value = NetFactionsClient.TollStation;
            NetDistress.Tick(this);
            if (distress.Value != NetDistress.Active) distress.Value = NetDistress.Active;
            if (ship.Value != Session.ShipIndex) ship.Value = Session.ShipIndex;   // bought another
            if (standing0.Value != Session.Standing[0]) standing0.Value = Session.Standing[0];
            if (standing1.Value != Session.Standing[1]) standing1.Value = Session.Standing[1];
            int sig = Standing.SignatureRace;
            if (signature.Value != sig) signature.Value = sig;
            if (empShockMs > 0f) empShockMs -= Time.deltaTime * 1000f;
            bool shock = empShockMs > 0f;
            if (empShock.Value != shock) empShock.Value = shock;
            bool vis = level == null || level.Player == null || level.Player.visualModel == null || level.Player.visualModel.gameObject.activeSelf;
            if (visible.Value != vis) visible.Value = vis;
            var jump = level != null ? level.SystemJump : null;
            if (jump != hookedJump)
            {
                if (hookedJump != null) { hookedJump.JumpFxStarted -= OnJumpFx; hookedJump.ChargeStarted -= OnCharge; }
                hookedJump = jump;
                if (jump != null) { jump.JumpFxStarted += OnJumpFx; jump.ChargeStarted += OnCharge; }
            }
            // Where the local player is: the scene they are in.
            Place now = level != null ? Place.Space : dock != null ? (dock.PlayerDeparting ? Place.Departing : Place.Hangar) : Place.None;
            int at = level != null && level.Layout != null ? level.NetOrbitId : dock != null && dock.Layout != null ? dock.Layout.stationIndex : -1;
            if (place.Value != (byte)now) place.Value = (byte)now;
            if (station.Value != at) station.Value = at;
            bool runs = level != null && level.NetAuthority;
            if (authority.Value != runs) authority.Value = runs;
            var run = level != null && level.FreelanceOrbit != null && level.FreelanceOrbit.Running && Freelance.Active ? level.FreelanceOrbit : null;
            long mission = run != null ? Freelance.Mission.netId : 0;
            if (missionRun.Value != mission) missionRun.Value = mission;
            if (run != null)
            {
                string route = run.RouteText;
                if (missionRoute.Value.ToString() != route) missionRoute.Value = route;
                if (Mathf.Abs(missionClock.Value - run.ClockMs) > 500f) missionClock.Value = run.ClockMs;
                int score = run.PlayerKills << 16 | run.OtherKills;
                if (missionScore.Value != score) missionScore.Value = score;
            }
            long held = Freelance.Active ? Freelance.Mission.netId : 0;
            if (missionHeld.Value != held) missionHeld.Value = held;
            int turretNow = PackTurrets(PlayerTurret.TurretItems(NetGame.Db, Session.Equipment));   // also changed in the hangar
            if (turretItem.Value != turretNow) turretItem.Value = turretNow;
            bool runsHangar = dock != null && NetHangar.Running;
            if (hangarRun.Value != runsHangar) hangarRun.Value = runsHangar;
            bool flew = dock != null && dock.ArrivedFlying;
            if (arrivedFlying.Value != flew) arrivedFlying.Value = flew;
            var mining = level != null ? level.Mining : null;
            int drilling = mining != null && mining.State == Mining.Phase.Mining && NetOrbit.Current != null ? NetOrbit.Current.IndexOf(mining.Target) : -1;
            if (miningAsteroid.Value != drilling) miningAsteroid.Value = drilling;
            bool onRock = mining != null && (mining.State == Mining.Phase.Landing || mining.State == Mining.Phase.Docked || mining.State == Mining.Phase.Mining);
            int landed = onRock && NetOrbit.Current != null ? NetOrbit.Current.IndexOf(mining.Target) : -1;
            if (landedAsteroid.Value != landed) landedAsteroid.Value = landed;
            bool siege = level != null && level.Siege != null && level.Siege.Running && level.Siege.Active;
            if (siegeRun.Value != siege) siegeRun.Value = siege;
            int carried = held != 0 ? NetMissions.PackCargo() : 0;
            if (missionCargo.Value != carried) missionCargo.Value = carried;
            // Testing (development builds): -mpdock / -mpaccept (NetGame).
            if (NetGame.TestDock && !testDocked && level != null && level.LaunchCameraOver && Time.timeSinceLevelLoad > 8f) { testDocked = true; level.Dock(true); }
            if (NetGame.TestAccept && now == Place.Hangar && NetSquad.Invites.Count > 0) NetSquad.Accept(NetSquad.Invites[NetSquad.Invites.Count - 1]);
            if (level == null)
            {
                // Docked: repaired (the pools' presence stays as last seen in space).
                if (dock != null) { if (hull.Value != 1f) hull.Value = 1f; WritePools(shield.Value < 0f ? -1f : 1f, armor.Value < 0f ? -1f : 1f); }
                return;
            }
            if (localShip == null)
            {
                if (level.Player == null) return;
                localShip = level.Player.transform;
            }
            if (level.Weapons != null) sender?.Hook(level.Weapons.Guns);
            foreach (var t in level.Turrets) if (t != null && t.Gun != null) sender?.Hook(new[] { t.Gun });
            foreach (var sentry in SentryGun.All) if (sentry != null && sentry.Gun != null) sender?.Hook(new[] { sentry.Gun });
            if (atObject.Value != ObjectDocking.PlayerDocked) atObject.Value = ObjectDocking.PlayerDocked;
            transform.SetPositionAndRotation(localShip.position, localShip.rotation);
            if ((position.Value - localShip.position).sqrMagnitude > 0.0001f) position.Value = localShip.position;
            if (Quaternion.Angle(rotation.Value, localShip.rotation) > 0.05f) rotation.Value = localShip.rotation;
            var hp = level.Health != null && level.Health.Target != null ? level.Health.Target.hitpoints : null;
            float h = level.Health != null && level.Health.Target != null ? level.Health.Target.HullFraction : 1f;
            if (Mathf.Abs(hull.Value - h) > 0.004f) hull.Value = h;
            // The look the others see: the engine glow (off while mining, docking at an object, cloaked, dead), boost, cloak.
            var ownAsm = level.Player.visualModel != null ? level.Player.visualModel.GetComponent<AssembledObject>() : null;
            var glowPart = ownAsm != null && ownAsm.playerVariantParts != null && ownAsm.playerVariantParts.Length > 0 ? ownAsm.playerVariantParts[0] : null;
            bool glowOn = (glowPart == null || glowPart.activeInHierarchy) && (level.Health == null || !level.Health.Dead);
            if (level.Health != null && level.Health.Dead) NetAggression.Clear();   // a respawn starts clean
            if (engine.Value != glowOn) engine.Value = glowOn;
            float b = level.Player.Model != null ? level.Player.Model.BoostVisualPercent : 0f;
            // Back to 0 exactly when the boost ends (the others read > 0 as boosting: Target.boosting shakes off missiles).
            if (Mathf.Abs(boost.Value - b) > 0.02f || (b == 0f && boost.Value != 0f)) boost.Value = b;
            float c = level.Cloak != null && level.Cloak.Rules != null ? level.Cloak.Rules.Percentage : 0f;
            if (Mathf.Abs(cloak.Value - c) > 0.5f || (c == 0f && cloak.Value != 0f)) cloak.Value = c;
            bool ph = level.Cloak != null && level.Cloak.Phasing;
            if (phase.Value != ph) phase.Value = ph;
            // The EMP lightning on the own ship too.
            if (shock && ownSparks == null) ownSparks = new EmpSparks(localShip);
            ownSparks?.SetEmitting(shock);
            WritePools(hp != null && hp.maxShield > 0 ? hp.shield / hp.maxShield : -1f, hp != null && hp.maxArmor > 0 ? (float)hp.armor / hp.maxArmor : -1f);
        }
    }
}
