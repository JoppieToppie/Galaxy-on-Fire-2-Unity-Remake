// EventGraphImporter.cs
// Imports a .gof2netevent graph as a TextAsset of the file's own text: Resources.Load finds a graph under
// Resources/GoF2Net/Events and NetEvents reads it like a graph in a server's Events folder (EventGraphFile, EventGraphScript),
// so a built-in event is that one file, in the Editor and in builds.
// Each import also reads the file the way the server does and compares the script with Graph Toolkit's view of the graph
// (EventGraphCompiler): a difference means Graph Toolkit's file format changed (a Unity update) and EventGraphFile needs
// updating. The graph's problems and a script NetEvents can't run are import warnings.

using System.Collections.Generic;
using System.IO;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [ScriptedImporter(2, EventGraph.Extension)]
    public class EventGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string file = File.ReadAllText(ctx.assetPath);
            string name = Path.GetFileName(ctx.assetPath);
            var graph = GraphDatabase.LoadGraphForImporter<EventGraph>(ctx.assetPath);
            if (name.Contains(" ")) ctx.LogImportWarning($"{name}: /event takes one word: rename it without spaces (e.g. my_event).");
            if (graph != null && graph.NodeCount > 0)   // a new graph is saved once before its Start node is added
            {
                var problems = new List<EventGraphScript.Problem>();
                string script = EventGraphScript.FromFile(file, problems);
                foreach (var p in problems) ctx.LogImportWarning($"{name}: {(p.node != null ? p.node + ": " : "")}{p.message}");
                string error = NetEvents.Check(script);
                if (error != null) ctx.LogImportWarning($"{name}: the script doesn't compile: {error}");
                string editor = EventGraphCompiler.Compile(graph);
                if (editor != script)
                    ctx.LogImportWarning($"{name}: the game reads this graph differently from the Editor (Graph Toolkit's file format changed?): " +
                                         $"update EventGraphFile.\nEditor:\n{editor}\nGame:\n{script}");
            }
            var asset = new TextAsset(file) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("graph", asset);
            ctx.SetMainObject(asset);
        }
    }
}
