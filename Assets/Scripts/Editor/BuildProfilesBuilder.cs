// BuildProfilesBuilder.cs  (Editor only)
// Menu "GoF2/Build/Backend Build Profiles": a build profile per graphics backend, each a copy of its platform's profile with
// that one API as a Player Settings override, in Assets/Settings/Build Profiles. A build compiles every shader once per API,
// so a single backend halves the shaders of a two-API platform. The platform's own profile (Linux, Windows, Android) stays
// the multiple-backend one, on the project's API list. An override is a whole copy of the Player Settings, so run this again
// after changing them or a platform's profile: it copies both afresh. Unity offers this only in the profile's inspector, so
// the override goes through its internal members. macOS and iOS are Metal alone, so one plain profile each; a profile made
// without its platform's Editor module has no platform settings, and is made again here once the module is there.

using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.EditorTools
{
    public static class BuildProfilesBuilder
    {
        const string Folder = "Assets/Settings/Build Profiles/";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static readonly (string name, BuildTarget target)[] MetalPlatforms =
        {
            ("macOS", BuildTarget.StandaloneOSX),
            ("iOS", BuildTarget.iOS),
        };

        static readonly (string name, string from, BuildTarget target, GraphicsDeviceType api)[] Profiles =
        {
            ("Linux Vulkan", "Linux", BuildTarget.StandaloneLinux64, GraphicsDeviceType.Vulkan),
            ("Linux OpenGL", "Linux", BuildTarget.StandaloneLinux64, GraphicsDeviceType.OpenGLCore),
            ("Windows D3D12", "Windows", BuildTarget.StandaloneWindows64, GraphicsDeviceType.Direct3D12),
            ("Windows D3D11", "Windows", BuildTarget.StandaloneWindows64, GraphicsDeviceType.Direct3D11),
            ("Android Vulkan", "Android™", BuildTarget.Android, GraphicsDeviceType.Vulkan),
            ("Android GLES3", "Android™", BuildTarget.Android, GraphicsDeviceType.OpenGLES3),
        };

        [MenuItem("GoF2/Build/Backend Build Profiles", priority = 224)]
        public static void Build()
        {
            foreach (var p in MetalPlatforms) MakePlatformProfile(p.name, p.target);
            foreach (var p in Profiles)
            {
                string path = Folder + p.name + ".asset";
                if (BuildProfile.GetActiveBuildProfile() == AssetDatabase.LoadAssetAtPath<BuildProfile>(path))
                {
                    Debug.LogWarning($"GoF2: {p.name} is the active profile, left as it is");
                    continue;
                }
                AssetDatabase.DeleteAsset(path);
                if (!AssetDatabase.CopyAsset(Folder + p.from + ".asset", path))
                    throw new InvalidOperationException($"No build profile {p.from} to copy");
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                Call(typeof(BuildProfile), profile, "CreatePlayerSettingsFromGlobal");
                var settings = profile.GetComponent<BuildProfilePlayerSettings>();
                Call(typeof(BuildProfilePlayerSettings), settings, "SetUseDefaultGraphicsAPIs", p.target, false);
                Call(typeof(BuildProfilePlayerSettings), settings, "SetGraphicsAPIs", p.target, new[] { p.api }, false);
                EditorUtility.SetDirty(settings);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                Debug.Log($"GoF2: build profile {p.name}: {p.api} only");
            }
        }

        static void MakePlatformProfile(string name, BuildTarget target)
        {
            string path = Folder + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
            if (profile != null && typeof(BuildProfile).GetProperty("platformBuildProfile", Any).GetValue(profile) != null) return;
            AssetDatabase.DeleteAsset(path);
            var util = typeof(BuildProfile).Assembly.GetType("UnityEditor.Build.Profile.BuildProfileModuleUtil");
            var id = Call(util, null, "GetPlatformId", target, StandaloneBuildSubtarget.Player);
            Call(typeof(BuildProfile), null, "CreateInstance", id, path);
            bool installed = BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target);
            Debug.Log($"GoF2: build profile {name}" + (installed ? "" : ": no platform settings until its Editor module is installed"));
        }

        static object Call(Type type, object target, string method, params object[] args)
        {
            foreach (var m in type.GetMethods(Any))
                if (m.Name == method && m.GetParameters().Length == args.Length && Matches(m, args))
                    return m.Invoke(target, args);
            throw new MissingMethodException(type.Name, method);
        }

        static bool Matches(MethodInfo method, object[] args)
        {
            var parameters = method.GetParameters();
            for (int i = 0; i < args.Length; i++)
                if (!parameters[i].ParameterType.IsInstanceOfType(args[i])) return false;
            return true;
        }
    }
}
