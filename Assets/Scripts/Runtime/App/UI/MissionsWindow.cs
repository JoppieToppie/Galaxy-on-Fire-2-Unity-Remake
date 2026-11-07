// MissionsWindow.cs
// The Missions window (MissionsWindow::init 0x17a604 / draw 0x17b240 / OnTouchEnd 0x17bbc0; Reference/research/
// lounge_ui.md 5, freelance_missions.md 3.4): title 129 "Missions", two panels side by side:
//   555 "Story"      the campaign objective text (story.json objectiveText, # = the target station), 424 "Show on map"
//   556 "Freelance"  the client's portrait, name, station and mission type (354 + type), the offer as the agent said it
//                    (#C = reward + the current standing bonus), 424 "Show on map" and, only in a station, 423 "Discard"
//                    (red) -> 418 "Are you sure?"; no mission: 174 "-BLANK-" (rework: a line saying what to do)
// Show on map opens the star map in mission mode (StarMap(true, mission)). Locked before campaign 9 like the Map.
// The tab 3219 "Most Wanted" (WantedWindow 0xf4a58, wingmen_wanted.md 2.5) when the station has a board
// (WantedBoard.Accessible): the board's criminals (active ones bright, the storyline one gold), and for the selected one
// the portrait, name, "3226 Status: 3228 Alive / 3227 Deceased", "3225 Bounty: N$" and "3223 Departed from" /
// "3224 Travelling to" (3229 N/A while inactive, " --" once dead) with its description (3174 + index); 424 Show on map
// for an active one (the star map centred on where it is travelling to).
// Remake (players' suggestion "Better Pirates"): the tab "Pirate activity" (PirateReports) while the pirate events are on:
// the reported outposts and named bosses (the dangerous ones gold, the ones dealt with dim), for the selected one the
// Pirate Boss face, its name, its status and bounty, the system it was seen in (never the station), its danger and how
// long ago it was reported; Show on map routes to that system. It shares the Most Wanted tab's list and details panel.
// Remake: the event graph quests under way (EventRunner.Quests: title and objective) follow the story's objective in the
// Story panel, Show on map for the story's target, else the first quest's; a single-player event graph bar mission
// (EventMissions.Active) fills the Freelance panel when there is no freelance mission, its Discard ends it.
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
        readonly Button missionsTab, wantedTab, pirateTab, wantedMap;
        readonly VisualElement missionsBody, wantedBody, wantedPortrait;
        readonly ScrollView wantedList, wantedScroll;
        readonly Label wantedName, wantedStatus, wantedBounty, wantedText;
        readonly List<Button> wantedRows = new List<Button>();
        enum Tab { Missions, Wanted, Pirates }

        const string PirateHelpEnglish = "Pirate activity\n\nThe authorities report pirate outposts and pirate bosses sighted in the systems. The board names the system but not the station: you have to search its orbits. Each report shows its threat level, the bounty and how long it stays on the board. Destroy the outpost or the boss to collect the bounty.";
        Tab tab;
        bool wantedShown => tab != Tab.Missions;   // the list-and-details panel (the Most Wanted's or the pirates')
        int wantedSelected = -1, pirateSelected = -1;

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
            // Modified: a "?" beside Back with the help of the tab shown (OptionInfo): Missions 635, Most Wanted 601, Pirate
            // activity (new: pirateHelp).
            var help = OptionInfo.Icon(() => tab == Tab.Pirates ? Localization.Extra("pirateHelp", PirateHelpEnglish)
                                            : tab == Tab.Wanted ? Localization.Get(601) : Localization.Get(635));
            help.AddToClassList("window-help");
            help.style.position = Position.Relative;
            help.style.width = help.style.height = 34f;
            close.parent.Insert(close.parent.IndexOf(close), help);
            // Rework: the window's keys (select, switch tab with Q / E or LB / RB, confirm, back) as a prompt row in the strip
            // under the window, like the hangar's hint row (docs/UI-DESIGN.md "Button prompts"; it was an "i").
            var prompts = MenuKeys.Bar(MenuKeys.Rows.Select | MenuKeys.Rows.Tabs | MenuKeys.Rows.Confirm | MenuKeys.Rows.Back);
            prompts.AddToClassList("prompt-bar--below");
            close.parent.parent.Add(prompts);
            storyMap = Bind("storyMap", () => ShowOnMap(storyMapTarget, storyMap));
            freelanceMap = Bind("freelanceMap", () => ShowOnMap(Freelance.Active ? Freelance.MapStation : GoF2Remake.Events.EventRunner.LocalMissionTarget, freelanceMap));
            discard = Bind("freelanceDiscard", AskDiscard);
            root.Q<Label>("missionsTitle").text = T(129).UiCaps();
            root.Q<Label>("storyHeading").text = T(555).UiCaps();
            root.Q<Label>("freelanceHeading").text = T(556).UiCaps();
            close.text = Localization.Extra("hudBack", "BACK").OgCase();
            storyMap.text = freelanceMap.text = T(424).UiCaps();
            discard.text = T(423).UiCaps();

            missionsBody = root.Q("missionsBody");
            wantedBody = root.Q("wantedBody");
            wantedPortrait = root.Q("wantedPortrait");
            wantedName = root.Q<Label>("wantedName");
            wantedStatus = root.Q<Label>("wantedStatus");
            wantedBounty = root.Q<Label>("wantedBounty");
            wantedText = root.Q<Label>("wantedText");
            wantedList = Scroll("wantedList");
            wantedScroll = Scroll("wantedScroll");
            missionsTab = Bind("missionsTab", () => ShowTab(Tab.Missions));
            wantedTab = Bind("wantedTab", () => ShowTab(Tab.Wanted));
            pirateTab = Bind("pirateTab", () => ShowTab(Tab.Pirates));
            wantedMap = Bind("wantedMap", () => { if (tab == Tab.Pirates) ShowPirateOnMap(); else ShowWantedOnMap(); });
            missionsTab.text = T(129).UiCaps();
            wantedTab.text = T(3219).UiCaps();
            pirateTab.text = Localization.Extra("pirateBoardTab", "Pirate activity").UiCaps();
            wantedMap.text = T(424).UiCaps();
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

        /// <summary>Modified: the labels set while this window was built, read again from the table loaded now
        /// (StationMenu.RefreshTexts on Localization.Changed). What Open / Fill writes already reads it as it goes.</summary>
        public void RefreshTexts()
        {
            root.Q<Label>("missionsTitle").text = T(129).UiCaps();
            root.Q<Label>("storyHeading").text = T(555).UiCaps();
            root.Q<Label>("freelanceHeading").text = T(556).UiCaps();
            close.text = Localization.Extra("hudBack", "BACK").OgCase();
            storyMap.text = freelanceMap.text = T(424).UiCaps();
            discard.text = T(423).UiCaps();
            missionsTab.text = T(129).UiCaps();
            wantedTab.text = T(3219).UiCaps();
            pirateTab.text = Localization.Extra("pirateBoardTab", "Pirate activity").UiCaps();
            wantedMap.text = T(424).UiCaps();
            if (IsOpen) Fill();
        }

        public void Open()
        {
            root.AddToClassList("missions-open");
            tab = Tab.Missions;
            Fill();
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.RemoveFromClassList("missions-open");
            menu.OnMissionsClosed();
        }

        bool BoardHere => level.Station != null && WantedBoard.Accessible(level.Database, level.Station.index);

        /// <summary>The Pirate activity tab: the pirate events on (a session's rule) and they can happen for this pilot yet
        /// (rank 2, campaign 0x10), or a session's board.</summary>
        static bool PiratesHere => PirateReports.Enabled
            && (GoF2Remake.Multiplayer.NetGame.Active || (Session.Rank >= 2 && (Session.FreePlay || Session.CampaignMission >= 0x10)));

        /// <summary>Modified: Q / E, LB / RB: the next / previous tab shown (Missions, Pirate activity, Most Wanted).</summary>
        public void StepTab(int dir)
        {
            var order = new List<Tab> { Tab.Missions };
            if (PiratesHere) order.Add(Tab.Pirates);
            if (BoardHere) order.Add(Tab.Wanted);
            if (order.Count < 2) return;
            int i = Mathf.Max(0, order.IndexOf(tab));
            ShowTab(order[((i + dir) % order.Count + order.Count) % order.Count]);
            menu.PlayRelease();
        }

        void ShowTab(Tab t)
        {
            tab = t == Tab.Wanted && !BoardHere || t == Tab.Pirates && !PiratesHere ? Tab.Missions : t;
            Fill();
        }

        void Fill()
        {
            var db = level.Database;
            bool board = BoardHere, pirates = PiratesHere;
            if (tab == Tab.Wanted && !board || tab == Tab.Pirates && !pirates) tab = Tab.Missions;
            // Modified: the Missions tab always shows (the window's title is gone: the tabs name it, as in the hangar).
            missionsTab.EnableInClassList("missions-tab--hidden", false);
            wantedTab.EnableInClassList("missions-tab--hidden", !board);
            pirateTab.EnableInClassList("missions-tab--hidden", !pirates);
            missionsTab.EnableInClassList("missions-tab--active", tab == Tab.Missions);
            wantedTab.EnableInClassList("missions-tab--active", tab == Tab.Wanted);
            pirateTab.EnableInClassList("missions-tab--active", tab == Tab.Pirates);
            root.Q<Label>("missionsTitle").text = (tab == Tab.Wanted ? T(3219) : tab == Tab.Pirates
                ? Localization.Extra("pirateBoardTab", "Pirate activity") : T(129)).UiCaps();
            missionsBody.EnableInClassList("missions-body--hidden", wantedShown);
            wantedBody.EnableInClassList("missions-body--hidden", !wantedShown);
            root.Q<Label>("wantedListHeading").text = tab == Tab.Pirates ? Localization.Extra("pirateListHeading", "Reported sightings") : T(3221);
            root.Q<Label>("wantedDetailsHeading").text = tab == Tab.Pirates ? Localization.Extra("pirateDetailsHeading", "Report") : T(3222);
            if (tab == Tab.Pirates) { FillPirates(); return; }
            if (wantedShown) { FillWanted(); return; }
            // Story: the objective text of every step (MissionsWindow::init 0x17a604: text DAT_00258f68[index] below 0xa4,
            // hidden and empty missions too: step 13's "find work in the Space Lounge" before the convoy); the map button only
            // for a mission with a target to show.
            bool story = !Session.FreePlay && Story.Step != null && Story.Step.objectiveText >= 0;
            var parts = new List<string>();
            if (story) parts.Add(Story.ObjectiveText(db));
            var quests = GoF2Remake.Events.EventRunner.Quests();
            foreach (var q in quests) parts.Add(q.title.UiCaps() + (q.objective.Length > 0 ? "\n" + q.objective : ""));
            storyText.text = parts.Count > 0 ? string.Join("\n\n", parts) : Localization.Extra("missionsNoStory", "No story mission right now.");   // the original: 174 "-BLANK-"
            // Remake multiplayer (no story there): the event graph mission the squad is on (EventMissions).
            var eventMission = GoF2Remake.Events.EventMissions.Active;
            bool inSession = GoF2Remake.Multiplayer.NetGame.Active;
            if (!story && quests.Count == 0 && eventMission != null && inSession)
                storyText.text = eventMission.title.UiCaps() + "\n" + string.Format(Localization.Extra("mpMissionCardBy", "Accepted by {0}"), eventMission.by)
                                 + "\n\n" + GoF2Remake.Events.EventMissions.ActiveText;
            storyMapTarget = story && !Session.StoryMission.IsEmpty && Session.StoryMission.visible ? Story.MapTarget : -1;
            if (storyMapTarget < 0) { int k = quests.FindIndex(q => q.target >= 0); storyMapTarget = k >= 0 ? quests[k].target : -1; }
            Show(storyMap, storyMapTarget >= 0);

            var m = Freelance.Mission;
            bool active = Freelance.Active;
            // Remake single player: an event graph's bar mission in the panel when there is no freelance mission.
            var graphMission = !active && !inSession ? eventMission : null;
            portrait.EnableInClassList("portrait-hidden", !active && graphMission?.face == null && graphMission?.character == null && (graphMission == null || graphMission.speakerId < 0));
            if (active) Portrait.Show(portrait, m.clientPortrait, false);
            else if (graphMission?.character != null) Portrait.ShowCharacter(portrait, Modding.ModCharacters.Find(graphMission.character), false);
            else if (graphMission?.face != null) Portrait.Show(portrait, graphMission.face, false);
            else if (graphMission != null && graphMission.speakerId >= 0) Portrait.ShowSpeaker(portrait, graphMission.speakerId, graphMission.speakerId == 0);
            if (graphMission != null)
            {
                freelanceClient.text = graphMission.clientName.UiCaps();
                freelanceWhere.text = (db.Stations.Find(s => s.index == graphMission.station)?.name ?? "") + "\n" + graphMission.title;
                string objective = GoF2Remake.Events.EventRunner.LocalMissionObjective;
                freelanceText.text = (objective.Length > 0 ? objective + "\n\n" : "") + GoF2Remake.Events.EventMissions.ActiveText;
                Show(freelanceMap, GoF2Remake.Events.EventRunner.LocalMissionTarget >= 0);
                Show(discard, true);
            }
            else
            {
                freelanceClient.text = active ? m.clientName.UiCaps() : "";
                string station = active ? db.Stations.Find(s => s.index == m.clientStation)?.name ?? "" : "";
                freelanceWhere.text = active ? $"{station}\n{m.Name}" : "";
                freelanceText.text = active ? FreelanceText(db, m) : Localization.Extra("missionsNoFreelance", "No mission taken. The visitors in a Space Lounge offer work.");
                Show(freelanceMap, active);
                Show(discard, active);
            }
            active |= graphMission != null;
            storyScroll.scrollOffset = freelanceScroll.scrollOffset = Vector2.zero;
            menu.Focus(active ? freelanceMap : story ? storyMap : close);
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Show on map in the Story panel: the campaign's target, else the first event graph quest's (-1: none).</summary>
        int storyMapTarget = -1;

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
            // Remake (players' suggestion): the map travels like the station's Map (a confirmed station launches with it programmed).
            var map = StarMap.Open(level.Database, StarMapMode.Mission, GalaxyMap.HasJumpDrive(level.Database), r =>
            {
                root.RemoveFromClassList("station-map-open");
                if (r.station >= 0) { menu.TravelFromMap(r); return; }
                menu.Focus(from);
            }, -1, target, false, routeFromSystem, -1, menu.CanTravelFromMap);
            if (map == null) root.RemoveFromClassList("station-map-open");
            else map.SetHelp(menu.MapHelpText);   // Modified: the station map's "?" here too
        }

        /// <summary>423 Discard -> 418 "Are you sure?" -> the same clean-up as a discarded mission.</summary>
        void AskDiscard()
        {
            string warning = GoF2Remake.Multiplayer.NetMissions.DiscardWarning();   // multiplayer: ends it for the squad
            if (!Freelance.Active && GoF2Remake.Events.EventMissions.Active != null && !GoF2Remake.Multiplayer.NetGame.Active)
            {
                menu.ShowDialog(T(418), () => { GoF2Remake.Events.EventRunner.AbandonLocalMission(); Fill(); });   // an event graph's bar mission
                return;
            }
            menu.ShowDialog(T(418) + warning, () => { GoF2Remake.Multiplayer.NetMissions.Abandon(); Freelance.Discard(); Fill(); menu.RefreshCredits(); });
        }

        public VisualElement[] NavItems()
        {
            var l = new List<VisualElement>();
            if (!missionsTab.ClassListContains("missions-tab--hidden"))
            {
                l.Add(missionsTab);
                if (!pirateTab.ClassListContains("missions-tab--hidden")) l.Add(pirateTab);   // Modified: in the tabs' order
                if (!wantedTab.ClassListContains("missions-tab--hidden")) l.Add(wantedTab);
            }
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
                var b = new Button { text = ItemInfo.TaggedWantedName(w) };   // (a mod's: its tag, dim)
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
                wantedText.text = Localization.Extra("missionsPickEntry", "Pick an entry from the list.");
                Show(wantedMap, false);
                return;
            }
            var face = w.character != null ? Modding.ModCharacters.Find(w.character) : null;   // remake mods: a character's portrait
            if (face != null) Portrait.ShowCharacter(wantedPortrait, face, false);
            else Portrait.Show(wantedPortrait, w.portraitParts, false);
            wantedName.text = ItemInfo.TaggedWantedName(w, true);
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
            wantedText.text = $"{T(3223)}\n{from}\n\n{T(3224)}\n{to}\n\n{(w.IsMod ? w.description ?? "" : T(3174 + w.index))}";
            wantedScroll.scrollOffset = Vector2.zero;
            Show(wantedMap, st != null && st.active && !dead && st.travelsTo >= 0);
        }

        // ---- the Pirate activity board (PirateReports) --------------------------------------------------------------

        void FillPirates()
        {
            wantedList.Clear();
            wantedRows.Clear();
            var list = PirateReports.Board();
            if (pirateSelected < 0 || !list.Exists(s => s.id == pirateSelected))
                pirateSelected = list.Count > 0 ? list[0].id : -1;
            foreach (var s in list)
            {
                int id = s.id;
                var b = new Button { text = $"{PirateTitle(s)} · {PirateReports.SystemName(level.Database, s.system)}" };
                b.AddToClassList("wanted-row");
                b.AddToClassList("gof-semibold");
                b.EnableInClassList("wanted-row--active", !s.done);
                b.EnableInClassList("wanted-row--dead", s.done);
                b.EnableInClassList("wanted-row--story", s.tier == 4 && !s.done);
                b.EnableInClassList("wanted-row--tier5", s.tier >= 5 && !s.done);
                b.EnableInClassList("wanted-row--selected", id == pirateSelected);
                b.RegisterCallback<PointerDownEvent>(_ => menu.PlayPush(), TrickleDown.TrickleDown);
                b.clicked += () => { menu.PlayRelease(); pirateSelected = id; FillPirates(); };
                b.RegisterCallback<FocusInEvent>(_ => { if (pirateSelected != id) { pirateSelected = id; ShowPirateDetails(); foreach (var r in wantedRows) r.EnableInClassList("wanted-row--selected", r == b); } });
                wantedList.Add(b);
                wantedRows.Add(b);
            }
            ShowPirateDetails();
            menu.Focus(wantedRows.Count > 0 ? wantedRows[Mathf.Max(0, list.FindIndex(s => s.id == pirateSelected))] : close);
        }

        /// <summary>A boss by its name, an outpost as 441 "Pirate Outpost".</summary>
        static string PirateTitle(PirateSighting s) => s.boss ? s.name : T(441);

        void ShowPirateDetails()
        {
            var db = level.Database;
            var s = PirateReports.Find(pirateSelected);
            wantedPortrait.EnableInClassList("portrait-hidden", s == null);
            if (s == null)
            {
                wantedName.text = wantedStatus.text = wantedBounty.text = "";
                wantedText.text = Localization.Extra("pirateBoardEmpty", "No pirate activity reported. Check after your next docking.");
                Show(wantedMap, false);
                return;
            }
            Portrait.ShowSpeaker(wantedPortrait, 9, false);   // the Pirate Boss face
            wantedName.text = PirateTitle(s).UiCaps();
            string status = s.done ? (s.boss ? T(3227) : Localization.Extra("pirateStatusDestroyed", "Destroyed"))
                                   : Localization.Extra("pirateStatusReported", "Reported");
            wantedStatus.text = $"{T(3226)} {status}";
            wantedBounty.text = $"{T(3225)} {ItemInfo.Credits(s.bounty)}";
            var sys = db.Systems.Find(x => x.index == s.system);
            string security = sys != null && sys.securityLevel >= 0 && sys.securityLevel <= 3 ? T(402 + sys.securityLevel) : "";
            float now = PirateReports.Now;
            int ago = Mathf.Max(0, Mathf.RoundToInt((now - s.reportedAt) / 60f)), left = Mathf.Max(0, Mathf.RoundToInt((s.expiresAt - now) / 60f));
            string what = (s.boss ? Localization.Extra("pirateBoss", "A pirate boss and their crew, preying on the traders here.")
                                  : Localization.Extra("pirateOutpost", "A pirate outpost with its guards, hidden far out from a station."))
                + " " + TierHint(s.tier);
            wantedText.text = $"{Localization.Extra("pirateLastSeen", "Last seen in")}\n{PirateReports.SystemName(db, s.system)} ({security})\n\n"
                + $"{string.Format(Localization.Extra("pirateThreat", "Threat: {0}"), PirateReports.TierName(s.tier))}\n"
                + (s.done ? "" : string.Format(Localization.Extra("pirateReportedAgo", "Reported {0} min ago, off the board in {1} min."), ago, left) + "\n\n")
                + what + "\n\n" + Localization.Extra("pirateSearchHint", "Its exact position is unknown: search the system's station orbits.");
            wantedScroll.scrollOffset = Vector2.zero;
            Show(wantedMap, !s.done);
        }

        static string TierHint(int tier)
        {
            switch (tier)
            {
                case 1: return Localization.Extra("pirateTierHint1", "Barely a threat: a lone ship or a handful of guards.");
                case 2: return Localization.Extra("pirateTierHint2", "A small band. Expect light resistance.");
                case 3: return Localization.Extra("pirateTierHint3", "A capable crew. Come prepared.");
                case 4: return Localization.Extra("pirateTierHint4", "Heavily armed and well guarded. Bring a strong ship.");
                default: return Localization.Extra("pirateTierHint5", "Extremely dangerous: for the best-equipped pilots or a squad.");
            }
        }

        /// <summary>The star map's route to the sighting's system (its station only routes there: no icon marks it).</summary>
        void ShowPirateOnMap()
        {
            var s = PirateReports.Find(pirateSelected);
            if (s == null || s.done) return;
            ShowOnMap(s.station, wantedMap);
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
