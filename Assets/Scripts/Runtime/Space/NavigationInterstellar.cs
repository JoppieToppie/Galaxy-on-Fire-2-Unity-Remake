// NavigationInterstellar.cs
// Remake: the drive that flies between the systems (Data.InterstellarSpace, attr 135), Navigation's side. In an orbit the
// quick menu (and the Khador Drive's key, without one) offers it by the item's name, under the Khador Drive's refusals (525
// on a mission, 612 with volatile goods, the story's own Void jumps); SystemJump plays the charge and the scene. Between the
// systems every system on the star map is a Kind.System target: drawn and locked like the jumpgate, the autopilot flies to
// it and holds the drive (NavigationCruiseDrive), the fast-forward button holds it by hand, and within ArriveUnits of a
// system the ship comes out there (under the autopilot only at its own). The story's requirements for the arrival orbit
// refuse as a planet jump's do. The Void is no place here: the autopilot menu's last entry (InterstellarSpace.VoidShown) is
// a dive into it (VoidDive), never drawn or locked, its distance nowhere, that ends in the alien orbit after a random time.
// Where a system brings the ship out (the user, 2026-11-03: the Khador Drive reaches any planet, so this should too; the sun's
// station, UpdateArrivals): a planet picked in flight, else the programmed destination when it is in that system, else the
// gate orbit (InterstellarSpace.ArrivalStation). A system's planets (GalaxyMap.SystemLayout, the star map's, in their map
// order) are targets of their own (Target.planetOf), real places round their sun (the user, 2026-11-03: "the sun should show
// first with planets around it, then you pick one and head toward it and then its orbit starts spawning"; then: only within
// a light year of the star, orbiting it, km near a planet). A system's zone (UpdateZone, InterstellarSpace.ZoneUnits):
// within a light year of the star flown to (the autopilot's, else any), the space round the ship grows ZoneScale times
// (WorldScale; every target moved out from the ship, so nothing on screen jumps; WorldScaled tells World.InterstellarField),
// its planets come out (only there), its arrival and approach work at the orbits' scale; a hop's energy cells count the
// distance at the space's own scale. Its planets are laid out once, the first time the zone opens, as the star map's disc (its angles and spacing, PlanetUnitsPerMapUnit) tilted PlanetDiscTilt toward the way
// the ship came; World.InterstellarField draws them as 3D planets. Locking one or flying the autopilot to it (the autopilot
// menu lists them under their system) picks it, and the system's arrival point is that planet (ArrivalPoint: the autopilot
// flies there, the edge and the approach are measured from it; before the planets are laid out, the sun). Within
// InterstellarSpace.ApproachUnits of it the arrival is settled: its orbit is being built ahead (World.SystemApproach), so a
// change would show; the planets' markers go.

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
        /// <summary>The planets show round their sun this far apart from the ship's eye, and on their own within this of the
        /// nearest system.</summary>
        /// <summary>The space's scale now: 1 between the systems, InterstellarSpace.ZoneScale in a system's zone (Zone).</summary>
        public float WorldScale { get; private set; } = 1f;
        /// <summary>The system whose zone the ship is in (its planets out, its arrival possible), null between the systems.</summary>
        public Target Zone { get; private set; }
        /// <summary>The space was scaled by the factor about the point (Unity): what keeps world points of its own follows.</summary>
        public event Action<Vector3, float> WorldScaled;
        /// <summary>The planets' disc: units a star map unit out from the sun past PlanetSunUnits (the sun's own size and some),
        /// tilted this far (degrees) from edge-on toward the ship's way in.</summary>
        public const float PlanetUnitsPerMapUnit = 6f, PlanetSunUnits = 70000f, PlanetDiscTilt = 55f;

        sealed class PlanetSpot { public Target planet; public float angle, radius; }

        sealed class SystemPick
        {
            public int gate;              // InterstellarSpace.ArrivalStation
            public int picked = -1;       // a planet picked in flight
            public bool laidOut;          // the planets have their places (the first time they showed)
            public Vector3 right, depth;  // their disc's axes (the orbit rings)
            public readonly List<PlanetSpot> planets = new List<PlanetSpot>();
        }
        readonly Dictionary<Target, SystemPick> picks = new Dictionary<Target, SystemPick>();

        /// <summary>A system target's sun: the target itself, or the system a planet target belongs to.</summary>
        public static Target SunOf(Target t) => t?.planetOf ?? t;

        void SetupInterstellar()
        {
            if (!Interstellar) return;
            picks.Clear();
            foreach (var p in InterstellarSpace.Systems(db))
            {
                var sun = new Target { kind = Kind.System, fixedPosition = OrbitLayout.ToUnity(p.position), station = p.station, system = p.system, name = p.name };
                Targets.Add(sun);
                var pick = picks[sun] = new SystemPick { gate = p.station };
                var layout = GalaxyMap.SystemLayout(db, db.Systems.Find(s => s.index == p.system));
                if (layout.Count < 2) continue;   // one planet: the sun is it
                foreach (var planet in layout)
                {
                    var st = db.Stations.Find(s => s.index == planet.station);
                    if (st == null || string.IsNullOrEmpty(st.name)) continue;
                    var t = new Target { kind = Kind.System, fixedPosition = sun.fixedPosition, station = planet.station, system = p.system, name = st.name.Trim(),
                                         planetOf = sun, hidden = true };
                    Targets.Add(t);
                    pick.planets.Add(new PlanetSpot { planet = t, angle = planet.angle * Mathf.PI * 2f / 65536f, radius = planet.radius });
                }
            }
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

        void RequestInterstellar() => StartSlipstream();

        /// <summary>The drive's entry, or the station map's "by the Khador Slipstream" once out: false when refused (a mission,
        /// a story jump, volatile goods, no such drive) with its message.</summary>
        public bool StartSlipstream()
        {
            if (Interstellar || InterstellarSpace.Drive(db) == null) return false;
            if ((JumpsBlocked != null && JumpsBlocked()) || (KhadorBlocked != null && KhadorBlocked()) || Story.ForcedKhadorTarget(Session.StationIndex) != null) { Say(JumpRefusal); return false; }
            if (GalaxyMap.HasVolatileGoods) { Say(GoF2Remake.Modding.ModJumpFx.VolatileRefusal()); return false; }
            InterstellarRequested?.Invoke();
            return true;
        }

        /// <summary>Into this space by the station's or the Khador map's choice (Session.SlipstreamAutopilot): the autopilot
        /// takes the programmed destination's system, whose arrival is that planet (UpdateArrivals).</summary>
        void PilotToDestination()
        {
            if (!Session.SlipstreamAutopilot) return;
            Session.SlipstreamAutopilot = false;
            int system = db.Stations.Find(s => s.index == Session.ProgrammedStation)?.system ?? -1;
            var sun = Targets.Find(t => t.kind == Kind.System && t.planetOf == null && t.system == system && !t.hidden);
            if (sun == null) return;
            Say($"{Localization.Get(546)}: {sun.name}", HudMessageKind.Target);
            SetAutopilot(sun);
        }

        List<Target> SystemEntries(List<Target> list)
        {
            var systems = Targets.FindAll(t => t.kind == Kind.System && !t.hidden && t.planetOf == null);
            int goal = db.Stations.Find(s => s.index == Session.ProgrammedStation)?.system ?? -1;
            var at = ship.transform.position;
            // the system in hand (locked, or the autopilot's) lists its planets under it, so it is always in the list: after
            // the programmed destination's system, before the nearest
            var focus = SunOf(AutopilotTarget != null && AutopilotTarget.kind == Kind.System ? AutopilotTarget : Locked);
            int Rank(Target t) => t.system == goal ? 0 : t == focus ? 1 : 2;
            systems.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b))
                                   : (a.Position - at).sqrMagnitude.CompareTo((b.Position - at).sqrMagnitude));
            int room = VoidDive != null ? SystemMenuEntries - 1 : SystemMenuEntries;
            for (int i = 0; i < systems.Count && i < room; i++)
            {
                list.Add(systems[i]);
                if (systems[i] != focus || !picks.TryGetValue(focus, out var pick)) continue;
                foreach (var spot in pick.planets)
                    if (spot.planet is var planet && !planet.hidden) list.Add(new Target { kind = Kind.System, station = planet.station, system = planet.system, planetOf = focus,
                                                              name = (planet.station == focus.station ? "\u203a " : "  ") + planet.name });
            }
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
            UpdateZone();
            var at = ship.transform.position;
            UpdateArrivals();
            PilotToDestination();
            foreach (var t in Targets)
            {
                if (t.kind != Kind.System || t.planetOf != null) continue;
                bool hide = t.system >= visible.Length || !visible[t.system];
                if (hide != t.hidden)
                {
                    t.hidden = hide;
                    if (hide && (Locked == t || Candidate == t)) { Locked = Candidate = null; LockTimer = 0f; }
                    if (hide && SunOf(AutopilotTarget) == t) SetAutopilot(null);
                }
                if (hide) continue;
                // The autopilot flies past the others' edges to its own (a dive into the Void past all): on its curve out of
                // the start a flight off the galaxy's disc came out at Weymire on the way.
                if (AutopilotTarget != null && AutopilotTarget.kind == Kind.System && t != SunOf(AutopilotTarget)) continue;
                if (t != Zone) continue;   // a system is arrived at in its zone only (InterstellarSpace.ZoneUnits)
                float units = (ArrivalPoint(t) - at).magnitude / M;   // to its arrival planet once laid out
                if (t == refusedSystem)
                {
                    if (units > InterstellarSpace.ArriveUnits * 1.5f) refusedSystem = null;
                    continue;
                }
                if (units >= InterstellarSpace.ArriveUnits) continue;
                if (PlanetJumpRefused != null && PlanetJumpRefused(t.station))
                {
                    refusedSystem = t;
                    if (SunOf(AutopilotTarget) == t) SetAutopilot(null);
                    continue;
                }
                Leave(t);
                return;
            }
        }

        bool Refused(int station) => PlanetJumpRefused != null && PlanetJumpRefused(station);

        /// <summary>Every frame between the systems: where each system brings the ship out (a planet picked in flight, else the
        /// programmed destination there, else the gate orbit), settled within InterstellarSpace.ApproachUnits; and its planets,
        /// shown round their sun only in its zone (Zone), a lock
        /// or the autopilot on one picking it. A planet the story refuses is drawn half-transparent and can't be picked.</summary>
        void UpdateArrivals()
        {
            var at = ship.transform.position;
            var lockedSun = SunOf(Locked);
            var lockingSun = SunOf(Candidate);   // a planet being locked keeps its system's planets out (its sun's lock let go)
            var pilotSun = AutopilotTarget != null && AutopilotTarget.kind == Kind.System && AutopilotTarget != VoidDive ? SunOf(AutopilotTarget) : null;
            Target nearestSun = null;
            float nearest = float.MaxValue;
            foreach (var sun in picks.Keys)
            {
                if (sun.hidden) continue;
                float d = (sun.Position - at).sqrMagnitude;
                if (d < nearest) { nearest = d; nearestSun = sun; }
            }
            int prog = Session.ProgrammedStation;
            foreach (var kv in picks)
            {
                var sun = kv.Key;
                var pick = kv.Value;
                var way = sun.Position - at;
                float units = way.magnitude / M;
                bool settled = sun == Zone && (ArrivalPoint(sun) - at).magnitude / M < InterstellarSpace.ApproachUnits;
                if (!settled)
                {
                    var chosen = Locked != null && Locked.planetOf == sun ? Locked : AutopilotTarget != null && AutopilotTarget.planetOf == sun ? AutopilotTarget : null;
                    if (chosen != null && !chosen.disabled && pick.picked != chosen.station)
                    {
                        pick.picked = chosen.station;
                        Say(string.Format(Localization.Extra("interstellarArrivalPicked", "{0}: arriving at {1}"), sun.name, chosen.name), HudMessageKind.Target);
                    }
                    sun.station = InterstellarSpace.ArrivalFor(db, sun.system, pick.gate, pick.picked, prog, Refused);
                }
                if (pick.planets.Count == 0) continue;
                bool show = !sun.hidden && !settled && sun == Zone;   // only in its zone, orbiting it
                if (show && !pick.laidOut) LayOut(sun, pick, way);
                foreach (var spot in pick.planets)
                {
                    var planet = spot.planet;
                    planet.disabled = Refused(planet.station);
                    if (planet.hidden == show)
                    {
                        planet.hidden = !show;
                        if (!show)
                        {
                            // gone with the arrival settled: the lock and the autopilot stay on its system (whose arrival is it)
                            if (Candidate == planet) { Candidate = null; LockTimer = 0f; }
                            if (Locked == planet) Locked = sun;
                            if (AutopilotTarget == planet) { AutopilotTarget = sun; ship.autopilotTarget = () => ArrivalPoint(sun); }
                        }
                    }
                }
            }
        }

        /// <summary>The system's planets their places (once): the star map's disc round the sun, the angles its, each
        /// PlanetSunUnits + its map radius x PlanetUnitsPerMapUnit out, the disc tilted PlanetDiscTilt from edge-on toward the
        /// ship's way in ('way', ship to sun), so the first look at it is the map's view of the system.</summary>
        void LayOut(Target sun, SystemPick pick, Vector3 way)
        {
            pick.laidOut = true;
            var fwd = way.sqrMagnitude > 1e-6f ? way.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, fwd);
            right.Normalize();
            var disc = Quaternion.AngleAxis(-PlanetDiscTilt, right);   // the far side tipped up toward the view
            var depth = disc * fwd;
            pick.right = right;
            pick.depth = depth;
            foreach (var spot in pick.planets)
            {
                float r = (PlanetSunUnits + spot.radius * PlanetUnitsPerMapUnit) * M;
                spot.planet.fixedPosition = sun.Position + (right * Mathf.Sin(spot.angle) + depth * Mathf.Cos(spot.angle)) * r;
            }
        }

        /// <summary>Where flying into a system brings the ship (Unity): its arrival planet once its planets have their places
        /// (World.InterstellarField draws it there, the approach builds its orbit round it), else the sun.</summary>
        public Vector3 ArrivalPoint(Target sun)
        {
            sun = SunOf(sun);
            if (sun == null) return Vector3.zero;
            if (picks.TryGetValue(sun, out var pick) && pick.laidOut)
                foreach (var spot in pick.planets) if (spot.planet.station == sun.station) return spot.planet.fixedPosition;
            return sun.Position;
        }

        /// <summary>Whether a system's planets have their places (World.InterstellarField draws them from then on).</summary>
        public bool PlanetsLaidOut(Target sun) => sun != null && picks.TryGetValue(sun, out var pick) && pick.laidOut;

        /// <summary>The disc a laid-out system's planets orbit in (its axes, Unity), for their orbit rings.</summary>
        public bool PlanetDisc(Target sun, out Vector3 right, out Vector3 depth)
        {
            right = depth = Vector3.zero;
            if (sun == null || !picks.TryGetValue(sun, out var pick) || !pick.laidOut) return false;
            right = pick.right;
            depth = pick.depth;
            return true;
        }

        /// <summary>Into a system's zone within InterstellarSpace.ZoneUnits of its star (the autopilot's system when it flies to
        /// one, so a system flown past isn't entered; else any), out again past ZoneExit of the zone's own size.</summary>
        void UpdateZone()
        {
            var at = ship.transform.position;
            if (Zone != null)
            {
                if ((Zone.Position - at).magnitude / M > InterstellarSpace.ZoneUnits * InterstellarSpace.ZoneScale * InterstellarSpace.ZoneExit)
                    Rescale(at, 1f / InterstellarSpace.ZoneScale, null);
                return;
            }
            var pilot = AutopilotTarget != null && AutopilotTarget.kind == Kind.System && AutopilotTarget != VoidDive ? SunOf(AutopilotTarget) : null;
            foreach (var sun in picks.Keys)
            {
                if (sun.hidden || (pilot != null && sun != pilot)) continue;
                if ((sun.Position - at).magnitude / M < InterstellarSpace.ZoneUnits) { Rescale(at, InterstellarSpace.ZoneScale, sun); return; }
            }
        }

        /// <summary>The space scaled by 'k' about 'pivot' (the ship): every fixed target moved out (or in) along its way from it.</summary>
        void Rescale(Vector3 pivot, float k, Target zone)
        {
            foreach (var t in Targets) if (t.transform == null) t.fixedPosition = pivot + (t.fixedPosition - pivot) * k;
            if (VoidDive != null) VoidDive.fixedPosition = pivot + (VoidDive.fixedPosition - pivot) * k;
            WorldScale *= k;
            if (Mathf.Abs(WorldScale - 1f) < 1e-3f) WorldScale = 1f;
            Zone = zone;
            WorldScaled?.Invoke(pivot, k);
        }

        /// <summary>An autopilot menu entry for a planet (SystemEntries' copy with its marker) as the planet's own target.</summary>
        Target MenuPlanet(Target t) =>
            t != null && t.planetOf != null && !Targets.Contains(t) ? Targets.Find(x => x.planetOf == t.planetOf && x.station == t.station) ?? t : t;

        void Leave(Target system)
        {
            SetAutopilot(null);
            LeaveAlong(ArrivalPoint(system) - ship.transform.position, system.station);
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
                if (t.kind != Kind.System || t.hidden || t.planetOf != null) continue;
                float d = (t.Position - at).sqrMagnitude;
                if (d < nearest) { nearest = d; best = t; }
            }
            if (best == null) return;
            Say(string.Format(Localization.Extra("interstellarNoCells", "{0} collapsed: falling out at {1}"), driveName, best.name));
            Leave(best);
        }
    }
}
