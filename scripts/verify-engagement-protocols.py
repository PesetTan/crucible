#!/usr/bin/env python3
"""Freeze and run the 48 paired, serial Engagement Protocol diagnostic schedules."""
import argparse, collections, datetime, gzip, hashlib, json, queue, sqlite3, subprocess, threading, os
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=Path(os.environ.get('CRUCIBLE_PROTOCOL_REPORT',str(ROOT/'data/reports/engagement-protocols-v1-release'))).resolve()
JAVA=ROOT/'vendor/jdk/Contents/Home/bin/java'
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def save(path,value):path.write_text(json.dumps(value,indent=2,ensure_ascii=False)+'\n')
def stamp():return datetime.datetime.now(datetime.timezone.utc).isoformat()
VARIANTS={'terror':'024a9a563f94c3768fdc877f8b3e804f5cf92848e405c6f19adec1bca126ba36','sultai':'ff98c3e7b42a44a8fff3900324cd91b637136556f1c91b0f350f540d7dfe6437','cryomancer':'d77268c9e42e53a638625bc24ab4b743a3540825f6a2cd83a6f2834ee9b6a966'}
OPPONENTS={'counters':('ae3091f889292407daddbc7b80d198da6a0cbd2450dc7182b820620c5c6b527d','control-shared-v1'),'aggression':('41dfff7b088fe771d3abcad49072315c55552c640789db96ce64d974694f5112','aggro-v001'),'combo':('d8dacc3149352d6d2aaa75b73ea945c6266d5c2ee7a7ac39b2ef196696d6db6f','forge-default'),'graveyard':('637c4e4afb7c22ded1aa3d1cd001b228c02e83efe430a2b0bcbb439e8dcfde24','control-shared-v1')}
def freeze():
    OUT.mkdir(parents=True,exist_ok=True)
    target=OUT/'frozen.json'
    if target.exists():raise ValueError('Inputs already frozen; preserve the experiment.')
    bundle=json.loads((ROOT/'data/engine-bundles/active.json').read_text())['id']
    db=sqlite3.connect(f'file:{ROOT}/data/simulator.sqlite?mode=ro',uri=True)
    catalog=sqlite3.connect(f'file:{ROOT.parents[1]}/standard_cards.sqlite?mode=ro',uri=True)
    decks={}
    for ident in list(VARIANTS.values())+[x[0] for x in OPPONENTS.values()]:
        payload,strategy,subtype=db.execute('select payload,strategy,subtype from decks where id=?',(ident,)).fetchone()
        deck=json.loads(payload);cards=[]
        for card in deck['cards']:
            if card['zone']!='MainDeck':continue
            found=catalog.execute('select attributes from cards where arena_id=?',(card['arenaId'],)).fetchone()
            cards.append(dict(name=card['name'].split(' // ')[0],quantity=card['quantity'],attributes=json.loads(found[0]) if found else card.get('attributes') or {}))
        assert sum(x['quantity'] for x in cards)==60
        decks[ident]=dict(id=ident,name=deck['name'],strategy=strategy,subtype=subtype,cards=cards,manifestHash=deck['manifestHash'],payloadHash=hashlib.sha256(payload.encode()).hexdigest())
    schedules=[]
    for variant,own in VARIANTS.items():
        for scenario,(opponent,profile) in OPPONENTS.items():
            for starter in (0,1):
                for seedindex in (0,1):
                    key=f'{variant}:{scenario}:{starter}:{seedindex}'
                    seed=int.from_bytes(hashlib.sha256(('engagement-v1:'+key).encode()).digest()[:8],'big',signed=True)
                    schedules.append(dict(key=key,variant=variant,scenario=scenario,own=own,opponent=opponent,opponentProfile=profile,starter=starter,seed=seed))
    baseline=ROOT/'data/engine-bundles'/bundle
    save(target,dict(createdAt=stamp(),bundle=bundle,decks=decks,schedules=schedules,turnLimit=150,performanceMode=False,baselineClasses={p.name:digest(p) for p in sorted((baseline/'bridge').glob('*.class'))},candidateClasses={p.name:digest(p) for p in sorted((OUT/'bridge').glob('*.class'))},sourceHash=digest(ROOT/'engine/ForgeBridge.java'),originalRuns={i:hashlib.sha256(p.encode()).hexdigest() for i,p in db.execute('select id,payload from runs')}))
    (OUT/'ForgeBridge.java').write_bytes((ROOT/'engine/ForgeBridge.java').read_bytes())
    print('Frozen 48 schedules, exact manifests, annotations, seeds, starts and bridge classes.',flush=True)
