// EventRunnerQuests.cs
// Part of EventRunner: the bar missions' graph list and starts, and single player's lasting runs: quests and
// a bar mission taken in single player (restoring them from checkpoints, their start conditions, the Missions window's views).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GoF2Remake.Data;
using UnityEngine;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    public static partial class EventRunner
    {
        /// <summary>Every event graph's details (its Start node's Mission settings), read again when the mods change or
        /// the Events folders' files do (looked at every 30 s).</summary>
        internal static List<MissionInfo> AllGraphs()
        {
            if (graphCache != null && graphCacheMods == Modding.ModManager.Revision)
            {
                if (EventHost.Now - graphCacheTime < 30f) return graphCache;
                // Modified: only when a file there changed. Reading and compiling every graph each 30 s stalled the
                // game for 0.75 s on an iPhone (#275: CheckQuestStarts asks every second in flight).
                graphCacheTime = EventHost.Now;
                if (FolderStamp() == graphCacheFiles) return graphCache;
            }
            graphCache = new List<MissionInfo>();
            graphCacheTime = EventHost.Now;
            graphCacheFiles = FolderStamp();
            GraphCompiles++;
            var names = List();
            // after List: its ModManager.Active may scan the mods and bump the revision, which made the list stale as soon
            // as it was made (read again on the next call, every second)
            graphCacheMods = Modding.ModManager.Revision;
            foreach (string name in names)
            {
                string text = Load(name, out string from, out string error);
                if (text == null || error != null) continue;
                var compiled = Compile(text, out _);
                if (compiled == null) continue;
                var info = Info(compiled);
                info.name = name;
                info.builtIn = from == X("mpEventBuiltIn", "built in");
                graphCache.Add(info);
            }
            return graphCache;
        }

        /// <summary>Server: every event graph with mission details (its Start node's Mission title).</summary>
        internal static List<MissionInfo> MissionGraphs() => AllGraphs().FindAll(m => m.IsMission);

        /// <summary>The graph 'name' is a bar mission (offered in the Space Lounges).</summary>
        public static bool IsBarMission(string name)
        {
            graphCache = null;   // the Mod Builder may have just written it
            return MissionGraphs().Exists(m => m.name == name);
        }

        /// <summary>Single player: every quest graph (its Start node's kind Quest).</summary>
        internal static List<MissionInfo> QuestGraphs() => AllGraphs().FindAll(m => m.IsQuest && !m.builtIn);

        /// <summary>Server: the mission event 'name' runs for 'team' (taken at 'station' by 'by'); the answer when it can't.</summary>
        internal static string StartMission(string name, HashSet<ulong> team, int station, IPilot by, GraphQuestState record = null)
        {
            foreach (ulong id in team)
                if (MissionOf(id) != null) return X("mpEventMissionBusy", "Someone in your squad is already on a mission like this.");
            string text = Load(name, out string from, out string error);
            if (text == null) return string.Format(X("mpEventMissing", "No event \"{0}\". /event list shows them."), name);
            if (error != null) return string.Format(X("mpEventError", "{0}: {1}"), name, error);
            if (MissionTaken(name) && (MissionGraphs().Find(m => m.name == name)?.takers ?? EventMissionTakers.OneInUniverse) == EventMissionTakers.OneInUniverse)
                return X("mpEventMissionTakenBy", "Another squad is already on this mission.");
            string answer = Start(name, text, from, by, new Dictionary<string, double>(), team, station, record);
            return MissionOf(by != null ? by.OwnerClientId : ulong.MaxValue) != null ? null : answer;
        }

        /// <summary>Server: a team is on the bar mission 'name' now.</summary>
        internal static bool MissionTaken(string name) => runs.Exists(r => r.team != null && !r.ended && r.running == name);

        /// <summary>Server: a team member's run (the mission's name and info) for a player joining late / asking again.</summary>
        internal static MissionInfo MissionInfoOf(ulong client) => MissionOf(client)?.info;
        /// <summary>The run of a lasting record (null: not running).</summary>
        static EventRun RunOf(GraphQuestState record) => runs.Find(r => r.record == record && !r.ended);

        /// <summary>LocalEvents: the loaded save's quests and bar mission start again from their checkpoints.</summary>
        internal static void RestoreLocal()
        {
            RestorePending = false;
            if (!EventHost.Local) return;
            foreach (var record in Session.GraphQuests.ToArray())
            {
                if (RunOf(record) != null) continue;
                string text = Load(record.name, out string from, out string error);
                if (text == null || error != null)
                {
                    // The graph is gone (its mod turned off) or broken: the record stays for when it is back.
                    Debug.LogWarning($"Event: {record.name} can't go on ({error ?? "not found"}); kept in the save");
                    continue;
                }
                var team = new HashSet<ulong> { 0 };
                string answer = Start(record.name, text, from, LocalPilot.Instance, new Dictionary<string, double>(), team,
                                      record.station, record, record.checkpoint ?? "");
                if (RunOf(record) == null) { Debug.LogWarning($"Event: {record.name}: {answer}"); continue; }
                if (record.kind == GraphQuestState.KindMission) EventMissions.RestoreActive(record);
                var started = RunOf(record);
                if (string.IsNullOrEmpty(record.title) && started != null) record.title = started.info.title;   // a campaign's quest: its graph's title
                Debug.Log($"Event: {record.name} goes on from {(string.IsNullOrEmpty(record.checkpoint) ? "the start" : "checkpoint " + record.checkpoint)}");
            }
        }

        /// <summary>Starts the quest graph 'name' (single player): a lasting run with its own record. 'quiet' (a graph's
        /// startquest): a quest under way or done isn't an error. The reason when it can't (null: started).</summary>
        public static string StartQuest(string name, bool quiet = false)
        {
            if (!EventHost.Local) return X("mpEventQuestSingle", "Story missions run in single player only.");
            if (Session.GraphQuestsDone.Contains(name) || Session.GraphQuestRepeats.ContainsKey(name) || Session.GraphQuests.Exists(q => q.name == name))
                return quiet ? null : string.Format(X("mpEventQuestTaken", "The mission {0} is under way or done."), name);
            string text = Load(name, out string from, out string error);
            if (text == null) return string.Format(X("mpEventMissing", "No event \"{0}\". /event list shows them."), name);
            if (error != null) return string.Format(X("mpEventError", "{0}: {1}"), name, error);
            var compiled = Compile(text, out string compileError);
            if (compiled == null) return string.Format(X("mpEventError", "{0}: {1}"), name, compileError);
            var info = Info(compiled);
            var record = new GraphQuestState { name = name, kind = GraphQuestState.KindQuest, title = info.title };
            Session.GraphQuests.Add(record);
            string answer = Start(name, text, from, LocalPilot.Instance, new Dictionary<string, double>(), new HashSet<ulong> { 0 }, -1, record);
            if (RunOf(record) == null) { Session.GraphQuests.Remove(record); return answer; }
            Debug.Log($"Event: quest {name} started");
            return null;
        }

        /// <summary>A quest is done: for good, or (a "mission repeat" quest) until its cooldown in playing time has passed;
        /// then it starts again when its start condition holds.</summary>
        static void QuestDone(string name, MissionInfo info)
        {
            if (info != null && info.repeatMinutes > 0f) Session.GraphQuestRepeats[name] = Session.PlaySeconds + info.repeatMinutes * 60f;
            else Session.GraphQuestsDone.Add(name);
        }

        /// <summary>randomstation(): a station of a system on the map where an encounter may come (a quest's roaming event: the
        /// ES2 Ships mod's Dreadnought raids): not the player's own orbit, an empty or special orbit (the deep science and
        /// Valkyrie stations, the Kaamo Club, the dropship orbits, Thynome), Loma, the supernova and Void systems 27 / 32 / 33, a
        /// pirate base's system, a story's orbit, a mobile or missing mod station. -1 when there is none.</summary>
        static int RandomStation()
        {
            var db = NetGame.Db;
            var visible = GalaxyMap.Visibility(db);
            var picks = new List<int>();
            foreach (var st in db.Stations)
            {
                int i = st.index, sys = st.system;
                if (i < 0 || i == Session.StationIndex || sys < 0 || sys >= visible.Length || !visible[sys]) continue;
                if (i == 10 || i == 100 || i == 101 || i == 108 || (i >= 102 && i <= 104)) continue;
                if (sys == 25 || sys == 27 || sys == 32 || sys == 33 || Modding.ModWorld.PirateHideout(sys) || PirateBases.SystemHasBase(db, sys)) continue;
                if (World.OrbitLayout.IsEmptyOrbit(i, Session.WorldIndex) || PirateEventRules.InStoryline(i)) continue;
                if (Modding.ModWorld.IsMobile(i) || Modding.ModWorld.IsMissingStation(i)) continue;
                picks.Add(i);
            }
            return picks.Count == 0 ? -1 : picks[UnityEngine.Random.Range(0, picks.Count)];
        }

        /// <summary>LocalEvents, in a game scene: every second each quest graph not done nor under way whose start condition
        /// holds ("mission starts"; none: at once, "never": only by startquest) starts.</summary>
        internal static void CheckQuestStarts()
        {
            if ((questCheck -= Time.unscaledDeltaTime) > 0f) return;
            questCheck = 1f;
            if (!EventHost.Local || RestorePending) return;
            foreach (var info in QuestGraphs())
            {
                if (Session.GraphQuestsDone.Contains(info.name) || Session.GraphQuests.Exists(q => q.name == info.name)) continue;
                bool again = Session.GraphQuestRepeats.TryGetValue(info.name, out float readyAt);
                if (again && Session.PlaySeconds < readyAt) continue;
                string when = info.starts.Trim();
                if (when.Equals("never", StringComparison.OrdinalIgnoreCase)) continue;
                bool holds = true;
                if (when.Length > 0)
                {
                    var probe = new EventRun(info.name, new List<Step>(), info, new HashSet<ulong> { 0 }, -1, LocalPilot.Instance, new Dictionary<string, double>());
                    try { holds = probe.Probe(when) != 0; }
                    catch (Exception e) { Debug.LogWarning($"Event: quest {info.name}'s start condition: {e.Message}"); continue; }
                }
                if (!holds) continue;
                if (again) Session.GraphQuestRepeats.Remove(info.name);
                string why = StartQuest(info.name, true);
                if (why != null) Debug.LogWarning($"Event: quest {info.name}: {why}");
            }
        }

        /// <summary>Remake (mods: Modding.ModUnlocks): a condition in the events' expression language outside any event, for
        /// this game (the quests' "Starts when" words: campaign, credits, rank, visited(...), quest(...), option(...), won(...),
        /// systemsvisited...). Empty = true; nonzero = true; a bad expression is false with 'error'.</summary>
        public static bool Condition(string expression, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(expression)) return true;
            var probe = new EventRun("condition", new List<Step>(), new MissionInfo(), new HashSet<ulong> { 0 }, -1, LocalPilot.Instance, new Dictionary<string, double>());
            try { return probe.Probe(expression) != 0; }
            catch (Exception e) { error = e.Message; return false; }
        }

        /// <summary>"systemsvisited": the star systems of the stations docked at (Session.VisitedStations).</summary>
        static int VisitedSystems()
        {
            var systems = new HashSet<int>();
            var db = NetGame.Db;
            foreach (int st in Session.VisitedStations)
            {
                var s = db.Stations.Find(x => x.index == st);
                if (s != null) systems.Add(s.system);
            }
            return systems.Count;
        }

        /// <summary>The Missions window's Discard: the single-player bar mission ends (not paid).</summary>
        public static void AbandonLocalMission()
        {
            foreach (var record in Session.GraphQuests.ToArray())
            {
                if (record.kind != GraphQuestState.KindMission) continue;
                Session.GraphQuests.Remove(record);
                var run = RunOf(record);
                if (run != null) { run.record = null; run.End(); }
            }
            EventMissions.ResetLocal();
        }

        /// <summary>SpaceLevel: a quest made this orbit its own (questorbit): no normal traffic is built there.</summary>
        public static bool QuietOrbit(int station)
        {
            foreach (var r in runs) if (!r.ended && r.quiet.Contains(station)) return true;
            return false;
        }

        /// <summary>The quests under way (single player), in the order they started.</summary>
        public static List<QuestView> Quests()
        {
            var list = new List<QuestView>();
            foreach (var record in Session.GraphQuests)
            {
                if (record.kind != GraphQuestState.KindQuest) continue;
                var run = RunOf(record);
                list.Add(new QuestView
                {
                    name = record.name, title = string.IsNullOrEmpty(record.title) ? record.name : record.title,
                    objective = run != null ? run.objective : record.objective, target = run != null ? run.target : record.target,
                });
            }
            return list;
        }

        /// <summary>The story icon (map, HUD) on a quest's target station.</summary>
        public static bool IsQuestTarget(int station)
        {
            if (station < 0) return false;
            foreach (var r in runs) if (!r.ended && r.record != null && r.record.kind == GraphQuestState.KindQuest && r.target == station) return true;
            return false;
        }
    }
}
