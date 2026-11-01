// CapitalShips.cs
// Remake (players' suggestion; option "Capital ship enhancements", Settings.CapitalShips, off by default): the free-flight
// capital ships (TrafficPlan.AddCapitalShip: the Terran battleship, the carrier past step 103, the Vossk battleship past 140;
// npc_combat_specials.md 2) fight back. Plain C# rules; CapitalShip (on the host) runs them in the orbit. Balanced after
// players' feedback (2026-10-11: a maxed ship killed the carrier barely losing its shield, the turrets outlasted the hulls,
// the Vossk battleship lost every fleet battle against the carrier, the resupply was unlimited).
//   escorts     3 + rank / 7 (+1 on Hard / Extreme, +2 for the carrier) of the dearer half of the race's fighters
//               (NpcTables.StrongFighter), hull x1.5, gun x1.3, looping round the host (their loop follows it in a battle)
//   turrets     (500 + 50 x rank) x difficulty hull, gun x(1 + 0.1 x rank); the Vossk battleship's 5 (the carrier has 8,
//               the Terran battleship 7) x1.6 both; every other one hunts the enemy's turrets (SpawnSpec.turretHunter)
//   hulls       the battleship's (Level::createShip kind 1 for ship 14: 25 x the fighter formula) x5 for the Terran
//               battleship, x8 for the carrier and the Vossk battleship (the original's are indestructible decor at
//               9 999 999); EMP x3 a freighter's, the collision boxes (2005 / 2006) as hit boxes, 8 s of explosions along
//               the hull, then an x14 blast; their turrets go with them; a crate of two pieces of tech level 5+ equipment,
//               rare goods and energy cells, at most once an hour of play (Session.CapitalLootReadyAt); destroying one by
//               the player is a delict of 20 with the race
//   provoked    the player's first hit on a friendly or neutral one: "Hold your fire!" (426-428); 3 hits or 0.5 % of its hull:
//               the race turns hostile (Traffic.AlarmAllFriends, the station remembers it); a hostile one at once
//   launches    the carrier launches 5 Inflicts (ship 5) from its deck pads (docking set 5), the Vossk battleship 5 H'Socs
//               (ship 9) from its wing pads (set 8), every 20 s, 15 in all, while it was attacked (by NPCs, or by the player
//               once provoked) in the last 20 s; they share its hostility
//   resupply    the carrier and the Vossk battleship are docking targets (ObjectDocking.Resupply, their pads) for a pilot
//               the race trusts (standing 71+) flying one of its ships, or anyone with its signature; docked, a shop window
//               like the hangar's (UI.CarrierShopWindow) sells the repair of hull and armor (unlimited), rounds for each
//               mounted secondary of its race (up to 50; 5-10 in store per kind, none for a blueprint product), its race's
//               gear (Store.Gear: the Vossk battleship 5 of the 17 Vossk-made items, attr 60; the carrier 6 of the Terran
//               items of tech level 4+, RaceItem: not Vossk-made, home in a Terran system; 1-2 of each, 10-20 rounds of a
//               secondary; into the hold) and energy cells (10-15 in
//               store) at the station's prices x1.25; the store is the ship's for the visit (Store)
//   fleet battle 3 % of the free-flight orbits in Terran and Vossk systems past step 103 (TrafficPlan.AddFleetBattle): a
//               Terran carrier or battleship and the Vossk battleship side by side 80 000 units apart, each with its turrets
//               and escorts and a wing of 4 + rank / 5 fighters looping round the other; the two sail a shared, slowly
//               curving course (BattleCourse) in lanes 21 000 units either side of it, weaving in and out and up and down,
//               banking into their turns (at most 15 deg) and staying near where the battle began; the enhanced turrets
//               reach and aim at the nearest point of an enemy hull (Target.NearestPoint), their shots flying 3600 ms; the
//               system race calls the battle 6 s in, the side that wins calls it and coasts to a stop; battle music while
//               both stand; the battle's fighters (escorts, wings, launched) take the nearest enemy within 60 000 units of
//               the course (BattleLeashUnits), not the first of the list (the system's own patrols came first, so the
//               visiting side's fighters flew off to them and never hit the enemy capital ship), and with none there fly
//               their loops round the capital ships; the player picks a side through the standing (or by shooting one: provoked as above), and
//               helping destroy one (its kill or 5 % of its hull) while the winners aren't hostile pays 30 000 + 3000 x rank
//   missiles    salvos of 6 homing missiles (CapitalShip): in a fleet battle at the enemy capital ship, and 1 in 3 at a
//               hostile fighter within 2 km of the hull; else at hostile ships within 2 km of the hull; on a big ship each
//               missile homes on its own spot (SpreadAim: one of its turrets 2 times in 5, else anywhere on its hull), so a
//               salvo spreads; the player's boost shakes off a salvo aimed at them

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class CapitalShips
    {
        public const int Inflict = 5, HSoc = 9;
        public const int CarrierInflicts = 15, InflictsPerWave = 5;
        public const float WaveMs = 20000f, UnderAttackMs = 20000f;
        public const float DeathMs = 8000f, DeathScale = 14f;
        public const float LootCooldownSeconds = 3600f;
        /// <summary>docks_hd set 5: the carrier's deck pads (step 102's drop-off points; approach points 2000 units above).</summary>
        public const int DeckPoints = 5;
        /// <summary>docks_hd set 8: the Vossk battleship's four wing pads (approach points ~5000 units above).</summary>
        public const int VosskDeckPoints = 8;
        public const float TurretLifetimeMs = 3600f;
        const float VosskTurretFactor = 1.6f;
        const int CarrierExtraEscorts = 2;
        // Missiles (CapitalShip): each race its own, the Terrans' Intelli Jet (37) and the Vossk-made S'koonn (38, attr 60);
        // salvos of 6, 250 ms apart, every 12-17 s (the first 3-15 s in, so two ships fire out of step); the item's own speed (18 / 10 u/ms; MissileSpeed without one) and 10 s of
        // flight. In a fleet battle 1 in 3 goes for a hostile fighter within reach of the hull (BattleFighterShare).
        public const int TerranMissile = 37, VosskMissile = 38, MissilesPerSalvo = 6;
        public const float BattleFighterShare = 1f / 3f;
        public const float MissileGapMs = 250f, SalvoMs = 12000f, SalvoJitterMs = 5000f, MissileSpeed = 8f, MissileLifetimeMs = 10000f;
        public const float MissileRangeUnits = 40000f, MissileCapitalFactor = 10f, MissileFactor = 5f;
        public const int FleetBattleChance = 3;
        public const float BattleSeparationUnits = 80000f;   // at the spawn
        // The battle's manoeuvres (CapitalShip.UpdateBattle, BattleCourse): game units, u/ms, degrees.
        public const float BattleSpeed = 0.35f, BattleLaneUnits = 21000f, BattleWeaveUnits = 6000f, BattleClimbUnits = 3000f,
                           BattleLeadUnits = 20000f, BattleHomeUnits = 60000f;
        /// <summary>A battle's fighters only take enemies this far from the battle's course (between the two capital ships).</summary>
        public const float BattleLeashUnits = 60000f;
        public const float BattleTurnDeg = 2f, BattleCourseTurnDeg = 1f, BattleBankDeg = 15f, BattlePitchDeg = 8f;
        public const float BattleRadioMs = 6000f;
        public static int BattleBounty => 30000 + 3000 * Rank;
        /// <summary>The carrier's name (lock plate, docking, its shop window).</summary>
        public static string CarrierName => Localization.Extra("capitalCarrier", "Carrier");
        const float ResupplyMarkup = 1.25f;
        const int RepairPerPoint = 10;

        static int Rank => Mathf.Min(Session.Rank, 20);

        /// <summary>A killable host (the carrier or the Vossk battleship; the Terran battleship was always killable).</summary>
        public static bool Killable(SpawnSpec s) => s != null && s.capitalEnhanced && (s.capital == SpawnSpec.CapitalCarrier || s.capital == SpawnSpec.CapitalVossk
                                                                                      || s.npcType?.Killable == true);   // remake mods: npcships.json

        /// <summary>A host that launches fighters and takes ships aboard: the carrier and the Vossk battleship. Remake mods: an
        /// NPC ship type's "launches" (npcships.json); one with a hull of its own launches only by them.</summary>
        public static bool HasDeck(SpawnSpec s) => s != null && (s.npcType?.launches != null
            || ((s.capital == SpawnSpec.CapitalCarrier || s.capital == SpawnSpec.CapitalVossk) && !(s.npcType?.ship >= 0)));

        /// <summary>The fighters it launches (the carrier's Inflicts, the Vossk battleship's H'Socs) and its pads' set.</summary>
        public static int LaunchShip(SpawnSpec s) => s.npcType?.launches is NpcShipType.Launches l && l.ships.Count > 0 ? l.ships[Random.Range(0, l.ships.Count)]
            : s.capital == SpawnSpec.CapitalVossk ? HSoc : Inflict;
        /// <summary>Its pads' set: a type's own pads (SpacePoints.NpcPadBase), else the game hull's; -1 = none (a mod hull).</summary>
        public static int PadSet(SpawnSpec s) => s.npcType?.docking is NpcShipType.Docking d && d.pads.Count > 0 ? SpacePoints.NpcPadBase + s.npcType.index
            : s.npcType?.ship >= 0 || (s.capital == SpawnSpec.CapitalMod && s.fixedObject == null) ? -1
            : s.capital == SpawnSpec.CapitalVossk ? VosskDeckPoints : DeckPoints;
        /// <summary>The launches: per wave, in all, the waves' gap (ms): a type's "launches", else 5 / 15 / 20 s.</summary>
        public static int PerWave(SpawnSpec s) => s.npcType?.launches?.perWave ?? InflictsPerWave;
        public static int LaunchTotal(SpawnSpec s) => s.npcType?.launches?.total ?? CarrierInflicts;
        public static float LaunchGapMs(SpawnSpec s) => s.npcType?.launches?.everyMs ?? WaveMs;

        /// <summary>TrafficPlan.AddCapitalShip with the option on: list[first] is the host, its turrets follow it.</summary>
        public static void Enhance(Database db, List<SpawnSpec> list, int first)
        {
            var host = list[first];
            host.capitalEnhanced = true;
            bool vossk = host.capital == SpawnSpec.CapitalVossk;
            float turretFactor = vossk ? VosskTurretFactor : 1f;
            int turrets = list.Count;
            for (int i = first + 1; i < turrets; i++)
            {
                list[i].hitpoints = (int)((500 + 50 * Rank) * Session.DifficultyFactor * turretFactor);
                list[i].gunFactor = (1f + 0.1f * Rank) * turretFactor;
                list[i].capitalEnhanced = true;
                list[i].turretHunter = (i - first) % 2 == 0;
            }
            int battleship = NpcTables.Hull(1, 14);
            if (HasDeck(host))
            {
                host.hitpoints = battleship * 8;
                host.empPoints = NpcTables.Emp(1) * 3;
                host.deathMs = DeathMs;
                host.explosionScale = DeathScale;
                host.noLoot = true;   // its crate is CapitalShip's (RollLoot)
                host.dockingType = ObjectDocking.Resupply;
                host.spacePoints = PadSet(host);
                if (host.capital == SpawnSpec.CapitalCarrier)
                    host.name = CarrierName;   // not text 1512 "Carrier": a medal (the hauler), "Spediteur" / "Курьер" in the other languages
            }
            else host.hitpoints = battleship * 5;
            var loop = LoopAround(host, host.position, 0);
            int escorts = 3 + Rank / 7 + (Session.Difficulty >= Session.DifficultyHard ? 1 : 0)
                          + (host.capital == SpawnSpec.CapitalCarrier ? CarrierExtraEscorts : 0);
            for (int i = 0; i < escorts; i++)
            {
                int ship = NpcTables.StrongFighter(db, host.race);
                var route = LoopAround(host, host.position, i + 1);   // the loop from its own corner (NpcShip.Setup clones it from index 0)
                list.Add(new SpawnSpec
                {
                    group = NpcGroup.Escort, race = host.race, ship = ship, route = route, capitalPart = true, capitalHost = host, capitalEnhanced = true,
                    position = loop.points[i % loop.points.Count] + new Vector3(Random.Range(-3000, 3000), Random.Range(-3000, 3000), Random.Range(-3000, 3000)),
                    hitpoints = (int)(NpcTables.Hull(0, ship) * 1.5f), gunItem = NpcTables.GunItem(host.race), gunFactor = 1.3f, routeAround = host,
                });
            }
        }

        /// <summary>Where one missile of a salvo homes on a big ship ('target' with hit boxes; Gun.SetAim), in its own frame:
        /// 2 times in 5 one of its live turrets (from 'ships', may be null), else a random point in one of its hit boxes
        /// (the bigger boxes likelier), so a salvo spreads over the hull instead of every missile striking its centre.
        /// False = a small target: its centre.</summary>
        public static bool SpreadAim(Target target, IReadOnlyList<NpcShip> ships, out Vector3 local)
        {
            local = Vector3.zero;
            var boxes = target != null ? target.boxes : null;
            if (boxes == null || boxes.Length == 0) return false;
            var host = ships != null ? target.GetComponent<NpcShip>() : null;
            if (host != null && Random.value < 0.4f)
            {
                turretPick.Clear();
                foreach (var s in ships)
                    if (s != null && s.Spec.capitalHost == host.Spec && s.IsTurret && s.Target.Alive && s.Current == NpcShip.State.Fly) turretPick.Add(s);
                if (turretPick.Count > 0)
                {
                    local = target.transform.InverseTransformPoint(turretPick[Random.Range(0, turretPick.Count)].transform.position);
                    return true;
                }
            }
            float total = 0f;
            foreach (var b in boxes) total += b.size.x * b.size.y * b.size.z;
            float pick = Random.Range(0f, total);
            var box = boxes[boxes.Length - 1];
            foreach (var b in boxes)
            {
                pick -= b.size.x * b.size.y * b.size.z;
                if (pick <= 0f) { box = b; break; }
            }
            local = new Vector3(Random.Range(box.min.x, box.max.x), Random.Range(box.min.y, box.max.y), Random.Range(box.min.z, box.max.z));
            return true;
        }

        static readonly List<NpcShip> turretPick = new List<NpcShip>();

        /// <summary>The escorts' loop round 'host' at 'centre' (game units), outside its hull (the carrier and the Vossk
        /// battleship reach ~35 000 units, the Terran battleship ~0.6 of that), starting at corner 'start'.</summary>
        public static Route LoopAround(SpawnSpec host, Vector3 centre, int start)
        {
            float r = host.capital == SpawnSpec.CapitalBattleship ? 0.6f : 1f;
            var corners = new[] { new Vector3(30000, 6000, 0), new Vector3(0, 9000, 50000), new Vector3(-30000, 6000, 0), new Vector3(0, 9000, -50000) };
            var loop = new Route(true);
            for (int k = 0; k < corners.Length; k++) loop.points.Add(centre + corners[(start + k) % corners.Length] * r);
            return loop;
        }

        // ---- loot ------------------------------------------------------------------------------------------------

        public static bool LootReady => Session.PlaySeconds >= Session.CapitalLootReadyAt;

        static readonly int[] RareGoods = { 113, 108, 107, 102, 101 };   // Implants, Vossk Organs, Organs, Rare Plants / Animals

        /// <summary>Any enhanced capital ship's crate, in this order: while the hour since the last capital ship's gear has
        /// passed (LootReady) two pieces of tech level 5+ weapons or equipment (a secondary 10-20 of it) and 8-15 t of rare
        /// goods, else 4-8 t of rare goods; 5-10 energy cells; then its hold, two to three of a freighter's cargo rolls
        /// (NpcTables.RollLoot). Remake (a player's report 2026-10-16: "does not drop cargo"): the hour used to empty the whole crate, and the
        /// Terran battleship had only a freighter's roll, nothing a third of the time. 'gear': the gear is in it.</summary>
        public static List<ItemStack> RollLoot(Database db) => RollLoot(db, out _);

        public static List<ItemStack> RollLoot(Database db, out bool gear)
        {
            var list = new List<ItemStack>();
            gear = false;
            if (db == null) return list;
            if (LootReady)
            {
                gear = true;
                var pool = db.Items.FindAll(it => it.maxPrice > 0 && it.blueprint.Count == 0 && it.TypeId <= 3 && it.techLevel >= 5 && it.occurrence > 0
                                                  && Hangar.IsSaleable(it.index) && !Modding.ModContent.IsMissingItem(it.index)
                                                  && Modding.ModUnlocks.ItemAvailable(it.index) && !Shop.NotInSession(db, it.index));   // a mod's gear (tech level 11+ too) once available
                for (int n = 0; n < 2 && pool.Count > 0; n++)
                {
                    var it = pool[Random.Range(0, pool.Count)];
                    pool.Remove(it);
                    list.Add(new ItemStack(it.index, it.TypeId == 1 ? Random.Range(10, 21) : 1));
                }
                list.Add(new ItemStack(RareGoods[Random.Range(0, RareGoods.Length)], Random.Range(8, 16)));
            }
            else list.Add(new ItemStack(RareGoods[Random.Range(0, RareGoods.Length)], Random.Range(4, 9)));
            list.Add(new ItemStack(GalaxyMap.EnergyCellItem, Random.Range(5, 11)));
            // The hold after them, each kind once (a kind already in the crate is left out).
            int fixedStacks = list.Count;
            for (int rolls = Random.Range(HoldRollsMin, HoldRollsMax + 1), tries = 0; rolls > 0 && tries < 20; tries++)
            {
                var hold = NpcTables.RollLoot(db, true);
                if (hold.Count == 0) continue;
                rolls--;
                foreach (var st in hold)
                {
                    int at = list.FindIndex(x => x.item == st.item);
                    if (at >= 0 && at < fixedStacks) continue;
                    Add(list, st.item, st.amount);
                }
            }
            return list;
        }

        public const int HoldRollsMin = 2, HoldRollsMax = 3;

        /// <summary>'amount' of 'item' into 'list', on its stack when it has one (a crate per kind: NpcShip.DropCrate).</summary>
        public static void Add(List<ItemStack> list, int item, int amount)
        {
            if (amount <= 0) return;
            var s = list.Find(x => x.item == item);
            if (s != null) s.amount += amount; else list.Add(new ItemStack(item, amount));
        }

        // ---- the resupply dock (the carrier, the Vossk battleship) ----------------------------------------------------

        /// <summary>Why the ship won't take the player, null = it will: not while it is hostile or in a battle; its race's
        /// signature, or the race's trust (standing 71+, Standing.IsFriend) and a ship of that race.</summary>
        public static string DockRefusal(NpcShip host)
        {
            bool vossk = host != null && host.Spec.capital == SpawnSpec.CapitalVossk;
            if (host == null || host.Target.hostileToPlayer || host.turnedEnemy)
                return vossk ? Localization.Extra("vosskDeckClosed", "The Vossk battleship's flight deck is closed to you.")
                             : Localization.Extra("carrierHostile", "The carrier's flight deck is closed to you.");
            if (InBattle(host))
                return vossk ? Localization.Extra("vosskInBattle", "The Vossk battleship is in battle: its flight deck is closed.")
                             : Localization.Extra("carrierInBattle", "The carrier is in battle: its flight deck is closed.");
            int race = host.Race;
            if (host.Spec.npcType?.docking is NpcShipType.Docking d)
            {
                // Remake mods: a type's own rules (npcships.json "docking": its standing, a ship of its race, a signature).
                if (d.signature && Standing.SignatureRace == race) return null;
                bool trusted = d.standingSet && race >= 0 && race <= 3 ? Standing.Toward(race) >= d.standing
                             : d.standing <= -100 || Standing.IsFriend(race) || (d.standing <= 0 && !Standing.IsEnemy(race));
                if (trusted && (!d.raceShip || Shop.RaceOfShip(Session.ShipIndex) == race)) return null;
                return string.Format(Localization.Extra("npcDockRefused", "{0} only takes pilots its race trusts{1}."),
                                     HostName(host), d.raceShip ? Localization.Extra("npcDockRaceShip", " in a ship of its race") : "");
            }
            if (Standing.SignatureRace == race) return null;
            if (Standing.IsFriend(race) && Shop.RaceOfShip(Session.ShipIndex) == race) return null;
            return vossk ? Localization.Extra("vosskRefused", "The Vossk battleship only takes pilots the Vossk trust in a Vossk ship, or pilots with a Vossk signature.")
                         : Localization.Extra("carrierRefused", "The carrier only takes trusted Terran pilots in a Terran ship, or pilots with a Terran signature.");
        }

        /// <summary>The resupply deck takes ships now: not while the ship is hostile, in a fleet battle or coasting after one.</summary>
        public static bool DeckOpen(NpcShip host) => host != null && !host.Target.hostileToPlayer && !InBattle(host);

        /// <summary>A fleet battle's capital ship while the enemy capital ship stands (or it still coasts): not a docking target.</summary>
        public static bool InBattle(NpcShip ship)
        {
            var c = ship != null && ship.Spec.fleetBattle ? ship.GetComponent<CapitalShip>() : null;
            return c != null && (c.InBattle || c.Underway);
        }

        /// <summary>The window's title: the carrier's name, the Vossk battleship's "Battleship" (1667).</summary>
        public static string HostName(NpcShip host) => host != null && !string.IsNullOrEmpty(host.Target.displayName) ? host.Target.displayName : CarrierName;

        public enum OfferKind { Repair, Ammo, Gear, Cargo }

        /// <summary>One row of the resupply window (UI.CarrierShopWindow).</summary>
        public class Offer
        {
            public OfferKind kind;
            public int item = -1;          // a mounted secondary or energy cells; -1 = the repair
            public int have;               // mounted (ammo) / in the hold (cargo)
            public int unitPrice;          // per unit; the repair: its whole price
            public int room;               // units it can take now (the store, the hold's room, up to AmmoCap mounted; the repair 1 or 0)
            public int stock = -1;         // left in the ship's store, -1 = unlimited (the repair)
            public bool notStocked;        // a blueprint product or (wrongRace) another race's weapon: never in store
            public bool wrongRace;
            public int missingHull, missingArmor;
        }

        /// <summary>A mounted secondary is topped up to at most this many (the hold's room limits energy cells).</summary>
        public const int AmmoCap = 50;
        const int CellsMin = 10, CellsMax = 15, AmmoMin = 5, AmmoMax = 10, VosskGear = 5, TerranGear = 6, TerranGearTech = 4;

        /// <summary>What one capital ship has to hand out this visit (CapitalShip.store), all of its race ('race'): per item,
        /// rolled when first asked for: energy cells 10-15, each secondary 5-10 rounds, a blueprint product or another
        /// race's weapon none (the blueprint stays worth having); its gear (Gear) rolled once.</summary>
        public class Store
        {
            readonly Dictionary<int, int> left = new Dictionary<int, int>();
            List<int> gear;
            public readonly int race;
            /// <summary>Remake mods: an NPC ship type's shop (npcships.json "docking"), null = the game's.</summary>
            public readonly NpcShipType.Docking dock;

            public Store(int race, NpcShipType.Docking dock = null) { this.race = race; this.dock = dock; }

            /// <summary>Its prices x the orbit station's; its repair and the price per point of hull / armour.</summary>
            public float Markup => dock?.markup ?? ResupplyMarkup;
            public bool Repairs => dock?.repair ?? true;
            public int RepairPrice => dock?.repairPrice ?? RepairPerPoint;

            public int Left(Database db, int item)
            {
                if (left.TryGetValue(item, out int n)) return n;
                var it = db != null ? db.Item(item) : null;
                n = it == null || IsBlueprintProduct(it) || (it.TypeId <= 3 && !RaceItem(db, it, race)) ? 0
                  : item == GalaxyMap.EnergyCellItem ? Random.Range(dock?.cellsMin ?? CellsMin, (dock?.cellsMax ?? CellsMax) + 1)
                  : Random.Range(dock?.ammoMin ?? AmmoMin, (dock?.ammoMax ?? AmmoMax) + 1);
                left[item] = n;
                return n;
            }

            public void Take(Database db, int item, int n) => left[item] = Mathf.Max(0, Left(db, item) - n);

            /// <summary>The weapons and equipment it sells into the hold: the Vossk battleship VosskGear of the Vossk-made
            /// items, the carrier TerranGear of the Terran ones (tech level 4+); 1-2 of each, 10-20 rounds of a secondary.</summary>
            public List<int> Gear(Database db)
            {
                if (gear != null) return gear;
                gear = new List<int>();
                if (db == null) return gear;
                // Remake mods: a type's own wares first ("sells": any item, its amount, its chance), then "gear" of its race's.
                if (dock != null)
                    foreach (var e in dock.sells)
                    {
                        if (db.Item(e.item) == null || gear.Contains(e.item) || Random.Range(0f, 100f) >= e.chance) continue;
                        gear.Add(e.item);
                        left[e.item] = Random.Range(e.min, e.max + 1);
                    }
                var pool = db.Items.FindAll(it => ArmoryItem(db, it, race) && !gear.Contains(it.index));
                int count = dock != null && dock.gear >= 0 ? dock.gear : race == 1 ? VosskGear : TerranGear;
                for (int k = 0; k < count && pool.Count > 0; k++)
                {
                    var it = pool[Random.Range(0, pool.Count)];
                    pool.Remove(it);
                    gear.Add(it.index);
                    left[it.index] = it.TypeId == 1 ? Random.Range(10, 21) : Random.Range(1, 3);
                }
                return gear;
            }
        }

        public static bool IsBlueprintProduct(ItemData it) => it != null && it.blueprint != null && it.blueprint.Count > 0;

        /// <summary>An item of 'race''s make: the Vossk's are the Vossk-made ones (attr 60, which the original sells only in
        /// Vossk systems, Generator::getItemBuyList); the Terrans' the others whose home system (attr 4, lowestPriceSystem:
        /// where it is cheapest and most stocked) is a Terran one. The original's data names no other maker; the user asked
        /// for Terran items only at the carrier (every non-Vossk item before: Nivelian and Midorian ones too).</summary>
        public static bool RaceItem(Database db, ItemData it, int race)
        {
            if (it == null) return false;
            bool vosskMade = it.Attr(60) == 1;
            if (race == 1) return vosskMade;   // the Vossk
            if (vosskMade) return false;
            var home = db?.Systems.Find(s => s.index == it.lowestPriceSystem);
            return home != null && home.raceId == race;
        }

        /// <summary>Gear a capital ship of 'race' may sell: a weapon or equipment of its make, priced and stocked somewhere
        /// (occurrence), saleable, no blueprint product, not the Khador Drive (85) or a signature (189-192); the Terrans'
        /// from tech level 4.</summary>
        static bool ArmoryItem(Database db, ItemData it, int race) =>
            it.TypeId <= 3 && it.maxPrice > 0 && it.occurrence > 0 && !IsBlueprintProduct(it) && RaceItem(db, it, race)
            && it.index != 85 && (it.index < 189 || it.index > 192) && (race == 1 || it.techLevel >= TerranGearTech)
            && Hangar.IsSaleable(it.index) && !Modding.ModContent.IsMissingItem(it.index) && Modding.ModUnlocks.ItemAvailable(it.index)
            && !Shop.NotInSession(db, it.index);

        /// <summary>The order of the gear rows: secondaries (after the mounted ones' rounds), primaries, turrets, equipment.</summary>
        static int GearOrder(ItemData it) => it == null ? 9 : it.TypeId == 1 ? 0 : it.TypeId == 0 ? 1 : it.TypeId == 2 ? 2 : 3;

        static int Price(Database db, int item) => db.Item(item) == null ? 0 : Shop.PriceList(db, Session.StationIndex, new[] { item })[0];

        public static string NotEnough => Localization.Extra("notEnoughCredits", "Not enough credits.");

        /// <summary>The resupply window's rows: the repair, each mounted secondary (once per item), energy cells; the station's
        /// prices x1.25; 'store' null = unlimited.</summary>
        public static List<Offer> ResupplyOffers(Database db, PlayerHealth health, Store store)
        {
            var list = new List<Offer>();
            var hp = health != null ? health.Hp : null;
            if (hp != null && (store?.Repairs ?? true))
            {
                int mh = Mathf.Max(0, hp.maxHull - hp.hull), ma = Mathf.Max(0, hp.maxArmor - hp.armor);
                list.Add(new Offer { kind = OfferKind.Repair, missingHull = mh, missingArmor = ma, unitPrice = (mh + ma) * (store?.RepairPrice ?? RepairPerPoint), room = mh + ma > 0 ? 1 : 0 });
            }
            foreach (var e in Session.Equipment)
            {
                var it = db.Item(e.item);
                if (it == null || it.TypeId != 1 || list.Exists(o => o.item == e.item)) continue;
                int have = 0;
                foreach (var x in Session.Equipment) if (x.item == e.item) have += Mathf.Max(0, x.amount);
                int stock = store != null ? store.Left(db, e.item) : -1;
                bool wrongRace = store != null && !RaceItem(db, it, store.race);
                list.Add(new Offer { kind = OfferKind.Ammo, item = e.item, have = have, unitPrice = UnitPrice(db, e.item, store), stock = stock,
                                     notStocked = store != null && (IsBlueprintProduct(it) || wrongRace), wrongRace = wrongRace,
                                     room = Limit(Mathf.Max(0, AmmoCap - have), stock) });
            }
            if (store != null)
            {
                var gear = new List<int>(store.Gear(db));
                gear.Sort((a, b) => GearOrder(db.Item(a)).CompareTo(GearOrder(db.Item(b))));
                foreach (int item in gear)
                {
                    if (list.Exists(o => o.item == item)) continue;   // a mounted secondary: its rounds row
                    int stock = store.Left(db, item);
                    list.Add(new Offer { kind = OfferKind.Gear, item = item, have = Shop.CargoOf(item), unitPrice = UnitPrice(db, item, store), stock = stock,
                                         room = Limit(Mathf.Max(0, Shop.FreeCargo(db)), stock) });
                }
            }
            int cells = GalaxyMap.EnergyCellItem;
            int cellStock = store != null ? store.Left(db, cells) : -1;
            list.Add(new Offer { kind = OfferKind.Cargo, item = cells, have = Shop.CargoOf(cells), unitPrice = UnitPrice(db, cells, store), stock = cellStock,
                                 room = Limit(Mathf.Max(0, Shop.FreeCargo(db)), cellStock) });
            return list;
        }

        static int Limit(int room, int stock) => stock < 0 ? room : Mathf.Min(room, stock);

        public static int UnitPrice(Database db, int item, Store store = null) => Mathf.Max(1, Mathf.RoundToInt(Price(db, item) * (store?.Markup ?? ResupplyMarkup)));

        /// <summary>Buys up to 'units' of the offer, as many as its room, the store and the credits allow; 'bought' = the
        /// units bought (the repair: 1); the HUD message (the reason when nothing was bought).</summary>
        public static string Buy(Database db, PlayerHealth health, Store store, Offer o, int units, out int bought)
        {
            bought = 0;
            if (o.kind == OfferKind.Repair)
            {
                var hp = health != null ? health.Hp : null;
                if (hp == null || o.room <= 0) return Localization.Extra("resupplyRepaired", "Hull and armor intact");
                if (Session.Credits < o.unitPrice) return NotEnough;
                Session.Credits -= o.unitPrice;
                hp.hull = hp.maxHull;
                hp.armor = hp.maxArmor;
                health.Target.hp = hp.hull;
                bought = 1;
                return Localization.Extra("resupplyRepairDone", "Repaired.");
            }
            if (o.room <= 0)
                return o.wrongRace ? Localization.Extra("resupplyWrongRace", "Not stocked here: this ship only carries its own race's weapons.")
                     : o.notStocked ? Localization.Extra("resupplyNotStocked", "Blueprint-built weapons aren't stocked here.")
                     : o.stock == 0 ? Localization.Extra("resupplySoldOut", "Sold out.")
                     : o.kind != OfferKind.Ammo ? Localization.Extra("resupplyHoldFull", "The hold is full.")
                     : string.Format(Localization.Extra("resupplyAmmoCap", "Topped up: {0} is the most you can take on here."), AmmoCap);
            int n = Mathf.Min(units, o.room, Session.Credits / Mathf.Max(1, o.unitPrice));
            if (n <= 0) return NotEnough;
            Session.Credits -= n * o.unitPrice;
            if (o.kind == OfferKind.Ammo)
            {
                var stack = Session.Equipment.Find(e => e.item == o.item);   // the gun rigs share the mounted stack
                if (stack != null) stack.amount += n;
            }
            else Shop.AddToCargo(o.item, n);   // energy cells, gear: into the hold
            store?.Take(db, o.item, n);
            o.have += n;
            o.room -= n;
            if (o.stock > 0) o.stock = Mathf.Max(0, o.stock - n);
            bought = n;
            return o.kind == OfferKind.Cargo ? $"+{n}t {GameNames.Item(o.item)}" : $"+{n} {GameNames.Item(o.item)}";
        }
    }
}
