// LootContainer.cs
// Remake (2026-11-03, for mods): a closed container floating in an orbit (stations.json "orbit.containers", Modding.ModWorld's
// OrbitSpec), after EVERSPACE 2's item containers (decompiled BP_ItemContainer and its EContainerType). It keeps its loot
// until it is opened, then throws it out as crates (one a stack, kept until emptied) for the tractor beam:
//   shootable     a hit opens it (ES2's OpenOnHit: any damage by the player, or while the player is within 2 km)
//   bulletproof   shots bounce off it (ES2's OpenOnInteract, "Bulletproof Container")
// Either opens with the action key (F) within OpenRangeM, in front of the ship (ES2's InteractComponent: InteractRadius 100 m
// x the ES2 ships' 3.6, no hold: InteractChargeDuration 0, bMustBeOnScreen). Its marker and lock are the radar's objects'
// (Target.RadarObjects), neutral. ES2's secure containers (OpenOnUnlocked: energized through conductors) aren't here.
// Loot: ES2's loot_types.CSV, a "Container" LootRolls 1-3 drops, a "RareContainer" (here the bulletproof one) 2-4: as many
// freighter loot rolls.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class LootContainer : MonoBehaviour
    {
        const float M = 0.05f;
        /// <summary>How far the action key opens one (Unity metres).</summary>
        public const float OpenRangeM = 100f * 3.6f;
        /// <summary>Its look: the game's cargo container at this size.</summary>
        const float ModelScale = 3f;
        /// <summary>Its crates fly apart at this times a crate's usual push.</summary>
        const float SpillBurst = 2f;

        public static readonly List<LootContainer> All = new List<LootContainer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public bool Bulletproof { get; private set; }
        public bool Opened { get; private set; }
        public Target Target { get; private set; }
        public IReadOnlyList<ItemStack> Loot => loot;
        readonly List<ItemStack> loot = new List<ItemStack>();
        int race;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void OnDestroy() { if (Target != null) Target.RadarObjects.Remove(Target); }

        /// <summary>A closed container at 'at' (Unity) holding 'cargo'.</summary>
        public static LootContainer Spawn(Vector3 at, IEnumerable<ItemStack> cargo, bool bulletproof, int race)
        {
            var go = Crate.Model(Crate.LookContainer, race, null, at, Random.rotation);
            go.name = bulletproof ? "Bulletproof container" : "Container";
            go.transform.localScale *= ModelScale;
            var c = go.AddComponent<LootContainer>();
            c.Bulletproof = bulletproof;
            c.race = race;
            foreach (var s in cargo) if (s != null && s.amount > 0) c.loot.Add(s.Clone());
            var t = c.Target = go.AddComponent<Target>();
            t.hp = t.maxHp = 1f;   // one hit
            t.invulnerable = bulletproof;
            t.radius = 600f * M;
            t.race = race;
            t.displayName = bulletproof ? Localization.Extra("containerBulletproof", "Bulletproof container") : Localization.Extra("container", "Container");
            t.plateNameOnly = t.plateNoIcon = true;
            t.destroyedSound = CombatAssets.Pick(CombatAssets.Load()?.garbageExplosion);
            t.Died += _ => { ShipBurn.ManualBurst(c.transform.position, 1f, 1600f, 1800f, 500f); c.Open(); };
            Target.RadarObjects.Add(t);
            return c;
        }

        /// <summary>ES2's loot_types.CSV: how many drops a container leaves (its Container / RareContainer rows' first two cells).</summary>
        public const int RollsMin = 1, RollsMax = 3, BulletproofRollsMin = 2, BulletproofRollsMax = 4;

        /// <summary>Lays out the orbit's containers (stations.json "orbit.containers"; none without), each RollsMin..RollsMax
        /// freighter loot rolls, a bulletproof one BulletproofRollsMin..Max; the number placed.</summary>
        public static int SpawnOrbit(Database db, OrbitLayout layout)
        {
            var spec = layout != null ? Modding.ModWorld.OrbitOf(layout.stationIndex) : null;
            if (spec == null || spec.containerCount <= 0) return 0;
            var centre = spec.containerCentre ?? new Vector3(30000f, 0f, 50000f);
            float half = spec.containerSpread * 0.5f;
            int race = db?.Systems.Find(s => s.index == layout.systemIndex)?.raceId ?? Standing.Pirate;
            for (int i = 0; i < spec.containerCount; i++)
            {
                bool proof = Random.value < spec.containerBulletproof;
                var cargo = new List<ItemStack>();
                int rolls = proof ? Random.Range(BulletproofRollsMin, BulletproofRollsMax + 1) : Random.Range(RollsMin, RollsMax + 1);
                for (int k = 0; k < rolls; k++) cargo.AddRange(WreckField.Loot(db));
                var at = centre + new Vector3(Random.Range(-half, half), Random.Range(-half, half) * 0.3f, Random.Range(-half, half));
                Spawn(OrbitLayout.ToUnity(at), cargo, proof, race);
            }
            return spec.containerCount;
        }

        /// <summary>The closed container the action key opens from 'ship': within OpenRangeM, in front, the nearest; null = none.</summary>
        public static LootContainer InReach(Transform ship)
        {
            if (ship == null) return null;
            LootContainer best = null;
            float bestD = OpenRangeM * OpenRangeM;
            foreach (var c in All)
            {
                if (c == null || c.Opened) continue;
                var d = c.transform.position - ship.position;
                float d2 = d.sqrMagnitude;
                if (d2 > bestD || Vector3.Dot(d, ship.forward) <= 0f) continue;
                best = c;
                bestD = d2;
            }
            return best;
        }

        /// <summary>Throws its loot out, a crate a stack, and drops its marker; a shot one is gone, a bulletproof one stays empty.</summary>
        public void Open()
        {
            if (Opened) return;
            Opened = true;
            var pos = transform.position;
            foreach (var s in loot)
            {
                if (s.amount <= 0) continue;
                var crate = Crate.Spawn(pos, new[] { s }, race, Crate.LookContainer);
                crate.keep = true;
                crate.Launch(Random.onUnitSphere, SpillBurst);
            }
            loot.Clear();
            if (Target != null)
            {
                Target.RadarObjects.Remove(Target);
                if (Target.Alive) Destroy(Target);   // the opened shell: no marker, no lock, shots fly through
                else { Destroy(gameObject); return; }
            }
            Target = null;
        }
    }
}
