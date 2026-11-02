// ProjectWorld.cs
// Remake-only: the Mod Builder's star systems, stations, rooms and the mod's own races, each one entry of the mod's own
// file (systems.json, stations.json, interiors.json, races.json; Modding/README.md "systems.json and stations.json",
// "Hangars and bars", "Races") kept as a JObject with typed accessors, like ProjectItem (ProjectContent.cs). Wherever a race
// goes (a system's owner, locals and raiders) a mod race's reference is kept as the string it was: the accessors read it,
// and only a pick in the builder replaces it. Plain C#.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    /// <summary>One systems.json entry of a project: a new system ("id") or a change to one ("override").</summary>
    public class ProjectSystem : ProjectEntry
    {
        public ProjectSystem(JObject o) : base(o) { }

        public bool IsOverride => json["override"] != null;
        public string OverrideRef => RefText(json["override"]);
        public string LocalId => S("id");
        public string Ref => IsOverride ? "override:" + OverrideRef : "id:" + LocalId;

        public string Name { get => Text("name"); set => SetText("name", value); }
        /// <summary>0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian (a race word reads as its number); -1 when a mod's race owns it
        /// (ModRace). Setting it replaces a mod race.</summary>
        public int Race
        {
            get
            {
                if (json["race"] is JValue v && v.Type == JTokenType.String)
                {
                    int r = ModRaces.GameRaceWord((string)v);
                    return r >= 0 && r <= 3 ? r : -1;
                }
                return Mathf.Clamp(N("race") ?? 0, 0, 3);
            }
            set => json["race"] = Mathf.Clamp(value, 0, 3);
        }
        /// <summary>Remake: the mod race that owns it ("race" as a mod race's id or "mod_id:id", ModRaces), null = a game race.
        /// Setting null leaves "race" alone (pick a game race with Race).</summary>
        public string ModRace
        {
            get => Race < 0 ? S("race") : null;
            set { if (!string.IsNullOrEmpty(value)) json["race"] = value; }
        }
        /// <summary>"locals": the game and mod races whose patrols fly here (empty = the owner's).</summary>
        public List<string> Locals { get => RaceList("locals"); set => SetRaceList("locals", value); }
        /// <summary>"raiders": the game and mod races that raid it (empty = the game's pick).</summary>
        public List<string> Raiders { get => RaceList("raiders"); set => SetRaceList("raiders", value); }
        /// <summary>"traffic": "mod": only the mods' ships fly here. Off leaves another value ("all") as it is.</summary>
        /// <summary>"pirateHeld": pirates hold every station of the system (Data.PirateHold).</summary>
        public bool PirateHeld
        {
            get => json["pirateHeld"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)json["pirateHeld"];
            set { if (value) json["pirateHeld"] = true; else json.Remove("pirateHeld"); }
        }
        public bool ModTraffic
        {
            get => S("traffic") == "mod";
            set { if (value) json["traffic"] = "mod"; else if (S("traffic") == "mod") json.Remove("traffic"); }
        }
        public int Security { get => N("security") ?? 2; set => json["security"] = Mathf.Clamp(value, 0, 3); }
        public bool Visible { get => B("visible", true); set => SetB("visible", value, true); }
        /// <summary>The star map position (the originals 15-94 / 2-96 / 10-90).</summary>
        public Vector3 Position { get => V3("position") ?? new Vector3(50, 50, 50); set => SetV3("position", new Vector3(Mathf.Round(value.x), Mathf.Round(value.y), Mathf.Round(value.z))); }
        public bool HasPosition => json["position"] is JArray;
        public int Sky { get => N("sky") ?? 0; set => json["sky"] = Mathf.Clamp(value, 0, 18); }
        public string GateStation { get => R("gateStation"); set => SetR("gateStation", value); }
        public string SunTexture { get => S("sunTexture"); set => SetS("sunTexture", value); }
        public Color? SunColor
        {
            get => json["sunColor"] is JArray a && a.Count >= 3 ? new Color((float)a[0], (float)a[1], (float)a[2]) : (Color?)null;
            set { if (value == null) json.Remove("sunColor"); else json["sunColor"] = new JArray(Round(value.Value.r), Round(value.Value.g), Round(value.Value.b)); }
        }
        public string SpaceMusic { get => S("spaceMusic"); set => SetS("spaceMusic", value); }
        public string StationMusic { get => S("stationMusic"); set => SetS("stationMusic", value); }

        static float Round(float f) => Mathf.Round(f * 1000f) / 1000f;

        /// <summary>A list of races as written (a number, a race word, a mod race's id or key; one value or a list).</summary>
        List<string> RaceList(string k) =>
            json[k] is JArray a ? a.Select(RefText).Where(x => x != null).ToList()
            : RefText(json[k]) is string one ? new List<string> { one } : new List<string>();

        void SetRaceList(string k, List<string> value)
        {
            if (value == null || value.Count == 0) json.Remove(k);
            else json[k] = new JArray(value.Select(RefToken));
        }

        /// <summary>The systems its jumpgate reaches (numbers or keys).</summary>
        public List<string> Gates
        {
            get => json["gates"] is JArray a ? a.Select(RefText).Where(x => x != null).ToList() : new List<string>();
            set { if (value == null || value.Count == 0) json.Remove("gates"); else json["gates"] = new JArray(value.Select(RefToken)); }
        }

        JObject Skybox => json["skybox"] as JObject;
        JObject SkyboxMade() { if (!(json["skybox"] is JObject o)) json["skybox"] = o = new JObject(); return o; }
        void DropEmptySkybox() { if (Skybox != null && !Skybox.HasValues) json.Remove("skybox"); }

        /// <summary>skybox "nebula": its own nebula image (a path in the mod), null = the sky's.</summary>
        public string Nebula
        {
            get => Skybox?["nebula"] is JValue v && v.Type == JTokenType.String ? (string)v : null;
            set { if (string.IsNullOrEmpty(value)) Skybox?.Remove("nebula"); else SkyboxMade()["nebula"] = value; DropEmptySkybox(); }
        }

        /// <summary>skybox "stars": null = the system's usual, "0" / "1" / "2" the game's layers, "none", or an image path.</summary>
        public string Stars
        {
            get => Skybox?["stars"] != null ? Skybox["stars"].ToString() : null;
            set
            {
                if (string.IsNullOrEmpty(value)) Skybox?.Remove("stars");
                else SkyboxMade()["stars"] = int.TryParse(value, out int n) ? new JValue(n) : new JValue(value);
                DropEmptySkybox();
            }
        }

        public float NebulaBrightness
        {
            get => Skybox?["nebulaBrightness"] is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : 1f;
            set { if (Mathf.Approximately(value, 1f)) Skybox?.Remove("nebulaBrightness"); else SkyboxMade()["nebulaBrightness"] = Round(value); DropEmptySkybox(); }
        }

        public float StarsBrightness
        {
            get => Skybox?["starsBrightness"] is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : 1f;
            set { if (Mathf.Approximately(value, 1f)) Skybox?.Remove("starsBrightness"); else SkyboxMade()["starsBrightness"] = Round(value); DropEmptySkybox(); }
        }
    }

    /// <summary>One stations.json entry of a project.</summary>
    public class ProjectStation : ProjectEntry
    {
        public ProjectStation(JObject o) : base(o) { }

        public bool IsOverride => json["override"] != null;
        public string OverrideRef => RefText(json["override"]);
        public string LocalId => S("id");
        public string Ref => IsOverride ? "override:" + OverrideRef : "id:" + LocalId;

        public string Name { get => Text("name"); set => SetText("name", value); }
        /// <summary>What the place is ("Satellite Ruins"), under its name on the star map.</summary>
        public string Subtitle { get => Text("subtitle"); set => SetText("subtitle", value); }
        /// <summary>Its lore: the star map's planet panel and its system's news ticker.</summary>
        public string Description { get => Text("description"); set => SetText("description", value); }
        /// <summary>Its system: a number or a key.</summary>
        public string System { get => R("system"); set => SetR("system", value); }
        public int TechLevel { get => N("techLevel") ?? 5; set => json["techLevel"] = Mathf.Clamp(value, 0, 10); }
        public int Planet { get => N("planet") ?? 0; set => json["planet"] = value; }
        public int? LooksLike { get => N("looksLike"); set => SetN("looksLike", value); }
        public string Model { get => S("model"); set => SetS("model", value); }
        public float? ModelSize { get => F("modelSize"); set => SetF("modelSize", value); }
        public float ModelYaw { get => F("modelYaw") ?? 0f; set => SetF("modelYaw", Mathf.Approximately(value, 0f) ? (float?)null : Mathf.Repeat(value, 360f)); }
        public bool ModelCentre { get => B("modelCentre", true); set => SetB("modelCentre", value, true); }
        /// <summary>box (default), sphere or none.</summary>
        public string Collision { get => S("collision") ?? "box"; set => SetS("collision", value == "box" ? null : value); }
        /// <summary>The race whose hangar and bar it has (terran, vossk, nivelian, midorian), null = its system's race.</summary>
        public string Interior { get => json["interior"]?.ToString(); set => SetS("interior", value); }
        public string Hangar { get => S("hangar"); set => SetS("hangar", value); }
        public string Bar { get => S("bar"); set => SetS("bar", value); }
        public string PlanetTexture { get => S("planetTexture"); set => SetS("planetTexture", value); }
        /// <summary>The star map's planet image, wrapped round its sphere (2:1 like a world map).</summary>
        public string MapTexture { get => S("mapTexture"); set => SetS("mapTexture", value); }
        public bool MapRing { get => B("mapRing", false); set => SetB("mapRing", value, false); }
        public string Music { get => S("music"); set => SetS("music", value); }
    }

    /// <summary>One interiors.json entry of a project: a hangar or a Space Lounge bar room, with its markers placed in the builder
    /// ("markers": { "pad": [x, y, z], ... } in the room's metres, beside the model's own marker nodes, which they win over).</summary>
    public class ProjectInterior : ProjectEntry
    {
        public ProjectInterior(JObject o) : base(o) { }

        public string Id => S("id");
        public string Ref => "id:" + Id;
        public bool IsBar { get => S("type") == "bar"; set => json["type"] = value ? "bar" : "hangar"; }
        public string Model { get => S("model"); set => SetS("model", value); }
        public float Scale { get => F("scale") ?? 1f; set => SetF("scale", Mathf.Approximately(value, 1f) ? (float?)null : value); }
        public float? Fov { get => F("fov"); set => SetF("fov", value); }
        public float? Near { get => F("near"); set => SetF("near", value); }
        public float? Far { get => F("far"); set => SetF("far", value); }
        public float? LightIntensity { get => F("lightIntensity"); set => SetF("lightIntensity", value); }
        public float? StartYaw { get => F("startYaw"); set => SetF("startYaw", value); }
        public int? ParkedMax { get => N("parkedMax"); set => SetN("parkedMax", value); }
        public bool Flights { get => B("flights", true); set => SetB("flights", value, true); }
        public float? Cruise { get => F("cruise"); set => SetF("cruise", value); }

        public Color? Ambient
        {
            get => json["ambient"] is JArray a && a.Count >= 3 ? new Color((float)a[0], (float)a[1], (float)a[2]) : (Color?)null;
            set { if (value == null) json.Remove("ambient"); else json["ambient"] = new JArray(R(value.Value.r), R(value.Value.g), R(value.Value.b)); }
        }

        /// <summary>fog [r, g, b, end metres], null = none.</summary>
        public (Color colour, float end)? Fog
        {
            get => json["fog"] is JArray a && a.Count >= 4 ? (new Color((float)a[0], (float)a[1], (float)a[2]), (float)a[3]) : ((Color, float)?)null;
            set { if (value == null) json.Remove("fog"); else json["fog"] = new JArray(R(value.Value.colour.r), R(value.Value.colour.g), R(value.Value.colour.b), Mathf.Round(value.Value.end)); }
        }

        static float R(float f) => Mathf.Round(f * 1000f) / 1000f;

        JObject Markers => json["markers"] as JObject;

        /// <summary>A marker the builder placed (room metres), null = none (the model's own node, if any).</summary>
        public Vector3? Marker(string name) =>
            Markers?[name] is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : (Vector3?)null;

        public void SetMarker(string name, Vector3? at)
        {
            if (at == null) { Markers?.Remove(name); if (Markers != null && !Markers.HasValues) json.Remove("markers"); return; }
            if (!(json["markers"] is JObject o)) json["markers"] = o = new JObject();
            float r(float f) => Mathf.Round(f * 100f) / 100f;
            o[name] = new JArray(r(at.Value.x), r(at.Value.y), r(at.Value.z));
        }

        public IEnumerable<string> MarkerNames => Markers?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();
    }
}

