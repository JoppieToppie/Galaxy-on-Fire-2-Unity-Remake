// ScrollBars.cs
// Modified: the hangar list's scrollbar (HangarWindow.UpdateListBar) on every vertical scroll view of the UI: a slim track
// along the view's right edge with a handle to drag (or a press on the track: the handle's middle jumps there), shown only
// while the content is taller than the view, so a window that grows past its height (more options, a longer text) gets
// one by itself. The hosts call Watch with their document's root (MainMenu, StationMenu, FlightHud, StarMap); it looks for
// new scroll views twice a second (pages built later, the options' drop-down lists). The bar sits just outside the scroll
// view's right edge (in its parent, like the hangar list's), so showing it moves nothing inside (the Debug page's cards
// reflowed when the content made room for it).
// Skipped: horizontal views, the hangar list (its own bar) and a view with the class "gof-no-scroll-bar".

using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class ScrollBars
    {
        const string Watched = "gof-scroll-bars-watched", Attached = "gof-scroll-bar-host", OptOut = "gof-no-scroll-bar";
        const long ScanMs = 500, SafetyMs = 200;

        /// <summary>Gives the document's vertical scroll views a scrollbar, now and as new ones appear.</summary>
        public static void Watch(VisualElement root)
        {
            if (root == null || root.ClassListContains(Watched)) return;
            root.AddToClassList(Watched);
            Scan(root);
            root.schedule.Execute(() => Scan(root)).Every(ScanMs);
        }

        static void Scan(VisualElement root) => root.Query<ScrollView>().ForEach(Attach);

        /// <summary>One scroll view's bar (once).</summary>
        public static void Attach(ScrollView sv)
        {
            if (sv == null || sv.ClassListContains(Attached) || sv.ClassListContains(OptOut)) return;
            if (sv.mode == ScrollViewMode.Horizontal || sv.name == "hangarList") return;
            sv.AddToClassList(Attached);
            new ScrollBars(sv);
        }

        readonly ScrollView view;
        readonly VisualElement bar, thumb;
        int pointer = -1;
        float grab;   // where on the handle it was grabbed (from its top)

        ScrollBars(ScrollView sv)
        {
            view = sv;
            bar = new VisualElement { pickingMode = PickingMode.Position };
            bar.AddToClassList("gof-scroll-bar");
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("gof-scroll-bar-track");
            bar.Add(track);
            thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            thumb.AddToClassList("gof-scroll-bar-thumb");
            bar.Add(thumb);
            bar.style.display = DisplayStyle.None;
            sv.parent?.Add(bar);
            bar.RegisterCallback<PointerDownEvent>(OnDown);
            bar.RegisterCallback<PointerMoveEvent>(OnMove);
            bar.RegisterCallback<PointerUpEvent>(OnUp);
            bar.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == pointer) EndDrag(); });
            bar.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == pointer) EndDrag(); });
            bar.RegisterCallback<DetachFromPanelEvent>(_ => EndDrag());
            // The view taken out (the pause menu builds a new body per page): its bar goes with it, else it stayed in the
            // parent at the old page's height (a scrollbar hanging out of the shorter page's panel). Back in, UpdateBar
            // puts it back.
            view.RegisterCallback<DetachFromPanelEvent>(_ => Remove());
            // The bar follows the view instead of a tick of its own: its size and place only change when the view is laid out,
            // its content grows or shrinks, or it is scrolled. A 60 Hz tick per view read the layout of every scroll view in
            // the document for ever, the flight HUD's included (ScrollBars.Watch is installed on it).
            view.RegisterCallback<GeometryChangedEvent>(_ => UpdateBar());
            view.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateBar());
            view.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => UpdateBar());
            view.verticalScroller.valueChanged += _ => UpdateBar();
            // A slow safety tick on top: the bar lives in the view's parent, which may clear its children without the view
            // itself being laid out again (UpdateBar puts it back). 5 Hz, not the 60 Hz this used to run at.
            sv.schedule.Execute(UpdateBar).Every(SafetyMs);
        }

        float ScrollMax => Mathf.Max(0f, view.contentContainer.layout.height - view.contentViewport.layout.height);

        /// <summary>Shown while the content overflows; the handle's size and place follow the scroll.</summary>
        void UpdateBar()
        {
            if (view.parent == null) { Remove(); return; }
            if (bar.parent != view.parent) view.parent.Add(bar);   // the parent rebuilt its children, or the view moved
            var vr = view.layout;
            float content = view.contentContainer.layout.height, viewport = view.contentViewport.layout.height;
            bool show = !float.IsNaN(vr.height) && !float.IsNaN(content) && !float.IsNaN(viewport) && vr.height > 24f
                        && content > viewport + 1f;
            if (!show)
            {
                EndDrag();
                bar.style.display = DisplayStyle.None;
                return;
            }
            bar.style.display = DisplayStyle.Flex;
            float h = vr.height - 8f;
            float barW = bar.resolvedStyle.width;
            if (float.IsNaN(barW) || barW <= 0f) barW = 12f;
            // Its slim track's centre 8 units right of the view's edge, whatever the touch area's width (phones): the left
            // edge there, and the USS translate (-50 %; on phones -6 units, its touch area reaching right) centres it. Not "- barW / 2": the resolved width is snapped to whole
            // pixels, so at a scale like 1.19 units a pixel each placement changed it (10 / 11 px) and the next one moved
            // the bar back, a bar flickering wider and narrower every other safety tick (a player's report in the Mod
            // Builder, 2026-10-22).
            bar.style.left = vr.xMax + 8f;
            bar.style.top = vr.y + 4f;
            bar.style.height = h;
            float handle = Mathf.Min(h, Mathf.Max(!float.IsNaN(barW) && barW > 20f ? 60f : 30f, h * viewport / content));   // finger-sized on phones
            float t = Mathf.Clamp01(view.scrollOffset.y / Mathf.Max(1f, ScrollMax));
            thumb.style.height = handle;
            thumb.style.top = t * (h - handle);
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || pointer >= 0) return;
            pointer = e.pointerId;
            bar.CapturePointer(pointer);
            thumb.AddToClassList("gof-scroll-bar-thumb--dragging");
            float y = e.localPosition.y, top = thumb.layout.y, h = thumb.layout.height;
            // On the handle: dragged from where it was grabbed; on the track: its middle jumps there.
            grab = y >= top && y <= top + h ? y - top : h * 0.5f;
            ScrollTo(y);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId != pointer) return;
            if (!UiPointerButtons.PrimaryHeld(e)) { EndDrag(); return; }
            ScrollTo(e.localPosition.y);
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != pointer) return;
            EndDrag();
            e.StopPropagation();
        }

        void Remove()
        {
            EndDrag();
            bar.RemoveFromHierarchy();
        }

        void EndDrag()
        {
            int id = pointer;
            pointer = -1;
            thumb.RemoveFromClassList("gof-scroll-bar-thumb--dragging");
            if (id >= 0 && bar.HasPointerCapture(id)) bar.ReleasePointer(id);
        }

        /// <summary>Scrolled so the handle's grabbed point is at 'y' on the track.</summary>
        void ScrollTo(float y)
        {
            float track = bar.layout.height, handle = thumb.layout.height;
            if (float.IsNaN(track) || track - handle <= 0f) return;
            float t = Mathf.Clamp01((y - grab) / (track - handle));
            view.scrollOffset = new Vector2(view.scrollOffset.x, t * ScrollMax);
            UpdateBar();
        }
    }
}
