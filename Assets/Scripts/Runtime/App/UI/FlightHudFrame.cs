// FlightHudFrame.cs
// FlightHud: per frame, the helpers Update and LateUpdate call (part of the partial FlightHud; its fields are in FlightHud.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class FlightHud
    {
        /// <summary>Globals::mouseCursorActivated: with the option on, the keyboard and mouse in use and the ship flyable, the
        /// cursor is captured and the mouse steers (ShipController.mouseSteering); a ring marks the mouse crosshair
        /// (PlayerEgo+0x94, the centre + the offset), the normal crosshair keeps showing the aim (+0xa0).</summary>
        /// <summary>A line on the HUD's message plate (single player's event notices, EventHost).</summary>
        public void ShowMessage(string text) => miningView?.ShowMessage(text);
        void UpdateMouseSteering()
        {
            if (ship == null || level == null) return;
            // Remake: Android too, with a mouse connected (the lock is Android's pointer capture); without one a keyboard
            // steers alone at its own sensitivity.
            bool cursor = Settings.MouseSteering && Mouse.current != null && InputMode.Current == InputKind.KeyboardMouse && !Vr.VrMode.Enabled
                      && !pauseMenu.IsOpen && !(nav != null && nav.MenuOpen) && !StarMap.IsOpen && !storyDialogue.IsOpen
                      && !level.Cutscene && level.LaunchCameraOver && Time.timeScale > 0f && (health == null || !health.Dead)
                      // the Liberator too: the mouse steers it (it was off); docked at an object only the turret view (it aims the
                      // turret; the cursor is free for the HUD otherwise)
                      // (#132: the approach to the docking point is an ordinary autopilot leg: the cursor stays locked, it showed
                      // the OS pointer on that leg)
                      && (level.Docking == null || !level.Docking.Busy || level.Docking.State == ObjectDocking.Phase.Approach
                          || PlayerTurret.ViewActive(ship.gameObject))
                      && !GoF2Remake.Multiplayer.NetChat.Typing   // multiplayer: the cursor free for the chat
                      && !MultiplayerWindow.IsOpenAny;            // and for the multiplayer window
            // PlayerEgo::right etc. forward to the MiningGame while drilling (0xacd48): the mouse steers the drill then (the PC
            // version's mining); approaching and landing it does nothing, the cursor stays locked.
            bool idle = mining == null || mining.State == Mining.Phase.Idle;
            // Free look keeps the cursor captured: the mouse orbits the camera instead of steering (FreeLookCamera.mouseLook).
            bool freeLook = level.FreeLook != null && level.FreeLook.FreeLookActive;
            if (level.FreeLook != null) level.FreeLook.mouseLook = cursor && freeLook;
            bool on = cursor && idle && !freeLook;
            ship.mouseSteering = on;
            if (mining != null) mining.mouseDrill = cursor && mining.State == Mining.Phase.Mining;
            var wantLock = cursor ? CursorLockMode.Locked : CursorLockMode.None;
            if (UnityEngine.Cursor.lockState != wantLock) UnityEngine.Cursor.lockState = wantLock;
            bool cursorShown = !cursor && InputMode.CursorShown;   // a controller in use hides it too
            if (UnityEngine.Cursor.visible != cursorShown) UnityEngine.Cursor.visible = cursorShown;
            if (mouseReticle != null && mouseReticle.parent == null) mouseReticle = null;   // a UI reload rebuilt the tree
            if (mouseReticle == null && safeArea != null)
            {
                mouseReticle = new VisualElement { pickingMode = PickingMode.Ignore };
                mouseReticle.AddToClassList("mouse-reticle");
                safeArea.Add(mouseReticle);
            }
            if (mouseReticle == null) return;
            UpdateStickRing(on && ship.Model.FreeFlight, ship.MouseOffset.magnitude / Mathf.Max(1f, Screen.height * ShipController.StickRadius));
            // Only once the mouse steers away from the centre (beyond ~4 % of the half screen height and the dead zone).
            bool show = on && !ship.MouseInDeadzone && ship.MouseOffset.magnitude > Screen.height * 0.02f;
            mouseReticle.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show || root.panel == null) return;
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + ship.MouseOffset;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(centre.x, Screen.height - centre.y));
            var parent = mouseReticle.parent.worldBound;
            mouseReticle.style.left = p.x - parent.x;
            mouseReticle.style.top = p.y - parent.y;
        }
        /// <summary>Free flight's mouse: a faint circle round the centre, the virtual joystick's reach
        /// (ShipController.StickRadius); it fades in as the aim dot nears it ('reach': the dot's offset of the radius, from
        /// StickRingFrom, fully shown at StickRingFull) and out again as it comes back (always shown it looked like a second
        /// radar ellipse; shown for any steering it came up for every small correction, players' feedback).</summary>
        void UpdateStickRing(bool mode, float reach)
        {
            float target = mode ? Mathf.InverseLerp(StickRingFrom, StickRingFull, reach) : 0f;
            float ms = Time.unscaledDeltaTime * 1000f;
            stickRingShown = Mathf.MoveTowards(stickRingShown, target, ms / (target > stickRingShown ? StickRingFadeInMs : StickRingFadeOutMs));
            if (!mode) stickRingShown = 0f;
            bool show = stickRingShown > 0f;
            if (stickRing != null && stickRing.parent == null) stickRing = null;   // a UI reload rebuilt the tree
            if (stickRing == null && safeArea != null)
            {
                stickRing = new VisualElement { pickingMode = PickingMode.Ignore };
                stickRing.AddToClassList("mouse-stick");
                safeArea.Insert(0, stickRing);
            }
            if (stickRing == null) return;
            stickRing.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            stickRing.style.opacity = stickRingShown;
            if (!show || root.panel == null) return;
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var p0 = RuntimePanelUtils.ScreenToPanel(root.panel, c);
            var p1 = RuntimePanelUtils.ScreenToPanel(root.panel, c + new Vector2(Screen.height * ShipController.StickRadius, 0f));
            float r = Mathf.Abs(p1.x - p0.x);
            var parent = stickRing.parent.worldBound;
            stickRing.style.left = p0.x - parent.x - r;
            stickRing.style.top = p0.y - parent.y - r;
            stickRing.style.width = 2f * r;
            stickRing.style.height = 2f * r;
            stickRing.style.borderTopLeftRadius = stickRing.style.borderTopRightRadius =
                stickRing.style.borderBottomLeftRadius = stickRing.style.borderBottomRightRadius = r;
        }
        /// <summary>Layout::drawFade: the campaign level's full-screen fade.</summary>
        void UpdateFade()
        {
            var c = level != null ? level.Campaign : null;
            float a = c != null ? c.FadeAlpha : 0f;
            screenFade.style.opacity = a;
            if (c != null && a > 0f) screenFade.style.backgroundColor = c.FadeColor;
        }
        /// <summary>Radio::draw: the campaign level's current radio line (portrait, name, text; its voice once).</summary>
        void UpdateRadio()
        {
            radioReveal.Tick(Time.unscaledDeltaTime * 1000f);   // the animated dialogue option
            var radio = level != null && level.Campaign != null ? level.Campaign.Radio : null;
            var line = radio?.Visible;
            int index = radio != null ? radio.VisibleIndex : -1;
            // Generic chatter (Level::createRadioMessage) in the same box while the level's own radio is quiet.
            var chatter = line == null && level != null && level.Traffic != null ? level.Traffic.ChatterVisible : null;
            if (chatter != null)
            {
                radioBox.EnableInClassList("radio--shown", true);
                if (chatter == shownChatter) return;
                shownChatter = chatter;
                radioShown = -1;
                radioSpeaker.text = chatter.speaker.UiCaps();
                bool chatterAlien = chatter.portrait == null && chatter.character == null && StoryTable.UsesAlienFont(chatter.speakerId);
                AlienText.Set(radioText, chatter.text, chatterAlien);
                if (chatter.character != null) Portrait.ShowCharacter(radioPortrait, Modding.ModCharacters.Find(chatter.character), false);
                else if (chatter.portrait != null) Portrait.Show(radioPortrait, chatter.portrait, false);
                else Portrait.ShowSpeaker(radioPortrait, chatter.speakerId, false);
                var voiceClip = StoryAssets.Load()?.Voice(chatter.voice);
                if (voiceClip != null) level.Traffic.HoldChatter(voiceClip.length * 1000f + 500f);
                if (chatter.portrait == null && StoryTable.IsNarration(chatter.speakerId)) radioReveal.Clear();
                else radioReveal.Begin(chatter.text, chatterAlien, voiceClip);
                if (voiceClip != null && voiceSource != null) { voiceSource.clip = GoF2Remake.Modding.ModSounds.Get(voiceClip); voiceSource.volume = Settings.VoiceVolume; voiceSource.Play(); }
                return;
            }
            shownChatter = null;
            radioBox.EnableInClassList("radio--shown", line != null);
            if (line == null || index == radioShown) { if (line == null) radioShown = -1; return; }
            radioShown = index;
            radioSpeaker.text = StoryTable.SpeakerName(line.speaker).UiCaps();
            string lineText = Localization.Get(line.text);
            bool lineAlien = StoryTable.UsesAlienFont(line.speaker);
            AlienText.Set(radioText, lineText, lineAlien);
            // Radio::update: images 21 and 63+ are no story face but a random one of a race (ImageFactory::createChar(male,
            // race): 0x40 Terran, 0x41 Nivelian, 0x15 Midorian, else Vossk), named 1597 + image ("Vossk", ...).
            if (line.speaker == 0x15 || (line.speaker >= 0x3f && line.speaker < 10000))
                Portrait.Show(radioPortrait, AgentGenerator.CreatePortrait(true, line.speaker == 0x40 ? 0 : line.speaker == 0x41 ? 2 : line.speaker == 0x15 ? 3 : 1), false);
            else Portrait.ShowSpeaker(radioPortrait, line.speaker, false);
            var clip = StoryAssets.Load()?.Voice(line.voice);
            if (clip != null) radio.HoldFor(clip.length * 1000f + 500f);
            if (StoryTable.IsNarration(line.speaker)) radioReveal.Clear();
            else radioReveal.Begin(lineText, lineAlien, clip);
            if (clip != null && voiceSource != null) { voiceSource.clip = GoF2Remake.Modding.ModSounds.Get(clip); voiceSource.volume = Settings.VoiceVolume; voiceSource.Play(); }
        }
        /// <summary>The action prompt sits under the radio box while a radio line shows (remake layout: both are centred at
        /// the top; the box's height depends on its lines, so it is measured).</summary>
        void PlaceDockPrompt()
        {
            if (!radioBox.ClassListContains("radio--shown") || dockPrompt.parent == null)
            {
                dockPrompt.style.top = StyleKeyword.Null;
                return;
            }
            var r = radioBox.worldBound;
            if (float.IsNaN(r.yMax) || r.height <= 0f) return;
            float y = dockPrompt.parent.WorldToLocal(new Vector2(r.center.x, r.yMax)).y + 12f;
            dockPrompt.style.top = Mathf.Max(120f, y);
        }
        /// <summary>The autopilot button (HUD key 0x40, MGame::OnTouchEnd): turns the autopilot off, cancels an asteroid
        /// approach, or opens / closes the autopilot menu.</summary>
        void OnAutopilotButton()
        {
            if (nav == null) return;
            if (nav.MenuOpen) CloseAutopilotMenu();
            else if (nav.Autopilot) nav.Interact();
            else if (mining != null && mining.State == Mining.Phase.Approaching) mining.Interact();
            else if (nav.CanOpenMenu) OpenAutopilotMenu();
        }
        /// <summary>The quick menu button (HUD key 4, MGame::OnTouchEnd; the PC version's E "Actions"): opens / closes the
        /// action menu, on the autopilot too.</summary>
        void OnActionsButton()
        {
            if (nav == null) return;
            if (nav.MenuOpen) CloseAutopilotMenu();
            else if (nav.CanOpenActions && nav.MenuEntries(true).Count > 0) OpenAutopilotMenu(true);
        }
    }
}
