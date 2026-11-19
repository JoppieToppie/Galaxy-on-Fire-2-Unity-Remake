// StoryTable.cs
// The campaign's step table (Reference/research/campaign_flow.md 4), loaded from Resources/GoF2Data/story.json, which
// Reference/tools/campaign/build_story_json.py writes from the binary's own tables:
//   Status::nextCampaignMission 0x0b6c98   per index: the mission it creates (type, reward, target station, statusValue,
//                                          production goods, visible)
//   DialogueWindow::init / loadContent 0x194734 / 0x194cd0   briefing / success pages: (speaker, text id); speaker name =
//                                          text 1597 + speaker
//   MissionsWindow::init 0x17a604 (DAT_00258f68)   objective text per index ('#' = target station name)
// Side effects of entering a step (items, ships, systems) are code: Story.ApplyStepEffects.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Serializable]
    public class DialoguePage
    {
        public int speaker;
        public int text;
        /// <summary>Voice .ogg name ("" = silent), from Globals::getDialogueSoundId's text -> event table.</summary>
        public string voice = "";
    }

    /// <summary>A scripted radio message (Level::createRadioMessages 0xd0574; triggers in dialogue_cutscenes.md 2.2).</summary>
    [Serializable]
    public class RadioLine
    {
        public int text, speaker, trigger, param, count = 1;
        public string voice = "";
    }

    /// <summary>A story speaker's portrait descriptor {body, part0..part3} (PTR 0x2647ec) and its layers in draw order.</summary>
    [Serializable]
    public class Speaker
    {
        public int[] portrait = { -1, -1, -1, -1, -1 };
        public List<PortraitLayer> layers = new List<PortraitLayer>();
    }

    /// <summary>One portrait part: the texture key ("body_part_variant" file name) and where it sits (anchor 16 top /
    /// 32 bottom at y).</summary>
    [Serializable]
    public class PortraitLayer
    {
        public string key;
        public int anchor, y;
    }

    [Serializable]
    public class PortraitOffset
    {
        public int anchor;   // 16 = top, 32 = bottom
        public int y;
    }

    [Serializable]
    public class PortraitBody
    {
        public List<PortraitOffset> parts = new List<PortraitOffset>();
    }

    [Serializable]
    public class StoryStep
    {
        public int index;
        public int type = -1;
        public int reward;
        public int station = -1;
        public int value;
        public int goodsItem = -1;
        public int goodsAmount;
        public bool visible = true;
        public int objectiveText = -1;
        public List<DialoguePage> briefing = new List<DialoguePage>();
        public List<DialoguePage> success = new List<DialoguePage>();
        public List<RadioLine> radio = new List<RadioLine>();
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class StoryTable
    {
        [Serializable]
        class File
        {
            public List<StoryStep> steps;
            public List<Speaker> speakers;
            public List<PortraitBody> portraitOffsets;
        }

        static File data;

        static File Data
        {
            get
            {
                if (data == null)
                {
                    var text = GoF2Remake.Data.GameContent.Text("GoF2Data/story");
                    data = text != null ? JsonUtility.FromJson<File>(text) : null;
                    if (data == null) { Debug.LogError("StoryTable: GoF2Data/story missing"); data = new File(); }
                    data.steps ??= new List<StoryStep>();
                    data.speakers ??= new List<Speaker>();
                    data.portraitOffsets ??= new List<PortraitBody>();
                }
                return data;
            }
        }

        public static IReadOnlyList<StoryStep> Steps => Data.steps;

        /// <summary>A story speaker's portrait descriptor, null for ids without one (1660+ names, random faces).</summary>
        public static int[] Portrait(int speaker) => speaker >= 0 && speaker < Data.speakers.Count ? Data.speakers[speaker].portrait : null;

        /// <summary>A story speaker's portrait layers (draw order), empty for ids without a portrait.</summary>
        public static List<PortraitLayer> PortraitLayers(int speaker) =>
            speaker >= 0 && speaker < Data.speakers.Count && Data.speakers[speaker].layers != null ? Data.speakers[speaker].layers : new List<PortraitLayer>();

        /// <summary>IMAGE_OFFSETS_IPAD_LARGE: where a body's part sits in the 160x200 portrait.</summary>
        public static PortraitOffset PortraitOffset(int body, int part) =>
            body >= 0 && body < Data.portraitOffsets.Count && part < Data.portraitOffsets[body].parts.Count ? Data.portraitOffsets[body].parts[part] : null;

        /// <summary>The step of a campaign index, null past the table.</summary>
        public static StoryStep Step(int index) => index >= 0 && index < Steps.Count ? Steps[index] : null;

        /// <summary>Speaker name (text 1597 + speaker); remake mods: a character that "replaces" the speaker
        /// (Modding.ModCharacters) and has a name of its own lends it, wherever the speaker is named.</summary>
        public static string SpeakerName(int speaker)
        {
            var r = Modding.ModCharacters.ReplacementFor(speaker);
            return r != null && r.HasName ? r.Name : OriginalSpeakerName(speaker);
        }

        /// <summary>The game's own speaker name (text 1597 + speaker), whatever a mod replaces.</summary>
        public static string OriginalSpeakerName(int speaker) => Localization.Get(1597 + speaker);

        /// <summary>The number of story speakers (multiplayer's /dialog looks them up by name).</summary>
        public static int SpeakerCount => Data.speakers.Count;

        /// <summary>Speakers 19 (Void) and 56 (Corny) talk in the alien font (Globals::fontAlien) in the dialogue window
        /// and the radio (dialogue_cutscenes.md 1.2 / 2.3).</summary>
        public static bool UsesAlienFont(int speaker) => speaker == 19 || speaker == 56;

        /// <summary>Not a character talking: 16 "Info" (the tutorial pages). The animated dialogue option leaves them plain
        /// (TextReveal); 17 "Story" (the narrator) types in like the characters.</summary>
        public static bool IsNarration(int speaker) => speaker == 16;

        /// <summary>The Info pages that only teach the controls or the menus (the steering and lock briefing at 0, leaving the
        /// station 1, mining 2, the autopilot 2, the hangar 5, waypoints / radar colours / fast-forward 7, the lounge 12,
        /// jumpgates 17, the EMP bomb 21). Remake: left out while the tutorial popups are off (Settings.TutorialHints); the
        /// Info pages that carry the story (the way to Kernstal 9, new coordinates 22, the tractor beam vendor 23, the
        /// blueprint and target hints 57 / 58) always show.</summary>
        static readonly HashSet<int> TutorialPages = new HashSet<int> { 1677, 1680, 1718, 1725, 1730, 1746, 1749, 1750, 1753, 1816, 1862, 1878 };

        /// <summary>A conversation's pages as shown: without the controls tutorials while the tutorial popups are off.</summary>
        public static List<DialoguePage> Shown(List<DialoguePage> pages)
        {
            if (pages == null || Settings.TutorialHints) return pages;
            if (!pages.Exists(p => p.speaker == 16 && TutorialPages.Contains(p.text))) return pages;
            return pages.FindAll(p => !(p.speaker == 16 && TutorialPages.Contains(p.text)));
        }
    }
}
