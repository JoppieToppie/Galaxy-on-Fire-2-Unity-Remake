// Options > Gameplay "Game experience" (Settings.ApplyExperience / CurrentExperience / MigrateExperienceDefaults, 2026-11-04):
// Fresh installs select Remake in both scopes; existing installs keep their options.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class GameExperienceTests : SandboxTest
    {
        DataUiTestKit.PrefsSnapshot prefs;

        [SetUp] public void Save() => prefs = new DataUiTestKit.PrefsSnapshot();
        [TearDown] public void Restore() => prefs?.Dispose();

        static void NothingStored()
        {
            Settings.Reset(DataUiTestKit.SettingFloatKeys.Concat(Settings.LanguageKeys));
            foreach (var k in new[] { "language", "experienceDefaults", "experienceDefaults.played", "welcomeShown" })
                PlayerPrefs.DeleteKey(DataUiTestKit.SettingsPrefix + k);
            DataUiTestKit.ClearSettingsCache();
        }

        [Test]
        public void AFreshInstallSelectsRemakeInBothScopes()
        {
            NothingStored();
            Settings.MigrateExperienceDefaults();
            Assert.IsTrue(Settings.FreshInstall);
            Assert.AreEqual(1, Settings.CurrentScopedExperience(true));
            Assert.AreEqual(1, Settings.CurrentScopedExperience(false));
            Assert.IsTrue(Settings.HangarFlights && Settings.KhadorSlipstream);
            Assert.IsFalse(Settings.FastForwardCruise || Settings.PlayOnInConversations || Settings.KeepEnemyLock);
            Assert.IsFalse(Settings.WelcomeShown);
            Settings.HangarFlights = false;
            Settings.MigrateExperienceDefaults();
            Assert.AreEqual(2, Settings.CurrentScopedExperience(true), "a later startup preserves manual Custom");
            Assert.IsFalse(Settings.HangarFlights);
        }

        [Test]
        public void EachPresetSetsItsOptionsAndReadsBack()
        {
            NothingStored();
            Settings.ApplyExperience(Settings.ExperienceCosmetic);
            Assert.AreEqual(Settings.ExperienceCosmetic, Settings.CurrentExperience());
            Assert.IsTrue(Settings.HangarFlights && Settings.AnimatedDialogue, "the presentation");
            Assert.IsFalse(Settings.PirateEvents || Settings.KhadorSlipstream, "no new gameplay");
            Settings.ApplyExperience(Settings.ExperienceRemake);
            Assert.AreEqual(Settings.ExperienceRemake, Settings.CurrentExperience());
            Assert.IsTrue(Settings.PirateEvents && Settings.CapitalShips && Settings.KhadorSlipstream && Settings.KaamoStacking);
            Assert.IsFalse(Settings.OriginalTargetLock || Settings.InformerOriginalRule);
            Assert.AreEqual(SessionRules.CollisionMixed, Settings.ShipCollision);
            Assert.AreEqual(Settings.KaamoVipCard, Settings.KaamoVipMode);
            Settings.ApplyExperience(Settings.ExperienceCustom);
            Assert.AreEqual(Settings.ExperienceRemake, Settings.CurrentExperience(), "Custom sets nothing");
            Settings.ApplyExperience(Settings.ExperienceOriginal);
            Assert.AreEqual(Settings.ExperienceOriginal, Settings.CurrentExperience());
        }

        [Test]
        public void OneChangedOptionMakesItCustom()
        {
            NothingStored();
            Settings.ApplyExperience(Settings.ExperienceRemake);
            Settings.CapitalShips = false;
            Assert.AreEqual(Settings.ExperienceCustom, Settings.CurrentExperience());
            Settings.CapitalShips = true;
            Assert.AreEqual(Settings.ExperienceCustom, Settings.CurrentExperience(), "manual edits stay Custom until a named preset is selected");
            Settings.ApplyExperience(Settings.ExperienceOriginal);
            Settings.LaunchCamera = false;
            Assert.AreEqual(Settings.ExperienceCustom, Settings.CurrentExperience(), "the original's own features count too");
        }

        [Test]
        public void ThePlayersRowShowsTheMatchAndCustomOnlyReadsBack()
        {
            NothingStored();
            var row = OptionsCatalog.All().First(o => o.id == "cosmeticExperience");
            var gameplay = OptionsCatalog.All().First(o => o.id == "gameplayExperience");
            Assert.AreEqual(3, row.choices().Length, "Original, Remake, Custom");
            Assert.AreEqual(2, row.getIndex(), "free Kaamo tools default on; the Original preset requires them off");
            row.setIndex(1);
            Assert.AreEqual(1, row.getIndex());
            Assert.AreEqual(2, gameplay.getIndex(), "cosmetics do not activate a gameplay preset");
            Assert.IsTrue(Settings.ShopByCategory);
            gameplay.setIndex(1);
            Assert.AreEqual(1, gameplay.getIndex());
            Assert.AreEqual(1, row.getIndex(), "gameplay leaves cosmetics alone");
            OptionsCatalog.All().First(o => o.id == "hangarFlights").setBool(false);
            Assert.AreEqual(2, row.getIndex(), "like the graphics preset");
            Assert.AreEqual(1, gameplay.getIndex());
            row.setIndex(0);
            Assert.IsFalse(Settings.ShopByCategory);
            Assert.AreEqual(1, gameplay.getIndex());
        }

        [Test]
        public void EveryExperienceRowReadsOffAsTheOriginal()
        {
            NothingStored();
            Settings.ApplyExperience(Settings.ExperienceOriginal);
            foreach (var o in OptionsCatalog.All().Where(o => o.page == OptionPage.Gameplay || o.page == OptionPage.Cosmetics))
            {
                if (o.visible != null && !o.visible()) continue;   // the smart lock's own option, hidden with the original's lock
                if (o.kind == OptionKind.Toggle && (Settings.ExperienceKeys.Contains(o.id) || o.id == "informerRule" || o.id == "targetLock"))
                    Assert.IsFalse(o.getBool(), o.id + ": off is the original");
                else if (o.kind == OptionKind.Choice && (o.id == "shipCollision" || o.id == "kaamoVip"))
                    Assert.AreEqual(0, o.getIndex(), o.id + ": the original first");
            }
        }

        /// <summary>An install migrated before an experience option existed (version 1), with 'preset' applied then and
        /// 'change' made after: the new option never stored.</summary>
        static void MigratedBeforeTheOption(int preset, System.Action change = null)
        {
            NothingStored();
            Settings.ApplyExperience(preset);
            change?.Invoke();
            PlayerPrefs.DeleteKey(DataUiTestKit.SettingsPrefix + "bombArming");
            PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "experienceDefaults", 1f);
            DataUiTestKit.ClearSettingsCache();
        }

        [Test]
        public void AnOptionAddedLaterKeepsThePlayersPreset()
        {
            MigratedBeforeTheOption(Settings.ExperienceRemake);
            Assert.AreEqual(Settings.ExperienceCustom, Settings.CurrentExperience(), "the new option at its original default");
            Settings.MigrateExperienceDefaults();
            Assert.IsTrue(Settings.BombArming, "the Remake preset's value");
            Assert.AreEqual(Settings.ExperienceRemake, Settings.CurrentExperience());
            Settings.BombArming = false;
            Settings.MigrateExperienceDefaults();
            Assert.IsFalse(Settings.BombArming, "once only");
        }

        [Test]
        public void AnOptionAddedLaterStaysTheOriginalsForACustomPlayer()
        {
            MigratedBeforeTheOption(Settings.ExperienceRemake, () => Settings.CapitalShips = false);
            Settings.MigrateExperienceDefaults();
            Assert.IsFalse(Settings.BombArming);
            Assert.AreEqual(Settings.ExperienceCustom, Settings.CurrentExperience());
        }

        [Test]
        public void AnInstallPlayedBeforeKeepsItsOptions()
        {
            NothingStored();
            Settings.Language = "en";            // played before: an option was set
            PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "pirateEvents", 0f);   // and one changed by the player
            DataUiTestKit.ClearSettingsCache();
            Settings.MigrateExperienceDefaults();
            Assert.IsFalse(Settings.FreshInstall);
            Assert.IsTrue(Settings.HangarFlights, "the old default kept");
            Assert.IsTrue(Settings.KhadorSlipstream);
            Assert.IsFalse(Settings.OriginalTargetLock, "the smart lock it had");
            Assert.AreEqual(SessionRules.CollisionMixed, Settings.ShipCollision);
            Assert.IsFalse(Settings.PirateEvents, "the player's own choice stays");
            Settings.HangarFlights = false;
            Settings.MigrateExperienceDefaults();
            Assert.IsFalse(Settings.HangarFlights, "once only");
        }
        [Test]
        public void LegacyRemakeMigrationSeedsMissingOptionsWithoutRebalancingStoredRules()
        {
            NothingStored();
            foreach (var key in Settings.ExperienceKeys)
                PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + key, Settings.ExperienceValue(Settings.ExperienceRemake, key));
            PlayerPrefs.DeleteKey(DataUiTestKit.SettingsPrefix + "bombArming");
            PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "experienceDefaults", 1f);
            DataUiTestKit.ClearSettingsCache();
            Settings.MigrateExperienceDefaults();
            Assert.IsTrue(Settings.BombArming);
            Assert.IsTrue(Settings.KeepEnemyLock && Settings.FastForwardCruise && Settings.PlayOnInConversations,
                "the release's historical Remake values must not be overwritten by the new named policy");
            Assert.AreEqual(2, Settings.CurrentScopedExperience(false));
            Settings.ApplyScopedExperience(false, 1);
            Assert.IsFalse(Settings.KeepEnemyLock || Settings.FastForwardCruise || Settings.PlayOnInConversations,
                "only explicit selection applies today's complete Remake policy");
        }
    }
}
