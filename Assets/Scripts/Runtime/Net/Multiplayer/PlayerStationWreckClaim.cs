using System;

namespace GoF2Remake.Multiplayer
{
    [Serializable]
    public sealed class PlayerStationWreckClaim
    {
        public string profile;
        public string factionId;
        public string claimantName;
        public long claimedUtcMilliseconds;
        public bool exclusive;
        public string challengerProfile, challengerFactionId;
        public double challengerSeconds;

        internal const long ExclusivityMilliseconds = 30 * 60 * 1000;

        internal long ExclusiveAt => claimedUtcMilliseconds + ExclusivityMilliseconds;
        internal bool HasClaimant => !string.IsNullOrEmpty(profile) || !string.IsNullOrEmpty(factionId);

        internal bool BelongsTo(WreckClaimant claimant) => HasClaimant &&
            string.Equals(profile ?? "", claimant.Profile ?? "", StringComparison.Ordinal) &&
            string.Equals(factionId ?? "", claimant.FactionId ?? "", StringComparison.Ordinal);
    }

    internal readonly struct WreckClaimant
    {
        internal readonly string Profile, FactionId, Name;

        internal WreckClaimant(string profile, string factionId, string name)
        {
            Profile = factionId == null ? profile : null;
            FactionId = factionId;
            Name = name;
        }

        internal bool SameSide(WreckClaimant other) =>
            string.Equals(Profile ?? "", other.Profile ?? "", StringComparison.Ordinal) &&
            string.Equals(FactionId ?? "", other.FactionId ?? "", StringComparison.Ordinal);
    }

    // Live observations are session-relative. Saved checkpoints record earned time only.
    internal sealed class WreckClaimControl
    {
        internal const double TransferSeconds = 5 * 60;
        WreckClaimant challenger;
        double earnedSeconds, lastSample;
        bool previouslyUncontested;

        internal WreckClaimant Challenger => challenger;

        internal WreckClaimControl(PlayerStationWreckClaim saved = null)
        {
            if (saved == null || (string.IsNullOrEmpty(saved.challengerProfile) && string.IsNullOrEmpty(saved.challengerFactionId))) return;
            challenger = new WreckClaimant(saved.challengerProfile, string.IsNullOrEmpty(saved.challengerFactionId) ? null : saved.challengerFactionId, null);
            if (!double.IsNaN(saved.challengerSeconds) && !double.IsInfinity(saved.challengerSeconds))
                earnedSeconds = Math.Clamp(saved.challengerSeconds, 0, TransferSeconds);
        }

        // While control is continuous, write at whole-second boundaries. On a pause or
        // departure, retain the exact last observed progress in the durable record.
        internal PlayerStationWreckClaim Checkpoint(PlayerStationWreckClaim claim)
        {
            double seconds = previouslyUncontested ? Math.Floor(earnedSeconds) : earnedSeconds;
            var currentCheckpoint = new WreckClaimant(claim.challengerProfile,
                string.IsNullOrEmpty(claim.challengerFactionId) ? null : claim.challengerFactionId, null);
            if (challenger.SameSide(currentCheckpoint))
            {
                if (seconds == claim.challengerSeconds ||
                    (previouslyUncontested && seconds < claim.challengerSeconds)) return null;
            }
            return new PlayerStationWreckClaim
            {
                profile = claim.profile,
                factionId = claim.factionId,
                claimantName = claim.claimantName,
                claimedUtcMilliseconds = claim.claimedUtcMilliseconds,
                exclusive = claim.exclusive,
                challengerProfile = challenger.Profile,
                challengerFactionId = challenger.FactionId,
                challengerSeconds = seconds
            };
        }

        internal PlayerStationWreckClaim Observe(PlayerStationWreckClaim claim,
            WreckClaimant? uncontested, bool contested, double sampleSeconds, long utcMilliseconds,
            bool firstClaimImmediate, bool challengerPresent = true)
        {
            if (claim.exclusive) return null;
            if (claim.HasClaimant && utcMilliseconds >= claim.ExclusiveAt)
            {
                return new PlayerStationWreckClaim
                {
                    profile = claim.profile,
                    factionId = claim.factionId,
                    claimantName = claim.claimantName,
                    claimedUtcMilliseconds = claim.claimedUtcMilliseconds,
                    exclusive = true,
                };
            }
            if (contested)
            {
                // A contest while the challenger is away supersedes their saved attempt.
                if (!challengerPresent) Reset();
                previouslyUncontested = false;
                return null;
            }
            if (!uncontested.HasValue)
            {
                Pause(); // nobody can erase or advance an attempt while the area is empty
                return null;
            }
            if (claim.BelongsTo(uncontested.Value))
            {
                Reset();
                return null;
            }

            var candidate = uncontested.Value;
            if (!challenger.SameSide(candidate))
            {
                challenger = candidate;
                earnedSeconds = 0;
                previouslyUncontested = false;
            }
            if (previouslyUncontested) earnedSeconds += Math.Max(0, sampleSeconds - lastSample);
            previouslyUncontested = true;
            lastSample = sampleSeconds;
            if (earnedSeconds < TransferSeconds && (claim.HasClaimant || !firstClaimImmediate))
                return null;

            return new PlayerStationWreckClaim
            {
                profile = candidate.Profile,
                factionId = candidate.FactionId,
                claimantName = candidate.Name,
                claimedUtcMilliseconds = utcMilliseconds,
            };
        }

        internal void Pause() => previouslyUncontested = false;

        void Reset()
        {
            challenger = default;
            previouslyUncontested = false;
            earnedSeconds = 0;
        }
    }
}
