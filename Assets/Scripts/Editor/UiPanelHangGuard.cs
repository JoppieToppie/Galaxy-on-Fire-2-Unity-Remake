// UiPanelHangGuard.cs  (Editor only)
// Keeps the Editor from hanging in "Application.UpdateScene: Waiting for Unity's code to finish executing" after a code
// reload (Unity 7000.0.0a16, 2026-11-13; the stacks were read from the hung Editor with dotnet-stack):
//   1. After the reload a PanelRenderer puts its root element back into its panel (PanelRenderer.ReactToHierarchyChanges ->
//      AddRootVisualElementToTree -> VisualElement.Hierarchy.Insert). The root's layout node still names a parent from
//      before the reload, so LayoutNode.AddChild's Assert.IsTrue(nodeData.Parent.IsUndefined) throws: "AssertionException:
//      Assertion failure. Value was False".
//   2. Hierarchy.Insert had already added the root to the parent's child list (PutChildAtIndex) but not yet set the root's
//      parent (SetParent): the root is left in a child list of an element that isn't its parent.
//   3. The next panel update walks the tree with StyleSheets.HierarchyTraversal.Recurse, which only steps on to the next
//      child when child.hierarchy.parent == element: on the half-inserted root it never does. The main thread spins there
//      for good, and the Editor has to be killed.
// The guard listens for a logged exception (the log callback runs on the main thread, between the failed insert and
// the panel update), takes every half-inserted element out of its child list again (RemoveOrphans) and,
// in edit mode, has the PanelRenderer whose root it was build a new root (IPanelComponent.HandleLiveReload: a new root
// element has a clean layout node). Everything it reaches is internal to UI Toolkit, so it goes through reflection and
// does nothing when a name is gone (a later Unity). Delete this file once Unity's own reload no longer asserts there.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.EditorTools
{
    [InitializeOnLoad]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class UiPanelHangGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static readonly FieldInfo ChildrenField = typeof(VisualElement).GetField("m_Children", Any);
        static bool repairing;

        static UiPanelHangGuard()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            if (ChildrenField == null) Debug.LogWarning("UiPanelHangGuard: VisualElement.m_Children is gone, the guard is off (see the file's header).");
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            // Any logged exception: the walk over the panels is cheap next to an exception, and the stack text's form (or
            // whether there is one) is Unity's to change.
            if (repairing || (type != LogType.Exception && type != LogType.Assert)) return;
            repairing = true;
            try
            {
                var orphans = new List<VisualElement>();
                int removed = RepairPanels(orphans);
                if (removed == 0) return;
                Debug.LogWarning($"UiPanelHangGuard: {removed} half-inserted UI element(s) taken out of their panel after \"{FirstLine(condition)}\" "
                                 + "(the Editor would have hung in the next panel update; Assets/Scripts/Editor/UiPanelHangGuard.cs).");
                if (!EditorApplication.isPlaying) EditorApplication.delayCall += () => RebuildRoots(orphans);
            }
            catch (Exception e) { Debug.LogWarning("UiPanelHangGuard: the repair failed (" + e.Message + ")."); }
            finally { repairing = false; }
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end).TrimEnd();
        }

        /// <summary>Every runtime panel's tree repaired (RemoveOrphans); the elements taken out are added to 'orphans'.</summary>
        public static int RepairPanels(List<VisualElement> orphans = null)
        {
            int removed = 0;
            foreach (var tree in PanelTrees()) removed += RemoveOrphans(tree, orphans);
            return removed;
        }

        /// <summary>Takes every element under 'root' that sits in a child list of an element that isn't its parent (what an
        /// exception in the middle of VisualElement.Hierarchy.Insert leaves behind) out of that list; how many it took out.</summary>
        public static int RemoveOrphans(VisualElement root, List<VisualElement> orphans = null)
        {
            if (root == null || ChildrenField == null) return 0;
            int removed = 0;
            var seen = new HashSet<VisualElement>();
            var open = new Stack<VisualElement>();
            open.Push(root);
            while (open.Count > 0)
            {
                var e = open.Pop();
                if (!seen.Add(e)) continue;
                if (!(ChildrenField.GetValue(e) is List<VisualElement> children)) continue;
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    var c = children[i];
                    if (c != null && c.hierarchy.parent == e) { open.Push(c); continue; }
                    children.RemoveAt(i);
                    if (c != null) orphans?.Add(c);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>The root of every runtime panel (UIElementsRuntimeUtility.GetSortedPlayerPanels, internal).</summary>
        static IEnumerable<VisualElement> PanelTrees()
        {
            var utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            var panels = utility?.GetMethod("GetSortedPlayerPanels", Any, null, Type.EmptyTypes, null)?.Invoke(null, null) as IEnumerable;
            if (panels == null) yield break;
            var trees = new List<VisualElement>();
            foreach (var p in panels) if (p is IPanel panel && panel.visualTree != null) trees.Add(panel.visualTree);
            foreach (var t in trees) yield return t;
        }

        /// <summary>Edit mode, a moment after the repair: each PanelRenderer whose root was taken out builds a new one and
        /// puts it into its panel (PanelRenderer's IPanelComponent.HandleLiveReload). The old root can't go back in: its
        /// layout node still has the parent that made the insert fail.</summary>
        static void RebuildRoots(List<VisualElement> orphans)
        {
            if (orphans == null || orphans.Count == 0) return;
            try
            {
                var rootProperty = typeof(PanelRenderer).GetProperty("rootVisualElement", Any);
                var reload = FindInterfaceMethod(typeof(PanelRenderer), "HandleLiveReload");
                if (rootProperty == null || reload == null)
                {
                    Debug.LogWarning("UiPanelHangGuard: can't rebuild the UI (PanelRenderer's internals changed); open the scene again to see it.");
                    return;
                }
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<PanelRenderer>(FindObjectsInactive.Include))
                {
                    if (renderer == null || !orphans.Contains(rootProperty.GetValue(renderer) as VisualElement)) continue;
                    reload.Invoke(renderer, null);
                    Debug.Log($"UiPanelHangGuard: rebuilt the UI of \"{renderer.name}\".");
                }
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Debug.LogWarning("UiPanelHangGuard: couldn't rebuild the UI (" + inner.Message + "); open the scene again to see it.");
            }
        }

        /// <summary>An explicitly implemented interface method of 'type' by its short name ("IPanelComponent.HandleLiveReload").</summary>
        static MethodInfo FindInterfaceMethod(Type type, string name)
        {
            foreach (var m in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                if (m.GetParameters().Length == 0 && (m.Name == name || m.Name.EndsWith("." + name, StringComparison.Ordinal))) return m;
            return null;
        }
    }
}
