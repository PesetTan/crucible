#!/usr/bin/env python3
"""Exercise the installed Windows app, real Forge, native close dialogs and data retention.
Runs only against disposable installation/state directories and alternate ports.
"""
import argparse, base64, ctypes, ctypes.wintypes as wt, hashlib, json, os, shutil, socket, subprocess, tempfile, time
import urllib.error, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import threading
from pathlib import Path


def wait(check, timeout=60):
    deadline=time.monotonic()+timeout; last=None
    while time.monotonic()<deadline:
        try:
            result=check()
            if result:return result
        except (OSError, ValueError, AssertionError, RuntimeError) as e:last=e
        time.sleep(.25)
    raise AssertionError(f'Timed out: {last}')

def port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]

def http(url,method='GET',body=None,headers=None,timeout=15):
    data=None if body is None else json.dumps(body).encode()
    req=urllib.request.Request(url,data=data,method=method,headers={'Content-Type':'application/json',**(headers or {})})
    try:
        with urllib.request.urlopen(req,timeout=timeout) as response:
            raw=response.read();return response.status,json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        raw=e.read()
        try:value=json.loads(raw)
        except ValueError:value=raw.decode(errors='replace')
        return e.code,value

class StartupFailure(Exception):pass

class Driver:
    def __init__(self,url,debugger):
        self.url=url
        code,r=http(url+'/session','POST',{'capabilities':{'alwaysMatch':{'browserName':'webview2','ms:edgeChromium':True,'ms:edgeOptions':{'debuggerAddress':debugger}}}},timeout=120)
        assert code==200,r
        self.id=r['value']['sessionId']
        self.call('POST','/timeouts',{'script':20000,'pageLoad':120000,'implicit':0})
    def call(self,method,path,body=None):
        code,r=http(f'{self.url}/session/{self.id}'+path,method,body,timeout=150)
        assert code==200,(path,r)
        return r.get('value') if r else None
    def js(self,script,*args):return self.call('POST','/execute/sync',{'script':script,'args':list(args)})
    def main(self):
        for handle in self.call('GET','/window/handles'):
            self.call('POST','/window',{'handle':handle})
            if self.js("return location.protocol==='http:' && document.querySelector('.shell')!==null"):return True
        return False
    def shot(self,path):path.write_bytes(base64.b64decode(self.call('GET','/screenshot')))
    def go(self,path):self.js('location.href=arguments[0]',path)
    def text(self):return self.js('return document.body.innerText')
    def button(self,text):
        return self.js("const b=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()===arguments[0]&&!b.disabled);if(!b)return false;b.click();return true",text)
    def value(self,label,value):
        assert self.js("const l=[...document.querySelectorAll('label')].find(l=>l.textContent.trim().startsWith(arguments[0]));const e=l?.querySelector('input');if(!e)return false;e.value=arguments[1];e.dispatchEvent(new Event('input',{bubbles:true}));return true",label,str(value)),label
    def dispose(self):
        try:self.call('DELETE','')
        except (AssertionError,OSError):pass

