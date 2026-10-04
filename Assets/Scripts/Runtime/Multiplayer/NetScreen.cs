// NetScreen.cs
// Remake multiplayer: what the server shows on a player's screen besides the chat (NetAdmin's /title, /timer and /dialog;
// the dialogues go to the scene's own dialogue window, DialogueView.Latest, one after another): a big
// title with an optional subtitle in the upper middle (fades in 0.3 s, holds, fades out 0.6 s), and a countdown with its
// label at the top centre under the HUD message (m:ss, the last 10 s in amber; it stays at 0:00 for 2 s). Its own panel
// (like FpsCounter: the star map's panel settings, sorted over the HUDs and menus), kept across scenes, so docking or
// jumping keeps it; cleared when the session ends.
// The reward box (/reward, a /dialog's reward page; Layout::showMissionRewardMessage / drawMissionRewardMessage 0xe7684):
// the title (216 "Mission accomplished!" by default) over "+ <credits>" and the items given, each with its shop icon, in a
// box at the centre: fades in over 2 s, holds until 5 s, fades out until 7 s; sound 36 Mission_accomplished.
// An event's scoreboard (NetEvents' "scoreboard on"): a box at the right edge, its title over the players and their scores.
// /sound and /music (NetEventAudio): a sound at the FX volume; a looped track at the music volume that takes over from the
// scene's music (Traffic and StationLevel scale theirs by SceneMusic, which fades to 0 while it plays), "stop" ends it.
// An event's question (NetEvents' "ask"): a box in the lower middle with the question, 2-4 answers and the seconds left;
// answered by a click / tap, 1-4 (also the keypad) or a controller's A / B / X / Y (Esc / the controller's View skips),
// sent to the server (NetState.SendAnswer); while it shows, the flight controls and the menus' keys rest (QuestionOpen:
// Navigation.InputHalted, NetChat.Keys, GameControls.BlocksMenus).
// The mission card (ShowMissionCard): when a squadmate takes a bar mission (a freelance one, NetMissions.Receive, or an event
// graph's, NetEventMissions), the others see which: a box in the upper middle with "NEW SQUAD MISSION", the mission's name,
// who took it, the client's face and the details (target, reward); 9 s (a click / tap closes it), with the message sound.

