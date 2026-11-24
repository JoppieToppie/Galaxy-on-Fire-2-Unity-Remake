# original_refs.py: where the remake's own assets point at the original's (docs/notes/player-assets.md "The content layer").
# A build without the originals loses those references (the sound tables' clips, the scenes' music...), and with them the
# originals' names; this records them from the project as it is, field path by field path, in
# Assets/Resources/GoF2Content/original_refs.json, which ships: the game fills those fields from the player's converted copy
# when a table loads or a scene opens (OriginalRefs).
#
#   python3 Reference/tools/player_assets/original_refs.py --project <a checkout with the originals> [--out <json>]
#
# Each entry: the asset ("Resources/GoF2Story/StoryAssets") or the scene component ("Scenes/Station|<class>|<GameObject>"),
# then its fields: path (Unity's serialized names, "voiceClipsEng[12]", "sounds[3].clips[0]") -> the original's project
# path ("Audio/VOICE_eng/NAME.ogg", "Textures/main/x.png"). Only references to original / derived assets are kept.
import argparse, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import inventory

REF = re.compile(r"\{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}")
DOC = re.compile(r"^--- !u!(\d+) &(-?\d+)")


def guid_map(project):
    """guid -> asset path (relative to Assets) for the originals and derived assets, and guid -> script class."""
    assets, scripts = {}, {}
    for root, _, files in os.walk(os.path.join(project, "Assets")):
        for f in files:
            if not f.endswith(".meta"): continue
            p = os.path.join(root, f)
            asset = p[:-5]
            rel = os.path.relpath(asset, project).replace(os.sep, "/")
            try:
                with open(p, encoding="utf-8", errors="replace") as fh:
                    head = fh.read(400)
            except OSError: continue
            m = re.search(r"guid: ([0-9a-f]{32})", head)
            if not m: continue
            if rel.endswith(".cs"): scripts[m.group(1)] = os.path.splitext(os.path.basename(rel))[0]
            elif inventory.classify(rel)[0] != inventory.OWN: assets[m.group(1)] = rel[len("Assets/"):]
    return assets, scripts


def walk(lines, assets):
    """The (path, original) pairs in one YAML document's fields."""
    keys, idx, out = {}, {}, []

    def path(upto):
        parts = []
        for i in sorted(k for k in keys if k <= upto):
            s = keys[i]
            if idx.get(i, -1) >= 0: s += f"[{idx[i]}]"
            parts.append(s)
        return ".".join(parts)

    def drop(deeper_than):
        for d in [k for k in keys if k > deeper_than]: keys.pop(d, None); idx.pop(d, None)

    for raw in lines:
        if not raw.strip() or raw.lstrip().startswith("#"): continue
        indent = len(raw) - len(raw.lstrip(" "))
        s = raw.strip()
        if s.startswith("- "):
            # a list item belongs to the key at the same indent (Unity's layout)
            idx[indent] = idx.get(indent, -1) + 1
            drop(indent)
            body = s[2:]
            m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*): ?(.*)$", body)
            if m:
                keys[indent + 2], idx[indent + 2] = m.group(1), -1
                value, at = m.group(2), indent + 2
            else:
                value, at = body, indent
            r = REF.search(value)
            if r and r.group(2) in assets: out.append((path(at), assets[r.group(2)]))
            continue
        m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*): ?(.*)$", s)
        if not m: continue
        keys[indent], idx[indent] = m.group(1), -1
        drop(indent)
        r = REF.search(m.group(2))
        if r and r.group(2) in assets: out.append((path(indent), assets[r.group(2)]))
    # Unity writes a MonoBehaviour's fields one level in ("MonoBehaviour:" at 0): drop that level from the paths
    return [(p.split(".", 1)[1] if "." in p else p, a) for p, a in out]


def docs(text):
    """(class id, file id, lines) of each YAML document."""
    cur, out = None, []
    for line in text.split("\n"):
        m = DOC.match(line)
        if m:
            cur = (int(m.group(1)), m.group(2), []); out.append(cur); continue
        if cur: cur[2].append(line)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", default="Assets/Resources/GoF2Content/original_refs.json")
    a = ap.parse_args()
    assets, scripts = guid_map(a.project)
    inv = json.load(open(os.path.join(os.path.dirname(__file__), "..", "..", "player_assets", "inventory.json")))
    result = {}
    for f in sorted(inv["hard_references"]):
        text = open(os.path.join(a.project, f), encoding="utf-8", errors="replace").read()
        rel = f[len("Assets/"):]
        if f.endswith(".unity"):
            ds = docs(text)
            names = {fid: next((l.split(":", 1)[1].strip() for l in lines if l.strip().startswith("m_Name:")), "")
                     for cid, fid, lines in ds if cid == 1}
            seen = {}
            for cid, fid, lines in ds:
                if cid != 114: continue
                go = next((re.search(r"fileID: (-?\d+)", l).group(1) for l in lines if l.strip().startswith("m_GameObject:")), None)
                sc = next((REF.search(l).group(2) for l in lines if l.strip().startswith("m_Script:") and REF.search(l)), None)
                fields = walk(lines, assets)
                if not fields: continue
                key = f"{os.path.splitext(rel)[0]}|{scripts.get(sc, sc)}|{names.get(go, '')}"
                seen[key] = seen.get(key, 0) + 1
                if seen[key] > 1: key += f"#{seen[key]}"
                result[key] = dict(fields)
        elif f.endswith(".asset") and "/Resources/" in f:
            fields = walk(text.split("\n"), assets)
            if fields: result[os.path.splitext(rel)[0]] = dict(fields)
        # (build profiles' icons, materials: not refilled here; see the note)
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    json.dump(result, open(a.out, "w", encoding="utf-8"), indent=1, ensure_ascii=False, sort_keys=True)
    for k, v in result.items(): print(f"{len(v):5d}  {k}")


if __name__ == "__main__":
    main()
