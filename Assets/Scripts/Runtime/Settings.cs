// Settings.cs
// Player options (original: Globals::init option defaults / the options menu: music, sound and voice volume,
// brightness, quality, steering sensitivity, invert, Reference/research/mainmenu_notes.md 2.6), stored in PlayerPrefs.
// Plain C# so menus and game code can share it; Bootstrap applies the process-wide ones. The rest are remake options.

using System;
using System.Collections.Generic;
using UnityEngine;

using R = GoF2Remake.Multiplayer.NetRules;

namespace GoF2Remake.Data
{
    /// <summary>Frame rate option (remake only; the original ran at the device's fixed rate).</summary>
    public enum FrameRate { Fps30, Fps60, Fps120, Uncapped, VSync }

    /// <summary>Window mode option (desktop only).</summary>
    public enum DisplayMode { Borderless, Fullscreen, Windowed }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Settings
    {
        const string Prefix = "gof2.";

        /// <summary>The original's vertical field of view (MGame::reset, 1.22 rad).</summary>
        public const float OriginalFov = 1.22f * Mathf.Rad2Deg;
        public const float DefaultDeadzone = 0.125f;   // the Input System's default stick dead zone

        public static event Action Changed;

        // ---- sound -------------------------------------------------------------------------------------------

        public static float MasterVolume { get => Get("masterVolume", 1f); set => Set("masterVolume", Mathf.Clamp01(value)); }
        public static float MusicVolume { get => Get("musicVolume", 0.8f); set => Set("musicVolume", Mathf.Clamp01(value)); }
        public static float SfxVolume { get => Get("sfxVolume", 1f); set => Set("sfxVolume", Mathf.Clamp01(value)); }
        public static float VoiceVolume { get => Get("voiceVolume", 1f); set => Set("voiceVolume", Mathf.Clamp01(value)); }

        // ---- graphics ----------------------------------------------------------------------------------------

        public static DisplayMode DisplayMode
        {
            get => (DisplayMode)Mathf.Clamp(Mathf.RoundToInt(Get("displayMode", 0f)), 0, (int)DisplayMode.Windowed);
            set => Set("displayMode", (float)value);
        }

        /// <summary>Screen resolution (desktop); 0 x 0 = the display's own.</summary>
        public static Vector2Int Resolution
        {
            get => new Vector2Int(Mathf.RoundToInt(Get("resolutionWidth", 0f)), Mathf.RoundToInt(Get("resolutionHeight", 0f)));
            set { SetQuiet("resolutionWidth", value.x); Set("resolutionHeight", value.y, force: true); }
        }

        /// <summary>Frame rate limit, applied by Bootstrap. V-Sync (display refresh rate) by default.</summary>
        public static FrameRate FrameRate
        {
            get => (FrameRate)Mathf.Clamp(Mathf.RoundToInt(Get("frameRate", (float)FrameRate.VSync)), 0, (int)FrameRate.VSync);
            set => Set("frameRate", (float)value);
        }

        /// <summary>URP render scale; 0 = the platform's render pipeline asset (1 on PC, 0.8 on mobile).</summary>
        public static float RenderScale { get => Get("renderScale", 0f); set => Set("renderScale", value <= 0f ? 0f : Mathf.Clamp(value, 0.5f, 2f)); }

        /// <summary>Upscaler: 0 off (URP's automatic bilinear / point), 1 AMD FSR 1 (sharpening, also at 100 %), 2 Unity STP
        /// (temporal: anti-aliasing and upscaling, replaces MSAA), 3 NVIDIA DLSS and 4 AMD FSR 2 / 3 / 4 (temporal, the render
        /// resolution by UpscalerQuality; desktop builds with the upscaler framework only, UpscalerFramework), 5 Apple MetalFX
        /// Spatial and 6 MetalFX Temporal (macOS / iOS on Metal, from the render scale; 6 temporal like STP).</summary>
        public static int Upscaler { get => Mathf.RoundToInt(Get("upscaler", 0f)); set => Set("upscaler", Mathf.Clamp(value, 0, 6)); }
        public const int UpscalerOff = 0, UpscalerFsr = 1, UpscalerStp = 2, UpscalerDlss = 3, UpscalerFsrTemporal = 4,
            UpscalerMetalFxSpatial = 5, UpscalerMetalFxTemporal = 6;

