// ShipInNeed.cs
// Remake (2026-11-06, for mods): a "Ship In Need" (encounter kind "needy", World.CruiseEncounterSpawner), after EVERSPACE 2's
// BP_Encounter_ItemsNeeded and its task (BP_Job_ItemsNeeded_Task_01): a damaged freelancer or freighter (its hull at 10-40 %,
// still) that needs a repair item (ES2's ValidHealItems: nano injector, nanobots; here GoF2's Nanotech 121 or Medical
// Supplies 98, Need units: the one the pilot carries enough of first, else one carried, else either). Within NoticeM its
// request on the HUD; within reach in front (LootContainer.OpenRangeM, ES2's InteractComponent) with enough aboard, the
// action key ("HELP") hands it over (Shop.RemoveFromCargo): the ship's hull comes back at RepairPerS (EnqueueHullRepair
// RepairRatePerSecond 75) and the encounter pays (GivesCreditsReward: CruiseEncounterSpawner.BattlePay). The ship lost, or the
// pilot past EncounterWatch.LeaveM (bFailIfPlayerLeaves), ends it.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.UI;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ShipInNeed : MonoBehaviour
    {
        public const int Need = 3;
        public const float NoticeM = 2000f, RepairPerS = 75f;
        public static readonly int[] Items = { 121, 98 };   // Nanotech, Medical Supplies

        static readonly List<ShipInNeed> Active = new List<ShipInNeed>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();

        SpaceLevel level;
        NpcShip ship;
        int item, amount, pay;
        bool told, helped;
        float repairLeft;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <summary>The item it asks for, given what the pilot carries: one with 'need' aboard first, else one aboard, else the first.</summary>
        public static int Pick(IList<int> items, System.Func<int, int> carried, int need)
        {
            foreach (var i in items) if (carried(i) >= need) return i;
            foreach (var i in items) if (carried(i) > 0) return i;
            return items[Random.Range(0, items.Count)];
        }

        static int Carried(int item)
        {
            int n = 0;
            foreach (var s in Session.Cargo) if (s != null && s.item == item) n += s.amount;
            return n;
        }

        public static ShipInNeed Start(SpaceLevel level, NpcShip ship, int amount, int pay)
        {
            if (level == null || ship == null) return null;
            var go = new GameObject("Ship in need");
            go.transform.SetParent(level.transform, false);
            var w = go.AddComponent<ShipInNeed>();
            w.level = level; w.ship = ship; w.amount = Mathf.Max(1, amount); w.pay = pay;
            w.item = Pick(Items, Carried, w.amount);
            var hp = ship.Hp;
            if (hp != null) { hp.shield = 0f; hp.hull = Mathf.Max(1, Mathf.RoundToInt(hp.maxHull * Random.Range(0.1f, 0.4f))); ship.Target.hp = hp.hull; }
            return w;
        }

        bool InReach(Transform pilot)
        {
            if (helped || pilot == null || ship == null) return false;
            var d = ship.transform.position - pilot.position;
            return d.sqrMagnitude <= LootContainer.OpenRangeM * LootContainer.OpenRangeM && Vector3.Dot(d, pilot.forward) > 0f && Carried(item) >= amount;
        }

        /// <summary>The action prompt for the HUD, null = none in reach.</summary>
        public static string Prompt(Transform pilot)
        {
            foreach (var w in Active) if (w != null && w.InReach(pilot)) return Localization.Extra("hudHelp", "HELP");
            return null;
        }

        /// <summary>The action key: hand the goods to a ship in need in reach; true = done.</summary>
        public static bool Act(Transform pilot)
        {
            foreach (var w in Active)
            {
                if (w == null || !w.InReach(pilot)) continue;
                Shop.RemoveFromCargo(w.item, w.amount);
                w.helped = true;
                w.repairLeft = w.ship.Hp != null ? w.ship.Hp.maxHull - w.ship.Hp.hull : 0f;
                Session.Credits += w.pay;
                w.level?.Navigation?.Post(string.Format(Localization.Extra("needyThanks", "Thank you! +{0} cr"), w.pay));
                return true;
            }
            return false;
        }

        void Update()
        {
            if (level == null || level.Player == null) { Destroy(gameObject); return; }
            if (ship == null || ship.Gone || ship.Target == null || !ship.Target.Alive)
            {
                if (!helped) level.Navigation?.Post(Localization.Extra("needyLost", "The ship in need is lost"));
                Destroy(gameObject);
                return;
            }
            float d = (level.Player.transform.position - ship.transform.position).magnitude;
            if (helped)
            {
                var hp = ship.Hp;
                if (hp != null && repairLeft > 0f)   // (EnqueueHullRepair at 75 a second)
                {
                    int step = Mathf.Min(Mathf.CeilToInt(RepairPerS * Time.deltaTime), (int)repairLeft);
                    hp.hull = Mathf.Min(hp.maxHull, hp.hull + step);
                    ship.Target.hp = hp.hull;
                    repairLeft -= step;
                }
                if (repairLeft <= 0f || d > EncounterWatch.LeaveM) Destroy(gameObject);
                return;
            }
            if (d > EncounterWatch.LeaveM) { Destroy(gameObject); return; }
            if (!told && d < NoticeM)
            {
                told = true;
                level.Navigation?.Post(string.Format(Localization.Extra("needyAsks", "A damaged ship needs {0} x {1}"), amount, ItemInfo.ItemName(item)));
            }
        }
    }
}
