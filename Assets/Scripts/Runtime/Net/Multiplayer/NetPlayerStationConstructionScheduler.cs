// Server-only derived construction schedule. Persistent UTC timestamps remain authoritative.
using System;
using System.Collections.Generic;

namespace GoF2Remake.Multiplayer
{
    internal enum ConstructionJobKind { Station, Module, Jumpgate, OrbitalStructure, DryDock, DonationFunding }

    // Unity's target framework does not require the newer .NET PriorityQueue API.
    // SortedSet orders by ready time, type and stable record ID; exact timestamps are retained
    // separately so stale/cancelled jobs cannot complete a replacement project.
    internal sealed class ConstructionDeadlineQueue
    {
        readonly SortedSet<(long ready, int kind, int id, long deadline)> pending =
            new SortedSet<(long, int, int, long)>();
        readonly Dictionary<(int kind, int id, long deadline), long> retryTimes =
            new Dictionary<(int, int, long), long>();

        long nextReady = long.MaxValue;
        internal int Count => pending.Count;

        void Add((long ready, int kind, int id, long deadline) job)
        {
            if (pending.Add(job)) nextReady = Math.Min(nextReady, job.ready);
        }

        internal void Reset()
        {
            pending.Clear();
            retryTimes.Clear();
            nextReady = long.MaxValue;
        }

        internal void BeginRebuild(long now)
        {
            retryTimes.Clear();
            foreach (var job in pending)
                if (job.ready > now && job.ready > job.deadline)
                    retryTimes[(job.kind, job.id, job.deadline)] = job.ready;
            pending.Clear();
            nextReady = long.MaxValue;
        }

        internal void Schedule(ConstructionJobKind kind, int id, long deadline, long now)
        {
            var key = ((int)kind, id, deadline);
            long ready = deadline > 0 ? deadline : now;
            if (retryTimes.TryGetValue(key, out long retry)) ready = Math.Max(ready, retry);
            Add((ready, (int)kind, id, deadline));
        }

        internal void EndRebuild() => retryTimes.Clear();

        internal bool TryTakeDue(long now, out (long ready, int kind, int id, long deadline) job)
        {
            job = default;
            // Constant-time idle check; only a due removal traverses the ordered set.
            if (pending.Count == 0 || nextReady > now) return false;
            job = pending.Min;
            pending.Remove(job);
            nextReady = pending.Count == 0 ? long.MaxValue : pending.Min.ready;
            return true;
        }

        internal void Retry((long ready, int kind, int id, long deadline) job, long now)
        {
            Add((now + 5, job.kind, job.id, job.deadline));
        }
    }

    public static partial class NetPlayerStations
    {
        static readonly ConstructionDeadlineQueue constructionSchedule = new ConstructionDeadlineQueue();
        static Registry constructionScheduleRegistry;
        static bool constructionScheduleDirty = true;

        static void ResetConstructionSchedule()
        {
            constructionSchedule.Reset();
            constructionScheduleRegistry = null;
            constructionScheduleDirty = true;
        }

        // Every successful registry write and recovered write-ahead transaction invalidates
        // this derived index. Coalesce multiple writes before the next server tick.
        static void MarkConstructionScheduleDirty() => constructionScheduleDirty = true;

        static bool AvailableForConstruction(PlayerStationRecord station) =>
            station != null && !station.destroyed && !station.retired && !station.wreckClaimOpen &&
            station.offeredForClaimUtc <= 0;

        static bool AwaitingModuleDonations(PlayerStationRecord station) =>
            AvailableForConstruction(station) && !station.underConstruction &&
            station.moduleBuildCompletesUtc <= 0 && !string.IsNullOrEmpty(station.moduleConstructionKind) &&
            station.moduleConstructionMaterials?.Count > 0 && station.donatedConstructionStock != null;

        static bool ScheduledGate(PlayerJumpgateRecord gate) =>
            gate != null && gate.building && gate.startedUtc > 0 && gate.completesUtc > 0 &&
            JumpgateConstructionPausedAt(gate) == 0;

        static bool ScheduledStructure(PlayerOrbitalStructure structure) =>
            structure != null && !structure.operational && structure.completesUtc > 0 &&
            OperationalSponsor(Find(structure.sponsorStationId));

        static bool ScheduledDryDock(PlayerStationRecord station) =>
            AvailableForConstruction(station) && !station.underConstruction && station.dryDockBuilding;