        /// <summary>DLSS / FSR 2+ quality mode (UpscalerFramework.Quality*): 0 native (DLAA / native AA), 1 quality (default),
        /// 2 balanced, 3 performance, 4 ultra performance.</summary>
        public static int UpscalerQuality { get => Mathf.RoundToInt(Get("upscalerQuality", 1f)); set => Set("upscalerQuality", Mathf.Clamp(value, 0, 4)); }

        /// <summary>MSAA samples (1 = off, 2, 4, 8); 0 = the platform's render pipeline asset.</summary>
        public static int Msaa { get => Mathf.RoundToInt(Get("msaa", 0f)); set => Set("msaa", value); }

        /// <summary>Original "Quality" option (504; texts 507-512): 0 Low (smoke off, fog off, detail low), 1 Medium (smoke
        /// off, fog off, detail medium), 2 High (everything on). Default High (Globals::init options+0x28 = 1.0).</summary>
        public static int Quality { get => Mathf.RoundToInt(Get("quality", 2f)); set => Set("quality", Mathf.Clamp(value, 0, 2)); }
        public static bool QualityEffects => Quality >= 2;

        /// <summary>Original "Brightness" option: 0 Dark, 1 Medium, 2 Bright (texts 513-515).</summary>
        public static int Brightness { get => Mathf.RoundToInt(Get("brightness", 1f)); set => Set("brightness", Mathf.Clamp(value, 0, 2)); }

        /// <summary>Exposure offset for the brightness option (applied through post-processing).</summary>
        public static float BrightnessExposure => (Brightness - 1) * 0.35f;

        /// <summary>Post-processing bloom on/off.</summary>
        /// <summary>Bloom: 0 off, 1 the remake's (URP Bloom on the HDR glow of lights, engines and effects), 2 the
        /// original's (ClassicBloomPass: every bright pixel, a soft 256 x 256 glow), 3 both (#45: the remake's glow on the
        /// lights with the original's on the sky, the stars and the planets). Old saves: the "bloom" toggle.</summary>
        public static int BloomStyle
        {
            get => Mathf.RoundToInt(Get("bloomStyle", GetBool("bloom", true) ? BloomRemake : BloomOff));
            set => Set("bloomStyle", value);
        }
        public const int BloomOff = 0, BloomRemake = 1, BloomOriginal = 2, BloomBoth = 3;

        /// <summary>URP's Bloom on the global volumes (the "Remake" bloom).</summary>
        public static bool Bloom => BloomStyle == BloomRemake || BloomStyle == BloomBoth;
        /// <summary>The original's bloom (ClassicBloomPass), alone or with the remake's.</summary>
        public static bool ClassicBloom => BloomStyle == BloomOriginal || BloomStyle == BloomBoth;

        /// <summary>The sun's lens flare in flight (LensFlareView).</summary>
        public static bool LensFlare { get => GetBool("lensFlare", true); set => SetBool("lensFlare", value); }

        /// <summary>Remake: the hangar ships' contact shadows (HangarShipShadow): 0 off, 1 only the player's own ship, 2 every
        /// ship (default). Off also drops the station camera's depth texture they read.</summary>
        public static int HangarShadows { get => Mathf.Clamp(Mathf.RoundToInt(Get("hangarShadows", HangarShadowsAll)), 0, 2); set => Set("hangarShadows", Mathf.Clamp(value, 0, 2)); }
        public const int HangarShadowsOff = 0, HangarShadowsPlayer = 1, HangarShadowsAll = 2;
        /// <summary>Remake: the hangar view's depth of field on the player's ship (StationLevel); off by default on phones.</summary>
        public static bool HangarDepthOfField { get => GetBool("hangarDof", !Application.isMobilePlatform); set => SetBool("hangarDof", value); }

        /// <summary>Remake: NPC ships fly with the player's engine system (the *_engine_glow_add mesh and the exhaust
        /// particles) instead of the original's *_engine_add mesh; from the next spawn.</summary>
        public static bool NpcPlayerEngines { get => GetBool("npcPlayerEngines", true); set => SetBool("npcPlayerEngines", value); }

        /// <summary>The chase camera's vertical field of view in degrees (at 16:9; the original's is OriginalFov).</summary>
        public static float FieldOfView { get => Get("fov", OriginalFov); set => Set("fov", Mathf.Clamp(value, 55f, 95f)); }

        /// <summary>Camera shake and rumble strength, 0..1 (hits, collisions, explosions, cutscenes).</summary>
        public static float CameraShake { get => Get("cameraShake", 1f); set => Set("cameraShake", Mathf.Clamp01(value)); }

