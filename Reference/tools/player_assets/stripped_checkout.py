# stripped_checkout.py: a checkout of the remake without the original game's assets and what's derived from them
# (docs/notes/player-assets.md "Builds without the originals"): exactly what a build that ships without them contains, for
# building releases and for testing the content layer (the test bed). Every tracked file inventory.py classifies as original
# or derived is left out by a non-cone sparse checkout (with its .meta; a folder with nothing else in it, and the folder's
# .meta, as a whole), and git-lfs fetches only the files kept.
#   python3 Reference/tools/player_assets/stripped_checkout.py patterns            the sparse-checkout rules (stdout)
#   python3 Reference/tools/player_assets/stripped_checkout.py create <dir> [rev]  a new worktree (rev: default HEAD, detached)
#   python3 Reference/tools/player_assets/stripped_checkout.py apply [<dir>]       an existing checkout's rules rewritten
# Run from a checkout of the repository. Never `git lfs pull` in such a checkout without --include: git-lfs writes every
# LFS file, the left-out ones too (apply removes them again).
import collections, os, re, struct, subprocess, sys, zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import inventory

KEEP = ("own",)


# Left-out images a kept asset references that the game fills the same way (UiImageSlots): the text icons' atlas, which
# the kept sprite asset and its material point at.
ASSET_SLOTS = {"Assets/Resources/Sprite Assets/gof2_text_icons.png"}


def slots(files, root="."):
    """Left-out images a stylesheet / layout names by path (url("project://database/...")): their .meta stays and a 4 x 4
    placeholder of the remake's own takes the image's place, so the reference holds; the game fills it from the player's
    copy (UiImageSlots)."""
    found = {p for p in ASSET_SLOTS if p in files}
    for f in files:
        if not f.endswith((".uss", ".uxml", ".tss")): continue
        path = os.path.join(root, f)
        if not os.path.exists(path): continue
        for m in re.finditer(r'url\("project://database/([^"?#]+)', open(path, encoding="utf-8", errors="replace").read()):
            p = m.group(1).replace("%20", " ")
            if p in files and inventory.classify(p)[0] not in KEEP: found.add(p)
    return found


def left_out(files, root="."):
    """The tracked files to leave out (assets and their .meta) and the folders left out whole."""
    out = set()
    assets = [f for f in files if not f.endswith(".meta")]
    for f in assets:
        if inventory.classify(f)[0] not in KEEP: out.add(f)
    keep_meta = {p + ".meta" for p in slots(files, root)}
    for f in files:
        if f.endswith(".meta") and f[:-5] in out and f not in keep_meta: out.add(f)
    # folders whose every tracked file is left out go as a whole (with their own .meta)
    under = collections.defaultdict(list)
    for f in files:
        parts = f.split("/")
        for i in range(1, len(parts)): under["/".join(parts[:i])].append(f)
    # (to a fixed point: a left-out folder's own .meta, inside its parent, is left out too)
    whole = set()
    while True:
        more = {d for d, fs in under.items() if d not in whole and d.startswith("Assets/") and all(x in out for x in fs)}
        if not more: break
        whole |= more
        out |= {d + ".meta" for d in more}
    top = {d for d in whole if not any(d.startswith(p + "/") for p in whole)}
    return out, top


def patterns(files, root="."):
    out, top = left_out(files, root)
    esc = lambda p: p.replace(" ", "\\ ")   # (gitignore rules: spaces escaped)
    lines = ["/*"]
    lines += [f"!/{esc(d)}/" for d in sorted(top)]
    lines += [f"!/{esc(f)}" for f in sorted(out) if not any(f.startswith(d + "/") for d in top)]
    return lines


def git(*args, cwd=None, env=None):
    return subprocess.run(["git", *args], cwd=cwd, env=env, check=True, capture_output=True, text=True).stdout


