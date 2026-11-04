// ModJumpFx.cs
// Modified (remake mods): a jump drive's own jump effect and rules. A mod item based on the Khador Drive (85) works as one
// (GalaxyMap.IsKhadorDrive) and may carry, in items.json:
//   "jumpFx": { "data": "fx/hyperdrive_3d.json", "texture": "fx/hyper_drive.png", "scale": 0.5, "glow": 2.5,
//               "meetMs": 2300, "meetDepth": 8200, "sound": "gof2:timeJump", "endHoldMs": 2000, "arrivalFx": true,
//               "startDelayMs": 2000, "chargeVoice": "RADIO_0_12", "cellsFactor": 2, "noVoid": true,
//               "failChance": 0.03, "failMax": 0.5, "failNoRoute": 0.25, "failTexture": "fx/hyper_drive_purple.png" }
// The rules are the drive's while no Khador Drive (85, or a ship's own) is aboard (ActiveDrive): with one, it is the one that
// jumps. "cellsFactor": x the Khador Drive's energy cells (after Extreme's x2); "freeJumps": true, none at all. "noVoid": no
// jump into the Void, and none out of it unless the Debug panel's free jumps are on. "startDelayMs": a jump asked for waits
// this long before it charges (not on top of the level's first 5 s; the Khador Drive's own is 2000). "chargeVoice": a story
// voice line (StoryAssets.Voice) as the charge starts. "endHoldMs": the scene holds this long after the effect, the camera
// drifting on, before the next area loads. "arrivalFx": true, the ship comes into the next orbit with the prologue's 2D
// hyper_drive effect in the drive's texture (hidden and held still until 2500 ms; the arrival camera waits for the effect).
// While the drive's effect hides the ship (on leaving, from the meeting point on; on arriving, until it shows) enemy shots
// fly through it and nothing targets it.
// Failures: "failChance" per gate jump of the route (0.03 = 3 % for a neighbour, 6 % two systems away...; x2 on Extreme, as the
// cells), at most "failMax"; "failNoRoute" from or to a system without jumpgate routes. A failed jump (SystemJump): when the
// charge ends, the prologue's hyperdrive failure (its explosion, rumble and broken engine, the camera rumbling from
// "failRumbleFromMs" and growing over 4 s up to "failRumble", 1 = the prologue's) for "failMs", the ship flying at
// "failHandling" of its speed and turning; then the jump with "failTexture" to a random station of a system on the map, the
// ship coming out anywhere within "failRadiusKm" (default 20, as the HUD counts) of the station, clear of the station, the
// gate and the asteroids. "failVoice": a story voice line "failVoiceMs" into the failure, at "failVoiceChance" (0..1,
// default 1) per failure (the prologue pilot's "Hey, what's happening?" is RADIO_0_15); with "failLine" (a text number,
// 1689 for that one) it comes in the radio box with "failSpeaker"'s face and name (a story speaker, 0 = Keith).
// "data": the effect's parts (made by the Blender tools of the hyper drive conversion): { "length_ms", "objects": [ { "name",
// "verts", "faces", "uvs" (per face corner), "keys" { "posX".."sclZ", "extra" (opacity 0..1): [[ms, value], ...] }, "base"
// { "parent_loc", "parent_scale", "parent_rotY" }, "billboard" } ] }, in Blender's frame (Z up, the viewer on +Y), game
// units. The ship flies into it along -Y; it reaches the point (0, -meetDepth, 0) at meetMs and vanishes there, and the
// level changes when the effect has played (SystemJump, World.ModJumpEffect). The camera is the Khador Drive's. "sound":
// "gof2:<name>", a sound of the game's story assets (StoryAssets: timeJump = 160 SpaceTimeJump, the prologue's time jump)
// in place of the Khador jump's (none = the Khador Drive's).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModJumpFx
    {
        public sealed class Part
        {
            public string name;
            public Vector3[] verts;            // per face corner (Blender frame, game units)
            public Vector2[] uv;
            public int[] tris;
            public readonly Dictionary<string, (float[] t, float[] v)> keys = new Dictionary<string, (float[], float[])>();
            public Vector3 baseLoc;
            public float baseScale = 1f, baseRotY;
            public bool hasBase, billboard;

            /// <summary>A channel's value at 'ms' (linear between the keys, held past the ends), 'fallback' without keys.</summary>
            public float Eval(string channel, float ms, float fallback)
            {
                if (!keys.TryGetValue(channel, out var k) || k.t.Length == 0) return fallback;
                if (ms <= k.t[0]) return k.v[0];
                for (int i = 1; i < k.t.Length; i++)
                    if (ms <= k.t[i])
                    {
                        float span = k.t[i] - k.t[i - 1];
                        return span > 0f ? Mathf.Lerp(k.v[i - 1], k.v[i], (ms - k.t[i - 1]) / span) : k.v[i];
                    }
                return k.v[k.v.Length - 1];
            }
        }

        public sealed class Spec
        {
            public ModInfo mod;
            public int item;   // the drive's item number
            public string data, texture, where, sound, chargeVoice;
            public float endHoldMs, startDelayMs;
            public bool freeJumps, noVoid, arrivalFx;
            public float cellsFactor = 1f;
            public float failChance, failMax = 1f, failNoRoute, failMs = 16500f, failRumbleFromMs = 5500f, failRumble = 1f;
            public float failHandling = 1f, failRadiusKm = 20f;
            public string failTexture;
            public string failVoice;                               // Modified: a story voice line as a failure starts
            public float failVoiceChance = 1f, failVoiceMs;
            public int failLine = -1, failSpeaker;                // Modified: the line's text in the radio box, and its face
            public float scale = 0.5f, glow = 2.5f, meetMs = 2300f, meetDepth = 8200f;
            public float lengthMs;
            public List<Part> parts;
        }

        static readonly Dictionary<int, Spec> cache = new Dictionary<int, Spec>();
        static int revision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); revision = -1; }

        /// <summary>The jump effect of the first mounted jump drive that has one, null = the Khador Drive's own.</summary>
        public static Spec Mounted()
        {
            foreach (var e in Session.Equipment)
            {
                var s = For(e.item);
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>The mod drive that jumps: the first mounted one with a "jumpFx", unless the Khador Drive itself (85) or a ship
        /// with the drive built in is aboard (then null: the Khador Drive's rules and effect).</summary>
        public static Spec ActiveDrive()
        {
            if (GalaxyMap.HasIntegratedDrive(Session.ShipIndex) || Session.Equipment.Exists(e => e.item == GalaxyMap.KhadorDriveItem)) return null;
            return Mounted();
        }

        /// <summary>Modified: Ship::hasVolatileGoods's refusal (text 612) with the drive that would jump by its own name: 612
        /// names the Khador Drive, so a mod drive takes "driveVolatile" with its item name.</summary>
        public static string VolatileRefusal()
        {
            var drive = ActiveDrive();
            if (drive == null) return Localization.Get(612);
            return string.Format(Localization.Extra("driveVolatile", "You can't use the {0} when transporting volatile goods. The "
                + "charging of the drive generates too much heat and could lead to an explosion."), GameNames.Item(drive.item));
        }

        /// <summary>An item's "jumpFx", read and its data parsed once per mod revision (null = none or unreadable).</summary>
        public static Spec For(int item)
        {
            if (revision != ModManager.Revision) { cache.Clear(); revision = ModManager.Revision; }
            if (cache.TryGetValue(item, out var s)) return s;
            s = null;
            if (ModContent.ItemEntry(item, out var mod, out var json, out string where) && json["jumpFx"] is JObject o)
            {
                try { s = Read(mod, o, where); s.item = item; }
                catch (ModJsonException e) { Warn(mod, e.Message); }
                catch (Exception e) when (ModFileException.IsFileError(e)) { Warn(mod, e.Message); }
            }
            cache[item] = s;
            return s;
        }

        static Spec Read(ModInfo mod, JObject o, string where)
        {
            var s = new Spec { mod = mod, where = where };
            s.data = (string)o["data"];
            s.texture = (string)o["texture"];
            s.sound = (string)o["sound"];
            s.freeJumps = (bool?)o["freeJumps"] ?? false;
            s.endHoldMs = Mathf.Max(0f, (float?)o["endHoldMs"] ?? 0f);
            s.cellsFactor = Mathf.Max(0f, (float?)o["cellsFactor"] ?? 1f);
            s.noVoid = (bool?)o["noVoid"] ?? false;
            s.failChance = Mathf.Clamp01((float?)o["failChance"] ?? 0f);
            s.failMax = Mathf.Clamp01((float?)o["failMax"] ?? 1f);
            s.failNoRoute = Mathf.Clamp01((float?)o["failNoRoute"] ?? s.failChance);
            s.failMs = Mathf.Max(0f, (float?)o["failMs"] ?? s.failMs);
            s.failRumbleFromMs = Mathf.Max(0f, (float?)o["failRumbleFromMs"] ?? s.failRumbleFromMs);
            s.failTexture = (string)o["failTexture"];
            s.chargeVoice = (string)o["chargeVoice"];
            s.failVoice = (string)o["failVoice"];
            s.failVoiceChance = Mathf.Clamp01((float?)o["failVoiceChance"] ?? 1f);
            s.failVoiceMs = Mathf.Max(0f, (float?)o["failVoiceMs"] ?? 0f);
            s.failLine = (int?)o["failLine"] ?? -1;
            s.failSpeaker = Mathf.Max(0, (int?)o["failSpeaker"] ?? 0);
            s.failHandling = Mathf.Clamp((float?)o["failHandling"] ?? 1f, 0.01f, 1f);
            s.startDelayMs = Mathf.Max(0f, (float?)o["startDelayMs"] ?? 0f);
            s.failRumble = Mathf.Clamp((float?)o["failRumble"] ?? 1f, 0f, 2f);
            s.arrivalFx = (bool?)o["arrivalFx"] ?? false;
            s.failRadiusKm = Mathf.Clamp((float?)o["failRadiusKm"] ?? 20f, 4f, 100f);
            if (string.IsNullOrEmpty(s.data)) throw new ModJsonException($"{where}: jumpFx needs \"data\"");
            s.scale = (float?)o["scale"] ?? s.scale;
            s.glow = (float?)o["glow"] ?? s.glow;
            s.meetMs = (float?)o["meetMs"] ?? s.meetMs;
            s.meetDepth = (float?)o["meetDepth"] ?? s.meetDepth;
            var data = ModJson.Read(mod.Source, s.data) as JObject;
            if (data == null) throw new ModJsonException($"{where}: jumpFx data {s.data} not found");
            string only = (string)o["collection"];
            s.parts = new List<Part>();
            float last = 0f;
            foreach (var t in data["objects"] as JArray ?? new JArray())
            {
                if (!(t is JObject p)) continue;
                if (!string.IsNullOrEmpty(only) && (string)p["collection"] != only) continue;
                var part = new Part { name = (string)p["name"] ?? "part", billboard = (bool?)p["billboard"] ?? false };
                var vs = (JArray)p["verts"]; var fs = (JArray)p["faces"]; var uvs = (JArray)p["uvs"];
                var verts = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
                for (int f = 0; f < fs.Count; f++)
                {
                    var face = (JArray)fs[f]; var fu = (JArray)uvs[f];
                    int start = verts.Count;
                    for (int k = 0; k < face.Count; k++)
                    {
                        var v = (JArray)vs[(int)face[k]];
                        verts.Add(new Vector3((float)v[0], (float)v[1], (float)v[2]));
                        uv.Add(new Vector2((float)fu[k][0], (float)fu[k][1]));
                    }
                    for (int k = 1; k + 1 < face.Count; k++)   // both sides (the effect is seen from anywhere)
                    {
                        tris.Add(start); tris.Add(start + k); tris.Add(start + k + 1);
                        tris.Add(start); tris.Add(start + k + 1); tris.Add(start + k);
                    }
                }
                part.verts = verts.ToArray(); part.uv = uv.ToArray(); part.tris = tris.ToArray();
                if (p["keys"] is JObject keys)
                    foreach (var kv in keys)
                    {
                        var list = (JArray)kv.Value;
                        var tt = new float[list.Count]; var vv = new float[list.Count];
                        for (int i = 0; i < list.Count; i++) { tt[i] = (float)list[i][0]; vv[i] = (float)list[i][1]; last = Mathf.Max(last, tt[i]); }
                        part.keys[kv.Key] = (tt, vv);
                    }
                if (p["base"] is JObject b && b.HasValues)
                {
                    part.hasBase = true;
                    if (b["parent_loc"] is JArray l) part.baseLoc = new Vector3((float)l[0], (float)l[1], (float)l[2]);
                    part.baseScale = (float?)b["parent_scale"] ?? 1f;
                    part.baseRotY = (float?)b["parent_rotY"] ?? 0f;
                }
                s.parts.Add(part);
            }
            if (s.parts.Count == 0) throw new ModJsonException($"{where}: jumpFx data {s.data} has no parts");
            s.lengthMs = Mathf.Max(last, (float?)data["length_ms"] ?? 0f);
            return s;
        }

        /// <summary>The effect's sound: "gof2:<field>" of StoryAssets (null = none named or not found: the Khador Drive's).</summary>
        public static AudioClip Sound(Spec s)
        {
            if (s == null || string.IsNullOrEmpty(s.sound) || !s.sound.StartsWith("gof2:")) return null;
            var assets = StoryAssets.Load();
            var f = assets != null ? typeof(StoryAssets).GetField(s.sound.Substring(5)) : null;
            if (f == null || f.FieldType != typeof(AudioClip)) { Warn(s.mod, $"{s.where}: jumpFx sound {s.sound} isn't a game sound"); return null; }
            return f.GetValue(assets) as AudioClip;
        }

        static void Warn(ModInfo mod, string message)
        {
            Debug.LogWarning($"Mod {mod?.Id}: {message}");
            if (mod != null && !mod.Warnings.Contains(message)) mod.Warnings.Add(message);
        }
    }
}
