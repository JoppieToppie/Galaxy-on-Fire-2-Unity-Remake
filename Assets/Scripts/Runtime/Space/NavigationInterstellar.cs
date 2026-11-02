// NavigationInterstellar.cs
// Remake: the drive that flies between the systems (Data.InterstellarSpace, attr 135), Navigation's side. In an orbit the
// quick menu (and the Khador Drive's key, without one) offers it by the item's name, under the Khador Drive's refusals (525
// on a mission, 612 with volatile goods, the story's own Void jumps); SystemJump plays the charge and the scene. Between the
// systems every system on the star map is a Kind.System target: drawn and locked like the jumpgate, the autopilot flies to
// it and holds the drive (NavigationCruiseDrive), the fast-forward button holds it by hand, and within ArriveUnits of a
// system the ship comes out there (under the autopilot only at its own). The story's requirements for the arrival orbit
// refuse as a planet jump's do. The Void is no place here: the autopilot menu's last entry (InterstellarSpace.VoidShown) is
// a dive into it (VoidDive), never drawn or locked, its distance nowhere, that ends in the alien orbit after a random time.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public partial class Navigation
    {
        /// <summary>The autopilot menu's "1." to "8.": the programmed destination's system, then the nearest.</summary>
        const int SystemMenuEntries = 8;

        /// <summary>This is the space between the systems, not an orbit.</summary>
        public bool Interstellar => layout != null && layout.interstellar;
        /// <summary>A system was reached: the Khador scene into it runs.</summary>
        public bool LeavingInterstellar { get; private set; }
        /// <summary>The drive's quick menu entry was picked and allowed (SystemJump charges).</summary>
        public event Action InterstellarRequested;
        /// <summary>A system's edge was reached: its arrival station (SystemJump jumps there).</summary>
        public event Action<int> SystemReached;
        Target refusedSystem;

        /// <summary>The autopilot menu's dive into the Void: a Kind.System target outside Targets (so never drawn, locked or
        /// measured) whose position the dive keeps ahead of the ship, its edge as far out as the time left takes (VoidDiveMs).</summary>
        public Target VoidDive { get; private set; }
        /// <summary>The dive's time left on the drive (ms); counts only while it cruises.</summary>
        public float VoidDiveMs { get; set; }
        Vector3 diveWay;

        void SetupInterstellar()
        {
            if (!Interstellar) return;
            foreach (var p in InterstellarSpace.Systems(db))
                Targets.Add(new Target { kind = Kind.System, fixedPosition = OrbitLayout.ToUnity(p.position), station = p.station, system = p.system, name = p.name });
            if (InterstellarSpace.VoidShown)
                VoidDive = new Target { kind = Kind.System, station = Session.VoidOrbit, system = InterstellarSpace.VoidSystem, name = Localization.Get(415) };
        }

        /// <summary>The dive picked from the menu: a new way off the disc and a new length, so no two are alike.</summary>
        void StartVoidDive()
        {
            var at = ship.transform.position / M;
            var forward = ship.transform.forward;
            diveWay = OrbitLayout.DirToUnity(InterstellarSpace.DiveWay(new Vector3(at.x, at.y, -at.z), new Vector3(forward.x, forward.y, -forward.z)));
            VoidDiveMs = UnityEngine.Random.Range(InterstellarSpace.DiveMinMs, InterstellarSpace.DiveMaxMs);
            PlaceVoidDive();
        }

        void PlaceVoidDive()
        {
            int speed = InterstellarSpace.Drive(db)?.Attr(CruiseDrive.SpeedAttr, 100) ?? 100;
            VoidDive.fixedPosition = ship.transform.position + diveWay * (InterstellarSpace.DiveUnits(speed, VoidDiveMs) * M);
        }

        /// <summary>Every frame of a dive: the time runs down while the drive cruises, and at its end the ship falls in.</summary>
        void UpdateVoidDive()
        {
            if (VoidDive == null || AutopilotTarget != VoidDive) return;
            if (DriveCruising) VoidDiveMs -= Time.deltaTime * 1000f;
            if (VoidDiveMs <= 0f) { VoidDiveMs = 0f; SetAutopilot(null); LeaveAlong(diveWay, Session.VoidOrbit); return; }
            PlaceVoidDive();
        }

        void AddSlipstreamEntry(List<Target> list)
        {
            if (Interstellar || InterstellarSpace.Drive(db) == null) return;
            if (Docking != null && Docking.State != ObjectDocking.Phase.Idle && Docking.State != ObjectDocking.Phase.Approach) return;
            list.Add(new Target { kind = Kind.Slipstream, name = driveName });
        }

        void RequestInterstellar()
        {
            if ((JumpsBlocked != null && JumpsBlocked()) || (KhadorBlocked != null && KhadorBlocked()) || Story.ForcedKhadorTarget(Session.StationIndex) != null) { Say(JumpRefusal); return; }
            if (GalaxyMap.HasVolatileGoods) { Say(Localization.Get(612)); return; }
            InterstellarRequested?.Invoke();
        }

        List<Target> SystemEntries(List<Target> list)
        {
            var systems = Targets.FindAll(t => t.kind == Kind.System && !t.hidden);
            int goal = db.Stations.Find(s => s.index == Session.ProgrammedStation)?.system ?? -1;
            var at = ship.transform.position;
            systems.Sort((a, b) => a.system == goal != (b.system == goal) ? (a.system == goal ? -1 : 1)
                                   : (a.Position - at).sqrMagnitude.CompareTo((b.Position - at).sqrMagnitude));
            int room = VoidDive != null ? SystemMenuEntries - 1 : SystemMenuEntries;
            for (int i = 0; i < systems.Count && i < room; i++) list.Add(systems[i]);
            if (VoidDive != null) list.Add(VoidDive);
            return list;
        }

        /// <summary>Every frame in flight: what the map shows now (a system can go, or be bought), and the systems' edges.</summary>
        void UpdateInterstellar()
        {
            if (!Interstellar || LeavingInterstellar) return;
            UpdateVoidDive();
            if (LeavingInterstellar) return;
            var visible = GalaxyMap.Visibility(db);
            var at = ship.transform.position;
            foreach (var t in Targets)
            {
                if (t.kind != Kind.System) continue;
                bool hide = t.system >= visible.Length || !visible[t.system];
                if (hide != t.hidden)
                {
                    t.hidden = hide;
                    if (hide && (Locked == t || Candidate == t)) { Locked = Candidate = null; LockTimer = 0f; }
                    if (hide && AutopilotTarget == t) SetAutopilot(null);
                }
                if (hide) continue;
                // The autopilot flies past the others' edges to its own (a dive into the Void past all): on its curve out of
                // the start a flight off the galaxy's disc came out at Weymire on the way.
                if (AutopilotTarget != null && AutopilotTarget.kind == Kind.System && t != AutopilotTarget) continue;
                float units = (t.Position - at).magnitude / M;
                if (t == refusedSystem)
                {
                    if (units > InterstellarSpace.ArriveUnits * 1.5f) refusedSystem = null;
                    continue;
                }
                if (units >= InterstellarSpace.ArriveUnits) continue;
                if (PlanetJumpRefused != null && PlanetJumpRefused(t.station))
                {
                    refusedSystem = t;
                    if (AutopilotTarget == t) SetAutopilot(null);
                    continue;
                }
                Leave(t);
                return;
            }
        }

        void Leave(Target system)
        {
            SetAutopilot(null);
            LeaveAlong(system.Position - ship.transform.position, system.station);
        }

        /// <summary>Out of this level at speed into 'station''s orbit, the ship flying along 'way'.</summary>
        void LeaveAlong(Vector3 way, int station)
        {
            LeavingInterstellar = true;
            if (way.sqrMagnitude > 1f) Session.WarpHeading = Quaternion.Inverse(Quaternion.LookRotation(way, Vector3.up)) * ship.transform.rotation;
            Session.WarpView = ship.transform.InverseTransformPoint(chase.transform.position);
            Session.WarpViewTurn = Quaternion.Inverse(ship.transform.rotation) * chase.transform.rotation;
            SystemReached?.Invoke(station);
        }

        /// <summary>No cells for the next leg: the ship falls out at the nearest system, so nobody is stranded between them.</summary>
        void FallOutOfSlipstream()
        {
            if (!Interstellar || LeavingInterstellar) return;
            Target best = null;
            float nearest = float.MaxValue;
            var at = ship.transform.position;
            foreach (var t in Targets)
            {
                if (t.kind != Kind.System || t.hidden) continue;
                float d = (t.Position - at).sqrMagnitude;
                if (d < nearest) { nearest = d; best = t; }
            }
            if (best == null) return;
            Say(string.Format(Localization.Extra("interstellarNoCells", "{0} collapsed: falling out at {1}"), driveName, best.name));
            Leave(best);
        }
    }
}
