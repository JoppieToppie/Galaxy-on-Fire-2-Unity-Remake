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
        public long completesUtc, maintenanceDueUtc, maintenanceDecayUtc;
        public int maintenanceIntervalSeconds;
        public List<PlayerStationMaterial> maintenanceMaterials = new List<PlayerStationMaterial>();

        internal bool MaintenanceDue(long now) =>
            operational && defense && maintenanceIntervalSeconds > 0 && maintenanceDueUtc > 0 && now >= maintenanceDueUtc;
        public bool operational;
        public List<PlayerStationMaterial> materials = new List<PlayerStationMaterial>();
        [NonSerialized] internal long nextShotUtc;
    }

    public static partial class NetPlayerStations
    {
        const int DefenseSlotsPerRing = 3;
        const int MaxOtherStructuresPerOrbit = 8;
        const int MaxOrbitalStructures = 512;
        const int DecayCheckpointSeconds = 60;
        const double OverdueIntegrityLossPerSecond = 0.01 / 86400;
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
            preset.Materials.TrueForAll(cost => OrbitalMaterialAvailable(cost.item)) &&
            preset.MaintenanceMaterials.TrueForAll(cost => OrbitalMaterialAvailable(cost.item));

        static bool OrbitalMaterialAvailable(int item) => NetGuard.Item(item) && NetGame.Db.Item(item) != null;

        internal static PlayerOrbitalStructure FindOrbitalStructure(int id) =>
            registry?.orbitalStructures?.Find(structure => structure != null && structure.id == id);

        internal static Vector3 OrbitalStructurePosition(PlayerOrbitalStructure structure)
        {
            if (structure.defense)
            {
                int ring = structure.slot / DefenseSlotsPerRing;
                float angle = (structure.slot % DefenseSlotsPerRing) * 120f + (ring % 2) * 60f;
                return Quaternion.Euler(0f, angle, 0f) * new Vector3(0, 0, 8000f + ring * 4000f);
            }
            return Quaternion.Euler(0f, 22.5f + structure.slot * 45f, 0f) * new Vector3(0, 3000f, 14000f);
        }

        internal static string StartOrbitalStructure(NetPlayer pilot, int sponsorId, int orbit, string presetId)
        {
            var settings = ModPlayerInfrastructure.Current;
            if (!settings.Enabled) return "New infrastructure projects are disabled.";
            var sponsor = Find(sponsorId);
            if (!CanIssueJumpgateOrder(pilot, sponsor, out _) || sponsor.underConstruction)
                return "Dock at an operational sponsor station you can manage.";
            if (!ValidOrbit(sponsor.system, orbit, out _)) return "Choose an existing orbit in the sponsor system.";
            ModPlayerInfrastructure.OrbitalDefinition preset = null;
            foreach (var entry in settings.OrbitalStructures)
                if (entry.Id == presetId) { preset = entry; break; }
            if (preset == null || !OrbitalPresetAvailable(preset)) return "That structure preset is unavailable.";
            var structures = registry.orbitalStructures;
            if (structures.Count >= MaxOrbitalStructures || registry.nextOrbitalStructureId >= int.MaxValue)
                return "This persistent world has reached its orbital project limit.";
            int capacity = preset.Defense ? settings.OrbitalDefenseLimit : MaxOtherStructuresPerOrbit;
            int occupied = 0;
            foreach (var entry in structures)
                if (entry != null && entry.orbit == orbit && entry.defense == preset.Defense) occupied++;
            if (occupied >= capacity) return "This orbit has no free " + (preset.Defense ? "defense" : "structure") + " slots.";

            int slot = 0;
            while (structures.Exists(entry => entry != null && entry.orbit == orbit && entry.defense == preset.Defense && entry.slot == slot)) slot++;

            var structure = new PlayerOrbitalStructure
            {
                id = registry.nextOrbitalStructureId++, sponsorStationId = sponsorId, orbit = orbit, slot = slot,
                name = preset.Name, assembly = preset.Assembly, stationModel = preset.StationModel, defense = preset.Defense,
                modelScale = preset.ModelScale, hitRadius = preset.HitRadius, maxIntegrity = preset.Integrity,
                integrity = preset.Integrity, range = preset.Range, damage = preset.Damage,
                fireIntervalSeconds = preset.FireIntervalSeconds, buildSeconds = preset.BuildSeconds,
                maintenanceIntervalSeconds = preset.MaintenanceIntervalSeconds
            };
            foreach (var cost in preset.Materials)
                structure.materials.Add(new PlayerStationMaterial { item = cost.item, required = cost.amount });
            foreach (var cost in preset.MaintenanceMaterials)
                structure.maintenanceMaterials.Add(new PlayerStationMaterial { item = cost.item, required = cost.amount });
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

        internal static string ContributeOrbitalStructure(NetPlayer pilot, int id, int item, int amount, bool fromStock = false, bool maintenance = false)
        {
            var structure = FindOrbitalStructure(id);
            if (!NetGame.IsServer || !sessionActive || structure == null)
                return "That project is unavailable for delivery.";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (maintenance ? !structure.MaintenanceDue(now) : structure.operational || structure.completesUtc > 0)
                return maintenance ? "That defense has no maintenance due." : "That project is unavailable for delivery.";
            if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != structure.orbit ||
                !NetProfiles.Enabled || !NetProfiles.Controls(pilot.OwnerClientId))
                return "Fly in the structure's orbit with a controlled profile to deliver materials.";
            var sponsor = Find(structure.sponsorStationId);
            if (!OperationalSponsor(sponsor)) return "Deliveries are paused while the sponsor is unavailable.";
            var materials = maintenance ? structure.maintenanceMaterials : structure.materials;
            var material = materials?.Find(entry => entry != null && entry.item == item);
            if (!OrbitalMaterialAvailable(item) || material == null || amount < 1 || amount > 500 || amount > material.required - material.delivered)
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

            bool paid = !maintenance && !fromStock && sponsor.ownershipType == PlayerStationOwnershipType.Faction;
            long payout = paid ? (long)Math.Max(1, NetGame.Db.Item(item)?.minPrice ?? 0) * amount : 0;
            if (paid && NetFactions.BankAmount(sponsor.ownerFactionId) < payout)
                return "The sponsor faction cannot cover this delivery.";
            bool wasOperational = structure.operational;
            long previousCompletion = structure.completesUtc, previousDue = structure.maintenanceDueUtc;
            long previousDecay = structure.maintenanceDecayUtc;
            float previousIntegrity = structure.integrity;
            material.delivered += amount;
            bool complete = materials.TrueForAll(entry => entry.delivered >= entry.required);
            if (complete && maintenance)
            {
                ApplyOverdueDecay(structure, now, 1);
                foreach (var entry in materials) entry.delivered = 0;
                structure.maintenanceDueUtc = now + structure.maintenanceIntervalSeconds;
                structure.maintenanceDecayUtc = 0;
            }
            else if (complete)
            {
                if (structure.buildSeconds == 0) ActivateOrbitalStructure(structure, now);
                else structure.completesUtc = now + structure.buildSeconds;
            }
            if (fromStock) stock.amount -= amount;
            bool saved = fromStock ? Save() : paid ? CommitConstructionContribution(account, sponsor.ownerFactionId, item, amount, payout)
                : CommitCargoTransaction(account, (item, -amount));
            if (!saved)
            {
                if (fromStock) stock.amount += amount;
                if (complete && maintenance)
                    foreach (var entry in materials) entry.delivered = entry.required;
                material.delivered -= amount;
                structure.operational = wasOperational;
                structure.completesUtc = previousCompletion;
                structure.maintenanceDueUtc = previousDue;
                structure.maintenanceDecayUtc = previousDecay;
                structure.integrity = previousIntegrity;
                return "Delivery could not be saved. Your cargo was kept.";
            }
            if (!fromStock && !(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(pilot.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", maintenance ? "Orbital defense maintenance" : "Orbital construction");
            if (maintenance) return complete ? $"{structure.name} maintenance complete." : "Delivered maintenance materials.";
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

        static void ActivateOrbitalStructure(PlayerOrbitalStructure structure, long now)
        {
            structure.operational = true;
            if (structure.maintenanceIntervalSeconds > 0)
                structure.maintenanceDueUtc = now + structure.maintenanceIntervalSeconds;
        }

        static bool ApplyOverdueDecay(PlayerOrbitalStructure structure, long now, int minimumElapsed)
        {
            if (!structure.MaintenanceDue(now) || structure.integrity <= 1f) return false;
            long elapsed = now - Math.Max(structure.maintenanceDueUtc, structure.maintenanceDecayUtc);
            if (elapsed < minimumElapsed) return false;

            double loss = structure.maxIntegrity * OverdueIntegrityLossPerSecond * elapsed;
            float remaining = (float)Math.Max(1d, structure.integrity - loss);
            if (remaining >= structure.integrity) return false;
            structure.integrity = remaining;
            structure.maintenanceDecayUtc = now;
            return true;
        }

        static void SaveOverdueDefenseDecay(long now)
        {
            var original = registry;
            List<(PlayerOrbitalStructure structure, float integrity, long checkpoint)> changes = null;
            foreach (var structure in original.orbitalStructures)
            {
                if (structure == null) continue;
                float integrity = structure.integrity;
                long checkpoint = structure.maintenanceDecayUtc;
                if (!ApplyOverdueDecay(structure, now, DecayCheckpointSeconds)) continue;
                changes ??= new List<(PlayerOrbitalStructure, float, long)>();
                changes.Add((structure, integrity, checkpoint));
            }
            if (changes == null || Save() || !ReferenceEquals(registry, original)) return;
            foreach (var change in changes)
            {
                change.structure.integrity = change.integrity;
                change.structure.maintenanceDecayUtc = change.checkpoint;
            }
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
            SaveOverdueDefenseDecay(now);
            if (!ReferenceEquals(registry, original)) return;
            foreach (var structure in original.orbitalStructures)
            {
                if (structure == null) continue;
                var sponsor = Find(structure.sponsorStationId);
                if (!OperationalSponsor(sponsor)) continue;
                if (!structure.operational && structure.completesUtc > 0 && now >= structure.completesUtc)
                {
                    ActivateOrbitalStructure(structure, now);
                    if (!Save())
                    {
                        structure.operational = false;
                        structure.maintenanceDueUtc = 0;
                    }
                    if (!ReferenceEquals(registry, original)) return;
                }
                if (!structure.operational || !structure.defense || structure.MaintenanceDue(now) || now < structure.nextShotUtc) continue;
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
            long oldDecay = structure.maintenanceDecayUtc;
            ApplyOverdueDecay(structure, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1);
            structure.integrity = Mathf.Max(0, structure.integrity - damage);
            if (structure.integrity == 0) list.RemoveAt(index);
            if (Save()) NoteOrbitalAggression(pilot, structure.orbit);
            else if (ReferenceEquals(registry, previous))
            {
                if (structure.integrity == 0) list.Insert(index, structure);
                structure.integrity = old;
                structure.maintenanceDecayUtc = oldDecay;
            }
        }
    }
}