namespace GoF2Remake.Modding
{
    /// <summary>Remake: one races.json entry of a project (ModRaces: a race of the mod's own that owns systems, flies ships
    /// and raids; Modding/README.md "Races"). Fields the builder doesn't edit stay.</summary>
    public class ProjectRace : ProjectEntry
    {
        public ProjectRace(JObject o) : base(o) { }

        /// <summary>The looks a mod race may borrow (the game race's hangar, bar, visitors, music, guns and gate) and their races.</summary>
        public static readonly string[] Looks = { "terran", "vossk", "nivelian", "midorian", "pirates", "void" };
        public static readonly int[] LookRaces = { 0, 1, 2, 3, 8, 9 };
        /// <summary>A relation's words, ModRaces.Friend / Neutral / Enemy.</summary>
        public static readonly string[] RelationWords = { "friend", "neutral", "enemy" };

        public string Id => S("id");
        public string Ref => "id:" + Id;
        public string Name { get => Text("name"); set => SetText("name", value); }
        public string Description { get => Text("description"); set => SetText("description", value); }

        /// <summary>"look" as an index of Looks (unknown or missing: Terran's, and the field stays until a pick).</summary>
        public int Look
        {
            get { int i = Array.IndexOf(LookRaces, ModRaces.GameRace(json["look"])); return i < 0 ? 0 : i; }
            set => json["look"] = Looks[Mathf.Clamp(value, 0, Looks.Length - 1)];
        }
        public int LookRace => LookRaces[Look];

