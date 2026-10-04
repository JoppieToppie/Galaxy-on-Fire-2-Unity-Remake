// LoungeChat.cs
// A conversation with a bar agent (SpaceLounge::startChat 0x198974 / onKeyPress 0x19c6fc / OnTouchEnd 0x1a0360;
// Reference/research/freelance_missions.md 3). Plain C#, driven by the station menu's chat panel:
//   Start      the offer text: greeting 750-755 + 756 / 757 (#N), intro 758-763 (missions, purchases), the body per offer
//              (mission 786 + type (+ 802), reward line 764-766 (+ 767 bonus); small talk 820-840 without repeats in one
//              lounge visit; item 768-772 + 773 / 774 (+ 775); purchase 777 / 778; wingmen 779-781; diplomat 878-883;
//              story sellers 874 / 875 + 886 + index + 876 / 877 / 879), the question 841-843. Known agents: the stored
//              offer again (the bonus re-evaluated), or 857 / 858 / 859 once accepted
//   Choices    the HD answer buttons (Reference/research/lounge_ui.md 1.3 / 1.6): 860 Okay + 861 No thanks, plus 776 Let me
//              see it (sellers 2 / 3 / 9 / 10) or 804 Show it on the map + 807 What's the risk? (missions other than
//              Challenge); a single "Okay." for small talk, closing lines and after the risk answer. 862 "What was that?"
//              is the phone path's (unreachable on HD) and isn't offered
//   Choose     No thanks closes the chat (Status+0xe0; its 845-849 line is never visible on HD), map (805 / 806 or the
//              star map), risk 808 + int(d / 10 * 5) (Okay returns to the offer), accept -> the checks (337 / 338 / 203 /
//              785, shown as a message) and the confirmation (865 (+ 864), 866 / 868-873 / 885)
//   Confirm    "Yes": 850-852 then the deal (mission 853-855 / Challenge 856); a single "Okay." closes the chat
//   Voice      SpaceLounge::getSoundId 0x19fdb4: one lounge greeting per chat start by offer, race and gender
// Remake multiplayer: AgentOffer.EventMission, an event graph's bar mission (NetEventMissions): a greeting, the offer text
// with the reward and the pilots it needs, the question; Okay checks the squad and asks to confirm, the server starts it.
// The original's single random generator isn't reproducible; UnityEngine.Random picks the text variants.

