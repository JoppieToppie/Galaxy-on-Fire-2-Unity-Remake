// ProjectQuest.cs
// Remake-only: a quest or bar mission made in the Mod Builder: events/<name>.gof2quest, a JSON list of steps the game turns
// into an event graph's script when it loads it (Events.QuestFile; Modding/README.md "Quests made in the Mod Builder"):
//   { "format": "gof2quest", "title", "kind": "quest" | "mission", "startsWhen", "reward", "offer", "client",
//     "stations": [...], "players": [min, max], "once" (bar missions), "steps": [ { "type": "dialog", ... }, ... ] }
// The step types and their fields are QuestFile's. Like the other entries a JObject with typed accessors; the steps stay
// JObjects (the builder edits their fields by name). Plain C#.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public class ProjectQuest : ProjectEntry
    {
        // The same as Events.QuestFile's (a layer above); Legacy = from when these were called quests (still read).
        public const string Extension = "gof2mission", LegacyExtension = "gof2quest", Format = "gof2mission";
        public const string Folder = "events";

        /// <summary>Its file name (without .gof2mission): the name startquest, campaign.json's "quest" and "quest(name)" use.</summary>
        public string Name { get; set; }
        public string Ref => "quest:" + Name;

        public ProjectQuest(string name, JObject o) : base(o)
        {
            Name = name;
            json["format"] = Format;   // an old "gof2quest" one is written back as a .gof2mission
            if (!(json["steps"] is JArray)) json["steps"] = new JArray();
        }

        public string Title { get => S("title"); set => SetS("title", value); }
        /// <summary>"quest" (starts by itself), "mission" (a bar mission, offered in the Space Lounges) or "event" (started
        /// by /event or another's Next event step: multiplayer events, chains).</summary>
        public string Kind
        {
            get { string k = S("kind"); return k == "mission" || k == "event" ? k : "quest"; }
            set => json["kind"] = value == "mission" || value == "event" ? value : "quest";
        }
        public bool IsMission { get => Kind == "mission"; set => Kind = value ? "mission" : "quest"; }
        public bool IsEvent => Kind == "event";

        /// <summary>The quest's variables: [{ name, value, setting }] (a setting: /event name=value changes it).</summary>
        public JArray Variables => json["variables"] as JArray ?? (JArray)(json["variables"] = new JArray());

        /// <summary>No "variables" key when the list is empty.</summary>
        public void TidyVariables()
        {
            if (json["variables"] is JArray a && a.Count == 0) json.Remove("variables");
        }

        /// <summary>Every step, nested ones too (blocks hold lists of their own), with the list it sits in.</summary>
        public static IEnumerable<(JObject step, JArray list)> All(JArray steps, System.Func<JObject, IEnumerable<JArray>> children)
        {
            foreach (var st in steps.OfType<JObject>().ToList())
            {
                yield return (st, steps);
                foreach (var list in children(st))
                    foreach (var x in All(list, children)) yield return x;
            }
        }
        /// <summary>A quest's start condition: "" = at once, "never" = only when another starts it, else an expression.</summary>
        public string StartsWhen { get => S("startsWhen") ?? ""; set => SetS("startsWhen", value); }
        public int Reward { get => N("reward") ?? 0; set => SetN("reward", value <= 0 ? (int?)null : value); }
        /// <summary>A story mission that comes again: minutes of playing time after it was done (0 = once; "repeat").</summary>
        public int Repeat { get => N("repeat") ?? 0; set => SetN("repeat", value <= 0 ? (int?)null : value); }
        public string Offer { get => Text("offer"); set => SetText("offer", value); }
        public string Client { get => S("client"); set => SetS("client", value); }
        public string Description { get => Text("description"); set => SetText("description", value); }

        /// <summary>The stations a bar mission is offered at (numbers or keys; empty = every station).</summary>
        public List<string> Stations
        {
            get => json["stations"] is JArray a ? a.Select(RefText).Where(x => x != null).ToList() : new List<string>();
            set { if (value == null || value.Count == 0) json.Remove("stations"); else json["stations"] = new JArray(value.Select(RefToken)); }
        }

        public JArray Steps => (JArray)json["steps"];
        public List<JObject> StepList => Steps.OfType<JObject>().ToList();

        public JObject AddStep(string type, int at = -1)
        {
            var o = new JObject { ["type"] = type };
            if (at < 0 || at >= Steps.Count) Steps.Add(o); else Steps.Insert(at, o);
            return o;
        }

        public void RemoveStep(JObject step) => step.Remove();

        /// <summary>Moves a step up / down inside the list it is in (a block's or the quest's).</summary>
        public void MoveStep(JObject step, int delta)
        {
            if (!(step.Parent is JArray list)) return;
            int i = list.IndexOf(step), j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            step.Remove();
            list.Insert(j, step);
        }

        /// <summary>Inserts a step after another (in the same list), or at the end of a list.</summary>
        public static void InsertAfter(JArray list, JObject after, JObject step)
        {
            int i = after != null ? list.IndexOf(after) : -1;
            if (i < 0) list.Add(step); else list.Insert(i + 1, step);
        }
    }
}
