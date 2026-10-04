// SkyLayerAssets.cs
// What the extra sky layers (SkyLayers) need at runtime and can't load by name: the layer meshes as their per-mesh
// prefabs (with the part animations) and one GoF2/SkyLayer material per layer. Made by GoF2 > Scenes > Space Scene
// (SpaceSceneBuilder.BuildSkyLayers) into Resources/GoF2Backdrop/SkyLayerAssets.

using UnityEngine;

namespace GoF2Remake.World
{
    [CreateAssetMenu(menuName = "GoF2/Sky Layer Assets")]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class SkyLayerAssets : ScriptableObject
    {
        public const string ResourcePath = Backdrop.MaterialFolder + "/SkyLayerAssets";

        [Tooltip("Mesh 18847 sn_skybox_planet_ring_alpha + texture 29018 (alpha).")]
        public GameObject ringSky;
        public Material ringSkyMaterial;
        [Tooltip("Mesh 18848 sn_skybox_storms_anim_add + texture 29019 (additive, animated).")]
        public GameObject storms;
        public Material stormsMaterial;
        [Tooltip("Meshes 17824 / 17825 sn_skybox_015_flares_1/2_anim; texture 10084 (mission < 106) or 10085 nasty (additive).")]
        public GameObject flares1, flares2;
        public Material flaresMaterial, flaresNastyMaterial;
        [Tooltip("Mesh 14375 skybox_asteroid_belt_alpha, textures 33410 / 33411 (alpha + LIGHT0).")]
        public GameObject asteroidBelt;
        public Material asteroidBeltMaterial;

        static SkyLayerAssets cached;
        public static SkyLayerAssets Load() => cached != null ? cached : cached = Resources.Load<SkyLayerAssets>(ResourcePath);
    }
}
