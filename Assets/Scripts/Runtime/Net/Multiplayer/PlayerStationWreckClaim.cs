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
            string.Equals(Profile, other.Profile, StringComparison.Ordinal) &&
            string.Equals(FactionId, other.FactionId, StringComparison.Ordinal);
    }

    // Holds live control samples. The exclusive deadline and the current claimant are persisted separately.
    internal sealed class WreckClaimControl
    {
        internal const double TransferSeconds = 5 * 60;
        WreckClaimant challenger;
        double earnedSeconds, lastSample;
        bool previouslyUncontested;

        internal PlayerStationWreckClaim Observe(PlayerStationWreckClaim claim,
            WreckClaimant? uncontested, bool contested, double sampleSeconds, long utcMilliseconds,
            bool firstClaimImmediate)
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
                // Neither the interval entering a contest nor the one leaving it can be credited.
                previouslyUncontested = false;
                return null;
            }
            if (!uncontested.HasValue || claim.BelongsTo(uncontested.Value))
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
