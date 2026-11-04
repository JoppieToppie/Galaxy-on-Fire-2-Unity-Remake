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
        static readonly HashSet<string> NotSettings = new HashSet<string> { "difficulty", "debugTools", "steering", "language" };

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
            for (int i = 0; i < bindings.Count; i++) Assert.AreSame(GameControls.Rows[i], bindings[i].control);
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
                        if (o.id == "gameExperience") n = Settings.ExperienceCustom;
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
            new[] { OptionPage.Sound, OptionPage.Graphics, OptionPage.Controls, OptionPage.Gameplay, OptionPage.Accessibility, OptionPage.Language };

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
        public void AndroidStartsOnTheLowPresetAt60FpsWithOriginalCollision()
        {
            try
            {
                Settings.ResetToDefaults();
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
    }
}