using GoF2Remake.Data;
using GoF2Remake.UI;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class NetScreen : MonoBehaviour
    {
        const float FadeIn = 0.3f, FadeOut = 0.6f, TimerLinger = 2f;

        static NetScreen instance;
        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualTreeAsset emptyTree;
        VisualElement titleBox, timerBox;
        Label title, subtitle, timerLabel, timerValue;
        float titleStart, titleEnd = -1f, timerEnd = -1f;
        string titleText = "", subtitleText = "", timerText = "";
        bool pending;
        static readonly Queue<(List<DialogueView.Page> pages, System.Action closed)> dialogs = new Queue<(List<DialogueView.Page>, System.Action)>();
        VisualElement rewardBox, rewardItems;
        Label rewardTitle, rewardCredits;
        float rewardStart = -1f;
        AudioSource sound, music;
        VisualElement scoreBox, scoreRows;
        Label scoreTitle;
        string scoreText = "";
        bool scorePending;
        int musicTrack = -1;
        float musicFade;
        VisualElement questionBox, answerRow, questionPortrait, questionSpeaker;
        Label questionText, questionTimer, questionName;
        readonly List<Button> answerButtons = new List<Button>();
        int questionId;
        float questionEnd = -1f, questionGone = -1f;
        string[] pendingQuestion;
        VisualElement cardBox, cardPortrait;
        Label cardHeader, cardTitle, cardBy, cardDetails;
        float cardStart = -1f;
        (string header, string title, string by, string details, int[] face, int speaker)? pendingCard;
        const float CardSeconds = 9f;

        static readonly string[] PadLabels = { "A", "B", "X", "Y" };

        // The theme's colours (GoF2Theme: --gof-panel, --gof-line, --gof-cyan, --gof-text, --gof-text-dim, --gof-amber).
        static readonly Color Panel = new Color(4f / 255f, 10f / 255f, 18f / 255f, 0.97f), Line = new Color(62f / 255f, 200f / 255f, 1f, 0.25f),
            Cyan = new Color(62f / 255f, 200f / 255f, 1f), TextColour = new Color(216f / 255f, 236f / 255f, 247f / 255f),
            TextDim = new Color(216f / 255f, 236f / 255f, 247f / 255f, 0.55f), Amber = new Color(1f, 166f / 255f, 48f / 255f),
            CyanFaint = new Color(62f / 255f, 200f / 255f, 1f, 0.12f);

        /// <summary>The station button's hover / focus look (.station-button:hover): white, an amber edge, moved right; the
        /// picked answer stays lit with amber text.</summary>
        static void Highlight(Button b, bool on, bool picked = false)
        {
            if (b.userData is string) return;   // picked: stays lit
            if (picked) b.userData = "picked";
            b.style.color = picked ? Amber : on ? Color.white : TextDim;
            b.style.borderLeftColor = on ? Amber : Color.clear;
            b.style.backgroundColor = on ? CyanFaint : Color.clear;
            b.style.translate = new Translate(on ? 14 : 0, 0);
        }

        /// <summary>An event's question is waiting for this player's answer: their other input rests meanwhile.</summary>
        public static bool QuestionOpen => instance != null && instance.questionId != 0 && NetGame.Active;

        /// <summary>The scene's own music volume factor: 0 while an event's track plays (faded over a second).</summary>
        public static float SceneMusic => instance == null ? 1f : 1f - instance.musicFade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; dialogs.Clear(); }

        /// <summary>A conversation from the server: shown in this scene's dialogue window once nothing else is open there;
        /// 'closed' runs when it closes (a reward page's payout).</summary>
        public static void QueueDialog(List<DialogueView.Page> pages, System.Action closed = null)
        {
            if (pages == null || pages.Count == 0 || Get() == null) { closed?.Invoke(); return; }
            if (dialogs.Count < 10) dialogs.Enqueue((pages, closed));
        }

        /// <summary>The reward box (Layout::showMissionRewardMessage): 'title' (null = 216 "Mission accomplished!"), "+ credits"
        /// when above 0 and the items, 7 s, with sound 36.</summary>
        public static void ShowReward(string heading, int credits, List<(int item, int amount)> items)
        {
            var s = Get();
            if (s == null) return;
            s.rewardStart = Time.unscaledTime;
            s.pendingReward = (string.IsNullOrEmpty(heading) ? Localization.Get(216) : heading, credits, items ?? new List<(int, int)>());
            var clip = Flight.CombatAssets.Load()?.missionAccomplished;
            if (clip != null)
            {
                if (s.sound == null) { s.sound = s.gameObject.AddComponent<AudioSource>(); s.sound.playOnAwake = false; s.sound.spatialBlend = 0f; s.sound.ignoreListenerPause = true; }
                s.sound.PlayOneShot(clip, Settings.SfxVolume);
            }
        }

        (string title, int credits, List<(int item, int amount)> items)? pendingReward;

        static NetScreen Get()
        {
            if (instance != null) return instance;
            var assets = StarMapAssets.Load();
            if (assets == null || assets.panelSettings == null) return null;
            var go = new GameObject("NetScreen");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NetScreen>();
            instance.runtimePanel = Instantiate(assets.panelSettings);
            instance.runtimePanel.sortingOrder = assets.panelSettings.sortingOrder + 90;   // over the HUDs and menus, under the FPS counter
            instance.runtimePanel.referenceResolution = new Vector2Int(1920, 1080);
            instance.runtimePanel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            instance.runtimePanel.match = 1f;
            instance.emptyTree = ScriptableObject.CreateInstance<VisualTreeAsset>();
            var pr = go.AddComponent<PanelRenderer>();
            pr.panelSettings = instance.runtimePanel;
            pr.visualTreeAsset = instance.emptyTree;
            go.SetActive(true);
            return instance;
        }

        /// <summary>The mission card: 'header' (default "New squad mission") over the mission's 'title', 'by' (who took it) and
        /// 'details' (lines); the client's face (a generated one, or a story speaker when 'speaker' >= 0; none: no portrait).</summary>
        public static void ShowMissionCard(string header, string title, string by, string details, int[] face, int speaker = -1)
        {
            var s = Get();
            if (s == null) return;
            s.pendingCard = (string.IsNullOrEmpty(header) ? Localization.Extra("mpMissionCard", "New squad mission") : header, title ?? "", by ?? "", details ?? "", face, speaker);
            s.cardStart = Time.unscaledTime;
            PlaySound((int)EventSound.Message);
        }

        /// <summary>A title (and subtitle) for 'seconds'; empty text clears it.</summary>
        public static void ShowTitle(string text, string sub, float seconds)
        {
            var s = Get();
            if (s == null) return;
            s.titleText = text ?? "";
            s.subtitleText = sub ?? "";
            s.titleStart = Time.unscaledTime;
            s.titleEnd = s.titleText.Length + s.subtitleText.Length == 0 ? -1f : Time.unscaledTime + Mathf.Max(0.5f, seconds);
            s.pending = true;
        }

        /// <summary>A countdown of 'seconds' with 'label'; seconds below 0 stops it.</summary>
        public static void ShowTimer(float seconds, string label)
        {
            var s = Get();
            if (s == null) return;
            s.timerEnd = seconds < 0f ? -1f : Time.unscaledTime + seconds;
            s.timerText = label ?? "";
            s.pending = true;
        }

        public static void Clear()
        {
            if (instance == null) return;
            instance.titleEnd = instance.timerEnd = -1f;
            instance.pending = true;
            instance.scoreText = "";
            instance.scorePending = true;
            instance.musicTrack = -1;
            instance.questionId = 0;
            instance.cardStart = -1f;
        }

        /// <summary>An event's question: "id US seconds US speaker US question US answer US answer..." (the speaker: "" or
        /// NetAdmin.ResolveSpeaker's spec with RS for US: "id RS name RS face").</summary>
        public static void ShowQuestion(string payload)
        {
            var s = Get();
            if (s == null) return;
            var f = (payload ?? "").Split('\u001f');
            if (f.Length < 6 || !int.TryParse(f[0], out int id)) return;
            s.pendingQuestion = f;
            s.questionId = id;
            float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds);
            s.questionEnd = seconds > 0f ? Time.unscaledTime + seconds : float.PositiveInfinity;
            s.questionGone = -1f;
            PlaySound((int)EventSound.Message);
        }

        /// <summary>The server closed the question (the time ran out, the event ended).</summary>
        public static void CloseQuestion(int id)
        {
            if (instance == null || instance.questionId != id) return;
            instance.questionId = 0;
            instance.questionGone = Time.unscaledTime;
        }

        void Answer(int choice)
        {
            if (questionId == 0) return;
            NetState.Instance?.SendAnswer(questionId, choice);
            questionId = 0;
            questionGone = Time.unscaledTime + (choice > 0 ? 1f : 0f);   // the picked answer shows for a second
            if (choice > 0 && choice <= answerButtons.Count) Highlight(answerButtons[choice - 1], true, picked: true);
            PlaySound((int)EventSound.Click);
        }

        /// <summary>The scoreboard: its title, then "name TAB score" lines; "" hides it.</summary>
        public static void ShowScoreboard(string text)
        {
            var s = Get();
            if (s == null) return;
            s.scoreText = text ?? "";
            s.scorePending = true;
        }

        /// <summary>An event sound (EventSound) at the FX volume.</summary>
        public static void PlaySound(int index)
        {
            var s = Get();
            var clip = NetEventAudio.Load()?.Sound(index);
            if (s == null || clip == null) return;
            s.Source(ref s.sound).PlayOneShot(clip, Settings.SfxVolume);
        }

        /// <summary>An event track (EventMusic) looped instead of the scene's music; -1 stops it.</summary>
        public static void PlayMusic(int index)
        {
            var s = Get();
            if (s == null) return;
            var clip = index >= 0 ? NetEventAudio.Load()?.Music(index) : null;
            s.musicTrack = clip != null ? index : -1;
            var src = s.Source(ref s.music);
            if (clip == null) return;   // the fade-out stops it
            src.clip = clip;
            src.loop = true;
            src.Play();
        }

        AudioSource Source(ref AudioSource field)
        {
            if (field == null)
            {
                field = gameObject.AddComponent<AudioSource>();
                field.playOnAwake = false;
                field.spatialBlend = 0f;
                field.ignoreListenerPause = true;
            }
            return field;
        }

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
        }

        void OnDisable() => panelRenderer?.UnregisterUIReloadCallback(OnUIReload);

        void OnDestroy()
        {
            if (runtimePanel != null) Destroy(runtimePanel);
            if (emptyTree != null) Destroy(emptyTree);
            if (instance == this) instance = null;
        }

        static Label Text(VisualElement parent, float size, Color colour, float spacing)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            var s = l.style;
            s.fontSize = size;
            s.color = colour;
            s.letterSpacing = spacing;
            s.unityTextAlign = TextAnchor.MiddleCenter;
            s.whiteSpace = WhiteSpace.Normal;
            s.maxWidth = 1500;
            s.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 8f, color = new Color(0f, 0f, 0f, 0.95f) };
            parent.Add(l);
            return l;
        }

        void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            titleBox = new VisualElement { pickingMode = PickingMode.Ignore };
            titleBox.style.position = Position.Absolute;
            titleBox.style.left = titleBox.style.right = 0;
            titleBox.style.top = Length.Percent(24);
            titleBox.style.alignItems = Align.Center;
            root.Add(titleBox);
            title = Text(titleBox, 64, Color.white, 8);
            title.AddToClassList("gof-semibold");
            subtitle = Text(titleBox, 30, new Color(1f, 0.75f, 0.35f), 4);
            subtitle.style.marginTop = 8;
            timerBox = new VisualElement { pickingMode = PickingMode.Ignore };
            timerBox.style.position = Position.Absolute;
            timerBox.style.left = timerBox.style.right = 0;
            timerBox.style.top = 96;
            timerBox.style.alignItems = Align.Center;
            root.Add(timerBox);
            timerLabel = Text(timerBox, 20, new Color(0.85f, 0.92f, 1f), 4);
            timerValue = Text(timerBox, 40, Color.white, 3);
            timerValue.AddToClassList("gof-semibold");
            // The reward box: centred, under the title's place.
            var rewardRow = new VisualElement { pickingMode = PickingMode.Ignore };
            rewardRow.style.position = Position.Absolute;
            rewardRow.style.left = rewardRow.style.right = 0;
            rewardRow.style.top = Length.Percent(44);
            rewardRow.style.alignItems = Align.Center;
            root.Add(rewardRow);
            rewardBox = new VisualElement { pickingMode = PickingMode.Ignore };
            var b = rewardBox.style;
            b.minWidth = 460;
            b.paddingLeft = b.paddingRight = 28;
            b.paddingTop = 16;
            b.paddingBottom = 18;
            b.alignItems = Align.Center;
            b.backgroundColor = new Color(0.015f, 0.04f, 0.07f, 0.88f);
            b.borderTopWidth = b.borderBottomWidth = b.borderLeftWidth = b.borderRightWidth = 2;
            b.borderTopColor = b.borderBottomColor = b.borderLeftColor = b.borderRightColor = new Color(1f, 0.72f, 0.3f, 0.9f);
            b.borderTopLeftRadius = b.borderTopRightRadius = b.borderBottomLeftRadius = b.borderBottomRightRadius = 4;
            rewardRow.Add(rewardBox);
            rewardTitle = Text(rewardBox, 28, Color.white, 3);
            rewardTitle.AddToClassList("gof-semibold");
            rewardCredits = Text(rewardBox, 34, new Color(1f, 0.78f, 0.35f), 2);
            rewardCredits.AddToClassList("gof-semibold");
            rewardCredits.style.marginTop = 6;
            rewardItems = new VisualElement { pickingMode = PickingMode.Ignore };
            rewardItems.style.marginTop = 6;
            rewardBox.Add(rewardItems);
            rewardBox.style.display = DisplayStyle.None;
            // The scoreboard: at the right edge, a little above the middle.
            scoreBox = new VisualElement { pickingMode = PickingMode.Ignore };
            var sb = scoreBox.style;
            sb.position = Position.Absolute;
            sb.right = 24;
            sb.top = Length.Percent(40);   // under the station's pilot list
            sb.minWidth = 300;
            sb.paddingLeft = sb.paddingRight = 18;
            sb.paddingTop = 10;
            sb.paddingBottom = 12;
            sb.backgroundColor = new Color(0.015f, 0.04f, 0.07f, 0.8f);
            sb.borderLeftWidth = 3;
            sb.borderLeftColor = new Color(1f, 0.72f, 0.3f, 0.9f);
            root.Add(scoreBox);
            scoreTitle = Text(scoreBox, 22, new Color(1f, 0.78f, 0.35f), 3);
            scoreTitle.AddToClassList("gof-semibold");
            scoreTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
            scoreRows = new VisualElement { pickingMode = PickingMode.Ignore };
            scoreRows.style.marginTop = 6;
            scoreBox.Add(scoreRows);
            scoreBox.style.display = DisplayStyle.None;
            scorePending = true;
            // The mission card: upper middle, the question's panel look with an amber top edge.
            var cardRow = new VisualElement { pickingMode = PickingMode.Ignore };
            cardRow.style.position = Position.Absolute;
            cardRow.style.left = cardRow.style.right = 0;
            cardRow.style.top = Length.Percent(14);
            cardRow.style.alignItems = Align.Center;
            root.Add(cardRow);
            cardBox = new VisualElement { pickingMode = PickingMode.Position };
            var cs = cardBox.style;
            cs.width = 820;
            cs.maxWidth = Length.Percent(80);
            cs.flexDirection = FlexDirection.Row;
            cs.alignItems = Align.Center;
            cs.paddingTop = cs.paddingBottom = 16;
            cs.paddingLeft = cs.paddingRight = 20;
            cs.backgroundColor = Panel;
            cs.borderTopWidth = 3;
            cs.borderBottomWidth = cs.borderLeftWidth = cs.borderRightWidth = 1;
            cs.borderTopColor = Amber;
            cs.borderBottomColor = cs.borderLeftColor = cs.borderRightColor = Line;
            cardBox.RegisterCallback<PointerDownEvent>(_ => cardStart = Time.unscaledTime - CardSeconds + FadeOut);   // a tap closes it
            cardRow.Add(cardBox);
            cardPortrait = new VisualElement { pickingMode = PickingMode.Ignore };
            cardPortrait.style.width = 128;
            cardPortrait.style.height = 160;
            cardPortrait.style.flexShrink = 0;
            cardPortrait.style.overflow = Overflow.Hidden;
            cardPortrait.style.marginRight = 20;
            cardBox.Add(cardPortrait);
            var cardText = new VisualElement { pickingMode = PickingMode.Ignore };
            cardText.style.flexGrow = 1;
            cardText.style.flexShrink = 1;
            cardBox.Add(cardText);
            cardHeader = Text(cardText, 18, Amber, 4);
            cardHeader.AddToClassList("gof-semibold");
            cardTitle = Text(cardText, 34, Color.white, 2);
            cardTitle.AddToClassList("gof-semibold");
            cardTitle.style.marginTop = 2;
            cardBy = Text(cardText, 19, Cyan, 1);
            cardBy.style.marginTop = 2;
            cardDetails = Text(cardText, 20, TextColour, 0);
            cardDetails.style.marginTop = 10;
            foreach (var l in new[] { cardHeader, cardTitle, cardBy, cardDetails }) l.style.unityTextAlign = TextAnchor.MiddleLeft;
            cardBox.style.display = DisplayStyle.None;
            // The question, like the Space Lounge's chat window (UI/Station/Lounge.uss .chat-*): a panel at the bottom with a
            // header (the speaker's name, the seconds left), the portrait beside the text, the answers stacked under it as
            // station buttons (.station-button .chat-choice). Inline styles: this panel doesn't load the station's sheets.
            var questionRow = new VisualElement { pickingMode = PickingMode.Ignore };
            questionRow.style.position = Position.Absolute;
            questionRow.style.left = questionRow.style.right = 0;
            questionRow.style.bottom = 110;
            questionRow.style.alignItems = Align.Center;
            root.Add(questionRow);
            questionBox = new VisualElement { pickingMode = PickingMode.Position };
            var q = questionBox.style;
            q.width = 1100;
            q.maxWidth = Length.Percent(92);
            q.backgroundColor = Panel;
            q.borderTopWidth = 3;
            q.borderBottomWidth = q.borderLeftWidth = q.borderRightWidth = 1;
            q.borderTopColor = Cyan;
            q.borderBottomColor = q.borderLeftColor = q.borderRightColor = Line;
            questionRow.Add(questionBox);
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            var h = header.style;
            h.flexDirection = FlexDirection.Row;
            h.alignItems = Align.Center;
            h.paddingTop = 12; h.paddingRight = 18; h.paddingBottom = 10; h.paddingLeft = 20;
            h.backgroundColor = new Color(62f / 255f, 200f / 255f, 1f, 0.08f);
            h.borderBottomWidth = 1;
            h.borderBottomColor = Line;
            questionBox.Add(header);
            questionName = Text(header, 24, Color.white, 3);
            questionName.AddToClassList("gof-semibold");
            questionName.style.unityTextAlign = TextAnchor.MiddleLeft;
            questionName.style.flexGrow = 1;
            questionTimer = Text(header, 16, TextDim, 1);
            questionTimer.style.unityTextAlign = TextAnchor.MiddleRight;
            questionSpeaker = new VisualElement { pickingMode = PickingMode.Ignore };
            var body = questionSpeaker.style;
            body.flexDirection = FlexDirection.Row;
            body.paddingTop = 16; body.paddingRight = 20; body.paddingLeft = 16;
            questionBox.Add(questionSpeaker);
            questionPortrait = new VisualElement { pickingMode = PickingMode.Ignore };
            questionPortrait.style.width = 160;
            questionPortrait.style.height = 200;
            questionPortrait.style.flexShrink = 0;
            questionPortrait.style.overflow = Overflow.Hidden;
            questionPortrait.style.marginRight = 20;
            questionSpeaker.Add(questionPortrait);
            questionText = Text(questionSpeaker, 23, TextColour, 0);
            questionText.style.textShadow = new TextShadow();
            questionText.style.unityTextAlign = TextAnchor.UpperLeft;
            questionText.style.flexShrink = 1;
            questionText.style.flexGrow = 1;
            answerRow = new VisualElement { pickingMode = PickingMode.Position };
            var c = answerRow.style;
            c.paddingTop = 12; c.paddingRight = 20; c.paddingBottom = 16; c.paddingLeft = 196;
            questionBox.Add(answerRow);
            questionBox.style.display = DisplayStyle.None;
            pending = true;
        }

        /// <summary>The question's speaker: a story character (id >= 0, a name = renamed), a generated face (-1, its
        /// descriptor) or the reader (-2: Keith's face, the pilot's name), as /dialog's pages (NetAdmin.Apply).</summary>
        void ShowSpeaker(string spec, string me)
        {
            var f = (spec ?? "").Split('\u001e');
            bool on = f.Length >= 3 && int.TryParse(f[0], out _);
            questionPortrait.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            answerRow.style.paddingLeft = on ? 196 : 20;
            questionName.text = Localization.Extra("mpAskTitle", "Question").ToUpperInvariant();
            if (!on) return;
            int.TryParse(f[0], out int id);
            string name = f[1].Replace("%player%", me);
            if (id == -1)
            {
                var parts = f[2].Split(',');
                var face = new int[parts.Length];
                for (int k = 0; k < parts.Length; k++) int.TryParse(parts[k], out face[k]);
                Portrait.Show(questionPortrait, face, false);
            }
            else
            {
                if (id == -2) { id = 0; name = me; }
                if (id >= StoryTable.SpeakerCount) id = 0;
                Portrait.ShowSpeaker(questionPortrait, id, id == 0);
                if (name.Length == 0) name = StoryTable.SpeakerName(id);
            }
            questionName.text = name.ToUpperInvariant();
            StylePortrait(questionPortrait, 160, 200);
        }

        /// <summary>The dialogue's .portrait-* styles, inline (this panel doesn't load Dialogue.uss).</summary>
        static void StylePortrait(VisualElement portrait, float width, float height)
        {
            portrait.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
            foreach (var child in portrait.Children())
            {
                child.style.position = Position.Absolute;
                child.style.left = 0;
                child.style.top = 0;
                if (child.ClassListContains("portrait-frame") || child.ClassListContains("portrait-layers")) { child.style.width = width; child.style.height = height; }
                if (child.ClassListContains("portrait-frame")) child.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
                if (child.ClassListContains("portrait-layers"))
                {
                    if (portrait.ClassListContains("portrait--mirrored")) child.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
                    foreach (var part in child.Children())
                    {
                        part.style.position = Position.Absolute;
                        part.style.left = 0;
                        part.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
                    }
                }
            }
        }

        /// <summary>The mission card: fades in 0.3 s, holds, fades out 0.6 s (CardSeconds in all).</summary>
        void UpdateCard(float now)
        {
            if (cardBox == null) return;
            if (pendingCard.HasValue)
            {
                var c = pendingCard.Value;
                pendingCard = null;
                cardHeader.text = c.header.ToUpperInvariant();
                cardTitle.text = c.title;
                cardBy.text = c.by;
                cardBy.style.display = c.by.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                cardDetails.text = c.details;
                cardDetails.style.display = c.details.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                bool face = (c.face != null && c.face.Length > 0) || c.speaker >= 0;
                cardPortrait.style.display = face ? DisplayStyle.Flex : DisplayStyle.None;
                if (c.speaker >= 0) Portrait.ShowSpeaker(cardPortrait, Mathf.Min(c.speaker, StoryTable.SpeakerCount - 1), c.speaker == 0);
                else if (face) Portrait.Show(cardPortrait, c.face, false);
                if (face) StylePortrait(cardPortrait, 128, 160);
            }
            float t = cardStart >= 0f ? now - cardStart : -1f;
            bool on = t >= 0f && t < CardSeconds && NetGame.Active;
            cardBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) { cardStart = -1f; return; }
            cardBox.style.opacity = Mathf.Min(Mathf.Clamp01(t / FadeIn), Mathf.Clamp01((CardSeconds - t) / FadeOut));
        }

        /// <summary>The question box: built when one arrives, answered by click / tap, 1-4, A B X Y; Esc / View skips.</summary>
        void UpdateQuestion(float now)
        {
            if (questionBox == null) return;
            if (pendingQuestion != null)
            {
                var f = pendingQuestion;
                pendingQuestion = null;
                string me = NetPlayer.Local != null ? NetPlayer.Local.DisplayName : "";
                ShowSpeaker(f[2], me);
                questionText.text = f[3].Replace("%player%", me);
                answerRow.Clear();
                answerButtons.Clear();
                for (int k = 4; k < f.Length && k < 8; k++)
                {
                    int choice = k - 3;
                    var button = new Button(() => Answer(choice)) { text = $"{choice}.  {f[k]}" };
                    var bs = button.style;
                    bs.backgroundColor = Color.clear;
                    bs.borderTopWidth = bs.borderBottomWidth = bs.borderRightWidth = 0;
                    bs.borderLeftWidth = 3;
                    bs.borderLeftColor = Color.clear;
                    bs.borderTopLeftRadius = bs.borderTopRightRadius = bs.borderBottomLeftRadius = bs.borderBottomRightRadius = 0;
                    bs.marginTop = bs.marginBottom = 2;
                    bs.marginLeft = bs.marginRight = 0;
                    bs.paddingTop = bs.paddingBottom = 10;
                    bs.paddingLeft = bs.paddingRight = 18;
                    bs.fontSize = 21;
                    bs.letterSpacing = 1;
                    bs.unityTextAlign = TextAnchor.MiddleLeft;
                    bs.color = TextDim;
                    bs.transitionDuration = new List<TimeValue> { new TimeValue(0.18f) };
                    bs.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };
                    button.AddToClassList("gof-semibold");
                    // A controller's button beside the text, like the station's hint glyphs.
                    var glyph = new Label(PadLabels[choice - 1]) { pickingMode = PickingMode.Ignore };
                    glyph.style.position = Position.Absolute;
                    glyph.style.right = 14;
                    glyph.style.top = 9;
                    glyph.style.fontSize = 16;
                    glyph.style.color = TextDim;
                    button.Add(glyph);
                    button.RegisterCallback<PointerEnterEvent>(_ => Highlight(button, true));
                    button.RegisterCallback<PointerLeaveEvent>(_ => Highlight(button, false));
                    button.RegisterCallback<FocusInEvent>(_ => Highlight(button, true));
                    button.RegisterCallback<FocusOutEvent>(_ => Highlight(button, false));
                    answerRow.Add(button);
                    answerButtons.Add(button);
                }
            }
            bool open = questionId != 0 && NetGame.Active;
            bool lingering = !open && questionGone >= 0f && now < questionGone;
            questionBox.style.display = open || lingering ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) { if (!lingering) questionGone = -1f; return; }
            if (now >= questionEnd) { questionId = 0; return; }   // the server closes it too
            questionTimer.text = float.IsPositiveInfinity(questionEnd) ? Localization.Extra("mpAskSkip", "Esc: skip")
                : $"{Mathf.CeilToInt(questionEnd - now)} s  ·  " + Localization.Extra("mpAskSkip", "Esc: skip");
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            for (int k = 1; k <= answerButtons.Count; k++)
            {
                bool key = kb != null && (kb[Key.Digit1 + (k - 1)].wasPressedThisFrame || kb[Key.Numpad1 + (k - 1)].wasPressedThisFrame);
                bool button = pad != null && (k == 1 ? pad.buttonSouth : k == 2 ? pad.buttonEast : k == 3 ? pad.buttonWest : pad.buttonNorth).wasPressedThisFrame;
                if (key || button) { Answer(k); return; }
            }
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame)) Answer(0);
        }

        void UpdateScoreboard()
        {
            if (!scorePending || scoreBox == null) return;
            scorePending = false;
            var lines = scoreText.Split('\n');
            bool on = scoreText.Length > 0;
            scoreBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            scoreTitle.text = lines[0].ToUpperInvariant();
            scoreRows.Clear();
            string me = NetPlayer.Local != null ? NetPlayer.Local.DisplayName : null;
            for (int i = 1; i < lines.Length; i++)
            {
                var f = lines[i].Split('\t');
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.marginTop = 2;
                var colour = f[0] == me ? new Color(1f, 0.85f, 0.45f) : Color.white;
                var name = Text(row, 20, colour, 1);
                name.text = $"{i}. {f[0]}";
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                name.style.marginRight = 24;
                var score = Text(row, 20, colour, 1);
                score.text = f.Length > 1 ? f[1] : "";
                score.style.unityTextAlign = TextAnchor.MiddleRight;
                scoreRows.Add(row);
            }
        }

        /// <summary>The event track fades in over the scene's music (which SceneMusic fades out), and out again when stopped.</summary>
        void UpdateMusic()
        {
            if (music == null) return;
            bool on = musicTrack >= 0 && NetGame.Active;
            musicFade = Mathf.MoveTowards(musicFade, on ? 1f : 0f, Time.unscaledDeltaTime);
            music.volume = musicFade * Settings.MusicVolume;
            if (!on && musicFade <= 0f && music.isPlaying) music.Stop();
            if (!NetGame.Active) musicTrack = -1;
        }

        /// <summary>drawMissionRewardMessage: alpha t / 2000 for 2 s, full until 5 s, (7000 - t) / 2000 until 7 s.</summary>
        void UpdateReward(float now)
        {
            if (rewardBox == null) return;
            if (pendingReward.HasValue)
            {
                var (heading, credits, items) = pendingReward.Value;
                pendingReward = null;
                rewardTitle.text = heading.ToUpperInvariant();
                rewardCredits.text = credits > 0 ? "+ " + ItemInfo.Credits(credits) : "";
                rewardCredits.style.display = credits > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                rewardItems.Clear();
                foreach (var (item, amount) in items)
                {
                    var row = new VisualElement { pickingMode = PickingMode.Ignore };
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginTop = 4;
                    var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                    icon.style.width = 90;
                    icon.style.height = 44;
                    icon.style.marginRight = 12;
                    var tex = Resources.Load<Texture2D>($"GoF2Icons/item_{item:000}");
                    if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                    icon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                    row.Add(icon);
                    var label = Text(row, 24, Color.white, 1);
                    label.text = amount > 1 ? $"{amount} x {ItemInfo.ItemName(item)}" : ItemInfo.ItemName(item);
                    rewardItems.Add(row);
                }
            }
            float t = rewardStart >= 0f ? (now - rewardStart) * 1000f : -1f;
            bool on = t >= 0f && t < 7000f;
            rewardBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) { rewardStart = -1f; return; }
            rewardBox.style.opacity = t < 2000f ? t / 2000f : t > 5000f ? (7000f - t) / 2000f : 1f;
        }

        void Update()
        {
            if (title == null) return;
            if (!NetGame.Active && (titleEnd >= 0f || timerEnd >= 0f || rewardStart >= 0f)) { titleEnd = timerEnd = rewardStart = -1f; pending = true; }
            if (!NetGame.Active) dialogs.Clear();
            var view = DialogueView.Latest;
            if (dialogs.Count > 0 && view != null && view.Usable && !view.IsOpen && !StarMap.IsOpen)
            {
                var (pages, closed) = dialogs.Dequeue();
                view.Show(pages, _ => closed?.Invoke());
            }
            float now = Time.unscaledTime;
            UpdateReward(now);
            UpdateMusic();
            UpdateQuestion(now);
            UpdateCard(now);
            if (!NetGame.Active && scoreText.Length > 0) { scoreText = ""; scorePending = true; }
            UpdateScoreboard();
            // The title: fade in, hold, fade out.
            bool titleOn = titleEnd >= 0f && now < titleEnd + FadeOut;
            if (pending) { title.text = titleText; subtitle.text = subtitleText; subtitle.style.display = subtitleText.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None; }
            titleBox.style.display = titleOn ? DisplayStyle.Flex : DisplayStyle.None;
            if (titleOn) titleBox.style.opacity = Mathf.Min(Mathf.Clamp01((now - titleStart) / FadeIn), Mathf.Clamp01((titleEnd + FadeOut - now) / FadeOut));
            else if (titleEnd >= 0f) titleEnd = -1f;
            // The countdown: m:ss, amber for the last 10 s, gone 2 s after 0.
            bool timerOn = timerEnd >= 0f && now < timerEnd + TimerLinger;
            timerBox.style.display = timerOn ? DisplayStyle.Flex : DisplayStyle.None;
            if (timerOn)
            {
                if (pending) { timerLabel.text = timerText.ToUpperInvariant(); timerLabel.style.display = timerText.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None; }
                int left = Mathf.CeilToInt(Mathf.Max(0f, timerEnd - now));
                timerValue.text = $"{left / 60}:{left % 60:00}";
                timerValue.style.color = left <= 10 ? new Color(1f, 0.7f, 0.25f) : Color.white;
            }
            else if (timerEnd >= 0f) timerEnd = -1f;
            pending = false;
        }
    }
}
