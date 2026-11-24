# inventory.py: which of the project's assets come from Galaxy on Fire 2 itself, which are made from those, and which are
# the remake's own (docs/notes/player-assets.md). The originals and what's derived from them can't ship: a player's own
# copy of the game is converted into them at first start. This lists, per folder, what each is and where the player's
# copy has it, and every hard reference (by GUID) from the remake's own scenes, prefabs, materials and ScriptableObjects to
# an original or derived asset: what has to load through the content layer instead.
#
#   python3 Reference/tools/player_assets/inventory.py [--ipa <Galaxy_on_Fire_2_HD.ipa>] [--out Reference/player_assets]
#
# Writes <out>/inventory.json and prints a summary. With --ipa it also checks the rules against the iOS app's files (names,
# FSB sample counts). Runs from the repository root; reads only .meta files and text assets, so LFS pointers are enough.
import argparse, collections, json, os, re, struct, subprocess, sys, zipfile

ORIGINAL, DERIVED, OWN = "original", "derived", "own"

# (path prefix, kind, what it is, where the player's copy has it). First match wins; more specific prefixes first.
RULES = [
    # ---- derived: made by the remake's tools from originals; rebuilt on the player's machine
    ("Assets/Resources/GoF2SkyHd/", DERIVED, "Enhanced skies: Real-ESRGAN upscales and star lists", "optional desktop step (sky/enhance_skies.py)"),
    ("Assets/Resources/GoF2SkyLayers/", DERIVED, "layered skies: patches, masks, cloud masks from the skies", "sky/sky_layers.py, nebula_clouds.py"),
    ("Assets/Resources/GoF2Planets3D/", DERIVED, "3D planets rebuilt from the planet images", "World.RebuiltPlanets' builder"),
    ("Assets/Resources/GoF2Reflections/", DERIVED, "reflection cubemaps from the skies", "from the converted skies"),
    ("Assets/Resources/Assembled/", DERIVED, "assembled prefabs (meshes + materials per assemblies.json)", "built at run time from assemblies.json"),
    ("Assets/Prefabs/", DERIVED, "per-mesh prefabs of the converted models", "built at run time"),
    ("Assets/Materials/", DERIVED, "materials of the game's resource table (textures + shader kind)", "built at run time"),
    ("Assets/Resources/GoF2Weapons/item_", OWN, "weapon fx tables (from weapon_fx.json) pointing at the game's prefabs and sounds", "ship; references refilled (OriginalRefs)"),
    # ---- originals: converted from the player's copy
    ("Assets/Textures/", ORIGINAL, "textures", "AEI (data/textures, data/assets/*/3d/textures)"),
    ("Assets/Models/", ORIGINAL, "meshes (+ .gof2mesh.json animation sidecars)", "AEM (data/meshes, data/assets/*/3d/meshes)"),
    ("Assets/Audio/", ORIGINAL, "sounds, music, voice", "FSB banks (FMOD_GOF2_<bank>.fsb)"),
    ("Assets/Skyboxes/", ORIGINAL, "sky cubemaps", "AEI cubemap strips"),
    ("Assets/Resources/GoF2Sky/", ORIGINAL, "sky cubemaps and sun / nebula images", "AEI"),
    ("Assets/Resources/GoF2Backdrop/", ORIGINAL, "planet, sun and backdrop images", "AEI"),
    ("Assets/Resources/GoF2Station/", ORIGINAL, "station scene images", "AEI"),
    ("Assets/Resources/GoF2Icons/", ORIGINAL, "item, ship and station icons", "regions of the interface AEI atlases"),
    ("Assets/Resources/GoF2Hud/", ORIGINAL, "HUD images", "regions of the interface AEI atlases"),
    ("Assets/Resources/GoF2Races/", ORIGINAL, "race and faction emblems", "regions of the interface AEI atlases"),
    ("Assets/Resources/Sprite Assets/gof2_text_icons.png", ORIGINAL, "inline text icons' atlas", "regions of the interface AEI atlases"),
    ("Assets/UI/Original/", ORIGINAL, "the original interface style's images", "regions of the interface AEI atlases"),
    # (Assets/UI/AppIcon: the remake's icon art, made 2026-10-05; ships, the user's call 2026-11-15: OWN by the fallthrough)
    ("Assets/UI/MainMenu/Images/card_", DERIVED, "main menu campaign cards (key art)", "cut from gof2_campaign_select_ipad_large at run time (OriginalTextures.MenuImage)"),
    ("Assets/UI/MainMenu/Images/logo_fishlabs.png", DERIVED, "the FISHLABS logo of the startup splash", "cut from gof2_logos_1440 at run time"),
    ("Assets/UI/Fonts/Klavika", ORIGINAL, "Klavika, the original HD versions' interface typeface: a commercial font (Process Type Foundry) the project has no licence to ship", "not in the player's copy (its text is glyph images); players who own it import it (Options > Accessibility)"),
    ("Assets/UI/Flight/Images/crosshair", DERIVED, "the HUD's crosshairs", "cut from gof2_interface_iphone4 at run time (UiImageSlots)"),
    ("Assets/UI/MainMenu/Images/logo_gof2.png", DERIVED, "the game's logo (#67's redraw of the atlas' cut)", "cut from gof2_logos_1440 at run time"),
    ("Assets/Localization/text_", ORIGINAL, "the game's text", ".lang files"),
    ("Assets/Localization/GoF2_strings", ORIGINAL, "the game's text (table)", ".lang files"),
    # the fact tables ship (the user, 2026-11-15): IDs, paths, rules and event names the remake recovered, no original bytes
    ("Assets/Resources/GoF2Data/resources.json", OWN, "the resource table's IDs and paths (recovered facts)", ""),
    ("Assets/Resources/GoF2Data/assemblies.json", OWN, "how objects are assembled (the remake's rules)", ""),
    ("Assets/Resources/GoF2Data/story.json", OWN, "the story's step / voice IDs and event names (recovered facts)", ""),
    ("Assets/Resources/GoF2Data/voice_table.json", OWN, "voice IDs to FMOD event names (recovered facts)", ""),
    ("Assets/Resources/GoF2Data/weapon_fx.json", OWN, "weapon effect names (recovered facts)", ""),
    ("Assets/Resources/GoF2Data/step_summaries.json", OWN, "the remake's own step summaries", ""),
    ("Assets/Resources/GoF2Data/hangar_heights.json", OWN, "the remake's measurements", ""),
    ("Assets/Resources/GoF2Data/nozzle_shapes.json", OWN, "the remake's measurements", ""),
    ("Assets/Resources/GoF2Data/void_station_extra.json", OWN, "the remake's collision volumes", ""),
    ("Assets/Resources/GoF2Data/generated_lods.bytes", DERIVED, "simplified LODs of the original meshes", "rebuilt at run time (LodMeshes.Simplify)"),
    ("Assets/Resources/GoF2Data/", ORIGINAL, "game data tables (items, ships, systems, stations...)", ".bin files + the resource table (per file: see notes)"),
    ("Assets/Shaders_Original_GLSL/", ORIGINAL, "the original's shader source (reference only)", "data/shader (not converted: the remake's shaders replace them)"),
    # ---- the remake's own
    ("Assets/Resources/GoF2Localization/", OWN, "the remake's own strings", ""),
    ("Assets/", OWN, "", ""),
]

