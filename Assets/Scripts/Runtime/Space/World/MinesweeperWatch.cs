// MinesweeperWatch.cs
// Remake (2026-11-06, for mods): a minesweeper encounter's outcome (encounter kind "minesweeper", World.CruiseEncounterSpawner),
// after EVERSPACE 2's BP_Encounter_Minesweeper with its stuck-freighter task (BP_Job_MineSweeper_Task_01, StuckFreighterVariant:
// one BP_Minefield_BiggerRadius round a freighter, a BP_MissionTask_DestroyTargets over its mines; the freighter destroyed fails
// it, GivesCreditsReward): every mine gone pays the encounter's credits (CruiseEncounterSpawner.BattlePay, ES2's one formula),
// "Minefield cleared"; the freighter lost ends it unpaid; flying off past EncounterWatch.LeaveM drops it.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class MinesweeperWatch : MonoBehaviour
    {
        SpaceLevel level;
        NpcShip freighter;
        List<OrbitMine> mines;
        int pay;
        Vector3 centre;

        public static MinesweeperWatch Start(SpaceLevel level, NpcShip freighter, List<OrbitMine> mines, int pay, Vector3 centreUnity)
        {
            var go = new GameObject("Minesweeper");
            go.transform.SetParent(level.transform, false);
            var w = go.AddComponent<MinesweeperWatch>();
            w.level = level; w.freighter = freighter; w.mines = mines; w.pay = pay; w.centre = centreUnity;
            return w;
        }

        /// <summary>The mines still out there.</summary>
        public static int Left(List<OrbitMine> mines)
        {
            int n = 0;
            foreach (var m in mines) if (m != null && !m.Gone) n++;
            return n;
        }

        void Update()
        {
            if (level == null || level.Player == null) { Destroy(gameObject); return; }
            if ((level.Player.transform.position - centre).sqrMagnitude > EncounterWatch.LeaveM * EncounterWatch.LeaveM) { Destroy(gameObject); return; }
            if (freighter == null || freighter.Gone || freighter.Target == null || !freighter.Target.Alive)
            {
                level.Navigation?.Post(Localization.Extra("minesweeperLost", "The freighter is lost"));
                Destroy(gameObject);
                return;
            }
            if (Left(mines) > 0) return;
            Session.Credits += pay;
            level.Navigation?.Post(string.Format(Localization.Extra("minesweeperCleared", "Minefield cleared: +{0} cr"), pay));
            Destroy(gameObject);
        }
    }
}
