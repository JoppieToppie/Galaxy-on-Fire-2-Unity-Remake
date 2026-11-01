// TrafficPlanNpcShips.cs
// TrafficPlan: the mods' NPC ships (Modding.ModNpcShips, npcships.json; part of the partial TrafficPlan). A type's ships are
// built here: its host (its hull: a ships.json ship flown like the mods' capital ships and freighters, ModCapitalShips, or a
// game hull by "looksLike"), its turrets (its "hardpoints", World.CapitalSubsystems on the host; else the game hull's own
// turrets), its escorts, its loot (rolled now: SpawnSpec.lootItem / extraLoot), its dock and launches (World.CapitalShip
// on the host). The game's own types, when a mod changed them, take the same steps on the game's ships (ApplyNpcType:
// AddCapitalShipAt, the freighters, the pirate boss); untouched they are the game's. New types come into an orbit by their
// "spawn" rules (AddModNpcShips, after the rest of the traffic).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public static partial class TrafficPlan
    {
        /// <summary>A capital ship kind's game type key (ModNpcShips.GameKeys).</summary>
        public static string NpcKeyOf(int capitalKind) => capitalKind == SpawnSpec.CapitalCarrier ? ModNpcShips.Carrier
            : capitalKind == SpawnSpec.CapitalVossk ? ModNpcShips.VosskBattleship : ModNpcShips.Battleship;

        /// <summary>A game look's capital kind (SpawnSpec.Capital*), 0 = not a capital ship.</summary>
        static int CapitalKindOf(string look) => look == ModNpcShips.Carrier ? SpawnSpec.CapitalCarrier
            : look == ModNpcShips.VosskBattleship ? SpawnSpec.CapitalVossk : look == ModNpcShips.Battleship ? SpawnSpec.CapitalBattleship : 0;

        /// <summary>The game hull's turrets of a look ("battleship", "carrier", "vossk_battleship") as hardpoints in the host's
        /// frame (the Mod Builder's "the game's turrets"): the battleship faces game +Z like a ship (its offsets are the frame's),
        /// the carrier and the Vossk battleship are objects turned the other way round ((-x, y, -z)).</summary>
        public static List<CapitalSubsystem> GameHardpoints(string look)
        {
            var list = new List<CapitalSubsystem>();
            bool ship = look == ModNpcShips.Battleship;
            IEnumerable<(Vector3 pos, Vector3 rot)> table = look == ModNpcShips.Battleship ? BattleshipTurrets
                : look == ModNpcShips.Carrier ? CarrierTurrets
                : look == ModNpcShips.VosskBattleship ? System.Array.ConvertAll(VosskTurrets, v => (v, Vector3.zero)) : null;
            if (table == null) return list;
            var hostRot = ship ? Quaternion.Euler(0f, 180f, 0f) : World.OrbitLayout.RotationToUnity(Vector3.zero);
            foreach (var (pos, rot) in table)
            {
                var up = Quaternion.Inverse(hostRot) * (World.OrbitLayout.RotationToUnity(rot) * Vector3.up);
                list.Add(new CapitalSubsystem
                {
                    kind = "turret", position = ship ? pos : new Vector3(-pos.x, pos.y, -pos.z),
                    up = new Vector3(-up.x, up.y, up.z),
                });
            }
            return list;
        }

        /// <summary>The docking pad set of a game look (the carrier's deck, the Vossk battleship's wing pads), -1 = none.</summary>
        static int GamePadSet(string look) => look == ModNpcShips.Carrier ? World.CapitalShips.DeckPoints
            : look == ModNpcShips.VosskBattleship ? World.CapitalShips.VosskDeckPoints : -1;

        // ---- a type's ships -----------------------------------------------------------------------------------------

        /// <summary>A type's ships at 'at' (game units) for 'race' (the type's own wins): host, escorts; returns the host's index.</summary>
        public static int AddNpcType(Database db, List<SpawnSpec> list, NpcShipType t, int race, Vector3 at)
        {
            if (t.race >= 0) race = t.race;
            int first = list.Count;
            string look = t.BaseKey;
            if (t.kind == ModNpcShips.Capital && t.ship < 0 && CapitalKindOf(look) is int ck && ck != 0)
            {
                // A game hull with its own turrets (unless hardpoints replace them), then this type's rules on it.
                AddCapitalHull(list, ck, at, t.hardpoints == null);
                var host = list[first];
                host.race = race;
                host.hitpoints = NpcTables.Hull(1, 14) * 5;   // killable, like the enhanced battleship
                host.noLoot = false;
                if (ck == SpawnSpec.CapitalVossk) host.nameText = -1;
                // Its turrets as the enhanced capital ships' (World.CapitalShips.Enhance), of its race.
                int rank = Mathf.Min(Session.Rank, 20);
                for (int i = first + 1; i < list.Count; i++)
                {
                    list[i].race = race;
                    list[i].hitpoints = (int)((500 + 50 * rank) * Session.DifficultyFactor);
                    list[i].gunFactor = 1f + 0.1f * rank;
                    list[i].capitalEnhanced = true;
                    list[i].turretHunter = (i - first) % 2 == 0;
                }
            }
            else list.Add(NpcHost(db, t, race, at));
            ApplyNpcType(db, list, first, t, true);
            return first;
        }

        /// <summary>A type's host alone: a mod hull flown like ModCapitalShips' (parked capital ship or cruising freighter), a
        /// game freighter, or a boss (a fighter of its ship, the pirate boss's hull and gun by rank).</summary>
        static SpawnSpec NpcHost(Database db, NpcShipType t, int race, Vector3 at)
        {
            int rank = Mathf.Min(Session.Rank, 20);
            if (t.kind == ModNpcShips.Boss)
            {
                int ship = t.ship >= 0 ? t.ship : ModTraffic.PickBoss(BossShips[Random.Range(0, BossShips.Length)]);
                return new SpawnSpec
                {
                    group = NpcGroup.Special, race = race, ship = ship, position = at,
                    hitpoints = Mathf.Max(1, (int)((15 * rank + 1500 + 4 * 45) * Session.DifficultyFactor)), speed = 3.5f,
                    gunItem = rank < 5 ? 23 : rank < 10 ? 15 : rank < 15 ? 26 : 21, gunFactor = 2f, alwaysEnemy = t.hostile != false,
                    nameText = t.name == null ? 1606 : -1,
                };
            }
            bool parked = t.kind == ModNpcShips.Capital;
            if (t.ship >= 0 && CustomShips.IsCustom(t.ship))
            {
                var s = ModCapitalShips.Spawn(t.ship, race, at);
                s.stationary = parked;
                s.group = parked ? NpcGroup.Special : NpcGroup.Freighter;
                s.hitpoints = Mathf.Max(1, Mathf.RoundToInt((parked ? NpcTables.Hull(1, 14) * 5 : NpcTables.Hull(1, t.ship)) * (CustomShips.Get(t.ship)?.hullFactor ?? 1f)));
                if (parked) { s.capital = SpawnSpec.CapitalMod; s.name ??= GameNames.Ship(t.ship); }
                return s;
            }
            // A game hull: the battleship (14) or a race's freighter (13 the Vossk one, 15 the others' by race).
            int hull = t.ship == 13 || t.ship == 14 || t.ship == 15 ? t.ship : race == 1 ? 13 : 15;
            if (t.ship >= 0 && hull != t.ship)
                Debug.LogWarning($"Mods: NPC ship {t.key}: ship {t.ship} isn't a mod ship or a game freighter / battleship; a freighter's hull instead");
            return new SpawnSpec
            {
                group = parked ? NpcGroup.Special : NpcGroup.Freighter, race = race, ship = hull, freighter = true, stationary = parked,
                position = at, hitpoints = parked ? NpcTables.Hull(1, 14) * 5 : -1, capital = parked ? SpawnSpec.CapitalMod : 0,
            };
        }

        /// <summary>A type's rules on its host list[first] (a game one's ships too, once a mod changed it): name, side, hull,
        /// speed, explosion, turrets, escorts ('ownEscorts': none of the game's were added), loot, dock and launches.</summary>
        public static void ApplyNpcType(Database db, List<SpawnSpec> list, int first, NpcShipType t, bool ownEscorts)
        {
            if (t == null || first < 0 || first >= list.Count) return;
            var host = list[first];
            host.npcType = t;
            if (t.name != null) { host.name = t.name; host.nameText = -1; }
            if (t.hostile == true) host.alwaysEnemy = true;
            else if (t.hostile == false && t.kind == ModNpcShips.Boss) host.alwaysEnemy = false;
            if (t.hull >= 0) host.hitpoints = Mathf.Max(1, Mathf.RoundToInt(t.hull * Session.DifficultyFactor));
            else if (!Mathf.Approximately(t.hullFactor, 1f))
            {
                int baseHull = host.hitpoints > 0 && host.hitpoints < 9999999 ? host.hitpoints
                             : host.fixedObject != null ? NpcTables.Hull(1, 14) * 5 : NpcTables.Hull(host.freighter ? 1 : 0, host.ship);
                host.hitpoints = Mathf.Max(1, Mathf.RoundToInt(baseHull * t.hullFactor));
            }
            if (host.hitpoints >= 9999999 && (t.hull >= 0 || !Mathf.Approximately(t.hullFactor, 1f))) host.hitpoints = NpcTables.Hull(1, 14) * 5;
            if (!Mathf.Approximately(t.gunFactor, 1f))
            {
                host.gunFactor *= t.gunFactor;
                for (int i = first + 1; i < list.Count; i++)
                    if (list[i].capitalHost == host && list[i].group == NpcGroup.Turret) list[i].gunFactor *= t.gunFactor;
            }
            if (t.speed > 0f) host.speed = t.speed;
            if (t.explosionScale > 0f) host.explosionScale = t.explosionScale;
            if (t.hardpoints != null)
                for (int i = list.Count - 1; i > first; i--)   // its hardpoints replace the hull's turrets (CapitalSubsystems)
                    if (list[i].capitalHost == host && list[i].group == NpcGroup.Turret) list.RemoveAt(i);
            if (t.escorts != null) AddNpcEscorts(db, list, host, t, ownEscorts);
            RollNpcLoot(db, host, t);
            if (t.kind == ModNpcShips.Capital)
            {
                if (t.docking != null)
                {
                    int set = t.docking.pads.Count > 0 ? SpacePoints.NpcPadBase + t.index : t.ship < 0 ? GamePadSet(t.BaseKey) : -1;
                    if (set >= 0) { host.dockingType = ObjectDocking.Resupply; host.spacePoints = set; }
                    else Debug.LogWarning($"Mods: NPC ship {t.key}: \"docking\" without \"pads\" on a hull that has none (no docking)");
                }
                if (t.NeedsCapitalShip) { host.capitalEnhanced = true; if (host.capital == 0) host.capital = SpawnSpec.CapitalMod; }
            }
        }

        /// <summary>The escorts: "count" (else the enhanced capital ships' 3 + rank / 7, +1 Hard / Extreme; a freighter 2, a boss
        /// its game count), of "ships" (else the dearer half of the race's fighters), hull and gun x their strength; a capital
        /// ship's loop round it, a freighter's follow it (NpcOrder Follow, Traffic.Create), a boss's beside it. The game's own
        /// escorts of the host are replaced.</summary>
        static void AddNpcEscorts(Database db, List<SpawnSpec> list, SpawnSpec host, NpcShipType t, bool ownEscorts)
        {
            var e = t.escorts;
            int rank = Mathf.Min(Session.Rank, 20);
            int gameCount = 0;
            if (!ownEscorts)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i] != host && list[i].group != NpcGroup.Turret && (list[i].capitalHost == host || list[i].escortLeader == host
                                                                                || (t.kind == ModNpcShips.Boss && list[i].pirateEvent == SpawnSpec.EventGuard)))
                    { list.RemoveAt(i); gameCount++; }
            int n = e.min >= 0 ? Random.Range(e.min, e.max + 1)
                  : !ownEscorts && gameCount > 0 ? gameCount
                  : t.kind == ModNpcShips.Capital ? 3 + rank / 7 + (Session.Difficulty >= Session.DifficultyHard ? 1 : 0)
                  : t.kind == ModNpcShips.Freighter ? 2 : 2 + rank / 5;
            int race = e.race >= 0 ? e.race : host.race;
            for (int i = 0; i < n; i++)
            {
                int ship = e.ships.Count > 0 ? e.ships[Random.Range(0, e.ships.Count)] : NpcTables.StrongFighter(db, race);
                var s = new SpawnSpec
                {
                    group = NpcGroup.Escort, race = race, ship = ship, alwaysEnemy = host.alwaysEnemy,
                    hitpoints = Mathf.Max(1, (int)(NpcTables.Hull(0, ship) * e.hull)), gunItem = NpcTables.GunItem(race), gunFactor = e.gun,
                };
                if (t.kind == ModNpcShips.Capital)
                {
                    var loop = NpcLoop(host, i + 1);
                    s.route = loop;
                    s.position = loop.points[0] + new Vector3(Random.Range(-3000, 3000), Random.Range(-3000, 3000), Random.Range(-3000, 3000));
                    s.capitalPart = true; s.capitalHost = host; s.capitalEnhanced = host.capitalEnhanced; s.routeAround = host;
                }
                else
                {
                    float side = (i % 2 == 0 ? 1f : -1f) * (2500f + 1500f * (i / 2));
                    s.position = host.position + new Vector3(side, 800f * (i % 3), -3000f - 1000f * i);
                    if (t.kind == ModNpcShips.Freighter) { s.escortLeader = host; s.escortOffset = new Vector3(side, 800f * (i % 3), -3000f - 1000f * (i / 2)); }
                    else s.pirateEvent = host.pirateEvent != 0 ? SpawnSpec.EventGuard : 0;
                }
                list.Add(s);
            }
        }

        /// <summary>A capital ship's escort loop (CapitalShips.LoopAround), wider round a long mod hull.</summary>
        static Route NpcLoop(SpawnSpec host, int start)
        {
            var loop = World.CapitalShips.LoopAround(host, host.position, start);
            if (!host.modCapital) return loop;
            float length = ModTraffic.HullLength(host.ship) / 0.05f;   // metres -> game units
            float r = Mathf.Max(0.6f, length / 70000f);
            for (int k = 0; k < loop.points.Count; k++) loop.points[k] = host.position + (loop.points[k] - host.position) * r;
            return loop;
        }

        /// <summary>The type's "loot" into the host's crate (lootItem + extraLoot): none, its items by chance, its cargo rolls,
        /// and with "keep" the game's own roll (a freighter's / a fighter's; a capital ship's own crate: World.CapitalShip).</summary>
        static void RollNpcLoot(Database db, SpawnSpec host, NpcShipType t)
        {
            var l = t.loot;
            if (l == null) return;
            if (l.none) { host.noLoot = true; host.lootItem = -1; host.extraLoot = null; return; }
            var stacks = new List<ItemStack>();
            if (l.keep && host.lootItem >= 0) stacks.Add(new ItemStack(host.lootItem, Mathf.Max(1, host.lootAmount)));
            if (l.keep && host.extraLoot != null) stacks.AddRange(host.extraLoot);
            if (l.keep && host.lootItem < 0 && !host.capitalEnhanced) stacks.AddRange(NpcTables.RollLoot(db, host.freighter));
            foreach (var e in l.items)
                if (Random.Range(0f, 100f) < e.chance && db?.Item(e.item) != null) World.CapitalShips.Add(stacks, e.item, Random.Range(e.min, e.max + 1));
            for (int rolls = Random.Range(l.rollsMin, l.rollsMax + 1), tries = 0; rolls > 0 && tries < 20; tries++)
            {
                var hold = NpcTables.RollLoot(db, true);
                if (hold.Count == 0) continue;
                rolls--;
                foreach (var st in hold) World.CapitalShips.Add(stacks, st.item, st.amount);
            }
            host.lootItem = -1; host.extraLoot = null; host.lootAmount = 0;
            if (stacks.Count == 0) { host.noLoot = !l.keep || !host.capitalEnhanced; return; }
            host.noLoot = false;
            host.lootItem = stacks[0].item;
            host.lootAmount = stacks[0].amount;
            if (stacks.Count > 1) host.extraLoot = stacks.GetRange(1, stacks.Count - 1);
        }

        // ---- the mods' types in an orbit ------------------------------------------------------------------------------

        /// <summary>The mods' new NPC ship types whose "spawn" rules hold here, each at its "chance", "count" of them: a
        /// capital ship parked in front of the station (or at its "position" / "distance"), clear of it; a freighter in the
        /// freighters' box; a boss out where the pirate boss comes. 'gameCapital': the game's capital ship special was rolled
        /// here (a "replaceGame" type takes its place: the caller drops it, true returned).</summary>
        public static bool AddModNpcShips(Database db, List<SpawnSpec> list, int station, SystemData system, int sysRace, bool gameCapital)
        {
            bool replaced = false;
            foreach (var t in ModNpcShips.ModTypes())
            {
                var s = t.spawn;
                if (s == null || s.chance <= 0f || !NpcSpawnHolds(t, station, system, sysRace)) continue;
                if (s.replaceGame && !gameCapital) continue;
                if (Random.Range(0f, 100f) >= s.chance) continue;
                if (s.replaceGame) replaced = true;
                int race = t.race >= 0 ? t.race : sysRace;
                for (int n = Random.Range(s.min, s.max + 1), i = 0; i < n; i++)
                    AddNpcType(db, list, t, race, NpcSpawnPoint(t, station, race));
            }
            return replaced;
        }

        /// <summary>A type's "spawn" places, ranks, story steps and condition hold in this orbit.</summary>
        public static bool NpcSpawnHolds(NpcShipType t, int station, SystemData system, int sysRace)
        {
            var s = t.spawn;
            if (s == null || system == null) return false;
            if (s.races != null && System.Array.IndexOf(s.races, sysRace) < 0) return false;
            if (s.systems != null && System.Array.IndexOf(s.systems, system.index) < 0) return false;
            if (s.stations != null && System.Array.IndexOf(s.stations, station) < 0) return false;
            if (s.security != null && System.Array.IndexOf(s.security, system.securityLevel) < 0) return false;
            int rank = Session.Rank, step = Session.CampaignMission;
            if (rank < s.minRank || rank > s.maxRank) return false;
            if (s.minStep >= 0 && step < s.minStep || s.maxStep >= 0 && step > s.maxStep) return false;
            if (s.freePlayOnly && !Session.FreePlay || s.storyOnly && Session.FreePlay) return false;
            return ModUnlocks.Holds(t.mod, s.condition, ModNpcShips.File + " " + t.key);
        }

        /// <summary>Where a type's ship comes in (game units): its "position", a point "distance" km out in front of the
        /// station, else its kind's place (a capital ship's box 4-8 km ahead, the freighters' box, the pirate boss's point).</summary>
        static Vector3 NpcSpawnPoint(NpcShipType t, int station, int race)
        {
            var s = t.spawn;
            if (s?.position is Vector3 p) return p;
            System.Func<Vector3> roll;
            if (s != null && s.maxKm > 0f)
                roll = () =>
                {
                    var dir = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.3f, 0.3f), Random.Range(0.2f, 1f)).normalized;
                    return dir * Random.Range(s.minKm, s.maxKm) * 20000f;   // km -> game units
                };
            else if (t.kind == ModNpcShips.Freighter)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                return new Vector3(side * (Random.Range(0, 60000) - 80000), Random.Range(0, 40000) - 20000, Random.Range(0, 160000) - 80000);
            }
            else if (t.kind == ModNpcShips.Boss) return RaiderSpawn() + new Vector3(0f, 0f, 30000f);
            else roll = () => new Vector3(Random.Range(0, 80000) - 40000, Random.Range(0, 10000) - 5000, Random.Range(0, 80000) + 80000);
            var volumes = t.ship >= 0 ? CollisionVolume.ForFreighter(t.ship, race)
                        : CapitalKindOf(t.BaseKey) is int k && k != 0 ? CapitalVolumes(k) : CollisionVolume.ForFreighter(15, race);
            return ClearOfStation(station, volumes, roll);
        }
    }
}
