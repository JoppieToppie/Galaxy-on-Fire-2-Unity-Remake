using GoF2Remake.World;
using Unity.Netcode;

namespace GoF2Remake.Multiplayer
{
    public sealed partial class NetPlayer
    {
        int deathRespawnDestination = -1, deathOrigin = -1;
        bool deathRespawnProtected, deathRecorded, deathPenaltyRequired;
        internal bool HasRecordedDeath => deathRecorded;
        internal bool RecordedDeathRequiresPenalty => deathPenaltyRequired;
        float deathRespawnArrivalAt;
        internal int DeathRespawnDestination => deathRespawnDestination;
        internal int DeathOrigin => deathRecorded ? deathOrigin : Station;
        internal void SetRecordedDeathDestination(int orbit)
        {
            if (IsServer && deathRecorded) deathRespawnDestination = orbit;
        }

        internal void RecordDeathRespawn(int origin)
        {
            if (!IsServer || deathRecorded || NetState.Instance?.ProfilesOn != true ||
                NetArena.InMatch(OwnerClientId) || Hull != 0f ||
                (!HasCurrentWreckClaimPose && NetState.Instance.HasWitnessedFlight(this) != true)) return;
            deathRecorded = true;
            deathOrigin = origin;
            deathPenaltyRequired = NetFactions.DropsEquipmentOnDeath(origin, IsPirate, OwnerClientId);
            deathRespawnDestination = NetFactions.DeathRespawnStation(this, NetProfiles.LastDeathDock(OwnerClientId), origin);
            deathRespawnProtected = Modding.ModPvpPiracy.Enabled &&
                NetFactions.SecureForBounties(origin, OwnerClientId) && !IsPirate;
        }

        void OnStationValueChanged(int previous, int current)
        {
            if (IsServer && InSpace && Hull == 0f) RecordDeathRespawn(previous);
            ValidatePlayerStationAccess();
        }

        void OnPlaceValueChanged(byte previous, byte current)
        {
            if (IsServer && previous == (byte)Place.Space && current != (byte)Place.Space && (deathRecorded || Hull == 0f) &&
                NetState.Instance?.ProfilesOn == true && !NetArena.InMatch(OwnerClientId))
            {
                RecordDeathRespawn(Station);
                if (deathRecorded && NetState.Instance.EnsureDeathPenalty(this) != true && !IsOwner)
                {
                    NetGame.Kick(OwnerClientId, "Your death penalty must be saved before respawning.");
                    return;
                }
                deathRespawnArrivalAt = UnityEngine.Time.unscaledTime + 0.5f;
            }
            ValidatePlayerStationAccess();
        }

        void ValidateDeathRespawnArrival()
        {
            if (!IsServer || !deathRecorded || !InHangar || UnityEngine.Time.unscaledTime < deathRespawnArrivalAt) return;
            if (NetState.Instance?.EnsureDeathPenalty(this) != true)
            {
                deathRespawnArrivalAt = UnityEngine.Time.unscaledTime + 2f;
                if (!IsOwner) NetGame.Kick(OwnerClientId, "Your death penalty must be saved before respawning.");
                return;
            }
            if (deathRespawnDestination >= 0 && Station == deathRespawnDestination)
            {
                deathRecorded = deathPenaltyRequired = false;
                deathOrigin = deathRespawnDestination = -1;
                NetState.Instance?.FinishRecordedDeath(this);
                return;
            }
            if (deathRespawnDestination < 0)
            {
                if (!IsOwner) NetGame.Kick(OwnerClientId, "No permitted death respawn station is available.");
                return;
            }
            deathRespawnArrivalAt = UnityEngine.Time.unscaledTime + 2f;
            ApproveDeathRespawnRpc(deathRespawnDestination, deathRespawnProtected);
        }

        void ValidatePlayerStationAccess()
        {
            if (!IsServer || !IsSpawned || Where != Place.Hangar || Station < 0) return;
            NetPlayerStations.ValidateDockedStationAccess(this);
        }

