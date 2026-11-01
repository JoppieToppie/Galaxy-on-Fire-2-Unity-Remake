// Settings.cs
// Player options (original: Globals::init option defaults / the options menu: music, sound and voice volume,
// brightness, quality, steering sensitivity, invert, Reference/research/mainmenu_notes.md 2.6), stored in PlayerPrefs.
// Plain C# so menus and game code can share it; Bootstrap applies the process-wide ones. The rest are remake options.

using System;
using System.Collections.Generic;
using UnityEngine;

using R = GoF2Remake.Data.SessionRules;

namespace GoF2Remake.Data
{
    /// <summary>Frame rate option (remake only; the original ran at the device's fixed rate).</summary>
    public enum FrameRate { Fps30, Fps60, Fps120, Uncapped, VSync }

    /// <summary>Window mode option (desktop only).</summary>
    public enum DisplayMode { Borderless, Fullscreen, Windowed }

    /// <summary>Settings.FlightStyle's values.</summary>
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class FlightStyles
    {
        public const int Original = 0, Free = 1;

        /// <summary>The style the game flies with now: what a mod or an event sets (Modding.ModFlight: an event, the ship
        /// flown, the mod campaign, a new-game option), else the player's option (Settings.FlightStyle).</summary>
        public static int Current { get { int f = Modding.ModFlight.ForcedStyle; return f >= 0 ? f : Settings.FlightStyle; } }

        /// <summary>A mod or an event decides the style (the option's choice doesn't apply now).</summary>
        public static bool Forced => Modding.ModFlight.ForcedStyle >= 0;
    }

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

        /// <summary>Frame rate limit, applied by Bootstrap. V-Sync (display refresh rate) by default; 60 on Android (the user's
        /// choice, 2026-10-22: a 120 Hz phone rendering at 120 heats up and throttles).</summary>
        public static FrameRate FrameRate
        {
            get => (FrameRate)Mathf.Clamp(Mathf.RoundToInt(Get("frameRate", (float)(AndroidDefaults ? FrameRate.Fps60 : FrameRate.VSync))), 0, (int)FrameRate.VSync);
            set => Set("frameRate", (float)value);
        }

        /// <summary>URP render scale; 0 = the platform's render pipeline asset (1 on PC, 0.8 on mobile).</summary>
        public static float RenderScale { get => Get("renderScale", Default("renderScale", 0f)); set => Set("renderScale", value <= 0f ? 0f : Mathf.Clamp(value, 0.5f, 2f)); }

        /// <summary>Upscaler: 0 off (URP's automatic bilinear / point), 1 AMD FSR 1 (sharpening, also at 100 %), 2 Unity STP
        /// (temporal: anti-aliasing and upscaling, replaces MSAA), 3 NVIDIA DLSS and 4 AMD FSR 2 / 3 / 4 (temporal, the render
        /// resolution by UpscalerQuality; desktop builds with the upscaler framework only, UpscalerFramework), 5 Apple MetalFX
        /// Spatial and 6 MetalFX Temporal (macOS / iOS on Metal, from the render scale; 6 temporal like STP).</summary>
        public static int Upscaler { get => Mathf.RoundToInt(Get("upscaler", Default("upscaler", 0f))); set => Set("upscaler", Mathf.Clamp(value, 0, 6)); }
        public const int UpscalerOff = 0, UpscalerFsr = 1, UpscalerStp = 2, UpscalerDlss = 3, UpscalerFsrTemporal = 4,
            UpscalerMetalFxSpatial = 5, UpscalerMetalFxTemporal = 6;

        /// <summary>DLSS / FSR 2+ quality mode (UpscalerFramework.Quality*): 0 native (DLAA / native AA), 1 quality (default),
        /// 2 balanced, 3 performance, 4 ultra performance.</summary>
        public static int UpscalerQuality { get => Mathf.RoundToInt(Get("upscalerQuality", 1f)); set => Set("upscalerQuality", Mathf.Clamp(value, 0, 4)); }

        /// <summary>MSAA samples (1 = off, 2, 4, 8); 0 = the platform's render pipeline asset.</summary>
        public static int Msaa { get => Mathf.RoundToInt(Get("msaa", Default("msaa", 0f))); set => Set("msaa", value); }

        // The original's "Quality" option (504; texts 507-512: Low = smoke off, fog off, detail low; Medium = smoke off, fog
        // off, detail medium; High = everything on, the default, Globals::init options+0x28 = 1.0) split into its parts
        // (remake, the user's request 2026-10-23: it doubled the Graphics preset). A player who had set it keeps what it meant
        // (OldQuality) until they set the new options.

        /// <summary>Remake: the fire and smoke effects (ShipSmoke, ShipBurn, WreckBurn): damaged ships' smoke, the death burn,
        /// burning wrecks. The original's Quality Low / Medium turned them off.</summary>
        public static bool FireSmoke { get => GetBool("fireSmoke", OldQuality("fireSmoke", 1f) > 0.5f); set => SetBool("fireSmoke", value); }

        /// <summary>Remake: the fog of the systems that have it (Bootstrap.SetSceneFog). Off in the original's Quality Low / Medium.</summary>
        public static bool Fog { get => GetBool("fog", OldQuality("fog", 1f) > 0.5f); set => SetBool("fog", value); }

