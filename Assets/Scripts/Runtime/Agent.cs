// Agent.cs
// Bar agents and freelance missions as plain data (Reference/research/freelance_missions.md 1-2):
//   Agent             Agent (game/Agent.c, 0x88 bytes): a bar visitor, generic (Generator::createAgent) or a story
//                         agent from agents.json, with its offer (AgentOffer) and, for offer 0 / 5, its mission
//   FreelanceMission  Mission (game/Mission.c, 100 bytes): type 0..15, client, target station, difficulty, the
//                         "production good" and amount, reward and standing bonus; type -1 = Mission::empty
//   AgentData         what the agents are made of: names.json (Globals::getRandomName), agents.json (story agents),
//                         the portrait part counts of ImageFactory::createChar
// Agents live on the station entry of the 3-station stack (StationStock.agents) like the original's Station
// objects, so they persist while the station stays among the last 3 visited. Serialised with the save game.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GoF2Remake.Data
{
    /// <summary>Agent+0x54 offer codes.</summary>
    public static class AgentOffer
    {
        public const int Mission = 0, SmallTalk = 1, SellItem = 2, SellBlueprint = 3, SellSystem = 4, Purchase = 5,
                         Wingmen = 6, Diplomat = 7, SellMod = 8, KaamoSpecial = 9, ShipDealer = 10,
                         EventMission = 11;   // remake multiplayer: an event graph's bar mission (NetEventMissions)
    }

    /// <summary>Mission types (Mission::getName = text 354 + type).</summary>
    public static class MissionType
    {
        public const int Empty = -1, Courier = 0, Defense = 1, Protection = 2, Recovery = 3, PirateHunting = 4, Salvage = 5,
                         Wanted = 6, JunkRemoval = 7, Purchase = 8, Escort = 9, Intercept = 10, Passenger = 11, Challenge = 12,
                         Informer = 13, StolenGoods = 14, OreMining = 15;

        public static string Name(int type) => type >= 0 && type <= 15 ? Localization.Get(354 + type) : "";
    }

    [Serializable]
    public class FreelanceMission
    {
        public int type = MissionType.Empty;
        public bool won, failed;
        public string clientName = "";
        public int clientRace;
        public bool clientMale = true;
        public int[] clientPortrait = new int[5];
        /// <summary>The agent's station (where it was offered; Recovery / Salvage return there, Challenge flies there).</summary>
        public int clientStation = -1;
        public int target = -1;
        public int difficulty = 1;
        /// <summary>Mission+0x54 / +0x58: the "production good" (Courier cargo description 813 + good, Purchase item,
        /// Recovery / Salvage container item) and its amount (containers, ships, passengers, items).</summary>
        public int good, amount;
        public int reward, bonus, costs;
        /// <summary>Mission+0x14: the Wanted type's target name.</summary>
        public string targetName = "";
        /// <summary>Mission+0x5c status value (-1 once Recovery / Salvage turned into the return trip).</summary>
        public int status;
        /// <summary>The offer text as the agent said it (text ids, rebuilt with the current values by LoungeChat).</summary>
        public List<int> textIds = new List<int>();
        /// <summary>Multiplayer (NetMissions): the squad's id for this mission, the same for every member (0 = single player /
        /// not shared).</summary>
        public long netId;

        public bool IsEmpty => type < 0;
        public string Name => MissionType.Name(type);
        public FreelanceMission Clone() => (FreelanceMission)MemberwiseClone();

        /// <summary>Mission::getReward + the standing bonus, recomputed with the current standing like SpaceLounge::startChat
        /// (types 8 and 12 get none).</summary>
        public int CurrentBonus
        {
            get
            {
                if (type == MissionType.Purchase || type == MissionType.Challenge) return 0;
                return AgentGenerator.Round50(reward * AgentGenerator.MissionBonus(clientRace));
            }
        }
        public int Total => reward + CurrentBonus;
    }

    [Serializable]
    public class Agent
    {
        public string name = "";
        public int race, station = -1;
        public bool male = true;
        /// <summary>Agent+0x38: agents.json index of a story agent, -1 = generic.</summary>
        public int storyIndex = -1;
        public int offer;
        /// <summary>Agent+0x4c &gt; 0: talked to before (isKnown).</summary>
        public bool known;
        /// <summary>Agent+0x74.</summary>
        public bool accepted;
        public int[] portrait = new int[5];
        /// <summary>Agent+0x2c..0x34: item / quantity / price of a seller (offer 2, 9), the story seller's price.</summary>
        public int sellItem = -1, sellQuantity, sellPrice;
        /// <summary>Agent+0x5c / +0x60 / +0x84: a story agent's system coordinates / blueprint / mod for sale.</summary>
        public int sellSystem = -1, sellBlueprint = -1, sellMod = -1;
        /// <summary>Offer 10 (agent 26, the Kaamo Club's dealer) and 11 (a custom ship's seller): the ship on offer (-1 = none
        /// left); its price in sellPrice.</summary>
        public int sellShip = -1;
        /// <summary>Agent+0x58: wingmen / diplomat price.</summary>
        public int costs;
        /// <summary>Agent+0x08..0x10: the wingman friends' names.</summary>
        public List<string> wingmen = new List<string>();
        public FreelanceMission mission = new FreelanceMission();
        /// <summary>Agent+0x20 / +0x24 and the stored mission string: the text ids of the offer as first said.</summary>
        public List<int> textIds = new List<int>();

        public bool IsStory => storyIndex >= 0;
        public bool HasMission => mission != null && !mission.IsEmpty;
    }

    /// <summary>A story agent row of agents.json.</summary>
    [Serializable]
    public class StoryAgentData
    {
        public int index;
        public string name;
        public int station, system, race;
        public bool male;
        public int sellItemSystem = -1, sellBlueprint = -1, sellMod = -1, sellItemPrice;
        public int[] portraitParts = new int[5];
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class AgentData
    {
        static Dictionary<string, List<string>> names;
        static List<StoryAgentData> story;

        [Serializable] class StoryWrapper { public List<StoryAgentData> list; }

        /// <summary>names.json: list name -> names (bobolan_0, terran_0_m, terran_1 ...).</summary>
        public static Dictionary<string, List<string>> Names
        {
            get
            {
                if (names != null) return names;
                names = new Dictionary<string, List<string>>();
                var ta = Resources.Load<TextAsset>("GoF2Data/names");
                if (ta == null) return names;
                // A flat { "key": ["a", "b"] } file; JsonUtility can't read dictionaries.
                foreach (Match m in Regex.Matches(ta.text, "\"([a-z0-9_]+)\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline))
                {
                    var list = new List<string>();
                    foreach (Match s in Regex.Matches(m.Groups[2].Value, "\"((?:[^\"\\\\]|\\\\.)*)\"")) list.Add(Regex.Unescape(s.Groups[1].Value));
                    names[m.Groups[1].Value] = list;
                }
                return names;
            }
        }

        public static List<StoryAgentData> StoryAgents
        {
            get
            {
                if (story != null) return story;
                var ta = Resources.Load<TextAsset>("GoF2Data/agents");
                story = ta != null ? JsonUtility.FromJson<StoryWrapper>("{\"list\":" + ta.text + "}").list : new List<StoryAgentData>();
                return story ?? (story = new List<StoryAgentData>());
            }
        }

        /// <summary>ImageFactory::createChar 0x14173c part counts per set (0x258320): parts[1..4] = nextInt(count).</summary>
        public static readonly int[][] PortraitPartCounts =
        {
            new[] { 11, 11, 11, 11 }, new[] { 4, 5, 6, 9 }, new[] { 5, 5, 5, 5 }, new[] { 1, 1, 1, 1 }, new[] { 3, 3, 5, 4 },
            new[] { 1, 1, 1, 1 }, new[] { 2, 3, 5, 0 }, new[] { 2, 2, 3, 2 }, new[] { 1, 1, 1, 1 }, new[] { 1, 1, 1, 1 },
            new[] { 4, 4, 5, 7 },
        };
    }
}
