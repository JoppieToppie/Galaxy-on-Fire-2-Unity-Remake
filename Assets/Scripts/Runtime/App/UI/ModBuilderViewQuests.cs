// ModBuilderViewQuests.cs
// The Mod Builder's Quests kind (part of ModBuilderView), edited in the Space Lounge where missions are offered: the
// project's quests, bar missions and events as step trees (ProjectQuest, events/<name>.gof2quest; the game turns the steps
// into an event graph's script, Events.QuestFile; every step type and its fields are Events.QuestSteps'; Modding/README.md
// "Quests made in the Mod Builder"). Left: the quests, a new one of each kind, Remove, the event graphs made in the Unity
// Editor (each converted to steps, or kept as it is) and Import an event graph (a .gof2event, converted or copied in). Right:
// the title, the kind, when a quest starts, a bar mission's offer, client and stations, the reward, the variables; then the
// steps: blocks (If, While, Repeat, At the same time, Every, When something happens, Ask, Vote, Random) show their own lists
// under them, the picked step opens for editing (moved up / down inside its list, copied, removed), "+ Add a step" after it
// or into a block. Speakers, stations, ships, items and quests come from ModRefs: the game's, the project's own (its
// characters!) and other mods' (which become dependencies). Play starts a play test with it under way
// (ModBuilderPlay.StartQuest); the script it makes can be looked at.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        VisualElement questList, questPage;
        ProjectQuest currentQuest;
        string currentGraph;
        JObject currentStep;
        /// <summary>The node graph of the quest (the Graph view), between the panels.</summary>
        QuestGraphView graph;
        VisualElement graphTools;
        Button graphWideButton;
        IVisualElementScheduledItem graphRedraw;
        static bool GraphMode { get => PlayerPrefs.GetInt("mb_quest_graph", 1) == 1; set => PlayerPrefs.SetInt("mb_quest_graph", value ? 1 : 0); }
        static bool GraphWide { get => PlayerPrefs.GetInt("mb_quest_graph_wide", 0) == 1; set => PlayerPrefs.SetInt("mb_quest_graph_wide", value ? 1 : 0); }

        static string StepName(string type) => QuestSteps.Get(type) is QuestStepType t ? T("mbStep_" + type, t.english) : type;

        void BuildQuestsPanels(VisualElement left, VisualElement right)
        {
            questList = ListPanel(left, T("mbQuests", "Missions"));
            Act(left, T("mbNewQuest", "+ New story mission"), () => NewQuest("quest"), "mbv-add");
            Act(left, T("mbNewMission", "+ New bar mission"), () => NewQuest("mission"), "mbv-add");
            Act(left, T("mbNewEvent", "+ New event"), () => NewQuest("event"), "mbv-add");
            Act(left, "+ " + T("mbImportGraph", "Import an event graph…"), ImportGraph, "mbv-add");
            Act(left, T("mbRemoveQuest", "Remove"), AskRemoveQuest, "mbv-danger", "mbv-remove");
            questPage = PagePanel(right);
            BuildQuestGraph();
        }

        // ---- the page ---------------------------------------------------------------------------------------------

        void BuildQuestPage()
        {
            questPage.Clear();
            if (currentGraph != null) { BuildGraphPage(currentGraph); return; }
            var q = currentQuest;
            UpdateQuestGraph();
            if (q == null) { Note(questPage, T("mbPickQuest", "Add a story mission or a bar mission on the left to start.")); return; }
            if (GraphMode) { BuildGraphInspector(q); return; }
            var top = Row(questPage);
            Act(top, T("mbPlayQuest", "Play it"), () => PlayQuest(q), "mbv-play");
            Act(top, T("mbShowScript", "Show the script"), () => ShowScript(q));
            Act(top, T("mbGraphView", "Graph view"), () => { GraphMode = true; currentStep = null; BuildQuestPage(); });
            BuildQuestSettings(q);

            Heading(questPage, T("mbStepsHeading", "Steps"));
            if (currentStep != null && !ProjectQuest.All(q.Steps, Lists).Any(x => x.step == currentStep)) currentStep = null;
            BuildStepList(questPage, q, q.Steps, "", 0);
            if (q.Steps.Count == 0) Note(questPage, T("mbNoSteps", "No steps: add the first one."));
            var addList = currentStep?.Parent as JArray ?? q.Steps;
            Act(questPage, currentStep != null ? T("mbAddStepAfter", "+ Add a step after it") : T("mbAddStep", "+ Add a step"),
                () => PickStepType(q, addList, currentStep, addList != q.Steps), "mbv-add");
            int looseChains = (q.Json["loose"] as JArray)?.Count ?? 0;
            if (looseChains > 0) Note(questPage, string.Format(T("mbLooseListNote", "{0} chain(s) of steps wired to nothing are only in the graph view."), looseChains));
            Note(questPage, T("mbStepsNote", "Steps run in order; a block runs the steps under it. Put a Checkpoint after steps not to repeat on load."));
        }

        /// <summary>The quest's own settings: title, kind, start, offer, reward, variables.</summary>
        void BuildQuestSettings(ProjectQuest q)
        {
            Text(questPage, T("mbTitleField", "Title"), q.Title, 60, v => { q.Title = v.Trim(); QuestEdited(); RebuildQuests(); });
            Note(questPage, string.Format(T("mbQuestName", "Its name for other missions and the campaign: {0}"), q.Name));
            string[] kinds = { "quest", "mission", "event" };
            Segments(questPage, T("mbKind", "Kind"), new[] { T("mbQuest", "Story mission"), T("mbBarMission", "Bar mission"), T("mbEvent", "Event") }, Array.IndexOf(kinds, q.Kind),
                     n => { q.Kind = kinds[n]; QuestEdited(true); RebuildQuests(); });
            if (q.Kind == "quest")
            {
                BuildStartsWhen(q);
                Int(questPage, T("mbRepeatMinutes", "Comes again after (minutes of play, 0 = once)"), q.Repeat, 0, 100000, v => { q.Repeat = v; QuestEdited(); });
            }
            else if (q.Kind == "mission") BuildMissionOffer(q);
            else Note(questPage, T("mbEventKindNote", "An event runs for everyone when an admin starts it (/event <name>) or a Next event step does."));
            if (q.Kind != "event") Int(questPage, T("mbRewardCredits", "Reward (credits)"), q.Reward, 0, 999999999, v => { q.Reward = v; QuestEdited(); });
            BuildVariables(q);
            if (q.Kind != "event")
                FlagButton(Row(questPage), T("mbAutoComplete", "Completes after its last step"), !(q.Get("autoComplete") is JValue ac && ac.Type == JTokenType.Boolean && !(bool)ac),
                           v => { q.Set("autoComplete", v ? null : (JToken)false); QuestEdited(); });
        }

        void BuildVariables(ProjectQuest q)
        {
            Heading(questPage, T("mbVariables", "Variables"));
            foreach (var v in q.Variables.OfType<JObject>().ToList())
            {
                var o = v;
                var box = Add(questPage, new VisualElement(), "mbv-page-box");
                var row = Row(box);
                Add(row, new Label((string)o["name"] ?? "?"), "mbv-inline-label");
                Act(row, "×", () => { o.Remove(); q.TidyVariables(); QuestEdited(true); }, "mbv-step");
                Text(box, T("mbVariableStart", "Starts at (an expression)"), o["value"]?.ToString() ?? "0", 120, s => { o["value"] = Num(s); QuestEdited(); });
                FlagButton(Row(box), T("mbVariableSetting", "Setting (/event name=value changes it)"), o["setting"] is JValue sv && sv.Type == JTokenType.Boolean && (bool)sv,
                           b => { if (b) o["setting"] = true; else o.Remove("setting"); QuestEdited(); });
            }
            Act(questPage, T("mbAddVariable", "+ Variable"), () => AskText(T("mbAddVariable", "+ Variable"), T("mbVariableName", "Name (letters, digits, _)"), "", name =>
            {
                name = name.Trim().ToLowerInvariant();
                if (!QuestFile.IsName(name)) { Status(T("mbBadVariable", "A variable's name: letters, digits and _, not starting with a digit."), true); return; }
                if (q.Variables.OfType<JObject>().Any(x => (string)x["name"] == name)) return;
                q.Variables.Add(new JObject { ["name"] = name, ["value"] = 0 });
                QuestEdited(true);
            }), "mbv-add");
            Note(questPage, T("mbVariablesNote", "Numbers the mission remembers: set them, test them (kills > goal), show them as {wave}."));
        }

        static JToken Num(string s) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (Math.Abs(d - Math.Round(d)) < 1e-9 ? (JToken)(long)Math.Round(d) : d) : (JToken)(s ?? "").Trim();

        // ---- the step tree ------------------------------------------------------------------------------------

        void BuildStepList(VisualElement parent, ProjectQuest q, JArray list, string prefix, int depth)
        {
            var steps = list.OfType<JObject>().ToList();
            for (int i = 0; i < steps.Count; i++) StepCard(parent, q, steps[i], prefix + (i + 1), depth);
        }

        void StepCard(VisualElement parent, ProjectQuest q, JObject st, string number, int depth)
        {
            string type = (string)st["type"] ?? "";
            var def = QuestSteps.Get(type);
            bool open = st == currentStep;
            var card = Add(parent, new VisualElement(), "mbv-step-card");
            card.EnableInClassList("mbv-step-card--on", open);
            card.EnableInClassList("mbv-step-card--block", def != null && (def.blocks.Length > 0 || def.answers || def.branches));
            var head = new Button(() => { sound(false); currentStep = open ? null : st; BuildQuestPage(); }) { focusable = false };
            head.AddToClassList("mbv-step-head");
            Add(head, new Label($"{number}. {StepName(type)}") { pickingMode = PickingMode.Ignore }, "mbv-step-name", "gof-semibold");
            string summary = StepSummary(st);
            if (summary.Length > 0) Add(head, new Label(summary) { pickingMode = PickingMode.Ignore }, "mbv-step-summary");
            card.Add(head);
            if (open)
            {
                var tools = Row(card);
                Act(tools, "▲", () => { q.MoveStep(st, -1); QuestEdited(true); }, "mbv-step");
                Act(tools, "▼", () => { q.MoveStep(st, 1); QuestEdited(true); }, "mbv-step");
                Act(tools, T("mbDuplicate", "Copy"), () =>
                {
                    var copy = (JObject)st.DeepClone();
                    ProjectQuest.InsertAfter((JArray)st.Parent, st, copy);
                    currentStep = copy;
                    QuestEdited(true);
                    RebuildQuests();
                }, "mbv-step");
                Act(tools, T("mbDelete", "Delete"), () => { st.Remove(); currentStep = null; QuestEdited(true); RebuildQuests(); }, "mbv-danger");
                if (def == null) Note(card, string.Format(T("mbUnknownStep", "\"{0}\" isn't a step this game knows."), type));
                else BuildStep(card, q, st, def, number, depth);
            }
            // A block's lists, always shown, each with its own "+ Add a step".
            if (def == null) return;
            foreach (var (label, list) in QuestSteps.Children(st))
            {
                var holder = Add(card, new VisualElement(), "mbv-step-children");
                Add(holder, new Label(label.ToUpperInvariant()), "mbv-label");
                BuildStepList(holder, q, list, number + ".", depth + 1);
                var l = list;
                Act(holder, T("mbAddStepHere", "+ Add a step here"), () => PickStepType(q, l, null, true), "mbv-add", "mbv-add--small");
            }
        }

        /// <summary>The step types by group; one goes after 'after' in 'list' (at its end without one).</summary>
        void PickStepType(ProjectQuest q, JArray list, JObject after, bool insideBlock)
        {
            var choices = new List<(string, Texture2D, Action)>();
            foreach (var (gk, gEnglish) in QuestSteps.Groups)
            {
                bool head = false;
                foreach (var t in QuestSteps.Types.Where(x => x.group == gk))
                {
                    if (t.topOnly && insideBlock) continue;
                    if (!head) { choices.Add((T("mbStepGroup_" + gk, gEnglish), null, null)); head = true; }
                    string type = t.type;
                    choices.Add(($"{StepName(type)}\n{T("mbStepWhat_" + type, t.what)}", null, () =>
                    {
                        var st = QuestSteps.New(type);
                        NewStepDefaults(st, type);
                        ProjectQuest.InsertAfter(list, after, st);
                        currentStep = st;
                        QuestEdited(true);
                        RebuildQuests();
                    }));
                }
            }
            ShowPicker(T("mbAddStep", "+ Add a step"), choices, true);
        }

        /// <summary>The step types by group for the graph: 'done' gets the new step; 'cut' adds a first tile that cuts the
        /// wire instead.</summary>
        void PickStepFor(Action<JObject> done, Action cut, bool insideBlock)
        {
            var choices = new List<(string, Texture2D, Action)>();
            if (cut != null) choices.Add((T("mbCutWire", "Cut the wire") + "\n" + T("mbCutWireWhat", "What it led to is wired to nothing"), null, cut));
            foreach (var (gk, gEnglish) in QuestSteps.Groups)
            {
                bool head = false;
                foreach (var t in QuestSteps.Types.Where(x => x.group == gk))
                {
                    if (t.topOnly && insideBlock) continue;
                    if (!head) { choices.Add((T("mbStepGroup_" + gk, gEnglish), null, null)); head = true; }
                    string type = t.type;
                    choices.Add(($"{StepName(type)}\n{T("mbStepWhat_" + type, t.what)}", null, () =>
                    {
                        var st = QuestSteps.New(type);
                        NewStepDefaults(st, type);
                        done(st);
                    }));
                }
            }
            ShowPicker(T("mbAddStep", "+ Add a step"), choices, true);
        }

        void NewStepDefaults(JObject st, string type)
        {
            switch (type)
            {
                case "dialog": st["pages"] = new JArray(new JObject { ["speaker"] = "player", ["text"] = "" }); break;
                case "radio": st["speaker"] = "player"; st["text"] = ""; break;
                case "goto": st["station"] = ProjectEntry.RefToken(Session.StationIndex.ToString()); st["dock"] = true; break;
                case "fight": st["station"] = ProjectEntry.RefToken(Session.StationIndex.ToString()); st["ships"] = new JArray(new JObject { ["ship"] = 2, ["count"] = 3, ["race"] = "pirate" }); st["quiet"] = true; break;
                case "cargo": st["item"] = 22; st["amount"] = 10; break;
                case "reward": st["credits"] = 5000; break;
                case "waypoint": case "questorbit": case "teleport": st["station"] = ProjectEntry.RefToken(Session.StationIndex.ToString()); break;
                case "spawn": st["ship"] = 2; st["race"] = "pirate"; st["count"] = 2; st["name"] = "Raider"; break;
                case "shipgoto": case "shiproute": case "shipfollow": case "shipattack": case "shipaction": st["ship"] = "Raider"; break;
                case "on": st["trigger"] = "died"; break;
                case "ask": case "vote": st["question"] = ""; break;
            }
        }

        string StepSummary(JObject st)
        {
            string S(string k) => st[k] is JValue v && v.Type != JTokenType.Null ? v.ToString(CultureInfo.InvariantCulture) : "";
            string Short(string s) => s.Length > 70 ? s.Substring(0, 67) + "…" : s;
            string type = S("type");
            var def = QuestSteps.Get(type);
            string who = S("who").Length > 0 && S("who") != "@a" ? "  ·  " + WhoText(S("who")) : "";
            switch (type)
            {
                case "dialog":
                    var pages = (st["pages"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                    return pages.Count == 0 ? "" : Short($"{WhoLabel(S2(pages[0], "speaker"))}: {S2(pages[0], "text")}") + (pages.Count > 1 ? $"  (+{pages.Count - 1})" : "") + who;
                case "radio": return Short($"{WhoLabel(S("speaker"))}: {S("text")}") + who;
                case "objective": return Short(S("text"));
                case "goto": return (st["dock"]?.Type == JTokenType.Boolean && !(bool)st["dock"] ? T("mbToOrbit", "the orbit of ") : T("mbDockAt", "dock at ")) + RefLabel(RefKind.Station, S("station"), "?");
                case "fight":
                    var ships = (st["ships"] as JArray)?.OfType<JObject>().Select(o => $"{(int?)o["count"] ?? 1} × {RefLabel(RefKind.Ship, S2(o, "ship"), "?")}") ?? Enumerable.Empty<string>();
                    return Short(string.Join(", ", ships) + "  ·  " + RefLabel(RefKind.Station, S("station"), T("mbWherever", "wherever the player is")));
                case "cargo": return $"{S("amount")} × {RefLabel(RefKind.Item, S("item"), "?")}";
                case "reward": return $"{S("credits")} $" + ((st["items"] as JArray)?.Count > 0 ? " + " + string.Format(T("mbItemsCount", "{0} items"), ((JArray)st["items"]).Count) : "") + who;
                case "give": return $"{(S("amount").Length > 0 ? S("amount") : "1")} × {RefLabel(RefKind.Item, S("item"), "?")}" + who;
                case "complete": return S("reward").Length > 0 ? S("reward") + " $" : "";
                case "fail": return Short(S("title"));
                case "if": case "while": case "waituntil": return Short(S("condition"));
                case "repeat": return S("count") + " ×" + (S("as").Length > 0 ? "  (" + S("as") + ")" : "");
                case "every": case "wait": return S("seconds") + " s";
                case "on":
                {
                    var f = def?.Field("trigger");
                    int i = f != null ? Array.IndexOf(f.values, S("trigger")) : -1;
                    string t = i >= 0 ? T("mbTrigger_" + S("trigger"), f.labels[i]) : S("trigger");
                    return t + (S("station").Length > 0 ? "  ·  " + RefLabel(RefKind.Station, S("station"), S("station")) : "");
                }
                case "ask": case "vote": return Short(S("question")) + "  (" + ((st["answers"] as JArray)?.Count ?? 0) + ")" + who;
                case "random": return string.Format(T("mbBranchesCount", "{0} branches"), (st["branches"] as JArray)?.Count ?? 0);
                case "set": return $"{S("variable")} = {S("value")}";
                case "note": return Short(S("text"));
                case "command": return Short(S("line"));
            }
            if (def == null) return "";
            // A plain step: its first few values.
            var parts = new List<string>();
            foreach (var f in def.fields)
            {
                if (f.key == "who" || !f.Shown(st) || !(st[f.key] is JValue v) || v.Type == JTokenType.Null) continue;
                string s = v.ToString(CultureInfo.InvariantCulture);
                if (s.Length == 0) continue;
                switch (f.kind)
                {
                    case QuestFieldKind.Station: s = RefLabel(RefKind.Station, s, s); break;
                    case QuestFieldKind.System: s = RefLabel(RefKind.System, s, s); break;
                    case QuestFieldKind.NpcShip: s = RefLabel(RefKind.NpcShip, s, s); break;
                    case QuestFieldKind.Ship: s = RefLabel(RefKind.Ship, s, s); break;
                    case QuestFieldKind.Item: s = RefLabel(RefKind.Item, s, s); break;
                    case QuestFieldKind.Bool: s = (bool)v ? T("mb_" + f.key, f.english) : ""; break;
                    case QuestFieldKind.Choice:
                        int i = Array.IndexOf(f.values, s);
                        if (i >= 0 && i < f.labels.Length) s = f.labels[i];
                        break;
                    case QuestFieldKind.Race:   // remake: a mod race's key reads as its name (RaceFieldChoices)
                        var rc = RaceFieldChoices(f).FirstOrDefault(c => c.value == s);
                        if (rc.value != null) s = rc.label;
                        break;
                }
                if (s.Length > 0) parts.Add(s);
                if (parts.Count >= 3) break;
            }
            return Short(string.Join("  ·  ", parts)) + who;
        }

        static string S2(JObject o, string k) => o[k] is JValue v && v.Type != JTokenType.Null ? v.ToString() : "";

        /// <summary>A speaker for people: a character's name ("Captain Vega"), a story speaker's, "Keith (the player)".</summary>
        string WhoLabel(string speaker) => string.IsNullOrEmpty(speaker) ? "?" : RefLabel(RefKind.Character, speaker, speaker);

        static readonly (string value, string english)[] WhoChoices =
        {
            ("@a", "Everyone the mission is for"), ("@trigger", "The player a When block is about"), ("@alive", "Players alive in space"),
            ("@docked", "Players docked"), ("@survivors", "Players never destroyed since the fight began"), ("@r", "A random player"),
        };

        static string WhoText(string who) => WhoChoices.FirstOrDefault(w => w.value == who) is var c && c.value != null ? T("mbWho_" + c.value.TrimStart('@'), c.english) : who;

        // ---- one step's fields ----------------------------------------------------------------------------------

        static readonly string[] StartModes = { "At once", "After a mission", "After a visit", "After a story step", "Never", "Condition" };

    }
}
