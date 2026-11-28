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

        // A shared renewal covers all recruited groups, priced from their original contracts.
        // Older saves have no prices; use a modest estimate per surviving pilot.
        static void EnsureContractPrices()
        {
            if (Session.WingmanContractPrices.Count == 0 && Hired)
                Session.WingmanContractPrices.Add(Session.Wingmen.Count * 1350 * (Session.IsExtreme ? 7 : 1));
        }

        public static long ExtensionPrice
        {
            get
            {
                EnsureContractPrices();
                long total = 0;
                foreach (int price in Session.WingmanContractPrices) if (price > 0) total += price;
                return System.Math.Max(1L, (total * Modding.ModWingmen.ExtensionPricePercent + 99) / 100);
            }
        }

        public static bool TryExtend()
        {
            if (!Modding.ModWingmen.ExtendContracts || !Expired) return false;
            long price = ExtensionPrice;
            if (Session.Credits < price) return false;
            Session.Credits -= price;
            Session.WingmanContractMs = Modding.ModWingmen.ExtensionSeconds * 1000f;
            return true;
        }

        public static bool CanHire(Agent agent) => agent != null && (!Hired || Modding.ModWingmen.MultipleGroups && !Expired) &&
            Session.Wingmen.Count + agent.wingmen.Count + 1 <= Modding.ModWingmen.MaxTotal;

        /// <summary>setWingmen(names), race, contract 600 000 ms, the agent's portrait, hired += friends + 1.</summary>
        public static void Hire(Agent agent)
        {
            var names = new List<string> { agent.name };
            names.AddRange(agent.wingmen);
            if (!CanHire(agent)) return;
            bool append = Hired;
            if (append) EnsureContractPrices();
            else Session.WingmanContractPrices.Clear();
            if (!append)
            {
                Session.Wingmen.Clear(); Session.WingmanShips.Clear(); Session.WingmanRaces.Clear();
                Session.WingmanRace = agent.race;
                Session.WingmanContractMs = ContractMs;
                Session.WingmanPortrait = agent.portrait != null ? (int[])agent.portrait.Clone() : new int[5];
            }
            while (Session.WingmanShips.Count < Session.Wingmen.Count) Session.WingmanShips.Add(-1);
            while (Session.WingmanRaces.Count < Session.Wingmen.Count) Session.WingmanRaces.Add(Session.WingmanRace);
            for (int i = 0; i < names.Count; i++)
            {
                Session.Wingmen.Add(names[i]);
                Session.WingmanShips.Add(agent.wingmanShips != null && i < agent.wingmanShips.Count ? agent.wingmanShips[i] : -1);
                Session.WingmanRaces.Add(agent.race);
            }
            Session.WingmanContractPrices.Add(System.Math.Max(1, agent.costs));
            Session.WingmenHired += names.Count;
        }

        /// <summary>Level::wingmanDied: the name leaves the list; the last one ends the contract.</summary>
        public static void Died(string name)
        {
            int i = Session.Wingmen.IndexOf(name);
            if (i >= 0 && i < Session.WingmanShips.Count) Session.WingmanShips.RemoveAt(i);
            if (i >= 0 && i < Session.WingmanRaces.Count) Session.WingmanRaces.RemoveAt(i);
            Session.Wingmen.Remove(name);
            if (Session.Wingmen.Count == 0) Dismiss();
        }

        /// <summary>setWingmen(null): names, portrait and contract cleared.</summary>
        public static void Dismiss()
        {
            Session.Wingmen = new List<string>();
            Session.WingmanShips = new List<int>();
            Session.WingmanRaces = new List<int>();
            Session.WingmanContractMs = 0f;
            Session.WingmanContractPrices.Clear();
        }
    }
}
