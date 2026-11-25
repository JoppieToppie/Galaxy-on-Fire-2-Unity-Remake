using GoF2Remake.World;
using Unity.Netcode;

namespace GoF2Remake.Multiplayer
{
    public sealed partial class NetPlayer
    {
        void OnStationValueChanged(int previous, int current) => ValidatePlayerStationAccess();
        void OnPlaceValueChanged(byte previous, byte current) => ValidatePlayerStationAccess();

        void ValidatePlayerStationAccess()
        {
            if (!IsServer || !IsSpawned || Where != Place.Hangar || Station < 0) return;
            NetPlayerStations.ValidateDockedStationAccess(this);
        }

        [Rpc(SendTo.Server)]
        internal void RequestDeathRespawnRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || !IsSpawned || !InSpace || Hull != 0f || NetArena.InMatch(OwnerClientId)) return;
            if (NetState.Instance?.EnsureDeathPenalty(this) != true) return;
            int last = NetProfiles.LastDeathDock(OwnerClientId);
            int destination = NetFactions.DeathRespawnStation(this, last);
            if (destination < 0) { NetState.Instance?.Notify(OwnerClientId, "No permitted respawn station is available."); return; }
            bool protectedDeath = Modding.ModPvpPiracy.Enabled && NetFactions.SecureForBounties(Station, OwnerClientId) && !IsPirate;
            ApproveDeathRespawnRpc(destination, protectedDeath);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ApproveDeathRespawnRpc(int destination, bool protectedDeath)
        {
            if (!IsOwner || !InSpace) return;
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
