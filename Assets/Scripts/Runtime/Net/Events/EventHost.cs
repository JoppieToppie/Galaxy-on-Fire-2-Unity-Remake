// EventHost.cs
// Where the event system (EventRunner, NetCommands, NetAdmin, NetTeleport, EventMissions) meets the game: a multiplayer
// session (the server's NetState RPCs, the NetPlayers, the NetProxies) or single player (mods' events and bar missions
// with no session: the one LocalPilot, orders applied at once with NetAdmin.Apply, the local traffic's ships, notices on
// the HUD message line or a station toast, ticked by LocalEvents). Every server-side call that reaches a player's game
// goes through here.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class EventHost
    {
        /// <summary>No session: single player runs the events itself.</summary>
        public static bool Local => !NetGame.Active;

        /// <summary>Commands and events may run: the session's server, or single player.</summary>
        public static bool ServerReady => Local || (NetState.Instance != null && NetState.Instance.IsSpawned && NetState.Instance.IsServer);

        /// <summary>The event screens and rules (titles, questions, music, respawn points, travel rules, waypoints) are in use:
        /// a session, or single player while a local event runs.</summary>
        public static bool ScreensActive => NetGame.Active || LocalEvents.Running;

        static readonly List<IPilot> local = new List<IPilot> { LocalPilot.Instance };
        static readonly List<IPilot> net = new List<IPilot>();

        /// <summary>Every player.</summary>
        public static IReadOnlyList<IPilot> Pilots
        {
            get
            {
                if (Local) return local;
                net.Clear();
                foreach (var p in NetPlayer.All) net.Add(p);
                return net;
            }
        }

        /// <summary>This game's own pilot name (a session's NetPlayer.Local, single player's pilot): %player% in texts.</summary>
        public static string LocalName => Local ? LocalPilot.Instance.DisplayName : NetPlayer.Local != null ? NetPlayer.Local.DisplayName : "";

        /// <summary>The player with that id (null: none).</summary>
        public static IPilot Find(ulong id) => Local ? (id == 0 ? LocalPilot.Instance : null) : NetSquad.Find(id);

        /// <summary>An admin order for one player's game (NetAdmin.Apply there).</summary>
        public static void Send(ulong who, NetAdmin.Order order, int a, int b, int c, string text, string by)
        {
            if (Local) { if (who == 0) NetAdmin.Apply(order, a, b, c, text, by); }
            else NetState.Instance?.SendAdmin(who, order, a, b, c, text, by);
        }

        public static void Teleport(ulong who, NetTeleport.Destination d, string by)
        {
            if (Local) { if (who == 0) NetTeleport.Go(d, by); }
            else NetState.Instance?.SendTeleport(who, d, by);
        }

        public static void NoticeAll(string text)
        {
            if (Local) LocalNotice(text);
            else NetState.Instance?.NoticeAll(text);
        }

        public static void NoticeTo(IPilot p, string text)
        {
            if (p == null || string.IsNullOrEmpty(text)) return;
            if (Local) LocalNotice(text);
            else NetState.Instance?.NoticeTo(p as NetPlayer, text);
        }

        /// <summary>Single player: a notice on the flight HUD's message line, docked a toast.</summary>
        static void LocalNotice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (UI.UiHooks.FlightMessage != null && UI.UiHooks.FlightMessage(text)) return;
            UI.UiHooks.StationToast?.Invoke(text);
            Debug.Log("Event: " + text);
        }

        /// <summary>Single player: an event graph quest failed: like the story's failure, its note (in flight the story's
        /// one-page note, docked the station dialog), then the last save (the auto-save) is loaded.</summary>
        public static void QuestFailed(string title)
        {
            var level = Object.FindAnyObjectByType<World.SpaceLevel>();
            if (level != null && level.StorySpace != null) { level.StorySpace.FailQuest(title); return; }
            System.Action reload = () =>
            {
                if (Modding.ModBuilder.PlayTesting) { Modding.ModBuilder.EndPlayTest(); return; }   // a Mod Builder play test
                if (Session.LoadAutosave() && Application.CanStreamedLevelBeLoaded("Station")) UnityEngine.SceneManagement.SceneManager.LoadScene("Station");
                else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            };
            if (UI.UiHooks.StationDialog == null || !UI.UiHooks.StationDialog(title + "\n\n" + Localization.Get(319), reload, true)) reload();
        }

        public static void SetFreeForAll(bool on)
        {
            if (!Local) NetState.Instance?.SetFreeForAll(on);   // single player has no other pilots
        }

        /// <summary>A question's answer (EventScreen) back to the event that asked.</summary>
        public static void Answer(int ask, int choice)
        {
            if (Local) EventRunner.OnAnswer(0, ask, choice);
            else NetState.Instance?.SendAnswer(ask, choice);
        }

        /// <summary>A bar mission of an event graph started / ended for a team member.</summary>
        public static void EventMission(ulong client, bool started, string payload)
        {
            if (Local)
            {
                if (client != 0) return;
                if (started) EventMissions.OnStarted(payload);
                else EventMissions.OnEnded(payload);
            }
            else NetState.Instance?.SendEventMission(client, started, payload);
        }

        // ---- the event's ships -----------------------------------------------------------------------------------

        public struct Ship
        {
            public int tag, station;
            public ulong killer;   // ulong.MaxValue: not destroyed by a player
            public long key;
            public bool flying;
            public string name;        // Spawn's "named" (the lock-plate name), "" = none
            public Vector3 position;   // game units, in its station's orbit
        }

        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / 0.05f;

        static readonly List<Ship> ships = new List<Ship>();

        /// <summary>Every ship an event spawned (its batch tag): the session's NetProxies, or single player's own traffic.</summary>
        public static List<Ship> EventShips()
        {
            ships.Clear();
            if (Local)
            {
                var traffic = Object.FindAnyObjectByType<World.Traffic>();
                if (traffic == null) return ships;
                foreach (var s in traffic.Ships)
                {
                    if (s == null || s.Spec == null || s.Spec.eventTag == 0) continue;
                    var t = s.Target;
                    bool alive = t != null && t.Alive;
                    ships.Add(new Ship
                    {
                        tag = s.Spec.eventTag, station = Session.StationIndex, key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s), flying = alive && s.gameObject.activeInHierarchy,
                        killer = !alive && t != null && !t.killedByNpc ? 0UL : ulong.MaxValue,
                        name = t != null ? t.displayName ?? "" : "", position = ToGame(s.transform.position),
                    });
                }
                return ships;
            }
            foreach (var proxy in Object.FindObjectsByType<NetProxy>())
            {
                if (proxy == null || !proxy.IsSpawned || proxy.EventTag == 0) continue;
                ships.Add(new Ship { tag = proxy.EventTag, station = proxy.Station, killer = proxy.Killer, flying = proxy.FlyingNow,
                                     key = (long)proxy.NetworkObjectId,   // (the same through a handover to another game)
                                      name = proxy.Label, position = ToGame(proxy.WorldPosition) });   // (the pose stream's: a server draws nothing)
            }
            return ships;
        }

        // ---- the clock ---------------------------------------------------------------------------------------------

        static float pausedTotal, pausedSince = -1f;

        /// <summary>The events' clock: real time, which in single player stops while the game is paused (Time.timeScale 0:
        /// the pause menu, conversations, maps), so an event's waits and timers don't run on under a menu.</summary>
        public static float Now
        {
            get
            {
                float t = Time.unscaledTime;
                if (!Local) return t;
                bool paused = Time.timeScale <= 0f;
                if (paused && pausedSince < 0f) pausedSince = t;
                if (!paused && pausedSince >= 0f) { pausedTotal += t - pausedSince; pausedSince = -1f; }
                return (paused ? pausedSince : t) - pausedTotal;
            }
        }

        /// <summary>The frame's time on that clock (0 while single player is paused).</summary>
        public static float DeltaTime => Local && Time.timeScale <= 0f ? 0f : Time.unscaledDeltaTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { pausedTotal = 0f; pausedSince = -1f; }
    }
}
