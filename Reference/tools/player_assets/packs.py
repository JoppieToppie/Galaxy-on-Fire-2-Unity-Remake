# packs.py: what each edition of the player's copy provides, from the converter's actual output (docs/notes/player-assets.md
# "What the player's copy can't provide"). Compares the project's converted originals with each edition's converted cache
# (Reference/tools/player_assets/convert) and sorts every original asset (textures, models, sounds, the data tables the .bin
# files rebuild; the text per string) per edition:
#   exact      the edition's conversion gives the project's file (images: the same pixels; meshes: the same sidecar; audio:
#              the same sample at the same length; text: the same string)
#   lower      the edition has it at a lower quality (a texture at a lower resolution, or the same size encoded worse, 30 dB
#              or better against the project's): the game runs from it, the project's version is an optional upgrade
#   differs    the edition has it, but not as the project has it (the remake's edits, or the edition's own version)
#   missing    the edition doesn't have it at all
# Per edition, each asset is required (the edition lacks it or has another version: the game needs the project's) or
# optional (the edition's lower-quality version works; the project's is an upgrade). The common pack is what every edition
# needs (required: needed by all; optional: an upgrade for all); a gap pack is what one edition needs beyond the common.
#
#   python3 Reference/tools/player_assets/packs.py --project <repo with the LFS files> --edition android=<cache> \
#       --edition ios=<cache> [--out Reference/player_assets/packs.json] [--jobs 6]
import argparse, collections, json, os, wave
from concurrent.futures import ProcessPoolExecutor

EXT_AUDIO = (".mp3", ".wav")


def png(path):
    from PIL import Image
    import numpy as np
    return np.asarray(Image.open(path).convert("RGBA"))


def cmp_texture(args):
    project, cache = args
    if not os.path.exists(cache): return "missing", None
    a, b = png(project), png(cache)
    if a.shape == b.shape:
        if (a == b).all(): return "exact", None
        import numpy as np
        m = ((a[..., :3].astype(float) - b[..., :3].astype(float)) ** 2).mean()
        psnr = 10 * np.log10(255 ** 2 / max(m, 1e-12))
        # the same picture, encoded better in the project (KiritoJPK's 2026-10-07 textures): a quality difference
        return ("lower", f"same size, {psnr:.0f} dB") if psnr >= 30 else ("differs", f"pixels, {psnr:.0f} dB")
    if b.shape[0] <= a.shape[0] and b.shape[1] <= a.shape[1]:
        # a lower resolution: the same picture? (PSNR against the project's at the edition's size)
        from PIL import Image
        import numpy as np
        s = np.asarray(Image.fromarray(a).resize((b.shape[1], b.shape[0]), Image.BOX)).astype(float)
        m = ((s[..., :3] - b[..., :3].astype(float)) ** 2).mean()
        psnr = 99.0 if m == 0 else 10 * np.log10(255 ** 2 / m)
        return ("lower", f"{b.shape[1]}x{b.shape[0]} of {a.shape[1]}x{a.shape[0]}, {psnr:.0f} dB") if psnr >= 20 else ("differs", f"lower and {psnr:.0f} dB")
    return "differs", f"{b.shape[1]}x{b.shape[0]} vs {a.shape[1]}x{a.shape[0]}"


def same_json(a, b):
    import math
    if isinstance(a, float) or isinstance(b, float):
        if a is None or b is None: return a is None and b is None
        if a == b: return True   # (infinities too: inf - inf is nan)
        return (math.isnan(a) and math.isnan(b)) or abs(a - b) <= 1e-6 * max(1.0, abs(a))
    if isinstance(a, dict): return isinstance(b, dict) and a.keys() == b.keys() and all(same_json(a[k], b[k]) for k in a)
    if isinstance(a, list): return isinstance(b, list) and len(a) == len(b) and all(same_json(x, y) for x, y in zip(a, b))
    return a == b


def norm_sidecar(m):
    """What the game reads the same either way: Blender's '.001' suffixes on the FBX-made parents' names (the converter's
    are the file's own), and an all-zero bounding sphere / pivot written as None by the converter."""
    import re
    m.pop("source", None)
    for p in m.get("parts", []):
        if isinstance(p.get("parent"), str): p["parent"] = re.sub(r"\.\d{3}$", "", p["parent"])
        for k in ("bsphere", "pivot"):
            if isinstance(p.get(k), list) and not any(p[k]): p[k] = None
    return m


def cmp_mesh(project_sidecar, cache_sidecar):
    if not os.path.exists(cache_sidecar): return "missing", None
    a, b = norm_sidecar(json.load(open(project_sidecar))), norm_sidecar(json.load(open(cache_sidecar)))
    return ("exact", None) if same_json(a, b) else ("differs", "sidecar")


def duration(path):
    """Seconds of an .ogg / .wav / .mp3 (ffprobe for the compressed ones)."""
    if path.endswith(".wav"):
        with wave.open(path) as w: return w.getnframes() / w.getframerate()
    import subprocess
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                         capture_output=True, text=True).stdout.strip()
    return float(out) if out else -1.0


