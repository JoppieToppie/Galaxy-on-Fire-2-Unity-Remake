// FlightHints.cs
// The one-time tutorial windows of a flight level (MGame::OnUpdate 0x1ac778, the Globals::hints block after LAB_001adcca;
// the flags are saved, Session.Hints): once the level and its level script are 5 s old, no jump scene, no dialogue and no
// window open, the first hint of this list whose condition holds opens a ChoiceWindow (the game pauses, sounds pause):
//   0x17 596 booster       a booster mounted, campaign >= 2           0x21 590 Khador Drive   a jump drive aboard
//   0x22 593 cloak         a cloak                                    0x20 584 planet travel  no autopilot, campaign > 9,
//                                                                                              no level mission
//   (the rest only while no wingmen fly along or 0x15 was shown; otherwise 0x15 637 wingmen)
//   0x1c 648 reputation    hostile with any race (|standing| > 70)    once per level: 619 swipe to dodge at campaign 5
//   once per level: 1725 how to mine (campaign 2, not mining, hurt or the level script 40 s old); 0x11 620 while mining
//   0x12 621 asteroid classes (mining, campaign > 3)                  0x37 617 mining failed (campaign 2)
//   0x14 636 cargo full (campaign > 6)                                0x23 / 0x24 radio 443 / 444 in Nivelian space (Keith)
//   0x28 599 hacking                                                  0x29 600 gamma (at 0x5b in 110's orbit after radio 4,
//                                                                                   else wherever gamma rays hurt)
//   0x2c 602 volatile goods                                           0x2e 603 docking (0x5b at 110, radio 4 over)
//   0x2f 606 take off (0x5b at 110, docked, 10 aboard, radio 6 over)  0x30 609 stay docked (0x5c at 113, docked, 0 aboard)
// Keyboard / controller players get the +1 variants (585 / 591 / 594 / 597 / 604 / 607 / 610) with the #KEY tokens replaced
// (Globals::replaceKeyBindingTokens). Also: 0x2d the first Red Plasma (204) after campaign 0x8e -> radio 0x1a, Keith's 3161
// (PlayerGasCloud::update). The add-on calls and Loma's toll are StorySpace's and Traffic's.

using System;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using UnityEngine;

namespace GoF2Remake.World
{
    public class FlightHints : MonoBehaviour
    {
        /// <summary>A one-button ChoiceWindow (the game pauses until it closes).</summary>
        public event Action<string> HintRequested;
        /// <summary>A HUD message (Hud::hudEvent).</summary>
        public event Action<string> Message;

        SpaceLevel level;
        float levelMs;
        bool dodgeShown, mineHowShown;

        public void Setup(SpaceLevel spaceLevel)
        {
            level = spaceLevel;
            if (level.GasClouds != null) level.GasClouds.PlasmaCollected += OnPlasma;
        }

        /// <summary>A hint flag set now (Globals::hints[i] = 1); false when it was already.</summary>
        static bool First(int flag) => Session.Hints.Add(flag);
        static bool Seen(int flag) => Session.Hints.Contains(flag);

        void Update()
        {
            if (level == null || HintRequested == null || level.Leaving) return;
            float before = levelMs;
            levelMs += Time.deltaTime * 1000f;
            // MGame::OnUpdate: when the level script's clock passes 5000 ms with volatile goods aboard, HUD event 0x2d (3202).
            if (before < 5000f && levelMs >= 5000f && GalaxyMap.HasVolatileGoods) Message?.Invoke(Localization.Get(3202));
            if (levelMs < 5000f || !level.StartSequenceOver || Time.timeScale <= 0f) return;
            if (level.Dialogue || level.Cutscene || level.Navigation == null || level.Navigation.Jumping || level.Navigation.PauseMenuOpen) return;
            if (level.SystemJump != null && level.SystemJump.Cinematic) return;
            if (level.Health != null && level.Health.Dead) return;
            // Remake option (off by default): no tutorial popups, nothing marked shown; none in a Mod Builder test flight.
            if (!Settings.TutorialHints || Modding.ModBuilder.Active) return;
            Check();
        }

        void Show(int text, bool keyVariant = false)
        {
            bool keys = InputMode.Current != InputKind.Touch;
            HintRequested?.Invoke(KeyTokens(Localization.Get(keyVariant && keys ? text + 1 : text)));
        }

