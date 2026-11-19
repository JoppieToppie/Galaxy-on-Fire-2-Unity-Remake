# make_packs.py: the packs that carry what the player's copy doesn't provide (docs/notes/player-assets.md "Packs"),
# distributed separately, never on GitHub. A pack is a zip (.gof2pack) of files at their converted copy's paths and a
# gof2pack.json manifest; the game keeps it and writes it over the converted copy (OriginalPacks). Made from a checkout
# with the originals and the derived assets (the main project):
#   python3 Reference/tools/player_assets/make_packs.py --project <repo> [--out ~/GOF2UnityRemakeMods/packs] [--version N]
# hd    the HD pack (order 2): the HD textures (every edition's optional list from packs.json and the common optional
#       ones; with each normal map its metallic / smoothness map, which the converter would otherwise make from the
#       edition's lower one), the Real-ESRGAN skies and their star lists (GoF2SkyHd), the sky patches made from them
#       (GoF2SkyLayers/patch_*), the 3D planets (GoF2Planets3D, from the planet_rebuild tools) and the large FISHLABS logo
#       (Resources/GoF2UiSlots: without the pack, the atlas' own cut from the copy).
# common (order 0) and gap_<edition> (order 1): what packs.json says each needs, in the converted copy's formats:
#       textures and data tables the project's (plus the Default Economy overlays, economy_default / weapons_hd_default,
#       which no copy makes); a sound or model another edition's conversion has exactly as the project has it, that
#       edition's converted file (the German voices for Android from the iOS / macOS copies as .mp3, Android's meshes for
#       the Apple editions as .aem + sidecar: --edition name=<its converted cache>); the remake's own remodels (no edition
#       has them) the .aem PackModelExporter wrote from the project's FBX (--models) with the project's sidecar; the text
#       tables the project's, every language, in the common pack (the remake's fixes; an edition's own wording and the
#       strings it lacks); the Original interface style's processed pieces the stylesheets name (UiImageSlots.FromPack)
#       as Resources/GoF2UiSlots/<name>.png.
import argparse, datetime, glob, json, os, zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))


def hd_files(project, packs):
    A = os.path.join(project, "Assets")
    files = set()
    for ed in packs["editions"]:
        files |= {a for a in packs["optional_hd"][ed]["assets"] if a.startswith("Textures/")}
    files |= {a for a in packs["common_pack"]["optional"]["assets"] if a.startswith("Textures/")}
    for t in list(files):
        if t.endswith("_normal_specular.png"):
            m = t.replace("_normal_specular.png", "_metallic_smoothness.png")
            if os.path.exists(os.path.join(A, m)): files.add(m)
    out = [(t, os.path.join(A, t)) for t in sorted(files)]
    # the large FISHLABS logo (the copy's is the atlas' 266 x 303), for the splash (UiImageSlots)
    out += [("Resources/GoF2UiSlots/logo_fishlabs.png", os.path.join(A, "UI", "MainMenu", "Images", "logo_fishlabs.png"))]
    for pattern in ("Resources/GoF2SkyHd/*.png", "Resources/GoF2SkyHd/*.bytes", "Resources/GoF2SkyLayers/patch_*.png",
                    "Resources/GoF2Planets3D/*/*.png", "Resources/GoF2Planets3D/*/*.json"):
        for f in sorted(glob.glob(os.path.join(A, pattern))):
            out.append((os.path.relpath(f, A).replace(os.sep, "/"), f))
    return out


def needed(project, packs, assets, eds, caches, models):
    """(pack path, source file) for a pack's assets."""
    A = os.path.join(project, "Assets")
    det = packs["details"]
    out = []
    for a in assets:
        exact = [e for e in eds if e not in det.get(a, {}) and e in caches]
        if a.startswith("Audio/"):
            stem = a[:-4]
            src = next(((stem + ext, os.path.join(caches[e], stem + ext)) for e in exact for ext in (".mp3", ".wav")
                        if os.path.exists(os.path.join(caches[e], stem + ext))), None)
            if src is None: raise SystemExit(f"{a}: no edition has it as the project has it")
            out.append(src)
        elif a.startswith("Models/") and a.endswith(".gof2mesh.json"):
            stem = a[:-len(".gof2mesh.json")]
            e = next((e for e in exact if os.path.exists(os.path.join(caches[e], stem + ".aem"))), None)
            if e is not None:
                out += [(stem + ".aem", os.path.join(caches[e], stem + ".aem")), (a, os.path.join(caches[e], a))]
            else:
                aem = os.path.join(models, stem + ".aem")
                if not os.path.exists(aem): raise SystemExit(f"{a}: the remake's own model, not exported (PackModelExporter)")
                out += [(stem + ".aem", aem), (a, os.path.join(A, a))]
        else:
            out.append((a, os.path.join(A, a)))
    return out


