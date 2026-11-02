// ModBuilderViewQuestFields.cs
// ModBuilderView: one quest step's fields (part of the partial ModBuilderView; its fields are in ModBuilderViewQuests.cs).

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
        void BuildStep(VisualElement card, ProjectQuest q, JObject st, QuestStepType def, string number, int depth)
        {
            if (!string.IsNullOrEmpty(def.what)) Note(card, T("mbStepWhat_" + def.type, def.what));
            foreach (var f in def.fields)
            {
                if (def.custom && f.key != "who") continue;
                if (!f.Shown(st)) continue;
                BuildField(card, q, st, f);
            }
            if (def.answers) BuildAnswers(card, st);
            if (def.branches) BuildBranches(card, st);
            if (def.custom) BuildCustomStep(card, q, st, number);
            else switch (def.type)
            {
                case "camera":
                    Note(card, T("mbPlaceNote", "A place: player [right up forward], station [x y z], x y z, ship <name>. 20 units = 1 m."));
                    break;
                case "shipgoto": case "shiproute": case "shipfollow": case "shipattack": case "shipaction":
                    Note(card, T("mbShipOrderNote", "The ship is named by a Spawn or Fight step (several: \"Name 1\", \"Name 2\"...)."));
                    break;
                case "on":
                    Note(card, T("mbOnNote", "Its steps run each time it happens, beside the rest; For: the player it happened to."));
                    break;
                case "command":
                    Note(card, T("mbCommandNote", "Any event command without \"/\": spawn @a 2 pirate 3 enemy, cutscene @a start, ..."));
                    break;
            }
        }
        void BuildField(VisualElement card, ProjectQuest q, JObject st, QuestField f)
        {
            string label = T("mbField_" + f.key, f.english);
            string S() => st[f.key] is JValue v && v.Type != JTokenType.Null ? v.ToString(CultureInfo.InvariantCulture) : null;
            string Cur() => S() ?? (f.def is JValue d && d.Type != JTokenType.Null ? d.ToString(CultureInfo.InvariantCulture) : null);
            void Set(JToken v, bool rebuild)
            {
                if (v == null || v.Type == JTokenType.Null || v.Type == JTokenType.String && ((string)v).Length == 0) st.Remove(f.key); else st[f.key] = v;
                QuestEdited(rebuild);
            }
            // Wired to a value node in the graph: the wire, not a field (× cuts it).
            if (QuestGraph.WiredValue(st[f.key]) is string wiredId)
            {
                Caption(card, label);
                var wrow = Row(card);
                var src = currentQuest != null ? QuestGraph.Value(currentQuest.Json, wiredId) : null;
                var probs = new List<string>();
                string expr = currentQuest != null ? QuestGraph.Resolve(currentQuest.Json, wiredId, probs.Add) : "?";
                Add(wrow, new Label($"◆ {(src != null ? T("mbValue_" + (string)src["type"], QuestGraph.GetValueType((string)src["type"])?.english ?? (string)src["type"]) : "?")}: {expr}"), "mbv-inline-label");
                Act(wrow, "×", () => Set(null, true), "mbv-step");
                return;
            }
            switch (f.kind)
            {
                case QuestFieldKind.Who:
                {
                    Caption(card, label);
                    var row = Row(card);
                    Add(row, new Label(WhoText(Cur() ?? "@a")), "mbv-inline-label");
                    Act(row, T("mbChoose", "Choose…"), () =>
                    {
                        var choices = WhoChoices.Select(w => (T("mbWho_" + w.value.TrimStart('@'), w.english) + "\n" + w.value, (Texture2D)null, (Action)(() => Set(w.value == "@a" ? null : w.value, true)))).ToList();
                        choices.Add((T("mbWhoCustom", "A selector…") + "\n@alive[orbit=78]", null, () =>
                            AskText(label, T("mbSelector", "A selector (@a, @alive[orbit=Var Hastra] ...)"), Cur() ?? "@a", v => Set(v.Trim() == "@a" ? null : v.Trim(), true), 80)));
                        ShowPicker(label, choices);
                    }, "mbv-step");
                    break;
                }
                case QuestFieldKind.Text:
                case QuestFieldKind.Variable:
                case QuestFieldKind.Xyz:
                case QuestFieldKind.Place:
                    Text(card, label, Cur(), 160, v => Set(v.Trim(), false));
                    break;
                case QuestFieldKind.LongText:
                    Text(card, label, Cur(), 600, v => Set(v, false), true);
                    break;
                case QuestFieldKind.Expression:
                    Text(card, label, Cur(), 200, v => Set(Num(v), false));
                    break;
                case QuestFieldKind.Condition:
                    ConditionField(card, label, Cur(), v => Set(v, false));
                    break;
                case QuestFieldKind.Int:
                case QuestFieldKind.Float:
                    if (f.optional || f.kind == QuestFieldKind.Float && (f.min < -1e8f || f.max > 1e8f) && f.min == float.MinValue)
                    {
                        var tf = Text(card, label, Cur(), 40, v => Set(string.IsNullOrWhiteSpace(v) ? null : Num(v), false));
                        tf.textEdition.placeholder = T("mbDefault", "default");
                    }
                    else if (f.kind == QuestFieldKind.Int)
                    {
                        int.TryParse(Cur() ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv);
                        Int(card, label, iv, (int)Math.Max(f.min, int.MinValue / 2), (int)Math.Min(f.max, int.MaxValue / 2), v => Set(v, false));
                    }
                    else
                    {
                        float.TryParse(Cur() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out float fv);
                        Float(card, label, fv, Math.Max(f.min, -1e9f), Math.Min(f.max, 1e9f), v => Set(Math.Round(v, 3), false));
                    }
                    break;
                case QuestFieldKind.Bool:
                {
                    bool on = st[f.key] is JValue b && b.Type == JTokenType.Boolean ? (bool)b : f.def is JValue d && d.Type == JTokenType.Boolean && (bool)d;
                    FlagButton(Row(card), label, on, v => Set(v, false), () => BuildQuestPage());
                    break;
                }
                case QuestFieldKind.Race when RaceFieldChoices(f).Count > f.values.Length:
                {
                    // Remake: the mods' own races after the game's, in the picker (Segments wouldn't fit them).
                    var choices = RaceFieldChoices(f);
                    string now = Cur() ?? "";
                    var shown = choices.FirstOrDefault(c => c.value == now);
                    Caption(card, label);
                    var row = Row(card);
                    Add(row, new Label(shown.value != null ? shown.label : now), "mbv-inline-label");
                    Act(row, T("mbChoose", "Choose…"), () => ShowPicker(label, choices.Select(c =>
                        (c.label, (Texture2D)null, (Action)(() => Set(c.value.Length == 0 ? null : c.value, true)))).ToList()), "mbv-step");
                    break;
                }
                case QuestFieldKind.Choice:
                case QuestFieldKind.Race:
                {
                    int cur = Math.Max(0, Array.IndexOf(f.values, Cur() ?? ""));
                    var labels = f.labels.Select((l, i) => T("mbChoice_" + f.key + "_" + f.values[i], l)).ToArray();
                    if (f.values.Length <= 5 || f.kind == QuestFieldKind.Race)
                        Segments(card, label, labels, cur, n => Set(f.values[n].Length == 0 ? null : f.values[n], true));
                    else
                    {
                        Caption(card, label);
                        var row = Row(card);
                        Add(row, new Label(labels[cur]), "mbv-inline-label");
                        Act(row, T("mbChoose", "Choose…"), () => ShowPicker(label, f.values.Select((v, i) =>
                            (labels[i], (Texture2D)null, (Action)(() => Set(v, true)))).ToList()), "mbv-step");
                    }
                    break;
                }
                case QuestFieldKind.Station:
                    RefField(card, label, RefKind.Station, Cur(), v => Set(string.IsNullOrEmpty(v) ? null : ProjectEntry.RefToken(v), true), f.optional ? T("mbNone", "none") : null, f.optional);
                    break;
                case QuestFieldKind.Ship:
                    RefField(card, label, RefKind.Ship, Cur(), v => Set(string.IsNullOrEmpty(v) ? null : ProjectEntry.RefToken(v), true), null, f.optional);
                    break;
                case QuestFieldKind.System:
                    RefField(card, label, RefKind.System, Cur(), v => Set(string.IsNullOrEmpty(v) ? null : ProjectEntry.RefToken(v), true), null, f.optional);
                    break;
                case QuestFieldKind.NpcShip:
                    RefField(card, label, RefKind.NpcShip, Cur(), v => Set(string.IsNullOrEmpty(v) ? null : v, true), null, f.optional);
                    break;
                case QuestFieldKind.Item:
                    RefField(card, label, RefKind.Item, Cur(), v => Set(string.IsNullOrEmpty(v) ? null : ProjectEntry.RefToken(v), true), null, f.optional);
                    break;
                case QuestFieldKind.Quest:
                    RefField(card, label, RefKind.Quest, Cur(), v => Set(v, true), clear: false, filter: e => e.value != q.Name);
                    break;
                case QuestFieldKind.Event:
                {
                    Caption(card, label);
                    var row = Row(card);
                    Add(row, new Label(Cur() ?? T("mbNone", "none")), "mbv-inline-label");
                    Act(row, T("mbChoose", "Choose…"), () =>
                    {
                        var choices = new List<(string, Texture2D, Action)> { (T("mbFromThisMod2", "This mod"), null, null) };
                        foreach (var other in Project.Quests.Where(x => x != q)) { string n = other.Name; choices.Add(((other.Title ?? n) + "\n" + n, null, () => Set(n, true))); }
                        foreach (var g in Project.EventGraphs()) { string n = g; choices.Add((n, null, () => Set(n, true))); }
                        choices.Add((T("mbFromGame", "The game"), null, null));
                        foreach (var b in EventRunner.List().Where(x => !Project.Quests.Any(o => o.Name == x) && !Project.EventGraphs().Contains(x))) { string n = b; choices.Add((n, null, () => Set(n, true))); }
                        ShowPicker(label, choices, true);
                    }, "mbv-step");
                    break;
                }
                case QuestFieldKind.Speaker:
                    SpeakerField(card, Cur(), v => Set(v, true), f.optional);
                    break;
                case QuestFieldKind.Music:
                {
                    Caption(card, label);
                    var row = Row(card);
                    Add(row, new Label(Cur() ?? "stop"), "mbv-inline-label");
                    Act(row, T("mbChoose", "Choose…"), () => PickMusicWord(v => Set(v, true)), "mbv-step");
                    break;
                }
                case QuestFieldKind.Sound:
                {
                    Caption(card, label);
                    var row = Row(card);
                    Add(row, new Label(Cur() ?? "?"), "mbv-inline-label");
                    Act(row, T("mbChoose", "Choose…"), () =>
                    {
                        var events = Events.EventAudio.Load();
                        var choices = Enum.GetValues(typeof(EventSound)).Cast<EventSound>().Select(s => (s.ToString(),
                            (Func<AudioClip>)(() => events?.Sound((int)s)), (Action)(() => Set(s.ToString().ToLowerInvariant(), true)))).ToList();
                        // The project's own new sounds (sounds/<name> not named like a game sound): played by name.
                        var own = Project.AudioFiles(ModProject.SoundsFolder).Where(f => !IsGameSound(f.name)).ToList();
                        if (own.Count > 0)
                        {
                            choices.Add((string.Format(T("mbFromThisMod", "This mod: {0}"), Project.Name ?? Project.Id), null, null));
                            foreach (var (n, _) in own)
                            {
                                string nm = n;
                                choices.Add((nm, () => ModSounds.Named(nm), () => Set(nm, true)));   // loaded with the project
                            }
                        }
                        ShowAudioPicker(label, choices, Settings.SfxVolume);
                    }, "mbv-step");
                    break;
                }
                case QuestFieldKind.ShipName:
                {
                    Text(card, label, Cur(), 32, v => Set(v.Trim(), false));
                    var names = ShipNames(q);
                    if (names.Count > 0)
                    {
                        var row = Row(card);
                        foreach (var n in names.Take(6)) { string nm = n; Act(row, nm, () => Set(nm, true), "mbv-step"); }
                    }
                    break;
                }
            }
            if (!string.IsNullOrEmpty(f.help)) Note(card, T("mbHelp_" + f.key, f.help));
        }
        /// <summary>The ship names the quest's Spawn and Fight steps give (for the ship orders).</summary>
        static List<string> ShipNames(ProjectQuest q)
        {
            var names = new List<string>();
            int enemies = 0;
            foreach (var (st, _) in ProjectQuest.All(q.Steps, Lists))
            {
                string type = (string)st["type"];
                if (type != "spawn" && type != "fight") continue;
                string n = (string)st["name"];
                if (string.IsNullOrWhiteSpace(n) && type == "fight") n = "Mission Enemy " + ++enemies;
                else if (type == "fight") enemies++;
                if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n.Trim())) names.Add(n.Trim());
            }
            return names;
        }
        void BuildAnswers(VisualElement card, JObject st)
        {
            var answers = st["answers"] as JArray ?? (JArray)(st["answers"] = new JArray());
            Caption(card, T("mbAnswers", "Answers (2 to 4; each runs the steps under it)"));
            int k = 0;
            foreach (var a in answers.OfType<JObject>().ToList())
            {
                var o = a;
                int n = ++k;
                var row = Row(card);
                Text(row, n + ".", (string)o["text"], 60, v => { o["text"] = v; QuestEdited(); });
                if (answers.Count > 2) Act(row, "×", () => { o.Remove(); QuestEdited(true); }, "mbv-step");
            }
            if (answers.Count < 4) Act(card, T("mbAddAnswer", "+ Answer"), () => { answers.Add(new JObject { ["text"] = "", ["steps"] = new JArray() }); QuestEdited(true); }, "mbv-add");
        }
        void BuildBranches(VisualElement card, JObject st)
        {
            var branches = st["branches"] as JArray ?? (JArray)(st["branches"] = new JArray());
            Caption(card, T("mbBranches", "Branches: one runs, the bigger its weight the likelier"));
            int k = 0;
            foreach (var b in branches.OfType<JObject>().ToList())
            {
                var o = b;
                int n = ++k;
                var row = Row(card);
                Float(row, string.Format(T("mbBranchWeight", "{0}: weight"), n), o["weight"] is JValue w && (w.Type == JTokenType.Integer || w.Type == JTokenType.Float) ? (float)w : 1f, 0f, 1000f,
                      v => { o["weight"] = Math.Round(v, 3); QuestEdited(); });
                if (branches.Count > 1) Act(row, "×", () => { o.Remove(); QuestEdited(true); }, "mbv-step");
            }
            Act(card, T("mbAddBranch", "+ Branch"), () => { branches.Add(new JObject { ["weight"] = 1, ["steps"] = new JArray() }); QuestEdited(true); }, "mbv-add");
        }
        /// <summary>The steps whose fields need their own page (pages, ships, items...).</summary>
        void BuildCustomStep(VisualElement card, ProjectQuest q, JObject st, string number)
        {
            string type = (string)st["type"];
            string S(string k) => st[k] is JValue v && v.Type != JTokenType.Null ? v.ToString() : null;
            void SetS(string k, string v) { if (string.IsNullOrEmpty(v)) st.Remove(k); else st[k] = v; QuestEdited(); }
            void SetRef(string k, string v) { if (string.IsNullOrEmpty(v)) st.Remove(k); else st[k] = ProjectEntry.RefToken(v); QuestEdited(true); }
            int I(string k, int d) => st[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (int)Math.Round((double)v) : d;
            string clipBase = q.Name + "_" + number.Replace('.', '_');
            switch (type)
            {
                case "dialog":
                {
                    var pages = st["pages"] as JArray ?? (JArray)(st["pages"] = new JArray());
                    int pn = 0;
                    foreach (var page in pages.OfType<JObject>().ToList())
                    {
                        var p = page;
                        int pi = pn++;
                        var box = Add(card, new VisualElement(), "mbv-page-box");
                        var prow = Row(box);
                        Add(prow, new Label(string.Format(T("mbPageN", "Page {0}"), pi + 1)), "mbv-inline-label");
                        if (pages.Count > 1) Act(prow, "×", () => { p.Remove(); QuestEdited(true); }, "mbv-step");
                        SpeakerField(box, (string)p["speaker"], v => { p["speaker"] = v; QuestEdited(true); });
                        Text(box, T("mbLine", "Text"), (string)p["text"], 600, v => { p["text"] = v; QuestEdited(); }, true);
                        VoiceField(box, clipBase + "_" + (pi + 1), (string)p["voice"], v => { if (v == null) p.Remove("voice"); else p["voice"] = v; QuestEdited(true); });
                    }
                    Act(card, T("mbAddPage", "+ Page"), () =>
                    {
                        string last = pages.OfType<JObject>().LastOrDefault()?["speaker"]?.ToString() ?? "player";
                        pages.Add(new JObject { ["speaker"] = last, ["text"] = "" });
                        QuestEdited(true);
                    }, "mbv-add");
                    Note(card, T("mbDialogNote", "%player% in a text is the player's name; {variable} shows a variable."));
                    break;
                }
                case "radio":
                    SpeakerField(card, S("speaker"), v => SetS("speaker", v));
                    Text(card, T("mbLine", "Text"), S("text"), 300, v => SetS("text", v), true);
                    VoiceField(card, clipBase, S("voice"), v => { SetS("voice", v); BuildQuestPage(); });
                    break;
                case "objective":
                    Text(card, T("mbObjectiveText", "Objective"), S("text"), 300, v => SetS("text", v), true);
                    RefField(card, T("mbTargetStation", "Target on the map"), RefKind.Station, S("station"), v => SetRef("station", v), T("mbNoTarget", "none"));
                    break;
                case "goto":
                    RefField(card, T("mbStation", "Station"), RefKind.Station, S("station"), v => SetRef("station", v), clear: false);
                    Segments(card, null, new[] { T("mbDocked", "Docked there"), T("mbInOrbit", "In its orbit") }, st["dock"]?.Type == JTokenType.Boolean && !(bool)st["dock"] ? 1 : 0,
                             n => { st["dock"] = n == 0; QuestEdited(true); });
                    Text(card, T("mbObjectiveOptional", "Objective meanwhile (optional)"), S("objective"), 300, v => SetS("objective", v));
                    break;
                case "fight":
                {
                    RefField(card, T("mbInOrbitOf", "In the orbit of"), RefKind.Station, S("station"), v => SetRef("station", v), T("mbWherever", "wherever the player is"));
                    var ships = st["ships"] as JArray ?? (JArray)(st["ships"] = new JArray());
                    foreach (var sh in ships.OfType<JObject>().ToList())
                    {
                        var o = sh;
                        var box = Add(card, new VisualElement(), "mbv-page-box");
                        var srow = Row(box);
                        Add(srow, new Label(RefLabel(RefKind.Ship, S2(o, "ship"), "?")), "mbv-inline-label");
                        Act(srow, T("mbChoose", "Choose…"), () => PickRef(RefKind.Ship, T("mbShip", "Ship"), e => { o["ship"] = ProjectEntry.RefToken(e.value); QuestEdited(true); }), "mbv-step");
                        Act(srow, "×", () => { o.Remove(); QuestEdited(true); }, "mbv-step");
                        Int(box, T("mbCount", "How many"), o["count"] is JValue c ? (int)c : 1, 1, 10, v => { o["count"] = v; QuestEdited(); });
                        string race = (string)o["race"] ?? "";
                        string[] races = { "", "pirate", "terran", "vossk", "nivelian", "midorian", "void" };
                        Segments(box, T("mbFlownBy", "Flown by"), new[] { T("mbItsMaker", "its maker"), "Pirates", "Terran", "Vossk", "Nivelian", "Midorian", "Void" },
                                 Math.Max(0, Array.IndexOf(races, race)), n => { if (n == 0) o.Remove("race"); else o["race"] = races[n]; QuestEdited(true); });
                        string beh = (string)o["behaviour"] ?? "enemy";
                        string[] behs = { "enemy", "neutral", "friendly" };
                        Segments(box, null, new[] { T("mbEnemy", "Enemy"), T("mbNeutral", "Neutral"), T("mbFriendly", "Friendly") }, Math.Max(0, Array.IndexOf(behs, beh)),
                                 n => { if (n == 0) o.Remove("behaviour"); else o["behaviour"] = behs[n]; QuestEdited(true); });
                    }
                    Act(card, T("mbAddShips", "+ Ships"), () => PickRef(RefKind.Ship, T("mbShip", "Ship"), e => { ships.Add(new JObject { ["ship"] = ProjectEntry.RefToken(e.value), ["count"] = 1 }); QuestEdited(true); }), "mbv-add");
                    Text(card, T("mbShipsName", "Their name (on the lock plate; ship orders use it)"), S("name"), 32, v => SetS("name", v));
                    var frow = Row(card);
                    FlagButton(frow, T("mbQuietOrbit", "No other traffic there"), st["quiet"]?.Type != JTokenType.Boolean || (bool)st["quiet"], v => st["quiet"] = v, () => BuildQuestPage());
                    Text(card, T("mbObjectiveOptional", "Objective meanwhile (optional)"), S("objective"), 300, v => SetS("objective", v));
                    Note(card, T("mbFightNote", "They arrive in front of the player in that orbit; the step ends when none is left."));
                    break;
                }
                case "cargo":
                    RefField(card, T("mbItem", "Item"), RefKind.Item, S("item"), v => SetRef("item", v), clear: false);
                    Int(card, T("mbAmount", "Amount"), I("amount", 1), 1, 100000, v => { st["amount"] = v; QuestEdited(); });
                    RefField(card, T("mbDockedAtOptional", "Docked at (optional)"), RefKind.Station, S("station"), v => SetRef("station", v), T("mbAnywhere", "anywhere"));
                    Text(card, T("mbObjectiveOptional", "Objective meanwhile (optional)"), S("objective"), 300, v => SetS("objective", v));
                    FlagButton(Row(card), T("mbCargoHandIn", "Hand it in (taken from the hold)"), st["take"]?.Type == JTokenType.Boolean && (bool)st["take"],
                               v => { if (v) st["take"] = true; else st.Remove("take"); }, () => QuestEdited(true));
                    break;
                case "reward":
                {
                    Int(card, T("mbCredits", "Credits"), I("credits", 0), 0, 999999999, v => { st["credits"] = v; QuestEdited(); });
                    var items = (st["items"] as JArray)?.OfType<JObject>().Select(o => new ProjectStack(ProjectEntry.RefText(o["item"]), (int?)o["amount"] ?? 1)).ToList() ?? new List<ProjectStack>();
                    StackList(card, T("mbItems", "Items"), items, l => { st["items"] = ProjectStack.Write(l); if (l.Count == 0) st.Remove("items"); QuestEdited(true); }, false);
                    Text(card, T("mbBoxTitle", "Title of the box (optional)"), S("title"), 60, v => SetS("title", v));
                    break;
                }
                case "checkpoint":
                    Text(card, T("mbCheckpointName", "Name (optional; a save remembers it)"), S("name"), 40, v => SetS("name", v?.Trim().Replace(' ', '_')));
                    Note(card, T("mbCheckpointNote", "Loading a save goes on from here; keep checkpoints on the mission's main line."));
                    break;
                case "startquest":
                    RefField(card, T("mbQuestField", "Mission"), RefKind.Quest, S("quest"), v => SetS("quest", v), clear: false, filter: e => e.value != q.Name);
                    break;
                case "complete":
                    Int(card, T("mbRewardOwn", "Reward (-1 = the mission's)"), I("reward", -1), -1, 999999999, v => { if (v < 0) st.Remove("reward"); else st["reward"] = v; QuestEdited(); });
                    Text(card, T("mbBoxTitle", "Title of the box (optional)"), S("title"), 60, v => SetS("title", v));
                    break;
                case "fail":
                    Text(card, T("mbFailText", "What the player is told"), S("title"), 120, v => SetS("title", v));
                    break;
            }
        }
        /// <summary>A speaker: a character (the project's own, the game's story characters, other mods'), or "race name".</summary>
        void SpeakerField(VisualElement parent, string value, Action<string> set, bool clear = false)
        {
            Caption(parent, T("mbSpeaker", "Speaker"));
            var row = Row(parent);
            Add(row, new Label(string.IsNullOrEmpty(value) ? T("mbNone", "none") : WhoLabel(value)), "mbv-inline-label");
            Act(row, T("mbChoose", "Choose…"), () => PickRef(RefKind.Character, T("mbSpeaker", "Speaker"), e => set(e.value)), "mbv-step");
            Act(row, T("mbRaceAndName", "A race and a name…"), () => AskText(T("mbRaceAndName", "A race and a name…"), T("mbRaceNameField", "e.g. vossk K'ekki, terran female Jane"), value, set, 60), "mbv-step");
            if (clear && !string.IsNullOrEmpty(value)) Act(row, "×", () => set(null), "mbv-step");
        }
        /// <summary>A page's voice: a sound file of the project (voices/), or the game's voice line by its name.</summary>
        void VoiceField(VisualElement parent, string clip, string value, Action<string> set)
        {
            var row = Row(parent);
            Add(row, new Label($"{T("mbVoice", "Voice")}: {value ?? T("mbNone", "none")}"), "mbv-inline-label");
            Act(row, T("mbVoiceFile", "Sound file…"), () => PickAudio(T("mbPickVoice", "Choose the voice line"), path =>
            {
                Project.SetAudio(ModProject.VoicesFolder, clip, path);
                set(clip);
            }), "mbv-step");
            Act(row, T("mbGameVoice", "Game's line…"), () => AskText(T("mbGameVoice", "Game's line…"), T("mbVoiceName", "The voice line's name (voices.md)"), value, set, 80), "mbv-step");
            if (value != null) Act(row, "×", () => set(null), "mbv-step");
        }
        void BuildStartsWhen(ProjectQuest q)
        {
            string w = q.StartsWhen.Trim();
            int mode = w.Length == 0 ? 0 : w == "never" ? 4 : Matches(w, @"^quest\((.+)\)\s*==\s*2$", out string qa) ? 1
                     : Matches(w, @"^visited\((.+)\)$", out string va) ? 2 : Matches(w, @"^campaign\s*>=\s*(\d+)$", out string ca) ? 3 : 5;
            Segments(questPage, T("mbStartsWhen", "Starts"), StartModes.Select(m => T("mbStart_" + m.Replace(" ", ""), m)).ToArray(), mode, n =>
            {
                q.StartsWhen = n switch { 0 => "", 4 => "never", 1 => $"quest({Project.Quests.FirstOrDefault(o => o != q)?.Name ?? "my_quest"}) == 2",
                                          2 => $"visited({Session.StationIndex})", 3 => "campaign >= 20", _ => w };
                QuestEdited(true);
            });
            switch (mode)
            {
                case 1:
                    Matches(w, @"^quest\((.+)\)\s*==\s*2$", out string after);
                    RefField(questPage, T("mbAfterQuest", "When this mission is done"), RefKind.Quest, after, v => { if (v != null) { q.StartsWhen = $"quest({v}) == 2"; QuestEdited(true); } }, clear: false,
                             filter: e => e.value != q.Name);
                    break;
                case 2:
                    Matches(w, @"^visited\((.+)\)$", out string st);
                    RefField(questPage, T("mbAfterVisit", "Once the player has docked at"), RefKind.Station, st, v => { if (v != null) { q.StartsWhen = $"visited({v})"; QuestEdited(true); } }, clear: false);
                    break;
                case 3:
                    Matches(w, @"^campaign\s*>=\s*(\d+)$", out string step);
                    Int(questPage, T("mbStoryStep", "From the story step"), int.TryParse(step, out int s) ? s : 20, 0, 162, v => { q.StartsWhen = $"campaign >= {v}"; QuestEdited(); });
                    Note(questPage, T("mbStoryStepNote", "The GoF2 story's step; -1 in free play and mod campaigns."));
                    break;
                case 5:
                    ConditionField(questPage, T("mbCondition", "Condition"), w, v => { q.StartsWhen = v ?? ""; QuestEdited(); });
                    break;
            }
        }
        static bool Matches(string s, string pattern, out string group)
        {
            var m = System.Text.RegularExpressions.Regex.Match(s ?? "", pattern);
            group = m.Success ? m.Groups[1].Value.Trim() : null;
            return m.Success;
        }
        void BuildMissionOffer(ProjectQuest q)
        {
            Text(questPage, T("mbOffer", "What the visitor says"), q.Offer, 600, v => { q.Offer = v; QuestEdited(); }, true);
            RefField(questPage, T("mbClient", "The client"), RefKind.Character, q.Client, v => { q.Client = v; QuestEdited(true); }, T("mbClientAny", "someone of the station's race"));
            Caption(questPage, T("mbOfferedAt", "Offered at"));
            var stations = q.Stations;
            var row = Row(questPage);
            Add(row, new Label(stations.Count == 0 ? T("mbEveryStation", "every station") : string.Join(", ", stations.Select(s => RefLabel(RefKind.Station, s, s)))), "mbv-inline-label");
            Act(row, "+", () => PickRef(RefKind.Station, T("mbOfferedAt", "Offered at"), e => { var l = q.Stations; if (!l.Contains(e.value)) l.Add(e.value); q.Stations = l; QuestEdited(true); }), "mbv-step");
            if (stations.Count > 0) Act(row, "×", () => { q.Stations = null; QuestEdited(true); }, "mbv-step");
            var players = q.Get("players") as JArray;
            int min = players != null && players.Count > 0 ? (int)players[0] : 1, max = players != null && players.Count > 1 ? (int)players[1] : 4;
            Int(questPage, T("mbMinPilots", "Pilots: at least"), min, 1, 16, v => { q.Set("players", new JArray(v, Math.Max(v, max))); QuestEdited(); });
            Int(questPage, T("mbMaxPilotsFull", "Pilots: at most"), max, 1, 16, v => { q.Set("players", new JArray(Math.Min(min, v), v)); QuestEdited(); });
            int chance = q.Get("chance") is JValue cv && (cv.Type == JTokenType.Integer || cv.Type == JTokenType.Float) ? (int)Math.Round((double)cv) : EventRunner.MissionInfo.DefaultChance;
            Int(questPage, T("mbOfferChance", "Bars offering it (%)"), chance, 0, 100, v => { q.Set("chance", v); QuestEdited(); });
            string takers = q.Get("takers")?.ToString() ?? "";
            var takerNames = new[] { T("mbTakersUniverse", "One squad in the universe"), T("mbTakersStation", "One squad per station"), T("mbTakersAny", "Any number of squads") };
            int takerIndex = takers == "station" ? 1 : takers == "any" ? 2 : 0;
            Caption(questPage, T("mbTakers", "Who can take it"));
            var takerRow = Row(questPage);
            for (int k = 0; k < 3; k++)
            {
                string value = k == 1 ? "station" : k == 2 ? "any" : null;
                FlagButton(takerRow, takerNames[k], k == takerIndex, _ => { q.Set("takers", value); QuestEdited(true); });
            }
            Note(questPage, T("mbTakersNote", "One squad in the universe: while a squad is on it, no bar anywhere offers it until they finish or fail it. One per station: only the bar it was taken at. Either way that bar waits 30 minutes before offering it again."));
        }
        /// <summary>A track for a Music step: the game's (the event words) or a mod's.</summary>
        void PickMusicWord(Action<string> pick)
        {
            var choices = new List<(string, Texture2D, Action)> { (T("mbStopMusic", "Stop the music"), null, () => pick("stop")), (T("mbFromGame", "The game"), null, null) };
            foreach (var m in Enum.GetNames(typeof(EventMusic))) { string w = m.ToLowerInvariant(); choices.Add((m, null, () => pick(w))); }
            string group = null;
            foreach (var e in ModRefs.List(RefKind.Music, Project).Where(e => e.group != ModRefs.GameGroup))
            {
                if (e.group != group) { group = e.group; choices.Add((e.own ? string.Format(T("mbFromThisMod", "This mod: {0}"), e.group) : string.Format(T("mbFromMod", "Mod: {0} (becomes a dependency)"), e.group), null, null)); }
                var en = e;
                choices.Add((e.name, null, () => { if (ModRefs.Use(Project, en)) RequestReload(false); pick(en.value); }));
            }
            ShowPicker(T("mbMusic", "Music"), choices);
        }
        void ShowScript(ProjectQuest q)
        {
            var problems = new List<string>();
            string script = QuestFile.Compile(q.ToJson(), problems);
            ShowScriptText(script, problems.Select(p => "× " + p).ToList());
        }
        void ShowScriptText(string script, List<string> problems)
        {
            modal.Clear();
            var box = Add(modal, new VisualElement(), "mbv-modal-box", "mbv-modal-box--list", "mbv-panel");
            Heading(box, T("mbScript", "The script the game runs"));
            foreach (var p in problems) Add(box, new Label(p), "mbv-note", "mbv-warn");
            var scroll = Add(box, new ScrollView(ScrollViewMode.Vertical), "mbv-picker-scroll");
            Add(scroll, new TextField { value = script, multiline = true, isReadOnly = true }, "mbv-field", "mbv-script");
            Act(box, T("mbClose", "Close"), CloseModal);
            modal.AddToClassList("mbv-modal--shown");
        }
        void PlayQuest(ProjectQuest q)
        {
            var problems = new List<string>();
            QuestFile.Compile(q.ToJson(), problems);
            if (problems.Count > 0) { Status("× " + problems[0], true); return; }
            SaveNow();
            ModBuilder.Reload();
            afterReload.Clear();
            string why = ModBuilderPlay.StartQuest(q.Name);
            if (why != null) Status(why, true);
        }
    }
}
