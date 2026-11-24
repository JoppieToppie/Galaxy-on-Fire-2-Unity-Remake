// OriginalRefs.cs
// Feature: docs/notes/player-assets.md "The content layer": the remake's own assets that pointed at the original's (the sound
// tables, the language table, the station and menu scenes' sounds) get those references back from the player's converted
// copy. Resources/GoF2Content/original_refs.json (Reference/tools/player_assets/original_refs.py, made from the project with
// the originals) records each such field by its serialized path ("voiceClipsEng[12]", "sounds[3].clips[0]") and the
// original's project path; a build without the originals has those fields empty (the list lengths kept), and Fill sets them:
// a table when it loads (Load), a scene's components when the scene opens (sceneLoaded). Only empty fields are filled, so a
// build that still has the originals is left as it is.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalRefs
    {
        public const string ManifestPath = "GoF2Content/original_refs";

        static Dictionary<string, Dictionary<string, string>> map;
        static readonly HashSet<UnityEngine.Object> filled = new HashSet<UnityEngine.Object>();
        static bool hooked, sceneLoaded;

        /// <summary>Prefab references met before the game's first scene loaded (Bootstrap's BeforeSceneLoad installers load
        /// tables): set once it has. A template built before lands in another Entities World than the scenes', and every
        /// model later parented under the same holder (OriginalAssemblies) logged "A transform hierarchy holds entities of
        /// more than one Entities World".</summary>
        static readonly List<(UnityEngine.Object target, string path, string asset)> beforeScene = new List<(UnityEngine.Object, string, string)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { map = null; filled.Clear(); lazy.Clear(); beforeScene.Clear(); hooked = sceneLoaded = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            SceneManager.sceneLoaded += (scene, _) =>
            {
                // (the test runner's own first scene doesn't count: its Entities World isn't the game's scenes')
                if (!sceneLoaded && !scene.name.StartsWith("InitTestScene", StringComparison.Ordinal))
                {
                    sceneLoaded = true;
                    foreach (var (target, path, asset) in beforeScene) if (target != null) Set(target, path, asset);
                    beforeScene.Clear();
                }
                FillScene(scene);
            };
        }

        static Dictionary<string, Dictionary<string, string>> Map
        {
            get
            {
                if (map != null) return map;
                map = new Dictionary<string, Dictionary<string, string>>();
                var ta = Resources.Load<TextAsset>(ManifestPath);
                if (ta == null) return map;
                // { "key": { "path": "asset", ... }, ... }: two levels of plain strings
                foreach (Match e in Regex.Matches(ta.text, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\\{(.*?)\\}", RegexOptions.Singleline))
                {
                    var fields = new Dictionary<string, string>();
                    foreach (Match f in Regex.Matches(e.Groups[2].Value, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                        fields[Regex.Unescape(f.Groups[1].Value)] = Regex.Unescape(f.Groups[2].Value);
                    map[Regex.Unescape(e.Groups[1].Value)] = fields;
                }
                Resources.UnloadAsset(ta);
                return map;
            }
        }

        /// <summary>Resources.Load with the asset's references to the originals filled from the converted copy.</summary>
        public static T Load<T>(string resourcesPath) where T : UnityEngine.Object
        {
            using var timing = ContentTimings.Measure("refs table");
            var a = Resources.Load<T>(resourcesPath);
            if (a != null) Fill(a, "Resources/" + resourcesPath);
            return a;
        }

        /// <summary>A list with more entries than this in one table is filled entry by entry when asked (Entry): the story's
        /// 4,300 voice lines decoded at once took ~20 s.</summary>
        public const int LazyListSize = 64;

        static readonly Dictionary<(UnityEngine.Object, string), string> lazy = new Dictionary<(UnityEngine.Object, string), string>();

        /// <summary>Fills the empty fields of 'target' its manifest entry names (once per object); a long list's entries wait
        /// for Entry.</summary>
        public static void Fill(UnityEngine.Object target, string key)
        {
            if (target == null || !GameContent.Installed || !filled.Add(target)) return;
            if (!Map.TryGetValue(key, out var fields)) return;
            var sizes = new Dictionary<string, int>();
            foreach (var path in fields.Keys) { string head = ListHead(path); if (head != null) sizes[head] = sizes.TryGetValue(head, out var n) ? n + 1 : 1; }
            foreach (var kv in fields)
            {
                string head = ListHead(kv.Key);
                if (head != null && sizes[head] > LazyListSize && !kv.Key.Contains(".")) lazy[(target, kv.Key)] = kv.Value;
                else if (Application.isPlaying && !sceneLoaded && kv.Value.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    beforeScene.Add((target, kv.Key, kv.Value));   // (built once a scene has loaded)
                else Set(target, kv.Key, kv.Value);
            }
        }

        /// <summary>A path's list field when the path is "field[i]..." (null otherwise).</summary>
        static string ListHead(string path)
        {
            int b = path.IndexOf('[');
            return b > 0 && (path.IndexOf('.') < 0 || path.IndexOf('.') > b) ? path.Substring(0, b) : null;
        }

        /// <summary>Entry 'i' of a long list ('field' of 'target'), filled from the converted copy the first time it is asked
        /// for when it is empty.</summary>
        /// <summary>Whether Entry has an asset for list[i]: the build's own, or the converted copy's (not loaded yet).</summary>
        public static bool Has<T>(UnityEngine.Object target, string field, IList<T> list, int i) where T : UnityEngine.Object =>
            list != null && i >= 0 && i < list.Count && (list[i] != null || lazy.ContainsKey((target, $"{field}[{i}]")));

        public static T Entry<T>(UnityEngine.Object target, string field, IList<T> list, int i) where T : UnityEngine.Object
        {
            if (list == null || i < 0 || i >= list.Count) return null;
            var v = list[i];
            if (v != null) return v;
            if (lazy.TryGetValue((target, $"{field}[{i}]"), out var asset))
            {
                v = Resolve(typeof(T), asset) as T;
                if (v != null) list[i] = v;
                lazy.Remove((target, $"{field}[{i}]"));
            }
            return v;
        }

        static void FillScene(Scene scene)
        {
            if (!GameContent.Installed) return;
            using var timing = ContentTimings.Measure("refs scene fill");
            string prefix = "Scenes/" + scene.name + "|";
            Dictionary<(string type, string go), List<MonoBehaviour>> byName = null;   // (the scene's components, gathered once)
            foreach (var kv in Map)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var parts = kv.Key.Substring(prefix.Length).Split('|');
                if (parts.Length < 2) continue;
                if (byName == null)
                {
                    byName = new Dictionary<(string, string), List<MonoBehaviour>>();
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            if (c == null) continue;
                            var k = (c.GetType().Name, c.gameObject.name);
                            if (!byName.TryGetValue(k, out var l)) byName[k] = l = new List<MonoBehaviour>();
                            l.Add(c);
                        }
                }
                if (byName.TryGetValue((parts[0], parts[1].Split('#')[0]), out var found))
                    foreach (var c in found) Fill(c, kv.Key);
            }
        }

        /// <summary>The original by its project path as the field's type wants it.</summary>
        static object Resolve(Type type, string asset)
        {
            if (typeof(AudioClip).IsAssignableFrom(type)) return OriginalAudio.Get(asset);
            if (typeof(Texture).IsAssignableFrom(type))
            {
                // the interface images the converter cut (Resources/GoF2Hud, GoF2Icons) and the story's portrait parts (imported
                // as sprites: no mipmaps, clamped) as interface images; the rest as the game's textures
                if (asset.StartsWith("Resources/GoF2Hud/") || asset.StartsWith("Resources/GoF2Icons/"))
                    return OriginalTextures.Image(asset.Substring("Resources/".Length, asset.Length - "Resources/".Length - 4));
                if (asset.StartsWith("Textures/textures/") && asset.EndsWith("_ipad_large.png")) return OriginalTextures.UiImage(asset);
                if (asset.StartsWith("Textures/")) return OriginalTextures.Get(asset);
            }
            if (typeof(TextAsset).IsAssignableFrom(type) && asset.StartsWith("Localization/text_"))
            {
                string code = asset.Substring("Localization/text_".Length).Replace(".json", "");
                var text = GameContent.LanguageText(code);
                return text != null ? new TextAsset(text) { name = "text_" + code } : null;
            }
            // the materials and prefabs (OriginalContent, GoF2.Space)
            return GameHooks.ResolveOriginal?.Invoke(type, asset);
        }

        /// <summary>The original's asset by its project path, as the content layer makes it from the converted copy (null
        /// without it): for code that loaded an original directly.</summary>
        public static T Find<T>(string projectPath) where T : UnityEngine.Object => GameContent.Installed ? Resolve(typeof(T), projectPath) as T : null;

        static readonly Regex Step = new Regex(@"^([A-Za-z_][A-Za-z0-9_]*)(?:\[(\d+)\])?$");

        /// <summary>Sets the field at 'path' ("a[3].b[0]") of 'obj' when it is empty.</summary>
        static void Set(object obj, string path, string asset)
        {
            try { SetPath(obj, path.Split('.'), 0, asset); }
            catch (Exception e) when (e is ArgumentException || e is InvalidCastException || e is IndexOutOfRangeException || e is TargetException)
            {
                Debug.LogWarning($"OriginalRefs: {path}: {e.Message}");
            }
        }

        /// <summary>Returns the (possibly replaced) value of 'obj' after setting below it: a struct comes back changed.</summary>
        static object SetPath(object obj, string[] steps, int i, string asset)
        {
            var m = Step.Match(steps[i]);
            if (!m.Success || obj == null) return obj;
            var f = FindField(obj.GetType(), m.Groups[1].Value);
            if (f == null) return obj;
            bool last = i == steps.Length - 1;
            object value = f.GetValue(obj);
            if (m.Groups[2].Success)
            {
                int at = int.Parse(m.Groups[2].Value);
                if (!(value is IList list) || at >= list.Count) return obj;
                var elemType = f.FieldType.IsArray ? f.FieldType.GetElementType() : f.FieldType.IsGenericType ? f.FieldType.GetGenericArguments()[0] : typeof(object);
                if (last)
                {
                    if (IsEmpty(list[at])) { var v = Resolve(elemType, asset); if (v != null) list[at] = v; }
                }
                else list[at] = SetPath(list[at], steps, i + 1, asset);
            }
            else if (last)
            {
                if (IsEmpty(value)) { var v = Resolve(f.FieldType, asset); if (v != null) f.SetValue(obj, v); }
            }
            else f.SetValue(obj, SetPath(value, steps, i + 1, asset));
            return obj;
        }

        static bool IsEmpty(object v) => v == null || (v is UnityEngine.Object o && o == null);

        static FieldInfo FindField(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }
    }
}
