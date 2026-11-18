// StoryDataTests.cs
// CLAUDE.md "Story": story.json (Reference/tools/campaign/build_story_json.py) and what it refers to. The step table
// 0-162 (campaign_flow.md), each page's text in the text table, the speaker's name (text 1597 + speaker), the voice clips
// in Resources/GoF2Story/StoryAssets (English; "<event>_Alt2" for the re-recorded lines, StoryAssets.Voice), the speakers'
// portrait layers (parts "body_part_variant" with their image heights, the background 0x485 and frame 0x511), the
// generic voice table (voice_table.json, Globals::getDialogueSoundId's table 0x255210). Speakers past the portrait table
// have none ("null for ids without one (1660+ names, random faces)", StoryTable.Portrait).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class StoryDataTests : SandboxTest
    {
        static StoryAssets Assets()
        {
            var a = StoryAssets.Load();
            Assert.IsNotNull(a, "Resources/GoF2Story/StoryAssets (GoF2 > Build > Story Assets)");
            return a;
        }

        static IEnumerable<DialoguePage> AllPages() => StoryTable.Steps.SelectMany(s => s.briefing.Concat(s.success));

        [Test]
        public void TheStepTableHasEveryCampaignIndex()
        {
            Assert.AreEqual(163, StoryTable.Steps.Count, "steps 0-162");
            for (int i = 0; i < 163; i++) Assert.AreEqual(i, StoryTable.Step(i).index, "step " + i);
            Assert.IsNull(StoryTable.Step(163));
            Assert.IsNull(StoryTable.Step(-1));
        }

        [Test]
        public void StepsReferToRealStationsAndItems()
        {
            foreach (var s in StoryTable.Steps)
            {
                if (s.station >= 0) Assert.That(s.station, Is.InRange(0, 134), $"step {s.index} station");
                if (s.goodsItem >= 0) Assert.That(s.goodsItem, Is.InRange(0, 232), $"step {s.index} goods");
                if (s.goodsItem >= 0) Assert.GreaterOrEqual(s.goodsAmount, 0, $"step {s.index} goods amount");
                Assert.GreaterOrEqual(s.reward, 0, $"step {s.index} reward");
            }
        }

        [Test]
        public void EveryStoryTextExists()
        {
            using (new DataUiTestKit.TextLanguage("en"))
            {
                int count = Localization.Count;
                foreach (var s in StoryTable.Steps)
                {
                    if (s.objectiveText >= 0) Assert.Less(s.objectiveText, count, $"step {s.index} objective");
                    foreach (var p in s.briefing.Concat(s.success))
                    {
                        Assert.That(p.text, Is.InRange(0, count - 1), $"step {s.index} page");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(Localization.Get(p.text)), $"step {s.index}: text {p.text}");
                    }
                    foreach (var r in s.radio)
                    {
                        Assert.That(r.text, Is.InRange(0, count - 1), $"step {s.index} radio");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(Localization.Get(r.text)), $"step {s.index}: radio text {r.text}");
                    }
                }
            }
        }

        [Test]
        public void EverySpeakerHasAName()
        {
            using (new DataUiTestKit.TextLanguage("en"))
            {
                var used = new HashSet<int>(AllPages().Select(p => p.speaker).Concat(StoryTable.Steps.SelectMany(s => s.radio).Select(r => r.speaker)));
                foreach (int sp in used)
                {
                    Assert.GreaterOrEqual(sp, 0);
                    string name = StoryTable.SpeakerName(sp);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(name) || name.StartsWith("#"), $"speaker {sp}: text {1597 + sp}");
                }
            }
        }

        // (the build's clip, or in a build without the originals the converted copy's: OriginalRefs)
        static bool HasClip(StoryAssets a, string voice) => a.HasEnglishVoice(voice);

        [Test]
        public void EveryStoryVoiceLineHasItsClip()
        {
            var a = Assets();
            Assert.AreEqual(a.voiceNamesEng.Count, a.voiceClipsEng.Count, "English names / clips");
            Assert.AreEqual(a.voiceNamesDeu.Count, a.voiceClipsDeu.Count, "German names / clips");
            var voices = AllPages().Select(p => p.voice).Concat(StoryTable.Steps.SelectMany(s => s.radio).Select(r => r.voice))
                .Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
            Assert.Greater(voices.Count, 1000, "the campaign is voiced");
            var missing = voices.Where(v => !HasClip(a, v)).ToList();
            Assert.IsEmpty(missing, "voice clips missing from StoryAssets");
        }

        [System.Serializable] class VoiceRow { public int text; public string voice; }
        [System.Serializable] class VoiceTable { public List<VoiceRow> items; }

        [Test]
        public void EveryGenericVoiceHasItsClip()
        {
            var a = Assets();
            var table = JsonUtility.FromJson<VoiceTable>(GameContent.Text("GoF2Data/voice_table"));
            Assert.IsNotNull(table?.items);
            Assert.Greater(table.items.Count, 0);
            var missing = table.items.Where(r => !HasClip(a, r.voice)).Select(r => r.voice).ToList();
            Assert.IsEmpty(missing, "voice_table.json lines missing from StoryAssets");
        }

        [Test]
        public void EveryPortraitSpeakerHasItsLayers()
        {
            var a = Assets();
            Assert.IsNotNull(a.portraitBackground, "portrait background 0x485");
            Assert.IsNotNull(a.portraitFrame, "portrait frame 0x511");
            Assert.Greater(StoryTable.SpeakerCount, 0);
            for (int sp = 0; sp < StoryTable.SpeakerCount; sp++)
            {
                var desc = StoryTable.Portrait(sp);
                Assert.AreEqual(5, desc?.Length ?? 0, $"speaker {sp}: {{body, part0..part3}}");
                var layers = StoryTable.PortraitLayers(sp);
                Assert.Greater(layers.Count, 0, $"speaker {sp} has layers");
                foreach (var l in layers)
                {
                    var (tex, height) = a.Part(l.key);
                    Assert.IsNotNull(tex, $"speaker {sp}: part {l.key}");
                    Assert.That(height, Is.InRange(1, tex.height), $"speaker {sp}: part {l.key} height");
                    Assert.IsTrue(l.anchor == 16 || l.anchor == 32, $"speaker {sp}: part {l.key} anchor (16 top / 32 bottom)");
                    StringAssert.StartsWith(desc[0] + "_", l.key, $"speaker {sp}: a part of its body");
                }
                for (int part = 0; part < 4; part++)
                    Assert.IsNotNull(StoryTable.PortraitOffset(desc[0], part), $"speaker {sp}: body {desc[0]} part {part} offset");
            }
            Assert.IsNull(StoryTable.Portrait(StoryTable.SpeakerCount), "past the table: a random face");
            Assert.AreEqual(0, StoryTable.PortraitLayers(-1).Count);
        }

        [Test]
        public void PortraitPartsListsAgree()
        {
            var a = Assets();
            Assert.AreEqual(a.partNames.Count, a.partTextures.Count, "part names / textures");
            Assert.AreEqual(a.partNames.Count, a.partHeights.Count, "part names / heights");
            Assert.AreEqual(a.partNames.Count, a.partNames.Distinct().Count(), "part names unique");
        }

        [Test]
        public void TheCutsceneSoundsAreThere()
        {
            var a = Assets();
            Assert.IsNotNull(a.introAtmo, "143 IntroAtmo");
            Assert.IsNotNull(a.outroSong, "144 OutroSong");
            Assert.IsNotNull(a.voidMusic, "145 Space_NoCombat_Void");
            Assert.IsNotNull(a.voidBattle, "136 Space_Combat_Void");
            Assert.IsNotNull(a.alert, "162 Alert");
            Assert.IsNotNull(a.timeJump, "160 SpaceTimeJump");
            Assert.IsNotNull(a.wormhole, "the wormhole mesh 16994");
            Assert.AreEqual(2, a.menuStatics?.Length ?? 0, "0x37d0 beer, 0x37d1 bra");
            Assert.AreEqual(3, a.voidStationExplosion?.Length ?? 0, "meshes 14285-14287");
        }
    }
}
