// CruiseDrive.cs
// Remake: a cruise drive, in-system fast travel for a player flying by hand, as plain rules. The drive is a mod item
// made from the Khador Drive (85) with the remake attributes cruiseSpeed 133 and cruiseRange 134 (Modding/README.md
// "Cruise drives"; the ES2 importer's Slipstream and Khador Slipstream): Shop.IsCruiseDrive, its own one-per-ship kind
// (Shop.KindOf: Kind, so it sits beside a Khador Drive). Navigation runs it on the fast-forward button (NavigationCruiseDrive):
//   charge   a fresh press held for attr 37 ms (the Khador Drive's charge attribute); released, blocked or something in
//            the way: nothing happens. Charged: one leg's energy cells (item 122, attr 38; x2 on Extreme like a Khador
//            jump's, none with the free-jumps cheat) are paid and the ship cruises; too few cells: refused.
//   cruise   the ship's forward speed x attr 105 / 100, eased in over RampUpMs and out over RampDownMs (smoothstep,
//            like the multiplayer cruise), scaled down from SlowUnits to StopUnits of a hull ahead (Proximity); every
//            attr 106 units flown pay another leg; no cells for the next leg: it drops.
//   drop     the button released, the drive blocked (the autopilot, a jump, a menu, the time extender, mining, a docking),
//            hostiles around (the fast-forward's own rule), a hull or an asteroid within StopUnits ahead (CubeAhead for the
//            asteroids' cubes), the locked landmark within ArriveUnits, a hit on the ship, an encounter (CruiseEncounters).
//            A drop needs a fresh press to charge again.
// Modelled on No Man's Sky's pulse travel (the abort at an obstacle, the drop at a safe distance from the target) and
// EVERSPACE 2's cruise mode (the charge delay, the slowdown near objects, the interruptions): the behaviour only.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class CruiseDrive
    {
        public enum Phase { Off, Charging, Cruising }
        /// <summary>Why the drive isn't running (LastStop; Update's 'blocked': None = it may run now).</summary>
        public enum Stop { None, Released, Blocked, Hostiles, Obstacle, Arrived, NoCells, Hit, Encounter }
        public enum Event { None, ChargeStarted, Engaged, LegPaid, Refused, Stopped }

        public const int EnergyCellItem = 122;
        /// <summary>The remake attributes: 133 the speed factor x 100 (400 = x4; > 0 marks a cruise drive), 134 the units
        /// flown per leg of energy cells; the Khador Drive's 37 (charge ms) and 38 (cells) do the rest.</summary>
        public const int SpeedAttr = 133, RangeAttr = 134, ChargeAttr = 37, CellsAttr = 38;
        /// <summary>The remake attribute 135 (> 0): the drive flies the space between the systems (Data.InterstellarSpace).</summary>
        public const int InterstellarAttr = 135;
        /// <summary>Shop.KindOf: the cruise drives' own one-per-ship kind, past every item category.</summary>
        public const int Kind = 1000;
        /// <summary>A hull or an asteroid ahead within StopUnits (400 m) drops the cruise; from SlowUnits (2 km) the speed eases
        /// down to it. The locked landmark (station, gate, docking target) within ArriveUnits (1 km, the fast-forward's limit)
        /// drops it.</summary>
        public const float StopUnits = 8000f, SlowUnits = 40000f, ArriveUnits = 20000f;
        /// <summary>The nose line's half-width (metres) for what lies ahead: the hull sweep's sphere, the asteroids' cubes grown by it.</summary>
        public const float SweepRadius = 12f;
        public const float RampUpMs = 800f, RampDownMs = 1200f;

        public readonly int item, chargeMs, cellsPerLeg, legUnits;
        public readonly float speedFactor;
        /// <summary>A paid leg survives a stop (interstellar space, where a crossing is priced by the distance flown): the next
        /// engage flies it out and pays nothing.</summary>
        public bool keepLeg;

        public Phase State { get; private set; } = Phase.Off;
        public Stop LastStop { get; private set; } = Stop.None;
        /// <summary>Ms into the charge.</summary>
        public float Timer { get; private set; }
        /// <summary>Units left of the paid leg.</summary>
        public float LegLeft { get; private set; }
        /// <summary>The ramp 0..1 (RampUpMs / RampDownMs), what Factor eases on.</summary>
        public float Level { get; private set; }
        /// <summary>1 with nothing ahead, down to 0 at a hull StopUnits ahead (kept after a stop while the ramp runs down).</summary>
        public float Proximity { get; private set; } = 1f;
        /// <summary>With Event.Engaged / LegPaid: the energy cells the hold loses now (0 with the free-jumps cheat).</summary>
        public int CellsDue { get; private set; }
        bool wasHeld;
        float carryMs;   // CarrySpeed's ramp down while it runs, then RampDownMs

        public CruiseDrive(int item, int speedPercent, int legUnits, int chargeMs, int cells, bool extreme)
        {
            this.item = item;
            speedFactor = Mathf.Max(1f, speedPercent / 100f);
            this.legUnits = Mathf.Max(1, legUnits);
            this.chargeMs = Mathf.Max(0, chargeMs);
            cellsPerLeg = Mathf.Max(0, cells) * (extreme ? 2 : 1);
        }

        public bool Cruising => State == Phase.Cruising;
        public bool Charging => State == Phase.Charging;
        public float ChargeRate => State != Phase.Charging ? 0f : chargeMs > 0 ? Mathf.Clamp01(Timer / chargeMs) : 1f;
        /// <summary>The forward speed factor now: 1 off, up to speedFactor cruising with nothing ahead.</summary>
        public float Factor => 1f + (speedFactor - 1f) * Level * Level * (3f - 2f * Level) * Proximity;

        /// <summary>One frame. 'held': the button; 'blocked': why the drive may not run now (None = it may); 'movedUnits':
        /// the ship's movement this frame; 'obstacleUnits': the first hull ahead (float.MaxValue = none); 'landmarkUnits':
        /// the locked landmark (float.MaxValue = none); 'cellsInCargo' the energy cells in the hold; 'free': the cheat.</summary>
        public Event Update(float dtMs, bool held, Stop blocked, float movedUnits, float obstacleUnits, float landmarkUnits,
                            int cellsInCargo, bool free)
        {
            CellsDue = 0;
            dtMs = Mathf.Max(0f, dtMs);
            var ev = Event.None;
            bool press = held && !wasHeld, wasCruising = State == Phase.Cruising;
            wasHeld = held;
            if (State != Phase.Off)
            {
                Stop stop = !held ? Stop.Released : blocked != Stop.None ? blocked
                          : obstacleUnits < StopUnits ? Stop.Obstacle : landmarkUnits < ArriveUnits ? Stop.Arrived : Stop.None;
                if (stop != Stop.None) { End(stop); ev = Event.Stopped; }
            }
            switch (State)
            {
                case Phase.Off:
                    if (ev == Event.None && press && blocked == Stop.None && obstacleUnits >= StopUnits && landmarkUnits >= ArriveUnits)
                    {
                        State = Phase.Charging;
                        Timer = 0f;
                        LastStop = Stop.None;
                        ev = Event.ChargeStarted;
                    }
                    break;
                case Phase.Charging:
                    Timer += dtMs;
                    if (Timer >= chargeMs)
                    {
                        bool paid = keepLeg && LegLeft > 0f;
                        if (paid || free || cellsInCargo >= cellsPerLeg)
                        {
                            State = Phase.Cruising;
                            if (!paid)
                            {
                                LegLeft = legUnits;
                                CellsDue = free ? 0 : cellsPerLeg;
                            }
                            ev = Event.Engaged;
                        }
                        else { End(Stop.NoCells); ev = Event.Refused; }
                    }
                    break;
                case Phase.Cruising:
                    LegLeft -= Mathf.Max(0f, movedUnits);
                    while (LegLeft <= 0f)
                    {
                        if (free) { LegLeft += legUnits; continue; }
                        if (cellsInCargo - CellsDue >= cellsPerLeg)
                        {
                            LegLeft += legUnits;
                            CellsDue += cellsPerLeg;
                            ev = Event.LegPaid;
                            continue;
                        }
                        End(Stop.NoCells);
                        ev = Event.Stopped;
                        break;
                    }
                    break;
            }
            if (State != Phase.Off)
                Proximity = obstacleUnits >= SlowUnits ? 1f : Mathf.Clamp01((obstacleUnits - StopUnits) / (SlowUnits - StopUnits));
            // The ramp: up from the frame after the engage (the engaging frame's dt is the charge's), down at once.
            bool up = wasCruising && State == Phase.Cruising;
            Level = Mathf.MoveTowards(Level, up ? 1f : State == Phase.Cruising ? Level : 0f, dtMs / (up ? RampUpMs : carryMs > 0f ? carryMs : RampDownMs));
            if (Level <= 0f || State != Phase.Off) carryMs = 0f;
            return ev;
        }

        /// <summary>The ship comes in at full speed (into or out of the space between the systems): no cruise, the ramp runs down
        /// from the top over 'ms'.</summary>
        public void CarrySpeed(float ms) { Level = 1f; carryMs = ms; }

        /// <summary>Where the line from 'from' along 'dir' (a unit vector) enters the axis-aligned cube of half-size 'half' about
        /// 'centre' (an asteroid's, PlayerCollision's cube), in the line's own metres: 0 from inside it, float.MaxValue when it
        /// misses or the cube lies behind. The slab test.</summary>
        public static float CubeAhead(Vector3 from, Vector3 dir, Vector3 centre, float half)
        {
            float near = 0f, far = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float f = from[i] - centre[i], d = dir[i];
                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (Mathf.Abs(f) > half) return float.MaxValue;
                    continue;
                }
                float t1 = (-half - f) / d, t2 = (half - f) / d;
                if (t1 > t2) { float t = t1; t1 = t2; t2 = t; }
                if (t1 > near) near = t1;
                if (t2 < far) far = t2;
                if (far < near) return float.MaxValue;
            }
            return near;
        }

        /// <summary>A hit on the ship (EVERSPACE 2's interruption): a charge or cruise ends; false = nothing ran.</summary>
        public bool Interrupt(Stop reason = Stop.Hit)
        {
            if (State == Phase.Off) return false;
            End(reason);
            return true;
        }

        void End(Stop reason)
        {
            State = Phase.Off;
            LastStop = reason;
            Timer = 0f;
            if (!keepLeg) LegLeft = 0f;
        }
    }
}