        // ---- controls ----------------------------------------------------------------------------------------

        /// <summary>Steering sensitivity (FlightModel.Sensitivity, 0..2.2).</summary>
        public static float Sensitivity { get => Get("sensitivity", 1f); set => Set("sensitivity", Mathf.Clamp(value, 0f, 2.2f)); }
        /// <summary>Flight steering, up / down inverted (options[0x10]'s "Invert controls", 500; every input: stick, keys,
        /// touch, tilt, mouse).</summary>
        public static bool InvertPitch { get => GetBool("invertPitch", false); set => SetBool("invertPitch", value); }
        /// <summary>Remake: flight steering, left / right inverted.</summary>
        public static bool InvertYaw { get => GetBool("invertYaw", false); set => SetBool("invertYaw", value); }
        /// <summary>Remake: the mining minigame's drill inverted on its own, up / down and left / right.</summary>
        public static bool InvertDrillY { get => GetBool("invertDrillY", false); set => SetBool("invertDrillY", value); }
        public static bool InvertDrillX { get => GetBool("invertDrillX", false); set => SetBool("invertDrillX", value); }
        /// <summary>Globals::mouseCursorActivated (the PC version): the mouse moves the crosshair and steers (desktop only).</summary>
        public static bool MouseSteering { get => GetBool("mouseSteering", true); set => SetBool("mouseSteering", value); }
        /// <summary>Remake: mouse steering's dead zone, a fraction of the steering range around the centre (0 = none, the
        /// original's: PlayerEgo::update steers by the raw offset).</summary>
        public static float MouseDeadzone { get => Get("mouseDeadzone", DefaultMouseDeadzone); set => Set("mouseDeadzone", Mathf.Clamp(value, 0f, 0.3f)); }
        public const float DefaultMouseDeadzone = 0.08f;
        /// <summary>Remake: Discord Rich Presence (DiscordPresence, desktop).</summary>
        public static bool DiscordPresence { get => GetBool("discordPresence", true); set => SetBool("discordPresence", value); }
        /// <summary>options[0x11] = 0: the accelerometer steers (MGame::handleAccelerometer).</summary>
        public static bool TiltSteering { get => GetBool("tiltSteering", false); set => SetBool("tiltSteering", value); }
        /// <summary>options+0x18: the tilt sensitivity, 0..1 (default 1, the maximum).</summary>
        public static float TiltSensitivity { get => Get("tiltSensitivity", 1f); set => Set("tiltSensitivity", Mathf.Clamp01(value)); }
        /// <summary>Remake (Windows): a motion controller's gyro steers (ControllerGyro, JoyShockLibrary).</summary>
        public static bool GyroSteering { get => GetBool("gyroSteering", false); set => SetBool("gyroSteering", value); }
        /// <summary>Remake: the gyro's speed; 1 = a full steering offset for 20 degrees of controller rotation.</summary>
        public static float GyroSensitivity { get => Get("gyroSensitivity", 1f); set => Set("gyroSensitivity", Mathf.Clamp(value, 0.25f, 3f)); }
        /// <summary>options+0x1c / +0x20: the calibrated position (Globals::init 0.6 / 0.6).</summary>
        public static float TiltCalX { get => Get("tiltCalX", 0.6f); set => Set("tiltCalX", value); }
        public static float TiltCalZ { get => Get("tiltCalZ", 0.6f); set => Set("tiltCalZ", value); }
        public static bool TiltCalibrated { get => GetBool("tiltCalibrated", false); set => SetBool("tiltCalibrated", value); }

        /// <summary>Remake: haptic feedback strength, 0..1 (0 = off): the controller's rumble and the phone's vibration
        /// (Haptics). Full by default.</summary>
        public static float HapticsIntensity { get => Get("haptics", 1f); set => Set("haptics", Mathf.Clamp01(value)); }

        /// <summary>Controller stick dead zone (InputSettings.defaultDeadzoneMin).</summary>
        /// <summary>Remake (#61, the original's Options > Controls > Configure, options+0x54 / +0x58): where the touch
        /// controls' left group (the stick) and right cluster (fire) sit, 0 = highest, 1 = lowest the original allows;
        /// -1 = the original's defaults (S 415, F 365; TouchControls).</summary>
        public static float TouchLeftHeight { get => Get("touchLeftHeight", -1f); set => Set("touchLeftHeight", value < 0f ? -1f : Mathf.Clamp01(value)); }
        public static float TouchRightHeight { get => Get("touchRightHeight", -1f); set => Set("touchRightHeight", value < 0f ? -1f : Mathf.Clamp01(value)); }
        public static float StickDeadzone { get => Get("stickDeadzone", DefaultDeadzone); set => Set("stickDeadzone", Mathf.Clamp(value, 0.05f, 0.4f)); }

