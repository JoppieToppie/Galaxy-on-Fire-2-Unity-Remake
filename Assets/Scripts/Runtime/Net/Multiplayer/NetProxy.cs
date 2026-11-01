// NetProxy.cs
// An orbit object (an NPC ship, a fixed object, a turret, a mission's space junk) of one player's game shown to the other
// players in that orbit (NetOrbit asks NetState to spawn one per object; the host spawns it owned by that player). Only the
// players flying in its orbit have it (NetInterest: Netcode's observers; a player arriving gets it with its state). Its
// owner writes:
//   its description once (ProxySpec: the model, scale, name, explosion, hit boxes, turret items, and the whole SpawnSpec it
//     was made from with its cargo: written again only when that cargo or a reference to another proxy changes);
//   its status when it changes (NetProxyStatus: race, standing toward the owner's player, hull, radius, hidden, life,
//     cloak, EMP, stealable cargo, mission role, killer, waypoint) and the players it turned on (aggressors);
//   its pose in the pose stream (NetPoses: unreliable, ~15 bytes, 10 Hz near another player, less far off or idle), not
//     as NetworkVariables (docs/notes/multiplayer-sync.md "Bandwidth").
// The others load the same model at that scale and smooth its motion (NetSmoothing). There it is a Target in
// Target.NetShips, so the radar locks it and the HUD marks it like a traffic ship; hits on it (guns, missiles) go to the
// owner's ship (Target.RemoteDamage), which takes them as its own player's hits. Its life follows the ship's state:
// dying (no marker, no lock, the pose still followed), dead (the explosion at its scale, the model hidden unless it
// leaves a wreck), flying again after a relaunch. Its shots are mirrored (NetShotSender -> NetShotMirror, NetShotData).
// Another player's EMP reaches the owner's ship too (NpcShip.OnRemoteEmp), an EMP-disabled ship shows its lightning to
// everyone and, holding cargo, can be stolen from by the others' tractor beams (StealUpRpc), and the player whose hit
// destroyed it gets the kill (KillCreditRpc). The markers' colours follow each player's own standings and squad. A
// freelance mission's ships look like any other ship to players outside the mission's team.
// When its game leaves the orbit (docks, jumps, disconnects) the server hands it to the orbit's runner (NetState.HandOver:
// the ownership changes, the proxy stays), every kind of object, not only flying traffic: the runner rebuilds it from its
// description and status where its copy is, with the copy's speed (NetOrbit.AdoptHandedOver, Bind), so the others see no
// change; a dying one finishes dying, a wreck stays a wreck (kept as is: the new owner drives the proxy without a ship).
// Hits, EMP, shots and kill credits go through the server's checks (DamageUpRpc, ShotUpRpc, KillCreditUpRpc: the shooter
// flies in this ship's orbit, sane values, a credit only for a player who hit it a moment ago, once).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed class NetProxy : NetworkBehaviour, World.IOrbitProxy, INetPosed
    {
        public const byte Hostile = 0, Friend = 1, Neutral = 2;
        public const byte Flying = 0, Dying = 1, Dead = 2;

        static readonly NetworkVariableReadPermission Read = NetworkVariableReadPermission.Everyone;
        static readonly NetworkVariableWritePermission Owner = NetworkVariableWritePermission.Owner;

        readonly NetworkVariable<int> station = new NetworkVariable<int>(-1);   // server-written at spawn
        readonly NetworkVariable<int> localId = new NetworkVariable<int>(-1);
        readonly NetworkVariable<ulong> creator = new NetworkVariable<ulong>(ulong.MaxValue);  // server-written at spawn: whose ship
        readonly NetworkVariable<bool> handedOver = new NetworkVariable<bool>(false);   // server-written: handed to an orbit's runner (NetState.HandOver)
        readonly NetworkVariable<FixedString4096Bytes> spec = new NetworkVariable<FixedString4096Bytes>(default, Read, Owner);   // ProxySpec, base64
        readonly NetworkVariable<NetProxyStatus> status = new NetworkVariable<NetProxyStatus>(new NetProxyStatus { race = -1, relation = Neutral, hull = 65535, radius = 50f, stealItem = -1, killer = ulong.MaxValue }, Read, Owner);
        // Hostility toward the other players (NpcShip.aggressors, client ids), for their markers and a takeover.
        readonly NetworkVariable<FixedString128Bytes> aggressors = new NetworkVariable<FixedString128Bytes>(default, Read, Owner);
        const int FreighterVolumes = 100000;
        int boxesFor = -1;

        readonly NetSmoothing smoothing = new NetSmoothing();
        readonly NetPoses.SendState poseSend = new NetPoses.SendState();
        Vector3 posePosition;
        Quaternion poseRotation = Quaternion.identity;
        bool hasPose;
        NpcShip ship;
        Target junk;   // the owner's space junk (a Junk removal mission, or one taken over), instead of a ship
        GameObject visual;
        Target target;
        byte shownLife = Flying;
        NetShotSender sender;
        NetShotMirror mirror;
        int pendingStation = -1, pendingId = -1;
        bool shown, viewer;
        int lastAggressors;
        EmpSparks sparks;
        // Its engine parts dark while the owner's ship is EMP-disabled (NpcShip.UpdateEmpEngines); playerEngines: which parts.
        bool playerEngines, enginesEmpOff;
        World.NpcCloak cloakLook;   // the cloak drawn on this copy (NpcCloak.Show), like another player's
        string specRead;            // the spec text last decoded (Spec)
        ProxySpec specCache;
        float specCheckTimer, keptSince = -1f;

        NetProxyStatus St => status.Value;

        public int Station => station.Value;
        /// <summary>Its race (Standing ids; -1 unknown).</summary>
        public int Race => St.race;
        /// <summary>The player whose game first had this object (the owner at spawn), kept through handovers.</summary>
        public ulong Creator => creator.Value;
        /// <summary>Owned here without a ship or junk behind it: handed over and not rebuilt (yet), or kept as it is (a wreck,
        /// a dying ship finishing).</summary>
        public bool Orphan => IsSpawned && IsOwner && ship == null && junk == null;
        /// <summary>Taken over by this player (FreelanceOrbit): hidden here until the old owner's copy is gone.</summary>
        public bool Adopted { get; private set; }
        /// <summary>A freelance mission's space junk (its id range, NetOrbit.JunkBase; known from the spawn).</summary>
        public bool IsJunk => localId.Value >= NetOrbit.JunkBase || (Spec?.junkKind ?? -1) >= 0;
        public int JunkKind => Spec?.junkKind ?? -1;
        public bool IsMissionShip => Spec?.missionShip ?? false;
        /// <summary>A player's hired wingman (their game's NPC): it goes with its player, never handed over.</summary>
        public bool IsWingman => Spec?.wingman ?? false;
        /// <summary>The event batch that spawned it (EventRunner), 0 = none.</summary>
        public int EventTag => (Spec?.eventFlags ?? 0) >> 1;
        /// <summary>The client id of the player who destroyed it, MaxValue = none yet or an NPC.</summary>
        public ulong Killer => St.killer;
        /// <summary>Flying (not dying, dead or gone): an event's living ship.</summary>
        public bool FlyingNow => St.life == Flying;
        public int RoleFlags => St.roleFlags;
        /// <summary>RoleFlags' bit of a co-op story level's ship (NpcShip.StoryShip; FreelanceOrbit's roles are below it).</summary>
        public const int StoryRole = 1 << 24;
        /// <summary>A co-op story level's ship of the leader's: it goes when the leader leaves its orbit (never handed over).</summary>
        public bool IsStoryShip => (RoleFlags & StoryRole) != 0;
        public bool IsFlying => St.life == Flying;
        public byte Life => St.life;
        public string Label => Spec?.label ?? "";
        bool OtherGame => creator.Value != NetworkManager.LocalClientId;
        /// <summary>Its description (null before the owner's first arrives, or unreadable).</summary>
        public ProxySpec Spec
        {
            get
            {
                string text = spec.Value.ToString();
                if (text != specRead) { specRead = text; specCache = ProxySpec.Decode(text); }
                return specCache;
            }
        }
        /// <summary>Another game in the orbit can rebuild it (everything but a wingman, which goes with its player).</summary>
        public bool HandOverable => !IsWingman && (Spec != null || localId.Value >= NetOrbit.JunkBase);
        /// <summary>The server handed it to this orbit's runner: its new owner rebuilds it (NetOrbit.AdoptHandedOver).</summary>
        public bool HandedOver => handedOver.Value;
        /// <summary>Server: before the ownership change (HandOver).</summary>
        public void MarkHandedOver() { if (IsServer) handedOver.Value = true; }
        /// <summary>A flying mission ship (or the mining plant, junk) of another game, for a squadmate's takeover (FreelanceOrbit).</summary>
        public bool MissionAdoptable => (IsMissionShip || IsJunk) && St.life == Flying && !Adopted && OtherGame;
        /// <summary>The ship as a spawn of this game at 'gamePosition' (its whole spec; ship -1 = not a ship).</summary>
        public SpawnSpec AdoptSpec(Vector3 gamePosition)
        {
            var s = Spec?.spec;
            if (s == null) return new SpawnSpec { ship = -1, position = gamePosition };
            var copy = ProxySpecCopy(s);
            copy.position = gamePosition;
            if (Spec.maxHull > 0) copy.hitpoints = Spec.maxHull;
            copy.capitalHost = copy.battleFoe = copy.routeAround = null;
            return copy;
        }
        /// <summary>A fresh copy of a decoded spec (each rebuild gets its own).</summary>
        static SpawnSpec ProxySpecCopy(SpawnSpec s)
        {
            var w = new WireWriter(256);
            ProxySpec.WriteSpec(w, s);
            return ProxySpec.ReadSpec(new WireReader(w.ToArray()));
        }
        /// <summary>Its pose: the owner's object, else the last one received.</summary>
        public Vector3 WorldPosition => IsOwner && ship != null ? ship.Model.position : IsOwner && junk != null ? junk.transform.position : posePosition;
        public Quaternion WorldRotation => IsOwner && ship != null ? ship.Model.rotation : IsOwner && junk != null ? junk.transform.rotation : poseRotation;
        /// <summary>Where its copy is drawn here and how fast it moves (a rebuild starts there, NetOrbit.AdoptHandedOver).</summary>
        public Vector3 ShownPosition => shown && hasPose ? transform.position : posePosition;
        public Quaternion ShownRotation => shown && hasPose ? transform.rotation : poseRotation;
        public Vector3 ShownVelocity => smoothing.Velocity;
        public float HullFraction => St.HullFraction;
        public NetProxyStatus Status => St;
        /// <summary>The ship this game runs for it (null = another game's, or none).</summary>
        public NpcShip BoundShip => IsOwner ? ship : null;

        public void MarkAdopted()
        {
            Adopted = true;
            SetShown(false);
        }

        /// <summary>Taken over by this game (FreelanceOrbit's promotion): hidden here, gone in the other games.</summary>
        public void Adopt()
        {
            MarkAdopted();
            NetState.Instance?.AdoptedRpc(NetworkObjectId);
        }
        public int LocalId => localId.Value;
        /// <summary>Host: when it was spawned (NetState's sweep waits for its owner's pose to arrive).</summary>
        public float SpawnedAt { get; private set; }

        /// <summary>This proxy's ship as a Target here: the owner's NpcShip, else the proxy Target.</summary>
        public Target LocalTarget => IsOwner && !viewer ? (ship != null ? ship.Target : junk) : target;

        // ---- the pose stream (NetPoses) -----------------------------------------------------------------------------

        public NetPoses.SendState PoseSend => poseSend;
        public int PoseStation => station.Value;

        public bool CurrentPose(out Vector3 position, out Quaternion rotation, out bool hasLook, out PoseLook look)
        {
            hasLook = false;
            look = default;
            if (ship != null && !ship.Gone) { position = ship.Model.position; rotation = ship.Model.rotation; return true; }
            if (junk != null) { position = junk.transform.position; rotation = junk.transform.rotation; return true; }
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        public void ReceivePose(in PoseEntry entry, float gap, bool show)
        {
            posePosition = entry.position;
            poseRotation = entry.rotation;
            hasPose = true;
            if (show && !IsOwner) smoothing.Push(entry.position, gap);
        }

        /// <summary>The server's last pose goes with the spawn to a player it is shown to (arriving in the orbit).</summary>
        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            serializer.SerializeValue(ref hasPose);
            if (!hasPose) return;
            serializer.SerializeValue(ref posePosition);
            serializer.SerializeValue(ref poseRotation);
            if (serializer.IsReader) smoothing.Push(posePosition);
        }

        // ---- spawn, ownership ---------------------------------------------------------------------------------------

        /// <summary>Host, before spawning: the orbit and the owner's ship id (written in OnNetworkSpawn).</summary>
        public void Init(int stationIndex, int id)
        {
            pendingStation = stationIndex;
            pendingId = id;
        }

        public override void OnNetworkSpawn()
        {
            DontDestroyOnLoad(gameObject);
            NetPoses.Register(this);
            if (IsServer)
            {
                station.Value = pendingStation;
                localId.Value = pendingId;
                creator.Value = OwnerClientId;
                SpawnedAt = Time.unscaledTime;
            }
            if (IsOwner && creator.Value == NetworkManager.LocalClientId && !handedOver.Value)
            {
                var orbit = NetOrbit.Current != null && NetOrbit.Current.Station == station.Value ? NetOrbit.Current : null;
                if (orbit != null && localId.Value >= NetOrbit.JunkBase)
                {
                    // A freelance mission's space junk: a one-hit target, always hostile.
                    junk = orbit.Junk(localId.Value);
                    if (junk == null) { DropStale(); return; }
                    orbit.Register(this, junk);
                    WriteJunk(orbit.JunkKind(localId.Value));
                    return;
                }
                ship = orbit != null ? orbit.Ship(localId.Value) : null;
                if (ship == null) { DropStale(); return; }   // the ship went (or the scene changed) before this spawned
                orbit.Register(this, ship);
                WriteSpec();
                SendState();
                sender = new NetShotSender(SendShot, BlastUpRpc, () => ship != null ? ship.CurrentTarget : null);
                sender.Hook(ship.Guns);
                return;
            }
            InitViewer();
        }

        /// <summary>The owner's junk as the others see it.</summary>
        void WriteJunk(int kind)
        {
            spec.Value = new ProxySpec { junkKind = kind, label = Target.JunkName, explosionScale = 0.5f }.Encode();
            var s = St;
            s.race = Standing.Pirate;
            s.relation = Hostile;
            s.flags = NetProxyStatus.AlwaysHostile;
            s.radius = junk.radius;
            status.Value = s;
        }

        /// <summary>The owner's ship as the others see it and as another game would rebuild it.</summary>
        void WriteSpec()
        {
            var m = ship.Model;
            var sp = ship.Spec;
            var p = new ProxySpec
            {
                model = ship.ModelPath ?? "",
                scale = m.lossyScale,
                label = ship.Target != null && !string.IsNullOrEmpty(ship.Target.displayName) ? ship.Target.displayName : "",
                explosionScale = ship.IsFixed ? sp.explosionScale : ship.IsFreighter ? ship.FreighterExplosionScale : ship.FighterExplosionScale,   // NpcShip.UpdateDying
                leavesWreck = (ship.IsFixed || ship.IsFreighter) && !sp.modCapital,   // (a mod capital ship leaves none)
                hitVolume = sp.freighter ? FreighterVolumes + sp.ship * 16 + Mathf.Clamp(sp.race, 0, 15)
                          : sp.capitalEnhanced && sp.fixedObject != null && sp.collisionId >= 0 ? sp.collisionId : -1,
                missionShip = ship.MissionShip,
                maxHull = ship.Hp != null ? ship.Hp.maxHull : -1,
                eventFlags = sp.eventTag << 1 | (sp.alwaysEnemy ? 1 : 0),
                wingman = ship.IsWingman,
                spec = sp,
                cargo = new List<ItemStack>(),
                hostRef = RefOf(sp.capitalHost), foeRef = RefOf(sp.battleFoe), aroundRef = RefOf(sp.routeAround),
            };
            foreach (var c in ship.CargoList) p.cargo.Add(new ItemStack(c.item, c.amount));
            var capital = ship.GetComponent<CapitalShip>();
            if (capital != null) { p.capitalLaunched = capital.Launched; p.capitalProvoked = capital.Provoked; }
            if (ship.MountedTurrets != null)
            {
                var packed = NpcMountedTurrets.Pack(sp.ship, ship.MountedTurrets.Items);
                if (packed.Length <= 60) p.turrets = packed;
            }
            string text = p.Encode();
            if (System.Text.Encoding.UTF8.GetByteCount(text) > 4000) { p.spec.route = null; text = p.Encode(); }   // a very long route
            if (spec.Value.ToString() != text) spec.Value = text;
        }

        /// <summary>The proxy of the ship made from 's' (0 = none, or not spawned yet: written again once it is).</summary>
        ulong RefOf(SpawnSpec s)
        {
            if (s == null || NetOrbit.Current == null) return 0;
            var proxy = NetOrbit.Current.ProxyOf(NetOrbit.Current.ShipOf(s));
            return proxy != null && proxy.IsSpawned ? proxy.NetworkObjectId : 0;
        }

        /// <summary>The mirrored shots pass through the shooter's own side, like the real ones (its gun hits only its enemies,
        /// NpcShip.HitTargets; same-race ships never hit each other): a capital ship's missiles left its launchers right
        /// beside its own turrets' copies and "hit" them at once here. Players (this one, the others) are never passed.</summary>
        bool SameSide(Target t) =>
            t != null && target != null && !t.isPlayer && t.race >= 0 && t.race == target.race && t.GetComponent<NetPlayer>() == null;

        /// <summary>A copy of another game's object here: the Target, the smoothing, the shot mirror.</summary>
        void InitViewer()
        {
            if (viewer) return;
            viewer = true;
            mirror = new NetShotMirror(null, () => target, SameSide);
            target = gameObject.AddComponent<Target>();
            target.isShip = true;
            target.customDeath = true;
            target.maxHp = 100f;
            target.RemoteDamage = (amount, hitVector, byNpc, _) => DamageUpRpc(amount, hitVector, byNpc);   // NPCs have no shield
            target.RemoteEmp = emp => EmpUpRpc(emp);
            target.RemoteSteal = (item, amount) => StealUpRpc(item, amount);
            target.RemoteHeal = points => HealUpRpc(points);   // another player's repair beam (RepairBeam)
            // Space junk is lockable after the ships and a far dot, like the owner's own junk (Target.RadarObjects).
            if (IsJunk) { Target.RadarObjects.Add(target); target.plateNameOnly = target.plateNoIcon = true; }
            else Target.NetShips.Add(target);
            SetShown(false);
        }

        /// <summary>The host took it over from a player who left, or the server handed it to this orbit's runner: no ship
        /// behind it here yet, so it shows like anyone else's until the runner rebuilds it (Bind).</summary>
        public override void OnGainedOwnership()
        {
            if (IsSpawned && ship == null && junk == null) InitViewer();
        }

        /// <summary>The server handed it to another orbit's runner (this game left the orbit): a copy here from now on. A ship
        /// still here (a place that flickered) goes: another game runs it now.</summary>
        public override void OnLostOwnership()
        {
            if (!IsSpawned) return;
            if (ship != null && !ship.Gone) ship.Vanish();
            ship = null;
            junk = null;
            keptSince = -1f;
            sender?.Unhook();
            sender = null;
            InitViewer();
        }

        /// <summary>This orbit's runner rebuilt the object it was handed (NetOrbit.AdoptHandedOver): the copy here goes and
        /// this proxy follows the new ship (or junk), so the others keep seeing the same one.</summary>
        public void Bind(NpcShip rebuilt) => BindTo(rebuilt, null);
        public void Bind(Target rebuiltJunk) => BindTo(null, rebuiltJunk);

        void BindTo(NpcShip rebuilt, Target rebuiltJunk)
        {
            if (viewer)
            {
                viewer = false;
                if (target != null)
                {
                    Target.NetShips.Remove(target);
                    Target.RadarObjects.Remove(target);
                    HullCollision.Detach(target);
                    Destroy(target);
                    target = null;
                }
                mirror?.Clear();
                mirror = null;
                sparks?.Clear();
                sparks = null;
                cloakLook?.Dispose();
                cloakLook = null;
                if (visual != null) { Destroy(visual); visual = null; }
                shown = false;
            }
            keptSince = -1f;
            poseSend.sent = false;   // the first pose of the rebuilt object goes at once
            if (rebuiltJunk != null)
            {
                junk = rebuiltJunk;
                NetOrbit.Current?.Register(this, rebuiltJunk);
                return;
            }
            ship = rebuilt;
            NetOrbit.Current?.Register(this, rebuilt);
            lastAggressors = -1;   // written again from the new ship
            sender ??= new NetShotSender(SendShot, BlastUpRpc, () => ship != null ? ship.CurrentTarget : null);
            sender.Hook(rebuilt.Guns);
        }

        /// <summary>NetOrbit.AdoptHandedOver: not rebuilt but kept as it is (a wreck, a ship finishing its death): this game
        /// drives the proxy without a ship (KeepAsIs's clock).</summary>
        public void KeepAsIs()
        {
            if (keptSince < 0f) keptSince = Time.unscaledTime;
        }

        public bool Kept => keptSince >= 0f;

        /// <summary>Tests (MultiplayerSessionTests): as if this game had just been handed the object by another one: its ship
        /// goes, the proxy is a copy at the ship's last pose (NetOrbit.SimulateHandOverForTest).</summary>
        internal void ReleaseForTest()
        {
            if (ship == null && junk == null) return;
            posePosition = WorldPosition;
            poseRotation = WorldRotation;
            hasPose = true;
            smoothing.Push(posePosition);
            var old = ship;
            var oldJunk = junk;
            ship = null;
            junk = null;
            sender?.Unhook();
            sender = null;
            if (old != null && !old.Gone) old.Vanish();
            if (oldJunk != null) oldJunk.gameObject.SetActive(false);
            InitViewer();
        }

        /// <summary>The owner has no ship for it (gone before the spawn, a scene change): it goes.</summary>
        void DropStale()
        {
            var state = NetState.Instance;
            if (state != null && state.IsSpawned) state.DespawnRpc(NetworkObjectId);
        }

        /// <summary>NetState: a squadmate took this ship over while its creator is still here: the creator's ship goes.</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void TakenOverRpc()
        {
            if (ship != null) ship.Vanish();
            else if (junk != null) junk.gameObject.SetActive(false);
        }

        public override void OnNetworkDespawn()
        {
            NetPoses.Unregister(this);
            if (target != null) { Target.NetShips.Remove(target); Target.RadarObjects.Remove(target); }
            sender?.Unhook();
            mirror?.Clear();
            sparks?.Clear();
            cloakLook?.Dispose();
            cloakLook = null;
        }

        // ---- shots and hits -------------------------------------------------------------------------------------------

        void SendShot(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, ulong targetId) =>
            ShotUpRpc(new NetShotData(item, position, velocity, up, lifetimeMs, homingDelayMs, targetId));

        /// <summary>The owner's NPC shot, through the server (only the ship's game sends its shots): checked and limited like
        /// a player's (NetPlayer.ShotUpRpc), then to the players in its orbit.</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void ShotUpRpc(NetShotData shot)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Shot(shot.item, shot.position, shot.velocity, shot.up, shot.lifetimeMs, shot.homingDelayMs)) { NetRateLimit.Reject(OwnerClientId, $"an NPC shot of item {shot.item}"); return; }
            if (NetInterest.OrbitGroup(this, station.Value, OwnerClientId, out var to)) ShotRpc(shot, to);
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        void ShotRpc(NetShotData shot, RpcParams rpc = default)
        {
            if (shown) mirror?.Shot(shot.item, shot.position, shot.velocity, shot.up, shot.lifetimeMs, shot.homingDelayMs, NetShots.Resolve(shot.targetId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BlastUpRpc(int item, Vector3 point)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Item(item) || !NetGuard.Position(point)) { NetRateLimit.Reject(OwnerClientId, $"an NPC blast of item {item}"); return; }
            if (NetInterest.OrbitGroup(this, station.Value, OwnerClientId, out var to)) BlastRpc(item, point, to);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void BlastRpc(int item, Vector3 point, RpcParams rpc = default)
        {
            if (shown) mirror?.Blast(item, point);
        }

        /// <summary>Another game's hit on this ship, through the server: only from a game flying in this ship's orbit, a finite
        /// damage up to NetGuard.MaxDamage, at its rate. (A direct RPC to the owner let a modified client destroy every NPC
        /// of any orbit from anywhere.)</summary>
        [Rpc(SendTo.Server)]
        void DamageUpRpc(float amount, Vector3 hitVector, bool byNpc, RpcParams rpc = default)
        {
            ulong shooter = rpc.Receive.SenderClientId;
            if (shooter == OwnerClientId || !NetRateLimit.Allow(shooter, NetRateLimit.Kind.Hit)) return;
            if (!NetGuard.Damage(amount) || !NetGuard.Finite(hitVector)) { NetRateLimit.Reject(shooter, $"a hit of {amount}"); return; }
            if (!NetGuard.InOrbit(shooter, station.Value)) return;
            NetState.Instance?.NoteHit(NetworkObjectId, shooter);
            DamageRpc(amount, hitVector, byNpc, shooter);
        }

        /// <summary>The checked hit (DamageUpRpc): the owner's ship takes it, and turns on that player's squad (not on its own
        /// player: taken as an NPC's hit, so no standing change or kill credit for the owner).</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void DamageRpc(float amount, Vector3 hitVector, bool byNpc, ulong shooter)
        {
            if (junk != null) { if (junk.Alive) junk.Damage(amount, true, hitVector); return; }   // counts for the mission (all junk)
            if (ship == null || ship.Target == null || !ship.Target.Alive) return;
            // Another game's NPC (its shot at this player's wingman): an NPC's hit, nobody's kill.
            if (byNpc) { ship.Target.Damage(amount, true, hitVector); return; }
            var by = NetSquad.Find(shooter);
            // Whether it was after them (their kill counts only then, like the player's own: Traffic.OnShipDied).
            bool hostile = by != null && World.NpcShip.HostileToRemote != null && World.NpcShip.HostileToRemote(ship, by.LocalTarget);
            ship.OnRemoteHit(shooter, (int)amount);
            ship.Target.killedByRemote = true;   // a freelance mission counts another player's kill as its player's
            ship.Target.remoteKiller = shooter;  // and the raid news names them (NetOrbit)
            ship.Target.Damage(amount, true, hitVector);
            if (ship.Target.Alive) { ship.Target.killedByRemote = false; ship.Target.remoteKiller = ulong.MaxValue; }
            if (!ship.Target.Alive) { var s = St; s.killer = shooter; status.Value = s; }
            if (!ship.Target.Alive && NetOrbit.Current != null) KillCreditUpRpc(shooter, ship.Race, NetOrbit.Current.SystemRace, hostile);
        }

        /// <summary>Another game's EMP on this ship, through the server like a hit.</summary>
        [Rpc(SendTo.Server)]
        void EmpUpRpc(int emp, RpcParams rpc = default)
        {
            ulong shooter = rpc.Receive.SenderClientId;
            if (shooter == OwnerClientId || !NetRateLimit.Allow(shooter, NetRateLimit.Kind.Hit)) return;
            if (emp <= 0 || emp > NetGuard.MaxDamage) { NetRateLimit.Reject(shooter, $"an EMP of {emp}"); return; }
            if (NetGuard.InOrbit(shooter, station.Value)) EmpRpc(emp, shooter);
        }

        /// <summary>The checked EMP: the owner's ship takes it (it may turn on them).</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void EmpRpc(int emp, ulong shooter)
        {
            if (ship != null) ship.OnRemoteEmp(shooter, emp);
        }

        /// <summary>Another player's tractor beam stole 'amount' of 'item' from this ship (CombatRadar.Capture: rnd(amount) of
        /// the first entry), through the server: only from that orbit, only while it is disabled and holds that much.</summary>
        [Rpc(SendTo.Server)]
        void StealUpRpc(int item, int amount, RpcParams rpc = default)
        {
            ulong thief = rpc.Receive.SenderClientId;
            if (thief == OwnerClientId || !NetRateLimit.Allow(thief, NetRateLimit.Kind.Claim)) return;
            var s = St;
            if (!s.Has(NetProxyStatus.EmpDisabled) || item != s.stealItem || amount < 1 || amount > s.stealAmount)
            {
                NetRateLimit.Reject(thief, $"a cargo steal of {amount} x {item}");
                return;
            }
            if (NetGuard.InOrbit(thief, station.Value)) StealRpc(item, amount);
        }

        /// <summary>The checked steal: the units leave the owner's ship (NpcShip.StealFrom, KIPlayer::captureCrate).</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void StealRpc(int item, int amount)
        {
            if (ship != null) ship.StealFrom(item, amount);
        }

        /// <summary>Remake: another player's repair beam healed this ship 'points' of hull (RepairBeam: a ship friendly to
        /// them), through the server like a hit: only from that orbit, a sane amount, at its rate.</summary>
        [Rpc(SendTo.Server)]
        void HealUpRpc(int points, RpcParams rpc = default)
        {
            ulong healer = rpc.Receive.SenderClientId;
            if (healer == OwnerClientId || !NetRateLimit.Allow(healer, NetRateLimit.Kind.Hit)) return;
            if (!NetGuard.Damage(points)) { NetRateLimit.Reject(healer, $"a repair of {points}"); return; }
            if (NetGuard.InOrbit(healer, station.Value)) HealRpc(points);
        }

        /// <summary>The checked repair: the owner's ship gets the hull back (Player::heal: hull only, up to its maximum).</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void HealRpc(int points)
        {
            if (ship == null || ship.Gone || ship.Hp == null || ship.Target == null || !ship.Target.Alive || ship.Current != NpcShip.State.Fly) return;
            ship.Hp.hull = Mathf.Min(ship.Hp.maxHull, ship.Hp.hull + points);
            ship.Target.hp = ship.Hp.hull;
        }

        bool credited;   // the server: this ship's kill credit went out (one per proxy: a relaunched ship gets a new one)

        /// <summary>The ship's game says 'shooter' destroyed it: the server passes the credit on once, and only to a player who
        /// hit this ship a moment ago (a modified owner can't hand out kills, or standing changes, to anyone at will).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void KillCreditUpRpc(ulong shooter, int race, int systemRace, bool wasHostile)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Kill) || credited) return;
            var state = NetState.Instance;
            if (state == null || !state.HitRecently(NetworkObjectId, shooter, 15f) || NetSquad.Find(shooter) == null)
            {
                NetRateLimit.Reject(OwnerClientId, "a kill credit for a player who didn't hit the ship");
                return;
            }
            credited = true;
            KillCreditRpc(Mathf.Clamp(race, -1, 15), Mathf.Clamp(systemRace, -1, 15), wasHostile, RpcTarget.Single(shooter, RpcTargetUse.Temp));
        }

        /// <summary>This player destroyed another game's NPC ship: their standing and kills, like the player's own kill
        /// (Traffic.OnShipDied: Standing.ApplyKill, and the kill counted when it was after them).</summary>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void KillCreditRpc(int race, int systemRace, bool wasHostile, RpcParams rpc = default)
        {
            if (Shop.SystemOf(NetGame.Db, Session.StationIndex) != 25) Standing.ApplyKill(race, systemRace);   // not in Loma
            if (!wasHostile) return;
            Session.Kills++;
            if (race == Standing.Pirate) Session.PirateKills++;
        }

        /// <summary>The other players this ship is hostile to (their client ids), for a takeover.</summary>
        public IEnumerable<ulong> Aggressors
        {
            get
            {
                foreach (var part in aggressors.Value.ToString().Split(','))
                    if (ulong.TryParse(part, out ulong id)) yield return id;
            }
        }

        // ---- the owner's status ---------------------------------------------------------------------------------------

        void SendState()
        {
            var t = ship.Target;
            if (t == null) return;
            var s = St;
            s.race = (short)t.race;
            s.relation = t.hostileToPlayer ? Hostile : t.friendToPlayer ? Friend : Neutral;
            s.hull = NetProxyStatus.Hull(t.HullFraction);
            s.radius = t.radius;
            byte l = ship.Current == NpcShip.State.Dying ? Dying : ship.Current == NpcShip.State.Dead || ship.Gone ? Dead : Flying;
            // Destroyed by this game's own player (not an NPC's shot, not another player's: DamageRpc wrote that one).
            if (l != Flying && s.life == Flying && s.killer == ulong.MaxValue && !t.killedByNpc && !t.killedByRemote && !t.Alive)
                s.killer = OwnerClientId;
            s.life = l;
            bool emp = ship.Hp != null && ship.Hp.empDisabled && l == Flying;
            byte f = 0;
            // A cloaked ship isn't hidden here: the others draw its cloak and can still hit it (an EMP breaks it, a blind
            // shot lands), as in its own game; only a level's or a sleeper's hiding hides the copy.
            if (ship.Hidden || ship.ScriptRadarHidden) f |= NetProxyStatus.Hidden;
            if (t.race == Standing.Pirate || t.race == Standing.Void || t.race == Standing.Specter) f |= NetProxyStatus.AlwaysHostile;
            if (emp) f |= NetProxyStatus.EmpDisabled;
            if (ship.Asleep) f |= NetProxyStatus.Asleep;
            if (ship.Inactive) f |= NetProxyStatus.Inactive;
            if (ship.turnedEnemy) f |= NetProxyStatus.Turned;
            s.flags = f;
            s.cloak = (byte)Mathf.Clamp(Mathf.RoundToInt(ship.CloakPercent), 0, 100);
            s.roleFlags = (ship.MissionShip && NetOrbit.Current != null ? NetOrbit.Current.MissionRole(ship) : 0) | (ship.StoryShip ? StoryRole : 0);
            int si = -1, sa = 0;
            if (emp && ship.HasCargo)
                foreach (var c in ship.CargoList)
                    if (c.amount > 0) { si = c.item; sa = c.amount; break; }
            s.stealItem = (short)si;
            s.stealAmount = sa;
            s.routeIndex = (byte)Mathf.Clamp(ship.RouteIndex, 0, 255);
            s.empMs = (ushort)Mathf.Clamp(Mathf.RoundToInt((ship.Hp != null ? ship.Hp.EmpElapsedMs : 0f) / 100f), 0, 65535);   // (x 100 ms)
            if (!s.Equals(St)) status.Value = s;
            if (ship.aggressors.Count != lastAggressors)
            {
                lastAggressors = ship.aggressors.Count;
                string list = string.Join(",", ship.aggressors);
                if (list.Length > 120) list = list.Substring(0, list.LastIndexOf(',', 120));
                aggressors.Value = list;
            }
        }

        // ---- the copies ------------------------------------------------------------------------------------------------

        /// <summary>The model arrives after the spawn when the owner is a client (its first values follow as a delta).</summary>
        bool wreckDamaged;

        void BuildVisual()
        {
            var p = Spec;
            if (visual != null || p == null || (p.junkKind < 0 && p.model.Length == 0)) return;   // the owner's values first
            GameObject prefab;
            string path = p.model;
            int wreck = -1;
            if (p.junkKind >= 0)
            {
                var assets = CombatAssets.Load();
                prefab = assets != null && assets.junk != null && p.junkKind < assets.junk.Length ? assets.junk[p.junkKind] : null;
            }
            else if (path.StartsWith(NpcShip.TurretModelPrefix))
            {
                // A turret object built from a turret item (SpawnSpec.turretItem): the item's turret model, still.
                prefab = int.TryParse(path.Substring(NpcShip.TurretModelPrefix.Length), out int ti) && ti >= 0 && ti < NetGame.Db.Items.Count
                    ? WeaponFx.Load(ti)?.turretMounted : null;
            }
            else if (path.StartsWith(NpcShip.WreckModelPrefix))
            {
                // A freighter wreck held at its end (NpcShip.ShowWreckAtEnd: the Supernova wrecks with a hidden blueprint).
                var assets = CombatAssets.Load();
                if (!int.TryParse(path.Substring(NpcShip.WreckModelPrefix.Length), out wreck)) wreck = -1;
                prefab = assets != null && assets.wrecks != null && wreck >= 0 && wreck < assets.wrecks.Length ? assets.wrecks[wreck] : null;
            }
            // Only an assembled prefab (the owner's word: no other Resources asset loaded into the others' games).
            // A mod's ship ("Assembled/mod/ships/ship_NNN_mod": the session runs the same mods, NetMods) is built at run time.
            else if (path.StartsWith($"{AssembledObject.ResourcesFolder}/{Modding.ModShips.Pack}/"))
                prefab = Modding.ModShips.Template(path.Substring(path.LastIndexOf('/') + 1));
            else prefab = path.StartsWith(AssembledObject.ResourcesFolder + "/") ? Resources.Load<GameObject>(path) : null;
            visual = prefab != null ? Instantiate(prefab, transform, false) : new GameObject("(no model)");
            if (wreck >= 0) PartAnimation.HoldAllAtEnd(visual);
            if (path.StartsWith(NpcShip.TurretModelPrefix)) GunRig.StripForFx(visual);
            visual.transform.SetParent(transform, false);
            var s = p.scale;   // finite and within reason (the battleship is 2x, a capital ship more), else as modelled
            bool sane = NetGuard.Finite(s) && Mathf.Abs(s.x) <= 100f && Mathf.Abs(s.y) <= 100f && Mathf.Abs(s.z) <= 100f;
            visual.transform.localScale = sane ? s : Vector3.one;
            var asm = visual.GetComponent<AssembledObject>();
            playerEngines = asm != null && (GoF2Remake.Data.Settings.NpcPlayerEngines || !asm.HasNpcExhaust) && asm.playerVariantParts != null
                            && asm.playerVariantParts.Length > 0 && asm.playerVariantParts[0] != null;
            asm?.SetPlayerVariant(playerEngines);
            enginesEmpOff = false;
            name = $"NetProxy {path}";
            // Its turret items, still on their mounts (the owner's game aims and fires them; the shots are mirrored).
            if (NpcMountedTurrets.Unpack(p.turrets, out int turretShip, out var turretItems))
                TurretCore.DisplayAll(NetGame.Db, turretShip, turretItems, visual.transform);
            visual.SetActive(shown);
            // Shots hit its real shape; a fighter's (a ship's model that leaves no wreck: not a freighter or a fixed object,
            // not a turret or junk) only with Precise ship collision (HullBody.flyableShip).
            bool fighter = p.junkKind < 0 && wreck < 0 && !p.leavesWreck && path.Contains("/ships/");
            if (target != null && !(St.radius <= 0f && !IsJunk)) HullCollision.Attach(visual, target, null, fighter);   // a fighter turret: none
            turretHullDetached = false;
            if (prefab != null && p.junkKind < 0 && wreck < 0) { cloakLook?.Dispose(); cloakLook = new World.NpcCloak(visual.transform); }
        }

        bool turretHullDetached;

        void SetShown(bool on)
        {
            shown = on;
            if (target != null) { target.enabled = on; target.untargetable = !on; }
            if (visual != null) visual.SetActive(on);
            if (!on) { mirror?.Clear(); sparks?.SetEmitting(false); }
            else { smoothing.Snap(); shownLife = St.life; }   // no explosion for a ship that died before we came
        }

        void ApplyTarget()
        {
            var st = St;
            var p = Spec;
            target.race = st.race;
            // Toward this player: an always-hostile race, a ship this player's squad shot, or one hostile to its owner while
            // the owner is a squadmate; the owner's friends only through the squad too (else neutral).
            var me = NetPlayer.Local;
            var owner = NetSquad.Find(OwnerClientId);
            bool mateOwner = me != null && NetSquad.Same(owner, me);
            bool shotBySquad = false;
            foreach (var id in Aggressors) if (NetSquad.SameClient(id, me)) { shotBySquad = true; break; }
            bool empDisabled = st.Has(NetProxyStatus.EmpDisabled);
            // ... and this player's own standing toward its race (Standing.IsEnemy / IsFriend), which the owner's AI uses too.
            target.hostileToPlayer = st.Has(NetProxyStatus.AlwaysHostile) || shotBySquad || Standing.IsEnemy(st.race) || (mateOwner && st.relation == Hostile);
            target.friendToPlayer = !target.hostileToPlayer && Standing.IsFriend(st.race);
            if (empDisabled && sparks == null) sparks = new EmpSparks(transform);
            sparks?.SetEmitting(empDisabled);
            if (empDisabled != enginesEmpOff && visual != null)
            {
                enginesEmpOff = empDisabled;
                visual.GetComponent<AssembledObject>()?.SetExhaust(!enginesEmpOff, playerEngines);
            }
            // Dying / dead (a tumble, a wreck, destroyed junk): not alive here either, so shots pass it like on the owner's.
            target.hp = st.life == Flying ? Mathf.Max(0.001f, st.HullFraction) * target.maxHp : 0f;
            target.netMaxHull = p != null ? p.maxHull : -1;   // the repair / transfusion beams' pick (RepairBeam)
            target.radius = st.radius;
            int volume = p != null ? p.hitVolume : -1;
            if (boxesFor != volume)
            {
                // The owner's hit boxes (NpcShip.Setup): shots hit the hull, not only the cube at its centre.
                boxesFor = volume;
                target.boxes = volume >= FreighterVolumes ? NpcShip.LocalBoxes(CollisionVolume.ForFreighter((volume - FreighterVolumes) / 16, (volume - FreighterVolumes) % 16))
                             : volume >= 0 ? NpcShip.LocalBoxes(CollisionVolume.ForStaticObject(volume)) : null;
            }
            // Its cloak (a Specter's): drawn like the owner's, off the radar and the lock from a quarter (NpcCloak.Hidden),
            // turrets and NPCs don't aim at it (Target.cloaked); still hit by shots and blasts.
            float cloaked = st.life == Flying ? st.cloak : 0f;
            cloakLook?.Show(cloaked, Time.deltaTime * 1000f);
            target.cloaked = cloaked >= 25f;
            // A fighter turret (ships 45 / 51, radius 0 on the owner's side: Traffic.AttachFighterTurret) is nobody's target
            // and has no hull on the others' copies either; it had a marker and a hull bar there, soaked shots and, its
            // owner's turret being invulnerable, never died (Discord report "Indestructible turret").
            bool fighterTurret = st.radius <= 0f && !IsJunk;
            if (fighterTurret && !turretHullDetached) { HullCollision.Detach(target); turretHullDetached = true; }
            bool hidden = st.Has(NetProxyStatus.Hidden);
            target.untargetable = hidden || st.life != Flying || cloaked >= 25f || fighterTurret;
            // A mission ship's name only for the mission's team (its owner and their squad).
            bool team = !IsMissionShip || NetSquad.SameClient(OwnerClientId, NetPlayer.Local);
            // The owner's game writes the label (a wingman's name comes from its save): no rich-text tags reach the others'
            // lock plates (<size>, <color> to impersonate staff or cover the HUD).
            target.displayName = IsJunk ? Target.JunkName : Label.Length > 0 && team ? NetNews.Safe(Label) : null;
            // A steal candidate while it is disabled with cargo; a mission ship's cargo only for the mission's team (like its
            // crates, NetCrate).
            bool stealable = TargetLockRules.RemoteStealable(empDisabled, st.life == Flying, team, st.stealItem, st.stealAmount);
            target.netStealItem = stealable ? st.stealItem : -1;
            target.netStealAmount = stealable ? st.stealAmount : 0;
            if (st.life != shownLife)
            {
                // NpcShip.UpdateDying's end: the explosion (with its sound); a fighter's model goes with it.
                if (st.life == Dead) Explosion.Spawn(transform.position, p != null ? p.explosionScale : 1f);
                shownLife = st.life;
            }
            bool leavesWreck = p != null && p.leavesWreck;
            bool show = !hidden && (st.life != Dead || leavesWreck);
            // A fixed object's or freighter's copy keeps its hull through the death (no wreck animation here): it takes the
            // wreck's damaged material like the owner's wreck (CombatAssets.ApplyWreckDamage).
            if (st.life != Flying && leavesWreck && !wreckDamaged && visual != null) { CombatAssets.ApplyWreckDamage(visual); wreckDamaged = true; }
            if (visual != null && visual.activeSelf != show) visual.SetActive(show);
        }

        /// <summary>The owner of a proxy kept as it is (KeepAsIs): a dying ship finishes (dead after DyingSeconds: the
        /// explosion everywhere), a dead one without a wreck goes a moment later, a wreck stays.</summary>
        void UpdateKept()
        {
            const float DyingSeconds = 2.5f, GoneSeconds = 2f;
            var s = St;
            float since = Time.unscaledTime - keptSince;
            if (s.life == Dying && since > DyingSeconds) { s.life = Dead; status.Value = s; keptSince = Time.unscaledTime; return; }
            bool wreck = Spec != null && Spec.leavesWreck;
            if (s.life == Dead && !wreck && since > GoneSeconds) { keptSince = float.MaxValue; DropStale(); }
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner && viewer && keptSince >= 0f) UpdateKept();
            if (!IsOwner || viewer)
            {
                var local = NetPlayer.Local;
                bool here = local != null && local.InSpace && local.Station == station.Value && !Adopted && smoothing.Has;
                if (here != shown) SetShown(here);
                if (!shown) return;
                BuildVisual();
                smoothing.Apply(transform, poseRotation);
                ApplyTarget();
                mirror?.Update(Time.deltaTime * 1000f);
                return;
            }
            if (junk != null)
            {
                var js = St;
                js.life = junk.Alive ? Flying : Dead;
                js.hull = (ushort)(junk.Alive ? 65535 : 0);
                if (!js.Equals(St)) status.Value = js;
                return;
            }
            if (ship == null) return;
            sender?.Hook(ship.Guns);   // a gun set later (the second slot, a level's SetGun)
            SendState();
            // The description again when its cargo or a reference to another proxy changed (about once a second).
            if ((specCheckTimer -= Time.unscaledDeltaTime) <= 0f)
            {
                specCheckTimer = 1f;
                WriteSpec();
            }
        }
    }
}