        /// <summary>Remake: how far the levels of detail switch (QualitySettings.lodBias x 0.35 / 0.6 / 1 / 2 for 0 Low,
        /// 1 Medium, 2 High, 3 Ultra; the original's Quality Low / Medium / High were the first three).</summary>
        public static int DetailDistance { get => Mathf.Clamp(Mathf.RoundToInt(Get("detailDistance", OldQuality("detailDistance", 2f))), 0, 3); set => Set("detailDistance", Mathf.Clamp(value, 0, 3)); }
        public static float DetailDistanceFactor => DetailDistance switch { 0 => 0.35f, 1 => 0.6f, 3 => 2f, _ => 1f };

        /// <summary>Remake: texture filtering (QualitySettings.anisotropicFiltering, Bootstrap): 0 bilinear (Disable),
        /// 1 anisotropic where a texture asks for it (Enable, the phones' quality level), 2 forced on every texture
        /// (ForceEnable, the desktop's quality level), 3 forced at 16x.</summary>
        public static int TextureFiltering { get => Mathf.Clamp(Mathf.RoundToInt(Get("textureFiltering", Default("textureFiltering", 2f))), 0, 3); set => Set("textureFiltering", Mathf.Clamp(value, 0, 3)); }

        /// <summary>A part of the original's Quality while the player had set it and not the new option: High 2 = every part
        /// on and detail High, Medium 1 / Low 0 = effects off and that detail. Else the option's default.</summary>
        static float OldQuality(string key, float other)
        {
            if (defaultReads < 0 && PlayerPrefs.HasKey(Prefix + "quality"))
            {
                int q = Mathf.Clamp(Mathf.RoundToInt(PlayerPrefs.GetFloat(Prefix + "quality", 2f)), 0, 2);
                return key == "detailDistance" ? q : q >= 2 ? 1f : 0f;
            }
            return Default(key, other);
        }

