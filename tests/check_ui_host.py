#!/usr/bin/env python3
"""Exercise the native UI gateway against an isolated fake backend; no real runs are modified."""
import argparse,http.client,json,os,socket,subprocess,tempfile,threading,time,urllib.request,urllib.error
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path

def port():
 with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
def call(base,path,method='GET',body=None,headers=None):
 data=body if isinstance(body,bytes) else None if body is None else json.dumps(body).encode()
 request=urllib.request.Request(base+path,data=data,method=method,headers={'Content-Type':'application/json',**(headers or {})})
 try:r=urllib.request.urlopen(request,timeout=8)
 except urllib.error.HTTPError as e:r=e
 return r.status,r.read(),dict(r.headers)
class Backend(BaseHTTPRequestHandler):
 tokens=0;last_token='';mutations=[]
 def log_message(self,*args):pass
 def send(self,code,body=b'',headers=None):
  self.send_response(code)
  for key,value in (headers or {'Content-Type':'application/json'}).items():self.send_header(key,value)
  self.end_headers();self.wfile.write(body)
 def do_GET(self):
  if self.path=='/api/v1/session':
   Backend.tokens+=1;Backend.last_token=f'backend-private-{Backend.tokens}';self.send(200,json.dumps({'instanceToken':Backend.last_token}).encode());return
  if self.path=='/api/v1/library-state':
   if self.headers.get('If-None-Match')=='"fixture-v1"':self.send(304,headers={'ETag':'"fixture-v1"'});return
   self.send(200,b'{"version":1,"decks":[],"manaValues":{}}',{'Content-Type':'application/json','ETag':'"fixture-v1"'});return
  if self.path.startswith('/api/v1/runs/fixture/export'):self.send(200,b'seed\n9223372036854775807\n',{'Content-Type':'text/csv','Content-Disposition':'attachment; filename="fixture.csv"'});return
  if self.path=='/api/v1/fixture':self.send(200,b'{"seed":9223372036854775807,"rate":0.1234567890123456789}');return
  self.send(404,b'{"code":404,"message":"Fixture endpoint missing"}')
 def do_POST(self):
  token=self.headers.get('X-Instance-Token');body=self.rfile.read(int(self.headers.get('Content-Length','0')))
  if token!=Backend.last_token:self.send(403);return
  Backend.mutations.append({'token':token,'key':self.headers.get('Idempotency-Key'),'body':body.decode()});self.send(200,body)
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--runtime',required=True,type=Path);parser.add_argument('--output',type=Path);args=parser.parse_args();runtime=args.runtime.resolve();server=ThreadingHTTPServer(('127.0.0.1',0),Backend);threading.Thread(target=server.serve_forever,daemon=True).start();backend=f'http://127.0.0.1:{server.server_port}';ui_port=port();ui=f'http://127.0.0.1:{ui_port}';checks=[]
 with tempfile.TemporaryDirectory(prefix='crucible-ui-check-') as temp:
  root=Path(temp)/'projects/crucible';(root/'docs/specs').mkdir(parents=True)
  env=dict(os.environ,MTG_PROJECT_ROOT=str(root),MTG_PORT=str(ui_port),MTG_BACKEND_URL=backend+'/',MTG_UI_STATE_ROOT=str(root/'ui-state'),ASPNETCORE_ENVIRONMENT='Production',CRUCIBLE_HOST_OWNER='fixture-owner-'+('a'*48))
  with open(Path(temp)/'ui.log','wb') as log:
   child=subprocess.Popen([str(runtime/('CrucibleUi.exe' if os.name=='nt' else 'CrucibleUi'))],cwd=runtime,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT)
  try:
   deadline=time.monotonic()+20
   while True:
    if child.poll() is not None:raise AssertionError((Path(temp)/'ui.log').read_text())
    try:
     status,raw,_=call(ui,'/api/v1/health')
     if status==200:break
    except OSError:pass
    assert time.monotonic()<deadline,'UI startup timeout';time.sleep(.05)
   health=json.loads(raw);assert health['role']=='ui' and health['runtimeMode']=='native-aot' and not health['reflectionJsonEnabled'];checks.append('native health')
   assert call(ui,'/api/v1/health',headers={'Host':'untrusted.example'})[0]==403
   assert call(ui,'/api/v1/session',headers={'Origin':'https://untrusted.example'})[0]==403;checks.append('Host and Origin guards')
   session=json.loads(call(ui,'/api/v1/session')[1]);token=session['instanceToken'];assert not token.startswith('backend-private-')
   assert call(ui,'/api/v1/fixture','POST',{'test':'uncredentialed'})[0]==403;assert not Backend.mutations;checks.append('UI token and backend credential isolation')
   assert call(ui,'/api/v1/fixture')[1]==call(backend,'/api/v1/fixture')[1];checks.append('exact 64-bit and fractional JSON forwarding')
   status,_,headers=call(ui,'/api/v1/library-state');assert status==200
   assert call(ui,'/api/v1/library-state',headers={'If-None-Match':headers['ETag']})[:2]==(304,b'');checks.append('conditional 304 passthrough')
   for index in range(2):
    payload={'seed':'9223372036854775807','index':index};status,raw,_=call(ui,'/api/v1/fixture','POST',payload,{'X-Instance-Token':token,'Idempotency-Key':'fixture-action'})
    assert status==200 and json.loads(raw)==payload
   assert len(Backend.mutations)==2 and Backend.mutations[0]['token']!=Backend.mutations[1]['token'] and all(m['key']=='fixture-action' for m in Backend.mutations);checks.append('fresh backend tokens and idempotency passthrough')
   connection=http.client.HTTPConnection('127.0.0.1',ui_port,timeout=5);connection.putrequest('POST','/api/v1/fixture');connection.putheader('X-Instance-Token',token);connection.putheader('Content-Length',str(2_097_153));connection.endheaders();assert connection.getresponse().status==413;connection.close();assert len(Backend.mutations)==2;checks.append('bounded request bodies')
   status,raw,headers=call(ui,'/api/v1/runs/fixture/export');assert status==200 and b'9223372036854775807' in raw and headers['Content-Disposition'].startswith('attachment');checks.append('download headers')
   status,raw,headers=call(ui,'/runs/fixture');assert status==200 and b'/src/main.ts' not in raw and b'assets/index-' in raw and "frame-ancestors 'none'" in headers['Content-Security-Policy'];checks.append('static SPA deep links and CSP')
   assert call(ui,'/api/v1/missing')[0]==404;assert call(ui,'/api/v1/presentation/build-names?engine=../private&bridge=abc')[0]==422;checks.append('API 404 and presentation input bounds')
   catalog=json.loads(call(ui,'/api/v1/presentation/catalog')[1]);assert catalog['maximumDecks']==10 and catalog['modes'] and not catalog['decks'];assert 'auras-v001' in catalog['currentProfiles'] and catalog['pilotLabels']['auras-v001']=='Ajani · Auras v001';checks.append('source-generated presentation metadata and full current pilot catalog')
   server.shutdown();server.server_close();assert call(ui,'/api/v1/library-state')[0]==503;checks.append('backend outage remains recoverable')
   assert call(ui,'/api/v1/host/shutdown','POST',headers={'X-Instance-Token':token,'X-Crucible-Owner':'wrong'})[0]==403
   assert child.poll() is None
   assert call(ui,'/api/v1/host/shutdown','POST',headers={'X-Instance-Token':token,'X-Crucible-Owner':env['CRUCIBLE_HOST_OWNER']})[0]==202
   child.wait(timeout=15);assert child.returncode==0;checks.append('launcher ownership credential and graceful native shutdown')
  finally:
   if child.poll() is None:child.terminate();child.wait(timeout=10)
   if args.output:
    args.output.mkdir(parents=True,exist_ok=True);(args.output/'ui-host-checks.log').write_bytes((Path(temp)/'ui.log').read_bytes())
 result={'checks':checks,'passed':True}
 if args.output:(args.output/'ui-host-checks.json').write_text(json.dumps(result,indent=2))
 print(json.dumps(result,indent=2))
if __name__=='__main__':main()
