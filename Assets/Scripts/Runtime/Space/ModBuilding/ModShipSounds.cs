// ModShipSounds.cs
// Remake mods: a ship's own engine and boost sounds and a kit's weapon sounds (EVERSPACE 2's per ship type and weapon,
// written by the ES2 importer). Unlike ModSounds (sounds/<game clip name>: a replacement everywhere) these belong to one ship
// or one weapon model, so their files may sit anywhere in the mod (the importer uses audio/).
//   ships.json, a new ship or an override:
//     "sounds": { "engine": S, "boostStart": S, "boostLoop": S, "boostStop": S, "boostDepleted": S, "charger": S }
//       engine      the engine loop (PlayerEngine; also the ship's NPCs, hangar flights and other players' copies): volume
//                   and pitch go from their first value at a standstill to their second at top speed
//       boostStart  one shot when a boost starts; boostStop when it ends ("pitch" [a, b]: a random pitch in between);
//       boostDepleted one shot when a free-flight boost dies of an empty pool, beside boostStop (EVERSPACE 2's
//                   TryToBoostWithEnergyDepletedSound)
//       boostLoop   a loop while boosting: volume / pitch from the boost's start to its full strength (BoostVisualPercent)
//       charger     a loop while the booster recharges: volume / pitch from empty to full
//     A ship without "sounds" (or without one of them) keeps the game's.
//   shipkits.json, a hardpoint weapon rule (ModHardpoints):
//     "sound": { "shot": S, "loop": S, "release": ms }
//       shot        one per shot ("file" lists: one at random each shot; "pitch" [a, b] a random pitch); with a loop, played
//                   once as the firing starts (a beam's ignition)
//       loop        a loop while the trigger is held (beams), faded out over "release" ms (default 100) when let go
//     The player's guns and turret on that kit's ships, and other players' mirrored shots, take the rule's sound in place of
//     the weapon's own.
//   S = "file" | [ "file", ... ] | { "file": "..." | "files": [ ... ], "volume": v | [a, b], "pitch": p | [a, b] }
//   A file missing under its own name is taken in another of the audio formats of the same name (Resolve).
//   Volumes are the AudioSource's (x the FX volume; the game's engine loops play at about 0.27, its shots at 0.7).
// Loaded decoded with the rest of the mods (Preload, ModLoading's loading screen).