def write(path, manifest, files):
    tmp = path + ".tmp"
    total = 0
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_STORED, allowZip64=True) as z:   # (pngs are compressed already)
        for rel, src in files:
            z.write(src, rel); total += os.path.getsize(src)
        manifest.update(files=len(files), bytes=total)
        z.writestr("gof2pack.json", json.dumps(manifest))
    os.replace(tmp, path)
    print(f"{os.path.basename(path)}: {len(files)} files, {total / 1e6:.0f} MB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", default=os.path.expanduser("~/GOF2UnityRemakeMods/packs"))
    ap.add_argument("--version", type=int, default=int(datetime.date.today().strftime("%Y%m%d")))
    ap.add_argument("--edition", action="append", default=[], help="name=its converted cache (for the files it has exactly)")
    ap.add_argument("--models", default=os.path.expanduser("~/GOF2UnityRemakeMods/packs/stage_models"))
    ap.add_argument("--only", help="hd, common or gap_<edition>")
    a = ap.parse_args()
    caches = dict(e.split("=", 1) for e in a.edition)
    packs = json.load(open(os.path.join(ROOT, "Reference", "player_assets", "packs.json")))
    os.makedirs(a.out, exist_ok=True)
    eds = packs["editions"]
    A = os.path.join(a.project, "Assets")
    plans = []
    if a.only in (None, "common"):
        files = needed(a.project, packs, packs["common_pack"]["required"]["assets"], eds, caches, a.models)
        for t in ("economy_default.json", "weapons_hd_default.json"):
            files.append((f"Resources/GoF2Data/{t}", os.path.join(A, "Resources", "GoF2Data", t)))
        # the stylesheets' images the copy can't give exactly (the Original style's processed pieces: UiImageSlots.FromPack)
        cs = open(os.path.join(ROOT, "Assets", "Scripts", "Runtime", "App", "UI", "UiImageSlots.cs"), encoding="utf-8").read()
        block = cs[cs.index("FromPack ="):]
        block = block[block.index("{") + 1:block.index("};")]
        import re as _re
        for name in _re.findall(r'"([^"]+)"', block):
            src = os.path.join(A, "UI", "Original", name + ".png")
            files.append((f"Resources/GoF2UiSlots/{name}.png", src))
        for f in sorted(os.listdir(os.path.join(A, "Localization"))):
            if f.startswith("text_") and f.endswith(".json"): files.append((f"Localization/{f}", os.path.join(A, "Localization", f)))
        plans.append(("gof2_common.gof2pack", {"name": "common", "title": "Common pack", "order": 0, "edition": ""}, files))
    for e in eds:
        if a.only in (None, "gap_" + e):
            files = needed(a.project, packs, packs["gap_packs"][e]["assets"], eds, caches, a.models)
            plans.append((f"gof2_gap_{e}.gof2pack", {"name": "gap_" + e, "title": f"{e} gap pack", "order": 1, "edition": e}, files))
    if a.only in (None, "hd"):
        plans.append(("gof2_hd.gof2pack", {"name": "hd", "title": "HD pack", "order": 2, "edition": ""}, hd_files(a.project, packs)))
    for name, manifest, files in plans:
        missing = [r for r, s in files if not os.path.exists(s)]
        if missing: raise SystemExit(f"{name}: missing {missing[:5]}")
        manifest["version"] = a.version
        write(os.path.join(a.out, name), manifest, sorted(set(files)))


if __name__ == "__main__":
    main()
