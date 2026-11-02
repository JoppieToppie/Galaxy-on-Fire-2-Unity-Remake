// EventGraphScriptSteps.cs
// EventGraphScript.Writer: the steps (part of the partial EventGraphScript.Writer; its fields are in EventGraphScript.cs).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    public static partial class EventGraphScript
    {
        partial class Writer
        {
            static bool IsStep(string kind) => kind != null && kind.StartsWith("Event") && !ValueKinds.Contains(kind);
            bool IsMain(EventGraphData.Node n) => graph.mainGraph == null || n.graph == null || n.graph == graph.mainGraph;
            EventGraphData.Node Following(EventGraphData.Node node, string exit) => outputs.TryGetValue((node.id, exit), out var n) ? n.node : null;
            bool Wired(EventGraphData.Node node, string exit) => outputs.ContainsKey((node.id, exit));
            void Chain(EventGraphData.Node from, string exit)
            {
                var node = Following(from, exit);
                while (node != null)
                {
                    if (node.kind == EventGraphData.SubgraphKind)
                    {
                        if (!visited.Add(node)) { Error(node, "Reached from two places: a step can follow only one other."); return; }
                        EnterSubgraph(node, outputs[(from.id, exit)].port);
                        return;   // the flow goes on from the sub-graph's flow output (LeaveSubgraph)
                    }
                    if (node.kind == EventGraphData.VariableKind)
                    {
                        LeaveSubgraph(node);
                        return;
                    }
                    if (!visited.Add(node)) { Error(node, "Reached from two places: a step can follow only one other."); return; }
                    if (!IsStep(node.kind)) { Error(node, "Not a step: only the Flow, Event and Command nodes go on the white arrows."); return; }
                    if (node.kind == "EventOnNode") { Error(node, "An On node starts its own flow: nothing leads into it."); return; }
                    var outer = marking;
                    marking = node;
                    Emit(node);
                    marking = outer;
                    from = node;
                    exit = "Next";
                    node = Following(node, "Next");
                }
            }
            /// <summary>A local sub-graph inlined: its flow from the input variable the flow came in by.</summary>
            void EnterSubgraph(EventGraphData.Node sub, string inPort)
            {
                if (!string.IsNullOrEmpty(Get(sub, EventGraphData.SubgraphAsset, false)))
                {
                    Error(sub, "A sub-graph saved as its own asset can't run on a server: use a local sub-graph (inside this graph).");
                    return;
                }
                if (callers.Contains(sub) || callers.Count > 16) { Error(sub, "A sub-graph inside itself."); return; }
                string subGraph = Get(sub, EventGraphData.SubgraphGraph, false);
                var entry = graph.nodes.FirstOrDefault(n => n.graph == subGraph && n.kind == EventGraphData.VariableKind
                                                            && Get(n, EventGraphData.VariableId, false) == inPort);
                if (entry == null) { Warning(sub, "Nothing inside the sub-graph uses that flow input."); return; }
                var exit = outputs.Keys.FirstOrDefault(k => k.Item1 == entry.id);
                if (exit.Item1 == null) { Warning(sub, "The sub-graph's flow input leads nowhere."); return; }
                callers.Push(sub);
                Chain(entry, exit.Item2);
                callers.Pop();
            }
            /// <summary>The flow reached a sub-graph's flow output: it goes on after the sub-graph's node, from that output.</summary>
            void LeaveSubgraph(EventGraphData.Node outNode)
            {
                string id = Get(outNode, EventGraphData.VariableId, false);
                if (callers.Count == 0 || !variablesById.TryGetValue(id, out var v) || !v.flow || v.kind != 2)
                {
                    Error(outNode, "Not a step: only a sub-graph's flow output ends its flow here.");
                    return;
                }
                var caller = callers.Pop();
                Chain(caller, id);
                callers.Push(caller);
            }
            void Block(EventGraphData.Node from, string exit)
            {
                var outer = marking;
                indent++;
                Chain(from, exit);
                indent--;
                marking = outer;
            }
            static string Join(params string[] parts) => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
            void Emit(EventGraphData.Node n)
            {
                switch (n.kind)
                {
                    case "EventCommentNode":
                        foreach (string line in Get(n, "Text").Replace("\r", "").Split('\n')) Line("# " + line.Trim());
                        break;
                    case "EventWaitNode":
                        Line("wait " + Expr(n, "Seconds"));
                        break;
                    case "EventWaitUntilNode":
                    {
                        string line = "wait until " + Expr(n, "Condition");
                        if (IsWired(n, "Timeout") || Num(n, "Timeout") > 0) line += " timeout " + Expr(n, "Timeout");
                        Line(line);
                        break;
                    }
                    case "EventIfNode":
                        Line("if " + Expr(n, "Condition"));
                        Block(n, "Then");
                        if (Wired(n, "Else")) { Line("else"); Block(n, "Else"); }
                        Line("end");
                        break;
                    case "EventWhileNode":
                        Line("while " + Expr(n, "Condition"));
                        Block(n, "Body");
                        Line("end");
                        break;
                    case "EventRepeatNode":
                    {
                        string counter = Get(n, "Counter");
                        Line("repeat " + Expr(n, "Count") + (string.IsNullOrWhiteSpace(counter) ? "" : " as " + VariableName(counter, n)));
                        Block(n, "Body");
                        Line("end");
                        break;
                    }
                    case "EventSetNode":
                    {
                        string name = VariableName(Get(n, "Variable"), n);
                        if (!variableNames.Contains(name)) Warning(n, $"No blackboard variable \"{name}\": nothing can read it.");
                        Line($"set {name} = {Expr(n, "Value")}");
                        break;
                    }
                    case "EventStopNode":
                        Line("stop");
                        break;
                    case "EventMissionCompleteNode":
                    {
                        string title = Text(n, "Title", false);
                        string reward = IsWired(n, "Reward") ? Expr(n, "Reward") : Num(n, "Reward") >= 0 ? Number(Num(n, "Reward")) : "";
                        Line(("complete " + reward).TrimEnd() + (title.Length > 0 ? " | " + title : ""));
                        break;
                    }
                    case "EventMissionFailedNode":
                        Line(("fail " + Text(n, "Title", false)).TrimEnd());
                        break;
                    case "EventWaitForeverNode":
                        Line("wait until 0");
                        break;
                    case "EventCheckpointNode":
                    {
                        string name = Clean(n, Get(n, "Name", false));
                        if (indent > 0) Error(n, "A Checkpoint goes on the main flow from Start, not inside If / While / Repeat / Run In Parallel / Every / Ask / On.");
                        if (name.Contains(" ")) name = name.Replace(' ', '_');
                        Line("checkpoint " + name);
                        break;
                    }
                    case "EventSetObjectiveNode":
                    {
                        Line(("objective " + Text(n, "Text", false)).TrimEnd());
                        string station = inputs.TryGetValue((n.id, "Orbit"), out var orbit)
                            ? (orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : Fail(n, "Orbit: only a Get Orbit node goes here.")) : "";
                        Line("target " + (station.Length > 0 ? station : "off"));
                        break;
                    }
                    case "EventQuestOrbitNode":
                    {
                        string station = inputs.TryGetValue((n.id, "Orbit"), out var orbit) && orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : "";
                        if (station.Length == 0) Error(n, "Wire a Get Orbit into Orbit: the mission's orbit.");
                        Line("questorbit " + station + (Bool(n, "On") ? "" : " off"));
                        break;
                    }
                    case "EventStartQuestNode":
                    {
                        string name = Text(n, "Quest");
                        if (name.Length == 0 || name.Contains(" ")) Error(n, "Start Mission needs a story mission's name (one word, the graph's file name).");
                        Line("startquest " + name);
                        break;
                    }
                    case "EventParallelNode":
                        Line("parallel");
                        if (!Wired(n, "Branch")) Warning(n, "Nothing on Branch.");
                        Block(n, "Branch");
                        Line("end");
                        break;
                    case "EventEveryNode":
                        Line("every " + Expr(n, "Seconds"));
                        if (!Wired(n, "Body")) Warning(n, "Nothing on Body.");
                        Block(n, "Body");
                        Line("end");
                        break;
                    case "EventRandomBranchNode":
                        RandomBranch(n);
                        break;
                    case "EventVoteNode":
                    case "EventAskNode":
                    {
                        bool vote = n.kind == "EventVoteNode";
                        string exit = vote ? "Choice " : "Answer ";
                        var answers = new List<string>();
                        for (int k = 1; k <= 4; k++)
                        {
                            string a = Text(n, "Answer " + k);
                            if (a.Length == 0) break;
                            answers.Add(a);
                        }
                        string question = Text(n, "Question"), speaker = Text(n, "Speaker", false);
                        if (question.Length == 0) Error(n, "Ask needs a question.");
                        if (speaker.Length > 0) question = speaker + " : " + question;
                        if (answers.Count < 2) Error(n, "Ask needs at least 2 answers (Answer 1, 2...).");
                        if ((question + string.Join("", answers)).IndexOf('|') >= 0) Error(n, "A question or answer can't hold |.");
                        for (int k = answers.Count + 1; k <= 4; k++)
                            if (Wired(n, exit + k)) Warning(n, $"Answer {k} has no text: its branch never runs.");
                        Line(Join(vote ? "vote" : "ask", Text(n, "Players"), Arg(n, "Seconds")) + " | " + question + " | " + string.Join(" | ", answers));
                        for (int k = 1; k <= answers.Count; k++)
                        {
                            if (!Wired(n, exit + k)) continue;
                            indent++;
                            Line((vote ? "choice " : "answer ") + k);
                            Block(n, exit + k);
                            Line("end");
                            indent--;
                        }
                        if (vote && Wired(n, "No Votes"))
                        {
                            indent++;
                            Line("choice 0");
                            Block(n, "No Votes");
                            Line("end");
                            indent--;
                        }
                        Line("end");
                        break;
                    }
                    case "EventAddPointsNode":
                        Line(Join("points", Text(n, "Players"), Expr(n, "Amount")));
                        break;
                    case "EventScoreboardNode":
                        Line(Bool(n, "Show") ? Join("scoreboard on", Text(n, "Title")) : "scoreboard off");
                        break;
                    case "EventFlyToNode":
                    case "EventFlyRouteNode":
                    {
                        string ship = ShipName(n);
                        bool route = n.kind == "EventFlyRouteNode";
                        string places = route
                            ? string.Join(" / ", Text(n, "Route", false).Split(new[] { '/', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => QuotePlace(n, x.Trim())).Where(x => x.Length > 0))
                            : Place(n, "To");
                        if (places.Length == 0) { Error(n, route ? "Route: the places to fly through, / between them." : "To: where it flies (x y z, station x y z, player ..., ship <name>)."); break; }
                        var then = (EventShipThen)Int(n, "Then");
                        string line = Join(ship, route ? "route" : "goto", places, route && Bool(n, "Loop") ? "loop" : "",
                            IsWired(n, "Speed") || Num(n, "Speed") > 0 ? "speed " + Arg(n, "Speed") : "", IsWired(n, "Radius") || Num(n, "Radius") > 0 ? "radius " + Arg(n, "Radius") : "",
                            Bool(n, "Fight") ? "fight" : "", "then " + (then == EventShipThen.JumpOut ? "jump" : then.ToString().ToLowerInvariant()));
                        CheckOrder(n, line);
                        Line(Join("npc", Text(n, "Players"), line));
                        break;
                    }
                    case "EventFollowNode":
                    {
                        string leader = Place(n, "Leader");
                        if (leader.Length == 0) leader = "player";
                        string offset = Text(n, "Offset", false).Trim();
                        string line = Join(ShipName(n), Bool(n, "Fight") ? "escort" : "follow", leader, offset.Length > 0 ? "offset " + offset : "",
                            IsWired(n, "Speed") || Num(n, "Speed") > 0 ? "speed " + Arg(n, "Speed") : "");
                        CheckOrder(n, line);
                        Line(Join("npc", Text(n, "Players"), line));
                        break;
                    }
                    case "EventAttackNode":
                    {
                        string target = Place(n, "Target");
                        if (target.Length == 0) target = "player";
                        string line = Join(ShipName(n), "attack", target);
                        CheckOrder(n, line);
                        Line(Join("npc", Text(n, "Players"), line));
                        break;
                    }
                    case "EventShipActionNode":
                    {
                        var a = (EventShipAction)Int(n, "Action");
                        string verb = a == EventShipAction.JumpOut ? "jump" : a.ToString().ToLowerInvariant();
                        string line = Join(ShipName(n), verb, a == EventShipAction.Flee ? "seconds " + Arg(n, "Seconds") : "");
                        CheckOrder(n, line);
                        Line(Join("npc", Text(n, "Players"), line));
                        break;
                    }
                    case "EventStartCutsceneNode":
                        Line(Join("cutscene", Text(n, "Players"), "start", Bool(n, "Letterbox") ? "" : "nobars",
                            Bool(n, "Freeze") ? "freeze" : "", Bool(n, "Invulnerable") ? "invulnerable" : ""));
                        break;
                    case "EventEndCutsceneNode":
                        Line(Join("cutscene", Text(n, "Players"), "end"));
                        break;
                    case "EventCameraShotNode":
                    {
                        string from = Place(n, "From"), to = Place(n, "To"), look = Place(n, "LookAt");
                        if (from.Length == 0) { Error(n, "From: where the camera is (player 0 600 -2500, station 0 0 20000, x y z, ship <name>)."); break; }
                        string shot = Join(from, to.Length > 0 ? "to " + to : "", "over " + Arg(n, "Seconds"), look.Length > 0 ? "look " + look : "",
                            Bool(n, "Follow") ? "follow" : "", IsWired(n, "Fov") || Num(n, "Fov") > 0 ? "fov " + Arg(n, "Fov") : "",
                            IsWired(n, "Shake") || Num(n, "Shake") > 0 ? "shake " + Arg(n, "Shake") : "");
                        if (!shot.Contains("{") && !shot.Contains("%") && !EventCutscene.TryParseShot(shot, out _, out string why)) Error(n, "The shot: " + why + ".");
                        Line(Join("camera", Text(n, "Players"), shot));
                        break;
                    }
                    case "EventChaseCameraNode":
                        Line(Join("camera", Text(n, "Players"), "chase"));
                        break;
                    case "EventFadeNode":
                    {
                        var how = (EventFade)Int(n, "Direction");
                        if (how == EventFade.Clear) { Line(Join("fade", Text(n, "Players"), "clear")); break; }
                        string colour = Text(n, "Colour", false).Trim().TrimStart('#');   // "#" would start a comment in the script
                        if (colour.Length > 0 && !UnityEngine.ColorUtility.TryParseHtmlString(colour.StartsWith("#") ? colour : "#" + colour, out _))
                            Error(n, "Colour: a colour like #000000 (black) or #ffffff (white).");
                        Line(Join("fade", Text(n, "Players"), how == EventFade.Out ? "out" : "in", Arg(n, "Seconds"), colour));
                        break;
                    }
                    case "EventLetterboxNode":
                        Line(Join("letterbox", Text(n, "Players"), Bool(n, "On") ? "on" : "off"));
                        break;
                    case "EventPlaySoundNode":
                    {
                        // A mod's sound by name (ModSounds) instead of the game's list.
                        string modSound = Clean(n, Get(n, "ModSound", false));
                        Line(Join("sound", Text(n, "Players"), modSound.Length > 0 ? modSound : ((EventSound)Int(n, "Sound")).ToString().ToLowerInvariant()));
                        break;
                    }
                    case "EventPlayMusicNode":
                    {
                        // A mod's track by name (ModMusic) instead of the game's list.
                        string track = Clean(n, Get(n, "Track", false));
                        Line(Join("music", Text(n, "Players"), track.Length > 0 ? track : ((EventMusic)Int(n, "Music")).ToString().ToLowerInvariant()));
                        break;
                    }
                    case "EventStopMusicNode":
                        Line(Join("music", Text(n, "Players"), "stop"));
                        break;
                    case "EventStartEventNode":
                    {
                        string name = Text(n, "Event");
                        if (name.Length == 0 || name.Contains(" ")) Error(n, "Start Event needs an event's name (one word, the graph's file name).");
                        Line(Join("startevent", name, Text(n, "Settings")));
                        if (Wired(n, "Next")) Warning(n, "Nothing after Start Event runs: this event ends there.");
                        break;
                    }
                    case "EventFreeForAllNode":
                        Line("pvp " + (Bool(n, "On") ? "on" : "off"));
                        break;
                    case "EventSetRespawnNode":
                    {
                        string station = inputs.TryGetValue((n.id, "Orbit"), out var orbit) && orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : "";
                        if (station.Length == 0) Error(n, "Wire a Get Orbit into Orbit: where the ships come back.");
                        Line(Join("respawn", Text(n, "Players"), station, Text(n, "At"), "spread " + Arg(n, "Spread"), "delay " + Arg(n, "Delay")));
                        break;
                    }
                    case "EventMakeHostileNode":
                    {
                        var race = (EventRace)Int(n, "Race");
                        string within = IsWired(n, "Within") || Num(n, "Within") > 0 ? "within " + Arg(n, "Within") : "";
                        Line(Join("provoke", Text(n, "Players"), race == EventRace.Maker ? "" : race.ToString().ToLowerInvariant(), within));
                        break;
                    }
                    case "EventSetWaypointNode":
                    {
                        string station = inputs.TryGetValue((n.id, "Orbit"), out var orbit) && orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : "";
                        string at = Text(n, "At");
                        if (station.Length == 0) Error(n, "Wire a Get Orbit into Orbit: the waypoint's orbit.");
                        if (at.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length != 3) Error(n, "At: the waypoint's game coordinates \"x y z\".");
                        Line(Join("waypoint", Text(n, "Players"), station, at));
                        break;
                    }
                    case "EventClearWaypointNode":
                        Line(Join("waypoint", Text(n, "Players"), "off"));
                        break;
                    case "EventOpenWormholeNode":
                    {
                        string station = inputs.TryGetValue((n.id, "Orbit"), out var orbit) && orbit.node.kind == "EventGetOrbitNode" ? Orbit(orbit.node) : "";
                        string to = inputs.TryGetValue((n.id, "Destination"), out var dest) && dest.node.kind == "EventGetOrbitNode" ? Orbit(dest.node) : "";
                        string at = Text(n, "At");
                        if (station.Length == 0) Error(n, "Wire a Get Orbit into Orbit: where the wormhole opens.");
                        if (to.Length == 0) Error(n, "Wire a Get Orbit into Destination: where flying in takes them.");
                        if (at.Trim().Length > 0 && at.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length != 3) Error(n, "At: game coordinates \"x y z\" (or empty).");
                        string seconds = IsWired(n, "Seconds") || Num(n, "Seconds") > 0 ? "for " + Arg(n, "Seconds") : "";
                        Line(Join("wormhole", Text(n, "Players"), station, at, "to", to, seconds));
                        break;
                    }
                    case "EventCloseWormholeNode":
                        Line(Join("wormhole", Text(n, "Players"), "close"));
                        break;
                    case "EventSystemNode":
                    {
                        string system = Text(n, "System");
                        if (system.Trim().Length == 0) Error(n, "System: the system's number, name or \"mod_id:id\".");
                        var action = (EventSystemAction)Int(n, "Action");
                        Line(Join("system", Text(n, "Players"), system, action.ToString().ToLowerInvariant()));
                        break;
                    }
                    case "EventClearRespawnNode":
                        Line(Join("respawn", Text(n, "Players"), "off"));
                        break;
                    case "EventFlightModelNode":
                    {
                        var model = (EventFlightModel)Int(n, "Model");
                        Line(Join("flightstyle", Text(n, "Players"), model == EventFlightModel.FreeFlight ? "free" : model == EventFlightModel.Original ? "original" : "player"));
                        break;
                    }
                    case "EventRestrictTravelNode":
                    {
                        bool jumps = Bool(n, "NoJumps"), docking = Bool(n, "NoDocking");
                        // Remake: No Khador Drive (older graphs have no such option: off); "jumps" covers it already.
                        string k = Get(n, "NoKhador", false).Trim();
                        bool khador = !jumps && (k == "1" || k.Equals("true", StringComparison.OrdinalIgnoreCase));
                        Line(Join("restrict", Text(n, "Players"), jumps ? "jumps" : "", docking ? "docking" : "", khador ? "khador" : "", !jumps && !docking && !khador ? "off" : ""));
                        break;
                    }
                    case "EventScoreNode":
                        Line("score " + ((EventScoreBy)Int(n, "By")).ToString().ToLowerInvariant());
                        break;
                    case "EventWinnerNode":
                    {
                        var by = (EventWinnerBy)Int(n, "By");
                        Line(by == EventWinnerBy.Score ? "winner" : "winner " + by.ToString().ToLowerInvariant());
                        break;
                    }

                    case "EventTitleNode":
                    {
                        string title = Text(n, "Title"), sub = Text(n, "Subtitle"), players = Text(n, "Players");
                        if (title.Length == 0 && sub.Length == 0) { Line(Join("title", players, "clear")); break; }
                        string line = Join("title", players, title);
                        if (sub.Length > 0) line += " | " + sub;
                        if (IsWired(n, "Seconds") || Num(n, "Seconds") > 0) line += " for " + Arg(n, "Seconds");
                        Line(line);
                        break;
                    }
                    case "EventTimerNode":
                        Line(Join("timer", Text(n, "Players"), Arg(n, "Seconds"), Text(n, "Label")));
                        break;
                    case "EventStopTimerNode":
                        Line(Join("timer", Text(n, "Players"), "stop"));
                        break;
                    case "EventSpawnNode":
                    {
                        string ship = Text(n, "Ship");
                        if (ship.Length == 0) Error(n, "Spawn needs a ship.");
                        else CheckName(n, ship, v => EventNames.CheckShip(v, true));
                        var race = (EventRace)Int(n, "Race");
                        string count = IsWired(n, "Count") || Math.Abs(Num(n, "Count") - 1) > 1e-4 ? Arg(n, "Count") : "";
                        string at = Text(n, "At");
                        // Older graphs have no Name port: none.
                        string name = Text(n, "Name", false);
                        if (name.Length > 0 && name.IndexOf('{') < 0 && name.IndexOf('%') < 0 && name.Length > 32)
                            Warning(n, "The name is cut to 32 characters.");
                        string named = name.Length > 0 ? "named " + name : "";
                        bool scenery = false;
                        try { scenery = ship.IndexOf('{') < 0 && EventNames.IsObject(ship); }
                        catch (Exception) { }   // no game data here
                        if (string.Equals(ship.Trim(), "wreck", StringComparison.OrdinalIgnoreCase))
                        {
                            // A hackable freighter wreck: a freighter race only, one, no behaviour ("on hacked" when hacked).
                            string wr = race == EventRace.Terran || race == EventRace.Vossk || race == EventRace.Nivelian || race == EventRace.Midorian
                                ? race.ToString().ToLowerInvariant() : "";
                            if (race != EventRace.Maker && wr.Length == 0) Warning(n, "A wreck is a Terran, Vossk, Nivelian or Midorian freighter: the race is rolled.");
                            Line(Join("spawn", Text(n, "Players"), "wreck", wr, named, at.Length > 0 ? "at " + at : ""));
                        }
                        else if (scenery)   // an object: /spawn takes its whole name (no race, count or behaviour)
                            Line(Join("spawn", Text(n, "Players"), ship, named, at.Length > 0 ? "at " + at : ""));
                        else
                        {
                            // Remake: Hull / Guns (older graphs have neither: as usual).
                            string Factor(string port) => IsWired(n, port) ? port.ToLowerInvariant() + " " + Arg(n, port)
                                : n.values.ContainsKey(port) && Num(n, port) > 0 && Math.Abs(Num(n, port) - 1) > 1e-4 ? port.ToLowerInvariant() + " " + Arg(n, port) : "";
                            Line(Join("spawn", Text(n, "Players"), ship, race == EventRace.Maker ? "" : race.ToString().ToLowerInvariant(), count,
                                ((EventBehaviour)Int(n, "Behaviour")).ToString().ToLowerInvariant(), Factor("Hull"), Factor("Guns"), named, at.Length > 0 ? "at " + at : ""));
                        }
                        break;
                    }
                    case "EventSpawnTypeNode":   // remake: /spawn type
                    {
                        string type = Text(n, "Type");
                        if (type.Length == 0 || type.Contains(" ")) Error(n, "Name the NPC ship type (one word: pirate_boss, mod_id:id...).");
                        string name = Text(n, "Name"), at = Text(n, "At");
                        Line(Join("spawn", Text(n, "Players"), "type", type, name.Length > 0 ? "named " + name : "", at.Length > 0 ? "at " + at : ""));
                        break;
                    }
                    case "EventRewardNode":
                    {
                        string credits = IsWired(n, "Credits") || Num(n, "Credits") != 0 ? Arg(n, "Credits") : "";
                        string items = Text(n, "Items"), title = Text(n, "Title");
                        foreach (string part in items.Split('+'))
                        {
                            // "<item> [amount]": the amount off the end.
                            string item = part.Trim();
                            int sp = item.LastIndexOf(' ');
                            if (sp > 0 && int.TryParse(item.Substring(sp + 1), out _)) item = item.Substring(0, sp).Trim();
                            if (item.Length > 0) CheckName(n, item, EventNames.CheckItem);
                        }
                        string what = credits.Length > 0 && items.Length > 0 ? credits + " + " + items : credits + items;
                        if (what.Length == 0) Error(n, "A reward needs credits or items.");
                        Line(Join("reward", Text(n, "Players"), what) + (title.Length > 0 ? " | " + title : ""));
                        break;
                    }
                    case "EventHealNode": Line(Join("heal", Text(n, "Players"))); break;
                    case "EventAmmoNode": Line(Join("ammo", Text(n, "Players"))); break;
                    case "EventKillNode": Line(Join("kill", Text(n, "Players"))); break;
                    case "EventRevealNode": Line(Join("reveal", Text(n, "Players"))); break;
                    case "EventPeaceNode": Line(Join("peace", Text(n, "Players"))); break;
                    case "EventGiveNode":
                        CheckName(n, Text(n, "Item"), EventNames.CheckItem);
                        Line(Join("give", Text(n, "Players"), Text(n, "Item"), Arg(n, "Amount"), Bool(n, "Mount") ? "mount" : ""));
                        break;
                    case "EventTakeNode":   // remake: /take, a hand-in
                    {
                        CheckName(n, Text(n, "Item"), EventNames.CheckItem);
                        string amount = IsWired(n, "Amount") || Num(n, "Amount") >= 1 ? Arg(n, "Amount") : "all";
                        Line(Join("take", Text(n, "Players"), Text(n, "Item"), amount, Bool(n, "Mounted") ? "mounted" : ""));
                        break;
                    }
                    case "EventCreditsNode":
                        Line(Join("credits", Text(n, "Players"), Arg(n, "Amount")));
                        break;
                    case "EventStandingNode":   // remake: /standing (NetAdminStanding)
                        if (Text(n, "Toward").Length == 0) Error(n, "Name the race.");
                        Line(Join("standing", Text(n, "Players"), Text(n, "Toward"), Arg(n, "Amount"), Bool(n, "Set") ? "set" : ""));
                        break;
                    case "EventTeleportNode":
                        Line(Join("tp", Text(n, "Players"), Text(n, "Destination")));
                        break;
                    case "EventShipNode":
                        CheckName(n, Text(n, "Ship"), EventNames.CheckHull);
                        Line(Join("ship", Text(n, "Players"), Text(n, "Ship")));
                        break;
                    case "EventCheatNode":
                        Line(Join("cheat", Text(n, "Players"), ((EventCheat)Int(n, "Flag")).ToString().ToLowerInvariant(), Bool(n, "On") ? "on" : "off"));
                        break;
                    case "EventRadioNode":
                    {
                        string speaker = Text(n, "Speaker"), text = Text(n, "Text");
                        if (speaker.Length == 0) Error(n, "Radio needs a speaker (a story character, a race and a name, or player).");
                        if (text.Length == 0) Error(n, "Radio needs a text.");
                        Line(Join("radio", Text(n, "Players"), speaker) + " : " + text);
                        break;
                    }
                    case "EventChatNode":
                        Line("g " + Text(n, "Text"));
                        break;
                    case "EventDialogNode":
                    {
                        var pages = new List<string>();
                        foreach (string line in Get(n, "Pages").Replace("\r", "").Split('\n'))
                            if (line.Trim().Length > 0) pages.Add(Clean(n, line));
                        if (pages.Count == 0) Error(n, "A dialog needs a page.");
                        Line(Join("dialog", Text(n, "Players"), string.Join(" | ", pages)));
                        break;
                    }
                    case "EventRawCommandNode":
                    {
                        string line = Clean(n, Get(n, "Line"));
                        if (line.StartsWith("/")) line = line.Substring(1);
                        if (line.Length == 0) Warning(n, "An empty command does nothing.");
                        else Line(line);
                        break;
                    }
                    case "EventStartNode":
                        Error(n, "Only one Start node runs; this one is ignored.");
                        break;
                    default:
                        Error(n, $"Unknown node \"{n.kind}\" (made by a newer version?).");
                        break;
                }
            }
        }
    }
}
