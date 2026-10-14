import gzip
import importlib.util
import json
from pathlib import Path
import shutil
import sqlite3
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("pilot_optimizer", ROOT / "scripts/pilot_optimizer.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class QualityChecks(unittest.TestCase):
    def test_cpu_and_allocation_samples_are_not_combined(self):
        stack=dict(stackTrace=dict(frames=[dict(method=dict(type=dict(name="Example"),name="choose"))]))
        report=m.jfr_hotspots([dict(type="jdk.ExecutionSample",values=stack),*[dict(type="jdk.ObjectAllocationSample",values=stack) for _ in range(5)]])
        self.assertEqual(report["jdk.ExecutionSample"],[("Example.choose",1)])
        self.assertEqual(report["jdk.ObjectAllocationSample"],[("Example.choose",5)])

    def game(self, pair, arm, winner="A", draw=False, **changes):
        return dict(dict(pair=pair, arm=arm, status="completed", winner=winner, draw=draw, deck="target", opponent="opponent", elapsedSeconds=2 if arm == "baseline" else 1, engineMs=100), **changes)

    def test_faster_worse_candidate_is_rejected(self):
        report = m.compare_pairs([self.game(0,"baseline"), self.game(0,"candidate",winner="B")], 1)
        self.assertEqual(report["regressions"],1)
        self.assertEqual(report["medianElapsedRatio"],.5)
        self.assertFalse(report["acceptable"])

    def test_draw_to_loss_is_a_regression(self):
        report=m.compare_pairs([self.game(0,"baseline",draw=True,winner=None),self.game(0,"candidate",winner="B")],1)
        self.assertEqual(report["regressions"],1)

    def test_failure_is_not_a_loss_or_a_complete_pair(self):
        report=m.compare_pairs([self.game(0,"baseline"),self.game(0,"candidate",status="failed",winner=None)],1)
        self.assertEqual((report["completePairs"],report["technicalFailures"],report["regressions"]),(0,1,0))
        self.assertFalse(report["acceptable"])

    def test_incomplete_holdout_cannot_pass(self):
        report=m.compare_pairs([self.game(0,"baseline"),self.game(0,"candidate")],40)
        self.assertFalse(report["complete"])
        self.assertFalse(report["acceptable"])

    def test_quality_gains_keep_matchup_provenance(self):
        report=m.compare_pairs([self.game(0,"baseline",winner="B"),self.game(0,"candidate")],1)
        self.assertTrue(report["acceptable"])
        self.assertEqual(report["gains"],1)
        self.assertEqual(report["matchups"][0]["opponent"],"opponent")

class ReplayChecks(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.path=Path(self.temp.name)/"replay.jsonl"
        self.request=dict(attemptId="attempt",starter=0)
        state=dict(players=[dict(name="Unicode — creature")])
        raw=json.dumps(state,ensure_ascii=False,separators=(",",":"))
        self.rows=[dict(kind="game_started",starter=0),dict(kind="opening_hand",player="A"),dict(kind="opening_hand",player="B"),
                   dict(kind="snapshot",state=state,stateHash=m.hashlib.sha256(raw.encode()).hexdigest()),
                   dict(kind="game_finished",winner="A",draw=False,reason="AllOpponentsLost",detail="DurationMs=123",actualTurn=8)]
        self.addCleanup(self.temp.cleanup)

    def write(self):
        self.path.write_text("\n".join(json.dumps(dict(r,sequence=i,attemptId="attempt"),ensure_ascii=False) for i,r in enumerate(self.rows,1)))

    def test_complete_replay_checks_state_and_terminal(self):
        self.write(); result=m.verify_replay(self.path,self.request)
        self.assertEqual((result["winner"],result["engineMs"],result["actualTurns"]),("A",123,8))

    def test_corrupt_state_is_rejected(self):
        self.rows[3]["stateHash"]="corrupt";self.write()
        with self.assertRaisesRegex(ValueError,"checksum"):m.verify_replay(self.path,self.request)

    def test_duplicate_terminal_is_rejected(self):
        self.rows.append(self.rows[-1]);self.write()
        with self.assertRaisesRegex(ValueError,"terminal"):m.verify_replay(self.path,self.request)

    def test_wrong_start_is_rejected(self):
        self.rows[0]["starter"]=1;self.write()
        with self.assertRaisesRegex(ValueError,"terminal"):m.verify_replay(self.path,self.request)

    def test_typed_failure_is_rejected(self):
        self.rows.append(dict(kind="attempt_failed",reason="turn safeguard"));self.write()
        with self.assertRaisesRegex(ValueError,"Typed"):m.verify_replay(self.path,self.request)

    def test_truncation_is_not_an_outcome(self):
        self.rows.pop();self.write()
        with self.assertRaisesRegex(ValueError,"terminal"):m.verify_replay(self.path,self.request)

    def test_jfr_banner_is_separated_but_unknown_output_is_not_hidden(self):
        self.write(); original=self.path.read_text()
        self.path.write_text("[0.123s][info][jfr,startup] Started recording 1.\n"+original)
        m.separate_jfr_startup(self.path,self.path.with_suffix(".log"))
        self.assertEqual(self.path.read_text(),original)
        self.assertTrue(self.path.with_suffix(".jvm-output").exists())
        self.path.write_text("unexpected output\n"+original)
        m.separate_jfr_startup(self.path,self.path.with_suffix(".log"))
        with self.assertRaises(ValueError):m.verify_replay(self.path,self.request)

class PersistenceChecks(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name).resolve()
        self.addCleanup(self.temp.cleanup)
        (self.root/"data").mkdir()
        (self.root/"engine").mkdir()
        shutil.copyfile(ROOT/"engine/ForgeBridge.java",self.root/"engine/ForgeBridge.java")
        (self.root/"tests").mkdir()
        for name in m.FIXTURES:shutil.copyfile(ROOT/"tests"/(name+".java"),self.root/"tests"/(name+".java"))
        folder=self.root/"data/reports/subtype-pilots-v001";folder.mkdir(parents=True)
        shutil.copyfile(ROOT/"data/reports/subtype-pilots-v001/registry.json",folder/"registry.json")
        bundle=self.root/"data/engine-bundles/staging";(bundle/"bridge").mkdir(parents=True)
        (bundle/"bridge/ForgeBridge.class").write_bytes(b"fixture class identity")
        (bundle/"forge.jar").write_bytes(b"fixture engine identity")
        ah=m.adapter_hash(bundle/"bridge");eh=m.engine_hash(bundle,"forge.jar")
        self.bundle_id=eh+"-"+ah
        m.save(bundle/"bundle.json",dict(engineHash=eh,adapterHash=ah,jarName="forge.jar"))
        bundle.rename(bundle.parent/self.bundle_id)
        m.save(bundle.parent/"active.json",dict(id=self.bundle_id))
        self.backend=m.Backend(self.root)
        with self.backend.db() as db:
            db.executescript("CREATE TABLE decks(id TEXT,payload TEXT); CREATE TABLE runs(id TEXT,payload TEXT); CREATE TABLE slots(id TEXT,run_id TEXT,payload TEXT);")
            db.execute("INSERT INTO decks VALUES('original','immutable deck payload')")
        cards=[dict(name="Example",quantity=60,zone="MainDeck",attributes={"pilot":{"priority":40},"roles":["reviewed"]})]
        self.catalog=[dict(profile=p,coverage="exploratory",manifest=dict(id=d,name=d,strategy="Control",subtype=d,cards=cards)) for d,p in [("target","control-shared-v1"),("opponent","aggro-v001")]]
        self.request=dict(profile="control-shared-v1",catalog=self.catalog,key="unique",screenPairs=2,holdoutPairs=2)
        self.eid=self.backend.create(self.request)["id"]

    def test_original_payloads_are_preserved(self):
        self.backend.stage(self.eid,{})
        with self.backend.db() as db:self.assertEqual(db.execute("SELECT payload FROM decks").fetchone()[0],"immutable deck payload")

    def test_creation_is_idempotent_and_conflicts_are_rejected(self):
        self.assertEqual(self.backend.create(self.request)["id"],self.eid)
        with self.assertRaisesRegex(ValueError,"different request"):self.backend.create(dict(self.request,budgetSeconds=20))

    def test_separate_balanced_seed_schedules_are_frozen(self):
        frozen=self.backend.frozen(self.eid);schedule=frozen["schedule"]
        self.assertEqual(len({s["seed"] for s in schedule}),4)
        for phase in ["screen","holdout"]:self.assertEqual([s["starter"] for s in schedule if s["phase"]==phase],[0,1])

    def test_unregistered_and_fractional_tuning_are_rejected(self):
        for tuning in [{"Example|roles":1},{"Example|pilot.priority":1.5},{"Example|pilot.priority":True},{"Example|pilot.priority":1001}]:
            with self.assertRaises(ValueError):self.backend.stage(self.eid,dict(tuning=tuning))

    def test_candidates_have_distinct_identity_without_changing_base(self):
        before=m.digest(self.backend.folder(self.eid)/"frozen.json")
        a=self.backend.stage(self.eid,{})
        b=self.backend.stage(self.eid,dict(tuning={"Example|pilot.priority":50}))
        self.assertNotEqual(a["id"],b["id"])
        self.assertEqual(before,m.digest(self.backend.folder(self.eid)/"frozen.json"))

    def test_three_distinct_code_revision_rounds_are_enforced(self):
        base=(self.backend.folder(self.eid)/"bridge-template.java").read_text()
        fixture="public final class OptimizerCandidateChecks { public static void main(String[] args) {} }"
        for n in range(3):self.backend.stage(self.eid,dict(source=base+f"\n// revision {n}\n",fixtureSource=fixture))
        self.assertEqual(self.backend.exp(self.eid)["codeRounds"],3)
        with self.assertRaisesRegex(ValueError,"Three code"):self.backend.stage(self.eid,dict(source=base+"\n// fourth revision\n",fixtureSource=fixture))

    def test_code_revisions_require_focused_fixtures(self):
        base=(self.backend.folder(self.eid)/"bridge-template.java").read_text()
        with self.assertRaisesRegex(ValueError,"focused"):self.backend.stage(self.eid,dict(source=base+"\n// revision\n"))

    def test_focused_fixture_tampering_is_rejected(self):
        base=(self.backend.folder(self.eid)/"bridge-template.java").read_text()
        fixture="public final class OptimizerCandidateChecks { public static void main(String[] args) {} }"
        c=self.backend.stage(self.eid,dict(source=base+"\n// revision\n",fixtureSource=fixture))
        (self.backend.folder(self.eid)/"candidates"/c["id"]/"OptimizerCandidateChecks.java").write_text("tampered")
        with self.assertRaisesRegex(ValueError,"fixture checksum"):self.backend.compile(self.eid,c)

    def test_twelve_candidate_cap_and_reserved_revision_capacity(self):
        for n in range(12):self.backend.stage(self.eid,dict(tuning={"Example|pilot.priority":n}))
        with self.assertRaisesRegex(ValueError,"Candidate limit"):self.backend.stage(self.eid,dict(tuning={"Example|pilot.priority":100}))

    def test_staged_source_tampering_blocks_compilation(self):
        c=self.backend.stage(self.eid,{})
        (self.backend.folder(self.eid)/"candidates"/c["id"]/"ForgeBridge.java").write_text("tampered")
        with self.assertRaisesRegex(ValueError,"checksum"):self.backend.compile(self.eid,c)

    def test_fixture_regression_and_legacy_changes_are_rejected(self):
        case=dict(profile="opponent",dimension="combat",id="lethal",status="passed",actual="attack")
        baseline=dict(cases=[case],guards=[])
        changed=dict(cases=[dict(case,status="failed",actual="hold")],guards=[])
        result=self.backend.fixture_comparison(baseline,changed,"target")
        self.assertFalse(result["acceptable"])
        self.assertEqual(len(result["legacyChanges"]),1)

    def test_recovery_preserves_consumption_and_requires_explicit_resume(self):
        self.backend.update(self.eid,status="running",activeSeconds=9.5)
        self.backend.recover()
        e=self.backend.exp(self.eid)
        self.assertEqual((e["status"],e["activeSeconds"]),("interrupted",9.5))
        self.assertTrue(e["pauseRequested"])

    def test_local_process_budget_is_enforced_and_checkpointed(self):
        self.backend.update(self.eid,budgetSeconds=1)
        folder=self.backend.folder(self.eid)
        with self.assertRaises(m.Interrupted):
            self.backend.run_process(self.eid,[sys.executable,"-c","import time; time.sleep(10)"],folder,folder/"out",folder/"err",20)
        self.assertGreaterEqual(self.backend.exp(self.eid)["activeSeconds"],1)
        self.assertIsNone(self.backend.exp(self.eid)["checkpoint"])
        with self.assertRaisesRegex(ValueError,"exhausted"):self.backend.prepare(self.eid,{})

    def test_pause_interrupts_work_without_using_queue_budget(self):
        self.backend.pause(self.eid)
        self.assertEqual(self.backend.exp(self.eid)["activeSeconds"],0)
        folder=self.backend.folder(self.eid)
        with self.assertRaises(m.Interrupted):self.backend.run_process(self.eid,[sys.executable,"-c","pass"],folder,folder/"out",folder/"err",20)

    def test_small_diagnostic_cannot_be_promoted(self):
        c=self.backend.stage(self.eid,{})
        c.update(status="eligible",fixtures=dict(acceptable=True));self.backend.put_candidate(self.eid,c)
        self.backend.update(self.eid,status="completed")
        with self.assertRaisesRegex(ValueError,"promotion gates"):self.backend.promote(self.eid,c["id"])
        self.assertEqual(json.loads((self.root/"data/engine-bundles/active.json").read_text())["id"],self.bundle_id)

    def test_evidence_and_source_excerpts_are_bounded(self):
        packet=self.backend.evidence(self.eid,8)
        self.assertLess(len(m.encoded(packet)),16000)
        source=self.backend.source(self.eid,dict(term="optimizerEnabled"))
        self.assertLessEqual(len(source["excerpt"]),12000)
        with self.assertRaises(ValueError):self.backend.evidence(self.eid,100)

    def test_symlink_artifacts_are_rejected(self):
        c=self.backend.stage(self.eid,{})
        path=self.backend.folder(self.eid)/"candidates"/c["id"]/"ForgeBridge.java"
        path.unlink();path.symlink_to(self.root/"engine/ForgeBridge.java")
        with self.assertRaisesRegex(ValueError,"Symlinks"):self.backend.compile(self.eid,c)

    def test_blocked_engine_coverage_is_rejected_before_execution(self):
        catalog=json.loads(m.encoded(self.catalog));catalog[1]["coverage"]="blocked"
        with self.assertRaisesRegex(ValueError,"engine cards"):
            self.backend.create(dict(self.request,key="blocked",catalog=catalog,deckIds=["target"],opponentIds=["opponent"]))

    def test_raw_evidence_tampering_blocks_audit(self):
        c=self.backend.stage(self.eid,{})
        folder=self.backend.folder(self.eid)
        request=folder/"request.json";m.save(request,dict(attemptId="example",starter=0))
        replay=folder/"replay.jsonl.gz"
        with gzip.open(replay,"wb") as stream:stream.write(b"incomplete")
        game=dict(kind="game",phase="holdout",status="completed",replay=str(replay.relative_to(self.root)),replayHash=m.digest(replay),
                  request=str(request.relative_to(self.root)),requestHash=m.digest(request))
        with self.backend.db() as db:db.execute("INSERT INTO pilot_optimizer_evaluations VALUES(?,?,?,?)",("audit",self.eid,c["id"],m.encoded(game)))
        replay.write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError,"checksum"):self.backend.audit_games(self.eid,c["id"])

    def test_raw_fixture_positions_tampering_blocks_cached_acceptance(self):
        folder=self.backend.folder(self.eid)/"baseline-fixtures";folder.mkdir()
        classes=folder/"classes";classes.mkdir()
        (classes/"Probe.class").write_bytes(b"fixture bytecode")
        positions=folder/"positions.json";m.save(positions,dict(cases=[]))
        result=folder/"result.json"
        m.save(result,dict(cases=[],guards=[],positionsHash=m.digest(positions),classHashes={"Probe.class":m.digest(classes/"Probe.class")}))
        with self.backend.db() as db:
            db.execute("INSERT INTO pilot_optimizer_evaluations VALUES(?,?,?,?)",(m.identity([self.eid,"fixtures","baseline"]),self.eid,"baseline",m.encoded(dict(kind="fixtures",resultHash=m.digest(result)))))
        positions.write_text('{"cases":["changed"]}')
        with self.assertRaisesRegex(ValueError,"positions checksum"):self.backend.fixture_run(self.eid)

    def promotion_fixture(self):
        c=self.backend.stage(self.eid,{})
        classes=self.backend.folder(self.eid)/"candidates"/c["id"]/"classes";classes.mkdir()
        (classes/"ForgeBridge.class").write_bytes(b"new immutable compiled bridge identity")
        c.update(status="eligible",fixtures=dict(acceptable=True),classHashes=m.class_hashes(classes),adapterHash=m.adapter_hash(classes))
        self.backend.put_candidate(self.eid,c)
        self.backend.update(self.eid,status="completed",diagnosticOnly=False,screenPairs=10,holdoutPairs=40)
        games=[dict(kind="game",phase=phase,pair=i,arm=arm,status="completed",winner="A",draw=False,deck="target",opponent="opponent",elapsedSeconds=2,engineMs=100) for phase,count in [("screen",10),("holdout",40)] for i in range(count) for arm in ["baseline","candidate"]]
        return c,games

    def promotion_checks(self):
        return dict(cases=[dict(profile="control-shared-v1",dimension="resources",id="value",status="passed",actual="selected")],guards=[dict(passed=True)])

    def test_failed_publication_smoke_preserves_active_pointer_and_no_policy(self):
        c,games=self.promotion_fixture()
        with patch.object(self.backend,"games",side_effect=lambda eid,cid,phase=None:[g for g in games if phase is None or g["phase"]==phase]),patch.object(self.backend,"audit_games"),patch.object(self.backend,"fixture_run",return_value=self.promotion_checks()),patch.object(self.backend,"play",side_effect=ValueError("smoke failed")):
            with self.assertRaisesRegex(ValueError,"smoke failed"):self.backend.promote(self.eid,c["id"])
        self.assertEqual(json.loads((self.root/"data/engine-bundles/active.json").read_text())["id"],self.bundle_id)
        with self.backend.db() as db:self.assertEqual(db.execute("SELECT count(*) FROM pilot_optimizer_published").fetchone()[0],0)

    def test_isolated_publication_records_opt_in_and_is_idempotent(self):
        c,games=self.promotion_fixture()
        with patch.object(self.backend,"games",side_effect=lambda eid,cid,phase=None:[g for g in games if phase is None or g["phase"]==phase]),patch.object(self.backend,"audit_games"),patch.object(self.backend,"fixture_run",return_value=self.promotion_checks()),patch.object(self.backend,"play",return_value={}) as smoke:
            published=self.backend.promote(self.eid,c["id"])
            self.assertEqual(smoke.call_count,2)
            self.assertEqual(published["id"],c["id"])
            self.assertEqual(published["tuningHash"],c["tuningHash"])
            self.assertEqual(self.backend.promote(self.eid,c["id"]),published)
            self.assertEqual(smoke.call_count,2)
        self.assertNotEqual(json.loads((self.root/"data/engine-bundles/active.json").read_text())["id"],self.bundle_id)

if __name__=="__main__":unittest.main()
