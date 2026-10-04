// OptionsCatalog.cs
// Every player option as data (label, kind, range, get / set on Settings), in the order the menus show them. The main
// menu's Options panel (one tab per page) and the in-flight pause menu build their rows from it (OptionControl).
// Original options (Reference/research/mainmenu_notes.md 2.3 / 2.6): Music 34, FX 35, Voice 36, Brightness 503
// (513-515), Quality 504 (507-509, descriptions 510-512), Sensitivity 499, Invert controls 500, Default settings 497.
// The rest are remake options (Localization.Extra texts). Not here: touch / accelerometer steering and its
// calibration (490-494; tilt isn't built), the text language (the Language tab's buttons, with the voice language row under them).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.UI
{
    public enum OptionPage { Sound, Graphics, Controls, Gameplay, Language, Bindings }
    public enum OptionKind { Slider, Toggle, Choice, Button, Binding }

    public sealed class OptionDef
    {
        public string id;
        public OptionPage page;
        public OptionKind kind;
        public Func<string> label;
        public Func<string> description;   // optional line under the row
        public Func<UnityEngine.UIElements.VisualElement> extra;   // optional element under the row (not focusable)
        public bool inGameOnly;            // only in a running game's menus (pause / station), not the main menu
        public Func<bool> visible;         // optional: the row shows only while true (OptionControl.Refresh)

        // Slider
        public float min, max;
        public Func<float> get;
        public Action<float> set;
        public Func<float, string> format;

        // Toggle
        public Func<bool> getBool;
        public Action<bool> setBool;

        // Choice
        public Func<string[]> choices;
        public Func<int> getIndex;
        public Action<int> setIndex;
        public bool segmented;

        // Button
        public Action action;

        // Binding (a key bindings row, BindingRow)
        public Flight.ControlRow control;
    }

    public static class OptionsCatalog
    {
        static string X(string key, string english) => Localization.Extra(key, english);
        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)} %";

        public static string PageTitle(OptionPage page) => page switch
        {
            OptionPage.Sound => X("tabSound", "Sound"),
            OptionPage.Graphics => Localization.Get(502),
            OptionPage.Controls => Localization.Get(498),
            OptionPage.Bindings => X("tabBindings", "Key bindings"),
            OptionPage.Language => Localization.Get(0),
            _ => X("tabGameplay", "Gameplay"),
        };

        static readonly string[] VoiceCodes = { "auto", "en", "de" };
        static readonly float[] RenderScales = { 0.5f, 0.67f, 0.75f, 0.8f, 0.9f, 1f, 1.25f, 1.5f, 2f };
        static readonly int[] MsaaSamples = { 1, 2, 4, 8 };

        /// <summary>The options of this platform, page by page.</summary>
        public static List<OptionDef> All()
        {
            var list = new List<OptionDef>
            {
                // ---- sound
                Slider("master", OptionPage.Sound, () => X("masterVolume", "Master"), 0f, 1f, () => Settings.MasterVolume, v => Settings.MasterVolume = v, Percent),
                Slider("music", OptionPage.Sound, () => Localization.Get(34), 0f, 1f, () => Settings.MusicVolume, v => Settings.MusicVolume = v, Percent),
                Slider("sfx", OptionPage.Sound, () => Localization.Get(35), 0f, 1f, () => Settings.SfxVolume, v => Settings.SfxVolume = v, Percent),
                Slider("voice", OptionPage.Sound, () => Localization.Get(36), 0f, 1f, () => Settings.VoiceVolume, v => Settings.VoiceVolume = v, Percent),
                // Remake: the original sounds only the first primary gun (Player::calcWeaponSounds).
                Toggle("eachWeaponSound", OptionPage.Sound, () => X("eachWeaponSound", "Every weapon's own shot sound"),
                    () => Settings.EachWeaponSound, v => Settings.EachWeaponSound = v),
                // Remake: the voices apart from the text language (only English and German were recorded); on the Language tab.
                Choice("voiceLanguage", OptionPage.Language, () => X("voiceLanguage", "Voice language"), true,
                    () => new[] { X("voiceAuto", "As text"), "English", "Deutsch" },
                    () => Array.IndexOf(VoiceCodes, Settings.VoiceLanguage) is var i && i >= 0 ? i : 0,
                    i => Settings.VoiceLanguage = VoiceCodes[i]),
            };

            // ---- graphics
            if (Bootstrap.HasDisplayOptions)
            {
                list.Add(Choice("displayMode", OptionPage.Graphics, () => X("displayMode", "Display mode"), true,
                    () => new[] { X("borderless", "Borderless"), X("fullscreen", "Fullscreen"), X("windowed", "Windowed") },
                    () => (int)Settings.DisplayMode, i => Settings.DisplayMode = (DisplayMode)i));
                var sizes = Bootstrap.Resolutions();
                list.Add(Choice("resolution", OptionPage.Graphics, () => X("resolution", "Resolution"), false,
                    () => sizes.Select(r => $"{r.x} × {r.y}").Prepend(X("resolutionNative", "Display's own")).ToArray(),
                    () => { int i = sizes.IndexOf(Settings.Resolution); return i < 0 ? 0 : i + 1; },
                    i => Settings.Resolution = i <= 0 ? Vector2Int.zero : sizes[i - 1]));
            }
            list.Add(Choice("frameRate", OptionPage.Graphics, () => X("frameRate", "Frame rate"), true,
                () => new[] { "30", "60", "120", X("fpsUncapped", "UNCAPPED"), X("fpsVSync", "V-SYNC") },
                () => (int)Settings.FrameRate, i => Settings.FrameRate = (FrameRate)i));
            list.Add(Choice("quality", OptionPage.Graphics, () => Localization.Get(504), true,
                () => new[] { Localization.Get(507), Localization.Get(508), Localization.Get(509) },
                () => Settings.Quality, i => Settings.Quality = i,
                () => QualityDescription(Settings.Quality)));
            // DLSS / FSR 2+ pick their own render resolution (the Upscaler quality below): no render scale while one is on.
            // MetalFX keeps the render scale (its input resolution; URP clamps it into MetalFX Temporal's supported range).
            bool DlssOrFsrOn() => Bootstrap.ActiveUpscaler == Settings.UpscalerDlss || Bootstrap.ActiveUpscaler == Settings.UpscalerFsrTemporal;
            var renderScale = Choice("renderScale", OptionPage.Graphics, () => X("renderScale", "Render scale"), false,
                () => RenderScales.Select(Percent).ToArray(),
                () => Nearest(RenderScales, Settings.RenderScale > 0f ? Settings.RenderScale : Bootstrap.DefaultRenderScale),
                i => Settings.RenderScale = RenderScales[i]);
            renderScale.visible = () => !DlssOrFsrOn();
            list.Add(renderScale);
            // Only the upscalers this device runs (Android: FSR 1 needs GLES 3.1 / Vulkan, STP Vulkan; DLSS / FSR 2+: Windows
            // builds with the upscaler framework, the GPU and graphics API they need; MetalFX: macOS / iOS on Metal, UpscalerFramework). Asked again on every
            // refresh: DLSS / FSR are only known once URP has made its pipeline (the first frame), and the main menu builds
            // its rows before that.
            List<int> Upscalers()
            {
                var l = new List<int> { Settings.UpscalerOff };
                if (Bootstrap.FsrSupported) l.Add(Settings.UpscalerFsr);
                if (Bootstrap.StpSupported) l.Add(Settings.UpscalerStp);
                if (Bootstrap.DlssSupported) l.Add(Settings.UpscalerDlss);
                if (Bootstrap.FsrTemporalSupported) l.Add(Settings.UpscalerFsrTemporal);
                if (Bootstrap.MetalFxSpatialSupported) l.Add(Settings.UpscalerMetalFxSpatial);
                if (Bootstrap.MetalFxTemporalSupported) l.Add(Settings.UpscalerMetalFxTemporal);
                return l;
            }
            var upscalers = Upscalers();
            string UpscalerName(int u) => u switch
            {
                Settings.UpscalerFsr => "FSR 1",
                Settings.UpscalerStp => "STP",
                Settings.UpscalerDlss => "DLSS",
                Settings.UpscalerFsrTemporal => UpscalerFramework.BestFsrLabel ?? "FSR",
                Settings.UpscalerMetalFxSpatial => "MetalFX",
                Settings.UpscalerMetalFxTemporal => "MetalFX Temporal",
                _ => X("off", "Off"),
            };
            if (upscalers.Count > 1 || UpscalerFramework.Compiled)
                list.Add(Choice("upscaler", OptionPage.Graphics, () => X("upscaler", "Upscaler"), true,
                    () => Upscalers().Select(UpscalerName).ToArray(),
                    () => Math.Max(0, Upscalers().IndexOf(Bootstrap.ActiveUpscaler)),
                    i => { var l = Upscalers(); Settings.Upscaler = l[Math.Clamp(i, 0, l.Count - 1)]; },
                    () => Bootstrap.ActiveUpscaler switch
                    {
                        Settings.UpscalerFsr => X("upscalerFsr", "AMD FidelityFX Super Resolution 1: sharp upscaling from the render scale"),
                        Settings.UpscalerStp => X("upscalerStp", "Unity Spatial-Temporal Post-processing: temporal anti-aliasing and upscaling, replaces MSAA"),
                        Settings.UpscalerDlss => X("upscalerDlss", "NVIDIA DLSS: AI upscaling and anti-aliasing (DLAA at Native), replaces MSAA; the quality sets its resolution"),
                        Settings.UpscalerFsrTemporal => string.Format(X("upscalerFsrTemporal", "AMD {0}: temporal upscaling and anti-aliasing, replaces MSAA; the quality sets its resolution"),
                            UpscalerFramework.BestFsrLabel ?? "FSR"),
                        Settings.UpscalerMetalFxSpatial => X("upscalerMetalFxSpatial", "Apple MetalFX Spatial: sharp upscaling from the render scale"),
                        Settings.UpscalerMetalFxTemporal => X("upscalerMetalFxTemporal", "Apple MetalFX Temporal: temporal anti-aliasing and upscaling from the render scale, replaces MSAA"),
                        _ => X("upscalerOff", "Plain scaling from the render scale"),
                    }));
            // DLSS / FSR 2+: the render resolution by quality mode, in place of the render scale; only while one of them is on (in
            // builds with the framework).
            if (UpscalerFramework.Compiled)
            {
                var quality = Choice("upscalerQuality", OptionPage.Graphics, () => X("upscalerQuality", "Upscaler quality"), false,
                    () => new[] { X("upscalerNative", "Native (DLAA / native AA)"), X("upscalerQ", "Quality"), X("upscalerB", "Balanced"),
                                  X("upscalerP", "Performance"), X("upscalerUP", "Ultra performance") },
                    () => Settings.UpscalerQuality, i => Settings.UpscalerQuality = i,
                    () => X("upscalerQualityHelp", "The resolution DLSS / FSR render at, from native down to a third (replaces the render scale)"));
                quality.visible = DlssOrFsrOn;
                list.Add(quality);
            }
            // MSAA with a temporal upscaler on (STP, DLSS, FSR 2+, MetalFX Temporal): its anti-aliasing takes the place (shown as off); picking MSAA
            // turns the upscaler off.
            list.Add(Choice("msaa", OptionPage.Graphics, () => X("antiAliasing", "Anti-aliasing"), true,
                () => new[] { X("off", "Off"), "MSAA 2×", "MSAA 4×", "MSAA 8×" },
                () => Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler) ? 0
                    : Math.Max(0, Array.IndexOf(MsaaSamples, Settings.Msaa > 0 ? Settings.Msaa : Bootstrap.DefaultMsaa)),
                i =>
                {
                    if (i > 0 && Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler)) Settings.Upscaler = Settings.UpscalerOff;
                    Settings.Msaa = MsaaSamples[i];
                }));
            list.Add(Choice("brightness", OptionPage.Graphics, () => Localization.Get(503), true,
                () => new[] { Localization.Get(513), Localization.Get(514), Localization.Get(515) },
                () => Settings.Brightness, i => Settings.Brightness = i));
            // Remake: the remake's bloom (the HDR glow of lights and effects) or the original's (every bright pixel, ClassicBloomPass).
            list.Add(Choice("bloom", OptionPage.Graphics, () => X("bloom", "Bloom"), true,
                () => new[] { X("off", "Off"), X("bloomRemake", "Remake"), X("bloomOriginal", "Original") },
                () => Mathf.Clamp(Settings.BloomStyle, 0, 2), i => Settings.BloomStyle = i));
            list.Add(Toggle("lensFlare", OptionPage.Graphics, () => X("lensFlare", "Lens flare"), () => Settings.LensFlare, v => Settings.LensFlare = v));
            list.Add(Toggle("npcPlayerEngines", OptionPage.Graphics, () => X("npcPlayerEngines", "Other ships' engines like yours"),
                () => Settings.NpcPlayerEngines, v => Settings.NpcPlayerEngines = v));
            list.Add(Slider("fov", OptionPage.Graphics, () => X("fov", "Field of view"), 55f, 95f,
                () => Settings.FieldOfView, v => Settings.FieldOfView = Mathf.Round(v), v => $"{Mathf.RoundToInt(v)}°"));
            list.Add(Slider("cameraShake", OptionPage.Graphics, () => X("cameraShake", "Camera shake"), 0f, 1f,
                () => Settings.CameraShake, v => Settings.CameraShake = v, Percent));
            list.Add(Toggle("showFps", OptionPage.Graphics, () => X("showFps", "Show FPS"), () => Settings.ShowFps, v => Settings.ShowFps = v));

            // ---- controls
            // MenuTouchWindow state 8: 490 Touch / 491 Accelerometer pictures (options[0x11]), 492 Steering Calibration
            // (493, then OK stores the device's position), the sensitivity slider per mode (+0x14 / +0x18).
            if (Flight.TiltSteering.Available)
            {
                list.Add(Choice("steering", OptionPage.Controls, () => X("steering", "Steering"), true,
                    () => new[] { Localization.Get(490), Localization.Get(491) },
                    () => Settings.TiltSteering ? 1 : 0, i =>
                    {
                        bool tilt = i == 1;
                        if (tilt && !Settings.TiltCalibrated) Flight.TiltSteering.Calibrate();   // remake: calibrate on first use
                        Settings.TiltSteering = tilt;
                    }));
                list.Add(new OptionDef
                {
                    id = "calibrate", page = OptionPage.Controls, kind = OptionKind.Button, label = () => Localization.Get(492),
                    description = () => Localization.Get(493), action = Flight.TiltSteering.Calibrate,
                });
                list.Add(Slider("tiltSensitivity", OptionPage.Controls, () => Localization.Get(499) + " (" + Localization.Get(491) + ")", 0f, 1f,
                    () => Settings.TiltSensitivity, v => Settings.TiltSensitivity = v, v => Mathf.RoundToInt(v * 100f).ToString()));
            }
            list.Add(Slider("sensitivity", OptionPage.Controls, () => Localization.Get(499), 0.2f, 2.2f,
                () => Settings.Sensitivity, v => Settings.Sensitivity = v, v => v.ToString("0.0")));
            // options[0x10] "Invert controls" (500), split per axis; the mining drill has its own pair.
            list.Add(Toggle("invert", OptionPage.Controls, () => X("invertY", "Invert up / down"), () => Settings.InvertPitch, v => Settings.InvertPitch = v));
            list.Add(Toggle("invertYaw", OptionPage.Controls, () => X("invertX", "Invert left / right"), () => Settings.InvertYaw, v => Settings.InvertYaw = v));
            list.Add(Toggle("invertDrillY", OptionPage.Controls, () => X("invertDrillY", "Mining drill: invert up / down"), () => Settings.InvertDrillY, v => Settings.InvertDrillY = v));
            list.Add(Toggle("invertDrillX", OptionPage.Controls, () => X("invertDrillX", "Mining drill: invert left / right"), () => Settings.InvertDrillX, v => Settings.InvertDrillX = v));
            if (Flight.ControllerGyro.Supported)
            {
                list.Add(Toggle("gyroSteering", OptionPage.Controls, () => X("gyroSteering", "Controller gyro (DualSense, DualShock 4, Switch Pro)"),
                    () => Settings.GyroSteering, v => { Settings.GyroSteering = v; Flight.ControllerGyro.Recenter(); }));
                list.Add(Slider("gyroSensitivity", OptionPage.Controls, () => X("gyroSensitivity", "Gyro sensitivity"), 0.25f, 3f,
                    () => Settings.GyroSensitivity, v => Settings.GyroSensitivity = v, v => v.ToString("0.00")));
            }
            if (!Application.isMobilePlatform)
            {
                list.Add(Toggle("mouseSteering", OptionPage.Controls, () => X("mouseSteering", "Mouse steering"), () => Settings.MouseSteering, v => Settings.MouseSteering = v));
                // Remake: a dead zone around the centre, so a mouse near the middle leaves the ship flying straight.
                var mouseDeadzone = Slider("mouseDeadzone", OptionPage.Controls, () => X("mouseDeadzone", "Mouse steering dead zone"), 0f, 0.3f,
                    () => Settings.MouseDeadzone, v => Settings.MouseDeadzone = v, Percent);
                mouseDeadzone.description = () => X("mouseDeadzoneHelp", "How far the mouse can move from the centre before the ship turns.");
                list.Add(mouseDeadzone);
            }
            // Remake VR: the cockpit's grabbable stick (right grip) and throttle lever (left grip), else the controllers as a gamepad.
            var vrGrab = Toggle("vrGrabControls", OptionPage.Controls, () => X("vrGrabControls", "VR flight: grab the stick and throttle"),
                () => Settings.VrGrabControls, v => Settings.VrGrabControls = v);
            vrGrab.visible = () => Vr.VrMode.Enabled;
            list.Add(vrGrab);
            // Remake: haptic feedback (Haptics), the controller's rumble and the phone's vibration; moving it plays a sample.
            var haptics = Slider("haptics", OptionPage.Controls, () => X("haptics", "Vibration"), 0f, 1f,
                () => Settings.HapticsIntensity, v => { Settings.HapticsIntensity = v; Flight.Haptics.Preview(); },
                v => v <= 0f ? X("off", "Off") : Percent(v));
            haptics.description = () => Application.isMobilePlatform
                ? X("hapticsHelpMobile", "Phone vibration (touch controls) and controller rumble: hits, collisions, explosions, missiles, boost, jumps and mining.")
                : X("hapticsHelp", "Controller rumble: hits, collisions, explosions, shots, boost, jumps and mining.");
            list.Add(haptics);
            list.Add(Slider("deadzone", OptionPage.Controls, () => X("deadzone", "Stick dead zone"), 0.05f, 0.4f,
                () => Settings.StickDeadzone, v => Settings.StickDeadzone = v, Percent));
            // Remake: every flight control rebindable (GameControls): two keyboard / mouse keys and a controller button each.
            list.Add(new OptionDef
            {
                id = "resetBindings", page = OptionPage.Bindings, kind = OptionKind.Button,
                label = () => X("resetBindings", "Reset key bindings"),
                description = () => X("bindingsHelp", "Keys, second keys and controller buttons: pick one to change it. Esc (or the controller's B, or a tap) cancels, Backspace clears; nothing pressed for 10 seconds cancels too. The menu keys stay fixed."),
                action = Flight.GameControls.ResetToDefaults,
                extra = BindingRow.Header,
            });
            foreach (var row in Flight.GameControls.Rows)
                list.Add(new OptionDef { id = "bind_" + row.id, page = OptionPage.Bindings, kind = OptionKind.Binding, label = row.label, control = row });

            // ---- gameplay
            list.Add(Toggle("launchCamera", OptionPage.Gameplay, () => X("launchCamera", "Launch and arrival camera"),
                () => Settings.LaunchCamera, v => Settings.LaunchCamera = v));
            list.Add(Toggle("hangarFlights", OptionPage.Gameplay, () => X("hangarFlights", "Hangar arrival and take-off"),
                () => Settings.HangarFlights, v => Settings.HangarFlights = v));
            list.Add(Toggle("tutorialHints", OptionPage.Gameplay, () => X("tutorialHints", "Tutorial popups"),
                () => Settings.TutorialHints, v => Settings.TutorialHints = v));
            var pirateEvents = Toggle("pirateEvents", OptionPage.Gameplay, () => X("pirateEvents", "Pirate outposts and bosses"),
                () => Settings.PirateEvents, v => Settings.PirateEvents = v);
            pirateEvents.description = () => X("pirateEventsHelp", "Now and then an orbit holds a pirate outpost with its guards or a pirate boss with escorts; destroying them pays a bounty. Not in the original.");
            list.Add(pirateEvents);
            // Remake: the remake's own ships (custom_ships.json), single player only.
            var customShips = Toggle("customShips", OptionPage.Gameplay, () => X("customShips", "Custom ships"),
                () => Settings.CustomShipsEnabled, v => Settings.CustomShipsEnabled = v);
            customShips.description = () => X("customShipsHelp", "Ships that aren't in the original game (such as the USS Enterprise and the Jedi Starfighter), sold by visitors in station bars. Off: nobody offers them; a custom ship you already own stays yours. Single player only.");
            customShips.visible = () => !Multiplayer.NetGame.Active;
            list.Add(customShips);
            list.Add(Toggle("autoAdvance", OptionPage.Gameplay, () => X("autoAdvance", "Turn voiced dialogue pages automatically"),
                () => Settings.AutoAdvanceDialogue, v => Settings.AutoAdvanceDialogue = v));
            list.Add(Toggle("animatedDialogue", OptionPage.Gameplay, () => X("animatedDialogue", "Animated dialogue"),
                () => Settings.AnimatedDialogue, v => Settings.AnimatedDialogue = v));
            list.Add(Toggle("inputHints", OptionPage.Gameplay, () => X("inputHintsFlight", "Button hints in flight"),
                () => Settings.InputHints, v => Settings.InputHints = v));
            // Remake: Discord Rich Presence (DiscordPresence), desktop only.
            if (!Application.isMobilePlatform)
                list.Add(Toggle("discordPresence", OptionPage.Gameplay, () => X("discordPresence", "Show what I'm doing in Discord"),
                    () => Settings.DiscordPresence, v => Settings.DiscordPresence = v));
            // Remake: the difficulty of the game in progress (the new game's panel sets it first; saved with the game). NPC
            // hulls and guns, raiders and the Extreme rules follow from the next orbit or docking. Not in multiplayer: every
            // session plays on Normal.
            if (!Multiplayer.NetGame.Active)
            {
                float[] levels = { Session.DifficultyEasy, Session.DifficultyNormal, Session.DifficultyHard, Session.DifficultyExtreme };
                var difficulty = Choice("difficulty", OptionPage.Gameplay, () => X("difficulty", "Difficulty"), true,
                    () => levels.Select(Session.DifficultyName).ToArray(),
                    () => Nearest(levels, Session.Difficulty), i => Session.Difficulty = levels[Mathf.Clamp(i, 0, levels.Length - 1)],
                    () => X("difficultyHelp", "Changes the game in progress; enemies and the Extreme rules follow from the next orbit or docking."));
                difficulty.inGameOnly = true;
                list.Add(difficulty);
            }
            // Remake: the testing tools (Cheats.Unlocked), also opened by F10, LB + RB or three fingers on the main menu.
            var debug = Toggle("debugTools", OptionPage.Gameplay, () => X("debugTools", "Debug tools"), () => Cheats.Unlocked, v => Cheats.Unlocked = v);
            debug.description = () => X("debugToolsHelp", "A Debug button in the main menu (the mission select) and a Debug page in the pause and station menus (cheats, items, spawns).");
            debug.visible = () => Cheats.Allowed;   // not in a multiplayer session that doesn't allow the Debug menu
            list.Add(debug);
            return list;
        }

        /// <summary>510-512 "Low quality:\n- Smoke off\n- Fog off\n- Detail low" as one line: "Smoke off · Fog off · Detail low".</summary>
        static string QualityDescription(int quality)
        {
            var lines = Localization.Get(510 + quality).Replace("\r", "").Split('\n').Skip(1)
                .Select(l => l.TrimStart('-', ' ').Trim()).Where(l => l.Length > 0);
            return string.Join("  ·  ", lines);
        }

        static int Nearest(float[] values, float v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i;
            return best;
        }

        static OptionDef Slider(string id, OptionPage page, Func<string> label, float min, float max, Func<float> get, Action<float> set,
                                Func<float, string> format) =>
            new OptionDef { id = id, page = page, kind = OptionKind.Slider, label = label, min = min, max = max, get = get, set = set, format = format };

        static OptionDef Toggle(string id, OptionPage page, Func<string> label, Func<bool> get, Action<bool> set) =>
            new OptionDef { id = id, page = page, kind = OptionKind.Toggle, label = label, getBool = get, setBool = set };

        static OptionDef Choice(string id, OptionPage page, Func<string> label, bool segmented, Func<string[]> choices, Func<int> get,
                                Action<int> set, Func<string> description = null) =>
            new OptionDef
            {
                id = id, page = page, kind = OptionKind.Choice, label = label, segmented = segmented, choices = choices, getIndex = get,
                setIndex = set, description = description,
            };
    }
}