class Native:
    def __init__(self):
        self.ui=ctypes.WinDLL('user32',use_last_error=True)
        self.callback=ctypes.WINFUNCTYPE(wt.BOOL,wt.HWND,wt.LPARAM)
        self.ui.GetWindowTextW.argtypes=[wt.HWND,wt.LPWSTR,ctypes.c_int]
        self.ui.GetWindowThreadProcessId.argtypes=[wt.HWND,ctypes.POINTER(wt.DWORD)]
        self.ui.PostMessageW.argtypes=[wt.HWND,wt.UINT,wt.WPARAM,wt.LPARAM]
        self.ui.EnumWindows.argtypes=[self.callback,wt.LPARAM]
        self.ui.EnumChildWindows.argtypes=[wt.HWND,self.callback,wt.LPARAM]
    def text(self,h):
        buf=ctypes.create_unicode_buffer(1024);self.ui.GetWindowTextW(h,buf,len(buf));return buf.value
    def windows(self,parent=None):
        result=[]
        cb=self.callback(lambda h,_:(result.append(h),True)[1])
        if parent:self.ui.EnumChildWindows(parent,cb,0)
        else:self.ui.EnumWindows(cb,0)
        return result
    def main(self):return next((h for h in self.windows() if self.text(h)=='Crucible · Windows Beta'),None)
    def close(self):
        h=wait(self.main);self.ui.PostMessageW(h,0x10,0,0)
    def choose(self,text):
        main=wait(self.main);pid=wt.DWORD();self.ui.GetWindowThreadProcessId(main,ctypes.byref(pid))
        def find():
            for top in self.windows():
                owner=wt.DWORD();self.ui.GetWindowThreadProcessId(top,ctypes.byref(owner))
                if owner.value!=pid.value:continue
                for child in self.windows(top):
                    if self.text(child).replace('&','')==text:
                        self.ui.PostMessageW(child,0xF5,0,0);return True
            return False
        wait(find,15)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--installer',required=True,type=Path);p.add_argument('--output',required=True,type=Path);args=p.parse_args()
    if os.name!='nt':p.error('This integration check requires Windows.')
    output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    installer=args.installer.resolve();checks=[];driver=None;web=None
    def record(message):
        checks.append(message);print('Passed: '+message,flush=True)
    with tempfile.TemporaryDirectory(prefix='Crucible Windows Ω ') as temporary:
        base=Path(temporary);install=base/'Installed App Ω';state=base/'Saved Data Ω'
        state.mkdir();ui_port,backend_port=port(),port();assert ui_port!=backend_port
        ui=f'http://127.0.0.1:{ui_port}';backend=f'http://127.0.0.1:{backend_port}'
        env=dict(os.environ,MTG_PROJECT_ROOT=str(state),CRUCIBLE_UI_PORT=str(ui_port),CRUCIBLE_BACKEND_PORT=str(backend_port))
        for key in ['MTG_ASSET_ROOT','MTG_CARD_DB','MTG_JAVA','MTG_PYTHON','MTG_HOST_MODE','CRUCIBLE_HOST_OWNER','CRUCIBLE_LAUNCH_EVENT','DOTNET_ROOT','DOTNET_ROOT_X64']:
            env.pop(key,None)
        # Runtime proof: only Windows system utilities are on the app's PATH.
        env['PATH']=str(Path(os.environ['SystemRoot'])/'System32')+os.pathsep+os.environ['SystemRoot']
        def install_app():
            # NSIS requires /D last and unquoted, even when the path contains spaces.
            # A Python argument list quotes the whole /D=... argument and NSIS ignores it.
            result=subprocess.run(f'"{installer}" /S /D={install}',timeout=180,capture_output=True)
            assert result.returncode==0,result.stderr.decode(errors='replace')
            exes=[x for x in install.glob('*.exe') if not x.name.lower().startswith('uninstall')]
            assert len(exes)==1,{'expectedInstallDirectory':str(install),'executables':exes,'files':list(install.glob('**/*'))[:25]}
            return exes[0]
        executable=install_app();record('silent per-user install in a Unicode/spaced directory')
        native=Native()
        class Foreign(BaseHTTPRequestHandler):
            def log_message(self,*args):pass
            def do_GET(self):
                self.send_response(200);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(b'{"application":"another-program"}')
        foreign=ThreadingHTTPServer(('127.0.0.1',backend_port),Foreign)
        threading.Thread(target=foreign.serve_forever,daemon=True).start()
        # A non-automated collision window must not leave a browser process/profile
        # for EdgeDriver to reuse without its remote-debugging flags.
        collision_state=base/'Collision Data Ω'
        collision=subprocess.Popen([str(executable)],env=dict(env,MTG_PROJECT_ROOT=str(collision_state)))
        try:
            wait(lambda:any(native.text(h)=='Crucible Could Not Start' for h in native.windows()),30)
            assert http(backend+'/api/v1/health')[1]['application']=='another-program'
            assert not (collision_state/'data/engine-bundles/active.json').exists()
        finally:
            collision.terminate();collision.wait(timeout=10);foreign.shutdown();foreign.server_close()
        record('occupied backend port remains untouched and startup error is visible')
        webdriver_port,debug_port=port(),port()
        app_processes=[]
        driver_log=(output/'webdriver.log').open('wb')
        driver=subprocess.Popen([shutil.which('msedgedriver'),f'--port={webdriver_port}','--verbose',f'--log-path={output / "edgedriver.log"}'],env=dict(env,TAURI_WEBVIEW_AUTOMATION='true'),stdout=driver_log,stderr=subprocess.STDOUT)
        url=f'http://127.0.0.1:{webdriver_port}'
        wait(lambda:http(url+'/status')[0]==200)
        def capture_desktop(name):
            capture=Path(__file__).with_name('capture_windows_desktop.ps1')
            try:
                subprocess.run([str(Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'),'-NoProfile','-NonInteractive','-File',str(capture),'-OutputPath',str(output/name),'-ProcessOutputPath',str(output/(Path(name).stem+'-processes.json'))],timeout=20,capture_output=True)
            except (OSError,subprocess.TimeoutExpired):pass
        def launch():
            # Capture while InitSession is pending; EdgeDriver kills the app on timeout.
            child=subprocess.Popen([str(executable)],env=dict(env,TAURI_WEBVIEW_AUTOMATION='true',WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=f'--remote-debugging-port={debug_port} --remote-debugging-address=127.0.0.1'))
            app_processes.append(child)
            startup_capture=threading.Timer(10,lambda:capture_desktop('desktop-startup.png'))
            startup_capture.start()
            try:
                def debugger_ready():
                    if child.poll() is not None:raise StartupFailure(f'Desktop app exited: {child.returncode}')
                    return http(f'http://127.0.0.1:{debug_port}/json/list',timeout=1)[1]
                wait(debugger_ready,60)
                result=Driver(url,f'127.0.0.1:{debug_port}')
            finally:startup_capture.cancel()
            print('WebDriver connected; waiting for engine setup and the main app.',flush=True)
            last_setup_text=None
            def ready():
                nonlocal last_setup_text
                if any(native.text(h)=='Crucible Could Not Start' for h in native.windows()):
                    logs=state/'data/desktop/logs'
                    detail='\n'.join(f'{p.name}: {p.read_text(encoding="utf-8",errors="replace")[-4000:]}' for p in logs.glob('*') if p.is_file())
                    raise StartupFailure('The app displayed a startup error.\n'+detail)
                if result.main():return True
                setup_text=result.text().strip()
                if setup_text!=last_setup_text:
                    print('Setup window: '+setup_text.replace('\n',' | ')[:500],flush=True)
                    last_setup_text=setup_text
                return False
            wait(ready,1200)
            wait(lambda:'Choose the Decks' in result.text(),60)
            return result
        def health(origin):return http(origin+'/api/v1/health')[1]
        def down(origin):
            try:return http(origin+'/api/v1/health',timeout=1)[0]!=200
            except OSError:return True
        def api(path,method='GET',body=None):
            headers={}
            if method!='GET':
                headers={'X-Instance-Token':http(ui+'/api/v1/session')[1]['instanceToken'],'Idempotency-Key':os.urandom(16).hex()}
            status,value=http(ui+path,method,body,headers)
            assert status in (200,201,202),(path,status,value)
            return value
        succeeded=False
        try:
            # An external service can own the same database on a different port.
            # Refuse that conflict visibly without terminating or replacing its owner.
            lock_root=base/'Database Owner Ω';owner_port=port();owner_origin=f'http://127.0.0.1:{owner_port}'
            owner_secret='external-fixture-'+os.urandom(32).hex()
            with (output/'database-owner.log').open('wb') as owner_log:
                existing=subprocess.Popen([str(install/'resources/service/CrucibleBackend.exe')],env=dict(env,MTG_PROJECT_ROOT=str(lock_root),MTG_ASSET_ROOT=str(install/'resources/assets'),MTG_PORT=str(owner_port),MTG_HOST_MODE='service',CRUCIBLE_HOST_OWNER=owner_secret),stdout=owner_log,stderr=subprocess.STDOUT)
                blocker=None
                try:
                    wait(lambda:(h:=http(owner_origin+'/api/v1/health'))[0]==200 and h[1]['ready'],60)
                    owner_health=http(owner_origin+'/api/v1/health')[1]
                    blocker=subprocess.Popen([str(executable)],env=dict(env,MTG_PROJECT_ROOT=str(lock_root)))
                    def ownership_dialog():
                        return any('already owns this data directory' in native.text(control) for top in native.windows() if native.text(top)=='Crucible Could Not Start' for control in native.windows(top))
                    wait(ownership_dialog,30)
                    after=http(owner_origin+'/api/v1/health')[1]
                    assert existing.poll() is None and after['ready'] and after['instanceId']==owner_health['instanceId']
                    assert not (lock_root/'data/engine-bundles/active.json').exists()
                    record('database ownership conflict is visible; external native host remains untouched')
                finally:
                    if blocker and blocker.poll() is None:blocker.terminate();blocker.wait(timeout=10)
                    if existing.poll() is None:
                        try:
                            token=http(owner_origin+'/api/v1/session')[1]['instanceToken']
                            assert http(owner_origin+'/api/v1/host/shutdown','POST',headers={'X-Instance-Token':token,'X-Crucible-Owner':owner_secret})[0]==202
                            existing.wait(timeout=15)
                        finally:
                            if existing.poll() is None:existing.terminate();existing.wait(timeout=10)
                    lock_log=lock_root/'data/desktop/logs/setup.log'
                    if lock_log.exists():shutil.copy2(lock_log,output/'database-lock-startup.log')
            web=launch();web.shot(output/'first-launch.png');record('first-launch setup with real Forge and local Java; no SDK/runtime tools on PATH')
            for origin in [ui,backend]:
                h=health(origin);assert h['ready'] and h['runtimeMode']=='native-aot' and not h['reflectionJsonEnabled'],h
                token=http(origin+'/api/v1/session')[1]['instanceToken']
                assert http(origin+'/api/v1/host/shutdown','POST',headers={'X-Instance-Token':token,'X-Crucible-Owner':'wrong'})[0]==403
                assert http(origin+'/api/v1/host/shutdown','POST')[0]==403
            record('both native hosts and launcher-only shutdown authentication')
            before=[health(origin)['instanceId'] for origin in [ui,backend]]
            duplicate=subprocess.run([str(executable)],env=env,timeout=20)
            assert duplicate.returncode==0
            assert [health(origin)['instanceId'] for origin in [ui,backend]]==before
            record('duplicate desktop launch adopts no new hosts')
            library=api('/api/v1/library-state')
            assert library['decks'] and all(d['manifest']['sourceType']=='public' for d in library['decks'])
            decks=[d['manifest'] for d in library['decks'] if d['coverage']['status']!='blocked']
            # Prefer two fast interactive public lists, with a deterministic fallback.
            decks.sort(key=lambda d:(0 if 'Mono-Red Burn' in d['name'] else 1 if 'Rakdos Aggro' in d['name'] else 2,d['id']))
            decks=list({d['name']:d for d in reversed(decks)}.values())[::-1]
            assert len(decks)>=2
            web.go('/workshop?deck='+decks[0]['id'])
            wait(lambda:'60 Main-Deck Cards' in web.text())
            web.value('Deck name','Windows Beta Saved Deck Ω')
            wait(lambda:web.button('Save to Library'))
            wait(lambda:'Saved to Deck Library.' in web.text());web.shot(output/'workshop.png')
            record('Workshop edits and saves a real public deck')
            web.go('/')
            wait(lambda:'Choose the Decks' in web.text())
            assert web.js("document.querySelectorAll('details').forEach(d=>d.open=true);return true")
            for deck in decks[:2]:
                assert web.js("const c=[...document.querySelectorAll('.deck-card')].find(c=>c.textContent.includes(arguments[0]));const e=c?.querySelector('input[type=checkbox]');if(!e||e.disabled)return false;if(!e.checked)e.click();return true",deck['name'])
            web.value('Games per Matchup',2);web.value('Root Seed','9223372036854775807');web.value('Time limit per game',180)
            wait(lambda:web.button('Run Exploratory Games'))
            run_id=wait(lambda:web.js("return /^\\/runs\\/[a-f0-9]+$/.test(location.pathname)?location.pathname.split('/')[2]:null"))
            wait(lambda:api('/api/v1/runs/'+run_id)['status']=='completed',900)
            finished=api('/api/v1/runs/'+run_id)
            assert finished['config']['rootSeed']==9223372036854775807,finished['config']
            games=api('/api/v1/runs/'+run_id+'/games')
            assert len(games)==2 and all(g['status']=='completed' and g['result'] for g in games),games
            web.shot(output/'results.png')
            web.go(f'/runs/{run_id}/games/{games[0]["id"]}')
            wait(lambda:'Replay' in web.text());web.shot(output/'replay.png')
            assert api(f'/api/v1/runs/{run_id}/games/{games[0]["id"]}/replay?audit=true')
            record('two complete real Forge games, exact signed 64-bit seed, results, and replay')
            # Create enough new work to test native confirmation and explicit recovery.
            request=dict(deckIds=[d['id'] for d in decks[:2]],gamesPerPair=20,workers=2,timeoutSeconds=180,turnLimit=150,rootSeed='9223372036854775806',diagnostic=True,staggerStarts=True,startupSpacingSeconds=1,pilotMode=finished['config']['pilot'])
            interrupted=api('/api/v1/runs','POST',request)['id']
            wait(lambda:health(backend)['activeRuns']>0)
            native.close();native.choose('Keep Open')
            assert [health(origin)['instanceId'] for origin in [ui,backend]]==before
            native.close();native.choose('Stop And Exit')
            wait(lambda:down(ui) and down(backend),45)
            web.dispose();web=None
            web=launch()
            assert health(backend)['activeRuns']==0 and health(backend)['queuedRuns']==0
            assert api('/api/v1/runs/'+run_id)['status']=='completed'
            assert api('/api/v1/runs/'+interrupted)['status']=='cancelled'
            web.go('/runs/'+interrupted)
            wait(lambda:web.button('Resume Unfinished Games'))
            wait(lambda:health(backend)['activeRuns']>0)
            api('/api/v1/runs/'+interrupted+'/cancel','POST')
            wait(lambda:health(backend)['activeRuns']==0)
            native.close();wait(lambda:down(ui) and down(backend),45);web.dispose();web=None
            record('Keep Open / Stop And Exit, graceful owned-host shutdown, saved results, explicit resume')
            catalog=state/'catalog.sqlite';catalog_hash=hashlib.sha256(catalog.read_bytes()).hexdigest()
            replay_hashes={x.name:hashlib.sha256(x.read_bytes()).hexdigest() for x in (state/'data/replays').glob('*') if x.is_file()}
            assert replay_hashes
            sentinel=state/'keep-on-uninstall.txt';sentinel.write_text('user data')
            executable=install_app()
            assert hashlib.sha256(catalog.read_bytes()).hexdigest()==catalog_hash and sentinel.read_text()=='user data'
            record('silent same-version upgrade preserves user catalog and state')
            uninstallers=list(install.glob('*uninstall*.exe'));assert len(uninstallers)==1,uninstallers
            assert subprocess.run([str(uninstallers[0]),'/S'],timeout=120).returncode==0
            wait(lambda:not executable.exists())
            assert sentinel.read_text()=='user data' and hashlib.sha256(catalog.read_bytes()).hexdigest()==catalog_hash
            assert replay_hashes=={x.name:hashlib.sha256(x.read_bytes()).hexdigest() for x in (state/'data/replays').glob('*') if x.is_file()}
            record('uninstall retains catalog, simulation database, and replay bytes')
            (output/'summary.json').write_text(json.dumps({'checks':checks,'target':'Windows x64 hosted runner','humanWindows11Validation':False},indent=2)+'\n')
            succeeded=True
            print('Windows installed desktop checks passed: '+', '.join(checks))
        finally:
            if not succeeded:
                capture_desktop('desktop-failure.png')
                try:
                    profile_files=[str(p.relative_to(state)) for p in (state/'webview').rglob('*') if p.is_file()]
                    devtools={str(p.relative_to(state)):p.read_text(encoding='utf-8',errors='replace')[:1000] for p in state.rglob('DevToolsActivePort')}
                    (output/'startup-context.json').write_text(json.dumps({'windows':[native.text(h) for h in native.windows() if native.text(h).startswith('Crucible')],'webviewFiles':profile_files[:100],'devtoolsPorts':devtools},indent=2)+'\n')
                except OSError:pass
            if web:
                if not succeeded:
                    try:web.shot(output/'failure.png')
                    except (AssertionError,OSError):pass
                web.dispose()
            if driver:
                subprocess.run(['taskkill','/PID',str(driver.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                driver.wait(timeout=20)
            for app_process in app_processes:
                if app_process.poll() is None:
                    subprocess.run(['taskkill','/PID',str(app_process.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                    app_process.wait(timeout=20)
            driver_log.close()
            logs=state/'data/desktop/logs'
            if logs.exists():shutil.copytree(logs,output/'app-logs',dirs_exist_ok=True)
            (output/'progress.json').write_text(json.dumps({'checksCompleted':checks},indent=2)+'\n')

if __name__=='__main__':main()
