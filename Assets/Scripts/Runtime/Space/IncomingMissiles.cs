// IncomingMissiles.cs
// Remake (the original has no missile warning): every tracked homing missile, rocket or nuke aimed at the local player this
// frame, whoever fired it (an NPC's RocketGun, a capital ship's salvo, another player's shot mirrored by NetShotMirror).
// Gun.Update reports them while the target remains assigned; the flight HUD's MissileWarningView reads the same list.
// A boost shakes homing missiles off (Gun: a lock on a boosting Target is lost for good), so lost missiles are no longer reported.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class IncomingMissiles
    {
        public struct Missile
        {
            public Vector3 position, velocity;   // Unity metres, metres per ms
            public int frame;
            public Gun gun;                      // whose bullet it is (PointDefense shoots it down) ...
            public int index;                    // ... and which
        }

        static readonly Dictionary<(Gun, int), Missile> live = new Dictionary<(Gun, int), Missile>();
        static readonly List<(Gun, int)> stale = new List<(Gun, int)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => live.Clear();

        /// <summary>Gun.Update: bullet 'index' of 'gun' threatens the local player.</summary>
        public static void Report(Gun gun, int index, Vector3 position, Vector3 velocity) =>
            live[(gun, index)] = new Missile { position = position, velocity = velocity, frame = Time.frameCount, gun = gun, index = index };

        /// <summary>The tracked ordnance reported this frame or the last (guns and HUD update in any order); older shots go.</summary>
        public static void Collect(List<Missile> into)
        {
            into.Clear();
            stale.Clear();
            int now = Time.frameCount;
            foreach (var kv in live)
            {
                if (kv.Value.frame < now - 1) stale.Add(kv.Key);
                else into.Add(kv.Value);
            }
            foreach (var k in stale) live.Remove(k);
        }
    }
}
