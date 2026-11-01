// Freelance.cs
// The player's freelance mission (Status missions[1]; Reference/research/freelance_missions.md 3.3, 4, 5). Plain C#:
//   Accept / Discard     SpaceLounge::onKeyPress tail: one mission at a time (a new one discards the old: the Secure
//                        Containers / Cabins leave the hold, passengers go home); Courier loads item 116 x amount
//                        (unsaleable), Passenger boards the passengers; Extreme pays a tenth up front
//   IsMissionOrbit       Status::departStation 0xb63e0: the target station's orbit is a mission orbit unless the type is
//                        8 / 14 (docking only), 11 or 13
//   CheckDocked          Status::missionCompleted / missionFailed (docked = true) 0xb924c / 0xb91f4
//   Succeed / Fail       ModStation::OnTouchEnd / MGame::OnTouchEnd after the dialog: payout (docked: reward + bonus of
//                        1 000 001 or more pays 6 666, the original's guard at 0xeaaee), standing +5 toward the client,
//                        missions completed +1, cargo clean-up; a failed Challenge takes the wager
//   ToReturnTrip         MGame::successCheck, Recovery / Salvage: the container is aboard, bring it to the client's station
//                        (the mission becomes a Passenger-like type 11 with status -1)
// Texts: success 373-377 + 216 (Shima 108: 458), failure 384-388 + 392, return trip 389.
// Multiplayer (NetMissions): a squad has one mission, the same for every member, accepted with the whole squad docked at
// the agent's station; its progress is shared, a success anywhere pays everyone in it an equal share and ends it for all,
// so does a failure; the member carrying the containers / passengers delivers them.

