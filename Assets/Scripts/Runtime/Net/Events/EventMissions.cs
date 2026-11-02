// EventMissions.cs
// Remake events: the event graphs as bar missions. An event graph whose Start node has a Mission title (EventRunner'
// "mission" lines: title, offer, client, reward, players, stations) is offered in the Space Lounge of its stations (every
// station when it names none) by a visitor: the client (a story character, a race and a name like /dialog's speakers, else a
// generated face of the system's race). The docking player's game asks the server for the offers of its station
// (RequestOffers, from StationLevel.BuildBar); each one becomes a visitor (StationLevel.AddVisitor, on a free slot) whose chat
// (LoungeChat, AgentOffer.EventMission) shows the offer text, the reward and the players it needs; Okay asks for the squad
// (the whole squad docked here, like NetMissions.AcceptRefusal, and within the mission's player count) and the server starts
// the event for that team (EventRunner.StartMission: its selectors, counts, triggers and scoreboard cover only the team).
// Every team member then holds it (Active: the Missions window shows it, the pause menu's too); the others than the one who
// took it get the mission card (EventScreen.ShowMissionCard: the mission, who took it, the client, the reward). The event's
// Mission Complete / Mission Failed nodes end it (the reward split across the team), so does the team leaving the session.
// Not every bar offers one: its "mission chance" (the Start node's Offer chance, default 35 %) decides per station, for 30
// minutes (Offered: the same answer for everyone docking there and for a player docking again, so re-docking doesn't roll it
// anew); a station whose mission was taken doesn't offer it again until 30 minutes after that (Taken), so a team can't take
// the same job over and over ("mission takers": one squad in the universe, the default, offers it nowhere while a team is on
// it and a visitor still showing it refuses (EventRunner.StartMission); one per station only takes it out of that bar; any
// number never takes it out). When the run ends the team's games show the client's message like a freelance mission's
// (Finished: 373-377 + 216 on success, 384-388 + 392 on failure, the client's face and voice).
// Single player (no session) offers them the same way, one pilot only: the run is the player's one mission (next to no
// freelance mission), lasts across scenes and saves (Session.GraphQuests, EventRunner' checkpoints), shows in the Missions
// window's Freelance panel with Discard, and a success counts like a freelance mission's (missions +1, standing +5).