using System;
using System.Linq;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModShipSounds
    {
        /// <summary>One sound: its files (one at random), volume and pitch (each a value, or a range: along the sound's
        /// driver for a loop, a random pick for a one-shot).</summary>
        public sealed class Sound
        {
            public ModInfo mod;
            public readonly List<string> files = new List<string>();
            public float vol0 = 1f, vol1 = 1f, pitch0 = 1f, pitch1 = 1f;

            /// <summary>One of its clips at random (null while none is loaded).</summary>
            public AudioClip Clip
            {
                get
                {
                    if (files.Count == 1) return ClipOf(mod, files[0]);
                    int start = UnityEngine.Random.Range(0, files.Count);
                    for (int i = 0; i < files.Count; i++)
                        if (ClipOf(mod, files[(start + i) % files.Count]) is AudioClip c) return c;
                    return null;
                }
            }

            public float Volume(float t) => Mathf.Lerp(vol0, vol1, Mathf.Clamp01(t));
            public float Pitch(float t) => Mathf.Lerp(pitch0, pitch1, Mathf.Clamp01(t));
            public float RandomPitch => UnityEngine.Random.Range(Mathf.Min(pitch0, pitch1), Mathf.Max(pitch0, pitch1));
        }

        public sealed class ShipSounds
        {
            public Sound engine, boostStart, boostLoop, boostStop, boostDepleted, charger;
        }

        public sealed class WeaponSound
        {
            public Sound shot, loop;
            public float releaseMs = 100f;
        }

        static readonly HashSet<string> ShipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "engine", "boostStart", "boostLoop", "boostStop", "boostDepleted", "charger" };
        static readonly HashSet<string> WeaponKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shot", "loop", "release" };
        static readonly HashSet<string> SoundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "file", "files", "volume", "pitch" };

        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<int, ShipSounds> ships = new Dictionary<int, ShipSounds>();
        static int revision = -1, loading, count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { clips.Clear(); ships = new Dictionary<int, ShipSounds>(); revision = -1; loading = 0; count = 0; }

        public static int Count { get { Preload(); return count; } }
        public static int Loading => loading;
        public static bool Ready { get { Preload(); return loading == 0; } }

        static string Key(ModInfo mod, string file) => mod.Id + "/" + file;

        static AudioClip ClipOf(ModInfo mod, string file)
        {
            Preload();
            return clips.TryGetValue(Key(mod, file), out var c) ? c : null;
        }

        // ---- parsing (when a mod is scanned: a wrong entry shows in the browser) ----------------------------------------

        static Sound ParseSound(JToken t, ModInfo mod, string file, string where)
        {
            var s = new Sound { mod = mod };
            if (t.Type == JTokenType.String) s.files.Add((string)t);
            else if (t is JArray a) foreach (var f in a) s.files.Add((string)f);
            else if (t is JObject o)
            {
                foreach (var prop in o.Properties())
                    if (!SoundKeys.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                if (ModJson.Str(o, "file") is string one) s.files.Add(one);
                s.files.AddRange(ModJson.Strings(o, "files"));
                Range(o, "volume", ref s.vol0, ref s.vol1, where);
                Range(o, "pitch", ref s.pitch0, ref s.pitch1, where);
            }
            else throw new ModJsonException($"{where}: a sound is a file name, a list of them or {{ \"file\", \"volume\", \"pitch\" }}");
            if (s.files.Count == 0) throw new ModJsonException($"{where}: a sound needs a file");
            for (int i = 0; i < s.files.Count; i++)
            {
                string f = s.files[i];
                if (string.IsNullOrEmpty(f) || ModAudio.TypeOf(f) == AudioType.UNKNOWN) throw new ModJsonException($"{where}: \"{f}\" isn't an .ogg, .wav or .mp3 file");
                s.files[i] = Resolve(mod, f) ?? throw new ModJsonException($"{where}: the sound \"{f}\" isn't in the mod");
            }
            return s;
        }

        /// <summary>The file a sound names, or another audio file of the same name ("audio/engine.ogg" found as engine.wav): an
        /// importer that couldn't encode Ogg Vorbis on the player's computer keeps the .wav, and the JSON (which multiplayer
        /// compares) stays the same. Null when neither is in the mod.</summary>
        static string Resolve(ModInfo mod, string file)
        {
            if (mod.Source.Exists(file)) return file;
            string stem = file.Substring(0, file.Length - System.IO.Path.GetExtension(file).Length);
            foreach (var ext in ModAudio.Extensions)
                if (mod.Source.Exists(stem + ext)) return stem + ext;
            return null;
        }

        static void Range(JObject o, string key, ref float a, ref float b, string where)
        {
            var t = ModJson.Get(o, key);
            if (t == null) return;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) { a = b = (float)t; return; }
            if (t is JArray r && r.Count == 2 && r[0].Type != JTokenType.String && r[1].Type != JTokenType.String) { a = (float)r[0]; b = (float)r[1]; return; }
            throw new ModJsonException($"{where}: \"{key}\" must be a number or [from, to]");
        }

        /// <summary>A ships.json entry's "sounds" (ModContent.ParseShips), null without one.</summary>
        public static ShipSounds ParseShip(JObject entry, ModInfo mod, string file)
        {
            if (!(ModJson.Get(entry, "sounds") is JToken t)) return null;
            string where = ModJson.Where(t, file);
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"sounds\" must be an object {{ \"engine\": ..., \"boostStart\": ... }}");
            var s = new ShipSounds();
            foreach (var prop in o.Properties())
            {
                if (!ShipKeys.Contains(prop.Name)) { mod.Warnings.Add($"{ModJson.Where(prop, file)}: unknown sound \"{prop.Name}\" (ignored)"); continue; }
                if (prop.Value.Type == JTokenType.Null) continue;
                var snd = ParseSound(prop.Value, mod, file, ModJson.Where(prop, file));
                switch (prop.Name.ToLowerInvariant())
                {
                    case "engine": s.engine = snd; break;
                    case "booststart": s.boostStart = snd; break;
                    case "boostloop": s.boostLoop = snd; break;
                    case "booststop": s.boostStop = snd; break;
                    case "boostdepleted": s.boostDepleted = snd; break;
                    case "charger": s.charger = snd; break;
                }
            }
            return s;
        }

        /// <summary>A hardpoint weapon rule's "sound" (ModHardpoints.Read), null without one.</summary>
        internal static WeaponSound ParseWeapon(JObject rule, ModInfo mod, string file)
        {
            if (!(ModJson.Get(rule, "sound") is JToken t)) return null;
            string where = ModJson.Where(t, file);
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"sound\" must be an object {{ \"shot\": ..., \"loop\": ... }}");
            foreach (var prop in o.Properties())
                if (!WeaponKeys.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
            var w = new WeaponSound();
            if (ModJson.Get(o, "shot") is JToken s) w.shot = ParseSound(s, mod, file, ModJson.Where(s, file));
            if (ModJson.Get(o, "loop") is JToken l) w.loop = ParseSound(l, mod, file, ModJson.Where(l, file));
            w.releaseMs = Mathf.Max(0f, ModJson.Float(o, "release", 100f, file));
            return w.shot == null && w.loop == null ? null : w;
        }

        // ---- loading ----------------------------------------------------------------------------------------------------

        /// <summary>Loads every sound the mods that are on name, again when the mods changed (no-op otherwise).</summary>
        public static void Preload()
        {
            if (revision == ModManager.Revision) return;
            revision = ModManager.Revision;
            ModAudio.Sweep();   // the old revision's clips of changed files go
            clips.Clear();
            loading = 0;
            ships = ModContent.ShipSounds().ToDictionary(kv => kv.Key, kv => (ShipSounds)kv.Value);
            var wanted = new Dictionary<string, (ModInfo mod, string file)>(StringComparer.OrdinalIgnoreCase);
            void Want(Sound s) { if (s != null) foreach (var f in s.files) wanted[Key(s.mod, f)] = (s.mod, f); }
            foreach (var s in ships.Values) { Want(s.engine); Want(s.boostStart); Want(s.boostLoop); Want(s.boostStop); Want(s.boostDepleted); Want(s.charger); }
            foreach (var kit in ModShipKits.All())
                if (kit.hardpoints != null)
                    foreach (var r in kit.hardpoints.weapons) { Want(r.sound?.shot); Want(r.sound?.loop); }
            count = wanted.Count;
            int started = revision;
            foreach (var kv in wanted)
            {
                string key = kv.Key;
                loading++;
                ModAudio.Load(kv.Value.mod, kv.Value.file, clip =>
                {
                    if (started != revision) return;
                    loading--;
                    if (clip != null) clips[key] = clip;
                }, false);
            }
            if (count > 0) Debug.Log($"Mods: {count} ship / weapon sound(s) from the mods");
        }

        /// <summary>A ship's own sounds (ships.json "sounds"), null = the game's.</summary>
        public static ShipSounds Ship(int ship)
        {
            Preload();
            return ships.TryGetValue(ship, out var s) ? s : null;
        }

        /// <summary>The engine loop of a ship with its own (null = the game's), and its volume at 'level' of its top speed.</summary>
        public static AudioClip EngineClip(int ship, float level, out float volume)
        {
            var e = Ship(ship)?.engine;
            var clip = e?.Clip;
            volume = clip != null ? e.Volume(level) : 0f;
            return clip;
        }

        /// <summary>The sound of a weapon item on a kit ship's hardpoint (ModHardpoints' rule for its slot), null = its own.</summary>
        public static WeaponSound Weapon(int ship, ItemData item)
        {
            if (item == null) return null;
            Preload();
            if (count == 0) return null;
            return ModHardpoints.SoundOf(ship, item);
        }
    }
}
