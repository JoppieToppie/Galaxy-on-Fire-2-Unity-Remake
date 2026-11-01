// FlightHudGameOver.cs
// FlightHud: game over (MGame's game-over state) and the button sounds (part of the partial FlightHud; its fields are in FlightHud.cs).

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
        void OnGameOver()
        {
            if (GoF2Remake.Multiplayer.NetArenaClient.InMatch) return;   // an arena match respawns the ship instead
            gameOverMs = 0f;
            gameOver.AddToClassList("game-over--shown");
            // Remake (hardcore): the run's saves go at once (closing the game now can't keep them), Tap leaves to the menu.
            if (Session.Hardcore && !GoF2Remake.Multiplayer.NetGame.Active && !GoF2Remake.Events.EventRespawn.Active)
            {
                SaveGame.DeleteRun(Session.RunId);
                hardcoreDead = true;
                gameOverText.text = Localization.Extra("hardcoreDead", "Hardcore: your pilot is lost and the saves are deleted. Tap for the main menu.");
                return;
            }
            if (level != null && level.Challenge != null)   // the Supernova Challenge: the round's end asks "Play again?" (ChallengeOrbit)
            {
                gameOverText.text = "";
                return;
            }
            if (Modding.ModBuilder.TestFlying || Modding.ModBuilder.PlayTesting)   // a Mod Builder test flight / play test: back to the builder
            {
                gameOverText.text = Localization.Extra("mbTestDead", "Tap to return to the Mod Builder.");
                return;
            }
            gameOverText.text = GoF2Remake.Events.EventRespawn.Active || GoF2Remake.Multiplayer.NetCoop.RespawnsInSpace
                ? Localization.Extra("mpRespawnEvent", "Respawning in space...")      // an event's respawn point, a co-op guest beside the leader
                : GoF2Remake.Multiplayer.NetGame.Active
                ? Localization.Extra("mpRespawn", "Tap to respawn at the station.")   // multiplayer: no saves, docked again
                : Localization.Get(Session.HasAutosave ? 196 : 199);
        }
        /// <summary>Overlay fades in after 3000 ms over 4000 ms, then the blinking "Tap to load last savegame.".</summary>
        void UpdateGameOver()
        {
            if (gameOverMs < 0f) return;
            gameOverMs += Time.unscaledDeltaTime * 1000f;
            gameOver.EnableInClassList("game-over--dim", gameOverMs > 3000f);
            bool ready = gameOverMs > 7000f;
            // The original blinks "Tap to load last savegame." every 500 ms; the remake keeps it on (it fades in once).
            gameOverText.EnableInClassList("game-over-text--shown", ready);
            if (ready && ((GoF2Remake.Multiplayer.NetChat.Keys != null && GoF2Remake.Multiplayer.NetChat.Keys.anyKey.wasPressedThisFrame)
                          || (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame))))
                LoadLastSave();
        }
        /// <summary>GameRecord::load(last save) -> the station; no save -> the main menu.</summary>
        void LoadLastSave()
        {
            if (gameOverMs < 7000f) return;
            gameOverMs = -1f;
            if (hardcoreDead) { BackToMenu(); return; }   // hardcore: nothing to load
            if (level != null && level.Challenge != null) { gameOverMs = 7000f; return; }   // the challenge's question answers it
            if (Modding.ModBuilder.TestFlying) { Modding.ModBuilder.EndTestFlight(false); return; }
            if (Modding.ModBuilder.PlayTesting) { ModBuilderPlay.End(); return; }
            // Multiplayer: back at the station last docked at (like single player's auto-save), repaired (docking repairs),
            // everything else kept. Not the faction's home any more: dying was a free trip home.
            if (GoF2Remake.Multiplayer.NetGame.Active)
            {
                int docked = Session.LastDockedStation;
                // A defender destroyed in their station's siege fight respawns there (NetFactions).
                bool defending = GoF2Remake.Multiplayer.NetFactionsClient.DefendsSiegeHere(Session.StationIndex);
                if (!defending && docked >= 0 && docked < GoF2Remake.Multiplayer.NetGame.Db.Stations.Count) Session.StationIndex = docked;
                Session.DockedFromSpace = false;
                SceneManager.LoadScene("Station");
                return;
            }
            if (Session.LoadAutosave() && Application.CanStreamedLevelBeLoaded("Station")) SceneManager.LoadScene("Station");
            else BackToMenu();
        }
        /// <summary>Hud::drawOrbitInformation during the arrival camera after a gate / Khador jump.</summary>
        void UpdateOrbitInfo()
        {
            bool show = level != null && level.OrbitInfoVisible;
            orbitInfo.EnableInClassList("orbit-info--shown", show);
            root.EnableInClassList("hud-orbit-info", show);   // it takes the speed panel's corner
            if (!show || orbitInfoFilled) return;
            orbitInfoFilled = true;
            var st = level.StationInfo;
            int system = level.Layout.systemIndex, race = level.Layout.raceId;
            bool owned = GalaxyMap.HasOwner(system) && race >= 0 && race <= 3;
            var logo = orbitInfo.Q("orbitLogo");
            var tex = owned ? Resources.Load<Texture2D>($"GoF2Hud/logo_{race}") : null;
            logo.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (tex != null) { logo.style.backgroundImage = new StyleBackground(tex); logo.style.width = tex.width; logo.style.height = tex.height; }
            orbitInfo.Q<Label>("orbitStation").text = st == null ? "" : st.index == 101 ? st.name : $"{st.name} {Localization.Get(136)}";
            orbitInfo.Q<Label>("orbitSystem").text = st == null ? "" : $"{st.systemName} {Localization.Get(137)}";
            string holder = st == null ? "" : GoF2Remake.Multiplayer.NetFactionsClient.OwnerText(st.index);   // a faction's station (NetFactions)
            if (holder.Length > 0) orbitInfo.Q<Label>("orbitSystem").text += $"  ·  {holder}";
            int sec = Mathf.Clamp(GalaxyMap.SecurityOf(level.Database.Systems.Find(s => s.index == system)), 0, 3);
            var secLabel = orbitInfo.Q<Label>("orbitSecurity");
            secLabel.text = Localization.Get(402 + sec);
            secLabel.style.color = (Color)GalaxyMap.SecurityColours[sec];
        }
        /// <summary>The action prompt: mining (mine / abort / stop) when it has something to do, else dock.</summary>
        /// <summary>Remake (#24): inside the docking range Dock beats a lock on the station (its autopilot) or on a planet
        /// behind it (a planet jump): pressing the action after the Dock prompt showed flew the ship off in a planet jump
        /// whenever the lock completed meanwhile, which looked like a launch.</summary>
        bool DockBeforeLock => level != null && level.CanDock && nav != null && !nav.Autopilot && nav.Locked != null
                               && (nav.Locked.kind == Navigation.Kind.Planet || nav.Locked.kind == Navigation.Kind.Station);
        /// <summary>The action's press came from Enter (its second default key).</summary>
        static bool ActionByEnter() =>
            GameControls.Action.activeControl is UnityEngine.InputSystem.Controls.KeyControl k
            && (k.keyCode == UnityEngine.InputSystem.Key.Enter || k.keyCode == UnityEngine.InputSystem.Key.NumpadEnter);
        void Interact()
        {
            if (docking != null && docking.PromptText != null) docking.Interact();
            else if (DockBeforeLock) Dock();
            else if (nav != null && nav.PromptText != null) nav.Interact();
            else if (mining != null && mining.PromptText != null) mining.Interact();
            else Dock();
        }
        /// <summary>The panel's pixel size: the screen, or the target texture when rendering offscreen (tests).</summary>
        Vector2Int ScreenSize()
        {
            var rt = runtimePanel != null ? runtimePanel.targetTexture : null;
            return rt != null ? new Vector2Int(rt.width, rt.height) : new Vector2Int(Screen.width, Screen.height);
        }
        void UpdateCrosshair()
        {
            var cam = Camera.main;
            if (cam == null || crosshair.panel == null) return;
            // Hud::draw at crosshairPos: in the turret view (PlayerEgo::setTurretMode) where the turret's gun points, the
            // plasma collectors with their own crosshair (0x1f5e, GoF2Hud/plasma_crosshair_idle; 0x1f5d plasma_crosshair while
            // plasma is in range); else the ship's nose.
            var viewTurret = level != null ? level.Turret : null;
            bool turretView = viewTurret != null && viewTurret.InTurretView;
            var aim = turretView ? viewTurret.GunPosition + viewTurret.AimForward * CrosshairDistanceMeters
                                 : ChaseCamera.AimOrigin(ship.transform) + ship.transform.forward * CrosshairDistanceMeters;
            bool collector = turretView && viewTurret.IsCollector;
            crosshair.EnableInClassList("crosshair--plasma", collector);
            // PlayerEgo::draw: 0x1f5d while Radar::isPlasmaInRange, else 0x1f5e (#78: the remake showed 0x1f5d all the time).
            crosshair.EnableInClassList("crosshair--plasma-in", collector && level.GasClouds != null && level.GasClouds.PlasmaInRange);
            bool visible = Vector3.Dot(aim - cam.transform.position, cam.transform.forward) > 0f;
            crosshair.EnableInClassList("crosshair--hidden", !visible);
            // PlayerEgo::draw 0xae4d0: on the autopilot (+0x158, setAutoPilot) only the throttle is drawn, no crosshair,
            // except in the turret view (#85: the remake kept it). Transparent, not hidden: the throttle gauge and the lock
            // rings still take its place.
            crosshair.EnableInClassList("crosshair--autopilot", nav != null && nav.Autopilot && !turretView);
            if (!visible) return;
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(crosshair.panel, aim, cam);
            var parent = crosshair.parent.worldBound;
            crosshair.style.left = p.x - parent.x;
            crosshair.style.top = p.y - parent.y;
            if (hitFlashMs > 0f) hitFlashMs -= Time.deltaTime * 1000f;
            crosshair.EnableInClassList("crosshair--hit", hitFlashMs > 0f);
        }
        void UpdateLayout()
        {
            lastScreen = ScreenSize();
            lastSafeArea = Screen.safeArea;
            lastLayout = UiLayout.Revision;
            bool offscreen = runtimePanel != null && runtimePanel.targetTexture != null;
            float w = Mathf.Max(1, lastScreen.x), h = Mathf.Max(1, lastScreen.y);
            var layout = UiLayout.Configure(runtimePanel, root, w, h);   // docs/UI-DESIGN.md "Scaling"
            // The radar ellipse (657 x 491 units) is drawn for a 1080-unit-high panel: shrunk with a shorter one (a phone's,
            // a larger interface size) so its top and bottom dots stay on screen (2026-10-16, players' report at 900).
            RadarScale = Mathf.Min(1f, layout.panel.y / UiLayout.RefHeight);
            var radar = root.Q("radarEllipse");   // a 0 x 0 box at the centre: scaled around it
            if (radar != null) radar.style.scale = new Scale(new Vector3(RadarScale, RadarScale, 1f));
            ApplySafeArea(w, h, offscreen);
            hudLook = -1;   // again on the next frame (a rebuilt layout)
        }
        /// <summary>Options > Gameplay "HUD size" / "HUD opacity" (docs/UI-DESIGN.md "HUD"), applied when they change.</summary>
        void ApplyHudLook()
        {
            int key = Settings.HudScalePercent * 1000 + Settings.HudOpacityPercent;
            if (key == hudLook || root == null) return;
            hudLook = key;
            float s = Settings.HudScalePercent / 100f, a = Settings.HudOpacityPercent / 100f;
            foreach (var (cls, x, y) in HudScaled)
                root.Query(className: cls).ForEach(e =>
                {
                    e.style.transformOrigin = new TransformOrigin(Length.Percent(x), Length.Percent(y));
                    e.style.scale = new Scale(new Vector3(s, s, 1f));
                });
            foreach (var cls in HudFaded) root.Query(className: cls).ForEach(e => e.style.opacity = a);
        }
        /// <summary>Device safe-area insets (the notch / Dynamic Island, the home indicator, cutouts), see SafeArea.</summary>
        void ApplySafeArea(float w, float h, bool offscreen) =>
            SafeArea.Apply(root, safeArea, w, h, offscreen, SafeArea.Mode.Position, px =>
                Debug.Log($"FlightHud: screen {w}x{h}, safe area {Screen.safeArea} -> insets {px} (l r t b), panel {root.layout.size}, input {InputMode.Current}"));
        void Dock()
        {
            if (level != null && level.CanDock) level.Dock(true);
        }
        void OpenPause()
        {
            if (level == null || level.DockedInOrbit || pauseMenu.IsOpen || (health != null && health.Dead) || StarMap.IsOpen || storyDialogue.IsOpen) return;
            if (nav != null && nav.MenuOpen) CloseAutopilotMenu();
            touch?.ReleaseAll();
            ship?.SetSteer(Vector2.zero);
            weapons?.SetPrimaryHeld(false);
            pauseMenu.Open(level);
        }
        void BackToMenu()
        {
            GoF2Remake.Multiplayer.NetGame.Shutdown();   // leaving a multiplayer session
            if (Application.CanStreamedLevelBeLoaded(menuScene)) SceneManager.LoadScene(menuScene);
        }
        void PlayButton(bool push)
        {
            if (push && pressFrame == Time.frameCount) return;
            if (!push)
            {
                if (releaseFrame == Time.frameCount) return;
                releaseFrame = Time.frameCount;
            }
            var audio = CombatAudio.Load();
            PlayUi(audio == null ? null : push ? audio.buttonPush : audio.buttonRelease);
        }
        void PlayUi(AudioClip clip) => PlayUi(clip, 1f);
        void PlayUi(AudioClip clip, float volume)
        {
            if (clip == null) return;
            if (uiSource == null)
            {
                uiSource = gameObject.AddComponent<AudioSource>();
                uiSource.playOnAwake = false;
                uiSource.spatialBlend = 0f;
                uiSource.ignoreListenerPause = true;   // the pause menu pauses the listener
            }
            uiSource.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume * volume);
        }
        /// <summary>Every button and toggle inside this container clicks. Modified: as in the main menu, only the release on the
        /// click (the push on the press is gone, also a button's own: PlayButton); not in the carrier's shop window (its own).</summary>
        void ButtonSounds(VisualElement container)
        {
            if (container == null) return;
            container.RegisterCallback<PointerDownEvent>(e => { if (UiHover.SilentPress(e.target)) pressFrame = Time.frameCount; }, TrickleDown.TrickleDown);
            container.RegisterCallback<ClickEvent>(e => { if (UiHover.ClickSounds(e.target)) PlayButton(false); });
        }
    }
}
