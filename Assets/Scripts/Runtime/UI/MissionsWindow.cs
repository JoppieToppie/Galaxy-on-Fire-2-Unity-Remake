// MissionsWindow.cs
// The Missions window (MissionsWindow::init 0x17a604 / draw 0x17b240 / OnTouchEnd 0x17bbc0; Reference/research/
// lounge_ui.md 5, freelance_missions.md 3.4): title 129 "Missions", two panels side by side:
//   555 "Story"      the campaign objective text (story.json objectiveText, # = the target station), 424 "Show on map"
//   556 "Freelance"  the client's portrait, name, station and mission type (354 + type), the offer as the agent said it
//                    (#C = reward + the current standing bonus), 424 "Show on map" and, only in a station, 423 "Discard"
//                    (red) -> 418 "Are you sure?"; no mission: 174 "-BLANK-"
// Show on map opens the star map in mission mode (StarMap(true, mission)). Locked before campaign 9 like the Map.
// The tab 3219 "Most Wanted" (WantedWindow 0xf4a58, wingmen_wanted.md 2.5) when the station has a board
// (WantedBoard.Accessible): the board's criminals (active ones bright, the storyline one gold), and for the selected one
// the portrait, name, "3226 Status: 3228 Alive / 3227 Deceased", "3225 Bounty: N$" and "3223 Departed from" /
// "3224 Travelling to" (3229 N/A while inactive, " --" once dead) with its description (3174 + index); 424 Show on map
// for an active one (the star map centred on where it is travelling to).
// Plain class driven by StationMenu.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class MissionsWindow
    {
        readonly StationMenu menu;
        readonly StationLevel level;
        readonly VisualElement root, portrait;
        readonly Button close, storyMap, freelanceMap, discard;
        readonly Label storyText, freelanceText, freelanceClient, freelanceWhere;
        readonly ScrollView storyScroll, freelanceScroll;
        readonly Button missionsTab, wantedTab, wantedMap;
        readonly VisualElement missionsBody, wantedBody, wantedPortrait;
        readonly ScrollView wantedList, wantedScroll;
        readonly Label wantedName, wantedStatus, wantedBounty, wantedText;
        readonly List<Button> wantedRows = new List<Button>();
        bool wantedShown;
        int wantedSelected = -1;

        static string T(int id) => Localization.Get(id);

        public MissionsWindow(StationMenu menu, StationLevel level, VisualElement root)
        {
            this.menu = menu;
            this.level = level;
            this.root = root;
            portrait = root.Q("missionsPortrait");
            storyText = root.Q<Label>("storyText");
            freelanceText = root.Q<Label>("freelanceText");
            freelanceClient = root.Q<Label>("freelanceClient");
            freelanceWhere = root.Q<Label>("freelanceWhere");
            storyScroll = Scroll("storyScroll");
            freelanceScroll = Scroll("freelanceScroll");
            close = Bind("missionsClose", Close);
            storyMap = Bind("storyMap", () => ShowOnMap(Story.MapTarget, storyMap));
            freelanceMap = Bind("freelanceMap", () => ShowOnMap(Freelance.Mission.target, freelanceMap));
            discard = Bind("freelanceDiscard", AskDiscard);
            root.Q<Label>("missionsTitle").text = T(129).ToUpperInvariant();
            root.Q<Label>("storyHeading").text = T(555).ToUpperInvariant();
            root.Q<Label>("freelanceHeading").text = T(556).ToUpperInvariant();
            close.text = Localization.Extra("hudBack", "BACK");
            storyMap.text = freelanceMap.text = T(424).ToUpperInvariant();
            discard.text = T(423).ToUpperInvariant();

            missionsBody = root.Q("missionsBody");
            wantedBody = root.Q("wantedBody");
            wantedPortrait = root.Q("wantedPortrait");
            wantedName = root.Q<Label>("wantedName");
            wantedStatus = root.Q<Label>("wantedStatus");
            wantedBounty = root.Q<Label>("wantedBounty");
            wantedText = root.Q<Label>("wantedText");
            wantedList = Scroll("wantedList");
            wantedScroll = Scroll("wantedScroll");
            missionsTab = Bind("missionsTab", () => ShowTab(false));
            wantedTab = Bind("wantedTab", () => ShowTab(true));
            wantedMap = Bind("wantedMap", ShowWantedOnMap);
            missionsTab.text = T(129).ToUpperInvariant();
            wantedTab.text = T(3219).ToUpperInvariant();
            wantedMap.text = T(424).ToUpperInvariant();
            root.Q<Label>("wantedListHeading").text = T(3221);
            root.Q<Label>("wantedDetailsHeading").text = T(3222);
        }

        ScrollView Scroll(string name)
        {
            var s = root.Q<ScrollView>(name);
            s.verticalScrollerVisibility = ScrollerVisibility.Auto;
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            return s;
        }

        Button Bind(string name, System.Action action)
        {
            var b = root.Q<Button>(name);
            b.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
            b.clicked += () => { menu.PlayRelease(); action(); };
            return b;
        }

        public bool IsOpen => root.ClassListContains("missions-open");

        public void Open()
        {
            root.AddToClassList("missions-open");
            wantedShown = false;
            Fill();
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.RemoveFromClassList("missions-open");
            menu.OnMissionsClosed();
        }

        bool BoardHere => level.Station != null && WantedBoard.Accessible(level.Database, level.Station.index);

        void ShowTab(bool wanted)
        {
            wantedShown = wanted && BoardHere;
            Fill();
        }

        void Fill()
        {
            var db = level.Database;
            bool board = BoardHere;
            missionsTab.EnableInClassList("missions-tab--hidden", !board);
            wantedTab.EnableInClassList("missions-tab--hidden", !board);
            missionsTab.EnableInClassList("missions-tab--active", !wantedShown);
            wantedTab.EnableInClassList("missions-tab--active", wantedShown);
            root.Q<Label>("missionsTitle").text = (wantedShown ? T(3219) : T(129)).ToUpperInvariant();
            missionsBody.EnableInClassList("missions-body--hidden", wantedShown);
            wantedBody.EnableInClassList("missions-body--hidden", !wantedShown);
            if (wantedShown) { FillWanted(); return; }
            // Story: the objective text of every step (MissionsWindow::init 0x17a604: text DAT_00258f68[index] below 0xa4,
            // hidden and empty missions too: step 13's "find work in the Space Lounge" before the convoy); the map button only
            // for a mission with a target to show.
            bool story = !Session.FreePlay && Story.Step != null && Story.Step.objectiveText >= 0;
            storyText.text = story ? Story.ObjectiveText(db) : T(174);
            // Remake multiplayer (no story there): the event graph mission the squad is on (NetEventMissions).
            var eventMission = GoF2Remake.Multiplayer.NetEventMissions.Active;
            if (!story && eventMission != null)
                storyText.text = eventMission.title.ToUpperInvariant() + "\n" + string.Format(Localization.Extra("mpMissionCardBy", "Accepted by {0}"), eventMission.by)
                                 + "\n\n" + GoF2Remake.Multiplayer.NetEventMissions.ActiveText;
            Show(storyMap, story && !Session.StoryMission.IsEmpty && Session.StoryMission.visible && Story.MapTarget >= 0);

            var m = Freelance.Mission;
            bool active = Freelance.Active;
            portrait.EnableInClassList("portrait-hidden", !active);
            if (active) Portrait.Show(portrait, m.clientPortrait, false);
            freelanceClient.text = active ? m.clientName.ToUpperInvariant() : "";
            string station = active ? db.Stations.Find(s => s.index == m.clientStation)?.name ?? "" : "";
            freelanceWhere.text = active ? $"{station}\n{m.Name}" : "";
            freelanceText.text = active ? FreelanceText(db, m) : T(174);
            Show(freelanceMap, active);
            Show(discard, active);
            storyScroll.scrollOffset = freelanceScroll.scrollOffset = Vector2.zero;
            menu.Focus(active ? freelanceMap : story ? storyMap : close);
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Globals::getAgentMissionText: the offer text rebuilt from the stored ids with the current values.</summary>
        public static string FreelanceText(Database db, FreelanceMission m)
        {
            var agent = new Agent
            {
                name = m.clientName, race = m.clientRace, male = m.clientMale, mission = m,
                offer = m.type == MissionType.Purchase ? AgentOffer.Purchase : AgentOffer.Mission,
            };
            var chat = new LoungeChat(db, agent, Session.StationIndex, null);
            var ids = m.textIds != null && m.textIds.Count >= 6 ? m.textIds : new List<int> { -1, -1, -1, -1, -1, -1 };
            // Only the offer itself (no greeting or question) belongs in the window.
            return chat.Compose(agent, new List<int> { -1, -1, -1, ids[3], ids[4], -1 });
        }

        void ShowOnMap(int target, Button from, int routeFromSystem = -1)
        {
            if (target < 0) return;
            root.AddToClassList("station-map-open");
            var map = StarMap.Open(level.Database, StarMapMode.Mission, false, _ => { root.RemoveFromClassList("station-map-open"); menu.Focus(from); },
                                   -1, target, false, routeFromSystem);
            if (map == null) root.RemoveFromClassList("station-map-open");
        }

        /// <summary>423 Discard -> 418 "Are you sure?" -> the same clean-up as a discarded mission.</summary>
        void AskDiscard()
        {
            string warning = GoF2Remake.Multiplayer.NetMissions.DiscardWarning();   // multiplayer: ends it for the squad
            menu.ShowDialog(T(418) + warning, () => { GoF2Remake.Multiplayer.NetMissions.Abandon(); Freelance.Discard(); Fill(); menu.RefreshCredits(); });
        }

        public VisualElement[] NavItems()
        {
            var l = new List<VisualElement>();
            if (!missionsTab.ClassListContains("missions-tab--hidden")) { l.Add(missionsTab); l.Add(wantedTab); }
            if (wantedShown)
            {
                l.AddRange(wantedRows);
                if (wantedMap.style.display != DisplayStyle.None) l.Add(wantedMap);
            }
            else
                foreach (var b in new[] { storyMap, freelanceMap, discard })
                    if (b.style.display != DisplayStyle.None) l.Add(b);
            l.Add(close);
            return l.ToArray();
        }

        // ---- the Most Wanted board ---------------------------------------------------------------------------

        void FillWanted()
        {
            var db = level.Database;
            wantedList.Clear();
            wantedRows.Clear();
            var list = WantedBoard.ListFor(db, level.Station.index);
            int story = WantedBoard.StorylineRow;
            // WantedWindow::init: the first active entry is selected.
            if (wantedSelected < 0 || !list.Exists(w => w.index == wantedSelected))
                wantedSelected = (list.Find(w => WantedBoard.State(db, w.index)?.active == true) ?? (list.Count > 0 ? list[0] : null))?.index ?? -1;
            foreach (var w in list)
            {
                var st = WantedBoard.State(db, w.index);
                int index = w.index;
                var b = new Button { text = w.name };
                b.AddToClassList("wanted-row");
                b.AddToClassList("gof-semibold");
                b.EnableInClassList("wanted-row--active", st != null && st.active);
                b.EnableInClassList("wanted-row--dead", st != null && st.terminated);
                b.EnableInClassList("wanted-row--story", index == story);
                b.EnableInClassList("wanted-row--selected", index == wantedSelected);
                b.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
                b.clicked += () => { menu.PlayRelease(); wantedSelected = index; FillWanted(); };
                b.RegisterCallback<FocusInEvent>(_ => { if (wantedSelected != index) { wantedSelected = index; ShowWantedDetails(); foreach (var r in wantedRows) r.EnableInClassList("wanted-row--selected", r == b); } });
                wantedList.Add(b);
                wantedRows.Add(b);
            }
            ShowWantedDetails();
            menu.Focus(wantedRows.Count > 0 ? wantedRows[Mathf.Max(0, list.FindIndex(w => w.index == wantedSelected))] : close);
        }

        void ShowWantedDetails()
        {
            var db = level.Database;
            var w = wantedSelected >= 0 && wantedSelected < db.Wanted.Count ? db.Wanted[wantedSelected] : null;
            var st = w != null ? WantedBoard.State(db, w.index) : null;
            wantedPortrait.EnableInClassList("portrait-hidden", w == null);
            if (w == null)
            {
                wantedName.text = wantedStatus.text = wantedBounty.text = "";
                wantedText.text = T(174);
                Show(wantedMap, false);
                return;
            }
            Portrait.Show(wantedPortrait, w.portraitParts, false);
            wantedName.text = w.name.ToUpperInvariant();
            bool dead = st != null && st.terminated;
            wantedStatus.text = $"{T(3226)} {(dead ? T(3227) : T(3228))}";
            wantedBounty.text = $"{T(3225)} {ItemInfo.Credits(w.reward)}";
            string Place(int station)
            {
                var s = db.Stations.Find(x => x.index == station);
                return s == null ? T(3229) : $"{s.name} ({s.systemName})";
            }
            string from = dead ? " --" : st != null && st.active ? Place(st.lastSeen) : T(3229);
            string to = dead ? " --" : st != null && st.active ? Place(st.travelsTo) : T(3229);
            wantedText.text = $"{T(3223)}\n{from}\n\n{T(3224)}\n{to}\n\n{T(3174 + w.index)}";
            wantedScroll.scrollOffset = Vector2.zero;
            Show(wantedMap, st != null && st.active && !dead && st.travelsTo >= 0);
        }

        /// <summary>WantedWindow::OnTouchEnd 424: the star map on the criminal's destination (Mission(0, 0, travelsTo)).</summary>
        void ShowWantedOnMap()
        {
            var st = WantedBoard.State(level.Database, wantedSelected);
            if (st == null || st.travelsTo < 0) return;
            // WantedWindow: a dummy Mission(0, 0, travelsTo) and setStart(system of lastSeen): the route of the criminal's trip.
            int from = st.lastSeen >= 0 ? level.Database.Stations.Find(s => s.index == st.lastSeen)?.system ?? -1 : -1;
            ShowOnMap(st.travelsTo, wantedMap, from);
        }
    }
}