        [Rpc(SendTo.Server)]
        internal void RequestDeathRespawnRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || !IsSpawned || !InSpace || (!deathRecorded && Hull != 0f) || NetArena.InMatch(OwnerClientId)) return;
            RecordDeathRespawn(Station);
            if (!deathRecorded || NetState.Instance?.EnsureDeathPenalty(this) != true) return;
            if (deathRespawnDestination < 0) { NetState.Instance?.Notify(OwnerClientId, "No permitted respawn station is available."); return; }
            ApproveDeathRespawnRpc(deathRespawnDestination, deathRespawnProtected);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ApproveDeathRespawnRpc(int destination, bool protectedDeath)
        {
            if (!IsOwner || (InHangar && GoF2Remake.Data.Session.StationIndex == destination)) return;
            GoF2Remake.Data.Session.StationIndex = destination;
            GoF2Remake.Data.Session.DockedFromSpace = false;
            GoF2Remake.Data.Session.SkipRespawnDockingFine = protectedDeath;
            UnityEngine.SceneManagement.SceneManager.LoadScene("Station");
        }

        internal void ForceStationLaunch(string reason)
        {
            if (!IsServer || !IsSpawned) return;
            ForceStationLaunchRpc(reason ?? "You no longer have permission to remain at this station.");
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ForceStationLaunchRpc(string reason)
        {
            if (!IsOwner) return;
            if (dock == null) dock = FindAnyObjectByType<StationLevel>();
            if (dock != null && InHangar) dock.Launch();
            NetChat.Notice(reason ?? "You have been removed from this station.");
        }
    }

    public static partial class NetPlayerStations
    {
        static string StationBlacklistRefusal(PlayerStationRecord record, string account, string fallback)
        {
            if (record == null || record.offeredForClaimUtc > 0 || IsStationOwnerAccount(record, account)) return fallback;
            var ban = FindEffectiveBan(record, account, PlayerStationBanSource.OriginalBuilder);
            if (ban == null && !HasBuilderRights(record, account))
                ban = FindEffectiveBan(record, account, PlayerStationBanSource.OwnerAuthority);
            if (ban != null) return WithBlacklistReason("Access denied: " + (ban.faction ? "your faction is" : "you are") + " blacklisted at " + record.name + ".", ban.reason);
            var permission = FindPermissionOverride(record, account, PlayerStationPermission.Visit);
            if (permission?.builderState == PlayerStationPermissionState.Deny)
                return WithBlacklistReason(fallback, permission.builderReason);
            if (permission?.state == PlayerStationPermissionState.Deny && permission.builderState != PlayerStationPermissionState.Allow)
                return WithBlacklistReason(fallback, permission.reason);
            return FacilityRaceRefusal(record, account, "docking", fallback);
        }

        internal static void ValidateDockedStationAccess(NetPlayer player)
        {
            if (!NetGame.IsServer || player == null || !player.IsSpawned ||
                player.Where != NetPlayer.Place.Hangar || player.Station < 0) return;
            string account = NetProfiles.AccountOf(player.OwnerClientId);
            if (!CanVisitStation(player.Station, account))
                player.ForceStationLaunch(StationBlacklistRefusal(StationAt(player.Station), account, "You do not have permission to visit this station."));
        }

        internal static void RefreshAccessForFactionChange()
        {
            if (!NetGame.IsServer || registry?.stations == null) return;
            foreach (var record in registry.stations) EjectDeniedVisitors(record);
        }

        static void EjectDeniedVisitors(PlayerStationRecord record)
        {
            if (!NetGame.IsServer || record == null) return;
            foreach (var player in NetPlayer.All)
            {
                if (player == null || !player.IsSpawned || player.Where != NetPlayer.Place.Hangar ||
                    player.Station != record.stationIndex) continue;
                string account = NetProfiles.AccountOf(player.OwnerClientId);
                if (!CanVisitStation(record.stationIndex, account))
                    player.ForceStationLaunch(StationBlacklistRefusal(record, account, "Your permission to visit " + record.name + " has been revoked."));
            }
        }
    }
}
