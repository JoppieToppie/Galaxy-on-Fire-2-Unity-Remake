// MainMenuMultiplayer.cs
// Part of MainMenu: the Multiplayer panel: the server browser, the host card (mode, password, player limit, Debug /
// Combat / World / Mods switches, addresses), the pilot name prompt and the fade into hosting or joining.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using GoF2Remake.Modding;
using GoF2Remake.Multiplayer;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        VisualElement identityOverlay, identityRacePicker, identityPortraitPicker;
        Label identityFeedback, identitySelection;
        Button identityConfirm, identityLink;
        Toggle identityConsent;
        TextField identityCode, identityPortraitSearch;
        List<(string Key, string Name)> identityRaces, identityPortraits;
        string identityRace, identityPortrait, identityPreset;
        bool identityController;
        VisualElement identityBriefing;
        int identityBriefingIndex;
        bool identityBriefingComplete;
        Action<string, string> singlePlayerIdentity;

        bool UpdatePlayerIdentitySetup()
        {
            bool local = singlePlayerIdentity != null;
            if (!local && (!NetGame.Active || !NetProfileClient.IdentityRequired))
            {
                identityOverlay?.RemoveFromHierarchy();
                identityOverlay = null;
                identityBriefing = null;
                identityBriefingIndex = 0;
                identityBriefingComplete = false;
                return false;
            }
            if (identityOverlay == null || identityOverlay.parent != root || (!local &&
                (identityPreset != NetProfileClient.IdentityRace || identityController != NetProfileClient.Controller))) BuildIdentitySetup();
            identityOverlay.BringToFront();
            if (identityBriefing != null)
            {
                if (InputMode.Current == InputKind.Gamepad &&
                    (root.focusController?.focusedElement is not VisualElement briefingFocus || !identityBriefing.Contains(briefingFocus)))
                    identityBriefing.Q<Button>("briefingContinue")?.Focus();
                return true;
            }
            bool pending = !local && NetProfileClient.IdentitySubmitting;
            identityFeedback.text = local ? "" : NetProfileClient.IdentityMessage ?? "";
            identityConsent.SetEnabled(identityController && !pending && !string.IsNullOrEmpty(identityPortrait));
            identityCode.SetEnabled(!pending);
            identityConfirm.SetEnabled(identityController && !pending && identityConsent.value &&
                !string.IsNullOrEmpty(identityRace) && !string.IsNullOrEmpty(identityPortrait));
            identityLink.SetEnabled(!pending && !string.IsNullOrWhiteSpace(identityCode.value));
            identityRacePicker.SetEnabled(identityController && !pending && string.IsNullOrEmpty(identityPreset));
            identityPortraitPicker.SetEnabled(identityController && !pending && !string.IsNullOrEmpty(identityRace));
            identityPortraitSearch.SetEnabled(identityController && !pending && !string.IsNullOrEmpty(identityRace));
            if (InputMode.Current == InputKind.Gamepad &&
                (root.focusController?.focusedElement is not VisualElement focused || !identityOverlay.Contains(focused) || !focused.enabledInHierarchy))
                FocusIdentityChoice();
            return true;
        }

        void FocusIdentityChoice()
        {
            var first = identityRacePicker.enabledInHierarchy ? identityRacePicker.Q<Button>() : null;
            if (first == null && identityPortraitPicker.enabledInHierarchy) first = identityPortraitPicker.Q<Button>();
            if (first != null) first.Focus();
            else if (identityCode.enabledInHierarchy) identityCode.Focus();
            else Focusables(identityOverlay).Find(element => element is Button)?.Focus();
        }

        static void HighlightIdentityChoice(Button button, bool selected, bool focused = false)
        {
            button.AddToClassList("identity-choice");
            button.EnableInClassList("identity-choice--selected", selected);
            button.EnableInClassList("identity-choice--focused", focused);
            if (Settings.OriginalUi)
            {
                button.style.backgroundColor = StyleKeyword.Null;
                button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = StyleKeyword.Null;
                button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = StyleKeyword.Null;
                return;
            }
            var border = focused ? Color.white : selected ? new Color(0.52f, 0.87f, 1f) : new Color(0.20f, 0.34f, 0.44f);
            button.style.backgroundColor = selected ? new Color(0.12f, 0.29f, 0.39f) : new Color(0.045f, 0.10f, 0.15f);
            button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = border;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 2;
            button.style.color = Color.white;
        }

        static Label IdentityLabel(string value, int size)
        {
            var label = new Label(value);
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = Color.white;
            label.style.marginBottom = 8;
            label.style.flexShrink = 0;
            return label;
        }

        void BuildIdentitySetup()
        {
            identityOverlay?.RemoveFromHierarchy();
            identityBriefing = null;
            bool local = singlePlayerIdentity != null;
            identityPreset = local ? null : NetProfileClient.IdentityRace;
            identityController = local || NetProfileClient.Controller;
            identityRace = string.IsNullOrEmpty(identityPreset) ? null : identityPreset;
            identityPortrait = null;
            identityOverlay = new VisualElement { name = "firstJoinIdentity" };
            identityOverlay.style.position = Position.Absolute;
            identityOverlay.style.left = identityOverlay.style.right = identityOverlay.style.top = identityOverlay.style.bottom = 0;
            if (!Settings.OriginalUi) identityOverlay.style.backgroundColor = new Color(0.025f, 0.06f, 0.10f, 0.99f);
            identityOverlay.style.alignItems = Align.Center;
            identityOverlay.style.justifyContent = Justify.Center;
            root.Add(identityOverlay);

            // Explicit dimensions: max-height alone allowed the ScrollView to collapse to its minimum layout size.
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "identitySetupScroll" };
            scroll.style.width = Length.Percent(96);
            scroll.style.height = Length.Percent(94);
            scroll.style.maxWidth = 1600;
            scroll.style.flexShrink = 0;
            scroll.AddManipulator(new DragScroll(scroll));
            scroll.contentContainer.style.paddingLeft = scroll.contentContainer.style.paddingRight = 20;
            scroll.contentContainer.style.paddingTop = scroll.contentContainer.style.paddingBottom = 12;
            identityOverlay.Add(scroll);
            scroll.Add(IdentityLabel("CREATE YOUR PILOT", 32));
            scroll.Add(IdentityLabel(identityController
                ? (local ? "Choose your race, then a portrait for this game." : "Choose your race, then a portrait. Both choices are permanent on this server and affect station and jumpgate policies.")
                : "This profile is being set up on another device. Wait for confirmation, link an existing profile, or disconnect.", 18));

            identityRaces = ModRefs.RaceWords.Where(race => PlayerIdentity.IsSelectableRace(race.race)).Select(race => (Key: PlayerIdentity.RaceKey(race.race), Name: race.name))
                .Concat(ModRaces.All.Where(race => PlayerIdentity.IsSelectableRace(race.race)).Select(race => (Key: race.key, Name: ModRaces.Name(race.race))))
                .GroupBy(race => race.Key).Select(group => group.First()).ToList();
            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexShrink = 0;
            scroll.Add(split);
            var races = new VisualElement();
            races.style.width = 270;
            races.style.flexShrink = 0;
            races.style.marginRight = 20;
            races.Add(IdentityLabel("1. CHOOSE RACE", 21));
            identityRacePicker = new VisualElement { name = "identityRaceChoices" };
            identityRacePicker.style.flexDirection = FlexDirection.Column;
            races.Add(identityRacePicker);
            split.Add(races);

            var portraits = new VisualElement();
            portraits.style.flexGrow = 1;
            portraits.style.flexBasis = 0;
            portraits.style.minWidth = 0;
            portraits.Add(IdentityLabel("2. CHOOSE PORTRAIT", 21));
            identityPortraitSearch = new TextField { name = "identityPortraitSearch", label = "Filter portraits" };
            identityPortraitSearch.style.minHeight = 38;
            identityPortraitSearch.style.marginBottom = 8;
            portraits.Add(identityPortraitSearch);
            identityPortraitPicker = new VisualElement { name = "identityPortraitChoices" };
            identityPortraitPicker.style.flexDirection = FlexDirection.Row;
            identityPortraitPicker.style.flexWrap = Wrap.Wrap;
            portraits.Add(identityPortraitPicker);
            split.Add(portraits);

            // On narrow windows, keep the portrait cards usable by stacking the two panels.
            split.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                bool narrow = evt.newRect.width < 720;
                split.style.flexDirection = narrow ? FlexDirection.Column : FlexDirection.Row;
                races.style.width = narrow ? new StyleLength(Length.Percent(100)) : new StyleLength(270f);
                races.style.marginRight = narrow ? 0 : 20;
                races.style.marginBottom = narrow ? 12 : 0;
                identityRacePicker.style.flexDirection = narrow ? FlexDirection.Row : FlexDirection.Column;
                identityRacePicker.style.flexWrap = narrow ? Wrap.Wrap : Wrap.NoWrap;
                portraits.style.flexBasis = narrow ? new StyleLength(StyleKeyword.Auto) : new StyleLength(0f);
            });

            identitySelection = IdentityLabel("", 19);
            identitySelection.style.marginTop = 14;
            scroll.Add(identitySelection);
            identityConsent = new Toggle("I understand that neither choice can be changed later.");
            identityConsent.style.minHeight = 42;
            scroll.Add(identityConsent);
            identityConfirm = new Button(() =>
            {
                if (identityController && (local || !NetProfileClient.IdentitySubmitting) && identityConsent.value &&
                    !string.IsNullOrEmpty(identityRace) && !string.IsNullOrEmpty(identityPortrait))
                {
                    if (singlePlayerIdentity != null)
                    {
                        var complete = singlePlayerIdentity;
                        singlePlayerIdentity = null;
                        identityOverlay?.RemoveFromHierarchy();
                        identityOverlay = null;
                        complete(identityRace, identityPortrait);
                    }
                    else NetProfileClient.ChooseIdentity(identityRace, identityPortrait);
                }
            }) { text = local ? "CONFIRM IDENTITY & START" : "CONFIRM PERMANENT IDENTITY & JOIN" };
            scroll.Add(identityConfirm);
            identityFeedback = IdentityLabel("", 16);
            scroll.Add(identityFeedback);
            var linkHint = IdentityLabel("Already have a profile on this server? Link it instead.", 16);
            scroll.Add(linkHint);
            identityCode = new TextField("Link code") { maxLength = 16 };
            identityCode.style.minHeight = 40;
            scroll.Add(identityCode);
            identityLink = new Button(() => NetProfileClient.LinkIdentity(identityCode.value.Trim())) { text = "LINK EXISTING PROFILE" };
            scroll.Add(identityLink);
            if (local)
            {
                linkHint.style.display = DisplayStyle.None;
                identityCode.style.display = DisplayStyle.None;
                identityLink.style.display = DisplayStyle.None;
            }
            var cancel = new Button(() =>
            {
                if (singlePlayerIdentity != null)
                {
                    singlePlayerIdentity = null;
                    identityOverlay?.RemoveFromHierarchy();
                    identityOverlay = null;
                }
                else NetGame.RefuseJoin("Pilot setup cancelled.");
            }) { text = local ? "BACK" : "DISCONNECT" };
            scroll.Add(cancel);
            foreach (var button in new[] { identityConfirm, identityLink, cancel })
            {
                button.style.minHeight = 46;
                button.style.marginTop = 8;
                button.style.whiteSpace = WhiteSpace.Normal;
                button.style.flexShrink = 0;
            }

            void UpdateSelection()
            {
                var race = identityRaces.Find(entry => entry.Key == identityRace).Name;
                var portrait = identityPortraits.Find(entry => entry.Key == identityPortrait).Name;
                identitySelection.text = "Selected: " + (race ?? "No race") + " / " + (portrait ?? "No portrait");
                foreach (var button in identityRacePicker.Query<Button>().ToList())
                    HighlightIdentityChoice(button, (string)button.userData == identityRace, button.focusController?.focusedElement == button);
                foreach (var button in identityPortraitPicker.Query<Button>().ToList())
                    HighlightIdentityChoice(button, (string)button.userData == identityPortrait, button.focusController?.focusedElement == button);
            }

            void ShowPortraits(string filter)
            {
                identityPortraitPicker.Clear();
                if (!PlayerIdentity.TryResolveRace(identityRace, out int race))
                {
                    identityPortraitPicker.Add(IdentityLabel("Select a race to display its available portraits.", 18));
                    return;
                }
                foreach (var entry in identityPortraits)
                {
                    if (!string.IsNullOrWhiteSpace(filter) &&
                        entry.Name.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) < 0 &&
                        entry.Key.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!PlayerIdentity.TryResolveSelectionPortrait(race, entry.Key, out var parts, out var character)) continue;
                    string key = entry.Key;
                    var card = new Button(() =>
                    {
                        identityPortrait = key;
                        identityConsent.SetValueWithoutNotify(false);
                        UpdateSelection();
                    }) { userData = key };
                    card.style.width = 184;
                    card.style.minHeight = 244;
                    card.style.marginRight = card.style.marginBottom = 8;
                    card.style.paddingTop = card.style.paddingBottom = 6;
                    card.style.flexShrink = 0;
                    card.style.flexDirection = FlexDirection.Column;
                    card.style.alignItems = Align.Center;
                    card.style.justifyContent = Justify.Center;
                    var preview = new VisualElement { pickingMode = PickingMode.Ignore };
                    preview.AddToClassList("portrait");
                    preview.style.width = 160;
                    preview.style.height = 200;
                    preview.style.flexShrink = 0;
                    card.Add(preview);
                    if (character == null) Portrait.Show(preview, parts, false);
                    else Portrait.ShowCharacter(preview, character, false);
                    preview.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
                    var name = IdentityLabel(entry.Name, 16);
                    name.pickingMode = PickingMode.Ignore;
                    name.style.unityTextAlign = TextAnchor.MiddleCenter;
                    name.style.alignSelf = Align.Stretch;
                    card.Add(name);
                    HighlightIdentityChoice(card, key == identityPortrait);
                    card.RegisterCallback<FocusInEvent>(_ => HighlightIdentityChoice(card, key == identityPortrait, true));
                    card.RegisterCallback<FocusOutEvent>(_ => HighlightIdentityChoice(card, key == identityPortrait));
                    identityPortraitPicker.Add(card);
                }
                if (identityPortraitPicker.childCount == 0)
                    identityPortraitPicker.Add(IdentityLabel("No matching portraits.", 18));
            }

            void UpdatePortraits()
            {
                identityPortrait = null;
                identityConsent.SetValueWithoutNotify(false);
                identityPortraits = PlayerIdentity.TryResolveRace(identityRace, out int race)
                    ? PlayerIdentity.Portraits(race).ToList() : new List<(string, string)>();
                identityPortraitSearch.SetValueWithoutNotify("");
                ShowPortraits("");
                UpdateSelection();
            }

            foreach (var entry in identityRaces)
            {
                string key = entry.Key;
                var button = new Button(() =>
                {
                    if (identityRace == key) return;
                    identityRace = key;
                    UpdatePortraits();
                }) { text = entry.Name, userData = key };
                button.style.minHeight = 46;
                button.style.minWidth = 130;
                button.style.marginBottom = 6;
                button.style.marginRight = 6;
                button.style.fontSize = 18;
                button.style.whiteSpace = WhiteSpace.Normal;
                HighlightIdentityChoice(button, key == identityRace);
                button.RegisterCallback<FocusInEvent>(_ => HighlightIdentityChoice(button, key == identityRace, true));
                button.RegisterCallback<FocusOutEvent>(_ => HighlightIdentityChoice(button, key == identityRace));
                identityRacePicker.Add(button);
            }
            identityPortraitSearch.RegisterValueChangedCallback(evt => ShowPortraits(evt.newValue));
            UpdatePortraits();
            identityRacePicker.SetEnabled(identityController && string.IsNullOrEmpty(identityPreset));
            identityPortraitPicker.SetEnabled(identityController && !string.IsNullOrEmpty(identityRace));
            identityPortraitSearch.SetEnabled(identityController && !string.IsNullOrEmpty(identityRace));
            scroll.RegisterCallback<FocusInEvent>(evt =>
            {
                var content = scroll.contentContainer;
                if (evt.target is VisualElement element && element != content && content.Contains(element))
                    scroll.ScrollTo(element);
            });
            UiFocus.SkipScrollBars(identityOverlay);
            identityOverlay.RegisterCallback<NavigationCancelEvent>(evt => evt.StopPropagation());
            identityOverlay.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            if (identityController && string.IsNullOrEmpty(identityPreset) && !identityBriefingComplete)
                BuildIdentityBriefing(scroll);
            if (identityBriefing == null && InputMode.Current == InputKind.Gamepad) FocusIdentityChoice();
        }

        // These files are read from the active mod set before a pilot exists; no Lua approval or save state is needed.
        const string IdentityBriefingFile = "tutorials/preIdentity.json";

        void BuildIdentityBriefing(ScrollView identityScroll)
        {
            var pages = ModTutorialLayout.ReadPages(IdentityBriefingFile, singlePlayerIdentity != null);
            if (pages.Count == 0) return;
            identityBriefingIndex = Mathf.Clamp(identityBriefingIndex, 0, pages.Count - 1);
            identityScroll.style.display = DisplayStyle.None;
            identityBriefing = new VisualElement { name = "modIdentityBriefing" };
            identityBriefing.style.width = Length.Percent(96);
            identityBriefing.style.height = Length.Percent(94);
            identityBriefing.style.maxWidth = 1100;
            identityBriefing.style.paddingLeft = identityBriefing.style.paddingRight = 20;
            identityBriefing.style.paddingTop = identityBriefing.style.paddingBottom = 12;
            identityOverlay.Add(identityBriefing);

            int visited = identityBriefingIndex;
            void DrawPage()
            {
                identityBriefing.Clear();
                ModTutorialLayout.DrawPage(identityBriefing, pages, identityBriefingIndex, visited,
                    i => { identityBriefingIndex = i; DrawPage(); }, "gof-button");
                var footer = new VisualElement();
                footer.style.flexDirection = FlexDirection.Row;
                footer.style.flexShrink = 0;
                identityBriefing.Add(footer);
                var back = new Button(() => { identityBriefingIndex--; DrawPage(); }) { text = "BACK" };
                back.SetEnabled(identityBriefingIndex > 0);
                var next = new Button(() =>
                {
                    if (identityBriefingIndex + 1 < pages.Count)
                    {
                        identityBriefingIndex++;
                        visited = Mathf.Max(visited, identityBriefingIndex);
                        DrawPage();
                        return;
                    }
                    identityBriefingComplete = true;
                    identityBriefing.RemoveFromHierarchy();
                    identityBriefing = null;
                    identityScroll.style.display = DisplayStyle.Flex;
                    FocusIdentityChoice();
                }) { name = "briefingContinue", text = identityBriefingIndex + 1 < pages.Count ? "CONTINUE" : "CONTINUE TO RACE SELECTION" };
                var disconnect = new Button(() => NetGame.RefuseJoin("Pilot setup cancelled.")) { text = "DISCONNECT" };
                foreach (var button in singlePlayerIdentity != null ? new[] { back, next } : new[] { back, next, disconnect })
                {
                    button.style.minHeight = 48;
                    button.style.flexGrow = 1;
                    button.style.whiteSpace = WhiteSpace.Normal;
                    button.AddToClassList("gof-button");
                    footer.Add(button);
                }
                UiFocus.SkipScrollBars(identityBriefing);
                if (InputMode.Current == InputKind.Gamepad) next.Focus();
            }
            DrawPage();
        }

        /// <summary>The name a public game is listed under: the Game name field, else "<pilot>'s universe".</summary>
        string SessionName()
        {
            string n = mpSessionName != null ? mpSessionName.value.Replace("<", "").Replace(">", "").Trim() : "";
            return n.Length > 0 ? n : GoF2Remake.Multiplayer.NetGame.DefaultSessionName();
        }

        void SetupMultiplayerPanel()
        {
            mpAddress = root.Q<TextField>("mpAddress");
            mpStatus = root.Q<Label>("mpStatus");
            if (mpAddress != null) mpAddress.value = PlayerPrefs.GetString("mp_address", "127.0.0.1");
            mpName = root.Q<TextField>("mpName");
            if (mpName != null)
            {
                mpName.maxLength = GoF2Remake.Multiplayer.NetGame.MaxNameLength;
                mpName.value = GoF2Remake.Multiplayer.NetGame.PlayerName;
                mpName.RegisterValueChangedCallback(e => { GoF2Remake.Multiplayer.NetGame.PlayerName = e.newValue; ApplyHostMode(); });
            }
            mpAddressList = root.Q("mpAddressList");
            mpPort = root.Q<TextField>("mpPort");
            if (mpPort != null)
            {
                mpPort.maxLength = 5;
                mpPort.keyboardType = TouchScreenKeyboardType.NumberPad;   // the on-screen keyboard's number pad
                mpPort.value = GoF2Remake.Multiplayer.NetGame.HostPort.ToString();
                mpPort.RegisterValueChangedCallback(e =>
                {
                    if (ushort.TryParse(e.newValue, out ushort p) && p >= 1024) GoF2Remake.Multiplayer.NetGame.HostPort = p;
                    RefreshAddresses();
                });
            }
            mpLocalBox = root.Q("mpLocalBox");
            SetupHostMore();
            mpSessionName = root.Q<TextField>("mpSessionName");
            if (mpSessionName != null)
            {
                mpSessionName.maxLength = 48;
                mpSessionName.value = PlayerPrefs.GetString("mp_session_name", "");
                mpSessionName.RegisterValueChangedCallback(e => PlayerPrefs.SetString("mp_session_name", e.newValue.Trim()));
            }
            var hostPassword = root.Q<TextField>("mpHostPassword");
            if (hostPassword != null)
            {
                hostPassword.maxLength = GoF2Remake.Multiplayer.NetGame.MaxPasswordLength;
                hostPassword.isPasswordField = true;
                hostPassword.value = PlayerPrefs.GetString("mp_host_password", "");
                GoF2Remake.Multiplayer.NetGame.HostPassword = hostPassword.value;
                hostPassword.RegisterValueChangedCallback(e =>
                {
                    GoF2Remake.Multiplayer.NetGame.HostPassword = e.newValue;
                    PlayerPrefs.SetString("mp_host_password", GoF2Remake.Multiplayer.NetGame.CleanPassword(e.newValue));
                });
            }
            var maxPlayers = root.Q<TextField>("mpMaxPlayers");
            if (maxPlayers != null)
            {
                // The session's size, the host included (2..150, PlayerPrefs "mp_max_players"); shown clamped once left.
                maxPlayers.maxLength = 3;
                maxPlayers.keyboardType = TouchScreenKeyboardType.NumberPad;
                GoF2Remake.Multiplayer.NetGame.MaxPlayers = PlayerPrefs.GetInt("mp_max_players", GoF2Remake.Multiplayer.NetGame.DefaultMaxPlayers);
                maxPlayers.value = GoF2Remake.Multiplayer.NetGame.MaxPlayers.ToString();
                maxPlayers.RegisterValueChangedCallback(e =>
                {
                    if (!int.TryParse(e.newValue, out int n)) return;
                    // The Co-op campaign screen keeps its own (4 by default, "mp_coop_max_players").
                    if (coopPanelMode) { GoF2Remake.Multiplayer.NetGame.CoopMaxPlayers = n; return; }
                    GoF2Remake.Multiplayer.NetGame.MaxPlayers = n;
                    PlayerPrefs.SetInt("mp_max_players", GoF2Remake.Multiplayer.NetGame.MaxPlayers);
                });
                maxPlayers.RegisterCallback<FocusOutEvent>(_ => maxPlayers.SetValueWithoutNotify(GoF2Remake.Multiplayer.NetGame.MaxPlayers.ToString()));
            }
            // The Debug menu in the hosted session (PlayerPrefs "mp_allow_debug", off by default; NetGame.HostAllowsDebug).
            GoF2Remake.Multiplayer.NetGame.HostAllowsDebug = PlayerPrefs.GetInt("mp_allow_debug", 0) != 0;
            Bind("mpDebugOff", () => SetHostDebug(false));
            Bind("mpDebugOn", () => SetHostDebug(true));
            ApplyHostDebug();
            // Combat in the hosted session (PlayerPrefs "mp_pvp", PvE by default; NetGame.FreePvp): PvE = players fight only in
            // arena matches and faction sieges, PvP = anywhere. The server browser shows it as a chip.
            GoF2Remake.Multiplayer.NetGame.FreePvp = PlayerPrefs.GetInt("mp_pvp", 0) != 0;
            Bind("mpPvpOff", () => SetHostPvp(false));
            Bind("mpPvpOn", () => SetHostPvp(true));
            ApplyHostPvp();
            // The hosted world (PlayerPrefs "mp_persistent": 0 fresh by default, 1 persistent): persistent keeps every player's
            // progress, the factions, bans and news on this device (NetGame.HostWantsPersistent; the host is its master admin).
            // A co-op campaign is the main menu's own entry (the panel's co-op mode, SetPanelMode; NetGame.HostWantsCoop).
            GoF2Remake.Multiplayer.NetGame.HostWantsPersistent = PlayerPrefs.GetInt("mp_persistent", 0) == 1;
            GoF2Remake.Multiplayer.NetGame.HostWantsCoop = false;
            Bind("mpWorldFresh", () => SetHostWorld(0));
            Bind("mpWorldPersistent", () => SetHostWorld(1));
            // A co-op campaign's settings (shown with Co-op): the enemies' strength per player past the first
            // (PlayerPrefs "mp_coop_strength", +50 % by default), how many more enemy ships per player past the first
            // ("mp_coop_spawns", Off by default) and the story credits shared out or paid in full ("mp_coop_split").
            foreach (int pct in GoF2Remake.Data.CoopCampaign.StrengthChoices)
            {
                int choice = pct;
                Bind("mpCoopStrength" + pct, () => { GoF2Remake.Multiplayer.NetGame.CoopStrength = choice; ApplyHostWorld(); });
            }
            foreach (int pct in GoF2Remake.Data.CoopCampaign.SpawnChoices)
            {
                int choice = pct;
                Bind("mpCoopSpawns" + pct, () => { GoF2Remake.Multiplayer.NetGame.CoopSpawns = choice; ApplyHostWorld(); });
            }
            Bind("mpCoopRewardsShared", () => { GoF2Remake.Multiplayer.NetGame.CoopSplitRewards = true; ApplyHostWorld(); });
            Bind("mpCoopRewardsFull", () => { GoF2Remake.Multiplayer.NetGame.CoopSplitRewards = false; ApplyHostWorld(); });
            ApplyHostWorld();
            // Modded content in the hosted session (PlayerPrefs "mp_allow_mods", off by default; NetMods.HostAllowsMods).
            GoF2Remake.Multiplayer.NetMods.HostAllowsMods = PlayerPrefs.GetInt("mp_allow_mods", 0) != 0;
            Bind("mpModsOff", () => SetHostMods(false));
            Bind("mpModsOn", () => SetHostMods(true));
            ApplyHostMods();
            mpJoinPassword = root.Q<TextField>("mpJoinPassword");
            if (mpJoinPassword != null)
            {
                mpJoinPassword.maxLength = GoF2Remake.Multiplayer.NetGame.MaxPasswordLength;
                mpJoinPassword.isPasswordField = true;
                mpJoinPassword.value = GoF2Remake.Multiplayer.NetGame.JoinPassword;   // this run's only (not saved)
                mpJoinPassword.RegisterValueChangedCallback(e => GoF2Remake.Multiplayer.NetGame.JoinPassword = e.newValue);
            }
            mpServerList = root.Q<ScrollView>("mpServerList");
            Bind("mpModePublic", () => SetHostMode(HostMode.Public));
            Bind("mpModePrivate", () => SetHostMode(HostMode.Private));
            Bind("mpModeLocal", () => SetHostMode(HostMode.Local));
            ApplyHostMode();
            SetupHostHelp();
            Bind("mpHost", () => WithName(() =>
            {
                if (GoF2Remake.Multiplayer.NetGame.HostWantsCoop) AskCoopCampaign();   // which campaign the crew plays
                else StartCoroutine(LeaveForMultiplayer(null));
            }));
            Bind("mpJoin", () =>
            {
                string address = mpAddress != null ? mpAddress.value.Trim() : "";
                if (address.Length == 0) return;
                PlayerPrefs.SetString("mp_address", address);
                WithName(() => StartCoroutine(LeaveForMultiplayer(address)));
            });
        }

        /// <summary>Players' wish (2026-11-13): a "?" at the top right of each host setting's row with what its choices do
        /// (OptionInfo, as in Options: hover or tap). A controller hides those icons (.input-gamepad .option-info), so a
        /// focused setting's text goes to the status line under the cards instead.</summary>
        void SetupHostHelp()
        {
            void Help(string label, string key, string english)
            {
                if (!(root.Q<Label>(label) is Label l)) return;
                var icon = OptionInfo.Icon(() => Localization.Extra(key, english));
                icon.style.right = 0f;
                icon.style.top = -3f;   // the 22 px icon centred on the caption line
                l.parent.Add(icon);   // on the row: a Label with a child loses its text measure (height 0)
                l.parent.RegisterCallback<FocusInEvent>(_ =>
                {
                    if (InputMode.Current == InputKind.Gamepad) SetMpStatus(Localization.Extra(key, english));
                });
            }
            Help("mpDebugLabel", "mpTipDebug", "Allowed: every player gets the Debug pages with the cheats, items and spawns. Off: nobody does, and cheats switched on in single player do not count here.");
            Help("mpPvpLabel", "mpTipCombat", "PvE: players can only fight each other in arena matches and faction sieges. PvP: players can attack each other anywhere.");
            Help("mpWorldLabel", "mpTipWorld", "Fresh: nothing is kept on this device when the session ends, and there are no factions. Persistent: this device keeps every player's progress, the factions, bans and news for the next time you host, and you are the admin.");
            Help("mpModsLabel", "mpTipMods", "Allowed: the session runs with the mods you have switched on, and only players who have the same mods installed can join. Off: the session runs without mods.");
            Help("mpCoopStrengthLabel", "mpTipCoopStrength", "Makes the enemies tougher for every player after the first: this much more hull, and half of it as extra gun damage.");
            Help("mpCoopSpawnsLabel", "mpTipCoopSpawns", "Adds this many more enemy ships to the story's fights for every player after the first.");
            Help("mpCoopRewardsLabel", "mpTipCoopRewards", "Shared: the story's rewards are divided between the players. Each in full: every player gets the whole reward.");
        }

        /// <summary>-mphost: hosts once the menu is up (the panel has listed the addresses).</summary>
        IEnumerator TestHostSoon()
        {
            yield return new WaitForSeconds(2f);
            while (screen != MenuState.Menu) yield return null;
            yield return LeaveForMultiplayer(null);
        }

        /// <summary>-mpjoin: joins that address, again every 2 s until a host answers (and after a session ends).</summary>
        IEnumerator AutoJoin(string address)
        {
            while (true)
            {
                while (screen != MenuState.Menu) yield return null;
                yield return LeaveForMultiplayer(address);   // returns only when the connection failed
                yield return new WaitForSeconds(2f);
            }
        }

        void SetHostDebug(bool allowed)
        {
            GoF2Remake.Multiplayer.NetGame.HostAllowsDebug = allowed;
            PlayerPrefs.SetInt("mp_allow_debug", allowed ? 1 : 0);
            ApplyHostDebug();
        }

        void ApplyHostDebug()
        {
            bool allowed = GoF2Remake.Multiplayer.NetGame.HostAllowsDebug;
            root.Q<Button>("mpDebugOff")?.EnableInClassList("choice-segment--active", !allowed);
            root.Q<Button>("mpDebugOn")?.EnableInClassList("choice-segment--active", allowed);
        }

        void SetHostPvp(bool pvp)
        {
            GoF2Remake.Multiplayer.NetGame.FreePvp = pvp;
            PlayerPrefs.SetInt("mp_pvp", pvp ? 1 : 0);
            ApplyHostPvp();
        }

        void ApplyHostPvp()
        {
            bool pvp = GoF2Remake.Multiplayer.NetGame.FreePvp;
            root.Q<Button>("mpPvpOff")?.EnableInClassList("choice-segment--active", !pvp);
            root.Q<Button>("mpPvpOn")?.EnableInClassList("choice-segment--active", pvp);
        }

        /// <summary>0 fresh, 1 persistent (the Multiplayer screen's World switch).</summary>
        void SetHostWorld(int world)
        {
            GoF2Remake.Multiplayer.NetGame.HostWantsPersistent = world == 1;
            PlayerPrefs.SetInt("mp_persistent", world);
            ApplyHostWorld();
        }

        /// <summary>The main menu's Co-op campaign: the multiplayer panel as the co-op screen.</summary>
        void OpenCoop()
        {
            SetPanelMode(true);
            OpenMultiplayerPanel();
        }

        /// <summary>The panel as the Co-op campaign screen or the Multiplayer one: hosting a co-op campaign or a shared world,
        /// the browser's games (co-op ones only, or all), the title, texts and the host card's rows.</summary>
        void SetPanelMode(bool coop)
        {
            if (coop != coopPanelMode) { serverListKey = null; mpServerList?.Clear(); }   // the other screen's games
            coopPanelMode = coop;
            GoF2Remake.Multiplayer.NetGame.HostWantsCoop = coop;
            ApplyPanelMode();
            ApplyHostWorld();
            // A co-op campaign has its own size (4 by default): the field shows the screen's.
            root.Q<TextField>("mpMaxPlayers")?.SetValueWithoutNotify(GoF2Remake.Multiplayer.NetGame.MaxPlayers.ToString());
        }

        /// <summary>The co-op screen's own texts, or the Multiplayer screen's (and the rows only one of them has).</summary>
        void ApplyPanelMode()
        {
            if (root == null) return;
            void Text(string name, string text) { if (root.Q(name) is TextElement e) { if (e is Button b) InputGlyph.SetText(b, text); else e.text = text; } }
            bool coop = coopPanelMode;
            Text("multiplayerTitle", coop ? CoopTitle.UiCaps() : Localization.Extra("multiplayer", "Multiplayer").UiCaps());
            Text("mpIntro", coop ? Localization.Extra("coopIntro", "Play the whole story together: one campaign for the whole crew.")
                                 : Localization.Extra("mpIntroShort", "Meet other pilots, form squads and fly missions together."));
            Text("mpJoinTitle", (coop ? Localization.Extra("coopBrowserTitle", "Open campaigns") : Localization.Extra("mpBrowserTitle", "Server browser")).UiCaps());
            Text("mpHostTitle", (coop ? Localization.Extra("coopHostTitle", "Host a campaign") : Localization.Extra("mpHostTitle", "Host a game")).UiCaps());
            Text("mpHost", (coop ? Localization.Extra("coopHostButton", "Host campaign") : Localization.Extra("mpHost", "Host")).UiCaps());
            // The co-op screen: no World or Combat switch (the crew is one squad), where the host's game comes from under the title.
            foreach (var row in new[] { "mpWorldRow", "mpPvpRow" })
                if (root.Q(row) is VisualElement r) r.style.display = coop ? DisplayStyle.None : DisplayStyle.Flex;
            if (root.Q<Label>("mpCoopSaved") is Label saved)
            {
                saved.style.display = coop ? DisplayStyle.Flex : DisplayStyle.None;
                if (coop) saved.text = Localization.Extra("coopSavesNote", "You host one of your saves or a new game, and it saves into your save slots as in single player.");
            }
        }

        void ApplyHostWorld()
        {
            bool persistent = GoF2Remake.Multiplayer.NetGame.HostWantsPersistent, coop = GoF2Remake.Multiplayer.NetGame.HostWantsCoop;
            root.Q<Button>("mpWorldFresh")?.EnableInClassList("choice-segment--active", !persistent && !coop);
            root.Q<Button>("mpWorldPersistent")?.EnableInClassList("choice-segment--active", persistent);
            int strength = GoF2Remake.Multiplayer.NetGame.CoopStrength;
            foreach (int pct in GoF2Remake.Data.CoopCampaign.StrengthChoices)
                root.Q<Button>("mpCoopStrength" + pct)?.EnableInClassList("choice-segment--active", strength == pct);
            int spawns = GoF2Remake.Multiplayer.NetGame.CoopSpawns;
            foreach (int pct in GoF2Remake.Data.CoopCampaign.SpawnChoices)
                root.Q<Button>("mpCoopSpawns" + pct)?.EnableInClassList("choice-segment--active", spawns == pct);
            bool split = GoF2Remake.Multiplayer.NetGame.CoopSplitRewards;
            root.Q<Button>("mpCoopRewardsShared")?.EnableInClassList("choice-segment--active", split);
            root.Q<Button>("mpCoopRewardsFull")?.EnableInClassList("choice-segment--active", !split);
            foreach (var row in new[] { "mpCoopStrengthRow", "mpCoopSpawnsRow", "mpCoopRewardsRow" })
                if (root.Q(row) is VisualElement r) r.style.display = coop ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static string CoopTitle => Localization.Extra("coopTitle", "Co-op campaign");

        static string CampaignName(int campaign) => campaign switch { 1 => "Valkyrie", 2 => "Supernova", _ => "Galaxy on Fire 2" };

        /// <summary>Hosting a co-op campaign: one of the player's own saves (picked from the save slots, OpenCoopSlots) or a
        /// new game. The host's game saves into the slots as in single player (SaveGame.HostSaves).</summary>
        void AskCoopCampaign()
        {
            bool any = false;
            for (int i = 0; i < SaveGame.SlotCount && !any; i++) any = CoopCampaign.CanHost(SaveGame.Preview(i));
            if (!any) { AskNewCoopCampaign(); return; }
            ShowChoice(CoopTitle, Localization.Extra("coopHostText", "Play one of your saves with the crew, or start a new game?"),
                Localization.Extra("coopFromSave", "Continue a save"), OpenCoopSlots,
                Localization.Extra("coopNewGame", "New game"), AskNewCoopCampaign);
        }

        void AskNewCoopCampaign()
        {
            string text = Localization.Extra("coopNewText", "Which campaign does the crew play? Your new game saves over the auto-save when you dock, as in single player.");
            ShowChoice(CoopTitle, text, CampaignName(0), () => AskCoopEconomy(0), Localization.Extra("coopAddons", "Add-ons"),
                () => ShowChoice(CoopTitle, text, CampaignName(1), () => AskCoopEconomy(1), CampaignName(2), () => AskCoopEconomy(2)));
        }

        /// <summary>A new co-op campaign's economy, as single player's new game asks it (a continued save keeps its own): every
        /// crew member's game loads the same tables (NetSession.Economy, sent with NetState).</summary>
        void AskCoopEconomy(int campaign)
        {
            string text = Localization.Extra("coopEconomyText", "Which economy does the crew play? Every crew member's game uses its prices.")
                + "\n\n" + Session.EconomyName(Economy.Default) + ": " + Localization.Extra("economyDefaultDesc", "PC / Mac / iPhone prices: cheaper goods and blueprints, dearer ships.")
                + "\n" + Session.EconomyName(Economy.Android) + ": " + Localization.Extra("economyAndroidDesc", "Android prices: dearer goods and gear, bigger blueprints, cheaper ships.");
            void Host(Economy economy)
            {
                GoF2Remake.Multiplayer.NetGame.CoopEconomy = economy;
                HostCoop(campaign);
            }
            ShowChoice(CoopTitle, text, Session.EconomyName(Economy.Default), () => Host(Economy.Default),
                       Session.EconomyName(Economy.Android), () => Host(Economy.Android));
        }

        /// <summary>The Load panel as the co-op host's save picker (coopSlotPick): a slot hosts the campaign from it.</summary>
        void OpenCoopSlots()
        {
            coopSlotPick = true;
            SyncSaveFolder(true);
            BuildSlots();
            ApplyLoadPanelMode();
            OpenPanel("loadPanel");
        }

        /// <summary>A slot picked for the co-op campaign: refused when it can't be one (hardcore, a mod's campaign), else the
        /// co-op screen again (its status line shows a failed start) and hosting, after the Load's warning on missing mods.</summary>
        void PickCoopSlot(int slot)
        {
            var save = SaveGame.Preview(slot);
            if (save == null) return;
            if (!CoopCampaign.CanHost(save))
            {
                ShowNotice(CoopTitle, save.hardcore ? Localization.Extra("coopNoHardcore", "A hardcore game can't be played as a co-op campaign.")
                                                    : Localization.Extra("coopNoModCampaign", "A mod's campaign can't be played as a co-op campaign."));
                return;
            }
            void Host()
            {
                CloseCoopSlots();
                GoF2Remake.Multiplayer.NetGame.CoopSlot = slot;
                HostCoop(-1);
            }
            var missing = Modding.ModSaves.MissingMods(save);
            if (!GoF2Remake.Multiplayer.NetMods.HostAllowsMods && save.mods != null)
                foreach (var m in save.mods) if (!missing.Contains(m.name) && !missing.Contains(m.id)) missing.Add(string.IsNullOrEmpty(m.name) ? m.id : m.name);
            if (missing.Count > 0)
            {
                ShowDialog(Localization.Extra("modsMissingTitle", "Mods missing"), string.Format(Localization.Extra("coopModsMissingText",
                    "This game was saved with mods the session won't run: {0}.\nTheir items will be removed and refunded. Turn the mods on (and the Host card's Mods) to keep them.\n\nHost anyway?"),
                    string.Join(", ", missing)), Host);
                return;
            }
            Host();
        }

        /// <summary>From the co-op save picker back to the co-op screen.</summary>
        void CloseCoopSlots()
        {
            coopSlotPick = false;
            ApplyLoadPanelMode();
            OpenPanel("multiplayerPanel");
        }

        /// <summary>A new co-op campaign's lobby (NetGame.InLobby): the menu's dialog lists the crew; the host's Start takes
        /// everyone into the campaign at once (the main game's prologue in flight), Leave ends the session for this player
        /// (the host's: for everyone).</summary>
        IEnumerator CoopLobby()
        {
            fade.RemoveFromClassList("fade--on");
            screen = MenuState.Menu;
            StartCoroutine(FadeMusic(Settings.MusicVolume, 1f));
            bool host = GoF2Remake.Multiplayer.NetGame.IsServer;
            ShowDialog(CoopTitle, LobbyText(host), host ? () => StartCoopFromLobby() : (System.Action)null);
            dialogNo = LeaveLobby;
            var yes = root.Q<Button>("dialogYes");
            var no = root.Q<Button>("dialogNo");
            if (host) InputGlyph.SetText(yes, Localization.Extra("coopStart", "Start").UiCaps());
            else yes.style.display = DisplayStyle.None;
            InputGlyph.SetText(no, Localization.Extra("coopLeave", "Leave").UiCaps());
            Select(host ? yes : no);
            // Kept up to date while the crew comes and goes, until the host starts or the session ends.
            while (GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetGame.InLobby)
            {
                if (dialog.ClassListContains("dialog-backdrop--shown")) root.Q<Label>("dialogText").text = LobbyText(host);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yes.style.display = StyleKeyword.Null;
            if (dialog.ClassListContains("dialog-backdrop--shown")) CloseDialog();
            if (GoF2Remake.Multiplayer.NetGame.Active)
            {
                // Into the world: the fade as for any start.
                screen = MenuState.Leaving;
                fade.AddToClassList("fade--on");
                StartCoroutine(FadeMusic(0f, 1.2f));
            }
        }

        static string LobbyText(bool host)
        {
            var crew = GoF2Remake.Multiplayer.NetCoop.CrewNames();
            string list = string.Format(Localization.Extra("coopLobbyCrew", "Crew ({0}/{1}): {2}"), crew.Count,
                                        GoF2Remake.Data.CoopCampaign.CrewLimit, string.Join(", ", crew))
                + "\n" + Session.EconomyName(NetSession.Economy);   // the session's prices (the host's pick)
            string code = GoF2Remake.Multiplayer.NetGame.JoinCode;
            if (host && !string.IsNullOrEmpty(code)) list += "\n" + string.Format(Localization.Extra("coopLobbyCode", "Join code: {0}"), code);
            string wait = host ? Localization.Extra("coopLobbyHost", "Start once the crew is here: everyone begins the campaign together.")
                               : string.Format(Localization.Extra("coopLobbyWait", "Waiting for {0} to start the campaign."), crew.Count > 0 ? crew[0] : "");
            return list + "\n\n" + wait;
        }

        void StartCoopFromLobby()
        {
            if (GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetGame.InLobby) GoF2Remake.Multiplayer.NetCoop.StartCampaign();
        }

        void LeaveLobby()
        {
            root.Q<Button>("dialogYes").style.display = StyleKeyword.Null;
            GoF2Remake.Multiplayer.NetGame.Shutdown();
        }

        void HostCoop(int newCampaign)
        {
            GoF2Remake.Multiplayer.NetGame.CoopNewCampaign = newCampaign;
            StartCoroutine(LeaveForMultiplayer(null));
        }

        void SetHostMods(bool allowed)
        {
            GoF2Remake.Multiplayer.NetMods.HostAllowsMods = allowed;
            PlayerPrefs.SetInt("mp_allow_mods", allowed ? 1 : 0);
            ApplyHostMods();
        }

        void ApplyHostMods()
        {
            bool allowed = GoF2Remake.Multiplayer.NetMods.HostAllowsMods;
            root.Q<Button>("mpModsOff")?.EnableInClassList("choice-segment--active", !allowed);
            root.Q<Button>("mpModsOn")?.EnableInClassList("choice-segment--active", allowed);
            var label = root.Q<Label>("mpModsLabel");
            if (label != null)
            {
                var on = Modding.ModManager.SinglePlayerActive;
                int n = on.Count;
                // (script mods run in every player's game: the row says so)
                label.text = (n == 0 ? Localization.Extra("mpMods", "Mods")
                    : on.Exists(Modding.ModScripts.HasScripts) ? string.Format(Localization.Extra("mpModsCountScripts", "Mods ({0} on, with scripts)"), n)
                    : string.Format(Localization.Extra("mpModsCount", "Mods ({0} on)"), n)).UiCaps();
            }
        }

        /// <summary>The host card's settings scroll when they don't fit (several network adapters, a short screen), and the
        /// scroll bars are hidden everywhere: "More options" under them says so while settings are cut off below, and a
        /// tap scrolls down to them.</summary>
        void SetupHostMore()
        {
            var scroll = root.Q<ScrollView>("mpHostScroll");
            var more = root.Q<Label>("mpHostMore");
            if (scroll == null || more == null) return;
            more.text = "▼ " + Localization.Extra("mpMoreOptions", "More options").UiCaps();
            void Update()
            {
                float hidden = scroll.contentContainer.layout.height - scroll.contentViewport.layout.height;
                more.EnableInClassList("mp-host-more--shown", hidden > 1f && scroll.scrollOffset.y < hidden - 1f);
            }
            scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => Update());
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Update());
            scroll.verticalScroller.valueChanged += _ => Update();
            more.RegisterCallback<ClickEvent>(_ =>
                scroll.scrollOffset = new Vector2(0f, scroll.scrollOffset.y + scroll.contentViewport.layout.height * 0.8f));
        }

        void SetHostMode(HostMode mode)
        {
            Mode = mode;
            ApplyHostMode();
        }

        /// <summary>The host card for its choice: the segments, the text, the addresses only for the local network.</summary>
        void ApplyHostMode()
        {
            var mode = Mode;
            void Seg(string name, HostMode m) => root.Q<Button>(name)?.EnableInClassList("choice-segment--active", mode == m);
            Seg("mpModePublic", HostMode.Public);
            Seg("mpModePrivate", HostMode.Private);
            Seg("mpModeLocal", HostMode.Local);
            if (mpLocalBox != null) mpLocalBox.style.display = mode == HostMode.Local ? DisplayStyle.Flex : DisplayStyle.None;
            // Online: the address box's room goes (the fill still pushes Host to the bottom, level with Join).
            root.Q("mpHostFill")?.EnableInClassList("mp-card-fill--empty", mode != HostMode.Local);
            if (mpSessionName != null)
            {
                // Only a listed game has a name; the placeholder shows the default (the pilot name may have changed).
                mpSessionName.style.display = mode == HostMode.Public ? DisplayStyle.Flex : DisplayStyle.None;
                mpSessionName.textEdition.placeholder = string.Format(Localization.Extra("mpSessionNameHint", "Game name: {0}"), GoF2Remake.Multiplayer.NetGame.DefaultSessionName());
            }
            var text = root.Q<Label>("mpHostText");
            if (text != null)
                text.text = mode == HostMode.Public
                    ? Localization.Extra("mpHostPublicText", "Listed in the server browser; you also get a join code.")
                    : mode == HostMode.Private
                        ? Localization.Extra("mpHostPrivateText", "Not listed: friends join with the code you get.")
                        : Localization.Extra("mpHostText", "Players on your network join with an address (tap to copy):");
        }

        /// <summary>While the Multiplayer panel is open: the server list, again every 5 s.</summary>
        IEnumerator BrowseServers()
        {
            float next = 0f;
            while (openPanel != null && panels.TryGetValue("multiplayerPanel", out var mp) && openPanel == mp)
            {
                if (Time.unscaledTime >= next && !serverQueryRunning && screen == MenuState.Menu)
                {
                    next = Time.unscaledTime + ServerRefreshSeconds;
                    yield return QueryServers();
                }
                if (serverPingRevision != GoF2Remake.Multiplayer.NetPing.Revision)
                {
                    serverPingRevision = GoF2Remake.Multiplayer.NetPing.Revision;
                    foreach (var fill in serverPingRows) fill();
                }
                yield return null;
            }
            serverBrowse = null;
        }

        IEnumerator QueryServers()
        {
            if (mpServerList == null) yield break;
            serverQueryRunning = true;
            if (mpServerList.childCount == 0) ServerListNote(Localization.Extra("mpSearching", "Looking for games..."), true);
            var query = GoF2Remake.Multiplayer.NetLobby.Query();
            while (!query.IsCompleted) yield return null;
            serverQueryRunning = false;
            var list = query.IsFaulted ? null : query.Result;
            if (list != null && coopPanelMode) list = list.FindAll(x => x.coop);   // the Co-op campaign screen: open campaigns only
            var count = root.Q<Label>("mpServerCount");
            if (count != null)
                count.text = list == null ? "" : list.Count == 1 ? Localization.Extra("mpOneGame", "1 game")
                    : string.Format(Localization.Extra("mpGames", "{0} games"), list.Count);
            if (list == null) { ServerListNote(GoF2Remake.Multiplayer.NetGame.Status); yield break; }
            if (list.Count == 0) { ServerListNote(NoGamesText); yield break; }
            // The round trip to each listed game's Relay region (NetPing: one QoS measurement per region, at most
            // once a minute); the rows fill their ping in when it lands.
            GoF2Remake.Multiplayer.NetPing.Measure(list.ConvertAll(x => x.region));
            // Unchanged since the last refresh: the rows stay (a controller's focus on one too).
            var key = new System.Text.StringBuilder();
            foreach (var e in list) key.Append(e.code).Append(e.name).Append(e.players).Append('/').Append(e.maxPlayers).Append(e.password).Append(e.mods).Append(e.pvp).Append('|');
            if (key.ToString() == serverListKey) yield break;
            serverListKey = key.ToString();
            mpServerList.Clear();
            serverPingRows.Clear();
            foreach (var e in list)
            {
                var entry = e;
                // Two lines: the name and its tags, then the host, the players and the version.
                var row = new Button { focusable = true };
                row.AddToClassList("mp-address-row");
                row.AddToClassList("mp-server-row");
                var missingMods = entry.MissingMods;   // a modded game: the mods this game lacks (NetMods)
                // Its script mods (as far as this game has them) and those whose scripts this player hasn't allowed yet.
                var scriptMods = entry.Modded ? GoF2Remake.Multiplayer.NetMods.ScriptMods(entry.mods) : new System.Collections.Generic.List<string>();
                var unallowed = scriptMods.Count > 0 && missingMods.Count == 0 ? GoF2Remake.Multiplayer.NetMods.UnallowedScripts(entry.mods) : new System.Collections.Generic.List<string>();
                bool joinable = !entry.Full && entry.SameVersion && missingMods.Count == 0 && unallowed.Count == 0;
                row.EnableInClassList("mp-server-row--full", !joinable);
                var top = new VisualElement { pickingMode = PickingMode.Ignore };
                top.AddToClassList("mp-server-line");
                var name = new Label(entry.name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("mp-server-name");
                name.AddToClassList("gof-semibold");
                top.Add(name);
                void Tag(string text, string extra)
                {
                    var tag = new Label(text.UiCaps()) { pickingMode = PickingMode.Ignore };
                    tag.AddToClassList("mp-server-tag");
                    if (extra != null) tag.AddToClassList(extra);
                    top.Add(tag);
                }
                if (entry.dedicated) Tag(Localization.Extra("mpServerTag", "Server"), null);
                if (entry.coop) Tag(Localization.Extra("mpCoopTag", "Co-op"), "mp-server-tag--mods");   // a co-op campaign (NetCoop)
                if (entry.coop && entry.economy.HasValue) Tag(Session.EconomyName(entry.economy.Value), null);   // its prices (NetSession.Economy)
                if (entry.password) Tag(Localization.Extra("mpPasswordTag", "Password"), "mp-server-tag--password");
                if (entry.Modded) Tag(Localization.Extra("mpModdedTag", "Modded"), "mp-server-tag--mods");
                if (scriptMods.Count > 0) Tag(Localization.Extra("modsScripts", "Scripts"), "mp-server-tag--password");
                if (entry.pvp.HasValue)   // players fight anywhere / only in arenas and sieges (NetGame.FreePvp)
                    Tag(entry.pvp.Value ? Localization.Extra("mpPvp", "PvP") : Localization.Extra("mpPve", "PvE"),
                        entry.pvp.Value ? "mp-server-tag--pvp" : "mp-server-tag--pve");
                // Where the game is and how far away: the short name of its Relay region and the round trip to it,
                // with a green / amber / red dot (NetPing). A game listed by a build from before the region key has
                // none; a ping not measured yet reads "..." and fills in by itself.
                if (!string.IsNullOrEmpty(entry.region))
                {
                    var ping = new VisualElement { pickingMode = PickingMode.Ignore };
                    ping.AddToClassList("mp-server-ping");
                    var regionLabel = new Label(GoF2Remake.Multiplayer.NetPing.RegionName(entry.region)) { pickingMode = PickingMode.Ignore };
                    regionLabel.AddToClassList("mp-server-region");
                    ping.Add(regionLabel);
                    var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                    dot.AddToClassList("mp-server-ping-dot");
                    ping.Add(dot);
                    var msLabel = new Label { pickingMode = PickingMode.Ignore };
                    msLabel.AddToClassList("mp-server-ping-ms");
                    ping.Add(msLabel);
                    top.Add(ping);
                    void FillPing()
                    {
                        int ms = GoF2Remake.Multiplayer.NetPing.Of(entry.region);
                        int quality = GoF2Remake.Multiplayer.NetPing.Quality(ms);
                        msLabel.text = ms < 0 ? "..." : string.Format(Localization.Extra("mpPingMs", "{0} ms"), ms);
                        dot.EnableInClassList("mp-server-ping-dot--good", quality == 0);
                        dot.EnableInClassList("mp-server-ping-dot--fair", quality == 1);
                        dot.EnableInClassList("mp-server-ping-dot--poor", quality == 2);
                    }
                    FillPing();
                    serverPingRows.Add(FillPing);
                }
                row.Add(top);
                var bottom = new VisualElement { pickingMode = PickingMode.Ignore };
                bottom.AddToClassList("mp-server-line");
                string info = (entry.dedicated ? "" : entry.host + "  ·  ") + $"{entry.players} / {entry.maxPlayers}"
                              + (entry.Full ? "  ·  " + Localization.Extra("mpFull", "full") : "")
                              + (missingMods.Count > 0 ? "  ·  " + string.Format(Localization.Extra("mpNeedsModsShort", "needs mods: {0}"), string.Join(", ", missingMods))
                                 : unallowed.Count > 0 ? "  ·  " + string.Format(Localization.Extra("mpScriptsShort", "allow its scripts first: {0}"), string.Join(", ", unallowed))
                                 : entry.Modded ? "  ·  " + entry.modNames : "");
                var infoLabel = new Label(info) { pickingMode = PickingMode.Ignore };
                infoLabel.AddToClassList("mp-server-info");
                bottom.Add(infoLabel);
                // The version: every row; another version's says it can't be joined from here.
                var version = new Label(entry.SameVersion ? entry.version
                    : entry.version == GoF2Remake.Multiplayer.NetGame.Version ? Localization.Extra("mpOtherBuild", "another build")
                    : string.Format(Localization.Extra("mpNeedsVersion", "needs {0}"), entry.version)) { pickingMode = PickingMode.Ignore };
                version.AddToClassList("mp-server-version");
                version.EnableInClassList("mp-server-version--other", !entry.SameVersion);
                bottom.Add(version);
                row.Add(bottom);
                row.clicked += () =>
                {
                    if (joinable && entry.password && GoF2Remake.Multiplayer.NetGame.CleanPassword(GoF2Remake.Multiplayer.NetGame.JoinPassword).Length == 0)
                    {
                        // A password game: the field first (the server checks it).
                        SetMpStatus(Localization.Extra("mpEnterPassword", "This game needs a password: enter it under Join."));
                        mpJoinPassword?.Focus();
                    }
                    else if (joinable) WithName(() => StartCoroutine(LeaveForMultiplayer(entry.code)));
                    else if (mpStatus != null)
                        mpStatus.text = entry.Full ? Localization.Extra("mpGameFull", "That game is full.")
                            : entry.SameVersion && missingMods.Count > 0 ? string.Format(Localization.Extra("mpNeedsMods",
                                "Needs mods you don't have (or another version): {0}."), string.Join(", ", missingMods))
                            : entry.SameVersion && unallowed.Count > 0 ? GoF2Remake.Multiplayer.NetMods.ScriptRefusal(entry.mods)
                            : entry.version == GoF2Remake.Multiplayer.NetGame.Version
                            ? string.Format(Localization.Extra("mpOtherBuildText", "That game runs another build of {0}: only the same build can join."), entry.version)
                            : string.Format(Localization.Extra("mpOtherVersion", "That game runs version {0}, yours is {1}."), entry.version, GoF2Remake.Multiplayer.NetGame.Version);
                };
                mpServerList.Add(row);
            }
        }

        void ServerListNote(string text, bool busy = false)
        {
            serverListKey = "";
            mpServerList.Clear();
            serverPingRows.Clear();
            var note = new Label(text);
            note.AddToClassList("mp-server-empty");
            UiBusy.Mark(note, busy);
            mpServerList.Add(note);
        }

        /// <summary>The Multiplayer panel's status line; 'busy' puts the waiting arc before it (docs/UI-DESIGN.md "Waiting").</summary>
        void SetMpStatus(string text, bool busy = false)
        {
            if (mpStatus == null) return;
            mpStatus.text = text;
            UiBusy.Mark(mpStatus, busy && !string.IsNullOrEmpty(text));
        }

        string NoGamesText => coopPanelMode
            ? Localization.Extra("coopNoGames", "No open co-op campaigns right now. Host one, or join with a code.")
            : Localization.Extra("mpNoGames", "No public games right now. Host one, or join with a code.");

        void OpenMultiplayer()
        {
            SetPanelMode(false);
            OpenMultiplayerPanel();
        }

        void OpenMultiplayerPanel()
        {
            SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);   // why the last session ended
            OpenPanel("multiplayerPanel");
            // The first visit: the name first; Back there leaves the panel (#20: with a controller it came back to the
            // prompt on every move).
            if (NeedsName(null)) dialogNo = () => { if (openPanel == panels["multiplayerPanel"]) Back(); };
        }

        /// <summary>Multiplayer needs a pilot name (the others see it on the lock plate, in the chat and the pilot lists):
        /// without one (NetGame.PlayerName empty) a prompt asks for it, then runs 'then'. False = a name is set already.</summary>
        bool NeedsName(Action then)
        {
            if (GoF2Remake.Multiplayer.NetGame.Clean(GoF2Remake.Multiplayer.NetGame.PlayerName).Length > 0 || dialogField == null) return false;
            ShowDialog(Localization.Extra("mpNameTitle", "Pilot name"),
                       Localization.Extra("mpNameText", "The name other pilots see. Change it later at the top of the Multiplayer panel."), then);
            dialogField.value = "";
            dialogField.textEdition.placeholder = Localization.Extra("mpNamePlaceholder", "Your pilot name");
            dialogField.style.display = DisplayStyle.Flex;
            InputGlyph.SetText(root.Q<Button>("dialogYes"), "OK");
            InputGlyph.SetText(root.Q<Button>("dialogNo"), Localization.Get(170).UiCaps());   // Back
            dialogCheck = () =>
            {
                string name = GoF2Remake.Multiplayer.NetGame.Clean(dialogField.value);
                if (name.Length == 0) { dialogField.Focus(); return false; }
                GoF2Remake.Multiplayer.NetGame.PlayerName = name;
                if (mpName != null) mpName.SetValueWithoutNotify(GoF2Remake.Multiplayer.NetGame.PlayerName);
                ApplyHostMode();   // the default game name is the pilot's
                return true;
            };
            // With a controller the prompt opens on OK (A on it with no name, or Up, goes to the field): a field focused
            // at once brought the Xbox's keyboard up before the prompt had settled, and what it typed got lost (#20).
            if (InputMode.Current == InputKind.Gamepad) Select(root.Q<Button>("dialogYes"));
            else dialogField.Focus();
            return true;
        }

        /// <summary>Hosting and joining: 'action' once there is a pilot name (asked first when there is none).</summary>
        void WithName(Action action)
        {
            if (!NeedsName(action)) action();
        }

        /// <summary>The host card's addresses: one row per adapter (NetGame.LocalAddresses) with the port when it isn't the
        /// default; a tap copies it (what a friend types into Join).</summary>
        void RefreshAddresses()
        {
            if (mpAddressList == null) return;
            mpAddressList.Clear();
            ushort port = GoF2Remake.Multiplayer.NetGame.HostPort;
            var addresses = GoF2Remake.Multiplayer.NetGame.LocalAddresses();
            if (Debug.isDebugBuild) Debug.Log("Multiplayer addresses: " + string.Join(", ", addresses));
            if (addresses.Count == 0)
            {
                // No network: hosting still works on this device, but nobody else could join yet.
                var none = new Label(Localization.Extra("mpNoNetwork", "No network: connect to Wi-Fi or a hotspot so others can join."));
                none.AddToClassList("mp-card-text");
                mpAddressList.Add(none);
            }
            foreach (var (name, address) in addresses)
            {
                string join = port == GoF2Remake.Multiplayer.NetGame.DefaultPort ? address : $"{address}:{port}";
                var row = new Button { focusable = true };
                row.AddToClassList("mp-address-row");
                var n = new Label(name) { pickingMode = PickingMode.Ignore };
                n.AddToClassList("mp-address-name");
                var v = new Label(join) { pickingMode = PickingMode.Ignore };
                v.AddToClassList("mp-address-value");
                v.AddToClassList("gof-semibold");
                var c = new Label(Localization.Extra("mpCopy", "copy")) { pickingMode = PickingMode.Ignore };
                c.AddToClassList("mp-address-copy");
                row.Add(n); row.Add(v); row.Add(c);
                row.clicked += () =>
                {
                    GUIUtility.systemCopyBuffer = join;
                    foreach (var other in mpAddressList.Children()) other.RemoveFromClassList("mp-address-row--copied");
                    foreach (var other in mpAddressList.Query<Label>(className: "mp-address-copy").ToList()) other.text = Localization.Extra("mpCopy", "copy");
                    row.AddToClassList("mp-address-row--copied");
                    c.text = Localization.Extra("mpCopied", "copied");
                };
                mpAddressList.Add(row);
            }
        }

        /// <summary>Fades out, then hosts (address null: NetGame loads Space for everyone) or joins (the host's scene loads
        /// once connected). A failed start or connection fades the menu back in with the reason.</summary>
        IEnumerator LeaveForMultiplayer(string address)
        {
            if (screen != MenuState.Menu) yield break;
            screen = MenuState.Leaving;
            bool started;
            if (address == null && Mode != HostMode.Local)
            {
                // Online: the Relay session (and its listing) is reserved before the fade, so a failure shows here.
                SetMpStatus(Localization.Extra("mpOnlineStarting", "Starting an online session..."), true);
                var prep = GoF2Remake.Multiplayer.NetGame.PrepareOnlineHost(Mode == HostMode.Public ? SessionName() : null);
                while (!prep.IsCompleted) yield return null;
                if (prep.IsFaulted) GoF2Remake.Multiplayer.NetGame.AbortStart(prep.Exception);
                if (prep.IsFaulted || !prep.Result)
                {
                    SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);
                    screen = MenuState.Menu;
                    yield break;
                }
                SetMpStatus("");
                fade.AddToClassList("fade--on");
                StartCoroutine(FadeMusic(0f, 1.2f));
                yield return new WaitForSeconds(1.3f);
                started = TryStart(GoF2Remake.Multiplayer.NetGame.StartHost);
            }
            else if (address == null)
            {
                // Only fade out when it can start: a port in use says so at once (with a free one in the port field).
                if (!GoF2Remake.Multiplayer.NetGame.CanHost())
                {
                    SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);
                    if (GoF2Remake.Multiplayer.NetGame.SuggestedPort > 0 && mpPort != null) mpPort.value = GoF2Remake.Multiplayer.NetGame.SuggestedPort.ToString();
                    screen = MenuState.Menu;
                    yield break;
                }
                SetMpStatus("");
                fade.AddToClassList("fade--on");
                StartCoroutine(FadeMusic(0f, 1.2f));
                yield return new WaitForSeconds(1.3f);
                started = TryStart(GoF2Remake.Multiplayer.NetGame.StartHost);
            }
            else
            {
                // Joining: the menu stays while it connects (a missing host would be a long black screen); the fade once connected.
                SetMpStatus(string.Format(Localization.Extra("mpConnecting", "Connecting to {0}..."), address), true);
                if (GoF2Remake.Multiplayer.NetGame.IsJoinCode(address))
                {
                    // A join code: Relay finds the session, then it connects like an address.
                    var join = GoF2Remake.Multiplayer.NetGame.StartClientOnline(address);
                    while (!join.IsCompleted) yield return null;
                    if (join.IsFaulted) GoF2Remake.Multiplayer.NetGame.AbortStart(join.Exception);
                    started = !join.IsFaulted && join.Result;
                }
                else started = TryStart(() => GoF2Remake.Multiplayer.NetGame.StartClient(address));
                while (started && GoF2Remake.Multiplayer.NetGame.Active && !GoF2Remake.Multiplayer.NetGame.Connected) yield return null;
                if (started && GoF2Remake.Multiplayer.NetGame.Active)
                {
                    fade.AddToClassList("fade--on");
                    StartCoroutine(FadeMusic(0f, 1.2f));
                }
            }
            // A new co-op campaign's lobby: the crew gathers here until the host starts (the scene change ends this).
            while (started && GoF2Remake.Multiplayer.NetGame.Active && !GoF2Remake.Multiplayer.NetGame.InLobby) yield return null;
            if (started && GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetGame.InLobby) yield return CoopLobby();
            while (started && GoF2Remake.Multiplayer.NetGame.Active) yield return null;   // the scene change ends this
            SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);
            // The port was in use: the free one it found goes into the port field (hosting again uses it).
            if (address == null && GoF2Remake.Multiplayer.NetGame.SuggestedPort > 0 && mpPort != null)
                mpPort.value = GoF2Remake.Multiplayer.NetGame.SuggestedPort.ToString();
            fade.RemoveFromClassList("fade--on");
            screen = MenuState.Menu;
            StartCoroutine(FadeMusic(Settings.MusicVolume, 1f));
        }

        /// <summary>A host / join start that threw ends with its reason in the status line (NetGame.AbortStart), not a
        /// coroutine stopped on the fade or on "Connecting..." for good (a Discord report: unable to play multiplayer at all).</summary>
        static bool TryStart(System.Func<bool> start)
        {
            try { return start(); }
            catch (System.Exception e) { GoF2Remake.Multiplayer.NetGame.AbortStart(e); return false; }
        }
    }
    // Shared, data-driven tutorial presentation for pilot setup and station management.
    // The hosts own navigation, eligibility and acknowledgement; this class only reads and renders pages.
    internal static class ModTutorialLayout
    {
        internal sealed class Page
        {
            internal string ModName;
            internal ModInfo Mod;
            internal JObject Data;
            internal bool Contents;
        }

        static bool Text(JToken token, int max) => token != null && token.Type == JTokenType.String &&
            !string.IsNullOrWhiteSpace((string)token) && ((string)token).Length <= max;

        static bool Choice(JToken token, params string[] choices) =>
            token == null || (token.Type == JTokenType.String && choices.Contains((string)token));

        static bool Number(JToken token, int min, int max) => token == null ||
            (token.Type == JTokenType.Integer && (double)token >= min && (double)token <= max);

        internal static List<Page> ReadPages(string file, bool singlePlayerOnly = false)
        {
            var result = new List<Page>();
            foreach (var mod in ModManager.Active)
            {
                if (mod.Broken) continue;
                string path = mod.Source.Exists(file) ? file : "text/" + file;
                if (!mod.Source.Exists(path)) continue;
                try
                {
                    if (!(ModJson.Read(mod.Source, path) is JObject document) ||
                        !(document["pages"] is JArray pages) || pages.Count > 20 ||
                        !Choice(document["navigation"], "linear", "contents"))
                        throw new ModJsonException("Expected a pages array with at most 20 entries.");
                    if (singlePlayerOnly && (bool?)document["singlePlayer"] != true) continue;
                    var parsed = new List<Page>();
                    foreach (var entry in pages)
                    {
                        if (!(entry is JObject page) || !Text(page["title"], 120) ||
                            !(page["sections"] is JArray sections) || sections.Count < 1 || sections.Count > 24 ||
                            !Choice(page["layout"], "column", "cards", "grid") ||
                            !Number(page["columns"], 1, 3) || !Number(page["gap"], 0, 32) ||
                            !Number(page["titleSize"], 20, 40))
                            throw new ModJsonException("Invalid tutorial page title, sections or layout.");
                        foreach (var section in sections)
                        {
                            if (!(section is JObject block) ||
                                !Choice(block["style"], "text", "card", "warning", "info", "divider") ||
                                ((string)block["style"] != "divider" && !Text(block["text"], 2000)) ||
                                (block["text"] != null && !Text(block["text"], 2000)) ||
                                (block["heading"] != null && !Text(block["heading"], 120)) ||
                                !Number(block["headingSize"], 14, 32) || !Number(block["textSize"], 12, 26) ||
                                !Number(block["imageHeight"], 64, 480) ||
                                (block["image"] != null && !Text(block["image"], 240)) ||
                                !Choice(block["alignment"], "left", "center", "right") ||
                                (block["fullWidth"] != null && block["fullWidth"].Type != JTokenType.Boolean))
                                throw new ModJsonException("Invalid tutorial section.");
                        }
                        parsed.Add(new Page
                        {
                            ModName = mod.Name, Mod = mod, Data = page,
                            Contents = (string)document["navigation"] == "contents"
                        });
                    }
                    result.AddRange(parsed);
                }
                catch (Exception e) { Debug.LogWarning("Mod " + mod.Id + " " + path + ": " + e.Message); }
            }
            return result;
        }

        static Label LabelFor(string text, int size, string alignment = "left")
        {
            var label = new Label(text) { enableRichText = false };
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = Color.white;
            label.style.flexShrink = 0;
            label.style.marginBottom = 6;
            label.style.unityTextAlign = alignment == "center" ? TextAnchor.UpperCenter :
                alignment == "right" ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            return label;
        }

        internal static void DrawPage(VisualElement host, List<Page> pages, int index, int visited,
            Action<int> selectPage, string buttonClass)
        {
            if (!host.ClassListContains("mod-tutorial-keyboard"))
            {
                host.AddToClassList("mod-tutorial-keyboard");
                host.RegisterCallback<KeyDownEvent>(e =>
                {
                    var view = host.Q<ScrollView>("modTutorialScroll");
                    if (view == null) return;
                    float step = view.contentViewport.layout.height * 0.85f;
                    float max = Mathf.Max(0, view.contentContainer.layout.height - view.contentViewport.layout.height);
                    float y = view.scrollOffset.y;
                    switch (e.keyCode)
                    {
                        case KeyCode.PageUp: y -= step; break;
                        case KeyCode.PageDown: y += step; break;
                        case KeyCode.Home: y = 0; break;
                        case KeyCode.End: y = max; break;
                        default: return;
                    }
                    if (float.IsNaN(y) || float.IsNaN(max)) return;
                    view.scrollOffset = new Vector2(0, Mathf.Clamp(y, 0, max));
                    e.StopPropagation();
                }, TrickleDown.TrickleDown);
            }
            var entry = pages[index];
            var page = entry.Data;
            var progress = LabelFor(entry.ModName + "  |  " + (index + 1) + " / " + pages.Count, 15);
            host.Add(progress);
            var title = LabelFor((string)page["title"], (int?)page["titleSize"] ?? 30);
            title.AddToClassList("gof-semibold");
            host.Add(title);

            var workspace = new VisualElement();
            workspace.style.flexGrow = 1;
            workspace.style.flexBasis = 0;
            workspace.style.minHeight = 0;
            workspace.style.minWidth = 0;
            workspace.style.flexDirection = FlexDirection.Column;
            host.Add(workspace);

            if (entry.Contents)
            {
                var contents = new ScrollView(ScrollViewMode.Horizontal)
                {
                    horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                    verticalScrollerVisibility = ScrollerVisibility.Hidden
                };
                contents.AddToClassList("gof-no-scroll-bar");
                contents.AddManipulator(new DragScroll(contents));
                contents.style.flexShrink = 0;
                contents.style.height = 52;
                contents.contentContainer.style.flexDirection = FlexDirection.Row;
                workspace.Add(contents);
                for (int i = 0; i < pages.Count; i++)
                {
                    int target = i;
                    string caption = (string)pages[i].Data["title"];
                    var button = new Button(() => selectPage(target))
                    {
                        text = (i + 1) + ". " + caption,
                        tooltip = caption
                    };
                    button.AddToClassList(buttonClass);
                    button.style.minHeight = 42;
                    button.style.whiteSpace = WhiteSpace.Normal;
                    button.style.flexShrink = 0;
                    button.style.maxWidth = 212;
                    button.style.marginBottom = 4;
                    button.style.marginRight = 4;
                    button.SetEnabled(i <= visited && i != index);
                    if (i == index) button.style.backgroundColor = new Color(0.18f, 0.36f, 0.46f);
                    contents.Add(button);
                }
                contents.RegisterCallback<FocusInEvent>(e =>
                {
                    if (!DragScroll.PointerActive && e.target is VisualElement target && contents.contentContainer.Contains(target))
                        contents.ScrollTo(target);
                });
                workspace.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    bool wide = e.newRect.width >= 760;
                    workspace.style.flexDirection = wide ? FlexDirection.Row : FlexDirection.Column;
                    contents.mode = wide ? ScrollViewMode.Vertical : ScrollViewMode.Horizontal;
                    contents.style.width = wide ? new StyleLength(220) : new StyleLength(Length.Percent(100));
                    contents.style.height = wide ? new StyleLength(StyleKeyword.Auto) : new StyleLength(52);
                    contents.style.marginRight = wide ? 12 : 0;
                    contents.contentContainer.style.flexDirection = wide ? FlexDirection.Column : FlexDirection.Row;
                });
            }

            var body = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "modTutorialScroll",
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden
            };
            body.style.flexGrow = 1;
            body.style.flexBasis = 0;
            body.style.minHeight = 0;
            body.style.minWidth = 0;
            body.style.marginRight = 12;
            body.AddManipulator(new DragScroll(body));
            workspace.Add(body);
            ScrollBars.Attach(body);
            // Focused content stays visible; the game already handles right-stick scrolling.
            body.RegisterCallback<FocusInEvent>(e =>
            {
                if (!DragScroll.PointerActive && e.target is VisualElement target && body.contentContainer.Contains(target))
                    body.ScrollTo(target);
            });
            var sections = new VisualElement();
            sections.style.flexShrink = 0;
            sections.style.flexDirection = FlexDirection.Row;
            sections.style.flexWrap = Wrap.Wrap;
            sections.style.alignItems = Align.Stretch;
            body.Add(sections);

            string layout = (string)page["layout"] ?? "column";
            int wanted = layout == "column" ? 1 : (int?)page["columns"] ?? 2;
            int gap = (int?)page["gap"] ?? 10;
            var panels = new List<(VisualElement panel, bool full)>();
            foreach (JObject block in (JArray)page["sections"])
            {
                string style = (string)block["style"] ?? "text";
                string alignment = (string)block["alignment"] ?? "left";
                var panel = new VisualElement();
                panel.style.flexShrink = 0;
                panel.style.flexGrow = 0;
                panel.style.minWidth = 0;
                panel.style.marginBottom = gap;
                if (style == "divider")
                {
                    panel.style.height = 1;
                    panel.style.backgroundColor = new Color(0.35f, 0.5f, 0.6f);
                }
                else
                {
                    panel.style.paddingLeft = panel.style.paddingRight = style == "text" ? 4 : 14;
                    panel.style.paddingTop = panel.style.paddingBottom = style == "text" ? 4 : 12;
                    if (style != "text")
                    {
                        panel.AddToClassList(style == "warning" ? "identity-briefing-warning" : "identity-briefing-card");
                        if (!Settings.OriginalUi)
                            panel.style.backgroundColor = style == "warning" ? new Color(0.26f, 0.15f, 0.055f) :
                                style == "info" ? new Color(0.09f, 0.22f, 0.30f) : new Color(0.07f, 0.17f, 0.23f);
                    }
                    if (block["heading"] != null)
                    {
                        var heading = LabelFor((string)block["heading"], (int?)block["headingSize"] ?? 22, alignment);
                        heading.AddToClassList("gof-semibold");
                        panel.Add(heading);
                    }
                    if (block["image"] != null)
                    {
                        string path = (string)block["image"];
                        // Use the mod's own asset source and the game's managed texture cache, never an external URL.
                        if (entry.Mod.Source.Exists(path))
                        {
                            try
                            {
                                var texture = ModMaterials.Texture(entry.Mod, path, false);
                                if (texture != null)
                                {
                                    var illustration = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
                                    illustration.style.height = (int?)block["imageHeight"] ?? 180;
                                    illustration.style.flexShrink = 0;
                                    illustration.style.marginBottom = 8;
                                    panel.Add(illustration);
                                }
                            }
                            catch (Exception e) { Debug.LogWarning("Mod " + entry.Mod.Id + " tutorial image " + path + ": " + e.Message); }
                        }
                        else Debug.LogWarning("Mod " + entry.Mod.Id + " tutorial image missing: " + path);
                    }
                    panel.Add(LabelFor((string)block["text"], (int?)block["textSize"] ?? 18, alignment));
                }
                sections.Add(panel);
                panels.Add((panel, style == "divider" || (bool?)block["fullWidth"] == true));
            }
            void Reflow(float width)
            {
                if (float.IsNaN(width) || width <= 0) return;
                int columns = Mathf.Clamp(Mathf.FloorToInt((width + gap) / (280f + gap)), 1, wanted);
                // Fixed widths keep grid cells aligned even on a partly filled last row.
                float cell = Mathf.Max(1, (width - (columns - 1) * gap) / columns);
                int column = 0;
                foreach (var item in panels)
                {
                    bool full = item.full || columns == 1;
                    // A full-width block starts a fresh row naturally under flex wrapping.
                    item.panel.style.width = full ? width : cell;
                    item.panel.style.marginRight = full || column == columns - 1 ? 0 : gap;
                    column = full ? 0 : (column + 1) % columns;
                }
            }
            // Give a safe single-column initial layout, then calculate columns from the actual viewport.
            foreach (var item in panels) item.panel.style.width = Length.Percent(100);
            sections.RegisterCallback<GeometryChangedEvent>(e => Reflow(e.newRect.width));
        }
    }

}
