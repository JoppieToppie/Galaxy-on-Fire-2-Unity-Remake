// StorySpace.cs
// The story in a flight level (MGame, Reference/research/campaign_flow.md 3.2 / 3.3, campaign_levels_a.md 1.3):
//   MGame::dialogueEvent 0x1b0498   after the launch / arrival camera and once the level clock (MGame+0x48, paused with
//                                   the game) is past 5000 ms, the briefing of the level mission (when it has briefing
//                                   pages and is visible); it restarts the mission clock
//   MGame::successCheck 0x1b0620    from 5000 ms of level time: the campaign mission complete (Story.IsComplete in
//                                   space) or the campaign level's win objective -> the success conversation; closing it
//                                   credits the reward and advances the story (index &gt; 45 without pages: advance at
//                                   once); the level keeps running with the new index. New index 15 -> station 98
//                                   (arrested), 22 -> back into Kappa's station; Valkyrie: 65 -> straight into Kothar's
//                                   orbit with Khador, 74 -> docked at Kothar, 81 -> into the alien orbit (Alice stranded)
//   MGame::gameOverCheck 0x1b0d04   the campaign level's fail objective -> "Mission failed!" (392, + 527 at 38 / 40 / 41)
//                                   and "Game Over" (319), then the last save; a failure or the player's death counts
//                                   toward Globals::lastCampaignMissionFailCount (3 in a row: NPC guns x0.7)
//   successCheck, index 38          the surviving freighters become unkillable (9 999 999); 63: the pirates stop shooting
//                                   (removeAllGuns); 73: the surviving convoy freighters unkillable and moving again
//   Navigation (index 24)           the jump to Sahi needs a scanner and a tractor beam: Carla's note 532 instead
//   MGame::OnUpdate 0x1ac778        add-on entry calls: in free flight (no level mission, not mining, no autopilot) at
//                                   index 45 the Valkyrie call (conversation 46), at 84 the Supernova call (85), each
//                                   followed by two nextCampaignMission (the remake owns both add-ons); the chapter calls
//                                   (Status+0x178, set at 93 / 111 / 143): outside the Void, the level 5 s old and 12 s
//                                   of playing time since the step began (Status+0x100), Carla's hail and Keith's reply
//                                   (0xc60 + 2k / 0xc61 + 2k, k = 0 / 1 / 2); at 122-124 without a freelance mission and
//                                   not mining, an hour after the step began (then again every hour) Mrs Moonsprocket
//                                   complains (radio 0x1c: 0xc5c / 0xc5d at 122, else 0xc5e / 0xc5f, speaker 38)
//   Supernova                       after a space success conversation (MGame::OnTouchEnd, campaign_levels_b.md 3,
//                                   campaign_levels_c.md 1.5): 95 -> Thynome's orbit, 96 -> Alioth's, 100 -> docked at
//                                   Katashun, 110 -> docked at Thynome, 120 -> docked at Bak S'ondorr, 126 -> Katashun's
//                                   orbit, 127 -> Alioth's, 134 -> docked at Var Lupra, 144 -> Var Lupra's orbit again,
//                                   155 -> the orbit the Void was entered from, 161 -> Maissa's orbit, 162 -> docked at
//                                   Maissa; step 125's decoy scans (MGame::OnInitialize) in the freighter stations' orbits
// The conversation is shown by the flight HUD (DialogueRequested); the game is paused meanwhile (Navigation.Paused).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class StorySpace : MonoBehaviour
    {
        /// <summary>Show these pages; call the action (skipped) when the window closes.</summary>
        public event Action<List<DialoguePage>, Action<bool>> DialogueRequested;
        public bool DialogueOpen { get; private set; }

        SpaceLevel level;
        CampaignLevel campaign;
        float levelMs;
        bool briefingChecked, failed;

        /// <summary>Remake: a completed mission's success conversation waits SuccessPauseMs (the original opens it on the
        /// frame the mission completes, while the asteroid it was mined from is still bursting); meanwhile the mission
        /// counts as won (no failure) and the player can't be hurt, as if the conversation had already paused the game.</summary>
        const float SuccessPauseMs = 1500f;
        float successPauseMs = -1f;
        List<DialoguePage> pendingSuccess;
        int pendingReward;
        public bool SuccessPending => successPauseMs >= 0f;

        public void Setup(SpaceLevel spaceLevel, CampaignLevel campaignLevel)
        {
            level = spaceLevel;
            campaign = campaignLevel;
        }

        void Update()
        {
            if (Session.FreePlay || level == null || DialogueRequested == null || DialogueOpen || level.Leaving) return;
            levelMs += Time.deltaTime * 1000f;
            if (SuccessPending)
            {
                successPauseMs -= Time.deltaTime * 1000f;
                if (successPauseMs >= 0f) return;
                successPauseMs = -1f;
                int reward = pendingReward;
                Open(pendingSuccess, _ => AfterSuccess(reward));
                pendingSuccess = null;
                return;
            }
            if (level.Health != null && level.Health.Dead)
            {
                if (!failed && campaign != null) { failed = true; CountFailure(); }
                return;
            }
            if (!level.StartSequenceOver) return;
            if (!briefingChecked && levelMs > 5000f)
            {
                briefingChecked = true;
                var step = Story.Step;
                var briefing = step != null ? StoryTable.Shown(step.briefing) : null;
                if (campaign != null && briefing != null && briefing.Count > 0 && Story.Mission.visible && !Story.Mission.won)
                {
                    Open(briefing, _ => campaign.ResetClock());
                    return;
                }
            }
            if (campaign != null && !failed && campaign.Failed && Story.Index == campaign.BuiltIndex) { Fail(); return; }
            if (briefingChecked) CheckSuccess();   // successCheck: the same > 5000 ms, after dialogueEvent
            if (levelMs >= 5000f && !DialogueOpen) CheckAddonEntry();
            if (levelMs >= 5000f && !DialogueOpen && !level.Navigation.Jumping && (level.SystemJump == null || !level.SystemJump.Cinematic))
            {
                if (CheckReminder()) return;
                CheckPassengerComplaint();
                CheckChapterCall();
            }
            CheckDecoyScan();
        }

        /// <summary>Playing time since the step began (Status::getPlayingTime - Status+0x100), ms.</summary>
        static float StepAgeMs => (Session.PlaySeconds - Session.StoryStepStart) * 1000f;

        /// <summary>MGame::OnUpdate at 0x17 / 0x18 in free flight (no level mission, not mining): an hour of playing time after
        /// the step began, the passenger's nag (Tommy 533 / 534, Carla 535 / 536, voiced; the step clock restarts); at 0x18
        /// with a sort-13 item (the tractor beam) aboard, once, Carla's "Okay, let's go to Sahi." (1912, hint 0x25).</summary>
        bool CheckReminder()
        {
            int n = Story.Index;
            if ((n != 0x17 && n != 0x18) || campaign != null || level.FreelanceOrbit != null) return false;
            if (level.Mining != null && level.Mining.State != Flight.Mining.Phase.Idle) return false;
            if (StepAgeMs > 3600000f)
            {
                int k = UnityEngine.Random.Range(0, 2);
                var page = n == 0x17
                    ? new DialoguePage { speaker = 5, text = 533 + k, voice = k == 0 ? "MSG_MISSION_23_REMINDER" : "MSG_MISSION_23_REMINDER_2" }
                    : new DialoguePage { speaker = 6, text = 535 + k, voice = k == 0 ? "MSG_MISSION_24_REMINDER" : "MSG_MISSION_24_REMINDER_2" };
                Session.StoryStepStart = Session.PlaySeconds;
                Open(new List<DialoguePage> { page }, null);
                return true;
            }
            if (n == 0x18 && Shop.FirstMounted(level.Database, 13) != null && Session.Hints.Add(0x25))
            {
                Open(new List<DialoguePage> { new DialoguePage { speaker = 6, text = 1912, voice = "MISSION_REMINDER_24" } }, null);
                return true;
            }
            return false;
        }

        static readonly string[] ComplaintVoices =
            { "MOONSPROCKET_PASSENGER_TALK_0_0", "MOONSPROCKET_PASSENGER_TALK_0_1_Alt2", "MOONSPROCKET_PASSENGER_TALK_1_0", "MOONSPROCKET_PASSENGER_TALK_1_1" };

        /// <summary>MGame::OnUpdate at 0x7a-0x7c: Mrs Moonsprocket (Level::createRadioMessage 0x1c, image 0x26) asks to be
        /// dropped off, one of two lines, once an hour of playing time has passed since Status+0x100 (which it resets).</summary>
        void CheckPassengerComplaint()
        {
            int n = Story.Index;
            if (n < 0x7a || n > 0x7c || level.Traffic == null || StepAgeMs <= 3600000f) return;
            if (Session.FreelanceMission != null && !Session.FreelanceMission.IsEmpty) return;
            if (level.Mining != null && level.Mining.State != Flight.Mining.Phase.Idle) return;
            int text = (n == 0x7a ? 0xc5c : 0xc5e) + UnityEngine.Random.Range(0, 2);
            level.Traffic.QueueLine(text, 0x26, ComplaintVoices[text - 0xc5c]);
            Session.StoryStepStart = Session.PlaySeconds;
        }

        /// <summary>MGame::OnUpdate, Status+0x178: the chapter's radio call once, outside the Void, 12 000 ms of playing time
        /// after the step began (the level at least 5 s old: the caller).</summary>
        void CheckChapterCall()
        {
            if (!Session.StoryRadioPending || StepAgeMs <= 12000f || level.Layout.alienOrbit || level.Traffic == null) return;
            if (campaign != null && campaign.Cutscene) return;
            Session.StoryRadioPending = false;
            int k = Story.Index >= 143 ? 2 : Story.Index >= 111 ? 1 : 0;
            level.Traffic.QueueLine(0xc60 + 2 * k, 6, $"CARLA_ANNOYING_CALL_{k}_0");
            level.Traffic.QueueLine(0xc61 + 2 * k, 0, $"CARLA_ANNOYING_CALL_{k}_1");
        }

        bool decoyChecked;

        /// <summary>MGame::OnInitialize at index 125 (campaign_levels_c.md 3.5): a freighter mission station not scanned yet
        /// gets its bit (Status::getFreighterMissionStationBit) and Keith scans (0xaf4 + rnd(4) at 1500 ms, 0xafa + rnd(4)).</summary>
        void CheckDecoyScan()
        {
            if (decoyChecked || Story.Index != 125 || level.Traffic == null || levelMs < 1500f) return;
            decoyChecked = true;
            int bit = System.Array.IndexOf(Story.FreighterStations, level.Layout.stationIndex);
            if (bit < 0 || (Story.Mission.value & (1 << bit)) != 0) return;
            Story.Mission.value |= 1 << bit;
            int a = 0xaf4 + UnityEngine.Random.Range(0, 4), b = 0xafa + UnityEngine.Random.Range(0, 4);
            level.Traffic.QueueLine(a, 0, GenericVoice.For(a));
            level.Traffic.QueueLine(b, 0, GenericVoice.For(b));
        }

        void CheckSuccess()
        {
            // This level's orbit (Session.StationIndex already names the next one on the frame a ride / jump starts).
            var ctx = new StoryContext { docked = false, levelMs = levelMs, station = level.Layout.stationIndex };
            bool levelWon = campaign != null && Story.Index == campaign.BuiltIndex && campaign.Won;
            if (Story.Mission.won || !(Story.IsComplete(level.Database, ctx) || levelWon)) return;
            Story.Mission.won = true;
            // MGame::successCheck, index 0x26: the remaining freighters get 9 999 999 hull.
            if (Story.Index == 38 && campaign != null)
                foreach (var s in campaign.Ships) if (s != null && s.IsFreighter && s.Target.Alive) s.SetHull(9999999);
            if (Story.Index == 63 && campaign != null)
                foreach (var s in campaign.Ships) if (s != null && s.Race == Flight.Standing.Pirate) s.shootingEnabled = false;
            if (Story.Index == 73 && campaign != null)
                foreach (var s in campaign.Ships)
                    if (s != null && s.IsFreighter && s.Target.Alive) { s.SetHull(9999999); s.frozen = false; s.SetMoving(true); }
            int reward = Story.Mission.reward;
            var step = Story.Step;
            var success = step != null ? StoryTable.Shown(step.success) : null;
            if (success != null && success.Count > 0)
            {
                pendingSuccess = success;
                pendingReward = reward;
                successPauseMs = SuccessPauseMs;
                if (level.Health != null) level.Health.invulnerable = true;   // from this frame on (SpaceLevel keeps it set)
            }
            else AfterSuccess(reward);
        }

        /// <summary>MGame::OnTouchEnd after a campaign success conversation in space.</summary>
        void AfterSuccess(int reward)
        {
            Session.Credits += reward;
            int n = Story.Advance(level.Database);
            if (n == 15) { Session.StationIndex = 98; level.Dock(); }       // arrested: taken to Alioth
            else if (n == 22) level.Dock();                                       // back into Kappa's station
            else if (n == 65) level.TravelTo(100);                                // Khador freed: on to Kothar (MGame::OnTouchEnd 3246)
            else if (n == 74) { Session.StationIndex = 100; level.Dock(); }  // the convoy taken: docked at Kothar (3290)
            else if (n == 81) level.TravelTo(Session.VoidOrbit);                  // Alice's drive: after her into the Void (3270)
            // Supernova (MGame::OnTouchEnd ~3315, campaign_levels_c.md 1.5).
            else if (n == 95) level.TravelTo(10);                                 // "Meanwhile, back on Thynome station..."
            else if (n == 96 || n == 127) level.TravelTo(98);                     // on to Alioth
            else if (n == 100) { Session.StationIndex = 120; level.Dock(); }      // docked at Katashun
            else if (n == 110) { Session.StationIndex = 10; level.Dock(); }       // docked at Thynome (its lounge)
            else if (n == 120) { Session.StationIndex = 126; level.Dock(); }      // back at Bak S'ondorr
            else if (n == 126) level.TravelTo(120);                               // Harval and the refugees at Katashun
            else if (n == 134) { Session.StationIndex = 112; level.Dock(); }      // docked at Var Lupra
            else if (n == 144) level.TravelTo(112);                               // the array firing cutscene
            else if (n == 155) level.TravelTo(Session.VoidReturnStation >= 0 ? Session.VoidReturnStation : 98);   // out of the Void
            else if (n == 161) level.TravelTo(93);                                // "Meanwhile on Maissa..."
            else if (n == 162) { Session.StationIndex = 93; level.Dock(); }       // the end: docked at Maissa
        }

        /// <summary>MGame::gameOverCheck: Globals::lastCampaignMissionFailed / FailCount.</summary>
        static void CountFailure()
        {
            int index = Story.Index;
            if (Session.LastFailedMission == index) Session.FailCount++;
            else { Session.LastFailedMission = index; Session.FailCount = 1; }
        }

        /// <summary>Navigation.PlanetJumpRefused: index 24 needs a scanner and a tractor beam for the jump to Sahi (532, Carla).</summary>
        public bool RefusePlanetJump(int station)
        {
            // The Supernova's requirement notes (3213-3218).
            int refusal = Story.RequirementRefusal(level.Database, station);
            if (refusal >= 0) { OpenText(Localization.Get(refusal), 16, null); return true; }
            if (Session.FreePlay || Story.Index != 24 || station != Story.Mission.station) return false;
            if (Shop.FirstMounted(level.Database, 17) != null && Shop.FirstMounted(level.Database, 13) != null) return false;
            ShowPages(new List<DialoguePage> { new DialoguePage { speaker = 6, text = 532, voice = "MSG_MISSION_24_NO_EQUIPMENT_INSTALLED" } }, null);
            return true;
        }

        /// <summary>Remake: an event graph quest failed (EventRunner): the story's failure note, then the last save.</summary>
        public void FailQuest(string title)
        {
            if (failed) return;
            failed = true;
            OpenText(title + "\n\n" + Localization.Get(319), 16, () => level.LoadLastSave());
        }

        void Fail()
        {
            failed = true;
            CountFailure();
            int index = Story.Index;
            string text = Localization.Get(392);
            if (index == 38 || index == 40 || index == 41) text += "\n" + Localization.Get(527);
            text += "\n\n" + Localization.Get(319);
            OpenText(text, 16, () => level.LoadLastSave());
        }

        void CheckAddonEntry()
        {
            int index = Story.Index;
            if (index != Story.GameWonIndex && index != Story.Dlc1WonIndex) return;
            if (Story.IsLevelMission(Session.StationIndex)) return;
            if (level.Mining != null && level.Mining.State != Flight.Mining.Phase.Idle) return;
            if (level.Navigation != null && level.Navigation.Autopilot) return;
            var call = StoryTable.Step(index + 1);   // conversation 46 / 85
            if (call == null || call.success.Count == 0) return;
            Open(call.success, _ => { Story.Advance(level.Database); Story.Advance(level.Database); });
        }

        /// <summary>A conversation from another level script (the Kaamo siege's calls), paused like the story's.</summary>
        public void ShowPages(List<DialoguePage> pages, Action<bool> after)
        {
            if (DialogueRequested == null) { after?.Invoke(false); return; }
            Open(pages, after);
        }

        void Open(List<DialoguePage> pages, Action<bool> after)
        {
            DialogueOpen = true;
            if (level.Navigation != null) level.Navigation.Paused = true;
            DialogueRequested?.Invoke(pages, skipped =>
            {
                DialogueOpen = false;
                if (level.Navigation != null) level.Navigation.Paused = false;
                after?.Invoke(skipped);
            });
        }

        /// <summary>A one-page note (failure) as a page list with a literal text.</summary>
        public event Action<string, int, Action> MessageRequested;

        void OpenText(string text, int speaker, Action after)
        {
            DialogueOpen = true;
            if (level.Navigation != null) level.Navigation.Paused = true;
            MessageRequested?.Invoke(text, speaker, () =>
            {
                DialogueOpen = false;
                if (level.Navigation != null) level.Navigation.Paused = false;
                after?.Invoke();
            });
        }
    }
}