def placeholder_png():
    """A 4 x 4 transparent PNG."""
    raw = b"".join(b"\x00" + b"\x00" * 16 for _ in range(4))
    chunk = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 4, 4, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def lfs_pull_kept(cwd, files):
    out, top = left_out(files, cwd)
    lfs = [p for p in git("lfs", "ls-files", "-n", cwd=cwd).split("\n") if p]
    kept = [p for p in lfs if p not in out and not any(p.startswith(d + "/") for d in top)]
    for i in range(0, len(kept), 200):
        git("lfs", "pull", "--include=" + ",".join(kept[i:i + 200]), cwd=cwd)
    # (a stray full pull's files: gone again)
    git("sparse-checkout", "reapply", cwd=cwd, env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"})
    return len(kept)


def apply(cwd):
    # (.meta files an earlier run switched to a texture import: back as committed, before the rules are worked out again)
    edited = [l[2:] for l in git("ls-files", "-v", "Assets", cwd=cwd).split("\n") if l.startswith("h ") and l.endswith(".meta")]
    if edited:
        git("update-index", "--no-assume-unchanged", *edited, cwd=cwd)
        git("checkout", "--", *edited, cwd=cwd)
    files = [f for f in git("ls-files", "Assets", cwd=cwd).split("\n") if f]
    rules = patterns(files, cwd)
    path = git("rev-parse", "--git-path", "info/sparse-checkout", cwd=cwd).strip()
    path = path if os.path.isabs(path) else os.path.join(cwd, path)
    git("config", "core.sparseCheckout", "true", cwd=cwd)
    git("config", "core.sparseCheckoutCone", "false", cwd=cwd)
    # (the placeholders sit at left-out paths: without this git clears their skip-worktree bit and calls them changed,
    # one `git add -A` from committing a placeholder over the real image)
    git("config", "--worktree", "sparse.expectFilesOutsideOfPatterns", "true", cwd=cwd)
    open(path, "w").write("\n".join(rules) + "\n")
    git("sparse-checkout", "reapply", cwd=cwd, env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"})
    # (a former placeholder whose file is kept now: the committed file back, its LFS content fetched below)
    stale = [f for f in git("diff", "--name-only", cwd=cwd).split("\n")
             if f and os.path.exists(os.path.join(cwd, f)) and open(os.path.join(cwd, f), "rb").read() == placeholder_png()]
    if stale: subprocess.run(["git", "checkout", "--", *stale], cwd=cwd, check=True, env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"})
    n = lfs_pull_kept(cwd, files)
    placeholders = slots(files, cwd)
    for p in placeholders:
        open(os.path.join(cwd, p), "wb").write(placeholder_png())
    if placeholders: git("update-index", "--skip-worktree", *sorted(placeholders), cwd=cwd)   # (left out, as git should see them)
    # A placeholder imported as a sprite keeps its 4 x 4 rectangle whatever its texture becomes (the title logo, the
    # crosshairs showed nothing): imported as a plain texture here, its GUID kept, git told to look away.
    resprited = []
    for p in sorted(placeholders):
        meta = os.path.join(cwd, p + ".meta")
        if not os.path.exists(meta): continue
        text = open(meta, encoding="utf-8").read()
        new = re.sub(r"^(\s*)textureType: 8$", r"\1textureType: 0", text, flags=re.M)
        new = re.sub(r"^(\s*)spriteMode: [12]$", r"\1spriteMode: 0", new, flags=re.M)
        if new != text:
            open(meta, "w", encoding="utf-8").write(new)
            resprited.append(p + ".meta")
    if resprited: git("update-index", "--assume-unchanged", *resprited, cwd=cwd)
    print(f"{cwd}: {len(rules) - 1} rules, {n} LFS files kept, {len(placeholders)} stylesheet images as placeholders")


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "patterns"
    if cmd == "patterns":
        files = [f for f in git("ls-files", "Assets").split("\n") if f]
        print("\n".join(patterns(files)))
    elif cmd == "create":
        d, rev = os.path.abspath(sys.argv[2]), sys.argv[3] if len(sys.argv) > 3 else "HEAD"
        env = {**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"}
        subprocess.run(["git", "worktree", "add", "--no-checkout", "--detach", d, rev], check=True, env=env)
        git("config", "core.sparseCheckout", "true", cwd=d)
        git("config", "core.sparseCheckoutCone", "false", cwd=d)
        files = [f for f in git("ls-tree", "-r", "--name-only", rev, "Assets", cwd=d).split("\n") if f]
        path = git("rev-parse", "--git-path", "info/sparse-checkout", cwd=d).strip()
        path = path if os.path.isabs(path) else os.path.join(d, path)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, "w").write("\n".join(patterns(files, d)) + "\n")
        subprocess.run(["git", "checkout", "--detach", rev], cwd=d, check=True, env=env)
        apply(d)
    elif cmd == "apply":
        apply(os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.getcwd())
    else:
        raise SystemExit(__doc__ if __doc__ else "patterns | create <dir> [rev] | apply [<dir>]")


if __name__ == "__main__":
    main()
