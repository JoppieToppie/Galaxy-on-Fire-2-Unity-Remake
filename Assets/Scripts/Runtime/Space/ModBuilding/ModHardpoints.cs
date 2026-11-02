// ModHardpoints.cs
// Remake mods: a customizable ship's weapons and turret as models of the kit (EVERSPACE 2's weapons and player turret on its
// ships' sockets). A kit in shipkits.json may give
//   "hardpoints": {
//     "turret": "equipment/turret.glb",      the model a turret item shows on the build's "builtIn" turret mounts: nodes
//                                            "pivot" (turns sideways), under it one whose name contains "gun" (tilts up and
//                                            down; the shots fly along it) and optionally "muzzle" (where they leave); the
//                                            pivot and gun nodes unrotated, the barrel along the parts' nose
//     "weapons": [ { "slot": "primary" | "secondary" | "turret", "model": "equipment/pulse_laser.glb",
//                    "items": [0, 1, "other_mod:plasma_gun"], "categories": [0] }, ... ] }
// For each mounted weapon the first rule of its slot whose "items" (numbers, also matching mod items based on them, or
// "mod:id" keys) or "categories" (item category numbers) match, or that has neither, gives its model ("model": "" = none).
// A weapon model's origin goes on the mount (its "muzzle" node, if any, is where its shots leave); a turret's weapon goes on
// the turret's gun with its muzzle on the turret's "muzzle" (EVERSPACE 2 seats its turret weapons so); a turret without a
// "muzzle" node takes the weapon's origin on its gun node and the weapon's muzzle as its own. Every model loads
// with the kit's parts (ModShips: the kit's materials applied) and shows at the kit's scale and yaw, like the parts.
// Primary / secondary models sit on the mounts in WeaponSystem's order (the n-th primary on the n-th primary mount; a weapon
// past the mounts shows nothing more), turned by the mount's "rotation" (MountTurn: a gun on a slanted wing, Galaxy on Fire
// 3's hardpoints) and move that gun's fire point to the model's muzzle (MuzzleOffset); the turret
// replaces PlayerTurret's invisible stand-in (TurretModel). Shown on the player's ship in flight and on the hangar
// turntable (AttachWeapons), the turret also on other players' ships (PlayerTurret.BuildStatic). A mount's "nodes" (parts of
// the ship's own model, a hardpoint's pylon) show only while the mount carries a weapon (ShowMountNodes, from AttachWeapons:
// any ship with such mounts, every time the weapons change).
// "toggle": true lets the player hide them (the Customize screen's Weapon models row, Build.weapons "weapons=off", Hidden): no
// weapon or turret models, every pylon hidden, the shots from the mounts themselves (the turret an invisible stand-in), the
// hangar height without them.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModHardpoints
    {
        public sealed class Rule
        {
            public string slot, model;
            public List<string> items = new List<string>();
            public List<int> categories = new List<int>();
            public ModShipSounds.WeaponSound sound;   // "sound" (ModShipSounds), null = the weapon's own
            public bool Unfiltered => items.Count == 0 && categories.Count == 0;
        }

        public sealed class Set
        {
            public string turret;
            /// <summary>"toggle": the player may hide the weapon models, pylons and turret (Build.weapons).</summary>
            public bool toggle;
            public readonly List<Rule> weapons = new List<Rule>();
        }

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "turret", "weapons", "toggle" };
        static readonly HashSet<string> RuleFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "slot", "model", "items", "categories", "sound" };
        static readonly string[] Slots = { "primary", "secondary", "turret" };

        /// <summary>A kit's "hardpoints" (ModShipKits.Read); null without one.</summary>
        internal static Set Read(JObject kit, ModInfo mod, string file, string where)
        {
            if (!(ModJson.Get(kit, "hardpoints") is JToken t)) return null;
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"hardpoints\" must be an object {{ \"turret\": ..., \"weapons\": [ ... ] }}");
            foreach (var prop in o.Properties())
                if (!Fields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
            var set = new Set { turret = ModJson.Str(o, "turret"), toggle = ModJson.Bool(o, "toggle", false, file) };
            if (!string.IsNullOrEmpty(set.turret) && !mod.Source.Exists(set.turret))
                throw new ModJsonException($"{where}: the turret model \"{set.turret}\" isn't in the mod");
            if (ModJson.Get(o, "weapons") is JToken wt)
            {
                if (!(wt is JArray list)) throw new ModJsonException($"{where}: \"hardpoints\": \"weapons\" must be a list of rules");
                foreach (var e in list)
                {
                    string ewhere = ModJson.Where(e, file);
                    if (!(e is JObject r)) throw new ModJsonException($"{ewhere}: each weapon rule must be an object {{ \"slot\", \"model\", ... }}");
                    foreach (var prop in r.Properties())
                        if (!RuleFields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, file)}: unknown field \"{prop.Name}\" (ignored)");
                    var rule = new Rule { slot = (ModJson.Str(r, "slot") ?? "").ToLowerInvariant(), model = ModJson.Str(r, "model") ?? "",
                                          items = ModJson.Strings(r, "items") };
                    rule.sound = ModShipSounds.ParseWeapon(r, mod, file);
                    if (!Slots.Contains(rule.slot)) throw new ModJsonException($"{ewhere}: \"slot\" must be \"primary\", \"secondary\" or \"turret\"");
                    if (rule.model.Length > 0 && !mod.Source.Exists(rule.model)) throw new ModJsonException($"{ewhere}: the model \"{rule.model}\" isn't in the mod");
                    foreach (var c in ModJson.Strings(r, "categories"))
                    {
                        if (!int.TryParse(c, out int n)) throw new ModJsonException($"{ewhere}: \"categories\" must be item category numbers");
                        rule.categories.Add(n);
                    }
                    set.weapons.Add(rule);
                }
            }
            return set;
        }

        /// <summary>The model files a kit's hardpoints use (ModShips loads them with the parts).</summary>
        public static IEnumerable<string> Files(ModShipKits.Kit kit)
        {
            if (kit?.hardpoints == null) yield break;
            if (!string.IsNullOrEmpty(kit.hardpoints.turret)) yield return kit.hardpoints.turret;
            foreach (var r in kit.hardpoints.weapons) if (r.model.Length > 0) yield return r.model;
        }

        static ModShipKits.Kit KitOfShip(int ship) => ModShipKits.KitOf(ModShipKits.KitShip(ship)) is ModShipKits.Kit k && k.hardpoints != null ? k : null;

        /// <summary>Whether a kit ship lets the player hide its weapon models ("toggle": true).</summary>
        public static bool Toggles(int ship) => KitOfShip(ship)?.hardpoints.toggle == true;

        /// <summary>The weapon models, pylons and turret model hidden on 'ship' by 'shown' (null = the local player's build).</summary>
        public static bool Hidden(int ship, ModShipKits.Build shown = null) =>
            Toggles(ship) && (shown ?? ModShipKits.PlayerBuild(ship))?.WeaponsHidden == true;

        static bool Matches(Rule r, ItemData item)
        {
            if (r.Unfiltered) return true;
            if (r.categories.Contains(item.categoryId)) return true;
            foreach (var s in r.items)
            {
                if (int.TryParse(s, out int n)) { if (n == item.index || n == item.Look) return true; }
                else if (!string.IsNullOrEmpty(item.modKey) && string.Equals(s, item.modKey, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>The model file of 'item' in a slot ("primary" / "secondary" / "turret"), null = none.</summary>
        static string ModelFile(ModShipKits.Kit kit, string slot, ItemData item)
        {
            if (kit?.hardpoints == null || item == null) return null;
            foreach (var r in kit.hardpoints.weapons)
                if (r.slot == slot && Matches(r, item)) return r.model.Length > 0 ? r.model : null;
            return null;
        }

        /// <summary>The "sound" of the rule that gives 'item' its model on a kit ship (its slot by the item's type), null = none.</summary>
        public static ModShipSounds.WeaponSound SoundOf(int ship, ItemData item)
        {
            var kit = KitOfShip(ship);
            if (kit == null || item == null) return null;
            foreach (var r in kit.hardpoints.weapons)
                if (r.slot == item.type && Matches(r, item)) return r.sound;
            return null;
        }

        static Transform Find(Transform root, Func<string, bool> name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && name(t.name.ToLowerInvariant())) return t;
            return null;
        }

        static Transform MuzzleOf(Transform root) => Find(root, n => n == "muzzle" || n.StartsWith("muzzle"));

        /// <summary>A point of a template in its own frame (the template's root is unrotated and unscaled).</summary>
        static Vector3 Local(GameObject template, Transform node) => template.transform.InverseTransformPoint(node.position);

        static Quaternion KitTurn(ModShipKits.Kit kit) => Quaternion.Euler(0f, kit.yaw, 0f);

        /// <summary>How a primary / secondary mount turns its weapon model (the ship model's frame): its "rotation" (Euler
        /// degrees, or a quaternion), then upsideDown's half turn about the ship's length.</summary>
        public static Quaternion MountTurn(WeaponMount m)
        {
            var q = Quaternion.identity;
            var r = m?.rotation;
            if (r != null && r.Length >= 4) q = new Quaternion(r[0], r[1], r[2], r[3]).normalized;
            else if (r != null && r.Length >= 3) q = Quaternion.Euler(r[0], r[1], r[2]);
            return m != null && m.upsideDown ? q * Quaternion.Euler(0f, 0f, 180f) : q;
        }

        /// <summary>A holder at the kit's turn and scale with a copy of 'template' in it.</summary>
        static Transform Holder(Transform parent, string name, Vector3 localPosition, Quaternion extraTurn, ModShipKits.Kit kit, GameObject template)
        {
            var h = new GameObject(name).transform;
            h.SetParent(parent, false);
            h.localPosition = localPosition;
            h.localRotation = extraTurn * KitTurn(kit);
            h.localScale = Vector3.one * kit.scale;
            if (template != null)
            {
                var copy = UnityEngine.Object.Instantiate(template, h, false);
                copy.name = template.name;
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;
                copy.SetActive(true);
            }
            return h;
        }

        // The weapon and turret models in a build's colours (ES2 lights its missile pod's bay frame in Emissive 2, paints it in
        // the hull colours): copies of their materials per build's colours, reused (the weapons are attached again whenever
        // they change).
        static readonly Dictionary<string, Dictionary<Material, Material>> coloured = new Dictionary<string, Dictionary<Material, Material>>();
        static int colouredRevision = -1;

        static void Colour(GameObject root, ModShipKits.Kit kit, ModShipKits.Build build)
        {
            if (root == null || kit == null || build == null || kit.colorSlots.Count == 0 || build.colors.Count == 0) return;
            if (colouredRevision != ModManager.Revision) { coloured.Clear(); colouredRevision = ModManager.Revision; }
            string key = kit.Key + "|" + string.Join(";", build.colors.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
            if (!coloured.TryGetValue(key, out var cache))
            {
                if (coloured.Count >= 32) coloured.Clear();
                coloured[key] = cache = new Dictionary<Material, Material>();
            }
            ModShips.ApplyColors(root, kit, build, cache);
        }

        static void Layered(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
        }

        /// <summary>The kit turret for 'item' on 'ship' (a kit ship with a "turret" model), in the ship's frame: "Turret" ->
        /// "pivot" (turns about its up) -> "kit_gun" (tilts about its right, barrel along its forward) -> "muzzle", the meshes
        /// under them at the kit's turn and scale, the item's turret weapon (if a rule gives one) on the gun with its muzzle on
        /// the turret's. Null = no kit turret (PlayerTurret's stand-in). Its pivot / gun nodes are assumed unrotated.</summary>
        public static GameObject TurretModel(int ship, ItemData item, ModShipKits.Build shown = null)
        {
            var kit = KitOfShip(ship);
            if (kit == null || string.IsNullOrEmpty(kit.hardpoints.turret) || Hidden(ship, shown)) return null;
            var t = ModShips.PartTemplate(kit, kit.hardpoints.turret);
            if (t == null) return null;
            var p = Find(t.transform, n => n == "pivot");
            var g = p != null ? Find(p, n => n.Contains("gun")) : null;
            if (p == null || g == null)
            {
                Warn(kit.mod, $"{ModShipKits.File}: {kit.localId}: the turret model {kit.hardpoints.turret} needs a \"pivot\" node with a \"...gun\" node under it");
                return null;
            }
            var u = MuzzleOf(g) ?? MuzzleOf(t.transform);
            Vector3 K(Vector3 v) => KitTurn(kit) * (v * kit.scale);   // the kit's frame -> the ship's
            Vector3 pp = Local(t, p), gp = Local(t, g), up = u != null ? Local(t, u) : gp;

            var root = new GameObject("Turret");
            // The base: the model without its pivot's subtree.
            var baseHolder = Holder(root.transform, "base", Vector3.zero, Quaternion.identity, kit, t);
            var cut = Find(baseHolder, n => n == "pivot");
            if (cut != null) UnityEngine.Object.DestroyImmediate(cut.gameObject);
            // The pivot's own meshes (without the gun), then the gun's.
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = K(pp);
            var pivotHolder = Holder(pivot, "pivot meshes", Vector3.zero, Quaternion.identity, kit, p.gameObject);
            var gunCut = Find(pivotHolder, n => n.Contains("gun"));
            if (gunCut != null) UnityEngine.Object.DestroyImmediate(gunCut.gameObject);
            var gun = new GameObject("kit_gun").transform;
            gun.SetParent(pivot, false);
            gun.localPosition = K(gp - pp);
            Holder(gun, "gun meshes", Vector3.zero, Quaternion.identity, kit, g.gameObject);
            var muzzle = new GameObject("muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = K(up - gp);
            // The item's turret weapon, its muzzle on the turret's.
            string wfile = ModelFile(kit, "turret", item);
            var w = wfile != null ? ModShips.PartTemplate(kit, wfile) : null;
            if (w != null)
            {
                var wm = MuzzleOf(w.transform);
                var at = u != null && wm != null ? muzzle.localPosition - K(Local(w, wm)) : Vector3.zero;
                // A turret without a muzzle of its own: the weapon on the gun node, its muzzle the turret's (the shots, the
                // plasma stream leave there).
                if (u == null && wm != null) muzzle.localPosition = K(Local(w, wm));
                Holder(gun, "weapon", at, Quaternion.identity, kit, w);
            }
            Colour(root, kit, shown ?? ModShipKits.PlayerBuild(ship));
            return root;
        }

        /// <summary>The primary and secondary weapon models of 'equipment' on the build's mounts of 'ship' (a kit ship with
        /// weapon rules), under 'shipModel' in one "Hardpoints" object; null = none. 'shown': the build the model shows (another
        /// player's ship; null = Database.MountsOf, the local player's).</summary>
        public static GameObject AttachWeapons(Database db, int ship, IList<ItemStack> equipment, Transform shipModel, ModShipKits.Build shown = null)
        {
            if (shipModel == null) return null;
            bool hidden = Hidden(ship, shown);
            ShowMountNodes(db, ship, hidden ? null : equipment, shipModel, shown);
            var kit = KitOfShip(ship);
            if (kit == null || kit.hardpoints.weapons.Count == 0 || hidden) return null;
            GameObject root = null;
            foreach (bool secondary in new[] { false, true })
            {
                var mounts = shown != null ? ModShipKits.MountsShown(db, ship, secondary ? 1 : 0, shown) : db.MountsOf(ship, secondary ? 1 : 0);
                var items = WeaponItems(db, equipment, secondary);
                for (int i = 0; i < items.Count && i < mounts.Count; i++)
                {
                    string file = ModelFile(kit, secondary ? "secondary" : "primary", items[i]);
                    var t = file != null ? ModShips.PartTemplate(kit, file) : null;
                    if (t == null) continue;
                    if (root == null)
                    {
                        root = new GameObject("Hardpoints");
                        root.transform.SetParent(shipModel, false);
                    }
                    Holder(root.transform, (secondary ? "secondary " : "primary ") + (i + 1), Flight.WeaponSystem.MountToLocal(mounts[i]),
                           MountTurn(mounts[i]), kit, t);
                }
            }
            if (root != null)
            {
                Layered(root, shipModel.gameObject.layer);
                Colour(root, kit, shown ?? ModShipKits.PlayerBuild(ship));
            }
            return root;
        }

        /// <summary>Each primary / secondary mount's "nodes" (WeaponMount.nodes: its pylon in the ship's model) shown while the
        /// mount carries a weapon (each weapon on the mount of its slot, Hangar.MountOrder), hidden while it is empty ('equipment' null:
        /// every one hidden, the weapon models hidden by the build).</summary>
        static void ShowMountNodes(Database db, int ship, IList<ItemStack> equipment, Transform shipModel, ModShipKits.Build shown)
        {
            Dictionary<string, Transform> byName = null;
            foreach (bool secondary in new[] { false, true })
            {
                var mounts = shown != null ? ModShipKits.MountsShown(db, ship, secondary ? 1 : 0, shown) : db.MountsOf(ship, secondary ? 1 : 0);
                if (mounts == null || !mounts.Exists(m => m.nodes != null && m.nodes.Length > 0)) continue;
                if (byName == null)
                {
                    byName = new Dictionary<string, Transform>();
                    foreach (var t in shipModel.GetComponentsInChildren<Transform>(true)) byName.TryAdd(t.name, t);
                }
                var items = equipment == null ? new List<ItemData>() : WeaponItems(db, equipment, secondary);
                for (int i = 0; i < mounts.Count; i++)
                {
                    if (mounts[i].nodes == null) continue;
                    bool used = i < items.Count && items[i] != null;
                    foreach (var n in mounts[i].nodes)
                        if (n != null && byName.TryGetValue(n, out var node) && node.gameObject.activeSelf != used) node.gameObject.SetActive(used);
                }
            }
        }

        /// <summary>The primary (or secondary) weapon item on each mount (WeaponSystem.Setup's), null for an empty slot
        /// between two weapons (Hangar.MountOrder: each weapon on the mount of its slot, as Level::createPlayer does).</summary>
        public static List<ItemData> WeaponItems(Database db, IList<ItemStack> equipment, bool secondary)
        {
            var list = new List<ItemData>();
            if (equipment == null) return list;
            foreach (int i in Hangar.MountOrder(db, equipment, secondary ? 1 : 0))
            {
                var it = i >= 0 ? db.Item(equipment[i].item) : null;
                list.Add(it != null && it.type == (secondary ? "secondary" : "primary") ? it : null);
            }
            return list;
        }

        /// <summary>Where a mount's weapon model has its muzzle, from the mount (Unity ship space; zero without a model or a
        /// muzzle node): 'shown' is the item whose model the mount shows (its first weapon).</summary>
        public static Vector3 MuzzleOffset(int ship, ItemData shown, bool secondary, WeaponMount mount)
        {
            var kit = KitOfShip(ship);
            if (kit == null || Hidden(ship)) return Vector3.zero;   // no model: the shots leave from the mount
            string file = ModelFile(kit, secondary ? "secondary" : "primary", shown);
            var t = file != null ? ModShips.PartTemplate(kit, file) : null;
            var mz = t != null ? MuzzleOf(t.transform) : null;
            if (mz == null) return Vector3.zero;
            var turn = MountTurn(mount) * KitTurn(kit);
            return turn * (Local(t, mz) * kit.scale);
        }

        // ---- the hangar parking height of a customizable ship ----------------------------------------------------------

        public const float HoverGap = 1.5f;   // Unity metres between the lowest point of a parked kit ship and the floor
        static readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        static int heightsRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { heights.Clear(); heightsRevision = -1; coloured.Clear(); colouredRevision = -1; }

        /// <summary>The pivot height (game units above the floor, like StationTables.ShipY) at which a customizable ship's model
        /// 'assembly' (a build variant, ModShipKits.VariantAssembly) floats HoverGap clear of the floor: its lowest point, its
        /// weapon models under it and a turret hung under the hull included ('equipment': the mounted items, null = none;
        /// 'shown': the build whose mounts they sit on, null = Database.MountsOf). Null = no kit ship. Measured on the model's
        /// template, kept per model and weapons once the build's parts are loaded (the default build stands in meanwhile).</summary>
        public static float? HangarHeight(Database db, int ship, string assembly, IList<ItemStack> equipment, ModShipKits.Build shown = null)
        {
            if (ModShipKits.KitShip(ship) == null || string.IsNullOrEmpty(assembly)) return null;
            if (heightsRevision != ModManager.Revision) { heights.Clear(); heightsRevision = ModManager.Revision; }
            if (Hidden(ship, shown)) equipment = null;   // no weapon models under the hull
            string key = assembly + "|" + (equipment == null ? "" : string.Join(",", equipment.Select(e => e.item)));
            if (heights.TryGetValue(key, out float known)) return known;
            var t = ModShips.Template(assembly);
            if (t == null) return null;
            float bottom = ModelBottom(t);
            var kit = KitOfShip(ship);
            if (kit != null && equipment != null) bottom = Mathf.Min(bottom, HardpointsBottom(db, kit, ship, equipment, shown));
            float h = (HoverGap - bottom) / ModelScaleM;
            if (t.name == assembly) heights[key] = h;   // not the default build standing in while the parts load
            return h;
        }

        const float ModelScaleM = 0.05f;   // metres per game unit

        /// <summary>The lowest point of a ship model in its own frame (Unity metres; its meshes' boxes, not the glow parts).</summary>
        static float ModelBottom(GameObject model)
        {
            var asm = model.GetComponent<Visuals.AssembledObject>();
            var skip = new List<Transform>();
            if (asm != null)
            {
                if (asm.playerVariantParts != null) foreach (var p in asm.playerVariantParts) if (p != null) skip.Add(p.transform);
                if (asm.npcVariantParts != null) foreach (var p in asm.npcVariantParts) if (p != null) skip.Add(p.transform);
            }
            var renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && !skip.Exists(s => r.transform.IsChildOf(s))).ToArray();
            return renderers.Length > 0 ? ModShipBuilder.BoundsIn(model.transform, renderers).min.y : 0f;
        }

        /// <summary>The lowest point of the weapon models (and a turret hung under the hull) the equipment shows on the ship.</summary>
        static float HardpointsBottom(Database db, ModShipKits.Kit kit, int ship, IList<ItemStack> equipment, ModShipKits.Build shown)
        {
            float low = float.MaxValue;
            void Add(GameObject template, Vector3 at, Quaternion extraTurn)
            {
                if (template == null) return;
                var r = template.GetComponentsInChildren<Renderer>(true).Where(x => x is MeshRenderer || x is SkinnedMeshRenderer).ToArray();
                if (r.Length == 0) return;
                var b = ModShipBuilder.BoundsIn(template.transform, r);
                var turn = extraTurn * KitTurn(kit);
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    low = Mathf.Min(low, (at + turn * (corner * kit.scale)).y);
                }
            }
            foreach (bool secondary in new[] { false, true })
            {
                var mounts = shown != null ? ModShipKits.MountsShown(db, ship, secondary ? 1 : 0, shown) : db.MountsOf(ship, secondary ? 1 : 0);
                var items = WeaponItems(db, equipment, secondary);
                for (int i = 0; i < items.Count && i < mounts.Count; i++)
                {
                    string file = ModelFile(kit, secondary ? "secondary" : "primary", items[i]);
                    Add(file != null ? ModShips.PartTemplate(kit, file) : null, Flight.WeaponSystem.MountToLocal(mounts[i]), MountTurn(mounts[i]));
                }
            }
            if (!string.IsNullOrEmpty(kit.hardpoints.turret))
            {
                var mounts = shown != null ? ModShipKits.MountsShown(db, ship, 2, shown) : db.MountsOf(ship, 2);
                var turrets = Flight.PlayerTurret.TurretItems(db, equipment);
                for (int i = 0; i < turrets.Count && i < mounts.Count; i++)
                    if (mounts[i].builtIn && mounts[i].upsideDown)
                        Add(ModShips.PartTemplate(kit, kit.hardpoints.turret), Flight.WeaponSystem.MountToLocal(mounts[i]), Quaternion.Euler(0f, 0f, 180f));
            }
            return low;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
