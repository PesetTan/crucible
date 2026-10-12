using System.Net;
using System.Net.Http.Json;
using MtgSimulator;
static void Check(bool condition,string text){if(!condition)throw new Exception(text);}
var config=new RunConfig(["A","B"],2,1,180,150,42,true,"engine","adapter");
var run=new SimulationRun("run",DateTimeOffset.UtcNow,"running",config,2);
var key=new CachedGameKey("A","B","A",1,0);
var cap=new Capabilities(true,"Forge","engine","adapter","java",[],[]);
var state=new UiSnapshot(1,[],cap,[run],new(){["run"]=[]},[new(run,0,0,0,1,1,0)],null,[],[],[],[new(config,[key])],[new(run,0,0,0,1,1,0)],"run",[],new(4,1000000000));
var down=false;var tokens=0;var mutations=0;string? createQuery=null;string? replayQuery=null;
using var http=new HttpClient(new FakeHandler(request=>{
    var path=request.RequestUri!.AbsolutePath;
    if(path.EndsWith("ui-state")){if(down)return new(HttpStatusCode.ServiceUnavailable);return Reply(state);}
    if(path.EndsWith("session")){tokens++;return Reply(new{instanceToken="token-"+tokens});}
    if(path.EndsWith("replay")){replayQuery=request.RequestUri.Query;return Reply(new List<ReplayRecord>());}
    Check(request.Headers.GetValues("X-Instance-Token").Single()=="token-"+tokens,"Every action must use the current service token.");mutations++;
    if(path=="/api/v1/runs"){createQuery=request.RequestUri.Query;Check(request.Headers.GetValues("Idempotency-Key").Single()=="request-key","Creation must preserve idempotency.");return Reply(new{id="new-run"});}
    return Reply(new{});
})){BaseAddress=new Uri("http://127.0.0.1:5079/")};
var remote=new RemoteUiBackend(http);
await remote.Refresh("run");
Check(remote.ConnectionError is null&&remote.Run("run").Status=="running"&&remote.ActiveSessionProgress().Single().Running==1,"UI must read active state over the API.");
Check(remote.CachedGameKeys(config).Contains(key)&&remote.CachedGameKeys(config with{RootSeed=99}).Count==0,"Cache keys must retain seed/engine identity.");
down=true;await remote.Refresh("run");
Check(remote.ConnectionError is not null&&remote.Run("run").Id=="run","A service outage must retain the last results with a connection warning.");
down=false;state=state with{Runs=[run with{Status="completed"}]};await remote.Refresh("run");
Check(remote.ConnectionError is null&&remote.Run("run").Status=="completed","The UI must recover when the service returns.");
await remote.Cancel("run");await remote.Resume("run");
Check(tokens==2&&mutations==2,"Service restarts must not leave mutations with an old token.");
Check(await remote.Create(new(["A","B"]),"request-key",true)=="new-run"&&createQuery=="?requireIdle=true","The UI must forward the active-session startup guard.");
await remote.Replay(new("game","run","A","B","A",1,0,"completed",[]),0,100,true);
Check(replayQuery!.Contains("audit=true"),"Audit requests must preserve the explicit audit flag.");
Console.WriteLine("Host separation checks passed: API snapshots, identities, outage recovery, fresh credentials, guarded actions, and replay audit.");
static HttpResponseMessage Reply<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value,options:Json.Options)};
sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(reply(request));
}
