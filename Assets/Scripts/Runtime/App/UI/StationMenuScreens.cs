// StationMenuScreens.cs
// Part of StationMenu: opening and closing the station's screens (hangar window, Missions, Status, lounge, star map),
// the launch and its refusals, the yes / no dialogs and toasts, and Esc / B stepping back.

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
        /// <summary>Station button 0 (ModStation::OnKeyPress): the Hangar window over the main view.</summary>
        void OpenHangar()
        {
            if (HangarOpen || level == null || !Story.HangarUnlocked || ArrivalPending) return;
            if (RefuseObserver()) return;
            if (level.View != StationView.Hangar) level.SetView(StationView.Hangar);
            FirstVisitHint(8, 622);   // before the first row's selection hint
            hangarWindow.Open();
            hangarConfirmHeld = true;   // #104: the press that opened it isn't also the hangar's Buy
            level.SetHangarWindowOpen(true);
            boozeAtOpen = BoozeInHold();   // ModStation::OnKeyPress: ModStation+0xcc
            root.AddToClassList("hangar-open");
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            OnViewChanged();
        }

        /// <summary>ModStation::OnKeyPress: a window's first opening shows its help once (Layout::initHelpWindow; the flag
        /// in Globals::hints): hangar 622 (8), lounge 627 (0xd), map 628 (0xe, campaign &gt; 15) or the system map 631
        /// (0xf, earlier), missions 635 (0x13), status 640 (0x18, phones only). Keyboard / controller: the +1 variants
        /// where the original has them.</summary>
        void FirstVisitHint(int flag, int text, bool keyVariant = false)
        {
            if (DialogOpen || !Settings.TutorialHints || !Session.Hints.Add(flag)) return;   // the tutorial popups option
            ShowDialog(HintText(text, keyVariant), null, true);
        }

        static string HintText(int text, bool keyVariant) =>
            World.FlightHints.KeyTokens(Localization.Get(keyVariant && InputMode.Current != InputKind.Touch ? text + 1 : text));

        public void CloseHangar()
        {
            if (!HangarOpen) return;
            if (!hangarWindow.ReadyToClose()) return;   // an uncommitted blueprint shipment asks first
            hangarWindow.Close();
            level?.SetHangarWindowOpen(false);
            // ModStation::OnTouchEnd: the booze gained during this visit counts for Personal Need (Status+0xa8).
            int gained = BoozeInHold() - boozeAtOpen;
            if (gained > 0) Session.BoozeBought += gained;
            root.RemoveFromClassList("hangar-open");
            OnViewChanged();
            Select(hangarButton);
        }

        /// <summary>The Missions window (129) over the current view.</summary>
        void OpenMissions()
        {
            if (level == null || missions == null || missions.IsOpen || !Story.MapUnlocked) return;
            CloseHangar();
            lounge?.CloseChat(false);
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            missions.Open();
            BuildHints(InputMode.Current);
            FirstVisitHint(0x13, 635);
        }

        /// <summary>The Status window (169).</summary>
        void OpenStatus()
        {
            if (level == null || status == null || status.IsOpen) return;
            CloseHangar();
            lounge?.CloseChat(false);
            if (missions != null && missions.IsOpen) missions.Close();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            status.Open();
            BuildHints(InputMode.Current);
            if (Application.isMobilePlatform) FirstVisitHint(0x18, 640, true);
        }

        public void OnStatusClosed()
        {
            Select(statusButton);
            BuildHints(InputMode.Current);
        }

        public void OnMissionsClosed()
        {
            Select(missionsButton);
            BuildHints(InputMode.Current);
        }

        /// <summary>A lounge voice greeting (2D, SFX not paused); null stops the current one.</summary>
        public void PlayVoice(AudioClip clip)
        {
            if (voiceSource == null) return;
            voiceSource.Stop();
            if (clip == null) return;
            voiceSource.clip = GoF2Remake.Modding.ModSounds.Get(clip);
            voiceSource.volume = Settings.VoiceVolume;
            voiceSource.Play();
        }

        /// <summary>Focus for keyboard / controller (not in touch mode).</summary>
        public void Focus(VisualElement e) => Select(e);

        /// <summary>Credits changed outside the hangar window (a deal in the lounge).</summary>
        public void RefreshCredits() => BuildHints(InputMode.Current);

        /// <summary>Multiplayer: a trade with another player changed the hold and the credits (NetTradeClient).</summary>
        public void CargoChanged()
        {
            hangarWindow?.StockChanged();
            RefreshCredits();
        }

        void OpenLounge()
        {
            if (level == null || ArrivalPending || !Story.LoungeUnlocked(level.Station != null ? level.Station.index : -1)) return;
            if (RefuseObserver()) return;
            CloseHangar();
            level?.SetView(StationView.Lounge);
            FirstVisitHint(0xd, 627);
        }

        /// <summary>ModStation::OnKeyPress case 2: the star map (station mode; jump mode with a Khador Drive). Refused while
        /// the hold is overloaded (204). A picked station departs at once (StarMap::depart): the launch sequence, then the
        /// autopilot to it or, for another system with a drive, the Khador charge.</summary>
        void OpenMap()
        {
            if (level == null || StarMap.IsOpen || !Story.MapUnlocked || ArrivalPending) return;
            if (RefuseObserver()) return;
            if (new Hangar(level.Database, level.Stock).Overloaded) { ShowDialog(Localization.Get(204), null, true); return; }
            if (Story.MapRefusal is string refusal) { ShowDialog(refusal, null, true); return; }   // index 77: take the Cronus
            CloseHangar();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            root.AddToClassList("station-map-open");
            var map = StarMap.Open(level.Database, StarMapMode.Station, GalaxyMap.HasJumpDrive(level.Database), OnMapClosed);
            if (map == null) { root.RemoveFromClassList("station-map-open"); return; }
            int cm = Session.WorldIndex;
            if (!Settings.TutorialHints) { }   // the tutorial popups option
            else if (cm > 15 && Session.Hints.Add(0xe)) map.ShowHint(HintText(628, true));
            else if (cm < 16 && Session.Hints.Add(0xf)) map.ShowHint(HintText(631, true));
            // Modified: the same help any time, behind the map's "?" (top right).
            map.SetHelp(MapHelpText);
            // Modified: and the station's title with "| MAP" in its header, as the station's own views.
            map.SetStationTitle(root.Q<Label>("stationTitle")?.text);
        }

        /// <summary>Remake: a station picked on the Missions window's map: the Map button's departure (an overloaded hold
        /// stays, 204).</summary>
        public void TravelFromMap(StarMapResult result)
        {
            if (new Hangar(level.Database, level.Stock).Overloaded) { ShowDialog(Localization.Get(204), null, true); return; }
            if (missions != null && missions.IsOpen) missions.Close();
            OnMapClosed(result);
        }

        void OnMapClosed(StarMapResult result)
        {
            root.RemoveFromClassList("station-map-open");
            if (result.station < 0)
            {
                ApplyInputMode();
                // Rework (the controller walk, docs/UI-DESIGN.md "Focus"): back on the Map button for the keys and a controller;
                // nothing had the focus, so the D-pad and A did nothing until B. A frame later: the map's panel lets go first.
                root.schedule.Execute(() =>
                {
                    var now = root.focusController?.focusedElement;
                    if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing && !StarMap.IsOpen && (now == null || now == DefaultFocus()))
                        Select(mapButton != null && mapButton.enabledInHierarchy ? mapButton : DefaultFocus());
                }).ExecuteLater(1);
                return;
            }
            Session.ProgrammedStation = result.station == Session.StationIndex ? -1 : result.station;
            Session.RouteSafest = result.safestRoute;
            Session.InstantJump = result.instantJump;
            Session.EnergyCellsForNextJump = result.instantJump ? result.cells : 0;
            if (RefuseLaunchForStory()) return;
            level.Launch();
        }

        /// <summary>ModStation::leaveStation at campaign index 21: "Equip the EMP bombs before leaving." (531) without an EMP
        /// bomb (items 41-43) mounted.</summary>
        /// <summary>ModStation::leaveStation 0xec1ec: at 6 / 7 a ship with no fire power or no shield / armor (combined HP =
        /// base HP) stays: 529 (nothing to mount in the hold) or 530 (mount what is in the hold); at 20 / 21 in the target
        /// station 531 (21: without an EMP bomb 41-43); at 77 anything but the Cronus 326.</summary>
        bool RefuseLaunchForStory()
        {
            if (Session.FreePlay) return false;
            var db = level.Database;
            int n = Story.Index, station = level.Station != null ? level.Station.index : -1;
            int text = -1;
            if ((n == 6 || n == 7) && (Shop.FirePower(db) == 0f || Shop.CombinedHp(db) == Shop.BaseHp(db)))
                text = Session.Cargo.Exists(c => { int t = db.Item(c.item)?.TypeId ?? 4; return t == 0 || t == 3; }) ? 530 : 529;
            else if (n == 20 && station == Story.Mission.station) text = 531;
            else if (n == 21 && station == Story.Mission.station && !Session.Equipment.Exists(e => e.item >= 41 && e.item <= 43)) text = 531;
            else if (n == 77 && Session.ShipIndex != 37) text = 326;
            if (text < 0) return false;
            ShowDialog(Localization.Get(text), null, true);
            return true;
        }

        /// <summary>Multiplayer: another device of this player's server profile controls it, so this one stays on the main
        /// view (NetProfileClient.Refusal): no hangar, lounge, map or launch.</summary>
        bool RefuseObserver()
        {
            if (!(GoF2Remake.Multiplayer.NetProfileClient.Refusal is string text)) return false;
            ShowToast(text);
            return true;
        }

        /// <summary>ModStation::leaveStation: refused while the cargo hold is overloaded (204). Remake: launches at once, without
        /// the original's "Depart the station?" (397).</summary>
        void AskLaunch()
        {
            if (ArrivalPending) return;
            if (RefuseObserver()) return;
            if (new Hangar(level.Database, level.Stock).Overloaded) { ShowDialog(Localization.Get(204), null, true); return; }
            if (RefuseLaunchForStory()) return;
            level.Launch();
        }

        /// <summary>ChoiceWindow: yes / no, or a message with one button ('info').</summary>
        public void ShowDialog(string text, System.Action onYes, bool info = false)
        {
            dialogAction = onYes;
            dialogNoAction = null;
            root.Q<Label>("dialogText").text = text;
            InputGlyph.SetText(dialogYes, info ? Localization.Extra("ok", "OK") : Localization.Get(134).ToUpperInvariant());
            InputGlyph.SetText(dialogNo, Localization.Get(135).ToUpperInvariant());
            dialogNo.style.display = info ? DisplayStyle.None : DisplayStyle.Flex;
            dialog.AddToClassList("station-dialog-backdrop--shown");
            Play(infoSound);
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            Select(dialogYes);
        }

        /// <summary>Remake (#83): after "Keep" (331), whether the equipment mounted now moves to the new ship (true) or stays on
        /// the old hull in the Kaamo Club (false); asked only while Settings.KaamoKeepsEquipment would keep it there and
        /// something movable is mounted (Hangar.KeepAsksAboutEquipment), else 'then(false)' at once.</summary>
        public void AskMoveEquipment(System.Action<bool> then)
        {
            if (!Hangar.KeepAsksAboutEquipment) { then(false); return; }
            string text = string.Format(Localization.Extra("kaamoMoveEquipment",
                "Move your equipment to the new ship?\n\nYes: it is mounted on the new ship (what doesn't fit goes to the cargo hold).\nNo: it stays on your {0} in the Kaamo Club."),
                ItemInfo.ShipName(Session.ShipIndex));
            ShowChoice(text, Localization.Get(134), Localization.Get(135), () => then(true), () => then(false));
        }

        /// <summary>ChoiceWindow with its own two labels (327: 330 "Sell" / 331 "Keep").</summary>
        public void ShowChoice(string text, string yes, string no, System.Action onYes, System.Action onNo)
        {
            ShowDialog(text, onYes);
            dialogNoAction = onNo;
            InputGlyph.SetText(dialogYes, yes.ToUpperInvariant());
            InputGlyph.SetText(dialogNo, no.ToUpperInvariant());
        }

        void CloseDialog()
        {
            dialogPicture?.RemoveFromHierarchy();
            dialogPicture = null;
            dialog.RemoveFromClassList("station-dialog-backdrop--shown");
            dialogAction = null;
            dialogNoAction = null;
            // A controller's A (or Enter) that answered the dialog isn't also the hangar window's Action: the UI takes the
            // press first, and in the same frame the hangar saw A just pressed and bought the selected ship again (a
            // player's report). The window waits for it to be released, like after the press that opened it (#104).
            if (HangarOpen) hangarConfirmHeld = true;
            if (SystemMenuOpen)
            {
                // Back to the slot just picked (or the first item of the page).
                var items = SystemMenuItems();
                int i = SavePageOpen && lastSavedSlot >= 0 && lastSavedSlot < items.Length ? lastSavedSlot : 0;
                Select(items[i]);
            }
            else if (missions != null && missions.IsOpen) Select(missions.NavItems()[0]);
            else if (lounge != null && lounge.ChatOpen) lounge.FocusFirst();
            else if (!HangarOpen) Select(launchButton);
            else if (root.focusController?.focusedElement is VisualElement f) f.Blur();
        }

        /// <summary>A short message at the top ("#N mounted.", "You need an additional #C." ...), 3 s.</summary>
        public void ShowToast(string text, bool busy = false)
        {
            toast.text = text;
            UiBusy.Mark(toast, busy);   // a wait: the turning arc before it (docs/UI-DESIGN.md "Waiting")
            toast.AddToClassList("station-toast--shown");
            toastMs = 3000f;
        }

        static int BoozeInHold()
        {
            int n = 0;
            foreach (var s in Session.Cargo) if (Session.IsBooze(s.item)) n += s.amount;
            return n;
        }

        void Back()
        {
            // An option's open drop-down or an "i" / "?" window closes first, as in the main menu (MainMenu.EscBack): both are
            // laid over the whole document, so a page closed under one left it drawing over the next.
            if (ChoicePopup.IsOpen) { Play(buttonRelease); ChoicePopup.Close(); }
            else if (OptionInfo.IsOpen) { Play(buttonRelease); OptionInfo.Hide(); }
            else if (infoWindow != null && infoWindow.IsOpen) { Play(buttonRelease); infoWindow.Close(); }
            else if (MultiplayerWindow.IsOpenAny) { Play(buttonRelease); MultiplayerWindow.CloseAny(); }   // multiplayer: the faction window
            else if (SquadView.PopupOpen) { Play(buttonRelease); SquadView.DeclinePopup(); }   // multiplayer: the invitation's Decline
            else if (DialogOpen)
            {
                // The original's ChoiceWindow can't be dismissed (ModStation::OnKeyPress ignores every key while it is open):
                // back = its No button, or the OK of a message, so the choice's action always runs (the docking fine's No
                // launches; closing it without one kept the player docked without paying).
                Play(buttonRelease);
                var a = dialogNo.resolvedStyle.display != DisplayStyle.None ? dialogNoAction : dialogAction;
                CloseDialog();
                a?.Invoke();
            }
            else if (missions != null && missions.IsOpen) { Play(buttonRelease); missions.Close(); }
            else if (status != null && status.IsOpen) { Play(buttonRelease); status.Close(); }
            else if (lounge != null && lounge.ChatOpen) { Play(buttonRelease); lounge.CloseChat(); }
            else if (HangarOpen) { Play(buttonRelease); if (!hangarWindow.Back()) CloseHangar(); }
            else if (SystemMenuOpen && sysPage == SysPage.Options && optionsView != null && optionsView.CloseResetPrompt()) Play(buttonRelease);   // the defaults prompt first
            else if (SystemMenuOpen && sysPage != SysPage.Main) { Play(buttonRelease); ShowSystemPage(SysPage.Main); }
            else if (SystemMenuOpen) { Play(buttonRelease); CloseSystemMenu(); }
            else if (level != null && level.View == StationView.Lounge) { Play(buttonRelease); level.SetView(StationView.Hangar); }
            else { Play(buttonRelease); OpenSystemMenu(); }
        }

        void BackToMenu()
        {
            GoF2Remake.Multiplayer.NetGame.Shutdown();   // leaving a multiplayer session
            if (Application.CanStreamedLevelBeLoaded(menuScene)) SceneManager.LoadScene(menuScene);
        }

        /// <summary>Modified: every text this document owns, read from the table loaded now. Called while building and again
        /// on Localization.Changed, so the in-game Options' language row applies without leaving the station
        /// (OptionsCatalog.SetLanguage). The windows built with this one re-label themselves the same way; the ones made
        /// when they open (the star map, the Options page, the dialogs) already read the table as they are built.</summary>
        public void RefreshTexts()
        {
            if (root == null) return;
            var st = level != null ? level.Station : null;
            string T(int id) => Localization.Get(id);
            root.Q<Label>("stationTitle").text = st == null ? "" : ItemInfo.WithTag(   // remake mods: the mod's tag, dim (as it reads)
                (st.index == 101 || Modding.ModWorld.IsMobile(st.index) ? st.name : $"{st.name} {T(136)}").ToUpperInvariant(), GameNames.StationTag(st.index));   // no suffix for station 101 or a mobile station (a ship)
            root.Q<Label>("systemName").text = st == null ? "" : ItemInfo.WithTag($"{st.systemName} {T(137)}", GameNames.SystemTag(st.system));
            // Multiplayer: the faction holding this station (NetFactions).
            string holder = st == null ? "" : GoF2Remake.Multiplayer.NetFactionsClient.OwnerText(st.index);
            if (holder.Length > 0) root.Q<Label>("systemName").text += $"  ·  {holder}";
            root.Q<Label>("techLevel").text = st == null ? "" : $"{T(133)}: {st.techLevel}";
            int race = level != null ? level.Layout.raceId : -1;
            var raceLabel = root.Q<Label>("raceName");
            raceLabel.text = race >= 0 && race < 4 || Modding.ModRaces.Is(race) ? GoF2Remake.Flight.Standing.RaceName(race) : "";   // (a mod race's name)
            raceLabel.style.display = raceLabel.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ShowRaceIcon(raceLabel.parent, race, st != null ? st.index : -1);
            shownCredits = shownLoad = shownMaxLoad = int.MinValue;   // the funds plates again in this language (UpdateFunds)
            hangarButton.text = T(167).ToUpperInvariant();
            loungeButton.text = T(398).ToUpperInvariant();
            mapButton.text = T(177).ToUpperInvariant();
            missionsButton.text = T(129).ToUpperInvariant();
            statusButton.text = T(169).ToUpperInvariant();
            inspectButton.text = Localization.Extra("stationInspect", "INSPECT SHIP");
            customizeButton.text = Localization.Extra("stationCustomize", "CUSTOMIZE SHIP");
            // Remake: its key on the button, L with keys and mouse, X with a controller (the root's input-* class picks one).
            // The glyphs are children of the button, which then holds its text in a Label of its own, so it is built again
            // rather than relabelled: setting .text on a button with children leaves the old Label in place.
            launchButton.Clear();
            launchButton.text = Localization.Extra("stationLaunch", "LAUNCH");
            var launchKey = InputGlyph.Key("L");
            launchKey.AddToClassList("launch-glyph--key");
            var launchPad = InputGlyph.Pad(PadButton.X);
            launchPad.AddToClassList("launch-glyph--pad");
            InputGlyph.WithGlyphs(launchButton, launchKey, launchPad);
            InputGlyph.SetText(root.Q<Button>("loungeBack"), Localization.Extra("hudBack", "BACK"));
            InputGlyph.SetText(dialogNo, T(135).ToUpperInvariant());
            root.Q<Button>("menuButton").text = Localization.Extra("hudMenu", "MENU");
            root.Q<Label>("systemMenuTitle").text = T(172).ToUpperInvariant();       // Menu
            saveGameButton.text = T(30).ToUpperInvariant();                          // Save game
            mainMenuButton.text = Modding.ModBuilder.PlayTesting ? Localization.Extra("mbBackToBuilder", "Back to Mod Builder").ToUpperInvariant()
                                : T(522).ToUpperInvariant();                       // Back to Main Menu
            InputGlyph.SetText(systemClose, Localization.Extra("hudBack", "BACK"));
            InputGlyph.SetText(saveBack, Localization.Extra("hudBack", "BACK"));
            // The system menu's own entries (BuildSystemExtras names them as it builds them).
            if (newGameButton != null) newGameButton.text = T(28).ToUpperInvariant();
            if (loadGameButton != null) loadGameButton.text = T(29).ToUpperInvariant();
            if (optionsButton != null) optionsButton.text = T(31).ToUpperInvariant();
            if (aboutButton != null) aboutButton.text = T(43).ToUpperInvariant();
            RefreshViewTitle();
            hangarWindow?.RefreshTexts();
            missions?.RefreshTexts();
            status?.RefreshTexts();
            lounge?.RefreshTexts();
            BuildHints(InputMode.Current);
        }
        void OnViewChanged()
        {
            if (root == null || level == null) return;
            bool inLounge = level.View == StationView.Lounge;
            hangarButton.EnableInClassList("station-button--current", HangarOpen);
            loungeButton.EnableInClassList("station-button--current", inLounge);
            RefreshViewTitle();
            // Inspect ship: the hangar's view only (not in the lounge, nor in VR, which stands in the room already).
            inspectButton.style.display = inLounge || Vr.VrMode.Enabled ? DisplayStyle.None : StyleKeyword.Null;
            RefreshCustomizeButton();
            dragVelocity = 0f;
            lounge?.OnViewChanged();
            ApplyStoryLocks();
            BuildHints(InputMode.Current);
            // Rework (the controller walk, docs/UI-DESIGN.md "Focus"): the keys and a controller start on the lounge's first
            // visitor, and back in the hangar view on Space Lounge (nothing had the focus either way). A frame later: the
            // visitor rows are built then.
            if (inLounge != viewWasLounge && InputMode.Current != InputKind.Touch && !InputMode.MousePointing)
            {
                // Tried every 0.1 s for up to 5 s: the first visit's intro camera keeps the rows away until it ends.
                float until = Time.unscaledTime + 5f;
                bool done = false;
                root.schedule.Execute(() =>
                {
                    if (level == null || touchMode || Time.unscaledTime > until) { done = true; return; }
                    var f = root.focusController?.focusedElement as VisualElement;
                    if (level.View == StationView.Lounge)
                    {
                        if (lounge == null || lounge.ChatOpen || f != null && f.ClassListContains("lounge-row")) { done = true; return; }
                        if (!level.IntroPlaying) { lounge.FocusFirst(); done = root.focusController?.focusedElement != null; }
                    }
                    else
                    {
                        if (!HangarOpen && !SystemMenuOpen && !DialogOpen && (f == null || !f.ClassListContains("station-button") || f == hangarButton))
                            Select(loungeButton);
                        done = true;
                    }
                }).Every(100).Until(() => done);
            }
            viewWasLounge = inLounge;
        }
        /// <summary>ModStation::OnInitialize: Hangar from index 5, Map from 9, Space Lounge from 13 (half transparent before; the
        /// original draws it opaque from 12, but OnKeyPress refuses it before 13: after reporting to Norris; a player's report).</summary>
        void ApplyStoryLocks()
        {
            int station = level != null && level.Station != null ? level.Station.index : -1;
            hangarButton.SetEnabled(Story.HangarUnlocked);
            mapButton.SetEnabled(Story.MapUnlocked);
            missionsButton.SetEnabled(Story.MapUnlocked);   // Missions: locked before campaign 9 like the Map
            loungeButton.SetEnabled(Story.LoungeUnlocked(station));
            // Remake mods: a station without a Space Lounge ("bar": "none") has no button for it
            loungeButton.style.display = Modding.ModWorld.NoBar(station) ? DisplayStyle.None : DisplayStyle.Flex;
            if (hints != null) BuildHints(InputMode.Current);   // the key hints follow the enabled buttons
        }
        bool HangarOpen => hangarWindow != null && hangarWindow.IsOpen;
    }
}
