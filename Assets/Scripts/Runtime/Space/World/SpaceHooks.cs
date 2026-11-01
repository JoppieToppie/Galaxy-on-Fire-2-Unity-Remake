// SpaceHooks.cs
// What the flight and station levels (GoF2.Space) need from the layers above them: multiplayer (GoF2.Net: the shared
// orbit, the squad, the arena, the event graphs) and the application (GoF2.App: the star map, the VR rig, the graphics
// settings). GoF2.Space sits below those, so it can't call them: they fill these members when the game starts
// (NetHooksInstaller, AppHooksInstaller; in the Editor EditorHookInstaller), and until then every member answers like single
// player without events (an Editor test, a scene loaded on its own).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GoF2Remake.World
{
    /// <summary>Another player's game as the levels see it (Multiplayer.NetPlayer).</summary>
    public interface IOrbitPeer
    {
        ulong OwnerClientId { get; }
        bool IsOwner { get; }
        bool IsSpawned { get; }
        bool InSpace { get; }
        int Station { get; }
        /// <summary>The squad mission this player's game runs in its orbit (FreelanceMission.netId, 0 = none).</summary>
        long MissionRun { get; }
        /// <summary>The squad mission this player holds (0 = none).</summary>
        long MissionHeld { get; }
        string MissionRoute { get; }
        float MissionClock { get; }
        int MissionScore { get; }
        /// <summary>This player's game runs the Kaamo siege in its orbit.</summary>
        bool SiegeRun { get; }
        string DisplayName { get; }
        /// <summary>A distress call is on.</summary>
        bool Distress { get; }
    }

    /// <summary>Another game's NPC ship or junk shown here (Multiplayer.NetProxy), for a mission orbit's takeover.</summary>
    public interface IOrbitProxy
    {
        bool IsSpawned { get; }
        int Station { get; }
        ulong Creator { get; }
        bool IsJunk { get; }
        int JunkKind { get; }
        int RoleFlags { get; }
        bool MissionAdoptable { get; }
        Vector3 WorldPosition { get; }
        Quaternion WorldRotation { get; }
        string Label { get; }
        float HullFraction { get; }
        IEnumerable<ulong> Aggressors { get; }
        /// <summary>The ship as a spawn of this game's traffic at 'gamePosition' (ship -1 = not a ship).</summary>
        SpawnSpec AdoptSpec(Vector3 gamePosition);
        /// <summary>This game took it over: the old copy goes, here and in the other games.</summary>
        void Adopt();
    }

    /// <summary>Another game's crate shown here (Multiplayer.NetCrate).</summary>
    public interface IOrbitCrate
    {
        bool IsSpawned { get; }
        bool IsOwner { get; }
        int Station { get; }
        bool IsMissionCrate { get; }
        Vector3 WorldPosition { get; }
        /// <summary>This game took it over: the old copy goes, here and in the other games.</summary>
        void Adopt();
    }

    /// <summary>The shared orbit's asteroid state (Multiplayer.NetOrbit): who destroyed one, who else drills it.</summary>
    public interface ISharedOrbit
    {
        /// <summary>Another player's game destroyed it: their name, and whether they drilled it out.</summary>
        bool DestroyedBy(Target asteroid, out string by, out bool mined);
        /// <summary>Another player sits on it (landed or drilling).</summary>
        bool OthersOn(Target asteroid);
        /// <summary>How many other players drill it now.</summary>
        int OtherMiners(Target asteroid);
    }

    /// <summary>An arena match's orbit (Multiplayer.NetArenaClient.Match).</summary>
    public class ArenaMatchInfo
    {
        /// <summary>The match's own orbit id (NetArena.OrbitBase + the match), the orbit it copies (the Void's home), and
        /// whether the Void fighters are in it.</summary>
        public int orbitId, template;
        public bool voids;
    }

    public delegate bool PoseQuery(out Vector3 position, out Quaternion rotation);
    public delegate bool StationPoseQuery(int station, out Vector3 position, out Quaternion rotation);
    public delegate bool StarMapOpener(Database db, StarMapMode mode, bool jumpDrive, Action<StarMapResult> closed, int promptStation, bool askVoid);

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SpaceHooks
    {
        // ---- the shared orbit (NetOrbit, NetPlayer, NetProxy, NetCrate, NetState) ----------------------------------------

        public static Func<IEnumerable<IOrbitPeer>> PeersProvider;
        public static Func<IOrbitPeer> LocalPeerProvider;
        public static Func<IEnumerable<IOrbitProxy>> ProxiesProvider;
        public static Func<IEnumerable<IOrbitCrate>> CratesProvider;
        public static Func<ISharedOrbit> SharedOrbitProvider;

        /// <summary>The players' games in the session (this one's included); none outside one.</summary>
        public static IEnumerable<IOrbitPeer> Peers => PeersProvider != null ? PeersProvider() : Array.Empty<IOrbitPeer>();
        /// <summary>This game's player in the session, null outside one.</summary>
        public static IOrbitPeer LocalPeer => LocalPeerProvider?.Invoke();
        public static IEnumerable<IOrbitProxy> Proxies => ProxiesProvider != null ? ProxiesProvider() : Array.Empty<IOrbitProxy>();
        public static IEnumerable<IOrbitCrate> Crates => CratesProvider != null ? CratesProvider() : Array.Empty<IOrbitCrate>();
        /// <summary>The shared orbit of the session (NetOrbit.Current), null outside one.</summary>
        public static ISharedOrbit SharedOrbit => SharedOrbitProvider?.Invoke();

        /// <summary>The level is built (SpaceLevel.Start's end): multiplayer adds its NetOrbit.</summary>
        public static Action<SpaceLevel> LevelBuilt;
        /// <summary>The station is built: multiplayer adds its NetHangar.</summary>
        public static Action<StationLevel> StationBuilt;
        /// <summary>No other player is in that orbit (NetState.OrbitEmpty): this game builds its traffic.</summary>
        public static Func<int, bool> OrbitEmpty;
        /// <summary>A point out of the other players' sight (NetOrbit.OutOfSight), game units.</summary>
        public static Func<Vector3, Vector3> OutOfSight;
        /// <summary>Another player is docked at that station (NetHangar.OtherDockedHere).</summary>
        public static Func<int, bool> OtherDockedHere;
        /// <summary>The Kaamo siege at that station is won (NetState.SiegeWonRpc): the others there win it too.</summary>
        public static Action<int> SiegeWon;
        /// <summary>A pirate sighting's event is destroyed (id, killer's client id; NetState.PirateSightingDoneRpc).</summary>
        public static Action<int, ulong> PirateSightingDone;
        /// <summary>The player hacked a wreck in this orbit (station, the wreck's name): an event's or quest's "on hacked"
        /// (EventRunner.OnWreckHacked; in a session through NetState.WreckHackedRpc).</summary>
        public static Action<int, string> WreckHacked;
        /// <summary>A line in the multiplayer chat as a notice (NetChat.Notice).</summary>
        public static Action<string> Notice;

        public static bool IsOrbitEmpty(int orbit) => OrbitEmpty == null || OrbitEmpty(orbit);
        public static Vector3 OutOfSightOf(Vector3 gamePos) => OutOfSight != null ? OutOfSight(gamePos) : gamePos;
        public static bool OtherDocked(int station) => OtherDockedHere != null && OtherDockedHere(station);

        // ---- squad missions (NetMissions) --------------------------------------------------------------------------------

        /// <summary>NetMissions' results: what a squadmate's game reports.</summary>
        public const int MissionFailure = 0, MissionSuccess = 1, MissionAbandoned = 2;
        /// <summary>A squad mission's result reached this game (result, share, from whom): the follower orbit's dialog;
        /// true = shown.</summary>
        public static Func<int, int, string, bool> MissionResultView;
        /// <summary>Progress on the squad's mission (ore unloaded, a container captured...): the squad has it.</summary>
        public static Action<FreelanceMission, int> AddMissionStatus;
        /// <summary>This game builds the mission orbit at that station (no squadmate runs it there).</summary>
        public static Func<int, bool> RunMissionHere;
        /// <summary>A member of this player's mission team is in that orbit.</summary>
        public static Func<int, bool> MissionTeamHere;

        public static void MissionStatus(FreelanceMission m, int delta) => AddMissionStatus?.Invoke(m, delta);
        public static bool RunsMission(int station) => RunMissionHere == null || RunMissionHere(station);
        public static bool TeamHere(int station) => MissionTeamHere != null && MissionTeamHere(station);

        // ---- the squad, arena, distress, teleport ------------------------------------------------------------------------

        /// <summary>The local player's squad (0 = none) and its members (this game's player included).</summary>
        public static Func<int> LocalSquadProvider;
        public static Func<IEnumerable<IOrbitPeer>> SquadProvider;
        public static int LocalSquad => LocalSquadProvider != null ? LocalSquadProvider() : 0;
        public static IEnumerable<IOrbitPeer> Squad => SquadProvider != null ? SquadProvider() : Array.Empty<IOrbitPeer>();

        /// <summary>The distress call on / off (its message); help a squadmate (by client id; its message).</summary>
        public static Func<string> ToggleDistress;
        public static Func<ulong, string> HelpSquadmate;
        /// <summary>Arriving by travel at a squadmate's distress call: beside them.</summary>
        public static StationPoseQuery DistressArrival;
        /// <summary>A teleport's pose for this arrival (NetTeleport.TakePose).</summary>
        public static PoseQuery TeleportPose;

        /// <summary>The arena match this game plays (NetArenaClient.Current), null = none.</summary>
        // ---- remake mods (PR #87): another player's customizable ship parked in this hangar (NetHangar) ----
        public static Func<ulong, int, AssemblyData> GuestAssembly;
        public static Func<ulong, int, float> GuestShipY;
        public static Func<ulong, int, Modding.ModShipKits.Build> GuestBuild;

        public static Func<ArenaMatchInfo> ArenaProvider;
        /// <summary>In a match of the running session (NetArenaClient.InMatch).</summary>
        public static Func<bool> InArenaMatch;
        public static Func<Pose> ArenaSpawnPose;
        public static Action ArenaLevelReady;
        public static ArenaMatchInfo Arena => ArenaProvider?.Invoke();
        public static bool InArena => InArenaMatch != null && InArenaMatch();

        /// <summary>Remake mods: the player is docked inside the orbit (EVERSPACE 2's dock, Es2Dock): the station's menu runs
        /// over the space level, whose flight controls wait (Navigation.InputHalted). Set by SpaceLevel.</summary>
        public static bool DockedInOrbit { get; set; }

        // ---- event graphs (EventRules, EventCutscene, EventScreen, EventRunner, EventMissions, EventRespawn) -------------

        /// <summary>An event's Restrict Travel.</summary>
        public static Func<bool> EventNoDocking, EventNoJumps;
        /// <summary>An event's cutscene holds the camera / makes the player invulnerable.</summary>
        public static Func<bool> EventCinematic, EventInvulnerable;
        /// <summary>An event's question is on screen: the controls and keys rest.</summary>
        public static Func<bool> EventQuestionOpen;
        /// <summary>The scene's music volume factor while an event's track plays (1 = none).</summary>
        public static Func<float> EventSceneMusic;
        /// <summary>A quest keeps that orbit free of normal traffic.</summary>
        public static Func<int, bool> QuietOrbit;
        /// <summary>The event graphs' bar missions offered at that station (their visitors).</summary>
        public static Action<int, Func<Agent, bool>> RequestEventOffers;
        /// <summary>An event's respawn point takes the destroyed player back (this long after the death, ms).</summary>
        public static Func<float, bool> RespawnDue;
        public static Action Respawn;

        // ---- the co-op campaign (NetCoop; CampaignLevel's leader and follower) ------------------------------------------

        /// <summary>The leader's story level now (a few times a second while it runs): out to the crew in that orbit.</summary>
        public static Action<CoopLevelState> CoopLevelState;
        /// <summary>The leader's level showed radio line 'line' of step 'index' in 'station'.</summary>
        public static Action<int, int, int> CoopRadioShown;
        /// <summary>The leader's level route changed (station, points in game units, the index reached).</summary>
        public static Action<int, List<Vector3>> CoopRoute;
        /// <summary>The leader's cutscene camera (station, position, rotation, field of view).</summary>
        public static Action<int, Vector3, Quaternion, float> CoopCamera;
        /// <summary>The leader's level moved it on (station left, station reached, docked, by travel).</summary>
        public static Action<int, int, bool, bool> CoopTravel;
        /// <summary>The leader's level in 'station' failed (or the leader died in it): the crew's failure.</summary>
        public static Action<int> CoopLevelFailed;
        /// <summary>This game's story conversation opened / closed (the leader holds its level meanwhile).</summary>
        public static Action<bool> CoopDialog;
        /// <summary>The leader: a crew member in 'station' has a story conversation open (the level holds).</summary>
        public static Func<int, bool> CoopHold;
        /// <summary>The leader flies in 'station' (a follower's level waits for it).</summary>
        public static Func<int, bool> CoopLeaderHere;
        /// <summary>The leader's ship's pose in 'station' (false = not there).</summary>
        public static StationPoseQuery CoopLeaderPose;

        public static bool NoDocking => EventNoDocking != null && EventNoDocking();
        public static bool NoJumps => EventNoJumps != null && EventNoJumps();
        public static bool Cinematic => EventCinematic != null && EventCinematic();
        public static bool Invulnerable => EventInvulnerable != null && EventInvulnerable();
        public static bool QuestionOpen => EventQuestionOpen != null && EventQuestionOpen();
        public static float SceneMusic => EventSceneMusic != null ? EventSceneMusic() : 1f;
        public static bool IsQuietOrbit(int station) => QuietOrbit != null && QuietOrbit(station);

        // ---- the application (GoF2.App) ----------------------------------------------------------------------------------

        /// <summary>The multiplayer window is open.</summary>
        public static Func<bool> MultiplayerWindowOpen;
        /// <summary>The star map is open; open it (false = it couldn't).</summary>
        public static Func<bool> StarMapOpenProvider;
        public static StarMapOpener OpenStarMap;
        /// <summary>The VR eye camera (VrRig.Current.Eye), null without VR.</summary>
        public static Func<Camera> VrEye;
        /// <summary>A dedicated server switches the menu scene off (DedicatedServer.ShutOff): true = it did.</summary>
        public static Func<Scene, bool> ServerShutOff;
        /// <summary>The scene's fog (Bootstrap.SetSceneFog: off below Quality High).</summary>
        public static Action<bool> SetSceneFog;
        /// <summary>A temporal upscaler (STP, DLSS, FSR 2+, MetalFX Temporal) is active.</summary>
        public static Func<bool> TemporalUpscaler;
        /// <summary>The keyboard unless the chat or an event's question takes it (NetChat.Keys).</summary>
        public static Func<Keyboard> KeysProvider;

        public static bool MultiplayerWindowIsOpen => MultiplayerWindowOpen != null && MultiplayerWindowOpen();
        public static bool StarMapOpen => StarMapOpenProvider != null && StarMapOpenProvider();
        public static Keyboard Keys => KeysProvider != null ? KeysProvider() : Keyboard.current;

        public static void SceneFog(bool on)
        {
            if (SetSceneFog != null) SetSceneFog(on);
            else RenderSettings.fog = on && Settings.Fog;
        }
    }
}
