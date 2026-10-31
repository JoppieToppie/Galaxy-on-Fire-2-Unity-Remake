"""Run the tests in the open Editor (through the `unity` CLI) and print only what matters: the totals, then each
failed test with its message and the stack lines from the project's own code.

    python -I Reference/tools/tests/run_tests.py                     # the edit-mode suite
    python -I Reference/tools/tests/run_tests.py --filter GoF2Remake.Tests.ShopPriceTests
    python -I Reference/tools/tests/run_tests.py --mode PlayMode --filter GoF2Remake.Tests.SceneSmokeTests
    python -I Reference/tools/tests/run_tests.py --reload            # after Play mode: drop its leftover statics first

The full result (every test's record) stays in Temp/test_results.json for a closer look. Exit code 1 when a test failed
or the run didn't finish.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
UNITY = shutil.which("unity") or shutil.which("unity.cmd") or "unity"
STACK_LINES = 4
MESSAGE_CHARS = 600


def unity(*args, timeout=None):
    run = subprocess.run([UNITY, *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                         errors="replace", timeout=timeout)
    return run.returncode, run.stdout + run.stderr


def result_json(text):
    """The first JSON object in the CLI's output (newer versions print it as a table column, with the parameters after it)."""
    start = text.find("{")
    if start < 0:
        return None
    try:
        return json.JSONDecoder().raw_decode(text[start:])[0]
    except ValueError:
        return None


def editor_ready():
    code, out = unity("command", "editor_status", "--result-only", timeout=60)
    status = result_json(out) or {}
    return (code == 0 and status.get("status") == "ready" and not status.get("compiling")
            and not status.get("domainReloadInProgress") and status.get("playMode") == "stopped")


def wait_ready(limit_s=300):
    deadline = time.time() + limit_s
    while time.time() < deadline:
        try:
            if editor_ready():
                return True
        except subprocess.TimeoutExpired:
            pass
        time.sleep(2)
    return False


def find(node, key):
    """The first value of 'key' anywhere in a JSON tree (the CLI nests the runner's response; the edit-mode result
    capitalizes its keys, test_status's play-mode result doesn't)."""
    if isinstance(node, dict):
        for k, v in node.items():
            if k.lower() == key.lower():
                return v
        for v in node.values():
            found = find(v, key)
            if found is not None:
                return found
    elif isinstance(node, list):
        for v in node:
            found = find(v, key)
            if found is not None:
                return found
    return None


def project_stack(trace):
    lines = [l.strip() for l in (trace or "").splitlines() if l.strip()]
    own = [l for l in lines if "Assets/" in l.replace("\\", "/")]
    root = ROOT.replace("\\", "/") + "/"
    return [l[3:] if l.startswith("at ") else l for l in
            (l.replace("\\", "/").replace(root, "") for l in (own or lines)[:STACK_LINES])]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mode", default="EditMode", choices=["EditMode", "PlayMode"])
    ap.add_argument("--filter", help="a test, fixture or namespace name")
    ap.add_argument("--reload", action="store_true", help="recompile first (after Play mode, for clean statics)")
    ap.add_argument("--timeout", type=int, default=900, help="seconds to wait for the run")
    a = ap.parse_args()

    if not wait_ready(60):
        print("The Editor isn't ready (not running, compiling, or in Play mode). Check `unity status`.")
        return 1
    if a.reload:
        unity("command", "eval", "--", "UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation("
              "UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache); return 0;", timeout=60)
        time.sleep(3)
        if not wait_ready(600):
            print("The Editor didn't finish recompiling.")
            return 1

    args = ["--mode", a.mode]
    if a.filter:
        args += ["--filter", a.filter]
    if a.mode == "PlayMode":
        args += ["--async_tests", "true"]
    started = time.time()
    code, out = unity("command", "run_tests", "--result-only", "--timeout", str(a.timeout), "--", *args,
                      timeout=a.timeout + 60)
    data = result_json(out)
    if a.mode == "PlayMode":
        deadline = started + a.timeout
        while time.time() < deadline:
            time.sleep(5)
            _, out = unity("command", "test_status", "--result-only", timeout=60)
            data = result_json(out) or {}
            state = str(find(data, "status") or "").lower()
            if find(data, "Summary") is not None or state in ("completed", "error", "cancelled", "no_tests"):
                break

    with open(os.path.join(ROOT, "Temp", "test_results.json"), "w", encoding="utf-8") as fh:
        fh.write(out)
    summary = find(data, "Summary") if data else None
    if summary is None:
        print("No test result. The CLI said:")
        print(out.strip()[:2000])
        return 1

    results = find(data, "Results") or []
    failed = [r for r in results if r.get("Status") not in ("Passed", "Skipped")]
    count = lambda key: summary.get(key, summary.get(key.lower(), 0))
    print("{mode}: {p}/{t} passed, {f} failed, {s} skipped{i} ({sec:.0f} s){flt}".format(
        mode=a.mode, p=count("Passed"), t=count("Total"), f=count("Failed"), s=count("Skipped"),
        i=", {} inconclusive".format(count("Inconclusive")) if count("Inconclusive") else "",
        sec=time.time() - started, flt=" [filter {}]".format(a.filter) if a.filter else ""))
    for r in failed:
        print()
        print("FAILED " + r.get("FullName", "?") + " (" + str(r.get("Status")) + ")")
        message = (r.get("Message") or "").strip()
        if message:
            message = message if len(message) <= MESSAGE_CHARS else message[:MESSAGE_CHARS] + " ..."
            print("  " + message.replace("\n", "\n  "))
        for line in project_stack(r.get("StackTrace")):
            print("    at " + line)
    if failed:
        print()
        print("Full records: Temp/test_results.json")
    return 1 if failed or count("Failed") else 0


if __name__ == "__main__":
    sys.exit(main())
