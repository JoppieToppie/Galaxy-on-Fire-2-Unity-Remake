// ModShipKitsReading.cs
// ModShipKits: reading a kit's files (kit.json, colours, skins, liveries, decals, trails) (part of the partial ModShipKits; its fields are in ModShipKits.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static partial class ModShipKits
    {
        static void Read(ModInfo mod, List<Kit> into)
        {
            if (!(ModJson.Read(mod.Source, File) is JToken token)) return;
            if (!(token is JArray list)) throw new ModJsonException($"{File}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, File);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each kit must be an object {{ ... }}");
                foreach (var prop in o.Properties())
                    if (!KitFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                var k = new Kit
                {
                    mod = mod, localId = ModJson.Str(o, "id"), name = ModJson.Text(o, "name"),
                    scale = ModJson.Float(o, "scale", 1f, File), yaw = ModJson.Float(o, "yaw", 0f, File),
                };
                if (string.IsNullOrEmpty(k.localId) || !ModManifest.ValidId(k.localId))
                    throw new ModJsonException($"{where}: \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (!ids.Add(k.localId)) throw new ModJsonException($"{where}: the id \"{k.localId}\" is used twice");
                if (k.scale <= 0f) throw new ModJsonException($"{where}: \"scale\" must be above 0");
                if (ModJson.Get(o, "materials") is JToken mt)
                {
                    try { k.materials = JsonUtility.FromJson<MaterialList>("{\"list\":" + mt.ToString() + "}").list ?? new List<CustomShipMaterial>(); }
                    catch (Exception e) { throw new ModJsonException($"{where}: \"materials\": {e.Message}"); }
                }
                if (!(ModJson.Get(o, "parts") is JArray parts)) throw new ModJsonException($"{where}: \"parts\" must be a list of the kit's parts");
                // A kit still being made (the Mod Builder writes one before its first part): left out, not an error.
                if (parts.Count == 0) { Warn(mod, $"{where}: the kit \"{k.localId}\" has no parts yet (left out)"); continue; }
                var partIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pt in parts)
                {
                    string pwhere = ModJson.Where(pt, File);
                    if (!(pt is JObject po)) throw new ModJsonException($"{pwhere}: each part must be an object {{ ... }}");
                    foreach (var prop in po.Properties())
                        if (!PartFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                    var p = new Part
                    {
                        id = ModJson.Str(po, "id"), slot = ModJson.Str(po, "slot"), model = ModJson.Str(po, "model"), name = ModJson.Text(po, "name"),
                        types = ModJson.Strings(po, "types") ?? new List<string>(), tiers = ModJson.Strings(po, "tiers") ?? new List<string>(),
                    };
                    if (string.IsNullOrEmpty(p.id) || !ModManifest.ValidId(p.id))
                        throw new ModJsonException($"{pwhere}: \"id\" is missing or uses more than a-z, 0-9, _ and -");
                    p.anchor = ModJson.Str(po, "anchor");
                    if (ModJson.Get(po, "anchors") is JObject an)
                        foreach (var ap in an.Properties())
                            if (ap.Value is JArray av && av.Count >= 3)
                                p.anchors[ap.Name] = new Vector3((float)av[0], (float)av[1], (float)av[2]);
                    if (!partIds.Add(p.id)) throw new ModJsonException($"{pwhere}: the part id \"{p.id}\" is used twice in this kit");
                    if (string.IsNullOrEmpty(p.slot) || !ModManifest.ValidId(p.slot)) throw new ModJsonException($"{pwhere}: \"slot\" is missing (e.g. \"wings\")");
                    if (Build.Reserved(p.slot)) throw new ModJsonException($"{pwhere}: \"{p.slot}\" can't be a slot's name (a build uses it)");
                    if (string.IsNullOrEmpty(p.model)) throw new ModJsonException($"{pwhere}: \"model\" is missing (the part's glTF / GLB)");
                    if (!mod.Source.Exists(p.model)) throw new ModJsonException($"{pwhere}: the model \"{p.model}\" isn't in the mod");
                    foreach (var tier in p.tiers)
                        if (!mod.Source.Exists(tier)) throw new ModJsonException($"{pwhere}: the tier model \"{tier}\" isn't in the mod");
                    p.mounts = ReadMounts(ModJson.Get(po, "mounts"), pwhere);
                    if (ModJson.Get(po, "stats") is JObject st)
                    {
                        p.armor = ModJson.Float(st, "armor", 0f, File); p.cargo = ModJson.Float(st, "cargo", 0f, File);
                        p.handling = ModJson.Float(st, "handling", 0f, File); p.equipment = ModJson.Int(st, "equipment", 0, File);
                        p.topSpeed = ModJson.Float(st, "topSpeed", 0f, File); p.turnRate = ModJson.Float(st, "turnRate", 0f, File);
                        p.acceleration = ModJson.Float(st, "acceleration", 0f, File); p.strafeSpeed = ModJson.Float(st, "strafeSpeed", 0f, File);
                    }
                    if (ModJson.Get(po, "slots") is JObject sl)
                    {
                        p.primarySlots = ModJson.Int(sl, "primary", -1, File); p.secondarySlots = ModJson.Int(sl, "secondary", -1, File);
                        p.turretSlots = ModJson.Int(sl, "turret", -1, File);
                    }
                    if (ModJson.Get(po, "tierMounts") is JArray tm)
                        foreach (var t2 in tm) p.tierMounts.Add(ReadMounts(t2, pwhere));
                    k.parts.Add(p);
                }
                var slots = ModJson.Strings(o, "slots");
                k.slots = slots != null && slots.Count > 0 ? slots : k.parts.Select(p => p.slot).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var p in k.parts)
                    if (!k.slots.Contains(p.slot, StringComparer.OrdinalIgnoreCase))
                        throw new ModJsonException($"{where}: the part \"{p.id}\"'s slot \"{p.slot}\" isn't in \"slots\"");
                ReadColors(o, k, where);
                ReadLiveries(o, k, mod, where);
                ReadDecals(o, k, mod, where);
                ReadTrails(o, k, mod, where);
                ReadSkins(o, k, mod, where);
                k.hardpoints = ModHardpoints.Read(o, mod, File, where);
                into.Add(k);
            }
        }
        static List<WeaponMount> ReadMounts(JToken t, string where)
        {
            var list = new List<WeaponMount>();
            if (t == null) return list;
            if (!(t is JArray arr)) throw new ModJsonException($"{where}: \"mounts\" must be a list of mounts");
            foreach (var e in arr)
            {
                if (!(e is JObject)) continue;
                try { list.Add(JsonUtility.FromJson<WeaponMount>(e.ToString())); }
                catch (Exception ex) { throw new ModJsonException($"{where}: a mount: {ex.Message}"); }
            }
            return list;
        }
        internal static List<ColorChoice> ReadPalette(JArray arr, string file)
        {
            var list = new List<ColorChoice>();
            foreach (var e in arr)
            {
                if (!(e is JObject eo)) continue;
                var c = new ColorChoice
                {
                    id = ModJson.Str(eo, "id"), name = ModJson.Text(eo, "name"),
                    metallic = ModJson.Float(eo, "metallic", -1f, file), smoothness = ModJson.Float(eo, "smoothness", -1f, file),
                };
                if (string.IsNullOrEmpty(c.id)) throw new ModJsonException($"{ModJson.Where(e, file)}: a palette colour needs an \"id\"");
                if (!TryHex(ModJson.Str(eo, "color"), out c.color)) throw new ModJsonException($"{ModJson.Where(e, file)}: \"color\" must be \"#RRGGBB\"");
                list.Add(c);
            }
            return list;
        }
        static void ReadColors(JObject o, Kit k, string where)
        {
            if (ModJson.Get(o, "palettes") is JToken pt)
            {
                if (!(pt is JObject po)) throw new ModJsonException($"{where}: \"palettes\" must be an object {{ \"main\": [ ... ] }}");
                foreach (var prop in po.Properties())
                {
                    if (!(prop.Value is JArray arr)) throw new ModJsonException($"{ModJson.Where(prop, File)}: a palette must be a list of colours");
                    k.palettes[prop.Name] = ReadPalette(arr, File);
                }
            }
            if (ModJson.Get(o, "colorSlots") is JArray slots)
                foreach (var t in slots)
                {
                    if (!(t is JObject so)) continue;
                    var slot = new ColorSlot { id = ModJson.Str(so, "id"), palette = ModJson.Str(so, "palette"), defaultValue = ModJson.Str(so, "default"), name = ModJson.Text(so, "name") };
                    if (string.IsNullOrEmpty(slot.id) || !ModManifest.ValidId(slot.id)) throw new ModJsonException($"{ModJson.Where(t, File)}: a colour slot needs an \"id\" (a-z, 0-9, _ and -)");
                    if (ModJson.Get(so, "steps") is JArray st)
                    {
                        // A strength slot: "steps": [0, 0.25, ...], "scales": "<colour slot id>", "default": "0.5".
                        slot.steps = st.Where(x => x.Type == JTokenType.Float || x.Type == JTokenType.Integer).Select(x => Mathf.Clamp((float)x, 0f, 4f)).ToArray();
                        slot.scales = ModJson.Str(so, "scales");
                        if (slot.steps.Length == 0 || string.IsNullOrEmpty(slot.scales)) throw new ModJsonException($"{ModJson.Where(t, File)}: a strength slot needs numbers in \"steps\" and the colour slot it \"scales\"");
                        k.colorSlots.Add(slot);
                        continue;
                    }
                    if (slot.palette == null || !k.palettes.ContainsKey(slot.palette)) throw new ModJsonException($"{ModJson.Where(t, File)}: \"palette\" \"{slot.palette}\" isn't in \"palettes\"");
                    if (ModJson.Get(so, "targets") is JArray ta)
                        foreach (var x in ta)
                            if (x is JObject xo)
                                slot.targets.Add(new ColorTarget
                                {
                                    material = ModJson.Str(xo, "material") ?? "", property = (ModJson.Str(xo, "property") ?? "base").ToLowerInvariant(),
                                    intensity = ModJson.Float(xo, "intensity", 1f, File),
                                });
                    k.colorSlots.Add(slot);
                }
            if (ModJson.Get(o, "presets") is JArray presets)
                foreach (var t in presets)
                {
                    if (!(t is JObject pr)) continue;
                    var preset = new Preset { id = ModJson.Str(pr, "id"), name = ModJson.Text(pr, "name") };
                    if (string.IsNullOrEmpty(preset.id)) throw new ModJsonException($"{ModJson.Where(t, File)}: a preset needs an \"id\"");
                    if (ModJson.Get(pr, "colors") is JObject co)
                        foreach (var prop in co.Properties()) if (prop.Value.Type == JTokenType.String) preset.colors[prop.Name] = (string)prop.Value;
                    k.presets.Add(preset);
                }
        }
        static ColorChoice Choice(string id, string english, string hex)
        {
            TryHex(hex, out var c);
            return new ColorChoice { id = id, name = new Dictionary<string, string> { ["en"] = english }, color = c };
        }
        /// <summary>A weapon colour value: a weapon palette id or "#RRGGBB" (false: none, or not one).</summary>
        public static bool ResolveWeaponColor(Kit kit, string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(value) || value == Build.NoSkin) return false;
            if (value.StartsWith("#")) return TryHex(value, out color);
            var choice = kit.WeaponColors.Find(c => string.Equals(c.id, value, StringComparison.OrdinalIgnoreCase));
            if (choice == null) return false;
            color = choice.color;
            return true;
        }
        static void ReadSkins(JObject o, Kit k, ModInfo mod, string where, string file = File)
        {
            if (!(ModJson.Get(o, "skins") is JToken st)) return;
            if (!(st is JArray list)) throw new ModJsonException($"{where}: \"skins\" must be a list of skins");
            float[] Rgb(JObject x, string field, string w)
            {
                if (!(ModJson.Get(x, field) is JToken t)) return null;
                if (!(t is JArray a) || a.Count < 3) throw new ModJsonException($"{w}: \"{field}\" must be [r, g, b] (0..1)");
                return a.Take(3).Select(v => (float)v).ToArray();
            }
            foreach (var t in list)
            {
                string swhere = ModJson.Where(t, file);
                if (!(t is JObject so)) throw new ModJsonException($"{swhere}: each skin must be an object {{ \"id\", \"name\", \"materials\" }}");
                foreach (var prop in so.Properties())
                    if (!SkinFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                var skin = new Skin { mod = mod, id = ModJson.Str(so, "id"), name = ModJson.Text(so, "name"), types = ModJson.Strings(so, "types") ?? new List<string>(),
                                      engineColor = Rgb(so, "engineColor", swhere) };
                if (string.IsNullOrEmpty(skin.id) || !ModManifest.ValidId(skin.id)) throw new ModJsonException($"{swhere}: a skin's \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (skin.id == Build.NoSkin) throw new ModJsonException($"{swhere}: \"none\" can't be a skin's id (it means the model's own look)");
                if (k.skins.Exists(x => string.Equals(x.id, skin.id, StringComparison.OrdinalIgnoreCase))) throw new ModJsonException($"{swhere}: the skin id \"{skin.id}\" is used twice in this kit");
                if (ModJson.Get(so, "materials") is JArray ma)
                    foreach (var mt in ma)
                    {
                        string mwhere = ModJson.Where(mt, file);
                        if (!(mt is JObject mo)) throw new ModJsonException($"{mwhere}: each skin material must be an object {{ \"material\", \"diffuse\", ... }}");
                        foreach (var prop in mo.Properties())
                            if (!SkinMaterialFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                        var m = new SkinMaterial
                        {
                            material = ModJson.Str(mo, "material"), diffuse = ModJson.Str(mo, "diffuse"), emission = ModJson.Str(mo, "emission"),
                            metallicSmoothness = ModJson.Str(mo, "metallicSmoothness"), normal = ModJson.Str(mo, "normal"),
                            specularGlossiness = ModJson.Str(mo, "specularGlossiness"),
                            color = Rgb(mo, "color", mwhere), emissionColor = Rgb(mo, "emissionColor", mwhere),
                            emissionIntensity = ModJson.Float(mo, "emissionIntensity", 1f, file),
                        };
                        if (string.IsNullOrEmpty(m.material)) throw new ModJsonException($"{mwhere}: \"material\" is missing (the model's material name, or part of it)");
                        foreach (var path in new[] { m.diffuse, m.emission, m.metallicSmoothness, m.specularGlossiness, m.normal })
                            if (!string.IsNullOrEmpty(path) && !mod.Source.Exists(path)) throw new ModJsonException($"{mwhere}: the texture \"{path}\" isn't in the mod");
                        skin.materials.Add(m);
                    }
                if (ModJson.Get(so, "colors") is JToken ct)
                {
                    if (ct.Type == JTokenType.String) skin.colorPreset = (string)ct;
                    else if (ct is JObject co) { foreach (var c in co.Properties()) if (c.Value.Type == JTokenType.String) skin.colors[c.Name] = (string)c.Value; }
                    else throw new ModJsonException($"{swhere}: \"colors\" is a preset's id or {{ \"slot\": \"value\" }}");
                    if (skin.colorPreset != null)
                    {
                        var pr = k.presets.Find(x => string.Equals(x.id, skin.colorPreset, StringComparison.OrdinalIgnoreCase));
                        if (pr == null) Warn(mod, $"{swhere}: \"colors\": no preset \"{skin.colorPreset}\" in this kit (ignored)");
                        else foreach (var kv in pr.colors) skin.colors[kv.Key] = kv.Value;
                    }
                }
                skin.decal = ModJson.Str(so, "decal");
                if (skin.decal != null && k.FindDecal(skin.decal) == null) { Warn(mod, $"{swhere}: \"decal\": no decal \"{skin.decal}\" in this kit (ignored)"); skin.decal = null; }
                skin.livery = ModJson.Str(so, "livery");
                if (skin.livery != null && k.FindLivery(skin.livery) == null) { Warn(mod, $"{swhere}: \"livery\": no livery \"{skin.livery}\" in this kit (ignored)"); skin.livery = null; }
                if (skin.materials.Count == 0 && skin.engineColor == null && skin.colors.Count == 0 && skin.livery == null && skin.decal == null)
                    Warn(mod, $"{swhere}: the skin \"{skin.id}\" changes nothing (no \"materials\", \"colors\" or \"livery\")");
                k.skins.Add(skin);
            }
        }
        static void ReadLiveries(JObject o, Kit k, ModInfo mod, string where, string file = File)
        {
            if (ModJson.Get(o, "liveryAxes") is JToken ax)
            {
                if (!(ax is JObject ao)) throw new ModJsonException($"{where}: \"liveryAxes\" must be {{ \"forward\": \"x\", \"right\": \"z\", \"up\": \"y\" }}");
                Vector3 Axis(string field, Vector3 def)
                {
                    string w = ModJson.Str(ao, field);
                    if (w == null) return def;
                    w = w.Trim().ToLowerInvariant();
                    float sign = w.StartsWith("-") ? -1f : 1f;
                    var v = w.TrimStart('-', '+') switch { "x" => new Vector3(-1f, 0f, 0f), "y" => Vector3.up, "z" => Vector3.forward, _ => Vector3.zero };   // glTF x is Unity's -x
                    if (v == Vector3.zero) throw new ModJsonException($"{where}: \"liveryAxes\": \"{field}\" is \"x\", \"y\" or \"z\" (\"-x\" for the other way)");
                    return v * sign;
                }
                k.liveryForward = Axis("forward", k.liveryForward);
                k.liveryRight = Axis("right", k.liveryRight);
                k.liveryUp = Axis("up", k.liveryUp);
            }
            if (!(ModJson.Get(o, "liveries") is JToken lt)) return;
            if (!(lt is JArray list)) throw new ModJsonException($"{where}: \"liveries\" must be a list of liveries");
            foreach (var t in list)
            {
                string lw = ModJson.Where(t, file);
                if (!(t is JObject lo)) throw new ModJsonException($"{lw}: each livery must be an object {{ \"id\", \"name\", \"texture\" }}");
                foreach (var prop in lo.Properties())
                    if (!LiveryFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                var l = new Livery { mod = mod, id = ModJson.Str(lo, "id"), name = ModJson.Text(lo, "name"), texture = ModJson.Str(lo, "texture"),
                                     icon = ModJson.Str(lo, "icon"), types = ModJson.Strings(lo, "types") ?? new List<string>() };
                if (string.IsNullOrEmpty(l.id) || !ModManifest.ValidId(l.id)) throw new ModJsonException($"{lw}: a livery's \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (l.id == Build.NoSkin) throw new ModJsonException($"{lw}: \"none\" can't be a livery's id");
                if (k.FindLivery(l.id) != null) throw new ModJsonException($"{lw}: the livery id \"{l.id}\" is used twice in this kit");
                if (string.IsNullOrEmpty(l.texture) || !mod.Source.Exists(l.texture)) throw new ModJsonException($"{lw}: the livery's \"texture\" is missing or isn't in the mod");
                if (l.icon != null && !mod.Source.Exists(l.icon)) { Warn(mod, $"{lw}: the icon {l.icon} isn't in the mod (ignored)"); l.icon = null; }
                if (ModJson.Get(lo, "color") is JToken c)
                {
                    if (c.Type == JTokenType.String) { if (!TryHex((string)c, out l.color)) throw new ModJsonException($"{lw}: \"color\" is \"#RRGGBB\" or [r, g, b]"); }
                    else if (c is JArray ca && ca.Count >= 3) l.color = new Color((float)ca[0], (float)ca[1], (float)ca[2]);
                    else throw new ModJsonException($"{lw}: \"color\" is \"#RRGGBB\" or [r, g, b]");
                }
                l.metallic = Mathf.Clamp01(ModJson.Float(lo, "metallic", 0f, file));
                l.smoothness = Mathf.Clamp01(ModJson.Float(lo, "smoothness", 0.4f, file));
                l.opacity = Mathf.Clamp01(ModJson.Float(lo, "opacity", 1f, file));
                l.clipAngle = Mathf.Clamp(ModJson.Float(lo, "clipAngle", 90f, file), 0f, 90f);
                l.tiling = ModJson.Bool(lo, "tiling", false, file);
                Vector2 Pair(string field, Vector2 def)
                {
                    if (!(ModJson.Get(lo, field) is JToken pt)) return def;
                    if (!(pt is JArray pa) || pa.Count < 2) throw new ModJsonException($"{lw}: \"{field}\" must be [x, y]");
                    return new Vector2((float)pa[0], (float)pa[1]);
                }
                l.scale = Pair("scale", Vector2.one);
                if (Mathf.Abs(l.scale.x) < 1e-3f || Mathf.Abs(l.scale.y) < 1e-3f) throw new ModJsonException($"{lw}: \"scale\" can't be 0");
                l.offset = Pair("offset", Vector2.zero);
                if (ModJson.Get(lo, "mirror") is JToken mt)
                {
                    if (!(mt is JArray ma) || ma.Count < 2) throw new ModJsonException($"{lw}: \"mirror\" must be [x, y] (true / false)");
                    l.mirrorX = (bool)ma[0]; l.mirrorY = (bool)ma[1];
                }
                k.liveries.Add(l);
            }
        }
        static void ReadDecals(JObject o, Kit k, ModInfo mod, string where, string file = File)
        {
            if (!(ModJson.Get(o, "decals") is JToken dt)) return;
            if (!(dt is JArray list)) throw new ModJsonException($"{where}: \"decals\" must be a list of decals");
            foreach (var t in list)
            {
                string dw = ModJson.Where(t, file);
                if (!(t is JObject d)) throw new ModJsonException($"{dw}: each decal must be an object {{ \"id\", \"name\", \"insignia\", \"type\" }}");
                foreach (var prop in d.Properties())
                    if (!DecalFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                var dc = new Decal { mod = mod, id = ModJson.Str(d, "id"), name = ModJson.Text(d, "name"), insignia = ModJson.Str(d, "insignia"),
                                     type = ModJson.Str(d, "type"), icon = ModJson.Str(d, "icon"), types = ModJson.Strings(d, "types") ?? new List<string>() };
                if (string.IsNullOrEmpty(dc.id) || !ModManifest.ValidId(dc.id)) throw new ModJsonException($"{dw}: a decal's \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (dc.id == Build.NoSkin) throw new ModJsonException($"{dw}: \"none\" can't be a decal's id");
                if (k.FindDecal(dc.id) != null) throw new ModJsonException($"{dw}: the decal id \"{dc.id}\" is used twice in this kit");
                if (dc.insignia == null && dc.type == null) throw new ModJsonException($"{dw}: a decal needs \"insignia\" or \"type\" (a PNG mask)");
                foreach (var path in new[] { dc.insignia, dc.type })
                    if (path != null && !mod.Source.Exists(path)) throw new ModJsonException($"{dw}: the texture \"{path}\" isn't in the mod");
                if (dc.icon != null && !mod.Source.Exists(dc.icon)) { Warn(mod, $"{dw}: the icon {dc.icon} isn't in the mod (ignored)"); dc.icon = null; }
                k.decals.Add(dc);
            }
        }
        static void ReadTrails(JObject o, Kit k, ModInfo mod, string where, string file = File)
        {
            if (!(ModJson.Get(o, "trails") is JToken tt)) return;
            if (!(tt is JArray list)) throw new ModJsonException($"{where}: \"trails\" must be a list of engine trails");
            foreach (var t in list)
            {
                string tw = ModJson.Where(t, file);
                if (!(t is JObject to)) throw new ModJsonException($"{tw}: each trail must be an object {{ \"id\", \"name\", \"kind\" }}");
                foreach (var prop in to.Properties())
                    if (!TrailFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                var tr = new EngineTrail { mod = mod, id = ModJson.Str(to, "id"), name = ModJson.Text(to, "name"), kind = (ModJson.Str(to, "kind") ?? "default").ToLowerInvariant(),
                                           icon = ModJson.Str(to, "icon"), texture = ModJson.Str(to, "texture"), mask = ModJson.Str(to, "mask"), normal = ModJson.Str(to, "normal") };
                if (string.IsNullOrEmpty(tr.id) || !ModManifest.ValidId(tr.id)) throw new ModJsonException($"{tw}: a trail's \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (k.FindTrail(tr.id) != null) throw new ModJsonException($"{tw}: the trail id \"{tr.id}\" is used twice in this kit");
                if (tr.kind != "default" && !Es2Trails.Known(tr.kind)) Warn(mod, $"{tw}: unknown trail kind \"{tr.kind}\" (drawn as the Default veil)");
                foreach (var path in new[] { tr.texture, tr.mask, tr.normal })
                    if (path != null && !mod.Source.Exists(path)) throw new ModJsonException($"{tw}: the texture \"{path}\" isn't in the mod");
                if (tr.icon != null && !mod.Source.Exists(tr.icon)) { Warn(mod, $"{tw}: the icon {tr.icon} isn't in the mod (ignored)"); tr.icon = null; }
                if (ModJson.Get(to, "curves") is JToken ct)
                {
                    if (!(ct is JArray ca)) throw new ModJsonException($"{tw}: \"curves\" must be a list of number lists");
                    foreach (var c in ca)
                    {
                        if (!(c is JArray cn) || cn.Count == 0) throw new ModJsonException($"{tw}: each curve must be a list of numbers");
                        tr.curves.Add(cn.Select(x => (float)x).ToArray());
                    }
                }
                if (ModJson.Get(to, "mesh") is JToken mt)
                {
                    if (!(mt is JObject mo)) throw new ModJsonException($"{tw}: \"mesh\" must be {{ \"positions\", \"normals\", \"uv\", \"indices\" }}");
                    float[] F(string field) => ModJson.Get(mo, field) is JArray a ? a.Select(x => (float)x).ToArray() : new float[0];
                    var pos = F("positions"); var nrm = F("normals"); var uv = F("uv");
                    var idx = ModJson.Get(mo, "indices") is JArray ia ? ia.Select(x => (int)x).ToArray() : new int[0];
                    int n = pos.Length / 3;
                    if (n == 0 || nrm.Length != n * 3 || uv.Length != n * 2 || idx.Length % 3 != 0 || idx.Any(i => i < 0 || i >= n))
                        throw new ModJsonException($"{tw}: \"mesh\" needs 3 numbers per position and normal, 2 per uv and triangles of valid indices");
                    tr.mesh = new TrailMesh
                    {
                        positions = Enumerable.Range(0, n).Select(i => new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2])).ToArray(),
                        normals = Enumerable.Range(0, n).Select(i => new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2])).ToArray(),
                        uv = Enumerable.Range(0, n).Select(i => new Vector2(uv[i * 2], uv[i * 2 + 1])).ToArray(),
                        indices = idx,
                    };
                }
                if (tr.mesh == null && (tr.kind == "rings" || tr.kind == "squares" || tr.kind == "triangles"))
                    throw new ModJsonException($"{tw}: a \"{tr.kind}\" trail needs its \"mesh\"");
                k.trails.Add(tr);
            }
        }
        internal static bool TryHex(string text, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(text)) return false;
            return ColorUtility.TryParseHtmlString(text.StartsWith("#") ? text : "#" + text, out c);
        }
        /// <summary>A colour value: a palette id of the slot's palette, or "#RRGGBB[/metallic/smoothness]"; a strength slot's
        /// value is a number (the colour then grey at that level).</summary>
        public static bool Resolve(Kit kit, ColorSlot slot, string value, out Color color, out float metallic, out float smoothness)
        {
            color = Color.white; metallic = smoothness = -1f;
            if (string.IsNullOrEmpty(value)) return false;
            if (slot != null && slot.IsStrength)
            {
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) return false;
                f = Mathf.Clamp(f, 0f, 4f);
                color = new Color(f, f, f, 1f);
                return true;
            }
            if (value.StartsWith("#"))
            {
                var bits = value.Split('/');
                if (!TryHex(bits[0], out color)) return false;
                if (bits.Length > 1) float.TryParse(bits[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out metallic);
                if (bits.Length > 2) float.TryParse(bits[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out smoothness);
                return true;
            }
            var choice = kit.PaletteOf(slot).Find(c => string.Equals(c.id, value, StringComparison.OrdinalIgnoreCase));
            if (choice == null) return false;
            color = choice.color; metallic = choice.metallic; smoothness = choice.smoothness;
            return true;
        }
        /// <summary>The strength a build gives a colour slot's emission (a strength slot that scales it; 1 without one).</summary>
        public static float StrengthOf(Kit kit, Build build, string slotId)
        {
            var s = kit.colorSlots.Find(c => c.IsStrength && string.Equals(c.scales, slotId, StringComparison.OrdinalIgnoreCase));
            if (s == null) return 1f;
            string v = build.colors.TryGetValue(s.id, out var bv) ? bv : s.defaultValue;
            return Resolve(kit, s, v, out var c, out _, out _) ? c.r : 1f;
        }
        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
