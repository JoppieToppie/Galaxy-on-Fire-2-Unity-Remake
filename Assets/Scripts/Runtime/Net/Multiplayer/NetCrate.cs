// NetCrate.cs
// Multiplayer: an orbit authority's crate (a destroyed ship's container) shown to the other players in that orbit (NetOrbit
// asks NetState to spawn one per crate; the host spawns it owned by that player). For the others it is a real Crate (the
// race's container model, the same loot, `remote`: no drift or expiry of its own) while they are in that orbit, so their
// radar marks it and their tractor beam locks and pulls it; it follows the owner's crate until their own beam pulls it.
// One player gets it: the host keeps the claim (the first player whose beam starts pulling; Crate.PullStarted). The others'
// crates are claimedByOther (their radar leaves it, a beam already on it lets go), and the claimant's capture waits until
// the claim is confirmed (captureBlocked). Another player's capture destroys the owner's crate (and so everyone's); the
// owner's own capture or the crate's expiry despawns it (NetOrbit). A claimant who leaves loses the claim. Only a player
// flying in the crate's orbit may claim it (NetGuard), the claims are rate limited (NetRateLimit), the copies take only
// items that exist from the owner's loot, and only the server may tell the owner to remove it.
// Only the players in its orbit have it (NetInterest); its pose travels in the pose stream (NetPoses), its look, loot and
// age in one value (data). When its game leaves the orbit the server hands it to the orbit's runner like the ships
// (NetState.HandOver): the runner's copy becomes its real crate, with the age it had (OnGainedOwnership), so it neither
// vanishes nor starts its 60 s over.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed class NetCrate : NetworkBehaviour, World.IOrbitCrate, INetPosed
    {
        const ulong NoClaim = ulong.MaxValue;

        static readonly NetworkVariableReadPermission Read = NetworkVariableReadPermission.Everyone;
        static readonly NetworkVariableWritePermission Owner = NetworkVariableWritePermission.Owner;

        readonly NetworkVariable<int> station = new NetworkVariable<int>(-1);   // server-written at spawn
        readonly NetworkVariable<int> localId = new NetworkVariable<int>(-1);
        readonly NetworkVariable<ulong> claimant = new NetworkVariable<ulong>(NoClaim);   // server-written
        // "race|look|flags|born|loot": Crate.race, Crate.look (-1 = not written yet), fromFriend 1 / missionCrate 2 /
        // missionLoot 4 (only the owner's squad takes it), the server time it was made (s), "item:amount,...".
        readonly NetworkVariable<FixedString512Bytes> data = new NetworkVariable<FixedString512Bytes>(default, Read, Owner);
        string dataRead;
        int race = -1, look = -1;
        bool fromFriend, missionCrate, missionLoot;
        double born;
        string lootText = "";
        Vector3 posePosition;
        Quaternion poseRotation = Quaternion.identity;
        bool hasPose;
        readonly NetPoses.SendState poseSend = new NetPoses.SendState();

        readonly NetSmoothing smoothing = new NetSmoothing();
        Crate crate;          // the owner: the real crate; the others: the copy (while in the orbit)
        bool reported, wasPulling, adopted;

        /// <summary>A Recovery / Salvage container (a squadmate's takeover of the mission builds its own, FreelanceOrbit).</summary>
        public bool IsMissionCrate { get { ReadData(); return missionCrate; } }
        /// <summary>Its pose (Unity): the owner's crate, else the stream's last.</summary>
        public Vector3 WorldPosition => IsOwner && crate != null ? crate.transform.position : posePosition;

        void ReadData()
        {
            string text = data.Value.ToString();
            if (text == dataRead) return;
            dataRead = text;
            var parts = text.Split('|');
            if (parts.Length < 5) return;
            int.TryParse(parts[0], out race);
            int.TryParse(parts[1], out look);
            int.TryParse(parts[2], out int flags);
            double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out born);
            fromFriend = (flags & 1) != 0;
            missionCrate = (flags & 2) != 0;
            missionLoot = (flags & 4) != 0;
            lootText = parts[4];
        }

        static double ServerNow => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

        void WriteData()
        {
            int flags = (crate.fromFriend ? 1 : 0) | (crate.missionCrate ? 2 : 0) | (crate.missionLoot ? 4 : 0);
            double madeAt = ServerNow - crate.AgeMs / 1000f;
            string text = $"{crate.race}|{crate.look}|{flags}|{madeAt.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}|{Encode(crate.loot)}";
            if (data.Value.ToString() != text) data.Value = text;
        }

        // ---- the pose stream (NetPoses) -------------------------------------------------------------------------------

        public NetPoses.SendState PoseSend => poseSend;
        public int PoseStation => station.Value;

        public bool CurrentPose(out Vector3 p, out Quaternion r, out bool hasLook, out PoseLook shipLook)
        {
            hasLook = false;
            shipLook = default;
            p = crate != null ? crate.transform.position : default;
            r = crate != null ? crate.transform.rotation : Quaternion.identity;
            return crate != null;
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

        /// <summary>Owned here through a handover (its game left the orbit, NetState.HandOver) or a disconnect (the host
        /// holds it): the copy here becomes the real crate, as old as it was there (its expiry runs on), and NetOrbit keeps it
        /// like its own. Not in its orbit: nothing (the server hands it on to someone there).</summary>
        void TakeOverCopy()
        {
            if (NetOrbit.Current == null || NetOrbit.Current.Station != station.Value) return;
            ReadData();
            crate.remote = false;
            crate.AgeMs = Mathf.Max(0f, (float)(ServerNow - born) * 1000f);
            NetOrbit.Current.AdoptCrate(this, crate);
            poseSend.sent = false;
        }

        public override void OnGainedOwnership() => ownedSince = Time.unscaledTime;

        /// <summary>Handed to another game while this one still had it (a place that flickered): a copy again.</summary>
        public override void OnLostOwnership()
        {
            if (crate == null || crate.remote) return;
            crate.remote = true;
            NetOrbit.Current?.ForgetCrate(crate);
        }

        float ownedSince = -1f;
        bool despawnAsked;

        /// <summary>Taken over by this player (their own crate now stands in for it): no copy here any more.</summary>
        public void MarkAdopted()
        {
            adopted = true;
            if (crate != null && !IsOwner) Destroy(crate.gameObject);
            crate = null;
        }

        /// <summary>Taken over by this game (FreelanceOrbit's promotion): gone here and in the other games.</summary>
        public void Adopt()
        {
            MarkAdopted();
            NetState.Instance?.AdoptedRpc(NetworkObjectId);
        }

        /// <summary>NetState: a squadmate took the mission over and built its own container: the owner's goes.</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void TakenOverRpc()
        {
            if (crate != null) Destroy(crate.gameObject);
        }
        int pendingStation = -1, pendingId = -1;

        public int Station => station.Value;
        /// <summary>Host: when it was spawned (NetState's sweep waits for its owner's position to arrive).</summary>
        public float SpawnedAt { get; private set; }

        /// <summary>Host, before spawning: the orbit and the owner's crate id (written in OnNetworkSpawn).</summary>
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
                SpawnedAt = Time.unscaledTime;
            }
            if (!IsOwner) return;
            crate = NetOrbit.Current != null && NetOrbit.Current.Station == station.Value ? NetOrbit.Current.Crate(localId.Value) : null;
            if (crate == null)
            {
                // Captured or gone before this spawned (NetOrbit had dropped its id already): nothing to show.
                var state = NetState.Instance;
                if (state != null && state.IsSpawned) state.DespawnRpc(NetworkObjectId);
                return;
            }
            NetOrbit.Current.Register(this, crate);
            WriteData();
            crate.PullStarted = () => ClaimRpc();
            if (crate.pulled) ClaimRpc();   // the owner's beam (an auto tractor) had it before the spawn
        }

        public override void OnNetworkDespawn()
        {
            NetPoses.Unregister(this);
            if (!IsOwner && crate != null) Destroy(crate.gameObject);
        }

        /// <summary>A beam started pulling it: the claim, if nobody has it yet.</summary>
        [Rpc(SendTo.Server)]
        void ClaimRpc(RpcParams rpc = default)
        {
            ulong who = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(who, NetRateLimit.Kind.Claim)) return;
            // Only a player flying in the crate's orbit (a claim from elsewhere would hold every crate of the session back).
            if (!NetGuard.InOrbit(who, station.Value)) return;
            // Another player's mission loot: only the mission's team (the owner's squad) may claim it.
            ReadData();
            if (missionLoot && !NetSquad.SameClient(who, NetSquad.Find(OwnerClientId))) return;
            if (claimant.Value == NoClaim) claimant.Value = who;
        }

        /// <summary>The claimant's beam let go (or they left the orbit): anyone may take it again.</summary>
        [Rpc(SendTo.Server)]
        void ReleaseRpc(RpcParams rpc = default)
        {
            ulong who = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(who, NetRateLimit.Kind.Claim) && claimant.Value == who) claimant.Value = NoClaim;
        }

        /// <summary>The claimant's capture: the owner's crate is gone (and with it everyone's).</summary>
        [Rpc(SendTo.Server)]
        void CapturedRpc(RpcParams rpc = default)
        {
            ulong who = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(who, NetRateLimit.Kind.Claim) && claimant.Value == who) RemoveRpc();
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void RemoveRpc()
        {
            if (crate != null) Destroy(crate.gameObject);
        }

        /// <summary>A copy for this player (in the crate's orbit): built once the owner's values are here.</summary>
        void BuildCopy()
        {
            ReadData();
            if (crate != null || look < 0 || !hasPose) return;   // the owner's values and a pose first
            var assets = CombatAssets.Load();
            var prefab = assets != null ? assets.CrateModel(look, race) : null;
            var go = prefab != null ? Instantiate(prefab, posePosition, poseRotation) : new GameObject();
            go.name = "Crate (other player's)";
            crate = go.AddComponent<Crate>();
            crate.remote = true;
            crate.race = race;
            crate.look = look;
            crate.fromFriend = fromFriend;
            crate.missionCrate = missionCrate;
            crate.missionLoot = missionLoot;
            foreach (var s in Decode(lootText)) crate.loot.Add(s);
            crate.PullStarted = () => ClaimRpc();
            crate.CapturedHere = () =>
            {
                // Captured here (CombatRadar): the owner removes its crate, once.
                if (reported || claimant.Value != NetworkManager.LocalClientId) return;
                reported = true;
                CapturedRpc();
            };
            smoothing.Snap();
            ApplyClaim(NetworkManager.LocalClientId);   // the claim rules from the first frame (an auto tractor grabs at once)
        }

        void Update()
        {
            if (!IsSpawned) return;
            ulong me = NetworkManager.LocalClientId;
            // A claimant who left (the session or the crate's orbit: docked, jumped, dead) loses the claim.
            if (IsServer && claimant.Value != NoClaim && !NetOrbit.InOrbit(claimant.Value, station.Value)) claimant.Value = NoClaim;
            if (IsOwner)
            {
                if (crate != null && crate.remote) TakeOverCopy();
                if (crate == null || crate.remote)
                {
                    // Handed to this game without a copy here (taken a moment before): gone.
                    bool hereNow = NetOrbit.Current != null && NetOrbit.Current.Station == station.Value;
                    if (crate == null && hereNow && ownedSince >= 0f && Time.unscaledTime - ownedSince > 2f && !despawnAsked
                        && NetState.Instance != null && NetState.Instance.IsSpawned)
                    {
                        despawnAsked = true;
                        NetState.Instance.DespawnRpc(NetworkObjectId);
                    }
                    return;   // (its own crate gone: NetOrbit asks for the despawn)
                }
                ApplyClaim(me);
                TrackPull(me);
                // Its loot when a beam took part of it (a steal crate, several kinds): the others' copies follow.
                if ((lootCheck -= Time.unscaledDeltaTime) <= 0f) { lootCheck = 1f; WriteData(); }
                return;
            }
            var local = NetPlayer.Local;
            bool here = local != null && local.InSpace && local.Station == station.Value;
            if (!here || adopted)
            {
                // Not in its orbit (any more): no copy (built again on coming back while it still exists), no claim.
                if (crate != null) Destroy(crate.gameObject);
                crate = null;
                if (wasPulling || claimant.Value == me) { wasPulling = false; if (claimant.Value == me) ReleaseRpc(); }
                return;
            }
            if (crate == null)
            {
                // The copy is gone (a capture reported itself, CapturedHere) or never built.
                if (!reported) BuildCopy();
                return;
            }
            ApplyClaim(me);
            TrackPull(me);
            if (!crate.pulled)
            {
                smoothing.Apply(crate.transform, poseRotation);
            }
        }

        float lootCheck;

        void ApplyClaim(ulong me)
        {
            // Another player's mission loot is out of reach: only the owner's squad (the mission's team) takes it.
            bool outsider = crate.missionLoot && !NetSquad.SameClient(OwnerClientId, NetPlayer.Local);
            bool otherClaims = claimant.Value != NoClaim && claimant.Value != me;
            crate.claimedByOther = outsider || otherClaims;
            // The owner's own crate: only another player's claim holds it back (its capture needs no confirmation).
            crate.captureBlocked = outsider || (IsOwner ? otherClaims : claimant.Value != me);
        }

        /// <summary>This player's beam let go of it without capturing it: the claim goes back.</summary>
        void TrackPull(ulong me)
        {
            bool pulling = crate.pulled;
            if (wasPulling && !pulling && claimant.Value == me) ReleaseRpc();
            wasPulling = pulling;
        }

        static string Encode(List<ItemStack> stacks)
        {
            var sb = new StringBuilder();
            foreach (var s in stacks)
            {
                if (s.amount <= 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(s.item).Append(':').Append(s.amount);
                if (sb.Length > 480) break;
            }
            return sb.ToString();
        }

        /// <summary>The owner's loot (its own game's word): only items that exist, at most MaxLootAmount each (a modified owner
        /// can't hand out unknown item ids that break the taker's cargo code).</summary>
        static IEnumerable<ItemStack> Decode(string text)
        {
            foreach (var part in text.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int item) && int.TryParse(kv[1], out int amount) && amount > 0 && NetGuard.Item(item))
                    yield return new ItemStack(item, Mathf.Min(amount, MaxLootAmount));
            }
        }

        const int MaxLootAmount = 1000;
    }
}
