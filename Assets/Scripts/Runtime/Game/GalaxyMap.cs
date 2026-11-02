// GalaxyMap.cs
// The star map's rules as plain C# (Reference/research/starmap_travel.md, helper Reference/tools/starmap/starmap_tables.py):
//   StarMap::StarMap 0xd6880        galaxy-view sun position per system (integer-truncated game units)
//   SolarSystem::systemIsInSystemRoutes 0x180c86   which systems the gate map can enter (the current one or a gate route)
//   SystemPathFinder 0x14062e..     BFS over jumpRoutesTo, only into visible systems; energy cells = gate jumps, 4 when the
//                                   gates can't reach it, x2 in hardcore (StarMap::OnTouchBegin). Remake: A* over the same
//                                   graph (the same jump counts; among routes of equal length it may take another), which
//                                   also finds the jumpgate route planner's safest route (RoutePath)
//   StarMap::initStarSystem 0xd7810 system-view layout: java.util.Random(system * 1000), a free slot per station, radius
//                                   6400 + rand(5600) + 1600 (+ rand(5600) + 1600 per next planet), then one orbit yaw each
//   Ship::hasJumpDrive 0x1a39a4     item 85 fitted, or the integrated drive of ships 37 Cronus, 38 Typhon, 40 Nemesis
// Remake-only: the campaign takes the player out of gateless Mido; without it a new game would be stuck there, so in free
// play a ship in a system without a jumpgate counts as having a Khador Drive (it still needs energy cells).

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GalaxyMap
    {
        public const int KhadorDriveItem = 85, EnergyCellItem = 122;
        public const int ChargeMs = 5000;   // item 85 attr 37

        /// <summary>DAT_00254484: map planet scale per station texture index (x16 / 65536).</summary>
        static readonly int[] PlanetScale = { 320, 192, 256, 256, 192, 256, 192, 192, 320, 256, 192, 192, 320, 256, 320, 256, 256, 256, 320,
                                              192, 320, 256, 256, 0, 256, 256, 192, 256 };

        /// <summary>DAT_0025451c: security colours, 0 Dangerous .. 3 Secure (texts 402..405).</summary>
        public static readonly Color32[] SecurityColours = { new Color32(255, 42, 0, 255), new Color32(255, 108, 0, 255), new Color32(237, 237, 0, 255), new Color32(0, 237, 0, 255) };

        public struct Planet
        {
            public int station, slot, angle, radius, textureIndex;
            public float scale, orbitYaw;
            public bool ring, gate;
        }

        /// <summary>Status::getSystemVisibilities, set up from systems.json initiallyVisible on first use, plus Loma (25):
        /// Status::resetGame shows it whenever the Valkyrie add-on is owned (options[0x35]), and both add-ons are owned in
        /// the remake, so the black market is on the map from a new game's start.</summary>
        public static bool[] Visibility(Database db)
        {
            int n = 0;
            foreach (var s in db.Systems) n = Mathf.Max(n, s.index + 1);
            if (Session.SystemVisible == null || Session.SystemVisible.Length < n)
            {
                // A save made with fewer systems (a mod's systems added since) keeps what it had revealed; only the new
                // systems start from initiallyVisible (before, the whole map was rebuilt and the story's reveals were lost).
                var old = Session.SystemVisible;
                var v = new bool[n];
                foreach (var s in db.Systems) v[s.index] = old != null && s.index < old.Length ? old[s.index] : s.initiallyVisible;
                if (LomaSystem < n) v[LomaSystem] = true;
                Session.SystemVisible = v;
            }
            // Remake multiplayer: the finished game's map, every system the campaigns reveal (Story.RevealedSystems).
            if (Session.CompletedWorld)
                foreach (int s in Story.RevealedSystems)
                    if (s < Session.SystemVisible.Length) Session.SystemVisible[s] = true;
            // Remake mods: a mod campaign with its own galaxy hides every other system (Modding.ModCampaigns).
            if (Modding.ModCampaigns.ModGalaxy)
                foreach (var s in db.Systems)
                    if (s.index < Session.SystemVisible.Length && !Modding.ModCampaigns.SystemAllowed(s.index)) Session.SystemVisible[s.index] = false;
            return Effective(Session.SystemVisible);
        }

        /// <summary>Status::setSystemVisibility(system, true): the system revealed for good (bought coordinates, a story step).
        /// Into Session.SystemVisible, never Visibility's result: that is a copy while an event, a mod's condition or a missing
        /// mod system overrides the map, and a reveal written into it was lost (Discord report "Bug with buying a hidden
        /// systems": Suteo's and Y'mirr's coordinates bought, the systems stayed hidden; the story's reveals still showed).</summary>
        public static void Reveal(Database db, int system)
        {
            Visibility(db);
            if (system >= 0 && system < Session.SystemVisible.Length) Session.SystemVisible[system] = true;
        }

        static bool[] effective;

        /// <summary>Remake mods: the map as shown now: the revealed systems (Session.SystemVisible, what is saved), each system an
        /// event / quest showed or hid (Session.SystemShown) and each with a mod's "available" condition (ModUnlocks) as they
        /// say. A copy, so hiding a system for a while doesn't forget that it was revealed; Session.SystemVisible itself when
        /// nothing overrides it. A placeholder of a mod that is off or gone (Modding.ModWorld.IsMissingSystem: "?" at -1000 on
        /// every axis, its number kept by the mod registry) is never shown, revealed or not: the map's overview zoomed out to
        /// frame it, also on games with no mod installed any more.</summary>
        static bool[] Effective(bool[] revealed)
        {
            bool missing = false;
            for (int i = 0; i < revealed.Length && !missing; i++) missing = revealed[i] && Modding.ModWorld.IsMissingSystem(i);
            if (Session.SystemShown.Count == 0 && !Modding.ModUnlocks.AnySystemConditions && !missing) return revealed;
            if (effective == null || effective.Length != revealed.Length) effective = new bool[revealed.Length];
            System.Array.Copy(revealed, effective, revealed.Length);
            for (int i = 0; i < effective.Length; i++)
            {
                if (Forced(i) is bool shown) effective[i] = shown;
                if (missing && Modding.ModWorld.IsMissingSystem(i)) effective[i] = false;
            }
            return effective;
        }

        /// <summary>A system shown (true) or hidden (false) whatever was revealed: an event's / quest's "system" command first,
        /// then a mod's "available" condition; null = the map's own state.</summary>
        public static bool? Forced(int system) =>
            Session.SystemShown.TryGetValue(system, out bool shown) ? shown : Modding.ModUnlocks.SystemAvailable(system);

        /// <summary>The system is hidden on purpose now (a "system ... hide" or an "available" condition that doesn't hold): a
        /// player in it is taken out (SpaceLevel).</summary>
        public static bool ForcedHidden(int system) => Forced(system) == false;

        /// <summary>Where a player goes when the system they are in disappears: the nearest system on the map along the
        /// jumpgate routes (its gate station, else its first station), else the station last docked at when its system is on
        /// the map, else the first visible system's, else Var Hastra.</summary>
        public static int FallbackStation(Database db, int system)
        {
            var visible = Visibility(db);
            bool Shown(int s) => s >= 0 && s < visible.Length && visible[s];
            int StationIn(int s)
            {
                var sys = db.Systems.Find(x => x.index == s);
                if (sys?.stations == null || sys.stations.Count == 0) return -1;
                return sys.stations.Contains(sys.jumpgateStation) ? sys.jumpgateStation : sys.stations[0];
            }
            var seen = new HashSet<int> { system };
            var queue = new Queue<int>();
            queue.Enqueue(system);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                var sys = db.Systems.Find(x => x.index == at);
                if (sys?.jumpRoutesTo == null) continue;
                foreach (int n in sys.jumpRoutesTo)
                {
                    if (!seen.Add(n)) continue;
                    if (Shown(n) && StationIn(n) is int st && st >= 0) return st;
                    queue.Enqueue(n);
                }
            }
            int last = Session.LastDockedStation;
            if (last >= 0 && db.Stations.Find(x => x.index == last) is StationData ls && ls.system != system && Shown(ls.system)) return last;
            foreach (var s in db.Systems)
                if (s.index != system && Shown(s.index) && StationIn(s.index) is int st && st >= 0) return st;
            // Var Hastra only when Mido is shown (a mod campaign's galaxy can leave it out): -1 = nowhere to go.
            return db.Stations.Find(x => x.index == 78) is StationData vh && Shown(vh.system) ? 78 : -1;
        }

        /// <summary>An event's / quest's "system &lt;system&gt; show | hide | reset" (NetAdmin Order.System): 'shown' true / false
        /// over the map until reset (null).</summary>
        public static void SetShown(int system, bool? shown)
        {
            if (shown is bool s) Session.SystemShown[system] = s;
            else Session.SystemShown.Remove(system);
        }

        public const int LomaSystem = 25, ShimaSystem = 26;

        public static bool IsVisible(Database db, int system)
        {
            var v = Visibility(db);
            return system >= 0 && system < v.Length && v[system];
        }

        /// <summary>SolarSystem::hasNoOwner: no race icon, race line or logo.</summary>
        /// <summary>StarMap::drawOnScreenInfo: Shima reads 3 "Secure" once the Kaamo Club's siege is over (Status+0x114 &gt; 1).</summary>
        public static int SecurityOf(SystemData s) =>
            s == null ? 0 : s.index == KaamoClub.SystemIndex && Session.KaamoState > 1 ? 3 : s.securityLevel;

        public static bool HasOwner(int system) => system != 23 && system != 24 && system != 26 && system != 32 && system != 33;

        /// <summary>The galaxy-view sun sprite position (game units).</summary>
        public static Vector3 SunPosition(SystemData s)
        {
            var p = s.mapPosition;
            return new Vector3((int)((100 - p.x) / 100.0 * 14000.0) - 10000,
                               (int)((100 - p.y) / 100.0 * 13000.0) - 9000,
                               (int)((100 - p.z) / 100.0 * 6000.0) + 1000);
        }

        public static bool IsInRoutes(SystemData from, int to) => from != null && (from.index == to || from.jumpRoutesTo.Contains(to));

        /// <summary>SystemPathFinder::getSystemPath: [a .. b] through visible systems with the fewest gate jumps, null if
        /// unreachable or a == b.</summary>
        public static List<int> SystemPath(Database db, int a, int b) => RoutePath(db, a, b, false);

        /// <summary>Remake (jumpgate route planning): [a .. b] through visible systems by A*, null if unreachable or a == b.
        /// Every jump costs 1; 'safest' adds 1 per security level below Secure of the system it enters (JumpCost), so the
        /// route goes round a Dangerous system for up to three more jumps.</summary>
        public static List<int> RoutePath(Database db, int a, int b, bool safest)
        {
            if (a == b) return null;
            var visible = Visibility(db);
            var systems = new Dictionary<int, SystemData>();
            foreach (var s in db.Systems) systems[s.index] = s;
            if (!systems.TryGetValue(b, out var goal)) return null;
            // The heuristic: the map distance left over the longest gate link. No jump covers more and none costs less than 1,
            // so it never overestimates (and is consistent: a closed system is never reopened).
            float longest = 0f;
            foreach (var s in db.Systems)
                foreach (int m in s.jumpRoutesTo)
                    if (systems.TryGetValue(m, out var t)) longest = Mathf.Max(longest, MapDistance(s, t));
            float H(int n) => longest > 0f && systems.TryGetValue(n, out var s) ? MapDistance(s, goal) / longest : 0f;

            var cost = new Dictionary<int, float> { [a] = 0f };
            var prev = new Dictionary<int, int> { [a] = -1 };
            var open = new List<int> { a };
            var closed = new HashSet<int>();
            while (open.Count > 0)
            {
                // the lowest cost + heuristic; ties: the nearer the target, then the lower index (the same route every time)
                int pick = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    float fi = cost[open[i]] + H(open[i]), fp = cost[open[pick]] + H(open[pick]);
                    if (fi < fp - 1e-4f || (Mathf.Abs(fi - fp) <= 1e-4f && (H(open[i]) < H(open[pick]) - 1e-4f
                        || (Mathf.Abs(H(open[i]) - H(open[pick])) <= 1e-4f && open[i] < open[pick])))) pick = i;
                }
                int n = open[pick];
                open.RemoveAt(pick);
                if (n == b)
                {
                    var path = new List<int> { b };
                    while (prev[path[path.Count - 1]] >= 0) path.Add(prev[path[path.Count - 1]]);
                    path.Reverse();
                    return path;
                }
                closed.Add(n);
                if (!systems.TryGetValue(n, out var sys)) continue;
                foreach (int m in sys.jumpRoutesTo)
                {
                    if (m < 0 || m >= visible.Length || !visible[m] || closed.Contains(m) || !systems.TryGetValue(m, out var next)) continue;
                    float c = cost[n] + JumpCost(next, safest);
                    if (cost.TryGetValue(m, out var known) && known <= c) continue;
                    cost[m] = c;
                    prev[m] = n;
                    if (!open.Contains(m)) open.Add(m);
                }
            }
            return null;
        }

        /// <summary>A gate jump into 'to': 1, and on the safest route 1 more per security level below Secure (Dangerous 4, Risky
        /// 3, Average 2, Secure 1; SecurityOf, so the Kaamo Club's system once the siege is over is Secure).</summary>
        public static float JumpCost(SystemData to, bool safest) => safest ? 1f + (3 - Mathf.Clamp(SecurityOf(to), 0, 3)) : 1f;

        /// <summary>The danger of a route: the security levels below Secure of the systems it enters (0 = all Secure).</summary>
        public static int RouteDanger(Database db, List<int> path)
        {
            int danger = 0;
            if (path == null) return 0;
            for (int i = 1; i < path.Count; i++) danger += 3 - Mathf.Clamp(SecurityOf(db.Systems.Find(s => s.index == path[i])), 0, 3);
            return danger;
        }

        static float MapDistance(SystemData a, SystemData b) => Vector3.Distance(SunPosition(a), SunPosition(b));

        /// <summary>Remake (jumpgate route planning, players' suggestion 2026-10-17): the gates reach system b from a, one jump or
        /// several (a == b counts).</summary>
        public static bool GateReachable(Database db, int a, int b) =>
            IsInRoutes(db.Systems.Find(s => s.index == a), b) || SystemPath(db, a, b) != null;

        /// <summary>Remake: the gate jumps from system a to b on the fewest-jumps or the safest route, 0 when a == b or the gates
        /// don't reach it.</summary>
        public static int GateJumps(Database db, int a, int b, bool safest = false)
        {
            if (a == b) return 0;
            var path = RoutePath(db, a, b, safest);
            return path != null ? path.Count - 1 : 0;
        }

        /// <summary>Remake: where the gate of the station 'from' jumps for the destination 'to': the destination itself in a gate
        /// neighbour on the fewest-jumps route (the original's only case: its maps program nothing further), else the gate orbit of
        /// the next system on the gate route (RoutePath, the safest one when 'safest': it may leave a neighbour's gate unused),
        /// where the autopilot flies on to the next gate. The original's gate took the ship straight to any programmed station,
        /// any distance.</summary>
        public static int NextHop(Database db, int from, int to, bool safest = false)
        {
            int a = db.Stations.Find(s => s.index == from)?.system ?? -1;
            int b = db.Stations.Find(s => s.index == to)?.system ?? -1;
            if (a < 0 || b < 0 || (!safest && IsInRoutes(db.Systems.Find(s => s.index == a), b))) return to;
            var path = RoutePath(db, a, b, safest);
            if (path == null || path.Count < 3) return to;
            int gateStation = db.Systems.Find(s => s.index == path[1])?.jumpgateStation ?? -1;
            return gateStation >= 0 ? gateStation : to;
        }

        /// <summary>StarMap::OnTouchBegin: energy cells for a Khador jump from system a to b ('noGate': the target has no gate
        /// route at all, warning 581).</summary>
        public static int EnergyCells(Database db, int a, int b, out bool noGate)
        {
            noGate = false;
            var path = SystemPath(db, a, b);
            int n = path != null ? path.Count - 1 : 0;
            if (n == 0 && a != b)
            {
                n = 4;
                var target = db.Systems.Find(s => s.index == b);
                noGate = target == null || target.jumpRoutesTo.Count == 0;
            }
            if (Cheats.FreeJumps) return 0;
            return Session.IsExtreme ? n * 2 : n;
        }

        public static int CellsInCargo() => Session.Cargo.Find(s => s.item == EnergyCellItem)?.amount ?? 0;

        /// <summary>Ship::hasVolatileGoods 0x1a3b90: 209 K'mirkk Toad Mutagen or 204 Red Plasma in the hold (no Khador Drive, 612).</summary>
        public static bool HasVolatileGoods => Session.Cargo.Exists(s => (s.item == 209 || s.item == 204) && s.amount > 0);

        public static void RemoveCells(int n)
        {
            var stack = Session.Cargo.Find(s => s.item == EnergyCellItem);
            if (stack == null) return;
            stack.amount -= n;
            if (stack.amount <= 0) Session.Cargo.Remove(stack);
        }

        /// <summary>Ship::hasJumpDriveIntegrated (37 / 38 / 40, and a mod ship's "integrated": "khadorDrive").</summary>
        public static bool HasIntegratedDrive(int ship) => Database.Shared.HasIntegratedDrive(ship);

        /// <summary>Ship::hasJumpDrive, plus the remake's free-play rule for gateless systems (see the file header).</summary>
        public static bool HasJumpDrive(Database db)
        {
            if (HasIntegratedDrive(Session.ShipIndex) || Session.Equipment.Exists(e => e.item == KhadorDriveItem)) return true;
            if (!Session.FreePlay) return false;
            int system = db.Stations.Find(s => s.index == Session.StationIndex)?.system ?? -1;
            return (db.Systems.Find(s => s.index == system)?.jumpgateStation ?? 0) < 0;
        }

        /// <summary>StarMap::initStarSystem: the system view's planets in station order (same layout on every visit).</summary>
        public static List<Planet> SystemLayout(Database db, SystemData sys)
        {
            var list = new List<Planet>();
            if (sys == null) return list;
            int n = sys.stations.Count;
            var rnd = new JavaRandom(sys.index * 1000L);
            var used = new bool[n + 1];
            int radius = 6400;
            foreach (int station in sys.stations)
            {
                int slot;
                do slot = rnd.NextInt(n + 1); while (used[slot]);
                used[slot] = true;
                radius += rnd.NextInt(5600) + 1600;
                int tex = db.Stations.Find(s => s.index == station)?.textureIndex ?? 0;
                list.Add(new Planet
                {
                    station = station, slot = slot, angle = slot * (65536 / (n + 1)), radius = radius, textureIndex = tex,
                    scale = (tex >= 0 && tex < PlanetScale.Length ? PlanetScale[tex] : 256) * 16 / 65536f,
                    ring = tex == 9 || tex == 16 || tex == 21 || station == 120 || station == 126 || station == 130 || station == 132,
                    gate = station == sys.jumpgateStation,
                });
            }
            for (int k = 0; k < list.Count; k++)   // the orbit loop runs after all planets
            {
                var p = list[k];
                p.orbitYaw = rnd.NextInt(3141) / 1000f;
                // Remake mods: stations.json "mapPlace" (after the draws, so the others keep their places).
                var place = Modding.ModWorld.MapPlaceOf(p.station);
                if (place != null)
                {
                    if (place.angle.HasValue) p.angle = Mathf.RoundToInt(place.angle.Value / 360f * 65536f);
                    if (place.distance.HasValue) p.radius = Mathf.RoundToInt(place.distance.Value);
                }
                list[k] = p;
            }
            return list;
        }
    }
}
