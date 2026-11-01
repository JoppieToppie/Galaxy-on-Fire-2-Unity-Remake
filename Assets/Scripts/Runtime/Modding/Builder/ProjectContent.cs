// ProjectContent.cs
// Remake-only: the Mod Builder's other kinds of content beside its ships (ModProject.Ships / ProjectShip), each one entry of
// the mod's own file kept as a JObject (fields the builder doesn't edit, from a hand edit, stay) with typed accessors:
//   ProjectItem       an items.json entry: a new item ("id" + "base") or a change to a game item ("override");
//   ProjectCharacter  a characters.json entry.
// The formats are Modding/README.md's (ModContent, ModCharacters read them). Plain C#.

using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    /// <summary>A JObject entry with the accessors both kinds share.</summary>
    public abstract class ProjectEntry
    {
        protected readonly JObject json;
        protected ProjectEntry(JObject o) { json = o; }

        public JObject ToJson() => (JObject)json.DeepClone();
        /// <summary>The entry's JSON itself (the graph editor and Edit as JSON work on it in place).</summary>
        public JObject Json => json;

        protected string S(string k) => json[k] is JValue v && v.Type == JTokenType.String ? (string)v : null;
        protected int? N(string k) => json[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? Mathf.RoundToInt((float)v) : (int?)null;
        protected bool B(string k, bool d) => json[k] is JValue v && v.Type == JTokenType.Boolean ? (bool)v : d;
        protected void SetS(string k, string v) { if (string.IsNullOrEmpty(v)) json.Remove(k); else json[k] = v; }
        protected void SetN(string k, int? v) { if (v == null) json.Remove(k); else json[k] = v.Value; }
        /// <summary>A flag written only when it isn't the game's default.</summary>
        protected void SetB(string k, bool v, bool d) { if (v == d) json.Remove(k); else json[k] = v; }
        protected float? F(string k) => json[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : (float?)null;
        protected void SetF(string k, float? v) { if (v == null) json.Remove(k); else json[k] = Mathf.Round(v.Value * 1000f) / 1000f; }

        /// <summary>A reference to other content: a game number or a key ("mod_id:id"), as text (null = none).</summary>
        protected string R(string k) => RefText(json[k]);
        protected void SetR(string k, string v) { if (string.IsNullOrEmpty(v)) json.Remove(k); else json[k] = RefToken(v); }

        /// <summary>A reference as written: a number stays a number, a key a string.</summary>
        public static JToken RefToken(string v) => int.TryParse(v, out int n) ? new JValue(n) : new JValue(v);
        public static string RefText(JToken t) =>
            t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.String) ? v.ToString() : null;

        /// <summary>[x, y, z] numbers (null = none).</summary>
        protected Vector3? V3(string k) => json[k] is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : (Vector3?)null;
        protected void SetV3(string k, Vector3? v)
        {
            if (v == null) { json.Remove(k); return; }
            float R3(float f) => Mathf.Round(f * 1000f) / 1000f;
            json[k] = new JArray(R3(v.Value.x), R3(v.Value.y), R3(v.Value.z));
        }

        /// <summary>A field read as raw JSON (the builder's less common fields), null = none.</summary>
        public JToken Get(string k) => json[k];
        public void Set(string k, JToken v) { if (v == null || v.Type == JTokenType.Null) json.Remove(k); else json[k] = v; }

        /// <summary>A text field: a string, or one per language ({"en": ..., "de": ...}). The builder edits the English one
        /// (else the only one); a per-language field keeps its other languages.</summary>
        protected string Text(string k)
        {
            if (json[k] is JValue v && v.Type == JTokenType.String) return (string)v;
            if (json[k] is JObject o) return (string)(o["en"] ?? o.Properties().FirstOrDefault()?.Value);
            return null;
        }

        protected void SetText(string k, string value)
        {
            if (json[k] is JObject o) { if (string.IsNullOrEmpty(value)) o.Remove("en"); else o["en"] = value; return; }
            SetS(k, value);
        }
    }

    /// <summary>One items.json entry of a project.</summary>
    /// <summary>weaponfx.json: a weapon effect of the mod's own, a look and sound based on a game weapon's ("base"), which
    /// weapon items take with "fx": "<id>" (ModWeapons). Its JSON is an fx object with its "id", "name" and "base".</summary>
    public class ProjectWeaponEffect : ProjectEntry
    {
        public ProjectWeaponEffect(JObject o) : base(o) { }

        public string Id { get => S("id"); set => SetS("id", value); }
        public string Name { get => S("name"); set => SetS("name", value); }
        /// <summary>The game weapon its look starts from (what it leaves out is that weapon's; the builder's range fires it).</summary>
        public int Base { get => N("base") ?? -1; set => SetN("base", value < 0 ? (int?)null : value); }
    }

    public class ProjectItem : ProjectEntry
    {
        public ProjectItem(JObject o) : base(o) { }

        /// <summary>"fx" naming one of the project's weapon effects (null: its own look, or the base's).</summary>
        public string EffectId
        {
            get => json["fx"] is JValue v && v.Type == JTokenType.String ? (string)v : null;
            set { if (string.IsNullOrEmpty(value)) json.Remove("fx"); else json["fx"] = value; }
        }
        /// <summary>"fx" naming a game weapon whose whole look and sound it takes (null: none).</summary>
        public int? GameLook
        {
            get => json["fx"] is JValue v && v.Type == JTokenType.Integer ? (int)v : (int?)null;
            set { if (value == null) json.Remove("fx"); else json["fx"] = value.Value; }
        }
        /// <summary>It takes a weapon effect or a game weapon's look rather than its own fx.</summary>
        public bool TakesLook => EffectId != null || GameLook != null;

        public bool IsOverride => json["override"] != null;
        /// <summary>The game item an override changes (-1: another mod's "mod:id").</summary>
        public int OverrideIndex => IsOverride && int.TryParse(json["override"].ToString(), out int i) ? i : -1;
        public string LocalId => S("id");
        /// <summary>A new item's base: the game item it is a copy of (-1: another mod's "mod:id").</summary>
        public int BaseIndex => json["base"] is JValue v && int.TryParse(v.ToString(), out int i) ? i : -1;
        /// <summary>The original item it looks and works like: an override's item, a new item's base.</summary>
        public int Original => IsOverride ? OverrideIndex : BaseIndex;
        /// <summary>builder.json's key for it.</summary>
        public string Ref => IsOverride ? "override:" + json["override"] : "id:" + LocalId;

        public string Name { get => Text("name"); set => SetText("name", value); }
        public string Description { get => Text("description"); set => SetText("description", value); }
        /// <summary>Its own shop icon (a 180 x 88 PNG in the project, frame included), null = the base item's.</summary>
        public string Icon { get => S("icon"); set => SetS("icon", value); }
        public int? TechLevel { get => N("techLevel"); set => SetN("techLevel", value); }
        public int? Occurrence { get => N("occurrence"); set => SetN("occurrence", value); }

        /// <summary>"stock": how many units a shop holds of it (a number, or [least, most]), null = the amount the game
        /// gives its kind of item (Shop.ModStock).</summary>
        public int? StockMin => N("stock") ?? StockRange(0);
        public int? StockMax => N("stock") ?? StockRange(1);

        int? StockRange(int end) => json["stock"] is JArray a && a.Count >= 2 ? Mathf.RoundToInt((float)a[end]) : (int?)null;

        /// <summary>Sets it: null = none, one number when least and most are the same, else [least, most].</summary>
        public void SetStock(int? min, int? max)
        {
            if (min == null) json.Remove("stock");
            else if (max == null || max.Value <= min.Value) json["stock"] = min.Value;
            else json["stock"] = new JArray(min.Value, max.Value);
        }

        /// <summary>The price range: "minPrice" / "maxPrice", or the "price" shorthand (n, or [min, max]).</summary>
        public int? MinPrice => N("minPrice") ?? PriceShorthand(0);
        public int? MaxPrice => N("maxPrice") ?? PriceShorthand(1);

        int? PriceShorthand(int end)
        {
            if (json["price"] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)) return Mathf.RoundToInt((float)v);
            if (json["price"] is JArray a && a.Count >= 2) return Mathf.RoundToInt((float)a[end]);
            return null;
        }

        /// <summary>Sets the range (written as minPrice / maxPrice, the shorthand dropped).</summary>
        public void SetPrices(int? min, int? max)
        {
            json.Remove("price");
            SetN("minPrice", min);
            SetN("maxPrice", max);
        }

        JObject Stats
        {
            get
            {
                if (!(json["stats"] is JObject o)) json["stats"] = o = new JObject();
                return o;
            }
        }

        /// <summary>The value the entry gives an attribute (by any of its stat names, ItemStats), null = the base's.</summary>
        public int? Stat(int attribute)
        {
            if (!(json["stats"] is JObject o)) return null;
            foreach (var p in o.Properties())
                if (ItemStats.IdOf(p.Name) == attribute && p.Value is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                    return Mathf.RoundToInt((float)v);
            return null;
        }

        /// <summary>Sets an attribute under its readable stat name (other names for it dropped); null = the base's again.</summary>
        public void SetStat(int attribute, string name, int? value)
        {
            var o = Stats;
            foreach (var p in o.Properties().ToList()) if (ItemStats.IdOf(p.Name) == attribute) p.Remove();
            if (value != null) o[name] = value.Value;
            if (!o.HasValues) json.Remove("stats");
        }

        /// <summary>fx "turret": the turret's own model (a GLB in the project, ModWeapons.BuildTurret), null = the base
        /// item's. Read from either form ("file" or { "model": ... }), written as the object; the rest of "fx" stays. The
        /// size, offset, muzzle and shot start apply to either model; a "turret" left empty is dropped.</summary>
        public string TurretModel
        {
            get => Turret is JValue v && v.Type == JTokenType.String ? (string)v : Turret is JObject o ? (string)o["model"] : null;
            set
            {
                var o = TurretObject();
                if (string.IsNullOrEmpty(value)) o.Remove("model"); else { o["model"] = value; o.Remove("item"); }
                DropEmptyTurret();
            }
        }

        /// <summary>fx "turret" "item": another item's turret model (a game turret's: no file), null = none.</summary>
        public int? TurretItem
        {
            get => Turret is JObject o && o["item"] is JValue v && v.Type == JTokenType.Integer ? (int)v : (int?)null;
            set
            {
                var o = TurretObject();
                if (value == null) o.Remove("item"); else { o["item"] = value.Value; o.Remove("model"); }
                DropEmptyTurret();
            }
        }

        // ---- the look: another item's projectile / muzzle flash / impact and sounds (ModWeapons: no files needed) ----

        /// <summary>The fx part names the builder edits.</summary>
        public static readonly string[] FxParts = { "projectile", "muzzle", "impact" };

        /// <summary>A part with a sprite or model of its own (a hand edit): the builder leaves it alone.</summary>
        public bool FxPartIsCustom(string part) => json["fx"] is JObject fx && fx[part] is JObject p && (p["sprite"] != null || p["model"] != null);

        /// <summary>fx part "item": another item's part (the base item's own when null).</summary>
        public int? FxPartItem(string part) =>
            json["fx"] is JObject fx && fx[part] is JObject p && p["item"] is JValue v && v.Type == JTokenType.Integer ? (int)v : (int?)null;

        /// <summary>fx part "scale": its size against the original's (null = 1).</summary>
        public float? FxPartScale(string part) =>
            json["fx"] is JObject fx && fx[part] is JObject p && p["scale"] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : (float?)null;

        public void SetFxPartItem(string part, int? item) => SetFxPartValue(part, "item", item.HasValue ? new JValue(item.Value) : null);
        public void SetFxPartScale(string part, float? scale) => SetFxPartValue(part, "scale", scale.HasValue && !Mathf.Approximately(scale.Value, 1f) ? new JValue(scale.Value) : null);

        void SetFxPartValue(string part, string key, JToken value)
        {
            if (FxPartIsCustom(part)) return;
            var p = json["fx"] is JObject fx && fx[part] is JObject o ? o : new JObject();
            if (value == null) p.Remove(key); else p[key] = value;
            SetFx(part, p.HasValues ? p : null);
        }

        /// <summary>fx "shot" / "explosionSound" given as an item's number: that item's sounds (null = the base item's, or files
        /// a hand edit gave: SoundIsCustom).</summary>
        public int? SoundItem(string key) => json["fx"] is JObject fx && fx[key] is JValue v && v.Type == JTokenType.Integer ? (int)v : (int?)null;
        public bool SoundIsCustom(string key) => json["fx"] is JObject fx && fx[key] != null && SoundItem(key) == null;
        public void SetSoundItem(string key, int? item) { if (!SoundIsCustom(key)) SetFx(key, item.HasValue ? new JValue(item.Value) : null); }

        /// <summary>fx "turret" "muzzle" / "shotStart": game units along the gun node, null = the base item's.</summary>
        public float? TurretMuzzle { get => TurretNumber("muzzle"); set => SetTurretNumber("muzzle", value); }
        public float? TurretShotStart { get => TurretNumber("shotStart"); set => SetTurretNumber("shotStart", value); }
        /// <summary>fx "turret" "scale": the model's size factor, null = 1 (as made).</summary>
        public float? TurretScale { get => TurretNumber("scale"); set => SetTurretNumber("scale", value); }

        /// <summary>fx "turret" "offset": the model moved off its mount (game units: x toward the pilot's left, y up, z forward).</summary>
        public Vector3Int TurretOffset
        {
            get => Turret is JObject o && o["offset"] is JArray a && a.Count >= 3
                ? new Vector3Int(Mathf.RoundToInt((float)a[0]), Mathf.RoundToInt((float)a[1]), Mathf.RoundToInt((float)a[2])) : Vector3Int.zero;
            set
            {
                var o = TurretObject();
                if (value == Vector3Int.zero) o.Remove("offset"); else o["offset"] = new JArray(value.x, value.y, value.z);
                DropEmptyTurret();
            }
        }

        JToken Turret => json["fx"] is JObject fx ? fx["turret"] : null;

        float? TurretNumber(string k) =>
            Turret is JObject o && o[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : (float?)null;

        void SetTurretNumber(string k, float? value)
        {
            var o = TurretObject();
            if (value == null) o.Remove(k); else o[k] = value.Value;
            DropEmptyTurret();
        }

        void DropEmptyTurret()
        {
            if (Turret is JObject o && !o.HasValues) SetFx("turret", null);
        }

        /// <summary>fx "turret" as an object (a plain file name turned into { "model": file }).</summary>
        JObject TurretObject()
        {
            if (Turret is JObject o) return o;
            o = new JObject();
            if (Turret is JValue v && v.Type == JTokenType.String) o["model"] = (string)v;
            SetFx("turret", o);
            return o;
        }

        void SetFx(string k, JToken value)
        {
            if (!(json["fx"] is JObject fx))
            {
                if (value == null) return;
                json["fx"] = fx = new JObject();
            }
            if (value == null) fx.Remove(k); else fx[k] = value;
            if (!fx.HasValues) json.Remove("fx");
        }
    }

    /// <summary>One characters.json entry of a project.</summary>
    public class ProjectCharacter : ProjectEntry
    {
        public ProjectCharacter(JObject o) : base(o) { }

        public string Id => S("id");
        public string Ref => "id:" + Id;
        public string Name { get => Text("name"); set => SetText("name", value); }
        public string Portrait { get => S("portrait"); set => SetS("portrait", value); }
        /// <summary>The race word (terran, vossk, nivelian, midorian, pirate, void), null = none given.</summary>
        public string Race { get => S("race"); set => SetS("race", value); }
        public bool Male { get => !string.Equals(S("gender"), "female", System.StringComparison.OrdinalIgnoreCase); set => json["gender"] = value ? "male" : "female"; }
        public bool Mirrored { get => B("mirrored", false); set => SetB("mirrored", value, false); }
        public bool Frame { get => B("frame", true); set => SetB("frame", value, true); }
        public bool Background { get => B("background", true); set => SetB("background", value, true); }
        /// <summary>The story character it stands in for (a speaker's name or number), null = nobody.</summary>
        public string Replaces { get => json["replaces"]?.ToString(); set => SetS("replaces", value); }
    }
}