        // ---- gameplay ----------------------------------------------------------------------------------------

        /// <summary>The launch / arrival camera (LevelScript's start sequence); off = straight to the chase camera.</summary>
        /// <summary>VR flight: the cockpit's grabbable stick and throttle (Vr.VrControls) instead of only the controllers as a gamepad.</summary>
        public static bool VrGrabControls { get => GetBool("vrGrabControls", false); set => SetBool("vrGrabControls", value); }

        /// <summary>The launch / arrival fly-in camera (off in VR: a moving outside camera is a motion-sickness trigger).</summary>
        public static bool LaunchCamera { get => GetBool("launchCamera", true) && !Vr.VrMode.Enabled; set => SetBool("launchCamera", value); }

        /// <summary>Remake: the ship flies into the hangar after docking and out of it when launching (HangarFlight).</summary>
        public static bool HangarFlights { get => GetBool("hangarFlights", true); set => SetBool("hangarFlights", value); }

        /// <summary>Remake: the tutorial popups (the flight hints, the stations' first-visit help, the map and hangar hints;
        /// FlightHints, StationMenu, HangarWindow). Off by default; a new game asks (MainMenu.StartGame). While off, a hint is
        /// not marked as shown, so turning it on later still shows it once.</summary>
        public static bool TutorialHints { get => GetBool("tutorialHints", false); set => SetBool("tutorialHints", value); }

        /// <summary>Informer missions (#28): false = the remake's rule (once the spy is dead, other deaths in its orbit no
        /// longer spoil the mission); true = the original's (PlayerFighter::update 0xf1c8c: any other ship dying before the
        /// docking fails it, even after the spy). A session rule (NetRules).</summary>
        public static bool InformerOriginalRule { get => Rule(R.InformerOriginalRule, "informerOriginalRule", false); set => SetRule(R.InformerOriginalRule, "informerOriginalRule", value); }
        /// <summary>The ship lock as the original picks it (Radar::draw: the first ship of the list in the box, any faction,
        /// and any completed lock replaces the old one); off (default) = the remake's smarter lock (CombatRadar).</summary>
        public static bool OriginalTargetLock { get => GetBool("originalTargetLock", false); set => SetBool("originalTargetLock", value); }
        /// <summary>The crate markers on a ship dying with cargo (Radar::draw, the original's; NpcShip.DyingWithCargo). Off
        /// (players' suggestion, for realism): no markers; aimed at, it is still salvage for the tractor beam.</summary>
        public static bool DyingCargoMarkers { get => GetBool("dyingCargoMarkers", true); set => SetBool("dyingCargoMarkers", value); }
        /// <summary>Remake (players' suggestion): NPC ships lose a cloaked target, fly to where they last saw it and go back to
        /// their routes (NpcShip.Pursuable). Off = the original: chased and aimed at, only not fired at. A session rule.</summary>
        public static bool CloakLosesPursuers { get => Rule(R.CloakLosesPursuers, "cloakLosesPursuers", false); set => SetRule(R.CloakLosesPursuers, "cloakLosesPursuers", value); }
        /// <summary>Remake (GitHub #6): now and then a free-flight orbit holds a pirate outpost or a pirate boss with escorts
        /// (TrafficPlan.AddPirateEvent), each with a bounty. A session rule.</summary>
        public static bool PirateEvents { get => Rule(R.PirateEvents, "pirateEvents", true); set => SetRule(R.PirateEvents, "pirateEvents", value); }
        /// <summary>Remake (players' suggestion): the Kaamo Club mechanics sell their upgrade again, the price doubling per
        /// level (LoungeChat.ModPrice); off = the original's one of each. Levels already fitted stay either way. A session
        /// rule.</summary>
        public static bool KaamoStacking { get => Rule(R.KaamoStacking, "kaamoStacking", true); set => SetRule(R.KaamoStacking, "kaamoStacking", value); }
        /// <summary>Remake (players' suggestion): a hull stored in the Kaamo Club keeps the items mounted on it (Hangar). A
        /// session rule.</summary>
        public static bool KaamoKeepsEquipment { get => Rule(R.KaamoKeepsEquipment, "kaamoKeepsEquipment", true); set => SetRule(R.KaamoKeepsEquipment, "kaamoKeepsEquipment", value); }
        /// <summary>Remake (players' suggestion): the Specter (44) and the Scimitar (49) have the U'tool built in
        /// (Ship::hasCloakIntegrated); a cloak mounted on them goes into that bay, replacing it, instead of taking an
        /// equipment slot (Hangar.HasCloakBay). Off = the original: it takes a slot. A session rule.</summary>
        public static bool CloakBay { get => Rule(R.CloakBay, "cloakBay", true); set => SetRule(R.CloakBay, "cloakBay", value); }
        /// <summary>Remake (players' suggestion): the capital ships fight back (World.CapitalShips): escorts, stronger turrets,
        /// a killable carrier and Vossk battleship with loot, the carrier's Inflicts and its resupply dock. Off = the original.
        /// A session rule.</summary>
        public static bool CapitalShips { get => Rule(R.CapitalShips, "capitalShips", false); set => SetRule(R.CapitalShips, "capitalShips", value); }

