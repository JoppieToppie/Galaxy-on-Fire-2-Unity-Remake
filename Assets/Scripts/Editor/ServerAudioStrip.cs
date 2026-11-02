// ServerAudioStrip.cs
// The Linux / Windows dedicated server builds without the game's audio (ServerBuild). Unity ships everything under Resources, and
// a few assets there hold nearly every AudioClip (StoryAssets' voices, CombatAssets, EventAudio, CombatAudio, the weapon
// fx...: 4346 of the 4418 clips, 171 MB of resources.resource in a server build); the dedicated server optimizations strip
// textures and shaders but not audio. A server plays nothing (DedicatedServer mutes the process) and reads only sound names,
// from enums (EventAudio.Names), so for the server build Strip() clears every AudioClip reference in the Resources assets
// (each file copied to Library/GoF2ServerAudioBackup first) and Restore() copies the originals back afterwards; a backup
// left by a crash is put back on the next Editor load. The scenes in the build (the main menu) lose their clips in the
// build's copy only (AssetPostprocessor.OnProcessScene).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ServerAudioStrip : AssetPostprocessor
    {
        const string BackupFolder = "Library/GoF2ServerAudioBackup";

        /// <summary>Set by ServerBuild around its BuildPlayer call: the scenes of that build lose their clips.</summary>
        public static bool Active;

        /// <summary>Clears the AudioClip references of every asset under a Resources folder (backed up first). Returns how
        /// many references were cleared.</summary>
        public static int Strip()
        {
            Restore();   // never strip on top of a backup that wasn't put back
            int cleared = 0, files = 0;
            var paths = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && p.Contains("/Resources/") && (p.EndsWith(".asset") || p.EndsWith(".prefab")))
                .ToList();
            foreach (string path in paths)
            {
                if (!ReferencesAudio(path)) continue;
                string backup = Path.Combine(BackupFolder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(path, backup, true);
                int n = 0;
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (obj == null) continue;
                    var so = new SerializedObject(obj);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is AudioClip)
                        {
                            it.objectReferenceValue = null;
                            changed = true;
                            n++;
                        }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
                if (n > 0) { files++; cleared += n; }
                else File.Delete(backup);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: server build without audio: {cleared} clip references cleared in {files} Resources assets (put back after the build).");
            return cleared;
        }

        /// <summary>Puts the backed-up assets back (after the build, or on Editor load after a crash).</summary>
        public static void Restore()
        {
            if (!Directory.Exists(BackupFolder)) return;
            var restored = new List<string>();
            foreach (string backup in Directory.GetFiles(BackupFolder, "*", SearchOption.AllDirectories))
            {
                string path = Path.GetRelativePath(BackupFolder, backup).Replace('\\', '/');
                File.Copy(backup, path, true);
                restored.Add(path);
            }
            Directory.Delete(BackupFolder, true);
            foreach (string path in restored) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (restored.Count > 0) Debug.Log($"GoF2: {restored.Count} Resources assets got their audio references back.");
        }

        [InitializeOnLoadMethod]
        static void RestoreAfterCrash()
        {
            if (Directory.Exists(BackupFolder) && !BuildPipeline.isBuildingPlayer)
                EditorApplication.delayCall += () => { if (!BuildPipeline.isBuildingPlayer && !Active) Restore(); };
        }

        /// <summary>Whether the asset references an audio file directly (its own dependencies), so only those are loaded.</summary>
        static bool ReferencesAudio(string path)
        {
            foreach (string dep in AssetDatabase.GetDependencies(path, false))
            {
                string ext = Path.GetExtension(dep).ToLowerInvariant();
                if (ext == ".ogg" || ext == ".wav" || ext == ".mp3" || ext == ".aif" || ext == ".aiff" || ext == ".flac") return true;
            }
            return false;
        }

        void OnProcessScene(Scene scene, UnityEditor.Build.Content.SceneImportContext context)
        {
            if (!Active || context.loadingReason != UnityEditor.Build.Content.ProcessSceneMode.PlayerBuild) return;
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is AudioClip)
                        {
                            it.objectReferenceValue = null;
                            changed = true;
                            n++;
                        }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
            if (n > 0) Debug.Log($"GoF2: server build: {n} clip references left out of scene {scene.name}.");
        }
    }
}
