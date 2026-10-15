#!/usr/bin/env python3
"""Compare JIT/AOT hosts on frozen disposable data; never run games in the live database."""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


def backup(source, destination):
    with sqlite3.connect(f"file:{source}?mode=ro", uri=True) as original:
        with sqlite3.connect(destination) as copy:
            original.backup(copy)


def port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def request(address, path, body=None, headers=None, method=None):
    payload = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(address + path, data=payload,
                                 headers={"Content-Type": "application/json", **(headers or {})}, method=method)
    started = time.perf_counter()
    try:
        response = urllib.request.urlopen(req, timeout=45)
    except urllib.error.HTTPError as error:
        response = error
    raw = response.read()
    return response.code, raw, dict(response.headers), (time.perf_counter() - started) * 1000


def usage(pid):
    raw = subprocess.check_output(["ps", "-p", str(pid), "-o", "time=,rss="], text=True).split()
    minutes, seconds = raw[0].split(":")
    return 60 * int(minutes) + float(seconds), int(raw[1]) / 1024


def digest(database, table):
    result = hashlib.sha256()
    with sqlite3.connect(f"file:{database}?mode=ro", uri=True) as db:
        rows = db.execute(f"SELECT id,payload FROM {table} ORDER BY id").fetchall()
    for identity, payload in rows:
        result.update(identity.encode() + b"\0" + payload.encode())
    return {"count": len(rows), "sha256": result.hexdigest()}


