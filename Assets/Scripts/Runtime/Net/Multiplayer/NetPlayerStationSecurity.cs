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
        internal static void ValidateDockedStationAccess(NetPlayer player)
        {
            if (!NetGame.IsServer || player == null || !player.IsSpawned ||
                player.Where != NetPlayer.Place.Hangar || player.Station < 0) return;
            string account = NetProfiles.AccountOf(player.OwnerClientId);
            if (!CanVisitStation(player.Station, account))
                player.ForceStationLaunch("You do not have permission to visit this station.");
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
                    player.ForceStationLaunch("Your permission to visit " + record.name + " has been revoked.");
            }
        }
    }
}
