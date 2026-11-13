// NpcTestBase.cs
// Shared set-up for the NPC / traffic tests: the sandbox (SandboxTest) plus the statics the NPC rules read that
// Session.ResetNewGame doesn't reset (the difficulty, NpcTables' level flags, the menu camera), put back after each test,
// the two Gameplay options TrafficPlan reads (pirate events, capital ship enhancements) set to a known value and put back,
// and helpers to put the player at a rank and in a world. The rank thresholds are the original's
// (Status::checkForLevelUp, DAT_00252b0c, quoted in Reference/research/npc_traffic_ai.md 2.2).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public abstract class NpcTestBase : SandboxTest
    {
        /// <summary>XP per rank 0..20 (npc_traffic_ai.md 2.2).</summary>
        protected static readonly int[] RankXp = { 0, 7, 21, 42, 70, 105, 147, 196, 252, 315, 385, 462, 546, 637, 735, 840, 952, 1071, 1197, 1330, 1650 };

        float savedDifficulty;
        int savedFreelanceType, savedFailCount, savedLastFailed;
        bool savedInCampaign, savedPirateEvents, savedCapitalShips, savedDerelicts;
        Vector3? savedMenuCamera;

        [SetUp]
        public void SaveNpcStatics()
        {
            savedDifficulty = Session.Difficulty;
            savedFreelanceType = NpcTables.LevelFreelanceType;
            savedInCampaign = NpcTables.InCampaignLevel;
            savedFailCount = Session.FailCount;
            savedLastFailed = Session.LastFailedMission;
            savedMenuCamera = TrafficPlan.MenuCamera;
            savedPirateEvents = Settings.PirateEvents;
            savedCapitalShips = Settings.CapitalShips;
            Session.Difficulty = Session.DifficultyNormal;
            NpcTables.LevelFreelanceType = -1;
            NpcTables.InCampaignLevel = false;
            Session.FailCount = 0;
            Session.LastFailedMission = -1;
            TrafficPlan.MenuCamera = null;
            if (Settings.PirateEvents) Settings.PirateEvents = false;
            if (Settings.CapitalShips) Settings.CapitalShips = false;
            savedDerelicts = Settings.DerelictWrecks;
            if (savedDerelicts) Settings.DerelictWrecks = false;
        }

        [TearDown]
        public void RestoreNpcStatics()
        {
            Session.Difficulty = savedDifficulty;
            NpcTables.LevelFreelanceType = savedFreelanceType;
            NpcTables.InCampaignLevel = savedInCampaign;
            Session.FailCount = savedFailCount;
            Session.LastFailedMission = savedLastFailed;
            TrafficPlan.MenuCamera = savedMenuCamera;
            if (Settings.PirateEvents != savedPirateEvents) Settings.PirateEvents = savedPirateEvents;
            if (Settings.CapitalShips != savedCapitalShips) Settings.CapitalShips = savedCapitalShips;
            if (Settings.DerelictWrecks != savedDerelicts) Settings.DerelictWrecks = savedDerelicts;
        }

        /// <summary>The remake's free play (no story), campaign index 20 (the research tables' "campaign mission 20").</summary>
        protected static void InFreePlay(int rank = 0)
        {
            Session.FreePlay = true;
            Session.CompletedWorld = false;
            Session.CampaignMission = 20;
            SetRank(rank);
        }

        /// <summary>A story game at campaign index 'index'.</summary>
        protected static void InStory(int index, int rank = -1)
        {
            Session.FreePlay = false;
            Session.CompletedWorld = false;
            Session.CampaignMission = index;
            if (rank >= 0) SetRank(rank);
        }

        /// <summary>The finished game's world (multiplayer's): free play at index 162.</summary>
        protected static void InCompletedWorld(int rank = 0)
        {
            Session.UseCompletedWorld();
            SetRank(rank);
        }

        /// <summary>Kills so the XP lands exactly on rank r's threshold (the other XP sources as they are).</summary>
        protected static void SetRank(int r)
        {
            Session.Kills = 0;
            int others = Session.Xp;
            Session.Kills = Math.Max(0, RankXp[r] - others);
            Assume.That(Session.Rank == r, "the other XP sources alone are past rank " + r);
        }

        protected static SystemData SystemOf(int station)
        {
            var st = Db.Stations.Find(s => s.index == station);
            Assert.IsNotNull(st, "station " + station);
            var sys = Db.Systems.Find(s => s.index == st.system);
            Assert.IsNotNull(sys, "system of station " + station);
            return sys;
        }

        protected static List<SpawnSpec> Build(int station, Vector3 player = default, Vector3? wormhole = null) =>
            TrafficPlan.Build(Db, station, station == Session.VoidOrbit ? null : SystemOf(station), player, wormhole);

        protected static int Count(List<SpawnSpec> list, NpcGroup group) => list.FindAll(s => s.group == group).Count;

        /// <summary>Per-build counts of the original's groups (npc_tables.py's keys).</summary>
        protected struct Counts { public int local, jumpers, freighters, raiders; }

        protected static Counts CountGroups(List<SpawnSpec> list) => new Counts
        {
            local = Count(list, NpcGroup.Local), jumpers = Count(list, NpcGroup.Jumper),
            freighters = Count(list, NpcGroup.Freighter), raiders = Count(list, NpcGroup.Raider),
        };

        /// <summary>Runs Build n times from a fixed seed and hands each list to 'each'.</summary>
        protected static void Repeat(int station, int n, int seed, Action<List<SpawnSpec>> each, Vector3 player = default)
        {
            UnityEngine.Random.InitState(seed);
            for (int i = 0; i < n; i++) each(Build(station, player));
        }

        protected static int[] Pool(int race) => race switch
        {
            // npc_traffic_ai.md 2.5 (Globals::getRandomEnemyFighter): races 4-8 and >= 11 fly the pirates'.
            0 => new[] { 1, 5, 7, 17, 22, 26, 27, 28, 33, 34, 36 },
            1 => new[] { 9 },
            2 => new[] { 4, 12, 16, 18, 21, 31, 35 },
            3 => new[] { 3, 6, 19, 20, 30 },
            9 => new[] { 8 },
            10 => new[] { 44 },
            _ => new[] { 2, 11, 23, 24, 25, 29, 32 },
        };
    }
}
