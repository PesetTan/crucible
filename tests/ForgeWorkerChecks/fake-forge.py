#!/usr/bin/env python3
# Controlled transport faults; real Forge equivalence is tested separately.
import json,os,pathlib,sys,time
worker='--worker' in sys.argv
for line in sys.stdin:
 r=json.loads(line);seq=0;seed=r['seed']
 with pathlib.Path('requests.log').open('a') as f:f.write(json.dumps(dict(pid=os.getpid(),attempt=r['attemptId'],worker=worker))+'\n')
 def emit(kind,**kw):
  global seq
  seq+=1
  print(json.dumps(dict(kind=kind,sequence=seq,actualTurn=0,attemptId=r['attemptId'],**kw)),flush=True)
 if seed in [5,6]:time.sleep(5)
 if seed==7:sys.exit(3)
 if seed==2:
  emit('attempt_failed',reason='Controlled engine failure');sys.exit(3)
 if seed==9:time.sleep(.1)
 emit('game_started',starter=r['starter'])
 for p in ['A','B']:emit('opening_hand',player=p,mulligans=0,keptHandSize=7,policyAccepted=True)
 emit('game_finished',winner='A',draw=False,reason='AllOpponentsLost',detail='DurationMs=1')
 if seed==3:emit('game_finished',winner='A',draw=False,reason='Duplicate')
 if worker:
  print(json.dumps(dict(kind='worker_complete',attemptId='wrong' if seed==4 else r['attemptId'],protocolVersion=1,heapUsedBytes=1000000000 if seed==8 else 100)),flush=True)
 else:sys.exit(0)
