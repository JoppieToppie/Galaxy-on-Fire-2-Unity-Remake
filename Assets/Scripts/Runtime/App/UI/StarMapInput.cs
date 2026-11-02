// StarMapInput.cs
// StarMap: keyboard and controller, the input mode and hints (part of the partial StarMap; its fields are in StarMap.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class StarMap
    {
        // ---- keyboard / controller -------------------------------------------------------------------------
        void HandleKeys(float dtMs)
        {
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            bool Pressed(Func<Keyboard, bool> k, Func<Gamepad, bool> g) => (kb != null && k(kb)) || (pad != null && g(pad));

            if (Pressed(k => k.escapeKey.wasPressedThisFrame, g => g.buttonEast.wasPressedThisFrame) && (builder == null || systemView || overview || DialogOpen))
            { Play(assets.buttonRelease); Back(); return; }
            if (builder != null) return;   // the builder's keys are its own (typing in its fields)
            int padDir = 0;
            Vector2 dpad = pad != null ? pad.dpad.ReadValue() : Vector2.zero;
            if (dpad.x < -0.5f) padDir = 1; else if (dpad.x > 0.5f) padDir = 2; else if (dpad.y > 0.5f) padDir = 3; else if (dpad.y < -0.5f) padDir = 4;
            bool padNew = padDir != 0 && padDir != lastPadDir;
            lastPadDir = padDir;
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame) || (padNew && padDir == 1);
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame) || (padNew && padDir == 2);
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame) || (padNew && padDir == 3);
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame) || (padNew && padDir == 4);
            bool confirm = Pressed(k => k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame,
                                   g => g.buttonSouth.wasPressedThisFrame);

            if (DialogOpen)
            {
                if (!dialogInfo && (left || (kb != null && kb.aKey.wasPressedThisFrame))) MoveDialogFocus(-1);
                else if (!dialogInfo && (right || (kb != null && kb.dKey.wasPressedThisFrame))) MoveDialogFocus(1);
                if (confirm) AnswerFocused();
                return;
            }
            if (world == null || zoomDir != 0) return;
            if (Pressed(k => k.kKey.wasPressedThisFrame, g => g.buttonNorth.wasPressedThisFrame)) { Play(assets.buttonRelease); ToggleKey(); }
            if (!systemView && Pressed(k => k.qKey.wasPressedThisFrame, g => g.buttonWest.wasPressedThisFrame)) SetOverview(!overview);
            if (askVoid && Pressed(k => k.vKey.wasPressedThisFrame, g => g.rightShoulder.wasPressedThisFrame)) { Play(assets.buttonRelease); AskVoid(); return; }
            if (!systemView && InOverview)
            {
                if (left || right || up || down) SelectInDirection(left ? Vector2.left : right ? Vector2.right : up ? Vector2.down : Vector2.up);
                if (confirm && overview)
                {
                    if (selected < 0) SelectInDirection(Vector2.zero);
                    else FocusFromOverview(selected);
                }
                return;
            }

            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (!systemView && kb.aKey.isPressed) move.x -= 1f;
                if (!systemView && kb.dKey.isPressed) move.x += 1f;
            }
            if (pad != null) { var s = pad.leftStick.ReadValue(); if (s.magnitude > 0.2f) move += s; }
            move = Vector2.ClampMagnitude(move, 1f);

            if (!systemView)
            {
                if (move != Vector2.zero)
                {
                    // Move the view like a drag of 1500 px/s (screen-right = camera toward game -X).
                    pan += new Vector2(-move.x, move.y) * DragScale * 1.5f * dtMs;
                    autoCentre = false;
                    vel = Vector2.zero;
                }
                if (left || right || up || down) SelectInDirection(left ? Vector2.left : right ? Vector2.right : up ? Vector2.down : Vector2.up);
                if (confirm)
                {
                    if (selected < 0) SelectInDirection(Vector2.zero);
                    else if (centred == selected) TryZoomIn();
                    else autoCentre = true;
                }
            }
            else
            {
                if (move != Vector2.zero)
                {
                    yaw = Mathf.Repeat(yaw - move.x * 16f * dtMs, 65536f);
                    pitch = Mathf.Clamp(pitch + move.y * 16f * dtMs, -8192f, 8192f);
                    frontPlanet = -1;
                    autoRotate = false;
                }
                int step = left || (kb != null && kb.aKey.wasPressedThisFrame) ? -1 : right || (kb != null && kb.dKey.wasPressedThisFrame) ? 1 : 0;
                if (step != 0 && planets.Count > 0 && !autoRotate)
                {
                    selectedPlanet = selectedPlanet < 0 ? NearestFrontPlanet() : (selectedPlanet + step + planets.Count) % planets.Count;
                    Play(assets.planetRelease);
                    autoRotate = true;
                }
                if (confirm && !autoRotate)
                {
                    if (selectedPlanet < 0) { selectedPlanet = NearestFrontPlanet(); Play(assets.planetRelease); autoRotate = selectedPlanet >= 0; }
                    else if (selectedPlanet == frontPlanet) Confirm(selectedPlanet);
                    else { Play(assets.planetRelease); autoRotate = true; }
                }
            }
        }
        /// <summary>Keys / D-pad: the nearest visible system on screen in that direction from the selection (or the screen
        /// centre); Vector2.zero = the one nearest the centre.</summary>
        void SelectInDirection(Vector2 dir)
        {
            Vector2 from = PanelSize / 2f;
            if (selected >= 0 && suns[selected] != null && Project(suns[selected].position, out var sp)) from = sp;
            int best = -1;
            float bestScore = float.MaxValue;
            foreach (var s in db.Systems)
            {
                if (s.index == selected || s.index >= suns.Length || suns[s.index] == null) continue;
                if (!Project(suns[s.index].position, out var q)) continue;
                var d = q - from;
                float dist = d.magnitude;
                float score;
                if (dir == Vector2.zero) score = dist;
                else
                {
                    float cos = dist > 0f ? Vector2.Dot(d / dist, dir) : 0f;
                    if (cos < 0.4f) continue;
                    score = dist * (2f - cos);
                }
                if (score < bestScore) { bestScore = score; best = s.index; }
            }
            if (best < 0) return;
            Select(best);
            autoCentre = true;
        }
        int NearestFrontPlanet()
        {
            int best = -1;
            float bestZ = float.MaxValue;
            for (int k = 0; k < planetObjects.Count; k++)
            {
                if (planetObjects[k] == null) continue;
                float z = cam.WorldToViewportPoint(planetObjects[k].position).z;
                if (z < bestZ) { bestZ = z; best = k; }
            }
            return best;
        }
        // ---- input mode / hints ----------------------------------------------------------------------------
        void ApplyInputMode()
        {
            if (root == null) return;
            var kind = InputMode.Current;
            root.EnableInClassList("input-touch", kind == InputKind.Touch);
            root.EnableInClassList("input-keyboard", kind == InputKind.KeyboardMouse);
            root.EnableInClassList("input-gamepad", kind == InputKind.Gamepad);
            if (DialogOpen) HighlightDialog();
            BuildHints(kind);
        }
        void BuildHints(InputKind kind)
        {
            UpdateMapButtons();
            if (hints == null) return;
            hints.Clear();
            AddHints(hints, kind);
        }
        /// <summary>Modified: the map's own buttons, not its hints: what the Full map / Close up button says and which of it,
        /// the Key button and the Jump to the Void button show. Split out of BuildHints, which the "i" window used to re-run
        /// only to collect the hint rows.</summary>
        void UpdateMapButtons()
        {
            bool sys = systemView || zoomDir > 0;
            if (zoomButton != null)
            {
                zoomButton.text = overview ? Localization.Extra("mapCloseUp", "CLOSE UP") : Localization.Extra("mapFullMap", "FULL MAP");
                bool zoomShown = !sys && !revealing && builder == null;   // the builder's map: none of the map's own buttons
                zoomButton.style.display = zoomShown ? DisplayStyle.Flex : DisplayStyle.None;
                // Both push to the right edge: the Key button sits right after the Full map button while that shows.
                keyButton.style.marginLeft = zoomShown ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);
            }
            if (voidButton != null) voidButton.style.display = askVoid && builder == null && !revealing ? DisplayStyle.Flex : DisplayStyle.None;
        }
        /// <summary>Modified: the hint rows for 'kind' into 'into' (the hint bar's box, or the "i" window's).</summary>
        void AddHints(VisualElement into, InputKind kind)
        {
            string X(string key, string english) => Localization.Extra(key, english);
            string select = X("hudSelect", "SELECT"), back = X("hudBack", "BACK"), confirm = X("hudConfirm", "CONFIRM");
            string zoom = X("mapZoomIn", "ZOOM IN"), moveText = X("mapMove", "MOVE"), turn = X("mapTurn", "TURN");
            string key = T(400).ToUpperInvariant();
            if (DialogOpen)
            {
                if (kind == InputKind.KeyboardMouse)
                {
                    if (!dialogInfo) Hint(into, select, InputGlyph.Key("←"), InputGlyph.Key("→"));
                    Hint(into, confirm, InputGlyph.Key("ENTER", true));
                    Hint(into, back, InputGlyph.Key("ESC"));
                }
                else if (kind == InputKind.Gamepad)
                {
                    if (!dialogInfo) Hint(into, select, InputGlyph.Pad(PadButton.DPad));
                    Hint(into, confirm, InputGlyph.Pad(PadButton.A));
                    Hint(into, back, InputGlyph.Pad(PadButton.B));
                }
                return;
            }
            bool sys = systemView || zoomDir > 0;
            string fullMap = overview ? X("mapCloseUp", "CLOSE UP") : X("mapFullMap", "FULL MAP");
            string toVoid = VoidButtonText;
            if (kind == InputKind.KeyboardMouse)
            {
                Hint(into, select, InputGlyph.Key(sys ? "A" : "←"), InputGlyph.Key(sys ? "D" : "→"));
                if (!overview) Hint(into, sys ? turn : moveText, InputGlyph.Key("W"), InputGlyph.Key("S"));
                Hint(into, sys ? confirm : zoom, InputGlyph.Key("ENTER", true));
                if (!sys) Hint(into, fullMap, InputGlyph.Key("Q"));
                if (askVoid) Hint(into, toVoid, InputGlyph.Key("V"));
                Hint(into, key, InputGlyph.Key("K"));
                Hint(into, back, InputGlyph.Key("ESC"));
            }
            else if (kind == InputKind.Gamepad)
            {
                Hint(into, select, InputGlyph.Pad(PadButton.DPad));
                if (!overview) Hint(into, sys ? turn : moveText, InputGlyph.Pad(PadButton.LeftStick));
                Hint(into, sys ? confirm : zoom, InputGlyph.Pad(PadButton.A));
                if (!sys) Hint(into, fullMap, InputGlyph.Pad(PadButton.X));
                if (askVoid) Hint(into, toVoid, InputGlyph.Pad(PadButton.RightBumper));
                Hint(into, key, InputGlyph.Pad(PadButton.Y));
                Hint(into, back, InputGlyph.Pad(PadButton.B));
            }
        }
        static void Hint(VisualElement into, string label, params VisualElement[] glyphs)
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore };
            h.AddToClassList("hint");
            // Modified: the glyphs in a row of their own (hint-keys) and the label under them, as in the hangar window.
            var keys = new VisualElement { pickingMode = PickingMode.Ignore };
            keys.AddToClassList("hint-keys");
            foreach (var g in glyphs) keys.Add(g);
            h.Add(keys);
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("hint-label");
            l.AddToClassList("gof-semibold");
            h.Add(l);
            into.Add(h);
        }
        void Play(AudioClip clip)
        {
            if (clip == null) return;
            Source().PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
        }
        AudioSource Source()
        {
            var src = GetComponent<AudioSource>();
            if (src == null) { src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.ignoreListenerPause = true; }
            return src;
        }
        /// <summary>A one-shot 2D sound that survives the map closing (used by the owners for the jump sounds).</summary>
        public static void PlayDetached(AudioClip clip)
        {
            if (clip == null) return;
            var go = new GameObject("Sound " + clip.name);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
            Destroy(go, clip.length + 0.5f);
        }
    }
}
