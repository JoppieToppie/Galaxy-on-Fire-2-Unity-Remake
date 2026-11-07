// WreckField.cs
// Remake (2026-11-03, for mods): an orbit's field of wrecks to salvage (stations.json "orbit.wrecks", Modding.ModWorld's
// OrbitSpec), after EVERSPACE 2's wreck fields (its Wreck Field, Ghost Fleet and Warship Remains locations: war wrecks with
// loot containers among them). Each visit lays out 'count' wreck containers (the junk crate's look) at random within the
// field's cube, each with a freighter's loot roll (NpcTables.RollLoot; an empty roll: a little Space Waste), kept until
// emptied (Crate.keep: no 60 s expiry). Salvaged like any crate (the tractor beam, flying into it).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public static class WreckField
    {
        /// <summary>What an empty roll leaves (item 99, Space Waste, as the game's junk crates hold).</summary>
        public const int ScrapItem = 99;

        /// <summary>Lays out the orbit's wreck field (none without one); the containers placed.</summary>
        public static int Spawn(Database db, OrbitLayout layout)
        {
            var spec = layout != null ? Modding.ModWorld.OrbitOf(layout.stationIndex) : null;
            if (spec == null || spec.wreckCount <= 0) return 0;
            var centre = spec.wreckCentre ?? new Vector3(-40000f, 0f, 60000f);
            float half = spec.wreckSpread * 0.5f;
            int race = db?.Systems.Find(s => s.index == layout.systemIndex)?.raceId ?? Standing.Pirate;
            for (int i = 0; i < spec.wreckCount; i++)
            {
                var at = centre + new Vector3(Random.Range(-half, half), Random.Range(-half, half) * 0.3f, Random.Range(-half, half));
                Crate.Spawn(OrbitLayout.ToUnity(at), Loot(db), race, Crate.LookJunk).keep = true;
            }
            return spec.wreckCount;
        }

        /// <summary>One wreck's cargo: a freighter's loot roll, else 1-5 Space Waste.</summary>
        public static List<ItemStack> Loot(Database db)
        {
            var cargo = db != null ? NpcTables.RollLoot(db, true) : new List<ItemStack>();
            if (cargo.Count == 0) cargo.Add(new ItemStack(ScrapItem, Random.Range(1, 6)));
            return cargo;
        }
    }
}