using System;
using System.Collections.Generic;
using GoF2Remake.Flight;
using GoF2Remake.Multiplayer;
using GoF2Remake.UI;
using Random = UnityEngine.Random;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class LoungeChat
    {
        public enum Choice { Okay, NoThanks, Repeat, Map, Risk }
        /// <summary>ConfirmShip (remake, AgentOffer.SellShip): ConfirmText, then (Kaamo Club owned) 327 Sell / Keep, then
        /// ConfirmShipTrade.</summary>
        public enum Outcome { None, Confirm, Refused, ShowMap, Closed, ConfirmShip }

        /// <summary>The single "Okay." closes the chat (small talk, closing lines).</summary>
        bool closing;
        /// <summary>SpaceLounge+0x36: the risk line is shown; "Okay." returns to the offer.</summary>
        bool riskShown;
        /// <summary>The message of a failed check (Outcome.Refused, the ChoiceWindow as a message).</summary>
        public string RefusalText { get; private set; } = "";
        /// <summary>The single white "Okay." (not the green / red pair).</summary>
        public bool SingleOkay => Choices.Count == 1;

        /// <summary>A story seller's blueprint -> the produced item (agents.json sellBlueprint is the product's item index).</summary>
        public static Func<int, int> BlueprintProduct = bp => bp;

        readonly Database db;
        readonly int station;
        readonly HashSet<int> smallTalkUsed;
        public Agent Agent { get; }
        public string Text { get; private set; } = "";
        public List<Choice> Choices { get; } = new List<Choice>();
        /// <summary>The confirmation question after Okay (Outcome.Confirm).</summary>
        public string ConfirmText { get; private set; } = "";
        /// <summary>The station to show on the star map (Outcome.ShowMap).</summary>
        public int MapTarget { get; private set; } = -1;
        /// <summary>After the deal: the star map opens on the bought system (offer 4).</summary>
        public int RevealedSystem { get; private set; } = -1;
        /// <summary>The Kaamo dealer's ship went to the club (the hangar's parked hulls change).</summary>
        public bool BoughtShip { get; private set; }
        bool askedRisk, askedMap;

        static string T(int id) => Localization.Get(id);
        static string C(int credits) => ItemInfo.Credits(credits);

        /// <summary>'smallTalkUsed': SpaceLounge+0x58, the small-talk lines said during this lounge visit.</summary>
        public LoungeChat(Database db, Agent agent, int station, HashSet<int> smallTalkUsed)
        {
            this.db = db;
            this.station = station;
            this.smallTalkUsed = smallTalkUsed ?? new HashSet<int>();
            Agent = agent;
        }

        // ---- the opening --------------------------------------------------------------------------------------

        public void Start()
        {
            var a = Agent;
            bool first = !a.known;
            Choices.Clear();
            if (a.offer == AgentOffer.EventMission)
            {
                var o = NetEventMissions.OfferOf(a);
                a.known = true;
                closing = a.accepted || o == null;
                Text = closing ? T(858) : T(750 + Random.Range(0, 6)) + " " + NetEventMissions.ChatText(o) + "\n" + T(841 + Random.Range(0, 3));
                SetChoices();
                return;
            }
            if (!a.known)
            {
                Session.AgentsTalkedTo++;
                a.known = true;
                if (a.offer == AgentOffer.Purchase && !a.HasMission) a.mission = AgentGenerator.CreateMission(db, a, station, true);
                a.textIds = PickTextIds(a);
            }
            else if (a.accepted || a.offer == AgentOffer.SmallTalk && a.textIds.Count == 0)
            {
                Text = T(a.offer == AgentOffer.Purchase ? 857
                         : a.offer == AgentOffer.Wingmen || a.HasMission && a.mission.type == MissionType.Challenge ? 859 : 858);
                closing = true;
                SetChoices();
                return;
            }
            if (a.offer == AgentOffer.ShipDealer && !KaamoClub.Owned)
            {
                // Until the club is owned the dealer only greets (750-755); that chat doesn't count as talked to.
                Text = T(750 + Random.Range(0, 6));
                if (first) Session.AgentsTalkedTo--;
                closing = true;
                SetChoices();
                return;
            }
            if (a.offer == AgentOffer.ShipDealer && (a.sellShip < 0 || a.sellShip == Session.ShipIndex || KaamoClub.HasShip(a.sellShip)))
            {
                Text = T(858);
                closing = true;
                SetChoices();
                return;
            }
            if (a.offer == AgentOffer.SellShip && (a.sellShip < 0 || a.sellShip == Session.ShipIndex || !CustomShips.Offered(a.sellShip)))
            {
                // remake: the ship on offer is the one the player flies (bought here, or elsewhere), or custom ships were
                // switched off since this bar was generated
                Text = T(858);
                closing = true;
                SetChoices();
                return;
            }
            if (a.offer == AgentOffer.SellMod && Session.HasMod(a.sellMod))
            {
                Text = T(858);   // the mod is already on this hull
                closing = true;
                SetChoices();
                return;
            }
            Text = Compose(a, a.textIds);
            closing = !HasDeal;
            SetChoices();
        }

        bool HasDeal
        {
            get
            {
                var a = Agent;
                if (a.accepted) return false;
                switch (a.offer)
                {
                    case AgentOffer.SmallTalk: return false;
                    case AgentOffer.Diplomat: return Standing.IsEnemy(a.race);
                    case AgentOffer.Mission: case AgentOffer.Purchase: return a.HasMission;
                    case AgentOffer.SellMod: return !Session.HasMod(a.sellMod);   // installed on this hull: 858
                    case AgentOffer.EventMission: return NetEventMissions.OfferOf(a) != null;
                    default: return true;
                }
            }
        }

        bool IsMissionOffer => Agent.offer == AgentOffer.Mission || Agent.offer == AgentOffer.Purchase;
        bool IsSeller => Agent.offer == AgentOffer.SellItem || Agent.offer == AgentOffer.SellBlueprint || Agent.offer == AgentOffer.SellMod
                         || Agent.offer == AgentOffer.KaamoSpecial || Agent.offer == AgentOffer.ShipDealer || Agent.offer == AgentOffer.SellShip;

        /// <summary>drawLounge: which answer buttons show (lounge_ui.md 1.3).</summary>
        void SetChoices()
        {
            Choices.Clear();
            Choices.Add(Choice.Okay);
            if (closing || riskShown) return;
            Choices.Add(Choice.NoThanks);
            var a = Agent;
            bool seller = a.offer == AgentOffer.SellItem || a.offer == AgentOffer.SellBlueprint
                          || a.offer == AgentOffer.KaamoSpecial || a.offer == AgentOffer.ShipDealer || a.offer == AgentOffer.SellShip;
            if (seller) Choices.Add(Choice.Map);
            else if (a.offer == AgentOffer.Mission && a.HasMission && a.mission.type != MissionType.Challenge)
            {
                Choices.Add(Choice.Map);
                Choices.Add(Choice.Risk);
            }
        }

        public static string ChoiceLabel(Agent agent, Choice c) => c switch
        {
            Choice.Okay => T(860),
            Choice.NoThanks => T(861),
            Choice.Repeat => T(862),
            Choice.Map => agent.offer == AgentOffer.Mission || agent.offer == AgentOffer.Purchase ? T(804) : T(776),
            _ => T(807),
        };

        // ---- text -------------------------------------------------------------------------------------------

        /// <summary>The variant ids of the first chat: [greeting, name line, intro, body variant, reward line, question],
        /// -1 = none.</summary>
        List<int> PickTextIds(Agent a)
        {
            int greet = -1, nameLine = -1, intro = -1, body = -1, reward = -1, question = -1;
            bool story = a.IsStory;
            bool challenge = a.HasMission && a.mission.type == MissionType.Challenge;
            if (story) greet = 874 + Random.Range(0, 2);
            else if (a.offer != AgentOffer.SmallTalk && a.offer != AgentOffer.Diplomat && !challenge)
            {
                greet = 750 + Random.Range(0, 6);
                nameLine = 756 + Random.Range(0, 2);
            }
            if (!story && (a.offer == AgentOffer.Mission && !challenge || a.offer == AgentOffer.Purchase)) intro = 758 + Random.Range(0, 6);
            switch (a.offer)
            {
                case AgentOffer.Mission: reward = 764 + Random.Range(0, 3); break;
                case AgentOffer.SmallTalk: body = PickSmallTalk(a); break;
                case AgentOffer.SellItem:
                case AgentOffer.SellShip: body = 768 + Random.Range(0, 5); reward = 773 + Random.Range(0, 2); break;
                case AgentOffer.Purchase: body = 777 + Random.Range(0, 2); break;
            }
            if (a.offer != AgentOffer.SmallTalk && a.offer != AgentOffer.Diplomat) question = 841 + Random.Range(0, 3);
            return new List<int> { greet, nameLine, intro, body, reward, question };
        }

        /// <summary>820-840 without repeats in one visit; 836 only from Terrans, 833 only from men (otherwise 824).</summary>
        int PickSmallTalk(Agent a)
        {
            int line = 0;
            for (int i = 0; i < 100; i++) { line = Random.Range(0, 21); if (!smallTalkUsed.Contains(line)) break; }
            smallTalkUsed.Add(line);
            if (line == 16 && a.race != 0) line = 4;
            if (line == 13 && !a.male) line = 4;
            return 820 + line;
        }

        string StationName(int s) => db.Stations.Find(x => x.index == s)?.name ?? "";
        string SystemName(int s) => db.Systems.Find(x => x.index == s)?.name ?? "";

        /// <summary>SpaceLounge::startChat / Globals::getAgentMissionText 0xfa7d4: the offer text from its ids.</summary>
        public string Compose(Agent a, List<int> ids)
        {
            int Id(int k) => ids != null && k < ids.Count ? ids[k] : -1;
            var parts = new List<string>();
            if (Id(0) >= 0) parts.Add(T(Id(0)));
            if (Id(1) >= 0) parts.Add(T(Id(1)).Replace("#N", a.name));
            if (Id(2) >= 0) parts.Add(T(Id(2)));
            string head = string.Join(" ", parts);
            string body = Body(a, ids);
            string text = head.Length > 0 && body.Length > 0 ? head + " " + body : head + body;
            if (Id(5) >= 0) text += "\n" + T(Id(5));
            return text;
        }

        string Body(Agent a, List<int> ids)
        {
            int Id(int k) => ids != null && k < ids.Count ? ids[k] : -1;
            switch (a.offer)
            {
                case AgentOffer.Mission:
                    return a.HasMission ? MissionText(a.mission, Id(4)) : "";
                case AgentOffer.SmallTalk:
                {
                    int line = Id(3) >= 0 ? Id(3) : 824;
                    var stations = db.Stations;
                    return T(line).Replace("#S", stations[Random.Range(0, stations.Count)].name).Replace("#N", a.name)
                                  .Replace("#ORE", T(1428 + Random.Range(0, 10)));
                }
                case AgentOffer.SellItem:
                case AgentOffer.KaamoSpecial:
                {
                    int intro = Id(3) >= 0 ? Id(3) : 768, line = Id(4) >= 0 ? Id(4) : 773;
                    string s = T(intro) + "\n" + T(line).Replace("#Q", a.sellQuantity.ToString()).Replace("#P", ItemInfo.ItemName(a.sellItem))
                                                        .Replace("#C", C(a.sellPrice));
                    if (a.sellQuantity > 1) s += " " + T(775).Replace("#C", C(a.sellPrice / a.sellQuantity));
                    return s;
                }
                case AgentOffer.Purchase:
                {
                    var m = a.mission;
                    return T(Id(3) >= 0 ? Id(3) : 777).Replace("#Q", m.amount.ToString()).Replace("#P", ItemInfo.ItemName(m.good)).Replace("#C", C(m.reward));
                }
                case AgentOffer.Wingmen:
                    // With all medals the "fans" pay the player (782-784).
                    return T((Achievements.GotAllMedals ? 782 : 779) + a.wingmen.Count).Replace("#C", C(a.costs)).Replace("#W", a.wingmen.Count > 0 ? a.wingmen[0] : "");
                case AgentOffer.Diplomat:
                {
                    if (!Standing.IsEnemy(a.race)) return T(883);
                    a.costs = AgentGenerator.DiplomatCosts(a.race);
                    int id = a.race switch { 2 => 878, 3 => 880, 0 => 881, _ => 882 };
                    return T(id).Replace("#C", C(a.costs));
                }
                case AgentOffer.SellBlueprint:
                    return StoryLine(a) + " " + T(876).Replace("#N", BlueprintName(a.sellBlueprint)).Replace("#C", C(a.sellPrice));
                case AgentOffer.SellSystem:
                    return StoryLine(a) + " " + T(877).Replace("#S", SystemName(a.sellSystem)).Replace("#C", C(a.sellPrice));
                case AgentOffer.SellMod:
                    return StoryLine(a) + " " + T(879).Replace("#SHIP_NAME", ItemInfo.ShipName(Session.ShipIndex)).Replace("#N", a.name)
                                                      .Replace("#C", C(ModPrice(a)));
                case AgentOffer.SellShip:
                {
                    // Remake: a seller's lines (768-772, 773 / 774) with the ship, and what it costs with the current ship
                    // traded in (the dealer's rule, Hangar.CanBuyShipFor).
                    int intro = Id(3) >= 0 ? Id(3) : 768, line = Id(4) >= 0 ? Id(4) : 773;
                    int net = a.sellPrice - Shop.ShipPrice(db, Session.ShipIndex, station);
                    string trade = net >= 0
                        ? Localization.Extra("loungeShipTradeIn", "With your #SHIP_NAME traded in, that's #C.")
                        : Localization.Extra("loungeShipTradeInBack", "With your #SHIP_NAME traded in, you get #C back.");
                    return T(intro) + "\n" + T(line).Replace("#Q", "1").Replace("#P", ItemInfo.ShipName(a.sellShip)).Replace("#C", C(a.sellPrice))
                         + " " + trade.Replace("#SHIP_NAME", ItemInfo.ShipName(Session.ShipIndex)).Replace("#C", C(Math.Abs(net)));
                }
                case AgentOffer.ShipDealer:
                    // 912 "I have a very unique ship on offer." + (remake) the ship and its price in the item line.
                    return StoryLine(a) + " " + T(773).Replace("#Q", "1").Replace("#P", ItemInfo.ShipName(a.sellShip)).Replace("#C", C(a.sellPrice));
            }
            return "";
        }

        /// <summary>The story agent's own line (886 + index). The mechanics' lines (907-910) name the ship and the mod's gain:
        /// SpaceLounge::startChat fills #SHIP_NAME with the current ship and #N from the table DAT_00254400 (mod, value):
        /// armor +40, cargo +30 (t), 1 slot, handling +20.</summary>
        string StoryLine(Agent a)
        {
            if (a.storyIndex < 0) return "";
            string s = T(886 + a.storyIndex).Replace("#SHIP_NAME", ItemInfo.ShipName(Session.ShipIndex));
            return a.sellMod >= 0 && a.sellMod < ModGain.Length ? s.Replace("#N", ModGain[a.sellMod].ToString()) : s;
        }

        static readonly int[] ModGain = { 40, 30, 1, 20 };

        static string BlueprintName(int bp)
        {
            int item = BlueprintProduct(bp);
            return item >= 0 ? ItemInfo.ItemName(item) : "";
        }

        /// <summary>Agent::getModPricePercentage 0x1a698c: the ship's price x 20 / 30 / 40 / 20 % (mods 0..3).</summary>
        int ModPrice(Agent a)
        {
            int[] pct = { 20, 30, 40, 20 };
            int ship = db.Ship(Session.ShipIndex)?.price ?? 0;
            return a.sellMod >= 0 && a.sellMod < pct.Length ? ship * pct[a.sellMod] / 100 : a.sellPrice;
        }

        /// <summary>786 + type (#P, #Q, #S, #N; Recovery / Salvage + 802) and the reward line with #C = reward + the current
        /// bonus (+ 767 with the bonus percentage). Challenge: 798 only.</summary>
        public string MissionText(FreelanceMission m, int rewardLine)
        {
            if (m.status == -1) return T(803).Replace("#S", StationName(m.target));
            if (m.type == MissionType.Challenge) return T(798).Replace("#C", C(m.reward));
            string s = T(786 + m.type);
            if (m.type == MissionType.Recovery || m.type == MissionType.Salvage) s += " " + T(802);
            string good = m.type == MissionType.Courier ? T(813 + m.good)
                        : m.type == MissionType.Purchase || m.type == MissionType.OreMining ? ItemInfo.ItemName(m.good) : "";
            string where = m.type == MissionType.StolenGoods ? SystemName(db.Stations.Find(x => x.index == m.target)?.system ?? 0) : StationName(m.target);
            s = s.Replace("#P", good).Replace("#Q", m.amount.ToString()).Replace("#S", where).Replace("#N", m.targetName).Replace("#C", C(m.Total));
            if (m.type != MissionType.Purchase)
            {
                s += "\n" + T(rewardLine >= 0 ? rewardLine : 765).Replace("#C", C(m.Total));
                int bonus = m.CurrentBonus;
                if (bonus > 0) s += " " + T(767).Replace("#P", ((int)Math.Round(AgentGenerator.MissionBonus(m.clientRace) * 100f)).ToString());
            }
            return s;
        }

        // ---- choices ----------------------------------------------------------------------------------------

        public Outcome Choose(Choice c)
        {
            var a = Agent;
            switch (c)
            {
                case Choice.NoThanks:
                    Session.OffersDeclined++;
                    return Outcome.Closed;
                case Choice.Repeat:
                    Session.OffersRepeated++;
                    Text = Compose(a, a.textIds);
                    return Outcome.None;
                case Choice.Risk:
                    askedRisk = true;
                    riskShown = true;
                    Text = T(808 + (int)(a.mission.difficulty / 10f * 5f));
                    SetChoices();
                    return Outcome.None;
                case Choice.Map:
                    askedMap = true;
                    if (!IsMissionOffer) { MapTarget = -1; return Outcome.ShowMap; }   // "Let me see it": the item details
                    int target = a.mission.target;
                    if (target == station) { Text = T(805); return Outcome.None; }
                    int sysHere = db.Stations.Find(x => x.index == station)?.system ?? -1;
                    if ((db.Stations.Find(x => x.index == target)?.system ?? -2) == sysHere) { Text = T(806); return Outcome.None; }
                    MapTarget = target;
                    return Outcome.ShowMap;
                default:
                    if (closing) return Outcome.Closed;
                    if (riskShown)
                    {
                        riskShown = false;
                        Text = Compose(a, a.textIds);   // the bonus re-evaluated
                        SetChoices();
                        return Outcome.None;
                    }
                    return Accept();
            }
        }

        Outcome Accept()
        {
            var a = Agent;
            Outcome Refuse(string text) { RefusalText = text; return Outcome.Refused; }
            switch (a.offer)
            {
                case AgentOffer.EventMission:
                {
                    var o = NetEventMissions.OfferOf(a);
                    if (o == null) return Outcome.Closed;
                    string refusal = NetEventMissions.AcceptRefusal(o);
                    if (refusal != null) return Refuse(refusal);
                    ConfirmText = o.reward > 0 ? T(865).Replace("#M", o.title).Replace("#C", C(o.reward))
                        : string.Format(Localization.Extra("mpEventMissionConfirm", "Take the mission {0}?"), o.title);
                    if (NetSquad.InSquad) ConfirmText += " " + Localization.Extra("mpEventMissionSquad", "Your whole squad takes it.");
                    return Outcome.Confirm;
                }
                case AgentOffer.Mission:
                case AgentOffer.Purchase:
                {
                    var m = a.mission;
                    string refusal = Freelance.AcceptRefusal(db, m);
                    if (refusal != null) return Refuse(refusal);
                    int upFront = Freelance.UpFrontCost(m);
                    if (upFront > Session.Credits) return Refuse(T(203).Replace("#C", C(upFront - Session.Credits)));
                    // SpaceLounge::OnTouchEnd 0x1a0360: a generic agent 865 (+ on Extreme "27 Costs: ..."), a story agent 863.
                    if (a.IsStory) ConfirmText = T(863);
                    else
                    {
                        ConfirmText = T(865).Replace("#M", m.Name).Replace("#C", C(m.Total));
                        if (Session.IsExtreme && upFront > 0) ConfirmText += $"\n{T(27)}: {C(upFront)}";
                    }
                    if (Freelance.Active) ConfirmText += " " + T(864);
                    return Outcome.Confirm;
                }
                case AgentOffer.Wingmen:
                    if (Session.Wingmen.Count > 0) return Refuse(T(785));
                    if (Achievements.GotAllMedals) { ConfirmText = T(867).Replace("#C", C(a.costs)); return Outcome.Confirm; }
                    if (a.costs > Session.Credits) return Refuse(T(203).Replace("#C", C(a.costs - Session.Credits)));
                    ConfirmText = T(866).Replace("#Q", (a.wingmen.Count + 1).ToString()).Replace("#C", C(a.costs));
                    return Outcome.Confirm;
                case AgentOffer.Diplomat:
                    if (a.costs > Session.Credits) return Refuse(T(203).Replace("#C", C(a.costs - Session.Credits)));
                    ConfirmText = T(885).Replace("#C", C(a.costs));
                    return Outcome.Confirm;
                case AgentOffer.SellShip:
                {
                    // Remake: the dealer's checks (336 / 329 / 203) at the trade-in price; 873 "Buy ship for #C?".
                    var r = TradeHangar().CanBuyShipFor(a.sellShip, a.sellPrice, out int need);
                    if (r == Hangar.Result.Passengers) return Refuse(T(336));
                    if (r == Hangar.Result.SameShip) return Refuse(T(329));
                    if (r == Hangar.Result.NoCredits) return Refuse(T(203).Replace("#C", C(need)));
                    ConfirmText = T(873).Replace("#C", C(a.sellPrice));
                    return Outcome.ConfirmShip;
                }
            }
            int price = Price(a);
            if (price > Session.Credits) return Refuse(T(203).Replace("#C", C(price - Session.Credits)));
            ConfirmText = a.offer switch
            {
                AgentOffer.SellBlueprint => T(869).Replace("#P", BlueprintName(a.sellBlueprint)).Replace("#C", C(price)),
                AgentOffer.SellSystem => T(870).Replace("#S", SystemName(a.sellSystem)).Replace("#C", C(price)),
                AgentOffer.SellMod => T(871).Replace("#C", C(price)),
                AgentOffer.ShipDealer => T(873).Replace("#C", C(price)),
                _ => T(868).Replace("#Q", a.sellQuantity.ToString()).Replace("#P", ItemInfo.ItemName(a.sellItem)).Replace("#C", C(price)),
            };
            return Outcome.Confirm;
        }

        int Price(Agent a) => a.offer == AgentOffer.SellMod ? ModPrice(a) : a.sellPrice;

        /// <summary>The trades of this station (Hangar, as the hangar window opens it).</summary>
        Hangar TradeHangar() => new Hangar(db, Session.RecentStations.Find(s => s.station == station) ?? new StationStock { station = station });

        /// <summary>Remake, AgentOffer.SellShip after the confirmation: 'keep' = 331 (the old hull to the Kaamo Club, the full
        /// price), else the trade-in (the seller takes the old hull). Null when bought, else the refusal (328 / 203 / 336).</summary>
        public string ConfirmShipTrade(bool keep)
        {
            var a = Agent;
            var h = TradeHangar();
            int need;
            var r = keep ? h.CanKeepAndBuyShipFor(a.sellShip, a.sellPrice, out need) : h.CanBuyShipFor(a.sellShip, a.sellPrice, out need);
            if (r == Hangar.Result.AlreadyStored) return T(328);
            if (r == Hangar.Result.Passengers) return T(336);
            if (r == Hangar.Result.SameShip) return T(329);
            if (r == Hangar.Result.NoCredits) return T(203).Replace("#C", C(need));
            if (!(keep ? h.KeepAndBuyShipFor(a.sellShip, a.sellPrice) : h.BuyShipFor(a.sellShip, a.sellPrice))) return T(858);
            a.accepted = true;
            BoughtShip = true;
            Text = T(850 + Random.Range(0, 3));
            closing = true;
            SetChoices();
            return null;
        }

        /// <summary>"Yes" on the confirmation: 850-852 and the deal.</summary>
        public void Confirm()
        {
            var a = Agent;
            string thanks = T(850 + Random.Range(0, 3));
            switch (a.offer)
            {
                case AgentOffer.EventMission:
                    NetEventMissions.Accept(NetEventMissions.OfferOf(a));   // the server starts it (or says why not)
                    thanks += " " + T(853 + Random.Range(0, 3));
                    break;
                case AgentOffer.Mission:
                case AgentOffer.Purchase:
                    if (!askedRisk) Session.AcceptedBlindRisk++;
                    if (!askedMap) Session.AcceptedBlindMap++;
                    Freelance.Accept(db, a);
                    thanks += " " + (a.mission.type == MissionType.Challenge ? T(856) : T(853 + Random.Range(0, 3)));
                    break;
                case AgentOffer.Wingmen:
                    Session.Credits += Achievements.GotAllMedals ? a.costs : -a.costs;
                    Wingmen.Hire(a);
                    a.accepted = true;
                    break;
                case AgentOffer.Diplomat:
                    Session.Credits -= a.costs;
                    Rehabilitate(a.race);
                    a.accepted = true;
                    break;
                case AgentOffer.SellSystem:
                    Session.Credits -= a.sellPrice;
                    var vis = GalaxyMap.Visibility(db);
                    if (vis != null && a.sellSystem >= 0 && a.sellSystem < vis.Length) vis[a.sellSystem] = true;
                    RevealedSystem = a.sellSystem;
                    a.accepted = true;
                    break;
                case AgentOffer.SellBlueprint:
                    Session.Credits -= a.sellPrice;
                    Session.UnlockedBlueprints.Add(a.sellBlueprint);
                    a.accepted = true;
                    break;
                case AgentOffer.SellMod:
                    Session.Credits -= ModPrice(a);
                    Session.AddShipMod(a.sellMod);
                    break;
                case AgentOffer.ShipDealer:
                    // SpaceLounge::onKeyPress: a bare hull (no equipment, no mods, race 0) into the club's storage.
                    Session.Credits -= a.sellPrice;
                    KaamoClub.Store(a.sellShip, 0, null);
                    a.accepted = true;
                    BoughtShip = true;
                    break;
                default:
                    Session.Credits -= a.sellPrice;
                    GiveItem(a.sellItem, a.sellQuantity);
                    a.accepted = true;
                    break;
            }
            if (a.IsStory && a.accepted && a.offer != AgentOffer.KaamoSpecial && a.offer != AgentOffer.ShipDealer)
                Session.StoryAgentsAccepted.Add(a.storyIndex);
            Text = thanks;
            closing = true;
            SetChoices();
        }

        // ---- voice (SpaceLounge::getSoundId 0x19fdb4 / getSpecificSoundForRace 0x1a0080) ---------------------

        /// <summary>The lounge greeting file for this chat start (LOUNGE_eng / _deu), null = silent (pirates, others).</summary>
        public string VoiceName()
        {
            var a = Agent;
            string set = a.race switch
            {
                0 or 5 => a.male ? "TERRAN_MALE" : "TERRAN_FEMALE",
                1 => "VOSSK",
                2 => "NIVELIAN",
                3 => a.portrait == null || a.portrait[0] == 2 ? "NIVELIAN" : "TERRAN_MALE",
                4 => "MULTIPOD",
                6 => "BOBOLAN",
                7 => "GREY",
                _ => null,
            };
            if (set == null) return null;
            string kind; int count;
            int type = a.HasMission ? a.mission.type : -1;
            switch (a.offer)
            {
                case AgentOffer.Mission:
                    if (type == MissionType.Courier || type == MissionType.Passenger) { kind = "DELIVERY"; count = 4; }
                    else if (type == MissionType.Challenge) { kind = "CHALLENGE"; count = 4; }
                    else { kind = Random.Range(0, 2) == 0 ? "SPECIAL" : "FIGHT"; count = 4; }
                    break;
                case AgentOffer.SmallTalk: kind = "GENERIC"; count = 2; break;
                case AgentOffer.SellItem: case AgentOffer.SellBlueprint: case AgentOffer.SellMod:
                case AgentOffer.KaamoSpecial: case AgentOffer.ShipDealer: case AgentOffer.SellShip: kind = "BLUEPRINT"; count = 2; break;
                case AgentOffer.SellSystem: kind = "COORDINATES"; count = 2; break;
                case AgentOffer.Purchase: kind = "PRODUCTION"; count = 4; break;
                case AgentOffer.Wingmen: kind = "WINGMAN"; count = 4; break;
                case AgentOffer.Diplomat: kind = "DIPLOMAT"; count = 4; break;
                case AgentOffer.EventMission: kind = "SPECIAL"; count = 4; break;
                default: return null;
            }
            if (a.offer != AgentOffer.SmallTalk && Random.Range(0, 100) < 30) { kind = "GENERIC"; count = 2; }
            if (a.accepted) { kind = "GENERIC"; count = 2; }
            if (a.offer == AgentOffer.SmallTalk && a.textIds.Count > 3)
            {
                int line = a.textIds[3];
                if (line == 820 || line == 824 || line == 827 || line == 833) { kind = "GENERIC_NEG"; count = 2; }
            }
            return $"{set}_GREETING_LOUNGE_{kind}_{Random.Range(1, count + 1):00}";
        }

        /// <summary>drawLounge's hover label: a generic agent never talked to shows its race; otherwise the name and, for known
        /// agents, the role (the mission type, 306 Wingmen, 305 Merchant, 884 Diplomat).</summary>
        public static (string name, string role) Plate(Agent a)
        {
            if (a.offer == AgentOffer.EventMission) return (a.name, NetEventMissions.OfferOf(a)?.title ?? "");   // always says what it offers
            if (!a.known && !a.IsStory) return (T(406 + a.race), "");
            if (!a.known) return (a.name, "");
            string role = a.HasMission ? a.mission.Name : a.offer == AgentOffer.Wingmen ? T(306)
                        : a.offer == AgentOffer.SellItem || a.offer == AgentOffer.SellShip ? T(305)
                        : a.offer == AgentOffer.Diplomat ? T(884) : "";
            return (a.name, role);
        }

        /// <summary>Standing::rehabilitate 0x14289c: the race's axis just inside the neutral band (+-35).</summary>
        static void Rehabilitate(int race)
        {
            if (race == 0) Session.Standing[0] = -35;
            else if (race == 1) Session.Standing[0] = 35;
            else if (race == 2) Session.Standing[1] = -35;
            else if (race == 3) Session.Standing[1] = 35;
        }

        /// <summary>Bought items go to the hold; secondaries join a mounted stack of the same item.</summary>
        void GiveItem(int item, int amount)
        {
            if (Session.IsBooze(item)) Session.BoozeTypes.Add(item);   // SpaceLounge::onKeyPress: Status+0xac
            var it = db.Item(item);
            if (it != null && it.TypeId == 1)
            {
                var mounted = Session.Equipment.Find(e => e.item == item);
                if (mounted != null) { mounted.amount += amount; return; }
            }
            Shop.AddToCargo(item, amount);
        }
    }
}
