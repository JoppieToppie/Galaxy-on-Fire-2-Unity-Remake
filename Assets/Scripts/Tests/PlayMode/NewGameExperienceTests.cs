using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class NewGameExperienceTests : PlayModeTest
    {
        sealed class Preferences : IDisposable
        {
            readonly Dictionary<string, (bool had, float value)> saved = new Dictionary<string, (bool, float)>();
            public Preferences()
            {
                var keys = ExperiencePresets.Cosmetics.Concat(ExperiencePresets.Gameplay).Select(v => v.Key)
                    .Concat(new[] { "cosmeticExperienceCustom", "gameplayExperienceCustom", "welcomeShown" });
                foreach (var key in keys)
                    saved[key] = (PlayerPrefs.HasKey("gof2." + key), PlayerPrefs.GetFloat("gof2." + key));
                Settings.WelcomeShown = true;
            }
            public void Dispose()
            {
                foreach (var pair in saved)
                    if (pair.Value.had) PlayerPrefs.SetFloat("gof2." + pair.Key, pair.Value.value);
                    else PlayerPrefs.DeleteKey("gof2." + pair.Key);
                PlayerPrefs.Save();
                var cache = (Dictionary<string, float>)typeof(Settings).GetField("cache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                cache.Clear();
            }
        }

        [UnityTest]
        public IEnumerator WelcomeChoicesApplyCompleteCosmeticAndGameplayPolicies()
        {
            using (new Preferences())
            {
                yield return Load("MainMenu");
                var menu = Find<MainMenu>();
                foreach (int choice in new[] { Settings.ExperienceOriginal, Settings.ExperienceCosmetic, Settings.ExperienceRemake })
                {
                    Settings.OriginalUi = true;
                    Settings.KaamoSpecialUiFeatures = true;
                    Settings.ShopByCategory = false;
                    Invoke(menu, "ChooseExperience", choice);
                    Assert.AreEqual(choice == Settings.ExperienceOriginal ? 0 : 1, Settings.CurrentScopedExperience(true));
                    Assert.AreEqual(choice == Settings.ExperienceRemake ? 1 : 0, Settings.CurrentScopedExperience(false));
                    Assert.IsFalse(Settings.OriginalUi);
                    Assert.AreEqual(choice != Settings.ExperienceOriginal, Settings.KaamoSpecialUiFeatures);
                    Assert.IsTrue(Settings.WelcomeShown);
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator NewGameKeepsTutorialCapitalAndCosmeticChoices()
        {
            using (new Preferences())
            {
                Settings.ApplyExperience(Settings.ExperienceRemake);
                yield return Load("MainMenu");
                var menu = Find<MainMenu>();
                Invoke(menu, "EnterMenu");
                yield return Run(0.3f);
                Invoke(menu, "PickDifficulty", Session.DifficultyNormal);
                Invoke(menu, "PickEconomy", Economy.Default);
                yield return null;
                var root = Get<VisualElement>(menu, "root");
                var tutorials = root.Q<Button>("tutorialToggle");
                var capital = root.Q<Button>("capitalToggle");
                Assert.IsFalse(tutorials.ClassListContains("choice-button--on"));
                Assert.IsTrue(capital.ClassListContains("choice-button--on"));
                tutorials.Focus();
                using (var submit = NavigationSubmitEvent.GetPooled()) tutorials.SendEvent(submit);
                capital.Focus();
                using (var submit = NavigationSubmitEvent.GetPooled()) capital.SendEvent(submit);
                Assert.IsTrue(Settings.TutorialHints, "the actual new-game tutorial button uses Cosmetics' setting");
                Assert.IsFalse(Settings.CapitalShips, "the actual new-game capital button uses Gameplay's setting");
                Assert.AreEqual(2, Settings.CurrentScopedExperience(true));
                Assert.AreEqual(2, Settings.CurrentScopedExperience(false));
                var before = ExperiencePresets.Cosmetics.Concat(ExperiencePresets.Gameplay)
                    .ToDictionary(v => v.Key, v => PlayerPrefs.GetFloat("gof2." + v.Key));
                Invoke(menu, "BeginGame", Economy.Default);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "Space", 30f, "the new-game prologue");
                yield return Run(1f);
                Assert.AreEqual(Session.DifficultyNormal, Session.Difficulty);
                Assert.IsTrue(Settings.TutorialHints);
                Assert.IsFalse(Settings.CapitalShips);
                foreach (var pair in before) Assert.AreEqual(pair.Value, PlayerPrefs.GetFloat("gof2." + pair.Key), pair.Key);
            }
        }
    }
}
