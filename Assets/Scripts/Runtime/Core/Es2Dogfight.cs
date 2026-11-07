// Es2Dogfight.cs
// Remake option (2026-11-06, Options > Gameplay "NPC dogfighting" "ES2 for all", or a mod hull's "traffic": { "ai": "es2" };
// Settings.NpcDogfight, a session rule): an NPC fighter fights the way EVERSPACE 2's outlaw fighters do (BP_Ship_NPC's
// BT_Default with BT_FleeSubtree; es2_ships_importer research/npc_ai.md) instead of GoF2's. World.NpcShip steers by it
// (NpcShipEs2):
//   Attack       BTT_Attack: a run at a point beside the target (a random 45..85 m off the line to it, at a random angle
//                about it, chosen once a run) until within 160 m of that point
//   gain distance  for 6.5 s +-25 % (BTD_TimeLimitVariance, VariancePercent 0.25) after each run:
//     Orbit      20 %: BTT_OrbitAttack KeepDirectionOnHorizontalOrbit, 500 m round it, for 3 s +-50 %, then on as below
//     FlyAway    BTT_FlyAwayFrom: zig-zag away (200..350 m legs, 55 deg off "away") until 400 m from it: the next run
//     tailed     BTS_DetectIfTailed (every 2.5 +-0.75 s: it sits behind us, nose on us, within 3 x our size of its line)
//                ends the fly-away: 65 % Zigzag (350..550 m legs, 35 deg), else 30 % a BarrelRoll (1.5 s) then Zigzag, else
//                TurnAndFight (BTT_Attack again); each until the gain window ends
//   Flee         BT_FleeSubtree while the hull is below 20 % (BP_Ship_NPC's FleeThreshHealthRatio 0.2): away to 1000 m, then
//                loiter there. GoF2's NPCs have no shield to recharge (ES2 ends the flight at 75 % shield), so it flees once,
//                for FleeMaxMs at most, and comes back.
// Difficulty (GoF2's selection; ES2's trees don't change with it): no barrel roll on Easy, as GoF3's evasive roll
// (Gof3Dogfight.EvadeChance). ES2's distances in metres x UnitsPerMetre (its ships' scale 3.6 x 20 game units a metre, the
// importer's). Plain C#; 'random' gives 0..1.

namespace GoF2Remake.Flight
{
    public class Es2Dogfight
    {
        public enum Mode { Attack, Orbit, FlyAway, Zigzag, BarrelRoll, TurnAndFight, Flee, FleeIdle }

        public const float UnitsPerMetre = 3.6f * 20f;
        public const float AttackArriveM = 160f, OffsetMinM = 45f, OffsetMaxM = 85f;
        public const float GainMs = 6500f, GainVariance = 0.25f;
        public const float OrbitChance = 0.2f, OrbitMs = 3000f, OrbitVariance = 0.5f, OrbitRadiusM = 500f, OrbitStepDeg = 45f;
        public const float FlyAwayM = 400f, FlyAwayLegMinM = 200f, FlyAwayLegMaxM = 350f, FlyAwayDeviationDeg = 55f;
        public const float ZigzagChance = 0.65f, RollChance = 0.3f, ZigzagLegMinM = 350f, ZigzagLegMaxM = 550f, ZigzagDeviationDeg = 35f;
        public const float RollMs = 1500f;
        public const float TailCheckMs = 2500f, TailCheckDeviationMs = 750f, TailSizeFactor = 3f;
        public const float FleeHullRatio = 0.2f, FleeSafeM = 1000f, FleeMaxMs = 20000f, FleeLegM = 200f, FleeDeviationDeg = 30f;

        readonly System.Func<float> random;
        public readonly float rollChance;
        float gainMs, modeMs, tailMs, fleeMs;
        bool fled;

        public Mode Current { get; private set; } = Mode.Attack;
        /// <summary>The run's point beside the target: its angle about the line (radians) and its distance off it (units).</summary>
        public float OffsetAngle { get; private set; }
        public float OffsetUnits { get; private set; }
        /// <summary>-1 or 1: which way it circles in an orbit, and the side of a barrel roll.</summary>
        public int Side { get; private set; } = 1;
        /// <summary>Set when a mode with zig-zag legs starts: the steering takes a new leg at once.</summary>
        public bool NewLeg { get; set; }

        public Es2Dogfight(float rollChance, System.Func<float> random)
        {
            this.rollChance = rollChance;
            this.random = random;
            Begin(Mode.Attack);
        }

        /// <summary>No barrel roll on Easy (Session.Difficulty below 0.25), ES2's 30 % of the rest otherwise.</summary>
        public static float RollChanceFor(float difficulty) => difficulty < 0.25f ? 0f : RollChance;

