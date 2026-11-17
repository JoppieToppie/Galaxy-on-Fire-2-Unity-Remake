// ShipClassCatalog.cs
// The remake's canonical base ship classes. These definitions are available to engine systems and mods, but deliberately
// do not assign a class to any original ship. A modder must opt a ship in through ships.json "shipClass".
using System;
using System.Collections.Generic;

namespace GoF2Remake.Modding
{
    /// <summary>Default docking behaviour associated with a recognised base ship class.</summary>
    public enum ShipDockingRole
    {
        SizeBased = 0,       // preserve legacy small-hull internal docking; no external docking permission
        InternalOnly = 1,
        ExternalOnly = 2
    }

    /// <summary>Stable identities for the remake's fourteen built-in ship classes.</summary>
    public enum BaseShipClass
    {
        LightFighter,
        HeavyFighter,
        LightBomber,
        HeavyBomber,
        Interceptor,
        StealthBomber,
        Hauler,
        LightPrivateer,
        Privateer,
        HeavyPrivateer,
        Frigate,
        Freighter,
        Cruiser,
        Carrier
    }

    public sealed class ShipClassDefinition
    {
        public BaseShipClass Id { get; }
        public string Key { get; }
        public string DisplayName { get; }
        public ShipDockingRole DockingRole { get; }

        internal ShipClassDefinition(BaseShipClass id, string key, string displayName, ShipDockingRole dockingRole)
        {
            Id = id;
            Key = key;
            DisplayName = displayName;
            DockingRole = dockingRole;
        }
    }

    /// <summary>
    /// Canonical class registry shared by equipment restrictions, docking and UI/scanner presentation.
    /// The registry defines classes, not ship assignments: all original ships remain unclassified until a mod labels them.
    /// Unknown namespaced/custom class ids remain valid and retain their own classTags.
    /// </summary>
    public static class ShipClassCatalog
    {
        static readonly List<ShipClassDefinition> definitions = new List<ShipClassDefinition>
        {
            new ShipClassDefinition(BaseShipClass.LightFighter, "light_fighter", "Light Fighter", ShipDockingRole.InternalOnly),
            new ShipClassDefinition(BaseShipClass.HeavyFighter, "heavy_fighter", "Heavy Fighter", ShipDockingRole.InternalOnly),
            new ShipClassDefinition(BaseShipClass.LightBomber, "light_bomber", "Light Bomber", ShipDockingRole.InternalOnly),
            new ShipClassDefinition(BaseShipClass.HeavyBomber, "heavy_bomber", "Heavy Bomber", ShipDockingRole.InternalOnly),
            new ShipClassDefinition(BaseShipClass.Interceptor, "interceptor", "Interceptor", ShipDockingRole.InternalOnly),
            new ShipClassDefinition(BaseShipClass.StealthBomber, "stealth_bomber", "Stealth Bomber", ShipDockingRole.InternalOnly),
            new ShipClassDefinition(BaseShipClass.Hauler, "hauler", "Hauler", ShipDockingRole.SizeBased),
            new ShipClassDefinition(BaseShipClass.LightPrivateer, "light_privateer", "Light Privateer", ShipDockingRole.SizeBased),
            new ShipClassDefinition(BaseShipClass.Privateer, "privateer", "Privateer", ShipDockingRole.SizeBased),
            new ShipClassDefinition(BaseShipClass.HeavyPrivateer, "heavy_privateer", "Heavy Privateer", ShipDockingRole.SizeBased),
            new ShipClassDefinition(BaseShipClass.Frigate, "frigate", "Frigate", ShipDockingRole.ExternalOnly),
            new ShipClassDefinition(BaseShipClass.Freighter, "freighter", "Freighter", ShipDockingRole.SizeBased),
            new ShipClassDefinition(BaseShipClass.Cruiser, "cruiser", "Cruiser", ShipDockingRole.ExternalOnly),
            new ShipClassDefinition(BaseShipClass.Carrier, "carrier", "Carrier", ShipDockingRole.ExternalOnly)
        };

        static readonly Dictionary<string, ShipClassDefinition> byKey = BuildIndex();

        static Dictionary<string, ShipClassDefinition> BuildIndex()
        {
            var index = new Dictionary<string, ShipClassDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions) index.Add(definition.Key, definition);
            return index;
        }

        /// <summary>All fourteen definitions, in their stable display/order sequence.</summary>
        public static IReadOnlyList<ShipClassDefinition> All => definitions;

        public static ShipClassDefinition Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            byKey.TryGetValue(key.Trim(), out var definition);
            return definition;
        }

        public static bool TryGet(BaseShipClass id, out ShipClassDefinition definition)
        {
            foreach (var candidate in definitions)
                if (candidate.Id == id) { definition = candidate; return true; }
            definition = null;
            return false;
        }

        /// <summary>Known class keys are stored in canonical lowercase snake_case; custom keys are preserved.</summary>
        public static string CanonicalId(string key) => Get(key)?.Key ?? key;

        public static ShipDockingRole DockingRoleOf(string key) => Get(key)?.DockingRole ?? ShipDockingRole.SizeBased;
    }
}
