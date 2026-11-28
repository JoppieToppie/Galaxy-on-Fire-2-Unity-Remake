using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class RemakeDefaultSettingsTests : SandboxTest
    {
        [TestCase(true)] [TestCase(false)]
        public void EveryScopedPreferenceHasAnExplicitPresetValue(bool cosmetics)
        {
            var keys = cosmetics ? Settings.CosmeticsKeys.Concat(new[] { "rebuiltPlanets", "cameraShake", "remasteredEffects" }) : Settings.GameplayKeys;
            var excluded = new[] { "cosmeticExperienceCustom", "gameplayExperienceCustom", "preciseShipCollision", "autoJumpgate", "systemMapAnimationSpeed",
                "showStoryStep", "speedrunTimer", "speedrunSplits", "speedrunLength", "speedrunTiming", "speedrunCompare",
                "speedrunPosition", "speedrunLiveSplit" };
            CollectionAssert.AreEquivalent(keys.Except(excluded), ExperiencePresets.ForScope(cosmetics).Select(v => v.Key));
        }

        [Test]
        public void DifficultyAndToolsDoNotSelectCustomGameplay()
        {
            using (new DataUiTestKit.PrefsSnapshot())
            using (new SettingsScope<float>(() => Session.Difficulty, v => Session.Difficulty = v))
            {
                Settings.ApplyScopedExperience(false, 1);
                Session.Difficulty = Session.DifficultyExtreme;
                foreach (string id in new[] { "showStoryStep" })
                {
                    var def = OptionsCatalog.All().First(v => v.id == id);
                    if (def.setIndex != null) def.setIndex(3);
                    else def.setBool(true);
                    var control = new OptionControl(def);
                    typeof(OptionControl).GetMethod("NotifyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(control, null);
                    Assert.AreEqual(1, Settings.CurrentScopedExperience(false), id);
                }
            }
        }

        [TestCase(true, 0)] [TestCase(true, 1)] [TestCase(false, 0)] [TestCase(false, 1)]
        public void NamedPresetsReplaceEveryParameterInsteadOfInheritingCustom(bool cosmetics, int choice)
        {
            using (new DataUiTestKit.PrefsSnapshot())
            {
                foreach (var value in ExperiencePresets.ForScope(cosmetics))
                    PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + value.Key, value.ForChoice(choice) == 0f ? 1f : 0f);
                DataUiTestKit.ClearSettingsCache();
                Settings.ApplyScopedExperience(cosmetics, choice);
                foreach (var value in ExperiencePresets.ForScope(cosmetics))
                    Assert.AreEqual(value.ForChoice(choice), PlayerPrefs.GetFloat(DataUiTestKit.SettingsPrefix + value.Key), value.Key);
                Assert.AreEqual(choice, Settings.CurrentScopedExperience(cosmetics));
            }
        }

        [TestCase(0)] [TestCase(1)]
        public void CustomOnlyMechanicsAreDisabledByBothNamedPresets(int choice)
        {
            using (new DataUiTestKit.PrefsSnapshot())
            {
                Settings.Es2Aiming = Settings.Es2NpcFlight = Settings.CollisionDamage = true;
                Settings.FastForwardCruise = true;
                Settings.FlightStyle = FlightStyles.Free;
                Settings.ApplyScopedExperience(false, choice);
                Assert.IsFalse(Settings.Es2Aiming || Settings.Es2NpcFlight || Settings.CollisionDamage || Settings.FastForwardCruise);
                Assert.AreEqual(FlightStyles.Original, Settings.FlightStyle);
                Settings.Es2NpcFlight = true;
                Assert.AreEqual(2, Settings.CurrentScopedExperience(false));
                Settings.ApplyScopedExperience(false, 2);
                Assert.IsTrue(Settings.Es2NpcFlight, "Custom preserves manual settings");
            }
        }

        [TestCase(0)] [TestCase(1)]
        public void BothPresetsUseTimeSpeedupAndPauseConversationsWithoutKeepingEnemyLock(int preset)
        {
            using (new DataUiTestKit.PrefsSnapshot())
            {
                Settings.FastForwardCruise = true;
                Settings.PlayOnInConversations = true;
                Settings.KeepEnemyLock = true;
                Settings.ApplyScopedExperience(false, preset);
                Assert.IsFalse(Settings.FastForwardCruise);
                Assert.IsFalse(Settings.PlayOnInConversations);
                Assert.IsFalse(Settings.KeepEnemyLock);
                Assert.AreEqual(preset, Settings.CurrentScopedExperience(false));
            }
        }

        [Test]
        public void DefaultsBypassRulesConsentAndKeepBothRemakeLabelsAfterRefresh()
        {
            using (new DataUiTestKit.PrefsSnapshot())
            using (new SettingsScope<bool>(() => Cheats.Unlocked, v => Cheats.Unlocked = v))
            {
                bool confirmed = GameRulesAccess.Confirmed;
                try
                {
                    GameRulesAccess.Reset();
                    Settings.SelectCustomExperience(true);
                    Settings.SelectCustomExperience(false);
                    var view = new OptionsView(() => { }, false);
                    view.SelectTab(5);
                    view.Activate(view.DefaultsButton);
                    Assert.IsTrue(view.ResetPromptOpen);
                    Assert.IsFalse(view.RulesPromptOpen);
                    view.Activate(view.ResetAllButton);
                    view.RefreshTexts();
                    Assert.IsFalse(GameRulesAccess.Confirmed, "reset does not grant access to custom rules");
                    Assert.IsFalse(view.RulesPromptOpen);
                    Assert.AreEqual(1, Settings.CurrentScopedExperience(true));
                    Assert.AreEqual(1, Settings.CurrentScopedExperience(false));
                }
                finally { if (confirmed) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); }
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void DefaultsSelectRemakeAndDisableToolsWithoutChangingDifficulty(bool allTabs)
        {
            using (new DataUiTestKit.PrefsSnapshot())
            using (new SettingsScope<bool>(() => Cheats.Unlocked, v => Cheats.Unlocked = v))
            using (new SettingsScope<float>(() => Session.Difficulty, v => Session.Difficulty = v))
            {
                Session.Difficulty = Session.DifficultyExtreme;
                Settings.ApplyScopedExperience(false, 0);
                Settings.ShowStoryStep = true;
                Settings.SpeedrunTimer = true;
                Settings.SpeedrunLiveSplit = true;
                Cheats.Unlocked = true;
                if (allTabs) Settings.ResetToDefaults();
                else OptionsCatalog.ResetPage(OptionPage.Gameplay);
                Assert.AreEqual(Session.DifficultyExtreme, Session.Difficulty);
                Assert.AreEqual(1, Settings.CurrentScopedExperience(false));
                Assert.IsFalse(Settings.PirateEvents);
                Assert.IsFalse(Settings.ShowStoryStep);
                Assert.IsFalse(Settings.SpeedrunTimer);
                Assert.IsFalse(Settings.SpeedrunLiveSplit);
                Assert.IsFalse(Cheats.Unlocked);
                if (allTabs) Assert.AreEqual(1, Settings.CurrentScopedExperience(true));
            }
        }
        [TestCase("rebuiltPlanets", true, 0f, 1f)]
        [TestCase("cameraShake", true, 1f, 0.3f)]
        [TestCase("remasteredEffects", true, 0f, 1f)]
        [TestCase("turretLeadAim", false, 0f, 1f)]
        [TestCase("remakeFreeLook", false, 0f, 1f)]
        public void UpstreamOptionsRemainInTheirPresetScope(string key, bool cosmetics, float original, float remake)
        {
            using var prefs = new DataUiTestKit.PrefsSnapshot();
            for (int choice = 0; choice < 2; choice++)
            {
                Settings.ApplyScopedExperience(cosmetics, choice);
                float expected = choice == 0 ? original : remake;
                Assert.AreEqual(expected, PlayerPrefs.GetFloat(DataUiTestKit.SettingsPrefix + key));
                var option = OptionsCatalog.All().Single(d => d.id == key);
                if (option.setBool != null) option.setBool(expected == 0f);
                else option.set(0f);
                Assert.AreEqual(2, Settings.CurrentScopedExperience(cosmetics), key);
            }
        }

        [Test]
        public void ChorusRemainsSelectableAsCustomWithoutChangingNamedFlightPolicies()
        {
            using var prefs = new DataUiTestKit.PrefsSnapshot();
            var flight = OptionsCatalog.All().Single(d => d.id == "flightStyle");
            Assert.AreEqual(3, flight.choices().Length);
            Assert.IsNull(flight.setBool);
            Settings.ApplyScopedExperience(false, 1);
            flight.setIndex(FlightStyles.Chorus);
            Assert.AreEqual(FlightStyles.Chorus, Settings.FlightStyle);
            Assert.AreEqual(2, Settings.CurrentScopedExperience(false));
            Settings.ApplyScopedExperience(false, 2);
            Assert.AreEqual(FlightStyles.Chorus, Settings.FlightStyle);
            Settings.ApplyScopedExperience(false, 0);
            Assert.AreEqual(FlightStyles.Original, Settings.FlightStyle);
        }

        [TestCase(0)] [TestCase(1)]
        public void AddedFreeUiPreferencePreservesAnExplicitNamedPreset(int choice)
        {
            using var prefs = new DataUiTestKit.PrefsSnapshot();
            Settings.ApplyScopedExperience(true, choice);
            Settings.ApplyScopedExperience(false, choice);
            PlayerPrefs.DeleteKey(DataUiTestKit.SettingsPrefix + "kaamoSpecialUiFeatures");
            PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "experienceDefaults", 10f);
            DataUiTestKit.ClearSettingsCache();
            Settings.MigrateExperienceDefaults();
            Assert.AreEqual(choice == 1, Settings.KaamoSpecialUiFeatures);
            Assert.AreEqual(choice, Settings.CurrentScopedExperience(true));
        }

        [Test]
        public void FreeUiPreferenceDoesNotTurnAFreshNamedPresetIntoCustom()
        {
            using var prefs = new DataUiTestKit.PrefsSnapshot();
            Settings.Reset(DataUiTestKit.SettingFloatKeys.Concat(Settings.LanguageKeys));
            foreach (var key in new[] { "language", "experienceDefaults", "experienceDefaults.played", "welcomeShown" })
                PlayerPrefs.DeleteKey(DataUiTestKit.SettingsPrefix + key);
            DataUiTestKit.ClearSettingsCache();
            Settings.MigrateExperienceDefaults();
            Assert.That(Settings.CurrentScopedExperience(true), Is.InRange(0, 1));
            Assert.That(Settings.CurrentScopedExperience(false), Is.InRange(0, 1));
        }
    }
}