ASSET_TEXT = (".unity", ".prefab", ".asset", ".mat", ".controller", ".anim", ".uxml", ".uss", ".playable", ".mask", ".overrideController")


# Inside the original / derived folders: the remake's own work, which ships whatever folder it sits in.
OWN_EXT = (".cs", ".shader", ".hlsl", ".cginc", ".uss", ".uxml", ".tss", ".shadergraph", ".compute")
OWN_FILES = {
    # the inline text icons' sprite table (names and cells only; its atlas is a placeholder the game draws: TextIcons)
    "Assets/Resources/Sprite Assets/gof2_text_icons.asset",
    # computed layout data (numbers only): ships like the fact tables (the user, 2026-11-15)
    "Assets/Resources/GoF2SkyLayers/layered_skies.json", "Assets/Resources/GoF2SkyLayers/nebula_clouds.json",
    # seeded noise only (sky_layers.py star_mask / nebula_masks, nebula_clouds.py masks): nothing of the original's
    "Assets/Resources/GoF2SkyLayers/star_mask.png", "Assets/Resources/GoF2SkyLayers/nebula_masks.png",
    "Assets/Resources/GoF2SkyLayers/cloud_masks.png",
    # the template materials and ScriptableObjects scenes and code point at (their texture slots filled at run time)
    "Assets/Resources/GoF2Sky/SpaceSky.mat", "Assets/Resources/GoF2Backdrop/SkyLayerAssets.asset",
}


def classify(path):
    if path.endswith(OWN_EXT) or path in OWN_FILES:
        return OWN, "", "", ""
    if path.startswith("Assets/Resources/GoF2Planets3D/") and path.endswith(".json"):
        return OWN, "", "", ""   # (the rebuilt planets' layout numbers)
    if path.startswith("Assets/Resources/GoF2Station/") and path.endswith((".mat", ".asset")):
        return OWN, "", "", ""   # (the station scene's own material and assets)
    for prefix, kind, what, source in RULES:
        if path.startswith(prefix): return kind, prefix, what, source
    return OWN, "", "", ""


