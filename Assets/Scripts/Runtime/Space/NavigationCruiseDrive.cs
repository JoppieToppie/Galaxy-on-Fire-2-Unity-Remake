// NavigationCruiseDrive.cs
// Remake: the mounted cruise drive (Flight.CruiseDrive, a mod item made from the Khador Drive with cruiseSpeed; the
// ES2 importer's Slipstream and Khador Slipstream) on the fast-forward button while the player flies by hand (the
// original fast-forward keeps the button on the autopilot and the approaches, CanFastForward). Its forward speed factor
// joins the multiplayer cruise's in CruiseFactor (ShipController's step), so it looks and sounds like a boost
// (FlightModel.CruiseVisual). The energy cells go the Khador Drive's way (GalaxyMap.RemoveCells, "-1t Energy Cells";
// 583 + the cells when there aren't enough; the free-jumps cheat makes it free). What stops it says so on the HUD with
// the item's name (Extra cruiseDrive*). Something ahead (ObstacleAheadUnits): HullCollision.Sweep along the nose for what
// the player is pushed out of (stations, freighters, objects: HullBody.obstacle), not fighters; the same objects' collision
// volumes (Obstacle.RayDistance) where their hull takes no part, so the Original and Mixed ship-collision settings see them
// too; and the live asteroids' cubes (Target.isAsteroid, PlayerCollision's cube; they have no hulls) by CruiseDrive.CubeAhead,
// so a field slows and drops the cruise like a station does, as No Man's Sky's pulse and EVERSPACE 2's cruise are stopped by
// what lies in the way. Between the systems, the last leg the hold can pay is said (Extra interstellarLastLeg): the next one
// would fall the ship out.
// Encounters (CruiseEncounters, the level's EncountersAllowed and SpawnEncounter -> CruiseEncounterSpawner): an ambush
// appears AmbushAheadUnits ahead and drops the drive (Stop.Encounter; a flyby doesn't); a visible one is a signal, a
// waypoint target SignalAheadUnits ahead and to one side (lockable, the autopilot flies to it) that spawns when the ship
// comes within SignalReachUnits and fades after SignalLifeMs or SignalLostUnits away; a "quest" entry starts its quest
// graph (GameHooks.StartQuest, single player). Between the systems (CruiseEncounters.Between) every one pulls the ship out of
// the slipstream, the drive dropped and the autopilot off: in deep space the warp eases out over PullOutMs (nobody fires or is
// hurt in it, SpaceLevel.WarpHolds) and the encounter is then met AmbushAheadUnits ahead, out of the slipstream until the
// drive is back at full cruise (OutOfSlipstream); at the nearest system the ship falls out there (Leave) and the encounter
// waits in that orbit (Session.CarriedEncounter) until its arrival's warp has ended. None rolls while a system's orbit is
// being built ahead (ApproachRuns), on a dive into the Void or on the way out.

