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
// shows the lightning for 1.5 s. The owner also shares their standings (Standing.*With; mods'races too,
// NetModRaceStandings), so the NPCs of an orbit another player runs treat them by their own standing.
// Elsewhere (another orbit, docked) all of it is hidden; the players docked at the same station see them in their hangar
// instead (NetHangar).

using System.Collections.Generic;
using System.Linq;
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
    public sealed partial class NetPlayer : NetworkBehaviour, IPilot, World.IOrbitPeer, INetPosed
    {
        public enum Place : byte { None = 0, Space = 1, Hangar = 2, Departing = 3 }

        /// <summary>The collision sphere per unit of the model's bounding radius (about both ships' size).</summary>
        const float CollisionScale = 1.6f;

        static readonly NetworkVariableReadPermission Read = NetworkVariableReadPermission.Everyone;
        static readonly NetworkVariableWritePermission Write = NetworkVariableWritePermission.Owner;

        readonly NetworkVariable<int> ship = new NetworkVariable<int>(-1, Read, Write);
        readonly NetworkVariable<FixedString128Bytes> pilot = new NetworkVariable<FixedString128Bytes>(default, Read, Write);
        readonly NetworkVariable<int> station = new NetworkVariable<int>(-1, Read, Write);
        readonly NetworkVariable<byte> place = new NetworkVariable<byte>((byte)Place.None, Read, Write);
        readonly NetworkVariable<bool> authority = new NetworkVariable<bool>(false, Read, Write);   // its game built / runs its orbit's traffic
        readonly NetworkVariable<int> runsOrbit = new NetworkVariable<int>(-1);   // the server's: the orbit it runs (NetState.UpdateOrbitRunners)
        readonly NetworkVariable<bool> runsOrbitActive = new NetworkVariable<bool>(false);   // separate: -1 is also a valid Void orbit id
        readonly NetworkVariable<int> squad = new NetworkVariable<int>(0);   // the host's (NetSquad), 0 = none
        readonly NetworkVariable<bool> observer = new NetworkVariable<bool>(false);   // the server's: another device controls the profile
        readonly NetworkVariable<FixedString32Bytes> factionTag = new NetworkVariable<FixedString32Bytes>();   // the server's: NetFactions, "" = none
        readonly NetworkVariable<int> wreckControlProgress = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        internal int WreckControlProgress => wreckControlProgress.Value;
        internal int WreckControlEarnedSeconds => (wreckControlProgress.Value >> 2) - 1;
        internal bool WreckControlContested => (wreckControlProgress.Value & 1) != 0;
        internal bool WreckControlAdvancing => (wreckControlProgress.Value & 2) != 0;

        internal void SetWreckControlProgress(int earnedSeconds, bool contested, bool advancing)
        {
            int value = ((earnedSeconds + 1) << 2) | (contested ? 1 : 0) | (advancing ? 2 : 0);
            if (IsServer && wreckControlProgress.Value != value) wreckControlProgress.Value = value;
        }

        readonly NetworkVariable<FixedString4096Bytes> stationProjectsFundable = new NetworkVariable<FixedString4096Bytes>(default,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        internal void SetStationProjectFundingAccess(string ids)
        {
            if (!IsServer) return;
            var encoded = new FixedString4096Bytes(ids);
            if (!stationProjectsFundable.Value.Equals(encoded)) stationProjectsFundable.Value = encoded;
        }

        internal bool CanFundStationProjectInUi(int id) =>
            System.Array.IndexOf(stationProjectsFundable.Value.ToString().Split(','), id.ToString()) >= 0;

        internal bool CanUseStationStockInUi => AvailableStationActions.HasFlag(StationProjectActions.UseStationStock);
        internal bool CanRelinquishBuilderRightsInUi => AvailableStationActions.HasFlag(StationProjectActions.RelinquishBuilder);

        readonly NetworkVariable<byte> stationProjectActions = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        internal StationProjectActions AvailableStationActions => (StationProjectActions)stationProjectActions.Value;
        internal void SetStationProjectActions(StationProjectActions value)
        {
            if (IsServer && stationProjectActions.Value != (byte)value) stationProjectActions.Value = (byte)value;
        }

        readonly NetworkVariable<FixedString64Bytes> profileAccount = new NetworkVariable<FixedString64Bytes>(default,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<FixedString64Bytes> factionId = new NetworkVariable<FixedString64Bytes>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        int gateRequestSequence;
        System.Action<int> gateTravelReply;
        readonly NetworkVariable<int> factionHome = new NetworkVariable<int>(-1);   // the server's: the faction's home station, -1 = none
        readonly NetworkVariable<int> tollStation = new NetworkVariable<int>(-1, Read, Write);   // NetFactionsClient.TollStation
        readonly NetworkVariable<bool> distress = new NetworkVariable<bool>(false, Read, Write);  // NetDistress: calls for help
        readonly NetworkVariable<bool> admin = new NetworkVariable<bool>(false);   // the server's: admin commands (NetCommands)
        readonly NetworkVariable<byte> staffRole = new NetworkVariable<byte>(0);   // the server's: the profile's role (NetModeration)
        readonly NetworkVariable<bool> visible = new NetworkVariable<bool>(true, Read, Write);   // the ship's model shows (gone in a jump, exploded)
        // Owner-written platform/input flags for mod-selected SMEP AI. Unknown clients default to mobile touch.
        readonly NetworkVariable<bool> mobilePlatform = new NetworkVariable<bool>(true, Read, Write);
        readonly NetworkVariable<bool> mobileTouchControls = new NetworkVariable<bool>(true, Read, Write);
        readonly NetworkVariable<int> standing0 = new NetworkVariable<int>(0, Read, Write);    // Session.Standing, the signature
        readonly NetworkVariable<int> standing1 = new NetworkVariable<int>(0, Read, Write);
        readonly NetworkVariable<int> signature = new NetworkVariable<int>(-1, Read, Write);
        // Remake mods: the standings toward mods'races (NetModRaceStandings), and another player's read back
        readonly NetworkVariable<FixedString4096Bytes> modRaceStanding = new NetworkVariable<FixedString4096Bytes>(default, Read, Write);
        readonly Dictionary<string, int> modRaceStandings = new Dictionary<string, int>(System.StringComparer.Ordinal);
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
        readonly NetworkVariable<FixedString128Bytes> weapons = new NetworkVariable<FixedString128Bytes>(default, Read, Write);   // primaries | secondaries (PackWeapons)
        readonly NetworkVariable<FixedString512Bytes> build = new NetworkVariable<FixedString512Bytes>(default, Read, Write);   // a kit ship's build (BuildText)
        readonly NetworkVariable<bool> hangarRun = new NetworkVariable<bool>(false, Read, Write);   // runs its hangar's NPC ships (NetHangar)
        readonly NetworkVariable<bool> arrivedFlying = new NetworkVariable<bool>(false, Read, Write);   // docked by flying in (StationLevel)
        readonly NetworkVariable<int> miningAsteroid = new NetworkVariable<int>(-1, Read, Write);   // drilling this asteroid (NetOrbit index)
        readonly NetworkVariable<int> landedAsteroid = new NetworkVariable<int>(-1, Read, Write);   // landing on / docked at / drilling it

        /// <summary>Every player in the session, the local one included.</summary>
        public static readonly List<NetPlayer> All = new List<NetPlayer>();
        /// <summary>The local player's, null outside a session.</summary>
        public static NetPlayer Local { get; private set; }

        public int Station => station.Value;
        /// <summary>The ship's pose in its orbit (Unity space; the owner's ship; elsewhere the pose stream's last, NetPoses),
        /// for NetTeleport, the events, the commands.</summary>
        public Vector3 Position => posePosition;
        public Quaternion Rotation => poseRotation;
        public Place Where => (Place)place.Value;
        public bool InSpace => Where == Place.Space;
        /// <summary>Docked (taking off included).</summary>
        public bool InHangar => Where == Place.Hangar || Where == Place.Departing;
        /// <summary>This player's game says it runs its orbit's traffic (it built it, or was handed it): the server's pick
        /// among the claims is RunsOrbit.</summary>
        public bool OrbitAuthority => authority.Value;
        /// <summary>The orbit the server made this player run (NetState.UpdateOrbitRunners).</summary>
        public int RunsOrbit => runsOrbit.Value;
        /// <summary>Whether the server selected this player as an orbit runner (separate from the orbit id: Void is -1).</summary>
        public bool RunsOrbitActive => runsOrbitActive.Value;
        public int ShipIndex => ship.Value;
        /// <summary>Server: the ship this player flew before ShipIndex (a dealer trade-in puts it on the dealer's list,
        /// NetState.StockShipRpc: the trade may arrive before or after the new ship), -1 = none.</summary>
        public int PreviousShip { get; private set; } = -1;
        public int SquadId => squad.Value;
        /// <summary>This player's standing axes and signature (their Session): Standing.IsEnemyWith / IsFriendWith.</summary>
        public int Standing0 => standing0.Value;
        public int Standing1 => standing1.Value;
        public int Signature => signature.Value;
        /// <summary>This player's standings toward mods'races (mod race key -> value): Standing.IsEnemyWith.</summary>
        public IReadOnlyDictionary<string, int> ModRaceStandings => IsOwner ? Session.ModRaceStanding : modRaceStandings;
        public float CloakPercent => look.cloak;
        internal bool ScanVisible => visible.Value && Hull > 0f && CloakPercent < 25f && HasCurrentWreckClaimPose;
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
            if (items != null && items.Contains(-1)) items = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(items, i => i >= 0));   // empty slots (PlayerTurret.TurretItems)
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

        /// <summary>The mounted primary and secondary weapons, "p,p|s,s" (PackWeapons): a kit ship shows them as models
        /// (Modding.ModHardpoints), on its model in space and in the hangar (NetHangar).</summary>
        public string Weapons => weapons.Value.ToString();

        /// <summary>Server-side validation that a reported weapon belongs to this player's synchronised loadout.</summary>
        public bool HasWeaponItem(int item)
        {
            if (item < 0) return false;
            foreach (int turret in UnpackTurrets(turretItem.Value)) if (turret == item) return true;
            // The flyable capital hulls have built-in turrets and the Intelli Jet / S'koonn salvo outside the normal loadout.
            if (ship.Value == 14 && (item == World.CapitalShips.TerranMissile || item == World.CapitalShips.VosskMissile || item == 48 || item == 49)) return true;
            foreach (var stack in WeaponStacks(NetGame.Db, weapons.Value.ToString())) if (stack.item == item) return true;
            return false;
        }

        /// <summary>The build of this player's customizable ship (Modding.ModShipKits: parts, tier, colours, the save's text),
        /// "" = not a kit ship or its default build; the others show their ship so (its parts load on demand).</summary>
        public string BuildText => build.Value.ToString();

        /// <summary>This player's ship build as the others' games show it (normalized; the default for "" or unknown parts),
        /// null = no kit ship.</summary>
        public Modding.ModShipKits.Build ShownBuild => Modding.ModShipKits.RemoteBuild(ship.Value, BuildText);

        /// <summary>The local player's build text to share: their kit ship's build, "" for another ship (or a text past the
        /// variable's 512 bytes: the default build shows then).</summary>
        static string LocalBuildText()
        {
            if (Modding.ModShipKits.CustomizationOf(Session.ShipIndex) == null) return "";
            string text = Modding.ModShipKits.PlayerBuild(Session.ShipIndex)?.ToString() ?? "";
            return System.Text.Encoding.UTF8.GetByteCount(text) <= 500 ? text : "";
        }

        /// <summary>The primary and secondary weapon items of 'equipment' on their mounts, -1 for an empty slot between two
        /// (at most 12 of each, within the variable's 128 bytes); "" = none.</summary>
        public static string PackWeapons(Database db, System.Collections.Generic.IList<ItemStack> equipment)
        {
            string List(bool secondary) => string.Join(",", Modding.ModHardpoints.WeaponItems(db, equipment, secondary).Take(12).Select(i => i?.index ?? -1));
            string text = List(false) + "|" + List(true);
            if (text == "|") return "";
            if (text.Length > 120) text = text.Substring(0, text.LastIndexOf(',', 120));   // never a cut number
            return text;
        }

        /// <summary>PackWeapons' text as equipment stacks, each in the slot of its place (an empty slot's -1, unknown or
        /// unreadable numbers left out; an older version's text has no -1: its weapons side by side).</summary>
        public static System.Collections.Generic.List<ItemStack> WeaponStacks(Database db, string packed)
        {
            var list = new System.Collections.Generic.List<ItemStack>();
            if (string.IsNullOrEmpty(packed) || db == null) return list;
            foreach (var half in packed.Split('|'))
            {
                var parts = half.Split(',');
                for (int place = 0; place < parts.Length; place++)
                    if (int.TryParse(parts[place], out int n) && n >= 0 && db.Item(n) is ItemData it && (it.type == "primary" || it.type == "secondary"))
                        list.Add(new ItemStack(n, 1) { slot = place + 1 });
            }
            return list;
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
        public float Hull => WireQuant.Fraction(look.hull);
        /// <summary>Shield / armor fractions, -1 = the ship has none.</summary>
        public float Shield => WireQuant.Fraction(look.shield);
        public float Armor => WireQuant.Fraction(look.armor);
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
        /// <summary>Server: this player runs 'orbit'; active distinguishes the Void's -1 id from no assigned orbit.</summary>
        public void SetRunsOrbit(int orbit, bool active)
        {
            if (!IsServer) return;
            if (runsOrbit.Value != orbit) runsOrbit.Value = orbit;
            if (runsOrbitActive.Value != active) runsOrbitActive.Value = active;
        }
        /// <summary>Another device of this player's profile controls it: this one stays docked (NetProfiles).</summary>
        public bool Observer => observer.Value;
        /// <summary>Server: NetProfiles' role for this device.</summary>
        public void SetObserver(bool on) { if (IsServer && observer.Value != on) observer.Value = on; }
        /// <summary>The player's faction tag (NetFactions), "" = no faction.</summary>
        public string FactionTag => factionTag.Value.ToString();
        /// <summary>Profile identity visible only to this player and the server, for owner-bound station infrastructure.</summary>
        public string ProfileAccount => profileAccount.Value.ToString();
        /// <summary>Server-replicated faction identity used to enforce faction-owned infrastructure access.</summary>
        public string FactionId => factionId.Value.ToString();
        /// <summary>Server: NetFactions' tag and stable faction ID for this player.</summary>
        public void SetFactionTag(string tag)
        {
            if (!IsServer) return;
            if (factionTag.Value.ToString() != (tag ?? "")) factionTag.Value = tag ?? "";
            string account = NetProfiles.AccountOf(OwnerClientId);
            string id = NetFactions.Of(account)?.id ?? "";
            if (factionId.Value.ToString() != id) factionId.Value = id;
        }
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
        bool shown, joinNoticePending, joinAnnounced;
        const float CollisionModeReportInterval = 2f, CollisionModeReportGrace = 8f, CollisionModeMismatchGrace = 2f;
        const byte UnknownCollisionMode = byte.MaxValue;
        byte reportedCollisionMode = UnknownCollisionMode;
        float nextCollisionModeReport, collisionModeReportDeadline, collisionModeMismatchSince;
        bool collisionModeWarningSent;
        /// <summary>The chat's join / leave notices (a relog loop flooded the chat): a join is told only once the player has
        /// stayed JoinNoticeSeconds, and not when they left less than QuietRejoinSeconds ago; a leave only when their join
        /// was told (or they were here before this game joined), and once per QuietRejoinSeconds per name.</summary>
        const float JoinNoticeSeconds = 15f, QuietRejoinSeconds = 120f;
        static readonly Dictionary<string, float> lastLeft = new Dictionary<string, float>(), lastLeaveNotice = new Dictionary<string, float>();
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

        /// <summary>Server: the selected authority for an orbit, or null when nobody is currently running it.</summary>
        internal static NetPlayer OrbitRunner(int orbitStationIndex) =>
            NetGame.IsServer ? All.Find(p => p != null && p.IsSpawned && p.OrbitAuthority &&
                p.RunsOrbitActive && p.RunsOrbit == orbitStationIndex) : null;

        /// <summary>The completed construction was saved: refresh the live orbit's gate.</summary>
        internal void BroadcastJumpgateRestored(int orbitStationIndex)
        {
            if (!IsServer || !IsSpawned || !RunsOrbitActive || RunsOrbit != orbitStationIndex) return;
            var layout = OrbitLayout.Build(NetGame.Db, orbitStationIndex);
            var gate = NetPlayerStations.FindJumpgate(orbitStationIndex);
            if (layout != null && layout.hasJumpgate && gate != null)
                JumpgateRestoredRpc(orbitStationIndex, layout.jumpgate, gate.modelPreset, gate.modelReference ?? "");
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void JumpgateRestoredRpc(int orbitStationIndex, Vector3 gatePosition, int modelPreset, string modelReference)
        {
            if (!RunsOrbitActive || RunsOrbit != orbitStationIndex ||
                Local == null || !Local.InSpace || Local.Station != orbitStationIndex) return;
            if (Local.level != null && Local.level.Layout != null &&
                Local.level.Layout.stationIndex == orbitStationIndex)
                Local.level.OnPlayerJumpgateRestored(gatePosition, modelPreset, modelReference);
        }

        /// <summary>Server: notify pilots of a persisted EMP shutdown immediately.</summary>
        internal void BroadcastJumpgateEmp(int orbitStationIndex, long untilUtc)
        {
            if (!IsServer || !IsSpawned || !RunsOrbitActive || RunsOrbit != orbitStationIndex) return;
            JumpgateEmpRpc(orbitStationIndex, untilUtc);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void JumpgateEmpRpc(int orbitStationIndex, long untilUtc)
        {
            if (!RunsOrbitActive || RunsOrbit != orbitStationIndex ||
                Local == null || !Local.InSpace || Local.Station != orbitStationIndex) return;
            if (Local.level != null && Local.level.Layout != null &&
                Local.level.Layout.stationIndex == orbitStationIndex)
                Local.level.OnPlayerJumpgateEmpShutdown(untilUtc);
        }

        /// <summary>Server: broadcast a saved gate-destruction event on the orbit runner's network object.</summary>
        internal void BroadcastJumpgateExplosion(int orbitStationIndex, bool permanentlyDestroyed)
        {
            if (!IsServer || !IsSpawned || !RunsOrbitActive || RunsOrbit != orbitStationIndex) return;
            JumpgateExplosionRpc(orbitStationIndex, permanentlyDestroyed);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void JumpgateExplosionRpc(int orbitStationIndex, bool permanentlyDestroyed)
        {
            // Every player object receives its own invocation; only the runner's object handles the event
            // and only clients physically present in the orbit spawn the effect.
            if (!RunsOrbitActive || RunsOrbit != orbitStationIndex ||
                Local == null || !Local.InSpace || Local.Station != orbitStationIndex) return;
            if (Local.level != null && Local.level.Layout != null &&
                Local.level.Layout.stationIndex == orbitStationIndex)
                Local.level.OnPlayerJumpgateDestroyed(permanentlyDestroyed);
        }

        int stationUiSequence;
        float stationUiSentAt;
        System.Action<string> stationUiReply;
        internal bool RequestStationUiCommand(string command, System.Action<string> reply)
        {
            if (!IsOwner || !IsSpawned || reply == null || string.IsNullOrEmpty(command) ||
                !command.StartsWith("/station ", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (stationUiReply != null && Time.realtimeSinceStartup - stationUiSentAt < 10f) return false;
            stationUiReply = reply;
            stationUiSentAt = Time.realtimeSinceStartup;
            stationUiSequence = stationUiSequence == int.MaxValue ? 1 : stationUiSequence + 1;
            StationUiCommandRpc(stationUiSequence, command.Substring(9));
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void StationUiCommandRpc(int sequence, string args, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId) return;
            string result;
            if (string.IsNullOrWhiteSpace(args) || args.Length > NetGuard.MaxCommandArgs)
                result = "Invalid station command.";
            else if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Command))
                result = "Too many station requests. Try again shortly.";
            else
                result = NetCommands.RunOnServer("station", args, this) ?? "Station command unavailable.";
            StationUiResultRpc(sequence, result ?? "");
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void StationUiResultRpc(int sequence, string result)
        {
            if (!IsOwner || sequence != stationUiSequence) return;
            var reply = stationUiReply;
            stationUiReply = null;
            if (!string.IsNullOrEmpty(result)) NetChat.Notice(result);
            reply?.Invoke(result);
        }

        int stationMarketSequence;
        System.Action<NetPlayerStations.MarketUiCatalogue> stationMarketReply;
        internal bool RequestStationMarket(int station, int page, System.Action<NetPlayerStations.MarketUiCatalogue> reply)
        {
            if (!IsOwner || !IsSpawned || reply == null) return false;
            stationMarketReply = reply;
            stationMarketSequence = stationMarketSequence == int.MaxValue ? 1 : stationMarketSequence + 1;
            StationMarketRequestRpc(stationMarketSequence, station, page);
            return true;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void StationMarketRequestRpc(int sequence, int station, int page, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId || !NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Request)) return;
            StationMarketReplyRpc(sequence, JsonUtility.ToJson(NetPlayerStations.MarketCatalogueFor(this, station, page)));
        }
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void StationMarketReplyRpc(int sequence, string json)
        {
            if (!IsOwner || sequence != stationMarketSequence) return;
            var reply = stationMarketReply;
            stationMarketReply = null;
            try { reply?.Invoke(JsonUtility.FromJson<NetPlayerStations.MarketUiCatalogue>(json)); }
            catch (System.Exception e) { Debug.LogWarning("Station marketplace snapshot: " + e.Message); }
        }

        int stationPlannerSequence;
        System.Action<int[]> stationPlannerReply;
        internal bool RequestStationPlannerLocations(System.Action<int[]> reply)
        {
            if (!IsOwner || !IsSpawned || reply == null) return false;
            stationPlannerReply = reply;
            stationPlannerSequence = stationPlannerSequence == int.MaxValue ? 1 : stationPlannerSequence + 1;
            StationPlannerRequestRpc(stationPlannerSequence);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void StationPlannerRequestRpc(int sequence, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId || !NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Request)) return;
            StationPlannerReplyRpc(sequence, NetPlayerStations.AvailableConstructionOrbits(this));
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void StationPlannerReplyRpc(int sequence, int[] orbits)
        {
            if (!IsOwner || sequence != stationPlannerSequence) return;
            var reply = stationPlannerReply;
            stationPlannerReply = null;
            reply?.Invoke(orbits ?? System.Array.Empty<int>());
        }

        /// <summary>Explicit player-gate travel request, separate from ordinary orbit changes.</summary>
        public bool RequestPlayerJumpgateTravel(int orbit, System.Action<int> done)
        {
            if (!IsOwner || !IsSpawned || done == null) return false;
            gateTravelReply = done;
            gateRequestSequence = gateRequestSequence == int.MaxValue ? 1 : gateRequestSequence + 1;
            PlayerGateTravelUpRpc(orbit, gateRequestSequence);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PlayerGateTravelUpRpc(int orbitStationIndex, int sequence, RpcParams rpc = default)
        {
            int arrival = -1;
            if (IsServer && rpc.Receive.SenderClientId == OwnerClientId &&
                NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Claim))
            {
                int system = NetPlayerStations.JumpgateDestinationForInteraction(this, orbitStationIndex);
                if (system >= 0)
                {
                    int target = NetPlayerStations.JumpgateDestinationStation(orbitStationIndex);
                    if (target >= 0)
                    {
                        bool newlyDiscovered = !NetProfiles.HasDiscoveredSystem(ProfileAccount, system);
                        if (NetProfiles.DiscoverSystem(ProfileAccount, system))
                        {
                            arrival = target;
                            JumpgateDiscoveryRpc(system, newlyDiscovered);
                        }
                    }
                }
            }
            PlayerGateTravelReplyRpc(sequence, arrival);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void PlayerGateTravelReplyRpc(int sequence, int arrival)
        {
            if (!IsOwner || sequence != gateRequestSequence) return;
            var reply = gateTravelReply;
            gateTravelReply = null;
            reply?.Invoke(arrival);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void JumpgateDiscoveryRpc(int systemIndex, bool newlyDiscovered)
        {
            if (!IsOwner || NetGame.Db?.Systems?.Find(x => x != null && x.index == systemIndex) == null) return;
            GalaxyMap.Reveal(NetGame.Db, systemIndex);
            if (newlyDiscovered)
            {
                string name = NetGame.Db.Systems.Find(x => x != null && x.index == systemIndex)?.name ?? ("System " + systemIndex);
                NetChat.Notice("New system discovered: " + name + ".");
            }
        }

        public override void OnNetworkSpawn()
        {
            DontDestroyOnLoad(gameObject);
            All.Add(this);
            name = $"NetPlayer {OwnerClientId}";
            if (IsServer) collisionModeReportDeadline = Time.unscaledTime + CollisionModeReportGrace;
            if (IsServer) ship.OnValueChanged += (old, _) => PreviousShip = old;
            if (IsServer)
            {
                station.OnValueChanged += OnStationValueChanged;
                place.OnValueChanged += OnPlaceValueChanged;
            }
            if (IsServer)
            {
                string account = NetProfiles.AccountOf(OwnerClientId);
                profileAccount.Value = account ?? "";
                var faction = NetFactions.Of(account);
                factionId.Value = faction?.id ?? "";
                factionTag.Value = faction?.tag ?? "";
                SetStaffRole(NetModeration.RoleOfClient(OwnerClientId));   // signed in before the ship spawned
            }
            if (IsServer) NetNews.SendAll(OwnerClientId);   // the sector's recent news for their ticker
            NetPoses.Register(this);
            if (IsOwner)
            {
                Local = this;
                spawnedAt = Time.unscaledTime;
                ship.Value = Session.ShipIndex;
                pilot.Value = NetGame.Clean(NetGame.PlayerName);
                NetModRaceStandings.Resend();
                sender = new NetShotSender(SendShot, BlastUpRpc, () => level != null && level.Weapons != null ? level.Weapons.LockTarget : null, () => NetworkObjectId);
                SceneManager.sceneLoaded += OnSceneLoaded;
                admin.OnValueChanged += (_, on) => NetChat.Notice(on
                    ? Localization.Extra("mpYouAdmin", "You are now an admin: /kick is available (/help).")
                    : Localization.Extra("mpYouNotAdmin", "You are no longer an admin."));
                FindLevel();
                return;
            }
            ship.OnValueChanged += (_, s) => { if (target != null) target.netShipIndex = s; BuildModel(s); };   // the class scanner reads it
            // Docked = repaired (the pose stream stops there, so its last hull would linger until the next launch's first pose).
            place.OnValueChanged += (_, now) => { if (now != (byte)Place.Space) { look.hull = WireQuant.Fraction(1f); hasPose = false; } };
            turretItem.OnValueChanged += (_, _) => BuildTurret();
            weapons.OnValueChanged += (_, _) => BuildTurret();
            build.OnValueChanged += (_, _) => BuildModel(ship.Value);

            target = gameObject.AddComponent<Target>();
            target.isShip = true;
            target.netShipIndex = ship.Value;
            target.netPilot = true;
            target.customDeath = true;
            target.maxHp = 100f;
            // This player's weapons hitting them count toward them being this player's enemy too (NetAggression, both ways); an NPC's shot
            // relayed by this orbit's authority isn't.
            target.RemoteDamage = (amount, hitVector, byNpc, shieldFactor) => { if (!byNpc) NetAggression.Hit(OwnerClientId, amount); HitUpRpc(amount, hitVector, byNpc, shieldFactor, -1, 0, 0); };
            target.RemoteHit = (amount, hitVector, byNpc, shieldFactor, weaponItem, sourceObjectId, shotId) =>
            { if (!byNpc) NetAggression.Hit(OwnerClientId, amount); HitUpRpc(amount, hitVector, byNpc, shieldFactor, weaponItem, sourceObjectId, shotId); };
            target.RemoteEmp = emp => { NetAggression.Hit(OwnerClientId, emp); EmpUpRpc(emp); };
            target.RemoteHeal = points => HealUpRpc(points);   // a squadmate's repair beam (NetPlayerEquipment)
            ApplyName();
            pilot.OnValueChanged += (_, _) => ApplyName();
            factionTag.OnValueChanged += (_, _) => ApplyName();
            NetModRaceStandings.Decode(modRaceStanding.Value, modRaceStandings);
            modRaceStanding.OnValueChanged += (_, v) => NetModRaceStandings.Decode(v, modRaceStandings);
            Target.NetShips.Add(target);
            obstacle = gameObject.AddComponent<Obstacle>();
            obstacle.projectFromVolume = false;
            mirror = new NetShotMirror(transform, () => target, ThroughSquad, () => WeaponTint.ShipColor(ship.Value, ShownBuild), () => ship.Value);
            BuildModel(ship.Value);
            // A mod's ship: this player can arrive before the session's mods are on and their models built (NetMods); the
            // model is built again then.
            Modding.ModShips.ModelsChanged += OnModShipModels;
            SetShown(false);
            // The join notice: once their name is here, and not for the players already in the session when this one joined.
            joinNoticePending = Local != null && Time.unscaledTime - Local.spawnedAt > 3f;
            joinAnnounced = !joinNoticePending;   // already here when this game joined: their leave is told
            spawnedAt = Time.unscaledTime;
        }

        /// <summary>Whether this pilot has confirmed that its weapon hit tests use the server's collision rule.</summary>
        internal bool CollisionModeCompliant => IsOwner && IsServer
            ? HullCollision.Mode == NetRules.HostCollision
            : reportedCollisionMode == (byte)NetRules.HostCollision;

        /// <summary>The owner's effective collision mode, not its saved preference. Repeated reports recover from a lost
        /// update and let the server block weapon damage from a client that remains on a different mode.</summary>
        void ReportCollisionModeIfNeeded()
        {
            if (!IsOwner || IsServer || !NetGame.Active || !NetRules.TryGetCollision(out _)) return;
            float now = Time.unscaledTime;
            byte actual = (byte)HullCollision.Mode;
            if (actual == lastCollisionModeReported && now < nextCollisionModeReport) return;
            lastCollisionModeReported = actual;
            nextCollisionModeReport = now + CollisionModeReportInterval;
            ReportCollisionModeRpc(actual);
        }

        byte lastCollisionModeReported = UnknownCollisionMode;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReportCollisionModeRpc(byte mode, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId) return;
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Request)) return;
            if (mode > SessionRules.CollisionOriginal)
            {
                NetRateLimit.Reject(OwnerClientId, "an invalid collision-mode report");
                return;
            }

            reportedCollisionMode = mode;
            CheckReportedCollisionMode();
        }

        internal void CheckReportedCollisionMode()
        {
            if (!IsServer || IsOwner) return;
            if (reportedCollisionMode == UnknownCollisionMode && Time.unscaledTime < collisionModeReportDeadline) return;
            bool matches = reportedCollisionMode == NetRules.HostCollision;
            if (matches)
            {
                collisionModeMismatchSince = 0f;
                if (collisionModeWarningSent)
                {
                    collisionModeWarningSent = false;
                    NetState.Instance?.NoticeTo(this, Localization.Extra("mpCollisionModeRestored", "Your ship collision mode now matches the server."));
                }
                return;
            }

            if (collisionModeMismatchSince <= 0f) collisionModeMismatchSince = Time.unscaledTime;
            if (Time.unscaledTime - collisionModeMismatchSince < CollisionModeMismatchGrace) return;
            if (collisionModeWarningSent) return;
            collisionModeWarningSent = true;
            string actual = reportedCollisionMode == UnknownCollisionMode
                ? Localization.Extra("mpCollisionModeUnknown", "not reported")
                : SessionRules.CollisionText(reportedCollisionMode);
            string expected = SessionRules.CollisionText(NetRules.HostCollision);
            string message = string.Format(Localization.Extra("mpCollisionModeMismatch", "Your ship collision mode ({0}) does not match this server's {1} mode. Weapon damage from your game is disabled until it matches."), actual, expected);
            NetState.Instance?.NoticeTo(this, message);
            Debug.LogWarning($"NetPlayer: {DisplayName}'s collision mode is {actual}; server requires {expected}. Weapon damage from this game is blocked.");
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                station.OnValueChanged -= OnStationValueChanged;
                place.OnValueChanged -= OnPlaceValueChanged;
            }
            // Host: a player going (disconnected: Netcode despawns the player object before OnClientDisconnectCallback) hands
            // what they carried for the squad's mission to a squadmate (not when the whole session closes).
            if (IsServer && !IsOwner && NetState.Instance != null && !NetworkManager.ShutdownInProgress) NetState.Instance.HandOverMission(this);
            All.Remove(this);
            NetPoses.Unregister(this);
            if (!IsOwner) NetPoses.Forget(OwnerClientId);
            if (Local == this) Local = null;
            else if (!IsOwner && NetGame.Active && !NetGame.Dedicated)
            {
                string who = DisplayName;
                float now = Time.unscaledTime;
                lastLeft[who] = now;
                if (joinAnnounced && (!lastLeaveNotice.TryGetValue(who, out float told) || now - told > QuietRejoinSeconds))
                {
                    lastLeaveNotice[who] = now;
                    NetChat.Notice(NetChat.LeftText(who));
                }
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Modding.ModShips.ModelsChanged -= OnModShipModels;
            if (target != null) Target.NetShips.Remove(target);
            sender?.Unhook();
            mirror?.Clear();
            cloakLook?.Dispose();
            sparks?.Clear();
            ownSparks?.Clear();
            ClearEquipment();
        }

        GameObject turretModel, weaponModels;
        static bool testDocked, testLaunched;

        /// <summary>The mounted turret on the model (CutScene::checkForTurret's static turret: the shots are mirrored), and a
        /// kit ship's weapon models (ModHardpoints; the shots come mirrored from the owner's muzzles).</summary>
        void BuildTurret()
        {
            if (turretModel != null) Destroy(turretModel);
            if (weaponModels != null) Destroy(weaponModels);
            turretModel = weaponModels = null;
            if (model == null) return;
            // On the mounts of the build their model shows (a kit ship: theirs; else the ship's own).
            var kitBuild = ShownBuild;
            if (turretItem.Value >= 0) turretModel = PlayerTurret.BuildStatic(NetGame.Db, ship.Value, TurretStacks(turretItem.Value), model.transform, kitBuild);
            weaponModels = Modding.ModHardpoints.AttachWeapons(NetGame.Db, ship.Value, WeaponStacks(NetGame.Db, Weapons), model.transform, kitBuild);
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
        void HitUpRpc(float amount, Vector3 hitVector, bool byNpc, float shieldFactor, int weaponItem, ulong sourceObjectId, uint shotId, RpcParams rpc = default)
        {
            ulong shooter = rpc.Receive.SenderClientId;
            if (shooter == OwnerClientId || !NetRateLimit.Allow(shooter, NetRateLimit.Kind.Hit)) return;
            if (shotId != 0 && (sourceObjectId == 0 || NetShotRegistry.IsCanceled(sourceObjectId, shotId))) return;
            if (!NetGuard.Damage(amount) || !NetGuard.Finite(hitVector)) { NetRateLimit.Reject(shooter, $"a hit of {amount}"); return; }
            // a weapon's shield factor (Flight.Gun.ShieldFactor: 0.01 .. 10, or 0 = through the shield); an NPC's shot has none
            shieldFactor = byNpc || float.IsNaN(shieldFactor) ? 1f : shieldFactor <= 0f ? 0f : Mathf.Clamp(shieldFactor, 0.01f, 10f);
            var by = NetSquad.Find(shooter);
            if (!NetGuard.SameOrbit(by, this)) return;   // gone from the orbit a moment ago, or a hit from elsewhere
            // "byNpc" is supplied by the caller, so it is not proof of an NPC hit. Only the server-selected
            // runner of this orbit may relay NPC damage. This closes the easy bypass where any client could
            // set byNpc=true to ignore PvP and squad protection. The runner still simulates NPC combat locally;
            // fully proving an individual shot requires authoritative NPC combat, not a client flag.
            if (byNpc && !NetGuard.NpcDamageFrom(by, Station))
            {
                NetRateLimit.Reject(shooter, "NPC damage from a client that does not run this orbit");
                return;
            }
            if (!byNpc && weaponItem >= 0)
            {
                var weapon = Database.Shared.Item(weaponItem);
                if (!by.HasWeaponItem(weaponItem) || weapon == null) { NetRateLimit.Reject(shooter, $"a hit from unmounted weapon {weaponItem}"); return; }
                shieldFactor = Gun.ShieldFactor(weapon); // typed damage never trusts a client-supplied shield multiplier
            }
            if (byNpc && weaponItem >= 0 && (by.RunsOrbit != Station || Database.Shared.Item(weaponItem) == null)) { NetRateLimit.Reject(shooter, $"an NPC hit from untrusted weapon {weaponItem}"); return; }
            if (!byNpc && NetSquad.Same(by, this)) return;
            if (!by.CollisionModeCompliant)
            {
                by.CheckReportedCollisionMode();
                return;
            }
            NetState.Instance?.NoteHit(NetworkObjectId, shooter);
            HitRpc(amount, hitVector, byNpc, shooter, shieldFactor, weaponItem);
        }

        /// <summary>The checked hit (HitUpRpc): this player's own ship takes it; destroyed by a player = a notice for everyone.</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void HitRpc(float amount, Vector3 hitVector, bool byNpc, ulong shooter, float shieldFactor, int weaponItem)
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
            var item = weaponItem >= 0 ? GoF2Remake.Data.Database.Shared.Item(weaponItem) : null;
            var damageType = item != null ? (DamageType)Mathf.Clamp(item.Attr(Gun.DamageTypeAttr), 0, 2) : DamageType.None;
            own.lastPlayerWeapon = byNpc ? -1 : weaponItem;
            own.Damage(amount, byNpc, hitVector, shieldFactor, damageType);
            if (alive && !own.Alive && !byNpc && NetState.Instance != null) NetState.Instance.DestroyedByRpc(shooter);
        }

        /// <summary>Client: an explosive or EMP blast reached a precise gate mesh.</summary>
        public void ReportJumpgateBlast(int orbitStationIndex, int weaponItem, uint shotId,
            Vector3 detonation, Vector3 gateSurface)
        {
            if (IsOwner && IsSpawned && InSpace && Station == orbitStationIndex &&
                shotId != 0 && NetGuard.Position(detonation) && NetGuard.Position(gateSurface))
                JumpgateBlastUpRpc(orbitStationIndex, weaponItem, shotId, detonation, gateSurface);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void JumpgateBlastUpRpc(int orbitStationIndex, int weaponItem, uint shotId,
            Vector3 detonation, Vector3 gateSurface, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId ||
                !InSpace || Station != orbitStationIndex || shotId == 0 ||
                !NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Hit) ||
                !NetGuard.Item(weaponItem) || !HasWeaponItem(weaponItem) ||
                !NetGuard.Position(detonation) || !NetGuard.Position(gateSurface) ||
                !NetPlayerStations.HasAttackableJumpgateAtStation(orbitStationIndex)) return;
            var weapon = NetGame.Db.Item(weaponItem);
            if (weapon == null) return;
            var kind = (Gun.Kind)weapon.categoryId;
            if (kind is not (Gun.Kind.EmpBomb or Gun.Kind.Nuke or Gun.Kind.Ionizing
                or Gun.Kind.Mine or Gun.Kind.ShockBlast)) return;
            float blastRadius = Mathf.Max(0, weapon.Attr(14)) * OrbitLayout.MetersPerUnit;
            if (blastRadius <= 0f) return;
            var layout = OrbitLayout.Build(NetGame.Db, orbitStationIndex);
            if (layout == null || !layout.hasJumpgate) return;
            Vector3 gateCentre = OrbitLayout.ToUnity(layout.Place(layout.jumpgate));
            float gateRadius = layout.JumpgateRadius * OrbitLayout.MetersPerUnit;
            if (!NetShotRegistry.TryConsumeGateBlast(NetworkObjectId, shotId, weaponItem,
                detonation, gateSurface, gateCentre, gateRadius, blastRadius)) return;
            float factor = Mathf.Clamp01((blastRadius - Vector3.Distance(detonation, gateSurface)) / blastRadius);
            float structural = kind == Gun.Kind.EmpBomb ? 0f : Mathf.Max(0, weapon.Attr(9)) * factor;
            float empDamage = Mathf.Max(0, weapon.Attr(10)) * factor;
            if (NetPlayerStations.ApplyJumpgateCombatHit(orbitStationIndex, weaponItem, structural, empDamage))
                NetPlayerStations.NoteOrbitalAggression(this, orbitStationIndex);
        }

        /// <summary>Only an exact local scenery mesh hit can request gate damage. The server
        /// independently checks the recorded shot, mounted weapon, orbit and approximate flight path.</summary>
        public void ReportJumpgateImpact(int orbitStationIndex, int weaponItem, uint shotId, Vector3 point)
        {
            if (IsOwner && IsSpawned && InSpace && Station == orbitStationIndex &&
                shotId != 0 && NetGuard.Position(point))
                JumpgateHitUpRpc(orbitStationIndex, weaponItem, shotId, point);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void JumpgateHitUpRpc(int orbitStationIndex, int weaponItem, uint shotId, Vector3 point,
            RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId ||
                !InSpace || Station != orbitStationIndex ||
                !NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Hit) ||
                !NetGuard.Item(weaponItem) || !HasWeaponItem(weaponItem) ||
                !NetGuard.Position(point) || shotId == 0 ||
                !NetPlayerStations.HasAttackableJumpgateAtStation(orbitStationIndex)) return;
            var layout = OrbitLayout.Build(NetGame.Db, orbitStationIndex);
            if (layout == null || !layout.hasJumpgate) return;
            Vector3 gateCentre = OrbitLayout.ToUnity(layout.Place(layout.jumpgate));
            float gateRadius = layout.JumpgateRadius * OrbitLayout.MetersPerUnit;
            if (!NetShotRegistry.TryConsumeGateHit(NetworkObjectId, shotId, weaponItem, point,
                gateCentre, gateRadius)) return;
            var weapon = NetGame.Db.Item(weaponItem);
            if (weapon == null) return;
            // Damage numbers are resolved from the server's item registry, never the client's report.
            float structural = Mathf.Max(0, weapon.Attr(9));
            float empDamage = Mathf.Max(0, weapon.Attr(10));
            if (NetPlayerStations.ApplyJumpgateCombatHit(orbitStationIndex, weaponItem, structural, empDamage))
                NetPlayerStations.NoteOrbitalAggression(this, orbitStationIndex);
        }

        void SendShot(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, ulong targetId, uint shotId) =>
            ShotUpRpc(new NetShotData(item, position, velocity, up, lifetimeMs, homingDelayMs, targetId, shotId));

        /// <summary>The owner's shot, through the server (only the owner sends this player's shots): checked (a weapon item,
        /// finite values) and limited, then drawn by the mirrors of the players in the same orbit (NetInterest). (Unchecked,
        /// any client could put shots on any player's ship for everyone, or flood them.) Sent once, when fired (NetShotData):
        /// every game flies its own copy.</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void ShotUpRpc(NetShotData shot)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Shot(shot.item, shot.position, shot.velocity, shot.up, shot.lifetimeMs, shot.homingDelayMs)) { NetRateLimit.Reject(OwnerClientId, $"a shot of item {shot.item}"); return; }
            NetShotRegistry.Register(NetworkObjectId, shot.shotId, shot.item, shot.targetId,
                Mathf.Max(0f, shot.lifetimeMs + 2000f), shot.position, shot.velocity);
            if (InSpace && NetInterest.OrbitGroup(this, Station, OwnerClientId, out var to)) ShotRpc(shot, to);
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        void ShotRpc(NetShotData shot, RpcParams rpc = default)
        {
            if (shown) mirror?.Shot(shot.item, shot.position, shot.velocity, shot.up, shot.lifetimeMs, shot.homingDelayMs, NetShots.Resolve(shot.targetId), shot.shotId, NetworkObjectId, true);
        }

        public static void InterceptMirrorShot(ulong sourceObjectId, uint shotId) => Local?.InterceptUpRpc(sourceObjectId, shotId);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void InterceptUpRpc(ulong sourceObjectId, uint shotId, RpcParams rpc = default)
        {
            ulong requester = rpc.Receive.SenderClientId;
            if (requester != OwnerClientId || !NetRateLimit.Allow(requester, NetRateLimit.Kind.Shot)) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(sourceObjectId, out var sourceObject)) return;
            var source = sourceObject.GetComponent<NetPlayer>();
            var defender = NetSquad.Find(requester);
            if (source == null || defender == null || source == this || source.OwnerClientId == requester || source.Station != Station || NetSquad.Same(defender, source)) return;
            if (!NetShotRegistry.TryCancel(sourceObjectId, shotId, NetworkObjectId)) return;
            source.CancelShotRpc(shotId);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void CancelShotRpc(uint shotId) => sender?.CancelShot(shotId);

        /// <summary>The owner's blast (a bomb's ignition), through the server like a shot.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BlastUpRpc(int item, Vector3 point)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Item(item) || !NetGuard.Position(point)) { NetRateLimit.Reject(OwnerClientId, $"a blast of item {item}"); return; }
            if (InSpace && NetInterest.OrbitGroup(this, Station, OwnerClientId, out var to)) BlastRpc(item, point, to);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void BlastRpc(int item, Vector3 point, RpcParams rpc = default)
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
            // A customizable ship in this player's build (its parts loading meanwhile show the default build; ModShips.ModelsChanged
            // builds it again), any other ship its own model.
            var kitBuild = Modding.ModShipKits.RemoteBuild(index, BuildText);
            var prefab = AssembledObject.LoadPrefab(kitBuild != null ? Modding.ModShipKits.VariantAssembly(index, kitBuild) : Database.Shared.ShipAssembly(index));
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
            // Shots hit and the local ship slides along its real shape; with Original ship collision its cube and its sphere.
            HullCollision.Attach(model, target, obstacle, HullCollision.FlyableShip(index));
            BuildTurret();
            // Its look and sound: the exhaust (their build's exhaust style, ModExhausts, in place of the particles), the cloak,
            // the engine loop (3D, at space distances).
            if (kitBuild != null) Modding.ModExhausts.Apply(Database.Load(), index, model, kitBuild,
                                                            Modding.ModShipKits.MountsShown(Database.Load(), index, 3, kitBuild));
            asm = model.GetComponent<AssembledObject>();
            if (exhaust != null) Destroy(exhaust);
            exhaust = ShipExhaust.AttachRemote(gameObject, Database.Shared, model.transform, index, () => shown && look.engine && look.cloak < 25,
                                               () => Boost, () => look.cloak,
                                               mounts: kitBuild != null ? Modding.ModShipKits.MountsShown(Database.Shared, index, 3, kitBuild) : null);
            cloakLook?.Dispose();
            cloakLook = new World.NpcCloak(model.transform, keepEngine: true);
            if (engineLoop != null) Destroy(engineLoop);
            engineLoop = World.HangarFlight.AddEngine(model, true, Database.Shared, index, out engineVolume);
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
            bool cloaked = look.cloak > 0, hidden = look.cloak >= 25;
            asm?.SetExhaust(look.engine && !hidden, true);
            cloakLook?.Show(look.cloak, dtMs);
            if (target != null)
            {
                target.cloaked = cloaked;          // NPCs keep chasing but hold their fire (Target.cloaked)
                // Off the radar, its markers and the lock (a lock held is dropped, CombatRadar) as soon as the cloak engages,
                // like the original's Player+0x5e; the auto turrets and sentries ignore it too.
                target.untargetable = cloaked;
                target.boosting = look.boost > 0;   // remake: a boost (or the cloak) shakes off the missiles homing on them (Gun)
                target.phased = look.phase;          // remake: a phase cloak: shots and blasts pass through
            }
            if (obstacle != null) obstacle.enabled = !look.phase;   // and the local ship flies through it
            if (engineLoop != null)
            {
                bool run = look.engine && !hidden;
                engineLoop.volume = engineVolume;
                engineLoop.pitch = 1f + 0.12f * Boost;
                if (run && !engineLoop.isPlaying) engineLoop.Play();
                else if (!run && engineLoop.isPlaying) engineLoop.Stop();
            }
            if (look.empShock && sparks == null) sparks = new EmpSparks(transform);
            sparks?.SetEmitting(look.empShock);
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
            if (!by.CollisionModeCompliant) { by.CheckReportedCollisionMode(); return; }
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
            PlayerCloak.Of(level.Health)?.Break();   // remake (Settings.EmpBreaksCloak, the session's): the cloak drops
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
            look.shield = WireQuant.Fraction(s);
            look.armor = WireQuant.Fraction(a);
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
            if (IsServer) CheckReportedCollisionMode();
            if (!IsOwner)
            {
                if (target != null)
                {
                    target.mobilePlatform = mobilePlatform.Value;
                    target.mobileTouchControls = mobileTouchControls.Value;
                }
                if (joinNoticePending && pilot.Value.Length > 0 && Time.unscaledTime - spawnedAt > JoinNoticeSeconds)
                {
                    joinNoticePending = false;
                    joinAnnounced = true;
                    // Back within QuietRejoinSeconds of leaving (a dropped connection, a relog): no notice.
                    if (!lastLeft.TryGetValue(DisplayName, out float left) || Time.unscaledTime - left > QuietRejoinSeconds)
                        NetChat.Notice(NetChat.JoinedText(DisplayName));
                }
                if (jumpedGate != null && (gateResetMs -= Time.deltaTime * 1000f) <= 0f) { SystemJump.ResetGate(jumpedGate); jumpedGate = null; }
                bool show = SharesOrbit && visible.Value && smoothing.Has;
                // Their ship vanished after its death tumble: the explosion (PlayerEgo::explode, PlayerHealth at 3000 ms).
                if (shown && !show && SharesOrbit && Hull <= 0f) Explosion.Spawn(transform.position, Database.Shared.Ship(ShipIndex)?.explosionScale ?? 1f);
                if (show != shown) SetShown(show);
                if (InSpace && Hull <= 0f) NetAggression.Forget(OwnerClientId);   // destroyed: no longer this player's enemy
                ApplyEquipment(shown);   // the emergency bubble, the sentry copies (NetPlayerEquipment)
                if (!shown) return;
                smoothing.Apply(transform, poseRotation);
                ApplyLook(Time.deltaTime * 1000f);
                if (target != null)
                {
                    target.hp = Hull * target.maxHp;   // 0 = destroyed: no marker, no lock, no NPC after it
                    target.netShield = Shield;          // their marker's shield bar and armor over the hull (CombatView)
                    target.netArmor = Armor;
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
            ReportCollisionModeIfNeeded();
            bool isMobile = Application.isMobilePlatform;
            bool usesTouch = isMobile && InputMode.Current == InputKind.Touch;
            if (mobilePlatform.Value != isMobile) mobilePlatform.Value = isMobile;
            if (mobileTouchControls.Value != usesTouch) mobileTouchControls.Value = usesTouch;
            if (target != null)
            {
                target.mobilePlatform = isMobile;
                target.mobileTouchControls = usesTouch;
            }
            NetProfileClient.Tick();   // the server profile's periodic upload
            WriteEquipment();   // max hull, emergency clock, sentries (NetPlayerEquipment)
            if (tollStation.Value != NetFactionsClient.TollStation) tollStation.Value = NetFactionsClient.TollStation;
            NetDistress.Tick(this);
            NetFollow.Tick(this);
            if (distress.Value != NetDistress.Active) distress.Value = NetDistress.Active;
            if (ship.Value != Session.ShipIndex) ship.Value = Session.ShipIndex;   // bought another
            if (standing0.Value != Session.Standing[0]) standing0.Value = Session.Standing[0];
            if (standing1.Value != Session.Standing[1]) standing1.Value = Session.Standing[1];
            int sig = Standing.SignatureRace;
            if (signature.Value != sig) signature.Value = sig;
            if (NetModRaceStandings.Changed(out var factionStandings) && factionStandings != modRaceStanding.Value)
                modRaceStanding.Value = factionStandings;
            if (empShockMs > 0f) empShockMs -= Time.deltaTime * 1000f;
            bool shock = empShockMs > 0f;
            look.empShock = shock;
            bool vis = level == null || level.Player == null || level.Player.visualModel == null || level.Player.visualModel.gameObject.activeSelf;
            if (visible.Value != vis) visible.Value = vis;
            var jump = level != null ? level.SystemJump : null;
            if (jump != hookedJump)
            {
                if (hookedJump != null) { hookedJump.JumpFxStarted -= OnJumpFx; hookedJump.ChargeStarted -= OnCharge; }
                hookedJump = jump;
                if (jump != null) { jump.JumpFxStarted += OnJumpFx; jump.ChargeStarted += OnCharge; }
            }
            // Where the local player is: the scene they are in. Modified (review of #168): docked EVERSPACE 2's way the flight
            // level stays loaded under the station (SpaceLevel.DockedInOrbit): docked at that station, not in space.
            bool docked = level != null && level.DockedInOrbit;
            Place now = docked ? Place.Hangar : level != null ? Place.Space : dock != null ? (dock.PlayerDeparting ? Place.Departing : Place.Hangar) : Place.None;
            int at = docked ? Session.StationIndex : level != null && level.Layout != null ? level.NetOrbitId : dock != null && dock.Layout != null ? dock.Layout.stationIndex : -1;
            if (place.Value != (byte)now) place.Value = (byte)now;
            if (now != Place.Space) PoseSend.sent = false;   // back in space: the first pose goes at once
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
            string weaponsNow = PackWeapons(NetGame.Db, Session.Equipment);   // also changed in the hangar
            if (weapons.Value.ToString() != weaponsNow) weapons.Value = weaponsNow;
            string buildNow = LocalBuildText();   // the hangar's Customize ship
            if (build.Value.ToString() != buildNow) build.Value = buildNow;
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
            if (NetGame.TestLaunch > 0f && !testLaunched && dock != null && now == Place.Hangar && Time.timeSinceLevelLoad > NetGame.TestLaunch) { testLaunched = true; dock.Launch(); }
            if (NetGame.TestAccept && now == Place.Hangar && NetSquad.Invites.Count > 0) NetSquad.Accept(NetSquad.Invites[NetSquad.Invites.Count - 1]);
            if (level == null)
            {
                // Docked: repaired (the pools' presence stays as last seen in space).
                if (dock != null) { look.hull = WireQuant.Fraction(1f); WritePools(Shield < 0f ? -1f : 1f, Armor < 0f ? -1f : 1f); }
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
            posePosition = localShip.position;
            poseRotation = localShip.rotation;
            hasPose = true;
            var hp = level.Health != null && level.Health.Target != null ? level.Health.Target.hitpoints : null;
            float h = level.Health != null && level.Health.Target != null ? level.Health.Target.HullFraction : 1f;
            look.hull = WireQuant.Fraction(h);
            // The look the others see: the engine glow (off while mining, docking at an object, cloaked, dead), boost, cloak.
            var ownAsm = level.Player.visualModel != null ? level.Player.visualModel.GetComponent<AssembledObject>() : null;
            var glowPart = ownAsm != null && ownAsm.playerVariantParts != null && ownAsm.playerVariantParts.Length > 0 ? ownAsm.playerVariantParts[0] : null;
            // (also off in free flight with no forward thrust: FlightModel.EnginesOff)
            bool glowOn = (glowPart == null || glowPart.activeInHierarchy) && (level.Health == null || !level.Health.Dead)
                          && (level.Player.Model == null || !level.Player.Model.EnginesOff);
            if (level.Health != null && level.Health.Dead) NetAggression.Clear();   // a respawn starts clean
            look.engine = glowOn;
            float b = level.Player.Model != null ? level.Player.Model.BoostVisualPercent : 0f;
            // 0 exactly when the boost ends (the others read > 0 as boosting: Target.boosting shakes off missiles); in steps of
            // 1/16, so a boost ramp is a few sends, not one a frame.
            look.boost = b <= 0f ? (byte)0 : (byte)Mathf.Clamp(Mathf.CeilToInt(b * 16f) * 16 - 1, 15, 255);
            float c = level.Cloak != null && level.Cloak.Rules != null ? level.Cloak.Rules.Percentage : 0f;
            // In steps of 5 % (off the radar from 25 %), 0 exactly when it is off.
            look.cloak = c <= 0f ? (byte)0 : (byte)Mathf.Clamp(Mathf.CeilToInt(c / 5f) * 5, 5, 100);
            look.phase = level.Cloak != null && level.Cloak.Phasing;
            // The EMP lightning on the own ship too.
            if (shock && ownSparks == null) ownSparks = new EmpSparks(localShip);
            ownSparks?.SetEmitting(shock);
            WritePools(hp != null && hp.maxShield > 0 ? hp.shield / hp.maxShield : -1f, hp != null && hp.maxArmor > 0 ? (float)hp.armor / hp.maxArmor : -1f);
            NetPoses.Tick(this);   // this game's poses (its ship, its orbit's NPCs and crates), as they are due
        }
    }
}
