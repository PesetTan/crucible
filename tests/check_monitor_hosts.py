#!/usr/bin/env python3
"""Exercise published monitor hosts against disposable data, never the live simulator DB."""
import argparse
import json
import os
from pathlib import Path
import shutil
import socket
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def unused_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def get(port, path="/api/v1/health"):
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{port}{path}", timeout=2) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def ready(port, process):
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AssertionError("Host exited; inspect the integration log")
        try:
            code, data = get(port)
            if code == 200:
                return json.loads(data)
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(0.2)
    raise AssertionError("Host readiness timeout")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--backend-runtime", type=Path)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"))
    args = parser.parse_args()
    runtime = args.runtime.resolve()
    source = Path(__file__).resolve().parents[1]
    children = []
    with tempfile.TemporaryDirectory(prefix="crucible-monitor-hosts-") as temporary:
        base = Path(temporary).resolve()
        root = base / "projects/crucible"
        (root / "docs/specs").mkdir(parents=True)
        (root / "scripts").mkdir()
        for name in ("pilot-optimizer.py", "pilot_optimizer.py"):
            shutil.copy2(source / "scripts" / name, root / "scripts" / name)
        # Startup verifies frozen deck fingerprints. Copy its card source read-only, while all
        # simulator state, imported manifests, sessions, and replays remain disposable.
        source_cards = source.parents[1] / "standard_cards.sqlite"
        with sqlite3.connect(f"file:{source_cards}?mode=ro", uri=True) as original:
            with sqlite3.connect(base / "standard_cards.sqlite") as fixture:
                original.backup(fixture)
        backend_port, ui_port = unused_port(), unused_port()
        while ui_port == backend_port:
            ui_port = unused_port()

        def start(mode, port, runtime_folder=runtime):
            if mode == "service" and args.backend_runtime:
                runtime_folder = args.backend_runtime.resolve()
            log = open(base / f"{mode}-{len(children)}.log", "wb")
            env = dict(os.environ, MTG_HOST_MODE=mode, MTG_PORT=str(port), MTG_PROJECT_ROOT=str(root),
                       MTG_UI_STATE_ROOT=str(root / "persistent-ui-state"),
                       MTG_BACKEND_URL=f"http://127.0.0.1:{backend_port}/", ASPNETCORE_ENVIRONMENT="Production")
            env.pop("MTG_LAN_ADDRESS", None)
            executable = runtime_folder / "CrucibleBackend"
            assembly = "CrucibleBackend.dll" if mode == "service" and (runtime_folder / "CrucibleBackend.dll").is_file() else "DeckTester.dll"
            command = [str(executable)] if mode == "service" and executable.is_file() else [args.dotnet, str(runtime_folder / assembly)]
            process = subprocess.Popen(command, cwd=runtime_folder,
                                       env=env, stdout=log, stderr=subprocess.STDOUT)
            log.close()
            children.append(process)
            return process

        def stop(process):
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=35)

        try:
            backend = start("service", backend_port)
            health = ready(backend_port, backend)
            assert health["application"] == "crucible" and health["role"] == "service"
            assert health["database"] == "available" and health["projectRoot"] == str(root)
            assert health["processId"] == backend.pid and health["activeRuns"] == 0
            ui = start("ui", ui_port)
            ui_health = ready(ui_port, ui)
            assert ui_health["role"] == "ui" and ui_health["database"] == "not-applicable"
            # A duplicate backend must fail without taking the existing listener or lock.
            duplicate = start("service", unused_port())
            assert duplicate.wait(timeout=15) != 0
            assert json.loads(get(backend_port)[1])["instanceId"] == health["instanceId"]
            # A health probe must not create a missing DB or load games/engine resources.
            database = root / "data/simulator.sqlite"
            moved = database.with_suffix(".health-fixture")
            database.rename(moved)
            try:
                started = time.monotonic()
                status, payload = get(backend_port)
                assert status == 503 and not json.loads(payload)["ready"]
                assert time.monotonic() - started < 2 and not database.exists()
                assert get(ui_port)[0] == 200
            finally:
                moved.rename(database)
            assert get(backend_port)[0] == 200
            # Switching the frontend runtime never touches the backend's loaded files or identity.
            replacement = base / "replacement-runtime"
            shutil.copytree(runtime, replacement)
            stop(ui)
            ui = start("ui", ui_port, replacement)
            assert ready(ui_port, ui)["instanceId"] != ui_health["instanceId"]
            assert json.loads(get(backend_port)[1])["instanceId"] == health["instanceId"]
            assert backend.poll() is None
            # Backend outage leaves frontend health independent and responsive.
            stop(backend)
            assert get(ui_port)[0] == 200
            # Seed abandoned work. Startup recovery must preserve explicit resume semantics.
            config = dict(deckIds=[], gamesPerPair=2, workers=1, timeoutSeconds=180, turnLimit=150,
                          rootSeed=42, diagnostic=True, engineHash="fixture", adapterHash="fixture")
            run = dict(id="interrupted-fixture", createdAt="2026-10-15T00:00:00Z", status="queued", config=config, total=0)
            with sqlite3.connect(database) as db:
                db.execute("INSERT INTO runs(id,payload) VALUES(?,?)", (run["id"], json.dumps(run)))
            backend = start("service", backend_port)
            recovered = ready(backend_port, backend)
            assert recovered["queuedRuns"] == 0 and recovered["activeRuns"] == 0
            status, payload = get(backend_port, "/api/v1/runs/interrupted-fixture")
            run = json.loads(payload)
            assert status == 200 and run["status"] == "cancelled" and "resume explicitly" in run["error"]
            assert (root / "persistent-ui-state/protection-keys").is_dir()
            assert not (root / "data/engine-bundles").exists()
            print("Published-host checks passed: role identity, bounded DB health, duplicate-service lock, frontend isolation, outage recovery, persistent UI state, explicit simulation resume.")
        except BaseException:
            for log in base.glob("*.log"):
                print(log.name, log.read_text(errors="replace")[-2500:])
            raise
        finally:
            for process in reversed(children):
                try:
                    stop(process)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait(timeout=5)


if __name__ == "__main__":
    main()
