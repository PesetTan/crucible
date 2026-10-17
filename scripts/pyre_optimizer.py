"""Pyre transport/content adapter for the existing bounded optimizer workflow."""
import copy
import gzip
import hashlib
import json
import math
import os
import random
from pathlib import Path
import shutil
import sqlite3
import subprocess
import uuid


def canonical(v):
    return json.dumps(v,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def graph_candidate(draft,tuning,revision):
    result=copy.deepcopy(draft)
    graph=result.get('modules')
    if not graph:
        raise ValueError('Independent Pilot Module graph required')
    target=result['pilots'][0]['id']
    artifact=next(a for a in graph['artifacts'] if a['manifest']['id']==target and a['manifest']['kind']=='pilot')
    policy=json.loads(bytes.fromhex(artifact['payload_hex']))
    defaults=dict(creature_weight=3.0,aura_weight=2.0,interaction_weight=1.0,mulligan_floor=5)
    if policy['base'] not in ('white-aggro-v1','boros-aggro-v1'):
        raise ValueError('Initial numeric optimizer supports the reviewed Aggro Module evaluators only')
    defaults.update(policy['config'] or {})
    for name,value in tuning.items():
        if name not in defaults or type(value) not in (int,float) or not math.isfinite(value):
            raise ValueError('Unknown/nonfinite Pyre tuning')
        if name=='mulligan_floor':
            if type(value) is not int or not 0<=value<=7:raise ValueError('Invalid mulligan floor')
        elif not 0<=value<=100:raise ValueError('Pyre weight outside 0..100')
        defaults[name]=value
    policy['config']=defaults
    payload=canonical(policy)
    artifact['manifest']['version']='1.0.'+str(revision)
    artifact['manifest']['payload']['sha256']=hashlib.sha256(payload).hexdigest()
    artifact['payload_hex']=payload.hex()
    artifact['sha256']=hashlib.sha256(canonical(artifact['manifest'])+payload).hexdigest()
    graph['sha256']='';graph['sha256']=hashlib.sha256(canonical(graph)).hexdigest()
    result['pilots'][0]['config']=None
    return result


def make_backend(base):
    encoded,identity,digest,save,now=base.encoded,base.identity,base.digest,base.save,base.now

    class PyreBackend(base.Backend):
        def inspect(self,request):
            installation=json.loads((self.root/'data/pyre-provider.json').read_text())
            return dict(providerId='pyre',cohorts=sorted(installation['cohorts']),numericPaths=['creature_weight','aura_weight','interaction_weight','mulligan_floor'],defaults=dict(budgetSeconds=600,maxCandidates=3,screenPairs=10,holdoutPairs=40),scope='Frozen typed Aggro Pilot Modules; no strategic improvement claim')

        def create(self,request):
            key=request.get('key','')
            if not isinstance(key,str) or not 1<=len(key)<=128:raise ValueError('Provide an idempotency key')
            inputs={k:v for k,v in request.items() if k not in ('key','catalog')};request_hash=identity(inputs)
            with self.db() as db:old=db.execute('SELECT id,request_hash FROM pilot_optimizer_experiments WHERE key=?',(key,)).fetchone()
            if old:
                if old[1]!=request_hash:raise ValueError('Idempotency key conflict')
                return self.status(old[0])
            installation=json.loads((self.root/'data/pyre-provider.json').read_text())
            if digest(Path(installation['executable']))!=installation['executableHash']:raise ValueError('Native artifact changed')
            runner=Path(installation.get('runner',''))
            if not runner.is_file() or digest(runner)!=installation.get('runnerHash'):raise ValueError('Register an exact ProviderRun assembly for optimization')
            cohort=request.get('pyreCohort');template=Path(installation['cohorts'].get(cohort,''))
            draft=json.loads(template.read_text());graph_candidate(draft,{},0)
            if request.get('sourceRunId'):raise ValueError('Pyre optimization requires a freshly frozen baseline; historical outcomes remain diagnostic')
            budget=request.get('budgetSeconds',600);screen=request.get('screenPairs',10);holdout=request.get('holdoutPairs',40);seed=request.get('rootSeed',42)
            if type(budget) is not int or not 1<=budget<=7200:raise ValueError('Invalid compute budget')
            if any(type(x) is not int or x<2 or x>1000 or x%2 for x in (screen,holdout)):raise ValueError('Balanced even pair counts required')
            if type(seed) is not int or not -(2**63)<=seed<2**63:raise ValueError('Invalid root seed')
            eid=uuid.uuid4().hex;folder=self.folder(eid);folder.mkdir()
            save(folder/'baseline-draft.json',draft)
            for source,name in [(installation['metrics'],'metrics.json'),(installation['controllerMetrics'],'controller-metrics.json')]:shutil.copyfile(source,folder/name)
            shutil.copyfile(base.__file__,folder/'backend.py');shutil.copyfile(__file__,folder/'pyre-backend.py')
            schedule=[];used=set()
            for phase,count in [('screen',screen),('holdout',holdout)]:
                for pair in range(count):
                    value=int.from_bytes(hashlib.sha256(canonical(['pyre-optimizer-v1',seed,phase,pair//2])).digest()[:8],'big',signed=True)
                    coordinates=(value,pair%2)
                    if coordinates in used:raise ValueError('Seed/start coordinate overlap')
                    used.add(coordinates);schedule.append(dict(phase=phase,pair=pair,deck='A',opponent='B',seed=value,starter=pair%2))
            runtime={p.name:digest(p) for p in runner.parent.iterdir() if p.is_file() and p.suffix in ('.dll','.json')}
            frozen=dict(providerId='pyre',profile=draft['pilots'][0]['id'],pyreCohort=cohort,installation=installation,
                runnerFiles=runtime,schedule=schedule,rootSeed=seed,scope='Provider workflow conformance with immutable numeric Pilot Modules',
                baselineTemplateHash=hashlib.sha256(canonical(draft)).hexdigest(),runtimeRatioLimit=2.0,settings=dict(turnLimit=150,timeoutSeconds=30),registry=dict(creature_weight=[0,100],aura_weight=[0,100],interaction_weight=[0,100],mulligan_floor=[0,7]))
            save(folder/'frozen.json',frozen)
            evidence={str(p.relative_to(folder)):digest(p) for p in folder.iterdir() if p.is_file()}
            e=dict(id=eid,providerId='pyre',createdAt=now(),updatedAt=now(),status='created',frozenHash=digest(folder/'frozen.json'),evidenceHashes=evidence,
                budgetSeconds=budget,activeSeconds=0.0,maxCodeRounds=0,codeRounds=0,maxCandidates=3,screenPairs=screen,holdoutPairs=holdout,
                diagnosticOnly=screen<10 or holdout<40,pauseRequested=False,error=None,scope=frozen['scope'],modelUsage=None)
            with self.db() as db:db.execute('INSERT INTO pilot_optimizer_experiments VALUES(?,?,?,?)',(eid,key,request_hash,encoded(e)))
            return self.status(eid)

        def status(self,eid):
            e=self.exp(eid);e['remainingSeconds']=max(0,e['budgetSeconds']-e['activeSeconds']);e['candidates']=self.candidates(eid);e['registry']=self.frozen(eid)['registry'];return e

        def verify_frozen(self,eid):
            e=self.exp(eid)
            for name,expected in e['evidenceHashes'].items():
                if digest(base.no_links(self.folder(eid)/name))!=expected:raise ValueError('Frozen Pyre evidence changed: '+name)
            return self.frozen(eid)

        def verify_runtime(self,eid):
            f=self.verify_frozen(eid);i=f['installation']
            if digest(Path(i['executable']))!=i['executableHash']:raise ValueError('Native executable changed')
            for name,expected in f['runnerFiles'].items():
                if digest(Path(i['runner']).parent/name)!=expected:raise ValueError('Frozen provider adapter changed')
            return f

        def stage(self,eid,request):
            e=self.exp(eid);f=self.verify_frozen(eid)
            if e.get('holdoutStarted') or e['status'] in ('queued','running','completed','promoted','promoting'):raise ValueError('Candidates are sealed once evaluation starts or holdout is exposed')
            if any(request.get(k) is not None for k in ('source','fixtureSource')):raise ValueError('Pyre code candidates require a separate versioned host review')
            tuning=request.get('tuning',{});current=self.candidates(eid)
            if not isinstance(tuning,dict):raise ValueError('Tuning object required')
            cid=identity([e['frozenHash'],tuning]);old=next((c for c in current if c['id']==cid),None)
            if old:return old
            if len(current)>=e['maxCandidates']:raise ValueError('Candidate budget exhausted')
            draft=json.loads((self.folder(eid)/'baseline-draft.json').read_text());candidate=graph_candidate(draft,tuning,len(current)+1)
            folder=self.folder(eid)/'candidates'/cid;folder.mkdir(parents=True);save(folder/'draft.json',candidate)
            c=dict(id=cid,label=str(request.get('label','numeric candidate'))[:120],kind='parameters',tuning=tuning,tuningHash=identity(tuning),sourceHash=digest(folder/'draft.json'),status='staged',createdAt=now(),codeRound=0)
            with self.db() as db:db.execute('INSERT INTO pilot_optimizer_candidates VALUES(?,?,?)',(cid,eid,encoded(c)))
            return c

        def sweep(self,eid):
            if not self.candidates(eid):
                self.stage(eid,dict(label='unchanged policy control',tuning={}))
                self.stage(eid,dict(label='higher creature priority',tuning={'creature_weight':4.0}))
                self.stage(eid,dict(label='higher Aura priority',tuning={'aura_weight':3.0}))

        def compile(self,eid,candidate):
            self.verify_runtime(eid);path=self.folder(eid)/'candidates'/candidate['id']/'draft.json'
            if digest(path)!=candidate['sourceHash']:raise ValueError('Immutable candidate content changed')
            return candidate

        def fixture_run(self,eid,candidate=None):
            f=self.verify_runtime(eid);folder=self.folder(eid);path=folder/'baseline-draft.json' if candidate is None else folder/'candidates'/candidate['id']/'draft.json'
            name='baseline' if candidate is None else candidate['id'];out=folder/('preflight-'+name+'.json');log=folder/('preflight-'+name+'.log')
            rc,_=self.run_process(eid,[f['installation']['executable'],'preflight','--manifest',str(path)],folder,out,log,15)
            if rc:raise ValueError('Native typed candidate preflight failed')
            resolved=json.loads(out.read_text());original=json.loads((folder/'baseline-draft.json').read_text());draft=json.loads(path.read_text())
            same=all(draft[k]==original[k] for k in draft if k not in ('modules','pilots')) and draft['pilots'][1]==original['pilots'][1]
            base_artifacts={a['manifest']['id']:a for a in original['modules']['artifacts']};changed=[a['manifest']['id'] for a in draft['modules']['artifacts'] if a!=base_artifacts[a['manifest']['id']]]
            isolated=set(changed)<=set([original['pilots'][0]['id']]);passed=same and isolated and resolved['artifact_sha256']==f['installation']['executableHash']
            return dict(cases=[dict(profile=f['profile'],dimension='typed immutable content',id='candidate-boundary',status='passed' if passed else 'failed',actual=passed)],guards=[dict(name='native preflight and unchanged opponent/Card/Mechanic/deck inputs',passed=passed)])

        def play(self,eid,candidate,slot,arm,bundle_override=None,phase_override=None,profile=False):
            if profile:raise ValueError('Native profiling uses separate engine instrumentation, not JVM JFR')
            self.verify_runtime(eid);f=self.frozen(eid);phase=phase_override or slot['phase'];evaluation_id=identity([eid,candidate['id'],phase,slot['pair'],arm])
            with self.db() as db:old=db.execute('SELECT payload FROM pilot_optimizer_evaluations WHERE id=?',(evaluation_id,)).fetchone()
            if old:
                g=json.loads(old[0])
                if g['status']!='completed' or digest(self.root/g['evidence'])!=g['evidenceHash']:raise ValueError('Retained evaluation failed or changed')
                return g
            folder=self.folder(eid)/'candidates'/candidate['id']/phase/(str(slot['pair']//2)+'-'+arm);(folder/'docs/specs').mkdir(parents=True,exist_ok=True);(folder/'data').mkdir(exist_ok=True)
            template=Path(bundle_override) if bundle_override else self.folder(eid)/'baseline-draft.json' if arm=='baseline' else self.folder(eid)/'candidates'/candidate['id']/'draft.json'
            installation=dict(f['installation'],metrics=str(self.folder(eid)/'metrics.json'),controllerMetrics=str(self.folder(eid)/'controller-metrics.json'),cohorts={'frozen':str(template)})
            save(folder/'data/pyre-provider.json',installation)
            request=dict(deckIds=[],providerId='pyre',pyreCohort='frozen',gamesPerPair=2,workers=1,staggerStarts=False,timeoutSeconds=f['settings']['timeoutSeconds'],turnLimit=f['settings']['turnLimit'],rootSeed=slot['seed'],capture='summary')
            # A balanced two-slot run is one paired block. Both starts stay together in the partition.
            save(folder/'request.json',dict(key=identity([eid,phase,slot['pair']//2]),request=request))
            out=folder/'result.json';log=folder/'stderr.log';game=dict(kind='game',id=evaluation_id,phase=phase,pair=slot['pair'],arm=arm,deck='A',opponent='B',seed=slot['seed'],starter=slot['starter'],startedAt=now(),providerId='pyre',tuningHash=candidate['tuningHash'] if arm=='candidate' else identity({}))
            try:
                prior=next((g for g in self.games(eid,candidate['id'],phase) if g['arm']==arm and g['seed']==slot['seed'] and g.get('status')=='completed'),None)
                if prior:
                    if digest(out)!=prior['evidenceHash']:raise ValueError('Balanced block evidence changed')
                    elapsed=prior['elapsedSeconds'];rc=0
                else:
                    rc,elapsed=self.run_process(eid,['dotnet',f['installation']['runner'],'--root',str(folder),'--input',str(folder/'request.json')],folder,out,log,60)
                if rc:raise ValueError('Coordinator did not complete the paired block')
                result=json.loads(out.read_text());rows=[s for s in result['slots'] if (s['starter']==s['deckA'])==(slot['starter']==0)]
                if len(rows)!=1 or result['valid']!=2:raise ValueError('Incomplete native coordinator block')
                row=rows[0]['result'];game.update(status='completed',winner='A' if row['winner']==rows[0]['deckA'] else 'B' if row['winner']==rows[0]['deckB'] else None,draw=row['draw'],engineMs=row['durationMs'],elapsedSeconds=elapsed,finishedAt=now(),evidence=str(out.relative_to(self.root)),evidenceHash=digest(out),replay=None)
                # Raw delivery is retained by the actual Store, including the other balanced start.
            except base.Interrupted:
                save(folder/'interrupted.json',dict(game,status='interrupted'));raise
            except (OSError,ValueError,TimeoutError) as error:game.update(status='failed',error=str(error),finishedAt=now())
            with self.db() as db:db.execute('INSERT INTO pilot_optimizer_evaluations VALUES(?,?,?,?)',(evaluation_id,eid,candidate['id'],encoded(game)))
            if game['status']!='completed':raise ValueError(game['error'])
            return game

        def evaluate_games(self,eid,candidate,phase):
            f=self.frozen(eid)
            for slot in [s for s in f['schedule'] if s['phase']==phase]:
                order=['baseline','candidate'] if (slot['pair']//2)%2==0 else ['candidate','baseline']
                for arm in order:self.play(eid,candidate,slot,arm)
            games=self.games(eid,candidate['id'],phase)
            result=base.compare_pairs(games,self.exp(eid)[phase+'Pairs'])
            by_pair={}
            for game in games:by_pair.setdefault(game['pair'],{})[game['arm']]=game
            blocks={}
            for pair,arms in by_pair.items():
                if set(arms)=={'baseline','candidate'} and all(base.outcome(g) is not None for g in arms.values()):
                    blocks.setdefault(pair//2,[]).append((base.outcome(arms['candidate'])-base.outcome(arms['baseline']))/2)
            values=[sum(v)/2 for v in blocks.values() if len(v)==2]
            if values:
                rng=random.Random(identity([f['rootSeed'],phase,'paired-block-bootstrap-v1']))
                draws=sorted(sum(rng.choice(values) for _ in values)/len(values) for _ in range(10000))
                result['pairedWinPointDifference']=sum(values)/len(values)
                result['pairedBlockBootstrap95']=[draws[249],draws[9749]]
                result['independentSeedBlocks']=len(values)
            result['runtimeGuard']=result['medianElapsedRatio'] is not None and result['medianElapsedRatio']<=self.frozen(eid)['runtimeRatioLimit'];result['acceptable'] &= result['runtimeGuard']
            result['uncertainty']='Paired finite-sample guard; balanced seed blocks stay together. No population improvement claim.'
            return result

        def report(self,eid):
            r=super().report(eid);r.update(providerId='pyre',scope=self.frozen(eid)['scope'],productionActivation='Explicit authorization required; this adapter never changes live provider registrations')
            return r

        def evidence(self,eid,limit=8):
            if not 1<=limit<=32:raise ValueError('Evidence limit 1..32')
            return dict(providerId='pyre',games=[g for c in self.candidates(eid) for g in self.games(eid,c['id'])][:limit])

        def audit_games(self,eid,cid):
            self.verify_runtime(eid)
            for game in self.games(eid,cid):
                if game['status']!='completed' or digest(self.root/game['evidence'])!=game['evidenceHash']:raise ValueError('Evaluation audit failed')
                result=json.loads((self.root/game['evidence']).read_text())
                if result['status']!='completed' or result['valid']!=2:raise ValueError('Incomplete result evidence')
            return True

        def promote(self,eid,cid):
            # The phase exercises pointer mechanics only in explicitly marked disposable roots.
            if not (self.root/'.pyre-optimizer-sandbox').is_file():raise ValueError('Production Pyre activation requires a separately authorized registry operation')
            e=self.exp(eid);c=self.candidate(eid,cid)
            with self.db() as db:prior=db.execute('SELECT payload FROM pilot_optimizer_published WHERE id=?',(cid,)).fetchone()
            if prior:
                policy=json.loads(prior[0]);published=self.root/'data/pyre-optimizer-publications'/(cid+'.json')
                if digest(published)!=policy['sourceHash']:raise ValueError('Published immutable content changed')
                return policy
            if e['status']!='completed' or e['diagnosticOnly'] or c['status']!='eligible':raise ValueError('Candidate lacks complete promotion gates')
            self.audit_games(eid,cid);folder=self.root/'data/pyre-optimizer-publications';folder.mkdir(exist_ok=True)
            pointer=folder/'active.json';previous=json.loads(pointer.read_text()) if pointer.exists() else None
            candidate=self.folder(eid)/'candidates'/cid/'draft.json';published=folder/(cid+'.json')
            if published.exists() and digest(published)!=c['sourceHash']:raise ValueError('Immutable publication conflict')
            if not published.exists():shutil.copyfile(candidate,published)
            try:
                for slot in self.frozen(eid)['schedule'][:2]:self.play(eid,c,slot,'candidate',published,'publication-smoke')
                f=self.frozen(eid);graph=json.loads(published.read_text())['modules']['sha256']
                policy=dict(version=1,id=cid,providerId='pyre',cohort=f['pyreCohort'],engineHash=f['installation']['executableHash'],sourceHash=digest(published),baselineTemplateHash=f['baselineTemplateHash'],graphHash=graph,experimentId=eid)
                with self.db() as db:db.execute('INSERT INTO pilot_optimizer_published VALUES(?,?)',(cid,encoded(policy)))
                save(pointer,dict(candidateId=cid,sha256=digest(published),previous=previous));self.update(eid,status='promoted',publishedPolicyId=cid)
                return policy
            except BaseException:
                with self.db() as db:db.execute('DELETE FROM pilot_optimizer_published WHERE id=?',(cid,))
                if previous is None:pointer.unlink(missing_ok=True)
                else:save(pointer,previous)
                raise

        def dispatch(self,action,request):
            if action=='rollback':
                if not (self.root/'.pyre-optimizer-sandbox').is_file():raise ValueError('Isolated rollback root required')
                pointer=self.root/'data/pyre-optimizer-publications/active.json';current=json.loads(pointer.read_text());previous=current.get('previous')
                if previous is None:pointer.unlink()
                else:save(pointer,previous)
                return dict(restored=previous)
            if action in ('detail','source','profile'):raise ValueError('Native evidence is available through raw records; this Forge-specific view is unsupported')
            return super().dispatch(action,request)

    return PyreBackend
