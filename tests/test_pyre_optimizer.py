import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
import pilot_optimizer as base
from pyre_optimizer import make_backend,graph_candidate

class PyreOptimizerChecks(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix='pyre-optimizer-guards-');self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name);(self.root/'data').mkdir()
        self.executable=self.root/'native-fixture';self.executable.write_text('not executable; unit boundary fixture only')
        self.runner=self.root/'ProviderRun.dll';self.runner.write_bytes(b'fixture')
        self.core=self.root/'Crucible.Core.dll';self.core.write_bytes(b'fixture-core')
        self.metrics=self.root/'metrics.json';self.metrics.write_text('{}')
        def artifact(identifier,kind,policy):
            b=json.dumps(policy).encode();m=dict(id=identifier,kind=kind,version='1.0.0',payload=dict(sha256=hashlib.sha256(b).hexdigest()))
            return dict(manifest=m,payload_hex=b.hex(),sha256=hashlib.sha256(b).hexdigest())
        self.draft=dict(pilots=[dict(id='unit.aggro',config=None),dict(id='unit.opponent',config=None)],modules=dict(artifacts=[artifact('unit.aggro','pilot',dict(base='white-aggro-v1',config={})),artifact('unit.opponent','pilot',dict(base='boros-aggro-v1',config={}))],sha256='fixture'),decks=[[],[]])
        template=self.root/'template.json';template.write_text(json.dumps(self.draft))
        installation=dict(executable=str(self.executable),executableHash=base.digest(self.executable),runner=str(self.runner),runnerHash=base.digest(self.runner),metrics=str(self.metrics),controllerMetrics=str(self.metrics),cohorts={'unit':str(template)})
        (self.root/'data/pyre-provider.json').write_text(json.dumps(installation))
        self.backend=make_backend(base)(self.root)
        self.experiment=self.backend.create(dict(key='unit',providerId='pyre',pyreCohort='unit'))['id']

    def test_partitions_keep_seed_blocks_and_starters_together(self):
        schedule=self.backend.frozen(self.experiment)['schedule'];parts={}
        for row in schedule:parts.setdefault(row['phase'],set()).add((row['seed'],row['starter']))
        self.assertFalse(parts['screen']&parts['holdout'])
        for phase in parts:
            seeds={s for s,_ in parts[phase]}
            self.assertTrue(all({(s,0),(s,1)}<=parts[phase] for s in seeds))

    def test_candidate_changes_only_target_pilot_artifact(self):
        changed=graph_candidate(self.draft,dict(creature_weight=4),1)
        self.assertEqual(changed['modules']['artifacts'][1],self.draft['modules']['artifacts'][1])
        self.assertEqual(changed['decks'],self.draft['decks'])
        self.assertNotEqual(changed['modules']['artifacts'][0]['sha256'],self.draft['modules']['artifacts'][0]['sha256'])
        for tuning in [dict(creature_weight=float('nan')),dict(mulligan_floor=1.5),dict(opponent_hand=1),dict(aura_weight=101)]:
            with self.assertRaises(ValueError):graph_candidate(self.draft,tuning,1)

    def test_bounds_and_holdout_seal(self):
        for value in [1,2,3]:self.backend.stage(self.experiment,dict(tuning=dict(creature_weight=value)))
        with self.assertRaises(ValueError):self.backend.stage(self.experiment,dict(tuning=dict(creature_weight=4)))
        self.backend.update(self.experiment,holdoutStarted=True,status='paused')
        with self.assertRaises(ValueError):self.backend.stage(self.experiment,dict(tuning={}))

    def test_source_and_runner_dependencies_are_immutable(self):
        self.backend.verify_runtime(self.experiment)
        self.core.write_bytes(b'changed')
        with self.assertRaises(ValueError):self.backend.verify_runtime(self.experiment)
        (self.backend.folder(self.experiment)/'baseline-draft.json').write_text('{}')
        with self.assertRaises(ValueError):self.backend.verify_frozen(self.experiment)

    def test_compute_budget_and_production_activation_guards(self):
        self.backend.stage(self.experiment,dict(tuning={}))
        self.backend.update(self.experiment,activeSeconds=600)
        with self.assertRaises(ValueError):self.backend.prepare(self.experiment,dict(search=False))
        with self.assertRaisesRegex(ValueError,'Production Pyre activation'):self.backend.promote(self.experiment,'anything')
        self.assertFalse((self.root/'data/pyre-optimizer-publications').exists())

if __name__=='__main__':unittest.main()