using System.Collections.Generic;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class Freelance
    {
        public enum DockResult { None, Success, Failure }

        public const int Documents = 115, SecureContainer = 116, SecureCabin = 117;

        public static FreelanceMission Mission => Session.FreelanceMission;
        public static bool Active => Mission != null && !Mission.IsEmpty;

        // ---- passengers -------------------------------------------------------------------------------------

        /// <summary>Ship::getMaxPassengers (Ship+0x44): the mounted cabins' sizes (category 20: 3 / 5 / 10).</summary>
        public static int MaxPassengers(Database db)
        {
            int n = 0;
            foreach (var e in Session.Equipment)
            {
                var it = db.Item(e.item);
                if (it != null && it.categoryId == 20) n += it.Stat("cabinSize");
            }
            return n;
        }

        // ---- accepting --------------------------------------------------------------------------------------

        /// <summary>The checks before the confirmation: free cargo for Courier (337), cabins for Passenger (338); null = ok.</summary>
        public static string AcceptRefusal(Database db, FreelanceMission m)
        {
            string squad = GameHooks.SquadAcceptRefusal?.Invoke();   // multiplayer: the whole squad docked here
            if (squad != null) return squad;
            // Remake single player: an event graph's bar mission is the player's one mission too.
            string eventMission = GameHooks.ActiveEventMissionTitle?.Invoke();
            if (eventMission != null && !NetSession.Active)
                return string.Format(Localization.Extra("mpEventMissionActive", "You are already on the mission {0}."), eventMission);
            if (m.type == MissionType.Courier)
            {
                int free = Shop.FreeCargo(db) + (Active ? ContainersAboard() : 0);
                if (free < m.amount) return Localization.Get(337).Replace("#Q", m.amount.ToString());
            }
            if (m.type == MissionType.Passenger && MaxPassengers(db) < m.amount)
                return Localization.Get(338).Replace("#Q", m.amount.ToString());
            return null;
        }

        /// <summary>Extreme: a tenth of reward + bonus up front (203 when short).</summary>
        public static int UpFrontCost(FreelanceMission m) => Session.IsExtreme ? m.Total / 10 : 0;

        public static int ContainersAboard()
        {
            int n = 0;
            foreach (var s in Session.Cargo) if (s.item == SecureContainer || s.item == SecureCabin) n += s.amount;
            return n;
        }

        /// <summary>After "Yes": the mission becomes the player's (a copy; the agent keeps its offer, marked accepted).</summary>
        public static void Accept(Database db, Agent agent)
        {
            if (Active) Discard();
            var m = agent.mission.Clone();
            m.promisedBonus = m.CurrentBonus;   // the amount the chat promised is what the mission pays
            Session.Credits -= UpFrontCost(m);
            m.textIds = new List<int>(agent.textIds);
            if (m.type == MissionType.Courier)
            {
                Shop.AddToCargo(SecureContainer, m.amount);
                Session.Unsaleable.Add(SecureContainer);
            }
            if (m.type == MissionType.Passenger) Session.Passengers = m.amount;
            Session.FreelanceMission = m;
            Session.InformerKilled = Session.InformerFailed = false;
            agent.accepted = true;
            GameHooks.MissionAccepted?.Invoke(m);   // multiplayer: the squad gets it too
        }

        /// <summary>A discarded or failed mission: containers out of the hold, passengers home, no mission.</summary>
        public static void Discard()
        {
            ClearCargo();
            Session.Passengers = 0;
            Session.InformerKilled = Session.InformerFailed = false;
            Session.FreelanceMission = new FreelanceMission();
        }

        static void ClearCargo()
        {
            foreach (int item in new[] { SecureContainer, SecureCabin })
            {
                int n = 0;
                foreach (var s in Session.Cargo) if (s.item == item) n += s.amount;
                Shop.RemoveFromCargo(item, n);
                Session.Unsaleable.Remove(item);
            }
        }

        // ---- in space ---------------------------------------------------------------------------------------

        /// <summary>Status::departStation: does arriving at 'station' make its orbit the mission orbit?</summary>
        public static bool IsMissionOrbit(int station)
        {
            if (!Active || Mission.target != station) return false;
            int t = Mission.type;
            return t != MissionType.Purchase && t != MissionType.StolenGoods && t != MissionType.Passenger && t != MissionType.Informer;
        }

        /// <summary>MGame::dockEvent / Radar::draw / MGame::UseKhadorDrive: the level mission (Status+400, set by
        /// Status::departStation on arrival) refuses docking, the jumpgate, planet jumps and the Khador Drive (525) unless its
        /// type is 0 (Courier: delivered by docking) or 11 (a return trip). Lifts once the mission is won, failed or gone.
        /// The original never fails a mission for leaving: coming back rebuilt it from scratch (#32).</summary>
        public static bool BlocksTravel(int station) =>
            IsMissionOrbit(station) && Mission.type != MissionType.Courier && !Mission.won && !Mission.failed;

        /// <summary>Recovery / Salvage won in space: the container is aboard (unsaleable), take it to the client.</summary>
        public static void ToReturnTrip()
        {
            var m = Mission;
            Session.Unsaleable.Add(m.type == MissionType.Recovery ? SecureCabin : SecureContainer);
            m.type = MissionType.Passenger;
            m.target = m.clientStation;
            m.status = -1;
            m.won = false;
            m.textIds = new List<int> { 803 };
            GameHooks.MissionShared?.Invoke(m, false);   // the squad's copies become the return trip too
        }

        // ---- docking ----------------------------------------------------------------------------------------

        /// <summary>Status::missionCompleted / missionFailed with docked = true.</summary>
        public static DockResult CheckDocked(int station)
        {
            if (!Active) return DockResult.None;
            var m = Mission;
            switch (m.type)
            {
                case MissionType.Courier:
                case MissionType.Passenger:
                    // Multiplayer: only the squad member carrying the containers / passengers delivers them.
                    return station == m.target && (GameHooks.CanDeliver?.Invoke(m) ?? true) ? DockResult.Success : DockResult.None;
                case MissionType.Purchase:
                    return station == m.target && CargoCount(m.good) >= m.amount ? DockResult.Success : DockResult.None;
                case MissionType.Informer:
                    if (Session.InformerFailed) return DockResult.Failure;
                    return Session.InformerKilled ? DockResult.Success : DockResult.None;
                case MissionType.StolenGoods:
                    return station == m.clientStation && CargoCount(Documents) > 0 ? DockResult.Success : DockResult.None;
            }
            return DockResult.None;
        }

        static int CargoCount(int item)
        {
            int n = 0;
            foreach (var s in Session.Cargo) if (s.item == item) n += s.amount;
            return n;
        }

        /// <summary>A Stolen goods mission's documents are in the hold.</summary>
        public static bool HasDocuments => CargoCount(Documents) > 0;

        /// <summary>The station the mission's markers point at (the star map's and the HUD's icon), -1 = none. Stolen goods: the
        /// original marks the agent's station from the start; remake (a player's report): none while the documents are still
        /// to be found (the briefing names only the system), then the client's station.</summary>
        public static int MarkedStation => !Active ? -1 : Mission.type != MissionType.StolenGoods ? Mission.target : HasDocuments ? Mission.clientStation : -1;

        /// <summary>The station whose system the map marks and Show on map opens on: the marked one, else (Stolen goods before
        /// the documents are aboard) the documents' station, of which the map shows only the system.</summary>
        public static int MapStation => MarkedStation >= 0 ? MarkedStation : Active ? Mission.target : -1;

        /// <summary>ModStation::OnInitialize: the Stolen goods mission's documents wait in the target station's shop. Remake: not
        /// again while they are in the hold (#113: every docking at the target put another set in its shop).</summary>
        public static void OnEnterStation(StationStock stock)
        {
            if (!Active || Mission.type != MissionType.StolenGoods || stock == null || stock.station != Mission.target) return;
            if (NetSession.Active && Mission.status > 0) return;   // multiplayer: a squadmate has them
            if (HasDocuments) return;
            if (stock.items.Find(r => r.item == Documents) == null) Shop.InsertStock(stock, new ItemStack(Documents, 1));
        }

        // ---- results ----------------------------------------------------------------------------------------

        /// <summary>DialogueWindow::loadContent mode 1 (freelance): 373-377 + 216, Shima 458; Challenge 370 with the score.</summary>
        public static string SuccessText(out int textId, int playerKills = 0, int rivalKills = 0)
        {
            var m = Mission;
            if (m.target == 108) { textId = 458; return Localization.Get(458); }
            if (m.type == MissionType.Challenge)
            {
                textId = 370;
                return Localization.Get(370).Replace("#Q1", playerKills.ToString()).Replace("#Q2", rivalKills.ToString());
            }
            textId = 373 + Random.Range(0, 5);
            return Localization.Get(textId) + "\n\n" + Localization.Get(216);
        }

        /// <summary>Mode 2: 384-388 + 392.</summary>
        public static string FailureText(out int textId)
        {
            textId = 384 + Random.Range(0, 5);
            return Localization.Get(textId) + "\n\n" + Localization.Get(392);
        }

        /// <summary>The client's voice line for 'textId' (Globals::getDialogueSoundId with the mission's agent).</summary>
        public static string Voice(int textId) => GenericVoice.For(textId, Mission.clientRace, Mission.clientMale, Mission.clientPortrait);

        /// <summary>389: Recovery / Salvage won, return to the client's station.</summary>
        public static string ReturnText(Database db) =>
            Localization.Get(389).Replace("#S", db.Stations.Find(s => s.index == Mission.clientStation)?.name ?? "");

        /// <summary>Success after the dialog: cargo clean-up and statistics per type, missions +1, standing +5 toward the
        /// client (Standing::applyMissionCompleted), reward + bonus paid. Returns the amount paid.</summary>
        public static int Succeed(bool docked)
        {
            var m = Mission;
            switch (m.type)
            {
                case MissionType.Purchase: Shop.RemoveFromCargo(m.good, m.amount); break;
                case MissionType.Passenger: Session.PassengersDelivered += m.status == -1 ? 0 : Session.Passengers; break;
                case MissionType.Courier: Session.ContainersDelivered += m.amount; break;
                case MissionType.StolenGoods: Shop.RemoveFromCargo(Documents, 1); break;
            }
            int pay = m.Total;
            if (docked && pay >= 1000001) pay = 6666;
            if (GameHooks.SplitReward != null) pay = GameHooks.SplitReward(m, pay);   // multiplayer: an equal share for everyone in the squad
            Standing.ApplyDelict(m.clientRace, -5);
            Session.FreelanceCompleted++;
            CoopCampaign.FreelanceDone(m.netId);   // remake co-op: the crew's count for step 13 (a squad mission once)
            Session.Credits += pay;
            RecordChallenge(true);
            Discard();
            return pay;
        }

        /// <summary>Failure after the dialog: a lost Challenge costs the wager; the mission is gone.</summary>
        public static void Fail()
        {
            // Multiplayer: failed for the squad too (a lost Challenge's wager shared).
            int wager = Mission.type == MissionType.Challenge ? Mission.reward : 0;
            if (GameHooks.MissionFailed != null) wager = GameHooks.MissionFailed(Mission, wager);
            Session.Credits -= wager;
            RecordChallenge(false);
            Discard();
        }

        // ---- the rival afterwards (remake, a player's suggestion) ----------------------------------------------

        /// <summary>The bar's rival of a Challenge: the accepted agent of that mission at its station (one of the last three
        /// visited, Session.RecentStations), null when the bar has moved on.</summary>
        public static Agent Rival(FreelanceMission m)
        {
            if (m == null || m.type != MissionType.Challenge) return null;
            var stock = Session.RecentStations.Find(s => s.station == m.clientStation);
            return stock?.agents?.Find(a => a != null && a.accepted && a.HasMission && a.mission.type == MissionType.Challenge
                                            && a.name == m.clientName);
        }

        /// <summary>The Challenge ended ('won': the player's): its rival remembers (Agent.challengeResult).</summary>
        static void RecordChallenge(bool won)
        {
            var rival = Rival(Mission);
            if (rival != null) rival.challengeResult = won ? Agent.PlayerWon : Agent.RivalWon;
        }

        static readonly (string key, string english)[] RivalLostLines =
        {
            ("challengeRivalLost0", "That's some aim you've got, Mr. Maxwell. My compliments."),
            ("challengeRivalLost1", "Didn't think anyone could outshoot me out there. You earned every credit."),
            ("challengeRivalLost2", "Still trying to work out how you did that. Fine flying, pilot."),
            ("challengeRivalLost3", "All right, all right, you won. Next round's on me."),
        };

        static readonly (string key, string english)[] RivalWonLines =
        {
            ("challengeRivalWon0", "Need a few shooting lessons, Mr. Maxwell? I'm cheap."),
            ("challengeRivalWon1", "Thanks for the credits. Come back when you've learned to aim."),
            ("challengeRivalWon2", "Better luck next time, Mr. Maxwell. Maybe with a bigger gun."),
            ("challengeRivalWon3", "Don't feel bad, pilot. Not everyone's cut out for this."),
        };

        /// <summary>What the rival says at the bar after the contest (LoungeChat, in place of 859 "See you outside"): one
        /// of four lines by 'result' (Agent.PlayerWon / RivalWon), null with no result.</summary>
        public static string RivalLine(int result)
        {
            var lines = RivalLines(result);
            return lines.Count > 0 ? lines[Random.Range(0, lines.Count)] : null;
        }

        /// <summary>The four lines RivalLine picks from for 'result', in the current language (none with no result).</summary>
        public static List<string> RivalLines(int result)
        {
            var set = result == Agent.PlayerWon ? RivalLostLines : result == Agent.RivalWon ? RivalWonLines : null;
            var lines = new List<string>();
            if (set != null) foreach (var (key, english) in set) lines.Add(Localization.Extra(key, english));
            return lines;
        }
    }
}
