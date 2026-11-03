// MultiplayerWindow.cs
// Remake-only: the station's multiplayer window (Chat / Faction / Arena / Profile / Admin) in a session, the buttons for
// everything the chat commands do (NetFactions, NetArena, NetProfiles). A "MULTIPLAYER" button in the top bar, left of the
// Menu button, opens it (a dot on it when a faction invitation or a duel challenge waits). Tabs:
//   Chat: the whole chat (NetChat: the last lines, kept while the window lives), the channel (Local / Global), the line
//     and a Send button; Enter sends, a line starting with "/" is a command (NetCommands); the flight / station chat
//     panel (ChatView) hides meanwhile. Built once (a snapshot doesn't rebuild it: the line keeps its focus and draft);
//   Squad (NetSquad, client-side, rebuilt when it changes): invitations (Accept / Decline), the members with where they
//     are and a distress call (Help), Leave, the pilots docked here to Invite; distress calls themselves are made in space
//     (the flight squad window, the E menu, /sos), which the tab says;
//   Trade (NetTradeClient, client-side like Squad): the trade requests (Accept / Decline); without a trade the pilots
//     docked here to Trade with; in one, this player's offer (credits, the goods with -1 / remove) beside the other's,
//     the tradeable cargo to add (+1 / +10 / All), who accepted, Accept trade / Cancel trade. It opens by itself when a
//     trade starts (NetTradeClient.Opened);
//   Faction: invitations (Join), without a faction a Create form and the factions; in one: the bank (Deposit / Withdraw), the
//     members (Promote / Demote / Make leader / Kick by rank), the pilots online to Invite, the territory (the claims,
//     and for the station docked at: Claim / Make home / Unclaim / Siege), the sieges, Leave / Disband (asked twice);
//   Arena: a challenge waiting (Accept / Decline), the Void fighters option, the pilots to Challenge, the free-for-all
//     queue (Join / Leave), the matches, the leaderboard;
//   Profile: the profile, Take control (another device controls it), Get a link code, Link with a code (+ force);
//   Admin (only for the server's ops, admins and masters, NetModeration): a reason and minutes field, every pilot online
//     with Kick (the minutes as the cooldown), Ban for the minutes, Ban for good and the roles (admins: op; masters:
//     admin), the bans with Unban; for admins also the server's status and an announcement, the staff, every profile
//     (a filter; Ban / Unban, roles, Delete for masters, asked twice), the factions (Disband, asked twice) and the server's
//     settings (NetServerSettings: a field or a switch with Save each; the ones the launcher's command line sets say so).
// The Profile tab also has "Claim this server" (the server's admin token, /claimadmin) for its owner.
// Every button sends the chat command (NetPanel.Command) and the window shows the server's answer (the next chat notice)
// at its foot. The content comes from the server's snapshot (NetPanel.Latest, asked for every 2 s while open) and is
// rebuilt only when it changed, so a text field keeps its focus. Esc / B closes it (StationMenu.Back); while it is open
// the station menu's own keys wait. Built in code; the buttons use Squad.uss.
// Rework (players' report from a Retroid Pocket G2): on a small high-density screen (UiScale.Large) the window fills the
// screen with text about 1.45x and finger-sized buttons (.mpw--large); a phone typing into a field moves the window to the
// top half (the on-screen keyboard covers the bottom) and scrolls to the field. Controllers / keys: LB / RB (Q / E) switch
// the tabs, the focus starts on the current tab, the list scrolls to the focused control, and a rebuild (a new snapshot)
// puts the focus back on the same button; the Chat tab takes the focus into its line only with keys and mouse (a
// controller's D-pad stayed trapped in it, a phone's keyboard covered the window at once).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed partial class MultiplayerWindow : MonoBehaviour
    {
        enum Tab { Chat, Squad, Trade, Faction, Arena, Profile, Admin }

        static MultiplayerWindow current;

        /// <summary>The window is open (StationMenu: Esc closes it, its keys wait).</summary>
        public static bool IsOpenAny => current != null && current.isOpen;

        public static void CloseAny() { if (current != null) current.Close(); }

        VisualElement plate, window, tabs, body, chatPane, flightColumn;
        Button sosButton;
        ScrollView scroll, chatScroll;
        TextField chatField;
        Button chatChannel, menuButton;
        Button plateButton;
        Label status;
        Tab tab;
        bool isOpen, voids, force, confirmLeave;
        string tagText = "", nameText = "", amountText = "", linkText = "", reasonText = "", minutesText = "5";
        string sayText = "", filterText = "", tokenText = "", confirmKey = "";
        readonly Dictionary<string, string> settingEdits = new Dictionary<string, string>();
        int shownRole = -1;
        float refresh, answerUntil;
        bool typing;              // one of the window's text fields has the focus: the game's keys are off (NetChat.SetTyping)
        bool pendingRebuild;      // a snapshot came while a field had the focus or a finger / button was down
        float pressedSince = -1f; // a pointer went down on the window (unscaled time), -1 = none
        StyleSheet sheet;
        VisualElement host;           // the parent the window was built on (the scale's measure)
        bool large, raised;           // UiScale.Large; the window moved up for a phone's keyboard
        float nextScaleCheck;
        Button currentTabButton;
        /// <summary>The text's scale: 1, or LargeScale on a small high-density screen.</summary>
        static float scale = 1f;
        VisualElement scrim;
        const float LargeScale = 1.45f;

        static readonly Color Panel = new Color(0.02f, 0.04f, 0.07f, 0.93f), Accent = new Color(0.56f, 0.85f, 1f),
                              Dim = new Color(0.85f, 0.92f, 0.97f, 0.65f), Good = new Color(0.47f, 0.9f, 0.55f), Bad = new Color(1f, 0.55f, 0.47f);

        /// <summary>The window's button and window on 'parent' (the station menu's or the flight HUD's safe area).</summary>
        public static void Attach(GameObject host, VisualElement parent, bool flight = false)
        {
            if (parent == null) return;
            var view = host.GetComponent<MultiplayerWindow>();
            if (view == null) view = host.AddComponent<MultiplayerWindow>();
            view.flight = flight;
            view.Build(parent);
        }

        /// <summary>The flight HUD's: its own button on the right (under the readout, over the squad window), N toggles it.</summary>
        bool flight;

        /// <summary>FlightHud: N (or the button) opens / closes it.</summary>
        public static void ToggleAny() { if (current != null) current.Toggle(); }

        /// <summary>A text field of the window has the focus (its keys aren't the game's: FlightHud leaves N alone).</summary>
        public static bool TypingAny => current != null && current.isOpen && TextFieldKeys.InTextField(current.window?.focusController?.focusedElement);

        void OnEnable()
        {
            current = this;
            NetPanel.Changed += OnChanged;
            NetChat.Added += OnChat;
            NetTradeClient.Opened += OnTradeOpened;
        }

        void OnDisable()
        {
            if (current == this) current = null;
            NetPanel.Changed -= OnChanged;
            NetChat.Added -= OnChat;
            NetTradeClient.Opened -= OnTradeOpened;
        }

        /// <summary>The Trade tab opened (the pilot list's Trade, a trade request's Accept).</summary>
        public static void OpenTradeAny() { if (current != null && !current.flight) current.OpenTab(Tab.Trade); }

        /// <summary>A trade started: its tab (docked: the station's window).</summary>
        void OnTradeOpened() { if (!flight) OpenTab(Tab.Trade); }

        void OpenTab(Tab t)
        {
            tab = t;
            confirmLeave = false;
            BuildTabs();
            if (isOpen) { ShowPane(); Rebuild(); }
            else Open();
        }

        void OnDestroy()
        {
            if (typing) { typing = false; NetChat.DropTyping(); }   // the scene's actions go with it (ChatView does the same)
            sosButton?.RemoveFromHierarchy();
            plate?.RemoveFromHierarchy();
            window?.RemoveFromHierarchy();
            flightColumn?.RemoveFromHierarchy();
        }

        // ---- building -------------------------------------------------------------------------------------

        /// <summary>ChatView's Chat button takes the classes of the button it sits beside (the station's top-bar button, the
        /// flight HUD's Multiplayer button): the same size, border, font and padding. Its own state classes stay
        /// (chat-tab--open...); the chat's base look (.chat-tab) goes.</summary>
        Button movedChat;   // the Chat button placed beside this window's button (its large class follows UpdateLayout)

        // ---- open / close / refresh -----------------------------------------------------------------------

        void Toggle() { if (isOpen) Close(); else Open(); }

        void Open()
        {
            if (!NetGame.Active) return;
            isOpen = true;
            confirmLeave = false;
            window.style.display = DisplayStyle.Flex;
            if (scrim != null) { scrim.style.display = DisplayStyle.Flex; scrim.BringToFront(); }
            window.BringToFront();
            if (FocusesItself) FocusLater(currentTabButton);
            status.text = "";
            refresh = 0f;
            ShowPane();
            Rebuild();
        }

        void Close()
        {
            isOpen = false;
            pressedSince = -1f;
            if (window != null) window.style.display = DisplayStyle.None;
            if (scrim != null) scrim.style.display = DisplayStyle.None;
            if (window?.focusController?.focusedElement is VisualElement f) f.Blur();
        }

        void Update()
        {
            if (plate == null) return;
            // The window's text fields: the game's own keys (the station's 1 / 2 / M / L, the flight controls, the hangar's
            // A / D) are off while one has the focus.
            bool fieldFocused = TypingAny;
            if (fieldFocused != typing) { typing = fieldFocused; NetChat.SetTyping(fieldFocused); }
            UpdateLayout();
            UpdateTabKeys();
            if (pendingRebuild && !Busy) { pendingRebuild = false; Rebuild(); }
            UpdateSos();
            bool session = NetGame.Active;
            plate.style.display = session ? DisplayStyle.Flex : DisplayStyle.None;
            // In the top bar: the new button takes the bar's free space on its left while it shows.
            if (plate == plateButton && menuButton != null)
                menuButton.style.marginLeft = session ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);
            if (!session) { if (isOpen) Close(); return; }
            var s = NetPanel.Latest;
            bool waiting = (s != null && (s.factionInvites.Count > 0 || s.duelFrom.Length > 0))
                           || NetTradeClient.Requests.Count > 0 || (NetTradeClient.Current != null && !(isOpen && tab == Tab.Trade));
            bool unread = unreadChat && !(isOpen && tab == Tab.Chat);
            string label = plate == plateButton ? Localization.Extra("mpMultiplayer", "Multiplayer") : Localization.Extra("mpFactionArena", "Faction · Arena");
            plateButton.text = (label + (waiting || unread ? "  •" : "")).UiCaps();
            if (isOpen && tab == Tab.Squad && SquadKey() != squadKey) Rebuild();
            if (isOpen && tab == Tab.Trade && TradeKey() != tradeKey && !Busy) Rebuild();
            // The plate's dot needs a snapshot now and then even while the window is closed.
            if ((refresh -= Time.unscaledDeltaTime) <= 0f) { refresh = isOpen ? NetPanel.RefreshSeconds : NetPanel.RefreshSeconds * 3f; NetPanel.Request(); }
        }

        void OnChanged()
        {
            if (NetPanel.Latest != null && NetPanel.Latest.role != shownRole && tabs != null) BuildTabs();   // made an op / admin, or no longer
            if (!isOpen || tab == Tab.Squad || tab == Tab.Trade) return;   // they follow the players, not the snapshot
            // Not under a finger or a text field being typed into (a rebuild made new fields: the focus and Android's keyboard
            // went, and a pressed button was gone before its release): once they are done (Update).
            if (Busy) pendingRebuild = true;
            else Rebuild();
        }

        /// <summary>A pointer is down on the window (at most 5 s: a release lost elsewhere) or one of its text fields has the
        /// focus: the content stays as it is meanwhile.</summary>
        bool Busy => (pressedSince >= 0f && Time.unscaledTime - pressedSince < 5f)
                     || TextFieldKeys.InTextField(window?.focusController?.focusedElement) && body != null
                        && body.Contains(window.focusController.focusedElement as VisualElement);

        /// <summary>The flight button: in a squad in space (or while a call runs), its text the call's state.</summary>
        void UpdateSos()
        {
            if (sosButton == null) return;
            var me = NetPlayer.Local;
            bool show = NetGame.Active && (NetDistress.Active || (me != null && me.InSpace && NetSquad.LocalSquad != 0 && !NetArena.IsArenaOrbit(me.Station)));
            sosButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            string text = (NetDistress.Active ? Localization.Extra("mpDistressEnd", "End the call") : Localization.Extra("mpDistressCall", "Distress call")).UiCaps();
            if (sosButton.text != text) sosButton.text = text;
            sosButton.EnableInClassList("squad-button--leave", !NetDistress.Active);
        }

        /// <summary>FlightHud: the "Distress call" binding (unbound by default) or the button.</summary>
        public static void ToggleDistressAny() { if (current != null && current.flight) current.ToggleDistress(); }

        void ToggleDistress()
        {
            string msg = NetDistress.Toggle();
            if (!string.IsNullOrEmpty(msg)) NetChat.Notice(msg);
        }

        /// <summary>The server's answer to a button (the next notice), and invitations / challenges as they come.</summary>
        void OnChat(NetChat.Message m)
        {
            if (m == null) return;
            AddChatLine(m);
            if (m.channel != NetChat.Channel.Notice && !m.own && !(isOpen && tab == Tab.Chat)) unreadChat = true;
            if (!isOpen || m.channel != NetChat.Channel.Notice || tab == Tab.Chat) return;
            if (Time.unscaledTime < answerUntil) status.text = m.text;
            NetPanel.Request();
        }

        void Send(string command)
        {
            answerUntil = Time.unscaledTime + 4f;
            status.text = "…";
            confirmLeave = false;
            NetPanel.Command(command);
        }

        // ---- the tabs ---------------------------------------------------------------------------------------

        // The garrison being edited per claim (ships, level), until it is sent or the claim is gone.
        readonly Dictionary<int, (int ships, int level)> garrisonEdit = new Dictionary<int, (int, int)>();

        // The VAT rate being edited per claim, until it is sent or the claim is gone.
        readonly Dictionary<int, int> vatEdit = new Dictionary<int, int>();

        // ---- small builders ---------------------------------------------------------------------------------

        // ---- the Squad tab --------------------------------------------------------------------------------------

        string squadKey = "";

        // ---- the Trade tab --------------------------------------------------------------------------------------

        string tradeKey = "";

        // ---- the Chat tab ---------------------------------------------------------------------------------------

        bool unreadChat;
        const int ChatKeep = 200;

    }
}
