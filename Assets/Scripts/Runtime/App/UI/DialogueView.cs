// DialogueView.cs
// The story dialogue window (DialogueWindow, Reference/research/dialogue_cutscenes.md 1), shared by the station menu and
// the flight HUD, which host UI/Dialogue/Dialogue.uxml as a template instance and call Tick every frame:
//   pages          (speaker, text id, voice): portrait (mirrored for Keith, speaker 0) and name (text 1597 + speaker);
//                  the Void (19) and Corny (56) talk in the alien font (AlienText)
//   buttons        Back (179, from page 2), Skip (395, only with more than one page: confirm "Skip the dialogue?" 396 with
//                  Yes 134 / No 135), Next (180) / Close (181) on the last page (DialogueWindow::OnTouchEnd 0x196020)
//   voice          the page's line plays when it opens; with voice on (the remake: voice volume > 0) the window turns the
//                  page when the line has ended plus a pause (the FMOD "pause" property isn't parsed: 500 ms), never past
//                  the last page (DialogueWindow::update 0x195bf0)
//   one-page note  ShowMessage: text only with OK (524) (the DialogueWindow(text, name, portrait) variant)
// Input: keyboard Enter / Space / right = next, Backspace / left = back, Esc = skip; controller A = next, B = back,
// Y / Menu = skip, X = auto; the confirmation takes Enter / A = yes, Esc / B = no. With a controller the buttons carry
// their glyphs (remake: nothing showed that Y skips). The box is a little larger than the original's 694x486 so the
// remake's font reads well.
// Remake: the AUTO toggle in the header (Settings.AutoAdvanceDialogue, the Gameplay option "Auto-play dialogue"): on, a
// voiced page turns when its line has ended (the original's auto-advance), a page without voice after a reading time
// once it has typed in; never past the last page.
// Remake option "Animated dialogue" (TextReveal): the page types in with pacing, shouting and tinted names; Next during
// it shows the page at once, pages read before show at once, the portrait fades in when the speaker changes, the text
// scrolls along with a long page, and the Next button breathes once the page is complete (with AUTO on only Close, on the last page).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class DialogueView : IDialogueWindow
    {
        /// <summary>The newest dialogue window (the flight HUD's or the station menu's): multiplayer's /dialog shows there.</summary>
        public static DialogueView Latest;

        /// <summary>It is on screen (its panel lives: a scene change leaves the old one behind).</summary>
        public bool Usable => root != null && root.panel != null;

        const float VoicePauseMs = 500f;
        /// <summary>Auto-play without a voice line: this long after the page has shown, plus per character.</summary>
        const float ReadBaseMs = 1500f, ReadPerCharMs = 45f, ReadMinMs = 2500f, ReadMaxMs = 12000f;

        readonly VisualElement root, portrait, confirm;
        readonly Label speaker, text, confirmText;
        readonly ScrollView scroll;
        readonly Button back, skip, next, confirmYes, confirmNo, auto;
        readonly Label nextLabel;   // the Next button's text, beside its controller glyph (InputGlyph.WithGlyphs)
        readonly AudioSource voice;
        readonly List<DialogueLine> pages = new List<DialogueLine>();
        Action<bool> closed;
        int page;
        bool message;
        float pauseMs;
        /// <summary>Remake: a conversation that opens while the player is busy (drilling with the arrows, firing with Space)
        /// ignores its keys, buttons and taps this long, so they don't turn its pages unread (Gunant's pirate warning at the
        /// end of step 4's mining was skipped that way).</summary>
        const float InputGraceSeconds = 0.8f;
        float openedAt = -10f;   // Time.unscaledTime (the real clock: every view that shows one runs on it, Tick or not)
        bool InGrace => Time.unscaledTime - openedAt < InputGraceSeconds;
        readonly TextReveal reveal;
        bool narration;
        int seenUpTo = -1;
        string shownSpeaker;
        float portraitMs = float.MaxValue, pulseMs;
        const float PortraitFadeMs = 250f, PulseMs = 700f;

        /// <summary>Button sounds (true = press, false = release), set by the host.</summary>
        public Action<bool> ButtonSound;
        /// <summary>A page shows (its text id): DialogueWindow::loadContent's per-text side effects.</summary>
        public Action<int> PageShown;
        public bool IsOpen => root != null && root.ClassListContains("dialogue-backdrop--shown");

        bool paused;
        /// <summary>A menu over the conversation (the station's system menu): the voice pauses where it is, and the typing,
        /// the input and the voice auto-advance wait (a paused source reads as not playing, which would turn the page).</summary>
        public bool Paused
        {
            get => paused;
            set
            {
                if (paused == value) return;
                paused = value;
                if (voice == null) return;
                if (value) voice.Pause(); else voice.UnPause();
            }
        }

        /// <summary>'container' holds the Dialogue.uxml instance; 'voiceSource' plays the lines (2D, not paused with the game).</summary>
        public DialogueView(VisualElement container, AudioSource voiceSource)
        {
            root = container.Q("dialogueRoot");
            if (root == null) { Debug.LogError("DialogueView: no dialogueRoot (Dialogue.uxml instance) in the UI"); return; }
            Latest = this;
            voice = voiceSource;
            portrait = root.Q("dialoguePortrait");
            speaker = root.Q<Label>("dialogueSpeaker");
            text = root.Q<Label>("dialogueText");
            scroll = root.Q<ScrollView>("dialogueScroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            confirm = root.Q("dialogueConfirm");
            confirmText = root.Q<Label>("dialogueConfirmText");
            back = Bind("dialogueBack", Back);
            skip = Bind("dialogueSkip", AskSkip);
            next = Bind("dialogueNext", Next);
            confirmYes = Bind("dialogueConfirmYes", () => { HideConfirm(); Close(true); });
            confirmNo = Bind("dialogueConfirmNo", HideConfirm);
            back.text = Localization.Get(179).UiCaps();
            skip.text = Localization.Get(395).UiCaps();
            confirmText.text = Localization.Get(396);
            confirmYes.text = Localization.Get(134).UiCaps();
            confirmNo.text = Localization.Get(135).UiCaps();
            // With a controller every button shows its glyph (remake: nothing showed that Y skips).
            PadGlyph(back, PadButton.B);
            PadGlyph(skip, PadButton.Y);
            nextLabel = PadGlyph(next, PadButton.A);
            PadGlyph(confirmYes, PadButton.A);
            PadGlyph(confirmNo, PadButton.B);
            reveal = new TextReveal(text);
            scroll.RegisterCallback<PointerDownEvent>(_ => { if (!InGrace) reveal.Finish(); });   // a tap on the text shows it all
            // The AUTO toggle at the right of the header, a dot that lights while it is on (an X glyph with a controller).
            // A row: the indicator, then the label (the button's own text can't share the row with a child).
            auto = new Button { name = "dialogueAuto" };
            auto.AddToClassList("dialogue-auto");
            var dot = new VisualElement();
            dot.AddToClassList("dialogue-auto-dot");
            auto.Add(dot);
            auto.RegisterCallback<PointerDownEvent>(_ => ButtonSound?.Invoke(true), TrickleDown.TrickleDown);
            auto.clicked += () => { ButtonSound?.Invoke(false); ToggleAuto(); };
            root.Q(className: "dialogue-header")?.Add(auto);
            var autoGlyph = InputGlyph.Pad(PadButton.X);
            autoGlyph.AddToClassList("dialogue-pad-glyph");
            autoGlyph.pickingMode = PickingMode.Ignore;
            auto.Add(autoGlyph);
            var autoLabel = new Label(Localization.Extra("dialogueAuto", "AUTO").OgCase());
            autoLabel.AddToClassList("dialogue-auto-label");
            autoLabel.AddToClassList("gof-semibold");
            autoLabel.pickingMode = PickingMode.Ignore;
            auto.Add(autoLabel);
            RefreshAuto();
        }

        static Label PadGlyph(Button b, PadButton button)
        {
            var g = InputGlyph.Pad(button);
            g.AddToClassList("dialogue-pad-glyph");
            return InputGlyph.WithGlyphs(b, g);
        }

        void ToggleAuto()
        {
            Settings.AutoAdvanceDialogue = !Settings.AutoAdvanceDialogue;
            pauseMs = 0f;
            RefreshAuto();
        }

        void RefreshAuto() => auto?.EnableInClassList("dialogue-auto--on", Settings.AutoAdvanceDialogue);

        Button Bind(string name, Action action)
        {
            var b = root.Q<Button>(name);
            b.RegisterCallback<PointerDownEvent>(_ => ButtonSound?.Invoke(true), TrickleDown.TrickleDown);
            b.clicked += () => { if (InGrace) return; ButtonSound?.Invoke(false); action(); };
            return b;
        }

        /// <summary>A campaign conversation (briefing / success). 'onClosed(skipped)' runs when it closes.</summary>
        public void Show(IList<DialoguePage> story, Action<bool> onClosed)
        {
            var list = new List<DialogueLine>();
            foreach (var p in story) list.Add(DialogueLine.From(p));
            Show(list, onClosed);
        }

        public void Show(List<DialogueLine> list, Action<bool> onClosed)
        {
            if (root == null || list.Count == 0) { onClosed?.Invoke(false); return; }
            pages.Clear();
            pages.AddRange(list);
            closed = onClosed;
            message = false;
            page = 0;
            openedAt = Time.unscaledTime;
            seenUpTo = -1;
            shownSpeaker = null;
            root.AddToClassList("dialogue-backdrop--shown");
            HideConfirm();
            LoadPage();
        }

        /// <summary>The one-page note from a bar agent (DialogueWindow(mission, level, mode)): name, generated portrait, OK.</summary>
        public void ShowAgentMessage(string body, string name, int[] portraitParts, Action onClosed, string voiceLine = null)
        {
            Show(new List<DialogueLine> { new DialogueLine { speaker = -1, text = body, agentName = name, agentPortrait = portraitParts, voice = voiceLine } }, _ => onClosed?.Invoke());
            message = true;
            LoadPage();
        }

        /// <summary>The one-page note: text, a speaker's portrait and name, OK.</summary>
        public void ShowMessage(string body, int speakerId, Action onClosed)
        {
            Show(new List<DialogueLine> { new DialogueLine { speaker = speakerId, text = body } }, _ => onClosed?.Invoke());
            message = true;
            LoadPage();
        }

        void LoadPage()
        {
            var p = pages[page];
            string who = (p.agentName ?? StoryTable.SpeakerName(p.speaker)).UiCaps();
            speaker.text = who;
            bool alien = p.agentName == null && StoryTable.UsesAlienFont(p.speaker);
            AlienText.Set(text, p.text, alien);
            scroll.scrollOffset = Vector2.zero;
            var clip = voice != null ? StoryAssets.Load()?.Voice(p.voice) : null;
            // Only characters talking get the effects; the info / narrator pages stay plain.
            narration = p.agentName == null && StoryTable.IsNarration(p.speaker);
            if (narration) reveal.Clear();
            else reveal.Begin(p.text, alien, clip, instant: page <= seenUpTo, speaker: p.agentName);
            seenUpTo = Mathf.Max(seenUpTo, page);
            // A new speaker's portrait and name fade in (animated dialogue only).
            bool fade = Settings.AnimatedDialogue && !narration && who != shownSpeaker;
            shownSpeaker = who;
            portraitMs = fade ? 0f : float.MaxValue;
            portrait.style.opacity = fade ? 0f : 1f;
            speaker.style.opacity = fade ? 0f : 1f;
            next.RemoveFromClassList("dialogue-button--ready");
            pulseMs = 0f;
            if (p.character != null) Portrait.ShowCharacter(portrait, Modding.ModCharacters.Find(p.character), false);
            else if (p.agentPortrait != null) Portrait.Show(portrait, p.agentPortrait, false);
            else Portrait.ShowSpeaker(portrait, p.speaker, p.speaker == 0);
            bool last = page == pages.Count - 1;
            back.EnableInClassList("dialogue-button--hidden", message || page == 0);
            skip.EnableInClassList("dialogue-button--hidden", message || pages.Count <= 1);
            auto.EnableInClassList("dialogue-button--hidden", message || pages.Count <= 1);
            RefreshAuto();
            nextLabel.text = (message ? Localization.Get(524) : Localization.Get(last ? 181 : 180)).UiCaps();
            pauseMs = 0f;
            PageShown?.Invoke(p.textId);
            if (voice != null)
            {
                voice.Stop();
                if (clip != null)
                {
                    voice.clip = GoF2Remake.Modding.ModSounds.Get(clip);
                    voice.volume = Settings.VoiceVolume;
                    voice.Play();
                }
                else voice.clip = null;
            }
        }

        void Next()
        {
            if (!IsOpen) return;
            if (reveal.Running) { reveal.Finish(); return; }   // first the rest of the page
            if (page >= pages.Count - 1) { Close(false); return; }
            page++;
            LoadPage();
        }

        void Back()
        {
            if (!IsOpen || page == 0) return;
            page--;
            LoadPage();
        }

        void AskSkip()
        {
            if (!IsOpen || message || pages.Count <= 1) return;
            confirm.AddToClassList("dialogue-confirm--shown");
        }

        void HideConfirm() => confirm?.RemoveFromClassList("dialogue-confirm--shown");
        bool ConfirmOpen => confirm != null && confirm.ClassListContains("dialogue-confirm--shown");

        void Close(bool skipped)
        {
            paused = false;
            voice?.Stop();
            root.RemoveFromClassList("dialogue-backdrop--shown");
            var c = closed;
            closed = null;
            c?.Invoke(skipped);
        }

        void TickAnimation(float dtMs)
        {
            reveal.Tick(dtMs);
            if (portraitMs < PortraitFadeMs)
            {
                portraitMs += dtMs;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(portraitMs / PortraitFadeMs));
                portrait.style.opacity = a;
                speaker.style.opacity = a;
            }
            if (reveal.Running)
            {
                // A long page scrolls along with the typing (only ever down, so a reader scrolling back isn't fought).
                float content = scroll.contentContainer.layout.height, view = scroll.contentViewport.layout.height;
                if (content > view && !float.IsNaN(content) && !float.IsNaN(view))
                {
                    float y = (content - view) * Mathf.Clamp01(reveal.Progress * 1.1f - 0.1f);
                    if (y > scroll.scrollOffset.y) scroll.scrollOffset = new Vector2(0f, y);
                }
                return;
            }
            if (!Settings.AnimatedDialogue || narration) return;
            // Not while AUTO turns the pages: the button needn't ask to be pressed. AUTO never closes the conversation, so
            // the last page's Close still does.
            if (Settings.AutoAdvanceDialogue && page < pages.Count - 1) { next.RemoveFromClassList("dialogue-button--ready"); pulseMs = 0f; return; }
            pulseMs += dtMs;
            if (pulseMs >= PulseMs) { pulseMs = 0f; next.ToggleInClassList("dialogue-button--ready"); }
        }

        /// <summary>Keyboard / controller input and the voice auto-advance; call every frame (unscaled time).</summary>
        public void Tick(float unscaledDtMs)
        {
            if (!IsOpen) return;
            RefreshAuto();   // the Gameplay option is the same setting: a change in the menu over the conversation shows here
            if (paused) return;
            root.EnableInClassList("dialogue--pad", InputMode.Current == InputKind.Gamepad);
            TickAnimation(unscaledDtMs);
            if (InGrace) return;   // the keys of whatever the player was doing
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            bool Pressed(Func<Keyboard, bool> k, Func<Gamepad, bool> g) => (kb != null && k(kb)) || (pad != null && g(pad));
            if (ConfirmOpen)
            {
                if (Pressed(k => k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame, g => g.buttonSouth.wasPressedThisFrame))
                { ButtonSound?.Invoke(false); HideConfirm(); Close(true); }
                else if (Pressed(k => k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame, g => g.buttonEast.wasPressedThisFrame))
                { ButtonSound?.Invoke(false); HideConfirm(); }
                return;
            }
            if (Pressed(k => k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame,
                        g => g.buttonSouth.wasPressedThisFrame || g.dpad.right.wasPressedThisFrame))
            { ButtonSound?.Invoke(false); Next(); return; }
            if (Pressed(k => k.backspaceKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame, g => g.buttonEast.wasPressedThisFrame || g.dpad.left.wasPressedThisFrame))
            { if (page > 0) { ButtonSound?.Invoke(false); Back(); } return; }
            if (Pressed(k => k.escapeKey.wasPressedThisFrame, g => g.buttonNorth.wasPressedThisFrame || g.startButton.wasPressedThisFrame))
            { ButtonSound?.Invoke(false); AskSkip(); return; }
            if (!message && pages.Count > 1 && Pressed(_ => false, g => g.buttonWest.wasPressedThisFrame))
            { ButtonSound?.Invoke(false); ToggleAuto(); return; }

            // DialogueWindow::update: with voice on, turn the page once the line has ended and its pause passed. Remake: the
            // AUTO toggle turns it off, and turns a page without a voice line too, a reading time after it has shown.
            if (!Settings.AutoAdvanceDialogue || page >= pages.Count - 1 || reveal.Running) return;
            bool voiced = voice != null && voice.clip != null && Settings.VoiceVolume > 0f;
            if (voiced && voice.isPlaying) return;
            pauseMs += unscaledDtMs;
            float wait = voiced ? VoicePauseMs
                       : Mathf.Clamp(ReadBaseMs + ReadPerCharMs * (pages[page].text?.Length ?? 0), ReadMinMs, ReadMaxMs);
            if (pauseMs >= wait) { page++; LoadPage(); }
        }
    }
}
