// WingmanCrew.cs
// Remake mods (wingmen.json, Modding.ModWingmen; Modding/README.md "Wingmen"): one mod's wingmen. 'chance' % of the bars'
// wingmen offers (AgentGenerator.CreateAgent) fly its ships, those of the agent's race when it has any; a hired wingman in
// one of them says its radio lines (World.NpcShip: when it kills, when another one is lost, when it is in trouble, when the
// player shot what was after it, when an enemy is on the player's tail, when the player is down). Plain C#.

using System.Collections.Generic;

namespace GoF2Remake.Data
{
    public class WingmanCrew
    {
        public const string Kill = "kill", Lost = "lost", Help = "help", Thanks = "thanks", Warn = "warn", PlayerDown = "playerDown";
        public static readonly string[] LineKinds = { Kill, Lost, Help, Thanks, Warn, PlayerDown };

        public Modding.ModInfo mod;
        public int chance = 40;
        /// <summary>The ships its crews fly (ship indices; the mod's ships with a "wingmanDevice" unless "ships" names them).</summary>
        public List<int> ships = new List<int>();
        public readonly Dictionary<string, List<string>> lines = new Dictionary<string, List<string>>();

        /// <summary>One line of that kind at random, null = none.</summary>
        public string Line(string kind, System.Func<int, int> random)
        {
            if (!lines.TryGetValue(kind, out var l) || l.Count == 0) return null;
            return l[random(l.Count)];
        }

        /// <summary>The ships for a crew of 'count' of 'race' (its own race's when it has any, else all of them), picked with
        /// 'random' (n -> 0..n-1); empty when it has none.</summary>
        public List<int> Pick(int race, int count, System.Func<int, int> random)
        {
            var pool = ships.FindAll(s => Shop.RaceOfShip(s) == race);
            if (pool.Count == 0) pool = new List<int>(ships);
            var picked = new List<int>();
            if (pool.Count == 0) return picked;
            for (int i = 0; i < count; i++) picked.Add(pool[random(pool.Count)]);
            return picked;
        }
    }
}
