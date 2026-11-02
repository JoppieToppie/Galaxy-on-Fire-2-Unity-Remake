// EventRules.cs
// Remake events: an event's travel restrictions on this player's game (/restrict, the event graphs' Restrict Travel;
// NetAdmin.Order.Rules): no jumps (planet jumps, the jumpgate, the Khador Drive: SpaceLevel's Navigation.JumpsBlocked and
// SystemJump.GateBlocked) and / or no docking (SpaceLevel.DockingBlocked), refused with 525 "Not possible on a mission.";
// remake: "khador" refuses only the Khador Drive and the slipstream (Navigation.KhadorBlocked), like the Valkyrie's last
// mission, the gates and planet jumps still open.
// An admin's /tp still moves the player. Cleared by "/restrict off", the event's end (EventRunner) and the session's end.

using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class EventRules
    {
        public const int Jumps = 1, Docking = 2, Khador = 4;

        static int flags;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => flags = 0;

        public static bool NoJumps => (flags & Jumps) != 0 && EventHost.ScreensActive;
        public static bool NoDocking => (flags & Docking) != 0 && EventHost.ScreensActive;
        public static bool NoKhador => (flags & Khador) != 0 && EventHost.ScreensActive;

        internal static void Apply(int value) => flags = value;
    }
}
