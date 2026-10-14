"""Local, deterministic Crucible experiment backend. No model clients or network calls."""
from __future__ import annotations
import contextlib
import datetime as dt
import gzip
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import sqlite3
import statistics
import subprocess
import time
import uuid

PRIORITIES = {"pilot.priority", "pilotCopyPriority", "pilotCastCopyPriority",
              "pilotDuplicateHandPriority", "pilotDuplicatePriority", "control.keepPriority", "control.graveyardExilePriority"}
FIXTURES = ["PilotAttributeChecks", "AggroIntegrationChecks", "ControlIntegrationChecks", "SubtypePilotChecks",
            "ReanimatorCopyManaChecks", "ReanimatorPilotChecks", "ControlPilotChecks", "OptimizerTuningChecks", "OptimizerFixtureLauncher"]
RESOURCE_DIRS = ["cardsfolder", "tokenscripts", "editions", "ai", "formats", "effects", "defaults"]
SCOPE = "Exploratory local Forge evidence; curated fixtures and finite paired games, not Arena forecasts or full pilot validation."
UNMEASURED = ["general sequencing", "blocking", "mulligans", "general interaction", "full inherited Forge hidden-information audit"]

def encoded(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)

def identity(value):
    return hashlib.sha256(encoded(value).encode()).hexdigest()

