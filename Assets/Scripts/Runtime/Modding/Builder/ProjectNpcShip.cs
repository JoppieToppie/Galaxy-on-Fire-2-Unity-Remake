// ProjectNpcShip.cs
// Remake-only: one npcships.json entry of a Mod Builder project (ModNpcShips, Modding/README.md "NPC ships"): a new NPC ship
// ("id", its "kind", its hull: "ship" or "looksLike") or a change to the game's ("override": battleship, carrier,
// vossk_battleship, freighter, pirate_boss) or another mod's. Kept as its JObject like the other kinds (ProjectContent.cs);
// the sections (hardpoints, escorts, loot, spawn, docking, launches) are edited as JSON objects in place
// (ModBuilderSpaceViewNpc). Plain C#.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public class ProjectNpcShip : ProjectEntry
    {
        public ProjectNpcShip(JObject o) : base(o) { }

        public bool IsOverride => json["override"] != null;
        public string OverrideRef => RefText(json["override"]);
        public string LocalId => S("id");
        public string Ref => IsOverride ? "override:" + OverrideRef : "id:" + LocalId;

        public string Name { get => Text("name"); set => SetText("name", value); }
        /// <summary>capital, freighter or boss (a change: the game ship's own).</summary>
        public string Kind
        {
            get => IsOverride ? KindOfGame(OverrideRef) : S("kind") ?? ModNpcShips.Capital;
            set { if (!IsOverride) SetS("kind", value); }
        }
        /// <summary>Its hull: a ships.json ship (a number or a key), null = looksLike's / the game's.</summary>
        public string Ship { get => R("ship"); set => SetR("ship", value); }
        /// <summary>A game NPC ship's hull (ModNpcShips.Looks), null = by its kind.</summary>
        public string LooksLike { get => S("looksLike"); set => SetS("looksLike", value); }
        public string Race { get => json["race"]?.ToString(); set => SetS("race", value); }
        /// <summary>null = its kind's (a boss hostile, the others by the standing).</summary>
        public bool? Hostile { get => json["hostile"] is JValue v && v.Type == JTokenType.Boolean ? (bool)v : (bool?)null; set { if (value == null) json.Remove("hostile"); else json["hostile"] = value.Value; } }
        public int? Hull { get => N("hull"); set => SetN("hull", value); }
        public float HullFactor { get => F("hullFactor") ?? 1f; set => SetF("hullFactor", Mathf.Approximately(value, 1f) ? (float?)null : value); }
        public float GunFactor { get => F("gunFactor") ?? 1f; set => SetF("gunFactor", Mathf.Approximately(value, 1f) ? (float?)null : value); }
        public float? Speed { get => F("speed"); set => SetF("speed", value); }
        public float? ExplosionScale { get => F("explosionScale"); set => SetF("explosionScale", value); }
        public bool Missiles { get => B("missiles", true); set => SetB("missiles", value, true); }
        public bool Retreat { get => B("retreat", false); set => SetB("retreat", value, false); }

        /// <summary>The hardpoints list (made on first use with 'make'), null = none: its hull's own turrets.</summary>
        public JArray Hardpoints(bool make)
        {
            if (json["hardpoints"] is JArray a) return a;
            if (!make) return null;
            a = new JArray();
            json["hardpoints"] = a;
            return a;
        }
        public void DropHardpoints() => json.Remove("hardpoints");

        /// <summary>A section object ("escorts", "spawn", "docking", "launches", "loot"), made on first use with 'make'.</summary>
        public JObject Section(string key, bool make)
        {
            if (json[key] is JObject o) return o;
            if (!make) return null;
            o = new JObject();
            json[key] = o;
            return o;
        }
        public void DropSection(string key) => json.Remove(key);

        /// <summary>"loot": false (nothing at all).</summary>
        public bool NoLoot { get => json["loot"] is JValue v && v.Type == JTokenType.Boolean && !(bool)v; set { if (value) json["loot"] = false; else if (NoLoot) json.Remove("loot"); } }

        /// <summary>A game type's kind.</summary>
        public static string KindOfGame(string key) => key == ModNpcShips.FreighterKey ? ModNpcShips.Freighter
            : key == ModNpcShips.PirateBoss ? ModNpcShips.Boss : ModNpcShips.Capital;

        /// <summary>[x, y, z] of a JSON array (game units), null = none.</summary>
        public static Vector3? Xyz(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : (Vector3?)null;
        public static JArray Xyz(Vector3 v) => new JArray(Mathf.Round(v.x), Mathf.Round(v.y), Mathf.Round(v.z));

        /// <summary>The hull's game key for its look: the change's own key, else looksLike, else by kind.</summary>
        public string BaseKey => IsOverride && ModNpcShips.GameKeys.Contains(OverrideRef) ? OverrideRef
            : LooksLike ?? (Kind == ModNpcShips.Freighter ? ModNpcShips.FreighterKey : Kind == ModNpcShips.Boss ? ModNpcShips.PirateBoss : ModNpcShips.Battleship);

        public IEnumerable<JObject> Pads => (Section("docking", false)?["pads"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
    }
}
