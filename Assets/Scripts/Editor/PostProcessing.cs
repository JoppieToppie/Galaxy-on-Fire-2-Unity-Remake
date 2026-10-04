// PostProcessing.cs  (Editor only)
// Bloom for the emissive parts. The original has no bloom; the glowing layers (*_emissive, *_lights_add,
// *_engine_add, *_glow...) are unlit/additive meshes drawn at full brightness. Here those materials get
// _Glow > 1 so they come out in HDR above the bloom threshold, while hulls, skyboxes and UI stay below it.
//
// Menu "GoF2/Import/Apply Emissive Glow To Materials": sets _Glow on the GoF2 materials (also run by
//   "Build Materials And Prefabs").
// Menu "GoF2/Scenes/Add Post Processing To Open Scene": global Volume with Assets/Settings/GoF2_VolumeProfile.asset
//   (Bloom) and post-processing enabled on the main camera (also run by "Create Flight Test Scene").

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.EditorTools
{
    public static class PostProcessing
    {
        public const string ProfilePath = "Assets/Settings/GoF2_VolumeProfile.asset";

        /// <summary>HDR multiplier for unlit emissive layers (window lights, hull markings).</summary>
        public const float EmissiveGlow = 4f;
        /// <summary>HDR multiplier for additive layers (light sprites, engine flames, FX).</summary>
        public const float AdditiveGlow = 2.5f;
        /// <summary>The ships' engine glow (ship_engine_glow / v_ship_engine_glow: the player's engines, and the NPCs' with
        /// the remake option): a little more than the other additive layers.</summary>
        public const float EngineGlow = 3.5f;

        // Mesh names of the glowing layers. Unlit materials not matching this (skyboxes, galaxy map
        // background) keep _Glow = 1 so they never bloom.
        static readonly Regex GlowMesh = new Regex(@"(emissive|lights|engine|glow|_add)", RegexOptions.IgnoreCase);

        [MenuItem("GoF2/Import/Apply Emissive Glow To Materials", priority = 302)]
        public static void ApplyGlow()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(ImportSettings.Root + "/Resources/GoF2Data/resources.json");
            var table = JsonUtility.FromJson<ResTable>(json.text);
            var glowMats = new HashSet<int>(table.meshes.Where(m => GlowMesh.IsMatch(System.IO.Path.GetFileNameWithoutExtension(m.model)))
                                                        .Select(m => m.materialId));
            var shading = table.materials.ToDictionary(m => m.id, m => m.shading);
            var engineGlow = new HashSet<int>(table.materials.Where(m => m.textures != null && m.textures.Any(t => t.EndsWith("ship_engine_glow.png"))).Select(m => m.id));

            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { ImportSettings.Root + "/Materials" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || !mat.HasProperty("_Glow")) continue;
                var m = Regex.Match(mat.name, @"^mat_(\d+)_");
                if (!m.Success) continue;
                int id = int.Parse(m.Groups[1].Value);
                shading.TryGetValue(id, out var sh);
                float glow = 1f;
                if (engineGlow.Contains(id)) glow = EngineGlow;
                else if (sh == "additive" || sh == "additive_anim") glow = AdditiveGlow;   // all additive layers are light
                // A lit alpha-test layer is lit geometry, not a light, whatever its mesh is called (the wrecked station's
                // *_alpha_emissive girders: the original's shader cuts at alpha 0.5 and lights it; glowing, the red its
                // transparent texels carry bloomed around every girder).
                else if (sh == "lit_alpha_test") glow = 1f;
                else if (glowMats.Contains(id)) glow = EmissiveGlow;
                if (!Mathf.Approximately(mat.GetFloat("_Glow"), glow))
                {
                    mat.SetFloat("_Glow", glow);
                    EditorUtility.SetDirty(mat);
                    changed++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: emissive glow applied ({changed} materials changed).");
        }

        /// <summary>Creates the Bloom profile if it doesn't exist yet.</summary>
        public static VolumeProfile GetOrCreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            var bloom = profile.Add<Bloom>();
            // Threshold 1: only HDR values (the _Glow materials, bright specular) bloom.
            bloom.threshold.Override(1f);
            bloom.intensity.Override(1.5f);
            bloom.scatter.Override(0.7f);
            bloom.highQualityFiltering.Override(true);
            AssetDatabase.AddObjectToAsset(bloom, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        [MenuItem("GoF2/Scenes/Add Post Processing To Open Scene", priority = 113)]
        public static void AddToScene()
        {
            var profile = GetOrCreateProfile();
            var volume = Object.FindObjectsByType<Volume>().FirstOrDefault(v => v.sharedProfile == profile);
            if (volume == null)
            {
                var go = new GameObject("GoF2 Post Processing");
                Undo.RegisterCreatedObjectUndo(go, "Add GoF2 post processing");
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profile;
            }

            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("GoF2: no main camera; enable Post Processing on your camera manually."); return; }
            var data = cam.GetUniversalAdditionalCameraData();
            Undo.RecordObject(data, "Enable post processing");
            data.renderPostProcessing = true;
            data.volumeLayerMask |= 1 << volume.gameObject.layer;
            EditorUtility.SetDirty(data);
            Debug.Log($"GoF2: post processing added (Bloom, profile {ProfilePath}) and enabled on '{cam.name}'.");
        }
    }
}