        /// <summary>The Gameplay options that change how the game plays are the session's in multiplayer (NetRules), else the
        /// player's own.</summary>
        static bool Rule(int bit, string key, bool fallback) => R.TryGet(bit, out bool on) ? on : GetBool(key, fallback);
        static void SetRule(int bit, string key, bool value) { SetBool(key, value); R.HostChanged(bit, value); }

        /// <summary>The player's own values of the session rules (a player host's start, NetRules).</summary>
        public static int LocalRules()
        {
            int m = 0;
            if (GetBool("cloakLosesPursuers", false)) m |= R.CloakLosesPursuers;
            if (GetBool("pirateEvents", true)) m |= R.PirateEvents;
            if (GetBool("capitalShips", false)) m |= R.CapitalShips;
            if (GetBool("informerOriginalRule", false)) m |= R.InformerOriginalRule;
            if (GetBool("kaamoStacking", true)) m |= R.KaamoStacking;
            if (GetBool("kaamoKeepsEquipment", true)) m |= R.KaamoKeepsEquipment;
            if (GetBool("cloakBay", true)) m |= R.CloakBay;
            return m;
        }

        /// <summary>DialogueWindow::update: with voice, turn the page once the line has ended.</summary>
        public static bool AutoAdvanceDialogue { get => GetBool("autoAdvanceDialogue", true); set => SetBool("autoAdvanceDialogue", value); }

        /// <summary>Remake: the dialogue and radio text types in with pacing, shouting, actions and tinted names
        /// (TextReveal); off = the original's plain page at once.</summary>
        public static bool AnimatedDialogue { get => GetBool("animatedDialogue", true); set => SetBool("animatedDialogue", value); }

        /// <summary>The keyboard / controller hint rows of the HUDs (the touch controls always show).</summary>
        public static bool InputHints { get => GetBool("inputHints", true); set => SetBool("inputHints", value); }
        /// <summary>Remake: every primary weapon item sounds its own shots (the original: only the first primary gun).</summary>
        public static bool EachWeaponSound { get => GetBool("eachWeaponSound", false); set => SetBool("eachWeaponSound", value); }
        /// <summary>Remake: the touch fire button's double-press autofire latch on the keyboard / mouse / controller fire
        /// binding (the PC version had none).</summary>
        public static bool KeyAutofire { get => GetBool("keyAutofire", false); set => SetBool("keyAutofire", value); }
        /// <summary>Remake (#37): Level out also brings the nose to the horizon (FlightModel.LevelPitch); off = the original's
        /// roll only.</summary>
        public static bool LevelPitch { get => GetBool("levelPitch", true); set => SetBool("levelPitch", value); }
        /// <summary>Remake (desktop): no sound while the window isn't focused (Bootstrap; the volume, not AudioListener.pause,
        /// which the pause menu uses).</summary>
        public static bool MuteInBackground { get => GetBool("muteInBackground", false); set => SetBool("muteInBackground", value); }
        /// <summary>Remake: the frame rate at the top centre (FpsCounter).</summary>
        public static bool ShowFps { get => GetBool("showFps", false); set => SetBool("showFps", value); }
        /// <summary>Remake (#40): the story step at the bottom right (StoryStepLabel), for bug reports; off by default.</summary>
        public static bool ShowStoryStep { get => GetBool("showStoryStep", false); set => SetBool("showStoryStep", value); }

