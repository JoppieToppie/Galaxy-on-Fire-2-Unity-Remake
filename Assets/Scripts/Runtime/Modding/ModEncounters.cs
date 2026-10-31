// ModEncounters.cs
// Remake mods: a cruise drive's encounters (Flight.CruiseEncounters; Modding/README.md "Cruise drives"), encounters.json:
//   [ { "id": "ambush", "kind": "pirates", "weight": 20, "visible": false, "minRank": 4, "security": [0, 1], "ships": 4 },
//     { "id": "beacon", "kind": "quest", "quest": "my_beacon", "weight": 5, "name": { "en": "Old beacon" } },
//     { "override": "flyby", "weight": 0 } ]
// "id" adds an entry (kinds: pirates, trader, derelict, distress, flyby, quest), "override" changes one of the game's own
// (pirates, trader, derelict, distress, flyby) or another mod's "mod:id": weight, visible, minRank, security, freePlayOnly,
// ships, name. A "quest" entry starts that quest graph (single player) when it comes; the graph does the rest.

using System;
using System.Collections.Generic;
using GoF2Remake.Flight;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModEncounters
    {
        public const string File = "encounters.json";

        public class Def
        {
            public ModInfo mod;
            public JObject json;
            public string where, localId, overrideRef;
            public Dictionary<string, string> name;
            public string key => localId != null ? mod.Id + ":" + localId : null;
        }

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "override", "kind", "weight", "visible", "minRank", "security", "freePlayOnly", "quest", "ships", "name",
        };

        static int revision = -1;
        static List<CruiseEncounters.Entry> all;
        static readonly Dictionary<string, Def> defByKey = new Dictionary<string, Def>(StringComparer.Ordinal);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { revision = -1; all = null; defByKey.Clear(); }

        public static void Parse(ModInfo mod, List<Def> into)
        {
            var token = ModJson.Read(mod.Source, File);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{File}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, File);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each encounter must be an object {{ ... }}");
                var d = new Def { mod = mod, json = o, where = where, localId = ModJson.Str(o, "id"), overrideRef = ModJson.Str(o, "override"), name = ModJson.Text(o, "name") };
                if ((d.localId == null) == (d.overrideRef == null))
                    throw new ModJsonException($"{where}: an encounter needs either \"id\" (a new one) or \"override\" (change one: pirates, trader, derelict, distress, flyby or \"mod:id\")");
                if (d.localId != null)
                {
                    if (!ModManifest.ValidId(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" may only use a-z, 0-9, _ and -");
                    if (!ids.Add(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" is used twice");
                    string kind = ModJson.Str(o, "kind");
                    if (kind == null || Array.IndexOf(CruiseEncounters.Kinds, kind.ToLowerInvariant()) < 0)
                        throw new ModJsonException($"{where}: \"kind\" must be one of {string.Join(", ", CruiseEncounters.Kinds)}");
                    if (kind.ToLowerInvariant() == CruiseEncounters.Quest && string.IsNullOrEmpty(ModJson.Str(o, "quest")))
                        throw new ModJsonException($"{where}: a \"quest\" encounter needs \"quest\", the quest graph it starts");
                }
                if (ModJson.Get(o, "security") is JToken sec)
                {
                    if (!(sec is JArray sa)) throw new ModJsonException($"{where}: \"security\" must be a list of levels [0, 1] (0 Dangerous .. 3 Secure)");
                    foreach (var v in sa)
                        if (v.Type != JTokenType.Integer || (int)v < 0 || (int)v > 3) throw new ModJsonException($"{where}: \"security\" levels are 0 to 3");
                }
                foreach (var f in new[] { "weight", "minRank", "ships" }) ModJson.Int(o, f, 0, File);
                foreach (var f in new[] { "visible", "freePlayOnly" }) ModJson.Bool(o, f, false, File);
                foreach (var prop in o.Properties())
                    if (!Fields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                into.Add(d);
            }
        }

        /// <summary>The table the active mods make of the game's own (CruiseEncounters.BuiltIn): their overrides applied, their
        /// entries added (ids "mod:id"), in load order; made again when the mods change.</summary>
        public static List<CruiseEncounters.Entry> All()
        {
            if (all != null && revision == ModManager.Revision) return all;
            revision = ModManager.Revision;
            defByKey.Clear();
            var list = CruiseEncounters.BuiltIn();
            foreach (var mod in ModManager.Active)
                foreach (var d in ModContent.Parse(mod).encounters)
                {
                    if (d.overrideRef != null)
                    {
                        var target = list.Find(e => string.Equals(e.id, d.overrideRef, StringComparison.OrdinalIgnoreCase));
                        if (target == null) { mod.Warnings.Add($"{d.where}: no encounter \"{d.overrideRef}\" to override (ignored)"); continue; }
                        Apply(d, target);
                        continue;
                    }
                    var e = new CruiseEncounters.Entry { id = d.key, kind = ModJson.Str(d.json, "kind").ToLowerInvariant(), quest = ModJson.Str(d.json, "quest") };
                    Apply(d, e);
                    list.Add(e);
                    defByKey[e.id] = d;
                }
            all = list;
            return all;
        }

        static void Apply(Def d, CruiseEncounters.Entry e)
        {
            var o = d.json;
            e.weight = ModJson.Int(o, "weight", e.weight, File);
            e.visible = ModJson.Bool(o, "visible", e.visible, File);
            e.minRank = ModJson.Int(o, "minRank", e.minRank, File);
            e.freePlayOnly = ModJson.Bool(o, "freePlayOnly", e.freePlayOnly, File);
            e.ships = ModJson.Int(o, "ships", e.ships, File);
            if (ModJson.Get(o, "security") is JArray sa)
            {
                var levels = new List<int>();
                foreach (var v in sa) levels.Add((int)v);
                e.security = levels.ToArray();
            }
            if (d.name != null) e.name = ModContent.TranslatedText(d.mod, "encounter." + (d.localId ?? d.overrideRef) + ".name", d.name);
        }
    }
}