def normalized_game(slot, replays):
    result = {key: value for key, value in slot["result"].items() if key != "durationMs"}
    replay = replays / slot["attempts"][-1]["replay"]
    records = []
    with gzip.open(replay, "rt") as stream:
        for line in stream:
            record = json.loads(line)
            record.pop("attemptId", None)
            records.append(record)
    encoded = json.dumps(records, sort_keys=True, separators=(",", ":")).encode()
    return {"seed": slot["seed"], "starter": slot["starter"], "index": slot["index"],
            "result": result, "normalizedReplaySha256": hashlib.sha256(encoded).hexdigest()}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--jit-runtime", type=Path, required=True)
    parser.add_argument("--native-runtime", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--database", type=Path)
    parser.add_argument("--idle-seconds", type=float, default=12)
    parser.add_argument("--games", action="store_true", help="Run two matched complete Forge games per host.")
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[1]
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    database = (args.database or project / "data/simulator.sqlite").resolve()
    runtimes = {"jit": args.jit_runtime.resolve(), "native-aot": args.native_runtime.resolve()}
    evidence = {"database": str(database), "cases": {}, "matchedGames": args.games}
    with tempfile.TemporaryDirectory(prefix="crucible-service-benchmark-") as temporary:
        base = Path(temporary)
        backup(database, base / "simulator.sqlite")
        backup(project.parents[1] / "standard_cards.sqlite", base / "source.sqlite")
        expected = {table: digest(base / "simulator.sqlite", table) for table in ["decks", "runs", "slots"]}
        evidence["frozenData"] = expected
        # One immutable bundle selection for both hosts, even if the live selection changes.
        bundle_base = project / "data/engine-bundles"
        active = (bundle_base / "active.json").read_bytes()
        bundle_id = json.loads(active)["id"]
        evidence["bundleId"] = bundle_id
        responses = {}; games = {}
        for kind, runtime in runtimes.items():
            case = base / kind; root = case / "projects/crucible"
            for folder in ["docs/specs", "scripts", "data/engine-bundles", "data/replays"]:
                (root / folder).mkdir(parents=True, exist_ok=True)
            shutil.copy2(base / "simulator.sqlite", root / "data/simulator.sqlite")
            shutil.copy2(base / "source.sqlite", case / "standard_cards.sqlite")
            for script in ["pilot-optimizer.py", "pilot_optimizer.py"]:
                shutil.copy2(project / "scripts" / script, root / "scripts" / script)
            (root / "data/engine-bundles/active.json").write_bytes(active)
            (root / "data/engine-bundles" / bundle_id).symlink_to(bundle_base / bundle_id, target_is_directory=True)
            case_port = port(); address = f"http://127.0.0.1:{case_port}"
            env = dict(os.environ, MTG_HOST_MODE="service", MTG_PROJECT_ROOT=str(root), MTG_PORT=str(case_port),
                       MTG_JAVA=str(project / "vendor/jdk/Contents/Home/bin/java"), MTG_PYTHON=sys.executable,
                       ASPNETCORE_ENVIRONMENT="Production")
            command = [str(runtime / "CrucibleBackend")] if kind == "native-aot" else [shutil.which("dotnet"), str(runtime / "CrucibleBackend.dll")]
            log = open(output / f"{kind}.log", "wb")
            started = time.perf_counter()
            process = subprocess.Popen(command, cwd=runtime, env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
            log.close()
            try:
                deadline = time.monotonic() + 60
                while True:
                    if process.poll() is not None:
                        raise RuntimeError(f"{kind} exited with {process.returncode}: {(output / (kind + '.log')).read_text()[-3000:]}")
                    try:
                        status, raw, _, _ = request(address, "/api/v1/health")
                        health = json.loads(raw)
                        if status == 200 and health["ready"]:
                            break
                    except (OSError, ValueError):
                        pass
                    if time.monotonic() > deadline:
                        raise TimeoutError(f"{kind} startup timed out")
                    time.sleep(.025)
                startup = time.perf_counter() - started
                assert health["runtimeMode"] == kind and not health["reflectionJsonEnabled"], health
                result = {"startupSeconds": startup, "health": health, "coldRssMiB": usage(process.pid)[1], "requests": {}}
                current = {}
                paths = ["/api/v1/capabilities", "/api/v1/decks", "/api/v1/ui-state", "/api/v1/library-state", "/api/v1/run-history", "/api/v1/cards/search?q=Swamp"]
                for path in paths:
                    status, raw, headers, ms = request(address, path)
                    assert status == 200, (kind, path, status, raw[:2000])
                    data = json.loads(raw)
                    if path.endswith("ui-state"):
                        # Capacity is live OS state; all simulation evidence remains exact.
                        data.pop("resources", None)
                    current[path] = data
                    row = {"milliseconds": ms, "bytes": len(raw)}
                    tag = headers.get("ETag")
                    if tag:
                        checks = [request(address, path, headers={"If-None-Match": tag}) for _ in range(5)]
                        assert all(check[0] == 304 and not check[1] for check in checks)
                        row["unchangedMilliseconds"] = [check[3] for check in checks]
                    result["requests"][path] = row
                decks = current["/api/v1/decks"]
                assert len(decks) == expected["decks"]["count"], "Import changed frozen deck identities"
                assert request(address, "/api/v1/imports", {}, method="POST")[0] == 403
                assert request(address, "/api/v1/health", headers={"Host": "untrusted.invalid"})[0] == 403
                assert request(address, "/api/v1/health", headers={"Origin": "https://untrusted.invalid"})[0] == 403
                for table in ["runs", "slots"]:
                    assert digest(root / "data/simulator.sqlite", table) == expected[table], f"Startup changed {table}"
                first = usage(process.pid); tick = time.monotonic(); time.sleep(args.idle_seconds); last = usage(process.pid)
                elapsed = time.monotonic() - tick
                result["idle"] = {"seconds": elapsed, "cpuPercent": 100 * (last[0] - first[0]) / elapsed, "rssMiB": last[1]}
                if args.games:
                    chosen = sorted((row["manifest"] for row in decks if row["coverage"]["status"] != "blocked" and row["manifest"]["sourceType"] != "custom"), key=lambda deck: deck["id"])[:2]
                    token = json.loads(request(address, "/api/v1/session")[1])["instanceToken"]
                    headers = {"X-Instance-Token": token, "Idempotency-Key": "native-host-equivalence"}
                    body = {"deckIds": [deck["id"] for deck in chosen], "gamesPerPair": 2, "workers": 1,
                            "timeoutSeconds": 180, "turnLimit": 150, "rootSeed": 6510142026,
                            "diagnostic": True, "staggerStarts": False, "pilotMode": "forge-default-v1"}
                    status, raw, _, _ = request(address, "/api/v1/runs", body, headers, "POST")
                    assert status == 200, (status, raw)
                    run_id = json.loads(raw)["id"]; deadline = time.monotonic() + 420
                    while True:
                        state = json.loads(request(address, f"/api/v1/runs/{run_id}/state")[1])
                        run = state["runs"][0]
                        if run["status"] not in ["queued", "running", "cancelling"]:
                            break
                        assert time.monotonic() < deadline, "Matched game run timed out"
                        time.sleep(.5)
                    assert run["status"] == "completed" and all(slot["status"] == "completed" for slot in state["slots"]), run
                    games[kind] = sorted((normalized_game(slot, root / "data/replays") for slot in state["slots"]), key=lambda slot: slot["seed"])
                    result["gameRunId"] = run_id; result["gameConfig"] = run["config"]
                responses[kind] = current; evidence["cases"][kind] = result
                (output / "summary.json").write_text(json.dumps(evidence, indent=2))
                print(f"{kind}: ready in {startup:.3f}s; idle {result['idle']['rssMiB']:.1f} MiB, {result['idle']['cpuPercent']:.2f}% CPU", flush=True)
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=35)
                    except subprocess.TimeoutExpired:
                        process.kill(); process.wait()
        assert responses["jit"] == responses["native-aot"], "JIT/AOT API evidence differs"
        evidence["apiParity"] = True
        if args.games:
            assert games["jit"] == games["native-aot"], "Matched complete games differ"
            evidence["games"] = games; evidence["gameParity"] = True
        (output / "summary.json").write_text(json.dumps(evidence, indent=2))
        print("Service parity verified.", flush=True)


if __name__ == "__main__":
    main()
