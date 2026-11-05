// Options > Gameplay "Game experience" (Settings.ApplyExperience / CurrentExperience / MigrateExperienceDefaults, 2026-11-04):
// Original by default, Cosmetic and Remake at once, Custom after any one change, and an install played before keeps its options.

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
        public void AFreshInstallPlaysTheOriginal()
        {
            NothingStored();
            Assert.AreEqual(Settings.ExperienceOriginal, Settings.CurrentExperience(), "every option's default is the original's");
            Assert.IsFalse(Settings.HangarFlights);
            Assert.IsFalse(Settings.KhadorSlipstream);
            Assert.IsTrue(Settings.OriginalTargetLock);
            Assert.AreEqual(SessionRules.CollisionOriginal, Settings.ShipCollision);
            Settings.MigrateExperienceDefaults();
            Assert.IsTrue(Settings.FreshInstall);
            Assert.AreEqual(Settings.ExperienceOriginal, Settings.CurrentExperience(), "the migration stores nothing for it");
            Assert.IsFalse(Settings.WelcomeShown);
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
            Assert.AreEqual(Settings.ExperienceRemake, Settings.CurrentExperience());
            Settings.ApplyExperience(Settings.ExperienceOriginal);
            Settings.LaunchCamera = false;
            Assert.AreEqual(Settings.ExperienceCustom, Settings.CurrentExperience(), "the original's own features count too");
        }

        [Test]
        public void ThePlayersRowShowsTheMatchAndCustomOnlyReadsBack()
        {
            NothingStored();
            var row = OptionsCatalog.All().First(o => o.id == "gameExperience");
            Assert.AreEqual(Settings.ExperienceCustom + 1, row.choices().Length, "Original, Cosmetic, Remake, Custom");
            Assert.AreEqual(Settings.ExperienceOriginal, row.getIndex());
            row.setIndex(Settings.ExperienceRemake);
            Assert.AreEqual(Settings.ExperienceRemake, row.getIndex());
            OptionsCatalog.All().First(o => o.id == "hangarFlights").setBool(false);
            Assert.AreEqual(Settings.ExperienceCustom, row.getIndex(), "like the graphics preset");
        }

        [Test]
        public void EveryExperienceRowReadsOffAsTheOriginal()
        {
            NothingStored();
            Settings.ApplyExperience(Settings.ExperienceOriginal);
            foreach (var o in OptionsCatalog.All().Where(o => o.page == OptionPage.Gameplay))
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
    }
}
