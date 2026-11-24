// Complete named policies for the two experience scopes. UI placement must not leave hidden inherited values.
using System;
using System.Collections.Generic;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ExperiencePresets
    {
        public sealed class Value
        {
            public readonly string Key;
            public readonly float Original, Remake;
            public Value(string key, float original, float remake) { Key = key; Original = original; Remake = remake; }
            public float ForChoice(int choice) => choice == 0 ? Original : Remake;
        }

        public static IReadOnlyList<Value> Cosmetics { get; } = Array.AsReadOnly(new[]
        {
            new Value("launchCamera", 1f, 1f),
            new Value("hangarFlights", 0f, 1f),
            new Value("animatedDialogue", 0f, 1f),
            new Value("autoAdvanceDialogue", 1f, 1f),
            new Value("originalUi", 0f, 0f), // legacy UI is a manual Custom choice
            new Value("tutorialHints", 0f, 0f), // the new-game screen can opt in
            new Value("inputHints", 1f, 1f),
            new Value("hintScale", 100f, 100f),
            new Value("shopByCategory", 0f, 1f),
            new Value("kaamoSpecialUiFeatures", 0f, 1f),
            new Value("hudMode", Settings.HudFull, Settings.HudFull),
            new Value("hudScale", 100f, 100f),
            new Value("hudOpacity", 100f, 100f),
            new Value("dyingCargoMarkers", 1f, 1f),
            new Value("farShipMarkers", Settings.FarMarkerOriginal, Settings.FarMarkerOriginal),
            new Value("markerDistance", 0f, 1f),
            new Value("interstellarDistance", 1f, 1f),
            new Value("systemMapAnimationSpeed", 1f, 1f),
            // Preserve the presentation policies introduced on main after the original PR.
            new Value("rebuiltPlanets", 0f, 1f),
            new Value("cameraShake", 1f, 0.3f),
            new Value("remasteredEffects", 0f, 1f),
        });

        public static IReadOnlyList<Value> Gameplay { get; } = Array.AsReadOnly(new[]
        {
            new Value("pirateEvents", 0f, 1f),
            new Value("capitalShips", 0f, 1f),
            new Value("derelictWrecks", 0f, 1f),
            new Value("stationScale", 1f, 1f),
            new Value("kaamoStacking", 0f, 1f),
            new Value("kaamoMaxLevel", SessionRules.DefaultKaamoLevel, SessionRules.DefaultKaamoLevel),
            new Value("kaamoVip", Settings.KaamoVipOff, Settings.KaamoVipCard),
            new Value("cloakBay", 0f, 1f),
            new Value("kaamoKeepsEquipment", 0f, 1f),
            new Value("blueprintAutocomplete", 0f, 0f),
            new Value("cloakLosesPursuers", 0f, 1f),
            new Value("empBreaksCloak", 0f, 1f),
            new Value("bombArming", 0f, 1f),
            new Value("smarterNpcTargeting", 0f, 1f),
            new Value("npcDogfight", SessionRules.DogfightOriginalMode, SessionRules.DefaultDogfight),
            new Value("es2Aiming", 0f, 0f),
            new Value("es2NpcFlight", 0f, 0f),
            new Value("turretLeadAim", 0f, 1f),
            new Value("remakeFreeLook", 0f, 1f),
            new Value("shipCollision", SessionRules.CollisionOriginal, SessionRules.CollisionMixed),
            new Value("originalTargetLock", 1f, 0f),
            new Value("keepEnemyLock", 0f, 0f),
            new Value("collisionDamage", 0f, 0f),
            new Value("flightStyle", FlightStyles.Original, FlightStyles.Original),
            new Value("fastForwardCruise", 0f, 0f),
            new Value("khadorSlipstream", 0f, 1f),
            new Value("hostileAutopilotFastForward", 0f, 0f),
            new Value("autoJumpgate", 1f, 1f),
            new Value("informerOriginalRule", 1f, 0f),
            new Value("playOnInConversations", 0f, 0f),
            new Value("modShipTraffic", 0f, 2f),
        });

        public static IReadOnlyList<Value> ForScope(bool cosmetics) => cosmetics ? Cosmetics : Gameplay;
        public static bool Contains(bool cosmetics, string key)
        {
            foreach (var value in ForScope(cosmetics)) if (value.Key == key) return true;
            return false;
        }
    }
}
