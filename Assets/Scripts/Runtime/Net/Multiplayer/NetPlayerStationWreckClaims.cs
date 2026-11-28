using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const float WreckControlRadiusMetres = 5000f;
        const double WreckControlSampleSeconds = 0.25;
        const bool FirstWreckClaimImmediate = true;

        static readonly Dictionary<int, WreckClaimControl> wreckControl = new Dictionary<int, WreckClaimControl>();
        static double lastWreckSample = double.NegativeInfinity;

        static void ResetWreckControl()
        {
            wreckControl.Clear();
            lastWreckSample = double.NegativeInfinity;
        }

        // Call from the authoritative partial-destruction transaction after settling the station's assets.
        // Replaying that transaction must preserve any already established claim.
        internal static void OpenWreckClaim(PlayerStationRecord station)
        {
            if (!NetGame.IsServer || !sessionActive || station == null || station.wreckClaimOpen) return;
            station.wreckClaim = new PlayerStationWreckClaim();
            station.wreckClaimOpen = true;
        }

        static void TickWreckClaims()
        {
            double sample = Time.realtimeSinceStartupAsDouble;
            if (sample - lastWreckSample < WreckControlSampleSeconds) return;
            bool uninterrupted = sample - lastWreckSample <= 2;
            lastWreckSample = sample;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            List<(PlayerStationRecord Station, PlayerStationWreckClaim Previous, bool ClaimChanged)> changes = null;
            bool publish = false;

            foreach (var station in registry.stations)
            {
                var claim = station?.wreckClaimOpen == true ? station.wreckClaim : null;
                if (claim == null || claim.exclusive)
                {
                    if (station != null) wreckControl.Remove(station.id);
                    continue;
                }
                if (!wreckControl.TryGetValue(station.id, out var control))
                    wreckControl.Add(station.id, control = new WreckClaimControl(claim));

                var side = ObserveWreckControl(station.stationIndex, control.Challenger,
                    out bool contested, out bool challengerPresent);
                if (!uninterrupted) control.Pause();
                var next = control.Observe(claim, side, contested, sample, now, FirstWreckClaimImmediate, challengerPresent);
                bool claimChanged = next != null;
                next ??= control.Checkpoint(claim);
                if (next == null) continue;

                changes ??= new List<(PlayerStationRecord, PlayerStationWreckClaim, bool)>();
                changes.Add((station, claim, claimChanged));
                publish |= claimChanged;
                station.wreckClaim = next;
            }

            if (changes == null) return;
            if (Save(publish))
            {
                foreach (var change in changes)
                    if (change.ClaimChanged) wreckControl.Remove(change.Station.id);
                return;
            }

            // Save can reload the registry after journal recovery. Roll back only the detached or
            // current objects we changed, without overwriting the recovered registry.
            foreach (var change in changes) change.Station.wreckClaim = change.Previous;
        }

        static WreckClaimant? ObserveWreckControl(int orbit, WreckClaimant challenger,
            out bool contested, out bool challengerPresent)
        {
            contested = false;
            challengerPresent = false;
            WreckClaimant? first = null;
            bool eligibleToClaim = false;
            if (orbit < 0) return first;
            foreach (var pilot in NetPlayer.All)
            {
                // Both the wreck position and the pilot pose are in Unity metres.
                if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != orbit ||
                    !pilot.HasCurrentWreckClaimPose || pilot.Hull <= 0 ||
                    (pilot.Position - StationPosition(orbit)).sqrMagnitude > WreckControlRadiusMetres * WreckControlRadiusMetres) continue;
                string account = NetProfiles.AccountOf(pilot.OwnerClientId);
                bool controls = !string.IsNullOrEmpty(account) && NetProfiles.Controls(pilot.OwnerClientId);
                eligibleToClaim |= controls;
                var faction = NetFactions.Of(account);
                var side = new WreckClaimant(string.IsNullOrEmpty(account) ? $"guest:{pilot.OwnerClientId}" : account,
                    faction?.id, faction?.name ?? pilot.DisplayName);
                if (controls && challenger.SameSide(side)) challengerPresent = true;
                if (!first.HasValue) first = side;
                else if (!first.Value.SameSide(side)) contested = true;
            }
            return eligibleToClaim && !contested ? first : null;
        }

        static string WreckClaimStatus(PlayerStationRecord station)
        {
            var claim = station.wreckClaim;
            string heading = $"Station #{station.id} wreck ({station.name})";
            if (!claim.HasClaimant) return heading + "\nUnclaimed";
            if (claim.exclusive) return heading + $"\nExclusive claim held by {claim.claimantName}";
            var remaining = Math.Max(0, claim.ExclusiveAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            long seconds = (remaining + 999) / 1000;
            return heading + $"\nClaim held by {claim.claimantName}\nExclusive in {seconds / 60}:{seconds % 60:00}";
        }
    }
}
