// AssembledObject.cs
// Root component of an assembled prefab (Resources/Assembled/...): one game object (ship, station, jumpgate,
// asteroid, hangar...) put together from its separate game meshes the way the original code does it
// (AEGeometry root mesh + addChild meshes + setLodMeshes). Built by "GoF2 > Build > Assembled Prefabs"
// from Resources/GoF2Data/assemblies.json.

using UnityEngine;

namespace GoF2Remake.Visuals
{
    public class AssembledObject : MonoBehaviour
    {
        /// <summary>Folder under Resources that holds the assembled prefabs ({pack}/{category}/{name}).</summary>
        public const string ResourcesFolder = "Assembled";

        /// <summary>Loads an assembled prefab by its assemblies.json entry (only what a level needs gets loaded).</summary>
        /// <summary>The entry's prefab path under Resources (null without an entry).</summary>
        public static string PrefabPath(GoF2Remake.Data.AssemblyData entry) => entry == null ? null : $"{ResourcesFolder}/{entry.pack}/{entry.category}/{entry.name}";

        public static GameObject LoadPrefab(GoF2Remake.Data.AssemblyData entry)
        {
            if (entry == null) return null;
            if (entry.pack != Modding.ModShips.Pack && entry.name != null && entry.name.IndexOf(Modding.ModShipKits.VariantSeparator) > 0)
                return Modding.ModShips.SkinnedTemplate(entry);   // a game ship in a mod's skin (Modding.ModShipKits.CustomizationOf)
            if (entry.pack == Modding.ModShips.Pack)   // a mod's ship or station (built at run time)
                return Modding.ModStations.IsStationAssembly(entry.name) ? Modding.ModStations.Template(entry.name) : Modding.ModShips.Template(entry.name);
            var prefab = Resources.Load<GameObject>(PrefabPath(entry));
            if (prefab == null) Debug.LogWarning($"AssembledObject: no prefab for '{entry.name}' ({entry.pack}/{entry.category})");
            return prefab;
        }

        /// <summary>Remake mods: the assembly this object was made from (its prefab's name: Awake runs inside Instantiate,
        /// before any code renames the copy), the scope of a mod's per-object textures (textures/&lt;scope&gt;/).</summary>
        public string ModScope { get; private set; }

        // Remake mods: texture replacements (skins) on this object's renderers.
        void Awake()
        {
            ModScope = name.EndsWith("(Clone)") ? name.Substring(0, name.Length - "(Clone)".Length).Trim() : name;
            Modding.ModTextures.Apply(gameObject, ModScope);
            EmissiveReflection.Apply(gameObject);   // Remake: the emissive layers reflect the cubemap like the hull
            FxFog.Apply(gameObject);   // Modified: the burning station's fire and smoke take the scene's fog like the hull
            if (GetComponent<AutoLods>() == null) gameObject.AddComponent<AutoLods>();   // Remake: levels of detail (Settings.Lods)
        }

        [Tooltip("Resource id of the root (LOD 0) mesh.")]
        public int rootMeshId;

        [Tooltip("Where the composition rule comes from in the decompiled code.")]
        public string origin;

        [Tooltip("Original LOD switch distances in game units (the LODGroup approximates these for the reference FOV).")]
        public float[] lodDistancesGameUnits;

        [Tooltip("Original last-visible distance in game units (0 = always visible).")]
        public float lastVisibleDistanceGameUnits;

        [Tooltip("Separate object the game spawns when this one is destroyed (e.g. *_explosion_anim).")]
        public GameObject explosionPrefab;

        [Tooltip("Parts only used when this is the player's own ship (Globals::getShipGroup param_3 = true).")]
        public GameObject[] playerVariantParts;

        [Tooltip("Parts only used for NPC ships (Globals::getShipGroup param_3 = false).")]
        public GameObject[] npcVariantParts;

        [Tooltip("Parts the game only shows under a condition (mission state, race, random, graphics option).")]
        public GameObject[] conditionalParts;

        [Tooltip("The condition for each entry of conditionalParts, as recovered from the code.")]
        public string[] conditions;

        [Tooltip("Rotation the game applies to the whole object when it places it (engine-space euler degrees). " +
                 "Not baked into the prefab, which stays in model space.")]
        public Vector3 spawnRotationEngine;

        /// <summary>The engine exhaust on / off: the player parts ('player') or the NPC parts (cutscenes: engines off).</summary>
        /// <summary>The NPC engine parts exist (the Kaamo Club's ships 55-63 have only the player's engine glow).</summary>
        public bool HasNpcExhaust => npcVariantParts != null && npcVariantParts.Length > 0;

        public void SetExhaust(bool on, bool player)
        {
            var parts = player ? playerVariantParts : npcVariantParts;
            if (parts != null && parts.Length > 0) { foreach (var g in parts) if (g != null) g.SetActive(on); return; }
            // Assemblies without engine variants (the freighters: one *_engine_add part): that part (#46: step 41's crashed
            // Errkt kept his engines lit, PlayerFixedObject::setExhaustVisible(false) did nothing).
            foreach (Transform child in transform)
                if (child.name.EndsWith("_engine_add")) child.gameObject.SetActive(on);
        }

        /// <summary>Switches between the player and NPC parts (engine meshes etc.).</summary>
        public void SetPlayerVariant(bool player)
        {
            if (playerVariantParts != null) foreach (var g in playerVariantParts) if (g != null) g.SetActive(player);
            if (npcVariantParts != null) foreach (var g in npcVariantParts) if (g != null) g.SetActive(!player);
        }
    }
}
