// MainMenuNavigation.cs
// Part of MainMenu: keyboard and controller navigation: OnNavigate, the Game options and Multiplayer panels' own
// rules, and the focus helpers (Focusables, FocusFirst, EnsureVisible...).

using System;
using System.Collections;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        void OnNavigate(NavigationMoveEvent e)
        {
            pointerDriven = false;
            if (identityOverlay != null && identityOverlay.parent == root)
            {
                var current = root.focusController?.focusedElement as VisualElement;
                if (TextFieldKeys.IsTyping(e, current)) { Swallow(e); return; }
                var choices = Focusables(identityOverlay).FindAll(element => element is Button || element is Toggle || element is TextField);
                if (choices.Count > 0)
                {
                    int dx = e.direction == NavigationMoveEvent.Direction.Left ? -1 : e.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
                    int dy = e.direction == NavigationMoveEvent.Direction.Up ? -1 : e.direction == NavigationMoveEvent.Direction.Down ? 1 : 0;
                    var identityNext = current == null || !choices.Contains(current) ? choices[0] :
                        (dx == 0 && dy == 0 ? current : SpatialNav.Next(choices, current, dx, dy));
                    identityNext?.Focus();
                }
                Swallow(e);
                return;
            }
            if (screen != MenuState.Menu) return;
            var focused = root.focusController?.focusedElement as VisualElement;
            // Typing into a field (the address, the code, a name): W A S D and the side arrows are letters and the cursor.
            if (TextFieldKeys.IsTyping(e, focused))
            {
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            SetTouchMode(false);
            bool vertical = e.direction == NavigationMoveEvent.Direction.Up || e.direction == NavigationMoveEvent.Direction.Down;
            bool horizontal = e.direction == NavigationMoveEvent.Direction.Left || e.direction == NavigationMoveEvent.Direction.Right;
            if (!vertical && !horizontal) return;

            if (horizontal && focused is ChoiceRow choiceRow)
            {
                choiceRow.Step(e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1);
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            if (horizontal && focused is BindingRow bindingRow)
            {
                bindingRow.Step(e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1);
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }

            // Sliders and toggles keep left/right for themselves.
            if (horizontal && focused is BaseField<float> || horizontal && focused is BaseField<int>) return;

            // The Debug panel's two columns (#24): left / right jump between the mission list and the cheats (a controller had
            // to step through all 160 missions to reach them), back to where the focus was in that column.
            if (horizontal && focused != null && panels.TryGetValue("debugPanel", out var debugPanel) && openPanel == debugPanel)
            {
                var missionsColumn = debugPanel.Q(className: "debug-column--missions");
                var cheatsColumn = debugPanel.Q(className: "debug-column--cheats");
                bool inMissions = missionsColumn != null && missionsColumn.Contains(focused);
                bool inCheats = cheatsColumn != null && cheatsColumn.Contains(focused);
                var to = e.direction == NavigationMoveEvent.Direction.Right && inMissions ? cheatsColumn
                       : e.direction == NavigationMoveEvent.Direction.Left && inCheats ? missionsColumn : null;
                if (to != null)
                {
                    if (inMissions) debugMissionFocus = focused; else debugCheatFocus = focused;
                    var remembered = to == cheatsColumn ? debugCheatFocus : debugMissionFocus;
                    var columnItems = Focusables(to).FindAll(v => !(v is TextField));
                    var target = remembered != null && columnItems.Contains(remembered) ? remembered : columnItems.Count > 0 ? columnItems[0] : null;
                    if (target != null) { target.Focus(); EnsureVisible(target); }
                    e.StopPropagation();
                    root.focusController?.IgnoreEvent(e);
                    return;
                }
            }

            if (NavigateGameOptions(e, focused)) return;
            if (NavigateOptions(e, focused)) return;
            if (NavigateMultiplayer(e, focused)) return;

            var scope = dialog.ClassListContains("dialog-backdrop--shown") ? dialog : openPanel ?? mainButtons;
            var items = Focusables(scope);
            if (scope == mainButtons && updateButton != null && updateButton.focusable && updateRow.ClassListContains("update-row--shown"))
                items.Add(updateButton);   // "Update available" below the list
            if (items.Count == 0) return;
            int i = focused != null ? items.IndexOf(focused) : -1;
            int step = e.direction == NavigationMoveEvent.Direction.Up || e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
            // The main list wraps round (docs/UI-DESIGN.md "Navigation": a single-axis list wraps, XAG 112); a panel's
            // items and a dialog's buttons stop at their ends.
            bool wrap = scope == mainButtons && vertical;
            int next = i < 0 ? 0 : wrap ? (i + step + items.Count) % items.Count : Mathf.Clamp(i + step, 0, items.Count - 1);
            items[next].Focus();
            e.StopPropagation();
            root.focusController?.IgnoreEvent(e);
        }

        bool Swallow(NavigationMoveEvent e)
        {
            e.StopPropagation();
            root.focusController?.IgnoreEvent(e);
            return true;
        }

        /// <summary>The Game options panel with the mods' option cards (RefreshModOptions): two columns. Left / right step
        /// through the cards (the first card's left goes back to the toggles), right from a toggle or Start goes to the cards
        /// (the one last on); up / down stay in the toggles, Start and Back (down from a card: Start). False: not this case.</summary>
        bool NavigateGameOptions(NavigationMoveEvent e, VisualElement focused)
        {
            if (focused == null || modOptionCards == null || dialog.ClassListContains("dialog-backdrop--shown")) return false;
            if (!panels.TryGetValue("gameOptionsPanel", out var panel) || openPanel != panel) return false;
            if (modOptionCards.parent == null || modOptionCards.parent.resolvedStyle.display == DisplayStyle.None) return false;
            var cards = Focusables(modOptionCards.contentContainer);
            if (cards.Count == 0) return false;
            bool onCard = modOptionCards.Contains(focused);
            var d = e.direction;
            VisualElement to = null;
            if (onCard)
            {
                lastOptionCard = focused;
                int i = cards.IndexOf(focused);
                if (d == NavigationMoveEvent.Direction.Right) to = cards[Mathf.Min(i + 1, cards.Count - 1)];
                else if (d == NavigationMoveEvent.Direction.Left)
                    to = i > 0 ? cards[i - 1] : lastOptionToggle != null && panel.Contains(lastOptionToggle) ? lastOptionToggle : panel.Q("kaamoToggle");
                else if (d == NavigationMoveEvent.Direction.Down) to = panel.Q("gameOptionsStart");
                else to = focused;   // up: stays
            }
            else
            {
                var column = Focusables(panel).FindAll(v => !modOptionCards.Contains(v));
                if (d == NavigationMoveEvent.Direction.Right)
                {
                    lastOptionToggle = focused;
                    to = lastOptionCard != null && cards.Contains(lastOptionCard) ? lastOptionCard : cards[0];
                }
                else if (d == NavigationMoveEvent.Direction.Left) to = focused;
                else
                {
                    int i = column.IndexOf(focused);
                    if (i < 0) return false;
                    to = column[Mathf.Clamp(i + (d == NavigationMoveEvent.Direction.Up ? -1 : 1), 0, column.Count - 1)];
                }
            }
            if (to == null) return false;
            if (to != focused) Select(to);
            e.StopPropagation();
            root.focusController?.IgnoreEvent(e);
            return true;
        }

        /// <summary>The Multiplayer panel (a Discord report: with a controller the host card couldn't be reached from the server
        /// browser): the neighbour on screen (SpatialNav), so left / right cross between the browser and the host card and up /
        /// down stay in a column; across with nothing in the same row, the nearest item that way; the target is scrolled into
        /// view (the host card's settings scroll). Before, the panel was one list in document order. False: not this panel,
        /// or a dialog is open.</summary>
        bool NavigateMultiplayer(NavigationMoveEvent e, VisualElement focused)
        {
            if (dialog.ClassListContains("dialog-backdrop--shown")) return false;
            if (!panels.TryGetValue("multiplayerPanel", out var panel) || openPanel != panel) return false;
            var items = Focusables(panel);
            if (items.Count == 0) return false;
            VisualElement to;
            if (focused == null || !items.Contains(focused)) to = items.Find(v => !(v is TextField)) ?? items[0];
            else
            {
                int dx = e.direction == NavigationMoveEvent.Direction.Left ? -1 : e.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
                int dy = e.direction == NavigationMoveEvent.Direction.Up ? -1 : e.direction == NavigationMoveEvent.Direction.Down ? 1 : 0;
                // Up / down stay in the card (the two cards' rows don't line up: the nearest row was often the other card's),
                // then leave it (the name field above, Back below).
                var card = CardOf(focused);
                if (dy != 0)
                {
                    to = card != null ? SpatialNav.Next(items, focused, 0, dy, v => CardOf(v) == card) : null;
                    to ??= SpatialNav.Next(items, focused, 0, dy, card != null ? v => CardOf(v) == null : null);
                }
                else if (card != null)
                {
                    // Across: the row's neighbour in either card, else the nearest item of the other card on that side.
                    to = SpatialNav.Next(items, focused, dx, 0, v => CardOf(v) != null);
                    if (to == null)
                    {
                        var f = focused.worldBound;
                        float best = float.MaxValue;
                        foreach (var c in items)
                        {
                            var r = c.worldBound;
                            var other = CardOf(c);
                            if (other == null || other == card || float.IsNaN(r.x) || (r.center.x - f.center.x) * dx <= 1f) continue;
                            float d = (r.center - f.center).sqrMagnitude;
                            if (d < best) { best = d; to = c; }
                        }
                    }
                }
                else to = SpatialNav.Next(items, focused, dx, 0, v => CardOf(v) == null);   // the name field, Back
                to ??= focused;
            }
            if (to != focused) { to.Focus(); EnsureVisible(to); }
            e.StopPropagation();
            root.focusController?.IgnoreEvent(e);
            return true;
        }

        /// <summary>The multiplayer panel's card (the server browser or the host card) 'v' is in, null = neither.</summary>
        static VisualElement CardOf(VisualElement v)
        {
            for (var p = v; p != null; p = p.parent)
                if (p.ClassListContains("mp-card")) return p;
            return null;
        }

        /// <summary>What the keys can reach in 'scope', in order. 'scopeShown': the scope itself counts as shown whatever its
        /// resolved style says (an Options page just switched to: its style resolves with the next repaint).</summary>
        static List<VisualElement> Focusables(VisualElement scope, bool scopeShown = false)
        {
            var list = new List<VisualElement>();
            var stop = scopeShown ? scope : scope.parent;
            scope.Query<VisualElement>().Where(v => v.focusable && v.canGrabFocus && v.enabledInHierarchy && (v == scope || v.resolvedStyle.display != DisplayStyle.None)
                                                     && !(v.parent is BaseField<float>) && !(v.parent is BaseField<int>) && !(v.parent is Toggle)
                                                     && !InsideTextField(v) && IsShown(v, stop))
                .ForEach(list.Add);
            return list;
        }

        /// <summary>A text field's own parts (its TextInput and text element): the field is the one stop. Listed too, the
        /// D-pad stepped from the field onto its own input, which hands the focus back to the field: the multiplayer panel's
        /// name field (its first item) never let a controller go (#20).</summary>
        static bool InsideTextField(VisualElement v)
        {
            for (var p = v.parent; p != null; p = p.parent)
                if (p is TextField) return true;
            return false;
        }

        /// <summary>No element from 'v' up to (not including) 'stop' is display: none.</summary>
        static bool IsShown(VisualElement v, VisualElement stop)
        {
            for (var p = v; p != null && p != stop; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        void FocusFirst(VisualElement scope)
        {
            var items = Focusables(scope);
            // The difficulty list starts on Normal (the original's first entry), Easy above it; the economy on the last one picked.
            var normal = scope.name == "difficultyPanel" ? scope.Q<Button>("normalButton")
                : scope.name == "economyPanel" ? scope.Q<Button>(Session.Economy == Economy.Android ? "economyAndroidButton" : "economyDefaultButton")
                : scope.name == "gameOptionsPanel" ? scope.Q<Button>("gameOptionsStart") : null;
            // Not on a text field (the multiplayer panel's name is its first item): focused, it takes the keys, and on the
            // Xbox it can bring up the system keyboard, which then takes B (#20). The fields are one step away.
            var first = items.Find(v => !(v is TextField));
            if (normal != null && items.Contains(normal)) Select(normal);
            else if (first != null) Select(first);
            else if (items.Count > 0) Select(items[0]);
        }

        static void SetFocusable(VisualElement scope, bool on)
        {
            // Modified: and clickable: the dimmed main buttons behind an open panel still took clicks and taps.
            foreach (var b in scope.Query<Button>().ToList())
            {
                b.focusable = on;
                b.pickingMode = on ? PickingMode.Position : PickingMode.Ignore;
            }
        }

        static void EnsureVisible(VisualElement v)
        {
            for (var p = v.parent; p != null; p = p.parent)
                if (p is ScrollView sv) { sv.ScrollTo(v); return; }
        }
    }
}
