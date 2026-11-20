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
                "cosmeticExperience", "launchCamera", "hangarFlights", "animatedDialogue", "autoAdvance");
            Add(OptionPage.Cosmetics, OptionSection.Interface, OptionGroup.None,
                "originalUi", "tutorialHints", "inputHints", "hintScale", "shopByCategory", "kaamoSpecialUiFeatures");
            Add(OptionPage.Cosmetics, OptionSection.Hud, OptionGroup.None,
                "hudMode", "hudScale", "hudOpacity", "dyingCargoMarkers", "farShipMarkers", "markerDistance", "interstellarDistance", "systemMapAnimationSpeed");
            Add(OptionPage.Gameplay, OptionSection.Tools, OptionGroup.None,
                "showStoryStep", "speedrunTimer", "speedrunSplits", "speedrunLength", "speedrunTiming", "speedrunCompare",
                "speedrunPosition", "speedrunLiveSplit", "speedrunReset", "speedrunFolder", "saveFolder", "debugTools");
            // Storage commands are outside the gameplay profile and the rule-editing consent gate.
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.None, "gameplayExperience");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.None, "difficulty");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.World,
                "pirateEvents", "capitalShips", "derelictWrecks", "stationScale");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Progression,
                "kaamoStacking", "kaamoMaxLevel", "kaamoVip", "cloakBay",
                "kaamoKeepsEquipment", "blueprintAutocomplete");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Combat,
                "cloakLosesPursuers", "empBreaksCloak", "bombArming", "smarterNpcTargeting", "npcDogfight",
                "es2Aiming", "es2NpcFlight", "shipCollision", "targetLock", "keepEnemyLock", "collisionDamage", "flightStyle",
                "turretLeadAim", "remakeFreeLook");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Flight,
                "fastForwardMode", "khadorSlipstream",
                "hostileAutopilotFastForward", "autoJumpgate");
            Add(OptionPage.Gameplay, OptionSection.GameRules, OptionGroup.Missions,
                "informerRule", "playOnInConversations", "modShipTraffic");
            return result;
        }

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
            (OptionSection.GraphicsUpscaling, new[] { "renderScale", "upscaler", "upscalerQuality", "dlssPreset", "fsr4Lite", "frameGeneration", "frameGenerationProvider", "frameGenerationTarget", "reflex", "antiLag",
                                                      "neuralRendering", "neuralRenderingIntensity", "neuralRenderingTone", "neuralRenderingStructure",
                                                      "neuralRenderingSkin", "neuralRenderingQuality" }),
            (OptionSection.GraphicsQuality, new[] { "msaa", "postAa", "textureQuality", "textureFiltering", "detailDistance", "lods" }),
            (OptionSection.GraphicsEffects, new[] { "bloom", "bloomStrength", "lensFlare", "fireSmoke", "fog", "hangarShadows", "hangarDof", "hangarRobots",
                                                    "barRobots", "npcPlayerEngines", "nisSharpness", "dynamicVibrance", "dynamicVibranceIntensity",
                                                    "dynamicVibranceSaturation", "remasteredEffects" }),
            (OptionSection.GraphicsWorld, new[] { "skyStyle", "nebulaClouds", "liveSkies", "rebuiltPlanets", "addPack", "hdResourcesWindow", "hdResources" }),
            (OptionSection.GraphicsCamera, new[] { "fov", "cameraShake", "chaseCamera", "es2FreeFlightCamera" }),
            (OptionSection.GraphicsInterface, new[] { "uiWidth", "uiMargin", "screenCutout", "showFps", "fpsPosition", "gameCursor" }),
        };

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
        public static bool HasGraphicsPlace(string id) => id == "graphicsPreset" || id != null && GraphicsPlaces.ContainsKey(id);

        static void OrganizeGraphics(List<OptionDef> list)
        {
            var rows = list.Where(row => row.page == OptionPage.Graphics).ToList();
            int Rank(OptionDef row) => row.id == "graphicsPreset" ? -1
                : row.id != null && GraphicsPlaces.TryGetValue(row.id, out var place) ? place.rank
                : GraphicsPlaces["remasteredEffects"].rank;   // (unlisted: the end of Effects, in the order added)
            foreach (var row in rows)
            {
                row.group = OptionGroup.None;
                row.section = row.id == "graphicsPreset" ? OptionSection.None
                    : row.id != null && GraphicsPlaces.TryGetValue(row.id, out var place) ? place.section : OptionSection.GraphicsEffects;
            }
            list.RemoveAll(row => row.page == OptionPage.Graphics);
            list.AddRange(rows.OrderBy(Rank));   // (a stable sort)
        }

        static void OrganizeGameplay(List<OptionDef> list)
        {
            foreach (var row in list.Where(row => row.page == OptionPage.Gameplay)) OrganizeGameplayOption(row);
            var ordered = list.Where(row => row.section != OptionSection.None)
                .OrderBy(row => row.page == OptionPage.Gameplay ? row.section == OptionSection.GameRules ? 0 : row.section == OptionSection.Mods ? 1 : 2 : (int)row.section)
                .ThenBy(row => row.id == "gameplayExperience" ? -1 : (int)row.group)
                .ToList();
            list.RemoveAll(row => row.section != OptionSection.None);
            list.AddRange(ordered);
        }
    }
}
