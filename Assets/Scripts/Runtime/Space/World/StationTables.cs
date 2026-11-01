// StationTables.cs
// Constants of the docked-station screens, decoded from the binary (Reference/research/station_interior.md).
// All positions are GAME units (Unity = (x, y, -z) * 0.05), angles in radians.
//   Hangar (CutScene 0x17, ModStation):  room per hangar index, ship height, parked-ship slots, camera
//   Bar (SpaceLounge, CutScene 4):       room per race, visitor slots, intro (A) and resting (B) cameras
// Hangar index h (Level::createScene 0xc2910): station 101 -> 8 (battlestation), 100 -> 7 (deep science), else the
// system race 0..3. Tables are indexed by h (rows 4..6 unused).

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class StationTables
    {
        public static int HangarIndex(int station, int systemRace)
        {
            systemRace = GoF2Remake.Flight.Standing.Look(systemRace);   // remake mods: a faction's look race
            return station == 101 ? 8 : station == 100 ? 7 : systemRace >= 0 && systemRace < 4 ? systemRace : 0;
        }

        public static int BarRace(int systemRace)
        {
            systemRace = GoF2Remake.Flight.Standing.Look(systemRace);
            return systemRace >= 0 && systemRace < 4 ? systemRace : 0;
        }

        // ---- hangar ------------------------------------------------------------------------------------------

        /// <summary>UNK_00253e48: the room (assembled: statics + child props).</summary>
        public static readonly string[] HangarRoom =
            { "hangar_terran", "hangar_vossk", "hangar_nivelian", "hangar_midorian", null, null, null, "hangar_deep_science", "hangar_battlestation" };

        /// <summary>DAT_00253d48 (= DAT_002520f0): ship pivot height above the hangar floor, per ship index.</summary>
        static readonly int[] ShipHeight =
        {
            160, 150, 220, 310, 160, 180, 140, 170, 170, 110, 250, 205, 240, 250, 250, 250,
            170, 170, 190, 460, 170, 210, 140, 170, 160, 190, 170, 240, 220, 280, 200, 200,
            140, 160, 180, 150, 310, 390, 480, 200, 300, 300, 170, 200, 170, 370, 370, 300,
            440, 340, 170, 470, 170, 170, 170, 170, 200, 170, 250, 150, 170, 250, 270, 170,
        };

        public static float ShipY(int ship)
        {
            if ((ship < 0 || ship >= ShipHeight.Length) && KitShipY(ship) is float kit) return kit;   // remake mods: a customizable ship
            ship = ModelShip(ship);
            return ship >= 0 && ship < ShipHeight.Length ? ShipHeight[ship]
                 : Mathf.Max(0, GoF2Remake.Data.CustomShips.Get(ship)?.hangarHeight ?? 250);   // remake: custom_ships.json
        }

        /// <summary>Remake (Modding.GameModels): a ship flying one of the game's ship models sits on the pads like that
        /// ship (its height and lifts), unless a mod ship's entry gives its own hangar height.</summary>
        static int ModelShip(int ship)
        {
            var c = GoF2Remake.Data.CustomShips.Get(ship);
            if (c != null) return c.gameModelShip >= 0 && c.hangarHeight < 0 ? c.gameModelShip : ship;
            if (ship >= 0 && ship < ShipHeight.Length && GoF2Remake.Data.Database.Shared.ShipModels.TryGetValue(ship, out var model))
            {
                int s = GoF2Remake.Modding.GameModels.ShipOf(model);
                if (s >= 0) return s;
            }
            return ship;
        }

        /// <summary>Remake mods: a customizable ship floats clear of the floor in the build it is shown in (the player's, with
        /// the player's weapons on the ship flown; Modding.ModHardpoints.HangarHeight), null = no kit ship.</summary>
        static float? KitShipY(int ship)
        {
            if (Modding.ModShipKits.KitShip(ship) == null) return null;
            var db = GoF2Remake.Data.Database.Shared;
            var asm = PlayerHull.Assembly(db, ship);
            return Modding.ModHardpoints.HangarHeight(db, ship, asm?.name, ship == GoF2Remake.Data.Session.ShipIndex ? GoF2Remake.Data.Session.Equipment : null);
        }

        // ---- remake: lifted off the pad where the hull would cut into it (hangar_heights.json, GoF2 > Build > Hangar Heights) ----

        public const int LiftBins = 24;   // headings, 15 deg apart (Unity yaw)
        [System.Serializable] public class HangarLift { public int hangar, slot, ship; public float[] lift; }
        [System.Serializable] public class HangarLiftFile { public List<HangarLift> entries; }

        static Dictionary<(int, int, int), float[]> lifts;

        /// <summary>After rebuilding hangar_heights.json (the Editor tool).</summary>
        public static void ClearHangarLifts() => lifts = null;

        /// <summary>The Unity pivot height of 'ship' on 'slot' (-1 = the player's turntable, which turns: its largest lift) of
        /// hangar 'hangar' at 'rotation': the original's floor y + ShipY, raised only where the hull would otherwise cut into
        /// the pad (remake: the wide hulls into the Midorian pads' raised rims, any ship into the raised pedestals of
        /// Midorian / deep science slot 2), never lowered.</summary>
        /// 'shipY': the ship's height to use instead of ShipY (another player's build of a customizable ship).
        public static float PadPivotY(int hangar, int slot, int ship, Quaternion rotation, float shipY = float.NaN)
        {
            var slots = hangar >= 0 && hangar < ParkedSlots.Length ? ParkedSlots[hangar] : null;
            float floor = slot >= 0 && slots != null && slot < slots.Length ? slots[slot].y : 0f;
            float y = (floor + (float.IsNaN(shipY) ? ShipY(ship) : shipY)) * OrbitLayout.MetersPerUnit;
            var l = Lift(hangar, slot, ship);
            if (l == null) return y;
            if (slot < 0) return y + Mathf.Max(l);
            float bin = Mathf.Repeat(rotation.eulerAngles.y, 360f) / 360f * l.Length;
            int near = Mathf.RoundToInt(bin);
            if (Mathf.Abs(bin - near) < 0.02f) return y + l[near % l.Length];   // a measured heading (FitParkingYaw)
            int a = Mathf.FloorToInt(bin) % l.Length, b = (a + 1) % l.Length;   // else the two around it
            return y + Mathf.Max(l[a], l[b]);
        }

        static float[] Lift(int hangar, int slot, int ship)
        {
            if (lifts == null)
            {
                lifts = new Dictionary<(int, int, int), float[]>();
                var ta = Resources.Load<TextAsset>("GoF2Data/hangar_heights");
                var file = ta != null ? JsonUtility.FromJson<HangarLiftFile>(ta.text) : null;
                if (file?.entries != null) foreach (var e in file.entries) lifts[(e.hangar, e.slot, e.ship)] = e.lift;
            }
            return lifts.TryGetValue((hangar, slot, ModelShip(ship)), out var l) && l != null && l.Length > 0 ? l : null;
        }

        static bool HasLift(int hangar, int slot, int ship) => Lift(hangar, slot, ship) != null;

        /// <summary>Remake: the parking yaw (game radians) for 'ship' on 'slot'. Where the hull is lifted off the pad
        /// (hangar_heights.json) it is turned to the nearest heading the lift was measured at (every 15 deg: in between, a
        /// long hull swept up to 2 m into what the two measured headings cleared), and when that heading needs more than 1 m
        /// above the least lift there, to the nearest one that doesn't: a long hull on a Midorian pedestal reaches over the
        /// 22 m structures beside it at some headings, and lifted clear of them it hung 10 m in the air (the Rhino); turned a
        /// little it fits at the pedestal's height. Elsewhere the original's random yaw.</summary>
        public static float FitParkingYaw(int hangar, int slot, int ship, float gameYaw)
        {
            if (slot < 0 || !HasLift(hangar, slot, ship)) return gameYaw;
            const float Step = Mathf.PI / 12f, Slack = 1f;
            float UnityYaw(float yaw) => OrbitLayout.RotationToUnity(new Vector3(0f, yaw, 0f)).eulerAngles.y;
            float Height(float yaw) => PadPivotY(hangar, slot, ship, OrbitLayout.RotationToUnity(new Vector3(0f, yaw, 0f)));
            // Onto a measured heading (the game yaw may turn either way in Unity's).
            float u = UnityYaw(gameYaw), off = Mathf.DeltaAngle(u, Mathf.Round(u / 15f) * 15f) * Mathf.Deg2Rad;
            float snapped = gameYaw + off;
            float su = UnityYaw(snapped);
            if (Mathf.Abs(Mathf.DeltaAngle(su, Mathf.Round(su / 15f) * 15f)) > 0.1f) snapped = gameYaw - off;
            gameYaw = snapped;
            float least = float.PositiveInfinity;
            for (int k = 0; k < LiftBins; k++) least = Mathf.Min(least, Height(gameYaw + k * Step));
            if (Height(gameYaw) <= least + Slack) return gameYaw;
            for (int k = 1; k <= LiftBins / 2; k++)
                foreach (int sign in new[] { 1, -1 })
                {
                    float yaw = gameYaw + sign * k * Step;
                    if (Height(yaw) <= least + Slack) return yaw;
                }
            return gameYaw;
        }

        /// <summary>ModStation::OnInitialize state 0x3c: turntable start, +0xe0 = 270 px (Nivelian -200) / 120 px per rad.</summary>
        public static float StartYaw(int hangar) => (hangar == 2 ? -200f : 270f) / TurntablePixelsPerRadian;
        public const float TurntablePixelsPerRadian = 120f;

        /// <summary>UNK_00253ee8: parked ships = nextInt(max + 1). Remake: Midorian 5 (the original's 3), its remodelled
        /// room's third row of pads; Terran 5 (the original's 2), its remodelled room's three new pads; Vossk 18 (the
        /// original's 9) of its remodelled room's 49 bays; Nivelian 5 (the original's 2), its remodelled room's three new
        /// pads (ParkedSlots).</summary>
        public static readonly int[] ParkedMax = { 5, 18, 5, 5, 0, 0, 0, 3, 0 };

        /// <summary>Parked-ship slots (UNK_00253f10 ...); the ship's own height is added to y.</summary>
        public static readonly Vector3[][] ParkedSlots =
        {
            new[]
            {
                new Vector3(0, 0, 2891), new Vector3(0, 0, 5781),
                // Remake: the remodelled Terran room's new pads (2026-10-23), measured on its mesh with rays (the same pad
                // as the original two, at their height): a second column beside them, and one behind the turntable.
                new Vector3(5459, 0, 2891), new Vector3(5459, 0, 5781), new Vector3(0, 0, -2891),
            },
            new[]
            {
                new Vector3(-3715, 0, 661), new Vector3(-6983, 36, 2548), new Vector3(-9408, 36, 5438),
                new Vector3(-9408, 36, 16301), new Vector3(-6983, 36, 19191), new Vector3(-3715, 36, 21078),
                new Vector3(-9408, 5359, 16301), new Vector3(-6983, 5359, 19191), new Vector3(-3715, 5359, 21078),
                // Remake: the remodelled Vossk room (2026-10-24), a whole ring of bays on three tiers round the hub (game
                // (0, 0, 10860)), every 20 deg on the original slots' ring (10856), measured on its mesh with rays (floors
                // at 268 m, 0 and -270 m; no bay over the portal on the two lower tiers). The other side of the middle tier:
                new Vector3(0, 0, 21716), new Vector3(3713, 0, 21061), new Vector3(6978, 0, 19176), new Vector3(9402, 0, 16288),
                new Vector3(10691, 0, 12745), new Vector3(10691, 0, 8975), new Vector3(9402, 0, 5432), new Vector3(6978, 0, 2544),
                new Vector3(3713, 0, 659),
                // the rest of the top tier (over the turntable and the portal too),
                new Vector3(0, 5359, 0), new Vector3(-3713, 5359, 659), new Vector3(-6978, 5359, 2544), new Vector3(-9402, 5359, 5432),
                new Vector3(-10691, 5359, 8975), new Vector3(-10691, 5359, 12745), new Vector3(0, 5359, 21716), new Vector3(3713, 5359, 21061),
                new Vector3(6978, 5359, 19176), new Vector3(9402, 5359, 16288), new Vector3(10691, 5359, 12745), new Vector3(10691, 5359, 8975),
                new Vector3(9402, 5359, 5432), new Vector3(6978, 5359, 2544), new Vector3(3713, 5359, 659),
                // and the new bottom tier.
                new Vector3(0, -5403, 0), new Vector3(-3713, -5403, 659), new Vector3(-6978, -5403, 2544), new Vector3(-9402, -5403, 5432),
                new Vector3(-9402, -5403, 16288), new Vector3(-6978, -5403, 19176), new Vector3(-3713, -5403, 21061), new Vector3(0, -5403, 21716),
                new Vector3(3713, -5403, 21061), new Vector3(6978, -5403, 19176), new Vector3(9402, -5403, 16288), new Vector3(10691, -5403, 12745),
                new Vector3(10691, -5403, 8975), new Vector3(9402, -5403, 5432), new Vector3(6978, -5403, 2544), new Vector3(3713, -5403, 659),
            },
            new[]
            {
                new Vector3(4096, 0, 0), new Vector3(4096, 0, 4096),
                // Remake: the remodelled Nivelian room's new pads (2026-11-01, hangar_nivelian_alt_alpha_add), measured on
                // its mesh with rays (the same pad as the original two, at their height): beside the turntable and the
                // original two, one each side.
                new Vector3(0, 0, 4096), new Vector3(0, 0, -4096), new Vector3(4096, 0, -4096),
            },
            new[]
            {
                new Vector3(-4096, 0, 0), new Vector3(0, 0, 4096), new Vector3(-4096, 0, 4096),
                // Remake: the remodelled Midorian room's third row, behind the turntable (a raised pedestal like slot 2,
                // a sunk pad like the turntable's; Reference/contrib/immersive_hangars).
                new Vector3(0, 0, -4096), new Vector3(-4096, 0, -4096),
            },
            null, null, null,
            new[] { new Vector3(-1961, 0, 8512), new Vector3(-1961, 0, 5562), new Vector3(-1961, 0, 2610) },
            new[] { new Vector3(4096, 0, 0) },
        };

        /// <summary>Remake-only hangar flights (HangarFlight): the way in and out of each hangar, in UNITY metres (hangar
        /// root space), measured on the room meshes. 'gate' = the forcefield's centre (the hangar_*_alpha / Vossk
        /// anim_add portal / battlestation alpha plane), 'outward' = toward space; the ships pass it level, at a height
        /// inside 'gateSpan' (the field's height with a margin). Vertical pads (hub unused): the ships cross the room at
        /// 'cruise' height, above the parked ships, and land straight down. Vossk: its bays have roofs and face the
        /// ring's centre ('hub'), so the ships swing through the centre and fly into the bay from 'approach' metres in front of
        /// it, 'hover' metres up, then land straight down. The rooms are only modelled where the 16:9 hangar camera
        /// looks, so the lanes stay inside the room.</summary>
        public sealed class HangarLane
        {
            public Vector3 gate, outward;
            public Vector2 gateSpan;
            public float cruise;
            public bool bays;
            public Vector3 hub;
            public float approach, hover;
            /// <summary>Pillars from floor to ceiling the lane steers round (x, z and radius, metres; HangarFlight
            /// AvoidPillars), null = none.</summary>
            public Vector3[] pillars;
            /// <summary>Bays: the radius (metres) of a column standing at 'hub' the run goes round (HangarFlight.AroundColumn)
            /// instead of through the centre, 0 = none.</summary>
            public float column;
        }

        /// <summary>The Nivelian hangar's roof pillars (x, z, radius), measured on hangar_nivelian's mesh (the original's six
        /// with rays at 50 m up: 5-7 m round, up to 148 m; a player's report 2026-10-16, ships flew through the one in front
        /// of the right pad). Modified: the remodelled room's twelve (2026-11-01, a slice of its mesh at 50 m: 5.7 m round),
        /// a 3 x 4 grid round its six pads.</summary>
        static readonly Vector3[] NivelianPillars =
        {
            new Vector3(-102.6f, -307.3f, 8f), new Vector3(-102.6f, -102.5f, 8f), new Vector3(-102.6f, 102.3f, 8f), new Vector3(-102.6f, 307.1f, 8f),
            new Vector3(102.3f, -307.3f, 8f), new Vector3(102.3f, -102.5f, 8f), new Vector3(102.3f, 102.3f, 8f), new Vector3(102.3f, 307.1f, 8f),
            new Vector3(307.2f, -307.3f, 8f), new Vector3(307.2f, -102.5f, 8f), new Vector3(307.2f, 102.3f, 8f), new Vector3(307.2f, 307.1f, 8f),
        };

        public static readonly HangarLane[] HangarLanes =
        {
            new HangarLane { gate = new Vector3(0f, 55f, -549f), outward = Vector3.back, gateSpan = new Vector2(30f, 115f), cruise = 45f },     // Terran
            new HangarLane { gate = new Vector3(-625f, 5f, -543f), outward = Vector3.left, gateSpan = new Vector2(-250f, 120f), bays = true,   // Vossk
                             hub = new Vector3(0f, 0f, -543f), approach = 70f, hover = 5f, column = 100f },   // the remodelled room's column (its base ring, the struts)
            new HangarLane { gate = new Vector3(357f, 70f, -565f), outward = Vector3.back, gateSpan = new Vector2(25f, 130f), cruise = 50f,    // Nivelian
                             pillars = NivelianPillars },
            new HangarLane { gate = new Vector3(-203f, 50f, -407f), outward = Vector3.back, gateSpan = new Vector2(20f, 80f), cruise = 45f },   // Midorian
            null, null, null,
            new HangarLane { gate = new Vector3(177f, 80f, -633f), outward = Vector3.back, gateSpan = new Vector2(10f, 180f), cruise = 50f },   // Deep Science
            new HangarLane { gate = new Vector3(-226f, 50f, 12f), outward = Vector3.left, gateSpan = new Vector2(25f, 85f), cruise = 40f },     // Battlestation
        };

        /// <summary>Phone camera table DAT_002546c4, relative to the ship pivot (0, ShipY, 0).</summary>
        public static readonly Vector3[] HangarCameraPos =
        {
            new Vector3(1076, 900, -2273), new Vector3(1787, 1086, -1632), new Vector3(-2247, 1010, -1845), new Vector3(1800, 800, -1778),
            Vector3.zero, Vector3.zero, Vector3.zero, new Vector3(1200, 800, -2678), new Vector3(2200, 800, -1678),
        };

        /// <summary>UNK_0025473c (pitch) and UNK_0025478c (yaw); roll -0.03 (0xbcf5c28f). Rotation order 2 = Ry * Rx * Rz.</summary>
        public static readonly Vector2[] HangarCameraRot =
        {
            new Vector2(-0.27f, -3.36f), new Vector2(-0.30f, -3.75f), new Vector2(-0.18f, -2.05f), new Vector2(-0.20f, 2.57f),
            Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(-0.20f, 3.00f), new Vector2(-0.20f, 2.50f),
        };
        public const float HangarCameraRoll = -0.03f;
        public const float HangarFov = 0.8f, HangarNear = 200f, HangarFar = 100000f;   // CutScene::initialize 0xa4074

        /// <summary>ModStation::resetLight 0xe9f1c: LIGHT0 ambient by system race (race 8 = pirates).</summary>
        public static Color HangarAmbient(int systemRace) => systemRace switch
        {
            1 => new Color(0.25f, 0.55f, 0.25f),
            2 => new Color(0.15f, 0.15f, 0.35f),
            3 => new Color(0.45f, 0.25f, 0.25f),
            8 => new Color(0.83f, 0.28f, 0.12f),
            _ => new Color(0.25f, 0.25f, 0.25f),
        };

        /// <summary>LIGHT0 direction toward the light, game space: -(camDir + (-0.2, -0.3, 0)) with camDir = (0.383, 0, 0.924).</summary>
        public static readonly Vector3 HangarTowardLight = new Vector3(-0.183f, 0.300f, -0.924f);

        /// <summary>Vossk fog, linear 0..end, colour 0x011e0c (hangar 30000, bar 5000).</summary>
        public static readonly Color VosskFog = new Color(1f / 255f, 30f / 255f, 12f / 255f);
        public const float HangarFogEnd = 30000f, BarFogEnd = 5000f;

        /// <summary>Globals::getRandomEnemyFighter 0xf9034: generic fighters per race (index &lt; 37, not 0/8/9/10/13/14/15).</summary>
        public static readonly int[][] RaceFighters =
        {
            new[] { 1, 5, 7, 17, 22, 26, 27, 28, 33, 34, 36 },   // Terran
            new[] { 9 },                                          // Vossk: H'Soc
            new[] { 4, 12, 16, 18, 21, 31, 35 },                  // Nivelian
            new[] { 3, 6, 19, 20, 30 },                           // Midorian
        };
        public static readonly int[] PirateFighters = { 2, 11, 23, 24, 25, 29, 32 };
        public static readonly int[] DeepScienceShips = { 37, 38, 40 };   // station 100 override

        // ---- bar ---------------------------------------------------------------------------------------------

        public static readonly string[] BarRoom = { "bar_terran", "bar_vossk", "bar_nivelian", "bar_midorian" };

        /// <summary>UNK_00254000: 7 visitor slots per bar race (feet position).</summary>
        public static readonly Vector3[][] VisitorSlots =
        {
            new[] { new Vector3(-2325, 0, -964), new Vector3(-1023, 212, 745), new Vector3(-1098, 212, 427), new Vector3(-1164, 212, -227),
                    new Vector3(-1979, 0, -108), new Vector3(-2556, 0, 646), new Vector3(-1439, 212, -860) },
            new[] { new Vector3(-694, 102, -271), new Vector3(914, 102, 453), new Vector3(-680, 102, 1734), new Vector3(-417, 102, 1454),
                    new Vector3(462, 102, -101), new Vector3(285, 102, 913), new Vector3(-680, 102, 155) },
            new[] { new Vector3(-1279, 0, 264), new Vector3(-1220, 0, -192), new Vector3(-1849, 0, -1214), new Vector3(-875, 0, -686),
                    new Vector3(48, 0, -911), new Vector3(-457, 0, -2195), new Vector3(-794, 0, -1788) },
            new[] { new Vector3(1542, -10, -642), new Vector3(1357, -10, -1986), new Vector3(1924, -10, -1377), new Vector3(-194, -10, -2542),
                    new Vector3(979, -10, -202), new Vector3(1047, -10, 220), new Vector3(2319, -10, -498) },
        };

        /// <summary>Bar cameras: intro start A (DAT_0025ce84, yaw DAT_00252070) and rest B (DAT_0025ceb4, yaw DAT_00252080).</summary>
        public static readonly Vector3[] BarCameraStart =
            { new Vector3(-1315, 1160, -2728), new Vector3(1484, 2980, -1626), new Vector3(-2472, 734, -3000), new Vector3(-2598, 400, 1813) };
        public static readonly float[] BarCameraStartYaw = { 0.03f, 0.03f, 0.21f, 1.87f };
        public static readonly Vector3[] BarCameraRest =
            { new Vector3(-1800, 620, 1805), new Vector3(607, 610, 2362), new Vector3(136, 444, 1122), new Vector3(2222, 450, 1227) };
        public static readonly float[] BarCameraRestYaw = { 0.04f, 0.43f, 0.60f, 0.46f };
        public const float BarFov = 1.2f, BarNear = 200f, BarFar = 48000f;
        public const float BarIntroMs = 3000f;

        /// <summary>DAT_00254380: visitor mesh per agent race (female Terrans: 14724). Cyborgs and Midorians use the Terran man.</summary>
        public enum Visitor { TerranMale, TerranFemale, Vossk, Nivelian, Multipod, Bobolan, Grey }
        public static Visitor VisitorFor(int race, bool female) => race switch
        {
            0 => female ? Visitor.TerranFemale : Visitor.TerranMale,
            1 => Visitor.Vossk,
            2 => Visitor.Nivelian,
            4 => Visitor.Multipod,
            6 => Visitor.Bobolan,
            7 => Visitor.Grey,
            _ => Visitor.TerranMale,   // 3 Midorian, 5 Cyborg (14725)
        };

        // ---- music (Globals::playMusicAndFadeOutCurrent 0xf9dc0) ----------------------------------------------

        public enum Music { Terran, Vossk, Nivelian, Midorian, HomeBase, Valkyrie, DeepScience }
        public static Music MusicFor(int station, int systemRace) =>
            station == 108 ? Music.HomeBase : station == 101 ? Music.Valkyrie : station == 10 || station == 100 ? Music.DeepScience
            : systemRace == 1 ? Music.Vossk : systemRace == 2 ? Music.Nivelian : systemRace == 3 ? Music.Midorian : Music.Terran;
    }
}
