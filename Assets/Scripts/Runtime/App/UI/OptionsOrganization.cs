// Built-in option placement is explicit. A new gameplay rule must never silently become a cosmetic preference.
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoF2Remake.UI
{
    public static partial class OptionsCatalog
    {
        static readonly Dictionary<string, (OptionPage page, OptionSection section, OptionGroup group)> Organization = BuildOrganization();

        static Dictionary<string, (OptionPage page, OptionSection section, OptionGroup group)> BuildOrganization()
        {
            var result = new Dictionary<string, (OptionPage page, OptionSection section, OptionGroup group)>();
            void Add(OptionPage page, OptionSection section, OptionGroup group, params string[] ids)
            {
                foreach (var id in ids) result.Add(id, (page, section, group));
            }
            Add(OptionPage.Cosmetics, OptionSection.SpecialFeatures, OptionGroup.None,
                "presentationPreset", "launchCamera", "hangarFlights", "animatedDialogue", "autoAdvance", "systemMapAnimationSpeed");
            Add(OptionPage.Cosmetics, OptionSection.Interface, OptionGroup.None,
                "originalUi", "inputHints", "hintScale", "shopByCategory", "kaamoSpecialUiFeatures", "autoJumpgate");
            Add(OptionPage.Cosmetics, OptionSection.Hud, OptionGroup.None,
                "hudMode", "hudScale", "hudOpacity", "farShipMarkers", "markerDistance", "dyingCargoMarkers", "interstellarDistance");
            Add(OptionPage.Gameplay, OptionSection.Tools, OptionGroup.None,
                "showStoryStep", "speedrunTimer", "speedrunSplits", "speedrunLength", "speedrunTiming", "speedrunCompare",
                "speedrunPosition", "speedrunLiveSplit", "speedrunReset", "speedrunFolder", "saveFolder", "debugTools");
            // Storage commands are outside the gameplay profile and the rule-editing consent gate.
            Add(OptionPage.Gameplay, OptionSection.None, OptionGroup.None, "gameplayExperience");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.World,
                "stationScale");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Progression,
                "kaamoKeepsEquipment");
            // (how you fly and aim first, then what weapons and cloaks do, then collisions, then the NPCs)
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Combat,
                "flightStyle", "remakeFreeLook", "targetLock", "keepEnemyLock", "turretLeadAim", "bombArming",
                "cloakLosesPursuers", "empBreaksCloak", "collisionDamage", "shipCollision",
                "smarterNpcTargeting", "npcDogfight", "es2Aiming", "es2NpcFlight");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Flight,
                "fastForwardMode", "hostileAutopilotFastForward");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Missions,
                "informerRule", "playOnInConversations", "modShipTraffic");
            return result;
        }

        /// <summary>A row's place in its section or group: the order the table above lists it in (2026-11-20: the rows
        /// stood in the order the catalog happened to make them); a row not listed keeps its place after them.</summary>
        static int OrganizationRank(string id)
        {
            if (organizationRanks == null)
            {
                organizationRanks = new Dictionary<string, int>();
                foreach (string key in Organization.Keys) organizationRanks.Add(key, organizationRanks.Count);   // (a Dictionary lists in the order added)
            }
            return id != null && organizationRanks.TryGetValue(id, out int rank) ? rank : int.MaxValue;
        }
        static Dictionary<string, int> organizationRanks;

        /// <summary>Coverage check for catalog/tests; mod-provided settings have their own explicit group.</summary>
        public static bool HasExplicitOrganization(string id) => id == "discordPresence"
            || (id != null && (Organization.ContainsKey(id) || id.StartsWith("mod:", StringComparison.Ordinal)));

        /// <summary>Keep unknown future rules in gated Gameplay, rather than quietly moving them into Cosmetics.</summary>
        public static void OrganizeGameplayOption(OptionDef row)
        {
            if (row.id == "discordPresence")
            {
                row.page = OptionPage.Accessibility;
                row.section = OptionSection.None;
                row.group = OptionGroup.None;
            }
            else if (row.id != null && Organization.TryGetValue(row.id, out var placement))
            {
                row.page = placement.page;
                row.section = placement.section;
                row.group = placement.group;
            }
            else if (row.id != null && row.id.StartsWith("mod:", StringComparison.Ordinal))
            {
                row.page = OptionPage.Gameplay; row.section = OptionSection.GameRules; row.group = OptionGroup.Missions;
            }
            else
            {
                row.page = OptionPage.Gameplay;
                row.section = OptionSection.GameRules;
                row.group = row.id != null && row.id.StartsWith("mod:", StringComparison.Ordinal) ? OptionGroup.Mods : OptionGroup.Custom;
            }
        }

        // ---- the Graphics tab's categories (remake, 2026-11-20, the user's request: "in categories just like gameplay has")
        // Sections with a heading, like the Cosmetics and Gameplay tabs': the preset row above them all, then each
        // category's rows in the order listed here. A row not listed (a new option) lands under Effects.
        static readonly (OptionSection section, string[] ids)[] GraphicsLayout =
        {
            (OptionSection.GraphicsDisplay, new[] { "displayMode", "resolution", "frameRate", "brightness", "hdr", "hdrPaperWhite", "hdrDetectLimits",
                                                    "hdrPeak", "hdrBlack", "hdrTonemapping", "hdrAcesPreset", "hdrRangeReduction", "hdrHueShift", "hdrCalibrate" }),
            (OptionSection.GraphicsUpscaling, new[] { "renderScale", "upscaler", "upscalerQuality", "dlssPreset", "fsr4Lite" }),
            // (what makes frames and what paces them: frame generation with Reflex / Anti-Lag beside it)
            (OptionSection.GraphicsFrames, new[] { "frameGeneration", "frameGenerationProvider", "frameGenerationTarget", "reflex", "antiLag" }),
            (OptionSection.GraphicsQuality, new[] { "msaa", "postAa", "nisSharpness", "textureQuality", "textureFiltering", "detailDistance", "lods" }),
            (OptionSection.GraphicsEffects, new[] { "bloom", "bloomStrength", "lensFlare", "fireSmoke", "fog", "npcPlayerEngines",
                                                    "dynamicVibrance", "dynamicVibranceIntensity", "dynamicVibranceSaturation",
                                                    "neuralRendering", "neuralRenderingIntensity", "neuralRenderingTone", "neuralRenderingStructure",
                                                    "neuralRenderingSkin", "neuralRenderingQuality", "remasteredEffects" }),
            (OptionSection.GraphicsStations, new[] { "hangarShadows", "hangarDof", "hangarRobots", "barRobots" }),
            (OptionSection.GraphicsWorld, new[] { "skyStyle", "nebulaClouds", "liveSkies", "rebuiltPlanets" }),
            (OptionSection.GraphicsCamera, new[] { "fov", "cameraShake", "chaseCamera", "es2FreeFlightCamera" }),
            (OptionSection.GraphicsInterface, new[] { "uiWidth", "uiMargin", "screenCutout", "showFps", "fpsPosition", "gameCursor" }),
            // (the main menu's: the HD resources and add-on packs)
            (OptionSection.GraphicsPacks, new[] { "addPack", "hdResourcesWindow", "hdResources" }),
        };

        /// <summary>The rows above the categories, in this order: the preset and the benchmark (the user: under Interface
        /// "is not a good place"), which are about all of the tab's options.</summary>
        static readonly string[] GraphicsTop = { "graphicsPreset", "benchmark" };

        static readonly Dictionary<string, (OptionSection section, int rank)> GraphicsPlaces = BuildGraphicsPlaces();

        static Dictionary<string, (OptionSection section, int rank)> BuildGraphicsPlaces()
        {
            var result = new Dictionary<string, (OptionSection section, int rank)>();
            int rank = 0;
            foreach (var (section, ids) in GraphicsLayout)
                foreach (var id in ids) result.Add(id, (section, rank++));
            return result;
        }

        /// <summary>The Graphics row has a category of its own choosing (the catalog's test: a new row is placed on purpose).</summary>
        public static bool HasGraphicsPlace(string id) => id != null && (Array.IndexOf(GraphicsTop, id) >= 0 || GraphicsPlaces.ContainsKey(id));

        static void OrganizeGraphics(List<OptionDef> list)
        {
            var rows = list.Where(row => row.page == OptionPage.Graphics).ToList();
            int Top(OptionDef row) => row.id != null ? Array.IndexOf(GraphicsTop, row.id) : -1;
            int Rank(OptionDef row) => Top(row) >= 0 ? Top(row) - GraphicsTop.Length
                : row.id != null && GraphicsPlaces.TryGetValue(row.id, out var place) ? place.rank
                : GraphicsPlaces["remasteredEffects"].rank;   // (unlisted: the end of Effects, in the order added)
            foreach (var row in rows)
            {
                row.group = OptionGroup.None;
                row.section = Top(row) >= 0 ? OptionSection.None
                    : row.id != null && GraphicsPlaces.TryGetValue(row.id, out var place) ? place.section : OptionSection.GraphicsEffects;
            }
            list.RemoveAll(row => row.page == OptionPage.Graphics);
            list.AddRange(rows.OrderBy(Rank));   // (a stable sort)
        }

        // ---- the other tabs' categories (remake, 2026-11-20, the user's request: "add categories to the other settings
        // pages as well") ---------------------------------------------------------------------------------------------
        // Sound, Controls, Accessibility and Key bindings under headings like the Graphics tab's: each tab's categories
        // with their rows in the order shown. A row not listed (a new option) lands at the end of the tab's first
        // category; a row in 'top' stays above the headings (the bindings' reset button with the columns' header). The
        // Language tab has two rows and no headings.
        // (made on first use, not by a field initializer: the order of those across the class's files is not defined, and
        // this one reads MusicTrackRows)
        static (OptionPage page, string[] top, (OptionSection section, string[] ids)[] layout)[] pageLayouts;
        static (OptionPage page, string[] top, (OptionSection section, string[] ids)[] layout)[] PageLayouts => pageLayouts ??= BuildPageLayouts();

        static (OptionPage page, string[] top, (OptionSection section, string[] ids)[] layout)[] BuildPageLayouts() => new (OptionPage, string[], (OptionSection, string[])[])[]
        {
            (OptionPage.Sound, new string[0], new[]
            {
                (OptionSection.SoundVolume, new[] { "master", "music", "sfx", "voice", "muteInBackground" }),
                (OptionSection.SoundEffects, new[] { "eachWeaponSound", "challengeComboSounds", "medalSound" }),
                (OptionSection.SoundMusic, MusicTrackRows.Select(row => "musicTrack_" + row.track).ToArray()),
            }),
            (OptionPage.Controls, new string[0], new[]
            {
                (OptionSection.ControlsFlight, new[] { "sensitivity", "keyAutofire", "levelPitch", "autoRoll", "boostToggle", "cockpitInCycle" }),
                (OptionSection.ControlsInvert, new[] { "invert", "invertYaw", "invertDrillY", "invertDrillX", "invertOrbitY", "invertOrbitX" }),
                (OptionSection.ControlsMouse, new[] { "mouseSteering", "mouseSensitivity", "mouseDeadzone" }),
                // (the vibration with what vibrates: the controller, on a phone or tablet the device itself)
                (OptionSection.ControlsController, UnityEngine.Application.isMobilePlatform
                    ? new[] { "deadzone", "gyroSteering", "gyroSensitivity", "padGlyphs", "nintendoLayout" }
                    : new[] { "deadzone", "haptics", "gyroSteering", "gyroSensitivity", "padGlyphs", "nintendoLayout" }),
                (OptionSection.ControlsTouch, UnityEngine.Application.isMobilePlatform
                    ? new[] { "touchLeftHeight", "touchRightHeight", "floatingTouchStick", "touchOpacity", "haptics" }
                    : new[] { "touchLeftHeight", "touchRightHeight", "floatingTouchStick", "touchOpacity" }),
                (OptionSection.ControlsVr, new[] { "vrGrabControls" }),
            }),
            (OptionPage.Accessibility, new string[0], new[]
            {
                (OptionSection.AccessSize, new[] { "uiScale", "uiTextScale", "cursorSize", "uiFont", "uiFontImport", "uiFontRemove" }),
                (OptionSection.AccessVisibility, new[] { "highContrast", "colorblindMarkers", "dialogueHighlights", "reduceMotion" }),
                (OptionSection.AccessPrivacy, new[] { "discordPresence" }),
            }),
            (OptionPage.Bindings, new[] { "resetBindings" }, new[]
            {
                (OptionSection.BindFlight, new[] { "bind_steer", "bind_throttle", "bind_brake", "bind_boost", "bind_roll", "bind_levelOut", "bind_strafeLeft",
                                                   "bind_strafeRight", "bind_dodgeLeft", "bind_dodgeRight", "bind_thrust", "bind_strafe", "bind_hover",
                                                   "bind_dampeners", "bind_drift", "bind_travelBoost", "bind_mouseSteering" }),
                (OptionSection.BindWeapons, new[] { "bind_firePrimary", "bind_fireSecondary", "bind_switchSecondary", "bind_turretView", "bind_autoTurret",
                                                    "bind_unlockTarget" }),
                (OptionSection.BindNavigation, new[] { "bind_action", "bind_autopilotMenu", "bind_actionsMenu", "bind_fastForward", "bind_khadorDrive",
                                                       "bind_drill" }),
                (OptionSection.BindEquipment, new[] { "bind_wingmen", "bind_cloak", "bind_timeExtender", "bind_device1", "bind_device2", "bind_device3",
                                                      "bind_headlights" }),
                (OptionSection.BindCamera, new[] { "bind_camera", "bind_cockpitView", "bind_freeLookHold", "bind_hudToggle", "bind_screenshot" }),
                (OptionSection.BindMultiplayer, new[] { "bind_chat", "bind_chatSend", "bind_chatChannel", "bind_multiplayerWindow", "bind_distressCall" }),
            }),
        };

        /// <summary>The tab's rows have categories (Sound, Controls, Accessibility, Key bindings; Graphics has its own).</summary>
        public static bool HasCategories(OptionPage page) => PageLayouts.Any(entry => entry.page == page);

        /// <summary>The row has a place of its own choosing on its tab: a category, or above them (the catalog's test: a new
        /// row is placed on purpose).</summary>
        public static bool HasPagePlace(OptionPage page, string id)
        {
            foreach (var (p, top, layout) in PageLayouts)
                if (p == page) return id != null && (top.Contains(id) || layout.Any(category => category.ids.Contains(id)));
            return true;
        }

        static void OrganizePages(List<OptionDef> list)
        {
            foreach (var (page, top, layout) in PageLayouts)
            {
                var places = new Dictionary<string, (OptionSection section, int rank)>();
                int next = 0;
                foreach (var (section, ids) in layout)
                    foreach (string id in ids) places[id] = (section, next++);
                var first = layout[0];
                int unlisted = places[first.ids[first.ids.Length - 1]].rank;   // (unlisted: the end of the first category, in the order added)
                var rows = list.Where(row => row.page == page).ToList();
                int Rank(OptionDef row) => row.id != null && top.Contains(row.id) ? -1
                    : row.id != null && places.TryGetValue(row.id, out var place) ? place.rank : unlisted;
                foreach (var row in rows)
                {
                    row.group = OptionGroup.None;
                    row.section = row.id != null && top.Contains(row.id) ? OptionSection.None
                        : row.id != null && places.TryGetValue(row.id, out var place) ? place.section : first.section;
                }
                list.RemoveAll(row => row.page == page);
                list.AddRange(rows.OrderBy(Rank));   // (a stable sort)
            }
        }

        static void OrganizeGameplay(List<OptionDef> list)
        {
            foreach (var row in list.Where(row => row.page == OptionPage.Gameplay)) OrganizeGameplayOption(row);
            var ordered = list.Where(row => row.section != OptionSection.None)
                .OrderBy(row => row.page == OptionPage.Gameplay ? row.section == OptionSection.GameRules ? 0 : row.section == OptionSection.Mods ? 1 : 2 : (int)row.section)
                .ThenBy(row => row.id == "gameplayExperience" ? -1 : (int)row.group)
                .ThenBy(row => OrganizationRank(row.id))
                .ToList();
            list.RemoveAll(row => row.section != OptionSection.None);
            list.AddRange(ordered);
        }
    }
}
