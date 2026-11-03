// ModTraffic.cs
// Remake mods: NPC fighters in the mods' ships. Whenever the game rolls an NPC fighter for a race (NpcTables.RandomFighter /
// StrongFighter: the orbit's locals, raiders and pirates, jumpers, escorts, guards, capital-ship escorts and fleet-battle
// wings, the story levels' generic pilots), it takes one of the mods' ships of that race instead at the chance the Gameplay
// option "Mod ships in traffic" sets (Settings.ModShipTraffic; Off / 10 / 25 / 50 %). A mod campaign's own "trafficShips"
// win over it. Candidates: every ship of the mods that are on with that "race" (0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian,
// 8 pirates and the minor races, 9 Void; the Specters none), available now (its "available" condition, ModUnlocks), its model
// built (ModShips; a "gof2:<model>" clone: the game's prefab) and no longer than MaxLength (a 300 m cruiser doesn't patrol as a fighter); weighted by ships.json
// "traffic": { "weight": 1, "races": [8] } ("weight": 0 = never; "races" = the races that fly it, default its own). A
// strong fighter (a capital ship's escort) is one of the dearer half. NPC hulls and guns don't depend on the ship flown
// (NpcTables), so only the look changes. A customizable (kit) ship's NPCs fly random builds of it (RandomBuild, Traffic.Create:
// parts, tier, colour scheme, now and then a skin, a livery or a decal; "traffic": { "randomBuild": false } = its default build), drawn from the
// parts already loaded; each docking loads a few more random builds' parts in the background (Warm). A boss hull ("traffic":
// { "boss": true }, the GoF3 Ships mod's five GoF3 bosses) never flies as a fighter: the pirate bosses fly it instead
// (PickBoss, at the same chance, up to MaxBossLength long). A capital ship or freighter ("traffic": { "capital": ... })
// never flies as a fighter either (ModCapitalShips).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModTraffic
    {
        /// <summary>The longest hull that flies as a fighter (Unity metres; PlayerHull.Big's 150 m).</summary>
        const float MaxLength = 150f;
        /// <summary>The longest boss hull (Unity metres): a pirate boss leads its escorts, it doesn't dock or land.</summary>
        const float MaxBossLength = 400f;

        static readonly Dictionary<int, float> lengths = new Dictionary<int, float>();
        static int lengthRevision = -1;
        // Candidates per race key, made again after CacheSeconds (an "available" condition may come true meanwhile) or a
        // change of the mods: a fighter roll is cheap again (a full scan took ~2 ms).
        static readonly Dictionary<int, (List<(int ship, float weight)> list, float made, int revision)> cache =
            new Dictionary<int, (List<(int, float)>, float, int)>();
        /// <summary>The cache's key for the boss hulls (no race key is below -1).</summary>
        const int BossKey = -100;
        const float CacheSeconds = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lengths.Clear(); lengthRevision = -1; cache.Clear(); }

        /// <summary>The fighter 'fallback' (the game's pick for 'race'), or now and then one of the mods' ships of that race.</summary>
        public static int Pick(int race, int fallback, bool strong = false)
        {
            // A mod race (ModRaces) flies its mods' ships whenever it has any, whatever the chance: the game has none of its own.
            bool faction = ModRaces.Is(race);
            float chance = faction ? 1f : Settings.ModShipTrafficChance;
            if (chance <= 0f || (!faction && ModCampaigns.HasTrafficShips(race)) || Random.value >= chance) return fallback;
            var pool = Candidates(race);
            if (pool.Count == 0) return fallback;
            if (strong && pool.Count > 1)
            {
                var db = Database.Load();
                pool = pool.OrderByDescending(p => db.Ship(p.ship)?.price ?? 0).Take((pool.Count + 1) / 2).ToList();
            }
            float total = pool.Sum(p => p.weight), at = Random.value * total;
            foreach (var (ship, weight) in pool) { if ((at -= weight) <= 0f) return ship; }
            return pool[pool.Count - 1].ship;
        }

        /// <summary>TrafficPlan's pirate boss: the game's 'fallback', or now and then one of the mods' boss hulls (at the
        /// "Mod ships in traffic" chance).</summary>
        public static int PickBoss(int fallback)
        {
            float chance = Settings.ModShipTrafficChance;
            if (chance <= 0f || Random.value >= chance) return fallback;
            var pool = Cached(BossKey);
            if (pool.Count == 0) return fallback;
            float total = pool.Sum(p => p.weight), at = Random.value * total;
            foreach (var (ship, weight) in pool) { if ((at -= weight) <= 0f) return ship; }
            return pool[pool.Count - 1].ship;
        }

        /// <summary>The mods' ships an NPC of 'race' may fly now, with their weights (a copy).</summary>
        public static List<(int ship, float weight)> Candidates(int race)
        {
            int key = Key(race);
            return key < 0 ? new List<(int, float)>() : Cached(key);
        }

        static List<(int ship, float weight)> Cached(int key)
        {
            if (cache.TryGetValue(key, out var have) && have.revision == ModManager.Revision
                && Time.realtimeSinceStartup - have.made < CacheSeconds) return new List<(int, float)>(have.list);
            var list = Scan(key);
            // A ship whose model is still loading isn't in it: keep such a list only briefly.
            cache[key] = (list, ModShips.Ready ? Time.realtimeSinceStartup : Time.realtimeSinceStartup - CacheSeconds + 0.5f, ModManager.Revision);
            return new List<(int, float)>(list);
        }

        static List<(int ship, float weight)> Scan(int key)
        {
            var list = new List<(int, float)>();
            var db = Database.Load();
            foreach (var c in CustomShips.All)
            {
                float weight = c.traffic != null ? c.traffic.weight : 1f;
                if (weight <= 0f || !CustomShips.Offered(c.index) || db.Ship(c.index) == null) continue;
                bool boss = c.traffic != null && c.traffic.boss;
                if (boss != (key == BossKey)) continue;   // (a boss hull is never a fighter)
                if (!string.IsNullOrEmpty(c.traffic?.capital)) continue;   // a capital ship or freighter (ModCapitalShips)
                bool flies = boss || (c.traffic?.races != null && c.traffic.races.Length > 0 ? c.traffic.races.Any(r => Key(r) == key) : Key(c.race) == key);
                if (!flies || !ModUnlocks.ShipAvailable(c.index)) continue;
                float length = Length(c.index);
                if (length <= 0f || length > (boss ? MaxBossLength : MaxLength)) continue;   // not built (yet), or too big
                list.Add((c.index, weight));
            }
            return list;
        }

        // ---- random builds of customizable ships ---------------------------------------------------------------------

        /// <summary>Warm-up per docking: how many random builds' parts start loading, and a kit's loaded part files past which
        /// no more of it are (the parts stay loaded for the run).</summary>
        static int WarmBuilds => Application.isMobilePlatform ? 1 : 3;
        static int WarmCap => Application.isMobilePlatform ? 40 : 150;

        /// <summary>Traffic.Create: the assembly an NPC of a kit ship flies, a random build of it (RandomBuild) when its
        /// "traffic" allows; null = the ship's own (not a kit ship).</summary>
        public static AssemblyData Assembly(int ship)
        {
            var c = ModShipKits.KitShip(ship);
            if (c == null || c.traffic?.randomBuild == false || ModShipKits.KitOf(c) == null) return null;
            var build = RandomBuild(ship, loadedOnly: true);
            return build == null ? null : ModShipKits.VariantAssembly(ship, build);
        }

        /// <summary>A random build of a kit ship: a part the type may fit in every slot, a tier, a colour scheme (one of the
        /// kit's presets, else a palette colour per slot) and now and then a skin. 'loadedOnly': only parts, tiers and skins
        /// already loaded (ModShips), so it can be assembled at once; a slot with nothing loaded keeps the default build's
        /// part. Null: not a kit ship.</summary>
        public static ModShipKits.Build RandomBuild(int ship, bool loadedOnly)
        {
            var c = ModShipKits.KitShip(ship);
            var kit = ModShipKits.KitOf(c);
            if (kit == null) return null;
            var fallback = ModShipKits.DefaultBuild(c, kit);
            var b = new ModShipKits.Build { tier = Random.Range(0, kit.MaxTier + 1) };
            foreach (string slot in kit.slots)
            {
                var allowed = kit.PartsFor(slot, c.kitType);
                if (loadedOnly) allowed = allowed.Where(p => ModShips.PartLoaded(kit, p)).ToList();
                if (allowed.Count > 0) b.parts[slot] = allowed[Random.Range(0, allowed.Count)].id;
                else if (fallback.parts.TryGetValue(slot, out var id)) b.parts[slot] = id;
            }
            var presets = kit.presets;
            if (presets.Count > 0 && Random.value < 0.75f)
                foreach (var kv in presets[Random.Range(0, presets.Count)].colors) b.colors[kv.Key] = kv.Value;
            else
                foreach (var slot in kit.colorSlots)
                {
                    var palette = kit.PaletteOf(slot);
                    if (!slot.IsStrength && palette.Count > 0) b.colors[slot.id] = palette[Random.Range(0, palette.Count)].id;
                }
            var skins = kit.SkinsFor(c.kitType);
            b.skin = skins.Count > 0 && Random.value < 0.3f ? skins[Random.Range(0, skins.Count)].id : ModShipKits.Build.NoSkin;
            var liveries = kit.LiveriesFor(c.kitType);
            b.livery = liveries.Count > 0 && Random.value < 0.2f ? liveries[Random.Range(0, liveries.Count)].id : ModShipKits.Build.NoSkin;
            var decals = kit.DecalsFor(c.kitType);
            b.decal = decals.Count > 0 && Random.value < 0.3f ? decals[Random.Range(0, decals.Count)].id : ModShipKits.Build.NoSkin;
            if (kit.FindSkin(b.skin) is ModShipKits.Skin picked)
            {
                // A skin with colours and a livery (EVERSPACE 2's) wears them, as when the player picks it.
                foreach (var kv in picked.colors) b.colors[kv.Key] = kv.Value;
                if (picked.livery != null) b.livery = picked.livery;
                if (picked.decal != null) b.decal = picked.decal;
            }
            var n = ModShipKits.Normalize(kit, c.kitType, b, fallback);
            if (!loadedOnly) return n;
            // The tier's extensions or the skin's textures not loaded yet: lower tiers, then without the skin.
            while (n.tier > 0 && !ModShips.BuildLoaded(ship, n)) n.tier--;
            if (!ModShips.BuildLoaded(ship, n) && (n.skin != ModShipKits.Build.NoSkin || n.livery != ModShipKits.Build.NoSkin || n.decal != ModShipKits.Build.NoSkin))
            {
                n.skin = n.livery = n.decal = ModShipKits.Build.NoSkin;
                n = ModShipKits.Normalize(kit, c.kitType, n, fallback);
            }
            return ModShips.BuildLoaded(ship, n) ? n : fallback;
        }

        /// <summary>StationLevel (docked, where a load doesn't stutter the flight): a few random builds of the kit ships NPCs may
        /// fly start loading their parts, so the traffic's random builds draw on more of the kits over time.</summary>
        public static void Warm()
        {
            if (!ModShips.Ready) return;
            var ships = CustomShips.All.Where(c => !string.IsNullOrEmpty(c.kit) && c.traffic?.randomBuild != false
                                                   && (c.traffic == null || c.traffic.weight > 0f) && CustomShips.Offered(c.index)
                                                   && ModShipKits.KitOf(c) is ModShipKits.Kit k && ModShips.LoadedParts(k) < WarmCap).ToList();
            for (int i = 0; i < WarmBuilds && ships.Count > 0; i++)
            {
                int ship = ships[Random.Range(0, ships.Count)].index;
                ModShips.Warm(ship, RandomBuild(ship, loadedOnly: false));
            }
        }

        /// <summary>A race as the traffic groups it: the four races, pirates (8, also the minor races), the Void (9); the
        /// Specters (10) -1.</summary>
        static int Key(int race) => Modding.ModRaces.Is(race) ? race : race >= 0 && race <= 3 ? race : race == 9 ? 9 : race == 10 ? -1 : race < 0 ? -1 : 8;

        /// <summary>A built mod ship's length (Unity metres, its meshes' bounds along its nose; 0: not built).</summary>
        public static float HullLength(int ship) => Length(ship);

        static float Length(int ship)
        {
            if (lengthRevision != ModManager.Revision) { lengths.Clear(); lengthRevision = ModManager.Revision; }
            if (lengths.TryGetValue(ship, out var have)) return have;
            float length = 0f;
            bool found = false;
            foreach (var (index, _, template) in ModShips.Built)
            {
                if (index != ship || template == null) continue;
                length = Measure(template);
                found = true;
                break;
            }
            // A mod ship on one of the game's own models ("model": "gof2:<name>", Database.ShipModels, a clone of a game
            // ship) has no built template (ModShips builds nothing for it): measure the game's prefab instead (#236: such
            // ships never flew in traffic, their length stayed 0).
            if (!found)
            {
                var db = Database.Load();
                if (db != null && db.ShipModels.TryGetValue(ship, out var gameModel) && GameModels.Find(db, gameModel) is AssemblyData entry)
                {
                    var prefab = Visuals.AssembledObject.LoadPrefab(entry);
                    if (prefab != null) length = Measure(prefab);
                }
            }
            if (length > 0f) lengths[ship] = length;   // (not built yet: asked again next time)
            return length;
        }

        /// <summary>A template's length along its nose (Unity metres, its meshes' bounds without the engine glows; 0: none).</summary>
        static float Measure(GameObject template)
        {
            var root = template.transform.worldToLocalMatrix;
            var b = new Bounds(); bool any = false;
            foreach (var mf in template.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.StartsWith("engine")) continue;
                var m = root * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2f - 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? b.size.z : 0f;
        }
    }
}
