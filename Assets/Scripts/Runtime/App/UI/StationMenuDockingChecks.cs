// StationMenuDockingChecks.cs
// Part of StationMenu: the checks run on docking (story success conversations, freelance delivery, the pirate base,
// the docking fine, the Kaamo Club, wingmen, the rescue shuttle, finished blueprints, Most Wanted, medals and their
// toasts) and the Void alarm.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class StationMenu
    {
        /// <summary>ModStation::OnInitialize / checkHints, the Most Wanted boards (wingmen_wanted.md 2.3, 2.8): criminals newly
        /// active on this board (3230 / 3231), the board hints 601 (the Terran board, from 128) and 613 (all boards, 162),
        /// and a board boss's ship on sale at Quineros (107, 3232).</summary>
        bool CheckWanted()
        {
            if (wantedChecked || level == null || level.Station == null || Session.FreePlay) return false;
            wantedChecked = true;
            var db = level.Database;
            int station = level.Station.index;
            int n = WantedBoard.ActivateNew(db, station);
            if (n == 1) ShowToast(Localization.Get(3230));
            else if (n > 1) ShowToast(Localization.Get(3231).Replace("#N", n.ToString()));
            if (station == 107)
                foreach (int ship in WantedBoard.QuinerosShips(db)) Story.AddDealerShip(107, ship);
            string note = null;
            if ((Session.WantedHints & 1) == 0 && Session.CampaignMission >= WantedBoard.AllBoards) Session.WantedHints |= 1;   // 613 says it all
            if ((Session.WantedHints & 1) == 0 && Session.CampaignMission >= WantedBoard.StorylineFirst && WantedBoard.Accessible(db, station))
            { Session.WantedHints |= 1; note = Localization.Get(601); }
            else if ((Session.WantedHints & 2) == 0 && Session.CampaignMission >= WantedBoard.AllBoards)
            { Session.WantedHints |= 2; note = Localization.Get(613); }
            else
            {
                int[] bosses = { 6, 12, 18, 24 };
                for (int k = 0; k < 4 && note == null; k++)
                {
                    int bit = 4 << k;
                    var st = WantedBoard.State(db, bosses[k]);
                    if ((Session.WantedHints & bit) != 0 || st == null || !st.terminated || bosses[k] >= db.Wanted.Count) continue;
                    Session.WantedHints |= bit;
                    note = Localization.Get(3232).Replace("#WANTED_NAME", db.Wanted[bosses[k]].name).Replace("#SHIP_NAME", GameNames.Ship(45 + k));
                }
            }
            if (note == null) return false;
            CloseHangar();
            storyDialogue.ShowMessage(note, 16, () => Select(launchButton));
            return true;
        }

        /// <summary>Achievements::checkForNewMedal on docking: "New medal!" (353) with each improved medal.</summary>
        bool CheckMedals()
        {
            if (medalsChecked || level == null) return false;
            medalsChecked = true;
            var improved = Achievements.Check(level.Database);
            medalQueue.Clear();
            foreach (int m in improved) if (m != 0) medalQueue.Enqueue(m);
            if (medalQueue.Count == 0) return false;
            if (medalToast == null) ShowNextMedal();
            return false;   // remake: the toasts don't hold up the station (the original's ChoiceWindows waited for OK)
        }

        /// <summary>ModStation::checkHints 0xee500: once each, one per frame: 0x1a all base medals (649), 0x1b all gold (650),
        /// all gold + the Supernova medals while blueprint 232 is locked (651, unlocked, autosave), 0x3a after campaign 0xa1
        /// with all gold + Supernova medals (or on Extreme) the Specter on sale at Katashun (3233, autosave).</summary>
        bool CheckMedalHints()
        {
            if (level == null || Session.FreePlay) return false;
            string text = null;
            if (!Session.Hints.Contains(0x1a) && Achievements.GotAllMedals) { Session.Hints.Add(0x1a); text = Localization.Get(649); }
            else if (!Session.Hints.Contains(0x1b) && Achievements.GotAllGoldMedals) { Session.Hints.Add(0x1b); text = Localization.Get(650); }
            else if (!Session.UnlockedBlueprints.Contains(232) && Achievements.GotAllGoldMedals && Achievements.GotAllSupernovaMedals)
            { Blueprints.Unlock(232); Session.Autosave(); text = Localization.Get(651); }
            else if (!Session.Hints.Contains(0x3a) && Session.CampaignMission > 0xa1 && (Session.IsExtreme || (Achievements.GotAllGoldMedals && Achievements.GotAllSupernovaMedals)))
            { Session.Hints.Add(0x3a); Session.Autosave(); text = Localization.Get(3233); }
            if (text == null) return false;
            ShowDialog(text, null, true);
            return true;
        }

        /// <summary>How long a medal's toast stays: the rarer, the longer (bronze 4 s, silver 5 s, gold 6 s, elite 7 s).</summary>
        static float MedalToastSeconds(int medal, int grade) =>
            medal >= Achievements.BaseCount ? 7f : grade == 1 ? 6f : grade == 2 ? 5f : 4f;

        /// <summary>ModStation::checkMedals 0xebe74: each new medal pays DAT_00251ff0[grade] (5000 gold / 2500 silver / 1000
        /// bronze; nothing on Extreme). The original shows one ChoiceWindow per medal (353, the plate, the hint 1552 + i)
        /// that waits for OK; remake: a toast at the top of the screen (the plate, 353, the name, the reward) that fades out
        /// after MedalToastSeconds (by grade; MedalToastQueuedSeconds while more are waiting, the last one its full time; the
        /// pointer resting on it pauses the countdown), one medal after another, and a tap / click on it opens the Status
        /// window on that medal.</summary>
        void ShowNextMedal()
        {
            if (medalToast != null) { medalToast.RemoveFromHierarchy(); medalToast = null; }
            if (medalQueue.Count == 0) return;
            int m = medalQueue.Dequeue();
            int grade = Achievements.Grade(m);
            int reward = Session.IsExtreme ? 0 : grade == 1 ? 5000 : grade == 2 ? 2500 : grade == 3 ? 1000 : 0;
            Session.Credits += reward;
            RefreshCredits();

            var toast = new VisualElement { name = "medalToast" };
            toast.AddToClassList("medal-toast");
            var plate = StatusWindow.MedalPlate(m, grade);
            plate.AddToClassList("medal-toast-plate");
            plate.pickingMode = PickingMode.Ignore;
            foreach (var c in plate.Children()) c.pickingMode = PickingMode.Ignore;
            toast.Add(plate);
            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.AddToClassList("medal-toast-texts");
            Label Line(string text, string cls)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                l.AddToClassList(cls);
                texts.Add(l);
                return l;
            }
            // The grade's colour (gold / silver / bronze, the elite orange) on the border, the top band and the title; an
            // unearned grade (elite 0) in the theme's amber.
            var tint = grade > 0 ? StatusWindow.MedalTint(m >= Achievements.BaseCount, grade) : new Color(0.94f, 0.70f, 0.35f);
            tint.a = 1f;
            toast.style.borderTopColor = tint;
            toast.style.borderLeftColor = toast.style.borderRightColor = toast.style.borderBottomColor = new Color(tint.r, tint.g, tint.b, 0.55f);
            Line(Localization.Get(353).ToUpperInvariant(), "medal-toast-title").style.color = tint;
            Line(Localization.Get(1507 + m), "medal-toast-name");
            if (reward > 0) Line($"+{reward:N0} $", "medal-toast-reward");
            Line(Localization.Extra(InputMode.Current == InputKind.Touch ? "medalToastTap" : "medalToastClick",
                InputMode.Current == InputKind.Touch ? "Tap to see your medals" : "Click to see your medals"), "medal-toast-hint");
            toast.Add(texts);
            toast.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                Play(buttonRelease);
                OpenMedal(m);
            });
            toast.RegisterCallback<PointerEnterEvent>(_ => medalToastHovered = true);
            toast.RegisterCallback<PointerLeaveEvent>(_ => medalToastHovered = false);
            (safeArea ?? root).Add(toast);
            medalToast = toast;
            medalToastLeft = medalQueue.Count > 0 ? MedalToastQueuedSeconds : MedalToastSeconds(m, grade);
            medalToastFading = medalToastHovered = false;
            // Remake's own medal sound (the original used the info window's blip): a rising synthesized chime with echoes.
            if (medalSound == null) medalSound = Resources.Load<AudioClip>("GoF2Sfx/MedalToast");
            Play(medalSound != null ? medalSound : infoSound);
            // Drops in and grows (the transition needs a frame), with a short bright flash.
            toast.schedule.Execute(() => toast.AddToClassList("medal-toast--shown"));
            toast.AddToClassList("medal-toast--flash");
            toast.schedule.Execute(() => toast.RemoveFromClassList("medal-toast--flash")).ExecuteLater(350);
        }

        /// <summary>Every frame: the shown toast's time, its fade-out, then the next medal.</summary>
        void UpdateMedalToast()
        {
            if (medalToast == null) return;
            if (medalToastFading || !medalToastHovered) medalToastLeft -= Time.unscaledDeltaTime;   // read while pointed at
            if (!medalToastFading && medalToastLeft <= 0f)
            {
                medalToastFading = true;
                medalToast.RemoveFromClassList("medal-toast--shown");
                medalToastLeft = MedalToastFadeSeconds;
            }
            else if (medalToastFading && medalToastLeft <= 0f) ShowNextMedal();
        }

        /// <summary>The toast tapped / clicked: the Status window with that medal selected (the rest of the queue goes on).</summary>
        void OpenMedal(int medal)
        {
            if (medalToast != null) { medalToast.RemoveFromHierarchy(); medalToast = null; }
            if (DialogOpen || SystemMenuOpen || status == null) return;
            if (!status.IsOpen) OpenStatus();
            status.SelectMedal(medal);
            if (medalQueue.Count > 0) ShowNextMedal();
        }

        void StartVoidAlarm()
        {
            var story = StoryAssets.Load();
            if (story == null || level == null) return;
            if (level.musicSource != null)
            {
                level.musicSource.Stop();
                level.musicSource.clip = Modding.ModMusic.Replace(story.voidBattle);
                level.musicSource.loop = true;
                if (story.voidBattle != null) level.musicSource.Play();
            }
            if (alarm == null) { alarm = gameObject.AddComponent<AudioSource>(); alarm.playOnAwake = false; alarm.loop = true; alarm.spatialBlend = 0f; }
            alarm.clip = GoF2Remake.Modding.ModSounds.Get(story.alert);
            alarm.volume = 0.198f * GoF2Remake.Flight.Sfx.EventGain * Settings.SfxVolume;
            if (alarm.clip != null) { alarm.Stop(); alarm.Play(); }
        }

        // ---- story (ModStation::OnUpdate / OnTouchEnd) ---------------------------------------------------

        /// <summary>Status::missionCompleted(docked): the completed campaign mission's success conversation.</summary>
        bool CheckStory()
        {
            if (level == null || level.Station == null || Session.FreePlay) return false;
            // Remake co-op: the story caught up with the leader's (a loaner given or the own ship back): the turntable and
            // the menu's story locks follow.
            if (coopCatchUp != CoopCampaign.CatchUpSerial)
            {
                coopCatchUp = CoopCampaign.CatchUpSerial;
                if (level.ShownShip != Session.ShipIndex) level.ReplacePlayerShip(Session.ShipIndex);
                ApplyStoryLocks();
            }
            var ctx = new StoryContext
            {
                docked = true,
                inLounge = level.View == StationView.Lounge && !level.IntroPlaying,
                station = level.Station.index,
            };
            if (ctx.inLounge && SpecialLounge(ctx.station)) return true;
            bool remote = CoopCampaign.RemoteWon;   // remake co-op: another crew member's game won the step
            if (!remote && !Story.IsComplete(level.Database, ctx)) return false;
            Story.Mission.won = true;
            if (!remote) CoopCampaign.ReportWon(Story.Index);
            CloseHangar();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            var step = Story.Step;
            var success = step != null ? StoryTable.Shown(step.success) : null;
            if (success != null && success.Count > 0)
                storyDialogue.Show(success, skipped =>
                {
                    // DialogueWindow::OnTouchEnd: skipping step 15's conversation starts the alarm too.
                    if (skipped && !Session.FreePlay && Session.CampaignMission == 0xf) StartVoidAlarm();
                    AfterStorySuccess();
                });
            else AfterStorySuccess();
            return true;
        }

        /// <summary>ModStation::OnUpdate's lounge searches (campaign_flow.md 3.1 5): at 116 the Pescal Inartu bars other than
        /// Maissa (90, 91, 92, 94) each play their flavour line once (2716 + slot, bit slot of the mission value); at 148 the
        /// brokers' bars Kappa (55), Inari Onu (66) and Coppolite (9) each play conversation 148 / 149 / 150 once (bits 1 / 2 /
        /// 4), and Kalun Amir (96) sets the index to 151 and plays its conversation (closing it advances to 152).</summary>
        bool SpecialLounge(int station)
        {
            var m = Story.Mission;
            if (Story.Index == 116 && (station == 90 || station == 91 || station == 92 || station == 94))
            {
                int slot = station == 94 ? 3 : station - 90;
                if ((m.value & (1 << slot)) != 0) return false;
                m.value |= 1 << slot;
                storyDialogue.Show(new System.Collections.Generic.List<DialoguePage> { new DialoguePage { speaker = 0, text = 2716 + slot, voice = $"MISSION_ALT_116_{slot}" } },
                                   _ => Select(launchButton));
                return true;
            }
            if (Story.Index != 148) return false;
            if (station == 96)
            {
                CoopCampaign.OnIndexSet(148);   // remake co-op: the leader follows
                Session.CampaignMission = 151;
                Session.StoryMission = StoryMission.From(StoryTable.Step(151));
                return false;   // the normal path: 151 (lounge at Kalun Amir) is complete right here
            }
            int k = station == 55 ? 0 : station == 66 ? 1 : station == 9 ? 2 : -1;
            if (k < 0 || (m.value & (1 << k)) != 0) return false;
            m.value |= 1 << k;
            var pages = StoryTable.Step(148 + k)?.success;
            if (pages == null || pages.Count == 0) return false;
            storyDialogue.Show(pages, _ => Select(launchButton));
            return true;
        }

        /// <summary>ModStation::OnTouchEnd after a campaign success conversation.</summary>
        void AfterStorySuccess()
        {
            var db = level.Database;
            int reward = Story.Mission.reward;
            // Remake co-op: the ending too, in the session (the main menu scene loads without leaving it; the credits'
            // end brings this player back here for Keith's epilogue, 44).
            if (Story.Index == 43 && CoopCampaign.Active)
            {
                Session.Credits += CoopCampaign.Share(reward);
                Session.EndingPending = true;
                if (Application.CanStreamedLevelBeLoaded(menuScene)) SceneManager.LoadScene(menuScene);
                return;
            }
            if (Story.Index == 43)
            {
                // ModStation::OnTouchEnd: index 43 starts the ending (the credits over the space backdrop, EndingCredits in the
                // main menu scene); nextCampaignMission (-> 44) and the station module follow when it is over.
                Session.Credits += reward;
                Session.EndingPending = true;
                BackToMenu();
                return;
            }
            int n = Story.Advance(db);
            Story.AfterDockedAdvance(n, level.Station.index);                // Khador's ships and rum at Kothar (77, 84)
            if (n == WantedBoard.StorylineFirst) wantedChecked = false;      // Status::activateNewWanted + hint 601 at step 128
            if (Story.ShipSwapped(n)) level.ReplacePlayerShip(Session.ShipIndex);   // the loaner / the own ship on the turntable
            if (n == 9 || n == 44 || n == 75 || n == 76 || n == 83)
            {
                // ModStation restarts the station module here so that OnInitialize runs again and the next conversation
                // (the new step is "docked here") follows. The remake stays in the scene instead: the per-docking story
                // tweaks, the autosave and the menu locks again; the next frame's CheckStory opens the conversation.
                int station = level.Station.index;
                Story.OnDocked(db, station, level.Stock);
                if (Story.AutosaveAllowed(station)) Session.Autosave();
                ApplyStoryLocks();
                return;
            }
            int launchTo = n switch { 89 => 109, 99 => 10, 109 => 114, 119 => 10, 133 => 120, 144 => 112, 160 => 10, _ => -1 };
            if (n == 78) { level.Launch(); return; }   // escape from Valkyrie: departStation + space
            if (launchTo >= 0)
            {
                // initStreamOutPosition + departStation(target): straight into the story orbit.
                Session.PreviousStationIndex = Session.StationIndex;
                Session.StationIndex = launchTo;
                Session.ArrivedByTravel = n != 144 && n != 160;
                Session.LaunchedFromStation = false;
                SceneManager.LoadScene(level.spaceScene);
                return;
            }
            Session.Credits += CoopCampaign.Share(reward);   // remake co-op: shared out across the crew (its setting)
            Session.Autosave();
            ApplyStoryLocks();
            Select(launchButton);
        }

        // ---- freelance (ModStation::OnUpdate: Status::missionCompleted / missionFailed, docked) --------------

        bool CheckFreelance()
        {
            if (level == null || level.Station == null) return false;
            var result = Freelance.CheckDocked(level.Station.index);
            if (result == Freelance.DockResult.None) return false;
            CloseHangar();
            lounge?.CloseChat(false);
            if (missions != null && missions.IsOpen) missions.Close();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            var m = Freelance.Mission;
            bool success = result == Freelance.DockResult.Success;
            int textId;
            string text = success ? Freelance.SuccessText(out textId) : Freelance.FailureText(out textId);
            string voiceLine = Freelance.Voice(textId);
            storyDialogue.ShowAgentMessage(text, m.clientName, m.clientPortrait, () =>
            {
                // Multiplayer: a squadmate may have ended it meanwhile (their result already paid this player's share).
                if (Freelance.Mission != m) { Select(launchButton); return; }
                if (success)
                {
                    // Layout::showMissionRewardMessage + Mission_accomplished (36), changeCredits(reward + bonus).
                    int paid = Freelance.Succeed(true);
                    ShowToast($"{Localization.Get(216)} +{ItemInfo.Credits(paid)}");
                    var combat = GoF2Remake.Flight.CombatAssets.Load();
                    Play(combat != null ? combat.missionAccomplished : null);
                }
                else Freelance.Fail();
                Select(launchButton);
            }, voiceLine);
            return true;
        }

        /// <summary>ModStation::OnInitialize / checkHints (npc_combat_specials.md 3.6, 3.7): a pirate base's station is unmanned
        /// (434 from Security, closing it relaunches at once); after an outpost kill the next docking pays 20000 (442). Both
        /// voiced (434: getDialogueSoundId(0x1b2, a Nivelian agent) MSG_PIRATE_STATION_NO_ENTRANCE; 442: sound 0x24d
        /// MSG_PIRATE_STATION_REWARD; #45: they were silent).</summary>
        bool CheckPirateBase()
        {
            if (baseChecked || level == null || level.Station == null) return false;
            baseChecked = true;
            int st = level.Station.index;
            if (PirateBases.StationHasBase(st))
            {
                CloseHangar();
                storyDialogue.ShowAgentMessage(Localization.Get(434), Localization.Get(PirateBases.SecurityName), PirateBases.Portrait, level.Launch,
                                               GenericVoice.For(434, 2));
                return true;
            }
            if (!Session.PirateBaseRewardPending) return false;
            Session.PirateBaseRewardPending = false;
            storyDialogue.ShowAgentMessage(Localization.Get(442), Localization.Get(PirateBases.NivelianName), PirateBases.Portrait, () =>
            {
                Session.Credits += PirateBases.Reward;
                RefreshCredits();
                ShowToast($"+{ItemInfo.Credits(PirateBases.Reward)}");
                Session.Autosave();
            }, GenericVoice.For(442, 2));
            return true;
        }

        /// <summary>ModStation::OnInitialize 0xe8080 (combat_equipment.md 7): an enemy race's station (205: |standing| / 100 *
        /// 2800 +- 100) or one whose forces the player attacked (206: rank * 150 + 1000) demands a bribe, x10 hardcore. Yes =
        /// pay, the attack forgotten (the standing stays); not enough credits = 203; No = straight back into space.</summary>
        bool CheckDockingFine()
        {
            if (fineChecked || level == null || level.Station == null) return false;
            fineChecked = true;
            // ModStation::OnInitialize skips the whole block after a load (setGameLoaded sets +0xac): #131, a save made at a
            // station with a low reputation charged the fine at every load.
            bool loaded = Session.ArrivedByLoad;
            Session.ArrivedByLoad = false;
            if (loaded) return false;
            int st = level.Station.index;
            var sys = level.Database.Systems.Find(s => s.index == level.Station.system);
            int race = sys != null ? sys.raceId : -1;
            if (st == 100 || st == 101 || st == 108 || level.Station.system == 25 || race < 0 || race > 3 || PirateBases.StationHasBase(st)) return false;
            if (!Session.FreePlay && Session.CampaignMission == 0x30) return false;
            bool enemy = GoF2Remake.Flight.Standing.IsEnemy(race);
            bool attacked = Session.AttackedStations.Contains(st);
            if (!enemy && !attacked) return false;
            int fine = enemy ? (int)(Mathf.Abs(GoF2Remake.Flight.Standing.Toward(race)) / 100f * 2800f) + Random.Range(0, 200) - 100
                             : Session.Rank * 150 + 1000;
            if (Session.IsExtreme) fine *= 10;
            CloseHangar();
            ShowChoice(Localization.Get(enemy ? 205 : 206).Replace("#C", ItemInfo.Credits(fine)), Localization.Get(134), Localization.Get(135), () =>
            {
                if (Session.Credits < fine)
                {
                    ShowDialog(Localization.Get(203).Replace("#C", ItemInfo.Credits(fine - Session.Credits)), level.Launch, true);
                    return;
                }
                Session.Credits -= fine;
                Session.AttackedStations.Remove(st);
                RefreshCredits();
                Session.Autosave();
            }, level.Launch);
            return true;
        }

        /// <summary>ModStation::OnInitialize 0xe8080 at the Kaamo Club (kaamo_club.md 4): state 1 -> the 18-page first
        /// visit and state 2; state 2 -> 476 (not enough credits / buskat) or 477 -> Yes: pay, 479-484 (remake), 485, owned.</summary>
        bool CheckKaamo()
        {
            if (kaamoChecked || level == null || level.Station == null) return false;
            kaamoChecked = true;
            if (level.Station.index != KaamoClub.Station || Session.KaamoState < 1 || Session.KaamoState > 2) return false;
            CloseHangar();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            if (Session.KaamoState == 1)
            {
                Session.KaamoState = 2;
                storyDialogue.Show(KaamoClub.FirstVisitPages(), _ => { Session.Autosave(); Select(launchButton); });
                return true;
            }
            if (!KaamoClub.CanBuy) { ShowDialog(Localization.Get(476), null, true); return true; }
            ShowDialog(Localization.Get(477), () =>
            {
                KaamoClub.Buy();
                level.Stock.items = Session.KaamoItems;   // the (cleared) storage is the station's stock now
                RefreshCredits();
                Session.Autosave();
                storyDialogue.Show(KaamoClub.PurchasePages(), _ => ShowDialog(Localization.Get(485), null, true));
            });
            return true;
        }

        /// <summary>ModStation::checkHints: an expired wingman contract ends here, 313 from the first wingman.</summary>
        bool CheckWingmenContract()
        {
            if (wingmenChecked) return false;
            wingmenChecked = true;
            Session.WingmanShowEmp = true;   // Status+0xf8 reset on the station visit
            if (!Wingmen.Expired) return false;
            // The first wingman's voice (race + portrait; body 10 is the only female face, AgentGenerator.CreatePortrait).
            var face = Session.WingmanPortrait;
            string voiceLine = GenericVoice.For(313, Session.WingmanRace, face == null || face.Length == 0 || face[0] != 10, face);
            storyDialogue.ShowAgentMessage(Localization.Get(313), Session.Wingmen[0], face, Wingmen.Dismiss, voiceLine);
            return true;
        }

        /// <summary>ModStation::OnInitialize: stranded in a system without gate routes (campaign &gt; 16, not at 101, no jump
        /// drive, no Khador Drive in the hold) -> 335, the interstellar shuttle to Dis (Magnetar, station 70) for 25 000;
        /// without the credits 203 (ModStation::OnTouchEnd 0xea4ec).</summary>
        bool CheckRescue()
        {
            if (rescueChecked || level == null || level.Station == null) return false;
            rescueChecked = true;
            var db = level.Database;
            if (Session.FreePlay || level.Station.index == 101 || Story.Index <= 16) return false;
            var sys = db.Systems.Find(s => s.index == level.Station.system);
            if (sys == null || (sys.jumpRoutesTo != null && sys.jumpRoutesTo.Count > 0)) return false;
            if (GalaxyMap.HasJumpDrive(db) || Session.Cargo.Exists(c => c.item == GalaxyMap.KhadorDriveItem)) return false;
            ShowDialog(Localization.Get(335), () =>
            {
                if (Session.Credits < 25000) { ShowDialog(Localization.Get(203).Replace("#C", ItemInfo.Credits(25000 - Session.Credits)), null, true); return; }
                Session.Credits -= 25000;
                Session.PreviousStationIndex = Session.StationIndex;
                Session.StationIndex = 70;
                SceneManager.LoadScene(gameObject.scene.name);   // SetCurrentApplicationModule(5): the station module at Dis
            });
            return true;
        }

        /// <summary>ModStation::checkPendingProducts 0xee258 (once per docking): blueprint products waiting here move to the
        /// hold, 213 "The following items have been moved to your cargo hold:" + one line each.</summary>
        bool CheckPendingProducts()
        {
            if (pendingChecked || level == null || level.Station == null) return false;
            pendingChecked = true;
            var moved = Blueprints.CollectPending(level.Database, level.Station.index);
            // Remake mods: a ship blueprint's ship finished for this station is taken now, like a bought ship.
            string ships = new Hangar(level.Database, level.Stock).DeliverBuiltShips();
            if (ships != null)
            {
                level.ReplacePlayerShip(Session.ShipIndex);
                if (moved.Count == 0) { ShowDialog(ships, null, true); return true; }
            }
            if (moved.Count == 0) return false;
            string text = Localization.Get(213);
            foreach (var p in moved) text += $"\n{p.quantity}x {ItemInfo.ItemName(p.item)}";
            ShowDialog(text, null, true);
            return true;
        }

        // ---- per frame -----------------------------------------------------------------------------------

        /// <summary>ModStation::OnInitialize and ModStation::checkHints: what the station has to say, one at a time, while no
        /// window is open; true when something opened.</summary>
        bool RunDockingChecks()
        {
            if (DialogOpen || SystemMenuOpen || HangarOpen) return false;
            arrivalChecksRun = true;
            if (CheckPirateBase() || CheckDockingFine() || CheckStory() || CheckFreelance() || CheckKaamo() || CheckPendingProducts()) return true;
            if (!storyDialogue.IsOpen && (CheckRescue() || CheckMedals() || CheckMedalHints())) return true;
            return CheckWanted() || CheckWingmenContract();
        }
    }
}