        /// <summary>Language code of Localization/text_{code}.json.</summary>
        public static string Language
        {
            get => PlayerPrefs.GetString(Prefix + "language", "en");
            set { PlayerPrefs.SetString(Prefix + "language", value); PlayerPrefs.Save(); Changed?.Invoke(); }
        }

        /// <summary>Remake: the voice language apart from the text: "auto" = German voices with the German text (the
        /// original's voice bank switch), else English; "en" / "de" fixed. Only English and German were recorded.</summary>
        public static string VoiceLanguage
        {
            get => PlayerPrefs.GetString(Prefix + "voiceLanguage", "auto");
            set { PlayerPrefs.SetString(Prefix + "voiceLanguage", value); PlayerPrefs.Save(); Changed?.Invoke(); }
        }

        /// <summary>Whether the German voice lines play (VoiceLanguage, "auto" follows the text language).</summary>
        public static bool GermanVoices => VoiceLanguage switch { "de" => true, "en" => false, _ => Language == "de" };

        // "Default settings" (497) by Options tab (OptionsCatalog.ResetPage): the keys of every option on it. A new option's
        // key goes in its tab's list. Not reset: the text language and the tilt calibration (taken on the device, not an
        // option); the Key bindings tab resets the bindings (GameControls), the in-game Difficulty and Debug tools rows are
        // the game's / Cheats' state.
        public static readonly string[] SoundKeys = { "masterVolume", "musicVolume", "sfxVolume", "voiceVolume", "eachWeaponSound", "muteInBackground" };
        public static readonly string[] GraphicsKeys =
        {
            "displayMode", "resolutionWidth", "resolutionHeight", "frameRate", "quality", "renderScale", "upscaler", "upscalerQuality",
            "msaa", "brightness", "bloom", "bloomStyle", "hangarShadows", "hangarDof", "lensFlare", "npcPlayerEngines", "fov",
            "cameraShake", "showFps",
        };
        public static readonly string[] ControlsKeys =
        {
            "tiltSteering", "tiltSensitivity", "sensitivity", "keyAutofire", "levelPitch", "invertPitch", "invertYaw", "invertDrillY",
            "invertDrillX", "gyroSteering", "gyroSensitivity", "mouseSteering", "mouseDeadzone", "vrGrabControls", "haptics", "stickDeadzone",
            "touchLeftHeight", "touchRightHeight",
        };
        public static readonly string[] GameplayKeys =
        {
            "launchCamera", "hangarFlights", "tutorialHints", "showStoryStep", "pirateEvents", "capitalShips", "kaamoStacking", "kaamoKeepsEquipment", "cloakBay", "informerOriginalRule",
            "originalTargetLock", "dyingCargoMarkers", "cloakLosesPursuers", "autoAdvanceDialogue", "animatedDialogue", "inputHints", "discordPresence",
        };
        public static readonly string[] LanguageKeys = { "voiceLanguage" };

        /// <summary>These options back to their defaults.</summary>
        public static void Reset(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                PlayerPrefs.DeleteKey(Prefix + key);
                cache.Remove(key);
            }
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>"Default settings" (497), every tab: every option back to its default and the key bindings, the language
        /// kept. (Before, a list from the early options: 19 later ones, animated dialogue, hangar flights and shadows, mouse
        /// steering, Show FPS, the target lock... were never reset.)</summary>
        public static void ResetToDefaults()
        {
            GoF2Remake.Flight.GameControls.ResetToDefaults();
            var all = new List<string>();
            foreach (var group in new[] { SoundKeys, GraphicsKeys, ControlsKeys, GameplayKeys, LanguageKeys }) all.AddRange(group);
            Reset(all);
        }

        // PlayerPrefs reads go through a cache: game code reads some options every frame.
        static readonly Dictionary<string, float> cache = new Dictionary<string, float>();

        static float Get(string key, float fallback)
        {
            if (cache.TryGetValue(key, out var v)) return v;
            v = PlayerPrefs.GetFloat(Prefix + key, fallback);
            cache[key] = v;
            return v;
        }

        static bool GetBool(string key, bool fallback) => Get(key, fallback ? 1f : 0f) > 0.5f;
        static void SetBool(string key, bool value) => Set(key, value ? 1f : 0f);

        static void SetQuiet(string key, float value)
        {
            PlayerPrefs.SetFloat(Prefix + key, value);
            cache[key] = value;
        }

        static void Set(string key, float value, bool force = false)
        {
            if (!force && Mathf.Approximately(PlayerPrefs.GetFloat(Prefix + key, float.NaN), value)) return;
            SetQuiet(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
