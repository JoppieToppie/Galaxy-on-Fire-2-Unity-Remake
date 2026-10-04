// EventGraphMenus.cs
// The event graphs' menu items. Project window: Assets > Create > GoF2 > Event Graph (a new graph with its Start node) and
// Event Graph From Template (a copy of Templates/<name> with new ids: Graph Toolkit keeps a graph's and its nodes' ids in the
// file, so a plain copy would share them with the template), and on
// a selected graph: export it (a copy of the file, to a place of your choice or the game's Events folder where /event finds
// it) and run it in Play mode. The graph window's right-click menu has the same "Event" actions on the graph as it is in the
// window (export saves it first).

using System.IO;
using System.Linq;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class EventGraphMenus
    {
        // ---- creating --------------------------------------------------------------------------------------------

        [MenuItem("Assets/Create/GoF2/Event Graph", false, 90)]
        static void CreateGraph()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(SelectedFolder(), "new_event." + EventGraph.Extension).Replace('\\', '/'));
            var graph = GraphDatabase.CreateGraph<EventGraph>(path);
            var start = new EventStartNode();
            graph.AddNode(start);
            start.Position = Vector2.zero;
            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        const string Templates = "Assets/Scripts/Editor/Events/Templates/";

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/King of the Hill", false, 91)]
        static void TemplateHill() => FromTemplate("king_of_the_hill");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Boss Fight", false, 92)]
        static void TemplateBoss() => FromTemplate("boss_fight");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Free For All", false, 93)]
        static void TemplateFreeForAll() => FromTemplate("free_for_all");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Pirate Base", false, 93)]
        static void TemplatePirateBase() => FromTemplate("pirate_base");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Pirate Hideout (Bar Mission)", false, 93)]
        static void TemplatePirateHideout() => FromTemplate("pirate_hideout");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Quiz", false, 93)]
        static void TemplateQuiz() => FromTemplate("quiz");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Race", false, 93)]
        static void TemplateRace() => FromTemplate("race");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Vote For The Next Event", false, 94)]
        static void TemplateVote() => FromTemplate("vote");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Waves", false, 94)]
        static void TemplateWaves() => FromTemplate("waves");

        [MenuItem("Assets/Create/GoF2/Event Graph From Template/Survival", false, 95)]
        static void TemplateSurvival() => FromTemplate("survival");

        public static string FromTemplate(string name)
        {
            string from = Templates + name + "." + EventGraph.Extension;
            if (!File.Exists(from)) { Debug.LogError("Event graph: no template " + from); return null; }
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(SelectedFolder(), name + "." + EventGraph.Extension).Replace('\\', '/'));
            File.WriteAllText(path, NewIds(File.ReadAllText(from)));
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            return path;
        }

        /// <summary>The graph file with every id renewed: each "m_Value0 / m_Value1" pair (graphs, nodes, variables) gets new
        /// random halves, and its Hash128 text (m_HashGuid, port references, sub-graph ports and links) follows.</summary>
        internal static string NewIds(string text)
        {
            var pair = new System.Text.RegularExpressions.Regex(@"m_Value0: (\d+)(\r?\n[ \t]*)m_Value1: (\d+)");
            var map = new System.Collections.Generic.Dictionary<(ulong, ulong), (ulong, ulong)>();
            var random = new System.Random();
            ulong Next() { var b = new byte[8]; random.NextBytes(b); return System.BitConverter.ToUInt64(b, 0); }
            text = pair.Replace(text, m =>
            {
                var old = (ulong.Parse(m.Groups[1].Value), ulong.Parse(m.Groups[3].Value));
                if (!map.TryGetValue(old, out var now)) map[old] = now = (Next(), Next());
                return $"m_Value0: {now.Item1}{m.Groups[2].Value}m_Value1: {now.Item2}";
            });
            foreach (var kv in map)
                text = text.Replace(new Hash128(kv.Key.Item1, kv.Key.Item2).ToString(), new Hash128(kv.Value.Item1, kv.Value.Item2).ToString());
            return text;
        }

        static string SelectedFolder()
        {
            string path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : "";
            if (string.IsNullOrEmpty(path)) return "Assets";
            return AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path);
        }


        // ---- a selected graph ------------------------------------------------------------------------------------

        static string SelectedGraph()
        {
            string path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : "";
            return path.EndsWith("." + EventGraph.Extension) ? path : null;
        }

        static bool LoadSelected(out EventGraph graph, out string name)
        {
            string path = SelectedGraph();
            graph = path != null ? GraphDatabase.LoadGraph<EventGraph>(path) : null;
            name = path != null ? Path.GetFileNameWithoutExtension(path) : null;
            return graph != null;
        }


        [MenuItem("Assets/GoF2/Event Graph/Export...")]
        static void ExportSelected() { if (LoadSelected(out var g, out string n)) Export(g, n); }

        [MenuItem("Assets/GoF2/Event Graph/Export To Events Folder")]
        static void ExportFolderSelected() { if (LoadSelected(out var g, out string n)) ExportToEvents(g, n); }

        [MenuItem("Assets/GoF2/Event Graph/Run In Play Mode")]
        static void RunSelected() { if (LoadSelected(out var g, out string n)) Run(g, n); }

        [MenuItem("Assets/GoF2/Event Graph/Export...", true)]
        [MenuItem("Assets/GoF2/Event Graph/Export To Events Folder", true)]
        static bool SelectedValid() => SelectedGraph() != null;

        [MenuItem("Assets/GoF2/Event Graph/Run In Play Mode", true)]
        static bool RunValid() => SelectedGraph() != null && Application.isPlaying;

        // ---- the graph window's right-click menu -----------------------------------------------------------------

        [GraphMenu(typeof(EventGraph))]
        static void GraphMenu(GraphMenuContext context)
        {
            var graph = context.Graph;
            if (graph == null) return;
            string name = string.IsNullOrEmpty(graph.Name) ? "event" : graph.Name;
            context.AppendSeparator("");
            context.AppendAction("Event/Export...", () => Export(graph, name));
            context.AppendAction("Event/Export To Events Folder", () => ExportToEvents(graph, name));
            if (Application.isPlaying)
            {
                context.AppendAction("Event/Run In Play Mode", () => Run(graph, name));
                if (NetEvents.Running) context.AppendAction("Event/Stop The Running Event", () => Debug.Log("Event graph: " + NetEvents.Command("stop", null)));
            }
        }

        // ---- the actions -----------------------------------------------------------------------------------------

        /// <summary>The graph's script, with its problems in the Console (null when it can't run).</summary>
        static string Script(Graph graph, bool logProblems = true)
        {
            var problems = new System.Collections.Generic.List<EventGraphScript.Problem>();
            string text = EventGraphCompiler.Compile(graph, problems);
            string error = NetEvents.Check(text);
            if (logProblems)
            {
                foreach (var p in problems.Where(p => !p.warning)) Debug.LogError($"Event graph {graph.Name}: {(p.node != null ? p.node + ": " : "")}{p.message}");
                foreach (var p in problems.Where(p => p.warning)) Debug.LogWarning($"Event graph {graph.Name}: {(p.node != null ? p.node + ": " : "")}{p.message}");
                if (error != null) Debug.LogError($"Event graph {graph.Name}: the script doesn't compile: {error}");
            }
            return error == null && problems.All(p => p.warning) ? text : null;
        }


        /// <summary>The graph's file, saved first (the window may hold unsaved changes); null when it doesn't run.</summary>
        static string SavedFile(Graph graph)
        {
            if (Script(graph) == null) return null;
            GraphDatabase.SaveGraph(graph);
            return GraphDatabase.GetGraphAssetPath(graph);
        }

        static void Export(Graph graph, string name)
        {
            string from = SavedFile(graph);
            if (from == null) return;
            string path = EditorUtility.SaveFilePanel("Export event graph", EventsFolder, name, EventGraph.Extension);
            if (string.IsNullOrEmpty(path)) return;
            File.Copy(from, path, true);
            Debug.Log($"Event graph {graph.Name}: exported to {path}");
        }

        static string EventsFolder => Path.Combine(Application.persistentDataPath, "Events");

        static void ExportToEvents(Graph graph, string name)
        {
            string from = SavedFile(graph);
            if (from == null) return;
            Directory.CreateDirectory(EventsFolder);
            string path = Path.Combine(EventsFolder, name + "." + EventGraph.Extension);
            File.Copy(from, path, true);
            Debug.Log($"Event graph {graph.Name}: exported to {path} (/event {name} runs it; it wins over a built-in event of that name).");
        }

        static void Run(Graph graph, string name)
        {
            string text = Script(graph);
            if (text == null) return;
            Debug.Log($"Event graph {graph.Name}: {NetEvents.StartText(name, text)}");
        }
    }
}
