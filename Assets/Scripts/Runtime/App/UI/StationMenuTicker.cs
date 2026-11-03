// StationMenuTicker.cs
// Part of StationMenu: the news ticker along the bottom edge, the menu's fly-in / fly-out animation around hangar
// flights, and the credits and cargo plates under the top bar.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class StationMenu
    {
        /// <summary>NewsTicker: built once per docking (ModStation::OnInitialize state 0x3c), main view only. Multiplayer
        /// (remake): the sector's own news first (NetNews: claims, sieges, raids, arena results ...); 'newsOnly' keeps
        /// the game's items already rolled.</summary>
        void SetupTicker(bool newsOnly = false)
        {
            tickerText = root.Q<Label>("tickerText");
            HookTickerDrag();
            var st = level != null ? level.Station : null;
            bool shown = st != null && NewsTicker.ShownAt(st.index, st.system);
            root.EnableInClassList("ticker-off", !shown);
            if (!shown) return;
            if (!newsOnly || tickerBase == null) tickerBase = NewsTicker.Build(level.Database, st.system, level.Layout.raceId) ?? "";
            // Remake: the Pirate activity board's reports in this system and its gate neighbours, before the game's own.
            tickerSingle = GoF2Remake.Multiplayer.NetNews.Ticker(st.index) + PirateReports.Ticker(level.Database, st.system) + tickerBase;
            tickerNewsDirty = false;
            tickerBuiltAt = Time.unscaledTime;
            tickerText.text = tickerSingle;
            tickerX = 0f;
            tickerReady = false;
            if (!newsOnly) tickerText.style.visibility = Visibility.Hidden;   // until it starts at the strip's right edge
        }

        /// <summary>NewsTicker::OnTouchBegin / OnTouchMove / OnTouchEnd: a press on the strip holds it (no auto-scroll) and it
        /// follows the pointer 1:1, back to reread or forward to skip; no inertia. The press is the ticker's, not the
        /// turntable's (ModStation::OnTouchBegin returns once the ticker took it).</summary>
        void HookTickerDrag()
        {
            if (tickerHooked || tickerText == null) return;
            tickerHooked = true;
            var strip = tickerText.parent;
            strip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (tickerPointer >= 0 || !tickerReady || e.button != 0) return;
                tickerPointer = e.pointerId;
                tickerLastX = e.position.x;
                strip.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            strip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != tickerPointer) return;
                if (!UiPointerButtons.PrimaryHeld(e)) { EndTickerDrag(strip); return; }
                tickerX += e.position.x - tickerLastX;
                tickerLastX = e.position.x;
                WrapTicker();
                tickerText.style.left = tickerX;
            });
            strip.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == tickerPointer) EndTickerDrag(strip); });
            strip.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == tickerPointer) EndTickerDrag(strip); });
            strip.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == tickerPointer) EndTickerDrag(strip); });
        }

        void EndTickerDrag(VisualElement strip)
        {
            int id = tickerPointer;
            tickerPointer = -1;   // first: releasing the capture sends PointerCaptureOut back here
            if (id >= 0 && strip.HasPointerCapture(id)) strip.ReleasePointer(id);
        }

        /// <summary>The repeated copies cover the strip while x stays in (-one copy, 0], in either direction.</summary>
        void WrapTicker()
        {
            if (tickerUnitWidth <= 0f) return;
            while (tickerX > 0f) tickerX -= tickerUnitWidth;
            while (tickerX <= -tickerUnitWidth) tickerX += tickerUnitWidth;
        }

        /// <summary>NewsTicker::update: x -= dt * 50 px/s. Remake: the strip is never empty: the news (one copy = the items +
        /// separator) is repeated to cover the strip plus one copy, starts already filled, and wraps by exactly one copy
        /// (the original scrolled one text in from the right edge).</summary>
        void UpdateTicker()
        {
            if (tickerText == null || root.ClassListContains("ticker-off")) return;
            float strip = tickerText.parent.resolvedStyle.width;
            if (float.IsNaN(strip) || strip <= 0f) return;
            if (!tickerReady)
            {
                string unit = tickerSingle + NewsTicker.Separator;
                var size = tickerText.MeasureTextSize(unit, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
                if (tickerSingle.Length == 0 || float.IsNaN(size.x) || size.x <= 0f) return;
                tickerReady = true;
                tickerUnitWidth = size.x;
                int copies = Mathf.CeilToInt(strip / tickerUnitWidth) + 1;
                var sb = new System.Text.StringBuilder(unit.Length * copies);
                for (int i = 0; i < copies; i++) sb.Append(unit);
                tickerText.text = sb.ToString();
                tickerX = 0f;
                tickerText.style.left = tickerX;
                tickerText.style.visibility = StyleKeyword.Null;
                return;
            }
            if (tickerPointer >= 0) return;   // held: NewsTicker::update skips the scroll while dragged
            tickerX -= Time.unscaledDeltaTime * NewsTicker.ScrollPxPerSecond;
            bool wrapped = tickerX <= -tickerUnitWidth;
            WrapTicker();
            // Multiplayer: new news, or the items' ages ("5 min ago") a minute old: rebuilt as the copy wraps.
            if (wrapped && (tickerNewsDirty || (GoF2Remake.Multiplayer.NetGame.Active && Time.unscaledTime - tickerBuiltAt > 60f)))
            {
                SetupTicker(true);
                return;
            }
            tickerText.style.left = tickerX;
        }

        /// <summary>Multiplayer: the sector's news changed (NetNews): at once while the strip has nothing yet, else when it
        /// wraps.</summary>
        void OnNews()
        {
            if (tickerText == null) return;
            if (tickerReady) tickerNewsDirty = true;
            else SetupTicker(true);
        }

        void SetupUiAnimation()
        {
            if (safeArea == null) return;
            uiTop = safeArea.Q(className: "top-bar");
            uiFunds = safeArea.Q("fundsBar");   // the credits and cargo: up and away with the bar
            uiTicker = safeArea.Q("ticker");
            uiInfo = safeArea.Q(className: "station-info");
            uiButtons = safeArea.Q(className: "station-buttons");
            uiBlocker = new VisualElement { name = "uiBlocker", pickingMode = PickingMode.Position };
            uiBlocker.style.position = Position.Absolute;
            uiBlocker.style.left = 0; uiBlocker.style.right = 0; uiBlocker.style.top = 0; uiBlocker.style.bottom = 0;
            uiBlocker.style.display = DisplayStyle.None;
            root.Add(uiBlocker);   // last: over everything, the drag zone included
            EventCallback<NavigationSubmitEvent> submit = e => StopWhileBlocked(e);
            EventCallback<NavigationMoveEvent> move = e => StopWhileBlocked(e);
            EventCallback<NavigationCancelEvent> cancel = e => StopWhileBlocked(e);
            root.RegisterCallback(submit, TrickleDown.TrickleDown);
            root.RegisterCallback(move, TrickleDown.TrickleDown);
            root.RegisterCallback(cancel, TrickleDown.TrickleDown);
        }

        void StopWhileBlocked(EventBase e)
        {
            if (!UiBlocked) return;
            e.StopPropagation();
            root.focusController?.IgnoreEvent(e);
        }

        void UpdateUiAnimation(bool flying)
        {
            if (safeArea == null) return;
            float target = flying ? 0f : 1f;
            if (!uiStateTaken) { uiStateTaken = true; uiShown = target; ApplyUiAnimation(); return; }
            if (uiShown == target) return;
            // Options > Accessibility "Reduce motion": the menu is there or away at once.
            uiShown = Settings.ReduceMotion ? target : Mathf.MoveTowards(uiShown, target, Time.unscaledDeltaTime * 1000f / UiAnimMs);
            ApplyUiAnimation();
        }

        /// <summary>1 - easeOutCubic(t): the share of the way still to go.</summary>
        static float UiRemaining(float t)
        {
            t = Mathf.Clamp01(t);
            return (1f - t) * (1f - t) * (1f - t);
        }

        void ApplyUiAnimation()
        {
            bool away = uiShown <= 0f, still = uiShown >= 1f;
            // Away: no inline value back in place, so the stylesheet's own rules (.station-map-open hides the menu under the
            // star map) apply again; an inline Flex here kept the station HUD over the map.
            if (away != safeAreaHidden)
            {
                safeAreaHidden = away;
                safeArea.style.display = away ? DisplayStyle.None : StyleKeyword.Null;
            }
            if (uiBlocker != null) uiBlocker.style.display = still ? DisplayStyle.None : DisplayStyle.Flex;
            // The side panel's entries in their order, the hidden buttons left out (a locked Map or Lounge takes no turn).
            uiItems.Clear();
            if (uiInfo != null) uiItems.Add(uiInfo);
            if (uiButtons != null)
                foreach (var b in uiButtons.Children())
                    if (b.resolvedStyle.display != DisplayStyle.None) uiItems.Add(b);
            if (still)
            {
                safeArea.style.opacity = StyleKeyword.Null;
                foreach (var el in new[] { uiTop, uiFunds, launchButton, uiTicker, uiInfo }) if (el != null) el.style.translate = StyleKeyword.Null;
                if (uiInfo != null) uiInfo.style.opacity = StyleKeyword.Null;
                if (uiButtons != null)
                    foreach (var b in uiButtons.Children()) { b.style.translate = StyleKeyword.Null; b.style.opacity = StyleKeyword.Null; }
                return;
            }
            // Ease out coming in (and so ease in going away: the same curve run backwards).
            float t = Mathf.Clamp01(uiShown);
            float k = UiRemaining(t);
            // The safe area fades over the first 40 %: the side entries do their own fade after that, the rest is in by then.
            safeArea.style.opacity = Mathf.SmoothStep(0f, 1f, t / 0.4f);
            if (uiTop != null) uiTop.style.translate = new Translate(0f, -UiTopOffset * k);
            if (uiFunds != null) uiFunds.style.translate = new Translate(0f, -UiFundsOffset * k);
            if (launchButton != null) launchButton.style.translate = new Translate(UiLaunchOffset * k, 0f);
            if (uiTicker != null) uiTicker.style.translate = new Translate(0f, UiTickerOffset * k);
            float ms = t * UiAnimMs;
            for (int i = 0; i < uiItems.Count; i++)
            {
                float p = Mathf.Clamp01((ms - i * UiItemStepMs) / UiItemMs);
                float r = UiRemaining(p);
                uiItems[i].style.translate = new Translate(-UiItemOffset * r, 0f);
                uiItems[i].style.opacity = 1f - r;
            }
        }

        void UpdateFunds()
        {
            if (fundsCargo == null || fundsCredits == null || level == null || level.Database == null) return;
            long credits = Session.Credits;
            int load = Shop.CargoLoad(), max = Shop.MaxLoad(level.Database);
            if (credits != shownCredits)
            {
                shownCredits = credits;
                fundsCredits.text = ItemInfo.Credits(credits);
            }
            if (load != shownLoad || max != shownMaxLoad)
            {
                shownLoad = load;
                shownMaxLoad = max;
                fundsCargo.text = $"{Localization.Get(184).UiCaps()}  {load} / {max} t";   // 184 Cargo, as the hangar's footer
                fundsCargo.EnableInClassList("station-funds-cargo--over", load > max);
            }
        }
    }
}