        /// <summary>Its emblem (a PNG in the project), null = its look race's.</summary>
        public string Icon { get => S("icon"); set => SetS("icon", value); }

        public Color? Colour
        {
            get => json["color"] is JArray a && a.Count >= 3 ? new Color((float)a[0], (float)a[1], (float)a[2]) : (Color?)null;
            set
            {
                if (value == null) { json.Remove("color"); return; }
                float r(float f) => Mathf.Round(Mathf.Clamp01(f) * 1000f) / 1000f;
                json["color"] = new JArray(r(value.Value.r), r(value.Value.g), r(value.Value.b));
            }
        }

        /// <summary>The player's standing at the start, -100..100 (0 = not written).</summary>
        public int Standing { get => Mathf.Clamp(N("standing") ?? 0, -100, 100); set => SetN("standing", value == 0 ? (int?)null : Mathf.Clamp(value, -100, 100)); }
        public bool Hostile { get => B("hostile", false); set => SetB("hostile", value, false); }
        /// <summary>Lounge diplomats restore it (default: unless it is hostile).</summary>
        public bool Diplomats { get => B("diplomats", !Hostile); set => SetB("diplomats", value, !Hostile); }
        /// <summary>The item whose look and sound its NPC fighters' guns have (a number or a key), null = its look race's.</summary>
        public string Gun { get => R("gun"); set => SetR("gun", value); }

        /// <summary>"relations" as written: (a mod race's or game race's reference, "friend" / "neutral" / "enemy"), in order.</summary>
        public List<(string other, string relation)> Relations =>
            json["relations"] is JObject o
                ? o.Properties().Select(p => (p.Name, p.Value is JValue v ? v.ToString().Trim().ToLowerInvariant() : "")).ToList()
                : new List<(string, string)>();

        /// <summary>Sets one relation (null = removed: the other side's, or the default); the object goes when it is empty.</summary>
        public void SetRelation(string other, string relation)
        {
            if (string.IsNullOrEmpty(other)) return;
            if (!(json["relations"] is JObject o))
            {
                if (relation == null) return;
                json["relations"] = o = new JObject();
            }
            if (relation == null) o.Remove(other); else o[other] = relation;
            if (!o.HasValues) json.Remove("relations");
        }
    }
}
