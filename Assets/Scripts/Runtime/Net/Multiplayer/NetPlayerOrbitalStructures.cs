using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Serializable]
    public sealed class PlayerOrbitalStructure
    {
        public int id, orbit, sponsorStationId, slot, buildSeconds, fireIntervalSeconds;
        public string name, assembly, stationModel;
        public bool defense;
        public float modelScale, hitRadius, integrity, maxIntegrity, range, damage;
        public long completesUtc;
        public bool operational;
        public List<PlayerStationMaterial> materials = new List<PlayerStationMaterial>();
        [NonSerialized] internal long nextShotUtc;
    }

    public static partial class NetPlayerStations
    {
        public const int MaxDefensesPerOrbit = 3;
        const int MaxOtherStructuresPerOrbit = 8;
        const int MaxOrbitalStructures = 512;
        static long nextOrbitalStructureTick;
        static readonly Dictionary<(int orbit, ulong client), long> orbitalAggression = new Dictionary<(int, ulong), long>();

        public static IReadOnlyList<PlayerOrbitalStructure> OrbitalStructures => registry?.orbitalStructures;

        internal static string OrbitalStructureAssembly(string assembly, string stationModel, Database db)
        {
            if (!string.IsNullOrEmpty(stationModel))
            {
                if (!ModWorld.TryResolveStation(stationModel, out int index) || IsPlayerStation(index)) return null;
                var model = db.Stations.Find(station => station != null && station.index == index);
                return model == null ? null : OrbitBuilder.StationPresetAssembly(db, model);
            }
            return !string.IsNullOrEmpty(assembly) && db.AssemblyByName(assembly) != null ? assembly : null;
        }

        internal static bool OrbitalPresetAvailable(ModPlayerInfrastructure.OrbitalDefinition preset) =>
            OrbitalStructureAssembly(preset.Assembly, preset.StationModel, NetGame.Db) != null &&
            preset.Materials.TrueForAll(cost => NetGuard.Item(cost.item) && NetGame.Db.Item(cost.item) != null);

        internal static PlayerOrbitalStructure FindOrbitalStructure(int id) =>
            registry?.orbitalStructures?.Find(structure => structure != null && structure.id == id);

        internal static Vector3 OrbitalStructurePosition(PlayerOrbitalStructure structure)
        {
            if (structure.defense)
                return Quaternion.Euler(0f, structure.slot * 120f, 0f) * new Vector3(0, 0, 8000f);
            return Quaternion.Euler(0f, 22.5f + structure.slot * 45f, 0f) * new Vector3(0, 3000f, 14000f);
        }

        internal static string StartOrbitalStructure(NetPlayer pilot, int sponsorId, int orbit, string presetId)
        {
            if (!ModPlayerInfrastructure.Current.Enabled) return "New infrastructure projects are disabled.";
            var sponsor = Find(sponsorId);
            if (!CanIssueJumpgateOrder(pilot, sponsor, out _) || sponsor.underConstruction)
                return "Dock at an operational sponsor station you can manage.";
            if (!ValidOrbit(sponsor.system, orbit, out _)) return "Choose an existing orbit in the sponsor system.";
            ModPlayerInfrastructure.OrbitalDefinition preset = null;
            foreach (var entry in ModPlayerInfrastructure.Current.OrbitalStructures)
                if (entry.Id == presetId) { preset = entry; break; }
            if (preset == null || !OrbitalPresetAvailable(preset)) return "That structure preset is unavailable.";
            var structures = registry.orbitalStructures;
            if (structures.Count >= MaxOrbitalStructures || registry.nextOrbitalStructureId >= int.MaxValue)
                return "This persistent world has reached its orbital project limit.";
            int capacity = preset.Defense ? MaxDefensesPerOrbit : MaxOtherStructuresPerOrbit;
            int slot = 0;
            while (slot < capacity && structures.Exists(entry => entry.orbit == orbit && entry.defense == preset.Defense && entry.slot == slot)) slot++;
            if (slot >= capacity) return "This orbit has no free " + (preset.Defense ? "defense" : "structure") + " slots.";

            var structure = new PlayerOrbitalStructure
            {
                id = registry.nextOrbitalStructureId++, sponsorStationId = sponsorId, orbit = orbit, slot = slot,
                name = preset.Name, assembly = preset.Assembly, stationModel = preset.StationModel, defense = preset.Defense,
                modelScale = preset.ModelScale, hitRadius = preset.HitRadius, maxIntegrity = preset.Integrity,
                integrity = preset.Integrity, range = preset.Range, damage = preset.Damage,
                fireIntervalSeconds = preset.FireIntervalSeconds, buildSeconds = preset.BuildSeconds
            };
            foreach (var cost in preset.Materials)
                structure.materials.Add(new PlayerStationMaterial { item = cost.item, required = cost.amount });
            var previous = registry;
            structures.Add(structure);
            if (Save()) return $"Started {structure.name} #{structure.id}. Deliver materials in its orbit.";
            if (ReferenceEquals(registry, previous))
            {
                structures.Remove(structure);
                registry.nextOrbitalStructureId--;
            }
            return "The orbital structure order could not be saved.";
        }

        internal static string ContributeOrbitalStructure(NetPlayer pilot, int id, int item, int amount, bool fromStock = false)
        {
            var structure = FindOrbitalStructure(id);
            if (!NetGame.IsServer || !sessionActive || structure == null || structure.operational || structure.completesUtc > 0)
                return "That project is unavailable for delivery.";
            if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != structure.orbit ||
                !NetProfiles.Enabled || !NetProfiles.Controls(pilot.OwnerClientId))
                return "Fly in the construction orbit with a controlled profile to deliver materials.";
            var sponsor = Find(structure.sponsorStationId);
            if (!OperationalSponsor(sponsor)) return "Construction is paused while the sponsor is unavailable.";
            var material = structure.materials.Find(entry => entry.item == item);
            if (material == null || amount < 1 || amount > 500 || amount > material.required - material.delivered)
                return "Choose a required item and an amount from 1 to 500 within the outstanding cost.";
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Use a controlled profile.";
            PlayerStationStorage stock = null;
            if (fromStock)
            {
                if (!CanUseStationConstructionStock(sponsor, account))
                    return "Only the sponsor owner or owning faction can use its donated stock.";
                stock = sponsor.donatedConstructionStock?.Find(entry => entry != null && entry.item == item);
                if (stock == null || stock.amount < amount) return "The sponsor does not have enough donated stock.";
            }
            else if (NetProfiles.CargoAmount(account, item) < amount) return "You do not have enough cargo.";

            bool paid = !fromStock && sponsor.ownershipType == PlayerStationOwnershipType.Faction;
            long payout = paid ? (long)Math.Max(1, NetGame.Db.Item(item)?.minPrice ?? 0) * amount : 0;
            if (paid && NetFactions.BankAmount(sponsor.ownerFactionId) < payout)
                return "The sponsor faction cannot cover this delivery.";
            material.delivered += amount;
            bool complete = structure.materials.TrueForAll(entry => entry.delivered >= entry.required);
            if (complete)
            {
                structure.operational = structure.buildSeconds == 0;
                if (!structure.operational) structure.completesUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + structure.buildSeconds;
            }
            if (fromStock) stock.amount -= amount;
            bool saved = fromStock ? Save() : paid ? CommitConstructionContribution(account, sponsor.ownerFactionId, item, amount, payout)
                : CommitCargoTransaction(account, (item, -amount));
            if (!saved)
            {
                if (fromStock) stock.amount += amount;
                material.delivered -= amount;
                structure.operational = false;
                structure.completesUtc = 0;
                return "Delivery could not be saved. Your cargo was kept.";
            }
            if (!fromStock && !(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(pilot.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Orbital construction");
            if (paid) NotifyProfileCreditChange(account, payout, "Orbital construction contribution");
            return structure.operational ? $"{structure.name} is operational." : "Delivered orbital construction materials.";
        }

        internal static string RemoveOrbitalStructure(NetPlayer pilot, int id)
        {
            var structure = FindOrbitalStructure(id);
            if (!NetGame.IsServer || structure == null) return "No orbital structure has that ID.";
            if (!CanIssueJumpgateOrder(pilot, Find(structure.sponsorStationId), out _) &&
                !(pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == structure.orbit && NetCommands.IsAdmin(pilot)))
                return "Dock at the sponsor station to dismantle the structure.";
            var previous = registry;
            int index = previous.orbitalStructures.IndexOf(structure);
            previous.orbitalStructures.RemoveAt(index);
            if (Save()) return $"{structure.name} dismantled.";
            if (ReferenceEquals(previous, registry)) previous.orbitalStructures.Insert(index, structure);
            return "The structure could not be dismantled.";
        }

        static bool OperationalSponsor(PlayerStationRecord sponsor) =>
            sponsor != null && !sponsor.underConstruction && !sponsor.reconstructing && !sponsor.destroyed &&
            !sponsor.wreckClaimOpen && !sponsor.retired && sponsor.offeredForClaimUtc <= 0;

        internal static void NoteOrbitalAggression(NetPlayer pilot, int orbit)
        {
            if (registry?.orbitalStructures?.Exists(entry => entry != null && entry.orbit == orbit && entry.defense && entry.operational) == true)
                orbitalAggression[(orbit, pilot.OwnerClientId)] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120;
        }

        static bool HostileToDefense(NetPlayer pilot, PlayerStationRecord sponsor, int orbit, int race, long now)
        {
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            if (!string.IsNullOrEmpty(account))
            {
                string ownFaction = sponsor.ownershipType == PlayerStationOwnershipType.Faction
                    ? sponsor.ownerFactionId : NetFactions.Of(sponsor.ownerProfile)?.id;
                if (sponsor.ownerProfile == account || (!string.IsNullOrEmpty(ownFaction) && ownFaction == NetFactions.Of(account)?.id))
                    return false;
            }
            return orbitalAggression.TryGetValue((orbit, pilot.OwnerClientId), out long until) && now < until ||
                !string.IsNullOrEmpty(account) && (HasBan(sponsor, account, PlayerStationBanSource.OriginalBuilder) ||
                    HasBan(sponsor, account, PlayerStationBanSource.OwnerAuthority)) ||
                Standing.IsEnemyWith(race, pilot.Standing0, pilot.Standing1, pilot.Signature, pilot.ModRaceStandings);
        }

        static void TickOrbitalStructures(long now)
        {
            if (now < nextOrbitalStructureTick || registry?.orbitalStructures == null) return;
            nextOrbitalStructureTick = now + 1;
            var expired = new List<(int orbit, ulong client)>();
            foreach (var mark in orbitalAggression)
                if (now >= mark.Value) expired.Add(mark.Key);
            foreach (var key in expired) orbitalAggression.Remove(key);

            var original = registry;
            foreach (var structure in original.orbitalStructures)
            {
                if (structure == null) continue;
                var sponsor = Find(structure.sponsorStationId);
                if (!OperationalSponsor(sponsor)) continue;
                if (!structure.operational && structure.completesUtc > 0 && now >= structure.completesUtc)
                {
                    structure.operational = true;
                    if (!Save()) structure.operational = false;
                    if (!ReferenceEquals(registry, original)) return;
                }
                if (!structure.operational || !structure.defense || now < structure.nextShotUtc) continue;
                var runner = NetPlayer.OrbitRunner(structure.orbit);
                if (runner == null) continue;
                int race = NetGame.Db.Systems.Find(system => system != null && system.index == sponsor.system)?.raceId ?? -1;
                Vector3 centre = OrbitalStructurePosition(structure);
                NetPlayer target = null;
                float closest = structure.range * structure.range;
                foreach (var pilot in NetPlayer.All)
                {
                    if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != structure.orbit ||
                        pilot.Hull <= 0 || !pilot.HasCurrentWreckClaimPose) continue;
                    float distance = (pilot.Position - centre).sqrMagnitude;
                    if (distance > closest || !HostileToDefense(pilot, sponsor, structure.orbit, race, now)) continue;
                    closest = distance;
                    target = pilot;
                }
                structure.nextShotUtc = now + structure.fireIntervalSeconds;
                if (target != null) target.ApplyOrbitalDefenseDamage(structure.orbit, structure.damage, centre);
                else runner.FireOrbitalDefenseAtNpc(structure, race, centre);
            }
        }

        internal static void DamageOrbitalStructure(NetPlayer pilot, PlayerOrbitalStructure structure, float damage)
        {
            if (!NetGame.IsServer || !structure.operational || damage <= 0 || !NetGuard.Finite(damage)) return;
            var previous = registry;
            if (previous?.orbitalStructures == null) return;
            var list = previous.orbitalStructures;
            float old = structure.integrity;
            int index = list.IndexOf(structure);
            if (index < 0) return;
            structure.integrity = Mathf.Max(0, old - damage);
            if (structure.integrity == 0) list.RemoveAt(index);
            if (Save()) NoteOrbitalAggression(pilot, structure.orbit);
            else if (ReferenceEquals(registry, previous))
            {
                if (structure.integrity == 0) list.Insert(index, structure);
                structure.integrity = old;
            }
        }
    }
}
