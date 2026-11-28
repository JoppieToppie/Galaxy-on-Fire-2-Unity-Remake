using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Modding
{
    public static partial class ModScripts
    {
        // Native UI bindings are scoped to a render pass. No server commands or native
        // click handlers are replaced. Restore edits before the next native rebuild.
        sealed class StationUiPass : IDisposable
        {
            internal readonly VisualElement root;
            internal readonly string screen;
            readonly Func<string> page;
            internal readonly List<Action> undo = new List<Action>();
            internal string Page => page() ?? "";
            internal StationUiPass(VisualElement root, string screen, Func<string> page)
            { this.root = root; this.screen = screen; this.page = page; }

            internal void Reset()
            {
                for (int i = undo.Count - 1; i >= 0; --i)
                    try { undo[i](); } catch (Exception e) { Debug.LogWarning("Station UI restore: " + e.Message); }
                undo.Clear();
            }

            internal VisualElement Find(string name)
            {
                if (name == "$root") return root;
                if (string.IsNullOrEmpty(name)) return null;
                return root.name == name ? root : root.Q<VisualElement>(name);
            }

            internal void Change<T>(Func<T> get, Action<T> set, T value)
            {
                T previous = get();
                if (EqualityComparer<T>.Default.Equals(previous, value)) return;
                set(value);
                undo.Add(() => { if (EqualityComparer<T>.Default.Equals(get(), value)) set(previous); });
            }

            public void Dispose()
            {
                var old = activeStationUi;
                activeStationUi = this;
                try { Raise("stationUi", screen, Page); }
                finally
                {
                    activeStationUi = old;
                    if (undo.Count != 0)
                    {
                        stationUiPasses[root] = this;
                        root.RegisterCallback<DetachFromPanelEvent>(StationUiDetached);
                    }
                }
            }
        }

        static StationUiPass activeStationUi;
        static readonly Dictionary<VisualElement, StationUiPass> stationUiPasses = new Dictionary<VisualElement, StationUiPass>();

        static void StationUiDetached(DetachFromPanelEvent evt)
        {
            if (evt.target is VisualElement root && ReferenceEquals(evt.currentTarget, root))
                RestoreStationUi(root);
        }

        static void RestoreStationUi(VisualElement root)
        {
            if (root == null || !stationUiPasses.TryGetValue(root, out var pass)) return;
            stationUiPasses.Remove(root);
            root.UnregisterCallback<DetachFromPanelEvent>(StationUiDetached);
            pass.Reset();
        }

        static void ResetStationUi()
        {
            foreach (var root in new List<VisualElement>(stationUiPasses.Keys)) RestoreStationUi(root);
        }

        internal static IDisposable StationUiScope(VisualElement root, string screen, Func<string> page)
        {
            if (root == null) return null;
            RestoreStationUi(root);
            return new StationUiPass(root, screen, page);
        }

        sealed partial class Vm
        {
            void BuildStationUiApi(Table g)
            {
                var api = new Table(script);
                g["stationUi"] = api;
                string Arg(CallbackArguments a, int i) => a.AsType(i, "game.stationUi", DataType.String).String;
                VisualElement Find(CallbackArguments a) => activeStationUi?.Find(Arg(a, 0));

                Fn(api, "names", a =>
                {
                    var result = new Table(script);
                    var pass = activeStationUi;
                    if (pass != null)
                        foreach (var element in pass.root.Query<VisualElement>().ToList())
                            if (!string.IsNullOrEmpty(element.name)) result.Append(DynValue.NewString(element.name));
                    return DynValue.NewTable(result);
                });
                Fn(api, "text", a =>
                {
                    var pass = activeStationUi;
                    var element = Find(a);
                    // Preserve the native key/controller glyph children.
                    var text = element?.Q<Label>(className: "glyph-button-label") as TextElement ?? element as TextElement;
                    if (pass == null || text == null) return DynValue.False;
                    pass.Change(() => text.text, v => text.text = v, Arg(a, 1));
                    return DynValue.True;
                });
                Fn(api, "hide", a =>
                {
                    var pass = activeStationUi;
                    var element = Find(a);
                    if (pass == null || element == null || element == pass.root) return DynValue.False;
                    pass.Change(() => element.style.display, v => element.style.display = v, new StyleEnum<DisplayStyle>(DisplayStyle.None));
                    return DynValue.True;
                });
                Fn(api, "style", a =>
                {
                    var pass = activeStationUi;
                    var e = Find(a);
                    if (pass == null || e == null) return DynValue.False;
                    string property = Arg(a, 1);
                    double number = a.AsType(2, "game.stationUi.style", DataType.Number).Number;
                    if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number > 10000) return DynValue.False;
                    float n = (float)number;
                    var length = new StyleLength(n);
                    switch (property)
                    {
                        case "width": pass.Change(() => e.style.width, v => e.style.width = v, length); break;
                        case "height": pass.Change(() => e.style.height, v => e.style.height = v, length); break;
                        case "minWidth": pass.Change(() => e.style.minWidth, v => e.style.minWidth = v, length); break;
                        case "minHeight": pass.Change(() => e.style.minHeight, v => e.style.minHeight = v, length); break;
                        case "fontSize": pass.Change(() => e.style.fontSize, v => e.style.fontSize = v, length); break;
                        case "marginTop": pass.Change(() => e.style.marginTop, v => e.style.marginTop = v, length); break;
                        case "marginBottom": pass.Change(() => e.style.marginBottom, v => e.style.marginBottom = v, length); break;
                        case "marginLeft": pass.Change(() => e.style.marginLeft, v => e.style.marginLeft = v, length); break;
                        case "marginRight": pass.Change(() => e.style.marginRight, v => e.style.marginRight = v, length); break;
                        case "paddingTop": pass.Change(() => e.style.paddingTop, v => e.style.paddingTop = v, length); break;
                        case "paddingBottom": pass.Change(() => e.style.paddingBottom, v => e.style.paddingBottom = v, length); break;
                        case "paddingLeft": pass.Change(() => e.style.paddingLeft, v => e.style.paddingLeft = v, length); break;
                        case "paddingRight": pass.Change(() => e.style.paddingRight, v => e.style.paddingRight = v, length); break;
                        case "flexGrow": pass.Change(() => e.style.flexGrow, v => e.style.flexGrow = v, new StyleFloat(n)); break;
                        default: return DynValue.False;
                    }
                    return DynValue.True;
                });
                Fn(api, "addLabel", a => AddStationUiControl(a, false));
                Fn(api, "addButton", a => AddStationUiControl(a, true));
            }

            DynValue AddStationUiControl(CallbackArguments a, bool button)
            {
                var pass = activeStationUi;
                if (pass == null) return DynValue.False;
                var parent = pass.Find(a.AsType(0, "game.stationUi", DataType.String).String);
                string id = a.AsType(1, "game.stationUi", DataType.String).String;
                string text = a.AsType(2, "game.stationUi", DataType.String).String;
                if (parent == null || parent is TextElement || string.IsNullOrWhiteSpace(id) || id.Length > 80 || text.Length > 4096) return DynValue.False;
                string name = "mod-ui-" + mod.Id + "-" + id;
                if (pass.Find(name) != null) return DynValue.False;
                string screen = pass.screen, page = pass.Page;
                TextElement control;
                if (button)
                {
                    control = new Button(() =>
                    {
                        if (!stopped) Raise("stationUiAction", new object[] { id, screen, page });
                    });
                    if (screen == "station_management" || screen == "marketplace_listing") control.AddToClassList("player-station-action");
                    else if (screen == "station") control.AddToClassList("station-button");
                    else control.AddToClassList("detail-action");
                }
                else
                {
                    control = new Label();
                    if (screen == "station_management" || screen == "marketplace_listing") control.AddToClassList("player-station-note");
                    control.style.whiteSpace = WhiteSpace.Normal;
                }
                control.name = name;
                control.enableRichText = false;
                control.text = text;
                parent.Add(control);
                pass.undo.Add(() => control.RemoveFromHierarchy());
                return DynValue.True;
            }
        }
    }
}
