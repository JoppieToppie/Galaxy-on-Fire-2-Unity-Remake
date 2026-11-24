// NetHooksInstaller.cs
// Fills in what the layers below need from the networking and the event graphs: the game's rules (GoF2.Game: NetSession,
// GameHooks: the session's state, the squad missions, the shared stock, the faction prices, the profile upload, the session
// rules, the event graphs' bar missions and conditions) and the levels (GoF2.Space: SpaceHooks, the shared orbit, the squad,
// the arena, the event graphs' rules, cutscenes and screens). Runs before the first scene, and in the Editor on every load
// (EditorHookInstaller).

using GoF2Remake.Data;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GoF2Remake.Events;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static class NetHooksInstaller
    {
        static readonly ConditionalWeakTable<IReadOnlyList<PlayerStationMaterial>, (int item, int amount)[]> wreckSalvage =
            new ConditionalWeakTable<IReadOnlyList<PlayerStationMaterial>, (int, int)[]>();

        static IReadOnlyList<(int item, int amount)> WreckSalvageAt(int orbit, bool jumpgate)
        {
            var materials = jumpgate ? NetPlayerStations.JumpgateSalvageAt(orbit) : NetPlayerStations.StationSalvageAt(orbit);
            return materials == null ? null : wreckSalvage.GetValue(materials,
                entries => entries.Where(entry => entry != null && entry.required > 0).Select(entry => (entry.item, entry.required)).ToArray());
        }

        static void ChatToScripts(NetChat.Message m) => Modding.ModScripts.Raise("chat", m.from, m.text, m.own);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            NetSession.ActiveProvider = () => NetGame.Active;
            NetSession.SessionGameProvider = () => NetGame.SessionGame;
            NetSession.HostingProvider = () => NetGame.IsServer;
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
            GameHooks.Number = EventRunner.Number;
            GameHooks.ResetLocalEvents = EventRunner.ResetLocal;
            GameHooks.StartQuest = EventRunner.StartQuest;   // a cruise drive encounter's quest graph (CruiseEncounters)
            // Mod scripts' game.send: to every player's scripts through the host; without a session this game's own.
            GameHooks.ScriptSend = (name, payload) =>
            {
                if (!NetGame.Active) { Modding.ModScripts.Receive(0, name, payload); return true; }
                if (NetState.Instance == null || !NetState.Instance.IsSpawned) return false;
                NetState.Instance.ScriptMessageRpc(name, payload);
                return true;
            };
            // ... and "chat": a chat line arrived (who, the text, whether it is this player's own).
            NetChat.Added -= ChatToScripts;
            NetChat.Added += ChatToScripts;
            // Mod scripts' game.command: "name args" as the event graphs' command lines run (single player or the server).
            GameHooks.RunCommand = line =>
            {
                line = (line ?? "").Trim();
                int space = line.IndexOf(' ');
                string name = space < 0 ? line : line.Substring(0, space);
                return NetCommands.RunOnServer(name, space < 0 ? "" : line.Substring(space + 1), null)
                    ?? string.Format(Localization.Extra("scriptNoCommand", "No command \"{0}\"."), name);
            };

            GameHooks.StockShared = NetStock.Shared;
            GameHooks.StockEntered = NetStock.OnEnterStation;
            GameHooks.StockItemChanged = (station, item, delta, price) => NetStock.ItemChanged(station, item, delta, price);
            GameHooks.StockShipChanged = NetStock.ShipChanged;
            GameHooks.FactionBuyPrice = NetFactionsClient.BuyPrice;
            GameHooks.FactionSellPrice = NetFactionsClient.SellPrice;
            GameHooks.FactionShipBuyPrice = NetFactionsClient.ShipBuyPrice;
            GameHooks.ProfileUpload = () => NetProfileClient.Upload();
            GameHooks.CanResolveModJump = () => NetProfileClient.CanResolveModJump;
            GameHooks.ResolveModJump = NetProfileClient.RequestModJump;
            GameHooks.SharedPirateBoard = () => NetState.Instance != null ? NetState.Instance.PirateBoardJson : null;
            GameHooks.SessionRule = NetRules.TryGet;
            GameHooks.RuleChangedByHost = NetRules.HostChanged;
            GameHooks.SessionKaamoLevel = NetRules.TryGetKaamoLevel;
            GameHooks.KaamoLevelChangedByHost = NetRules.HostKaamoLevelChanged;
            GameHooks.SessionFlightStyle = NetRules.TryGetFlight;
            // Single-player uses static reciprocal routes. Multiplayer also considers live
            // player-built gates and their construction, EMP and destruction state.
            GameHooks.JumpgateLinkOperational = (from, to) => !NetGame.Active ||
                NetPlayerStations.JumpgateLinkOperational(from, to);
            GameHooks.EventFlightStyle = () => Events.EventFlight.Forced;   // PR #87: the Set Flight Model node, /flightstyle

            // The co-op campaign (NetCoop; CoopCampaign).
            GameHooks.CoopActive = () => NetCoop.Active;
            GameHooks.CoopLeader = () => NetCoop.Leader;
            GameHooks.CoopCrewSize = () => NetCoop.CrewSize;
            GameHooks.CoopStepWon = NetCoop.ReportWon;
            GameHooks.CoopAdvanced = NetCoop.ReportAdvanced;
            GameHooks.CoopProgress = NetCoop.ReportProgress;
            GameHooks.CoopPublish = NetCoop.Publish;
            GameHooks.CoopFreelanceDone = NetCoop.ReportFreelance;

            GameHooks.ApplyPlayerStations = NetPlayerStations.Apply;
            GameHooks.IsPlayerStation = NetPlayerStations.IsPlayerStation;
            GameHooks.StationSceneryPresent = NetPlayerStations.StationSceneryPresent;
            GameHooks.StationClosedByCombat = NetPlayerStations.StationClosedByCombat;
            GameHooks.HasDryDockAtStation = NetPlayerStations.HasDryDockAtStation;
            GameHooks.HasPlayerJumpgate = NetPlayerStations.HasJumpgateAtStation;
            GameHooks.HasPlayerJumpgateWreck = NetPlayerStations.HasJumpgateWreckAtStation;
            GameHooks.PlayerStationModel = NetPlayerStations.LooksLike;
            GameHooks.PlayerJumpgateDestination = NetPlayerStations.JumpgateDestinationStation;
            GameHooks.PlayerJumpgateModelPreset = orbit => NetPlayerStations.FindJumpgate(orbit)?.modelPreset ?? 0;
            GameHooks.PlayerJumpgateModelReference = orbit => NetPlayerStations.FindJumpgate(orbit)?.modelReference;
            GameHooks.ResolvePlayerJumpgateModel = NetPlayerStations.ResolveJumpgateModel;

            InstallSpace();
        }

        static void InstallSpace()
        {
            SpaceHooks.PlayerStationPosition = NetPlayerStations.StationPosition;
            SpaceHooks.PlayerJumpgateOffline = NetPlayerStations.JumpgateOffline;
            SpaceHooks.CanUsePlayerJumpgate = orbit => NetPlayerStations.CanUseJumpgate(NetPlayer.Local, orbit);
            SpaceHooks.DiscoverPlayerJumpgate = orbit => NetPlayer.Local?.RequestJumpgateDiscovery(orbit);
            SpaceHooks.PlayerInfrastructureSnapshot = () => NetState.Instance?.PlayerStations;
            SpaceHooks.PlayerOrbitalModels = (db, orbit) => NetPlayerStations.OrbitalStructures?
                .Where(structure => structure != null && structure.orbit == orbit && structure.operational)
                .Select(structure => (structure.id, structure.name,
                    NetPlayerStations.OrbitalStructureAssembly(structure.assembly, structure.stationModel, db),
                    NetPlayerStations.OrbitalStructurePosition(structure), structure.modelScale, structure.hitRadius)).ToArray();
            SpaceHooks.PlayerWreckSalvage = WreckSalvageAt;
            SpaceHooks.PlayerWreckSalvagePosition = (orbit, jumpgate) =>
                jumpgate ? NetPlayerStations.JumpgateSalvagePosition(orbit) : NetPlayerStations.StationSalvagePosition(orbit);
            SpaceHooks.CollectPlayerWreckSalvage = (orbit, jumpgate, room) => NetPlayer.Local?.RequestWreckSalvage(orbit, room, jumpgate);
            SpaceHooks.ReportInfrastructureContact = (kind, id, item, shot, point, surface, blast) =>
            {
                var pilot = NetPlayer.Local;
                if (pilot == null) return;
                switch (kind)
                {
                    case InfrastructureContactKind.Station:
                        pilot.ReportPlayerStationContact(id, item, shot, point, surface, blast);
                        break;
                    case InfrastructureContactKind.OrbitalStructure:
                        pilot.ReportOrbitalStructureContact(id, item, shot, point, surface, blast);
                        break;
                    case InfrastructureContactKind.Jumpgate:
                        if (blast) pilot.ReportJumpgateBlast(id, item, shot, point, surface);
                        else pilot.ReportJumpgateImpact(id, item, shot, point);
                        break;
                }
            };
            SpaceHooks.GuestAssembly = NetHangar.GuestAssembly;   // PR #87: guests' customizable ships
            SpaceHooks.GuestShipY = NetHangar.GuestShipY;
            SpaceHooks.GuestBuild = NetHangar.GuestBuild;
            SpaceHooks.PeersProvider = () => NetPlayer.All;
            SpaceHooks.LocalPeerProvider = () => NetPlayer.Local;
            SpaceHooks.InterceptMirrorShot = NetPlayer.InterceptMirrorShot;   // point defense (PointDefense)
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
            SpaceHooks.ShipScanned = (station, name) =>
            {
                if (EventHost.Local) EventRunner.OnShipScanned(LocalPilot.Instance, station, name);
                else NetState.Instance?.ShipScannedRpc(station, name ?? "");
            };
            SpaceHooks.PadDocked = station =>
            {
                if (EventHost.Local) EventRunner.OnPadDocked(LocalPilot.Instance, station);
                else NetState.Instance?.PadDockedRpc(station);
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
            SpaceHooks.FollowSquadmate = client => NetFollow.Toggle(NetSquad.Find(client));
            SpaceHooks.FollowedSquadmate = () => NetFollow.Following;
            SpaceHooks.TeleportPose = NetTeleport.TakePose;
            SpaceHooks.ArenaProvider = () => NetArenaClient.Current;
            SpaceHooks.InArenaMatch = () => NetArenaClient.InMatch;
            SpaceHooks.ArenaSpawnPose = NetArenaClient.SpawnPose;
            SpaceHooks.ArenaLevelReady = NetArenaClient.OnLevelReady;

            SpaceHooks.EventNoDocking = () => EventRules.NoDocking;
            SpaceHooks.EventNoJumps = () => EventRules.NoJumps;
            SpaceHooks.EventNoKhador = () => EventRules.NoKhador;
            SpaceHooks.EventCinematic = () => EventCutscene.Cinematic;
            SpaceHooks.EventInvulnerable = () => EventCutscene.Invulnerable;
            SpaceHooks.EventQuestionOpen = () => EventScreen.QuestionOpen;
            SpaceHooks.EventSceneMusic = () => EventScreen.SceneMusic;
            SpaceHooks.QuietOrbit = EventRunner.QuietOrbit;
            SpaceHooks.RequestEventOffers = EventMissions.RequestOffers;
            // An event's respawn point, else a co-op guest destroyed in the leader's story orbit (beside the leader).
            SpaceHooks.RespawnDue = ms => EventRespawn.Due(ms) || NetCoop.RespawnDue(ms);
            SpaceHooks.Respawn = () => { if (EventRespawn.Active) EventRespawn.Go(); else NetCoop.Respawn(); };
            SpaceHooks.CoopLevelState = NetCoop.LevelState;
            SpaceHooks.CoopRadioShown = NetCoop.Radio;
            SpaceHooks.CoopRoute = NetCoop.Route;
            SpaceHooks.CoopCamera = NetCoop.Camera;
            SpaceHooks.CoopFx = NetCoop.Fx;
            SpaceHooks.CoopTravel = NetCoop.Travel;
            SpaceHooks.CoopLevelFailed = NetCoop.LevelFailed;
            SpaceHooks.CoopDialog = NetCoop.Dialog;
            SpaceHooks.CoopHold = NetCoop.Hold;
            SpaceHooks.CoopLeaderHere = NetCoop.LeaderHere;
            SpaceHooks.CoopLeaderPose = NetCoop.LeaderPose;
            SpaceHooks.ServerShutOff = DedicatedServer.ShutOff;
        }
    }
}
