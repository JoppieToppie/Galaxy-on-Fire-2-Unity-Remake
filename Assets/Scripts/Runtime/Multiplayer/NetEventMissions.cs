// NetEventMissions.cs
// Remake multiplayer: the event graphs as bar missions. An event graph whose Start node has a Mission title (NetEvents'
// "mission" lines: title, offer, client, reward, players, stations) is offered in the Space Lounge of its stations (every
// station when it names none) by a visitor: the client (a story character, a race and a name like /dialog's speakers, else a
// generated face of the system's race). The docking player's game asks the server for the offers of its station
// (RequestOffers, from StationLevel.BuildBar); each one becomes a visitor (StationLevel.AddVisitor, on a free slot) whose chat
// (LoungeChat, AgentOffer.EventMission) shows the offer text, the reward and the players it needs; Okay asks for the squad
// (the whole squad docked here, like NetMissions.AcceptRefusal, and within the mission's player count) and the server starts
// the event for that team (NetEvents.StartMission: its selectors, counts, triggers and scoreboard cover only the team).
// Every team member then holds it (Active: the Missions window shows it, the pause menu's too); the others than the one who
// took it get the mission card (NetScreen.ShowMissionCard: the mission, who took it, the client, the reward). The event's
// Mission Complete / Mission Failed nodes end it (the reward split across the team), so does the team leaving the session.