class Worker:
    def __init__(self,bundle,bridge,arm):
        self.stderr=(OUT/f'{arm}.stderr.log').open('a');self.queue=queue.Queue()
        jar=next(bundle.glob('*.jar'))
        self.proc=subprocess.Popen([str(JAVA),'-Xmx1200m','-cp',str(bridge)+':'+str(jar),'ForgeBridge','--worker'],cwd=bundle,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.stderr,text=True,bufsize=1)
        def read():
            for line in self.proc.stdout:self.queue.put(line)
            self.queue.put(None)
        threading.Thread(target=read,daemon=True).start()
    def game(self,request):
        self.proc.stdin.write(json.dumps(request,separators=(',',':'))+'\n');self.proc.stdin.flush();lines=[]
        while True:
            line=self.queue.get(timeout=180)
            if line is None:raise RuntimeError(f'Worker exited {self.proc.poll()}')
            event=json.loads(line)
            if event['kind']=='worker_complete':return lines
            lines.append(line)
    def close(self):
        if self.proc.poll() is None:self.proc.stdin.close();self.proc.wait(timeout=30)
        self.stderr.close()
def run():
    frozen=json.loads((OUT/'frozen.json').read_text());bundle=ROOT/'data/engine-bundles'/frozen['bundle']
    for arm,bridge,profile in [('baseline',bundle/'bridge','reanimator-v011'),('candidate',OUT/'bridge','reanimator-v012')]:
        hashes={p.name:digest(p) for p in sorted(bridge.glob('*.class'))}
        assert hashes==frozen[arm+'Classes'],'Frozen classes changed'
        worker=Worker(bundle,bridge,arm)
        try:
            for index,schedule in enumerate(frozen['schedules']):
                prefix=OUT/f'{arm}-{index:02d}'
                if prefix.with_suffix('.summary.json').exists():continue
                own=frozen['decks'][schedule['own']];opponent=frozen['decks'][schedule['opponent']]
                request=dict(attemptId=f'engagement-{arm}-{index:02d}',seed=schedule['seed'],starter=schedule['starter'],turnLimit=frozen['turnLimit'],performanceMode=frozen['performanceMode'],pilotVersion=profile,pilotA=profile,pilotB=schedule['opponentProfile'],deckA=own['cards'],deckB=opponent['cards'])
                save(prefix.with_suffix('.request.json'),request);started=stamp();lines=worker.game(request)
                with gzip.open(prefix.with_suffix('.jsonl.gz'),'wt') as stream:stream.writelines(lines)
                events=[json.loads(line) for line in lines];terminal=[e for e in events if e['kind']=='game_finished']
                assert len(terminal)==1 and not any(e['kind']=='attempt_failed' for e in events)
                assert next(e for e in events if e['kind']=='game_started')['starter']==request['starter']
                for number,event in enumerate(events,1):
                    assert event['sequence']==number and event['attemptId']==request['attemptId']
                    if event.get('state') is not None:assert hashlib.sha256(json.dumps(event['state'],separators=(',',':'),ensure_ascii=False).encode()).hexdigest()==event['stateHash']
                states=collections.Counter(x for e in events if e['kind']=='protocol_state' for x in e['activeProtocols'])
                assert {p.name:digest(p) for p in sorted(bridge.glob('*.class'))}==frozen[arm+'Classes'],'Classes changed during execution'
                summary=dict(index=index,key=schedule['key'],scenario=schedule['scenario'],variant=schedule['variant'],startedAt=started,finishedAt=stamp(),winner=terminal[0]['winner'],actualTurns=terminal[0]['actualTurn'],replayHash=digest(prefix.with_suffix('.jsonl.gz')),checkpoints=sum(e.get('state') is not None for e in events),protocolTransitions=states,protocolDecisions=sum(e['kind']=='pilot_decision' and bool(e.get('activeProtocols')) for e in events))
                save(prefix.with_suffix('.summary.json'),summary)
                print(arm,index+1,'/48',schedule['key'],summary['winner'],flush=True)
        finally:worker.close()
    audit(frozen)
def audit(frozen):
    pairs=[]
    for index,schedule in enumerate(frozen['schedules']):
        a=json.loads((OUT/f'baseline-{index:02d}.summary.json').read_text());b=json.loads((OUT/f'candidate-{index:02d}.summary.json').read_text())
        pairs.append(dict(key=schedule['key'],baseline=a['winner'],candidate=b['winner'],gained=a['winner']!='A' and b['winner']=='A',regressed=a['winner']=='A' and b['winner']!='A'))
    db=sqlite3.connect(f'file:{ROOT}/data/simulator.sqlite?mode=ro',uri=True)
    assert all(hashlib.sha256(db.execute('select payload from runs where id=?',(i,)).fetchone()[0].encode()).hexdigest()==h for i,h in frozen['originalRuns'].items())
    report=dict(completedAt=stamp(),pairs=48,executions=96,baselineWins=sum(p['baseline']=='A' for p in pairs),candidateWins=sum(p['candidate']=='A' for p in pairs),gains=sum(p['gained'] for p in pairs),regressions=sum(p['regressed'] for p in pairs),unchanged=sum(p['baseline']==p['candidate'] for p in pairs),comparisons=pairs,originalRunsUnchanged=len(frozen['originalRuns']))
    save(OUT/'comparison.json',report);print(json.dumps({k:v for k,v in report.items() if k!='comparisons'}),flush=True)
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--freeze',action='store_true');args=parser.parse_args()
    freeze() if args.freeze else run()
