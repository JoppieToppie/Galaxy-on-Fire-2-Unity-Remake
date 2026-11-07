// EventRunnerRun.cs
// Part of EventRunner: EventRun's life and step interpreter: its players and scope, End, checkpoints and Resume,
// Tick and the threads running the steps (RunSteps), handlers, commands, a mission's completion or failure.

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
        sealed partial class EventRun
        {
            /// <summary>The run is about this player: a mission's team, else everyone.</summary>
            public bool InScope(IPilot p) => p != null && (team == null || team.Contains(p.OwnerClientId));

            /// <summary>docked(station) / inorbit(station): the event's players docked there / alive in its orbit.</summary>
            public double PilotsAt(string station, bool docked)
            {
                if (!NetTeleport.ParseStation(station, out int st, out string left) || left.Length > 0)
                    throw new Exception(string.Format(X("mpSelNoStation", "No station \"{0}\" (a number, a name or void)."), station));
                int n = 0;
                foreach (var p in EventHost.Pilots)
                {
                    if (p == null || !p.IsSpawned || !InScope(p) || p.Station != st) continue;
                    if (docked ? p.InHangar : p.InSpace && p.Hull > 0f) n++;
                }
                return n;
            }

            /// <summary>distance(player, x, y, z): game units from the nearest of the event's players alive in space to that
            /// point of their orbit (-1: none; a mission keeps them in its orbit, else pair it with inorbit).</summary>
            public double NearestPilot(Vector3 gamePoint)
            {
                var at = World.OrbitLayout.ToUnity(gamePoint);
                double best = -1;
                foreach (var p in Players())
                {
                    if (!p.InSpace || p.Hull <= 0f) continue;
                    double d = (p.Position - at).magnitude / 0.05;
                    if (best < 0 || d < best) best = d;
                }
                return best;
            }

            /// <summary>The players in the session this run is about.</summary>
            List<IPilot> Players()
            {
                var list = new List<IPilot>();
                foreach (var p in EventHost.Pilots) if (p != null && p.IsSpawned && InScope(p)) list.Add(p);
                return list;
            }

            /// <summary>A notice in the chat of the run's players.</summary>
            void NoticeScope(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                if (team == null) { EventHost.NoticeAll(text); return; }
                foreach (var p in Players()) EventHost.NoticeTo(p, text);
            }

            public bool HasAsk(int id) => asks.ContainsKey(id);

            /// <summary>Ends the run: its scoreboard, music, respawn points, travel restrictions, waypoint and free for all go
            /// for its players; its questions close.</summary>
            public void End()
            {
                if (ended) return;
                ended = true;
                runs.Remove(this);
                if (scoreboardOn || scoreboardSent != null) SendScoreboard("");   // off on every screen
                if (EventHost.ServerReady)
                {
                    foreach (var p in Players())
                    {
                        EventHost.Send(p.OwnerClientId, NetAdmin.Order.Music, -1, 0, 0, "", "Server");
                        if (eventRespawn) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Respawn, -1, 0, 0, "off", "Server");
                        if (eventRules) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Rules, 0, 0, 0, "", "Server");
                        if (eventFlight) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Flight, -1, 0, 0, "", "Server");
                        if (eventWaypoint) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Waypoint, 0, 0, 0, "off", "Server");
                        if (eventWormhole) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Wormhole, 0, 0, 0, "close", "Server");
                        if (eventPickup) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Pickup, 0, 0, 0, "clear", "Server");
                        if (eventCutscene) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Cutscene, 0, 0, 0, "end", "Server");
                        if (eventFade) EventHost.Send(p.OwnerClientId, NetAdmin.Order.Fade, 0, 0, 0, "clear", "Server");
                        // (single player: a run started again from a save's checkpoint lent it before that checkpoint)
                        if (eventLoaner || (EventHost.Local && Session.ParkedShip != null && Session.ParkedShip.lentBy == running))
                            EventHost.Send(p.OwnerClientId, NetAdmin.Order.Lend, -1, 0, 0, running, "Server");
                    }
                    if (eventPvp) EventHost.SetFreeForAll(false);
                }
                CloseAsks();
                threads.Clear();
                handlers.Clear();
                current = null;
                if (team != null) EventMissions.Ended(team, running, succeeded ? EventMissions.Outcome.Success : failed ? EventMissions.Outcome.Failure : EventMissions.Outcome.Stopped);
            }

            public void SetVar(string name, double value) => vars[name] = value;

            /// <summary>An expression in this run (a quest's start condition).</summary>
            public double Probe(string expression) => Eval(Fill(expression));

            /// <summary>"checkpoint": what a save keeps of the run as it is now.</summary>
            void SaveCheckpoint(string name)
            {
                if (record == null) return;
                record.checkpoint = name;
                record.varNames = new List<string>(vars.Keys);
                record.varValues = new List<double>(vars.Values);
                record.objective = objective;
                record.target = target;
                record.quietOrbits = new List<int>(quiet);
            }

            /// <summary>A loaded save: the run goes on after its checkpoint 'name' with the variables, objective, target and quiet
            /// orbits saved there; the handlers and the parallel / every blocks of the main flow before it start again, its
            /// music, waypoint, respawn and travel restriction commands run again (in order). An unknown name (the graph
            /// changed): from the start.</summary>
            public void Resume(string name)
            {
                if (record != null)
                {
                    for (int k = 0; k < record.varNames.Count && k < record.varValues.Count; k++) vars[record.varNames[k]] = record.varValues[k];
                    objective = record.objective ?? "";
                    target = record.target;
                    quiet.Clear();
                    if (record.quietOrbits != null) quiet.UnionWith(record.quietOrbits);
                }
                if (string.IsNullOrEmpty(name)) return;
                int at = program.FindIndex(x => x.op == Op.Checkpoint && x.a == name);
                if (at < 0) { Debug.LogWarning($"Event {running}: no checkpoint \"{name}\" any more, from the start"); return; }
                current = threads[0];
                try
                {
                    for (int k = 0; k < at; k++)
                    {
                        var s = program[k];
                        if (s.depth != 0) continue;
                        switch (s.op)
                        {
                            case Op.Handler: AddHandler(s, k + 1); break;
                            case Op.Fork: if (threads.Count < MaxThreads) threads.Add(new Thread { pc = k + 1 }); break;
                            case Op.Score: scoreMode = Mode(s.a); break;
                            case Op.Scoreboard: scoreboardOn = s.a == "on"; scoreboardTitle = Fill(s.b ?? ""); break;
                            case Op.Command:
                            {
                                string word = s.a.Split(' ')[0].ToLowerInvariant();
                                if (word == "music" || word == "waypoint" || word == "wormhole" || word == "respawn" || word == "restrict" || word == "flightstyle")
                                    try { RunCommand(Fill(s.a)); } catch (Exception e) { Debug.LogWarning($"Event {running}: {e.Message}"); }
                                break;
                            }
                        }
                    }
                }
                finally { current = null; }
                threads[0].pc = at + 1;
            }

            /// <summary>A tick: the results, the automatic end, the scoreboard, the triggers, then every flow.</summary>
            public void Tick()
            {
                float now = EventHost.Now;
                if (team != null && Players().Count == 0)
                {
                    Debug.Log($"Server: the mission {running} ended: its players left");
                    End();
                    return;
                }
                UpdateStats();
                if (CheckEmpty(now)) return;
                UpdateScoreboard();
                DetectTriggers();
                for (int t = 0; t < threads.Count && !ended; t++) Run(threads[t], now);   // forks add threads: they run this tick too
                if (!ended) threads.RemoveAll(t => t.done);
            }

            public List<(string node, float progress)> ActiveSteps()
            {
                var list = new List<(string, float)>();
                float now = EventHost.Now;
                foreach (var t in threads)
                {
                    if (t.done) continue;
                    bool waiting = t.waitUntil >= 0f && t.pc > 0;
                    int at = waiting ? t.pc - 1 : t.pc;   // waiting: pc is already past the wait step
                    if (at < 0 || at >= program.Count) continue;
                    float progress = waiting && t.waitUntil > t.waitStart ? Mathf.Clamp01((now - t.waitStart) / (t.waitUntil - t.waitStart)) : -1f;
                    list.Add((program[at].node, progress));
                }
                return list;
            }

            public Dictionary<string, double> Variables()
            {
                var copy = new Dictionary<string, double>();
                foreach (var kv in vars) if (!kv.Key.StartsWith("_")) copy[kv.Key] = kv.Value;
                return copy;
            }

            public Dictionary<string, double> PlayerPoints()
            {
                var copy = new Dictionary<string, double>();
                foreach (var kv in points) { var p = EventHost.Find(kv.Key); copy[p != null ? p.DisplayName : kv.Key.ToString()] = kv.Value; }
                return copy;
            }

            // ---- the end of a mission ----

            /// <summary>"complete [reward] [| title]": the reward (an expression; none: the mission's) split evenly across the
            /// run's players still in the session (a global event: everyone), each share in the reward box under the title
            /// (default "Mission accomplished!"); then the run ends.</summary>
            void Complete(string rewardText, string title)
            {
                string expr = (rewardText ?? "").Trim();
                if (expr.Length == 0) expr = info.reward.Length > 0 ? info.reward : "0";
                double reward = Math.Max(0, Eval(Fill(expr)));
                var players = Players();
                long share = players.Count > 0 ? (long)Math.Floor(reward / players.Count) : 0;
                title = NetChat.Clean(Fill(title ?? ""));
                if (title.Length == 0) title = Localization.Get(216);   // "Mission accomplished!"
                var was = executing;
                executing = this;
                try
                {
                    foreach (var p in players)
                    {
                        string id = p.OwnerClientId.ToString(CultureInfo.InvariantCulture);
                        if (share > 0) NetCommands.RunOnServer("reward", $"{id} {share} | {title}", null);
                        else NetCommands.RunOnServer("title", $"{id} {title} for 6", null);
                    }
                }
                finally { executing = was; }
                int killed = 0;
                foreach (int k in kills.Values) killed += k;
                Debug.Log($"Server: {(team != null ? "mission" : "event")} {running} completed after {EventHost.Now - startTime:0} s " +
                          $"({killed} event ship(s) destroyed by players), {share} credits each for {players.Count} player(s)");
                succeeded = true;
                if (record != null)
                {
                    Session.GraphQuests.Remove(record);
                    if (record.kind == GraphQuestState.KindQuest) QuestDone(running, info);
                    else if (!string.IsNullOrEmpty(record.once)) Session.GraphQuestsDone.Add(record.once);   // "Only once": never offered again
                    record = null;
                }
                End();
            }

            /// <summary>"fail [title]": the title (default "Mission failed!") on the run's players' screens, then the run ends.</summary>
            void FailMission(string title)
            {
                title = NetChat.Clean(Fill(title ?? ""));
                if (title.Length == 0) title = Localization.Get(392);   // "Mission failed!"
                if (record != null && record.kind == GraphQuestState.KindQuest)
                {
                    // A quest fails like the story: the last save is loaded (back to the quest's checkpoint there).
                    Debug.Log($"Event: quest {running} failed");
                    record = null;
                    End();
                    EventHost.QuestFailed(title);
                    return;
                }
                if (record != null) { Session.GraphQuests.Remove(record); record = null; }   // a bar mission: lost
                failed = true;
                var was = executing;
                executing = this;
                try
                {
                    foreach (var p in Players())
                        NetCommands.RunOnServer("title", $"{p.OwnerClientId.ToString(CultureInfo.InvariantCulture)} {title} for 6", null);
                }
                finally { executing = was; }
                Debug.Log($"Server: {(team != null ? "mission" : "event")} {running} failed");
                End();
            }
            /// <summary>One flow until it waits, ends or has run MaxOpsPerTick steps.</summary>
            void Run(Thread th, float now)
            {
                if (th.done) return;
                if (th.waitUntil >= 0f)
                {
                    if (now < th.waitUntil) return;
                    th.waitUntil = -1f;
                }
                if (th.askWait != 0)
                {
                    if (asks.TryGetValue(th.askWait, out var ask) && !AskDone(ask, now)) return;
                    if (ask != null && ask.vote) th.pc = Winner(ask, th.voteEnd);
                    CloseAsk(th.askWait);
                    th.askWait = 0;
                }
                current = th;
                try { RunSteps(th, now); }
                finally { current = null; }
            }

            void RunSteps(Thread th, float now)
            {
                for (int ops = 0; ops < MaxOpsPerTick && !ended; ops++)
                {
                    if (th.pc >= program.Count)
                    {
                        if (th == threads[0]) Finish();   // the main flow's end is the event's
                        else th.done = true;
                        return;
                    }
                    var s = program[th.pc];
                    try
                    {
                        switch (s.op)
                        {
                            case Op.Command:
                                th.pc++;
                                RunCommand(Fill(s.a));
                                break;
                            case Op.Wait:
                                th.pc++;
                                th.waitStart = now;
                                th.waitUntil = now + Mathf.Max(0f, (float)Eval(s.a));
                                return;
                            case Op.WaitUntil:
                                if (th.untilDeadline < 0f) th.untilDeadline = s.b != null ? now + Mathf.Max(0f, (float)Eval(s.b)) : float.PositiveInfinity;
                                if ((th.checkTimer -= EventHost.DeltaTime) > 0f && now < th.untilDeadline) return;
                                th.checkTimer = CheckSeconds;
                                if (Eval(s.a) != 0 || now >= th.untilDeadline) { th.pc++; th.untilDeadline = -1f; th.checkTimer = 0f; break; }
                                return;
                            case Op.Set:
                                vars[s.a] = Eval(s.b);
                                th.pc++;
                                break;
                            case Op.Param:
                                vars[s.a] = settings.TryGetValue(s.a, out double given) ? given : Eval(s.b);
                                th.pc++;
                                break;
                            case Op.JumpIfFalse:
                                th.pc = Eval(s.a) != 0 ? th.pc + 1 : s.target;
                                break;
                            case Op.Jump:
                                th.pc = s.target;
                                break;
                            case Op.Fork:
                                if (threads.Count >= MaxThreads) throw new Exception(X("mpEventThreads", "too many parallel branches (16 at most)."));
                                threads.Add(new Thread { pc = th.pc + 1 });
                                th.pc = s.target;
                                break;
                            case Op.EndThread:
                                th.done = true;
                                return;
                            case Op.Ask:
                            case Op.Vote:
                            {
                                th.pc = s.target;
                                int id = Ask(s, now);
                                if (id != 0) { th.askWait = id; th.voteEnd = s.target; return; }
                                break;
                            }
                            case Op.StartEvent:
                                th.pc++;
                                SwitchTo(Fill(s.a), Fill(s.b));
                                return;
                            case Op.Handler:
                                AddHandler(s, th.pc + 1);
                                th.pc = s.target;
                                break;
                            case Op.Stop:
                                Finish();
                                return;
                            case Op.Score:
                                scoreMode = Mode(s.a);
                                th.pc++;
                                break;
                            case Op.Winner:
                                th.pc++;
                                AnnounceWinner(s.a == "-" ? scoreMode : Mode(s.a), null);
                                break;
                            case Op.Points:
                                th.pc++;
                                AddPoints(Fill(s.a), Eval(s.b));
                                break;
                            case Op.Mission:
                                th.pc++;
                                break;
                            case Op.Complete:
                                th.pc++;
                                Complete(s.a, s.b);
                                return;
                            case Op.MissionFail:
                                th.pc++;
                                FailMission(s.a);
                                return;
                            case Op.Checkpoint:
                                th.pc++;
                                SaveCheckpoint(s.a);
                                break;
                            case Op.Objective:
                                th.pc++;
                                objective = ObjectiveText(Fill(s.a ?? ""));
                                break;
                            case Op.Target:
                            {
                                th.pc++;
                                string where = Fill(s.a).Trim();
                                if (where.Equals("off", StringComparison.OrdinalIgnoreCase) || where.Equals("none", StringComparison.OrdinalIgnoreCase)) target = -1;
                                else if (NetTeleport.ParseStation(where, out int st, out string left) && left.Length == 0) target = st;
                                else throw new Exception(string.Format(X("mpSelNoStation", "No station \"{0}\" (a number, a name or void)."), where));
                                break;
                            }
                            case Op.QuietOrbit:
                            {
                                th.pc++;
                                string where = Fill(s.a).Trim();
                                bool off = where.EndsWith(" off", StringComparison.OrdinalIgnoreCase);
                                if (off || where.EndsWith(" on", StringComparison.OrdinalIgnoreCase)) where = where.Substring(0, where.LastIndexOf(' ')).Trim();
                                if (!NetTeleport.ParseStation(where, out int st, out string left) || left.Length > 0)
                                    throw new Exception(string.Format(X("mpSelNoStation", "No station \"{0}\" (a number, a name or void)."), where));
                                if (off) quiet.Remove(st); else quiet.Add(st);
                                break;
                            }
                            case Op.StartQuest:
                            {
                                th.pc++;
                                string why = EventRunner.StartQuest(Fill(s.a), true);
                                if (why != null) throw new Exception(why);
                                break;
                            }
                            case Op.Scoreboard:
                                th.pc++;
                                scoreboardOn = s.a == "on";
                                scoreboardTitle = Fill(s.b ?? "");
                                scoreboardTimer = 0f;
                                if (!scoreboardOn) SendScoreboard("");
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Fail(string.Format(X("mpEventLine", "line {0}: {1}"), s.line, e.Message));
                        return;
                    }
                }
            }

            /// <summary>An "on" line: its handler listens from now on (the block starts at 'body').</summary>
            void AddHandler(Step s, int body)
            {
                var h = new Handler { trigger = s.a, body = body };
                if (!string.IsNullOrEmpty(s.b))
                {
                    if (!NetTeleport.ParseStation(Fill(s.b), out h.station, out string left) || (left.Length > 0 && h.trigger != "near"))
                        throw new Exception(string.Format(X("mpSelNoStation", "No station \"{0}\" (a number, a name or void)."), s.b));
                    if (h.trigger == "near")
                    {
                        // near <station> <x y z> <radius>: game coordinates, metres.
                        var c = left.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var v = new float[4];
                        if (c.Length != 4) throw new Exception(X("mpEventBadNear", "on near <station> <x y z> <metres>."));
                        for (int k = 0; k < 4; k++)
                            if (!float.TryParse(c[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k])) throw new Exception(X("mpEventBadNear", "on near <station> <x y z> <metres>."));
                        h.point = World.OrbitLayout.ToUnity(new Vector3(v[0], v[1], v[2]));
                        h.radius = Mathf.Max(1f, v[3]);
                    }
                }
                else if (h.trigger == "near") throw new Exception(X("mpEventBadNear", "on near <station> <x y z> <metres>."));
                handlers.Add(h);
            }

            /// <summary>An objective as shown: no control characters but new lines, at most 600 characters.</summary>
            static string ObjectiveText(string text)
            {
                var sb = new System.Text.StringBuilder();
                foreach (char c in text) if (c == '\n' || !char.IsControl(c)) sb.Append(c);
                string t = sb.ToString().Trim();
                return t.Length > 600 ? t.Substring(0, 600) : t;
            }

            static ScoreMode Mode(string word) => word == "kills" ? ScoreMode.Kills : word == "points" ? ScoreMode.Points : ScoreMode.Time;

            void RunCommand(string line)
            {
                int sp = line.IndexOf(' ');
                string name = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
                string args = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                var was = executing;
                executing = this;
                string answer;
                try { answer = NetCommands.RunOnServer(name, args, null); }
                finally { executing = was; }
                if (answer == null) throw new Exception(string.Format(X("mpEventUnknownCmd", "unknown command \"{0}\"."), name));
                if (answer.Length > 0) Debug.Log($"Server: event {running}: {name}: {answer.Replace('\n', ' ')}");
            }

            void Finish()
            {
                Debug.Log($"Server: the event {running} ended");
                Notify(string.Format(X("mpEventEnded", "The event {0} ended."), running));
                End();
            }

            void Fail(string why)
            {
                Debug.LogWarning($"Server: the event {running} stopped: {why}");
                Notify(string.Format(X("mpEventFailed", "The event {0} stopped: {1}"), running, why));
                End();
            }

            /// <summary>The admin who started it (when still here), else the server log only; a mission: its team.</summary>
            void Notify(string text)
            {
                if (team != null) { NoticeScope(text); return; }
                var p = starter != ulong.MaxValue ? EventHost.Find(starter) : null;
                if (p != null) EventHost.NoticeTo(p, text);
            }

            /// <summary>The automatic end: this event's ships exist, and nobody has been alive in space in their orbits for 3 s.</summary>
            bool CheckEmpty(float now)
            {
                if (batches.Count == 0 || (vars.TryGetValue("autostop", out double auto) && auto == 0)) { emptySince = -1f; return false; }
                if ((emptyCheck -= EventHost.DeltaTime) > 0f) return false;
                emptyCheck = CheckSeconds;
                var stations = new HashSet<int>();
                foreach (var ship in EventHost.EventShips())
                    if (batches.ContainsKey(ship.tag)) stations.Add(ship.station);
                // Ships that vanished without being destroyed (their game left the orbit) still hold their orbit, so the
                // players leaving it ends the event instead of leaving it waiting for good.
                foreach (var b in batches.Values)
                    if (b.seen.Count > b.down.Count) stations.UnionWith(b.stations);
                if (stations.Count == 0) { emptySince = -1f; return false; }   // nothing of the event's in space (yet)
                foreach (var p in EventHost.Pilots)
                    if (p != null && p.IsSpawned && InScope(p) && p.InSpace && p.Hull > 0f && stations.Contains(p.Station)) { emptySince = -1f; return false; }
                if (emptySince < 0f) { emptySince = now; return false; }
                if (now - emptySince < EmptySeconds) return false;
                if (team != null) { FailMission(""); return true; }   // a mission: its players gone from its orbits
                string name = running;
                Debug.Log($"Server: the event {name} ended: no players left in its orbits");
                var was = executing;
                executing = this;
                try { AnnounceWinner(scoreMode, X("mpEventOverTitle", "Event over")); }
                finally { executing = was; }
                NoticeScope(string.Format(X("mpEventOverNotice", "The event {0} ended: no players left in its orbit."), name));
                End();
                return true;
            }
        }
    }
}
