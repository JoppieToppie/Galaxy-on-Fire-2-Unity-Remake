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
