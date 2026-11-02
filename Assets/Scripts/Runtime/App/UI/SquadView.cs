// SquadView.cs
// Multiplayer squad UI (NetSquad) in the flight HUD and the station menu, while a session runs:
//   the squad window (right side, only while in a squad; the header collapses it): every member with where they are
//     (an orbit or docked, by station name), a shield bar and a hull bar with the armor over it (like the HUD's; a pool the
//     ship lacks: not shown), and Leave; a member calling for help shows a red "⚠ HELP" and a Help button (NetDistress:
//     the Khador Drive or the autopilot to them); the local player's own row has Distress call / End the call in space;
//   in the station, the pilot list (collapsible too, only with other players docked here): each with Invite (or "In your
//     squad" / "Invited") and Trade (NetTradeClient; or "Asked" / "Trading", which opens the Multiplayer window's Trade tab);
//   an invitation popup (only while docked: squads form in a hangar): "<name> invites you to their squad", and when this
//     player has a mission that accepting abandons it (NetMissions.AbandonWarning), with Accept / Decline (45 s);
//   a trade request popup (docked, while no squad invitation shows): "<name> wants to trade with you", Accept (the
//     Multiplayer window's Trade tab opens) / Decline;
//   in the station, an online session's join code with Copy, on its own plate under the system information.
// The rows are rebuilt only when their content changes (a rebuilt button would lose a press); the bars update live.
// Styles: Resources/GoF2Net/Squad.uss.

