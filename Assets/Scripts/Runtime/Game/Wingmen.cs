// Wingmen.cs
// Hired wingmen (SpaceLounge::onKeyPress offer 6, Level::createWingmen / wingmanDied, ModStation::checkHints;
// Reference/research/wingmen_wanted.md 1): the agent and its 0-2 friends fly with the player (Traffic.SpawnWingmen,
// NpcShip wingman mode). The 600 000 ms contract runs down only while flying; at 0 they keep flying until the next
// docking, where they say goodbye (313) and leave. A dead wingman leaves the contract for good. Plain C#.

using System.Collections.Generic;

namespace GoF2Remake.Data
{
    public static class Wingmen
    {
        public const float ContractMs = 600000f;

        public static bool Hired => Session.Wingmen.Count > 0;
        public static bool Expired => Hired && Session.WingmanContractMs < 1f;

        /// <summary>setWingmen(names), race, contract 600 000 ms, the agent's portrait, hired += friends + 1.</summary>
        public static void Hire(Agent agent)
        {
            var names = new List<string> { agent.name };
            names.AddRange(agent.wingmen);
            Session.Wingmen = names;
            Session.WingmanShips = agent.wingmanShips != null ? new List<int>(agent.wingmanShips) : new List<int>();
            Session.WingmanRace = agent.race;
            Session.WingmanContractMs = ContractMs;
            Session.WingmanPortrait = agent.portrait != null ? (int[])agent.portrait.Clone() : new int[5];
            Session.WingmenHired += names.Count;
        }

        /// <summary>Level::wingmanDied: the name leaves the list; the last one ends the contract.</summary>
        public static void Died(string name)
        {
            int i = Session.Wingmen.IndexOf(name);
            if (i >= 0 && i < Session.WingmanShips.Count) Session.WingmanShips.RemoveAt(i);
            Session.Wingmen.Remove(name);
            if (Session.Wingmen.Count == 0) Dismiss();
        }

        /// <summary>setWingmen(null): names, portrait and contract cleared.</summary>
        public static void Dismiss()
        {
            Session.Wingmen = new List<string>();
            Session.WingmanShips = new List<int>();
            Session.WingmanContractMs = 0f;
        }
    }
}
