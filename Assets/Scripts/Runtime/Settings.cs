// Settings.cs
// Player options (original: Globals::init option defaults / the options menu: music, sound and voice volume,
// brightness, quality, steering sensitivity, invert, Reference/research/mainmenu_notes.md 2.6), stored in PlayerPrefs.
// Plain C# so menus and game code can share it; Bootstrap applies the process-wide ones. The rest are remake options.

using System;
using System.Collections.Generic;
using UnityEngine;

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
        /// original's (ClassicBloomPass: every bright pixel, a soft 256 x 256 glow). Old saves: the "bloom" toggle.</summary>
        public static int BloomStyle
        {
            get => Mathf.RoundToInt(Get("bloomStyle", GetBool("bloom", true) ? BloomRemake : BloomOff));
            set => Set("bloomStyle", value);
        }
        public const int BloomOff = 0, BloomRemake = 1, BloomOriginal = 2;

        /// <summary>URP's Bloom on the global volumes (the "Remake" bloom).</summary>
        public static bool Bloom => BloomStyle == BloomRemake;

        /// <summary>The sun's lens flare in flight (LensFlareView).</summary>
        public static bool LensFlare { get => GetBool("lensFlare", true); set => SetBool("lensFlare", value); }

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

        /// <summary>Remake: the remake's own ships (custom_ships.json: the USS Enterprise, the Jedi Starfighter...) can be had in
        /// single player: their lounge sellers, the debug Ships / spawn lists. Never in multiplayer (CustomShips.Available).
        /// Off keeps a custom ship the player already flies or stores.</summary>
        public static bool CustomShipsEnabled { get => GetBool("customShips", true); set => SetBool("customShips", value); }

        /// <summary>Remake (GitHub #6): now and then a free-flight orbit holds a pirate outpost or a pirate boss with escorts
        /// (TrafficPlan.AddPirateEvent), each with a bounty.</summary>
        public static bool PirateEvents { get => GetBool("pirateEvents", true); set => SetBool("pirateEvents", value); }

        /// <summary>DialogueWindow::update: with voice, turn the page once the line has ended.</summary>
        public static bool AutoAdvanceDialogue { get => GetBool("autoAdvanceDialogue", true); set => SetBool("autoAdvanceDialogue", value); }

        /// <summary>Remake: the dialogue and radio text types in with pacing, shouting, actions and tinted names
        /// (TextReveal); off = the original's plain page at once.</summary>
        public static bool AnimatedDialogue { get => GetBool("animatedDialogue", true); set => SetBool("animatedDialogue", value); }

        /// <summary>The keyboard / controller hint rows of the HUDs (the touch controls always show).</summary>
        public static bool InputHints { get => GetBool("inputHints", true); set => SetBool("inputHints", value); }
        /// <summary>Remake: every primary weapon item sounds its own shots (the original: only the first primary gun).</summary>
        public static bool EachWeaponSound { get => GetBool("eachWeaponSound", false); set => SetBool("eachWeaponSound", value); }
        /// <summary>Remake: the frame rate at the top centre (FpsCounter).</summary>
        public static bool ShowFps { get => GetBool("showFps", false); set => SetBool("showFps", value); }

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

        /// <summary>"Default settings" (497): every option back to its default, the language kept.</summary>
        public static void ResetToDefaults()
        {
            foreach (var key in new[]
                     {
                         "masterVolume", "musicVolume", "sfxVolume", "voiceVolume", "displayMode", "resolutionWidth", "resolutionHeight",
                         "frameRate", "renderScale", "upscaler", "upscalerQuality", "msaa", "quality", "brightness", "bloom", "bloomStyle", "lensFlare", "npcPlayerEngines", "fov", "cameraShake",
                         "sensitivity", "invertPitch", "invertYaw", "invertDrillY", "invertDrillX", "gyroSteering", "gyroSensitivity", "haptics", "stickDeadzone", "mouseDeadzone", "launchCamera", "autoAdvanceDialogue", "inputHints",
                         "pirateEvents", "tutorialHints",
                     })
                PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
            cache.Clear();
            GoF2Remake.Flight.GameControls.ResetToDefaults();   // the key bindings too
            Changed?.Invoke();
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