using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.UI;
using UnityEngine;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class EventMissions
    {
        const char US = '\u001f', RS = '\u001e', GS = '\u001d';

        /// <summary>A mission graph on offer (and the one a player is on).</summary>
        public sealed class Offer
        {
            public string name = "", title = "", text = "", speaker = "", by = "";
            public int race, reward = -1, minPlayers = 1, maxPlayers = 4, station = -1;
            public bool male = true;
            public int[] face;          // a generated face (null: a story speaker, speakerId, or a mod's character)
            public int speakerId = -1;
            public string character;    // a mod's character ("mod_id:id", Modding.ModCharacters)
            public string clientName = "";
            public string entry = "";   // the offer as the server sent it (single player keeps it in the save)
        }

        /// <summary>How long a station's roll for a mission lasts, and how long a taken mission stays gone from its bar (s).</summary>
        public const float OfferSeconds = 1800f;

        // Server: per mission and station, whether its bar offers it, until when (EventHost.Now) and the offer as sent (the
        // same visitor, a generated client's name and face too, for everyone docking there meanwhile).
        static readonly Dictionary<(string mission, int station), (bool offered, float until, string entry)> rolls =
            new Dictionary<(string, int), (bool, float, string)>();

        // This game: the offers whose visitor the player has talked to (their plate then names the mission, LoungeChat.Plate).
        static readonly HashSet<(string mission, int station)> talked = new HashSet<(string, int)>();

        static int offersStation = -1;
        static Func<Agent, bool> addVisitor;
        static readonly Dictionary<Agent, Offer> byAgent = new Dictionary<Agent, Offer>();

        /// <summary>The event mission this player is on (null: none).</summary>
        public static Offer Active { get; private set; }

        /// <summary>The Mod Builder's play test of a bar mission: offered in every bar while it runs, whatever its chance,
        /// stations and pilot count (ModBuilderPlay.StartQuest).</summary>
        public static string PlayTestOffer;
        static bool PlayTested(string name) => PlayTestOffer != null && PlayTestOffer == name && Modding.ModBuilder.PlayTesting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { offersStation = -1; addVisitor = null; byAgent.Clear(); Active = null; rolls.Clear(); talked.Clear(); PlayTestOffer = null; }

        /// <summary>EventRunner.Reset (a new session, the server going): every station rolls its offers anew.</summary>
        internal static void ResetOffers() => rolls.Clear();

        /// <summary>Server: whether 'station''s bar offers 'm' now (its chance, decided for OfferSeconds per station).</summary>
        internal static bool Offered(EventRunner.MissionInfo m, int station) => OfferEntry(m, station) != null;

        /// <summary>Server: the offer 'station''s bar makes of 'm' now (Entry, kept with the roll), null: none.</summary>
        static string OfferEntry(EventRunner.MissionInfo m, int station)
        {
            float now = EventHost.Now;
            var key = (m.name, station);
            if (!rolls.TryGetValue(key, out var roll) || now >= roll.until)
            {
                bool offered = m.chance >= 100 || (m.chance > 0 && UnityEngine.Random.Range(0, 100) < m.chance);
                roll = (offered, now + OfferSeconds, offered ? Entry(m, station) : null);
                rolls[key] = roll;
            }
            return roll.offered ? roll.entry : null;
        }

        /// <summary>Server: 'm' was taken at 'station': its bar doesn't offer it again for OfferSeconds.</summary>
        internal static void Taken(string name, int station) => rolls[(name, station)] = (false, EventHost.Now + OfferSeconds, null);

        /// <summary>LoungeChat: the player talked to an offer's visitor.</summary>
        public static void MarkTalked(Agent a)
        {
            var o = OfferOf(a);
            if (o != null) talked.Add((o.name, o.station));
        }

        static string X(string key, string english) => Localization.Extra(key, english);
        static bool InSession => NetGame.Active && NetState.Instance != null && NetState.Instance.IsSpawned;

        /// <summary>A new single-player game, a load, the Missions window's Discard: no event mission.</summary>
        internal static void ResetLocal()
        {
            Active = null;
            foreach (var a in byAgent.Keys) a.accepted = false;
        }

        /// <summary>EventRunner.RestoreLocal: a loaded save's bar mission is the player's again.</summary>
        internal static void RestoreActive(GraphQuestState record)
        {
            var o = Parse(record.offer);
            if (o == null) return;
            o.entry = record.offer;
            o.station = record.station;
            o.by = EventHost.LocalName;
            Active = o;
        }

        // ---- this player's game ----------------------------------------------------------------------------

        /// <summary>StationLevel.BuildBar: asks the server for the missions offered at 'station'; each arrives as a visitor
        /// through 'add' (false: no free slot).</summary>
        public static void RequestOffers(int station, Func<Agent, bool> add)
        {
            byAgent.Clear();
            offersStation = station;
            addVisitor = add;
            if (InSession) NetState.Instance.RequestEventOffers(station);
            else if (EventHost.Local) ReceiveOffers(station, Offers(station, true));   // single player: this game's own graphs
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
                o.entry = entry;
                var agent = new Agent
                {
                    name = o.clientName, race = o.race, male = o.male, station = station, offer = AgentOffer.EventMission,
                    portrait = o.face ?? AgentGenerator.CreatePortrait(true, 0),
                };
                if (Active != null && Active.name == o.name) agent.accepted = true;
                agent.known = talked.Contains((o.name, station));
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
            if (EventHost.Local)
            {
                // Single player: one pilot, no squad.
                if (o.reward > 0) parts.Add(string.Format(X("spEventMissionReward", "The reward is {0}."), ItemInfo.Credits(o.reward)));
                return string.Join("\n", parts);
            }
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
            if (EventHost.Local)
            {
                if (Active != null) return string.Format(X("mpEventMissionActive", "You are already on the mission {0}."), Active.title);
                if (Freelance.Active) return X("mpEventMissionFreelance", "You already have a mission: finish or discard it first (Missions).");
                return null;
            }
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
            if (o == null) return;
            if (EventHost.Local) AcceptLocal(o);
            else if (InSession) NetState.Instance.AcceptEventMission(o.name, o.station);
        }

        /// <summary>Single player: the run starts for the player as a lasting one (its record in Session.GraphQuests).</summary>
        static void AcceptLocal(Offer o)
        {
            var info = EventRunner.MissionGraphs().Find(m => m.name == o.name);
            if (info == null || ((!Offered(info, o.station) || !Startable(info)) && !PlayTested(o.name))) { EventHost.NoticeTo(LocalPilot.Instance, X("mpEventMissionGone", "That mission isn't offered here any more.")); return; }
            var record = new GraphQuestState { name = o.name, kind = GraphQuestState.KindMission, title = o.title, station = o.station, offer = o.entry,
                                               once = info.once ? o.name : "" };
            Session.GraphQuests.Add(record);
            string why = EventRunner.StartMission(o.name, new HashSet<ulong> { 0 }, o.station, LocalPilot.Instance, record);
            if (why != null) { Session.GraphQuests.Remove(record); EventHost.NoticeTo(LocalPilot.Instance, why); return; }
            Taken(o.name, o.station);
            OnStarted(o.entry + US + o.station.ToString(CultureInfo.InvariantCulture) + US + LocalPilot.Instance.DisplayName);
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
            string me = EventHost.LocalName;
            if (EventHost.Local) EventHost.NoticeTo(LocalPilot.Instance, string.Format(X("mpEventMissionTaken", "Mission accepted: {0}."), o.title));
            else if (o.by == me) NetChat.Notice(string.Format(X("mpEventMissionTaken", "Mission accepted: {0}."), o.title));
            else
            {
                NetChat.Notice(string.Format(X("mpMissionShared", "{0} accepted a squad mission: {1}."), o.by, o.title));
                var lines = new List<string>();
                if (o.clientName.Length > 0) lines.Add(string.Format(X("mpMissionCardClient", "Client: {0}"), o.clientName));
                var st = NetGame.Db?.Stations.Find(s => s.index == o.station);
                if (st != null) lines.Add(string.Format(X("mpMissionCardFrom", "Taken at: {0}"), st.name));
                if (o.reward > 0) lines.Add(string.Format(X("mpMissionCardReward", "Reward: {0}, shared by the squad"), ItemInfo.Credits(o.reward)));
                EventScreen.ShowMissionCard(null, o.title, string.Format(X("mpMissionCardBy", "Accepted by {0}"), o.by), string.Join("\n", lines), o.face, o.face == null ? o.speakerId : -1, o.character);
            }
        }

        /// <summary>How a mission's run ended: its Mission Complete, its Mission Failed (or the team leaving its orbits), or
        /// anything else (Discard, /event stop, the session going), which shows no message.</summary>
        public enum Outcome { Stopped = 0, Success = 1, Failure = 2 }

        /// <summary>NetState: the team's mission ended ("name US outcome").</summary>
        internal static void OnEnded(string name)
        {
            int cut = name.IndexOf(US);
            var outcome = cut >= 0 && int.TryParse(name.Substring(cut + 1), out int n) ? (Outcome)n : Outcome.Stopped;
            if (cut >= 0) name = name.Substring(0, cut);
            if (Active == null || Active.name != name) return;
            var ended = Active;
            if (outcome == Outcome.Success)
            {
                // Like a freelance mission's (Freelance.Succeed): missions completed +1, standing +5 toward the client's race.
                Session.FreelanceCompleted++;
                Flight.Standing.ApplyDelict(Active.race, -5);
            }
            Active = null;
            foreach (var a in byAgent.Keys) a.accepted = false;
            if (outcome != Outcome.Stopped) Finished(ended, outcome == Outcome.Success);
        }

        /// <summary>The client's message at the end, like a freelance mission's (Freelance.SuccessText / FailureText,
        /// DialogueWindow::loadContent modes 1 / 2): one of 373-377 + 216 or 384-388 + 392, with the client's face and voice.</summary>
        static void Finished(Offer o, bool success)
        {
            int textId = success ? 373 + UnityEngine.Random.Range(0, 5) : 384 + UnityEngine.Random.Range(0, 5);
            string text = Localization.Get(textId) + "\n\n" + Localization.Get(success ? 216 : 392);
            var line = new DialogueLine { speaker = -1, text = text, textId = textId, agentName = o.clientName };
            if (o.character != null) line.character = o.character;
            else if (o.face == null && o.speakerId >= 0) line.speaker = o.speakerId;
            else
            {
                line.agentPortrait = o.face;
                line.voice = GenericVoice.For(textId, o.race, o.male, o.face);
            }
            EventScreen.QueueDialog(new List<DialogueLine> { line }, null, true);   // the run is over: kept when the events stop
        }

        /// <summary>The Missions window's text for the active one (its offer and reward).</summary>
        public static string ActiveText => Active == null ? "" : ChatText(Active);

        // ---- the server ---------------------------------------------------------------------------------

        /// <summary>One offer: "name US title US text US speaker(RS) US race US male US reward US min US max".</summary>
        static string Entry(EventRunner.MissionInfo m, int station)
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
                race = Flight.Standing.Look(race);   // (a mod race's system: its look race's client)
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
            if (SpeakerSpec.TryParse(f[3], RS, out var who))
            {
                int id = who.id;
                o.clientName = who.name;
                if (id == SpeakerSpec.Generated) o.face = who.face;
                else if (id == SpeakerSpec.Character)
                {
                    o.character = who.characterKey;
                    var c = who.CharacterDef;
                    if (c != null) { o.race = c.race; o.male = c.male; }   // the bar figure
                    o.clientName = who.DisplayName("");
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
        internal static string Offers(int station, bool singlePlayer = false)
        {
            var list = new List<string>();
            foreach (var m in EventRunner.MissionGraphs())
                if (singlePlayer && PlayTested(m.name)) list.Add(Entry(m, station));   // the builder's play test: here for sure
                else
                // Single player: the mods' and the Events folder's graphs for one pilot (the built-ins are multiplayer examples).
                if ((m.stations.Count == 0 || m.stations.Contains(station)) && (!singlePlayer || (m.minPlayers <= 1 && !m.builtIn && Startable(m)))
                    && (m.takers != EventMissionTakers.OneInUniverse || !EventRunner.MissionTaken(m.name))   // one squad in the universe
                    && OfferEntry(m, station) is string entry)
                    list.Add(entry);
            return string.Join(GS.ToString(), list);
        }

        /// <summary>Single player (remake, a modder's request): a bar mission's "Starts when" holds and, with "Only once", it
        /// isn't done yet. A session's bars ignore both: the server has no one player's game to ask.</summary>
        internal static bool Startable(EventRunner.MissionInfo m)
        {
            if (m.once && Session.GraphQuestsDone.Contains(m.name)) return false;
            string when = (m.starts ?? "").Trim();
            if (when.Equals("never", StringComparison.OrdinalIgnoreCase)) return false;
            if (when.Length == 0) return true;
            if (EventRunner.Condition(when, out string error)) return true;
            if (error != null) Debug.LogWarning($"Event: bar mission {m.name}'s start condition: {error}");
            return false;
        }

        /// <summary>Server: a player takes the mission 'name' at 'station' for their squad (or alone).</summary>
        internal static void OnAccept(ulong client, string name, int station)
        {
            var p = EventHost.Find(client);
            if (p == null || !NetGuard.DockedAt(client, station)) return;
            void Refuse(string why) => EventHost.NoticeTo(p, why);
            var info = EventRunner.MissionGraphs().Find(m => m.name == name);
            if (info == null || (info.stations.Count > 0 && !info.stations.Contains(station)) || !Offered(info, station))
            { Refuse(X("mpEventMissionGone", "That mission isn't offered here any more.")); return; }
            var team = new HashSet<ulong> { client };
            var members = new List<IPilot> { p };
            if (p.SquadId != 0)
                foreach (var q in EventHost.Pilots)
                    if (q != null && q.IsSpawned && q != p && q.SquadId == p.SquadId) { team.Add(q.OwnerClientId); members.Add(q); }
            foreach (var q in members)
                if (!q.InHangar || q.Station != station) { Refuse(X("mpMissionSquadHere", "The whole squad must be docked at this station to accept a mission.")); return; }
            if (team.Count < info.minPlayers || team.Count > info.maxPlayers)
            { Refuse(string.Format(X("mpEventMissionCount", "This mission takes {0} to {1} pilots."), info.minPlayers, info.maxPlayers)); return; }
            string why = EventRunner.StartMission(name, team, station, p);
            if (why != null) { Refuse(why); return; }
            if (info.takers != EventMissionTakers.Anyone) Taken(name, station);
            string payload = Entry(info, station) + US + station.ToString(CultureInfo.InvariantCulture) + US + p.DisplayName;
            foreach (var q in members) EventHost.EventMission(q.OwnerClientId, true, payload);
        }

        /// <summary>Server, EventRunner (the run ended): the team's games drop it (and show the client's message).</summary>
        internal static void Ended(HashSet<ulong> team, string name, Outcome outcome)
        {
            if (team == null || !EventHost.ServerReady) return;
            string payload = name + US + ((int)outcome).ToString(CultureInfo.InvariantCulture);
            foreach (ulong id in team)
                if (EventHost.Find(id) != null) EventHost.EventMission(id, false, payload);
        }
    }
}
