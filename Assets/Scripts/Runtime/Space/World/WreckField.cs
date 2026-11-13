// WreckField.cs
// Remake (2026-11-03, for mods): an orbit's field of wrecks to salvage (stations.json "orbit.wrecks", Modding.ModWorld's
// OrbitSpec), after EVERSPACE 2's wreck fields (its Wreck Field, Ghost Fleet and Warship Remains locations: war wrecks with
// loot containers among them). Each visit lays out 'count' wreck containers (the junk crate's look) at random within the
// field's cube, each with a freighter's loot roll (NpcTables.RollLoot; an empty roll: a little Space Waste), kept until
// emptied (Crate.keep: no 60 s expiry). Salvaged like any crate (the tractor beam, flying into it). With "models" (2026-11-05,
// ES2's wreck meshes) 'hulks' wreck hulls are laid among them, one of the models at random each, the largest model 'size'
// game units at its largest side and the others in proportion as authored (x0.85-1.15 each), turned at random: the same for every player (seeded by the station), each a box of its bounds to
// slide along and steer around (Obstacle, turning with it), loaded with the mod crates (ModCrates: none before they are).

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
            SpawnHulks(spec, centre, half, layout.stationIndex);
            return spec.wreckCount;
        }

        /// <summary>The field's wreck hulls (its "models"); how many were placed.</summary>
        public static int SpawnHulks(Modding.ModWorld.OrbitSpec spec, Vector3 centre, float half, int station)
        {
            if (spec.wreckModels == null || spec.wreckModels.Count == 0 || spec.owner == null) return 0;
            int count = spec.wreckHulks >= 0 ? spec.wreckHulks : Mathf.Min(8, spec.wreckModels.Count * 2);
            if (count <= 0 || !Modding.ModCrates.Ready) return 0;
            var rng = new System.Random(station * 7919 + 17);
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            float largest = 0f;
            foreach (var m in spec.wreckModels) largest = Mathf.Max(largest, Modding.ModCrates.ExtentOf(spec.owner.Id + "|" + m));
            if (largest <= 1e-6f) return 0;
            var root = new GameObject("Wreck field");
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                string key = spec.owner.Id + "|" + spec.wreckModels[rng.Next(spec.wreckModels.Count)];
                var at = centre + new Vector3(R(-half, half), R(-half, half) * 0.3f, R(-half, half));
                var rot = Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));
                float size = spec.wreckSize * OrbitLayout.MetersPerUnit * Modding.ModCrates.ExtentOf(key) / largest * R(0.85f, 1.15f);
                var go = Modding.ModCrates.InstantiateFitted(key, OrbitLayout.ToUnity(at), rot, size, root.transform);
                if (go == null) continue;
                go.name = "Wreck hull";
                var b = Modding.ModCrates.LocalBounds(go);
                var o = go.AddComponent<Obstacle>();
                o.rotates = true;   // (the box in the hull's own frame: Obstacle turns the point back by its rotation)
                o.volumes.Add(CollisionVolume.Box(b.center, b.extents));
                made++;
            }
            if (made == 0) Object.Destroy(root);
            return made;
        }

        /// <summary>One wreck's cargo: a freighter's loot roll, else 1-5 Space Waste.</summary>
        public static List<ItemStack> Loot(Database db)
        {
            var cargo = db != null ? NpcTables.RollLoot(db, true) : new List<ItemStack>();
            if (cargo.Count == 0) cargo.Add(new ItemStack(ScrapItem, Random.Range(1, 6)));
            return cargo;
        }

        /// <summary>Remake (Settings.DerelictWrecks): a hacked derelict freighter's hold: a wreck's cargo; one time in five
        /// 2-5 t of the rare goods a capital ship carries; one time in eight a piece of its gear (CapitalShips.GearPool: a
        /// weapon or equipment of tech level 5+, a secondary 10-20 of it).</summary>
        public static List<ItemStack> DerelictLoot(Database db)
        {
            var cargo = Loot(db);
            if (Random.Range(0, 5) == 0) CapitalShips.Add(cargo, CapitalShips.RareGoods[Random.Range(0, CapitalShips.RareGoods.Length)], Random.Range(2, 6));
            if (db != null && Random.Range(0, DerelictGearOdds) == 0 && CapitalShips.GearPool(db) is var pool && pool.Count > 0)
            {
                var it = pool[Random.Range(0, pool.Count)];
                CapitalShips.Add(cargo, it.index, it.TypeId == 1 ? Random.Range(10, 21) : 1);
            }
            return cargo;
        }

        /// <summary>Remake (players' feedback, 2026-11-13: a crate needs a tractor beam): 'cargo' straight into the player's
        /// hold as far as it has room, like a captured crate's (missiles of a mounted launcher's type reload it; "<n>t
        /// <item>" to 'say' for each stack); the rest, which found no room.</summary>
        public static List<ItemStack> TakeAboard(Database db, List<ItemStack> cargo, System.Action<string> say = null)
        {
            var rest = new List<ItemStack>();
            foreach (var s in cargo)
            {
                int n = Mathf.Min(s.amount, Shop.FreeCargo(db));
                if (n > 0)
                {
                    var mounted = db.Item(s.item)?.TypeId == 1 ? Session.Equipment.Find(e => e.item == s.item) : null;
                    if (mounted != null) mounted.amount += n; else Shop.AddToCargo(s.item, n);
                    say?.Invoke($"{n}t {GameNames.Item(s.item)}");
                }
                if (n < s.amount) rest.Add(new ItemStack(s.item, s.amount - n));
            }
            return rest;
        }

        /// <summary>One derelict in this many holds a piece of gear / a blueprint.</summary>
        public const int DerelictGearOdds = 8, DerelictBlueprintOdds = 12;

        /// <summary>Remake (Settings.DerelictWrecks): the blueprint a hacked derelict teaches (Traffic.OnHackWon, one time in
        /// DerelictBlueprintOdds): one a lounge's story character sells (AgentData.StoryAgents; Sao Perula's once the
        /// Valkyrie add-on is won, like its seller) that the player doesn't know; -1 = none left. Not one a mod took over
        /// (its own sources) and not the campaign's or the hidden wrecks'.</summary>
        public static int DerelictBlueprint(Database db)
        {
            var pool = new List<int>();
            if (db != null)
                foreach (var s in AgentData.StoryAgents)
                    if (s.sellBlueprint >= 0 && (s.station != 106 || Story.Dlc1Won) && db.Item(s.sellBlueprint) != null && !Blueprints.IsUnlocked(s.sellBlueprint)
                        && Modding.ModBlueprints.Of(s.sellBlueprint) == null && !pool.Contains(s.sellBlueprint))
                        pool.Add(s.sellBlueprint);
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : -1;
        }
    }
}
