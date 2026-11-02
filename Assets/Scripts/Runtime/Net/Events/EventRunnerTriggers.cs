// EventRunnerTriggers.cs
// Part of EventRunner: EventRun's questions and votes (ask / vote, the answers, startevent's switch) and its
// triggers (the "on" handlers: detecting and firing them).

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
            // ---- questions ("ask") ----------------------------------------------------------------------------------

            /// <summary>Sends the question to the players the selector finds (NetAdmin.Order.Ask: "id US seconds US question US
            /// answers..."), returns its id (0: nobody to ask).</summary>
            int Ask(Step s, float now)
            {
                var players = NetCommands.FindTargets(Fill(s.a), null, out _, out string error);
                if (players.Count == 0 && error != null && error != NetCommands.NobodyMatches) throw new Exception(error);
                if (players.Count == 0) return 0;
                float seconds = Mathf.Max(0f, (float)Eval(Fill(s.b)));   // "20" or "{seconds}"
                var ask = new PendingAsk { id = nextAsk++, bodies = s.bodies, vote = s.op == Op.Vote, deadline = seconds > 0f ? now + seconds : float.PositiveInfinity };
                if (nextAsk > 1000000) nextAsk = 1;
                // The question may start with "<speaker> :" (NetAdmin.ResolveSpeaker, as /dialog): its spec travels with \u001e.
                string question = NetChat.Clean(Fill(s.parts[0])), speaker = "";
                int colon = NetAdmin.SplitSpeaker(question, new Dictionary<string, string>(), out string spec);
                if (colon > 0) { speaker = spec.Replace('\u001f', '\u001e'); question = question.Substring(colon + 1).Trim(); }
                var texts = new List<string> { ask.id.ToString(CultureInfo.InvariantCulture), Format(seconds), speaker, question.Replace('\u001f', ' ') };
                for (int k = 1; k < s.parts.Length; k++) texts.Add(NetChat.Clean(Fill(s.parts[k])).Replace('\u001f', ' '));
                string payload = string.Join("\u001f", texts);
                foreach (var p in players)
                {
                    ask.waiting.Add(p.OwnerClientId);
                    EventHost.Send(p.OwnerClientId, NetAdmin.Order.Ask, ask.id, 0, 0, payload, "Server");
                }
                asks[ask.id] = ask;
                return ask.id;
            }

            /// <summary>Everyone answered (or left), or the time is up.</summary>
            bool AskDone(PendingAsk ask, float now)
            {
                ask.waiting.RemoveWhere(id => EventHost.Find(id) == null);
                return ask.waiting.Count == 0 || now >= ask.deadline;
            }

            /// <summary>The question off the screens of those who didn't answer.</summary>
            void CloseAsk(int id)
            {
                if (!asks.TryGetValue(id, out var ask)) return;
                asks.Remove(id);
                if (!EventHost.ServerReady) return;
                foreach (var client in ask.waiting) EventHost.Send(client, NetAdmin.Order.Ask, -id, 0, 0, "", "Server");
            }

            void CloseAsks()
            {
                foreach (var id in new List<int>(asks.Keys)) CloseAsk(id);
                asks.Clear();
            }

            /// <summary>NetState.AnswerRpc (server): a player's answer (0 = skipped): its block runs with them as @trigger.</summary>
            internal void OnAnswer(ulong client, int id, int choice)
            {
                if (ended || !asks.TryGetValue(id, out var ask) || !ask.waiting.Remove(client)) return;
                if (choice >= 1 && choice <= ask.counts.Length) ask.counts[choice - 1]++;
                if (ask.vote) return;   // a vote's block runs once, when it is decided
                if (choice < 1 || choice > ask.bodies.Length || ask.bodies[choice - 1] < 0) return;
                if (threads.Count >= MaxThreads) { Debug.LogWarning($"Server: event {running}: an answer's block skipped (16 threads running)"); return; }
                var p = EventHost.Find(client);
                threads.Add(new Thread { pc = ask.bodies[choice - 1], subject = client, subjectName = p != null ? p.DisplayName : "" });
            }

            /// <summary>A decided vote: where its flow goes on (the most picked answer's block, a tie at random, else past it).</summary>
            int Winner(PendingAsk ask, int end)
            {
                int top = 0;
                var best = new List<int>();
                for (int k = 0; k < ask.counts.Length; k++)
                {
                    if (ask.counts[k] == 0) continue;
                    if (ask.counts[k] > top) { top = ask.counts[k]; best.Clear(); }
                    if (ask.counts[k] == top) best.Add(k);
                }
                Debug.Log($"Server: event {running}: vote {string.Join("/", ask.counts)}");
                if (best.Count == 0) return ask.bodies[4] >= 0 ? ask.bodies[4] : end;   // nobody voted: "choice 0"
                int pick = best[UnityEngine.Random.Range(0, best.Count)];
                return ask.bodies[pick] >= 0 ? ask.bodies[pick] : end;
            }

            /// <summary>startevent: this event ends and the named one starts with the settings, by the same starter.</summary>
            void SwitchTo(string name, string settingsText)
            {
                float now = EventHost.Now;
                if (now - switchSecond > 1f) { switchSecond = now; switches = 0; }
                if (++switches > 5) throw new Exception(X("mpEventSwitchLoop", "events start each other without a pause."));
                string text = Load(name, out string from, out string graphError);
                if (text == null) throw new Exception(string.Format(X("mpEventMissing", "No event \"{0}\". /event list shows them."), name));
                if (graphError != null) throw new Exception(name + ": " + graphError);
                var given = new Dictionary<string, double>();
                foreach (string w in settingsText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = w.IndexOf('=');
                    if (eq <= 0 || !double.TryParse(w.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        throw new Exception(string.Format(X("mpEventSettingForm", "Settings are name=number: \"{0}\"."), w));
                    given[w.Substring(0, eq).ToLowerInvariant()] = v;
                }
                var by = starter != ulong.MaxValue ? EventHost.Find(starter) : null;
                string was = running;
                Debug.Log($"Server: the event {was} starts {name}");
                // A lasting run hands its place to the next one: a quest is done, a bar mission's record goes to the new graph.
                GraphQuestState next = null;
                if (record != null)
                {
                    Session.GraphQuests.Remove(record);
                    if (record.kind == GraphQuestState.KindQuest) QuestDone(running, info);
                    next = new GraphQuestState { name = name, kind = record.kind, title = record.title, station = record.station, offer = record.offer };
                    Session.GraphQuests.Add(next);
                    record = null;
                }
                End();
                string answer = EventRunner.Start(name, text, from, by, given, team, station, next);
                NoticeScope(answer);
            }

            // ---- triggers ("on" handlers) ----------------------------------------------------------------------------

            Dictionary<ulong, PlayerState> Snapshot()
            {
                var map = new Dictionary<ulong, PlayerState>();
                foreach (var p in EventHost.Pilots)
                    if (p != null && p.IsSpawned && InScope(p))
                        map[p.OwnerClientId] = new PlayerState
                        {
                            inSpace = p.InSpace, dead = p.InSpace && p.Hull <= 0f, docked = p.InHangar, station = p.Station, name = p.DisplayName,
                        };
                return map;
            }

            /// <summary>Every 0.25 s: what changed for each player since the last look, and whether the event's enemies are all gone.</summary>
            void DetectTriggers()
            {
                if ((triggerTimer -= EventHost.DeltaTime) > 0f) return;
                triggerTimer = CheckSeconds;
                var now = Snapshot();
                foreach (var kv in now)
                {
                    var n = kv.Value;
                    if (!lastPlayers.TryGetValue(kv.Key, out var o)) { Fire("joined", kv.Key, n.name, n.station); continue; }
                    if (n.dead && !o.dead) Fire("died", kv.Key, n.name, n.station);
                    if (n.docked && !o.docked) Fire("docked", kv.Key, n.name, n.station);
                    if (n.inSpace && o.docked) Fire("launched", kv.Key, n.name, n.station);
                    if (n.inSpace && !n.dead && (!o.inSpace || o.station != n.station)) Fire("entered", kv.Key, n.name, n.station);
                    if (n.inSpace && !n.dead && o.dead) Fire("respawned", kv.Key, n.name, n.station);
                }
                foreach (var kv in lastPlayers)
                    if (!now.ContainsKey(kv.Key)) Fire("left", kv.Key, kv.Value.name, kv.Value.station);
                lastPlayers = now;
                // near: a player alive in space coming within the radius (again only after leaving it by a fifth more).
                foreach (var h in handlers)
                {
                    if (h.trigger != "near") continue;
                    foreach (var p in EventHost.Pilots)
                    {
                        if (p == null || !p.IsSpawned || !InScope(p)) continue;
                        bool here = p.InSpace && p.Hull > 0f && p.Station == h.station;
                        float d = here ? (p.Position - h.point).magnitude : float.MaxValue;
                        if (d <= h.radius && h.inside.Add(p.OwnerClientId)) StartHandler(h, p.OwnerClientId, p.DisplayName, "");
                        else if (d > h.radius * 1.2f) h.inside.Remove(p.OwnerClientId);
                    }
                }
                int enemies = batches.Count > 0 ? CountShips(true) : 0;
                if (lastEnemies > 0 && enemies == 0) Fire("cleared", ulong.MaxValue, "", -1);
                lastEnemies = enemies;
            }

            /// <summary>NetState.DestroyedByRpc (the server checked the kill): "on pvpkill" with the killer, the victim as %victim%.</summary>
            internal void OnPlayerKilled(IPilot killer, IPilot victim)
            {
                if (ended || killer == null || victim == null || !InScope(killer)) return;
                Fire("pvpkill", killer.OwnerClientId, killer.DisplayName, victim.Station, victim.DisplayName);
            }

            internal void OnWreckHacked(IPilot pilot, int station, string wreck)
            {
                if (ended || pilot == null || !InScope(pilot)) return;
                Fire("hacked", pilot.OwnerClientId, pilot.DisplayName, station, wreck ?? "");
            }

            internal void OnPadDocked(IPilot pilot, int station)
            {
                if (ended || pilot == null || !InScope(pilot)) return;
                Fire("pad", pilot.OwnerClientId, pilot.DisplayName, station);
            }

            /// <summary>A trigger happened: each handler listening for it (in that station, if limited) starts its block.</summary>
            void Fire(string trigger, ulong player, string name, int station, string other = "")
            {
                foreach (var h in handlers)
                {
                    if (h.trigger != trigger || (h.station >= 0 && h.station != station)) continue;
                    StartHandler(h, player, name, other);
                }
            }

            void StartHandler(Handler h, ulong player, string name, string other)
            {
                if (threads.Count >= MaxThreads) { Debug.LogWarning($"Server: event {running}: \"on {h.trigger}\" skipped (16 threads running)"); return; }
                threads.Add(new Thread { pc = h.body, subject = player, subjectName = name ?? "", otherName = other ?? "" });
            }
        }
    }
}
