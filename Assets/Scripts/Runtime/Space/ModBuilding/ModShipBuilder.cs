// ModShipBuilder.cs
// A mod's ship as the game's other ships are (an AssembledObject template), built at run time from its glTF model and
// its ships.json entry: the run-time port of PR #34's CustomShipBuilder (the Editor tool that baked the remake's own ships):
//   - the model as "hull", turned by modelPitch / modelYaw / modelRoll (Unity Euler x / y / z), scaled to modelLength game
//     units nose to tail and moved by modelOffset (game units in the ship's frame); a "materials" entry replaces the model's material on the renderers (name contains 'mesh') / submeshes it names (ModMaterials.FromSpec);
//   - the player's engine glow at every exhaust mount (slotType 3; playerVariantParts like *_engine_glow_add): the
//     Phantom's glow shape (a 12-segment disc and a flared ring 0.87 r behind it, radius x1.31) on the shared glow
//     sprite (mat_34813, centre uv (0.46, 0.947)); no exhaust mounts = no flame (ShipExhaust uses the same mounts); a
//     mount's glowSize makes it an ellipse with its own flame length, a glowColor / the ship's engineGlowColor tints it
//     (engineGlowTint: the sprite in white, the colour in the vertex colours);
//     with "flame" materials (EngineFlame, the model's own flame geometry) the glow part stays, empty; with "engineFx"
//     (Galaxy on Fire 3's engine and its particles, EngineFx) it stays without discs and carries those;
//   - the manoeuvring jets at the slotType-4 mounts (LateralThrusters), under the engine glow part;
//   - each "addParts" model (the original's additive layers, e.g. an original-game mod's *_lights_add / *_engine_glow_add /
//     *_engine_add, OriginalModImport) in the hull's frame with the game's material for its role: the lights always on, an
//     "engineGlow" under the engine glow part in place of the discs, an "engine" as the NPC variant;
//   - each "throttleGlow" / "extraGlows" entry: the hull triangles under the lit part of its mask, pushed 'offset' out,
//     additive, driven by ThrottleGlow (with its trails), plus an empty engine_state player part;
//   - no NPC engine parts but an "engine" add part; one LOD level culled at 80000 units like the generic ships.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Modding
{
    public static class ModShipBuilder
    {
        const float M = 0.05f;               // metres per game unit (ShipController.metersPerUnit)
        const float CullDistance = 80000f;   // game units, the generic ships' last visible distance
        const float ReferenceFov = 60f;
        const float GlowMaskThreshold = 0.08f;

        /// <summary>The template for 'c' around the instantiated glTF scene 'model' (re-parented as the hull). A kit ship's
        /// build (ModShipKits) passes the kit's 'kitScale' (Unity metres per model unit: every build of the kit the same size,
        /// not stretched to modelLength) and 'kitYaw', and 'hasFlames' when the kit's materials make engine flames.</summary>
        public static GameObject Build(CustomShipData c, ModInfo mod, GameObject model, float kitScale = 0f, float kitYaw = 0f, bool hasFlames = false,
                                       List<(CustomAddPart spec, GameObject scene)> addParts = null)
        {
            var root = new GameObject(c.assembly);
            var asm = root.AddComponent<AssembledObject>();
            asm.origin = $"mod {mod.Id}";
            asm.lodDistancesGameUnits = new float[0];
            asm.lastVisibleDistanceGameUnits = CullDistance;
            AutoLods.MarkRuntime(root, mod.Id);   // levels of detail made from the hull at run time (ModLodCache)

            model.name = "hull";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = kitScale > 0f ? Quaternion.Euler(0f, kitYaw, 0f) : Quaternion.Euler(c.modelPitch, c.modelYaw, c.modelRoll);
            FlattenSkinned(model);
            ModGltf.LoopAnimations(model);   // its glTF node animations (moving parts) play on a loop, a kit's parts' too
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var specs = c.materials ?? new List<CustomShipMaterial>();
            var mats = new Material[specs.Count];
            // (named by the entry's "material" filter like a kit's: the engine / damage / shield lookups find a material by its
            // name, and every filter entry was "ship_NNN_mod -1" alike)
            for (int i = 0; i < specs.Count; i++)
                mats[i] = ModMaterials.FromSpec(mod, specs[i], !string.IsNullOrEmpty(specs[i].material) ? $"{c.assembly} {specs[i].material}" : $"{c.assembly} {specs[i].mesh}{specs[i].submesh}");
            if (specs.Count > 0)
                foreach (var r in renderers)
                {
                    var mesh = MeshOf(r);
                    var assigned = r.sharedMaterials;
                    int count = Mathf.Max(1, mesh != null ? mesh.subMeshCount : assigned.Length);
                    if (assigned.Length != count) System.Array.Resize(ref assigned, count);
                    for (int s = 0; s < count; s++) { var m = MaterialFor(specs, mats, r.name, s, assigned[s] != null ? assigned[s].name : null); if (m != null) assigned[s] = m; }
                    r.sharedMaterials = assigned;
                    // A normal map from ships.json on a mesh without tangents (a model with no normal texture of its own:
                    // glTFast makes tangents only for those, e.g. an imported .aem's): Unity's, from its UVs, or the map
                    // shades nothing right.
                    if (mesh != null && mesh.isReadable && (mesh.tangents == null || mesh.tangents.Length == 0) && mesh.uv != null && mesh.uv.Length > 0
                        && System.Array.Exists(assigned, a => a != null && a.HasProperty("_BumpMap") && a.GetTexture("_BumpMap") != null))
                        mesh.RecalculateTangents();
                }
            foreach (var r in renderers) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            // "engine" materials glow only while the engines run (a parked ship's nozzles dark; EngineMaterialGlow).
            DamageEmbers.Add(root, specs.Select((sp, i) => sp != null && (!string.IsNullOrEmpty(sp.damageEmission) || !string.IsNullOrEmpty(sp.damageDetail)) && mats[i] != null ? mats[i].name : null).Where(n => n != null));
            EngineMaterialGlow.Add(root, specs.Select((sp, i) => sp != null && sp.engine && mats[i] != null ? mats[i].name : null).Where(n => n != null),
                specs.Select((sp, i) => sp != null && sp.boost && mats[i] != null ? mats[i].name : null).Where(n => n != null),
                specs.Select((sp, i) => (sp, i)).Where(x => x.sp != null && x.sp.engine && x.sp.engineIdle > 0f && mats[x.i] != null)
                     .GroupBy(x => mats[x.i].name).ToDictionary(g => g.Key, g => g.First().sp.engineIdle));
            // (a capital ship with "engineStyle": "es2": EVERSPACE 2's engine effects at its sockets, without flame cards)
            bool flames = specs.Any(sp => sp != null && sp.flame) || hasFlames || string.Equals(c.engineStyle, "es2", System.StringComparison.OrdinalIgnoreCase);
            if (flames) EngineFlame.Add(root);
            if (kitScale > 0f) model.transform.localScale = Vector3.one * kitScale;
            else
            {
                float length = BoundsIn(root.transform, renderers).size.z;
                if (length > 0f) model.transform.localScale *= c.modelLength * M / length;
                // modelOffset: the sized hull moved in the ship's frame (game units, x mirrored like the mounts, MountToLocal).
                if (c.modelOffset != null && c.modelOffset.Length >= 3)
                    model.transform.localPosition = new Vector3(-c.modelOffset[0], c.modelOffset[1], c.modelOffset[2]) * M;
            }

            var parts = new List<GameObject>();
            var npcParts = new List<GameObject>();
            bool ownGlow = addParts != null && addParts.Exists(p => p.spec.role == CustomAddPart.EngineGlow);
            // A ship with its own engine flames ("flame" materials) keeps the glow part (it marks the engines on / off for
            // the flames, the exhaust particles and the "engine" materials) without its glow discs.
            // Galaxy on Fire 3's engine effect (engineFx) replaces the discs too, drawn under the same part (EngineFx), and so
            // does the model's own engine glow (an "engineGlow" add part, the original's *_engine_glow_add).
            var glow = BuildEngineGlow(c, !flames && c.engineFx == null && !ownGlow);
            if (glow == null && ownGlow) glow = new GameObject("engine_glow");
            if (glow != null) parts.Add(glow);
            // The original's additive layers beside the hull (ships.json "addParts"), as the game's own ships have them
            // (Globals::getShipGroup): the lights always, the player's engine glow with the engines (under the glow part),
            // the NPCs' engine glow as the NPC variant.
            if (addParts != null)
                foreach (var (spec, scene) in addParts)
                {
                    var part = BuildAddPart(c, mod, spec, scene, model.transform);
                    if (part == null) continue;
                    if (spec.role == CustomAddPart.EngineGlow) part.transform.SetParent(glow.transform, false);
                    else
                    {
                        part.transform.SetParent(root.transform, false);
                        if (spec.role == CustomAddPart.Engine) npcParts.Add(part);
                    }
                }
            if (glow != null && c.engineFx != null) EngineFx.Add(root, glow, c, mod);
            var glowSpecs = new List<CustomThrottleGlow> { c.throttleGlow };
            if (c.extraGlows != null) glowSpecs.AddRange(c.extraGlows);
            GameObject engineState = null;
            for (int gi = 0; gi < glowSpecs.Count; gi++)
            {
                var tg = BuildThrottleGlow(c, mod, glowSpecs[gi], gi == 0 ? "" : "_" + (gi + 1), root.transform, renderers);
                if (tg == null) continue;
                tg.transform.SetParent(root.transform, false);
                if (engineState == null) { engineState = new GameObject("engine_state"); parts.Add(engineState); }
                tg.GetComponent<ThrottleGlow>().engineState = engineState;
            }
            foreach (var p in parts) p.transform.SetParent(root.transform, false);
            // Manoeuvring jets (slotType 4: EVERSPACE 2's lateral thrusters), under the engine glow part so they go with it,
            // and EVERSPACE 2's engine lights, trails and glow at the engine and booster sockets of a ship with its flames,
            // sized in its metres (the kit's scale).
            float es2Scale = model.transform.localScale.x;
            LateralThrusters.Add(root, glow != null ? glow.transform : null, c.mounts, es2Scale);
            if (flames) Es2EngineVfx.Add(root, c.mounts, es2Scale, c.es2Engine);
            asm.playerVariantParts = parts.ToArray();
            asm.npcVariantParts = npcParts.ToArray();
            asm.conditionalParts = new GameObject[0];
            asm.conditions = new string[0];

            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.5f, root.GetComponentsInChildren<Renderer>(true)) });
            group.RecalculateBounds();
            float tanHalf = Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);
            // A capital ship (a mod's 4 km hull) stays in sight as far as its size allows: 25 times its size, the generic ships'
            // CullDistance at least (a fighter's 4 km).
            float cull = Mathf.Max(CullDistance, group.size / M * 25f);
            asm.lastVisibleDistanceGameUnits = cull;
            float h = Mathf.Clamp01(group.size / (2f * cull * M * tanHalf));
            group.SetLODs(new[] { new LOD(h, root.GetComponentsInChildren<Renderer>(true)) });
            return root;
        }

        /// <summary>One add part: its glTF scene in a holder that takes the hull model's place, turn and size (the original's
        /// parts share the hull's origin), its renderers named with the game's ending for the role ("_lights_add" ...: the
        /// hull-only code, collision, shadows, levels of detail, aim spots, skips those) and drawn with the role's material
        /// (AddPartMaterial), white vertex colours where the mesh has none (the game's additive shader multiplies by them), no
        /// shadows. Null when the scene has nothing to draw.</summary>
        static GameObject BuildAddPart(CustomShipData c, ModInfo mod, CustomAddPart spec, GameObject scene, Transform hull)
        {
            FlattenSkinned(scene);
            var renderers = scene.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Object.Destroy(scene); return null; }
            string name = c.assembly + CustomAddPart.Suffix(spec.role);
            var holder = new GameObject(name);
            holder.transform.localPosition = hull.localPosition;
            holder.transform.localRotation = hull.localRotation;
            holder.transform.localScale = hull.localScale;
            scene.transform.SetParent(holder.transform, false);
            scene.transform.localPosition = Vector3.zero;
            scene.transform.localRotation = Quaternion.identity;
            scene.transform.localScale = Vector3.one;
            scene.name = name;
            var mat = AddPartMaterial(c, mod, spec, OwnTexture(renderers));
            foreach (var r in renderers)
            {
                r.gameObject.name = name;
                var mesh = MeshOf(r);
                int count = Mathf.Max(1, mesh != null ? mesh.subMeshCount : r.sharedMaterials.Length);
                if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, count).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (mesh != null && mesh.isReadable && (mesh.colors32 == null || mesh.colors32.Length == 0))
                {
                    var white = new Color32[mesh.vertexCount];
                    for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
                    mesh.colors32 = white;
                }
            }
            return holder;
        }

        /// <summary>The material an add part is drawn with: the game's for that role on the ship the model replaces, else on a
        /// game ship of its race (the lights and NPC engines: the race's glow, 3460x; the player's engine glow: 34813), else the
        /// mods' engine glow; with the part's 'texture', else its model's own base colour texture ('own'), a copy showing that
        /// instead (a player's report: a custom engine glow model with its own texture showed the race's glow sprite; the
        /// original-game imports' parts, AemImport, have no texture and keep the game's).</summary>
        internal static Material AddPartMaterial(CustomShipData c, ModInfo mod, CustomAddPart spec, (Texture tex, Vector2 scale, Vector2 offset) own = default)
        {
            var baseMat = GameAddMaterial(c, CustomAddPart.Suffix(spec.role)) ?? ModAssets.Get()?.engineGlow;
            if (baseMat == null) return null;
            Texture tex = null;
            Vector2 scale = Vector2.one, offset = Vector2.zero;
            if (!string.IsNullOrEmpty(spec.texture)) tex = ModMaterials.Texture(mod, spec.texture, false);
            if (tex == null && own.tex != null) (tex, scale, offset) = own;
            if (tex == null) return baseMat;
            var copy = new Material(baseMat) { name = $"{c.assembly} {spec.role} {tex.name}" };
            foreach (var p in new[] { "_MainTex", "_BaseMap" })
                if (copy.HasProperty(p)) { copy.SetTexture(p, tex); copy.SetTextureScale(p, scale); copy.SetTextureOffset(p, offset); }
            return copy;
        }

        /// <summary>The base colour texture the part's glTF materials bring (ModMaterials.FromGltf's _BaseMap, with its Y flip),
        /// the first found; none = default (the Lit templates' base map is empty: a model without textures, or without any
        /// material, ModMaterials.Default, has none). Never read off the templates themselves: in the Editor a template's
        /// first property read re-checks its keywords, drops the faint emission's _EMISSION and marks it dirty
        /// (ModAssetsGuard's case).</summary>
        internal static (Texture tex, Vector2 scale, Vector2 offset) OwnTexture(IEnumerable<Renderer> renderers)
        {
            foreach (var r in renderers)
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") is Texture t)
                        return (t, m.GetTextureScale("_BaseMap"), m.GetTextureOffset("_BaseMap"));
            return default;
        }

        /// <summary>The game's material for the part ending 'suffix' ("_lights_add" ...): on the original ship 'c' overrides,
        /// else the first game ship of c's race that has such a part. Null = none found.</summary>
        static Material GameAddMaterial(CustomShipData c, string suffix)
        {
            var db = Database.Shared;
            if (db == null) return null;
            Material Of(int ship)
            {
                var prefab = AssembledObject.LoadPrefab(db.ShipAssembly(ship, original: true));
                if (prefab == null) return null;
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    if (r.name.EndsWith(suffix) && r.sharedMaterial != null) return r.sharedMaterial;
                return null;
            }
            if (c.index >= 0 && c.index < GameNames.OriginalShips && Of(c.index) is Material own) return own;
            for (int i = 0; i < GameNames.OriginalShips; i++)
                if (i != 13 && i != 14 && i != 15 && Shop.RaceOfShip(i) == c.race && Of(i) is Material m) return m;
            return null;
        }

        /// <summary>Skinned meshes (a GLB whose hull is bound to a skeleton, common in exported game models) baked into plain
        /// meshes in their rest pose: a ship never animates, and everything that measures or collides with the hull (the
        /// length fit, the hangar height, mesh collision, the shadow bake, the Mod Builder's hull picking) reads the mesh in
        /// its bind pose, while the bones draw it elsewhere and at another size.</summary>
        internal static void FlattenSkinned(GameObject model)
        {
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var baked = new Mesh { name = smr.sharedMesh.name };
                baked.indexFormat = smr.sharedMesh.indexFormat;
                smr.BakeMesh(baked);   // the drawn pose, in the renderer's own space without its scale (the transform adds it)
                baked.RecalculateBounds();
                var go = smr.gameObject;
                var mats = smr.sharedMaterials;
                Object.DestroyImmediate(smr);
                go.AddComponent<MeshFilter>().sharedMesh = baked;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
        }

        /// <summary>A kit's "materials" onto a part's renderers (ModShipKits; the same rules as a ship's).</summary>
        internal static void ApplyMaterials(ModInfo mod, List<CustomShipMaterial> specs, Renderer[] renderers, string label)
        {
            if (specs == null || specs.Count == 0) return;
            var mats = new Material[specs.Count];
            // Named by the entry's 'material' filter when it has one, so a kit's colour targets find it again (ModShipKits).
            for (int i = 0; i < specs.Count; i++)
                mats[i] = ModMaterials.FromSpec(mod, specs[i], !string.IsNullOrEmpty(specs[i].material) ? specs[i].material : $"{label} {specs[i].mesh}{specs[i].submesh}");
            foreach (var r in renderers)
            {
                var mesh = MeshOf(r);
                var assigned = r.sharedMaterials;
                int count = Mathf.Max(1, mesh != null ? mesh.subMeshCount : assigned.Length);
                if (assigned.Length != count) System.Array.Resize(ref assigned, count);
                for (int sm = 0; sm < count; sm++) { var m = MaterialFor(specs, mats, r.name, sm, assigned[sm] != null ? assigned[sm].name : null); if (m != null) assigned[sm] = m; }
                r.sharedMaterials = assigned;
            }
        }

        internal static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;

        internal static Bounds BoundsIn(Transform root, Renderer[] renderers)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                var mesh = MeshOf(r);
                if (mesh == null) continue;
                var mb = mesh.bounds;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>The entry for a renderer's submesh: one naming that submesh first, else the first for every submesh
        /// (submesh -1); its 'mesh' must be part of the renderer's name (empty = any). Null = keep the model's own.</summary>
        internal static Material MaterialFor(List<CustomShipMaterial> specs, Material[] mats, string rendererName, int submesh, string currentMaterial = null)
        {
            string n = rendererName.ToLowerInvariant();
            string cur = (currentMaterial ?? "").ToLowerInvariant();
            int any = -1;
            for (int i = 0; i < specs.Count; i++)
            {
                var e = specs[i];
                if (!string.IsNullOrEmpty(e.mesh) && !n.Contains(e.mesh.ToLowerInvariant())) continue;
                // 'material': only where the model's own material has that name (a kit's parts name theirs, e.g. EVERSPACE 2's).
                if (!string.IsNullOrEmpty(e.material) && !cur.Contains(e.material.ToLowerInvariant())) continue;
                if (e.submesh == submesh) return mats[i];
                if (e.submesh < 0 && any < 0) any = i;
            }
            return any >= 0 ? mats[any] : null;
        }

        /// <summary>The engine glow at every exhaust mount: the Phantom's shape, a disc and a flared ring behind it. A mount's
        /// glowSize (half width, half height, flame length in game units) makes it an ellipse with its own length (its glowShape
        /// another outline in that box, EngineGlowShape: a rectangle, a hexagon...), its glowColor (else the ship's engineGlowColor) tints it: then the whole glow uses engineGlowTint (the white sprite)
        /// with vertex colours, white-hot at the centre, the colour at the rim and the ring; uncoloured mounts stay white.</summary>
        internal static GameObject BuildEngineGlow(CustomShipData c, bool discs = true)
        {
            var exhausts = c.mounts?.Where(m => m.slotType == 3).ToList();
            var assets = ModAssets.Get();
            if (exhausts == null || exhausts.Count == 0 || assets?.engineGlow == null) return null;
            if (!discs) return new GameObject("engine_glow");
            Color? ColourOf(WeaponMount m)
            {
                var g = m.glowColor != null && m.glowColor.Length >= 3 ? m.glowColor
                      : c.engineGlowColor != null && c.engineGlowColor.Length >= 3 ? c.engineGlowColor : null;
                return g != null ? new Color(g[0], g[1], g[2], 1f) : (Color?)null;
            }
            bool tinted = assets.engineGlowTint != null && exhausts.Any(m => ColourOf(m) != null);
            var mat = tinted ? assets.engineGlowTint : assets.engineGlow;
            var uvCentre = new Vector2(0.46f, 0.947f);
            const float UvDisc = 0.0254f, UvRing = 0.048f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colours = new List<Color>();
            var tris = new List<int>();
            foreach (var m in exhausts)
            {
                float rx = c.engineGlowRadius * M, ry = rx;
                if (m.glowSize != null && m.glowSize.Length >= 2) { rx = m.glowSize[0] * M; ry = m.glowSize[1] * M; }
                float back = m.glowSize != null && m.glowSize.Length >= 3 ? m.glowSize[2] * M : 0.87f * 0.5f * (rx + ry);
                var tint = ColourOf(m) ?? Color.white;
                var core = Color.Lerp(tint, Color.white, 0.55f);
                var o = WeaponSystem.MountToLocal(m);
                int centre = verts.Count;
                verts.Add(o); uvs.Add(uvCentre); colours.Add(core);
                // The outline's points in the unit box; the sprite is read along their direction only (its disc and ring
                // radii), so a square's corners take the rim's glow and nothing of the atlas beyond it.
                var outline = EngineGlowShape.Outline(m.glowShape);
                int segments = outline.Length;
                int disc = verts.Count;
                foreach (var d in outline)
                {
                    verts.Add(o + new Vector3(d.x * rx, d.y * ry, 0f)); uvs.Add(uvCentre + d.normalized * UvDisc); colours.Add(tint);
                }
                int ring = verts.Count;
                foreach (var d in outline)
                {
                    verts.Add(o + new Vector3(d.x * rx * 1.31f, d.y * ry * 1.31f, -back)); uvs.Add(uvCentre + d.normalized * UvRing); colours.Add(tint);
                }
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    tris.AddRange(new[] { centre, disc + i, disc + j });
                    tris.AddRange(new[] { disc + i, ring + i, ring + j, disc + i, ring + j, disc + j });
                }
            }
            var mesh = new Mesh { name = c.assembly + "_engine_glow" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            if (tinted) mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("engine_glow");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        static GameObject BuildThrottleGlow(CustomShipData c, ModInfo mod, CustomThrottleGlow tg, string suffix, Transform root, Renderer[] renderers)
        {
            if (tg == null || string.IsNullOrEmpty(tg.mask)) return null;
            var template = ModAssets.Get()?.engineGlow;
            var maskTex = ModMaterials.Texture(mod, tg.mask, false, true);
            if (maskTex == null || template == null) return null;
            float Lit(Vector2 uv) { var p = maskTex.GetPixelBilinear(uv.x, uv.y); return Mathf.Max(p.r, Mathf.Max(p.g, p.b)); }
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            float offset = tg.offset * M;
            foreach (var r in renderers)
            {
                if (!string.IsNullOrEmpty(tg.mesh) && !r.name.ToLowerInvariant().Contains(tg.mesh.ToLowerInvariant())) continue;
                var mesh = MeshOf(r);
                if (mesh == null || !mesh.isReadable) continue;
                var mv = mesh.vertices;
                var mn = mesh.normals;
                var mu = mesh.uv;
                if (mu == null || mu.Length != mv.Length) continue;
                bool hasNormals = mn != null && mn.Length == mv.Length;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (tg.submesh >= 0 && s != tg.submesh) continue;
                    var t = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector2 a = mu[t[i]], b = mu[t[i + 1]], d = mu[t[i + 2]];
                        if (Mathf.Max(Mathf.Max(Lit(a), Lit(b)), Mathf.Max(Lit(d), Lit((a + b + d) / 3f))) <= GlowMaskThreshold) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int v = t[i + k];
                            var n = hasNormals ? m.MultiplyVector(mn[v]).normalized : Vector3.zero;
                            verts.Add(m.MultiplyPoint3x4(mv[v]) + n * offset);
                            normals.Add(n);
                            uvs.Add(mu[v]);
                        }
                    }
                }
            }
            if (verts.Count == 0)
            {
                string w = $"ships.json: \"{c.name}\": no hull triangle under the glow mask {tg.mask}";
                if (!mod.Warnings.Contains(w)) mod.Warnings.Add(w);
                return null;
            }
            var glowMesh = new Mesh { name = c.assembly + "_throttle_glow" + suffix };
            if (verts.Count > 65535) glowMesh.indexFormat = IndexFormat.UInt32;
            glowMesh.SetVertices(verts);
            glowMesh.SetNormals(normals);
            glowMesh.SetUVs(0, uvs);
            glowMesh.SetTriangles(Enumerable.Range(0, verts.Count).ToArray(), 0);
            glowMesh.RecalculateBounds();

            var tint = tg.color != null && tg.color.Length >= 3 ? new Color(tg.color[0], tg.color[1], tg.color[2], 1f) : Color.white;
            var mat = new Material(template) { name = c.assembly + "_throttle_glow" + suffix };
            mat.SetTexture("_MainTex", maskTex);
            mat.SetTextureScale("_MainTex", Vector2.one);
            mat.SetTextureOffset("_MainTex", Vector2.zero);
            mat.SetColor("_Color", tint);
            mat.SetFloat("_Glow", tg.idle);
            mat.SetFloat("_UseVertexColor", 0f);
            mat.DisableKeyword("_USEVERTEXCOLOR_ON");

            var go = new GameObject("throttle_glow" + suffix);
            go.AddComponent<MeshFilter>().sharedMesh = glowMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var g = go.AddComponent<ThrottleGlow>();
            g.color = tint;
            g.idle = tg.idle;
            g.full = tg.full;
            g.boost = tg.boost;
            g.off = tg.off;
            if (tg.trailWidth > 0f && tg.trailCount > 0)
            {
                // trailCount trails across the glow's width on its rear edge, sized by an ellipse (sqrt(1 - t^2), at least 0.15).
                float minX = verts.Min(v => v.x), maxX = verts.Max(v => v.x);
                int n = Mathf.Max(1, tg.trailCount);
                float slice = (maxX - minX) / n;
                var points = new List<Vector3>();
                var profile = new List<float>();
                for (int i = 0; i < n; i++)
                {
                    float x = n == 1 ? (minX + maxX) * 0.5f : Mathf.Lerp(minX + slice * 0.5f, maxX - slice * 0.5f, i / (float)(n - 1));
                    var inSlice = verts.Where(v => Mathf.Abs(v.x - x) <= slice * 0.5f).ToList();
                    if (inSlice.Count == 0) continue;
                    float rearZ = inSlice.Min(v => v.z);
                    float sliceDepth = inSlice.Max(v => v.z) - rearZ;
                    var edge = inSlice.Where(v => v.z <= rearZ + sliceDepth * 0.1f).ToList();
                    points.Add(new Vector3(x, edge.Average(v => v.y), rearZ));
                    float t = (x - (minX + maxX) * 0.5f) / ((maxX - minX) * 0.5f);
                    profile.Add(Mathf.Max(0.15f, Mathf.Sqrt(Mathf.Max(0f, 1f - t * t))));
                }
                g.trailPoints = points.ToArray();
                g.trailProfile = profile.ToArray();
            }
            else if (tg.trailWidth > 0f)
            {
                // A trail from the glow's rear end on each side (the rearmost 3 % of its length, averaged per side).
                float minZ = verts.Min(v => v.z), maxZ = verts.Max(v => v.z);
                float cut = minZ + (maxZ - minZ) * 0.03f;
                var rear = verts.Where(v => v.z <= cut).ToList();
                var points = new List<Vector3>();
                foreach (var side in new[] { rear.Where(v => v.x < 0f).ToList(), rear.Where(v => v.x >= 0f).ToList() })
                    if (side.Count > 0) points.Add(new Vector3(side.Average(v => v.x), side.Average(v => v.y), minZ));
                g.trailPoints = points.ToArray();
                g.trailProfile = new float[0];
            }
            if (tg.trailWidth > 0f)
            {
                g.trailWidth = tg.trailWidth * M;
                g.trailTime = tg.trailTime > 0f ? tg.trailTime : 0.6f;
                g.trailBrightness = tg.trailBrightness > 0f ? tg.trailBrightness : 0.3f;
            }
            return go;
        }
    }
}
