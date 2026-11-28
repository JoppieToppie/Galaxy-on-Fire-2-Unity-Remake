// MovieTimeline.cs
// Remake-only: Movie mode's timeline (UI.MovieMode edits it in flight), players' request 2026-11-28 ("many people want to
// make movies of the game ... a timeline where you can move / spawn / call anything based on time"). A take is two
// things on one clock:
//   cues         any event command line (spawn, npc, camera, fade, letterbox, title, dialog, radio, sound, music, kill,
//                tp, ...: CLAUDE.md "Events" in docs/notes/event-graphs.md) at a time in seconds; Script() writes them as
//                the event runner's line form (sorted, a "wait" between), so a take is a global event run
//                (EventRunner.StartText) and every command the graphs have is a cue
//   camera keys  the camera's pose (orbit game coordinates, Unity world rotation with its roll, field of view) at a
//                time; between two keys a Catmull-Rom path, the rotation slerped; a key marked "cut" jumps there at its
//                time instead (the camera holds the key before it until then)
// Saved as JSON (<data>/Movies/<name>.gof2movie). Plain C#.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GoF2Remake.Events
{
    [Serializable]
    public sealed class MovieCue
    {
        public float time;
        public string command = "";
    }

    [Serializable]
    public sealed class MovieCameraKey
    {
        public float time;
        public Vector3 position;                       // orbit game coordinates (World.OrbitScale.ToGame)
        public Quaternion rotation = Quaternion.identity;
        public float fov = 60f;
        public bool cut;                               // a hard cut to this key at its time
    }

    public enum MovieTrack { Camera, Scene, Ships, Text, Sound, Other }

    [Serializable]
    public sealed class MovieTimeline
    {
        public const string Extension = "gof2movie";
        public const float MinLength = 1f, MaxLength = 1800f;

        public int version = 1;
        public string name = "movie";
        public float length = 30f;
        public int station = -1;                       // where it was made (its coordinates are that orbit's)
        public bool hasStart;                          // the player's ship is put here before each take
        public Vector3 startPosition;
        public Quaternion startRotation = Quaternion.identity;
        public List<MovieCue> cues = new List<MovieCue>();
        public List<MovieCameraKey> keys = new List<MovieCameraKey>();

        /// <summary>The cue's lane by its command word (the timeline's rows).</summary>
        public static MovieTrack TrackOf(string command)
        {
            string w = Word(command);
            switch (w)
            {
                case "camera": case "cutscene": case "fade": case "letterbox": return MovieTrack.Scene;
                case "spawn": case "npc": case "kill": case "heal": case "provoke": case "tp": case "ship": case "lend":
                case "wormhole": case "pickup": return MovieTrack.Ships;
                case "title": case "dialog": case "radio": case "timer": return MovieTrack.Text;
                case "sound": case "music": return MovieTrack.Sound;
                default: return MovieTrack.Other;
            }
        }

        static string Word(string command)
        {
            string c = Clean(command);
            int sp = c.IndexOf(' ');
            return (sp < 0 ? c : c.Substring(0, sp)).ToLowerInvariant();
        }

        /// <summary>One line, no leading "/" (the chat's form is accepted).</summary>
        public static string Clean(string command)
        {
            string c = (command ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return c.StartsWith("/") ? c.Substring(1).TrimStart() : c;
        }

        /// <summary>In time order; cues at the same time keep the order they were added in (OrderBy is stable, List.Sort isn't).</summary>
        public void Sort()
        {
            cues = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(cues, c => c.time));
            keys = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(keys, k => k.time));
        }

        /// <summary>The take as the event runner's line form: the cues in time order with a wait before each, then a wait to
        /// the end. autostop off: the take's ships out of sight mustn't end it.</summary>
        public string Script()
        {
            var ordered = new List<(float t, int i, string c)>();
            for (int i = 0; i < cues.Count; i++)
            {
                string c = Clean(cues[i].command);
                if (c.Length > 0) ordered.Add((Mathf.Max(0f, cues[i].time), i, c));
            }
            ordered.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : a.i.CompareTo(b.i));   // ties: the list's order
            var sb = new StringBuilder("set autostop = 0\n");
            float now = 0f;
            foreach (var (t, _, c) in ordered)
            {
                if (t > now + 0.0005f) sb.Append("wait ").Append(Seconds(t - now)).Append('\n');
                now = Mathf.Max(now, t);
                sb.Append(c).Append('\n');
            }
            if (length > now + 0.0005f) sb.Append("wait ").Append(Seconds(length - now)).Append('\n');
            return sb.ToString();
        }

        static string Seconds(float s) => s.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>The camera at 't' from the keys (false: no keys). Before the first key its pose, after the last the last's.</summary>
        public bool CameraAt(float t, out Vector3 position, out Quaternion rotation, out float fov)
        {
            position = Vector3.zero; rotation = Quaternion.identity; fov = 60f;
            int n = keys.Count;
            if (n == 0) return false;
            if (t <= keys[0].time || n == 1) { Take(keys[0], out position, out rotation, out fov); return true; }
            if (t >= keys[n - 1].time) { Take(keys[n - 1], out position, out rotation, out fov); return true; }
            int i = 0;
            while (i < n - 2 && t >= keys[i + 1].time) i++;
            var a = keys[i]; var b = keys[i + 1];
            if (b.cut || b.time - a.time < 1e-4f) { Take(a, out position, out rotation, out fov); return true; }
            float u = (t - a.time) / (b.time - a.time);
            // The neighbours across a cut aren't part of this path: the end key stands in for them.
            var p0 = i > 0 && !a.cut ? keys[i - 1].position : a.position;
            var p3 = i + 2 < n && !keys[i + 2].cut ? keys[i + 2].position : b.position;
            position = CatmullRom(p0, a.position, b.position, p3, u);
            rotation = Quaternion.Slerp(a.rotation, b.rotation, u);
            fov = Mathf.Lerp(a.fov, b.fov, u);
            return true;
        }

        static void Take(MovieCameraKey k, out Vector3 p, out Quaternion r, out float f) { p = k.position; r = k.rotation; f = k.fov; }

        /// <summary>The uniform Catmull-Rom spline through p1 (u 0) and p2 (u 1).</summary>
        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        // ---- files --------------------------------------------------------------------------------------------

        public static string Folder => Path.Combine(GamePaths.Data, "Movies");

        /// <summary>A file name from 'name': letters, digits, - and _ (others become _), "movie" when nothing is left.</summary>
        public static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char ch in (name ?? "").Trim()) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            string s = sb.ToString().Trim('_');
            return s.Length == 0 ? "movie" : s.Length > 64 ? s.Substring(0, 64) : s;
        }

        /// <summary>Writes it to the Movies folder; returns the path.</summary>
        public string Save()
        {
            name = SafeName(name);
            length = Mathf.Clamp(length, MinLength, MaxLength);
            Sort();
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, name + "." + Extension);
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
            return path;
        }

        /// <summary>A saved movie by name (null: none, or not readable).</summary>
        public static MovieTimeline Load(string name)
        {
            string path = Path.Combine(Folder, SafeName(name) + "." + Extension);
            if (!File.Exists(path)) return null;
            try
            {
                var m = JsonUtility.FromJson<MovieTimeline>(File.ReadAllText(path));
                if (m == null) return null;
                m.cues ??= new List<MovieCue>();
                m.keys ??= new List<MovieCameraKey>();
                foreach (var c in m.cues) c.command ??= "";
                m.length = Mathf.Clamp(m.length, MinLength, MaxLength);
                m.Sort();
                return m;
            }
            catch (Exception e) { Debug.LogWarning($"Movie {name}: {e.Message}"); return null; }
        }

        /// <summary>The saved movies' names, newest first.</summary>
        public static List<string> List()
        {
            var list = new List<string>();
            if (!Directory.Exists(Folder)) return list;
            var files = new List<FileInfo>(new DirectoryInfo(Folder).GetFiles("*." + Extension));
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            foreach (var f in files) list.Add(Path.GetFileNameWithoutExtension(f.Name));
            return list;
        }
    }
}
