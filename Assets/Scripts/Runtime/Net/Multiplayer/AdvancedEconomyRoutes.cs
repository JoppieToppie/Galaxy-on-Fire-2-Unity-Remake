using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;

namespace GoF2Remake.Multiplayer
{
    // Rebuilt once per economy tick, never per frame. Static links retain vanilla
    // directionality conventions; public player-built gates are directional.
    public sealed class AdvancedEconomyRoutes
    {
        readonly Dictionary<int, SortedSet<int>> edges = new Dictionary<int, SortedSet<int>>();
        readonly Dictionary<int, Dictionary<int, int>> parents = new Dictionary<int, Dictionary<int, int>>();
        readonly Dictionary<int, Dictionary<int, int>> distances = new Dictionary<int, Dictionary<int, int>>();

        public void Add(int from, int to)
        {
            if (!edges.TryGetValue(from, out var set)) edges[from] = set = new SortedSet<int>();
            set.Add(to);
            parents.Clear();
            distances.Clear();
        }

        public static AdvancedEconomyRoutes Build(Database db)
        {
            var graph = new AdvancedEconomyRoutes();
            foreach (var system in db.Systems)
                if (system?.jumpRoutesTo != null)
                    foreach (int to in system.jumpRoutesTo)
                    {
                        graph.Add(system.index, to);
                        graph.Add(to, system.index);
                    }
            if (ModAdvancedEconomy.PlayerGateRouting)
                foreach (var edge in NetPlayerStations.PublicEconomyGateRoutes())
                    graph.Add(edge.from, edge.to);
            return graph;
        }

        public int Distance(int from, int to)
        {
            if (from == to) return 0;
            EnsurePaths(from);
            return distances[from].TryGetValue(to, out int hops) ? hops : -1;
        }

        void EnsurePaths(int from)
        {
            if (!parents.TryGetValue(from, out var previous))
            {
                parents[from] = previous = new Dictionary<int, int> { [from] = from };
                var distance = distances[from] = new Dictionary<int, int> { [from] = 0 };
                var queue = new Queue<int>();
                queue.Enqueue(from);
                while (queue.Count != 0)
                {
                    int here = queue.Dequeue();
                    if (!edges.TryGetValue(here, out var next)) continue;
                    foreach (int there in next)
                    {
                        if (previous.ContainsKey(there)) continue;
                        previous.Add(there, here);
                        distance.Add(there, distance[here] + 1);
                        queue.Enqueue(there);
                    }
                }
            }
        }

        public List<int> Path(int from, int to)
        {
            if (from == to) return new List<int> { from };
            EnsurePaths(from);
            var previous = parents[from];
            if (!previous.ContainsKey(to)) return null;
            var path = new List<int> { to };
            while (to != from) { to = previous[to]; path.Add(to); }
            path.Reverse();
            return path;
        }
    }
}
