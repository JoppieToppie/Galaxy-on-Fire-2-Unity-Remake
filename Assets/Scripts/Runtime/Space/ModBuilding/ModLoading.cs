// ModLoading.cs
// Remake mods: what the main menu's loading screen shows (UI.ModLoadingView) while the mods that are on load in the
// background: their ship models (ModShips, all at once), stations and rooms (ModStations), weapon fx (ModWeapons), loot
// containers (ModCrates), music
// (ModMusic), sound effects (ModSounds) and the ships' and weapons' own sounds (ModShipSounds), then the ships' hangar shadows (World.ShipShadowBaker: read from the cache
// or baked, once the models are built). Busy until all are done; Progress weighs each ship as four steps, each station
// three, each weapon two and each track, sound or hangar shadow one.

using System.Collections.Generic;

namespace GoF2Remake.Modding
{
    public static class ModLoading
    {
        /// <summary>Starts every loader for the mods that are on (the startup splash, the main menu): their ship models,
        /// station models and planet / sun textures, weapon fx and sounds, sound effects, texture replacements (skins),
        /// characters' portraits and music. Each loader does nothing when its part of the mods hasn't changed.</summary>
        public static void Preload()
        {
            ModShips.Preload();
            ModStations.Preload();
            ModWeapons.Preload();
            ModCrates.Preload();
            ModSounds.Preload();
            ModTextures.Preload();
            ModCharacters.Preload();
            ModMusic.Preload();
            _ = Busy;   // and the rest (the ships' own sounds, voices, hangar shadows)
        }

        /// <summary>Something of the mods is still loading.</summary>
        public static bool Busy => !ModShips.Ready || !ModStations.Ready || !ModWeapons.Ready || !ModCrates.Ready || !ModMusic.Ready || !ModSounds.Ready || !ModShipSounds.Ready || !ModTextures.Ready
                                   || !ModVoices.ReplacementsReady
                                   || !World.ShipShadowBaker.ModShipsBaked;

        /// <summary>The mods that are on bring something to load (ship, station or room models, planets / suns / skies,
        /// weapon fx, music, sounds). The counts are known once Busy has started each loader; with none (no mods, or
        /// only quests) the loading screen isn't shown while the loaders finish.</summary>
        public static bool HasWork => ModShips.Count > 0 || ModStations.Count > 0 || ModStations.Backdrops > 0 || ModWeapons.Count > 0
                                      || ModMusic.Count > 0 || ModSounds.Count > 0 || ModShipSounds.Count > 0 || ModTextures.Count > 0
                                      || ModVoices.ReplacementCount > 0;

        /// <summary>How far it is, 0..1.</summary>
        public static float Progress
        {
            get
            {
                float shipWeight = ModShips.Count * 4f, stationWeight = ModStations.Count * 3f + 1f, weaponWeight = ModWeapons.Count * 2f;
                float musicWeight = ModMusic.Count, soundWeight = ModSounds.Count + ModShipSounds.Count, textureWeight = ModTextures.Count;
                float shadowWeight = World.ShipShadowBaker.PrebakeWanted ? ModShips.Count : 0f;
                float total = shipWeight + stationWeight + weaponWeight + musicWeight + soundWeight + textureWeight + shadowWeight;
                float skins = ModTextures.Count > 0 ? (ModTextures.Count - ModTextures.Loading) / (float)ModTextures.Count : 1f;
                float music = ModMusic.Count > 0 ? (ModMusic.Count - ModMusic.Loading) / (float)ModMusic.Count : 1f;
                float sounds = soundWeight > 0 ? (soundWeight - ModSounds.Loading - ModShipSounds.Loading) / soundWeight : 1f;
                float weapons = ModWeapons.Count > 0 ? ModWeapons.Done / (float)ModWeapons.Count : 1f;
                return (ModShips.Progress * shipWeight + ModStations.Progress * stationWeight + weapons * weaponWeight
                        + music * musicWeight + sounds * soundWeight + skins * textureWeight + World.ShipShadowBaker.PrebakeProgress * shadowWeight) / total;
            }
        }

        /// <summary>What is loading now, in words (the ship being built, else the music).</summary>
        public static string Line
        {
            get
            {
                var parts = new List<string>();
                if (!ModShips.Ready && ModShips.Count > 0)
                    parts.Add(string.Format(Data.Localization.Extra("modsLoadingShips", "Ships {0} / {1}"), ModShips.Done, ModShips.Count)
                              + (ModShips.Current != null ? ": " + ModShips.Current : ""));
                if (!ModStations.Ready && ModStations.Count > 0)
                    parts.Add(string.Format(Data.Localization.Extra("modsLoadingStations", "Stations {0} / {1}"), ModStations.Done, ModStations.Count)
                              + (ModStations.Current != null ? ": " + ModStations.Current : ""));
                if (!ModWeapons.Ready && ModWeapons.Count > 0)
                    parts.Add(string.Format(Data.Localization.Extra("modsLoadingWeapons", "Weapons {0} / {1}"), ModWeapons.Done, ModWeapons.Count));
                int soundCount = ModSounds.Count + ModShipSounds.Count, soundsLeft = ModSounds.Loading + ModShipSounds.Loading;
                if (soundsLeft > 0 && soundCount > 0)
                    parts.Add(string.Format(Data.Localization.Extra("modsLoadingSounds", "Sounds {0} / {1}"), soundCount - soundsLeft, soundCount));
                if (!ModTextures.Ready && ModTextures.Count > 0)
                    parts.Add(string.Format(Data.Localization.Extra("modsLoadingTextures", "Textures {0} / {1}"), ModTextures.Count - ModTextures.Loading, ModTextures.Count));
                if (!ModMusic.Ready && ModMusic.Count > 0)
                    parts.Add(string.Format(Data.Localization.Extra("modsLoadingMusic", "Music {0} / {1}"), ModMusic.Count - ModMusic.Loading, ModMusic.Count));
                var shadows = World.ShipShadowBaker.PrebakeCurrent;
                if (ModShips.Ready && shadows != null && World.ShipShadowBaker.PrebakeDone < World.ShipShadowBaker.PrebakeCount)
                    parts.Add(string.Format(Data.Localization.Extra("modsLoadingShadows", "Hangar shadows {0} / {1}"),
                                            World.ShipShadowBaker.PrebakeDone, World.ShipShadowBaker.PrebakeCount) + ": " + shadows);
                return string.Join("   ·   ", parts);
            }
        }
    }
}
