// EncounterMusic.cs
// Remake (2026-11-06, for mods): an encounter's own fight music (encounters.json "music", CruiseEncounters.Entry.music), after
// EVERSPACE 2's encounter music sets (DA_MusicSet_Rift_001, _HighRiskSector_001, _Dreadnought: a mission's Play Music in
// place of the sector's battle set): from the encounter's spawn (CruiseEncounterSpawner) its track takes the battle music's
// place (World.Traffic's music, Current) until the pilot flies past EncounterWatch.LeaveM from it or the orbit has been calm
// for CalmS (after GraceS for the fight to start). A newer encounter's replaces an older one's.

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class EncounterMusic : MonoBehaviour
    {
        public const float GraceS = 20f, CalmS = 15f;
        static EncounterMusic current;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;

        /// <summary>The track the fight plays now, null = the battle music.</summary>
        public static string Current => current != null ? current.track : null;

        SpaceLevel level;
        string track;
        Vector3 centre;
        float ageS, calmS;

        /// <summary>'centreGame' in game units.</summary>
        public static EncounterMusic Begin(SpaceLevel level, string track, Vector3 centreGame)
        {
            if (level == null || string.IsNullOrEmpty(track)) return null;
            if (current != null) Destroy(current.gameObject);
            var go = new GameObject("Encounter music");
            go.transform.SetParent(level.transform, false);
            var m = current = go.AddComponent<EncounterMusic>();
            m.level = level; m.track = track; m.centre = OrbitLayout.ToUnity(centreGame);
            return m;
        }

        /// <summary>Whether it still plays: near the place, and not calm for CalmS after the grace.</summary>
        public static bool StillOn(float ageS, float calmS, float distanceM) => distanceM <= EncounterWatch.LeaveM && (ageS < GraceS || calmS < CalmS);

        void OnDestroy() { if (current == this) current = null; }

        void Update()
        {
            if (level == null || level.Player == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            ageS += dt;
            calmS = level.Traffic != null && level.Traffic.HostileCount > 0 ? 0f : calmS + dt;
            if (!StillOn(ageS, calmS, (level.Player.transform.position - centre).magnitude)) Destroy(gameObject);
        }
    }
}