using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.UI;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetEventMissions
    {
        const char US = '\u001f', RS = '\u001e', GS = '\u001d';

        /// <summary>A mission graph on offer (and the one a player is on).</summary>
        public sealed class Offer
        {
            public string name = "", title = "", text = "", speaker = "", by = "";
            public int race, reward = -1, minPlayers = 1, maxPlayers = 4, station = -1;
            public bool male = true;
            public int[] face;          // a generated face (null: a story speaker, speakerId)
            public int speakerId = -1;
            public string clientName = "";
        }

        static int offersStation = -1;
        static Func<Agent, bool> addVisitor;
        static readonly Dictionary<Agent, Offer> byAgent = new Dictionary<Agent, Offer>();

        /// <summary>The event mission this player is on (null: none).</summary>
        public static Offer Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { offersStation = -1; addVisitor = null; byAgent.Clear(); Active = null; }

        static string X(string key, string english) => Localization.Extra(key, english);
        static bool InSession => NetGame.Active && NetState.Instance != null && NetState.Instance.IsSpawned;

        // ---- this player's game ----------------------------------------------------------------------------

        /// <summary>StationLevel.BuildBar: asks the server for the missions offered at 'station'; each arrives as a visitor
        /// through 'add' (false: no free slot).</summary>
        public static void RequestOffers(int station, Func<Agent, bool> add)
        {
            byAgent.Clear();
            offersStation = station;
            addVisitor = add;
            if (!NetGame.Active) Active = null;
            if (InSession) NetState.Instance.RequestEventOffers(station);
        }

        /// <summary>NetState: the offers of a station (the server's Offers payload).</summary>
        internal static void ReceiveOffers(int station, string payload)
        {
            if (station != offersStation || addVisitor == null || string.IsNullOrEmpty(payload)) return;
            foreach (string entry in payload.Split(GS))
            {
                var o = Parse(entry);
                if (o == null) continue;
                o.station = station;
                var agent = new Agent
                {
                    name = o.clientName, race = o.race, male = o.male, station = station, offer = AgentOffer.EventMission,
                    portrait = o.face ?? AgentGenerator.CreatePortrait(true, 0),
                };
                if (Active != null && Active.name == o.name) agent.accepted = true;
                if (addVisitor(agent)) byAgent[agent] = o;
            }
        }

        /// <summary>The offer a bar visitor makes (null: not an event mission's visitor).</summary>
        public static Offer OfferOf(Agent a) => a != null && byAgent.TryGetValue(a, out var o) ? o : null;

        /// <summary>The offer's text in the chat: the offer, then the reward and how many pilots it needs.</summary>
        public static string ChatText(Offer o)
        {
            var parts = new List<string>();
            if (o.text.Length > 0) parts.Add(o.text);
            if (o.reward > 0) parts.Add(string.Format(X("mpEventMissionReward", "The reward is {0}, shared by the squad."), ItemInfo.Credits(o.reward)));
            if (o.minPlayers > 1 || o.maxPlayers < 16)
                parts.Add(o.minPlayers == o.maxPlayers ? string.Format(X("mpEventMissionPilotsExact", "It takes {0} pilots."), o.minPlayers)
                    : o.minPlayers > 1 ? string.Format(X("mpEventMissionPilots", "It takes {0} to {1} pilots."), o.minPlayers, o.maxPlayers)
                    : string.Format(X("mpEventMissionPilotsMax", "Up to {0} pilots."), o.maxPlayers));
            return string.Join("\n", parts);
        }

        /// <summary>Why this player's squad can't take it here (null: it can).</summary>
        public static string AcceptRefusal(Offer o)
        {
            if (!InSession) return X("mpEventMissionOffline", "Not connected to the session.");
            if (Active != null) return string.Format(X("mpEventMissionActive", "You are already on the mission {0}."), Active.title);
            string squad = NetMissions.AcceptRefusal();
            if (squad != null) return squad;
            int pilots = NetSquad.InSquad ? NetSquad.Members().Count : 1;
            if (pilots < o.minPlayers) return string.Format(X("mpEventMissionTooFew", "This mission needs at least {0} pilots: invite some into your squad."), o.minPlayers);
            if (pilots > o.maxPlayers) return string.Format(X("mpEventMissionTooMany", "This mission takes at most {0} pilots."), o.maxPlayers);
            return null;
        }

        /// <summary>LoungeChat's confirmation: the server starts it for the squad (or answers why not).</summary>
        public static void Accept(Offer o)
        {
            if (InSession && o != null) NetState.Instance.AcceptEventMission(o.name, o.station);
        }

        /// <summary>NetState: the mission started for this player's team ("payload": Offers' entry, the station, the taker's name).</summary>
        internal static void OnStarted(string payload)
        {
            int cut = payload.LastIndexOf(US);
            int cut2 = cut > 0 ? payload.LastIndexOf(US, cut - 1) : -1;
            var o = cut2 > 0 ? Parse(payload.Substring(0, cut2)) : null;
            if (o == null) return;
            o.by = payload.Substring(cut + 1);
            int.TryParse(payload.Substring(cut2 + 1, cut - cut2 - 1), out o.station);
            Active = o;
            foreach (var kv in byAgent) if (kv.Value.name == o.name) kv.Key.accepted = true;
            string me = NetPlayer.Local != null ? NetPlayer.Local.DisplayName : "";
            if (o.by == me) NetChat.Notice(string.Format(X("mpEventMissionTaken", "Mission accepted: {0}."), o.title));
            else
            {
                NetChat.Notice(string.Format(X("mpMissionShared", "{0} accepted a squad mission: {1}."), o.by, o.title));
                var lines = new List<string>();
                if (o.clientName.Length > 0) lines.Add(string.Format(X("mpMissionCardClient", "Client: {0}"), o.clientName));
                var st = NetGame.Db?.Stations.Find(s => s.index == o.station);
                if (st != null) lines.Add(string.Format(X("mpMissionCardFrom", "Taken at: {0}"), st.name));
                if (o.reward > 0) lines.Add(string.Format(X("mpMissionCardReward", "Reward: {0}, shared by the squad"), ItemInfo.Credits(o.reward)));
                NetScreen.ShowMissionCard(null, o.title, string.Format(X("mpMissionCardBy", "Accepted by {0}"), o.by), string.Join("\n", lines), o.face, o.face == null ? o.speakerId : -1);
            }
        }

        /// <summary>NetState: the team's mission ended (completed, failed, or the event stopped).</summary>
        internal static void OnEnded(string name)
        {
            if (Active != null && Active.name == name) Active = null;
        }

        /// <summary>The Missions window's text for the active one (its offer and reward).</summary>
        public static string ActiveText => Active == null ? "" : ChatText(Active);

        // ---- the server ---------------------------------------------------------------------------------

        /// <summary>One offer: "name US title US text US speaker(RS) US race US male US reward US min US max".</summary>
        static string Entry(NetEvents.MissionInfo m, int station)
        {
            string speaker = null;
            int race = -1;
            bool male = true;
            if (m.client.Length > 0)
            {
                speaker = NetAdmin.ResolveSpeaker(m.client, new Dictionary<string, string>());
                var words = m.client.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                race = words.Length > 0 ? NetAdmin.RaceWord(words[0]) : -1;
                if (words.Length > 1 && words[1].Equals("female", StringComparison.OrdinalIgnoreCase)) male = false;
            }
            if (speaker == null)
            {
                // No client named: a generated face of the station's race.
                var db = NetGame.Db;
                var st = db.Stations.Find(s => s.index == station);
                race = st != null ? db.Systems.Find(s => s.index == st.system)?.raceId ?? 0 : 0;
                if (race < 0 || race > 3) race = 0;
                male = UnityEngine.Random.value < 0.7f;
                speaker = "-1" + US + AgentGenerator.RandomName(race, male) + US + string.Join(",", AgentGenerator.CreatePortrait(male, race));
            }
            if (race < 0) race = 0;
            int reward = double.TryParse(m.reward, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? (int)Math.Max(0, Math.Min(int.MaxValue, r)) : -1;
            string Clean(string s) => (s ?? "").Replace(US, ' ').Replace(GS, ' ');
            return string.Join(US.ToString(), m.name, Clean(m.title), Clean(m.offer), speaker.Replace(US, RS), race.ToString(CultureInfo.InvariantCulture),
                male ? "1" : "0", reward.ToString(CultureInfo.InvariantCulture), m.minPlayers.ToString(CultureInfo.InvariantCulture),
                m.maxPlayers.ToString(CultureInfo.InvariantCulture));
        }

        static Offer Parse(string entry)
        {
            var f = (entry ?? "").Split(US);
            if (f.Length < 9) return null;
            var o = new Offer { name = f[0], title = f[1], text = f[2] };
            int.TryParse(f[4], out o.race);
            o.male = f[5] == "1";
            int.TryParse(f[6], out o.reward);
            int.TryParse(f[7], out o.minPlayers);
            int.TryParse(f[8], out o.maxPlayers);
            // The client: "id RS name RS face" (NetAdmin.ResolveSpeaker): a generated face (-1) or a story speaker.
            var s = f[3].Split(RS);
            if (s.Length >= 2 && int.TryParse(s[0], out int id))
            {
                o.clientName = s[1];
                if (id == -1 && s.Length >= 3)
                {
                    var parts = s[2].Split(',');
                    o.face = new int[parts.Length];
                    for (int k = 0; k < parts.Length; k++) int.TryParse(parts[k], out o.face[k]);
                }
                else if (id >= 0)
                {
                    o.speakerId = id;
                    if (o.clientName.Length == 0) o.clientName = StoryTable.SpeakerName(id);
                }
            }
            if (o.clientName.Length == 0) o.clientName = Localization.Get(406 + o.race);
            return o;
        }

        /// <summary>Server: the offers of 'station' for a docking player (the missions they or their team aren't on).</summary>
        internal static string Offers(int station)
        {
            var list = new List<string>();
            foreach (var m in NetEvents.MissionGraphs())
                if (m.stations.Count == 0 || m.stations.Contains(station)) list.Add(Entry(m, station));
            return string.Join(GS.ToString(), list);
        }

        /// <summary>Server: a player takes the mission 'name' at 'station' for their squad (or alone).</summary>
        internal static void OnAccept(ulong client, string name, int station)
        {
            var p = NetSquad.Find(client);
            var state = NetState.Instance;
            if (p == null || state == null || !NetGuard.DockedAt(client, station)) return;
            void Refuse(string why) => state.NoticeTo(p, why);
            var info = NetEvents.MissionGraphs().Find(m => m.name == name);
            if (info == null || (info.stations.Count > 0 && !info.stations.Contains(station))) { Refuse(X("mpEventMissionGone", "That mission isn't offered here any more.")); return; }
            var team = new HashSet<ulong> { client };
            var members = new List<NetPlayer> { p };
            if (p.SquadId != 0)
                foreach (var q in NetPlayer.All)
                    if (q != null && q.IsSpawned && q != p && q.SquadId == p.SquadId) { team.Add(q.OwnerClientId); members.Add(q); }
            foreach (var q in members)
                if (!q.InHangar || q.Station != station) { Refuse(X("mpMissionSquadHere", "The whole squad must be docked at this station to accept a mission.")); return; }
            if (team.Count < info.minPlayers || team.Count > info.maxPlayers)
            { Refuse(string.Format(X("mpEventMissionCount", "This mission takes {0} to {1} pilots."), info.minPlayers, info.maxPlayers)); return; }
            string why = NetEvents.StartMission(name, team, station, p);
            if (why != null) { Refuse(why); return; }
            string payload = Entry(info, station) + US + station.ToString(CultureInfo.InvariantCulture) + US + p.DisplayName;
            foreach (var q in members) state.SendEventMission(q.OwnerClientId, true, payload);
        }

        /// <summary>Server, NetEvents (the run ended): the team's games drop it.</summary>
        internal static void Ended(HashSet<ulong> team, string name)
        {
            var state = NetState.Instance;
            if (state == null || !state.IsServer || team == null) return;
            foreach (ulong id in team)
                if (NetSquad.Find(id) != null) state.SendEventMission(id, false, name);
        }
    }
}
