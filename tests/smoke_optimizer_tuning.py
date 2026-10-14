"""Exercise scoped tuning, code-fixture staging and JFR through the real service."""
import json
from pathlib import Path
import tempfile
import time
import uuid
from smoke_pilot_optimizer import cli, immutable, ROOT

def main():
    before=immutable()
    inventory=cli("inspect")
    target=next(d for d in inventory["decks"] if d["profile"]=="reanimator-terror-v10")
    opponent=next(d for d in inventory["decks"] if d["profile"]=="auras-v001")
    e=cli("create","--profile",target["profile"],"--deck",target["id"],"--opponent",opponent["id"],
          "--screen-pairs","2","--holdout-pairs","2","--budget-seconds","900","--key","tuning-smoke-"+uuid.uuid4().hex)
    eid=e["id"]
    key="Formidable Speaker|pilot.priority"
    assert key in e["registry"]
    value=e["registry"][key]["default"]+10
    fixture='''import forge.gui.GuiBase;
import forge.GuiDesktop;
import forge.model.FModel;
import java.io.*;
public final class OptimizerCandidateChecks {
    public static void main(String[] args) {
        System.setProperty("java.awt.headless","true");
        GuiBase.setInterface(new GuiDesktop());FModel.initialize(null,null);
        ForgeBridge.output=new PrintStream(new ByteArrayOutputStream());ForgeBridge.attemptId="optimizer-focused-fixture";
        String profile=ForgeBridge.optimizerFixturePolicy.get("profile").getAsString();
        String a=ReanimatorCopyManaChecks.choose(profile,"Wistfulness","UBCG",true,true,"Plains",false);
        String b=ReanimatorCopyManaChecks.choose(profile,"Wistfulness","UBCG",true,true,"Jeskai Revelation",false);
        if(!a.equals("Formidable Speaker")||!a.equals(b))throw new AssertionError("Legal tuned value choice and concealed-state invariance");
        if(!ReanimatorCopyManaChecks.choose(profile,"Wistfulness","UUBG",true,true,"Plains",false).equals("Wistfulness"))throw new AssertionError("Actual payment activates effect");
        if(!ReanimatorCopyManaChecks.choose(profile,"Wistfulness","UBCG",true,false,"Plains",false).equals("Wistfulness"))throw new AssertionError("Absent metadata retains fallback");
        System.out.println("Focused optimizer checks: four actual engine choices passed.");System.exit(0);
    }
}
'''
    with tempfile.TemporaryDirectory() as temp:
        folder=Path(temp)
        tuning=folder/"tuning.json";tuning.write_text(json.dumps({key:value}))
        source=folder/"ForgeBridge.java";source.write_text((ROOT/"engine/ForgeBridge.java").read_text()+"\n// Source-staging integration control; behavior remains unchanged.\n")
        cases=folder/"OptimizerCandidateChecks.java";cases.write_text(fixture)
        c=cli("stage",eid,"--source",str(source),"--fixtures",str(cases),"--tuning",str(tuning),"--label","Scoped tuning and focused-fixture integration")
    print(json.dumps(dict(experimentId=eid,candidateId=c["id"],tuning={key:value},codeRound=c["codeRound"])),flush=True)
    cli("start",eid,"--no-search","--screen-only")
    deadline=time.monotonic()+600
    last=None
    while time.monotonic()<deadline:
        status=cli("status",eid)
        progress=(status["status"],tuple(c["status"] for c in status["candidates"]))
        if progress!=last:print(json.dumps(dict(status=progress,activeSeconds=status["activeSeconds"])),flush=True);last=progress
        if status["status"] in ["awaiting-review","failed","budget-exhausted"]:break
        time.sleep(2)
    assert status["status"]=="awaiting-review",status
    current=status["candidates"][0]
    assert current["fixtures"]["acceptable"] and current["screen"]["completePairs"]==2,current
    assert any(g["name"]=="OptimizerCandidateChecks" and g["passed"] for g in current["fixtures"]["guards"])
    profile=cli("profile",eid,"--candidate",c["id"])
    assert profile["hotspots"] and (ROOT/profile["recording"]).exists(),profile
    after=immutable();assert before==after
    report=cli("report",eid)
    assert report["promotionEligible"]==[]
    assert report["experiment"]["candidates"][0]["screen"]["completePairs"]==2
    result=dict(experimentId=eid,candidateId=c["id"],focusedFixturePassed=True,scopedNumericTuning=True,
                screeningGames=4,profiledGame=1,profiledGamesExcluded=True,holdoutUntouched=True,
                codeRounds=status["codeRounds"],flightRecording=profile["recording"],hotspots=profile["hotspots"][:5],originalPayloadsAndActiveBundlePreserved=True)
    (ROOT/"data/pilot-optimizations"/eid/"tuning-smoke-verification.json").write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(result),flush=True)

if __name__=="__main__":main()
