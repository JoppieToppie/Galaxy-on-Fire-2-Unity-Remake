// NetHangar.cs
// Multiplayer: the other players docked at the local player's station, in its hangar (StationLevel adds it while a session
// runs; the ships are HangarTraffic's guests). Where each other player is (NetPlayer) decides:
//   docking here from this station's orbit        flies in and parks on a free slot (if they flew in themselves)
//   starting the session here, respawned here     parked at once
//   already docked here when this player arrives  parked at once (so is one who came another way: a load, a new ship)
//   taking off (their own take-off started)        takes off and flies out
//   gone another way (a jump, a load, left)        gone at once
// A player whose ship changes while docked (bought another) gets the new one parked in place of the old; their turret shows.
// The hangar's NPC ships are the same for everyone docked there: the first player there runs them (Running,
// NetPlayer.HangarRun) and sends each landing and take-off (NetState.HangarNpcRpc); a player docking later starts without
// NPC ships and gets the running game's (a snapshot, ApplyNpcSnapshot); when the runner leaves, the player there with the
// lowest client id runs them on from what they have (the others take a new snapshot); two runners at once: the higher
// client id stops. The NPC ships always leave one pad free (HangarTraffic.KeepOneFree).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class NetHangar : MonoBehaviour
    {
        sealed class Seen
        {
            public NetPlayer.Place place;
            public int station, ship;
        }

        StationLevel level;
        readonly Dictionary<ulong, Seen> seen = new Dictionary<ulong, Seen>();
        readonly HashSet<ulong> present = new HashSet<ulong>();
        readonly List<ulong> gone = new List<ulong>();
        bool first = true;

        public void Setup(StationLevel stationLevel)
        {
            level = stationLevel;
            Current = this;
            var traffic = level.Traffic;
            if (traffic == null) return;
            traffic.KeyBase = (int)(NetGame.LocalId + 1) * 1000;
            traffic.NpcLanding += (key, ship, yaw) => Send(true, key, ship, yaw);
            traffic.NpcTakingOff += key => Send(false, key, -1, 0f);
        }

        /// <summary>The local player's hangar, null when not docked in a session.</summary>
        public static NetHangar Current { get; private set; }
        /// <summary>The local player runs their hangar's NPC ships (NetPlayer.HangarRun).</summary>
        public static bool Running => Current != null && Current.level != null && Current.level.Traffic != null && Current.level.Traffic.RunsNpcs;

        void OnDestroy() { if (Current == this) Current = null; }

        /// <summary>StationLevel: another player is docked at 'station' already (their game runs its NPC ships).</summary>
        public static bool OtherDockedHere(int station)
        {
            foreach (var p in NetPlayer.All)
                if (p != null && !p.IsOwner && p.IsSpawned && p.InHangar && p.Station == station) return true;
            return false;
        }

        int Here => level != null && level.Layout != null ? level.Layout.stationIndex : -1;

        void Send(bool landing, int key, int ship, float yaw)
        {
            var state = NetState.Instance;
            if (state != null && state.IsSpawned && Running) state.HangarNpcRpc(Here, landing, key, ship, yaw);
        }

        /// <summary>NetState: the running game's NPC ship lands / takes off.</summary>
        internal void OnRemoteNpc(bool landing, int key, int ship, float yaw)
        {
            var traffic = level != null ? level.Traffic : null;
            if (traffic == null || traffic.RunsNpcs) return;
            if (landing) traffic.RemoteNpcLands(key, ship, yaw); else traffic.RemoteNpcTakesOff(key);
            Debug.Log($"NetHangar: NPC ship {key} {(landing ? $"lands (ship {ship})" : "takes off")}");
        }

        /// <summary>NetState: another player docked here asks for the NPC ships as they are ("key:ship:yaw;...").</summary>
        internal void SendSnapshot(ulong requester)
        {
            var traffic = level != null ? level.Traffic : null;
            var state = NetState.Instance;
            if (traffic == null || !traffic.RunsNpcs || state == null || !state.IsSpawned) return;
            var sb = new System.Text.StringBuilder();
            foreach (var e in traffic.NpcSnapshot())
                sb.Append(e.key).Append(':').Append(e.ship).Append(':').Append(e.yaw.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            state.HangarSnapshotRpc(requester, sb.ToString());
        }

        /// <summary>NetState: the running game's NPC ships.</summary>
        internal void OnSnapshot(string data)
        {
            var traffic = level != null ? level.Traffic : null;
            if (traffic == null || traffic.RunsNpcs) return;
            var list = new List<(int, int, float)>();
            foreach (var one in (data ?? "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var f = one.Split(':');
                if (f.Length == 3 && int.TryParse(f[0], out int key) && int.TryParse(f[1], out int ship)
                    && float.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float yaw))
                    list.Add((key, ship, yaw));
            }
            traffic.ApplyNpcSnapshot(list);
            snapshotFrom = runner;
            Debug.Log($"NetHangar: the hangar's NPC ships from player {runner}: {data}");
        }

        ulong runner = ulong.MaxValue, snapshotFrom = ulong.MaxValue;
        float runnerCheckMs, sinceDockMs;

        /// <summary>Who runs the NPC ships here: the lowest client id among the runners docked here (or none).</summary>
        void UpdateRunner(HangarTraffic traffic, float dtMs)
        {
            sinceDockMs += dtMs;
            if ((runnerCheckMs -= dtMs) > 0f) return;
            runnerCheckMs = 500f;
            ulong me = NetGame.LocalId, best = ulong.MaxValue;
            foreach (var p in NetPlayer.All)
                if (p != null && !p.IsOwner && p.IsSpawned && p.Where == NetPlayer.Place.Hangar && p.Station == Here && p.HangarRun && p.OwnerClientId < best)
                    best = p.OwnerClientId;
            var state = NetState.Instance;
            if (traffic.RunsNpcs)
            {
                // Two runners (docked at the same moment): the higher client id stops and takes the other's.
                if (best < me) { traffic.RunsNpcs = false; runner = best; snapshotFrom = ulong.MaxValue; }
                return;
            }
            if (best == ulong.MaxValue)
            {
                // Nobody runs them here any more (the runner left): this game does, from what it has (after the others' values
                // had time to arrive).
                if (sinceDockMs > 2000f) traffic.RunsNpcs = true;
                return;
            }
            runner = best;
            if (snapshotFrom != runner && state != null && state.IsSpawned && (snapshotAskMs -= 500f) <= 0f)
            {
                snapshotAskMs = 2000f;   // asked again if no answer came
                state.HangarSnapshotRequestRpc(Here);
            }
        }

        float snapshotAskMs;

        // The turret each guest's ship shows (rebuilt with another ship or turret).
        readonly Dictionary<ulong, (GameObject ship, int item, GameObject turret)> turrets = new Dictionary<ulong, (GameObject, int, GameObject)>();

        void ShowTurret(ulong id, GameObject shipGo, NetPlayer p)
        {
            turrets.TryGetValue(id, out var t);
            int want = shipGo != null ? NetPlayer.PackTurrets(p.TurretItems) : -1;   // both turrets in one key
            if (t.ship == shipGo && t.item == want && (want < 0 || t.turret != null)) return;
            if (t.turret != null) Destroy(t.turret);
            var turret = shipGo != null && want >= 0
                ? GoF2Remake.Flight.PlayerTurret.BuildStatic(NetGame.Db, p.ShipIndex, NetPlayer.TurretStacks(want), shipGo.transform) : null;
            turrets[id] = (shipGo, want, turret);
        }

        void Update()
        {
            var traffic = level != null ? level.Traffic : null;
            if (traffic == null || level.Layout == null) return;
            UpdateRunner(traffic, Time.unscaledDeltaTime * 1000f);
            int here = level.Layout.stationIndex;
            present.Clear();
            foreach (var p in NetPlayer.All)
            {
                if (p == null || p.IsOwner || !p.IsSpawned) continue;
                ulong id = p.OwnerClientId;
                present.Add(id);
                bool known = seen.TryGetValue(id, out var before);
                bool dockedHere = p.InHangar && p.Station == here;
                if (dockedHere)
                {
                    if (known && before.ship != p.ShipIndex && traffic.HasGuest((long)id)) traffic.GuestLeaves((long)id, false);   // another ship
                    if (p.Where == NetPlayer.Place.Hangar && !traffic.HasGuest((long)id))
                    {
                        // Flies in only when seen docking from this orbit (not on this player's own arrival).
                        // (Not a player who just appears: a session start, a respawn at the station.)
                        bool fly = !first && known && before.place == NetPlayer.Place.Space && before.station == here && p.ArrivedFlying;
                        traffic.GuestArrives((long)id, p.ShipIndex, fly);
                    }
                    else if (p.Where == NetPlayer.Place.Departing) traffic.GuestLeaves((long)id, true);
                }
                else if (traffic.HasGuest((long)id))
                {
                    // Launched straight into this orbit (their flights off): still a take-off here; else just gone.
                    bool launched = p.InSpace && p.Station == here;
                    traffic.GuestLeaves((long)id, launched);
                }
                ShowTurret(id, dockedHere ? traffic.GuestShip((long)id) : null, p);
                if (!known) seen[id] = before = new Seen();
                before.place = p.Where;
                before.station = p.Station;
                before.ship = p.ShipIndex;
            }
            // Players who left the session.
            gone.Clear();
            foreach (var id in seen.Keys) if (!present.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                traffic.GuestLeaves((long)id, false);
                seen.Remove(id);
            }
            first = false;
        }
    }
}
