#!/usr/bin/env python3
"""Drive already-frozen profile experiments through the service's serial execution gate.

A single reviewed source revision is instantiated for each exact profile. Total subprocess
computation across all instances is capped, including rejected candidates. Never publishes.
"""
import argparse, importlib.util, json, time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('optimizer_client',ROOT/'scripts/pilot-optimizer.py')
client=importlib.util.module_from_spec(spec);spec.loader.exec_module(client)

def save(path,value):
    tmp=path.with_suffix('.tmp');tmp.write_text(json.dumps(value,indent=2)+'\n');tmp.replace(path)

def refresh_clocks(statuses, listed, read):
    listed={entry['id']:entry for entry in listed}
    for eid,state in statuses.items():
        fresh=listed.get(eid)
        if fresh is None:
            statuses[eid]=read(eid)
        else:
            state['activeSeconds']=fresh['activeSeconds'];state['status']=fresh['status']
    return sum(state['activeSeconds'] for state in statuses.values())

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report',type=Path,default=ROOT/'data/reports/engagement-protocols-all-pilots')
    parser.add_argument('--service',default='http://127.0.0.1:5079')
    parser.add_argument('--stage',action='store_true')
    parser.add_argument('--holdout',action='store_true')
    parser.add_argument('--finalist',action='store_true',help='Run screening then untouched holdout for an already frozen finalist')
    parser.add_argument('--only',action='append',default=[],help='Evaluate only these experiment IDs; meter all experiments in the batch')
    parser.add_argument('--screen-limit-seconds',type=int,default=3500,help='Reserve shared computation for untouched holdout; stop between screening jobs')
    # 400 seconds reserved for standalone preflight/build/fixture subprocesses outside the service meter.
    parser.add_argument('--service-budget-seconds',type=int,default=5000)
    args=parser.parse_args()
    if not 1<=args.service_budget_seconds<=5000:raise ValueError('Do not extend the user-revised shared 5400-second computation budget')
    out=args.report.resolve();rows=json.loads((out/'experiments.json').read_text())
    def call(method,path,value=None):return client.client(args.service,method,'/api/v1/pilot-optimizations'+path,value)
    statuses={row['id']:call('GET','/'+row['id']) for row in rows}
    def meter():
        # A previously queued job may finish after a scheduler restart/reorder.
        # Refresh every batch member's clock rather than trusting cached idle states.
        return refresh_clocks(statuses,call('GET',''),lambda eid:call('GET','/'+eid))
    save(out/'budget.json',dict(totalSeconds=5400,serviceLimit=args.service_budget_seconds,preflightReserveSeconds=400,maxDistinctCodeRevisions=3,maxDistinctSourceCandidates=12))
    if args.stage:
        source=(out/'candidate-ForgeBridge.java').read_text();fixture=(out/'OptimizerCandidateChecks.java').read_text()
        for row in rows:
            result=call('POST','/'+row['id']+'/candidates',dict(label='Engagement Protocols v2 / shared revision 1',source=source,fixtureSource=fixture,tuning={}))
            row['candidateId']=result['id']
        save(out/'experiments.json',rows)
    for row in rows:
        eid=row['id'];state=call('GET','/'+eid);statuses[eid]=state
        if args.only and eid not in args.only:continue
        if row.get('purpose')=='catalog-aware-finalist' and not (args.finalist or args.holdout):continue
        if state['status'] in ('completed','failed','budget-exhausted'):continue
        if args.holdout and not args.finalist:
            if state['status']!='awaiting-review' or not any(c['status']=='screened' for c in state['candidates']):continue
        elif not (args.holdout or args.finalist) and state['status']=='awaiting-review' and not any(c['status']=='staged' for c in state['candidates']):continue
        total=meter()
        finish=args.holdout or args.finalist
        phase_limit=args.service_budget_seconds if finish else min(args.screen_limit_seconds,args.service_budget_seconds)
        pause_at=max(0,args.service_budget_seconds-10) # leave a margin for polling and service cancellation
        if total>=min(phase_limit,pause_at):print('Phase computation allowance reached; remaining evidence stays unproven',flush=True);break
        if state['status'] not in ('queued','running'):
            call('POST','/'+eid+'/resume',dict(search=False,screenOnly=not finish))
        while True:
            state=call('GET','/'+eid);statuses[eid]=state
            total=meter()
            save(out/'progress.json',dict(phase='finalist' if args.finalist else 'holdout' if args.holdout else 'screen',activeSeconds=total,profiles=[dict(profile=r['profile'],**{k:statuses[r['id']].get(k) for k in ['id','status','activeSeconds','error','candidates']}) for r in rows]))
            if total>=pause_at and state['status'] in ('queued','running'):
                call('POST','/'+eid+'/pause',{})
            if state['status'] not in ('queued','running','pausing'):
                c=state['candidates'][-1] if state['candidates'] else {}
                phase=c.get('holdout' if finish else 'screen')
                compact={k:phase.get(k) for k in ['completePairs','requiredPairs','baselineWins','candidateWins','gains','regressions','technicalFailures','medianElapsedRatio']} if phase else None
                print(row['profile'],state['status'],c.get('status'),json.dumps(compact),f'total={total:.1f}s',flush=True)
                save(out/(row['profile']+'-'+eid[:8]+'-report.json'),call('GET','/'+eid+'/report'))
                break
            time.sleep(5)
        if total>=args.service_budget_seconds:break
    print('Batch phase finished. No activation or promotion performed.',flush=True)
if __name__=='__main__':main()
