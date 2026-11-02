// NetFollow.cs
// Remake-only (multiplayer): following a squadmate from orbit to orbit. Follow (the squad window in the flight HUD and the
// station menu, the Multiplayer window's Squad tab, the quick menu's "Follow <name>", or "/follow <name>") picks one; from
// then on, while this pilot is in space, each time the followed pilot turns up in another orbit (a planet jump, a
// jumpgate, the Khador Drive, a launch from another station) this game sets out after them the fastest way the ship has
// (NetDistress.TravelTo: the Khador Drive with the cells, else the autopilot to their planet or the gate route) and
// arrives beside them (NetDistress.ArrivalNear). Docked, it waits: the launch then goes after them.
// One trip per place they go (goingTo): the autopilot switched off on the way stays off until they move on, and a planet
// jump of one's own away from them isn't undone.
// Ends: Stop following (the same buttons, "/follow" with no name), a destination of one's own (the star map's pick, any
// Session.ProgrammedStation other than the one this set out for), the followed pilot leaving the squad or the game, this
// pilot leaving the squad, the session ending. Squadmates only (a stranger can't be trailed across the galaxy), never into
// an arena match, and not in the co-op campaign (its crew follows the leader's story already).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetFollow
    {
        const float CheckSeconds = 0.5f;
        public const ulong None = ulong.MaxValue;

        /// <summary>The followed squadmate's client id (None = nobody).</summary>
        public static ulong Following { get; private set; } = None;
        public static bool Active => Following != None;
        static string name = "";
        static int goingTo = int.MinValue;   // the station this game last set out for after them
        static float nextCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        /// <summary>NetGame.Leave: nobody followed into the next session (client ids come round again).</summary>
        public static void Clear() { Following = None; name = ""; goingTo = int.MinValue; nextCheck = 0f; }

        public static bool IsFollowing(NetPlayer p) => p != null && Active && p.OwnerClientId == Following;

        /// <summary>The Follow / Stop following buttons: follow 'target', or stop when it is the one followed. A message.</summary>
        public static string Toggle(NetPlayer target)
        {
            if (IsFollowing(target)) return Stop();
            var me = NetPlayer.Local;
            if (target == null || me == null || target == me) return null;
            if (CoopCampaign.Active) return X("mpFollowCoop", "In the co-op campaign the crew follows the leader's story already.");
            if (!NetSquad.Same(target, me)) return X("mpFollowSquad", "You can follow squadmates only: form a squad first (docked: /squad invite <pilot>).");
            Following = target.OwnerClientId;
            name = target.DisplayName;
            goingTo = Session.ProgrammedStation;   // a destination set before isn't "one of your own" from now on
            nextCheck = 0f;
            return string.Format(X("mpFollowStarted", "Following {0}: when they jump to another orbit or system, you go after them."), name);
        }

        /// <summary>Stop following (a message; null = nobody was followed).</summary>
        public static string Stop()
        {
            if (!Active) return null;
            string who = name;
            Clear();
            return string.Format(X("mpFollowStopped", "You stopped following {0}."), who);
        }

        public enum Step { Wait, Travel, Lost, OwnDestination }

        /// <summary>What following needs now. 'mate': the followed pilot is still in the game and the squad; 'placed': they
        /// are in space or docked (not between orbits); 'ready': this pilot is in space and free to set out (no jump,
        /// launch, arrival, cutscene or docking under way); 'programmed': Session.ProgrammedStation (-1 = none).</summary>
        public static Step Decide(bool mate, bool placed, int theirStation, bool theirArena, bool ready, int myStation, int programmed, int goingTo)
        {
            if (!mate) return Step.Lost;
            if (programmed >= 0 && programmed != goingTo) return Step.OwnDestination;
            if (!placed || theirArena || !ready || theirStation == myStation || theirStation == goingTo) return Step.Wait;
            return Step.Travel;
        }

        /// <summary>NetPlayer (the owner, every frame): set out after the followed pilot when they moved on.</summary>
        public static void Tick(NetPlayer me)
        {
            if (!Active || me == null) return;
            if ((nextCheck -= Time.unscaledDeltaTime) > 0f) return;
            nextCheck = CheckSeconds;
            var target = NetSquad.Find(Following);
            bool mate = target != null && target != me && NetSquad.Same(target, me) && !CoopCampaign.Active;
            var level = me.InSpace ? Object.FindAnyObjectByType<SpaceLevel>() : null;
            bool ready = Ready(me, level);
            int theirs = target != null ? target.Station : -1;
            // With them: this place counts as reached, so a planet jump of one's own away from them isn't undone.
            if (target != null && me.InSpace && theirs == me.Station && Session.ProgrammedStation < 0) goingTo = theirs;
            switch (Decide(mate, target != null && (target.InSpace || target.InHangar), theirs, target != null && NetArena.IsArenaOrbit(theirs),
                           ready, me.Station, Session.ProgrammedStation, goingTo))
            {
                case Step.Lost:
                    string who = name;
                    Clear();
                    NetChat.Notice(string.Format(X("mpFollowLost", "{0} isn't in your squad any more: you stopped following them."), who));
                    break;
                case Step.OwnDestination:
                    who = name;
                    Clear();
                    NetChat.Notice(string.Format(X("mpFollowOwnWay", "A destination of your own: you stopped following {0}."), who));
                    break;
                case Step.Travel:
                    goingTo = theirs;
                    string how = NetDistress.TravelTo(target, level, null);
                    if (!string.IsNullOrEmpty(how)) NetChat.Notice(string.Format(X("mpFollowGoing", "Following {0}: {1}"), name, how));
                    break;
            }
        }

        /// <summary>This pilot is in space and nothing else holds the ship: the launch or an arrival over, no jump charging
        /// or under way, no cutscene, conversation or docking (at the station or at an object), not in an arena match or
        /// between the systems.</summary>
        static bool Ready(NetPlayer me, SpaceLevel level)
        {
            if (level == null || !me.InSpace || NetArena.IsArenaOrbit(me.Station)) return false;
            if (!level.LaunchCameraOver || level.Cutscene || level.Leaving || level.WarpHolds || level.DockedInOrbit) return false;
            if (level.SystemJump == null || level.SystemJump.Cinematic || level.SystemJump.Charging) return false;
            var nav = level.Navigation;
            if (nav == null || nav.Jumping || nav.ArrivalHeld || nav.Conversation || nav.Interstellar) return false;
            return nav.Docking == null || nav.Docking.State == ObjectDocking.Phase.Idle;
        }

        static string X(string key, string english) => Localization.Extra(key, english);
    }
}
