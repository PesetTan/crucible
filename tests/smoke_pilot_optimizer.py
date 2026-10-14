"""Real CLI/API smoke: stage, pause, resume, complete paired games; never promote."""
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid

ROOT=Path(__file__).resolve().parents[1]
CLI=ROOT/"scripts/pilot-optimizer.py"
BASE="http://127.0.0.1:5079"

def cli(*args):
    run=subprocess.run([sys.executable,str(CLI),*args],cwd=ROOT,capture_output=True,text=True,timeout=600)
    if run.returncode:raise RuntimeError(run.stderr)
    return json.loads(run.stdout)

def immutable():
    with sqlite3.connect((ROOT/"data/simulator.sqlite").as_uri()+"?mode=ro",uri=True) as db:
        hashes={t:hashlib.sha256("\n".join(r[0] for r in db.execute("SELECT payload FROM "+t+" ORDER BY id")).encode()).hexdigest() for t in ["decks","runs","slots"]}
    return dict(payloadHashes=hashes,activeBundle=json.loads((ROOT/"data/engine-bundles/active.json").read_text()))

def main():
    before=immutable()
    inventory=cli("inspect")
    target=next(d for d in inventory["decks"] if d["profile"]=="reanimator-terror-v10")
    opponent=next(d for d in inventory["decks"] if d["profile"]=="auras-v001")
    # Mutation authentication must be enforced even on the new endpoints.
    try:
        urllib.request.urlopen(urllib.request.Request(BASE+"/api/v1/pilot-optimizations",data=b'{}',method="POST",headers={"Content-Type":"application/json"}))
        raise AssertionError("Unauthenticated mutation accepted")
    except urllib.error.HTTPError as error:assert error.code==403,error.code
    e=cli("create","--profile",target["profile"],"--deck",target["id"],"--opponent",opponent["id"],
          "--screen-pairs","2","--holdout-pairs","2","--budget-seconds","1800","--key","smoke-"+uuid.uuid4().hex)
    eid=e["id"]
    print(json.dumps(dict(experimentId=eid,target=target["name"],opponent=opponent["name"])),flush=True)
    c=cli("stage",eid,"--label","Unchanged policy integration control")
    cli("start",eid,"--no-search")
    # Pause after a real child computation has begun so recovery exercises partial evidence.
    deadline=time.monotonic()+120
    while time.monotonic()<deadline:
        status=cli("status",eid)
        if status["status"]=="running" and status.get("checkpoint"):break
        if status["status"] in ["failed","completed"]:raise AssertionError(status)
        time.sleep(.5)
    else:raise AssertionError("No running checkpoint")
    cli("pause",eid)
    deadline=time.monotonic()+30
    while time.monotonic()<deadline:
        status=cli("status",eid)
        if status["status"]=="paused":break
        time.sleep(.5)
    else:raise AssertionError("Pause did not finish")
    consumed=status["activeSeconds"]
    assert consumed>0
    print(json.dumps(dict(paused=True,activeSeconds=consumed)),flush=True)
    cli("resume",eid,"--no-search")
    deadline=time.monotonic()+1500
    last=None
    while time.monotonic()<deadline:
        status=cli("status",eid)
        progress=(status["status"],tuple(c["status"] for c in status["candidates"]))
        if progress!=last:print(json.dumps(dict(status=progress,activeSeconds=status["activeSeconds"])),flush=True);last=progress
        if status["status"] in ["completed","failed","budget-exhausted"]:break
        time.sleep(2)
    else:raise AssertionError("Smoke did not finish")
    assert status["status"]=="completed",status
    report=cli("report",eid)
    candidate=report["experiment"]["candidates"][0]
    assert candidate["fixtures"]["acceptable"],candidate
    assert candidate["screen"]["completePairs"]==2 and candidate["holdout"]["completePairs"]==2,candidate
    assert candidate["screen"]["regressions"]==candidate["holdout"]["regressions"]==0,candidate
    assert report["promotionEligible"]==[]
    try:cli("promote",eid,"--candidate",c["id"]);raise AssertionError("Diagnostic promotion accepted")
    except RuntimeError as error:assert "promotion gates" in str(error)
    evidence=cli("evidence",eid,"--limit","4")
    assert evidence["evidenceBytes"]<16000,evidence["evidenceBytes"]
    excerpt=cli("source",eid,"--term","optimizerEnabled")
    assert len(excerpt["excerpt"])<=12000
    if evidence["patterns"]:
        ref=evidence["patterns"][0]["examples"][0]
        decision=cli("detail",eid,"--replay",ref["replay"],"--sequence",str(ref["sequence"]))
        assert "state" not in decision and "decision" in decision
    with sqlite3.connect((ROOT/"data/simulator.sqlite").as_uri()+"?mode=ro",uri=True) as db:
        games=[json.loads(r[0]) for r in db.execute("SELECT payload FROM pilot_optimizer_evaluations WHERE experiment_id=?",(eid,))]
    games=[g for g in games if g.get("kind")=="game"]
    assert len(games)==8 and all(g["status"]=="completed" for g in games)
    for phase in ["screen","holdout"]:
        for pair in [0,1]:
            arms={g["arm"]:g for g in games if g["phase"]==phase and g["pair"]==pair}
            assert arms["baseline"]["checkpoints"]==arms["candidate"]["checkpoints"],(phase,pair)
    after=immutable()
    assert before==after,"Saved payloads or active bundle changed"
    result=dict(experimentId=eid,candidateId=c["id"],pausedAndResumed=True,fixtureCases=candidate["fixtures"]["cases"],
                completedGames=len(games),screenPairs=2,holdoutPairs=2,regressions=0,activeSeconds=status["activeSeconds"],
                originalPayloadsPreserved=True,activeBundlePreserved=True,evidenceBytes=evidence["evidenceBytes"],unauthenticatedMutationRejected=True,diagnosticPromotionRejected=True)
    (ROOT/"data/pilot-optimizations"/eid/"smoke-verification.json").write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(result),flush=True)

if __name__=="__main__":main()
