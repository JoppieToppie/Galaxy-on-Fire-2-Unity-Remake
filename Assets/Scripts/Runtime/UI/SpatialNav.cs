// SpatialNav.cs
// Remake: the neighbour on screen for keys / D-pad in menus laid out in rows and columns (the Debug pages' toggle grid, the
// two Give items buttons side by side): up / down go to the nearest row above / below (the item closest across in it),
// left / right to the nearest item in the same row. Plain helper for StationMenu and PauseMenu.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public static class SpatialNav
    {
        /// <summary>The item next to 'from' in the direction (dx -1 / 1 or dy -1 up / 1 down), or null when there is none
        /// that way. 'ok' leaves out items that can't be selected (hidden, disabled).</summary>
        public static VisualElement Next(IList<VisualElement> items, VisualElement from, int dx, int dy, Func<VisualElement, bool> ok = null)
        {
            if (from == null) return null;
            var f = from.worldBound;
            if (float.IsNaN(f.x)) return null;
            VisualElement best = null;
            float bestMain = float.MaxValue, bestCross = float.MaxValue;
            foreach (var c in items)
            {
                if (c == null || c == from || (ok != null && !ok(c))) continue;
                var r = c.worldBound;
                if (float.IsNaN(r.x)) continue;
                float main, cross;
                if (dy != 0)
                {
                    // Another row: its centre past this one's by more than a quarter of the taller one's height.
                    float d = (r.center.y - f.center.y) * dy;
                    if (d <= Mathf.Max(4f, Mathf.Max(f.height, r.height) * 0.25f)) continue;
                    main = d;
                    cross = Mathf.Abs(r.center.x - f.center.x);
                }
                else
                {
                    // The same row: overlapping vertically, its centre that way.
                    if (r.yMax <= f.yMin + 2f || r.yMin >= f.yMax - 2f) continue;
                    float d = (r.center.x - f.center.x) * dx;
                    if (d <= 1f) continue;
                    main = d;
                    cross = Mathf.Abs(r.center.y - f.center.y);
                }
                // The nearest row (within 8 px counts as the same), then the closest across in it.
                if (main < bestMain - 8f || (main <= bestMain + 8f && cross < bestCross))
                {
                    best = c;
                    bestMain = Mathf.Min(bestMain, main);
                    bestCross = cross;
                }
            }
            return best;
        }
    }
}
