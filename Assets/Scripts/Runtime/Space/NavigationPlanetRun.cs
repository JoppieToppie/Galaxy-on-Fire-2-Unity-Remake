// NavigationPlanetRun.cs
// Remake: a cruise to another station's planet, flown like an arrival from between the systems (Data.InterstellarSpace) with
// the same reach, Navigation's side; World.PlanetApproach builds the orbit ahead. Two ways in:
//   by hand    the cruise button held with the nose on a planet, any cruise drive: it ends when the drive does or the nose
//              leaves the course
//   the jump   with a drive that flies between the systems (InterstellarSpace.Drive) a planet jump is this run in place of the
//              original's scene: the action on a locked planet, or the autopilot reaching its planet. The autopilot flies it
//              and holds the drive; where the drive can't run (hostiles) or stops on the way, the plain jump goes, unless
//              the jumps are refused by then
// On the planet jump's terms either way: no charge and no energy cells (CruiseDrive.Engage, RunFree), a run by hand only
// while the nose stays near its course (RunFreeDegrees): turned further off, it pays like any cruise until it ends.
// The run hands the ship to SystemJump only when it isn't charging or running a jump of its own (TravelBusy): then it ends.
// Never into an orbit a script sets up on arrival (ScriptedArrival: a story mission's level, a freelance mission's, an event
// quest's): it opens as it was written for, after the planet jump, not in a warp; the jump on its planet is the plain one.
// The backdrop's planets stay with the camera, so a run is flown at a point fixed along the way to the planet when it starts,
// RunApproachUnits out; within ArriveUnits of it the ship goes through SystemJump into that orbit, Session.WarpLocal.
// Single player only: a session's orbits have nothing to swap into.

