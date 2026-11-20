// OptionsCatalogTests.cs
// CLAUDE.md "UI and platforms" / "Options": OptionsCatalog lists every option (label, kind, range, get / set) for the main
// menu's and the in-game Options pages. Each row's set must be read back by its get (a slider at its ends and middle,
// every choice, both toggle states), and "Default settings" for one tab (ResetPage) must bring back every option shown on
// that tab: CLAUDE.md, "a new option's key goes in its tab's list: the old single list had missed 19 later options". The
// Key bindings tab resets the bindings (GameControls). The original's text ids: Graphics 502, Controls 498, Language 0
// (Reference/research/mainmenu_notes.md); the defaults prompt's tab button "Only <tab>". Every PlayerPrefs value is put
// back afterwards.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using GoF2Remake.Visuals;
using NUnit.Framework;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class OptionsCatalogTests : SandboxTest
    {
        /// <summary>Rows that aren't a Settings value, or that "Default settings" deliberately keeps: the game's difficulty
        /// (Session), the Debug tools (Cheats), the tilt steering choice (calibrates the device when turned on), buttons,
        /// and the text language (Settings.ResetToDefaults keeps it: a Defaults button should not put a player back into a
        /// language they can't read).</summary>
        static readonly HashSet<string> NotSettings = new HashSet<string> { "difficulty", "debugTools", "steering", "language", "cosmeticExperience", "gameplayExperience" };

        DataUiTestKit.PrefsSnapshot prefs;
        DataUiTestKit.TextLanguage english;

        [SetUp]
        public void Save()
        {
            prefs = new DataUiTestKit.PrefsSnapshot();
            english = new DataUiTestKit.TextLanguage("en");
        }

        [TearDown]
        public void Restore()
        {
            english?.Dispose();
            prefs?.Dispose();
        }

        [Test]
        public void CosmeticsAndGameplayUseTheAgreedSubsections()
        {
            var all = OptionsCatalog.All();
            foreach (var id in new[] { "launchCamera", "hangarFlights", "animatedDialogue" })
            {
                var row = all.First(o => o.id == id);
                Assert.AreEqual(OptionPage.Cosmetics, row.page, id);
                Assert.AreEqual(OptionSection.SpecialFeatures, row.section, id);
            }
            foreach (var id in new[] { "inputHints", "shopByCategory" })
                Assert.AreEqual(OptionSection.Interface, all.First(o => o.id == id).section, id);
            foreach (var id in new[] { "hudMode", "hudScale", "hudOpacity", "farShipMarkers", "markerDistance" })
                Assert.AreEqual(OptionSection.Hud, all.First(o => o.id == id).section, id);
            foreach (var id in new[] { "showStoryStep", "speedrunTimer", "speedrunSplits", "speedrunCompare" })
                Assert.AreEqual(OptionSection.Tools, all.First(o => o.id == id).section, id);
            foreach (var id in new[] { "kaamoKeepsEquipment", "autoJumpgate" })
            {
                var row = all.First(o => o.id == id);
                Assert.AreEqual(OptionPage.Gameplay, row.page, id);
                Assert.AreEqual(OptionSection.GameRules, row.section, id);
            }
            // These options are supplied by separate PRs; validate placement when integrated, without requiring them.
            foreach (var row in all.Where(o => o.id == "systemMapAnimationSpeed" || o.id == "hostileAutopilotFastForward"))
            {
                bool cosmetic = row.id == "systemMapAnimationSpeed";
                Assert.AreEqual(cosmetic ? OptionPage.Cosmetics : OptionPage.Gameplay, row.page);
                Assert.AreEqual(cosmetic ? OptionSection.Hud : OptionSection.GameRules, row.section);
            }
            Assert.AreEqual(OptionPage.Accessibility, all.First(o => o.id == "discordPresence").page);
            var rules = all.Where(o => o.section == OptionSection.GameRules).ToList();
            Assert.AreEqual("gameplayExperience", rules[0].id);
            Assert.IsFalse(rules.Any(o => o.id == "debugTools"));
        }

        [Test]
        public void GameRulesShowWithoutAGate()
        {
            // (2026-11-20: the "Expand" button and its consent prompt are gone; the rows show under their groups at once)
            bool before = GameRulesAccess.Confirmed;
            bool debug = Cheats.Unlocked, stacking = Settings.KaamoStacking;
            try
            {
                GameRulesAccess.Reset();
                var view = new OptionsView(() => { }, false);
                for (int i = 0; i < 5; i++) view.StepTab(1);
                Assert.IsFalse(view.RulesPromptOpen);
                Assert.IsTrue(view.NavItems().Any(v => view.RowOf(v)?.def.id == "gameplayExperience"));
                Assert.IsTrue(view.NavItems().Any(v => view.RowOf(v)?.def.id == "difficulty"));
                Assert.IsTrue(view.NavItems().Any(v => view.RowOf(v)?.def.id == "pirateEvents"), "a rule row, without opening anything");
                Assert.IsFalse(view.NavItems().OfType<UnityEngine.UIElements.Button>().Any(b => b.ClassListContains("options-section-gate")));
                Assert.IsFalse(GameRulesAccess.Confirmed, "the Debug pages' own consent is not given by looking");
                Assert.AreEqual(debug, Cheats.Unlocked);
                Assert.AreEqual(stacking, Settings.KaamoStacking);
            }
            finally { if (before) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); }
        }

        static bool IsValue(OptionDef o) => o.kind == OptionKind.Slider || o.kind == OptionKind.Toggle || o.kind == OptionKind.Choice;

        static float Read(OptionDef o) => o.kind switch
        {
            OptionKind.Slider => o.get(),
            OptionKind.Toggle => o.getBool() ? 1f : 0f,
            _ => o.getIndex(),
        };

        /// <summary>Sets another value than 'current'; false when the row has no other (a one-entry choice).</summary>
        static bool Change(OptionDef o, float current)
        {
            switch (o.kind)
            {
                case OptionKind.Slider: o.set(Math.Abs(current - o.max) > 1e-3f ? o.max : o.min); return true;
                case OptionKind.Toggle: o.setBool(current < 0.5f); return true;
                default:
                    int n = o.choices().Length;
                    if (n <= 1) return false;
                    o.setIndex(((int)current + 1) % n);
                    return true;
            }
        }

        [Test]
        public void OptionIdsAreUnique()
        {
            var ids = OptionsCatalog.All().Select(o => o.id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), string.Join(", ", ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key)));
        }

        [Test]
        public void EveryOptionHasADescription()
        {
            // The "?" by every row (OptionInfo) and the line under it with a controller; the key binding rows are named
            // controls, explained by the Reset key bindings row above them.
            var missing = OptionsCatalog.All().Where(o => o.kind != OptionKind.Binding)
                .Where(o => o.description == null || string.IsNullOrWhiteSpace(o.description())).Select(o => o.id).ToList();
            Assert.IsEmpty(missing, string.Join(", ", missing));
        }

        [Test]
        public void EveryRowIsComplete()
        {
            foreach (var o in OptionsCatalog.All())
            {
                Assert.IsNotNull(o.label, o.id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(o.label()), o.id + " label");
                switch (o.kind)
                {
                    case OptionKind.Slider:
                        Assert.IsNotNull(o.get, o.id); Assert.IsNotNull(o.set, o.id); Assert.IsNotNull(o.format, o.id);
                        Assert.Less(o.min, o.max, o.id);
                        Assert.IsFalse(string.IsNullOrEmpty(o.format(o.get())), o.id + " format");
                        break;
                    case OptionKind.Toggle:
                        Assert.IsNotNull(o.getBool, o.id); Assert.IsNotNull(o.setBool, o.id);
                        break;
                    case OptionKind.Choice:
                        Assert.IsNotNull(o.getIndex, o.id); Assert.IsNotNull(o.setIndex, o.id);
                        var c = o.choices();
                        Assert.Greater(c.Length, 0, o.id);
                        Assert.That(o.getIndex(), Is.InRange(0, c.Length - 1), o.id + " index");
                        foreach (var s in c) Assert.IsFalse(string.IsNullOrWhiteSpace(s), o.id + " choice text");
                        break;
                    case OptionKind.Button:
                        Assert.IsNotNull(o.action, o.id);
                        break;
                    case OptionKind.Binding:
                        Assert.IsNotNull(o.control, o.id);
                        Assert.AreEqual(OptionPage.Bindings, o.page, o.id);
                        break;
                }
            }
        }

        [Test]
        public void TheBindingsTabListsEveryFlightControl()
        {
            var rows = OptionsCatalog.All().Where(o => o.page == OptionPage.Bindings).ToList();
            Assert.AreEqual("resetBindings", rows[0].id, "Reset key bindings first");
            var bindings = rows.Where(o => o.kind == OptionKind.Binding).ToList();
            Assert.AreEqual(GameControls.Rows.Count, bindings.Count);
            // (each control once; their order is the tab's categories', SettingsTests.OtherTabsHaveCategories)
            CollectionAssert.AreEquivalent(GameControls.Rows.ToList(), bindings.Select(o => o.control).ToList());
        }

        [Test]
        public void EveryOptionRoundTrips()
        {
            foreach (var o in OptionsCatalog.All())
            {
                if (!IsValue(o) || o.id == "debugTools" || o.id == "steering") continue;
                switch (o.kind)
                {
                    case OptionKind.Slider:
                        foreach (float v in new[] { o.min, o.max, (o.min + o.max) * 0.5f })
                        {
                            float want = o.id == "fov" ? (float)Math.Round(v) : v;   // the field of view row rounds to whole degrees
                            o.set(v);
                            Assert.AreEqual(want, o.get(), 1e-4f, $"{o.id} = {v}");
                        }
                        break;
                    case OptionKind.Toggle:
                        foreach (bool v in new[] { true, false, true })
                        {
                            o.setBool(v);
                            Assert.AreEqual(v, o.getBool(), $"{o.id} = {v}");
                        }
                        break;
                    default:
                        int n = o.choices().Length;
                        if (o.id == "graphicsPreset") n = Settings.PresetCustom;   // Custom only reads back, it sets nothing
                        if (o.id == "cosmeticExperience" || o.id == "gameplayExperience") n = 2;   // Custom only reads back
                        for (int i = 0; i < n; i++)
                        {
                            o.setIndex(i);
                            Assert.AreEqual(i, o.getIndex(), $"{o.id} = choice {i} of {n}");
                        }
                        break;
                }
            }
        }

        static IEnumerable<OptionPage> SettingsPages() =>
            new[] { OptionPage.Sound, OptionPage.Graphics, OptionPage.Controls, OptionPage.Gameplay, OptionPage.Cosmetics, OptionPage.Accessibility, OptionPage.Language };

        [TestCaseSource(nameof(SettingsPages))]
        public void DefaultSettingsForATabResetsEveryOptionOnIt(OptionPage page)
        {
            var rows = OptionsCatalog.All().Where(o => o.page == page && IsValue(o) && !NotSettings.Contains(o.id)).ToList();
            Assert.Greater(rows.Count, 0, page + " has options");
            var missed = new List<string>();
            foreach (var o in rows)
            {
                OptionsCatalog.ResetPage(page);
                float before = Read(o);
                if (!Change(o, before)) continue;
                if (Math.Abs(Read(o) - before) < 1e-4f) continue;   // nothing else to pick here (a device-dependent row)
                OptionsCatalog.ResetPage(page);
                if (Math.Abs(Read(o) - before) > 1e-4f) missed.Add(o.id);
            }
            Assert.IsEmpty(missed, $"options on the {page} tab that its \"Default settings\" doesn't reset (their key is missing from Settings.{page}Keys)");
        }

        [Test]
        public void ResettingOneTabKeepsTheOthers()
        {
            foreach (var page in SettingsPages()) OptionsCatalog.ResetPage(page);
            var all = OptionsCatalog.All().Where(o => IsValue(o) && !NotSettings.Contains(o.id)).ToList();
            foreach (var page in SettingsPages())
            {
                // Change one option on every other tab, reset this tab: the others stay changed.
                var others = all.Where(o => o.page != page).GroupBy(o => o.page).Select(g => g.First()).ToList();
                var changed = new List<(OptionDef o, float v)>();
                foreach (var o in others)
                {
                    float d = Read(o);
                    if (Change(o, d) && Math.Abs(Read(o) - d) > 1e-4f) changed.Add((o, Read(o)));
                }
                OptionsCatalog.ResetPage(page);
                foreach (var (o, v) in changed) Assert.AreEqual(v, Read(o), 1e-4f, $"resetting {page} changed {o.id} ({o.page})");
                foreach (var p in SettingsPages()) OptionsCatalog.ResetPage(p);
            }
        }

        [Test]
        public void TheBindingsTabResetsTheBindings()
        {
            var boost = DataUiTestKit.Row("boost");
            GameControls.Clear(boost, BindSlot.Key1);
            Assert.IsFalse(GameControls.IsBound(boost, BindSlot.Key1));
            OptionsCatalog.ResetPage(OptionPage.Bindings);
            Assert.IsTrue(GameControls.IsBound(boost, BindSlot.Key1));
        }

        [Test]
        public void TheDifficultyRowChangesTheGameInProgress()
        {
            var d = OptionsCatalog.All().FirstOrDefault(o => o.id == "difficulty");
            Assert.IsNotNull(d, "not in a multiplayer session: the Difficulty row");
            Assert.IsTrue(d.inGameOnly, "only in a running game's menus");
            Assert.AreEqual(OptionPage.Gameplay, d.page);
            Assert.AreEqual(4, d.choices().Length, "Easy, Normal, Hard, Extreme (texts 518-520, 25)");
            float[] levels = { 0f, 0.5f, 1f, 1.5f };   // options+0x2c: Easy 0, Normal 0.5, Hard 1, Extreme 1.5
            for (int i = 0; i < 4; i++)
            {
                d.setIndex(i);
                Assert.AreEqual(levels[i], Session.Difficulty, 1e-6f, "choice " + i);
                Assert.AreEqual(i, d.getIndex());
            }
        }

        [Test]
        public void SessionRuleRowsShowInSinglePlayer()
        {
            foreach (var id in new[] { "cloakLosesPursuers", "empBreaksCloak", "pirateEvents", "capitalShips", "kaamoStacking",
                                       "kaamoKeepsEquipment", "cloakBay", "informerRule" })
            {
                var o = OptionsCatalog.All().FirstOrDefault(x => x.id == id);
                Assert.IsNotNull(o, id);
                Assert.AreEqual(OptionPage.Gameplay, o.page, id);
                Assert.IsNotNull(o.visible, id + " hides for a session's client");
                Assert.IsTrue(o.visible(), id + " shows without a session");
            }
        }

        [Test]
        public void OriginalOptionsUseTheOriginalTexts()
        {
            var all = OptionsCatalog.All();
            string Label(string id) => all.First(o => o.id == id).label();
            Assert.AreEqual(Localization.Get(34), Label("music"), "Music 34");
            Assert.AreEqual(Localization.Get(35), Label("sfx"), "FX 35");
            Assert.AreEqual(Localization.Get(36), Label("voice"), "Voice 36");
            Assert.AreEqual(Localization.Get(503), Label("brightness"), "Brightness 503");
            Assert.AreEqual(Localization.Get(499), Label("sensitivity"), "Sensitivity 499");
            CollectionAssert.AreEqual(new[] { Localization.Get(507), Localization.Get(508), Localization.Get(509), "Ultra" },
                all.First(o => o.id == "detailDistance").choices(), "Low / Medium / High 507-509, then Ultra");
            Assert.IsFalse(all.Any(o => o.id == "quality"), "the original's Quality is split into its parts");
            CollectionAssert.AreEqual(new[] { Localization.Get(513), Localization.Get(514), Localization.Get(515) },
                all.First(o => o.id == "brightness").choices(), "Dark / Medium / Bright 513-515");
        }

        [Test]
        public void OnlyTheVolumesHaveASpeaker()
        {
            // OptionDef.volume, not the page: a row built by hand (the photo panel) leaves the page at Sound, its default.
            var volumes = OptionsCatalog.All().Where(o => o.volume).Select(o => o.id).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(new[] { "master", "music", "sfx", "voice" }, volumes);
            Assert.IsFalse(new OptionDef { kind = OptionKind.Slider }.volume, "a slider built by hand has none");
        }

        [Test]
        public void PageTitles()
        {
            Assert.AreEqual(Localization.Get(502), OptionsCatalog.PageTitle(OptionPage.Graphics));
            Assert.AreEqual(Localization.Get(498), OptionsCatalog.PageTitle(OptionPage.Controls));
            Assert.AreEqual(Localization.Get(0), OptionsCatalog.PageTitle(OptionPage.Language));
            Assert.AreEqual("Sound", OptionsCatalog.PageTitle(OptionPage.Sound));
            Assert.AreEqual("Gameplay", OptionsCatalog.PageTitle(OptionPage.Gameplay));
            Assert.AreEqual("Key bindings", OptionsCatalog.PageTitle(OptionPage.Bindings));
            Assert.AreEqual("Only Sound", OptionsCatalog.ResetTabLabel(OptionPage.Sound));
            Assert.AreEqual("Language", Localization.Get(0));
        }

        [Test]
        public void PostProcessAaPicksTheCameraModeAndTaaOnlyWhereUrpRunsIt()
        {
            Settings.ResetToDefaults();
            Assert.AreEqual(Settings.PostAaSmaa, Settings.PostAntialiasing, "the scenes' SMAA by default");
            Settings.PostAntialiasing = 9;
            Assert.AreEqual(Settings.PostAaOff, Settings.PostAntialiasing, "clamped to the last choice");

            static AntialiasingMode M(int o, bool prefersTaa = false, bool upscaler = false, bool msaa = false) =>
                ScreenAntialiasing.Mode(o, prefersTaa, upscaler, msaa);
            Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, M(Settings.PostAaSmaa));
            Assert.AreEqual(AntialiasingMode.FastApproximateAntialiasing, M(Settings.PostAaFxaa));
            Assert.AreEqual(AntialiasingMode.FastApproximateAntialiasing, M(Settings.PostAaFxaa, prefersTaa: true), "the station camera too");
            Assert.AreEqual(AntialiasingMode.None, M(Settings.PostAaOff, prefersTaa: true));
            Assert.AreEqual(AntialiasingMode.TemporalAntiAliasing, M(Settings.PostAaTaa));
            Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, M(Settings.PostAaTaa, msaa: true), "URP's TAA needs MSAA off");
            Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, M(Settings.PostAaTaa, upscaler: true), "a temporal upscaler anti-aliases");
            Assert.AreEqual(AntialiasingMode.TemporalAntiAliasing, M(Settings.PostAaSmaa, prefersTaa: true), "the station camera under SMAA");
            Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, M(Settings.PostAaSmaa, prefersTaa: true, msaa: true));
        }

        [Test]
        public void TheGraphicsPresetsSetTheirOptionsAndAChangeShowsCustom()
        {
            Settings.ResetToDefaults();
            Assert.AreEqual(Settings.PresetHigh, OptionsCatalog.CurrentPreset(), "a fresh desktop install shows High (its defaults)");
            Settings.ApplyPreset(Settings.PresetLow);
            Assert.AreEqual(Settings.PresetLow, OptionsCatalog.CurrentPreset());
            Assert.IsFalse(Settings.FireSmoke, "the original's Quality Low: smoke off");
            Assert.IsFalse(Settings.Fog, "and fog off");
            Assert.AreEqual(0, Settings.DetailDistance);
            Assert.AreEqual(Settings.BloomOff, Settings.BloomStyle);
            Assert.AreEqual(Settings.HangarShadowsOff, Settings.HangarShadows);
            Assert.IsFalse(Settings.NpcPlayerEngines);
            Assert.AreEqual(0.67f, Settings.RenderScale, 1e-4f);
            Assert.AreEqual(1, Settings.TextureQuality, "half-size textures");
            Assert.AreEqual(Settings.PostAaFxaa, Settings.PostAntialiasing, "Low: FXAA, the cheapest post-process AA");
            Settings.ApplyPreset(Settings.PresetMedium);
            Assert.AreEqual(Settings.PostAaSmaa, Settings.PostAntialiasing);
            Assert.AreEqual(Settings.PresetMedium, OptionsCatalog.CurrentPreset());
            Settings.LensFlare = false;
            Assert.AreEqual(Settings.PresetCustom, OptionsCatalog.CurrentPreset(), "one option changed: Custom");
            Settings.ApplyPreset(Settings.PresetCustom);
            Assert.IsFalse(Settings.LensFlare, "picking Custom changes nothing");
            Settings.ApplyPreset(Settings.PresetHigh);
            Assert.AreEqual(Settings.PresetHigh, OptionsCatalog.CurrentPreset());
            Assert.IsTrue(Settings.FireSmoke && Settings.Fog);
            Assert.AreEqual(2, Settings.DetailDistance);
            Settings.ApplyPreset(Settings.PresetUltra);
            Assert.AreEqual(Settings.PresetUltra, OptionsCatalog.CurrentPreset());
            Assert.AreEqual(1.5f, Settings.RenderScale, 1e-4f);
            Assert.AreEqual(8, Settings.Msaa);
            Assert.AreEqual(Settings.HangarShadowsAll, Settings.HangarShadows);
            Assert.IsTrue(Settings.HangarDepthOfField);
            Assert.IsFalse(Settings.Lods, "every model at full detail");
            Assert.AreEqual(3, Settings.DetailDistance);
            Assert.AreEqual(3, Settings.TextureFiltering, "16x");
            Assert.AreEqual(0, Settings.TextureQuality);
        }

        [Test]
        public void ThePresetRowOffersUltraBeforeCustom()
        {
            var row = OptionsCatalog.All().First(o => o.id == "graphicsPreset");
            var choices = row.choices();
            Assert.AreEqual("Ultra", choices[Settings.PresetUltra]);
            Assert.AreEqual(Settings.PresetCustom, choices.Length - 1, "Custom last");
        }

        [Test]
        public void AnOldQualitySettingCarriesOverIntoItsParts()
        {
            Settings.ResetToDefaults();
            UnityEngine.PlayerPrefs.SetFloat("gof2.quality", 1f);
            DataUiTestKit.ClearSettingsCache();
            Assert.IsFalse(Settings.FireSmoke, "Medium: smoke off");
            Assert.IsFalse(Settings.Fog, "fog off");
            Assert.AreEqual(1, Settings.DetailDistance, "detail Medium");
            Settings.Fog = true;
            Assert.IsTrue(Settings.Fog, "the new option wins once set");
            Settings.ResetToDefaults();
            Assert.IsTrue(Settings.FireSmoke && Settings.Fog, "Default settings clears the old one too");
            Assert.AreEqual(2, Settings.DetailDistance);
        }

        [Test]
        public void AndroidFallbackUsesLowGraphicsAt60FpsWithOriginalCollision()
        {
            try
            {
                Settings.ResetToDefaults();
                Settings.Reset(Settings.GameplayKeys); // test fresh preference fallbacks, without an explicit Remake selection
                Settings.OverrideAndroidDefaults(true);
                Assert.AreEqual(Settings.PresetLow, OptionsCatalog.CurrentPreset(), "every preset option at Low");
                Assert.AreEqual(FrameRate.Fps60, Settings.FrameRate);
                Assert.IsFalse(Settings.NpcPlayerEngines, "the other ships' engines the original's");
                Assert.AreEqual(SessionRules.CollisionOriginal, Settings.ShipCollision, "no hull meshes on phones");
                Settings.OverrideAndroidDefaults(false);
                Assert.AreEqual(SessionRules.CollisionOriginal, Settings.ShipCollision, "the Original experience everywhere");
                Assert.AreEqual(FrameRate.VSync, Settings.FrameRate, "elsewhere V-Sync");
                Assert.IsTrue(Settings.NpcPlayerEngines);
            }
            finally { Settings.OverrideAndroidDefaults(null); }
        }

        [Test]
        public void IosStartsOnTheMediumPresetAt60Fps()
        {
            try
            {
                Settings.ResetToDefaults();
                Settings.OverrideAndroidDefaults(false);
                Settings.OverrideIosDefaults(true);
                Assert.AreEqual(Settings.PresetMedium, OptionsCatalog.CurrentPreset(), "every preset option at Medium");
                Assert.AreEqual(FrameRate.Fps60, Settings.FrameRate, "not the 120 Hz screens' 120");
                Assert.AreEqual(0.8f, Settings.RenderScale, 1e-4f);
                Assert.AreEqual(Settings.UpscalerMetalFxSpatial, Settings.Upscaler, "Apple's own upscaler, not FSR 1");
                Assert.AreEqual(Settings.PostAaFxaa, Settings.PostAntialiasing, "FXAA's one pass for SMAA's three");
                Assert.IsFalse(Settings.FireSmoke);
                Settings.ApplyPreset(Settings.PresetLow);
                Assert.AreEqual(Settings.UpscalerMetalFxSpatial, Settings.Upscaler, "Low too");
                Assert.AreEqual(Settings.PresetLow, OptionsCatalog.CurrentPreset(), "and the row still reads Low");
                Settings.ResetToDefaults();
                Settings.FrameRate = FrameRate.Fps120;
                Assert.AreEqual(FrameRate.Fps120, Settings.FrameRate, "a player's choice stays theirs");
                Settings.OverrideIosDefaults(false);
                Settings.ResetToDefaults();
                Assert.AreEqual(Settings.PresetHigh, OptionsCatalog.CurrentPreset(), "elsewhere the desktop's");
            }
            finally { Settings.OverrideIosDefaults(null); Settings.OverrideAndroidDefaults(null); Settings.ResetToDefaults(); }
        }

        [Test]
        public void AFoldablesInnerScreenStaysAPhone()
        {
            Assert.Greater(UiLayout.PhoneMaxInches, 7.6f, "the iPhone Duo's inner screen");
            Assert.Greater(UiLayout.PhoneMaxInches, 8.0f, "the largest Fold-type inner screens");
            Assert.Less(UiLayout.PhoneMaxInches, 8.3f, "an iPad mini stays a desktop layout");
        }

        public void InterfaceStyleBelongsToCosmeticsUiAndItsReset()
        {
            var row = OptionsCatalog.All().Single(o => o.id == "originalUi");
            Assert.AreEqual(OptionPage.Cosmetics, row.page);
            Assert.AreEqual(OptionSection.Interface, row.section);
            StringAssert.Contains("manual Custom option", row.description());
            StringAssert.Contains("Cosmetic Experience", row.description());
            row.setIndex(1);
            Settings.Reset(Settings.GraphicsKeys);
            Assert.IsTrue(Settings.OriginalUi, "Graphics reset does not change the interface style");
            Settings.Reset(Settings.CosmeticsKeys);
            Assert.IsFalse(Settings.OriginalUi, "Cosmetics owns the reset");
        }

        [Test]
        public void NamedCosmeticPresetsSelectRemakeUiAndManualStyleSelectsCustom()
        {
            Settings.Reset(new[] { "originalUi" });
            Settings.ApplyExperience(Settings.ExperienceOriginal);
            Settings.ApplyScopedExperience(true, 0);
            Settings.ApplyScopedExperience(false, 0);
            Assert.IsFalse(Settings.OriginalUi, "Original gameplay never forces the old interface");
            Settings.OriginalUi = true;
            Assert.AreEqual(2, Settings.CurrentScopedExperience(true));
            Settings.ApplyScopedExperience(false, 1);
            Assert.IsTrue(Settings.OriginalUi, "Gameplay leaves cosmetic choices alone");
            Settings.ApplyExperience(Settings.ExperienceOriginal);
            Settings.ApplyScopedExperience(false, 1);
            Assert.IsFalse(Settings.OriginalUi, "named Cosmetics reset every cosmetic parameter; legacy UI is Custom-only");
        }

        [TestCase(false)] [TestCase(true)]
        public void GameplayCustomSelectsWithoutAPrompt(bool alreadyCustom)
        {
            bool confirmed = GameRulesAccess.Confirmed;
            try
            {
                GameRulesAccess.Reset();
                Settings.ApplyScopedExperience(false, 1);
                if (alreadyCustom) Settings.KaamoVipMode = Settings.KaamoVipOff;
                int vip = Settings.KaamoVipMode;
                bool pirates = Settings.PirateEvents;
                var view = new OptionsView(() => { }, false);
                for (int i = 0; i < 5; i++) view.StepTab(1);
                var field = view.NavItems().Single(e => view.RowOf(e)?.def.id == "gameplayExperience");
                var choice = (ChoiceRow)field;
                Assert.IsTrue(view.NavItems().Any(e => view.RowOf(e)?.def.id == "kaamoVip"), "the rules show before Custom is picked");
                choice.Step(1);
                Assert.IsFalse(view.RulesPromptOpen, "no consent prompt any more");
                Assert.AreEqual(2, view.RowOf(field).def.getIndex(), "Custom is explicitly Custom");
                Assert.AreEqual(2, ((ChoiceRow)field).Index, "the visible selection is Custom");
                Assert.IsTrue(view.NavItems().Any(e => view.RowOf(e)?.def.id == "kaamoVip"));
                Assert.AreEqual(vip, Settings.KaamoVipMode);
                Assert.AreEqual(pirates, Settings.PirateEvents);
            }
            finally { if (confirmed) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); }
        }

        [Test]
        public void EditingARuleOutsideThePresetTableStillSelectsCustom()
        {
            bool confirmed = GameRulesAccess.Confirmed;
            try
            {
                GameRulesAccess.Accept();
                Settings.ApplyScopedExperience(false, 0);
                var view = new OptionsView(() => { }, false);
                for (int i = 0; i < 5; i++) view.StepTab(1);
                var experience = view.NavItems().Single(e => view.RowOf(e)?.def.id == "gameplayExperience");
                var npc = view.NavItems().Single(e => view.RowOf(e)?.def.id == "npcDogfight");
                Assert.AreEqual(0, view.RowOf(experience).def.getIndex());
                view.RowOf(npc).Step(1);
                Assert.AreEqual(2, view.RowOf(experience).def.getIndex());
                Assert.AreEqual(2, ((ChoiceRow)experience).Index, "the visible row refreshes too");
                Settings.ApplyScopedExperience(false, 1);
                Assert.AreEqual(1, view.RowOf(experience).def.getIndex(), "an explicit preset clears Custom mode");
                Settings.ApplyScopedExperience(true, 0);
                Assert.AreEqual(1, view.RowOf(experience).def.getIndex(), "cosmetics do not change gameplay mode");
            }
            finally { if (confirmed) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); }
        }

        [TestCase(false, 0)] [TestCase(false, 1)] [TestCase(true, 0)] [TestCase(true, 1)]
        public void ManualChangesStayCustomEvenWhenValuesReturnToThePreset(bool cosmetics, int preset)
        {
            Settings.ApplyScopedExperience(cosmetics, preset);
            Assert.AreEqual(preset, Settings.CurrentScopedExperience(cosmetics));
            if (cosmetics)
            {
                bool original = Settings.HangarFlights;
                Settings.HangarFlights = !original;
                Settings.HangarFlights = original;
            }
            else
            {
                bool original = Settings.PirateEvents;
                Settings.PirateEvents = !original;
                Settings.PirateEvents = original;
            }
            Assert.AreEqual(2, Settings.CurrentScopedExperience(cosmetics), "matching values cannot reactivate a preset");
            DataUiTestKit.ClearSettingsCache();
            Assert.AreEqual(2, Settings.CurrentScopedExperience(cosmetics), "Custom survives reloading settings");
            Settings.ApplyScopedExperience(cosmetics, 0);
            Assert.AreEqual(0, Settings.CurrentScopedExperience(cosmetics), "explicit selection reactivates Original");
            Settings.ApplyScopedExperience(cosmetics, 1);
            Assert.AreEqual(1, Settings.CurrentScopedExperience(cosmetics), "explicit selection activates Remake");
            Settings.ApplyScopedExperience(cosmetics, 2);
            Assert.AreEqual(2, Settings.CurrentScopedExperience(cosmetics), "explicit Custom preserves values but changes mode");
        }

        [TestCase("Calibri - Regular|Klavika - Regular|Klavika - Medium", "system:Klavika - Medium|system:Klavika - Regular|system:Calibri - Regular")]
        [TestCase("Calibri - Regular", "system:Calibri - Regular")]
        [TestCase("Klavika - Normal|Calibri - Regular", "system:Klavika - Normal|system:Calibri - Regular")]
        [TestCase("klavika - medium|CALIBRI - Regular", "system:klavika - medium|system:CALIBRI - Regular")]
        [TestCase("Arial - Regular|Klavika Condensed - Regular|Calibri - Bold", "")]
        public void AutomaticFontCandidatesFollowFamilyAndBodyWeightPriority(string installed, string expected)
        {
            var method = typeof(GoF2Remake.UI.UiFont).GetMethod("DefaultSystemKeys",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var keys = (IEnumerable<string>)method.Invoke(null, new object[] { installed.Split('|') });
            Assert.AreEqual(expected, string.Join("|", keys));
        }

        [Test]
        public void AutomaticFontFallsBackToThemeWhenPreferredFamiliesAreAbsent()
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(GoF2Remake.UI.UiFont);
            var names = type.GetField("systemNames", flags);
            var font = type.GetField("automaticFont", flags);
            var family = type.GetField("automaticFamily", flags);
            object oldNames = names.GetValue(null), oldFont = font.GetValue(null), oldFamily = family.GetValue(null);
            try
            {
                names.SetValue(null, new[] { "Arial - Regular" });
                font.SetValue(null, null);
                family.SetValue(null, null);
                Settings.UiFont = "";
                Assert.IsNull(GoF2Remake.UI.UiFont.Current, "null inherits bundled Inter");
                Assert.AreEqual("Inter", GoF2Remake.UI.UiFont.DefaultName);
                Assert.IsNull(GoF2Remake.UI.UiFont.Problem);
                Assert.AreEqual("", Settings.UiFont, "automatic resolution does not persist a device-specific font");
            }
            finally
            {
                names.SetValue(null, oldNames);
                font.SetValue(null, oldFont);
                family.SetValue(null, oldFamily);
            }
        }

        [Test]
        public void AutomaticFontLabelMatchesItsResolvedFamilyAndKeepsTheAutomaticChoice()
        {
            Settings.UiFont = "";
            var current = GoF2Remake.UI.UiFont.Current;
            string family = GoF2Remake.UI.UiFont.DefaultName;
            if (current != null) Assert.AreEqual(family, current.faceInfo.familyName);
            else Assert.AreEqual("Inter", family);
            var row = OptionsCatalog.All().Single(d => d.id == "uiFont");
            Assert.AreEqual(family + " (default)", row.choices()[0]);
            Assert.AreEqual(0, row.getIndex());
            Assert.AreEqual("", Settings.UiFont);
            Assert.AreSame(current, GoF2Remake.UI.UiFont.Current, "reuse the resolved asset");
        }

        [Test]
        public void ExplicitMissingFontDoesNotSilentlySelectAnAutomaticSystemFont()
        {
            Settings.UiFont = "file:missing-hotfix-font.ttf";
            Assert.IsNull(GoF2Remake.UI.UiFont.Current);
            Assert.IsNotNull(GoF2Remake.UI.UiFont.Problem);
            Assert.AreEqual("file:missing-hotfix-font.ttf", Settings.UiFont);
        }

        [Test]
        public void KaamoUiDefaultsOnAndResetsOnlyWithCosmetics()
        {
            Settings.Reset(new[] { "kaamoSpecialUiFeatures" });
            var row = OptionsCatalog.All().Single(o => o.id == "kaamoSpecialUiFeatures");
            Assert.AreEqual("Kaamo special UI features", row.label());
            Assert.AreEqual(OptionPage.Cosmetics, row.page);
            Assert.AreEqual(OptionSection.Interface, row.section);
            Assert.IsTrue(row.getBool());
            row.setBool(false);
            Settings.Reset(Settings.GameplayKeys);
            Assert.IsFalse(row.getBool());
            Settings.Reset(Settings.CosmeticsKeys);
            Assert.IsTrue(row.getBool());
            Assert.AreEqual(OptionSection.GameRules, OptionsCatalog.All().Single(o => o.id == "kaamoVip").section);
        }

        [Test]
        public void GameplayPresetsOptIntoVipOnlyForRemake()
        {
            var gameplay = OptionsCatalog.All().Single(o => o.id == "gameplayExperience");
            gameplay.setIndex(1);
            Assert.AreEqual(Settings.KaamoVipCard, Settings.KaamoVipMode);
            bool pirates = Settings.PirateEvents;
            gameplay.setIndex(2);
            Assert.AreEqual(Settings.KaamoVipCard, Settings.KaamoVipMode, "Custom preserves VIP too");
            Assert.AreEqual(pirates, Settings.PirateEvents, "Custom preserves other rules");
            gameplay.setIndex(0);
            Assert.AreEqual(Settings.KaamoVipOff, Settings.KaamoVipMode);
        }

        [Test]
        public void CosmeticPresetsControlKaamoToolsAndStyleWithoutChangingVip()
        {
            Settings.KaamoVipMode = Settings.KaamoVipAlways;
            Settings.OriginalUi = true;
            Settings.ApplyScopedExperience(true, 0);
            Assert.IsFalse(Settings.KaamoSpecialUiFeatures);
            Assert.AreEqual(0, Settings.CurrentScopedExperience(true));
            Assert.IsFalse(Settings.OriginalUi, "legacy style requires a manual Custom override");
            Assert.AreEqual(Settings.KaamoVipAlways, Settings.KaamoVipMode, "cosmetics cannot change paid benefits");
            Settings.KaamoSpecialUiFeatures = true;
            Assert.AreEqual(2, Settings.CurrentScopedExperience(true), "an override is Custom");
            Settings.ApplyScopedExperience(true, 2);
            Assert.IsTrue(Settings.KaamoSpecialUiFeatures, "Custom preserves the override");
            Settings.ApplyScopedExperience(true, 1);
            Assert.IsTrue(Settings.KaamoSpecialUiFeatures);
            Assert.AreEqual(1, Settings.CurrentScopedExperience(true));
            Settings.KaamoSpecialUiFeatures = false;
            Assert.AreEqual(2, Settings.CurrentScopedExperience(true));
            Settings.ApplyScopedExperience(false, 1);
            Assert.IsFalse(Settings.KaamoSpecialUiFeatures, "Gameplay preset cannot change cosmetic tools");
            Assert.IsFalse(Settings.OriginalUi);
        }

    }
}
