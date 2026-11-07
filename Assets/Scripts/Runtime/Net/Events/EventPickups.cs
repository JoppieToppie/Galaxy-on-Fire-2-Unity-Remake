// EventPickups.cs
// Remake events (2026-11-05, for the GoF3 Ships mod's bar missions after Galaxy on Fire 3's freelance challenges: its orbs,
// coins and checkpoints): an event's / mission's pickups on this player's game (/pickup, the mission step Pickup;
// NetAdmin.Order.Pickup): glowing orbs at game coordinates in a station's orbit, a named one with its name on the HUD (a
// Navigation marker, unless unmarked), until "/pickup remove <name>" takes it away with a small flash, "/pickup clear", the event's end or
// the session's. They are only shown: a mission collects one with an "on near" at the same point, whose steps remove it, so
// the server decides (EventRunner's near check). Shown again whenever this player enters that orbit.

using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Events
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class EventPickups : MonoBehaviour
    {
        const string Model = "projectile_009_anim_add";   // a plasma ball, turning
        const float SizeMeters = 30f, SpinDegPerSec = 40f;

        sealed class Pickup
        {
            public int station;
            public Vector3 point;   // game units
            public string name;
            public bool marked;
            public GameObject go;
            public Navigation.Target marker;
        }

        static EventPickups instance;
        readonly List<Pickup> pickups = new List<Pickup>();
        SpaceLevel appliedTo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        /// <summary>How many pickups this game shows or will show (tests, the debug tools).</summary>
        public static int Count => instance != null ? instance.pickups.Count : 0;

        /// <summary>The server's order: "station x y z marked [name]" (game units, marked 1 / 0; a name already there
        /// moves), "remove name", or "clear".</summary>
        internal static void Apply(string text)
        {
            if (instance == null)
            {
                var go = new GameObject("EventPickups");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<EventPickups>();
            }
            text = (text ?? "").Trim();
            if (text == "clear") { instance.Clear(); return; }
            if (text.StartsWith("remove ")) { instance.Remove(text.Substring(7).Trim(), true); return; }
            if (!TryParse(text, out int station, out var point, out bool marked, out string name)) return;
            if (name.Length > 0) instance.Remove(name, false);
            instance.pickups.Add(new Pickup { station = station, point = point, marked = marked, name = name });
        }

        /// <summary>The order's text for one pickup: "station x y z marked [name]".</summary>
        public static bool TryParse(string text, out int station, out Vector3 point, out bool marked, out string name)
        {
            station = 0;
            point = Vector3.zero;
            marked = true;
            name = "";
            var f = (text ?? "").Split(new[] { ' ' }, 6);
            if (f.Length < 5 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out station)) return false;
            if (!Num(f[1], out float x) || !Num(f[2], out float y) || !Num(f[3], out float z) || (f[4] != "1" && f[4] != "0")) return false;
            point = new Vector3(x, y, z);
            marked = f[4] == "1";
            name = f.Length > 5 ? f[5].Trim() : "";
            return true;
        }

        static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) < 1e7f;

        void Remove(string name, bool flash)
        {
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var p = pickups[i];
                if (!string.Equals(p.name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (flash && p.go != null) Explosion.Spawn(p.go.transform.position, 0.15f, false);
                Hide(p);
                pickups.RemoveAt(i);
            }
        }

        void Clear()
        {
            foreach (var p in pickups) Hide(p);
            pickups.Clear();
        }

        void Hide(Pickup p)
        {
            if (p.go != null) Destroy(p.go);
            if (p.marker != null && appliedTo != null && appliedTo.Navigation != null) appliedTo.Navigation.Targets.Remove(p.marker);
            p.go = null;
            p.marker = null;
        }

        void Update()
        {
            if (!EventHost.ScreensActive) { if (pickups.Count > 0) Clear(); return; }
            if (pickups.Count == 0) return;
            var level = appliedTo != null ? appliedTo : FindAnyObjectByType<SpaceLevel>();
            if (level == null || level.Player == null || level.Layout == null) { Forget(); return; }
            if (appliedTo != level) { Forget(); appliedTo = level; }
            foreach (var p in pickups)
            {
                if (p.station != level.Layout.stationIndex) continue;
                if (p.go == null) Show(p, level);
                else p.go.transform.Rotate(0f, SpinDegPerSec * Time.deltaTime, 0f, Space.World);
            }
        }

        /// <summary>A new level (the old one's objects are gone with its scene): shown again there.</summary>
        void Forget()
        {
            foreach (var p in pickups) { p.go = null; p.marker = null; }
            appliedTo = null;
        }

        static void Show(Pickup p, SpaceLevel level)
        {
            var prefab = AssembledObject.LoadPrefab(level.Database.AssemblyByName(Model));
            var go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Pickup " + p.name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float size = bounds.size.magnitude;
            if (size > 1e-3f) go.transform.localScale *= SizeMeters / size;
            go.transform.position = OrbitLayout.ToUnity(p.point) + (go.transform.position - bounds.center) * (size > 1e-3f ? SizeMeters / size : 1f);
            p.go = go;
            if (p.marked && p.name.Length > 0 && level.Navigation != null)
            {
                p.marker = new Navigation.Target { kind = Navigation.Kind.Marker, transform = go.transform, fixedPosition = go.transform.position, name = p.name };
                level.Navigation.Targets.Add(p.marker);
            }
        }
    }
}
