#!/usr/bin/env python3
"""Exercise a native backend with clean installed assets and isolated state on macOS/Linux.
Reuse a local engine only as read-only setup inputs; never select or modify its active bundle.
"""
import argparse, hashlib, json, os, shutil, socket, subprocess, tempfile, time, urllib.request, urllib.error
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
def port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
def wait(check,timeout=60):
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        try:
            result=check()
            if result:return result
        except (OSError,ValueError):pass
        time.sleep(.1)
    raise AssertionError('Check timed out')
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--runtime',type=Path,required=True);p.add_argument('--engine-source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    runtime=args.runtime.resolve();engine=args.engine_source.resolve();output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    exe=runtime/('CrucibleBackend.exe' if os.name=='nt' else 'CrucibleBackend')
    with tempfile.TemporaryDirectory(prefix='Crucible installed Ω ') as temporary:
        base=Path(temporary);assets=base/'assets';root=base/'writable state';root.mkdir()
        shutil.copytree(ROOT/'resources/starter',assets/'starter');shutil.copytree(ROOT/'engine',assets/'engine',ignore=shutil.ignore_patterns('build'))
        (assets/'docs').mkdir();shutil.copy2(ROOT/'docs/archetype-tiers-2026-10-11.json',assets/'docs')
        vendor=root/'vendor';vendor.mkdir()
        shutil.copy2(engine/'vendor/forge-gui-desktop-2.0.15-jar-with-dependencies.jar',vendor)
        (vendor/'res').symlink_to(engine/'vendor/res',target_is_directory=True)
        (vendor/'jdk').symlink_to(engine/'vendor/jdk',target_is_directory=True)
        env=dict(os.environ,MTG_PROJECT_ROOT=str(root),MTG_ASSET_ROOT=str(assets),MTG_PORT=str(port()),MTG_HOST_MODE='service',CRUCIBLE_HOST_OWNER='isolated-owner-'+('b'*48))
        for key in ['MTG_JAVA','MTG_CARD_DB','CRUCIBLE_LAUNCH_EVENT']:env.pop(key,None)
        with (output/'setup.log').open('wb') as log:
            subprocess.run([str(exe),'--setup'],env=env,cwd=runtime,check=True,stdout=log,stderr=subprocess.STDOUT,timeout=240)
        origin='http://127.0.0.1:'+env['MTG_PORT']
        def call(path,method='GET',body=None,owner=None):
            headers={'Content-Type':'application/json'}
            if method!='GET':
                headers.update({'X-Instance-Token':call('/api/v1/session')[1]['instanceToken'],'Idempotency-Key':os.urandom(16).hex()})
                if owner:headers['X-Crucible-Owner']=owner
            request=urllib.request.Request(origin+path,data=json.dumps(body).encode() if body is not None else None,method=method,headers=headers)
            try:
                with urllib.request.urlopen(request,timeout=10) as r:
                    raw=r.read();return r.status,json.loads(raw) if raw else None
            except urllib.error.HTTPError as e:
                raw=e.read()
                return e.code,json.loads(raw) if raw else None
        checks=[];child=None
        log=(output/'backend.log').open('wb')
        def start():
            process=subprocess.Popen([str(exe)],cwd=runtime,env=env,stdout=log,stderr=subprocess.STDOUT)
            wait(lambda:call('/api/v1/health')[0]==200)
            assert call('/api/v1/health')[1]['runtimeMode']=='native-aot'
            return process
        try:
            child=start();checks.append('native installed startup and setup without Python')
            decks=call('/api/v1/decks')[1];assert decks and all(d['manifest']['sourceType']=='public' for d in decks)
            decks=sorted((d for d in decks if d['coverage']['status']!='blocked'),key=lambda d:(0 if 'Mono-Red Burn' in d['manifest']['name'] else 1 if 'Rakdos Aggro' in d['manifest']['name'] else 2,d['manifest']['id']))
            assert len(decks)>=2
            request=dict(deckIds=[d['manifest']['id'] for d in decks[:2]],gamesPerPair=2,workers=1,timeoutSeconds=180,turnLimit=150,rootSeed='9223372036854775807',diagnostic=True,staggerStarts=False,pilotMode='default-v1')
            # Use the actual configured default instead of guessing its identity.
            from re import search
            source=(ROOT/'src/DeckTester/ReanimatorPilots.cs').read_text()
            request['pilotMode']=search(r'const string CurrentAggroMode\s*=\s*"([^"]+)"',source).group(1)
            status,created=call('/api/v1/runs','POST',request);assert status==200,(status,created)
            run=created['id'];wait(lambda:call('/api/v1/runs/'+run)[1]['status'] in ('completed','failed'),900)
            saved=call('/api/v1/runs/'+run)[1];assert saved['status']=='completed',saved
            assert saved['config']['rootSeed']==9223372036854775807
            games=call('/api/v1/runs/'+run+'/games')[1];assert len(games)==2 and all(g['status']=='completed' for g in games)
            replay=call(f'/api/v1/runs/{run}/games/{games[0]["id"]}/replay?audit=true')[1];assert replay
            checks.append('two complete real Forge games, exact 64-bit seed, public catalog and replay')
            assert call('/api/v1/host/shutdown','POST',owner='wrong')[0]==403
            request.update(gamesPerPair=20,rootSeed='9223372036854775806')
            interrupted=call('/api/v1/runs','POST',request)[1]['id'];wait(lambda:call('/api/v1/health')[1]['activeRuns']>0)
            assert call('/api/v1/host/shutdown','POST',owner=env['CRUCIBLE_HOST_OWNER'])[0]==409
            assert call('/api/v1/host/shutdown?stopWork=true','POST',owner=env['CRUCIBLE_HOST_OWNER'])[0]==202
            child.wait(timeout=30);assert child.returncode==0
            child=start();health=call('/api/v1/health')[1]
            assert health['activeRuns']==0 and health['queuedRuns']==0
            assert call('/api/v1/runs/'+run)[1]['status']=='completed'
            assert call('/api/v1/runs/'+interrupted)[1]['status']=='cancelled'
            checks.append('busy shutdown guard, explicit owner shutdown, complete results retained, no automatic resume')
            assert call('/api/v1/host/shutdown','POST',owner=env['CRUCIBLE_HOST_OWNER'])[0]==202
            child.wait(timeout=30);assert child.returncode==0
            (output/'summary.json').write_text(json.dumps({'checks':checks,'run':saved,'replayRecords':len(replay)},indent=2)+'\n')
            print('Installed native backend checks passed: '+', '.join(checks))
        finally:
            if child and child.poll() is None:child.terminate();child.wait(timeout=30)
            log.close()
if __name__=='__main__':main()
