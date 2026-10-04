// LoungePanel.cs
// The Space Lounge's agents over the 3D bar (SpaceLounge 0x197890; Reference/research/freelance_missions.md 3,
// lounge_ui.md 1): tapping a visitor opens the chat with that agent (LoungeChat): portrait (ImageFactory::createChar
// parts), the agent's text, the answer buttons (green Okay / red No thanks, Let me see it or Show it on the map / What's
// the risk?; a single white Okay for closing lines) and one lounge voice greeting. A deal asks through the station's
// confirmation dialog, failed checks show as a message; "Show it on the map" opens the star map in mission mode (view
// only), bought coordinates open it on the new system when the chat closes. The camera does not move while talking.
// The plates over the visitors show what the original's hover label shows (the race until talked to, then the name and
// role); the remake shows them all the time and adds a visitor list for keyboard / controller.
// Plain class driven by StationMenu (like HangarWindow).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class LoungePanel
    {
        readonly StationMenu menu;
        readonly StationLevel level;
        readonly VisualElement root, tags, list, chatWindow, portrait, choices;
        readonly Label chatName, chatSub, chatText;
        readonly TextReveal chatReveal;   // the animated dialogue option
        readonly ScrollView chatScroll;
        readonly Button chatClose;
        readonly List<VisualElement> tagItems = new List<VisualElement>();
        readonly List<Button> rows = new List<Button>();
        readonly List<Button> choiceButtons = new List<Button>();
        readonly HashSet<int> smallTalkUsed = new HashSet<int>();   // SpaceLounge+0x58, per lounge visit
        LoungeChat chat;
        int selected = -1;
        bool built;

        static string T(int id) => Localization.Get(id);

        public LoungePanel(StationMenu menu, StationLevel level, VisualElement root)
        {
            if (level != null) level.VisitorPicked = i => { menu.PlayRelease(); OpenChat(i); };   // VR: the laser on a visitor
            this.menu = menu;
            this.level = level;
            this.root = root;
            tags = root.Q("loungeTags");
            list = root.Q("loungeList");
            chatWindow = root.Q("chatWindow");
            portrait = root.Q("chatPortrait");
            choices = root.Q("chatChoices");
            chatName = root.Q<Label>("chatName");
            chatSub = root.Q<Label>("chatSub");
            chatText = root.Q<Label>("chatText");
            chatReveal = new TextReveal(chatText);
            chatScroll = root.Q<ScrollView>("chatScroll");
            chatScroll.RegisterCallback<PointerDownEvent>(_ => chatReveal?.Finish());   // a tap shows the whole line
            chatScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            chatScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            chatClose = root.Q<Button>("chatClose");
            chatClose.text = Localization.Extra("hudBack", "BACK");
            chatClose.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            chatClose.clicked += () => { menu.PlayRelease(); CloseChat(); };
        }

        Database Db => level.Database;
        int Station => level.Station != null ? level.Station.index : Session.StationIndex;

        /// <summary>The bar is shown with its intro over: tags and list are up.</summary>
        public bool Active => level != null && level.View == StationView.Lounge && !level.IntroPlaying;
        public bool ChatOpen => chat != null;

        // ---- visitors ---------------------------------------------------------------------------------------

        void Build()
        {
            built = true;
            tags.Clear(); list.Clear(); tagItems.Clear(); rows.Clear();
            for (int i = 0; i < level.VisitorAgentCount; i++)
            {
                int index = i;
                var a = level.VisitorAgent(i);

                var tag = new VisualElement();
                tag.AddToClassList("lounge-tag");
                var name = new Label { pickingMode = PickingMode.Ignore };
                name.AddToClassList("lounge-tag-name");
                name.AddToClassList("gof-semibold");
                tag.Add(name);
                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("lounge-tag-mark");
                tag.Add(mark);
                tag.RegisterCallback<PointerDownEvent>(e => { menu.PlayPush(); e.StopPropagation(); });
                tag.RegisterCallback<PointerUpEvent>(e => { menu.PlayRelease(); OpenChat(index); e.StopPropagation(); });
                tags.Add(tag);
                tagItems.Add(tag);

                var row = new Button();
                row.AddToClassList("lounge-row");
                var titles = new VisualElement { pickingMode = PickingMode.Ignore };
                titles.AddToClassList("lounge-row-titles");
                var rn = new Label { pickingMode = PickingMode.Ignore };
                rn.AddToClassList("lounge-row-name");
                rn.AddToClassList("gof-semibold");
                titles.Add(rn);
                var rs = new Label { pickingMode = PickingMode.Ignore };
                rs.AddToClassList("lounge-row-sub");
                titles.Add(rs);
                row.Add(titles);
                row.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
                row.clicked += () => { menu.PlayRelease(); OpenChat(index); };
                row.RegisterCallback<FocusInEvent>(_ => selected = index);
                list.Add(row);
                rows.Add(row);
            }
            RefreshMarks();
        }

        /// <summary>The plates (race until talked to, then name + role) and a mark on agents not heard yet (remake).</summary>
        void RefreshMarks()
        {
            for (int i = 0; i < tagItems.Count; i++)
            {
                var a = level.VisitorAgent(i);
                var (name, role) = LoungeChat.Plate(a);
                tagItems[i].Q<Label>(className: "lounge-tag-name").text = role.Length > 0 ? $"{name}  <color=#FFA630>{role}</color>" : name;
                var mark = tagItems[i].Q(className: "lounge-tag-mark");
                mark.style.display = a.accepted ? DisplayStyle.None : DisplayStyle.Flex;
                mark.EnableInClassList("lounge-tag-mark--known", a.known);
                if (i < rows.Count)
                {
                    rows[i].Q<Label>(className: "lounge-row-name").text = name;
                    rows[i].Q<Label>(className: "lounge-row-sub").text = role.Length > 0 ? role : a.known || a.IsStory ? T(406 + a.race) : "";
                }
            }
        }

        public void OnViewChanged()
        {
            bool on = Active;
            root.EnableInClassList("lounge-open", on);
            if (!on) { if (ChatOpen) CloseChat(false); smallTalkUsed.Clear(); return; }
            if (!built) Build();
        }

        /// <summary>Per frame: the tags follow the visitors' heads on screen.</summary>
        public void Update()
        {
            chatReveal.Tick(Time.unscaledDeltaTime * 1000f);
            bool on = Active;
            if (on != root.ClassListContains("lounge-open")) OnViewChanged();
            if (built && tagItems.Count != level.VisitorAgentCount) Build();   // remake multiplayer: event missions' visitors joined
            if (!on || tags.panel == null) return;
            var cam = level.MainCamera;
            if (cam == null) return;
            for (int i = 0; i < tagItems.Count; i++)
            {
                var head = level.VisitorHead(i);
                bool front = Vector3.Dot(head - cam.transform.position, cam.transform.forward) > 0f;
                tagItems[i].style.display = front ? DisplayStyle.Flex : DisplayStyle.None;
                if (!front) continue;
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(tags.panel, head, cam);
                tagItems[i].style.left = p.x;
                tagItems[i].style.top = p.y;
                tagItems[i].EnableInClassList("lounge-tag--selected", i == selected);
            }
        }

        // ---- chat -------------------------------------------------------------------------------------------

        public void OpenChat(int index)
        {
            if (index < 0 || index >= level.VisitorAgentCount) return;
            selected = index;
            var a = level.VisitorAgent(index);
            chat = new LoungeChat(Db, a, Station, smallTalkUsed);
            chat.Start();
            menu.PlayVoice(StoryAssets.Load()?.Voice(chat.VoiceName()));   // SpaceLounge::getSoundId
            root.AddToClassList("chat-open");
            var (plateName, plateRole) = LoungeChat.Plate(a);
            chatName.text = plateName.ToUpperInvariant();
            chatSub.text = plateRole.Length > 0 ? plateRole : T(406 + a.race);
            var eventOffer = GoF2Remake.Multiplayer.NetEventMissions.OfferOf(a);
            if (eventOffer != null && eventOffer.face == null && eventOffer.speakerId >= 0)
                Portrait.ShowSpeaker(portrait, eventOffer.speakerId, eventOffer.speakerId == 0);   // a story character as the client
            else Portrait.Show(portrait, a.portrait, false);
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            ShowChat();
        }

        public void CloseChat(bool focusRow = true)
        {
            if (chat == null) return;
            chat = null;
            menu.PlayVoice(null);
            root.RemoveFromClassList("chat-open");
            RefreshMarks();
            if (focusRow && selected >= 0 && selected < rows.Count) menu.Focus(rows[selected]);
        }

        void ShowChat()
        {
            chatText.text = chat.Text;
            chatScroll.scrollOffset = Vector2.zero;
            chatReveal.Begin(chat.Text, false, speaker: chat.Agent?.name);
            choices.Clear();
            choiceButtons.Clear();
            foreach (var c in chat.Choices)
            {
                var choice = c;
                var b = new Button { text = LoungeChat.ChoiceLabel(chat.Agent, c) };
                b.AddToClassList("station-button");
                b.AddToClassList("chat-choice");
                b.AddToClassList("gof-semibold");
                // TouchButton colours: Okay green, No thanks red, a lone Okay white.
                if (!chat.SingleOkay && c == LoungeChat.Choice.Okay) b.AddToClassList("chat-choice--ok");
                if (c == LoungeChat.Choice.NoThanks) b.AddToClassList("chat-choice--no");
                b.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
                b.clicked += () => { menu.PlayRelease(); Choose(choice); };
                choices.Add(b);
                choiceButtons.Add(b);
            }
            choices.style.display = choiceButtons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            menu.Focus(choiceButtons.Count > 0 ? choiceButtons[0] : chatClose);
        }

        void Choose(LoungeChat.Choice c)
        {
            if (chat == null) return;
            var outcome = chat.Choose(c);
            switch (outcome)
            {
                case LoungeChat.Outcome.Confirm:
                {
                    var current = chat;
                    menu.ShowDialog(current.ConfirmText, () => { current.Confirm(); if (current.BoughtShip) level.RefreshParkedShips(); AfterDeal(current); });
                    return;
                }
                case LoungeChat.Outcome.ConfirmShip:
                {
                    // Remake (AgentOffer.SellShip): the dealer's flow, HangarWindow ShopShip: 873, then with the Kaamo Club
                    // owned 327 "sell your old ship or keep it" (330 / 331), the turntable's ship swapped, toast 303.
                    var current = chat;
                    void Trade(bool keep)
                    {
                        string refusal = current.ConfirmShipTrade(keep);
                        if (refusal != null) { menu.ShowDialog(refusal, null, true); return; }
                        level.ReplacePlayerShip(Session.ShipIndex);
                        if (keep) level.RefreshParkedShips();
                        menu.ShowToast(Localization.Get(303).Replace("#N", ItemInfo.ShipName(Session.ShipIndex)));
                        AfterDeal(current);
                    }
                    menu.ShowDialog(current.ConfirmText, () =>
                    {
                        if (KaamoClub.Owned)
                            menu.ShowChoice(Localization.Get(327), Localization.Get(330), Localization.Get(331), () => Trade(false), () => Trade(true));
                        else Trade(false);
                    });
                    return;
                }
                case LoungeChat.Outcome.ShowMap:
                    if (chat.MapTarget >= 0) OpenMissionMap(chat.MapTarget);
                    else ShowGoods();
                    return;
                case LoungeChat.Outcome.Refused:
                    menu.ShowDialog(chat.RefusalText, null, true);   // the ChoiceWindow as a message
                    return;
                case LoungeChat.Outcome.Closed:
                {
                    int reveal = chat.RevealedSystem;
                    CloseChat();
                    if (reveal >= 0)
                    {
                        // Bought coordinates: the star map opens on the newly visible system (StarMap(false, 0, true, sys)).
                        var sys = Db.Systems.Find(s => s.index == reveal);
                        if (sys != null && sys.stations.Count > 0) OpenMissionMap(sys.stations[0], reveal);
                    }
                    return;
                }
            }
            int focused = choiceButtons.FindIndex(b => b == root.focusController?.focusedElement);
            ShowChat();
            if (focused >= 0 && focused < choiceButtons.Count) menu.Focus(choiceButtons[focused]);
        }

        void AfterDeal(LoungeChat done)
        {
            if (chat != done) return;
            ShowChat();
            menu.RefreshCredits();
            RefreshMarks();
        }

        /// <summary>"Let me see it" (offers 2, 3, 8, 9, 10): the goods' name, category and description.</summary>
        void ShowGoods()
        {
            var a = chat.Agent;
            // "Let me see it" (776): ListItemWindow::set(..., showPrice = false), the full-screen details.
            if ((a.offer == AgentOffer.ShipDealer || a.offer == AgentOffer.SellShip) && a.sellShip >= 0) { menu.InfoWindow?.ShowShip(Db, a.sellShip, 0, false); return; }
            int item = a.offer == AgentOffer.SellBlueprint ? LoungeChat.BlueprintProduct(a.sellBlueprint) : a.sellItem;
            var it = item >= 0 ? Db.Item(item) : null;
            if (it == null) { chatText.text = chat.Text; return; }
            menu.InfoWindow?.ShowItem(Db, item, level.Station != null ? level.Station.system : -1, false, 0);
        }

        /// <summary>StarMap(true, mission, ...): view only, centred on the target; with 'reveal' the coordinates' reveal
        /// animation of that system (StarMap(false, 0, true, sys)).</summary>
        void OpenMissionMap(int station, int reveal = -1)
        {
            root.AddToClassList("station-map-open");
            var map = StarMap.Open(Db, StarMapMode.Mission, false, _ =>
            {
                root.RemoveFromClassList("station-map-open");
                if (chat != null && choiceButtons.Count > 0) menu.Focus(choiceButtons[0]);
            }, -1, reveal >= 0 ? -1 : station, false, -1, reveal);
            if (map == null) root.RemoveFromClassList("station-map-open");
        }

        // ---- navigation -------------------------------------------------------------------------------------

        /// <summary>Focusable items for up / down: the choices (+ Back) in a chat, else the visitor rows.</summary>
        public VisualElement[] NavItems()
        {
            if (ChatOpen)
            {
                var l = new List<VisualElement>(choiceButtons);
                if (chatClose.resolvedStyle.display == DisplayStyle.Flex) l.Add(chatClose);
                return l.Count > 0 ? l.ToArray() : new VisualElement[] { chatClose };
            }
            return rows.ToArray();
        }

        public void FocusFirst()
        {
            if (ChatOpen) menu.Focus(choiceButtons.Count > 0 ? choiceButtons[0] : chatClose);
            else if (rows.Count > 0) menu.Focus(rows[Mathf.Clamp(selected, 0, rows.Count - 1)]);
        }
    }
}
