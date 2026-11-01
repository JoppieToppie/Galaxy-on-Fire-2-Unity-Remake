// ModBuilderViewAudio.cs
// The Mod Builder's Audio kind (part of ModBuilderView), in the Space Lounge where the music plays: the project's audio files
// (Modding/README.md "Music", "Sound effects", "Voice-over"). Left: the three groups. Right, for the group picked: Music
// (a game track replaced: music/<track>.ogg; or a new track by name, for the quests' Music step, systems' and stations'
// music), Sound effects (a game sound replaced, sounds/<name>, from the list in Resources/GoF2ModBuilder/SoundNames.txt; or
// a new sound by name, which the quests' Sound step plays), Voice lines (a new line, voices/<clip> for every language or
// voices/<language>/ for one of the game's text languages, which the quests' conversations name; or one of the game's lines
// replaced, named like it, the story's included: StoryAssets.VoiceNames, Modding.ModVoices.Replacement). The pickers of the
// game's tracks, sounds and lines play each one on ▶ before it is picked (ShowAudioPicker: Modding.GameAudioIndex,
// StoryAssets.OriginalVoice). Each file plays on ▶
// (ModAudio, from the project's folder), is replaced or removed. The game reads them when the project is read again.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        VisualElement audioList, audioPage;
        int audioGroup;
        AudioSource audioPreview;
        string playing;
        Button pickerPlaying;    // the audio picker's ▶ of the clip playing

        static readonly (string key, string english)[] AudioGroups = { ("mbMusic", "Music"), ("mbSounds", "Sound effects"), ("mbVoices", "Voice lines") };

        void BuildAudioPanels(VisualElement left, VisualElement right)
        {
            audioList = ListPanel(left, T("mbAudio", "Audio"));
            audioPage = PagePanel(right);
        }

        void RebuildAudio()
        {
            audioList.Clear();
            if (Project == null) return;
            string[] folders = { ModProject.MusicFolder, ModProject.SoundsFolder, ModProject.VoicesFolder };
            for (int i = 0; i < AudioGroups.Length; i++)
            {
                int n = i;
                int count = Project.AudioFiles(folders[i]).Count
                            + (i == 2 ? LanguageTables.Offered.Sum(l => Project.AudioFiles(ModProject.VoicesFolder + "/" + l.code).Count) : 0);
                ListRow(audioList, T(AudioGroups[i].key, AudioGroups[i].english), string.Format(T("mbFilesN", "{0} files"), count), i == audioGroup,
                        () => { audioGroup = n; RebuildAudio(); BuildAudioPage(); }, null, null);
            }
        }

        void AudioChanged()
        {
            RequestReload(false);
            RebuildAudio();
            BuildAudioPage();
        }

        void BuildAudioPage()
        {
            audioPage.Clear();
            if (Project == null) return;
            switch (audioGroup)
            {
                case 0:
                    Heading(audioPage, T("mbMusic", "Music"));
                    Note(audioPage, T("mbMusicNote", "Named like a game track: it replaces it. Any other name is a new track."));
                    var mrow = Row(audioPage);
                    Act(mrow, T("mbReplaceTrack", "Replace a game track"), () =>
                    {
                        var choices = ModRefs.GameMusic.Select(t => (t, (Func<AudioClip>)(() => GameAudioIndex.Clip(t)), (Action)(() => PickAudio(T("mbPickMusic", "Choose the music file"), path =>
                        { Project.SetAudio(ModProject.MusicFolder, t, path); AudioChanged(); })))).ToList();
                        ShowAudioPicker(T("mbReplaceTrack", "Replace a game track"), choices, Settings.MusicVolume);
                    }, "mbv-add", "mbv-change");
                    Act(mrow, T("mbNewTrack", "+ New track"), () => AskText(T("mbNewTrack", "+ New track"), T("mbTrackName", "Its name (a-z, 0-9, _)"), "", name =>
                    {
                        string id = ModProject.Slug(name);
                        PickAudio(T("mbPickMusic", "Choose the music file"), path => { Project.SetAudio(ModProject.MusicFolder, id, path); AudioChanged(); });
                    }), "mbv-add");
                    FileRows(ModProject.MusicFolder, n => ModRefs.GameMusic.Contains(n, StringComparer.OrdinalIgnoreCase) ? T("mbReplaces", "replaces the game's") : T("mbNewTrackSub", "a new track"));
                    break;
                case 1:
                    Heading(audioPage, T("mbSounds", "Sound effects"));
                    Note(audioPage, T("mbSoundsNote2", "Named like a game sound: it replaces it everywhere. Any other name is a new sound for your missions' Sound step. A weapon's shot is set on its item."));
                    var srow = Row(audioPage);
                    Act(srow, T("mbReplaceSound", "Replace a game sound"), () =>
                    {
                        var choices = new List<(string, Func<AudioClip>, Action)>();
                        string folder = null;
                        foreach (var line in GameSounds())
                        {
                            int slash = line.IndexOf('/');
                            string f = slash > 0 ? line.Substring(0, slash) : "", name = slash > 0 ? line.Substring(slash + 1) : line;
                            if (f != folder) { folder = f; choices.Add((f.Replace('_', ' '), null, null)); }
                            string n = name;
                            choices.Add((n, () => GameAudioIndex.Clip(n), () => PickAudio(T("mbPickSound", "Choose the sound file"), path => { Project.SetAudio(ModProject.SoundsFolder, n, path); AudioChanged(); })));
                        }
                        ShowAudioPicker(T("mbReplaceSound", "Replace a game sound"), choices, Settings.SfxVolume);
                    }, "mbv-add", "mbv-change");
                    Act(srow, T("mbNewSound", "+ New sound"), () => AskText(T("mbNewSound", "+ New sound"), T("mbSoundName", "Its name (a-z, 0-9, _): the missions play it by this name"), "", name =>
                    {
                        string id = ModProject.Slug(name);
                        PickAudio(T("mbPickSound", "Choose the sound file"), path => { Project.SetAudio(ModProject.SoundsFolder, id, path); AudioChanged(); });
                    }), "mbv-add");
                    FileRows(ModProject.SoundsFolder, n => IsGameSound(n) ? T("mbReplaces", "replaces the game's") : T("mbNewSoundSub", "a new sound"));
                    break;
                default:
                    Heading(audioPage, T("mbVoices", "Voice lines"));
                    Note(audioPage, T("mbVoicesNote3", "Lines for your missions' conversations and radio, for every language or one: a player hears the one in their text language, else the one for every language. Named like one of the game's lines, a file replaces that line everywhere, the story's included."));
                    var vrow = Row(audioPage);
                    Act(vrow, T("mbNewVoice", "+ Voice line"), () => PickVoiceLanguage(T("mbNewVoice", "+ Voice line"), NewVoice), "mbv-add");
                    Act(vrow, T("mbReplaceVoice", "Replace a game line"), () => PickVoiceLanguage(T("mbReplaceVoice", "Replace a game line"), ReplaceVoice), "mbv-add", "mbv-change");
                    Caption(audioPage, T("mbEveryLanguage", "Every language") + " (voices)");
                    FileRows(ModProject.VoicesFolder, n => UsedBy(n));
                    foreach (var (code, name) in LanguageTables.Offered)
                        if (Project.AudioFiles(ModProject.VoicesFolder + "/" + code).Count > 0)
                        {
                            Caption(audioPage, $"{name} (voices/{code})");
                            FileRows(ModProject.VoicesFolder + "/" + code, UsedBy);
                        }
                    break;
            }
        }

        void NewVoice(string folder) => AskText(T("mbNewVoice", "+ Voice line"), T("mbClipName", "The line's name (a-z, 0-9, _)"), "", name =>
        {
            string id = ModProject.Slug(name);
            PickAudio(T("mbPickVoice", "Choose the voice line"), path => { Project.SetAudio(folder, id, path); AudioChanged(); });
        });

        /// <summary>The language a voice line is for: every language (voices/) or one of the game's text languages
        /// (voices/<code>/).</summary>
        void PickVoiceLanguage(string title, Action<string> folder)
        {
            var choices = new List<(string, Texture2D, Action)> { (T("mbEveryLanguage", "Every language"), LanguageFlags.Of("*"), () => folder(ModProject.VoicesFolder)) };
            foreach (var (code, name) in LanguageTables.Offered) { string c = code; choices.Add((name, LanguageFlags.Of(code), () => folder(ModProject.VoicesFolder + "/" + c))); }
            ShowPicker(title, choices);
        }

        /// <summary>One of the game's voice lines replaced: picked from its lines by event name (StoryAssets.VoiceNames), the
        /// file stored under that name in the language's folder.</summary>
        void ReplaceVoice(string folder)
        {
            var assets = StoryAssets.Load();
            var names = assets?.VoiceNames() ?? new List<string>();
            // The recording heard: the German one for voices/de, else the English one (the other languages have none).
            bool german = folder.EndsWith("/de") || (folder == ModProject.VoicesFolder && Settings.GermanVoices);
            var choices = names.Select(n => (n, (Func<AudioClip>)(() => assets?.OriginalVoice(n, german) ?? assets?.OriginalVoice(n, !german)), (Action)(() =>
                PickAudio(T("mbPickVoice", "Choose the voice line"), path => { Project.SetAudio(folder, n, path); AudioChanged(); })))).ToList();
            ShowAudioPicker(T("mbReplaceVoice", "Replace a game line"), choices, Settings.VoiceVolume);
        }

        /// <summary>A picker of the game's audio as rows: ▶ plays the clip (again: stops it), the name picks it; a choice
        /// without an action is a section's heading. Searched by name; the preview stops when the picker closes.</summary>
        void ShowAudioPicker(string title, List<(string name, Func<AudioClip> clip, Action pick)> choices, float volume)
        {
            StopAudio();
            modal.Clear();
            var box = Add(modal, new VisualElement(), "mbv-modal-box", "mbv-modal-box--list", "mbv-panel");
            Heading(box, title);
            Note(box, T("mbAudioPickerNote", "▶ plays it; the name picks it."));
            var field = Add(box, new TextField { maxLength = 60 }, "mbv-field", "mbv-search");
            field.textEdition.placeholder = T("mbSearch", "Search");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mbv-picker-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(scroll);
            var list = Add(scroll, new VisualElement(), "mbv-audio-list");
            void Fill(string filter)
            {
                list.Clear();
                pickerPlaying = null;
                var words = (filter ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string heading = null;
                int shown = 0;
                foreach (var (name, clip, pick) in choices)
                {
                    if (pick == null) { heading = name; continue; }
                    if (words.Length > 0 && !words.All(w => name.ToLowerInvariant().Contains(w))) continue;
                    if (shown >= 400) break;   // the voice lines run to thousands: a search narrows them
                    if (heading != null) { Add(list, new Label(heading.ToUpperInvariant()), "mbv-picker-heading", "gof-semibold"); heading = null; }
                    shown++;
                    var row = Row(list, "mbv-audio-row");
                    string key = "game:" + name;
                    Button play = null;
                    play = Act(row, playing == key ? "■" : "▶", () => PlayGameClip(key, clip, play, volume), "mbv-step");
                    if (playing == key) pickerPlaying = play;
                    var b = Act(row, name, () => { StopAudio(); CloseModal(); pick(); }, "mbv-menu-item", "mbv-audio-name");
                    b.tooltip = name;
                }
                if (shown == 0) Note(list, T("mbNothingFound", "Nothing matches."));
            }
            field.RegisterValueChangedCallback(e => Fill(e.newValue));
            field.schedule.Execute(() => field.Focus());
            box.Add(scroll);
            Fill(null);
            Act(box, T("mbCancel", "Cancel"), () => { StopAudio(); CloseModal(); });
            modal.AddToClassList("mbv-modal--shown");
        }

        /// <summary>The audio picker's ▶: plays the game's clip, or stops it when it is playing.</summary>
        void PlayGameClip(string key, Func<AudioClip> get, Button button, float volume)
        {
            bool same = playing == key;
            StopAudio();
            if (same) return;
            var clip = get?.Invoke();
            if (clip == null) { Status(T("mbNoPreview", "There is no recording of it to play."), true); return; }
            EnsurePreviewSource();
            playing = key;
            audioPreview.clip = clip;
            audioPreview.volume = volume;
            audioPreview.Play();
            level.MusicHeld = true;
            pickerPlaying = button;
            button.text = "■";
        }

        void EnsurePreviewSource()
        {
            if (audioPreview != null) return;
            var go = new GameObject("Mod Builder audio preview");
            audioPreview = go.AddComponent<AudioSource>();
            audioPreview.ignoreListenerPause = true;
            audioPreview.spatialBlend = 0f;
        }

        bool IsGameSound(string name) => GameSounds().Any(l => l.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) || string.Equals(l, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The quests that name a voice line, or that it replaces one of the game's.</summary>
        string UsedBy(string clip)
        {
            if (StoryAssets.Load()?.IsVoice(clip) == true) return T("mbReplacesLine", "replaces the game's line");
            var users = Project.Quests.Where(q => q.ToJson().ToString(Newtonsoft.Json.Formatting.None).Contains("\"voice\":\"" + clip + "\"")).Select(q => q.Title ?? q.Name).ToList();
            return users.Count == 0 ? T("mbNotUsed", "not used by a mission") : string.Join(", ", users);
        }

        void FileRows(string folder, Func<string, string> sub)
        {
            var files = Project.AudioFiles(folder);
            if (files.Count == 0) { Note(audioPage, T("mbNoFiles", "No files yet.")); return; }
            foreach (var (name, path) in files)
            {
                string n = name, p = path;
                var row = Row(audioPage);
                var play = Act(row, playing == p ? "■" : "▶", () => TogglePreview(p), "mbv-step");
                Add(row, new Label($"{n}  ·  {sub(n)}"), "mbv-inline-label");
                Act(row, T("mbReplaceFile", "Replace…"), () => PickAudio(T("mbPickAudio", "Choose the file"), src => { StopAudio(); Project.SetAudio(folder, n, src); AudioChanged(); }), "mbv-step");
                Act(row, "×", () => Confirm(T("mbRemoveFile", "Remove file"), string.Format(T("mbRemoveText", "Remove \"{0}\" from the project?"), n), () =>
                {
                    StopAudio();
                    Project.RemoveAudio(folder, n);
                    AudioChanged();
                }), "mbv-step");
            }
        }

        List<string> gameSounds;

        List<string> GameSounds()
        {
            if (gameSounds != null) return gameSounds;
            var asset = Resources.Load<TextAsset>("GoF2ModBuilder/SoundNames");
            gameSounds = asset == null ? new List<string>()
                : asset.text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            return gameSounds;
        }

        void TogglePreview(string path)
        {
            if (playing == path) { StopAudio(); BuildAudioPage(); return; }
            StopAudio();
            var mod = ModManager.BuilderProject;
            if (mod == null) return;
            playing = path;
            ModAudio.Load(mod, path, clip =>
            {
                if (playing != path || clip == null) { if (clip == null) Status(T("mbAudioUnreadable", "The file couldn't be played."), true); return; }
                EnsurePreviewSource();
                level.MusicHeld = true;
                audioPreview.clip = clip;
                audioPreview.volume = path.StartsWith(ModProject.MusicFolder) ? Settings.MusicVolume : path.StartsWith(ModProject.VoicesFolder) ? Settings.VoiceVolume : Settings.SfxVolume;
                audioPreview.Play();
            });
            BuildAudioPage();
        }

        /// <summary>Every frame in every kind (the quests' Sound picker plays too): a preview that ended, or whose picker was
        /// closed (Esc, a click outside), stops.</summary>
        void UpdateAudio()
        {
            if (playing == null || audioPreview == null) return;
            if (pickerPlaying != null && !ModalOpen) { StopAudio(); return; }
            if (!audioPreview.isPlaying)   // ended, or its clip went (the file replaced: ModAudio.Sweep)
            {
                bool fromPicker = pickerPlaying != null;
                StopAudio();
                if (!fromPicker && kind == ModBuilder.Kind.Audio) BuildAudioPage();
            }
        }

        void StopAudio()
        {
            playing = null;
            if (level != null) level.MusicHeld = false;
            if (pickerPlaying != null) { pickerPlaying.text = "▶"; pickerPlaying = null; }
            if (audioPreview == null) return;
            audioPreview.Stop();
            audioPreview.clip = null;
            UnityEngine.Object.Destroy(audioPreview.gameObject);
            audioPreview = null;
        }
    }
}