using System;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public partial class Navigation
    {
        /// <summary>The mounted cruise drive's rules, null without one (Setup).</summary>
        public CruiseDrive Drive { get; private set; }
        string driveName;
        Vector3 lastDrivePos;
        bool driveMoved;

        /// <summary>Set by the level: encounters may come in this orbit (its own traffic, nothing scripted, CruiseEncounterRules).</summary>
        [NonSerialized] public bool EncountersAllowed;
        /// <summary>Set by the level: puts an encounter into the orbit at a game-space point, facing a game-space direction.</summary>
        [NonSerialized] public Action<CruiseEncounters.Entry, Vector3, Vector3> SpawnEncounter;
        CruiseEncounters encounters;
        int systemSecurity;
        Target signalTarget;
        CruiseEncounters.Entry signalEntry;
        float signalMs;
        /// <summary>Set by the space between the systems (World.InterstellarField): a system's orbit is being built ahead.</summary>
        [NonSerialized] public bool ApproachRuns;
        /// <summary>Between the systems: an encounter pulled the ship out here, until the drive is back at full cruise.</summary>
        public bool OutOfSlipstream { get; private set; }
        /// <summary>The warp easing out of that pull-out, 1..0 (Visuals.WarpSky); the encounter comes at 0.</summary>
        public float PullOut => pullOutMs / PullOutMs;
        public const float PullOutMs = 2500f;
        float pullOutMs;
        CruiseEncounters.Entry pullOutEntry, carried;
        public const float AmbushAheadUnits = 22000f, SignalAheadUnits = 45000f, SignalSideUnits = 25000f, SignalReachUnits = 12000f,
                           SignalLostUnits = 200000f, SignalLifeMs = 180000f;

        /// <summary>A cruise drive is fitted and may charge or cruise now (the HUD's button and hint).</summary>
        public bool CanUseDrive => Drive != null && DriveBlock == CruiseDrive.Stop.None;
        /// <summary>The drive cruises (its speed is in CruiseFactor; Cruising follows it).</summary>
        public bool DriveCruising => Drive != null && Drive.Cruising;
        /// <summary>A signal from the cruise waits in the orbit (its waypoint target).</summary>
        public bool SignalPlaced => signalTarget != null;

        void SetupDrive(Database database)
        {
            // Remake: a Khador Slipstream the options switch off does nothing, but in interstellar space (it brought the ship
            // here, and the option can change in the pause menu: the ship would be stranded).
            var it = layout.interstellar ? Shop.MountedCruiseDrive(database) : Shop.WorkingCruiseDrive(database);
            Drive = it == null ? null
                : new CruiseDrive(it.index, it.Attr(CruiseDrive.SpeedAttr),
                                  layout.interstellar ? (int)InterstellarSpace.LegUnits : it.Attr(CruiseDrive.RangeAttr, 60000),
                                  it.Attr(CruiseDrive.ChargeAttr, 2000), it.Attr(CruiseDrive.CellsAttr, 1), Session.IsExtreme) { keepLeg = layout.interstellar };
            driveName = it != null ? GameNames.Item(it.index) : null;
            driveMoved = false;
            encounters = Drive != null ? new CruiseEncounters(Modding.ModEncounters.All()) : null;
            systemSecurity = database.Systems.Find(s => s.index == layout.systemIndex)?.securityLevel ?? 0;
            OutOfSlipstream = false;
            pullOutMs = 0f;
            pullOutEntry = null;
            if (Session.CarriedEncounter != null && !layout.interstellar) { carried = Session.CarriedEncounter; Session.CarriedEncounter = null; }
        }

        /// <summary>Why the drive may not run now; None = it may. The fast-forward's own rules (hostiles, a jump, the time
        /// extender) plus the autopilot, the approaches (the original fast-forward has the button there), a halt.</summary>
        CruiseDrive.Stop DriveBlock =>
            Drive == null || ship == null ? CruiseDrive.Stop.Blocked
            : HostilesPresent ? CruiseDrive.Stop.Hostiles
            : ArrivalHeld || Jumping || LeavingInterstellar || pullOutMs > 0f || halted || (Autopilot && !Interstellar && RunPlanet == null) || TimeExtender.Active || InputHalted
              || (mining != null && mining.State != Mining.Phase.Idle) || (Docking != null && Docking.Busy) ? CruiseDrive.Stop.Blocked
            : CruiseDrive.Stop.None;

        /// <summary>Every frame, before UpdateCruise (CruiseFactor reads the drive's Factor).</summary>
        void UpdateDrive(float dtMs)
        {
            if (Drive == null || ship == null) return;
            var p = ship.transform.position;
            float moved = driveMoved ? (p - lastDrivePos).magnitude / M / WorldScale : 0f;   // a hop's cells by the space's own scale
            lastDrivePos = p;
            driveMoved = true;
            // The button: the original fast-forward wins while it is allowed (the autopilot and the approaches).
            // Between the systems the autopilot holds it too: a crossing is the drive's whole flight (NavigationInterstellar).
            bool held = Interstellar ? fastForwardHeld || Autopilot : (fastForwardHeld && !CanFastForward) || (RunPlanet != null && runByAutopilot);
            bool free = Cheats.FreeJumps || RunFree;   // a flight to a planet is the planet jump's: nothing to pay
            var ev = Drive.Update(dtMs, held, DriveBlock, moved, Drive.State == CruiseDrive.Phase.Off && !held ? float.MaxValue : ObstacleAheadUnits(),
                                  LockedLandmarkUnits(), GalaxyMap.CellsInCargo(), free);
            switch (ev)
            {
                case CruiseDrive.Event.Engaged:
                case CruiseDrive.Event.LegPaid:
                    if (Drive.CellsDue > 0)
                    {
                        GalaxyMap.RemoveCells(Drive.CellsDue);
                        Say($"-{Drive.CellsDue}t {GameNames.Item(GalaxyMap.EnergyCellItem)}");
                        if (Interstellar && GalaxyMap.CellsInCargo() < Drive.cellsPerLeg)
                            Say(string.Format(Localization.Extra("interstellarLastLeg", "{0}: last leg of energy cells"), driveName));
                    }
                    break;
                case CruiseDrive.Event.Refused:
                    Say($"{Localization.Get(583)} {Drive.cellsPerLeg}.");
                    break;
                case CruiseDrive.Event.Stopped:
                    SayDriveStop(Drive.LastStop);
                    break;
            }
            if ((ev == CruiseDrive.Event.Refused || ev == CruiseDrive.Event.Stopped) && Drive.LastStop == CruiseDrive.Stop.NoCells) FallOutOfSlipstream();
            UpdateEncounters(dtMs);
        }

        /// <summary>A hit on the ship (PlayerHealth.Hit): the drive's charge or cruise ends.</summary>
        public void InterruptDrive()
        {
            if (Drive != null && Drive.Interrupt(CruiseDrive.Stop.Hit)) SayDriveStop(CruiseDrive.Stop.Hit);
        }

        void SayDriveStop(CruiseDrive.Stop stop)
        {
            string text = stop switch
            {
                CruiseDrive.Stop.Obstacle => Localization.Extra("cruiseDriveObstacle", "{0} disengaged: obstacle ahead"),
                CruiseDrive.Stop.Hostiles => Localization.Extra("cruiseDriveHostiles", "{0} disengaged: hostile ships"),
                CruiseDrive.Stop.Arrived => Localization.Extra("cruiseDriveArrived", "{0} disengaged: destination ahead"),
                CruiseDrive.Stop.Hit => Localization.Extra("cruiseDriveHit", "{0} disengaged: hit"),
                CruiseDrive.Stop.NoCells => Localization.Get(583) + " " + Drive.cellsPerLeg + ".",
                _ => null,
            };
            if (text != null) Say(string.Format(text, driveName));
        }

        /// <summary>The first hull the ship is pushed out of (stations, freighters, objects), such an object's collision volumes
        /// where its hull takes no part, or live asteroid within CruiseDrive.SlowUnits along its nose, in units; float.MaxValue
        /// = none.</summary>
        float ObstacleAheadUnits()
        {
            var t = ship.transform;
            var from = t.position;
            var to = from + t.forward * (CruiseDrive.SlowUnits * M);
            float best = HullCollision.Sweep(from, to, CruiseDrive.SweepRadius, b => b.obstacle != null && !b.transform.IsChildOf(t), out float d) ? d / M : float.MaxValue;
            best = Mathf.Min(best, VolumeAheadUnits(from, t.forward, best, t));
            return Mathf.Min(best, AsteroidAheadUnits(from, t.forward, best));
        }

        /// <summary>The first obstacle without a hull in play (Obstacle.UsesHull: the Original setting, or Mixed for a flyable
        /// ship's) whose volumes the nose line enters within 'limit' units; float.MaxValue = none.</summary>
        float VolumeAheadUnits(Vector3 from, Vector3 dir, float limit, Transform own)
        {
            float reach = Mathf.Min(limit, CruiseDrive.SlowUnits) * M, best = float.MaxValue;
            var all = Obstacle.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || !o.Active || o.UsesHull || o.transform.IsChildOf(own)) continue;
                float m = o.RayDistance(from, dir);
                if (m < best) best = m;
            }
            return best <= reach ? best / M : float.MaxValue;
        }

        /// <summary>The first live asteroid whose cube (the ship collision's, grown by the sweep's radius) the nose line enters
        /// within 'limit' units (at most SlowUnits); float.MaxValue = none. Asteroids have no hull, so their cubes are tested.</summary>
        float AsteroidAheadUnits(Vector3 from, Vector3 dir, float limit)
        {
            float reach = Mathf.Min(limit, CruiseDrive.SlowUnits) * M, best = float.MaxValue;
            var all = GoF2Remake.Flight.Target.All;   // the asteroid components, not Navigation's own Target
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.isAsteroid || !a.Alive) continue;
                var c = a.transform.position;
                float broad = reach + a.radius + CruiseDrive.SweepRadius;
                if ((c - from).sqrMagnitude > broad * broad) continue;
                float m = CruiseDrive.CubeAhead(from, dir, c, a.radius + CruiseDrive.SweepRadius);
                if (m < best) best = m;
            }
            return best <= reach ? best / M : float.MaxValue;
        }

        /// <summary>The locked landmark (station, gate, docking target, wormhole) in units; float.MaxValue = none.</summary>
        float LockedLandmarkUnits() => LandmarkLocked && !Interstellar ? (Locked.Position - ship.transform.position).magnitude / M : float.MaxValue;   // (between the systems the zone and the arrival stop it)

        // ---- encounters ------------------------------------------------------------------------------------------

        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;
        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 GameDir(Vector3 unityDir) => new Vector3(unityDir.x, unityDir.y, -unityDir.z);

        bool EncounterAllowed(CruiseEncounters.Entry e) =>
            CruiseEncounters.CanHappen(e, Session.Rank, systemSecurity, Session.FreePlay)
            && (e.kind != CruiseEncounters.Quest || (!string.IsNullOrEmpty(e.quest) && GameHooks.StartQuest != null && !GoF2Remake.Data.NetSession.Active))
            && (Interstellar ? CruiseEncounters.Between(e, homeTakes, 1f) != CruiseEncounters.PullOut.None
                             : !e.visible || signalTarget == null);   // one signal at a time

        /// <summary>Between the systems: the nearest system shown, and whether its orbit may have an encounter brought to it.</summary>
        Target home;
        bool homeTakes;

        void UpdateEncounters(float dtMs)
        {
            UpdateSignal(dtMs);
            UpdatePullOut(dtMs);
            UpdateCarried();
            if (encounters == null || !EncountersAllowed || SpawnEncounter == null || RunPlanet != null) return;
            bool rolls = !Interstellar || InterstellarRolls();
            var e = encounters.Update(dtMs, Drive.Cruising && rolls, EncounterAllowed, () => UnityEngine.Random.value);
            if (e == null) return;
            if (Interstellar) { PullOutOfSlipstream(e); return; }
            var t = ship.transform;
            var pos = ToGame(t.position);
            var fwd = GameDir(t.forward);
            if (e.kind == CruiseEncounters.Quest)
            {
                GameHooks.StartQuest?.Invoke(e.quest, true);   // the graph does the rest (radio, spawns, waypoints)
                return;
            }
            if (e.visible)
            {
                var side = Vector3.Cross(fwd, Vector3.up).normalized * (UnityEngine.Random.value < 0.5f ? -1f : 1f) * UnityEngine.Random.Range(0.3f, 1f) * SignalSideUnits;
                PlaceSignal(e, pos + fwd * SignalAheadUnits + side);
                return;
            }
            SpawnEncounter(e, pos + fwd * AmbushAheadUnits, fwd);
            if (e.StopsTheDrive && Drive.Interrupt(CruiseDrive.Stop.Encounter))
                Say(string.Format(Localization.Extra("cruiseDriveEncounter", "{0} disengaged: {1}"), driveName, EncounterName(e)));
        }

        /// <summary>Between the systems, an encounter may roll now: not on the way out, on a dive into the Void, out of the
        /// slipstream already or with a system's orbit built ahead (the arrival is near: its own encounters come there).</summary>
        bool InterstellarRolls()
        {
            if (LeavingInterstellar || OutOfSlipstream || ApproachRuns || Zone != null || (VoidDive != null && AutopilotTarget == VoidDive)) return false;
            home = null;
            float nearest = float.MaxValue;
            var at = ship.transform.position;
            foreach (var t in Targets)
            {
                if (t.kind != Kind.System || t.hidden || t.planetOf != null) continue;
                float d = (t.Position - at).sqrMagnitude;
                if (d < nearest) { nearest = d; home = t; }
            }
            homeTakes = home != null && CruiseEncounterRules.Allowed(db, home.station, home.system, false) && !ScriptedArrival(home.station)
                        && (PlanetJumpRefused == null || !PlanetJumpRefused(home.station));
            return true;
        }

        /// <summary>An encounter between the systems: out of the slipstream, in deep space or at the nearest system.</summary>
        void PullOutOfSlipstream(CruiseEncounters.Entry e)
        {
            var way = CruiseEncounters.Between(e, homeTakes, UnityEngine.Random.value);
            if (way == CruiseEncounters.PullOut.None) return;
            Drive.Interrupt(CruiseDrive.Stop.Encounter);
            SetAutopilot(null);
            Say(string.Format(Localization.Extra("cruiseDriveEncounter", "{0} disengaged: {1}"), driveName, EncounterName(e)));
            if (way == CruiseEncounters.PullOut.AtSystem && home != null)
            {
                Session.CarriedEncounter = e;
                Say(string.Format(Localization.Extra("interstellarPulledOut", "Pulled out of the slipstream at {0}"), home.name));
                Leave(home);
                return;
            }
            OutOfSlipstream = true;
            pullOutEntry = e;
            pullOutMs = PullOutMs;
        }

        /// <summary>Out of the slipstream in deep space: the warp eases out, then the encounter is there; back in once the drive
        /// cruises at full again.</summary>
        void UpdatePullOut(float dtMs)
        {
            if (!OutOfSlipstream) return;
            if (pullOutMs > 0f)
            {
                pullOutMs = Mathf.Max(0f, pullOutMs - Mathf.Max(0f, dtMs));
                if (pullOutMs > 0f || pullOutEntry == null) return;
                var e = pullOutEntry;
                pullOutEntry = null;
                SpawnAhead(e);
                return;
            }
            if (Drive.Cruising && Drive.Level >= 1f) OutOfSlipstream = false;
        }

        /// <summary>The encounter that pulled the ship out at this system: here once the arrival's warp has ended, if the orbit
        /// may have one.</summary>
        void UpdateCarried()
        {
            if (carried == null || Interstellar || ArrivalHeld || ship == null) return;
            var e = carried;
            carried = null;
            if (!EncountersAllowed || SpawnEncounter == null) return;
            encounters?.Came();
            if (e.kind == CruiseEncounters.Quest) { GameHooks.StartQuest?.Invoke(e.quest, true); return; }
            SpawnAhead(e);
        }

        /// <summary>An encounter met at once (one that pulled the ship out), AmbushAheadUnits ahead.</summary>
        void SpawnAhead(CruiseEncounters.Entry e)
        {
            var t = ship.transform;
            var fwd = GameDir(t.forward);
            SpawnEncounter?.Invoke(e, ToGame(t.position) + fwd * AmbushAheadUnits, fwd);
        }

        /// <summary>The signal's / encounter's name on the HUD: the entry's own (a mod's), else its kind's.</summary>
        public static string EncounterName(CruiseEncounters.Entry e)
        {
            if (!string.IsNullOrEmpty(e.name)) return e.name;
            return e.kind switch
            {
                CruiseEncounters.Pirates => Localization.Extra("encounterPirates", "Pirates"),
                CruiseEncounters.Trader => Localization.Extra("encounterTrader", "Trade convoy"),
                CruiseEncounters.Derelict => Localization.Extra("encounterDerelict", "Debris field"),
                CruiseEncounters.Distress => Localization.Extra("encounterDistress", "Distress call"),
                CruiseEncounters.Flyby => Localization.Extra("encounterFlyby", "Passing wing"),
                CruiseEncounters.Battle => Localization.Extra("encounterBattle", "Battle"),
                CruiseEncounters.Stash => Localization.Extra("encounterStash", "Hidden stash"),
                CruiseEncounters.Depot => Localization.Extra("encounterDepot", "Resource depot"),
                CruiseEncounters.HighRisk => Localization.Extra("encounterHighRisk", "High-risk area"),
                CruiseEncounters.Minesweeper => Localization.Extra("encounterMinesweeper", "Minesweeper"),
                CruiseEncounters.Challenge => Localization.Extra("encounterChallenge", "Shooting Challenge"),
                CruiseEncounters.Needy => Localization.Extra("encounterNeedy", "Ship In Need"),
                CruiseEncounters.LootDrone => Localization.Extra("encounterLootDrone", "Loot drone"),
                CruiseEncounters.Rift => Localization.Extra("encounterRift", "Rift"),
                // (a trap passes for an unknown signal, as EVERSPACE 2's does)
                _ => Localization.Extra("encounterUnknown", "Unknown signal"),
            };
        }

        void PlaceSignal(CruiseEncounters.Entry e, Vector3 gameAt)
        {
            signalEntry = e;
            signalMs = 0f;
            signalTarget = new Target { kind = Kind.Waypoint, fixedPosition = ToUnity(gameAt), name = EncounterName(e), freelance = true };
            Targets.Add(signalTarget);
            Say(string.Format(Localization.Extra("cruiseSignalFound", "Signal detected: {0}"), signalTarget.name), HudMessageKind.Scan);
        }

        void UpdateSignal(float dtMs)
        {
            if (signalTarget == null || ship == null) return;
            signalMs += Mathf.Max(0f, dtMs);
            float units = (signalTarget.Position - ship.transform.position).magnitude / M;
            if (units < SignalReachUnits)
            {
                var e = signalEntry;
                var at = ToGame(signalTarget.Position);
                ClearSignal();
                SpawnEncounter?.Invoke(e, at, GameDir(ship.transform.forward));
                return;
            }
            if (signalMs > SignalLifeMs || units > SignalLostUnits)
            {
                ClearSignal();
                Say(Localization.Extra("cruiseSignalLost", "Signal lost"));
            }
        }

        void ClearSignal()
        {
            if (signalTarget == null) return;
            Targets.Remove(signalTarget);
            if (Locked == signalTarget || Candidate == signalTarget) { Locked = Candidate = null; LockTimer = 0f; }
            if (AutopilotTarget == signalTarget) SetAutopilot(null);
            signalTarget = null;
            signalEntry = null;
        }
    }
}
