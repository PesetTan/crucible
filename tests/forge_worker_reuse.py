#!/usr/bin/env python3
"""Frozen, complete local games: fresh JVM versus sequential reusable JVM."""
import gzip, hashlib, json, os, pathlib, re, sqlite3, statistics, subprocess, time
ROOT = pathlib.Path(__file__).resolve().parents[1]
REPORT = ROOT / "data/reports/forge-worker-reuse"
JAVA = ROOT / "vendor/jdk/Contents/Home/bin/java"
db = sqlite3.connect(f"file:{ROOT}/data/simulator.sqlite?mode=ro", uri=True)
active = json.loads((ROOT / "data/engine-bundles/active.json").read_text())["id"]
BUNDLE = ROOT / "data/engine-bundles" / active
JAR = next(BUNDLE.glob("forge-gui-desktop-*.jar"))
CANDIDATE = REPORT / "candidate"
slots = [json.loads(p) for p, in db.execute("select payload from slots where run_id=?", ("c17ea3ec06644f3eb87be9b3241ee601",))]
groups = {}
for slot in slots:
    groups.setdefault((slot["deckA"], slot["deckB"]), []).append(slot)
selected = [sorted(group, key=lambda s: s["id"])[0] for _, group in sorted(groups.items())]
selected += [json.loads(p) for p, in db.execute("select payload from slots where run_id=?", ("93ab454b9391405db88d4271ace9cc11",))]
requests = []
for i, slot in enumerate(selected):
    run = json.loads(db.execute("select payload from runs where id=?", (slot["runId"],)).fetchone()[0])
    cfg = run["config"]
    decks = [json.loads(db.execute("select payload from decks where id=?", (slot[key],)).fetchone()[0]) for key in ["deckA", "deckB"]]
    request = dict(attemptId=f"reuse-check-{i}", seed=slot["seed"], starter=0 if slot["starter"]==slot["deckA"] else 1,
        turnLimit=cfg["turnLimit"], performanceMode=cfg.get("performanceMode", False), pilotVersion=cfg["pilot"],
        pilotA=cfg["pilotProfiles"][slot["deckA"]], pilotB=cfg["pilotProfiles"][slot["deckB"]])
    for key, deck in zip(["deckA", "deckB"], decks):
        request[key] = [dict(name=c["name"].split(" // ")[0], quantity=c["quantity"],
            attributes=cfg.get("cardAttributes", {}).get(c["name"], c.get("attributes")))
            for c in deck["cards"] if c["zone"]=="MainDeck"]
    requests.append(request)
# Alternate settings/profiles, then repeat earlier requests to detect order dependence.
requests += [dict(requests[0], attemptId="reuse-repeat-0"), dict(requests[-1], attemptId="reuse-repeat-v10", performanceMode=True),
    dict(requests[0], attemptId="reuse-repeat-after-mode")]
(REPORT / "requests.json").write_text(json.dumps(requests, indent=2))

def launch(label, worker=False):
    err = (REPORT / f"{label}.log").open("w")
    cmd = [str(JAVA), "-Xmx1200m", "-Djava.awt.headless=true", f"-Duser.home={ROOT}/data/engine-home",
        "-cp", f"{CANDIDATE}:{JAR}", "ForgeBridge"] + (["--worker"] if worker else [])
    return subprocess.Popen(cmd, cwd=BUNDLE, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=err, text=True), err

def play(proc, request, worker, label):
    start = time.monotonic()
    proc.stdin.write(json.dumps(request, separators=(",", ":")) + "\n"); proc.stdin.flush()
    if not worker: proc.stdin.close()
    records = []; boundary=False
    for line in proc.stdout:
        record = json.loads(line)
        if record["kind"] == "worker_complete":
            assert record["attemptId"] == request["attemptId"] and record["protocolVersion"] == 1
            boundary=True; break
        assert record["attemptId"] == request["attemptId"]
        assert record["sequence"] == len(records)+1
        if record.get("state") is not None:
            raw=json.dumps(record["state"],ensure_ascii=False,separators=(",", ":"))
            assert hashlib.sha256(raw.encode()).hexdigest()==record["stateHash"]
        records.append(record)
    elapsed = time.monotonic()-start
    if not worker: assert proc.wait(timeout=10)==0
    else: assert boundary and proc.poll() is None
    terminals=[r for r in records if r["kind"]=="game_finished"]
    assert len(terminals)==1 and not any(r["kind"]=="attempt_failed" for r in records)
    assert len([r for r in records if r["kind"]=="opening_hand"])==2
    with gzip.open(REPORT / f"{label}.jsonl.gz", "wt") as out:
        out.write("\n".join(json.dumps(r,separators=(",", ":")) for r in records)+"\n")
    normalized=[]
    for record in records:
        r=dict(record);r.pop("attemptId",None)
        if r["kind"]=="game_finished":r.pop("detail",None)
        normalized.append(r)
    return elapsed, normalized

fresh=[]; baseline=[]
for i, request in enumerate(requests):
    proc, err = launch(f"fresh-{i}")
    try:
        elapsed, records = play(proc, request, False, f"fresh-{i}")
        fresh.append(elapsed);baseline.append(records)
        print(f"fresh {i+1}/{len(requests)} {elapsed:.2f}s",flush=True)
    finally:
        if proc.poll() is None:proc.kill();proc.wait()
        err.close()
proc, err=launch("reused",True);reused=[]
try:
    for i, request in enumerate(requests):
        elapsed, records=play(proc,request,True,f"reused-{i}")
        assert records==baseline[i], f"State/action/outcome divergence at request {i}"
        reused.append(elapsed);print(f"reused {i+1}/{len(requests)} {elapsed:.2f}s identical",flush=True)
    proc.stdin.close();assert proc.wait(timeout=10)==0
finally:
    if proc.poll() is None:proc.kill();proc.wait()
    err.close()
summary=dict(games=len(requests),identical=len(requests),sourceBundle=active,
    freshSeconds=fresh,reusedSeconds=reused,freshAverage=statistics.mean(fresh),
    reusedAverage=statistics.mean(reused),warmReusedAverage=statistics.mean(reused[1:]),
    batchSpeedup=sum(fresh)/sum(reused))
(REPORT/"summary.json").write_text(json.dumps(summary,indent=2))
print(json.dumps(summary),flush=True)
