// NetEventRules.cs
// Remake multiplayer: an event's travel restrictions on this player's game (/restrict, the event graphs' Restrict Travel;
// NetAdmin.Order.Rules): no jumps (planet jumps, the jumpgate, the Khador Drive: SpaceLevel's Navigation.JumpsBlocked and
// SystemJump.GateBlocked) and / or no docking (SpaceLevel.DockingBlocked), refused with 525 "Not possible on a mission.".
// An admin's /tp still moves the player. Cleared by "/restrict off", the event's end (NetEvents) and the session's end.

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetEventRules
    {
        public const int Jumps = 1, Docking = 2;

        static int flags;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => flags = 0;

        public static bool NoJumps => (flags & Jumps) != 0 && NetGame.Active;
        public static bool NoDocking => (flags & Docking) != 0 && NetGame.Active;

        internal static void Apply(int value) => flags = value;
    }
}
