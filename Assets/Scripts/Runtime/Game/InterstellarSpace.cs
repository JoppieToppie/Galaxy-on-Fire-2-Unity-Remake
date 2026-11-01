// InterstellarSpace.cs
// Remake: the space between the systems as plain rules. A cruise drive with attr 135 (items.json stat "interstellar",
// CruiseDrive.InterstellarAttr) mounted, the quick menu's entry plays the Khador sequence and the Space scene reloads as
// this space instead of a destination (Session.EnterInterstellar, read once by SpaceLevel): the same station stays
// Session.StationIndex, so a save or a load in here is the orbit the ship left. No station, gate, planets, asteroids,
// traffic or story; every system on the star map is a sun at its galaxy map place (GalaxyMap.SunPosition), a target the
// autopilot flies to. Reaching one plays the Khador scene into its gate orbit, as a Khador jump arrives.
//   what shows    GalaxyMap.Visibility, the star map's own rule: only what is revealed (the story, bought coordinates), an
//                 event's show / hide, a mod's "available", never a missing mod's placeholder. Nothing else is reachable.
//   energy cells  a leg of the drive is LegUnits here, the average gate hop, so a crossing costs what the gate route's
//                 Khador jump would (GalaxyMap.EnergyCells: a cell a hop, x2 on Extreme): no price in the economy changes.
//   running out   the drive drops and the ship falls out at the nearest system (Nearest): nobody is stranded here.
//   the Void      no place here (the user: not a system of the known universe, no point to fly to, no distance): once the
//                 story is past the wormhole ride into it (VoidShown) the autopilot menu offers a dive off the galaxy's
//                 disc (DiveWay) that falls into the alien orbit after a random DiveMinMs..DiveMaxMs of the drive; the
//                 drive flies out of it too, from beside the system it was entered from (PositionOf).

