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
        public bool defense, hasPlacement;
        public int placementX, placementY, placementZ;
        public bool hasRotation;
        public int pitch, yaw, roll;
        public bool ignorePlayers, ignoreNpcs;
        public float modelScale, hitRadius, integrity, maxIntegrity, range, damage;
        public long completesUtc, maintenanceDueUtc, maintenanceDecayUtc;
        public int maintenanceIntervalSeconds;
        public bool hasMaintenanceDecayRate;
        public float maintenanceDecayPercentPerDay, minimumMaintenanceIntegrityPercent;
        public List<PlayerStationMaterial> maintenanceMaterials = new List<PlayerStationMaterial>();

        internal bool MaintenanceDue(long now) =>
            operational && defense && maintenanceIntervalSeconds > 0 && maintenanceDueUtc > 0 && now >= maintenanceDueUtc;
        internal float MaintenanceDecayPercentPerDay => hasMaintenanceDecayRate
            ? maintenanceDecayPercentPerDay : ModPlayerInfrastructure.DefaultOrbitalDecayPercentPerDay;
        internal float MaintenanceIntegrityFloor => maxIntegrity *
            (Math.Max(minimumMaintenanceIntegrityPercent, ModPlayerInfrastructure.MinimumOrbitalDecayIntegrityPercent) / 100f);
        internal bool CanLoseIntegrityToMaintenance =>
            MaintenanceDecayPercentPerDay > 0f && integrity > MaintenanceIntegrityFloor;

        public bool operational;
        public List<PlayerStationMaterial> materials = new List<PlayerStationMaterial>();
        [NonSerialized] internal long nextShotUtc;
    }

    public static partial class NetPlayerStations
    {
        const int DefenseSlotsPerRing = 3;
        const int MaxOrbitalStructures = 512;
        const int DecayCheckpointSeconds = 60;
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

        internal static bool OrbitalPresetAvailable(ModPlayerInfrastructure.OrbitalDefinition preset, int sponsorRace)
        {
            if (OrbitalStructureAssembly(preset.Assembly, preset.StationModel, NetGame.Db) == null ||
                !preset.Materials.TrueForAll(cost => OrbitalMaterialAvailable(cost.item)) ||
                !preset.MaintenanceMaterials.TrueForAll(cost => OrbitalMaterialAvailable(cost.item))) return false;
            if (preset.SponsorRaces.Count == 0) return true;
            foreach (string reference in preset.SponsorRaces)
                if (ModRaces.TryResolve(reference, null, out int race) && race == sponsorRace) return true;
            return false;
        }

        static int OrbitalSponsorRace(PlayerStationRecord sponsor) =>
            NetGame.Db.Systems.Find(system => system != null && system.index == sponsor.system)?.raceId ?? -1;

        static bool OrbitalMaterialAvailable(int item) => NetGuard.Item(item) && NetGame.Db.Item(item) != null;

        internal static PlayerOrbitalStructure FindOrbitalStructure(int id) =>
            registry?.orbitalStructures?.Find(structure => structure != null && structure.id == id);

        internal static Vector3 OrbitalStructurePosition(PlayerOrbitalStructure structure)
        {
            if (structure.hasPlacement)
                return new Vector3(structure.placementX, structure.placementY, structure.placementZ);
            if (structure.defense)
            {
                int ring = structure.slot / DefenseSlotsPerRing;
                float angle = (structure.slot % DefenseSlotsPerRing) * 120f + (ring % 2) * 60f;
                return Quaternion.Euler(0f, angle, 0f) * new Vector3(0, 0, 8000f + ring * 4000f);
            }
            return Quaternion.Euler(0f, 22.5f + structure.slot * 45f, 0f) * new Vector3(0, 3000f, 14000f);
        }

        internal static float OrbitalStationClearance(OrbitLayout layout, float hitRadius) =>
            Math.Max(1000f, (layout != null && layout.stationObject
                ? layout.stationReach * OrbitLayout.MetersPerUnit * layout.StationScale : 0f) + hitRadius);

        internal static string OrbitalPlacementError(int orbit, OrbitLayout layout, Vector3 position, float hitRadius)
        {
            float clearance = OrbitalStationClearance(layout, hitRadius);
            if ((position - StationPosition(orbit)).sqrMagnitude < clearance * clearance || position.sqrMagnitude > 120000f * 120000f)
                return "Place the structure clear of the station and within 120 km of the orbit centre.";
            if (OrbitalStructures != null)
                foreach (var other in OrbitalStructures)
                {
                    if (other == null || other.orbit != orbit) continue;
                    float separation = Math.Max(200f, other.hitRadius + hitRadius);
                    if ((OrbitalStructurePosition(other) - position).sqrMagnitude < separation * separation)
                        return "This position overlaps " + other.name + " #" + other.id + ".";
                }
            return null;
        }

        internal static string StartOrbitalStructure(NetPlayer pilot, int sponsorId, int orbit, string presetId, Vector3Int? placement = null, int? plannedSlot = null, Vector3Int? rotation = null)
        {
            if (!ValidPlacementRotation(rotation)) return "Rotation angles must be between -360 and 360 degrees.";
            var settings = ModPlayerInfrastructure.Current;
            if (!settings.Enabled) return "New infrastructure projects are disabled.";
            var sponsor = Find(sponsorId);
            if (!CanIssueJumpgateOrder(pilot, sponsor, out _) || sponsor.underConstruction)
                return "Dock at an operational sponsor station you can manage.";
            if (!ValidOrbit(sponsor.system, orbit, out _)) return "Choose an existing orbit in the sponsor system.";
            ModPlayerInfrastructure.OrbitalDefinition preset = null;
            foreach (var entry in settings.OrbitalStructures)
                if (entry.Id == presetId) { preset = entry; break; }
            if (preset == null || !OrbitalPresetAvailable(preset, OrbitalSponsorRace(sponsor))) return "That structure preset is unavailable.";
            var structures = registry.orbitalStructures;
            PlayerStationPlacement savedPlan = null;
            if (plannedSlot.HasValue)
            {
                int n = plannedSlot.Value;
                if (!preset.Defense || orbit != sponsor.stationIndex || n < 0 || n >= settings.OrbitalDefenseLimit || structures.Exists(p => p != null && p.orbit == orbit && p.defense && p.slot == n)) return "That defense slot is unavailable.";
                var plan = sponsor.placements?.Find(p => p?.kind == "defense-" + (n + 1));
                if (plan == null || plan.preset != presetId) return "Save the defense placement first.";
                savedPlan = plan;
                placement = Vector3Int.RoundToInt(StationPosition(orbit)) + new Vector3Int(plan.x, plan.y, plan.z);
            }
            if (placement is Vector3Int point &&
                OrbitalPlacementError(orbit, OrbitLayout.Build(NetGame.Db, orbit), point, preset.HitRadius) is string placementError)
                return placementError;
            if (structures.Count >= MaxOrbitalStructures || registry.nextOrbitalStructureId >= int.MaxValue)
                return "This persistent world has reached its orbital project limit.";
            int capacity = preset.Defense ? settings.OrbitalDefenseLimit : settings.OtherOrbitalStructureLimit;
            int occupied = 0;
            foreach (var entry in structures)
                if (entry != null && entry.orbit == orbit && entry.defense == preset.Defense) occupied++;
            if (occupied >= capacity) return "This orbit has no free " + (preset.Defense ? "defense" : "structure") + " slots.";

            int slot = plannedSlot ?? 0;
            if (!plannedSlot.HasValue)
            {
                while (structures.Exists(entry => entry != null && entry.orbit == orbit && entry.defense == preset.Defense && entry.slot == slot) ||
                    (preset.Defense && StationAt(orbit)?.placements?.Exists(p => p?.kind == "defense-" + (slot + 1)) == true)) slot++;
                if (slot >= capacity) return "All remaining slots are reserved by placement plans. Build a saved defense plan instead.";
            }

            var structure = new PlayerOrbitalStructure
            {
                id = registry.nextOrbitalStructureId++, sponsorStationId = sponsorId, orbit = orbit, slot = slot,
                name = preset.Name, assembly = preset.Assembly, stationModel = preset.StationModel, defense = preset.Defense,
                ignorePlayers = !preset.TargetPlayers, ignoreNpcs = !preset.TargetNpcs,
                modelScale = preset.ModelScale, hitRadius = preset.HitRadius, maxIntegrity = preset.Integrity,
                integrity = preset.Integrity, range = preset.Range, damage = preset.Damage,
                fireIntervalSeconds = preset.FireIntervalSeconds, buildSeconds = preset.BuildSeconds,
                maintenanceIntervalSeconds = preset.MaintenanceIntervalSeconds,
                hasMaintenanceDecayRate = true,
                maintenanceDecayPercentPerDay = preset.MaintenanceDecayPercentPerDay,
                minimumMaintenanceIntegrityPercent = preset.MinimumMaintenanceIntegrityPercent
            };
            if (placement is Vector3Int placed)
            {
                structure.hasPlacement = true;
                structure.placementX = placed.x;
                structure.placementY = placed.y;
                structure.placementZ = placed.z;
            }
            if (rotation.HasValue)
            {
                structure.hasRotation = true; structure.pitch = rotation.Value.x; structure.yaw = rotation.Value.y; structure.roll = rotation.Value.z;
            }
            if (savedPlan?.hasRotation == true)
            {
                structure.hasRotation = true;
                structure.pitch = savedPlan.pitch; structure.yaw = savedPlan.yaw; structure.roll = savedPlan.roll;
            }
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

        internal static string StartPlannedDefense(NetPlayer pilot, int sponsorId, int slot)
        {
            var sponsor = Find(sponsorId);
            if (sponsor == null || !CanIssueJumpgateOrder(pilot, sponsor, out _)) return "Dock at a station you can manage.";
            var plan = sponsor.placements?.Find(p => p?.kind == "defense-" + (slot + 1));
            if (plan == null) return "Save the defense placement first.";
            return StartOrbitalStructure(pilot, sponsorId, sponsor.stationIndex, plan.preset, plannedSlot: slot);
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

        internal static string FinishOrbitalFromStorage(NetPlayer pilot, int id)
        {
            var structure = FindOrbitalStructure(id);
            var sponsor = structure == null ? null : Find(structure.sponsorStationId);
            if (structure == null || structure.operational || structure.completesUtc > 0 || !OperationalSponsor(sponsor))
                return "That orbital project is not accepting construction materials.";
            if (!CanManageConstruction(pilot, sponsor, out string account) ||
                !CanManageStorage(pilot, sponsor, out _) || !AllowsFacilityUse(sponsor, account, "storage"))
                return "Dock at the sponsor station with construction and storage access.";
            if (structure.materials == null || structure.materials.Count == 0 ||
                structure.materials.Exists(material => material == null || material.delivered < 0 || material.delivered > material.required))
                return "The project has invalid material requirements.";
            var delivered = structure.materials.ConvertAll(material => material.delivered);
            var needed = new Dictionary<int, int>();
            foreach (var material in structure.materials)
            {
                int remaining = material.required - material.delivered;
                if (remaining <= 0) continue;
                needed.TryGetValue(material.item, out int amount);
                needed[material.item] = checked(amount + remaining);
            }
            EnsureStorageLots(sponsor);
            foreach (var entry in needed)
                if (OwnedStorageAmount(sponsor, account, entry.Key) < entry.Value)
                    return "Insufficient " + ConstructionItemName(entry.Key) + " in your station storage.";
            var previousLots = CloneStorageLots(sponsor);
            var previousStorage = sponsor.storage.ConvertAll(stack => stack == null ? null :
                new PlayerStationStorage { item = stack.item, amount = stack.amount });
            foreach (var entry in needed)
            {
                RemoveStorageLots(sponsor, account, entry.Key, entry.Value);
                var stack = sponsor.storage.Find(row => row != null && row.item == entry.Key);
                stack.amount -= entry.Value;
                if (stack.amount == 0) sponsor.storage.Remove(stack);
            }
            foreach (var material in structure.materials) material.delivered = material.required;
            long due = structure.maintenanceDueUtc, decay = structure.maintenanceDecayUtc;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (structure.buildSeconds == 0) ActivateOrbitalStructure(structure, now);
            else structure.completesUtc = now + structure.buildSeconds;
            if (Save()) return structure.operational ? $"{structure.name} is operational." : $"{structure.name} construction has started.";
            sponsor.storage = previousStorage;
            sponsor.storageLots = previousLots;
            for (int i = 0; i < delivered.Count; i++) structure.materials[i].delivered = delivered[i];
            structure.operational = false;
            structure.completesUtc = 0;
            structure.maintenanceDueUtc = due;
            structure.maintenanceDecayUtc = decay;
            return "The project could not be saved. Your stored goods were kept.";
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
            if (!structure.MaintenanceDue(now) || !structure.CanLoseIntegrityToMaintenance) return false;
            long elapsed = now - Math.Max(structure.maintenanceDueUtc, structure.maintenanceDecayUtc);
            if (elapsed < minimumElapsed) return false;

            double loss = structure.maxIntegrity * (structure.MaintenanceDecayPercentPerDay / 100d) * (elapsed / 86400d);
            float remaining = (float)Math.Max(structure.MaintenanceIntegrityFloor, structure.integrity - loss);
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
                if (!structure.operational || !structure.defense || structure.MaintenanceDue(now) || now < structure.nextShotUtc) continue;
                var runner = NetPlayer.OrbitRunner(structure.orbit);
                if (runner == null) continue;
                int race = OrbitalSponsorRace(sponsor);
                Vector3 centre = OrbitalStructurePosition(structure);
                NetPlayer target = null;
                float closest = structure.range * structure.range;
                if (!structure.ignorePlayers)
                {
                    foreach (var pilot in NetPlayer.All)
                    {
                        if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != structure.orbit ||
                            pilot.Hull <= 0 || !pilot.HasCurrentWreckClaimPose) continue;
                        float distance = (pilot.Position - centre).sqrMagnitude;
                        if (distance > closest || !HostileToDefense(pilot, sponsor, structure.orbit, race, now)) continue;
                        closest = distance;
                        target = pilot;
                    }
                }
                structure.nextShotUtc = now + structure.fireIntervalSeconds;
                if (target != null) target.ApplyOrbitalDefenseDamage(structure.orbit, structure.damage, centre);
                else if (!structure.ignoreNpcs) runner.FireOrbitalDefenseAtNpc(structure, race, centre);
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