        public bool Fires => Current != Mode.BarrelRoll;
        public bool Rolling => Current == Mode.BarrelRoll;
        /// <summary>0..1 through the barrel roll (0 when none).</summary>
        public float RollProgress => Current == Mode.BarrelRoll ? 1f - modeMs / RollMs : 0f;
        /// <summary>A gain-distance mode: it turns away from the target or circles it.</summary>
        public bool GainingDistance => Current == Mode.Orbit || Current == Mode.FlyAway || Current == Mode.Zigzag
                                       || Current == Mode.BarrelRoll || Current == Mode.TurnAndFight;
        /// <summary>A zig-zag leg's length range (units) and its deviation from "away" (degrees) in the current mode.</summary>
        public (float min, float max, float deviationDeg) Leg => Current switch
        {
            Mode.Zigzag => (ZigzagLegMinM * UnitsPerMetre, ZigzagLegMaxM * UnitsPerMetre, ZigzagDeviationDeg),
            Mode.Flee => (FleeLegM * UnitsPerMetre, FleeLegM * UnitsPerMetre, FleeDeviationDeg),
            _ => (FlyAwayLegMinM * UnitsPerMetre, FlyAwayLegMaxM * UnitsPerMetre, FlyAwayDeviationDeg),
        };

        /// <summary>What the fighter sees this frame.</summary>
        public struct View
        {
            public float distance;     // to the target, units
            public bool arrived;       // within AttackArriveM of the run's point
            public bool tailed;        // BTS_DetectIfTailed's test now (sampled on its interval)
            public float hullRatio;    // its hull / its maximum
        }

        /// <summary>A new target: a new run.</summary>
        public void NewTarget() { if (Current != Mode.Flee && Current != Mode.FleeIdle) Begin(Mode.Attack); }

        public void Step(float dtMs, View v)
        {
            if (!fled && v.hullRatio < FleeHullRatio) { fled = true; fleeMs = FleeMaxMs; Begin(Mode.Flee); return; }
            switch (Current)
            {
                case Mode.Flee:
                case Mode.FleeIdle:
                    if ((fleeMs -= dtMs) <= 0f) { Begin(Mode.Attack); return; }
                    var flee = v.distance >= FleeSafeM * UnitsPerMetre ? Mode.FleeIdle : Mode.Flee;
                    if (flee != Current) { Current = flee; NewLeg = true; }
                    return;
                case Mode.Attack:
                    if (v.arrived) StartGain();
                    return;
            }
            // the gain-distance window
            if ((gainMs -= dtMs) <= 0f) { Begin(Mode.Attack); return; }
            modeMs -= dtMs;
            switch (Current)
            {
                case Mode.Orbit:
                    if (modeMs <= 0f) Begin(Mode.FlyAway);
                    return;
                case Mode.FlyAway:
                    if (v.distance >= FlyAwayM * UnitsPerMetre) { Begin(Mode.Attack); return; }
                    if ((tailMs -= dtMs) <= 0f)
                    {
                        tailMs = TailInterval();
                        if (v.tailed) Tailed();
                    }
                    return;
                case Mode.BarrelRoll:
                    if (modeMs <= 0f) Begin(Mode.Zigzag);
                    return;
            }
        }

        void StartGain()
        {
            gainMs = GainMs * (1f + (random() * 2f - 1f) * GainVariance);
            Begin(random() < OrbitChance ? Mode.Orbit : Mode.FlyAway);
        }

        void Tailed()
        {
            if (random() < ZigzagChance) Begin(Mode.Zigzag);
            else if (random() < rollChance) Begin(Mode.BarrelRoll);
            else Begin(Mode.TurnAndFight);
        }

        float TailInterval() => TailCheckMs + (random() * 2f - 1f) * TailCheckDeviationMs;

        void Begin(Mode m)
        {
            Current = m;
            NewLeg = true;
            switch (m)
            {
                case Mode.Attack:
                    OffsetAngle = random() * 2f * (float)System.Math.PI;
                    OffsetUnits = (OffsetMinM + (OffsetMaxM - OffsetMinM) * random()) * UnitsPerMetre;
                    break;
                case Mode.Orbit:
                    modeMs = OrbitMs * (1f + (random() * 2f - 1f) * OrbitVariance);
                    Side = random() < 0.5f ? -1 : 1;
                    break;
                case Mode.FlyAway: tailMs = TailInterval(); break;   // (the service's timer restarts; IsTailed cleared)
                case Mode.BarrelRoll: modeMs = RollMs; Side = random() < 0.5f ? -1 : 1; break;
            }
        }

        /// <summary>BTS_DetectIfTailed: 'toTarget' from us to the target, 'ourForward' and 'targetForward' unit vectors, our size
        /// (the same unit as toTarget): it is behind us, its nose toward us, and we sit within TailSizeFactor x our size of its line.</summary>
        public static bool IsTailed(float tx, float ty, float tz, float fx, float fy, float fz, float gx, float gy, float gz, float ourRadius)
        {
            if (tx * fx + ty * fy + tz * fz >= 0f) return false;     // in front of us
            float along = gx * tx + gy * ty + gz * tz;
            if (along >= 0f) return false;                          // its nose away from us
            float px = tx - gx * along, py = ty - gy * along, pz = tz - gz * along;
            float r = ourRadius * TailSizeFactor;
            return px * px + py * py + pz * pz < r * r;
        }
    }
}
