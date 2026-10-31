// NetHooksInstaller.cs
// Fills in what the layers below need from the networking and the event graphs: the game's rules (GoF2.Game: NetSession,
// GameHooks: the session's state, the squad missions, the shared stock, the faction prices, the profile upload, the session
// rules, the event graphs' bar missions and conditions) and the levels (GoF2.Space: SpaceHooks, the shared orbit, the squad,
// the arena, the event graphs' rules, cutscenes and screens). Runs before the first scene, and in the Editor on every load
// (EditorHookInstaller).

using GoF2Remake.Data;
using System.Linq;
using GoF2Remake.Events;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static class NetHooksInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            NetSession.ActiveProvider = () => NetGame.Active;
            NetSession.SessionGameProvider = () => NetGame.SessionGame;
            NetSession.DebugAllowedProvider = () => NetGame.DebugAllowed;
            NetSession.DbProvider = () => NetGame.Db;
            NetSession.LocalIdProvider = () => NetGame.LocalId;
            NetSession.OrbitSeedProvider = NetGame.OrbitSeed;
            NetSession.ServerTimeProvider = () =>
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm.ServerTime.Time : -1;
            };

            GameHooks.SquadAcceptRefusal = NetMissions.AcceptRefusal;
            GameHooks.MissionAccepted = NetMissions.OnAccepted;
            GameHooks.MissionShared = NetMissions.Share;
            GameHooks.CanDeliver = NetMissions.CanDeliver;
            GameHooks.SplitReward = NetMissions.SplitReward;
            GameHooks.MissionFailed = NetMissions.Failed;

            GameHooks.ActiveEventMissionTitle = () => EventMissions.Active?.title;
            GameHooks.Condition = EventRunner.Condition;
            GameHooks.ResetLocalEvents = EventRunner.ResetLocal;
            GameHooks.StartQuest = EventRunner.StartQuest;   // a cruise drive encounter's quest graph (CruiseEncounters)

            GameHooks.StockShared = NetStock.Shared;
            GameHooks.StockEntered = NetStock.OnEnterStation;
            GameHooks.StockItemChanged = (station, item, delta, price) => NetStock.ItemChanged(station, item, delta, price);
            GameHooks.StockShipChanged = NetStock.ShipChanged;
            GameHooks.FactionBuyPrice = NetFactionsClient.BuyPrice;
            GameHooks.FactionSellPrice = NetFactionsClient.SellPrice;
            GameHooks.FactionShipBuyPrice = NetFactionsClient.ShipBuyPrice;
            GameHooks.ProfileUpload = () => NetProfileClient.Upload();
            GameHooks.SharedPirateBoard = () => NetState.Instance != null ? NetState.Instance.PirateBoardJson : null;
            GameHooks.SessionRule = NetRules.TryGet;
            GameHooks.RuleChangedByHost = NetRules.HostChanged;
            GameHooks.SessionKaamoLevel = NetRules.TryGetKaamoLevel;
            GameHooks.KaamoLevelChangedByHost = NetRules.HostKaamoLevelChanged;
            GameHooks.SessionFlightStyle = NetRules.TryGetFlight;
            GameHooks.EventFlightStyle = () => Events.EventFlight.Forced;   // PR #87: the Set Flight Model node, /flightstyle

            InstallSpace();
        }

        static void InstallSpace()
        {
            SpaceHooks.GuestAssembly = NetHangar.GuestAssembly;   // PR #87: guests' customizable ships
            SpaceHooks.GuestShipY = NetHangar.GuestShipY;
            SpaceHooks.GuestBuild = NetHangar.GuestBuild;
            SpaceHooks.PeersProvider = () => NetPlayer.All;
            SpaceHooks.LocalPeerProvider = () => NetPlayer.Local;
            SpaceHooks.ProxiesProvider = () => Object.FindObjectsByType<NetProxy>();
            SpaceHooks.CratesProvider = () => Object.FindObjectsByType<NetCrate>();
            SpaceHooks.SharedOrbitProvider = () => NetGame.Active ? NetOrbit.Current : null;
            SpaceHooks.LevelBuilt = level => { if (NetGame.Active) level.gameObject.AddComponent<NetOrbit>().Setup(level); };
            SpaceHooks.StationBuilt = level => { if (NetGame.Active) level.gameObject.AddComponent<NetHangar>().Setup(level); };
            SpaceHooks.OrbitEmpty = NetState.OrbitEmpty;
            SpaceHooks.OutOfSight = NetOrbit.OutOfSight;
            SpaceHooks.OtherDockedHere = NetHangar.OtherDockedHere;
            SpaceHooks.SiegeWon = station =>
            {
                var s = NetState.Instance;
                if (s != null && s.IsSpawned) s.SiegeWonRpc(station);
            };
            SpaceHooks.PirateSightingDone = (id, killer) => NetState.Instance?.PirateSightingDoneRpc(id, killer);
            SpaceHooks.WreckHacked = (station, name) =>
            {
                if (EventHost.Local) EventRunner.OnWreckHacked(LocalPilot.Instance, station, name);
                else NetState.Instance?.WreckHackedRpc(station, name ?? "");
            };
            SpaceHooks.Notice = NetChat.Notice;
            SpaceHooks.KeysProvider = () => NetChat.Keys;

            SpaceHooks.AddMissionStatus = NetMissions.AddStatus;
            SpaceHooks.RunMissionHere = NetMissions.ShouldRun;
            SpaceHooks.MissionTeamHere = NetMissions.TeamHere;

            SpaceHooks.LocalSquadProvider = () => NetSquad.LocalSquad;
            SpaceHooks.SquadProvider = () => NetSquad.Members().Cast<IOrbitPeer>();
            SpaceHooks.ToggleDistress = NetDistress.Toggle;
            SpaceHooks.HelpSquadmate = client => NetDistress.Help(NetSquad.Find(client));
            SpaceHooks.DistressArrival = NetDistress.ArrivalNear;
            SpaceHooks.TeleportPose = NetTeleport.TakePose;
            SpaceHooks.ArenaProvider = () => NetArenaClient.Current;
            SpaceHooks.InArenaMatch = () => NetArenaClient.InMatch;
            SpaceHooks.ArenaSpawnPose = NetArenaClient.SpawnPose;
            SpaceHooks.ArenaLevelReady = NetArenaClient.OnLevelReady;

            SpaceHooks.EventNoDocking = () => EventRules.NoDocking;
            SpaceHooks.EventNoJumps = () => EventRules.NoJumps;
            SpaceHooks.EventCinematic = () => EventCutscene.Cinematic;
            SpaceHooks.EventInvulnerable = () => EventCutscene.Invulnerable;
            SpaceHooks.EventQuestionOpen = () => EventScreen.QuestionOpen;
            SpaceHooks.EventSceneMusic = () => EventScreen.SceneMusic;
            SpaceHooks.QuietOrbit = EventRunner.QuietOrbit;
            SpaceHooks.RequestEventOffers = EventMissions.RequestOffers;
            SpaceHooks.RespawnDue = EventRespawn.Due;
            SpaceHooks.Respawn = EventRespawn.Go;
            SpaceHooks.ServerShutOff = DedicatedServer.ShutOff;
        }
    }
}
