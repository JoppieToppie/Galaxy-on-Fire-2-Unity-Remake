// VrSetup.cs
// "GoF2 > Setup > Configure VR (OpenXR)": the project's XR setup for PC VR (VrMode), idempotent. XR Plug-in Management for
// Standalone with the OpenXR loader, but not started with the player (Initialize XR on Startup off: VR only with the -vr
// launch flag, VrMode starts the loader itself), and OpenXR's controller profiles (Oculus / Meta Touch, Valve Index, HTC
// Vive, Windows Mixed Reality, HP Reverb G2, the Khronos simple controller) so the usual headsets' controllers map onto the
// Input System's XRController layouts.

using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace GoF2Remake.EditorTools
{
    public static class VrSetup
    {
        const string PerTargetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        [MenuItem("GoF2/Setup/Configure VR (OpenXR)", priority = 401)]
        public static void Configure()
        {
            var group = BuildTargetGroup.Standalone;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(PerTargetPath);
                if (perTarget == null)
                {
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, PerTargetPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;   // VR only with -vr (VrMode starts the loader)
            bool assigned = XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);

            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            int profiles = 0;
            if (openXr != null)
            {
                foreach (var feature in openXr.GetFeatures<OpenXRInteractionFeature>())
                {
                    bool want = feature is OculusTouchControllerProfile || feature is MetaQuestTouchPlusControllerProfile
                                || feature is ValveIndexControllerProfile || feature is HTCViveControllerProfile
                                || feature is MicrosoftMotionControllerProfile || feature is HPReverbG2ControllerProfile
                                || feature is KHRSimpleControllerProfile;
                    if (!want) continue;
                    feature.enabled = true;
                    profiles++;
                    EditorUtility.SetDirty(feature);
                }
                EditorUtility.SetDirty(openXr);
            }
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            Debug.Log($"VrSetup: OpenXR loader for Standalone {(assigned ? "assigned" : "already assigned")}, start on launch off, {profiles} controller profiles on.");
        }
    }
}