def cmp_audio(args):
    project, cache_stem = args
    cache = next((cache_stem + e for e in EXT_AUDIO if os.path.exists(cache_stem + e)), None)
    if cache is None: return "missing", None
    a, b = duration(project), duration(cache)
    # the project's are Ogg Vorbis re-encodes of the same samples: the same length within a frame or two
    return ("exact", None) if abs(a - b) <= 0.06 else ("differs", f"{b:.2f} s vs {a:.2f} s")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True)
    ap.add_argument("--edition", action="append", required=True, help="name=cache folder")
    ap.add_argument("--out", default="Reference/player_assets/packs.json")
    ap.add_argument("--jobs", type=int, default=6)
    a = ap.parse_args()
    eds = dict(e.split("=", 1) for e in a.edition)
    A = os.path.join(a.project, "Assets")
    verdict = collections.defaultdict(dict)   # asset -> edition -> (kind, detail)

    textures = [os.path.relpath(os.path.join(r, f), A) for r, _, fs in os.walk(os.path.join(A, "Textures")) for f in fs
                if f.endswith(".png") and "_metallic_smoothness" not in f]
    with ProcessPoolExecutor(a.jobs) as ex:
        for ed, cache in eds.items():
            res = ex.map(cmp_texture, [(os.path.join(A, t), os.path.join(cache, t)) for t in textures], chunksize=8)
            for t, r in zip(textures, res): verdict[t][ed] = r
    for r, _, fs in os.walk(os.path.join(A, "Models")):
        for f in fs:
            if not f.endswith(".gof2mesh.json"): continue
            rel = os.path.relpath(os.path.join(r, f), A)
            for ed, cache in eds.items(): verdict[rel][ed] = cmp_mesh(os.path.join(A, rel), os.path.join(cache, rel))
    audio = [os.path.relpath(os.path.join(r, f), A) for r, _, fs in os.walk(os.path.join(A, "Audio")) for f in fs if f.endswith(".ogg")]
    with ProcessPoolExecutor(a.jobs) as ex:
        for ed, cache in eds.items():
            res = ex.map(cmp_audio, [(os.path.join(A, p), os.path.join(cache, p[:-4])) for p in audio], chunksize=16)
            for p, r in zip(audio, res): verdict[p][ed] = r
    # the data tables the converter rebuilds from the .bin files (GameTables): an edition without a table's .bin (the macOS
    # app has none of the Supernova / Valkyrie ones) needs the project's; the same values = exact
    made = set()
    for cache in eds.values():
        d = os.path.join(cache, "Resources", "GoF2Data")
        if os.path.isdir(d): made |= {f for f in os.listdir(d) if f.endswith(".json")}
    for f in sorted(made):
        rel = os.path.join("Resources", "GoF2Data", f)
        if not os.path.exists(os.path.join(A, rel)): continue
        proj = json.load(open(os.path.join(A, rel), encoding="utf-8"))
        for ed, cache in eds.items():
            p = os.path.join(cache, rel)
            verdict[rel][ed] = ("missing", None) if not os.path.exists(p) else \
                (("exact", None) if json.load(open(p, encoding="utf-8")) == proj else ("differs", "table values"))
    # text: per string
    text = {}
    for f in sorted(os.listdir(os.path.join(A, "Localization"))):
        if not (f.startswith("text_") and f.endswith(".json")): continue
        proj = json.load(open(os.path.join(A, "Localization", f), encoding="utf-8"))
        per = {}
        for ed, cache in eds.items():
            p = os.path.join(cache, "Localization", f)
            if not os.path.exists(p): per[ed] = {"kind": "missing"}; continue
            mine = json.load(open(p, encoding="utf-8"))
            diff = [i for i in range(len(proj)) if i >= len(mine) or mine[i] != proj[i]]
            per[ed] = {"kind": "exact" if not diff else "differs", "strings_differing": len(diff), "ids": diff if len(diff) <= 200 else None}
        text[f] = per

    # the packs: per edition, what it needs required / optionally; the common part is what every edition needs
    req, opt = collections.defaultdict(set), collections.defaultdict(set)
    for asset, per in verdict.items():
        for e, (k, _) in per.items():
            if k == "lower": opt[e].add(asset)
            elif k != "exact": req[e].add(asset)
    common_req = set.intersection(*[req[e] for e in eds]) if eds else set()
    common_opt = set.intersection(*[opt[e] | req[e] for e in eds]) - common_req if eds else set()
    gap = {e: sorted(req[e] - common_req) for e in eds}
    optional = {e: sorted(opt[e] - common_opt - common_req) for e in eds}
    common = sorted(common_req)
    report = {
        "editions": list(eds),
        "totals": {e: dict(collections.Counter(per[e][0] for per in verdict.values())) for e in eds},
        "common_pack": {"required": {"count": len(common), "assets": common},
                        "optional": {"count": len(common_opt), "assets": sorted(common_opt)}},
        "gap_packs": {e: {"count": len(v), "assets": v} for e, v in gap.items()},
        "optional_hd": {e: {"count": len(v), "assets": v} for e, v in optional.items()},
        "text": text,
        "details": {k: {e: v for e, v in per.items() if v[0] != "exact"} for k, per in verdict.items() if any(v[0] != "exact" for v in per.values())},
    }
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    json.dump(report, open(a.out, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print("per edition:", json.dumps(report["totals"]))
    print("common pack: required", len(common), "optional", len(common_opt), "| gap packs:", {e: len(v) for e, v in gap.items()}, "| optional HD:", {e: len(v) for e, v in optional.items()})
    print("text:", {f: {e: (v["kind"], v.get("strings_differing")) for e, v in per.items()} for f, per in text.items()})


if __name__ == "__main__":
    main()
