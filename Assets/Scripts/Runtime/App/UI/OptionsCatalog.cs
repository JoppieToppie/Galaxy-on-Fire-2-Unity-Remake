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
    public enum OptionPage { Sound, Graphics, Controls, Gameplay, Language, Bindings, Accessibility }
    public enum OptionKind { Slider, Toggle, Choice, Button, Binding }

    public sealed class OptionDef
    {
        public string id;
        public OptionPage page;
        public OptionKind kind;
        /// <summary>A volume slider: a speaker before its value mutes it. Its own flag, not the page: a row built by hand
        /// (the photo panel, the Debug page) leaves the page at its default, Sound, and every slider there had the
        /// speaker.</summary>
        public bool volume;
        public Func<string> label;
        public Func<string> description;   // optional line under the row
        public Func<UnityEngine.UIElements.VisualElement> extra;   // optional element under the row (not focusable)
        public bool inGameOnly;            // only in a running game's menus (pause / station), not the main menu
        public bool menuOnly;              // only in the main menu (no game loaded), not the pause / station menus
        public Func<bool> visible;         // optional: the row shows only while true (OptionControl.Refresh)
        public Func<bool> enabled;         // optional: the row is greyed out and skipped by the keys / controller while false

        // Slider
        public float min, max;
        public Func<float> get;
        public Action<float> set;
        public Func<float, string> format;
        /// <summary>Optional: a number the player typed into the value box (as the box shows it: degrees, kelvin, metres,
        /// percent...) to the slider's value. Without it the box's numbers map in a straight line onto the range, from what
        /// 'format' shows at its two ends (OptionControl.FromShown).</summary>
        public Func<float, float> fromShown;
        /// <summary>Optional: the row's default (a slider's value, a choice's index, a toggle as 1 / 0) for its own reset.
        /// Without it a row kept in Settings takes Settings' (Settings.TryDefault); any other has none.</summary>
        public Func<float> defaultValue;

        // Toggle
        public Func<bool> getBool;
        public Action<bool> setBool;

        // Choice
        public Func<string[]> choices;
        public Func<int> getIndex;
        public Action<int> setIndex;
        public bool segmented;
        /// <summary>A change asks "Keep these settings?" with a countdown that puts the old value back (SettingsConfirm):
        /// the display mode, the resolution and the interface / text size, which can leave a screen unreadable.</summary>
        public bool confirm;

        // Button
        public Action action;

        // Binding (a key bindings row, BindingRow)
        public Flight.ControlRow control;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class OptionsCatalog
    {
        static string X(string key, string english) => Localization.Extra(key, english);

        /// <summary>The save folder row's press: the main menu asks and runs the folder picker (MainMenu.ChooseSaveFolder).</summary>
        public static Action SaveFolderPressed;
        static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)} %";

        /// <summary>Remake (Modding.ModFlight): who sets the flight style now instead of the Free flight toggle.</summary>
        static string FlightStyleForcedNote()
        {
            string who = Modding.ModFlight.ForcedBy switch
            {
                "event" => X("flightForcedEvent", "an event"),
                "session" => X("flightForcedSession", "the multiplayer session"),
                "ship" => X("flightForcedShip", "the ship you fly"),
                "campaign" => X("flightForcedCampaign", "this mod campaign"),
                _ => X("flightForcedOption", "a game option of a mod"),
            };
            string style = FlightStyles.Current == FlightStyles.Free ? X("flightStyleFree", "Free flight (EVERSPACE 2 style)") : X("flightStyleOriginal", "Original");
            return string.Format(X("flightForcedNote", "Now set by {0}: {1}."), who, style);
        }

        public static string PageTitle(OptionPage page) => page switch
        {
            OptionPage.Sound => X("tabSound", "Sound"),
            OptionPage.Graphics => Localization.Get(502),
            OptionPage.Controls => Localization.Get(498),
            OptionPage.Bindings => X("tabBindings", "Key bindings"),
            OptionPage.Language => Localization.Get(0),
            OptionPage.Accessibility => X("tabAccessibility", "Accessibility"),
            _ => X("tabGameplay", "Gameplay"),
        };

        /// <summary>Remake (players' request): "Default settings" for one tab only, the choice the menus offer next to every
        /// tab (Settings' key groups; the Key bindings tab resets the bindings).</summary>
        public static void ResetPage(OptionPage page)
        {
            switch (page)
            {
                case OptionPage.Sound: Settings.Reset(Settings.SoundKeys); break;
                case OptionPage.Graphics: Settings.Reset(Settings.GraphicsKeys); break;
                case OptionPage.Controls: Settings.Reset(Settings.ControlsKeys); break;
                case OptionPage.Gameplay: Settings.Reset(Settings.GameplayKeys); Modding.ModGameOptions.ResetSettings(); break;
                case OptionPage.Language: Settings.Reset(Settings.LanguageKeys); break;
                case OptionPage.Accessibility: Settings.Reset(Settings.AccessibilityKeys); break;
                case OptionPage.Bindings: Flight.GameControls.ResetToDefaults(); break;
            }
        }

        /// <summary>The defaults prompt's question and its two reset buttons' texts.</summary>
        public static string ResetQuestion => X("resetQuestion", "Reset to the default settings:");
        public static string ResetTabLabel(OptionPage page) => string.Format(X("resetTab", "Only {0}"), PageTitle(page));
        public static string ResetAllLabel => X("resetAll", "All tabs");

        static readonly string[] VoiceCodes = { "auto", "en", "de" };
        static readonly float[] RenderScales = { 0.5f, 0.67f, 0.75f, 0.8f, 0.9f, 1f, 1.25f, 1.5f, 2f };
        static readonly int[] MsaaSamples = { 1, 2, 4, 8 };
        /// <summary>Options > Graphics "Interface size" and "Screen edge margin" (UiLayout).</summary>
        static readonly int[] UiScaleSteps = { 80, 90, 100, 110, 125, 150 };
        static readonly int[] UiTextSteps = { 100, 115, 130, 150, 175, 200 };
        static readonly float[] UiMarginSteps = { -1f, 0f, 2.5f, 5f, 7.5f, 10f };

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
                Described(Toggle("eachWeaponSound", OptionPage.Sound, () => X("eachWeaponSound", "Every weapon's own shot sound"),
                    () => Settings.EachWeaponSound, v => Settings.EachWeaponSound = v),
                    () => X("eachWeaponSoundHelp", "With different primary weapons mounted, the original plays only the first one's shot sound for all of them. On: every weapon plays its own.")),
                // Remake: the voices apart from the text language (only English and German were recorded); on the Language tab.
                Choice("voiceLanguage", OptionPage.Language, () => X("voiceLanguage", "Voice language"), true,
                    () => new[] { X("voiceAuto", "As text"), "English", "Deutsch" },
                    () => Array.IndexOf(VoiceCodes, Settings.VoiceLanguage) is var i && i >= 0 ? i : 0,
                    i => Settings.VoiceLanguage = VoiceCodes[i]),
            };

            // Remake (desktop; phones pause in the background): silent while the window isn't focused.
            if (!Application.isMobilePlatform)
                list.Add(Toggle("muteInBackground", OptionPage.Sound, () => X("muteInBackground", "Mute in background"),
                    () => Settings.MuteInBackground, v => Settings.MuteInBackground = v));

            AddGraphics(list);
            AddControls(list);
            AddGameplay(list);

            foreach (var d in list)
                if (d.description == null && Help.TryGetValue(d.id, out var help)) d.description = () => X(help.key, help.english);
            return list;
        }

        /// <summary>The descriptions of the rows built without one (their "?" and, with a controller, the line under them):
        /// option id -> its Localization.Extra key and English text.</summary>
        static readonly Dictionary<string, (string key, string english)> Help = new Dictionary<string, (string key, string english)>
        {
            ["master"] = ("masterVolumeHelp", "The volume of every sound at once."),
            ["music"] = ("musicVolumeHelp", "The volume of the music."),
            ["sfx"] = ("sfxVolumeHelp", "The volume of weapons, engines, explosions and menu sounds."),
            ["voice"] = ("voiceVolumeHelp", "The volume of spoken dialogue and radio calls."),
            ["voiceLanguage"] = ("voiceLanguageHelp", "The language the characters speak. Only English and German were recorded; As text picks German with German text, else English."),
            ["muteInBackground"] = ("muteInBackgroundHelp", "Silences the game while its window isn't in front."),
            ["displayMode"] = ("displayModeHelp", "Borderless: a window filling the screen. Fullscreen: the game takes over the display. Windowed: a window you can move."),
            ["resolution"] = ("resolutionHelp", "The size of the game's picture. Display's own uses the screen's full resolution."),
            ["frameRate"] = ("frameRateHelp", "The most frames per second the game draws. V-Sync matches the screen's refresh rate."),
            ["renderScale"] = ("renderScaleHelp", "How sharp the 3D view renders compared to the screen. Lower runs faster but looks softer."),
            ["brightness"] = ("brightnessHelp", "Makes the whole picture darker or brighter."),
            ["bloom"] = ("bloomHelp", "The glow around bright things. Remake: lights and effects glow. Original: the original game's glow on everything bright. Both: the two together."),
            ["hangarShadows"] = ("hangarShadowsHelp", "Soft shadows under the ships in the hangar."),
            ["lensFlare"] = ("lensFlareHelp", "Rings and glare across the screen when you look toward the sun."),
            ["npcPlayerEngines"] = ("npcPlayerEnginesHelp", "Other ships get engine glows and exhaust flames like yours instead of the original's plainer ones."),
            ["fov"] = ("fovHelp", "How wide the flight camera sees. Wider shows more around your ship."),
            ["cameraShake"] = ("cameraShakeHelp", "How much the camera shakes from hits, explosions and boosts."),
            ["showFps"] = ("showFpsHelp", "Shows the frames per second on screen."),
            ["steering"] = ("steeringHelp", "Steer with the touch stick, or by tilting the device."),
            ["tiltSensitivity"] = ("tiltSensitivityHelp", "How strongly tilting the device turns the ship."),
            ["sensitivity"] = ("sensitivityHelp", "How quickly the ship turns when you steer."),
            ["keyAutofire"] = ("keyAutofireHelp", "Press fire twice quickly and the guns keep firing until you press it again, like the touch fire button."),
            ["levelPitch"] = ("levelPitchHelp", "Level out also brings the nose to the horizon, not only the wings."),
            ["invert"] = ("invertYHelp", "Pushing up points the nose down, like a flight stick."),
            ["invertYaw"] = ("invertXHelp", "Pushing left turns the ship right."),
            ["invertDrillY"] = ("invertDrillHelp", "Reverses the drill's movement in the mining game."),
            ["invertDrillX"] = ("invertDrillHelp", "Reverses the drill's movement in the mining game."),
            ["gyroSteering"] = ("gyroSteeringHelp", "Tilt the controller to steer: forward and back to pitch, sideways like a wheel to turn. Level out re-centres it."),
            ["gyroSensitivity"] = ("gyroSensitivityHelp", "How strongly tilting the controller turns the ship."),
            ["mouseSteering"] = ("mouseSteeringHelp", "The mouse steers the ship. Off: the cursor clicks the on-screen buttons. M or the middle mouse button switches it in flight."),
            ["vrGrabControls"] = ("vrGrabControlsHelp", "Grab the cockpit's stick and throttle with the controllers' grips. Off: the controllers work like a gamepad."),
            ["deadzone"] = ("deadzoneHelp", "How far a controller stick moves before it counts, so a worn stick doesn't drift."),
            ["launchCamera"] = ("launchCameraHelp", "A camera watches your ship fly past as you launch or arrive. Off: straight to the chase camera."),
            ["hangarFlights"] = ("hangarFlightsHelp", "Your ship flies into the hangar when you dock and out when you launch, and other ships come and go."),
            ["tutorialHints"] = ("tutorialHintsHelp", "Popups that explain the controls and menus the first time you need them."),
            ["autoAdvance"] = ("autoPlayDialogueHelp", "Conversations turn the page by themselves once a line has been spoken or read. AUTO in a conversation switches it too."),
            ["animatedDialogue"] = ("animatedDialogueHelp", "Dialogue types in letter by letter, with names and items in colour. Off: each page appears at once."),
            ["playOnInConversations"] = ("playOnInConversationsHelp", "In flight, the world keeps moving while a conversation is open. Your controls wait, nothing fires and story missions hold until it closes. Off: the game pauses."),
            ["discordPresence"] = ("discordPresenceHelp", "Shows your ship, location and mission on your Discord profile."),
            ["language"] = ("languageHelp", "The language of every text in the game."),
        };

        /// <summary>510-512 "Low quality:\n- Smoke off\n- Fog off\n- Detail low" as one line: "Smoke off · Fog off · Detail low".</summary>
        static int Nearest(float[] values, float v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i;
            return best;
        }

        static OptionDef Slider(string id, OptionPage page, Func<string> label, float min, float max, Func<float> get, Action<float> set,
                                Func<float, string> format) =>
            new OptionDef { id = id, page = page, kind = OptionKind.Slider, label = label, min = min, max = max, get = get, set = set, format = format,
                            volume = page == OptionPage.Sound };   // the Sound page's sliders are the volumes

        /// <summary>A Gameplay option that is the session's rule in multiplayer (NetRules): its row hides for a player in
        /// someone else's session (the host or the server decides; a player host changes it for everyone).</summary>
        static OptionDef SessionRule(OptionDef d)
        {
            var shown = d.visible;
            d.visible = () => !Multiplayer.NetRules.ClientLocked && (shown == null || shown());
            return d;
        }

        /// <summary>The flight HUD's size and opacity steps (Settings.HudScalePercent / HudOpacityPercent).</summary>
        static readonly int[] HudScaleSteps = { 70, 80, 90, 100, 115, 130, 150 };
        static readonly int[] HudOpacitySteps = { 30, 40, 50, 60, 70, 80, 90, 100 };

        /// <summary>The row's description: its "?" (OptionInfo), the line under it with a controller.</summary>
        static OptionDef Described(OptionDef d, Func<string> text)
        {
            d.description = text;
            return d;
        }

        /// <summary>A change of this row asks to be kept (OptionDef.confirm, SettingsConfirm).</summary>
        static OptionDef Confirmed(OptionDef d)
        {
            d.confirm = true;
            return d;
        }

        static OptionDef Toggle(string id, OptionPage page, Func<string> label, Func<bool> get, Action<bool> set) =>
            new OptionDef { id = id, page = page, kind = OptionKind.Toggle, label = label, getBool = get, setBool = set };

        /// <summary>Modified: the language of the running game: the setting, then the table (Localization.Load raises
        /// Changed, and loads the remake's own texts with it). The main menu does the same in its own buttons.</summary>
        static void SetLanguage(string code)
        {
            if (code == Settings.Language && Localization.Language == code) return;
            Settings.Language = code;
            var table = LanguageTables.TableFor(code);
            if (table != null) Localization.Load(code, table);
        }

        /// <summary>The graphics preset the options match (Settings.PresetKeys, the render scale and MSAA as they apply: 0 = the
        /// platform's asset), else Settings.PresetCustom.</summary>
        public static int CurrentPreset()
        {
            float Now(string key) => key switch
            {
                "fireSmoke" => Settings.FireSmoke ? 1f : 0f,
                "fog" => Settings.Fog ? 1f : 0f,
                "detailDistance" => Settings.DetailDistance,
                "textureFiltering" => Settings.TextureFiltering,
                "renderScale" => Settings.RenderScale > 0f ? Settings.RenderScale : Bootstrap.DefaultRenderScale,
                "upscaler" => Settings.Upscaler,
                "msaa" => Settings.Msaa > 0 ? Settings.Msaa : Bootstrap.DefaultMsaa,
                "bloomStyle" => Settings.BloomStyle,
                "hangarShadows" => Settings.HangarShadows,
                "hangarDof" => Settings.HangarDepthOfField ? 1f : 0f,
                "lensFlare" => Settings.LensFlare ? 1f : 0f,
                "npcPlayerEngines" => Settings.NpcPlayerEngines ? 1f : 0f,
                "lods" => Settings.Lods ? 1f : 0f,
                "textureQuality" => Settings.TextureQuality,
                _ => float.NaN,
            };
            for (int p = Settings.PresetLow; p <= Settings.PresetUltra; p++)
                if (Settings.PresetKeys.All(k => Mathf.Abs(Now(k) - Settings.PresetValue(p, k)) < 0.01f)) return p;
            return Settings.PresetCustom;
        }

        static OptionDef Choice(string id, OptionPage page, Func<string> label, bool segmented, Func<string[]> choices, Func<int> get,
                                Action<int> set, Func<string> description = null) =>
            new OptionDef
            {
                id = id, page = page, kind = OptionKind.Choice, label = label, segmented = segmented, choices = choices, getIndex = get,
                setIndex = set, description = description,
            };
    }
}
