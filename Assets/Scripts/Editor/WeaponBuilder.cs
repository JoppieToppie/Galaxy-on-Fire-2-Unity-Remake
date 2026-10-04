// WeaponBuilder.cs  (Editor only)
// Menu "GoF2/Build/Weapon Fx": turns Resources/GoF2Data/weapon_fx.json (made by
// Reference/tools/weapons/build_weapon_fx.py from Reference/research/weapons.md) into one WeaponFx asset per
// weapon item in Resources/GoF2Weapons/item_XXX: projectile, muzzle flash and impact prefabs (single-mesh prefabs,
// or the assembled prefab for rockets) and the shot sound (.ogg by name). Also makes CombatAudio (asteroid
// destroyed, target lock) and cuts the HUD crosshair (normal / hit) from Textures/textures/gof2_interface.png.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Flight;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class WeaponBuilder
    {
        const string OutDir = ImportSettings.Root + "/Resources/" + WeaponFx.ResourcesFolder;
        const string HudImageDir = ImportSettings.Root + "/UI/Flight/Images";

        [System.Serializable] class Entry { public int item; public string projectile, muzzle, impact, sound; public bool beam, soundLoops; public int soundId; }
        [System.Serializable] class Wrapper { public List<Entry> list; }

        /// <summary>The sentry guns' deploy sound: the shot table DAT_00252310 gives 2263 SentryGun_SG400 to all three
        /// (2262 Berger_SG100 / 2264 TSuum are never played).</summary>
        const int SentryDeploySound = 2263;

        /// <summary>Reference/research/fmod_event_ids.txt (from the FEV's LGCY data): system id -> the event's wave files.</summary>
        internal static Dictionary<int, List<string>> LoadEventTable()
        {
            var table = new Dictionary<int, List<string>>();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Reference/research/fmod_event_ids.txt");
            if (!File.Exists(path)) { Debug.LogWarning("GoF2: " + path + " missing, run Reference/tools/audio/build_event_table.py"); return table; }
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("#")) continue;
                var f = line.Split('	');
                if (f.Length < 5 || !int.TryParse(f[0], out int id)) continue;
                table[id] = f[4].Split('|').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("?"))
                               .Select(x => $"{ImportSettings.Root}/Audio/{x}").ToList();
            }
            return table;
        }

        /// <summary>Every wave of an event (a random / sequential sound definition picks among them at runtime).</summary>
        internal static AudioClip[] EventClips(Dictionary<int, List<string>> table, int id) =>
            table.TryGetValue(id, out var files) ? files.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c != null).ToArray()
                                                 : new AudioClip[0];

        /// <summary>The event holding this clip (its first), all of its waves; the clip alone when none does.</summary>
        static AudioClip[] EventClipsOf(Dictionary<int, List<string>> table, AudioClip clip)
        {
            if (clip == null) return new AudioClip[0];
            string path = AssetDatabase.GetAssetPath(clip);
            foreach (var kv in table.OrderBy(k => k.Key))
                if (kv.Value.Contains(path)) return EventClips(table, kv.Key);
            return new[] { clip };
        }

        /// <summary>Explosion::playSound by weapon and the BombGun / ObjectGun explosion types (weapons_special.md 3.4).</summary>
        static readonly Dictionary<int, (int type, string sound)> Explosions = new Dictionary<int, (int, string)>
        {
            { 41, (7, "Explosion_EMP_GL1") }, { 42, (7, "Explosion_EMP_GL2") }, { 43, (7, "Explosion_EMP_GLDX") },
            { 44, (0, "Explosion_Bomb_AMR_Tormentor") }, { 45, (0, "Explosion_Bomb_AMR_Oppressor") },
            { 46, (0, "Explosion_Bomb_AMR_Exctinctor") }, { 179, (0, "Explosion_Bomb_AMR_Exctinctor") },
            { 60, (0, "Garbage_Explosion") }, { 61, (0, "Garbage_Explosion") }, { 62, (0, "Garbage_Explosion") },
            { 176, (8, "Garbage_Explosion") }, { 177, (9, "Garbage_Explosion") }, { 178, (10, "Garbage_Explosion") },
            { 197, (0, "Plasma_Rocket_Explosion") }, { 221, (0, "Plasma_Rocket_Explosion") },
            { 226, (11, null) }, { 232, (13, "Fireworks") },
        };

        /// <summary>Turret items -> their assembled ship-mounted prefab; sentry items -> the deployed object.</summary>
        static readonly Dictionary<int, string> Turrets = new Dictionary<int, string>
        {
            { 47, "turret_001_ship_mounted" }, { 48, "turret_002_ship_mounted" }, { 49, "turret_003_ship_mounted" },
            { 180, "v_autoturret_001_anim_ship_mounted" }, { 181, "v_autoturret_002_ship_mounted" },
            { 182, "v_autoturret_003_ship_mounted" }, { 224, "sn_turret_004_ship_mounted" },
            // The plasma collectors (sort 35): the same pivot / base / gun build, the plasma stream under the gun.
            { 198, "sn_plasma_collector_001_ship_mounted" }, { 199, "sn_plasma_collector_002_ship_mounted" },
            { 200, "sn_plasma_collector_003_ship_mounted" },
        };

        [MenuItem("GoF2/Build/Weapon Fx", priority = 201)]
        public static void Build()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{ImportSettings.Root}/Resources/GoF2Data/weapon_fx.json");
            if (json == null) { Debug.LogError("GoF2: run Reference/tools/weapons/build_weapon_fx.py first"); return; }
            var entries = JsonUtility.FromJson<Wrapper>("{\"list\":" + json.text + "}").list;
            Directory.CreateDirectory(OutDir);
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { ImportSettings.Root + "/Audio" })
                .Select(AssetDatabase.GUIDToAssetPath).ToList();
            var events = LoadEventTable();
            int made = 0;
            // Sentry guns aren't in the projectile tables: they deploy an object that fires the look of items 2 / 20 / 14.
            foreach (int s in new[] { 211, 212, 213 })
                if (!entries.Exists(x => x.item == s)) entries.Add(new Entry { item = s });
            // Nor are the plasma collectors (no projectile): only their mounted model.
            foreach (int s in new[] { 198, 199, 200 })
                if (!entries.Exists(x => x.item == s)) entries.Add(new Entry { item = s });
            foreach (var e in entries)
            {
                string path = $"{OutDir}/item_{e.item:000}.asset";
                var fx = AssetDatabase.LoadAssetAtPath<WeaponFx>(path);
                if (fx == null) { fx = ScriptableObject.CreateInstance<WeaponFx>(); AssetDatabase.CreateAsset(fx, path); }
                fx.item = e.item;
                fx.projectile = FindPrefab(e.projectile);
                fx.muzzleFlash = FindPrefab(e.muzzle);
                fx.impact = FindPrefab(e.impact);
                // Player::playShootSound: the shot table's event id (build_weapon_fx.py), all its waves.
                int soundId = e.item >= 211 && e.item <= 213 ? SentryDeploySound : e.soundId;
                fx.shots = soundId > 0 ? EventClips(events, soundId) : new AudioClip[0];
                if (fx.shots.Length == 0 && !string.IsNullOrEmpty(e.sound)) fx.shots = EventClipsOf(events, FindClip(clips, e.sound));
                fx.shot = fx.shots.Length > 0 ? fx.shots[0] : null;
                fx.shotLoops = e.soundLoops;
                if (Explosions.TryGetValue(e.item, out var ex))
                {
                    fx.explosionType = ex.type;
                    fx.explosionSounds = EventClipsOf(events, FindClip(clips, ex.sound));
                    fx.explosionSound = fx.explosionSounds.Length > 0 ? fx.explosionSounds[0] : null;
                }
                else { fx.explosionType = -1; fx.explosionSound = null; fx.explosionSounds = new AudioClip[0]; }
                // 1116 AMR_Liberator_Engine_02: layer 1 (the Liberator engine, pitched) and layer 0 (EngineDLC_06).
                var liberatorEngine = e.item == 179 ? EventClips(events, 1116) : new AudioClip[0];
                fx.engineLoop = liberatorEngine.FirstOrDefault(c => c.name.StartsWith("AMR_Liberator_Engine"));
                fx.engineLoopExtra = liberatorEngine.FirstOrDefault(c => c.name.StartsWith("EngineDLC"));
                fx.turretMounted = Turrets.TryGetValue(e.item, out var turret) ? FindPrefab(turret) : null;
                fx.sentry = e.item >= 211 && e.item <= 213 ? FindPrefab($"sn_sentry_gun_00{e.item - 210}") : null;
                EditorUtility.SetDirty(fx);
                made++;
            }

            string audioPath = $"{OutDir}/CombatAudio.asset";
            var audio = AssetDatabase.LoadAssetAtPath<CombatAudio>(audioPath);
            if (audio == null) { audio = ScriptableObject.CreateInstance<CombatAudio>(); AssetDatabase.CreateAsset(audio, audioPath); }
            audio.asteroidDestroyed = FindClip(clips, "Destruction_Asteroid");
            audio.targetLock = FindClip(clips, "Target_Lock");
            audio.miningDrill = FindClip(clips, "Mining_Drill_Add_1");
            audio.miningDrillSlow = FindClip(clips, "Mining_Drill_Slow_1");
            audio.miningDrillAdd2 = FindClip(clips, "Mining_Drill_Add_2");
            audio.miningDrillSwitch = FindClip(clips, "Mining_Drill_Switch");
            audio.playerEngines = new[] { 42, 43, 44, 45, 1104, 1106, 1107 }.Select(id => EventClips(events, id).FirstOrDefault()).ToArray();
            audio.playerEngineExtras = new[] { 42, 43, 44, 45, 1104, 1106, 1107 }.Select(id => EventClips(events, id).Skip(1).FirstOrDefault()).ToArray();
            audio.buttonPush = EventClips(events, 124).FirstOrDefault();
            audio.buttonRelease = EventClips(events, 123).FirstOrDefault();
            audio.messageInfo = EventClips(events, 126).FirstOrDefault();
            audio.buttonInfo = EventClips(events, 97).FirstOrDefault();
            audio.boosters = new[] { 38, 39, 40, 41, 1102 }.Select(id => EventClips(events, id).FirstOrDefault()).ToArray();
            audio.miningLanding = FindClip(clips, "Mining_Landing");
            audio.miningDrillBroken = FindClip(clips, "Mining_Drill_Broken");
            audio.autopilotOn = FindClip(clips, "Autopilot_Activate");
            audio.autopilotOff = FindClip(clips, "Autopilot_Deactivate");
            audio.jumpToPlanet = FindClip(clips, "Jump_to_planets");
            EditorUtility.SetDirty(audio);

            BuildCrosshair();
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: {made} weapon fx assets in {OutDir}.");
        }

        /// <summary>Rockets/bombs are assembled objects (with their flame child); everything else a single-mesh prefab.</summary>
        static GameObject FindPrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            GameObject single = null, assembled = null;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (p.Contains("/Resources/Assembled/")) assembled = go; else single = go;
            }
            var result = assembled != null ? assembled : single;
            if (result == null) Debug.LogWarning($"GoF2: weapon fx prefab '{name}' not found");
            return result;
        }

        /// <summary>Exact file name first, then the first file that starts with it (e.g. Laser_Nirai_Charged_Impulse_v01).</summary>
        static AudioClip FindClip(List<string> clips, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string lower = name.ToLowerInvariant();
            string path = clips.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant() == lower)
                          ?? clips.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().StartsWith(lower));
            return path != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(path) : null;
        }

        /// <summary>Crosshair 0x4c0 and the orange "hit" one 0x4ce. The HD build draws them from gof2_interface_iphone4.png,
        /// 81x81 at (392, 78) and (1835, 1231) (ship_combat.md 7); weapons.md 9's 40x40 rects (339, 814 / 856) are the
        /// low-resolution gof2_interface.png's.</summary>
        static void BuildCrosshair()
        {
            string src = $"{ImportSettings.Root}/Textures/textures/gof2_interface_iphone4.png";
            if (!File.Exists(src)) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(src));
            Directory.CreateDirectory(HudImageDir);
            Cut(tex, 392, 78, 81, 81, $"{HudImageDir}/crosshair.png");
            Cut(tex, 1835, 1231, 81, 81, $"{HudImageDir}/crosshair_hit.png");
            Object.DestroyImmediate(tex);
        }

        static void Cut(Texture2D tex, int x, int y, int w, int h, string path)
        {
            var px = tex.GetPixels(x, tex.height - y - h, w, h);   // rects are top-left based
            var o = new Texture2D(w, h, TextureFormat.RGBA32, false);
            o.SetPixels(px);
            o.Apply();
            File.WriteAllBytes(path, o.EncodeToPNG());
            Object.DestroyImmediate(o);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;   // one sprite over the whole image (an old 'Multiple' rect clipped it)
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;   // block compression smeared the thin rings
            ti.SaveAndReimport();
        }
    }
}