        void Check()
        {
            var db = level.Database;
            int cm = Session.WorldIndex;
            bool levelMission = level.Campaign != null || level.FreelanceOrbit != null;
            var mining = level.Mining;
            bool isMining = mining != null && mining.State == Mining.Phase.Mining;
            var docking = level.Docking;

            if (!Seen(0x17) && Shop.FirstMounted(db, 14) != null && cm >= 2) { First(0x17); Show(596, true); return; }
            if (!Seen(0x21) && GalaxyMap.HasKhadorDrive(Database.Shared))
            { First(0x21); Show(590, true); return; }   // Ship::hasJumpDrive (a real drive, not the remake's gateless rule)
            if (!Seen(0x22) && PlayerCloak.HasCloak(db, Session.ShipIndex)) { First(0x22); Show(593, true); return; }
            if (!Seen(0x20) && !level.Navigation.Autopilot && cm > 9 && !levelMission) { First(0x20); Show(584, true); return; }
            if (Session.Wingmen.Count > 0 && !Seen(0x15)) { First(0x15); Show(637); return; }

            if (!Seen(0x1c) && (Mathf.Abs(Session.Standing[0]) > 70 || Mathf.Abs(Session.Standing[1]) > 70)) { First(0x1c); Show(648); return; }
            if (!dodgeShown && cm == 5) { dodgeShown = true; Show(619); return; }
            if (!Seen(0x11))
            {
                if (!isMining && !mineHowShown && cm == 2 && level.Health != null
                    && (level.Health.Hp.hull < level.Health.Hp.maxHull || levelMs > 40000f))
                { mineHowShown = true; Show(1725); return; }
                if (isMining) { First(0x11); Show(620); return; }
            }
            if (!Seen(0x12) && isMining && cm > 3) { First(0x12); Show(621); return; }
            if (cm == 2 && !Seen(0x37) && mining != null && mining.LostGame) { First(0x37); Show(617); return; }
            if (!Seen(0x14) && cm > 6 && Shop.FreeCargo(db) <= 0) { First(0x14); Show(636); return; }
            if (!level.Layout.alienOrbit && level.Traffic != null && level.Traffic.SystemRace == 2)
            {
                if (!Seen(0x23)) { First(0x23); nivelianNow = true; level.Traffic.QueueLine(443, 0, GenericVoice.For(443)); return; }
                if (!Seen(0x24) && !nivelianNow) { First(0x24); level.Traffic.QueueLine(444, 0, GenericVoice.For(444)); return; }
            }
            if (!Seen(0x28) && docking != null && docking.IsDocked && docking.Hacking != null) { First(0x28); Show(599); return; }
            var radio = level.Campaign != null ? level.Campaign.Radio : null;
            int station = level.Layout.stationIndex;
            if (!Seen(0x29))
            {
                bool gamma = cm == 0x5b ? station == 0x6e && radio != null && radio.Over(4) : level.Health != null && level.Health.Gamma >= 0f;
                if (gamma) { First(0x29); Show(600); return; }
            }
            if (!Seen(0x2c) && GalaxyMap.HasVolatileGoods) { First(0x2c); Show(602); return; }
            if (!Seen(0x2e) && cm == 0x5b && station == 0x6e && radio != null && radio.Over(4)) { First(0x2e); Show(603, true); return; }
            if (!Seen(0x2f) && cm == 0x5b && station == 0x6e && docking != null && docking.IsDocked && Session.StoryCounter == 10 && radio != null && radio.Over(6))
            { First(0x2f); Show(606, true); return; }
            if (!Seen(0x30) && cm == 0x5c && station == 0x71 && docking != null && docking.IsDocked && Session.StoryCounter == 0)
            { First(0x30); Show(609, true); return; }
        }
        bool nivelianNow;

        /// <summary>PlayerGasCloud::update: the first Red Plasma (204) after campaign 0x8e -> Keith's radio 3161 (kind 0x1a).</summary>
        void OnPlasma(int item)
        {
            if (item != 204 || Session.FreePlay || Session.CampaignMission <= 0x8e || !First(0x2d) || level.Traffic == null) return;
            level.Traffic.QueueLine(3161, 0, GenericVoice.For(3161));
        }

        /// <summary>Globals::replaceKeyBindingTokens: the keys the controls are bound to (GameControls; the PC version's
        /// defaults unless rebound) on the keyboard, or the controller's buttons. A control without a controller button
        /// names the autopilot menu's (its entry is in that menu).</summary>
        public static string KeyTokens(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("#KEY_", StringComparison.Ordinal) < 0) return s;
            bool pad = InputMode.Current == InputKind.Gamepad;
            string K(UnityEngine.InputSystem.InputAction a)
            {
                string t = GameControls.KeyText(a, pad);
                if (t.Length == 0 && pad) t = GameControls.KeyText(GameControls.AutopilotMenu, true);
                return t.Length > 0 ? t : "–";
            }
            // The PC version's Action Menu is the quick menu (GameControls.ActionsMenu: E, D-pad left).
            string actionMenu = K(GameControls.ActionsMenu);
            return s.Replace("#KEY_DOCK", K(GameControls.Action))
                    .Replace("#KEY_ACTION_MENU", actionMenu)
                    .Replace("#KEY_AUTOPILOT", K(GameControls.AutopilotMenu))
                    .Replace("#KEY_KHADOR_DRIVE", K(GameControls.KhadorDrive))
                    .Replace("#KEY_WINGMEN", K(GameControls.Wingmen))
                    .Replace("#KEY_CLOAK", K(GameControls.Cloak))
                    .Replace("#KEY_FAST_FORWARD", K(GameControls.FastForward))
                    .Replace("#KEY_PRIMARY", K(GameControls.FirePrimary))
                    .Replace("#KEY_SECONDARY_WEAPONS", K(GameControls.SwitchSecondary))
                    .Replace("#KEY_SECONDARY", K(GameControls.FireSecondary))
                    .Replace("#KEY_BOOST", K(GameControls.Boost));
        }
    }
}