using System.Collections.Generic;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class InterstellarSpace
    {
        /// <summary>Game units per unit of the galaxy map: the average gate hop (2510 on the map) is 30 km.</summary>
        public const float UnitsPerMapUnit = 240f;
        /// <summary>A leg of energy cells here: the average gate hop.</summary>
        public const float LegUnits = 600000f;
        /// <summary>A system's edge (8 km): the level changes here, at full warp with nothing near and the sky already the
        /// system's, and the ship flies the rest of the way in through the real orbit (CarryMeters).</summary>
        public const float ArriveUnits = 160000f;
        /// <summary>Where a system starts to show on the way in (20 km): its sky blends in, its orbit is built ahead.</summary>
        public const float ApproachUnits = 400000f;
        /// <summary>The planets' size of their own where they start to show, and at the edge.</summary>
        public const float PlanetFarSize = 0.03f, PlanetEdgeSize = 0.5f;
        /// <summary>How far outside the system it left the ship starts.</summary>
        public const float StartUnits = 240000f;
        /// <summary>Multiplayer: the orbit id everybody in this space shares (SpaceLevel.NetOrbitId), past every station and
        /// below NetArena.OrbitBase (an arena's are private and allow fights).</summary>
        public const int OrbitId = 90000;
        /// <summary>The middle of the original galaxy on the map: the space is centred on it, for floats' sake (+-66 km).</summary>
        static readonly Vector3 Centre = new Vector3(-3630f, -2370f, 4000f);
        /// <summary>The alien orbit's system index as OrbitLayout.Build gives it (it has no system).</summary>
        public const int VoidSystem = -1;
        /// <summary>How long a dive into the Void runs on the drive before it falls in (the user, 2026-11-01: 10 to 15 s). A
        /// leg at the base drive's 800 % is 37 s, so a dive takes one cell, half the Khador Drive's two.</summary>
        public const float DiveMinMs = 10000f, DiveMaxMs = 15000f;
        /// <summary>The story step that takes the player into the Void through the wormhole (the ride at 24): the Void is no
        /// place to fly to before it.</summary>
        const int VoidRideIndex = 24;

        public sealed class Point
        {
            public int system, station, textureIndex;
            public string name;
            public Vector3 position;   // game units
        }

        /// <summary>The mounted cruise drive when it flies this space, null otherwise: mounting it is the only switch.</summary>
        public static ItemData Drive(Database db)
        {
            var it = Shop.MountedCruiseDrive(db);
            return it != null && it.Attr(Flight.CruiseDrive.InterstellarAttr) > 0 ? it : null;
        }

        /// <summary>A system's place in this space (game units).</summary>
        public static Vector3 Position(SystemData s) => (GalaxyMap.SunPosition(s) - Centre) * UnitsPerMapUnit;

        /// <summary>The dive into the Void is offered: free play, else once the story has been there.</summary>
        public static bool VoidShown => Session.FreePlay || Story.Index > VoidRideIndex;

        /// <summary>Where an orbit is in this space: its system's (the first system's for a station that has none). The Void
        /// has no place: out of it the ship is beside the system it was entered from (Session.VoidReturnStation).</summary>
        public static Vector3 PositionOf(Database db, int station)
        {
            if (station == Session.VoidOrbit) station = Session.VoidReturnStation;
            int system = db.Stations.Find(s => s.index == station)?.system ?? -1;
            return Position(db.Systems.Find(s => s.index == system) ?? db.Systems[0]);
        }

        /// <summary>Where a jump into the system comes out: its gate orbit, else its first station; -1 = nowhere to go.</summary>
        public static int ArrivalStation(SystemData s)
        {
            if (s?.stations == null || s.stations.Count == 0) return -1;
            return s.stations.Contains(s.jumpgateStation) ? s.jumpgateStation : s.stations[0];
        }

        /// <summary>Every system this space shows now, in the database's order.</summary>
        public static List<Point> Systems(Database db)
        {
            var list = new List<Point>();
            foreach (var s in db.Systems)
            {
                int station = ArrivalStation(s);
                if (station < 0 || !GalaxyMap.IsVisible(db, s.index)) continue;
                list.Add(new Point { system = s.index, station = station, textureIndex = s.textureIndex, name = s.name.Trim(), position = Position(s) });
            }
            return list;
        }

        /// <summary>The way a dive into the Void goes from 'at' with the ship facing 'forward' (game units): off the galaxy's
        /// disc (thin along z: map z 1600 to 6400) on the side the ship is on, leaning its way, so the turn is short and
        /// the systems fall behind.</summary>
        public static Vector3 DiveWay(Vector3 at, Vector3 forward)
        {
            var off = new Vector3(0f, 0f, at.z < 0f ? -1f : 1f);
            var lean = Vector3.ProjectOnPlane(forward, off);
            return (off * 2f + (lean.sqrMagnitude > 1e-6f ? lean.normalized : Vector3.zero)).normalized;
        }

        /// <summary>How far ahead (game units) the dive's end lies with 'ms' of it left: the edge, and what the drive at
        /// 'speedPercent' covers in that time over the ship's own 100 m/s (as CarryMeters).</summary>
        public static float DiveUnits(int speedPercent, float ms) =>
            ArriveUnits + Mathf.Max(1f, speedPercent / 100f) * 100f / OrbitLayout.MetersPerUnit * Mathf.Max(0f, ms) / 1000f;

        /// <summary>The system closest to 'at' (game units), null with none.</summary>
        public static Point Nearest(List<Point> points, Vector3 at)
        {
            Point best = null;
            float d = float.MaxValue;
            foreach (var p in points)
            {
                float e = (p.position - at).sqrMagnitude;
                if (e < d) { d = e; best = p; }
            }
            return best;
        }

        /// <summary>The ship's start (game units): StartUnits outside the system it left, towards 'towards' (the programmed
        /// destination's system, else the galaxy's middle, the origin here).</summary>
        public static void Start(Vector3 from, Vector3 towards, out Vector3 position, out Vector3 forward)
        {
            forward = towards - from;
            forward = forward.sqrMagnitude > 1f ? forward.normalized : Vector3.forward;
            position = from + forward * StartUnits;
        }

        /// <summary>The ship's rotation (Unity) where it starts, in the space left from 'layout': SpaceLevel places it so, and the
        /// Khador Slipstream's hole shows the warp as it will look from there (World.KhadorFx).</summary>
        public static Quaternion StartRotation(Database db, OrbitLayout layout) => Quaternion.LookRotation(OrbitLayout.DirToUnity(StartPose(db, layout, out _)), Vector3.up);

        /// <summary>The ship's start (game units) and its heading: out of the system left (beside the one the Void was entered from),
        /// toward the programmed destination.</summary>
        public static Vector3 StartPose(Database db, OrbitLayout layout, out Vector3 position)
        {
            var from = PositionOf(db, layout.stationIndex);
            var goal = db.Stations.Find(s => s.index == Session.ProgrammedStation) is StationData prog
                       && db.Systems.Find(s => s.index == prog.system) is SystemData sys && sys.index != layout.systemIndex && GalaxyMap.IsVisible(db, sys.index)
                ? Position(sys) : Vector3.zero;
            Start(from, goal, out position, out var forward);
            return forward;
        }

        /// <summary>How far the sky has come from the last system's to the next one's, 'fromUnits' and 'toUnits' away: 0 at the
        /// last one's edge, where a flight out starts, 1 at the next one's, where an arrival finds its sky. So a crossing shows
        /// in the sky all the way, by the distance flown.</summary>
        public static float SkyShare(float fromUnits, float toUnits)
        {
            float left = Mathf.Max(0f, fromUnits - StartUnits), ahead = Mathf.Max(0f, toUnits - ArriveUnits);
            float share = left + ahead > 0f ? left / (left + ahead) : 1f;
            return share * share * (3f - 2f * share);
        }

        /// <summary>How far (metres) a ship carrying a cruise's speed flies while it eases to its own over 'ms' (the smoothstep's
        /// average is half way), the ship's own 100 m/s under it: an arrival starts this far out to end where arrivals do.</summary>
        public static float CarryMeters(int speedPercent, float ms) => (1f + (speedPercent / 100f - 1f) / 2f) * 100f * ms / 1000f;

        /// <summary>How big a system's planets and all else in it are of their own size on the way in, 'units' from it: a speck where the approach
        /// starts, half at the edge, at a steady rate of growth as a flight at a steady speed sees it.</summary>
        public static float ApproachSize(float units, float from = PlanetFarSize, float approachUnits = ApproachUnits) =>
            from * Mathf.Pow(PlanetEdgeSize / from, Mathf.InverseLerp(approachUnits, ArriveUnits, units));

        /// <summary>And in the orbit, as the arrival eases out ('left' 1..0): on from the edge's size to their own, slowing with
        /// the ship.</summary>
        public static float ArrivalSize(float left) => Mathf.Lerp(PlanetEdgeSize, 1f, 1f - left * left);

        /// <summary>The cells a straight crossing takes: one per started leg, x2 on Extreme.</summary>
        public static int CellsFor(float units, bool extreme) => Mathf.CeilToInt(units / LegUnits) * (extreme ? 2 : 1);

        /// <summary>The orbit emptied: nothing of the system the ship left is here.</summary>
        public static void Clear(OrbitLayout o)
        {
            o.interstellar = true;
            o.alienOrbit = false;   // out of the Void too: its rules (no autopilot, its locks) are the orbit's
            o.hasStation = o.stationObject = o.hasJumpgate = false;
            o.planets.Clear();
            o.asteroidCount = 0;
            o.modOres = null;
            o.sunScale = 0f;
            o.fog = false;
        }
    }
}
