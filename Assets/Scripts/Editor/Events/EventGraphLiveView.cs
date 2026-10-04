// EventGraphLiveView.cs
// The event graphs' live view in Play mode: while an event runs (NetEvents.RunningName), its graph (the .gof2netevent asset
// of that name) shows where each of its flows is, with Graph Toolkit's visualization API: the running step's accent animates,
// a wait fills its bar as it goes (NetEvents.ActiveSteps: the steps carry their node's id), and each blackboard variable node
// shows the variable's current value on its port (NetEvents.Variables). Cleared when the event ends or Play mode stops.

using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using Unity.GraphToolkit.Editor.GraphVisualization;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [InitializeOnLoad]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class EventGraphLiveView
    {
        const double Interval = 0.15;

        static double next;
        static string shownEvent;
        static EventGraph graph;
        static Context context;
        static readonly HashSet<Hash128> lit = new HashSet<Hash128>();

        static EventGraphLiveView()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingPlayMode) Clear(); };
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying) { if (context != null) Clear(); return; }
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + Interval;
            string running = NetEvents.RunningName;
            if (running != shownEvent)
            {
                Clear();
                shownEvent = running;
                if (running != null) Attach(running);
            }
            if (context == null || !context.IsValid || graph == null) return;
            try { Refresh(); }
            catch (System.Exception e) { Debug.LogWarning($"Event graph live view: {e.Message}"); Clear(); }
        }

        /// <summary>The project's event graph named like the running event.</summary>
        static void Attach(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("." + EventGraph.Extension) || System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                graph = GraphDatabase.LoadGraph<EventGraph>(path);
                if (graph == null) continue;
                context = Registry.GetActiveContext(graph.ID) ?? Registry.CreateVisualizationContext(graph.ID);
                return;
            }
        }

        static void Refresh()
        {
            var now = new HashSet<Hash128>();
            foreach (var (node, progress) in NetEvents.ActiveSteps())
            {
                if (string.IsNullOrEmpty(node)) continue;
                var id = Hash128.Parse(node);
                if (!id.isValid) continue;
                now.Add(id);
                var reference = context.GetNodeReference(id);
                if (!lit.Contains(id)) context.Motion.Play(reference, 1f);
                reference.FillAmount = progress >= 0f ? progress * 100f : 0f;
            }
            foreach (var id in lit)
                if (!now.Contains(id)) context.GetNodeReference(id).ClearCustomization();
            lit.Clear();
            lit.UnionWith(now);

            var values = NetEvents.Variables();
            foreach (var n in graph.GetNodes())
            {
                if (!(n is IVariableNode v) || v.Variable == null) continue;
                foreach (var port in n.GetOutputPorts())
                {
                    string key = v.Variable.Name.Trim().ToLowerInvariant().Replace(' ', '_');
                    var reference = context.GetPortReference(port.ID);
                    if (values.TryGetValue(key, out double value)) reference.SetPreview(EventGraphScript.Number(value));
                    else reference.ClearPreview();
                }
            }
        }

        static void Clear()
        {
            if (context != null && context.IsValid) context.Dispose();
            context = null;
            graph = null;
            shownEvent = null;
            lit.Clear();
        }
    }
}