        static void RebuildConstructionSchedule(long now)
        {
            if (!ReferenceEquals(constructionScheduleRegistry, registry)) constructionSchedule.Reset();
            constructionScheduleRegistry = registry;
            constructionScheduleDirty = false;
            constructionSchedule.BeginRebuild(now);
            foreach (var station in registry.stations)
            {
                if (!AvailableForConstruction(station)) continue;
                if (station.underConstruction && station.stationBuildCompletesUtc > 0)
                    constructionSchedule.Schedule(ConstructionJobKind.Station, station.id, station.stationBuildCompletesUtc, now);
                else if (!station.underConstruction && station.moduleBuildCompletesUtc > 0 &&
                    !string.IsNullOrEmpty(station.moduleConstructionKind))
                    constructionSchedule.Schedule(ConstructionJobKind.Module, station.id, station.moduleBuildCompletesUtc, now);
                else if (AwaitingModuleDonations(station))
                    constructionSchedule.Schedule(ConstructionJobKind.DonationFunding, station.id, 0, now);
                if (ScheduledDryDock(station))
                    constructionSchedule.Schedule(ConstructionJobKind.DryDock, station.id, station.dryDockCompletesUtc, now);
            }
            if (registry.jumpgates != null)
                foreach (var gate in registry.jumpgates)
                    if (ScheduledGate(gate))
                        constructionSchedule.Schedule(ConstructionJobKind.Jumpgate, gate.orbitStationIndex, gate.completesUtc, now);
            if (registry.orbitalStructures != null)
                foreach (var structure in registry.orbitalStructures)
                    if (ScheduledStructure(structure))
                        constructionSchedule.Schedule(ConstructionJobKind.OrbitalStructure, structure.id, structure.completesUtc, now);
            constructionSchedule.EndRebuild();
        }

        static void TickConstruction(long now)
        {
            if (registry?.stations == null || ProfileWritesBlocked) return;
            if (constructionScheduleDirty || !ReferenceEquals(constructionScheduleRegistry, registry))
                RebuildConstructionSchedule(now);
            var original = registry;
            // A successful completion invalidates the next tick's index, not this loop.
            // Every popped entry is revalidated against the current authoritative record.
            while (ReferenceEquals(registry, original) && !ProfileWritesBlocked &&
                constructionSchedule.TryTakeDue(now, out var job))
            {
                bool retry = false;
                switch ((ConstructionJobKind)job.kind)
                {
                    case ConstructionJobKind.Station:
                    {
                        var station = Find(job.id);
                        if (!AvailableForConstruction(station) || !station.underConstruction ||
                            station.stationBuildCompletesUtc != job.deadline) break;
                        var rollback = CompleteStationConstruction(station);
                        if (Save()) Apply(NetGame.Db);
                        else { rollback(); retry = true; }
                        break;
                    }
                    case ConstructionJobKind.Module:
                    {
                        var station = Find(job.id);
                        if (!AvailableForConstruction(station) || station.underConstruction ||
                            station.moduleBuildCompletesUtc != job.deadline ||
                            string.IsNullOrEmpty(station.moduleConstructionKind)) break;
                        var rollback = CompleteModuleConstruction(station, station.moduleConstructionKind);
                        if (!Save()) { rollback(); retry = true; }
                        break;
                    }
                    case ConstructionJobKind.DonationFunding:
                    {
                        var station = Find(job.id);
                        if (!AwaitingModuleDonations(station)) break;
                        bool funded = TryFundModuleFromDonations(station, out bool ready);
                        // Insufficient goods are not polled: the next committed delivery or
                        // other durable registry change re-evaluates the project.
                        retry = !funded && ready;
                        break;
                    }
                    case ConstructionJobKind.Jumpgate:
                    {
                        var gate = FindJumpgate(job.id);
                        if (!ScheduledGate(gate) || gate.completesUtc != job.deadline) break;
                        CompleteJumpgateIfDue(gate);
                        retry = gate.building;
                        break;
                    }
                    case ConstructionJobKind.OrbitalStructure:
                    {
                        var structure = FindOrbitalStructure(job.id);
                        if (!ScheduledStructure(structure) || structure.completesUtc != job.deadline) break;
                        long previousDue = structure.maintenanceDueUtc;
                        ActivateOrbitalStructure(structure, now);
                        if (!Save())
                        {
                            structure.operational = false;
                            structure.maintenanceDueUtc = previousDue;
                            retry = true;
                        }
                        break;
                    }
                    case ConstructionJobKind.DryDock:
                    {
                        var station = Find(job.id);
                        if (!ScheduledDryDock(station) || station.dryDockCompletesUtc != job.deadline) break;
                        retry = !CompleteDryDockJob(station, now);
                        break;
                    }
                }
                if (retry && ReferenceEquals(registry, original))
                    constructionSchedule.Retry(job, now);
            }
        }
    }
}
