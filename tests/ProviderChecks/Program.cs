using DeckTester;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Nodes;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
if(args.Length!=1)throw new ArgumentException("Provide an isolated initialized root.");
Check(Json.Decode<RunRequest>("{\"deckIds\":[\"A\",\"B\"]}") is {ProviderId:"forge",Capture:"summary"},"omitted provider/capture retain legacy defaults through source-generated JSON");
var paths=new AppPaths(args[0]);var store=new Store(paths);var installationPath=Path.Combine(paths.Data,"pyre-provider.json");var installation=File.ReadAllText(installationPath);
await using var forge=new ForgeEngine(paths,store);await using var pyre=new PyreEngine(paths,store);var providers=new EngineProviders(forge,pyre);
var prior=store.Runs().First(r=>r.Config.ProviderId=="pyre");
var request=new RunRequest(prior.Config.DeckIds,2,1,30,150,42,true,StaggerStarts:false){ProviderId="pyre",PyreCohort="initial"};
Check(providers.Discover().Single(p=>p.Id=="pyre").Capabilities.Available,"native discovery pins executable and contract");
Check(providers.Preflight(request).Count==0,"exact frozen decks pass native preflight");
var legacyDeck=store.Deck(request.DeckIds[0]);await store.SaveDeck(legacyDeck with{Cards=legacyDeck.Cards.Select(c=>c with{Zone="main"}).ToList()});
Check(providers.Preflight(request).Count==0,"historical imported main-zone spelling remains selectable without rewriting history");await store.SaveDeck(legacyDeck);
Check(providers.Preflight(request with{DeckIds=[request.DeckIds[0],"unsupported"]}).Count>0,"unsupported deck blocks before dispatch");
Check(providers.Preflight(request with{Capture="invented"}).Count>0,"unsupported capture blocks");
Check(providers.Preflight(request with{OptimizerPolicyId="forge-policy"}).Count>0,"Forge policy cannot leak into Pyre");
Check(providers.Preflight(request with{Workers=5}).Count>0,"worker cap enforced");
var locked=pyre.Configure(request,"native-fixtures");
Check(!pyre.HasBundle(locked with{Pyre=locked.Pyre! with{Manifest=locked.Pyre.Manifest+" "}}),"run manifest checksum is immutable");
Check(!Store.CompatibleSession(locked,locked with{Provider=locked.Provider! with{ExperimentId="other"}}),"experiments remain isolated");
Check(!Store.CompatibleSession(locked,locked with{Provider=null}),"legacy null-provider records remain Forge and isolated");
var run=new SimulationRun(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"running",locked,2);
var slot=new GameSlot(Guid.NewGuid().ToString("N"),run.Id,locked.DeckIds[0],locked.DeckIds[1],locked.DeckIds[0],1,0,"pending",[]);
var first=await providers.Play(run,slot,1,CancellationToken.None);var second=await providers.Play(run,slot with{Index=1},1,CancellationToken.None);
Check(first.Result is not null&&second.Result is not null&&first.Replay is null,"reused native worker returns actual complete metrics with unavailable summary replay");
await providers.ReleaseWorkers(run.Id);
using(var coordinator=new RunCoordinator(store,forge,NullLogger<RunCoordinator>.Instance,paths,providers:providers)){
    await coordinator.StartAsync(CancellationToken.None);while(!coordinator.IsReady)await Task.Delay(10);
    var key="coordinator-fixtures";var id=await coordinator.Create(request,key);Check(await coordinator.Create(request,key)==id,"coordinator creation is idempotent");
    while(store.Run(id).Status is "queued" or "running")await Task.Delay(20);
    Check(store.Run(id).Status=="completed"&&Metrics.Summarize(store.Run(id),store.Slots(id)).Valid==2,"shared coordinator durably commits complete native games");
    var accepted=store.Slots(id).First();var raw=store.PyreDeliveryForSlot(accepted)!;var config=Json.Decode<PyreInstallation>(installation);var receipt=await store.ImportPyreResult(raw,File.ReadAllText(config.Metrics),File.ReadAllText(config.ControllerMetrics),store.Run(id),accepted);
    Check(receipt.Duplicate&&store.Slots(id).Count==2,"coordinator raw redelivery does not change denominator");
    Check(!pyre.Evidence(accepted).Available,"summary evidence explicitly reports replay unavailable");
    try{await store.ImportPyreResult(raw,File.ReadAllText(config.Metrics),File.ReadAllText(config.ControllerMetrics),store.Run(id),accepted with{Index=99});throw new Exception("Wrong slot accepted");}catch(InvalidDataException){}
    Check(true,"native delivery cannot commit to another slot");
    Check(store.CachedGames(locked with{Provider=locked.Provider! with{ExperimentId="another"}}).Count==0,"raw SQL cache reader isolates provider/experiment");
    await coordinator.StopAsync(CancellationToken.None);
}
// Restart recovery never resumes automatically or dispatches imported/offline runs.
var abandoned=run with{Id=Guid.NewGuid().ToString("N"),Status="running",Total=1};var abandonedSlot=slot with{Id=Guid.NewGuid().ToString("N"),RunId=abandoned.Id,Status="running",Attempts=[new(1,DateTimeOffset.UtcNow,null,"running",null,null,null)]};
await store.CreateRun(abandoned,[abandonedSlot],"abandoned-fixture","fixture");
using(var recovery=new RunCoordinator(store,forge,NullLogger<RunCoordinator>.Instance,paths,providers:providers)){
    await recovery.StartAsync(CancellationToken.None);while(!recovery.IsReady)await Task.Delay(10);
    Check(store.Run(abandoned.Id).Status=="cancelled"&&store.Slots(abandoned.Id).Single().Attempts.Single().Status=="interrupted","restart preserves attempt and requires explicit resume");
    await recovery.Resume(abandoned.Id);while(store.Run(abandoned.Id).Status is "queued" or "running")await Task.Delay(20);
    Check(store.Run(abandoned.Id).Status=="completed"&&store.Slots(abandoned.Id).Single().Attempts.Count==2,"explicit resume keeps original slot and adds an attempt");
    var cancelId=await recovery.Create(request with{GamesPerPair=120},"cancel-native-fixture");
    while(!store.Slots(cancelId).Any(s=>s.Status=="running"))await Task.Delay(5);
    await recovery.Cancel(cancelId);while(recovery.ActiveCount>0)await Task.Delay(10);
    Check(store.Run(cancelId).Status=="cancelled"&&store.Slots(cancelId).All(s=>s.Status!="running"),"cancellation drains pending/running native attempts");
    await recovery.StopAsync(CancellationToken.None);
}
// Full trace is a different capability and retains its native checksums.
var full=pyre.Configure(request with{Capture="full"},"full-native-fixture");var fullRun=run with{Id=Guid.NewGuid().ToString("N"),Config=full};var fullSlot=slot with{Id=Guid.NewGuid().ToString("N"),RunId=fullRun.Id,Status="running",Attempts=[new(1,DateTimeOffset.UtcNow,null,"running",null,null,null)]};
await store.CreateRun(fullRun,[fullSlot],"full-native-fixture","full-fixture");
var fullResult=await providers.Play(fullRun,fullSlot,1,CancellationToken.None);var completed=fullSlot with{Status="completed",Result=fullResult.Result,Attempts=[fullSlot.Attempts.Single() with{FinishedAt=DateTimeOffset.UtcNow,Status="completed"}]};
await providers.Commit(store,fullRun,completed,fullResult);var evidence=pyre.Evidence(completed);Check(evidence.Available&&pyre.ReadTrace(completed).GetProperty("complete").GetBoolean(),"full native evidence verifies checksum and capture identity");
await providers.ReleaseWorkers(fullRun.Id);
var tracePath=Path.Combine(paths.Data,"pyre-evidence",evidence.RecordId+".json");var traceBytes=File.ReadAllText(tracePath);File.WriteAllText(tracePath,traceBytes.Replace("\"complete\":true","\"complete\":false"));
try{pyre.ReadTrace(completed);throw new Exception("Corrupt trace accepted");}catch(InvalidDataException){}File.WriteAllText(tracePath,traceBytes);Check(true,"corrupt native trace is rejected");
// Synthetic worker protocol/resource faults establish transport handling, not gameplay correctness.
var nativeInstallation=Json.Decode<PyreInstallation>(installation);
var fakePath=Path.Combine(paths.Data,"fault-worker.py");
var fakeSource="""
#!/usr/bin/python3
import hashlib,json,os,struct,subprocess,sys,time
REAL=__NATIVE__
def send(v):
 b=json.dumps(v,separators=(',',':')).encode();sys.stdout.buffer.write(struct.pack('>I',len(b))+b);sys.stdout.buffer.flush()
def digest(domain,value):
 b=json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode();d=domain.encode();return hashlib.sha256(struct.pack('>Q',len(d))+d+struct.pack('>Q',len(b))+b).hexdigest()
if sys.argv[1]!='worker':
 value=json.loads(subprocess.check_output([REAL]+sys.argv[1:]));value['artifact_sha256']=hashlib.sha256(open(__file__,'rb').read()).hexdigest()
 if sys.argv[1]=='preflight':value['experiment_id']='';value['experiment_id']=digest('spark-experiment-v1',value)
 print(json.dumps(value));sys.exit()
manifest=json.load(open(sys.argv[3]));mode=open(os.path.join(os.path.dirname(__file__),'fault-mode.txt')).read()
frame=dict(protocol='furnace-worker-v1',manifest=manifest,pid=os.getpid(),max_request_bytes=4194304,max_response_bytes=150994944,max_in_flight=1)
if mode=='protocol':frame['protocol']='bad'
if mode=='identity':frame['manifest']['artifact_sha256']='0'*64
send(frame)
if mode in ('protocol','identity'):sys.exit()
header=sys.stdin.buffer.read(4)
if len(header)!=4:sys.exit()
sys.stdin.buffer.read(struct.unpack('>I',header)[0])
if mode=='oversize':sys.stdout.buffer.write(struct.pack('>I',150994945));sys.stdout.buffer.flush()
elif mode=='truncated':sys.stdout.buffer.write(struct.pack('>I',100)+b'bad');sys.stdout.buffer.flush()
elif mode=='memory':ballast=bytearray(80*1024*1024);time.sleep(30)
elif mode=='timeout':time.sleep(30)
elif mode=='result':send(dict(protocol='furnace-worker-v1',canonical_result='{}'))
sys.exit(41)
""".Replace("__NATIVE__",System.Text.Json.JsonSerializer.Serialize(nativeInstallation.Executable));
File.WriteAllText(fakePath,fakeSource);
if(!OperatingSystem.IsWindows())File.SetUnixFileMode(fakePath,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
var fakeInstallation=nativeInstallation with{Executable=fakePath,ExecutableHash=ForgeEngine.FileHash(fakePath),WorkerRssMiB=32};
File.WriteAllText(installationPath,Json.Encode(fakeInstallation));
try{
 foreach(var mode in new[]{"protocol","identity","oversize","truncated","memory","timeout","result"}){
  File.WriteAllText(Path.Combine(paths.Data,"fault-mode.txt"),mode);
  var badConfig=pyre.Configure(request with{TimeoutSeconds=10},"fault-"+mode);var badRun=run with{Id=Guid.NewGuid().ToString("N"),Config=badConfig};var badSlot=slot with{RunId=badRun.Id};
  var timer=Stopwatch.StartNew();var rejected=false;
  try{await providers.Play(badRun,badSlot,1,CancellationToken.None);}catch(Exception error)when(error is IOException or InvalidDataException or OperationCanceledException or TimeoutException or KeyNotFoundException){rejected=true;}
  await providers.ReleaseWorkers(badRun.Id);Check(rejected&&timer.Elapsed.TotalSeconds<15,"bounded native worker fault: "+mode);
 }
}finally{File.WriteAllText(installationPath,installation);}
Check(pyre.HasBundle(locked),"restoring registration preserves exact prior native installation");
// Synthetic metric transport fixtures exercise mapping; they do not claim new Kernel rules support.
static string Rehash(JsonObject value){value["payload_hash"]="";using var d=JsonDocument.Parse(value.ToJsonString());value["payload_hash"]=PyreRecord.SemanticHash("spark-result-payload-v1",PyreRecord.Canonical(d.RootElement));return value.ToJsonString();}
var metricConfig=Json.Decode<PyreInstallation>(installation);var definitions=File.ReadAllText(metricConfig.Metrics);var controllerDefinitions=File.ReadAllText(metricConfig.ControllerMetrics);
var transport=JsonNode.Parse(first.Raw!)!.AsObject();var originalRows=transport["turn_samples"]!.AsArray();var rows=new JsonArray();
for(var turn=0;turn<=2;turn++)for(var player=0;player<2;player++){
 var row=originalRows[player]!.DeepClone();row["sequence"]=turn*2+player+1;row["game_turn_number"]=turn;row["player_turn_number"]=player==0?turn:0;row["point"]=turn==0?"Initial":turn==1?"TurnEnd":"Terminal";
 row["life"]=turn==2&&player==1?-3:20;row["creatures"]=turn==2?2:0;row["total_creature_power"]=turn==2?8:0;row["artifacts"]=turn==2?1:0;row["enchantments"]=turn==2?3:0;
 rows.Add(row);
}
var duplicateBoundary=rows[4]!.DeepClone();duplicateBoundary["sequence"]=7;duplicateBoundary["life"]=17;rows.Add(duplicateBoundary);
transport["game_turn_number"]=2;transport["player_turn_number"]=new JsonArray(2,0);transport["raw_metrics"]!["game_turn_number"]=2;transport["raw_metrics"]!["player_turn_number"]=new JsonArray(2,0);transport["turn_samples"]=rows.DeepClone();transport["raw_metrics"]!["data"]!["Observed"]!["samples"]=rows.DeepClone();
var mapped=PyreRecord.Parse(Rehash(transport),definitions,controllerDefinitions).Slot.Result!;
Check(mapped.PersonalTurns["A"]==2&&mapped.PersonalTurns["B"]==0&&mapped.Samples.All(x=>x.ActualTurn<=2),"extra/skipped turn transport does not fabricate alternating player turns");
Check(mapped.Samples.Single(x=>x.ActualTurn==2&&x.Player=="B").Life==-3,"mid-turn terminal sample and signed life survive chart mapping");
Check(mapped.Samples.Single(x=>x.ActualTurn==2&&x.Player=="A") is {Creatures:2,Power:8,Artifacts:1,Enchantments:3},"copied objects and replacement-adjusted facts map from measured rows");
Check(mapped.Samples.Count(x=>x.ActualTurn==2&&x.Player=="A")==1&&mapped.Samples.Single(x=>x.ActualTurn==2&&x.Player=="A").Life==17,"last same-turn capture point feeds legacy charts while raw points remain distinct");
var missing=transport.DeepClone().AsObject();missing["raw_metrics"]!["data"]!["Observed"]!["opening"]!["mulligans"]![0]="NotCaptured";
try{PyreRecord.Parse(Rehash(missing),definitions,controllerDefinitions);throw new Exception("Missing opening accepted as complete");}catch(InvalidDataException){}Check(true,"missing opening capture is not converted into zero or a complete record");
var damaged=transport.DeepClone().AsObject();damaged["turn_samples"]![2]!["hand_cards"]=-1;damaged["raw_metrics"]!["data"]!["Observed"]!["samples"]=damaged["turn_samples"]!.DeepClone();
try{PyreRecord.Parse(Rehash(damaged),definitions,controllerDefinitions);throw new Exception("Negative hand accepted");}catch(InvalidDataException){}Check(true,"malformed normalized metric rows are rejected");
Console.WriteLine("Provider native conformance complete.");
