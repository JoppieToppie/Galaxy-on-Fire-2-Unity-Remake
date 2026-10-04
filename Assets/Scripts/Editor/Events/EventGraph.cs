// EventGraph.cs
// The multiplayer events (NetEvents: /event <name>) as node graphs in Unity's Graph Toolkit. A .gof2netevent asset
// opens in the graph window (double click; Assets > Create > GoF2 > Event Graph makes one with its Start node). The graph
// file is the event: the server reads it itself (EventGraphFile + EventGraphScript, the game's compiler) from its Events
// folder or Resources/GoF2Net/Events (EventGraphImporter keeps the file's text). The right-click menu exports the graph (the
// game's Events folder) and runs it in a hosted Play-mode session.
// Wiring: the white arrows run the steps one after another from Start (If / While / Repeat run their branch, then Next);
// the data wires carry numbers and conditions (the script's expressions), a number wired into a text field becomes
// "{expression}". Script variables are the blackboard's Number variables, set to their default when the event starts.

using System;
using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Graph(Extension, GraphOptions.SupportsSubgraphs)]
    [Serializable]
    public class EventGraph : Graph
    {
        public const string Extension = NetEvents.GraphExtension;   // "gof2netevent"

        protected override IEnumerable<Type> BuildAvailableVariableTypes(IReadOnlyCollection<Type> baseSupportedTypes)
        {
            yield return typeof(float);
            yield return typeof(Flow);   // a sub-graph's In / Out (its flow in and out of it)
        }

        // No constant nodes (the ports' own fields hold the values; the server's reader doesn't know Graph Toolkit's constants).
        protected override IEnumerable<Type> BuildAvailableConstantTypes(IReadOnlyCollection<Type> baseSupportedTypes)
        {
            yield break;
        }

        // Into a text field may also go a number (written as {expression}), Get Players' players (a command's Players) and
        // Get Orbit's orbit (Teleport's Destination).
        public override bool IsConnectionAllowed(IPort output, IPort input) =>
            base.IsConnectionAllowed(output, input) || input.DataType == typeof(string)
            && (output.DataType == typeof(float) || output.DataType == typeof(Players) || output.DataType == typeof(Orbit));

        public override void OnGraphChanged(GraphLogger graphLogger)
        {
            var problems = new List<EventGraphScript.Problem>();
            string text = EventGraphCompiler.Compile(this, problems, out var nodes);
            if (problems.Count == 0 && NetEvents.Check(text) is string error)
                problems.Add(new EventGraphScript.Problem { message = "Script: " + error });
            foreach (var p in problems)
            {
                var node = p.nodeId != null && nodes.TryGetValue(p.nodeId, out var n) ? n : null;
                if (p.warning) graphLogger.LogWarning(p.message, node);
                else graphLogger.LogError(p.message, node);
            }
        }
    }

    /// <summary>The flow wires' type: no value, only the order of the steps.</summary>
    [Serializable]
    public struct Flow { }

    /// <summary>An orbit (Get Orbit -> Get Players / Teleport).</summary>
    [Serializable]
    public struct Orbit { }

    /// <summary>A group of players (Get Players -> a command's Players).</summary>
    [Serializable]
    public struct Players { }

    [DataTypeStyleMapper(typeof(EventGraph))]
    public class EventGraphStyles : DataTypeStyleMapper
    {
        public EventGraphStyles()
        {
            Register(typeof(Flow), new Color(0.92f, 0.92f, 0.92f));   // the built-in types keep Graph Toolkit's colours
            Register(typeof(Orbit), new Color(0.45f, 0.85f, 0.75f));
            Register(typeof(Players), new Color(0.85f, 0.55f, 0.95f));
        }
    }
}
