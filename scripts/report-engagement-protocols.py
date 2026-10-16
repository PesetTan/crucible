#!/usr/bin/env python3
"""Build a per-profile evidence report; optionally re-audit raw optimizer games. Never promotes."""
import argparse, collections, datetime, gzip, hashlib, json, sqlite3, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from pilot_optimizer import Backend, class_hashes, digest, compare_pairs, catalog_publication_supported, validate_bundle

def save(path,value):
    path.write_text(json.dumps(value,indent=2,ensure_ascii=False)+'\n')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report',type=Path,default=ROOT/'data/reports/engagement-protocols-all-pilots')
    parser.add_argument('--audit',action='store_true')
    args=parser.parse_args();out=args.report.resolve();backend=Backend(ROOT)
    entries=json.loads((out/'experiments.json').read_text());records=[];allgames=[];totals=collections.Counter();execution=[]
    budget=json.loads((out/'budget.json').read_text())
    catalog=None;source_hash=digest(out/'candidate-ForgeBridge.java');validated_bundles=set();source_variants=set()
    for row in entries:
        eid=row['id'];experiment=backend.exp(eid);frozen=backend.frozen(eid);candidate=backend.candidate(eid,row['candidateId'])
        source_variants.update(c['sourceHash'] for c in backend.candidates(eid))
        assert candidate['sourceHash']==source_hash
        assert not ({s['seed'] for s in frozen['schedule'] if s['phase']=='screen'} & {s['seed'] for s in frozen['schedule'] if s['phase']=='holdout'})
        games=[g for g in backend.games(eid,candidate['id']) if g['phase'] in ('screen','holdout') and not g.get('profiled')];allgames.extend(games)
        audit_errors=[]
        if args.audit:
            if frozen['bundleId'] not in validated_bundles:
                validate_bundle(ROOT,frozen['bundleId']);validated_bundles.add(frozen['bundleId'])
            try:backend.audit_games(eid,candidate['id'])
            except ValueError as error:
                if not any(g['status']=='failed' for g in games):raise
                audit_errors.append(str(error))
            if candidate.get('classHashes'):assert class_hashes(backend.folder(eid)/'candidates'/candidate['id']/'classes')==candidate['classHashes']
        if candidate.get('protocolCatalog'):
            path=backend.folder(eid)/'candidates'/candidate['id']/'protocol-catalog.json'
            assert digest(path)==candidate['protocolCatalogHash']
            assert json.loads(path.read_text())==candidate['protocolCatalog']
            if catalog is None:catalog=candidate['protocolCatalog'];(out/'candidate-catalog.json').write_bytes(path.read_bytes())
            else:assert catalog==candidate['protocolCatalog']
        pairs={};protocols=collections.Counter();decisions=collections.Counter();exercised=0;casts=0;cast_failures=[]
        for game in games:
            pairs.setdefault((game['phase'],game['pair']),{})[game['arm']]=game
            if game.get('startedAt') and game.get('finishedAt'):execution.append((game['startedAt'],game['finishedAt'],game['id']))
            if game['status']!='completed':continue
            if args.audit:
                request=json.loads((ROOT/game['request']).read_text())
                slot=next(s for s in frozen['schedule'] if s['phase']==game['phase'] and s['pair']==game['pair'])
                assert request['seed']==slot['seed']==game['seed'] and request['starter']==slot['starter']==game['starter']
                assert game['deck']==slot['deck'] and game['opponent']==slot['opponent'] and request['pilotA']==frozen['profile']
                assert request['turnLimit']==frozen['settings']['turnLimit'] and request['performanceMode']==frozen['settings']['performanceMode']
                for side,did in [('A',slot['deck']),('B',slot['opponent'])]:
                    expected=[dict(name=c['name'].split(' // ')[0],quantity=c['quantity'],attributes=c.get('attributes') or {}) for c in frozen['decks'][did]['cards'] if c['zone']=='MainDeck']
                    assert request['deck'+side]==expected
                if game['arm']=='candidate':assert request['optimizerPolicyA']==dict(version=1,id=candidate['id'],profile=frozen['profile'],tuning=candidate['tuning'])
                else:assert 'optimizerPolicyA' not in request
                actual_adapter=hashlib.sha256('\n'.join(game['classHashes'].values()).encode()).hexdigest()
                assert actual_adapter==(candidate['adapterHash'] if game['arm']=='candidate' else frozen['adapterHash'])
                assert 'optimizerPolicyB' not in request and request['pilotB']==frozen['profiles'][game['opponent']]
                with gzip.open(ROOT/game['replay'],'rt') as stream:events=[json.loads(line) for line in stream]
                target=[r for r in events if r.get('player')=='A' and r.get('kind')=='pilot_decision']
                if game['arm']=='candidate':
                    adapters=('subtype-development-on-pass','protocol-burn-on-pass')
                    exercised+=any(r.get('activeProtocols') and r.get('decision') in adapters for r in target)
                    for decision in target:
                        decisions[decision['decision']]+=1
                        if decision['decision'] in adapters:protocols.update(decision.get('activeProtocols',[]))
                    for index,event in enumerate(events):
                        if event.get('player')!='A' or event.get('decision') not in ('subtype-development-on-pass','protocol-burn-on-pass'):continue
                        ident=event.get('selectedId',event.get('sourceId'))
                        follow=events[index+1:index+65]
                        transition=next((r for r in follow if r.get('event')=='GameEventCardChangeZone' and f'({ident}) : [Hand] -> [Stack]' in r.get('detail','')),None)
                        if transition:casts+=1
                        else:cast_failures.append(dict(replay=game['replay'],sequence=event['sequence'],selectedId=ident))
                assert not any(r.get('optimizerPolicyId') for r in events if r.get('player')=='B')
        changed=0
        for arms in pairs.values():
            if set(arms)=={'baseline','candidate'} and all(g['status']=='completed' for g in arms.values()):
                changed+=arms['baseline']['checkpoints']!=arms['candidate']['checkpoints']
                if args.audit:
                    requests=[json.loads((ROOT/arms[arm]['request']).read_text()) for arm in ['baseline','candidate']]
                    for request in requests:
                        for key in ('attemptId','pilotVersion','optimizerPolicyA'):request.pop(key,None)
                    assert requests[0]==requests[1]
        fixtures=candidate.get('fixtures') or {}
        record=dict(profile=row['profile'],experiment=eid,candidate=candidate['id'],experimentStatus=experiment['status'],candidateStatus=candidate['status'],
                    purpose=row.get('purpose','profile-comparison'),rootSeed=frozen['rootSeed'],
                    sourceHash=source_hash,adapterHash=candidate.get('adapterHash'),frozen=f'data/pilot-optimizations/{eid}/frozen.json',
                    targetDecks=[dict(id=did,name=frozen['decks'][did]['name']) for did in frozen['targets']],opponents=[dict(id=did,name=frozen['decks'][did]['name'],profile=frozen['profiles'][did]) for did in frozen['opponents']],
                    screen=compare_pairs([g for g in games if g['phase']=='screen'],10),holdout=compare_pairs([g for g in games if g['phase']=='holdout'],40),
                    fixtures=dict(acceptable=fixtures.get('acceptable'),cases=fixtures.get('cases'),baselineFailures=fixtures.get('baselineFailures'),regressions=fixtures.get('regressions'),legacyChanges=fixtures.get('legacyChanges')),
                    activeSeconds=experiment['activeSeconds'],changedStatePairs=changed,exercisedCandidateGames=exercised if args.audit else None,
                    protocolActivations=dict(protocols),decisionCounts=dict(decisions),auditedCasts=casts,castAuditFailures=cast_failures,
                    completeExecutions=sum(g['status']=='completed' for g in games),technicalFailures=sum(g['status']=='failed' for g in games))
        record['auditErrors']=audit_errors
        record['interruptedAttempts']=[str(p.relative_to(ROOT)) for p in (backend.folder(eid)/'candidates'/candidate['id']).rglob('*.interrupted.json')]
        record['playedTargetIds']=sorted({g['deck'] for g in games})
        record['playedOpponentIds']=sorted({g['opponent'] for g in games})
        for phase in ('screen','holdout'):
            matched=[arms for (name,_),arms in pairs.items() if name==phase and set(arms)=={'baseline','candidate'} and all(g['status']=='completed' for g in arms.values())]
            record[phase+'Outcomes']={arm:dict(wins=sum(not p[arm]['draw'] and p[arm]['winner']=='A' for p in matched),draws=sum(p[arm]['draw'] for p in matched),losses=sum(not p[arm]['draw'] and p[arm]['winner']=='B' for p in matched)) for arm in ('baseline','candidate')}
        baseline_catalog_path=ROOT/'data/engine-bundles'/frozen['bundleId']/'protocol-catalog.json'
        record['catalogPublicationSupported']=catalog_publication_supported(experiment,candidate,json.loads(baseline_catalog_path.read_text()) if baseline_catalog_path.exists() else None)
        assert not cast_failures, str(cast_failures)
        records.append(record);totals.update(dict(completeExecutions=record['completeExecutions'],technicalFailures=record['technicalFailures'],changedStatePairs=changed,auditedCasts=casts))
    if catalog:
        for record in records:
            declared=catalog['sets'][record['profile']]
            record['parent']=declared['parent']
            record['declaredProtocolIds']=[d['id'] for d in declared['definitions']]
    # Revision 1 is retained separately and consumes the same shared computation meter.
    original=json.loads((out/'original-runs.json').read_text())
    with backend.db() as db:
        current={ident:hashlib.sha256(payload.encode()).hexdigest() for ident,payload in db.execute('SELECT id,payload FROM runs')}
    run_changes=[ident for ident,expected in original.items() if current.get(ident)!=expected]
    assert not run_changes, 'Historical run payloads changed: '+str(run_changes)
    execution.sort();overlap=[(a[2],b[2]) for a,b in zip(execution,execution[1:]) if a[1]>b[0]]
    assert not overlap, 'Comparative executions overlapped'
    active=json.loads((ROOT/'data/engine-bundles/active.json').read_text())['id']
    baseline={backend.frozen(row['id'])['bundleId'] for row in entries}
    summary=dict(generatedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),audit=args.audit,profiles=records,
                 sourceHash=source_hash,distinctCodeRevisions=len(source_variants),distinctSourceCandidates=len(source_variants),sourceVariants=sorted(source_variants),sharedOptimizerSeconds=sum(r['activeSeconds'] for r in records),
                 totalComputationLimitSeconds=budget['totalSeconds'],serviceComputationLimitSeconds=budget['serviceLimit'],preflightReserveSeconds=budget['preflightReserveSeconds'],
                 budgetRevision=budget.get('revision'),
                 publicationBlocker='Earlier frozen comparison backends predate catalog-aware publication. Their stale-catalog promotion path is guarded. The separately recorded catalog-aware finalist has the corrected publisher; all evidence gates and explicit activation authorization still apply.',
                 catalogAwareFinalists=[r['experiment'] for r in records if r['purpose']=='catalog-aware-finalist'],
                 inheritedReference=dict(profile='forge-default',role='Declared no-override reference and inherited behavior; no separate all-Forge paired-game arm'),
                 profileCount=len({r['profile'] for r in records}),experimentCount=len(records),
                 finalRevisionTotals=dict(totals),originalRunCount=len(original),originalRunsUnchanged=True,serialComparisons=not overlap,
                 activeBundle=active,baselineBundles=sorted(baseline),activeBundleUnchanged=baseline=={active},
                 scope='Finite local Forge evidence. No Arena forecasts, automatic activation, or full inherited Forge information audit.')
    save(out/'coverage.json',summary)
    lines=['# Engagement Protocols: Non-Reanimator Evaluation','',
           'Status: '+('raw evidence audited' if args.audit else 'evaluation snapshot')+'. Candidates remain opt-in and inactive.','',
           f"Recorded optimizer subprocess computation: {summary['sharedOptimizerSeconds']:.1f} seconds ({summary['sharedOptimizerSeconds']/60:.1f} minutes). Revised total allowance: {budget['totalSeconds']/60:.0f} minutes, including a {budget['preflightReserveSeconds']}-second preflight reserve. Two distinct source candidates/revisions; no promotion.",'',
           budget.get('revision',''),'',
           '| Profile | Frozen target versions | Fixture gate | Screen pairs | Holdout pairs | Screen W baseline/candidate | Gain/regression | Changed state pairs | Status |',
           '|---|---:|---|---:|---:|---:|---:|---:|---|']
    for r in records:
        s,h=r['screen'],r['holdout'];fixtures='Passed' if r['fixtures']['acceptable'] else 'Pending' if r['fixtures']['acceptable'] is None else 'Rejected'
        status='Technical failure' if r['technicalFailures'] else r['candidateStatus']
        label=r['profile']+(' (catalog-aware finalist)' if r['purpose']=='catalog-aware-finalist' else '')
        lines.append(f"| `{label}` | {len(r['targetDecks'])} | {fixtures} | {s['completePairs']}/10 | {h['completePairs']}/40 | {s['baselineWins']}/{s['candidateWins']} | {s['gains']}/{s['regressions']} | {r['changedStatePairs']} | {status} |")
    lines+=['','A screened profile has no holdout acceptance. Incomplete pairs and technical failures never count as losses or evidence of success. All target IDs, opponent policies, W/D/L terminal evidence, runtime ratios, changes and paths are in [coverage.json](coverage.json).',
            '', 'The fixture gate checks new regressions and legacy changes. The 335-case inventory retains eight known v9 copy/payment expectation failures and 42 aggression observations; it is not 335 passed assertions. See `known-baseline-diagnostics.json`.',
            '', 'Frozen target/opponent pools can exceed actual sampled coverage. `playedTargetIds`, `playedOpponentIds`, matched W/L/D and per-matchup rows in `coverage.json` identify exactly what ran.',
            '',summary['publicationBlocker'],
            '',f"Final-revision complete executions: {totals['completeExecutions']}; technical failures: {totals['technicalFailures']}. Original saved run payloads preserved: {len(original)}. Active bundle unchanged: {summary['activeBundleUnchanged']}. Serial comparison execution verified: {not overlap}.",
            '',f"Candidate source SHA-256: `{source_hash}`.",'','Artifacts: `experiments.json`, per-profile `*-report.json`, `revision1-review.json`, `saved-diagnosis.json`, `candidate-catalog.json`, and each experiment\'s `frozen.json`, candidate fixtures, requests and replays.']
    (out/'REPORT.md').write_text('\n'.join(lines)+'\n')
    print(json.dumps({k:v for k,v in summary.items() if k not in ('profiles',)},indent=2))
if __name__=='__main__':main()
