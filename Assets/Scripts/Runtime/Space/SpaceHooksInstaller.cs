// SpaceHooksInstaller.cs
// Fills in what the game's rules (GoF2.Game, GameHooks) need from the flight layer: repairing the live ship, resetting the
// key bindings, pruning the hangar shadows' cache. Runs before the first scene, and in the Editor on every load
// (EditorHookInstaller), so edit-mode tools see the same.

using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public static class SpaceHooksInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            GameHooks.RepairLiveShip = PlayerHealth.RepairLive;
            GameHooks.ResetControls = GameControls.ResetToDefaults;
            GameHooks.ModsScanned = ids => { ShipShadowBaker.PruneCache(ids); Visuals.ModLodCache.PruneCache(ids); };
            GameHooks.FlightMessage = text => UI.UiHooks.FlightMessage?.Invoke(text);
            // Mod scripts' game.message: the flight HUD's message line, docked a toast.
            GameHooks.Notice = text => UI.UiHooks.FlightMessage?.Invoke(text) == true || UI.UiHooks.StationToast?.Invoke(text) == true;
            // Remake mods (PR #87): the customizable ships' builds and the ships' own sounds, for GoF2.Game's rules and parsing.
            GameHooks.KitBuildEffect = Modding.ModShipKits.BuildEffect;
            GameHooks.KitMounts = Modding.ModShipKits.BuildMounts;
            GameHooks.ParseShipSounds = (o, mod, file) => Modding.ModShipSounds.ParseShip(o, mod, file);
            GameHooks.KitBuildText = o => Modding.ModShipKits.Build.FromJson(o).ToString();
            // Remake mods: a mod capital ship's / freighter's boxes from its own model.
            CollisionVolume.ShipVolumes = Modding.ModCapitalShips.HullVolumes;
            // Preserve the original Midorian-with-Nivelian-face exception and custom race identity by default.
            SpaceHooks.VisitorRace = (agent, station) => agent.modRace >= Modding.ModRaces.FirstRace ? agent.modRace
                : agent.race == 3 && agent.portrait != null && agent.portrait[0] == 2 ? 2 : agent.race;
        }
    }
}
