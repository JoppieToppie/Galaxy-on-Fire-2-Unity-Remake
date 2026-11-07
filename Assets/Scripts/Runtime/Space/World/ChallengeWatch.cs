// ChallengeWatch.cs
// Remake (2026-11-06, for mods): a shooting challenge's score (encounter kind "challenge", World.CruiseEncounterSpawner), after
// EVERSPACE 2's BP_Encounter_ShootingChallenge and its task (BP_Job_ShootingChallenge_Task_01: a Challenger on its primary
// weapon damage x ChallengerDealDamageFactor 3, NumGroups 4-5 enemy groups, BP_MissionTask_ChallengeDestroyMoreTargets, an
// "AdditionalEnemy" on a tie, bFailIfPlayerLeaves): each raider the player destroys scores for the player, one an NPC destroys
// (Target.killedByNpc: the challenger, or anyone else's shot) for the challenger; the score on the HUD after each kill. All
// gone: more for the player wins (ES2 gives XP, which GoF2 hasn't: Pay, half a battle's credits), fewer loses, a tie brings one
// more raider. Flying off past EncounterWatch.LeaveM ends it unpaid.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class ChallengeWatch : MonoBehaviour
    {
        SpaceLevel level;
        readonly List<NpcShip> raiders = new List<NpcShip>();
        int pay, player, challenger;
        Vector3 centreGame;
        public int PlayerScore => player;
        public int ChallengerScore => challenger;

        /// <summary>The outcome once every raider is gone: 1 the player won, -1 lost, 0 a tie (one more raider).</summary>
        public static int Outcome(int player, int challenger) => player > challenger ? 1 : player < challenger ? -1 : 0;

        public static ChallengeWatch Start(SpaceLevel level, List<NpcShip> raiders, int pay, Vector3 centreGame)
        {
            var go = new GameObject("Shooting challenge");
            go.transform.SetParent(level.transform, false);
            var w = go.AddComponent<ChallengeWatch>();
            w.level = level; w.pay = pay; w.centreGame = centreGame;
            foreach (var r in raiders) w.Add(r);
            return w;
        }

        void Add(NpcShip r)
        {
            if (r == null || r.Target == null) return;
            raiders.Add(r);
            r.Target.Died += t =>
            {
                if (this == null) return;
                if (t.killedByNpc) challenger++; else player++;
                level?.Navigation?.Post(string.Format(Localization.Extra("challengeScore", "You {0} : {1} Challenger"), player, challenger));
            };
        }

        void Update()
        {
            if (level == null || level.Player == null) { Destroy(gameObject); return; }
            var c = new Vector3(centreGame.x, centreGame.y, -centreGame.z) * OrbitLayout.MetersPerUnit;
            if ((level.Player.transform.position - c).sqrMagnitude > EncounterWatch.LeaveM * EncounterWatch.LeaveM)
            {
                level.Navigation?.Post(Localization.Extra("challengeLeft", "Challenge abandoned"));
                Destroy(gameObject);
                return;
            }
            if (EncounterWatch.AnyLeft(raiders)) return;
            switch (Outcome(player, challenger))
            {
                case 0:   // ES2's AdditionalEnemy
                    if (level.Traffic == null) { Destroy(gameObject); return; }
                    raiders.Clear();
                    Add(CruiseEncounterSpawner.Raider(level.Traffic, centreGame + new Vector3(Random.Range(-5000f, 5000f), Random.Range(-2000f, 2000f), Random.Range(-5000f, 5000f))));
                    level.Traffic.ConnectPlayers();
                    level.Navigation?.Post(Localization.Extra("challengeTie", "A tie: one more target"));
                    return;
                case 1:
                    Session.Credits += pay;
                    level.Navigation?.Post(string.Format(Localization.Extra("challengeWon", "Challenge won: +{0} cr"), pay));
                    break;
                default:
                    level.Navigation?.Post(Localization.Extra("challengeLost", "The challenger won"));
                    break;
            }
            Destroy(gameObject);
        }
    }
}
