// MusicTracks.cs
// The flight music's choice (Radar::draw 0x157c6c, played by Traffic.UpdateMusic) as a plain rule, and the remake option
// that turns single tracks of it off (Options > Sound, a Discord player's request 2026-11-04: the battle track cut into
// the calm music whenever red-marked ships showed up). Every track is on by default, the original's music.

using System;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class MusicTracks
    {
        // The tracks the option lists (the original's FMOD music events): Traffic's battle set, the 0xe071 set without 140.
        public const string BattleLow = "battleLow";        // 140 Space_Battle_Low (1-2 hostile ships)
        public const string BattleMedium = "battleMedium";  // 141 Space_Battle_Medium (3-4)
        public const string BattleFull = "battleFull";      // 142 Space_Battle_Full (5+)
        public const string VoidBattle = "voidBattle";      // 136 Space_Combat_Void (the alien orbit, an attacked station, 0x10)
        public const string Specters1 = "specters1";        // 149 StealthFighter1 (hostile Specters, 1-4)
        public const string Specters2 = "specters2";        // 150 StealthFighter2 (hostile Specters, 5+)
        public const string Wanted = "wanted";              // 151 WantedBoardCriminal (an uncovered Most Wanted criminal)

        /// <summary>The tracks in the order Options lists them.</summary>
        public static readonly string[] All = { BattleLow, BattleMedium, BattleFull, VoidBattle, Specters1, Specters2, Wanted };

        /// <summary>The Settings key of a track's toggle (in Settings.SoundKeys).</summary>
        public static string Key(string track) => "musicTrack." + track;

        /// <summary>The player's option: the track plays (default) or is skipped.</summary>
        public static bool On(string track) => Settings.MusicTrackOn(track);

        /// <summary>Traffic's music categories: 0 the calm track, 1-3 Space_Battle_Low / Medium / Full, 4 the Most Wanted
        /// criminal's, 5 / 6 the Specters'.</summary>
        public const int Calm = 0, Low = 1, Medium = 2, Full = 3, WantedCategory = 4, SpectersFew = 5, SpectersMany = 6;

        /// <summary>The track a category plays: null for the calm one (the orbit's, not listed). In the alien orbit, at a
        /// Void-attacked station and at campaign 0x10 every fight but the criminal's is 136.</summary>
        public static string Track(int category, bool voidBattle) => category switch
        {
            <= Calm => null,
            WantedCategory => Wanted,
            _ when voidBattle => VoidBattle,
            SpectersFew => Specters1,
            SpectersMany => Specters2,
            Low => BattleLow,
            Medium => BattleMedium,
            _ => BattleFull,
        };

        /// <summary>Radar::draw 0x157c6c's music category, the turned-off tracks skipped.
        /// hostiles: the scanner's active hostile ships; fleetBattle: a capital ships' battle rages (remake, at least 2);
        /// playing: the category playing (-1 none); calmHold: campaign 0x91 (a calm orbit never brings the calm track back);
        /// wanted: an uncovered Most Wanted criminal; specters: hostile Specters; on: whether a track plays (null = all).
        /// A turned-off battle track leaves the calm track playing; a turned-off criminal's or Specters' track the battle
        /// track the ship count picks (or the calm one, that off too).</summary>
        public static int Category(int hostiles, bool fleetBattle, int playing, bool calmHold, bool wanted, bool specters,
                                   bool voidBattle, Func<string, bool> on = null)
        {
            on ??= _ => true;
            int count = hostiles <= 0 ? Calm : hostiles <= 2 ? Low : hostiles <= 4 ? Medium : Full;
            if (count < Medium && fleetBattle) count = Medium;
            int cat = count;
            if (cat == Calm && playing > Calm && calmHold) cat = playing;
            if (cat > Calm && wanted) cat = WantedCategory;
            else if (cat > Calm && specters) cat = cat >= Full ? SpectersMany : SpectersFew;
            int allowed = Allowed(cat, count, voidBattle, on);
            // A battle track that plays is kept until the orbit is calm (the 0xe071 set: 136, 140-142, 149-151), also when
            // the track the ship count picks now is off; only an uncovered criminal switches to 151 (with 151 on).
            if (cat > Calm && playing > Calm && allowed != WantedCategory) allowed = playing;
            // A playing track turned off meanwhile gives way too.
            return Allowed(allowed, count, voidBattle, on);
        }

        static int Allowed(int cat, int count, bool voidBattle, Func<string, bool> on)
        {
            if (cat <= Calm || on(Track(cat, voidBattle))) return cat;
            if (cat >= WantedCategory && count > Calm && on(Track(count, voidBattle))) return count;
            return Calm;
        }
    }
}
