using System;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class OptionsOrganizationTests : SandboxTest
    {
        DataUiTestKit.PrefsSnapshot prefs;
        [SetUp] public void SavePreferences() => prefs = new DataUiTestKit.PrefsSnapshot();
        [TearDown] public void RestorePreferences() => prefs?.Dispose();

        [Test]
        public void EveryBuiltInExperienceRowHasExplicitOrganization()
        {
            var rows = OptionsCatalog.All().Where(o => o.page == OptionPage.Cosmetics || o.page == OptionPage.Gameplay).ToArray();
            Assert.IsNotEmpty(rows);
            foreach (var row in rows)
            {
                Assert.IsTrue(OptionsCatalog.HasExplicitOrganization(row.id), row.id + " needs an explicit layout decision");
                if (row.id == "cosmeticExperience" || row.id == "gameplayExperience" || row.id == "difficulty")
                {
                    Assert.AreEqual(OptionSection.None, row.section, row.id);
                    continue;
                }
                Assert.AreNotEqual(OptionSection.None, row.section, row.id);
                if (row.page == OptionPage.Cosmetics) Assert.AreEqual(OptionGroup.None, row.group, row.id);
                if (row.page == OptionPage.Gameplay && row.section != OptionSection.Tools && row.section != OptionSection.Mods)
                    Assert.AreEqual(OptionSection.GameRules, row.section, row.id + " shares the rule-editing gate");
                if (row.section == OptionSection.GameRules && row.id != "gameplayExperience" && row.id != "difficulty")
                    Assert.AreNotEqual(OptionGroup.None, row.group, row.id);
            }
        }

        [TestCase("newGameplayRule", OptionGroup.Custom, false)]
        [TestCase("mod:example.rule", OptionGroup.Missions, true)]
        public void UnknownAndModRulesStayInsideTheGameRulesGate(string id, OptionGroup group, bool explicitPlacement)
        {
            var row = new OptionDef { id = id, page = OptionPage.Gameplay };
            OptionsCatalog.OrganizeGameplayOption(row);
            Assert.AreEqual(OptionPage.Gameplay, row.page);
            Assert.AreEqual(OptionSection.GameRules, row.section);
            Assert.AreEqual(group, row.group);
            Assert.AreEqual(explicitPlacement, OptionsCatalog.HasExplicitOrganization(id));
        }

        [TestCase("launchCamera", OptionSection.SpecialFeatures)]
        [TestCase("originalUi", OptionSection.Interface)]
        [TestCase("tutorialHints", OptionSection.Interface)]
        [TestCase("hudMode", OptionSection.Hud)]
        [TestCase("dyingCargoMarkers", OptionSection.Hud)]
        [TestCase("showStoryStep", OptionSection.Tools)]
        [TestCase("speedrunTimer", OptionSection.Tools)]
        public void CosmeticRowsUseTheFourPresentationSections(string id, OptionSection section)
        {
            var row = OptionsCatalog.All().Single(o => o.id == id);
            Assert.AreEqual(section == OptionSection.Tools ? OptionPage.Gameplay : OptionPage.Cosmetics, row.page);
            Assert.AreEqual(section, row.section);
        }

        [Test]
        public void FlightModelBelongsToGameplayAndCombatOptionsHaveTheirOwnGroup()
        {
            var all = OptionsCatalog.All();
            var flight = all.Single(o => o.id == "flightStyle");
            Assert.AreEqual(OptionPage.Gameplay, flight.page);
            Assert.AreEqual(OptionSection.GameRules, flight.section);
            Assert.AreEqual(OptionGroup.Combat, flight.group);
            foreach (var id in new[] { "npcDogfight", "es2Aiming", "es2NpcFlight", "shipCollision", "collisionDamage" })
                Assert.AreEqual(OptionGroup.Combat, all.Single(o => o.id == id).group, id);
            Assert.AreEqual(OptionGroup.Progression, all.Single(o => o.id == "kaamoKeepsEquipment").group);
            foreach (var row in all.Where(o => o.id == "blueprintAutocomplete"))
                Assert.AreEqual(OptionGroup.Progression, row.group, row.id);
            foreach (var row in all.Where(o => o.id == "hostileAutopilotFastForward"))
                Assert.AreEqual(OptionGroup.Flight, row.group, row.id);
            foreach (var id in new[] { "autoJumpgate", "fastForwardMode" })
                Assert.AreEqual(OptionGroup.Flight, all.Single(o => o.id == id).group, id);
            Assert.AreEqual(OptionGroup.World, all.Single(o => o.id == "stationScale").group);
            Assert.IsFalse(all.Any(o => o.page == OptionPage.Gameplay && o.section == OptionSection.Convenience));
            Assert.IsFalse(all.Any(o => o.group == OptionGroup.Custom && !o.id.StartsWith("mod:")),
                "Custom-only policy is independent of a built-in option's functional location");
            var rules = all.Where(o => o.section == OptionSection.GameRules).ToArray();
            CollectionAssert.AreEqual(new[] { "gameplayExperience", "difficulty" }, all.Where(o => o.page == OptionPage.Gameplay).Take(2).Select(o => o.id));
            Assert.IsFalse(rules.Any(o => o.id == "debugTools"));
            Assert.AreEqual(OptionGroup.None, all.Single(o => o.id == "difficulty").group);
            Assert.AreEqual(OptionGroup.Missions, all.Single(o => o.id == "modShipTraffic").group);
            Assert.IsFalse(all.Any(o => o.section == OptionSection.Mods));
        }

        [Test]
        public void StorageCommandsRemainOutsideTheRuleEditingGate()
        {
            var row = new OptionDef { id = "saveFolder", page = OptionPage.Gameplay, kind = OptionKind.Button };
            OptionsCatalog.OrganizeGameplayOption(row);
            Assert.AreEqual(OptionSection.Tools, row.section);
            Assert.AreEqual(OptionGroup.None, row.group);
            var block = new OptionSectionBlock(new VisualElement(), row.section, _ => Assert.Fail("Storage needs no rule consent"), false);
            Assert.AreNotEqual(DisplayStyle.None, block.Body.style.display.value);
        }

        [TestCase(false)] [TestCase(true)]
        public void DebugEnablingAlwaysRequiresConfirmationWithoutOpeningGameRules(bool rulesAlreadyOpen)
        {
            bool before = GameRulesAccess.Confirmed;
            try
            {
                if (rulesAlreadyOpen) GameRulesAccess.Accept(); else GameRulesAccess.Reset();
                Action accept = null;
                int prompts = 0;
                bool enabled = false;
                var block = new OptionSectionBlock(new VisualElement(), OptionSection.Tools,
                    _ => Assert.Fail("Debug must use its own confirmation"), false,
                    action => { prompts++; accept = action; });
                var row = new OptionDef { id = "debugTools", setBool = value => enabled = value };
                block.Add(row, new VisualElement());
                Assert.AreEqual(DisplayStyle.Flex, block.Body.style.display.value);
                row.setBool(true);
                Assert.AreEqual(1, prompts);
                Assert.IsFalse(enabled, "requesting or cancelling the prompt cannot enable Debug");
                Assert.AreEqual(rulesAlreadyOpen, GameRulesAccess.Confirmed);
                accept();
                Assert.IsTrue(enabled);
                Assert.AreEqual(rulesAlreadyOpen, GameRulesAccess.Confirmed);
                row.setBool(false);
                Assert.IsFalse(enabled);
                Assert.AreEqual(1, prompts, "disabling needs no prompt");
                row.setBool(true);
                Assert.AreEqual(2, prompts, "each enabling needs confirmation");
                Assert.IsFalse(enabled);
            }
            finally { if (before) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); }
        }

        [Test]
        public void SubgroupHeadingsShowAndCustomAsksNothing()
        {
            bool confirmed = GameRulesAccess.Confirmed;
            try
            {
                GameRulesAccess.Reset();
                Settings.ApplyScopedExperience(false, 1);
                bool pirates = Settings.PirateEvents;
                Action accept = null;
                var block = new OptionSectionBlock(new VisualElement(), OptionSection.GameRules, a => accept = a, false);
                var ruleRow = new VisualElement();
                block.Add(new OptionDef { id = "pirateEvents", group = OptionGroup.World }, ruleRow);
                Assert.AreEqual(1, block.Body.parent.childCount);
                Assert.IsTrue(block.Body.Contains(ruleRow));
                Assert.AreEqual(1, block.Body.Query<Label>(className: "options-group-title").ToList().Count);
                Assert.AreNotEqual(DisplayStyle.None, block.Body.style.display.value, "the rules show without a gate (2026-11-20)");
                Assert.AreEqual(1, Settings.CurrentScopedExperience(false));
                OptionsCatalog.All().Single(o => o.id == "gameplayExperience").setIndex(2);
                Assert.IsNull(accept, "no consent is asked");
                Assert.AreEqual(2, Settings.CurrentScopedExperience(false));
                Assert.AreEqual(pirates, Settings.PirateEvents, "picking Custom applies no rule");
            }
            finally { if (confirmed) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); }
        }

        [TestCase(OptionPage.Cosmetics, "speedrunFolder")]
        [TestCase(OptionPage.Cosmetics, "speedrunReset")]
        [TestCase(OptionPage.Gameplay, "saveFolder")]
        public void ActionButtonsDoNotChangeExperienceSelection(OptionPage page, string id)
        {
            bool cosmetics = page == OptionPage.Cosmetics;
            Settings.ApplyScopedExperience(cosmetics, 0);
            int called = 0;
            var row = new OptionDef { id = id, page = page, label = () => id, kind = OptionKind.Button, action = () => called++ };
            new OptionControl(row).Activate();
            Assert.AreEqual(1, called);
            Assert.AreEqual(0, Settings.CurrentScopedExperience(cosmetics), "a tool/picker action is not a setting edit");
        }

        [Test]
        public void EmptySubgroupHeadingFollowsItsRowsVisibility()
        {
            bool visible = false;
            var block = new OptionSectionBlock(new VisualElement(), OptionSection.GameRules, _ => { }, false);
            block.Add(new OptionDef { id = "debugTools", group = OptionGroup.Debug, visible = () => visible }, new VisualElement());
            var header = block.Body.Q<Label>(className: "options-group-title");
            Assert.AreEqual(DisplayStyle.None, header.style.display.value);
            visible = true;
            block.Refresh();
            Assert.AreEqual(DisplayStyle.Flex, header.style.display.value);
            visible = false;
            block.Refresh();
            Assert.AreEqual(DisplayStyle.None, header.style.display.value);
        }

        [Test]
        public void SessionValueEditsStillSelectCustomWithoutASettingsSetter()
        {
            Settings.ApplyScopedExperience(false, 1);
            int selected = 0;
            var control = new OptionControl(new OptionDef
            {
                id = "sessionChoice", page = OptionPage.Gameplay, label = () => "Session choice", kind = OptionKind.Choice,
                choices = () => new[] { "A", "B" }, getIndex = () => selected, setIndex = value => selected = value,
            });
            control.Step(1);
            Assert.AreEqual(1, selected);
            Assert.AreEqual(2, Settings.CurrentScopedExperience(false));
        }

    }
}