def digest(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()

def save(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    tmp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n")
    os.replace(tmp, path)

def identifier(value):
    if not isinstance(value, str) or not re.fullmatch(r"[a-f0-9]{32,64}", value):
        raise ValueError("Invalid experiment/candidate identity")
    return value

def no_links(path):
    path = Path(path).absolute()
    if any(p.is_symlink() for p in [path, *path.parents]):
        raise ValueError("Symlinks are not accepted in optimizer artifacts")
    return path

def class_hashes(folder):
    rows = {p.name: digest(no_links(p)) for p in sorted(Path(folder).glob("*.class"))}
    if not rows or "ForgeBridge.class" not in rows:
        raise ValueError("Missing compiled bridge")
    return rows

def adapter_hash(folder):
    return hashlib.sha256("\n".join(class_hashes(folder).values()).encode()).hexdigest()

def engine_hash(bundle, jar):
    files = [bundle / jar]
    for name in RESOURCE_DIRS:
        files.extend(p for p in (bundle / "res" / name).rglob("*") if p.is_file())
    return hashlib.sha256("\n".join(str(p.relative_to(bundle)) + ":" + digest(p) for p in sorted(files)).encode()).hexdigest()

def validate_bundle(root, bundle_id):
    if not re.fullmatch(r"[a-f0-9]{64}-[a-f0-9]{64}", bundle_id):
        raise ValueError("Invalid bundle identity")
    bundle = no_links(root / "data/engine-bundles" / bundle_id)
    manifest = json.loads((bundle / "bundle.json").read_text())
    if manifest["engineHash"] + "-" + manifest["adapterHash"] != bundle_id:
        raise ValueError("Bundle manifest identity mismatch")
    if Path(manifest["jarName"]).name != manifest["jarName"]:
        raise ValueError("Invalid jar name")
    if adapter_hash(bundle / "bridge") != manifest["adapterHash"] or engine_hash(bundle, manifest["jarName"]) != manifest["engineHash"]:
        raise ValueError("Bundle checksum mismatch")
    return bundle, manifest

def path_value(attrs, path):
    for part in path.split("."):
        if not isinstance(attrs, dict) or part not in attrs:
            return None
        attrs = attrs[part]
    return attrs

def tuning_registry(frozen):
    profile = frozen["profile"]
    paths = sorted(PRIORITIES | {"subtypePilot.developOnPass." + profile})
    registry = {}
    for deck in frozen["targets"]:
        for card in frozen["decks"][deck]["cards"]:
            if card["zone"] != "MainDeck":
                continue
            for path in paths:
                value = path_value(card.get("attributes") or {}, path)
                if type(value) is int and -1000 <= value <= 1000:
                    registry[card["name"].split(" // ")[0] + "|" + path] = dict(default=value, minimum=-1000, maximum=1000,
                                                             scope="Existing reviewed numeric priority; eligibility is unchanged")
    return registry

def validate_tuning(tuning, registry):
    if not isinstance(tuning, dict) or len(tuning) > 64:
        raise ValueError("Tuning must contain at most 64 registered numeric entries")
    for key, value in tuning.items():
        if key not in registry or type(value) is not int or not registry[key]["minimum"] <= value <= registry[key]["maximum"]:
            raise ValueError("Unregistered or out-of-bounds tuning: " + key)
    return dict(sorted(tuning.items()))

def outcome(game):
    if game.get("status") != "completed":
        return None
    return 1 if game["draw"] else 2 if game["winner"] == "A" else 0

def compare_pairs(games, required):
    by_pair = {}
    for game in games:
        by_pair.setdefault(game["pair"], {})[game["arm"]] = game
    pairs = []
    technical = 0
    for number, arms in sorted(by_pair.items()):
        if set(arms) != {"baseline", "candidate"} or any(outcome(g) is None for g in arms.values()):
            technical += sum(g.get("status") == "failed" for g in arms.values())
            continue
        b, c = arms["baseline"], arms["candidate"]
        pairs.append(dict(pair=number, deck=b["deck"], opponent=b["opponent"], baseline=outcome(b), candidate=outcome(c),
                          delta=outcome(c)-outcome(b), elapsedRatio=c["elapsedSeconds"]/b["elapsedSeconds"],
                          engineRatio=c["engineMs"]/b["engineMs"] if b["engineMs"] > 0 else None))
    regressions = sum(p["delta"] < 0 for p in pairs)
    return dict(requiredPairs=required, completePairs=len(pairs), technicalFailures=technical,
                complete=len(pairs) == required and technical == 0, regressions=regressions,
                gains=sum(p["delta"] > 0 for p in pairs), baselineWins=sum(p["baseline"] == 2 for p in pairs),
                candidateWins=sum(p["candidate"] == 2 for p in pairs),
                medianElapsedRatio=statistics.median(p["elapsedRatio"] for p in pairs) if pairs else None,
                medianEngineRatio=statistics.median(p["engineRatio"] for p in pairs if p["engineRatio"] is not None) if any(p["engineRatio"] is not None for p in pairs) else None,
                acceptable=len(pairs) == required and technical == 0 and regressions == 0,
                matchups=[dict(deck=d, opponent=o, pairs=sum(p["deck"] == d and p["opponent"] == o for p in pairs),
                               gains=sum(p["delta"] > 0 and p["deck"] == d and p["opponent"] == o for p in pairs),
                               regressions=sum(p["delta"] < 0 and p["deck"] == d and p["opponent"] == o for p in pairs))
                          for d, o in sorted({(p["deck"], p["opponent"]) for p in pairs})])

def verify_replay(path, request, returncode=0):
    terminals, started, openings, decisions, checkpoints = [], [], set(), [], []
    sequence = 0
    with Path(path).open() as stream:
        for line in stream:
            if len(line) > 8 * 1024 * 1024:
                raise ValueError("Oversized replay record")
            row = json.loads(line)
            sequence += 1
            if row.get("sequence") != sequence or row.get("attemptId") != request["attemptId"]:
                raise ValueError("Replay sequence or attempt identity mismatch")
            if "state" in row:
                # Forge serializes this object in insertion order, with HTML escaping disabled.
                raw = json.dumps(row["state"], ensure_ascii=False, separators=(",", ":"))
                if hashlib.sha256(raw.encode()).hexdigest() != row.get("stateHash"):
                    raise ValueError("Replay state checksum mismatch")
                checkpoints.append(row["stateHash"])
            kind = row.get("kind")
            if kind == "attempt_failed":
                raise ValueError("Typed engine failure: " + str(row.get("reason")))
            if kind == "game_started":
                started.append(row.get("starter"))
            if kind == "opening_hand":
                if row.get("player") in openings:
                    raise ValueError("Duplicate final opening hand")
                openings.add(row.get("player"))
            if kind == "game_finished":
                terminals.append(row)
            if kind == "pilot_decision":
                decisions.append({k: row[k] for k in ["sequence", "actualTurn", "player", "pilotProfile", "decision", "selected", "reason", "visibleEvidence", "optimizerPolicyId"] if k in row})
    if returncode or started != [request["starter"]] or len(terminals) != 1 or openings != {"A", "B"}:
        raise ValueError("Incomplete or contradictory engine terminal evidence")
    terminal = terminals[0]
    if type(terminal.get("draw")) is not bool or not terminal.get("reason") or (not terminal["draw"] and terminal.get("winner") not in ("A", "B")) or (terminal["draw"] and terminal.get("winner") is not None):
        raise ValueError("Invalid terminal outcome")
    duration = re.fullmatch(r"DurationMs=(\d+)", terminal.get("detail", ""))
    if not duration:
        raise ValueError("Missing engine duration")
    return dict(winner=terminal.get("winner"), draw=terminal["draw"], reason=terminal["reason"],
                engineMs=int(duration[1]), actualTurns=terminal["actualTurn"], checkpoints=checkpoints, decisions=decisions)

def separate_jfr_startup(path, log):
    """Keep raw JVM output; isolate only JDK's known pre-main JFR startup banner."""
    path = Path(path)
    raw = path.with_suffix(".jvm-output")
    path.rename(raw)
    seen_json = False
    with raw.open() as source, path.open("w") as target, Path(log).open("a") as errors:
        for line in source:
            if not seen_json and re.fullmatch(r"\[[0-9.]+s\]\[info\]\[jfr,startup\](?: .*)?\n?", line):
                errors.write(line)
                continue
            seen_json = True
            target.write(line)

def jfr_hotspots(events):
    counts = {"jdk.ExecutionSample": {}, "jdk.ObjectAllocationSample": {}}
    for event in events:
        category = counts.get(event.get("type"))
        frames = ((event.get("values") or {}).get("stackTrace") or {}).get("frames") or []
        if category is not None and frames:
            method = frames[0].get("method") or {}
            name = str((method.get("type") or {}).get("name", "unknown")) + "." + str(method.get("name", "unknown"))
            category[name] = category.get(name, 0) + 1
    return {key: sorted(value.items(), key=lambda row: (-row[1], row[0]))[:12] for key, value in counts.items()}

class Interrupted(Exception):
    pass

class Backend:
    def __init__(self, root):
        self.root = Path(root).resolve()
        self.data = no_links(self.root / "data/pilot-optimizations")
        self.data.mkdir(parents=True, exist_ok=True)
        self.dbpath = self.root / "data/simulator.sqlite"
        self.java = self.root / "vendor/jdk/Contents/Home/bin/java"
        self.javac = self.java.with_name("javac")
        self.stop = False
        with self.db() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS pilot_optimizer_experiments(id TEXT PRIMARY KEY,key TEXT UNIQUE NOT NULL,request_hash TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS pilot_optimizer_candidates(id TEXT PRIMARY KEY,experiment_id TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS pilot_optimizer_candidates_experiment ON pilot_optimizer_candidates(experiment_id);
                CREATE TABLE IF NOT EXISTS pilot_optimizer_evaluations(id TEXT PRIMARY KEY,experiment_id TEXT NOT NULL,candidate_id TEXT NOT NULL,payload TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS pilot_optimizer_evaluations_experiment ON pilot_optimizer_evaluations(experiment_id);
                CREATE TABLE IF NOT EXISTS pilot_optimizer_replay_index(replay_hash TEXT PRIMARY KEY,payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS pilot_optimizer_published(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            """)

    @contextlib.contextmanager
    def db(self):
        db = sqlite3.connect(self.dbpath, timeout=30)
        try:
            yield db
            db.commit()
        except BaseException:
            db.rollback()
            raise
        finally:
            db.close()

    def exp(self, eid):
        identifier(eid)
        with self.db() as db:
            row = db.execute("SELECT payload FROM pilot_optimizer_experiments WHERE id=?", (eid,)).fetchone()
        if not row:
            raise ValueError("Unknown experiment")
        return json.loads(row[0])

    def update(self, eid, **changes):
        with self.db() as db:
            db.execute("BEGIN IMMEDIATE")
            row = db.execute("SELECT payload FROM pilot_optimizer_experiments WHERE id=?", (identifier(eid),)).fetchone()
            if not row:
                raise ValueError("Unknown experiment")
            e = json.loads(row[0])
            e.update(changes)
            e["updatedAt"] = now()
            db.execute("UPDATE pilot_optimizer_experiments SET payload=? WHERE id=?", (encoded(e), eid))
        return e

    def candidates(self, eid):
        self.exp(eid)
        with self.db() as db:
            return [json.loads(r[0]) for r in db.execute("SELECT payload FROM pilot_optimizer_candidates WHERE experiment_id=? ORDER BY rowid", (eid,))]

    def candidate(self, eid, cid):
        return next((c for c in self.candidates(eid) if c["id"] == identifier(cid)), None) or self.missing("Unknown candidate")

    def missing(self, message):
        raise ValueError(message)

    def put_candidate(self, eid, candidate):
        with self.db() as db:
            db.execute("UPDATE pilot_optimizer_candidates SET payload=? WHERE id=? AND experiment_id=?", (encoded(candidate), candidate["id"], eid))

    def folder(self, eid):
        return no_links(self.data / identifier(eid))

    def frozen(self, eid):
        e = self.exp(eid)
        path = no_links(self.folder(eid) / "frozen.json")
        if digest(path) != e["frozenHash"]:
            raise ValueError("Frozen experiment checksum mismatch")
        return json.loads(path.read_text())

    def inspect(self, request):
        return dict(scope=SCOPE, defaults=dict(budgetSeconds=7200, maxCodeRounds=3, maxCandidates=12, screenPairs=10, holdoutPairs=40),
                    profiles=sorted({d["profile"] for d in request.get("catalog", [])}),
                    decks=[dict(id=d["manifest"]["id"], name=d["manifest"]["name"], profile=d["profile"], coverage=d["coverage"]) for d in request.get("catalog", [])],
                    numericPaths=sorted(PRIORITIES), tuningBounds=[-1000, 1000], unmeasured=UNMEASURED)

    def create(self, request):
        key = request.get("key", "")
        if not isinstance(key, str) or not 1 <= len(key) <= 128:
            raise ValueError("Provide an Idempotency-Key of 1–128 characters")
        inputs = {k: v for k, v in request.items() if k not in ("key", "catalog")}
        request_hash = identity(inputs)
        with self.db() as db:
            row = db.execute("SELECT id,request_hash FROM pilot_optimizer_experiments WHERE key=?", (key,)).fetchone()
        if row:
            if row[1] != request_hash:
                raise ValueError("Idempotency key belongs to a different request")
            return self.status(row[0])
        profile = request.get("profile")
        catalog = {d["manifest"]["id"]: d for d in request.get("catalog", [])}
        if not profile or profile not in {d["profile"] for d in catalog.values()}:
            # A source run may pin a previously supported revision.
            if not request.get("sourceRunId"):
                raise ValueError("Choose an exact existing target profile from inspect")
        source = None
        slots = []
        if request.get("sourceRunId"):
            with self.db() as db:
                row = db.execute("SELECT payload FROM runs WHERE id=?", (request["sourceRunId"],)).fetchone()
                if not row:
                    raise ValueError("Unknown source run")
                source = json.loads(row[0])
                slots = [json.loads(r[0]) for r in db.execute("SELECT payload FROM slots WHERE run_id=? ORDER BY id", (source["id"],))]
            config = source["config"]
            if config.get("optimizerPolicy"):
                raise ValueError("Use an untuned source baseline; optimized baselines require an explicit new experiment")
            profiles = config.get("pilotProfiles") or {}
            if profile not in profiles.values():
                raise ValueError("Target profile is not recorded in source run")
            frozen_attrs = config.get("cardAttributes")
            if frozen_attrs is None:
                raise ValueError("Source run lacks frozen attributes; create a fresh-baseline experiment")
            for did in config["deckIds"]:
                if did not in catalog:
                    raise ValueError("Source deck is unavailable")
                catalog[did] = dict(catalog[did], profile=profiles.get(did, "forge-default"))
                catalog[did]["manifest"] = json.loads(encoded(catalog[did]["manifest"]))
                for c in catalog[did]["manifest"]["cards"]:
                    c["attributes"] = frozen_attrs.get(c["name"], {})
            eligible = config["deckIds"]
            bundle_id = config["engineHash"] + "-" + config["adapterHash"]
        else:
            eligible = sorted(catalog)
            bundle_id = json.loads((self.root / "data/engine-bundles/active.json").read_text())["id"]
            config = dict(turnLimit=150, timeoutSeconds=180, performanceMode=False, openingPolicy="London mulligan; Arena Bo1 opening hands approximated")
        targets = request.get("deckIds") or [d for d in eligible if catalog[d]["profile"] == profile and catalog[d]["coverage"] == "exploratory"]
        if not targets or any(d not in eligible or catalog[d]["profile"] != profile for d in targets):
            raise ValueError("Target decks must use the selected exact profile")
        opponents = request.get("opponentIds") or [d for d in eligible if d not in targets and catalog[d]["profile"] != profile and catalog[d]["coverage"] == "exploratory"]
        if not opponents or any(d not in eligible or d in targets or catalog[d]["profile"] == profile for d in opponents):
            raise ValueError("Choose distinct opponents with unchanged policies; mirrors need a separate experiment")
        decks = {d: catalog[d]["manifest"] for d in sorted(set(targets + opponents))}
        if any(catalog[d]["coverage"] != "exploratory" for d in decks):
            raise ValueError("Selected decks contain missing engine cards")
        if any(sum(c["quantity"] for c in d["cards"] if c["zone"] == "MainDeck") != 60 or any(type(c["quantity"]) is not int or c["quantity"] <= 0 or c.get("locallyLegal") is False for c in d["cards"] if c["zone"] == "MainDeck") for d in decks.values()):
            raise ValueError("Exactly 60 legal main-deck cards with positive quantities are required")
        budget = request.get("budgetSeconds", 7200)
        screen, holdout = request.get("screenPairs", 10), request.get("holdoutPairs", 40)
        if type(budget) is not int or not 1 <= budget <= 28800:
            raise ValueError("Budget must be 1–28,800 seconds")
        if any(type(n) is not int or n < 2 or n > 1000 or n % 2 for n in [screen, holdout]):
            raise ValueError("Pair counts must be even, between 2 and 1,000")
        bundle, manifest = validate_bundle(self.root, bundle_id)
        # Round-robin subtype strata; adjacent seeds form balanced play/draw pairs.
        strata = {}
        for did in sorted(opponents):
            strata.setdefault((decks[did].get("strategy") or "unknown", decks[did].get("subtype") or "unknown"), []).append(did)
        ordered = []
        for i in range(max(map(len, strata.values()))):
            ordered.extend(v[i] for _, v in sorted(strata.items()) if i < len(v))
        pairings = [(d, o) for o in ordered for d in sorted(targets)]
        schedule = []
        seed_root = request.get("rootSeed", 42)
        if type(seed_root) is not int or not -(2**63) <= seed_root < 2**63:
            raise ValueError("Root seed must be a signed 64-bit integer")
        used = set()
        for phase, count in [("screen", screen), ("holdout", holdout)]:
            for i in range(count):
                d, o = pairings[(i//2) % len(pairings)]
                compatible = [s for s in slots if {s["deckA"], s["deckB"]} == {d, o} and (s["starter"] == d) == (i % 2 == 0) and s["seed"] not in used]
                if compatible:
                    chosen = compatible[0]
                    seed, source_slot = chosen["seed"], chosen["id"]
                else:
                    seed = int.from_bytes(hashlib.sha256(encoded(["optimizer-v1", seed_root, phase, d, o, i]).encode()).digest()[:8], "big", signed=True)
                    source_slot = None
                if seed in used:
                    raise ValueError("Schedule seed collision")
                used.add(seed)
                schedule.append(dict(phase=phase, pair=i, deck=d, opponent=o, seed=seed, starter=0 if i % 2 == 0 else 1, sourceSlotId=source_slot))
        eid = uuid.uuid4().hex
        frozen = dict(version=1, profile=profile, sourceRunId=source["id"] if source else None, bundleId=bundle_id,
                      engineHash=manifest["engineHash"], adapterHash=manifest["adapterHash"], decks=decks,
                      targets=sorted(targets), opponents=sorted(opponents), profiles={d: catalog[d]["profile"] for d in decks},
                      settings={k: config[k] for k in ["turnLimit", "timeoutSeconds", "performanceMode", "openingPolicy"]},
                      schedule=schedule, rootSeed=seed_root, scope=SCOPE)
        folder = self.folder(eid)
        folder.mkdir()
        save(folder / "frozen.json", frozen)
        # Freeze fixture sources, inventory, and backend so later edits cannot silently redefine acceptance.
        for name in FIXTURES:
            dest = folder / "fixtures" / (name + ".java")
            dest.parent.mkdir(exist_ok=True)
            shutil.copyfile(self.root / "tests" / dest.name, dest)
        shutil.copyfile(self.root / "data/reports/subtype-pilots-v001/registry.json", folder / "fixtures/registry.json")
        shutil.copyfile(self.root / "engine/ForgeBridge.java", folder / "bridge-template.java")
        shutil.copyfile(Path(__file__), folder / "backend.py")
        evidence = {str(p.relative_to(folder)): digest(p) for p in sorted(folder.rglob("*")) if p.is_file()}
        e = dict(id=eid, createdAt=now(), updatedAt=now(), status="created", frozenHash=digest(folder / "frozen.json"),
                 evidenceHashes=evidence, budgetSeconds=budget, activeSeconds=0.0, maxCodeRounds=3, codeRounds=0,
                 maxCandidates=12, screenPairs=screen, holdoutPairs=holdout, diagnosticOnly=screen < 10 or holdout < 40,
                 pauseRequested=False, error=None, scope=SCOPE, modelUsage=None)
        with self.db() as db:
            db.execute("INSERT INTO pilot_optimizer_experiments VALUES(?,?,?,?)", (eid, key, request_hash, encoded(e)))
        return self.status(eid)

    def verify_frozen(self, eid):
        e = self.exp(eid)
        for name, expected in e["evidenceHashes"].items():
            if digest(no_links(self.folder(eid) / name)) != expected:
                raise ValueError("Experiment evidence changed: " + name)
        if digest(Path(__file__)) != e["evidenceHashes"]["backend.py"]:
            raise ValueError("Optimizer implementation changed; finish with the frozen backend or create a new experiment")
        return self.frozen(eid)

    def status(self, eid):
        e = self.exp(eid)
        e["remainingSeconds"] = max(0, e["budgetSeconds"] - e["activeSeconds"])
        e["candidates"] = [{k: c.get(k) for k in ["id", "kind", "label", "status", "sourceHash", "classHashes", "adapterHash", "tuningHash", "codeRound", "fixtures", "screen", "holdout"]} for c in self.candidates(eid)]
        e["registry"] = tuning_registry(self.frozen(eid))
        return e

    def run_process(self, eid, command, cwd, stdout, stderr, timeout, stdin=None):
        e = self.exp(eid)
        if self.stop or e.get("pauseRequested") or e["activeSeconds"] >= e["budgetSeconds"]:
            raise Interrupted("Paused or computation budget exhausted")
        stdout, stderr = Path(stdout), Path(stderr)
        stdout.parent.mkdir(parents=True, exist_ok=True)
        start, accounted = time.monotonic(), 0.0
        self.update(eid, checkpoint=dict(operationHash=identity(command), startedAt=now(), output=str(stdout.relative_to(self.root))))
        with stdout.open("wb") as out, stderr.open("wb") as err:
            proc = subprocess.Popen(command, cwd=cwd, stdin=subprocess.PIPE if stdin is not None else subprocess.DEVNULL,
                                    stdout=out, stderr=err, start_new_session=True)
            try:
                if stdin is not None:
                    proc.stdin.write((encoded(stdin) + "\n").encode())
                    proc.stdin.close()
                while True:
                    elapsed = time.monotonic() - start
                    delta, accounted = elapsed - accounted, elapsed
                    with self.db() as db:
                        db.execute("BEGIN IMMEDIATE")
                        current = json.loads(db.execute("SELECT payload FROM pilot_optimizer_experiments WHERE id=?", (eid,)).fetchone()[0])
                        current["activeSeconds"] += delta
                        current["updatedAt"] = now()
                        db.execute("UPDATE pilot_optimizer_experiments SET payload=? WHERE id=?", (encoded(current), eid))
                    if self.stop or current.get("pauseRequested") or current["activeSeconds"] >= current["budgetSeconds"]:
                        raise Interrupted("Paused or computation budget exhausted")
                    if elapsed >= timeout:
                        raise TimeoutError("Operation timed out; not a rules loss")
                    result = proc.poll()
                    if result is not None:
                        return result, elapsed
                    if stdout.stat().st_size > 256 * 1024 * 1024 or stderr.stat().st_size > 64 * 1024 * 1024:
                        raise ValueError("Operation output limit exceeded")
                    time.sleep(0.2)
            finally:
                if proc.poll() is None:
                    os.killpg(proc.pid, signal.SIGKILL)
                proc.wait()
                self.update(eid, checkpoint=None)

    def stage(self, eid, request):
        frozen = self.verify_frozen(eid)
        e = self.exp(eid)
        if e["status"] in ("queued", "running", "promoting", "promoted"):
            raise ValueError("Pause the experiment before staging a candidate")
        tuning = validate_tuning(request.get("tuning", {}), tuning_registry(frozen))
        supplied = request.get("source")
        source = supplied if supplied is not None else (self.folder(eid) / "bridge-template.java").read_text()
        if not isinstance(source, str) or len(source.encode()) > 2 * 1024 * 1024 or "public final class ForgeBridge" not in source:
            raise ValueError("Provide a bounded ForgeBridge.java source snapshot")
        source_hash = hashlib.sha256(source.encode()).hexdigest()
        is_code = source_hash != e["evidenceHashes"]["bridge-template.java"]
        fixture_source = request.get("fixtureSource")
        if is_code and not fixture_source:
            raise ValueError("Code revisions require a focused OptimizerCandidateChecks.java fixture snapshot")
        if fixture_source is not None and (not isinstance(fixture_source, str) or len(fixture_source.encode()) > 1024 * 1024 or "public final class OptimizerCandidateChecks" not in fixture_source):
            raise ValueError("Invalid focused fixture source")
        fixture_hash = hashlib.sha256(fixture_source.encode()).hexdigest() if fixture_source is not None else None
        cid = identity(dict(frozenHash=e["frozenHash"], sourceHash=source_hash, tuning=tuning, fixtureHash=fixture_hash))
        existing = next((c for c in self.candidates(eid) if c["id"] == cid), None)
        if existing:
            return existing
        folder = no_links(self.folder(eid) / "candidates" / cid)
        folder.mkdir(parents=True, exist_ok=False)
        (folder / "ForgeBridge.java").write_text(source)
        if fixture_source is not None:
            (folder / "OptimizerCandidateChecks.java").write_text(fixture_source)
        policy = dict(version=1, id=cid, profile=frozen["profile"], tuning=tuning)
        save(folder / "policy.json", policy)
        with self.db() as db:
            db.execute("BEGIN IMMEDIATE")
            e = json.loads(db.execute("SELECT payload FROM pilot_optimizer_experiments WHERE id=?", (eid,)).fetchone()[0])
            old = [json.loads(r[0]) for r in db.execute("SELECT payload FROM pilot_optimizer_candidates WHERE experiment_id=?", (eid,))]
            if e["status"] in ("queued", "running", "promoting", "promoted") or len(old) >= e["maxCandidates"]:
                raise ValueError("Candidate limit reached or experiment is active")
            revisions = {c["sourceHash"] for c in old if c["kind"] == "code"}
            new_round = is_code and source_hash not in revisions
            if new_round and e["codeRounds"] >= e["maxCodeRounds"]:
                raise ValueError("Three code revision rounds already used")
            e["codeRounds"] += int(new_round)
            c = dict(id=cid, label=str(request.get("label", "code" if is_code else "parameters"))[:120],
                     kind="code" if is_code else "parameters", sourceHash=source_hash, policyHash=digest(folder / "policy.json"),
                     tuning=tuning, tuningHash=identity(tuning), codeRound=e["codeRounds"] if is_code else 0,
                     status="staged", createdAt=now(), classHashes=None, fixtures=None, fixtureHash=fixture_hash)
            db.execute("INSERT INTO pilot_optimizer_candidates VALUES(?,?,?)", (cid, eid, encoded(c)))
            db.execute("UPDATE pilot_optimizer_experiments SET payload=? WHERE id=?", (encoded(e), eid))
        return c

    def sweep(self, eid):
        e = self.exp(eid)
        frozen = self.frozen(eid)
        current = self.candidates(eid)
        if not current:
            self.stage(eid, dict(label="unchanged policy control"))
        for key, spec in sorted(tuning_registry(frozen).items()):
            for offset in [-10, 10]:
                # Reserve capacity for the three explicitly reviewed code revisions.
                if len(self.candidates(eid)) >= e["maxCandidates"] - (e["maxCodeRounds"] - e["codeRounds"]):
                    return
                value = max(spec["minimum"], min(spec["maximum"], spec["default"] + offset))
                if value != spec["default"]:
                    self.stage(eid, dict(label=key + (" +10" if offset > 0 else " -10"), tuning={key: value}))

    def prepare(self, eid, request):
        self.verify_frozen(eid)
        e = self.exp(eid)
        if e["status"] in ("running", "queued", "promoting", "promoted", "completed"):
            raise ValueError("Experiment cannot be started in its current state")
        if e["activeSeconds"] >= e["budgetSeconds"]:
            raise ValueError("Computation budget exhausted; create a new experiment")
        if request.get("search", True):
            self.sweep(eid)
        if not self.candidates(eid):
            raise ValueError("Stage a candidate or enable parameter search")
        return self.update(eid, status="queued", pauseRequested=False, error=None, screenOnly=bool(request.get("screenOnly", False)))

    def pause(self, eid):
        e = self.exp(eid)
        if e["status"] in ("promoting", "promoted"):
            raise ValueError("Cannot pause promotion")
        return self.update(eid, pauseRequested=True, status="pausing" if e["status"] == "running" else "paused")

    def recover(self):
        with self.db() as db:
            rows = list(db.execute("SELECT id,payload FROM pilot_optimizer_experiments"))
        for eid, payload in rows:
            if json.loads(payload)["status"] in ("queued", "running", "pausing", "promoting"):
                self.update(eid, status="interrupted", pauseRequested=True, error="Service restarted; explicit resume required")
        return dict(recovered=True)

    def compile(self, eid, candidate):
        frozen = self.frozen(eid)
        bundle = self.root / "data/engine-bundles" / frozen["bundleId"]
        manifest = json.loads((bundle / "bundle.json").read_text())
        folder = self.folder(eid) / "candidates" / candidate["id"]
        if digest(no_links(folder / "ForgeBridge.java")) != candidate["sourceHash"] or digest(no_links(folder / "policy.json")) != candidate["policyHash"]:
            raise ValueError("Candidate source/policy checksum mismatch")
        if candidate.get("fixtureHash") and digest(no_links(folder / "OptimizerCandidateChecks.java")) != candidate["fixtureHash"]:
            raise ValueError("Focused fixture checksum mismatch")
        if candidate.get("classHashes"):
            if class_hashes(folder / "classes") != candidate["classHashes"]:
                raise ValueError("Candidate class checksum mismatch")
            return
        classes = folder / "classes"
        classes.mkdir(exist_ok=True)
        rc, _ = self.run_process(eid, [str(self.javac), "-cp", str(bundle / manifest["jarName"]), "-d", str(classes), str(folder / "ForgeBridge.java")], bundle,
                                 folder / "compile.out", folder / "compile.log", 120)
        if rc:
            raise ValueError("Candidate compilation failed; inspect compile.log")
        candidate["classHashes"] = class_hashes(classes)
        candidate["adapterHash"] = adapter_hash(classes)
        candidate["status"] = "compiled"
        self.put_candidate(eid, candidate)

    def fixture_run(self, eid, candidate=None):
        frozen = self.frozen(eid)
        folder = self.folder(eid) / ("baseline-fixtures" if candidate is None else "candidates/" + candidate["id"] + "/fixtures")
        result_path = folder / "result.json"
        if result_path.exists():
            result = json.loads(result_path.read_text())
            with self.db() as db:
                row = db.execute("SELECT payload FROM pilot_optimizer_evaluations WHERE id=?", (identity([eid, "fixtures", candidate["id"] if candidate else "baseline"]),)).fetchone()
            if not row or json.loads(row[0])["resultHash"] != digest(result_path):
                raise ValueError("Fixture result checksum mismatch")
            if result.get("positionsHash") and digest(no_links(folder / "positions.json")) != result["positionsHash"]:
                raise ValueError("Raw fixture positions checksum mismatch")
            actual_classes = {p.name: digest(no_links(p)) for p in sorted((folder / "classes").glob("*.class"))}
            if actual_classes != result["classHashes"]:
                raise ValueError("Fixture class checksum mismatch")
            return result
        folder.mkdir(parents=True, exist_ok=True)
        bundle = self.root / "data/engine-bundles" / frozen["bundleId"]
        manifest = json.loads((bundle / "bundle.json").read_text())
        bridge = bundle / "bridge" if candidate is None else self.folder(eid) / "candidates" / candidate["id"] / "classes"
        classes = folder / "classes"
        classes.mkdir(exist_ok=True)
        names = FIXTURES if candidate else [n for n in FIXTURES if n not in ("OptimizerTuningChecks", "OptimizerFixtureLauncher")]
        fixture_sources = [str(self.folder(eid) / "fixtures" / (n + ".java")) for n in names]
        if candidate and candidate.get("fixtureHash"):
            fixture_sources.append(str(self.folder(eid) / "candidates" / candidate["id"] / "OptimizerCandidateChecks.java"))
        cp = os.pathsep.join(map(str, [classes, bridge, bundle / manifest["jarName"]]))
        rc, _ = self.run_process(eid, [str(self.javac), "-cp", cp, "-d", str(classes), *fixture_sources], bundle,
                                 folder / "compile.out", folder / "compile.log", 120)
        if rc:
            raise ValueError("Fixture compilation failed")
        versions = [str(v) for v in [9, 10] if all(("reanimator-" + s + "-v" + str(v)).encode() in (bridge / "ForgeBridge.class").read_bytes() for s in ["terror", "sultai", "cryomancer", "shared"])]
        if b"reanimator-v011" in (bridge / "ForgeBridge.class").read_bytes(): versions.append("11")
        if not versions:
            raise ValueError("Pinned bridge has no supported diagnostic revisions")
        policy_path = self.folder(eid) / "candidates" / candidate["id"] / "policy.json" if candidate else None
        checks = [("PilotAttributeChecks", [str(self.folder(eid) / "fixtures/registry.json"), str(folder / "positions.json"), ",".join(versions)]),
                  ("ReanimatorPilotChecks", []), ("ControlPilotChecks", []), ("ReanimatorCopyManaChecks", [])]
        if candidate:
            checks.append(("OptimizerTuningChecks", []))
            if candidate.get("fixtureHash"):
                checks.append(("OptimizerCandidateChecks", []))
        guards = []
        for name, args in checks:
            entry = ["OptimizerFixtureLauncher", str(policy_path), name] if candidate and name != "OptimizerTuningChecks" else [name]
            cmd = [str(self.java), "-Xmx1400m", "-Djava.awt.headless=true", "-Duser.home=" + str(folder / "engine-home"), "-cp", cp, *entry, *args]
            rc, elapsed = self.run_process(eid, cmd, bundle, folder / (name + ".out"), folder / (name + ".log"), 600)
            guards.append(dict(name=name, passed=rc == 0, elapsedSeconds=elapsed, log=str((folder / (name + ".log")).relative_to(self.root))))
        raw = json.loads((folder / "positions.json").read_text()) if (folder / "positions.json").exists() else {"cases": []}
        result = dict(cases=raw["cases"], guards=guards, classHashes={p.name: digest(p) for p in sorted(classes.glob("*.class"))},
                      positionsHash=digest(folder / "positions.json") if (folder / "positions.json").exists() else None)
        save(result_path, result)
        with self.db() as db:
            db.execute("INSERT INTO pilot_optimizer_evaluations VALUES(?,?,?,?)", (identity([eid, "fixtures", candidate["id"] if candidate else "baseline"]), eid,
                       candidate["id"] if candidate else "baseline", encoded(dict(kind="fixtures", resultHash=digest(result_path)))))
        return result

    def fixture_comparison(self, baseline, candidate, target):
        base = {(c["profile"], c["dimension"], c["id"]): c for c in baseline["cases"]}
        cand = {(c["profile"], c["dimension"], c["id"]): c for c in candidate["cases"]}
        if len(base) != len(baseline["cases"]) or len(cand) != len(candidate["cases"]) or base.keys() != cand.keys():
            return dict(acceptable=False, reason="Fixture inventory changed", regressions=[], legacyChanges=[])
        regressions, legacy_changes = [], []
        for key, row in cand.items():
            old = base[key]
            if old["status"] == "passed" and row["status"] != "passed" or row["status"] == "error":
                regressions.append(list(key))
            if key[0] != target and (old["status"], old["actual"]) != (row["status"], row["actual"]):
                legacy_changes.append(list(key))
        guards_ok = all(c["passed"] for c in candidate["guards"])
        return dict(acceptable=guards_ok and not regressions and not legacy_changes, regressions=regressions, legacyChanges=legacy_changes,
                    guards=candidate["guards"], cases=len(cand), baselineFailures=sum(c["status"] == "failed" for c in base.values()),
                    unmeasured=UNMEASURED)

    def games(self, eid, cid, phase=None):
        with self.db() as db:
            rows = [json.loads(r[0]) for r in db.execute("SELECT payload FROM pilot_optimizer_evaluations WHERE experiment_id=? AND candidate_id=? ORDER BY rowid", (eid, cid))]
        return [r for r in rows if r.get("kind") == "game" and (phase is None or r["phase"] == phase)]

    def play(self, eid, candidate, slot, arm, bundle_override=None, phase_override=None, profile=False):
        frozen = self.frozen(eid)
        phase = phase_override or slot["phase"]
        evaluation_id = identity([eid, candidate["id"], phase, slot["pair"], arm])
        with self.db() as db:
            old = db.execute("SELECT payload FROM pilot_optimizer_evaluations WHERE id=?", (evaluation_id,)).fetchone()
        if old:
            game = json.loads(old[0])
            if game["status"] != "completed":
                raise ValueError("Technical failure requires a new experiment; it is not a loss")
            if digest(no_links(self.root / game["replay"])) != game["replayHash"]:
                raise ValueError("Stored replay checksum mismatch")
            return game
        folder = self.folder(eid) / "candidates" / candidate["id"] / phase
        folder.mkdir(parents=True, exist_ok=True)
        prefix = folder / (f'{slot["pair"]:04}-{arm}-' + uuid.uuid4().hex[:8])
        bundle = bundle_override or self.root / "data/engine-bundles" / frozen["bundleId"]
        manifest = json.loads((bundle / "bundle.json").read_text())
        bridge = bundle / "bridge" if arm == "baseline" or bundle_override else self.folder(eid) / "candidates" / candidate["id"] / "classes"
        if arm == "candidate" and not bundle_override and class_hashes(bridge) != candidate["classHashes"]:
            raise ValueError("Candidate classes changed")
        request = dict(attemptId=evaluation_id, seed=slot["seed"], starter=slot["starter"], **{k: frozen["settings"][k] for k in ["turnLimit", "performanceMode"]},
                       pilotVersion="optimizer-v1/" + candidate["id"] if arm == "candidate" else "optimizer-baseline-v1",
                       pilotA=frozen["profile"], pilotB=frozen["profiles"][slot["opponent"]])
        for side, did in [("A", slot["deck"]), ("B", slot["opponent"])]:
            request["deck" + side] = [dict(name=c["name"].split(" // ")[0], quantity=c["quantity"], attributes=c.get("attributes") or {}) for c in frozen["decks"][did]["cards"] if c["zone"] == "MainDeck"]
        if arm == "candidate":
            request["optimizerPolicyA"] = dict(version=1, id=candidate["id"], profile=frozen["profile"], tuning=candidate["tuning"])
        save(prefix.with_suffix(".request.json"), request)
        cmd = [str(self.java), "-Xmx1200m", "-Djava.awt.headless=true", "-Duser.home=" + str(self.folder(eid) / "engine-home"),
               "-cp", os.pathsep.join(map(str, [bridge, bundle / manifest["jarName"]])), "ForgeBridge"]
        if profile:
            cmd.insert(1, "-XX:StartFlightRecording=filename=" + str(prefix.with_suffix(".jfr")) + ",settings=profile,dumponexit=true")
            # JFR startup logging belongs on stderr, never in the JSON replay stream.
            cmd.insert(2, "-Xlog:jfr*=warning:stderr")
        game = dict(kind="game", id=evaluation_id, phase=phase, pair=slot["pair"], arm=arm, deck=slot["deck"], opponent=slot["opponent"],
                    seed=slot["seed"], starter=slot["starter"], startedAt=now(), requestHash=digest(prefix.with_suffix(".request.json")),
                    request=str(prefix.with_suffix(".request.json").relative_to(self.root)), profiled=profile,
                    sourceHash=candidate["sourceHash"] if arm == "candidate" else None, tuningHash=candidate["tuningHash"] if arm == "candidate" else identity({}),
                    classHashes=class_hashes(bridge), fixtureIdentity=self.exp(eid)["evidenceHashes"])
        try:
            rc, elapsed = self.run_process(eid, cmd, bundle, prefix.with_suffix(".jsonl"), prefix.with_suffix(".log"), frozen["settings"]["timeoutSeconds"], request)
            if profile:
                separate_jfr_startup(prefix.with_suffix(".jsonl"), prefix.with_suffix(".log"))
            result = verify_replay(prefix.with_suffix(".jsonl"), request, rc)
            game.update(result, status="completed", elapsedSeconds=elapsed, finishedAt=now())
        except Interrupted:
            # Preserve partial evidence; this arm is rerun explicitly on resume.
            save(prefix.with_suffix(".interrupted.json"), dict(game, status="interrupted", finishedAt=now()))
            raise
        except (ValueError, TimeoutError, OSError) as error:
            game.update(status="failed", error=str(error), finishedAt=now())
        if prefix.with_suffix(".jsonl").exists():
            with prefix.with_suffix(".jsonl").open("rb") as inp, gzip.open(prefix.with_suffix(".jsonl.gz"), "wb") as out:
                shutil.copyfileobj(inp, out)
            prefix.with_suffix(".jsonl").unlink()
            game.update(replay=str(prefix.with_suffix(".jsonl.gz").relative_to(self.root)), replayHash=digest(prefix.with_suffix(".jsonl.gz")))
        save(prefix.with_suffix(".analysis.json"), game)
        if profile and prefix.with_suffix(".jfr").exists():
            game["flightRecording"] = str(prefix.with_suffix(".jfr").relative_to(self.root))
            game["flightRecordingHash"] = digest(prefix.with_suffix(".jfr"))
        with self.db() as db:
            db.execute("INSERT INTO pilot_optimizer_evaluations VALUES(?,?,?,?)", (evaluation_id, eid, candidate["id"], encoded(game)))
        if game["status"] != "completed":
            raise ValueError(game["error"])
        return game

    def evaluate_games(self, eid, candidate, phase):
        frozen = self.frozen(eid)
        for slot in [s for s in frozen["schedule"] if s["phase"] == phase]:
            order = ["baseline", "candidate"] if slot["pair"] % 2 == 0 else ["candidate", "baseline"]
            for arm in order:
                self.play(eid, candidate, slot, arm)
        return compare_pairs(self.games(eid, candidate["id"], phase), self.exp(eid)[phase + "Pairs"])

    def work(self, eid):
        e = self.exp(eid)
        if e["status"] != "queued" or e.get("pauseRequested"):
            return e
        self.update(eid, status="running")
        try:
            frozen = self.verify_frozen(eid)
            validate_bundle(self.root, frozen["bundleId"])
            baseline = self.fixture_run(eid)
            if not baseline["cases"] or not all(g["passed"] for g in baseline["guards"]):
                raise ValueError("Baseline required guards are not healthy")
            for candidate in self.candidates(eid):
                if candidate["status"] in ("rejected", "screened", "eligible", "holdout-rejected"):
                    continue
                self.compile(eid, candidate)
                checks = self.fixture_run(eid, candidate)
                candidate["fixtures"] = self.fixture_comparison(baseline, checks, frozen["profile"])
                candidate["status"] = "fixtures-passed" if candidate["fixtures"]["acceptable"] else "rejected"
                self.put_candidate(eid, candidate)
                if candidate["status"] == "rejected":
                    continue
                candidate["screen"] = self.evaluate_games(eid, candidate, "screen")
                candidate["status"] = "screened" if candidate["screen"]["acceptable"] else "rejected"
                self.put_candidate(eid, candidate)
            finalists = [c for c in self.candidates(eid) if c["status"] in ("screened", "eligible", "holdout-rejected")]
            if self.exp(eid).get("screenOnly"):
                self.update(eid, status="awaiting-review")
                report = self.report(eid)
                save(self.folder(eid) / "report.json", report)
                return report
            if finalists:
                best = sorted(finalists, key=lambda c: (-c["screen"]["gains"], c["screen"]["medianElapsedRatio"], c["id"]))[0]
                best["holdout"] = self.evaluate_games(eid, best, "holdout")
                best["status"] = "eligible" if best["holdout"]["acceptable"] and not self.exp(eid)["diagnosticOnly"] else "holdout-rejected"
                self.put_candidate(eid, best)
                self.update(eid, finalist=best["id"])
            self.update(eid, status="completed", finishedAt=now())
        except Interrupted as error:
            e = self.exp(eid)
            self.update(eid, status="budget-exhausted" if e["activeSeconds"] >= e["budgetSeconds"] else "paused", error=str(error))
        except Exception as error:
            self.update(eid, status="failed", error=str(error))
        report = self.report(eid)
        save(self.folder(eid) / "report.json", report)
        return report

    def report(self, eid):
        e = self.status(eid)
        return dict(experiment=e, scope=SCOPE, unmeasured=UNMEASURED,
                    promotionEligible=[c["id"] for c in self.candidates(eid) if c["status"] == "eligible" and e["status"] == "completed" and not e["diagnosticOnly"]],
                    interpretation="Zero observed paired regressions is a finite-sample guard, not proof of general non-inferiority.",
                    tokenUsage=e.get("modelUsage"), accounting="Local computation only; queue time and GPT review excluded. No model API calls.")

    def profile(self, eid, cid):
        c = self.candidate(eid, cid)
        if self.exp(eid)["status"] in ("queued", "running", "promoting", "promoted"):
            raise ValueError("Pause the experiment before runtime profiling")
        self.verify_frozen(eid)
        self.compile(eid, c)
        game = self.play(eid, c, self.frozen(eid)["schedule"][0], "candidate", phase_override="runtime-profile-" + uuid.uuid4().hex[:8], profile=True)
        recording = self.root / game["flightRecording"]
        summary = recording.with_suffix(".samples.json")
        rc, _ = self.run_process(eid, [str(self.java.with_name("jfr")), "print", "--json", "--events", "jdk.ExecutionSample,jdk.ObjectAllocationSample", str(recording)],
                                 self.root, summary, summary.with_suffix(".log"), 60)
        if rc:
            raise ValueError("Could not inspect Flight Recorder samples")
        samples = jfr_hotspots(json.loads(summary.read_text()).get("recording", {}).get("events", []))
        return dict(recording=game["flightRecording"], sha256=game["flightRecordingHash"], hotspots=samples["jdk.ExecutionSample"],
                    allocationHotspots=samples["jdk.ObjectAllocationSample"],
                    scope="Execution and allocation samples are separate diagnostics; excluded from screening, holdout and speed rankings")

    def audit_games(self, eid, cid):
        for game in [g for g in self.games(eid, cid) if g["phase"] in ("screen", "holdout")]:
            if game["status"] != "completed":
                raise ValueError("Technical failures block promotion")
            path, request_path = no_links(self.root / game["replay"]), no_links(self.root / game["request"])
            if digest(path) != game["replayHash"] or digest(request_path) != game["requestHash"]:
                raise ValueError("Evaluation evidence checksum mismatch")
            temporary = self.folder(eid) / (".audit-" + uuid.uuid4().hex + ".jsonl")
            try:
                with gzip.open(path, "rb") as inp, temporary.open("wb") as out:
                    shutil.copyfileobj(inp, out)
                checked = verify_replay(temporary, json.loads(request_path.read_text()))
                if any(checked[k] != game[k] for k in ["winner", "draw", "reason", "engineMs", "checkpoints"]):
                    raise ValueError("Stored outcomes disagree with raw terminal evidence")
            finally:
                temporary.unlink(missing_ok=True)

    def evidence(self, eid, limit=8):
        if type(limit) is not int or not 1 <= limit <= 20:
            raise ValueError("Evidence limit must be 1–20")
        frozen = self.frozen(eid)
        patterns, failures = {}, []
        with self.db() as db:
            slots = [json.loads(r[0]) for r in db.execute("SELECT payload FROM slots WHERE run_id=?", (frozen["sourceRunId"],))] if frozen["sourceRunId"] else []
        replay_refs = []
        for slot in slots:
            if slot["status"] != "completed":
                failures.extend(dict(slot=slot["id"], status=a["status"], error=str(a.get("error", ""))[:240]) for a in slot["attempts"] if a["status"] == "failed")
                continue
            a = next((a for a in reversed(slot["attempts"]) if a["status"] == "completed"), None)
            if a and a.get("replay") and a.get("replayHash"):
                replay_refs.append((self.root / "data/replays" / Path(a["replay"]).name, a["replayHash"]))
        for c in self.candidates(eid):
            replay_refs.extend((self.root / g["replay"], g["replayHash"]) for g in self.games(eid, c["id"]) if g.get("replay"))
        for path, expected in replay_refs:
            if digest(no_links(path)) != expected:
                failures.append(dict(replay=str(path.relative_to(self.root)), error="Replay checksum mismatch"))
                continue
            with self.db() as db:
                cached = db.execute("SELECT payload FROM pilot_optimizer_replay_index WHERE replay_hash=?", (expected,)).fetchone()
            if cached:
                grouped = json.loads(cached[0])
            else:
                grouped = {}
                with gzip.open(path, "rt") as stream:
                    for line in stream:
                        r = json.loads(line)
                        if r.get("kind") != "pilot_decision" or r.get("pilotProfile") != frozen["profile"]:
                            continue
                        key = identity([r.get("decision"), r.get("selected"), r.get("reason")])
                        entry = grouped.setdefault(key, dict(decision=str(r.get("decision", ""))[:120], selected=str(r.get("selected", ""))[:160],
                                                           reason=str(r.get("reason", ""))[:300], count=0, examples=[]))
                        entry["count"] += 1
                        if len(entry["examples"]) < 2:
                            entry["examples"].append(dict(replay=str(path.relative_to(self.root)), sequence=r["sequence"], actualTurn=r.get("actualTurn"),
                                                          visibleEvidence=[str(x)[:100] for x in r.get("visibleEvidence", [])[:4]]))
                with self.db() as db:
                    db.execute("INSERT OR IGNORE INTO pilot_optimizer_replay_index VALUES(?,?)", (expected, encoded(grouped)))
            for key, entry in grouped.items():
                combined = patterns.setdefault(key, dict(entry, count=0, examples=[]))
                combined["count"] += entry["count"]
                combined["examples"] = (combined["examples"] + entry["examples"])[:2]
        fixture_failures = []
        for c in self.candidates(eid):
            if c.get("fixtures") and not c["fixtures"]["acceptable"]:
                fixture_failures.append(dict(candidate=c["id"], regressions=c["fixtures"].get("regressions", [])[:limit], legacyChanges=c["fixtures"].get("legacyChanges", [])[:limit]))
        packet = dict(scope=SCOPE, profile=frozen["profile"], replayCount=len(replay_refs), patterns=sorted(patterns.values(), key=lambda p: (-p["count"], p["decision"]))[:limit],
                      fixtureFailures=fixture_failures[:limit], technicalFailures=failures[:limit], unmeasured=UNMEASURED,
                      caveat="Frequency is not proof of poor play. Absent override records do not establish fallback quality.")
        text = encoded(packet)
        packet["evidenceBytes"] = len(text.encode())
        packet["tokenEstimate"] = math.ceil(len(text.encode())/4)
        packet["tokenEstimateMeaning"] = "Approximate packet size, not billed tokens"
        return packet

    def detail(self, eid, request):
        # Only references already included in this experiment's compact packet are accepted.
        ref, sequence = request.get("replay"), request.get("sequence")
        allowed = {(x["replay"], x["sequence"]) for p in self.evidence(eid, 20)["patterns"] for x in p["examples"]}
        if (ref, sequence) not in allowed:
            raise ValueError("Choose a replay/sequence reference from evidence")
        path = no_links(self.root / ref)
        with gzip.open(path, "rt") as stream:
            for line in stream:
                r = json.loads(line)
                if r.get("sequence") == sequence:
                    # Decision-only excerpt; never include omniscient snapshots in policy input.
                    return {k: r[k] for k in ["sequence", "actualTurn", "player", "pilotProfile", "decision", "options", "selected", "reason", "visibleEvidence", "source", "sourceId", "candidateScores"] if k in r}
        raise ValueError("Decision reference missing")

    def source(self, eid, request):
        self.verify_frozen(eid)
        cid, term = request.get("candidateId"), request.get("term", "")
        if not isinstance(term, str) or not 2 <= len(term) <= 80:
            raise ValueError("Provide a literal source search term of 2–80 characters")
        if cid:
            c = self.candidate(eid, cid)
            path, expected = self.folder(eid) / "candidates" / cid / "ForgeBridge.java", c["sourceHash"]
        else:
            path, expected = self.folder(eid) / "bridge-template.java", self.exp(eid)["evidenceHashes"]["bridge-template.java"]
        if digest(no_links(path)) != expected:
            raise ValueError("Source checksum mismatch")
        lines = path.read_text().splitlines()
        matches = [i for i, line in enumerate(lines) if term.lower() in line.lower()]
        if not matches:
            return dict(source=str(path.relative_to(self.root)), sha256=expected, matches=0, excerpt="")
        start = max(0, matches[0] - 8)
        excerpt = "\n".join(f"{i+1}: {lines[i]}" for i in range(start, min(len(lines), start + 40)))
        return dict(source=str(path.relative_to(self.root)), sha256=expected, matches=len(matches), startLine=start+1,
                    excerpt=excerpt[:12000], truncated=len(excerpt) > 12000)

    def promote(self, eid, cid):
        frozen = self.verify_frozen(eid)
        e, c = self.exp(eid), self.candidate(eid, cid)
        with self.db() as db:
            prior = db.execute("SELECT payload FROM pilot_optimizer_published WHERE id=?", (cid,)).fetchone()
        if prior:
            return json.loads(prior[0])
        if e["status"] != "completed" or e["diagnosticOnly"] or e["screenPairs"] < 10 or e["holdoutPairs"] < 40 or c["status"] != "eligible" or not c["fixtures"]["acceptable"]:
            raise ValueError("Candidate has not passed complete promotion gates")
        self.audit_games(eid, cid)
        screen = compare_pairs(self.games(eid, cid, "screen"), e["screenPairs"])
        comparison = compare_pairs(self.games(eid, cid, "holdout"), e["holdoutPairs"])
        if not screen["acceptable"] or not comparison["acceptable"]:
            raise ValueError("Screening/holdout is incomplete or has observed regressions")
        self.compile(eid, c)
        baseline_checks = self.fixture_run(eid)
        candidate_checks = self.fixture_run(eid, c)
        if not baseline_checks["cases"] or not self.fixture_comparison(baseline_checks, candidate_checks, frozen["profile"])["acceptable"]:
            raise ValueError("Fixture promotion audit did not pass")
        baseline, manifest = validate_bundle(self.root, frozen["bundleId"])
        bundle_id = manifest["engineHash"] + "-" + c["adapterHash"]
        final = no_links(self.root / "data/engine-bundles" / bundle_id)
        previous = json.loads((final.parent / "active.json").read_text())
        self.update(eid, status="promoting", pauseRequested=False)
        try:
            if not final.exists():
                staging = final.parent / (".optimizer-" + uuid.uuid4().hex)
                shutil.copytree(baseline, staging)
                shutil.rmtree(staging / "bridge")
                shutil.copytree(self.folder(eid) / "candidates" / cid / "classes", staging / "bridge")
                save(staging / "bundle.json", dict(manifest, adapterHash=c["adapterHash"]))
                if engine_hash(staging, manifest["jarName"]) != manifest["engineHash"] or adapter_hash(staging / "bridge") != c["adapterHash"]:
                    raise ValueError("Publication copy checksum mismatch")
                os.rename(staging, final)
            validate_bundle(self.root, bundle_id)
            for slot in frozen["schedule"][:2]:
                self.play(eid, c, slot, "candidate", final, "publication-smoke")
            # Publication only switches immutable identity; applying this policy stays opt-in.
            save(final.parent / "active.json", dict(id=bundle_id))
            policy = dict(version=1, id=cid, profile=frozen["profile"], tuning=c["tuning"], tuningHash=c["tuningHash"],
                          engineHash=manifest["engineHash"], adapterHash=c["adapterHash"], bundleId=bundle_id,
                          sourceHash=c["sourceHash"], experimentId=eid, publishedAt=now())
            with self.db() as db:
                db.execute("INSERT INTO pilot_optimizer_published VALUES(?,?)", (cid, encoded(policy)))
            self.update(eid, status="promoted", publishedPolicyId=cid)
            return policy
        except BaseException:
            save(final.parent / "active.json", previous)
            self.update(eid, status="completed", error="Promotion failed; active pointer restored")
            raise

    def dispatch(self, action, request):
        eid = request.get("id")
        if action == "inspect": return self.inspect(request)
        if action == "create": return self.create(request)
        if action == "recover": return self.recover()
        if action == "list":
            with self.db() as db:
                return [dict(id=e["id"], status=e["status"], createdAt=e["createdAt"], activeSeconds=e["activeSeconds"]) for e in [json.loads(r[0]) for r in db.execute("SELECT payload FROM pilot_optimizer_experiments ORDER BY rowid DESC LIMIT 100")]]
        identifier(eid)
        if action == "stage": return self.stage(eid, request)
        if action == "prepare": return self.prepare(eid, request)
        if action == "work": return self.work(eid)
        if action == "pause": return self.pause(eid)
        if action == "status": return self.status(eid)
        if action == "report": return self.report(eid)
        if action == "evidence": return self.evidence(eid, request.get("limit", 8))
        if action == "detail": return self.detail(eid, request)
        if action == "source": return self.source(eid, request)
        if action == "promote": return self.promote(eid, request["candidateId"])
        if action == "profile": return self.profile(eid, request["candidateId"])
        raise ValueError("Unknown optimizer action")
