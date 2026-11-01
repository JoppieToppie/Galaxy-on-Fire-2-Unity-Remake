# Assembled prefabs

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Assembled prefabs

The game stores every object as several meshes (hull, `_lights_add`, `_emissive`, `_engine_add`, `_lod_N`, ...) and
assembles them in code. `Prefabs/<pack>/...` are the single meshes (1:1 with resource ids, use them when code refers to
a mesh id); `Resources/Assembled/<pack>/<category>/` are the assembled objects (root `AssembledObject` + nested per-mesh
prefabs + `LODGroup`).

- Rules: `Reference/tools/asset_conversion/assemblies/rules/assemblies_{ships,stations,level,scenes}.json` +
  `*_notes.md` (functions, addresses, uncertainties). `build_assemblies.py` merges them into
  `Resources/GoF2Data/assemblies.json`; then run the menu item. Groups with no assembling code get a name-based assembly
  (`origin` says so). A full run no longer reproduces the committed file (2026-11-01: 215 lines elsewhere, LOD lists
  among them, changed for one new part): to add a part, run it to a copy and splice that entry's part in.
- `patch_resources.py` added 19 meshes the resource-table emulation had recorded as garbage (burning/wrecked supernova
  stations etc.), and (remake) the engine glows 17939 / 17941 of ships 39 / 41.
- Ships (`Globals::getShipGroup(idx, race, isPlayer)`): the player ship uses `*_engine_glow_add` (17900+idx), NPCs
  `*_engine_add` (18000+idx); both are in the prefab, switch with `SetPlayerVariant()`. Battleship (idx 14) is scaled
  2x. Ship 15 is the freighter, picked by race. Look ships up with `Database.ShipAssembly(idx, race)`: the add-on ships
  (39-41, 44+) are `v_ship_NNN_*` / `sn_ship_NNN_*`.
- A ship hull's own keys play from the start (`ImportPostprocessor.IsAnimatedShipHull` in Materials And Prefabs: under
  ships/, not a wreck or explosion): the Cronus' (`v_ship_037_deep_science`) engine pods sit inside the hull at rest and
  hang bobbing under its stern once played; the import played only "_anim" models, so they were hidden and still. Its
  prefab was set by hand (`play: 1`) rather than by a rebuild. The pods' own keys were then placed and turned onto the
  hull by eye in the Editor (#157: they hung beside it), by hand in the converted sidecar
  `Models/valkyrie/ships/v_ship_037_deep_science.gof2mesh.json` — posX is swapped between part0 / part1 and each carries
  a rotY (the file's rotY is Unity z, see "Coordinates and scale"), so re-running the asset conversion would undo it.
- LOD: each level shows only its LOD mesh, its LOD child meshes and the last-added child (usually the engine). Generic
  ships switch at 5000/13000 units and cull at 80000. **In this binary `AEGeometry::updateLod` always picks LOD 0** (the
  modded APK), the prefabs use the intended distances.
- **Levels of detail** (remake, 2026-10-22, the user's request; Options > Graphics, `Settings.Lods`, on by default on
  every platform (the user's choice; it was phones only at first); `Visuals.AutoLods`, added to every assembled
  object in `AssembledObject.Awake`, set up in Start, the prefabs untouched). An object with the original's own LOD meshes
  (generic ships, freighters, the battleship, jumpgates, asteroids) keeps its LODGroup and switch heights while on; off,
  the group is cut to LOD 0 culled where the last level was and the lower-only renderers get `forceRenderingOff` (= the
  binary's always-LOD-0). Those LOD meshes are, as converted, copies of LOD 0 (the same triangles at every level: the
  generic ships 2900-4900 each, unreadable), all but the deep science station's: `AutoLods.CopiedLevels` finds each copy
  by its LOD 0 hull (counts, bounds, place), and while on it shows that hull's made level instead (`SwapCopied`: level k
  for the original's level k, the deepest there is past it; put back when off); the builder cuts those hulls from 500
  triangles (`LodMeshes.MinTrianglesCopied`, the asteroids). Every other
  object gets made levels (`LodMeshes`: the Mod Builder optimizer's `MeshSimplifier` after joining vertices alike in every
  byte, in its `seams` / `borders` mode: a seam point collapses with all its copies at once, each onto a copy of the
  neighbour it shares a triangle with, and a point on one open border only along it, the border edges' upright planes
  in its quadric; the optimizer keeps both locked. The game's models split 93-97 % of their points by hard edges and UV
  seams and 17-41 % lie on open panel edges: with both locked 142 of 147 hull meshes got no level. The last level is cut
  `loose` too: a seam copy without a partner along the edge goes onto the neighbour's copy nearest it in UV (within
  0.05; the asteroids streaked black and white with any copy). Now 109 meshes have levels, 532k -> 350k -> 142k
  triangles (a generic ship ~4000 -> ~2200 -> ~900); most stations stay whole, being flat-shaded (a copy per face, so
  no copy has a partner along an edge, and level 1 isn't loose) or double-sided (non-manifold);
  level 1 = 50 %, level 2 = 20 % of the triangles; a level keeping over 85 % of the one above is dropped; meshes under
  1000 triangles and additive / light / glow / emissive layers (they sit on the hull) get none): a renderer per level under
  the mesh's own (`<name>_lod_gen1`, a non-readable mesh of only its vertices, so `HullCollision` and the shadow bake's
  "_lod" filters skip it) in a LODGroup: level 1 below 15 %, level 2 below 5 % of the screen's height (Quality's LOD bias
  scales them); a mod ship keeps its 80000-unit cull, an object without a LODGroup gets one that never culls. The level
  renderers copy their source's materials, property block, layer, enabled and forceRenderingOff every LateUpdate (skins,
  cloak, damage, Hide the ship). The game's levels are prebaked: **GoF2 > Build > Generated LODs** (`GeneratedLodsBuilder`)
  writes `Resources/GoF2Data/generated_lods.bytes` (indices only, per `LodMeshes.Key`: name, counts, size) for the ships,
  stations, jumpgates, turrets, asteroids and misc objects (the capital ships, the carrier, battlestations) with no LOD
  meshes of their own, hull models only (`IsHullModel`: readable in a build); `GameLods` makes a level's mesh on first use.
  The mods' ships and stations (`AutoLods.MarkRuntime` in `ModShipBuilder` / `ModStations`) get theirs at run time from
  the hull as shipped, optimized or not (`ModLodCache`: a worker thread; until then LOD 0 alone), kept in
  persistentDataPath/ModCache/Lods/<mod id>/<assembly>_<n>.lods (a name over 96 characters, a kit variant's whole build,
  cut to its start + an FNV-1a hash: past 255 bytes the write failed) headed by the mesh's hash: a changed model
  (optimized
  again, undone) is cut again and its old file deleted; the Optimize window drops the mod's folder (`Forget`) before the
  reload, `ModManager.Scan` the uninstalled mods' (`PruneCache`). Full detail (`AutoLods.FullDetail`, no made levels):
  the player's own ship, the hangar's close-up rooms, the previews, icons, model viewer, menu backdrop, sky layers and gun
  rigs (beside their ForceLOD(0) / LODGroup off). `HullCollision` counts as lower LODs only what LOD 0 doesn't show. Not
  in batch mode (servers).
- Stations are placed rotated (0, pi, 0); stored as `spawnRotationEngine`, not baked in. State-dependent parts (mission,
  race, jumpgate activation) are in `conditionalParts` and start inactive.
- Suns and planets are the `plane` mesh textured per system at runtime, and skyboxes pick layers per system: not pre-assembled.