        /// <summary>Remake: Texture quality, the mipmap levels skipped when textures are loaded (QualitySettings.
        /// globalTextureMipmapLimit, Bootstrap): 0 full, 1 half (a quarter of the memory), 2 quarter. The space skies,
        /// reflection cubemaps and HUD images keep theirs (AssetImport.KeepsFullMips: ignoreMipmapLimit); textures made at
        /// run time (the mods') aren't affected. Half in the Low preset.</summary>
        public static int TextureQuality { get => Mathf.Clamp(Mathf.RoundToInt(Get("textureQuality", Default("textureQuality", 0f))), 0, 2); set => Set("textureQuality", Mathf.Clamp(value, 0, 2)); }

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
            get => Mathf.RoundToInt(Get("bloomStyle", GetBool("bloom", Default("bloomStyle", BloomRemake) != BloomOff) ? BloomRemake : BloomOff));
            set => Set("bloomStyle", value);
        }
        public const int BloomOff = 0, BloomRemake = 1, BloomOriginal = 2, BloomBoth = 3;

        /// <summary>URP's Bloom on the global volumes (the "Remake" bloom).</summary>
        public static bool Bloom => BloomStyle == BloomRemake || BloomStyle == BloomBoth;
        /// <summary>The original's bloom (ClassicBloomPass), alone or with the remake's.</summary>
        public static bool ClassicBloom => BloomStyle == BloomOriginal || BloomStyle == BloomBoth;

        /// <summary>The sun's lens flare in flight (LensFlareView).</summary>
        public static bool LensFlare { get => GetBool("lensFlare", Default("lensFlare", 1f) > 0.5f); set => SetBool("lensFlare", value); }

        /// <summary>Remake: the hangar ships' contact shadows (HangarShipShadow): 0 off, 1 only the player's own ship, 2 every
        /// ship (default). Off also drops the station camera's depth texture they read.</summary>
        public static int HangarShadows { get => Mathf.Clamp(Mathf.RoundToInt(Get("hangarShadows", Default("hangarShadows", HangarShadowsPlayer))), 0, 2); set => Set("hangarShadows", Mathf.Clamp(value, 0, 2)); }
        public const int HangarShadowsOff = 0, HangarShadowsPlayer = 1, HangarShadowsAll = 2;
        /// <summary>Remake: levels of detail (AutoLods): far objects drawn with fewer triangles, the original's LOD meshes
        /// and the ones made from the models (the game's prebaked, the mods' at run time). Off: every model at full detail
        /// (LOD 0, as the original binary's AEGeometry::updateLod). On by default on every platform (the user's choice,
        /// 2026-10-22; it was phones only).</summary>
        public static bool Lods { get => GetBool("lods", Default("lods", 1f) > 0.5f); set => SetBool("lods", value); }
        /// <summary>Remake: the hangar view's depth of field on the player's ship (StationLevel); off by default on phones.</summary>
        public static bool HangarDepthOfField { get => GetBool("hangarDof", Default("hangarDof", 0f) > 0.5f); set => SetBool("hangarDof", value); }   // off by default (2026-10)

        /// <summary>Remake: NPC ships fly with the player's engine system (the *_engine_glow_add mesh and the exhaust
        /// particles) instead of the original's *_engine_add mesh; from the next spawn. Off on Android (the Low preset): a
        /// particle system per NPC ship and the additive glow's overdraw.</summary>
        public static bool NpcPlayerEngines { get => GetBool("npcPlayerEngines", Default("npcPlayerEngines", 1f) > 0.5f); set => SetBool("npcPlayerEngines", value); }

        // ---- graphics presets (remake, the user's request 2026-10-22) -----------------------------------------------

        /// <summary>Options > Graphics "Graphics preset": Low, Medium, High and Ultra set these options at once (ApplyPreset);
        /// the row shows the preset the options match, else Custom (OptionsCatalog). High is the desktop's defaults, so a fresh
        /// install there shows High; Low is Android's defaults (AndroidDefaults: every key here a player never set), for
        /// lower-end phones' frame rate and heat; Ultra (the user's request 2026-10-23) every option at its most. The frame
        /// rate isn't part of them (Android's default is 60 on its own).</summary>
        public const int PresetLow = 0, PresetMedium = 1, PresetHigh = 2, PresetUltra = 3, PresetCustom = 4;

        public static readonly string[] PresetKeys =
        {
            "renderScale", "upscaler", "msaa", "bloomStyle", "hangarShadows", "hangarDof", "lensFlare", "npcPlayerEngines", "lods",
            "textureQuality", "fireSmoke", "fog", "detailDistance", "textureFiltering",
        };

        // One row per preset, in PresetKeys' order. Low / Medium as the original's Quality Low / Medium (fire, smoke and fog
        // off, detail distance x0.35 / x0.6); render scale with FSR 1 sharpening it back up (only where the device runs FSR,
        // Bootstrap.ActiveUpscaler); MSAA 1 = off (the flight camera's SMAA stays); bloom off on Low (both blooms are
        // full-screen passes). Ultra: 150 % render scale (200 % is in the row, but four times the pixels of 100 % with MSAA 8x
        // is past most GPUs), MSAA 8x, every hangar ship's shadow, depth of field, no made levels of detail, detail distance
        // x2, 16x filtering.
        static readonly float[][] Presets =
        {
            //      scale  upscaler     msaa bloom        hangar shadows       dof lens npcEng lods textures fire fog detail filter
            new[] { 0.67f, UpscalerFsr, 1f, BloomOff,    HangarShadowsOff,    0f, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 1f },
            new[] { 0.8f,  UpscalerFsr, 1f, BloomRemake, HangarShadowsPlayer, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 1f, 1f },
            new[] { 1f,    UpscalerOff, 1f, BloomRemake, HangarShadowsPlayer, 0f, 1f, 1f, 1f, 0f, 1f, 1f, 2f, 2f },
            new[] { 1.5f,  UpscalerOff, 8f, BloomRemake, HangarShadowsAll,    1f, 1f, 1f, 0f, 0f, 1f, 1f, 3f, 3f },
        };

        /// <summary>'key''s value in 'preset' (PresetLow..PresetUltra).</summary>
        public static float PresetValue(int preset, string key) => Presets[Mathf.Clamp(preset, 0, Presets.Length - 1)][Array.IndexOf(PresetKeys, key)];

        /// <summary>Sets every option of 'preset' (PresetLow..PresetUltra; anything else does nothing), one Changed.</summary>
        public static void ApplyPreset(int preset)
        {
            if (preset < 0 || preset >= Presets.Length) return;
            for (int i = 0; i < PresetKeys.Length; i++) SetQuiet(PresetKeys[i], Presets[preset][i]);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>The Android defaults: the Low preset and 60 fps (the user's choice, 2026-10-22). Tests set the override.</summary>
        public static bool AndroidDefaults => androidDefaultsOverride ?? Application.platform == RuntimePlatform.Android;
        static bool? androidDefaultsOverride;
        public static void OverrideAndroidDefaults(bool? android) { androidDefaultsOverride = android; cache.Clear(); }

        /// <summary>A preset option's default: the Low preset's value on Android, else 'other'.</summary>
        static float Default(string key, float other) => AndroidDefaults ? PresetValue(PresetLow, key) : other;

        /// <summary>The chase camera's vertical field of view in degrees (at 16:9; the original's is OriginalFov).</summary>
        public static float FieldOfView { get => Get("fov", OriginalFov); set => Set("fov", Mathf.Clamp(value, 55f, 95f)); }

        /// <summary>Camera shake and rumble strength, 0..1 (hits, collisions, explosions, cutscenes).</summary>
        public static float CameraShake { get => Get("cameraShake", 1f); set => Set("cameraShake", Mathf.Clamp01(value)); }

        /// <summary>The chase camera follows at the original's damping and looks at its eased point (#114, ChaseCamera); off
        /// (default) = the remake's tighter follow (2026.10.09's: twice the rate, a slerped rotation).</summary>
        public static bool OriginalChaseCamera { get => GetBool("originalChaseCamera", false); set => SetBool("originalChaseCamera", value); }

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
        /// <summary>Remake (#121): the cameras that turn round the ship or look around by a drag, a stick or keys inverted on
        /// their own, up / down and left / right: free look, Action Freeze, Inspect ship, the photo tools' free camera and the
        /// Mod Builder's views (not the flight's steering, which has its own pair).</summary>
        public static bool InvertOrbitY { get => GetBool("invertOrbitY", false); set => SetBool("invertOrbitY", value); }
        public static bool InvertOrbitX { get => GetBool("invertOrbitX", false); set => SetBool("invertOrbitX", value); }
        /// <summary>An orbit / look camera's turn input with InvertOrbitX / InvertOrbitY applied.</summary>
        public static Vector2 OrbitTurn(Vector2 d) => new Vector2(InvertOrbitX ? -d.x : d.x, InvertOrbitY ? -d.y : d.y);
        /// <summary>Globals::mouseCursorActivated (the PC version): the mouse moves the crosshair and steers (desktop only).</summary>
        public static bool MouseSteering { get => GetBool("mouseSteering", true); set => SetBool("mouseSteering", value); }
        /// <summary>Remake: the controller hints' button icons (InputMode.Style): 0 Auto (the controller last used: Sony's
        /// PlayStation, any other Xbox), 1 always Xbox, 2 always PlayStation.</summary>
        public static int PadGlyphs { get => Mathf.Clamp(Mathf.RoundToInt(Get("padGlyphs", PadGlyphsAuto)), 0, 2); set => Set("padGlyphs", Mathf.Clamp(value, 0, 2)); }
        public const int PadGlyphsAuto = 0, PadGlyphsXbox = 1, PadGlyphsPlayStation = 2;
        /// <summary>Remake: mouse steering's dead zone, a fraction of the steering range around the centre (0 = none, the
        /// original's: PlayerEgo::update steers by the raw offset).</summary>
        public static float MouseDeadzone { get => Get("mouseDeadzone", DefaultMouseDeadzone); set => Set("mouseDeadzone", Mathf.Clamp(value, 0f, 0.3f)); }
        public const float DefaultMouseDeadzone = 0.08f;
        /// <summary>Remake: the mouse's speed in flight (MouseMove: steering, the mining drill, free look), 1 = a pixel per
        /// count. The original's PC cursor moved at the system's pointer speed and its sensitivity option didn't apply to the
        /// mouse (PlayerEgo::up's fixed divisor 12); the Input System reads the raw counts, so a high-DPI mouse steered at
        /// full deflection after a flick (Discord "Mouse Sensitivity seems to do nothing").</summary>
        public static float MouseSensitivity { get => Get("mouseSensitivity", 1f); set => Set("mouseSensitivity", Mathf.Clamp(value, MouseSensitivityMin, MouseSensitivityMax)); }
        public const float MouseSensitivityMin = 0.1f, MouseSensitivityMax = 3f;
        /// <summary>A mouse movement (Mouse.delta) at the MouseSensitivity option.</summary>
        public static Vector2 MouseMove(Vector2 delta) => delta * MouseSensitivity;
        /// <summary>Remake: how the player's ship flies (FlightStyles): the original's (a throttle, the ship always flying
        /// forward) or free flight after EVERSPACE 2 (thrust, strafe and up / down held, inertial dampeners, the mouse aiming
        /// through a virtual joystick; Flight.FlightModel's free mode). It also switches the key layout (GameControls).</summary>
        public static int FlightStyle { get => Mathf.Clamp(Mathf.RoundToInt(Get("flightStyle", FlightStyles.Original)), 0, 1); set => Set("flightStyle", Mathf.Clamp(value, 0, 1)); }
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
        /// <summary>Remake (the user's request, 2026-10-27): a touch on the stick's area moves the stick under the thumb (that
        /// spot is the centre, no deflection) and it goes back to its place when let go; off = the original's fixed stick.</summary>
        public static bool FloatingTouchStick { get => GetBool("floatingTouchStick", false); set => SetBool("floatingTouchStick", value); }
        public static float StickDeadzone { get => Get("stickDeadzone", DefaultDeadzone); set => Set("stickDeadzone", Mathf.Clamp(value, 0.05f, 0.4f)); }

        // ---- gameplay ----------------------------------------------------------------------------------------

        /// <summary>The launch / arrival camera (LevelScript's start sequence); off = straight to the chase camera.</summary>
        /// <summary>VR flight: the cockpit's grabbable stick and throttle (Vr.VrControls) instead of only the controllers as a gamepad.</summary>
        public static bool VrGrabControls { get => GetBool("vrGrabControls", false); set => SetBool("vrGrabControls", value); }

        /// <summary>The launch / arrival fly-in camera (off in VR: a moving outside camera is a motion-sickness trigger).</summary>
        public static bool LaunchCamera { get => GetBool("launchCamera", true) && !VrFlags.Enabled; set => SetBool("launchCamera", value); }

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
        /// <summary>Remake, with the smarter lock: while a hostile ship is locked and alive no neutral or friendly ship becomes
        /// a lock candidate (CombatRadar); on by default, the smarter lock's behaviour before it was its own option. Off: the
        /// priority alone (hostile first, then the nearest), so a neutral ship can be scanned while an enemy is locked.</summary>
        public static bool KeepEnemyLock { get => GetBool("keepEnemyLock", true); set => SetBool("keepEnemyLock", value); }
        /// <summary>Remake mods: how often an NPC fighter flies one of the mods' ships of its race (Modding.ModTraffic): 0 off,
        /// 1 10 %, 2 25 % (default), 3 50 %.</summary>
        public static int ModShipTraffic { get => Mathf.Clamp(Mathf.RoundToInt(Get("modShipTraffic", 2)), 0, 3); set => Set("modShipTraffic", Mathf.Clamp(value, 0, 3)); }
        public static float ModShipTrafficChance => ModShipTraffic switch { 1 => 0.1f, 2 => 0.25f, 3 => 0.5f, _ => 0f };
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
        /// <summary>Remake: with KaamoStacking, how many times one upgrade can be fitted to a ship (Session.MaxModLevel;
        /// 2..20, default 5). A session rule: the host's or the server's value in multiplayer (-kaamomaxlevel,
        /// "/set kaamomaxlevel").</summary>
        public static int KaamoMaxLevel
        {
            get => GameHooks.SessionKaamoLevel != null && GameHooks.SessionKaamoLevel(out int n) ? R.ClampKaamoLevel(n) : LocalKaamoLevel();
            set
            {
                int v = R.ClampKaamoLevel(value);
                Set("kaamoMaxLevel", v);
                GameHooks.KaamoLevelChangedByHost?.Invoke(v);
            }
        }
        /// <summary>The player's own Kaamo upgrade limit (a player host's start, NetRules).</summary>
        public static int LocalKaamoLevel() => R.ClampKaamoLevel(Mathf.RoundToInt(Get("kaamoMaxLevel", R.DefaultKaamoLevel)));
        /// <summary>Remake (players' suggestion): a hull stored in the Kaamo Club keeps the items mounted on it (Hangar). A
        /// session rule.</summary>
        public static bool KaamoKeepsEquipment { get => Rule(R.KaamoKeepsEquipment, "kaamoKeepsEquipment", true); set => SetRule(R.KaamoKeepsEquipment, "kaamoKeepsEquipment", value); }
        /// <summary>Remake (players' suggestion): the Specter (44) and the Scimitar (49) have the U'tool built in
        /// (Ship::hasCloakIntegrated); a cloak mounted on them goes into that bay, replacing it, instead of taking an
        /// equipment slot (Hangar.HasCloakBay). Off = the original: it takes a slot. A session rule.</summary>
        public static bool CloakBay { get => Rule(R.CloakBay, "cloakBay", true); set => SetRule(R.CloakBay, "cloakBay", value); }
        /// <summary>Remake (players' suggestion): an EMP hit on a cloaked or charging cloak ends it at once (PlayerCloak.Break,
        /// NpcCloak.Break): the player's, a Specter's, another player's. Off = the original: nothing ends a cloak early. A
        /// session rule.</summary>
        public static bool EmpBreaksCloak { get => Rule(R.EmpBreaksCloak, "empBreaksCloak", true); set => SetRule(R.EmpBreaksCloak, "empBreaksCloak", value); }
        /// <summary>Remake (players' report, 2026-10-17: a raider group ignored the patrols beside it and shredded the
        /// player): an NPC fighter takes the nearest hostile target in its box, the player a little likelier, and spreads out
        /// over the targets (NpcShip.UpdateSmartTargeting, NpcTargetRules). Off (default since 2026-10-18, #121: players found
        /// the fights too easy) = the original: the player first. A session rule.</summary>
        public static bool SmarterNpcTargeting { get => Rule(R.SmarterNpcTargeting, "smarterNpcTargeting", false); set => SetRule(R.SmarterNpcTargeting, "smarterNpcTargeting", value); }
        /// <summary>Remake (players' suggestion): the capital ships fight back (World.CapitalShips): escorts, stronger turrets,
        /// a killable carrier and Vossk battleship with loot, the carrier's Inflicts and its resupply dock. Off = the original.
        /// A session rule.</summary>
        public static bool CapitalShips { get => Rule(R.CapitalShips, "capitalShips", false); set => SetRule(R.CapitalShips, "capitalShips", value); }
        /// <summary>Remake (players' report, 2026-10-17: since the mesh collision the fighters were hard to hit): Options >
        /// Gameplay "Ship collision", SessionRules.CollisionExact / Mixed / Original. Exact: every hull on its real shape
        /// (Flight.HullCollision). Mixed (default): the ships a player can fly on the original's hit cube and collision
        /// sphere (HullBody.flyableShip), freighters, capital ships, stations, gates, turrets and objects on their real
        /// shape. Original: no mesh collision at all, the original's boxes, spheres and cubes everywhere. A session rule (the
        /// host's or the server's, -shipcollision; two rule bits, SessionRules.CollisionOf). The old toggle's key
        /// (preciseShipCollision, on = Exact) is read until the mode is set. Android's default is Original (2026-10-22: no
        /// hull meshes cooked or synced, HullCollision).</summary>
        public static int ShipCollision
        {
            get
            {
                if (GameHooks.SessionRule != null && GameHooks.SessionRule(R.PreciseShipCollision, out bool precise)
                    && GameHooks.SessionRule(R.OriginalCollision, out bool original))
                    return original ? R.CollisionOriginal : precise ? R.CollisionExact : R.CollisionMixed;
                return LocalCollision;
            }
            set
            {
                int mode = Mathf.Clamp(value, R.CollisionExact, R.CollisionOriginal);
                Set("shipCollision", mode);
                GameHooks.RuleChangedByHost?.Invoke(R.PreciseShipCollision, mode == R.CollisionExact);
                GameHooks.RuleChangedByHost?.Invoke(R.OriginalCollision, mode == R.CollisionOriginal);
            }
        }

        static int LocalCollision
        {
            get
            {
                float v = Get("shipCollision", -1f);
                if (v < -0.5f) return GetBool("preciseShipCollision", false) ? R.CollisionExact : AndroidDefaults ? R.CollisionOriginal : R.DefaultCollision;
                return Mathf.Clamp(Mathf.RoundToInt(v), R.CollisionExact, R.CollisionOriginal);
            }
        }

        /// <summary>The Gameplay options that change how the game plays are the session's in multiplayer (NetRules), else the
        /// player's own.</summary>
        static bool Rule(int bit, string key, bool fallback) => GameHooks.SessionRule != null && GameHooks.SessionRule(bit, out bool on) ? on : GetBool(key, fallback);
        static void SetRule(int bit, string key, bool value) { SetBool(key, value); GameHooks.RuleChangedByHost?.Invoke(bit, value); }

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
            if (GetBool("empBreaksCloak", true)) m |= R.EmpBreaksCloak;
            if (GetBool("smarterNpcTargeting", false)) m |= R.SmarterNpcTargeting;
            return R.WithCollision(m, LocalCollision);
        }

        /// <summary>DialogueWindow::update: with voice, turn the page once the line has ended.</summary>
        public static bool AutoAdvanceDialogue { get => GetBool("autoAdvanceDialogue", true); set => SetBool("autoAdvanceDialogue", value); }

        /// <summary>Remake: a conversation in flight leaves the game running (Navigation.ConversationRuns: the controls stop,
        /// nothing fires, the story level holds); off = the original's pause (MGame::OnUpdate halts for the dialogue window).</summary>
        public static bool PlayOnInConversations { get => GetBool("playOnInConversations", false); set => SetBool("playOnInConversations", value); }

        /// <summary>Remake: the dialogue and radio text types in with pacing, shouting, actions and tinted names
        /// (TextReveal); off = the original's plain page at once.</summary>
        public static bool AnimatedDialogue { get => GetBool("animatedDialogue", true); set => SetBool("animatedDialogue", value); }

        /// <summary>The keyboard / controller hint rows of the HUDs (the touch controls always show).</summary>
        public static bool InputHints { get => GetBool("inputHints", true); set => SetBool("inputHints", value); }
        /// <summary>Remake (immersion): how much of the flight HUD shows (FlightHud: `.hud-minimal` / `.hud-off`). 0 Full;
        /// 1 Minimal = the crosshair, lock ring, action prompt, missile warning, charge bar and HUD messages; 2 Off = only
        /// the HUD messages. Menus, conversations, the radio, the minigames and the touch controls always show.</summary>
        public const int HudFull = 0, HudMinimal = 1, HudOff = 2;
        public static int HudMode { get => Mathf.Clamp(Mathf.RoundToInt(Get("hudMode", HudFull)), HudFull, HudOff); set => Set("hudMode", Mathf.Clamp(value, HudFull, HudOff)); }
        /// <summary>Remake (docs/UI-DESIGN.md "HUD"): the flight HUD's panels (status bars, readouts, lock plate, messages,
        /// hints, crosshair) at 70..150 % of their size, apart from the menus' Interface size (FlightHud.ApplyHudLook).</summary>
        public static int HudScalePercent { get => Mathf.Clamp(Mathf.RoundToInt(Get("hudScale", 100f)), 70, 150); set => Set("hudScale", Mathf.Clamp(value, 70, 150)); }
        /// <summary>Remake: the flight HUD's markings at 30..100 % opacity (the messages, the radio and the menus stay
        /// opaque; FlightHud.ApplyHudLook).</summary>
        public static int HudOpacityPercent { get => Mathf.Clamp(Mathf.RoundToInt(Get("hudOpacity", 100f)), 30, 100); set => Set("hudOpacity", Mathf.Clamp(value, 30, 100)); }
        /// <summary>Remake (players' request, 2026-10-17: the far ship's dot hid the ship): a far ship's on-screen marker
        /// (CombatView, UI.FarShipMarkers). 0 Original: the filled dot over the ship, the ring when locked; 1 Ring: the lock
        /// ring's arcs without its centre around the ship; 2 Small dot: a small dot just above the ship. Locked, the remake's
        /// styles take the faction bracket around the ship. Per player.</summary>
        public const int FarMarkerOriginal = 0, FarMarkerRing = 1, FarMarkerSmallDot = 2;
        public static int FarShipMarkers { get => Mathf.Clamp(Mathf.RoundToInt(Get("farShipMarkers", FarMarkerOriginal)), FarMarkerOriginal, FarMarkerSmallDot); set => Set("farShipMarkers", Mathf.Clamp(value, FarMarkerOriginal, FarMarkerSmallDot)); }
        /// <summary>Remake: the distance under every far ship's on-screen marker, not only the locked one's (CombatView).</summary>
        public static bool MarkerDistance { get => GetBool("markerDistance", false); set => SetBool("markerDistance", value); }
        /// <summary>Remake: every primary weapon item sounds its own shots (the original: only the first primary gun).</summary>
        public static bool EachWeaponSound { get => GetBool("eachWeaponSound", false); set => SetBool("eachWeaponSound", value); }
        /// <summary>Remake: the touch fire button's double-press autofire latch on the keyboard / mouse / controller fire
        /// binding (the PC version had none).</summary>
        public static bool KeyAutofire { get => GetBool("keyAutofire", false); set => SetBool("keyAutofire", value); }
        /// <summary>Remake (#37): Level out also brings the nose to the horizon (FlightModel.LevelPitch); off (the default since
        /// 2026-10-27, the user's request: it pulled the nose back as soon as pitch was let go) = the original's roll only,
        /// which steering doesn't end.</summary>
        public static bool LevelPitch { get => GetBool("levelPitch", false); set => SetBool("levelPitch", value); }
        /// <summary>Remake (desktop): no sound while the window isn't focused (Bootstrap; the volume, not AudioListener.pause,
        /// which the pause menu uses).</summary>
        public static bool MuteInBackground { get => GetBool("muteInBackground", false); set => SetBool("muteInBackground", value); }
        /// <summary>Remake: the frame rate on screen (FpsCounter, placed by FpsPosition).</summary>
        public static bool ShowFps { get => GetBool("showFps", false); set => SetBool("showFps", value); }
        /// <summary>Modified: the mouse cursor drawn in the interface's style (GameCursor) instead of the system's.</summary>
        public static bool GameCursor { get => GetBool("gameCursor", true); set => SetBool("gameCursor", value); }

        /// <summary>Remake: where the FPS counter sits (Options > Graphics "FPS counter position", FpsCounter): the four corners
        /// and the middles of the four sides, top centre by default.</summary>
        public const int FpsTopLeft = 0, FpsTop = 1, FpsTopRight = 2, FpsLeft = 3, FpsRight = 4, FpsBottomLeft = 5, FpsBottom = 6, FpsBottomRight = 7;
        public static int FpsPosition { get => Mathf.Clamp(Mathf.RoundToInt(Get("fpsPosition", FpsTop)), FpsTopLeft, FpsBottomRight); set => Set("fpsPosition", Mathf.Clamp(value, FpsTopLeft, FpsBottomRight)); }
        /// <summary>Remake: Options > Accessibility "Interface size", a percentage of the automatic UI scale (UiLayout: the device
        /// class and the pixel floor), 80..150, 100 by default.</summary>
        public static int UiScalePercent { get => Mathf.Clamp(Mathf.RoundToInt(Get("uiScale", 100f)), 80, 150); set => Set("uiScale", Mathf.Clamp(value, 80, 150)); }

        /// <summary>Remake: Options > Accessibility "Text size", a percentage of the type scale (UiLayout: the root's
        /// ui-text-N class redefines the --text-* sizes, code takes UiLayout.Type), 100..200 (XAG 101: text to 200 %).</summary>
        public static int UiTextScalePercent { get => Mathf.Clamp(Mathf.RoundToInt(Get("uiTextScale", 100f)), 100, 200); set => Set("uiTextScale", Mathf.Clamp(value, 100, 200)); }

        /// <summary>Remake: Options > Accessibility "High contrast": opaque panels, brighter secondary text and lines
        /// (GoF2Common.uss .ui-high-contrast).</summary>
        public static bool HighContrast { get => GetBool("highContrast", false); set => SetBool("highContrast", value); }

        /// <summary>Remake: Options > Accessibility "Reduce motion": no sliding / fading UI transitions (.ui-reduce-motion,
        /// the station menu's fly-out).</summary>
        public static bool ReduceMotion { get => GetBool("reduceMotion", false); set => SetBool("reduceMotion", value); }

        /// <summary>Remake: Options > Accessibility "Colour-blind markers": friendly markers blue instead of green, so they
        /// stand apart from the red hostile ones for red-green colour blindness (CombatView).</summary>
        public static bool ColorblindMarkers { get => GetBool("colorblindMarkers", false); set => SetBool("colorblindMarkers", value); }

        /// <summary>Remake: Options > Accessibility "Interface font" (UiFont): "" the theme's Klavika, "file:<name>" a font file
        /// imported into the Fonts folder, "system:<family> - <style>" an installed one.</summary>
        public static string UiFont
        {
            get => uiFont ??= PlayerPrefs.GetString(Prefix + "uiFont", "");
            set
            {
                value ??= "";
                if (value == UiFont) return;
                uiFont = value;
                PlayerPrefs.SetString(Prefix + "uiFont", value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }
        static string uiFont;

        /// <summary>Remake: Options > Graphics "Interface width": the HUD and menus in a centred 16:9 or 21:9 band on a wider
        /// screen, or the full width (UiLayout.OptionInsets).</summary>
        public const int UiWidth16x9 = 0, UiWidth21x9 = 1, UiWidthFull = 2;
        public static int UiWidth { get => Mathf.Clamp(Mathf.RoundToInt(Get("uiWidth", UiWidth21x9)), UiWidth16x9, UiWidthFull); set => Set("uiWidth", Mathf.Clamp(value, UiWidth16x9, UiWidthFull)); }

        /// <summary>Remake: Options > Graphics "Screen edge margin", a percentage of the screen kept clear on every side (a TV
        /// that cuts its edges off); -1 = automatic, 5 % on a TV and none elsewhere (UiLayout.OptionInsets).</summary>
        public static float UiMarginPercent { get => Mathf.Clamp(Get("uiMargin", -1f), -1f, 10f); set => Set("uiMargin", Mathf.Clamp(value, -1f, 10f)); }

        /// <summary>Remake (#40): the story step at the bottom right (StoryStepLabel), for bug reports; off by default.</summary>
        public static bool ShowStoryStep { get => GetBool("showStoryStep", true); set => SetBool("showStoryStep", value); }

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
            "displayMode", "resolutionWidth", "resolutionHeight", "frameRate", "quality", "fireSmoke", "fog", "detailDistance", "textureFiltering", "renderScale", "upscaler", "upscalerQuality",
            "msaa", "textureQuality", "brightness", "bloom", "bloomStyle", "hangarShadows", "lods", "hangarDof", "lensFlare", "npcPlayerEngines", "fov",
            "cameraShake", "originalChaseCamera", "uiWidth", "uiMargin", "showFps", "fpsPosition", "gameCursor",
        };
        public static readonly string[] ControlsKeys =
        {
            "tiltSteering", "tiltSensitivity", "sensitivity", "keyAutofire", "levelPitch", "invertPitch", "invertYaw", "invertDrillY",
            "invertDrillX", "invertOrbitY", "invertOrbitX", "gyroSteering", "gyroSensitivity", "mouseSteering", "mouseDeadzone", "mouseSensitivity", "vrGrabControls", "haptics", "stickDeadzone",
            "touchLeftHeight", "touchRightHeight", "floatingTouchStick", "flightStyle", "padGlyphs",
        };
        public static readonly string[] GameplayKeys =
        {
            "launchCamera", "hangarFlights", "tutorialHints", "showStoryStep", "pirateEvents", "capitalShips", "kaamoStacking", "kaamoMaxLevel", "kaamoKeepsEquipment", "cloakBay", "informerOriginalRule",
            "originalTargetLock", "keepEnemyLock", "modShipTraffic", "dyingCargoMarkers", "cloakLosesPursuers", "empBreaksCloak", "smarterNpcTargeting", "shipCollision", "preciseShipCollision", "autoAdvanceDialogue", "animatedDialogue", "playOnInConversations", "inputHints", "hudMode", "hudScale", "hudOpacity", "discordPresence",
            "farShipMarkers", "markerDistance",
        };
        public static readonly string[] LanguageKeys = { "voiceLanguage" };
        public static readonly string[] AccessibilityKeys = { "uiScale", "uiTextScale", "uiFont", "highContrast", "reduceMotion", "colorblindMarkers" };

        /// <summary>These options back to their defaults.</summary>
        public static void Reset(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                PlayerPrefs.DeleteKey(Prefix + key);
                cache.Remove(key);
                if (key == "uiFont") uiFont = null;
            }
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>"Default settings" (497), every tab: every option back to its default and the key bindings, the language
        /// kept. (Before, a list from the early options: 19 later ones, animated dialogue, hangar flights and shadows, mouse
        /// steering, Show FPS, the target lock... were never reset.)</summary>
        public static void ResetToDefaults()
        {
            GameHooks.ResetControls?.Invoke();
            var all = new List<string>();
            foreach (var group in new[] { SoundKeys, GraphicsKeys, ControlsKeys, GameplayKeys, AccessibilityKeys, LanguageKeys }) all.AddRange(group);
            Reset(all);
            Modding.ModGameOptions.ResetSettings();   // the mods' gameplay settings (gameoptions.json "setting")
        }

        // PlayerPrefs reads go through a cache: game code reads some options every frame.
        static readonly Dictionary<string, float> cache = new Dictionary<string, float>();

        /// <summary>Remake (the options' per-row reset, OptionControl): what 'read' (an option's getter) gives with every
        /// option at its default, each Get answering its fallback. False when 'read' reads no option at all (a row not
        /// kept in Settings, e.g. the photo panel's).</summary>
        public static bool TryDefault<T>(Func<T> read, out T value)
        {
            value = default;
            if (read == null) return false;
            defaultReads = 0;
            try { value = read(); return defaultReads > 0; }
            catch (Exception) { return false; }
            finally { defaultReads = -1; }
        }
        static int defaultReads = -1;   // >= 0 while TryDefault reads: the options read meanwhile

        static float Get(string key, float fallback)
        {
            if (defaultReads >= 0) { defaultReads++; return fallback; }
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
