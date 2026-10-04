// EndingCredits.cs
// The main story's ending (ModStation::OnTouchEnd 0xea4ec at index 43, then ModStation::OnUpdate 0xed2a8 / OnRender2D
// 0xef208; Reference/research/dialogue_cutscenes.md 3.4, campaign_flow.md 9). The original draws it in the station
// module over CutScene(2), the same space backdrop as the main menu, so the remake plays it in the main menu scene
// (MainMenu starts it instead of its title when Session.EndingPending is set):
//   clock        ModStation+0xb0, from -12000 ms: the station fades to black (the scene switch here), at -6000 the
//                backdrop fades in until 0
//   music        144 OutroSong
//   radio        Brent Snocom (speaker 1): 2071 at 4000 ms, then 2072 / 2073 / 2074 chained (voices RADIO_43_0..3)
//   logo         after the last line: the logo 0x1b5a rises from below the screen at 0.03 px/ms to the centre, holds
//                4000 ms, then rises on with the staff credits (text 48, centred, width - 100) under it
//   the end      a fade out from 130 001 ms; past 136 000 ms, or a tap once the last line has shown:
//                nextCampaignMission (-> 44) and the station again (Keith alone the next morning, 44's pages)

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class EndingCredits : MonoBehaviour
    {
        const float Speed = 0.03f;   // px per ms
        const float HoldMs = 4000f, FadeOutFrom = 130001f, EndAt = 136000f, FadeInFrom = -6000f;

        class World : IRadioWorld
        {
            public float ms;
            public float MissionMs => ms;
            public int ScriptEvent => 0;
            public int ShipCount => 0;
            public bool ShipDead(int i) => false;
            public bool ShipActive(int i) => false;
            public float ShipHullFraction(int i) => 1f;
            public bool ShipHostile(int i) => false;
            public int RouteIndex => 0;
            public int CrateCargoCaptured => 0;
            public bool StationLocked => false;
            public bool PlayerArmorGone => false;
            public int EnemiesLeft => 0;
            public int FriendsLeft => 0;
            public bool ShipFriendly(int i) => false;
            public bool ShipEmpDisabled(int i) => false;
            public bool ShipInactive(int i) => false;
            public float ShipGameZ(int i) => 0f;
        }

        readonly World world = new World { ms = FadeInFrom };
        Radio radio;
        VisualElement layer, column, logo, fade, radioBox, radioPortrait;
        Label radioSpeaker, radioText, credits;
        TextReveal radioReveal;
        AudioSource music, voice;
        int radioShown = -1;
        float logoY = float.NaN, holdMs;
        bool logoRising, done;

        /// <summary>Starts the ending over the menu's backdrop; 'root' is the menu's UI root (its own content is hidden).</summary>
        public static EndingCredits Begin(GameObject host, VisualElement root, AudioSource musicSource)
        {
            var e = host.AddComponent<EndingCredits>();
            e.Build(root, musicSource);
            return e;
        }

        void Build(VisualElement root, AudioSource musicSource)
        {
            foreach (var child in root.Children()) child.style.display = DisplayStyle.None;
            var assets = StoryAssets.Load();
            if (assets == null || assets.endingUxml == null) { Debug.LogWarning("EndingCredits: run GoF2 > Build > Story Assets"); Finish(); return; }
            layer = assets.endingUxml.Instantiate();
            layer.pickingMode = PickingMode.Ignore;
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            root.Add(layer);
            column = layer.Q("endingColumn");
            logo = layer.Q("endingLogo");
            credits = layer.Q<Label>("endingText");
            fade = layer.Q("endingFade");
            radioBox = layer.Q("radio");
            radioPortrait = layer.Q("radioPortrait");
            radioSpeaker = layer.Q<Label>("radioSpeaker");
            radioText = layer.Q<Label>("radioText");
            var tex = Resources.Load<Texture2D>("GoF2Hud/ending_logo");
            if (tex != null) logo.style.backgroundImage = new StyleBackground(tex);
            credits.text = Localization.Get(48).Replace("\r", "");
            column.style.visibility = Visibility.Hidden;

            // Brent's four lines: 2071 at 4000 ms, the rest chained.
            var lines = new System.Collections.Generic.List<RadioLine>();
            for (int i = 0; i < 4; i++)
                lines.Add(new RadioLine { text = 2071 + i, speaker = 1, trigger = i == 0 ? 5 : 6, param = i == 0 ? 4000 : i - 1, voice = "RADIO_43_" + i });
            radio = new Radio(lines);

            music = musicSource != null ? musicSource : gameObject.AddComponent<AudioSource>();
            music.Stop();
            music.clip = assets.outroSong;
            music.loop = true;   // 144 OutroSong loops (72 s in the 136 s sequence)
            music.volume = Settings.MusicVolume;
            if (music.clip != null) music.Play();
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
        }

        void Update()
        {
            if (done || layer == null) return;
            float dt = Time.unscaledDeltaTime * 1000f;
            world.ms += dt;
            float t = world.ms;
            if (t >= 0f) radio.Update(dt, world);
            UpdateRadio();
            radioReveal?.Tick(dt);   // the animated dialogue option

            // Fades: the backdrop in until 0, out from 130 001 ms.
            float a = t < 0f ? Mathf.Clamp01(-t / -FadeInFrom) : t > FadeOutFrom ? Mathf.Clamp01((t - FadeOutFrom) / (EndAt - FadeOutFrom)) : 0f;
            fade.style.opacity = a;

            UpdateLogo(dt);
            bool tapped = TappedAnything();
            if (t > EndAt || (radio.LastOver && tapped)) Finish();
        }

        void UpdateLogo(float dt)
        {
            if (!radio.LastOver) return;
            float h = layer.layout.height;
            if (!(h > 0f)) return;
            float logoH = logo.layout.height > 0f ? logo.layout.height : 156f;
            if (float.IsNaN(logoY)) { logoY = h; logoRising = true; column.style.visibility = Visibility.Visible; }
            float centre = (h - logoH) / 2f;
            if (logoRising)
            {
                logoY -= Speed * dt;
                if (holdMs == 0f && logoY <= centre) { logoY = centre; logoRising = false; }
            }
            else
            {
                holdMs += dt;
                if (holdMs >= HoldMs) logoRising = true;
            }
            column.style.top = logoY;
        }

        /// <summary>Radio::draw in the ending: the same box as in flight, voice once when the line appears.</summary>
        void UpdateRadio()
        {
            var line = radio.Visible;
            radioBox.EnableInClassList("radio--shown", line != null);
            int index = radio.VisibleIndex;
            if (line == null || index == radioShown) return;
            radioShown = index;
            radioSpeaker.text = StoryTable.SpeakerName(line.speaker).ToUpperInvariant();
            string lineText = Localization.Get(line.text);
            bool alien = StoryTable.UsesAlienFont(line.speaker);
            AlienText.Set(radioText, lineText, alien);
            Portrait.ShowSpeaker(radioPortrait, line.speaker, false);
            var clip = StoryAssets.Load()?.Voice(line.voice);
            radioReveal ??= new TextReveal(radioText);
            if (StoryTable.IsNarration(line.speaker)) radioReveal.Clear();
            else radioReveal.Begin(lineText, alien, clip);
            if (clip != null) { voice.clip = clip; voice.volume = Settings.VoiceVolume; voice.Play(); }
        }

        static bool TappedAnything()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            if (pad != null) foreach (var c in pad.allControls) if (c is ButtonControl b && b.wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>nextCampaignMission (-> 44), the station module again.</summary>
        void Finish()
        {
            if (done) return;
            done = true;
            Session.EndingPending = false;
            if (!Session.FreePlay && Story.Index == 43) Story.Advance(Database.Load());
            if (music != null) music.Stop();
            SceneManager.LoadScene(Application.CanStreamedLevelBeLoaded("Station") ? "Station" : SceneManager.GetActiveScene().name);
        }
    }
}
