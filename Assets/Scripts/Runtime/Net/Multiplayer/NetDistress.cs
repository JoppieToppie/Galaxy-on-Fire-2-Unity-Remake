// NetDistress.cs
// Remake-only: a squad's distress calls (multiplayer). A pilot in space calls for help (the squad window's Distress call,
// or "/sos" in the chat): their NetPlayer shows it (NetPlayer.Distress) and the squadmates get a notice with where
// (NetState.DistressRpc). The call ends when they cancel it, dock, leave space or after MaxSeconds.
// A squadmate answers with Help (the squad window, or "/assist <name>"): the route to the caller's orbit is programmed
// (Session.ProgrammedStation) and flown the fastest way the ship has:
//   another system with a Khador Drive (GalaxyMap.HasJumpDrive) and the energy cells: the drive charges and jumps at
//     once (Session.InstantJump, SystemJump);
//   the same system, or no drive / too few cells: the autopilot to the caller's planet (then the planet jump) or to the
//     gate (Navigation.ContinueToProgrammedStation);
//   docked: the launch first (the same programmed station, like the star map's pick).
// Arriving in the caller's orbit by that jump, the helper comes out ArrivalDistance from the caller, facing them
// (SpaceLevel.SpawnPlayer), instead of at the far arrival point. The star map marks the squad (UI.StarMap: a green dot
// with the names, red with a distress call). Following a squadmate (NetFollow) travels the same way (TravelTo).

using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetDistress
    {
        public const float MaxSeconds = 600f;
        /// <summary>How far from the caller a helper arrives (Unity metres).</summary>
        const float ArrivalDistance = 1500f;

        /// <summary>This pilot's distress call runs.</summary>
        public static bool Active { get; private set; }
        static float since;
        static ulong helping = ulong.MaxValue;   // the caller being helped (the arrival next to them)
        static int helpStation = int.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; helping = ulong.MaxValue; helpStation = int.MinValue; }

        /// <summary>The squad window / "/sos": call for help (or end the call). A message for the player, null = none.</summary>
        public static string Toggle()
        {
            if (Active) { Cancel(true); return Localization.Extra("mpDistressCancelled", "Distress call ended."); }
            var me = NetPlayer.Local;
            if (me == null || NetSquad.LocalSquad == 0) return Localization.Extra("mpDistressNoSquad", "A distress call goes to your squad: join one first.");
            if (!me.InSpace || NetArena.IsArenaOrbit(me.Station)) return Localization.Extra("mpDistressNotHere", "Distress calls are for pilots in space.");
            Active = true;
            since = Time.unscaledTime;
            NetState.Instance?.DistressRpc(true);
            return Localization.Extra("mpDistressSent", "Distress call sent to your squad.");
        }

        static void Cancel(bool tell)
        {
            if (!Active) return;
            Active = false;
            if (tell) NetState.Instance?.DistressRpc(false);
        }

        /// <summary>NetPlayer (the owner, every frame): the call ends on docking, leaving space or after MaxSeconds.</summary>
        public static void Tick(NetPlayer me)
        {
            if (!Active) return;
            if (me == null || !me.InSpace && me.Where != NetPlayer.Place.None || Time.unscaledTime - since > MaxSeconds || NetSquad.LocalSquad == 0) Cancel(true);
        }

        /// <summary>The squad window's Help / "/assist": the fastest way to 'caller''s orbit. A message for the player.</summary>
        public static string Help(NetPlayer caller)
        {
            var me = NetPlayer.Local;
            if (caller == null || me == null || caller == me) return null;
            if (!caller.InSpace || NetArena.IsArenaOrbit(caller.Station))
                return string.Format(Localization.Extra("mpHelpNotInSpace", "{0} isn't in space."), caller.DisplayName);
            if (me.InSpace && me.Station == caller.Station) return string.Format(Localization.Extra("mpHelpHere", "{0} is in this orbit."), caller.DisplayName);
            var level = Object.FindAnyObjectByType<SpaceLevel>();
            if (level != null && (level.SystemJump == null || level.SystemJump.Cinematic || level.SystemJump.Charging || (level.Navigation != null && level.Navigation.Jumping)))
                return Localization.Extra("mpHelpBusy", "Not now: a jump is under way.");
            return TravelTo(caller, level, level == null ? Object.FindAnyObjectByType<StationLevel>() : null);
        }

        /// <summary>Help, and NetFollow's jump after the followed pilot: program 'target''s station and go there the fastest
        /// way (the header); arriving there by that jump, next to them. 'dock' = docked: the launch first. Its message.</summary>
        internal static string TravelTo(NetPlayer target, SpaceLevel level, StationLevel dock)
        {
            int station = target.Station;
            var db = NetGame.Db;
            int from = db.Stations.Find(s => s.index == Session.StationIndex)?.system ?? -1;
            int to = db.Stations.Find(s => s.index == station)?.system ?? -1;
            helping = target.OwnerClientId;
            helpStation = station;
            Session.ProgrammedStation = station;
            Session.InstantJump = false;
            string how;
            if (from != to && GalaxyMap.HasJumpDrive(db) && !GalaxyMap.HasVolatileGoods)
            {
                int cells = GalaxyMap.EnergyCells(db, from, to, out _);
                if (GalaxyMap.CellsInCargo() >= cells)
                {
                    Session.InstantJump = true;
                    Session.EnergyCellsForNextJump = cells;
                    how = string.Format(Localization.Extra("mpHelpKhador", "Khador Drive to {0} ({1} energy cells)."), target.DisplayName, cells);
                }
                else how = string.Format(Localization.Extra("mpHelpNoCells", "Too few energy cells for the Khador Drive ({0}): the autopilot flies to {1}."), cells, target.DisplayName);
            }
            else how = string.Format(Localization.Extra("mpHelpAutopilot", "The autopilot flies to {0}."), target.DisplayName);
            if (dock != null) dock.Launch();                                   // the launch, then the programmed station
            else if (level != null && !Session.InstantJump) level.Navigation?.ContinueToProgrammedStation();
            // (in space with a jump: SystemJump starts charging on its own, Session.InstantJump)
            return how;
        }

        /// <summary>SpaceLevel.SpawnPlayer: arriving in the helped pilot's orbit, next to them (false = the normal arrival).</summary>
        public static bool ArrivalNear(int station, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            if (station != helpStation || helping == ulong.MaxValue) return false;
            var caller = NetSquad.Find(helping);
            helping = ulong.MaxValue;
            helpStation = int.MinValue;
            if (caller == null || !caller.InSpace || caller.Station != station) return false;
            var at = caller.Position;
            var dir = Random.onUnitSphere;
            dir.y *= 0.3f;
            position = at + dir.normalized * ArrivalDistance;
            rotation = Quaternion.LookRotation(at - position, Vector3.up);
            return true;
        }
    }
}