using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public partial class Navigation
    {
        /// <summary>How close to the nose a planet takes the cruise, how far off the course a run by hand stays free, and how
        /// far off ends it (degrees).</summary>
        const float RunAimDegrees = 8f, RunFreeDegrees = 15f, RunLostDegrees = 35f;

        /// <summary>The planet the cruise is flying to, null without one.</summary>
        public Target RunPlanet { get; private set; }
        /// <summary>The point the run flies at.</summary>
        public Vector3 RunGoal { get; private set; }
        bool runByAutopilot, runEngaged;
        Target refusedRun;

        /// <summary>Where a run's goal starts (14 km): 6 km flown to ArriveUnits, half an approach between the systems (the user,
        /// 2026-11-01: a quicker way to another orbit).</summary>
        public const float RunApproachUnits = 280000f;

        /// <summary>Set by the level (SystemJump): a gate or Khador jump charges or runs, so a run can't hand the ship over.</summary>
        public System.Func<bool> TravelBusy;

        /// <summary>The drive runs on the planet jump's terms now: no cells (UpdateDrive's 'free').</summary>
        bool RunFree => RunPlanet != null
                        && (runByAutopilot || Vector3.Angle(ship.transform.forward, RunGoal - ship.transform.position) <= RunFreeDegrees);

        /// <summary>An orbit whose arrival a script sets up (SpaceLevel's storyOrbit, missionHere, questOrbit): a story mission's
        /// level, a freelance mission's target, an event quest's. Its opening (a placed start, an ambush, a cutscene) is written
        /// for the planet jump's arrival, not a warp's held fire and carried speed (the user, 2026-11-01: the safe way). Runs and
        /// arrivals from between the systems (SystemJump.LeaveInterstellar) take the plain way in there.</summary>
        public static bool ScriptedArrival(int station) =>
            (!Session.FreePlay && Story.IsLevelMission(station)) || Freelance.IsMissionOrbit(station) || SpaceHooks.IsQuietOrbit(station);

        bool RunsPossible => Drive != null && !Interstellar && !layout.alienOrbit && !NetSession.Active && !ArrivalHeld && !Jumping;

        /// <summary>A planet jump asked for: true when this run takes its place. False leaves it to the original's jump.</summary>
        bool StartPlanetRun(Target planet)
        {
            if (!RunsPossible || InterstellarSpace.Drive(db) == null || ScriptedArrival(planet.station)) return false;
            if (RunPlanet == planet) return true;
            RunPlanet = planet;
            bool runs = DriveBlock == CruiseDrive.Stop.None;
            RunPlanet = null;
            if (!runs) return false;
            BeginRun(planet, true);
            return true;
        }

        void BeginRun(Target planet, bool autopilot)
        {
            RunGoal = ship.transform.position + WayTo(planet).normalized * RunApproachUnits * M;
            runByAutopilot = autopilot;
            runEngaged = false;
            if (autopilot)
            {
                SetAutopilot(planet);
                ship.autopilotTarget = () => RunGoal;
            }
            RunPlanet = planet;
            if (!DriveCruising) Drive.Engage();
            Say(string.Format(Localization.Extra("cruisePlanetRun", "Course set: {0}"), planet.name));
        }

        void EndRun()
        {
            if (runByAutopilot && Autopilot) SetAutopilot(null);
            RunPlanet = null;
        }

        /// <summary>Every frame in flight.</summary>
        void UpdatePlanetRun()
        {
            if (LeavingInterstellar) return;
            if (!RunsPossible) { RunPlanet = null; return; }
            bool running = DriveCruising || Drive.Charging;
            if (!running) refusedRun = null;
            if (RunPlanet == null)
            {
                if (running && AimedPlanet() is Target aimed) BeginRun(aimed, false);
                return;
            }
            runEngaged |= DriveCruising;
            if (runByAutopilot)
            {
                if (AutopilotTarget != RunPlanet) { RunPlanet = null; return; }   // switched off
                if (runEngaged && !DriveCruising)
                {
                    // The drive stopped on the way: the plain jump, on the terms StartJump's callers check.
                    var planet = RunPlanet;
                    EndRun();
                    if (PlanetJumpRefused != null && PlanetJumpRefused(planet.station)) return;
                    if (JumpsBlocked != null && JumpsBlocked()) { Refuse(JumpRefusal); return; }
                    JumpToPlanet(planet);
                    return;
                }
            }
            else if (!DriveCruising) { RunPlanet = null; return; }
            var way = RunGoal - ship.transform.position;
            if (!runByAutopilot && Vector3.Angle(ship.transform.forward, way) > RunLostDegrees) { RunPlanet = null; return; }
            if (way.magnitude / M >= InterstellarSpace.ArriveUnits) return;
            if (TravelBusy != null && TravelBusy()) { EndRun(); return; }
            int station = RunPlanet.station;
            SetAutopilot(null);
            Session.WarpLocal = true;
            LeaveAlong(way, station);
        }

        /// <summary>The way to a planet: a backdrop's stand about the camera, not the ship.</summary>
        static Vector3 WayTo(Target planet)
        {
            var cam = Camera.main;
            return cam != null ? planet.Position - cam.transform.position : planet.Position;
        }

        /// <summary>The planet on the nose, none refused: the one the player locked first.</summary>
        Target AimedPlanet()
        {
            if ((JumpsBlocked != null && JumpsBlocked()) || !Story.PlanetJumpsAllowed) return null;
            Target best = null;
            float nearest = RunAimDegrees;
            foreach (var t in Targets)
            {
                if (t.kind != Kind.Planet || t.hidden || t == refusedRun || ScriptedArrival(t.station)) continue;
                float angle = Vector3.Angle(ship.transform.forward, WayTo(t));
                if (t == Locked && angle < RunAimDegrees) angle = 0f;   // on the nose too, else every frame began and lost it
                if (angle < nearest) { nearest = angle; best = t; }
            }
            if (best != null && PlanetJumpRefused != null && PlanetJumpRefused(best.station)) { refusedRun = best; return null; }
            return best;
        }
    }
}
