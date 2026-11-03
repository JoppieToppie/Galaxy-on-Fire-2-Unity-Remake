// MissileWarningView.cs
// Remake (the original has no missile warning): the flight HUD tells the player that homing missiles are flying at them,
// whoever fired them (IncomingMissiles: NPC rockets, capital ship salvos, other players' missiles in multiplayer):
//   - a pulsing red banner at the top, "INCOMING MISSILE" (with the count when there are several) over the boost key and
//     "BOOST TO EVADE" when the ship has a booster (a boost shakes them off: Gun, Target.boosting);
//   - a red diamond on each missile, on screen where it is, off screen on the radar ellipse (657 x 491, like the ship dots;
//     not in VR, where the ellipse would reach onto the cockpit's displays);
//   - an alarm beep (made here: two short tones), faster as the nearest missile closes in: a quarter of its time to impact,
//     110..650 ms.
// Driven by FlightHud.Update: Hide at the top of the frame, Update where the combat markers update.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class MissileWarningView
    {
        const float M = 0.05f;
        // Radar::elipsoidIntersect's ellipse, image 0x4c7's size (remake: x FlightHud.RadarScale, smaller in the phone layout).
        static float EllipseX => 657f * FlightHud.RadarScale;
        static float EllipseY => 491f * FlightHud.RadarScale;
        const float BeepMinMs = 110f, BeepMaxMs = 650f;

        readonly VisualElement layer, banner, hintRow;
        readonly Label title;
        readonly List<VisualElement> diamonds = new List<VisualElement>();
        readonly List<IncomingMissiles.Missile> missiles = new List<IncomingMissiles.Missile>();
        readonly AudioSource source;
        readonly AudioClip beep;
        InputKind hintKind = (InputKind)(-1);
        float sinceBeepMs = 1e9f;
        bool shown;

        public MissileWarningView(VisualElement root, GameObject host)
        {
            var safe = root.Q("safeArea") ?? root;
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("nav-markers");
            layer.AddToClassList("missile-markers");
            safe.Add(layer);
            banner = new VisualElement { pickingMode = PickingMode.Ignore };
            banner.AddToClassList("missile-warning");
            banner.AddToClassList("missile-warning--hidden");
            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("missile-warning-title");
            title.AddToClassList("gof-semibold");
            banner.Add(title);
            hintRow = new VisualElement { pickingMode = PickingMode.Ignore };
            hintRow.AddToClassList("missile-warning-hint");
            banner.Add(hintRow);
            safe.Add(banner);
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            beep = MakeBeep();
        }

        /// <summary>Two short tones (1320 Hz then 1760 Hz, 45 ms each with soft edges): the alarm's beep.</summary>
        static AudioClip MakeBeep()
        {
            const int rate = 44100;
            int part = rate * 45 / 1000, gap = rate * 15 / 1000;
            var data = new float[part * 2 + gap];
            for (int i = 0; i < data.Length; i++)
            {
                int k = i < part ? i : i - part - gap;
                if (i >= part && i < part + gap) continue;
                float f = i < part ? 1320f : 1760f;
                float env = Mathf.Clamp01(k / (rate * 0.004f)) * Mathf.Clamp01((part - k) / (rate * 0.008f));
                data[i] = Mathf.Sin(2f * Mathf.PI * f * k / rate) * env * 0.6f;
            }
            var clip = AudioClip.Create("MissileWarningBeep", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>The top of FlightHud.Update: nothing shows unless Update runs later this frame.</summary>
        public void Hide()
        {
            if (!shown) return;
            shown = false;
            banner.AddToClassList("missile-warning--hidden");
            foreach (var d in diamonds) d.style.display = DisplayStyle.None;
        }

        /// <param name="hasBooster">The player's ship has a booster (FlightModel.HasBooster): only then the boost hint shows.</param>
        public void Update(Transform player, Camera cam, bool allowed, bool hasBooster, float dtMs)
        {
            IncomingMissiles.Collect(missiles);
            sinceBeepMs += dtMs;
            if (!allowed || player == null || cam == null || missiles.Count == 0 || layer.panel == null) { Hide(); return; }
            shown = true;
            banner.RemoveFromClassList("missile-warning--hidden");
            title.text = missiles.Count == 1 ? Localization.Extra("missileIncoming", "INCOMING MISSILE").OgCase()
                       : string.Format(Localization.Extra("missileIncomingCount", "INCOMING MISSILES ×{0}"), missiles.Count);
            if (InputMode.Current != hintKind) BuildHint();
            hintRow.style.display = hasBooster ? DisplayStyle.Flex : DisplayStyle.None;   // no booster, no way to shake them off
            // The pulse: a 0.5 s cycle on the real clock.
            banner.style.opacity = 0.65f + 0.35f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 4f);

            var origin = layer.worldBound.position;
            var size = layer.panel.visualTree.layout.size;
            var centre = layer.layout.size / 2f;
            float nearestMs = float.MaxValue;
            for (int i = 0; i < missiles.Count; i++)
            {
                var m = missiles[i];
                var d = Diamond(i);
                var sp = cam.WorldToScreenPoint(m.position);
                bool onScreen = sp.z > 0f && sp.x >= 0f && sp.y >= 0f && sp.x <= Screen.width && sp.y <= Screen.height;
                if (!onScreen && Vr.VrMode.Enabled) { d.style.display = DisplayStyle.None; }
                else
                {
                    Vector2 p;
                    if (onScreen)
                    {
                        var v = cam.WorldToViewportPoint(m.position);
                        p = new Vector2(v.x * size.x, (1f - v.y) * size.y) - origin;
                    }
                    else
                    {
                        var local = cam.transform.InverseTransformPoint(m.position);
                        var dir = new Vector2(local.x, -local.y);
                        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                        float k = Mathf.Sqrt(dir.x * dir.x / (EllipseX * EllipseX) + dir.y * dir.y / (EllipseY * EllipseY));
                        p = centre + dir / k;
                    }
                    d.style.display = DisplayStyle.Flex;
                    d.style.left = p.x - 13f;
                    d.style.top = p.y - 13f;
                }
                // Time to impact: the distance over the closing speed (at least the missile's own speed's tenth).
                var to = player.position - m.position;
                float dist = to.magnitude;
                float closing = Mathf.Max(Vector3.Dot(m.velocity, to / Mathf.Max(dist, 1e-3f)), m.velocity.magnitude * 0.1f, 1e-5f);
                nearestMs = Mathf.Min(nearestMs, dist / closing);
            }
            for (int i = missiles.Count; i < diamonds.Count; i++) diamonds[i].style.display = DisplayStyle.None;

            float interval = Mathf.Clamp(nearestMs / 4f, BeepMinMs, BeepMaxMs);
            if (sinceBeepMs >= interval && Time.timeScale > 0f)
            {
                sinceBeepMs = 0f;
                source.PlayOneShot(beep, 0.35f * Settings.SfxVolume);
            }
        }

        VisualElement Diamond(int i)
        {
            while (diamonds.Count <= i)
            {
                var d = new VisualElement { pickingMode = PickingMode.Ignore };
                d.AddToClassList("missile-marker");
                d.style.display = DisplayStyle.None;
                layer.Add(d);
                diamonds.Add(d);
            }
            return diamonds[i];
        }

        /// <summary>The boost key (GameControls.Boost, as bound) and "BOOST TO EVADE"; touch: the text alone (the boost button).</summary>
        void BuildHint()
        {
            hintKind = InputMode.Current;
            hintRow.Clear();
            if (hintKind != InputKind.Touch)
                foreach (var g in InputGlyph.For(GameControls.Boost, hintKind)) hintRow.Add(g);
            var l = new Label(Localization.Extra("missileBoostHint", "BOOST TO EVADE").OgCase()) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("missile-warning-hint-label");
            l.AddToClassList("gof-semibold");
            hintRow.Add(l);
        }

        /// <summary>A rebound key: the hint shows it.</summary>
        public void RefreshHint() => hintKind = (InputKind)(-1);
    }
}