using System.Collections.Generic;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class SquadView : MonoBehaviour
    {
        const float RefreshSeconds = 0.25f;
        /// <summary>Pilot rows the list shows at most (the rest as "+N more"): a busy hangar filled the screen.</summary>
        const int MaxPilotRows = 6;

        static bool squadCollapsed, pilotsCollapsed, collapseChecked;
        /// <summary>The view of the scene on screen (the station's or the flight HUD's), for the popups below.</summary>
        static SquadView current;
        VisualElement codePlate;
        Label codeLabel;
        Button copyButton;

        VisualElement box, squadPanel, squadBody, pilotsPanel, pilotsBody, invitePopup, tradePopup;
        Button squadHeader, pilotsHeader, inviteAccept, inviteDecline, tradeAccept, tradeDecline;
        Label inviteText, tradeText;
        bool hangar;
        int hereStation = -1;
        string squadKey = "", pilotsKey = "";
        NetSquad.Invite shownInvite;
        NetTradeClient.Request shownTrade;
        readonly Dictionary<ulong, (VisualElement shield, VisualElement armor, VisualElement hull)> bars = new Dictionary<ulong, (VisualElement, VisualElement, VisualElement)>();
        float refresh;
        Database db;

        /// <summary>The squad UI on 'parent' (again after a UI reload); 'hangarStation' &gt;= 0 in the station: its pilot list.</summary>
        public static void Attach(GameObject host, VisualElement parent, int hangarStation = -1)
        {
            if (parent == null) return;
            var view = host.GetComponent<SquadView>();
            if (view == null) view = host.AddComponent<SquadView>();
            view.Build(parent, hangarStation);
        }

        void Build(VisualElement parent, int hangarStation)
        {
            db = Database.Load();
            current = this;
            hangar = hangarStation >= 0;
            hereStation = hangarStation;
            box?.RemoveFromHierarchy();
            invitePopup?.RemoveFromHierarchy();
            tradePopup?.RemoveFromHierarchy();
            squadKey = pilotsKey = "";
            shownInvite = null;
            shownTrade = null;
            var sheet = Resources.Load<StyleSheet>("GoF2Net/Squad");

            box = new VisualElement { name = "squad", pickingMode = PickingMode.Ignore };
            box.AddToClassList("squad");
            box.EnableInClassList("squad--flight", !hangar);   // under the flight HUD's Multiplayer / Distress call column
            if (sheet != null) box.styleSheets.Add(sheet);
            squadPanel = Panel(out squadHeader, out squadBody, () => { squadCollapsed = !squadCollapsed; squadKey = ""; });
            box.Add(squadPanel);
            pilotsPanel = Panel(out pilotsHeader, out pilotsBody, () => { pilotsCollapsed = !pilotsCollapsed; pilotsKey = ""; });
            box.Add(pilotsPanel);
            // Under everything else on that layer (the hangar, missions and status windows, the HUD): the first child.
            parent.Insert(0, box);

            invitePopup = Popup(sheet, Localization.Extra("mpInviteTitle", "Squad invitation"), out inviteText,
                                Localization.Extra("mpAccept", "Accept"), () => { if (shownInvite != null) NetSquad.Accept(shownInvite); shownInvite = null; },
                                Localization.Extra("mpDecline", "Decline"), () => { if (shownInvite != null) NetSquad.Decline(shownInvite); shownInvite = null; },
                                out inviteAccept, out inviteDecline);
            parent.Add(invitePopup);

            // A trade request (NetTradeClient): the same popup.
            tradePopup = Popup(sheet, Localization.Extra("mpTradeRequestTitle", "Trade request"), out tradeText,
                               Localization.Extra("mpAccept", "Accept"), () =>
                               {
                                   if (shownTrade != null) { NetTradeClient.Accept(shownTrade); MultiplayerWindow.OpenTradeAny(); }
                                   shownTrade = null;
                               },
                               Localization.Extra("mpDecline", "Decline"), () => { if (shownTrade != null) NetTradeClient.Decline(shownTrade); shownTrade = null; },
                               out tradeAccept, out tradeDecline);
            parent.Add(tradePopup);
            BuildCodePlate(parent, sheet);
            Refresh();
        }

        /// <summary>The station: an online session's join code (for asking friends in; a tap on Copy copies it) on its own
        /// plate under the system information.</summary>
        void BuildCodePlate(VisualElement parent, StyleSheet sheet)
        {
            codePlate?.RemoveFromHierarchy();
            codePlate = null;
            var info = hangar ? parent.Q(className: "station-info") : null;
            if (info == null || info.parent == null) return;
            codePlate = new VisualElement { name = "joinCodePlate" };
            codePlate.AddToClassList("station-info");
            codePlate.AddToClassList("squad-code");
            if (sheet != null) codePlate.styleSheets.Add(sheet);
            var title = new Label(Localization.Extra("mpJoinCode", "Join code").ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("info-line");
            title.AddToClassList("squad-code-title");
            codePlate.Add(title);
            var row = new VisualElement();
            row.AddToClassList("squad-code-row");
            codeLabel = new Label { pickingMode = PickingMode.Ignore };
            codeLabel.AddToClassList("info-system");
            codeLabel.AddToClassList("gof-semibold");
            codeLabel.AddToClassList("squad-code-value");
            row.Add(codeLabel);
            copyButton = MakeButton(Localization.Extra("mpCopy", "copy"), () =>
            {
                if (NetGame.JoinCode == null) return;
                GUIUtility.systemCopyBuffer = NetGame.JoinCode;
                copyButton.text = Localization.Extra("mpCopied", "copied").ToUpperInvariant();
            }, null);
            row.Add(copyButton);
            codePlate.Add(row);
            info.parent.Insert(info.parent.IndexOf(info) + 1, codePlate);
        }

        void RefreshCodePlate()
        {
            if (codePlate == null) return;
            string code = NetGame.Active ? NetGame.JoinCode : null;
            codePlate.style.display = code != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (code != null && codeLabel.text != code)
            {
                codeLabel.text = code;
                copyButton.text = Localization.Extra("mpCopy", "copy").ToUpperInvariant();
            }
        }

        static VisualElement Panel(out Button header, out VisualElement body, System.Action toggle)
        {
            var panel = new VisualElement();
            panel.AddToClassList("squad-panel");
            header = new Button(toggle) { focusable = false };   // clicks / taps only: Space or a controller's A in flight never press it
            header.AddToClassList("squad-header");
            panel.Add(header);
            body = new VisualElement();
            body.AddToClassList("squad-body");
            panel.Add(body);
            return panel;
        }

        /// <summary>A squad button. It never takes the focus (the distress call too: never by a stray key), except in the
        /// invitation and trade popups, which are answered with the keys or a controller (StationMenu walks them).</summary>
        /// <summary>A popup (the squad invitation, a trade request) in the interface rework's panel shape
        /// (docs/UI-DESIGN.md "Shapes"): the panel fill with its cyan left edge, a title with the amber accent under it,
        /// the message as running text, and the two answers as the shared button kinds (the yes primary) carrying the
        /// controller's A / B inside them, as a dialog's do.</summary>
        static VisualElement Popup(StyleSheet sheet, string title, out Label text, string yes, System.Action onYes,
                                   string no, System.Action onNo, out Button yesButton, out Button noButton)
        {
            var popup = new VisualElement();
            popup.AddToClassList("squad-invite");
            if (sheet != null) popup.styleSheets.Add(sheet);
            var heading = new Label(title.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("panel-title");
            heading.AddToClassList("squad-invite-title");
            popup.Add(heading);
            var accent = new VisualElement { pickingMode = PickingMode.Ignore };
            accent.AddToClassList("panel-accent");
            popup.Add(accent);
            text = new Label();
            text.AddToClassList("squad-invite-text");
            popup.Add(text);
            var row = new VisualElement();
            row.AddToClassList("squad-invite-buttons");
            yesButton = PopupButton(yes, onYes, true);
            noButton = PopupButton(no, onNo, false);
            row.Add(yesButton);
            row.Add(noButton);
            popup.Add(row);
            return popup;
        }

        /// <summary>One answer: the rework's secondary button, primary for the yes, focusable (the keys and a controller
        /// answer the popup) and with its controller glyph inside it (A only on the focused one, like a dialog's).</summary>
        static Button PopupButton(string text, System.Action onClick, bool primary)
        {
            var b = new Button(onClick) { text = text.ToUpperInvariant(), focusable = true };
            b.AddToClassList("gof-button");
            if (primary) b.AddToClassList("gof-button--primary");
            InputGlyph.PadPrompt(b, primary ? PadButton.A : PadButton.B, primary);
            return b;
        }

        static Button MakeButton(string text, System.Action onClick, string cls, bool focusable = false)
        {
            var b = new Button(onClick) { text = text.ToUpperInvariant(), focusable = focusable };
            b.AddToClassList("squad-button");
            if (cls != null) b.AddToClassList(cls);
            return b;
        }

        void OnDisable() { if (current == this) current = null; }

        float nextScaleCheck;

        void Update()
        {
            if (box == null) return;
            // A small high-density screen (UiScale): the large variant of the squad window and the invitation.
            if (Time.unscaledTime >= nextScaleCheck && box.panel != null)
            {
                nextScaleCheck = Time.unscaledTime + 1f;
                bool large = UiScale.Large(box);
                // Rework: on a small screen the panels start folded (their headers alone). Open, the squad and the pilots
                // here took most of the right side, over the lounge's visitor list and the chat.
                if (!collapseChecked && box.panel != null)
                {
                    collapseChecked = true;
                    if (large) { squadCollapsed = pilotsCollapsed = true; squadKey = pilotsKey = ""; }
                }
                box.EnableInClassList("squad--large", large);
                invitePopup.EnableInClassList("squad--large", large);
            }
            bool session = NetGame.Active;
            box.style.display = session ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshCodePlate();
            if (!session) { invitePopup.style.display = tradePopup.style.display = DisplayStyle.None; return; }
            if ((refresh -= Time.unscaledDeltaTime) > 0f) return;
            refresh = RefreshSeconds;
            Refresh();
        }

        void Refresh()
        {
            RefreshSquad();
            RefreshPilots();
            RefreshInvite();
            RefreshTrade();
        }

        string Where(NetPlayer p)
        {
            string station = db?.Stations.Find(s => s.index == p.Station)?.name ?? "?";
            return p.InHangar ? string.Format(Localization.Extra("mpDocked", "Docked at {0}"), station)
                 : p.InSpace ? string.Format(Localization.Extra("mpInOrbit", "{0} orbit"), station)
                 : Localization.Extra("mpTravelling", "Travelling");
        }

        void RefreshSquad()
        {
            var members = NetSquad.Members();
            squadPanel.style.display = members.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (members.Count <= 1) { squadKey = ""; return; }
            var sb = new StringBuilder(squadCollapsed ? "c" : "o");
            foreach (var m in members) sb.Append('|').Append(m.OwnerClientId).Append(m.DisplayName).Append(Where(m)).Append(m.Distress);
            sb.Append('|').Append(NetDistress.Active).Append(NetPlayer.Local != null && NetPlayer.Local.InSpace).Append('|').Append(NetFollow.Following);
            string key = sb.ToString();
            if (key != squadKey)
            {
                squadKey = key;
                squadHeader.text = $"{Localization.Extra("mpSquad", "Squad").ToUpperInvariant()} ({members.Count})  {(squadCollapsed ? "+" : "-")}";
                squadBody.Clear();
                bars.Clear();
                squadBody.style.display = squadCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
                foreach (var m in members)
                {
                    var row = new VisualElement();
                    row.AddToClassList("squad-member");
                    var name = new Label((m.Distress ? "⚠ " : "") + m.DisplayName + (m.IsOwner ? $"  ({Localization.Extra("mpYou", "you")})" : ""));
                    name.AddToClassList("squad-name");
                    if (m.Distress) name.style.color = new Color(1f, 0.4f, 0.35f);
                    row.Add(name);
                    // A call for help: the others' Help (the fastest way there), the local player's own call / end.
                    var caller = m;
                    if (!m.IsOwner && m.Distress)
                        row.Add(MakeButton(Localization.Extra("mpHelpButton", "Help"), () => { string msg = NetDistress.Help(caller); if (!string.IsNullOrEmpty(msg)) NetChat.Notice(msg); squadKey = ""; }, "squad-button--accept"));
                    // Follow them from orbit to orbit (NetFollow), or stop.
                    if (!m.IsOwner)
                        row.Add(MakeButton(NetFollow.IsFollowing(m) ? Localization.Extra("mpUnfollowButton", "Stop following") : Localization.Extra("mpFollowButton", "Follow"),
                                           () => { string msg = NetFollow.Toggle(caller); if (!string.IsNullOrEmpty(msg)) NetChat.Notice(msg); squadKey = ""; }, NetFollow.IsFollowing(m) ? null : "squad-button--accept"));
                    if (m.IsOwner && (m.InSpace || NetDistress.Active))
                        row.Add(MakeButton(NetDistress.Active ? Localization.Extra("mpDistressEnd", "End the call") : Localization.Extra("mpDistressCall", "Distress call"),
                                           () => { NetChat.Notice(NetDistress.Toggle()); squadKey = ""; }, NetDistress.Active ? null : "squad-button--leave"));
                    var where = new Label(Where(m));
                    where.AddToClassList("squad-where");
                    row.Add(where);
                    var shieldFill = Bar(row, "squad-bar--shield");
                    var hullFill = Bar(row, "squad-bar--hull");
                    var armorFill = new VisualElement();   // CombatView: the armor fill over the hull in one bar
                    armorFill.AddToClassList("squad-bar-fill");
                    armorFill.AddToClassList("squad-bar-armor");
                    hullFill.parent.Add(armorFill);
                    bars[m.OwnerClientId] = (shieldFill, armorFill, hullFill);
                    squadBody.Add(row);
                }
                squadBody.Add(MakeButton(Localization.Extra("mpLeaveSquad", "Leave squad"), NetSquad.Leave, "squad-button--leave"));
            }
            foreach (var m in members)
                if (bars.TryGetValue(m.OwnerClientId, out var b))
                {
                    SetBar(b.shield, m.Shield);
                    SetBar(b.hull, m.Hull);
                    b.armor.style.display = m.Armor < 0f ? DisplayStyle.None : DisplayStyle.Flex;
                    b.armor.style.width = Length.Percent(Mathf.Clamp01(m.Armor) * 100f);
                }
        }

        /// <summary>A thin bar (shield / armor / hull) under a member's name; returns its fill.</summary>
        static VisualElement Bar(VisualElement row, string cls)
        {
            var bar = new VisualElement();
            bar.AddToClassList("squad-bar");
            bar.AddToClassList(cls);
            var fill = new VisualElement();
            fill.AddToClassList("squad-bar-fill");
            bar.Add(fill);
            row.Add(bar);
            return fill;
        }

        /// <summary>The fraction's width; a pool the ship doesn't have (-1): no bar.</summary>
        static void SetBar(VisualElement fill, float fraction)
        {
            fill.parent.style.display = fraction < 0f ? DisplayStyle.None : DisplayStyle.Flex;
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        }

        void RefreshPilots()
        {
            var me = NetPlayer.Local;
            var pilots = new List<NetPlayer>();
            if (hangar && me != null)
            {
                pilots.Add(me);   // the local player first, marked "(you)"
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && !p.IsOwner && p.InHangar && p.Station == hereStation) pilots.Add(p);
            }
            pilotsPanel.style.display = pilots.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (pilots.Count == 0) { pilotsKey = ""; return; }
            var sb = new StringBuilder(pilotsCollapsed ? "c" : "o");
            foreach (var p in pilots) sb.Append('|').Append(p.OwnerClientId).Append(p.DisplayName).Append(NetSquad.Same(p, me)).Append(NetSquad.WasInvited(p))
                                        .Append(NetTradeClient.WasAsked(p)).Append(TradingWith(p));
            string key = sb.ToString();
            if (key == pilotsKey) return;
            pilotsKey = key;
            pilotsHeader.text = $"{Localization.Extra("mpPilotsHere", "Pilots in this hangar").ToUpperInvariant()} ({pilots.Count})  {(pilotsCollapsed ? "+" : "-")}";
            pilotsBody.Clear();
            pilotsBody.style.display = pilotsCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            int shown = 0;
            foreach (var p in pilots)
            {
                // A busy hangar: the list stops at MaxPilotRows and says how many more are here (the Multiplayer window's
                // Squad tab lists them all).
                if (++shown > MaxPilotRows)
                {
                    var more = new Label(string.Format(Localization.Extra("mpMorePilots", "+{0} more"), pilots.Count - MaxPilotRows));
                    more.AddToClassList("squad-tag");
                    pilotsBody.Add(more);
                    break;
                }
                var row = new VisualElement();
                row.AddToClassList("squad-pilot");
                var name = new Label(p.DisplayName + (p == me ? $"  ({Localization.Extra("mpYou", "you")})" : ""));
                name.AddToClassList("squad-name");
                row.Add(name);
                if (p == me) { }   // no invitation to oneself
                else if (NetSquad.Same(p, me))
                {
                    var tag = new Label(Localization.Extra("mpInYourSquad", "In your squad"));
                    tag.AddToClassList("squad-tag");
                    row.Add(tag);
                }
                else if (NetSquad.WasInvited(p))
                {
                    var tag = new Label(Localization.Extra("mpInvitedTag", "Invited"));
                    tag.AddToClassList("squad-tag");
                    row.Add(tag);
                }
                else
                {
                    var target = p;
                    row.Add(MakeButton(Localization.Extra("mpInvite", "Invite"), () => { NetSquad.InviteTo(target); pilotsKey = ""; }, null));
                }
                if (p != me) AddTrade(row, p);
                pilotsBody.Add(row);
            }
        }

        static bool TradingWith(NetPlayer p) => NetTradeClient.Current != null && p != null && NetTradeClient.Current.partner == p.OwnerClientId;

        /// <summary>A pilot's trade button: Trade, "Asked" while the request waits, "Trading" (opens the trade) during one.</summary>
        void AddTrade(VisualElement row, NetPlayer p)
        {
            if (TradingWith(p))
                row.Add(MakeButton(Localization.Extra("mpTradeTrading", "Trading"), MultiplayerWindow.OpenTradeAny, "squad-button--accept"));
            else if (NetTradeClient.WasAsked(p))
            {
                var tag = new Label(Localization.Extra("mpTradeAsked", "Asked"));
                tag.AddToClassList("squad-tag");
                row.Add(tag);
            }
            else
            {
                var target = p;
                row.Add(MakeButton(Localization.Extra("mpTradeButton", "Trade"), () => { NetTradeClient.Ask(target); pilotsKey = ""; }, null));
            }
        }

        void RefreshTrade()
        {
            var list = NetTradeClient.Requests;
            var latest = list.Count > 0 ? list[list.Count - 1] : null;
            // Docked only, one popup at a time (a squad invitation first), none while a trade is open.
            if (NetPlayer.Local == null || !NetPlayer.Local.InHangar || shownInvite != null || NetTradeClient.Current != null) latest = null;
            shownTrade = latest;
            if (latest != null) tradeText.text = string.Format(Localization.Extra("mpTradeRequested", "{0} wants to trade with you."), latest.name);
            tradePopup.style.display = shownTrade != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RefreshInvite()
        {
            var invites = NetSquad.Invites;
            var latest = invites.Count > 0 ? invites[invites.Count - 1] : null;
            if (NetPlayer.Local == null || !NetPlayer.Local.InHangar) latest = null;   // squads form only in a hangar
            shownInvite = latest;
            if (latest != null)
            {
                string warning = NetMissions.AbandonWarning();
                inviteText.text = string.Format(Localization.Extra("mpSquadInvited", "{0} invites you to their squad."), latest.name)
                                  + (warning.Length > 0 ? "\n" + warning : "");
            }
            invitePopup.style.display = shownInvite != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---- the popups, answered with the keys or a controller --------------------------------------------------

        /// <summary>A squad invitation or a trade request is on screen (only while docked). The station menu then walks its
        /// two buttons (StationMenu.OnNavigate), gives the focus to Accept and answers Esc / B with Decline.</summary>
        public static bool PopupOpen => Buttons() != null;

        /// <summary>Accept and Decline of the popup on screen, null without one.</summary>
        public static VisualElement[] PopupButtons => Buttons();

        static VisualElement[] Buttons()
        {
            var v = current;
            if (v == null || !NetGame.Active) return null;
            if (v.shownInvite != null && v.inviteAccept != null) return new VisualElement[] { v.inviteAccept, v.inviteDecline };
            if (v.shownTrade != null && v.tradeAccept != null) return new VisualElement[] { v.tradeAccept, v.tradeDecline };
            return null;
        }

        /// <summary>Esc / B: the popup's own No, so it is always answered (its action runs, like a station dialog's).</summary>
        public static void DeclinePopup()
        {
            var v = current;
            if (v == null) return;
            if (v.shownInvite != null) { NetSquad.Decline(v.shownInvite); v.shownInvite = null; v.invitePopup.style.display = DisplayStyle.None; }
            else if (v.shownTrade != null) { NetTradeClient.Decline(v.shownTrade); v.shownTrade = null; v.tradePopup.style.display = DisplayStyle.None; }
        }
    }
}
