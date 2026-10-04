// NetProxy.cs
// An orbit authority's NPC ship shown to the other players in that orbit (NetOrbit asks NetState to spawn one per living
// ship of its traffic; the host spawns it owned by that player). The owner follows the ship's model and writes its pose,
// race, standing toward the player (hostile / friend / neutral), hull fraction, hit cube and whether it is hidden
// (cloaked, a sleeping Most Wanted criminal); the others load the same assembled prefab (its Resources path) at that scale
// and smooth its motion (NetSmoothing), but only while they are in that orbit themselves (else it is hidden). There it is a
// Target in Target.NetShips, so the radar locks it and the HUD marks it like a traffic ship; hits on it (guns, missiles) go
// to the owner's ship (Target.RemoteDamage), which takes them as its own player's hits. Its life follows the ship's state:
// dying (the tumble or wreck animation: no marker, no lock, the pose still followed), dead (the explosion at its scale,
// the model hidden unless it leaves a wreck: freighters, fixed objects), flying again after a relaunch. Its shots (every
// gun, missiles and blasts included) are mirrored (NetShotSender -> NetShotMirror). Despawned when the ship leaves, or its
// owner leaves the orbit (NetOrbit, NetState). Another player's EMP reaches the owner's ship too (NpcShip.OnRemoteEmp), an
// EMP-disabled ship shows its lightning to everyone, and the player whose hit destroyed it gets the kill (their own
// standing and kill count, KillCreditRpc). The markers' colours follow each player's own standings and squad. A freelance
// mission's ships look like any other ship to players outside the mission's team: no mission name on the lock plate
// (the Hijacker, the Wanted target, the Challenge rival); their kills still help the team (NetMissions). A mission ship
// also carries its part in the mission (FreelanceOrbit.RoleFlags) and its hull, so a squadmate taking the mission orbit
// over adopts it with its role (FreelanceOrbit.Promote). Its creator (the player whose game has the ship) stays known
// after an owner change: the host holding a disconnected player's proxies shows them like anyone else (viewer mode) until
// they are taken over or swept. Its owner drops a proxy whose ship is gone before it spawned.
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
    public sealed class NetProxy : NetworkBehaviour
    {
        const byte Hostile = 0, Friend = 1, Neutral = 2;
        const byte Flying = 0, Dying = 1, Dead = 2;

        static readonly NetworkVariableReadPermission Read = NetworkVariableReadPermission.Everyone;
        static readonly NetworkVariableWritePermission Owner = NetworkVariableWritePermission.Owner;

        readonly NetworkVariable<int> station = new NetworkVariable<int>(-1);   // server-written at spawn
        readonly NetworkVariable<int> localId = new NetworkVariable<int>(-1);
        readonly NetworkVariable<Vector3> position = new NetworkVariable<Vector3>(default, Read, Owner);
        readonly NetworkVariable<Quaternion> rotation = new NetworkVariable<Quaternion>(Quaternion.identity, Read, Owner);
        readonly NetworkVariable<FixedString128Bytes> model = new NetworkVariable<FixedString128Bytes>(default, Read, Owner);
        readonly NetworkVariable<Vector3> scale = new NetworkVariable<Vector3>(Vector3.one, Read, Owner);
        readonly NetworkVariable<int> race = new NetworkVariable<int>(-1, Read, Owner);
        readonly NetworkVariable<byte> relation = new NetworkVariable<byte>(Neutral, Read, Owner);
        readonly NetworkVariable<float> hull = new NetworkVariable<float>(1f, Read, Owner);
        readonly NetworkVariable<float> radius = new NetworkVariable<float>(50f, Read, Owner);
        readonly NetworkVariable<bool> hidden = new NetworkVariable<bool>(false, Read, Owner);
        readonly NetworkVariable<FixedString64Bytes> label = new NetworkVariable<FixedString64Bytes>(default, Read, Owner);
        readonly NetworkVariable<byte> life = new NetworkVariable<byte>(Flying, Read, Owner);
        readonly NetworkVariable<float> explosionScale = new NetworkVariable<float>(1f, Read, Owner);
        readonly NetworkVariable<bool> leavesWreck = new NetworkVariable<bool>(false, Read, Owner);
        // A takeover's spawn spec (NetOrbit): -1 = not taken over (fixed objects, turrets, story ships, wingmen).
        readonly NetworkVariable<int> specShip = new NetworkVariable<int>(-1, Read, Owner);
        readonly NetworkVariable<byte> specGroup = new NetworkVariable<byte>(0, Read, Owner);
        readonly NetworkVariable<bool> specFreighter = new NetworkVariable<bool>(false, Read, Owner);
        // Hostility toward the other players (NpcShip.aggressors, an always-hostile race), for their markers.
        readonly NetworkVariable<FixedString128Bytes> aggressors = new NetworkVariable<FixedString128Bytes>(default, Read, Owner);
        readonly NetworkVariable<bool> alwaysHostile = new NetworkVariable<bool>(false, Read, Owner);
        readonly NetworkVariable<bool> empDisabled = new NetworkVariable<bool>(false, Read, Owner);
        readonly NetworkVariable<int> junkKind = new NetworkVariable<int>(-1, Read, Owner);   // >= 0: a mission's space junk (CombatAssets.junk)
        readonly NetworkVariable<bool> missionShip = new NetworkVariable<bool>(false, Read, Owner);   // a freelance mission's (NpcShip.MissionShip)
        readonly NetworkVariable<int> roleFlags = new NetworkVariable<int>(0, Read, Owner);    // its mission role (FreelanceOrbit.RoleFlags)
        readonly NetworkVariable<int> specHull = new NetworkVariable<int>(-1, Read, Owner);    // its max hull (a takeover keeps it)
        readonly NetworkVariable<int> eventFlags = new NetworkVariable<int>(0, Read, Owner);   // an event's batch tag << 1 | spawned as an enemy (NetEvents)
        readonly NetworkVariable<ulong> killer = new NetworkVariable<ulong>(ulong.MaxValue, Read, Owner);   // the player who destroyed it (NetEvents' kills)
        readonly NetworkVariable<ulong> creator = new NetworkVariable<ulong>(ulong.MaxValue);  // server-written at spawn: whose ship

        readonly NetSmoothing smoothing = new NetSmoothing();
        NpcShip ship;
        Target junk;   // the owner's space junk (a Junk removal mission), instead of a ship
        GameObject visual;
        Target target;
        byte shownLife = Flying;
        NetShotSender sender;
        NetShotMirror mirror;
        int pendingStation = -1, pendingId = -1;
        bool shown, viewer;
        int lastAggressors;
        EmpSparks sparks;

        public int Station => station.Value;
        /// <summary>Its race (Standing ids; -1 unknown).</summary>
        public int Race => race.Value;
        /// <summary>The player whose game has this ship (the owner at spawn; the host may own it after they left).</summary>
        public ulong Creator => creator.Value;
        /// <summary>Owned here without a ship: its owner left the session and it passed to the host (NetState's sweep).</summary>
        public bool Orphan => IsSpawned && IsOwner && ship == null && junk == null;
        /// <summary>Taken over by this player (NetOrbit): hidden here until the old owner's copy is gone.</summary>
        public bool Adopted { get; private set; }
        /// <summary>A freelance mission's space junk (its id range, NetOrbit.JunkBase; known from the spawn).</summary>
        public bool IsJunk => localId.Value >= NetOrbit.JunkBase;
        public int JunkKind => junkKind.Value;
        public bool IsMissionShip => missionShip.Value;
        /// <summary>A player's hired wingman (their game's NPC).</summary>
        public bool IsWingman => (NpcGroup)specGroup.Value == NpcGroup.Wingman;
        /// <summary>The event batch that spawned it (NetEvents), 0 = none.</summary>
        public int EventTag => eventFlags.Value >> 1;
        /// <summary>The client id of the player who destroyed it (their own game's player, or another player's hit), MaxValue =
        /// none yet or an NPC.</summary>
        public ulong Killer => killer.Value;
        /// <summary>Flying (not dying, dead or gone): an event's living ship.</summary>
        public bool FlyingNow => life.Value == Flying;
        public int RoleFlags => roleFlags.Value;
        public bool IsFlying => life.Value == Flying;
        public string Label => label.Value.ToString();
        bool OtherGame => creator.Value != NetworkManager.LocalClientId;
        /// <summary>A flying traffic ship another player's game ran, to take over (NetOrbit): its spec, pose (Unity) and hull.</summary>
        public bool Adoptable => specShip.Value >= 0 && !missionShip.Value && life.Value == Flying && !Adopted && OtherGame;
        /// <summary>A flying mission ship (or the mining plant, junk) of another game, for a squadmate's takeover (FreelanceOrbit).</summary>
        public bool MissionAdoptable => (missionShip.Value || IsJunk) && life.Value == Flying && !Adopted && OtherGame;
        public SpawnSpec AdoptSpec(Vector3 gamePosition)
        {
            int f = roleFlags.Value;
            return new SpawnSpec
            {
                group = (NpcGroup)specGroup.Value, race = race.Value, ship = specShip.Value, freighter = specFreighter.Value,
                position = gamePosition, hitpoints = specHull.Value,
                alwaysEnemy = (f & FreelanceOrbit.RoleAlwaysEnemy) != 0, alwaysFriend = (f & FreelanceOrbit.RoleAlwaysFriend) != 0,
                stationary = (f & FreelanceOrbit.RoleStationary) != 0, noLoot = (f & FreelanceOrbit.RoleNoLoot) != 0,
                eventTag = eventFlags.Value >> 1,   // an event's ship stays one (NetEvents counts it on)
            };
        }
        public Vector3 WorldPosition => position.Value;
        public Quaternion WorldRotation => rotation.Value;
        public float HullFraction => hull.Value;

        public void MarkAdopted()
        {
            Adopted = true;
            SetShown(false);
        }
        public int LocalId => localId.Value;
        /// <summary>Host: when it was spawned (NetState's sweep waits for its owner's position to arrive).</summary>
        public float SpawnedAt { get; private set; }

        /// <summary>This proxy's ship as a Target here: the owner's NpcShip, else the proxy Target.</summary>
        public Target LocalTarget => IsOwner ? (ship != null ? ship.Target : junk) : target;

        /// <summary>Host, before spawning: the orbit and the owner's ship id (written in OnNetworkSpawn).</summary>
        public void Init(int stationIndex, int id)
        {
            pendingStation = stationIndex;
            pendingId = id;
        }

        public override void OnNetworkSpawn()
        {
            DontDestroyOnLoad(gameObject);
            if (IsServer)
            {
                station.Value = pendingStation;
                localId.Value = pendingId;
                creator.Value = OwnerClientId;
                SpawnedAt = Time.unscaledTime;
            }
            if (IsOwner)
            {
                var orbit = NetOrbit.Current != null && NetOrbit.Current.Station == station.Value ? NetOrbit.Current : null;
                if (orbit != null && localId.Value >= NetOrbit.JunkBase)
                {
                    // A freelance mission's space junk: a one-hit target, always hostile.
                    junk = orbit.Junk(localId.Value);
                    if (junk == null) { DropStale(); return; }
                    orbit.Register(this, junk);
                    junkKind.Value = orbit.JunkKind(localId.Value);
                    position.Value = junk.transform.position;
                    rotation.Value = junk.transform.rotation;
                    race.Value = Standing.Pirate;
                    relation.Value = Hostile;
                    alwaysHostile.Value = true;
                    radius.Value = junk.radius;
                    explosionScale.Value = 0.5f;
                    return;
                }
                ship = orbit != null ? orbit.Ship(localId.Value) : null;
                if (ship == null) { DropStale(); return; }   // the ship went (or the scene changed) before this spawned
                NetOrbit.Current.Register(this, ship);
                var m = ship.Model;
                model.Value = ship.ModelPath ?? "";
                scale.Value = m.lossyScale;
                position.Value = m.position;
                rotation.Value = m.rotation;
                label.Value = ship.Target != null && !string.IsNullOrEmpty(ship.Target.displayName) ? ship.Target.displayName : "";
                explosionScale.Value = ship.IsFixed ? ship.Spec.explosionScale : ship.IsFreighter ? 6f : 1f;   // NpcShip.UpdateDying
                leavesWreck.Value = ship.IsFixed || ship.IsFreighter;
                var spec = ship.Spec;
                bool adoptable = !ship.MissionShip && spec.fixedObject == null && spec.turretAssembly == null && spec.convoyRole == 0 && spec.dockingType == 0
                                 && spec.wantedIndex < 0 && (spec.group == NpcGroup.Local || spec.group == NpcGroup.Raider
                                 || spec.group == NpcGroup.Freighter || spec.group == NpcGroup.Escort);
                // A mission ship keeps its spec too, for a squadmate's takeover of the mission (not fixed objects or turrets).
                bool missionAdoptable = ship.MissionShip && spec.fixedObject == null && spec.turretAssembly == null;
                specShip.Value = adoptable || missionAdoptable ? spec.ship : -1;
                specGroup.Value = (byte)spec.group;
                specFreighter.Value = spec.freighter;
                missionShip.Value = ship.MissionShip;
                specHull.Value = ship.Hp != null ? ship.Hp.maxHull : -1;
                eventFlags.Value = spec.eventTag << 1 | (spec.alwaysEnemy ? 1 : 0);
                SendState();
                sender = new NetShotSender(ShotUpRpc, BlastUpRpc, () => ship != null ? ship.CurrentTarget : null);
                sender.Hook(ship.Guns);
                return;
            }
            InitViewer();
        }

        /// <summary>A copy of another game's ship here: the Target, the smoothing, the shot mirror.</summary>
        void InitViewer()
        {
            if (viewer) return;
            viewer = true;
            position.OnValueChanged += (_, p) => smoothing.Push(p);
            // The spawn's default pose (a client owner's first values follow as a delta) isn't a position.
            if (position.Value != Vector3.zero) smoothing.Push(position.Value);
            mirror = new NetShotMirror(null, () => target);
            target = gameObject.AddComponent<Target>();
            target.isShip = true;
            target.customDeath = true;
            target.maxHp = 100f;
            target.RemoteDamage = (amount, hitVector, byNpc) => DamageUpRpc(amount, hitVector, byNpc);
            target.RemoteEmp = emp => EmpUpRpc(emp);
            // Space junk is lockable after the ships and a far dot, like the owner's own junk (Target.RadarObjects).
            if (IsJunk) { Target.RadarObjects.Add(target); target.plateNameOnly = target.plateNoIcon = true; }
            else Target.NetShips.Add(target);
            SetShown(false);
        }

        /// <summary>The host took it over from a player who left: no ship behind it here, so it shows like anyone else's.</summary>
        public override void OnGainedOwnership()
        {
            if (IsSpawned && ship == null && junk == null) InitViewer();
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
            if (target != null) { Target.NetShips.Remove(target); Target.RadarObjects.Remove(target); }
            sender?.Unhook();
            mirror?.Clear();
            sparks?.Clear();
        }

        /// <summary>The owner's NPC shot, through the server (only the ship's game sends its shots): checked and limited like
        /// a player's (NetPlayer.ShotUpRpc).</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void ShotUpRpc(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, ulong targetId)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Shot(item, position, velocity, up, lifetimeMs, homingDelayMs)) { NetRateLimit.Reject(OwnerClientId, $"an NPC shot of item {item}"); return; }
            ShotRpc(item, position, velocity, up, lifetimeMs, homingDelayMs, targetId);
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        void ShotRpc(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, ulong targetId)
        {
            if (shown) mirror?.Shot(item, position, velocity, up, lifetimeMs, homingDelayMs, NetShots.Resolve(targetId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BlastUpRpc(int item, Vector3 point)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Shot)) return;
            if (!NetGuard.Item(item) || !NetGuard.Position(point)) { NetRateLimit.Reject(OwnerClientId, $"an NPC blast of item {item}"); return; }
            BlastRpc(item, point);
        }

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Server)]
        void BlastRpc(int item, Vector3 point)
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
            ship.Target.Damage(amount, true, hitVector);
            if (ship.Target.Alive) ship.Target.killedByRemote = false;
            if (!ship.Target.Alive) killer.Value = shooter;
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

        void SendState()
        {
            var t = ship.Target;
            if (t == null) return;
            if (race.Value != t.race) race.Value = t.race;
            byte r = t.hostileToPlayer ? Hostile : t.friendToPlayer ? Friend : Neutral;
            if (relation.Value != r) relation.Value = r;
            float h = t.HullFraction;
            if (Mathf.Abs(hull.Value - h) > 0.004f) hull.Value = h;
            if (!Mathf.Approximately(radius.Value, t.radius)) radius.Value = t.radius;
            bool hide = ship.Hidden || ship.RadarHidden;
            if (hidden.Value != hide) hidden.Value = hide;
            bool always = t.race == Standing.Pirate || t.race == Standing.Void || t.race == Standing.Specter;
            if (alwaysHostile.Value != always) alwaysHostile.Value = always;
            if (ship.aggressors.Count != lastAggressors)
            {
                lastAggressors = ship.aggressors.Count;
                aggressors.Value = string.Join(",", ship.aggressors);
            }
            byte l = ship.Current == NpcShip.State.Dying ? Dying : ship.Current == NpcShip.State.Dead || ship.Gone ? Dead : Flying;
            // Destroyed by this game's own player (not an NPC's shot, not another player's: DamageRpc wrote that one).
            if (l != Flying && life.Value == Flying && killer.Value == ulong.MaxValue && !t.killedByNpc && !t.killedByRemote && !t.Alive)
                killer.Value = OwnerClientId;
            if (life.Value != l) life.Value = l;
            int role = ship.MissionShip && NetOrbit.Current != null ? NetOrbit.Current.MissionRole(ship) : 0;
            if (roleFlags.Value != role) roleFlags.Value = role;
            bool emp = ship.Hp != null && ship.Hp.empDisabled && l == Flying;
            if (empDisabled.Value != emp) empDisabled.Value = emp;
        }

        /// <summary>The model arrives after the spawn when the owner is a client (its first values follow as a delta).</summary>
        void BuildVisual()
        {
            if (visual != null || (IsJunk ? junkKind.Value < 0 : model.Value.Length == 0)) return;   // the owner's values first
            GameObject prefab;
            string path = model.Value.ToString();
            int wreck = -1;
            if (junkKind.Value >= 0)
            {
                var assets = CombatAssets.Load();
                prefab = assets != null && assets.junk != null && junkKind.Value < assets.junk.Length ? assets.junk[junkKind.Value] : null;
            }
            else if (path.StartsWith(NpcShip.WreckModelPrefix))
            {
                // A freighter wreck held at its end (NpcShip.ShowWreckAtEnd: the Supernova wrecks with a hidden blueprint).
                var assets = CombatAssets.Load();
                if (!int.TryParse(path.Substring(NpcShip.WreckModelPrefix.Length), out wreck)) wreck = -1;
                prefab = assets != null && assets.wrecks != null && wreck >= 0 && wreck < assets.wrecks.Length ? assets.wrecks[wreck] : null;
            }
            // Only an assembled prefab (the owner's word: no other Resources asset loaded into the others' games).
            else prefab = path.StartsWith(AssembledObject.ResourcesFolder + "/") ? Resources.Load<GameObject>(path) : null;
            visual = prefab != null ? Instantiate(prefab, transform, false) : new GameObject("(no model)");
            if (wreck >= 0) PartAnimation.HoldAllAtEnd(visual);
            visual.transform.SetParent(transform, false);
            var s = scale.Value;   // finite and within reason (the battleship is 2x, a capital ship more), else as modelled
            bool sane = NetGuard.Finite(s) && Mathf.Abs(s.x) <= 100f && Mathf.Abs(s.y) <= 100f && Mathf.Abs(s.z) <= 100f;
            visual.transform.localScale = sane ? s : Vector3.one;
            var asm = visual.GetComponent<AssembledObject>();
            asm?.SetPlayerVariant((GoF2Remake.Data.Settings.NpcPlayerEngines || !asm.HasNpcExhaust) && asm.playerVariantParts != null && asm.playerVariantParts.Length > 0
                                  && asm.playerVariantParts[0] != null);
            name = $"NetProxy {model.Value}";
            visual.SetActive(shown);
        }

        void SetShown(bool on)
        {
            shown = on;
            if (target != null) { target.enabled = on; target.untargetable = !on; }
            if (visual != null) visual.SetActive(on);
            if (!on) { mirror?.Clear(); sparks?.SetEmitting(false); }
            else { smoothing.Snap(); shownLife = life.Value; }   // no explosion for a ship that died before we came
        }

        void ApplyTarget()
        {
            target.race = race.Value;
            // Toward this player: an always-hostile race, a ship this player's squad shot, or one hostile to its owner while
            // the owner is a squadmate; the owner's friends only through the squad too (else neutral).
            var me = NetPlayer.Local;
            var owner = NetSquad.Find(OwnerClientId);
            bool mateOwner = me != null && NetSquad.Same(owner, me);
            bool shotBySquad = false;
            foreach (var id in Aggressors) if (NetSquad.SameClient(id, me)) { shotBySquad = true; break; }
            // ... and this player's own standing toward its race (Standing.IsEnemy / IsFriend), which the owner's AI uses too.
            target.hostileToPlayer = alwaysHostile.Value || shotBySquad || Standing.IsEnemy(race.Value) || (mateOwner && relation.Value == Hostile);
            target.friendToPlayer = !target.hostileToPlayer && Standing.IsFriend(race.Value);
            if (empDisabled.Value && sparks == null) sparks = new EmpSparks(transform);
            sparks?.SetEmitting(empDisabled.Value);
            // Dying / dead (a tumble, a wreck, destroyed junk): not alive here either, so shots pass it like on the owner's.
            target.hp = life.Value == Flying ? Mathf.Max(0.001f, hull.Value) * target.maxHp : 0f;
            target.radius = radius.Value;
            target.untargetable = hidden.Value || life.Value != Flying;
            // A mission ship's name only for the mission's team (its owner and their squad).
            bool team = !missionShip.Value || NetSquad.SameClient(OwnerClientId, NetPlayer.Local);
            target.displayName = IsJunk ? Target.JunkName : label.Value.Length > 0 && team ? label.Value.ToString() : null;
            if (life.Value != shownLife)
            {
                // NpcShip.UpdateDying's end: the explosion (with its sound); a fighter's model goes with it.
                if (life.Value == Dead) Explosion.Spawn(transform.position, explosionScale.Value);
                shownLife = life.Value;
            }
            bool show = !hidden.Value && (life.Value != Dead || leavesWreck.Value);
            if (visual != null && visual.activeSelf != show) visual.SetActive(show);
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (!IsOwner || viewer)
            {
                var local = NetPlayer.Local;
                bool here = local != null && local.InSpace && local.Station == station.Value && !Adopted;
                if (here != shown) SetShown(here);
                if (!shown) return;
                BuildVisual();
                smoothing.Apply(transform, rotation.Value);
                ApplyTarget();
                mirror?.Update(Time.deltaTime * 1000f);
                return;
            }
            if (junk != null)
            {
                if ((position.Value - junk.transform.position).sqrMagnitude > 0.0001f) position.Value = junk.transform.position;
                byte jl = junk.Alive ? Flying : Dead;
                if (life.Value != jl) life.Value = jl;
                float jh = junk.Alive ? 1f : 0f;
                if (hull.Value != jh) hull.Value = jh;
                return;
            }
            if (ship == null) return;
            sender?.Hook(ship.Guns);   // a gun set later (the second slot, a level's SetGun)
            var m = ship.Model;
            if ((position.Value - m.position).sqrMagnitude > 0.0001f) position.Value = m.position;
            if (Quaternion.Angle(rotation.Value, m.rotation) > 0.05f) rotation.Value = m.rotation;
            SendState();
        }
    }
}
