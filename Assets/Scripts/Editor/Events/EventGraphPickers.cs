// EventGraphPickers.cs
// Searchable pickers on the event graph's nodes that take a name (Graph Toolkit's NodeView: a button under the node): Spawn's
// ship or object, Give Item's item, Reward's items (added with " + "), Change Ship's hull, Get Orbit's and Teleport's station.
// The lists are the server's own (EventNames), so a picked name always resolves; typing still works and the compiler warns
// about names the server wouldn't know. A pick is one undo step.

using System;
using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.EditorTools
{
    public class EventSpawnNodeView : NodeView<EventSpawnNode>
    {
        public override void OnViewBuilt() => EventGraphPickers.Add(this, "Pick ship or object...", EventSpawnNode.Ship, () =>
        {
            var list = new List<(string, string)>();
            foreach (string s in EventNames.Ships()) list.Add(("Ships/" + s, s));
            foreach (string o in EventNames.Objects()) list.Add(("Objects/" + o, o));
            return list;
        });
    }

    public class EventGiveNodeView : NodeView<EventGiveNode>
    {
        public override void OnViewBuilt() => EventGraphPickers.Add(this, "Pick item...", EventGiveNode.Item, EventGraphPickers.Items);
    }

    public class EventRewardNodeView : NodeView<EventRewardNode>
    {
        public override void OnViewBuilt() => EventGraphPickers.Add(this, "Add item...", EventRewardNode.Items, EventGraphPickers.Items, append: true);
    }

    public class EventShipNodeView : NodeView<EventShipNode>
    {
        public override void OnViewBuilt() => EventGraphPickers.Add(this, "Pick hull...", EventShipNode.Ship,
            () => EventNames.Hulls().ConvertAll(h => (h, h)));
    }

    public class EventGetOrbitNodeView : NodeView<EventGetOrbitNode>
    {
        public override void OnViewBuilt() => EventGraphPickers.Add(this, "Pick station...", EventGetOrbitNode.Station, EventNames.Stations);
    }

    public class EventTeleportNodeView : NodeView<EventTeleportNode>
    {
        public override void OnViewBuilt() => EventGraphPickers.Add(this, "Pick station...", EventTeleportNode.Destination, EventNames.Stations);
    }

    public class EventStartEventNodeView : NodeView<EventStartEventNode>
    {
        public override void OnViewBuilt() => EventGraphPickers.Add(this, "Pick event...", EventStartEventNode.Event, () =>
        {
            // The project's event graphs (the built-ins under Resources/GoF2Net/Events first; templates aren't events).
            var list = new List<(string, string)>();
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("", new[] { "Assets" }))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("." + EventGraph.Extension) || path.Contains("/Editor/Events/Templates/")) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                list.Add((path.Contains("/Resources/GoF2Net/Events/") ? "Built in/" + name : "Project/" + name, name));
            }
            return list;
        });
    }

    public static class EventGraphPickers
    {
        public static List<(string, string)> Items() => EventNames.Items().ConvertAll(i => (i, i));

        /// <summary>A picker button under the node: the list (labels may hold "Group/" folders) sets the port's field.</summary>
        public static void Add<T>(NodeView<T> view, string text, string port, Func<List<(string label, string value)>> items, bool append = false) where T : Node
        {
            var button = new Button { text = text };
            var s = button.style;
            s.marginLeft = s.marginRight = 6;
            s.marginTop = 2;
            s.marginBottom = 6;
            s.fontSize = 11;
            button.clicked += () =>
            {
                List<(string, string)> list;
                try { list = items(); }
                catch (Exception e) { Debug.LogWarning($"Event graph: no names to pick ({e.Message})"); return; }
                new NamePicker(text.TrimEnd('.'), list, value => Set(view.Node, port, value, append)).Show(button.worldBound);
            };
            view.View.Root.Add(button);
        }

        static void Set(Node node, string port, string value, bool append)
        {
            var input = node.GetInputPortByName(port);
            var graph = node.Graph;
            if (input == null || graph == null) return;
            string current = "";
            input.TryGetValue(out current);
            string next = append && !string.IsNullOrWhiteSpace(current) ? current.Trim() + " + " + value : value;
            graph.UndoBeginRecordGraph("Pick " + port, new[] { node });
            input.TrySetValue(next);
            graph.UndoEndRecordGraph();
        }

        sealed class NamePicker : AdvancedDropdown
        {
            readonly string title;
            readonly List<(string label, string value)> items;
            readonly Action<string> picked;

            public NamePicker(string title, List<(string, string)> items, Action<string> picked) : base(new AdvancedDropdownState())
            {
                this.title = title;
                this.items = items;
                this.picked = picked;
                minimumSize = new Vector2(320, 420);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(title);
                var folders = new Dictionary<string, AdvancedDropdownItem>();
                for (int i = 0; i < items.Count; i++)
                {
                    string label = items[i].label;
                    var parent = root;
                    int slash = label.IndexOf('/');
                    if (slash > 0)
                    {
                        string folder = label.Substring(0, slash);
                        if (!folders.TryGetValue(folder, out parent)) root.AddChild(folders[folder] = parent = new AdvancedDropdownItem(folder));
                        label = label.Substring(slash + 1);
                    }
                    parent.AddChild(new AdvancedDropdownItem(label) { id = i });
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item.id >= 0 && item.id < items.Count) picked(items[item.id].value);
            }
        }
    }
}
