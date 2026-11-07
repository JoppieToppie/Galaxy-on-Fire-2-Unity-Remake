// EncounterWatch.cs
// Remake (2026-11-03): a cruise encounter that ends in a fight won (CruiseEncounterSpawner's battle and high-risk area, after
// EVERSPACE 2's encounter tasks and BP_EncounterBase.GiveEncounterRewards): watches the raiders it spawned; when they are
// all gone the pilot is paid, "Encounter won: +N cr". A high-risk area sends its next wave first (each the size of the first,
// at the area), and after the last one leaves a crate of three loot rolls. The player flying off (past LeaveM from the
// area) or the level ending stops it, unpaid (ES2's bFailIfPlayerLeaves).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class EncounterWatch : MonoBehaviour
    {
        public const float LeaveM = 15000f;
        const float M = 0.05f;

        SpaceLevel level;
        readonly List<NpcShip> ships = new List<NpcShip>();
        int pay, wavesLeft, waveSize;
        bool loot;
        Vector3 centreGame;

        public static EncounterWatch Start(SpaceLevel level, List<NpcShip> ships, int pay, bool loot, int waves = 1, int waveSize = 0, Vector3 centre = default)
        {
            if (level == null) return null;
            var w = new GameObject("Encounter watch").AddComponent<EncounterWatch>();
            w.level = level;
            w.ships.AddRange(ships.FindAll(s => s != null));
            w.pay = pay;
            w.loot = loot;
            w.wavesLeft = Mathf.Max(1, waves) - 1;
            w.waveSize = waveSize;
            w.centreGame = centre;
            return w;
        }

        /// <summary>The raiders still in the fight: spawned, alive, not gone.</summary>
        public static bool AnyLeft(List<NpcShip> ships)
        {
            foreach (var s in ships) if (s != null && !s.Gone && s.Target != null && s.Target.Alive) return true;
            return false;
        }

        void Update()
        {
            if (level == null || level.Player == null) { Destroy(gameObject); return; }
            if (ships.Count > 0)
            {
                var c = ships[0] != null ? ships[0].transform.position : level.Player.transform.position;
                if (centreGame != default) c = new Vector3(centreGame.x, centreGame.y, -centreGame.z) * M;
                if ((level.Player.transform.position - c).sqrMagnitude > LeaveM * LeaveM) { Destroy(gameObject); return; }
            }
            if (AnyLeft(ships)) return;
            if (wavesLeft > 0 && level.Traffic != null)
            {
                wavesLeft--;
                ships.Clear();
                for (int i = 0; i < waveSize; i++)
                    ships.Add(CruiseEncounterSpawner.Raider(level.Traffic, centreGame + new Vector3(Random.Range(-6000f, 6000f), Random.Range(-3000f, 3000f), Random.Range(-6000f, 6000f))));
                level.Traffic.ConnectPlayers();
                level.Navigation?.Post(Localization.Extra("encounterWave", "More hostiles inbound"));
                return;
            }
            if (loot)
            {
                var db = level.Database ?? Database.Shared;
                var cargo = new List<ItemStack>();
                for (int k = 0; k < 3; k++)
                    foreach (var s in NpcTables.RollLoot(db, true))
                    {
                        var have = cargo.Find(x => x.item == s.item);
                        if (have != null) have.amount += s.amount; else cargo.Add(new ItemStack(s.item, s.amount));
                    }
                if (cargo.Count > 0) Crate.Spawn(new Vector3(centreGame.x, centreGame.y, -centreGame.z) * M, cargo, Standing.Pirate, Crate.LookJunk);
            }
            if (pay > 0)
            {
                Session.Credits += pay;
                level.Navigation?.Post(string.Format(Localization.Extra("encounterWon", "Encounter won: +{0} cr"), pay));
            }
            Destroy(gameObject);
        }
    }
}