def git_files():
    out = subprocess.run(["git", "ls-files", "Assets"], capture_output=True, text=True, check=True).stdout.split("\n")
    return [f for f in out if f]


def guid_of(meta):
    try:
        with open(meta, encoding="utf-8", errors="replace") as f:
            for line in f:
                if line.startswith("guid:"): return line.split()[1]
    except OSError: pass
    return None


def ipa_check(ipa, files):
    """Name and count checks against the iOS app: textures / meshes by name, audio banks by sample count."""
    z = zipfile.ZipFile(ipa)
    stems = {os.path.splitext(os.path.basename(n))[0].lower() for n in z.namelist() if "." in os.path.basename(n)}
    banks = {}
    for n in z.namelist():
        if n.endswith(".fsb"):
            b = z.open(n).read(12)
            banks[re.sub(r"^FMOD_GOF2_?", "", os.path.basename(n)[:-4]) or "MAIN"] = struct.unpack("<I", b[8:12])[0]
    out = {}
    for prefix in ("Assets/Textures/", "Assets/Models/", "Assets/Skyboxes/", "Assets/Prefabs/"):
        fs = [f for f in files if f.startswith(prefix) and not f.endswith(".meta") and not f.endswith(".json")]
        hit = sum(1 for f in fs if os.path.splitext(os.path.basename(f))[0].lower() in stems
                  or re.sub(r"(_lod\d|_\d+)$", "", os.path.splitext(os.path.basename(f))[0].lower()) in stems)
        out[prefix] = {"files": len(fs), "named like a file in the app": hit}
    audio = collections.Counter(f.split("/")[2] for f in files if f.startswith("Assets/Audio/") and f.endswith((".ogg", ".wav", ".mp3")))
    out["Assets/Audio/"] = {b: {"project": audio.get(b, 0), "bank samples": n} for b, n in sorted(banks.items())}
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ipa"); ap.add_argument("--out", default="Reference/player_assets")
    a = ap.parse_args()
    files = git_files()
    assets = [f for f in files if not f.endswith(".meta")]
    kind_of = {f: classify(f) for f in assets}
    # folders
    folders = collections.OrderedDict()
    for prefix, kind, what, source in RULES:
        if not what: continue
        fs = [f for f in assets if kind_of[f][1] == prefix]
        if not fs: continue
        size = sum(os.path.getsize(f) for f in fs if os.path.exists(f))
        folders[prefix] = {"kind": kind, "what": what, "source": source, "files": len(fs), "bytes_here": size}
    # GUIDs of what can't ship, and who points at them
    blocked = {}
    for f in assets:
        if kind_of[f][0] != OWN:
            g = guid_of(f + ".meta")
            if g: blocked[g] = f
    refs = collections.defaultdict(set)
    rx = re.compile(r"guid[:=] ?([0-9a-f]{32})")
    for f in assets:
        if kind_of[f][0] != OWN or not f.endswith(ASSET_TEXT) or not os.path.exists(f): continue
        try: text = open(f, encoding="utf-8", errors="replace").read()
        except OSError: continue
        if text.startswith("version https://git-lfs"): continue
        for g in set(rx.findall(text)):
            if g in blocked: refs[f].add(blocked[g])
    hard = {f: sorted(v) for f, v in sorted(refs.items())}
    # Resources.Load sites in the runtime code
    loads = collections.Counter()
    for root, _, fs in os.walk("Assets/Scripts/Runtime"):
        for n in fs:
            if n.endswith(".cs"):
                c = open(os.path.join(root, n), encoding="utf-8", errors="replace").read().count("Resources.Load")
                if c: loads[os.path.join(root, n)] = c
    report = {
        "summary": {k: {"files": sum(1 for f in assets if kind_of[f][0] == k)} for k in (ORIGINAL, DERIVED, OWN)},
        "folders": folders,
        "hard_references": hard,
        "resources_load": {"sites": sum(loads.values()), "files": len(loads), "by_file": dict(sorted(loads.items()))},
    }
    if a.ipa: report["ipa_check"] = ipa_check(a.ipa, files)
    os.makedirs(a.out, exist_ok=True)
    json.dump(report, open(os.path.join(a.out, "inventory.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    for k, v in report["summary"].items(): print(f"{k:9s} {v['files']:6d} files")
    print()
    for p, v in folders.items(): print(f"{v['kind']:9s} {v['files']:5d}  {p}")
    print(f"\n{len(hard)} of the remake's own assets reference originals or derived assets by GUID:")
    by_type = collections.Counter(os.path.splitext(f)[1] for f in hard)
    print("  ", dict(by_type))
    print(f"Resources.Load: {report['resources_load']['sites']} sites in {report['resources_load']['files']} files")


if __name__ == "__main__":
    main()
