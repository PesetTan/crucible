using System.Net;
using System.Net.Http.Json;
using DeckTester;
static void Check(bool condition,string text){if(!condition)throw new Exception(text);}
var config=new RunConfig(["A","B"],2,1,180,150,42,true,"engine","adapter");
var run=new SimulationRun("run",DateTimeOffset.UtcNow,"running",config,2);
var key=new CachedGameKey("A","B","A",1,0);
var cap=new Capabilities(true,"Forge","engine","adapter","java",[],[]);
var state=new UiSnapshot(1,[],cap,[run],new(){["run"]=[]},[new(run,0,0,0,1,1,0)],null,[],[],[],[new(config,[key])],[new(run,0,0,0,1,1,0)],"run",[],new(4,1000000000));
var expectedSummary=Metrics.Summarize(run,[]);
var historyState=new HistorySnapshot(1,[],cap,[new(new(run,0,0,0,1,1,0),[],[])]);
var historyRequests=0;var snapshotRequests=0;string? requestedSnapshotTag=null;
var workshopState=new WorkshopSnapshot(1,[],[run],[new(run,0,0,0,1,1,0)],new(1,2,10),new(){[1]=3},new(4,1000000000));
var workshopRequests=0;
var legacyWorkshop=false;
var libraryDeck=new DeckManifest("library-deck","Test Deck","personal","Saved","source","frozen",null,new string('a',64),new string('b',64),run.CreatedAt,
    [new(1,"Test Card",4,"MainDeck","TST","Creature","{1}{B}","Test rules",true,"")],"Frozen evidence"){FriendlyName="merry-otter"};
var libraryState=new LibrarySnapshot(1,[new(libraryDeck,new(libraryDeck.Id,"exploratory",[new("Test Card","present_unvalidated","bundle/card",null)],[]))],new(){[1]=2});
var libraryRequests=0;var legacyLibrary=false;string? requestedLibraryTag=null;
var legacySession=false;var legacyPages=new List<int>();
var legacyRun=run with{Total=205};
var legacySlots=Enumerable.Range(0,205).Select(i=>new GameSlot("game-"+i,run.Id,"A","B","A",i,i,"pending",[])).ToList();
var down=false;var tokens=0;var mutations=0;string? createQuery=null;string? replayQuery=null;
using var http=new HttpClient(new FakeHandler(request=>{
    var path=request.RequestUri!.AbsolutePath;
    if(path.EndsWith("library-state")){
        libraryRequests++;requestedLibraryTag=request.Headers.IfNoneMatch.ToString();
        if(down)return new(HttpStatusCode.ServiceUnavailable);
        if(legacyLibrary)return new(HttpStatusCode.NotFound);
        var tag=libraryState.ETag();
        if(requestedLibraryTag==tag)return new(HttpStatusCode.NotModified);
        var reply=Reply(libraryState);reply.Headers.ETag=System.Net.Http.Headers.EntityTagHeaderValue.Parse(tag);return reply;
    }
    if(path.EndsWith("workshop-state")){workshopRequests++;if(down)return new(HttpStatusCode.ServiceUnavailable);if(legacyWorkshop)return new(HttpStatusCode.NotFound);return Reply(workshopState);}
    if(path.EndsWith("run-history")){historyRequests++;if(down)return new(HttpStatusCode.ServiceUnavailable);return Reply(historyState);}
    if(path.EndsWith("ui-state")||path.EndsWith("/state")){
        if(legacySession&&path.EndsWith("/state"))return new(HttpStatusCode.NotFound);
        snapshotRequests++;requestedSnapshotTag=request.Headers.IfNoneMatch.ToString();
        if(down)return new(HttpStatusCode.ServiceUnavailable);
        if(requestedSnapshotTag==state.ETag())return new(HttpStatusCode.NotModified);
        var reply=Reply(state);reply.Headers.ETag=System.Net.Http.Headers.EntityTagHeaderValue.Parse(state.ETag());return reply;
    }
    if(legacySession){
        if(down)return new(HttpStatusCode.ServiceUnavailable);
        if(path=="/api/v1/runs/run")return Reply(legacyRun);
        if(path.EndsWith("/capabilities"))return Reply(cap);
        if(path.EndsWith("/timings"))return Reply(new List<SessionTiming>());
        if(path.StartsWith("/api/v1/decks/"))return Reply(libraryState.Decks[0] with{Manifest=libraryDeck with{Id=path.Split('/').Last()}});
        if(path.EndsWith("/games")){
            var offset=int.Parse(request.RequestUri.Query.Split('&')[0].Split('=')[1]);legacyPages.Add(offset);
            Check(request.RequestUri.Query.Contains("limit=100"),"Legacy game reads must honor the page limit.");
            return Reply(legacySlots.Skip(offset).Take(100).ToList());
        }
    }
    if(path.EndsWith("summary"))return Reply(expectedSummary);
    if(path.EndsWith("session")){tokens++;return Reply(new{instanceToken="token-"+tokens});}
    if(path.EndsWith("replay")){replayQuery=request.RequestUri.Query;return Reply(new List<ReplayRecord>());}
    Check(request.Headers.GetValues("X-Instance-Token").Single()=="token-"+tokens,"Every action must use the current service token.");mutations++;
    if(path=="/api/v1/runs"){createQuery=request.RequestUri.Query;Check(request.Headers.GetValues("Idempotency-Key").Single()=="request-key","Creation must preserve idempotency.");return Reply(new{id="new-run"});}
    return Reply(new{});
})){BaseAddress=new Uri("http://127.0.0.1:5079/")};
var remote=new RemoteUiBackend(http,null!); // Replay file paths are not exercised by this HTTP boundary check.
var firstLibrary=(await remote.LibraryState())!;
Check(libraryRequests==1&&snapshotRequests==0&&tokens==0&&Json.Encode(firstLibrary)==Json.Encode(libraryState),"Library must load exact manifests, coverage and mana sorting in one focused read-only request.");
Check(ReferenceEquals(firstLibrary,await remote.LibraryState())&&requestedLibraryTag==libraryState.ETag(),"An unchanged library must reuse its snapshot after conditional revalidation.");
var oldTag=libraryState.ETag();
libraryState=libraryState with{Decks=[libraryState.Decks[0] with{Manifest=libraryDeck with{FriendlyName="new-name"}}]};
Check(libraryState.ETag()!=oldTag,"Metadata changes must invalidate the library response.");
var revisedLibrary=(await remote.LibraryState())!;
Check(!ReferenceEquals(firstLibrary,revisedLibrary)&&revisedLibrary.Decks[0].Manifest.FriendlyName=="new-name","Changed library data must replace the cached view.");
oldTag=libraryState.ETag();
Check((libraryState with{ManaValues=new(){[1]=3}}).ETag()!=oldTag,"Mana sorting changes must invalidate the library response.");
down=true;
Check(ReferenceEquals(revisedLibrary,await remote.LibraryState())&&remote.ConnectionError is not null,"A service outage must retain the last library with a warning.");
down=false;
Check(ReferenceEquals(revisedLibrary,await remote.LibraryState())&&remote.ConnectionError is null,"Conditional revalidation must also clear a recovered connection warning.");
using(var cancelledLibrary=new CancellationTokenSource()){
    cancelledLibrary.Cancel();
    try{await remote.LibraryState(cancelledLibrary.Token);throw new Exception("Cancelled library request was not stopped.");}catch(OperationCanceledException){}
}
Check((await remote.WorkshopState())!.ManaValues[1]==3&&workshopRequests==1&&snapshotRequests==0&&tokens==0,"Workshop must load its editor state without the setup snapshot or mutation credentials.");
down=true;
Check((await remote.WorkshopState())!.Runs.Single().Id==run.Id&&remote.ConnectionError is not null,"Workshop must retain its last state through an outage.");
down=false;workshopState=workshopState with{ActiveSessions=[]};
Check((await remote.WorkshopState())!.ActiveSessions.Count==0&&remote.ConnectionError is null,"Workshop must refresh the active-session guard after recovery.");
using(var cancelledWorkshop=new CancellationTokenSource()){
    cancelledWorkshop.Cancel();
    try{await remote.WorkshopState(cancelledWorkshop.Token);throw new Exception("Cancelled Workshop request was not stopped.");}catch(OperationCanceledException){}
}
Check((await remote.History())!.Sessions.Single().Progress.Running==1&&historyRequests==1&&snapshotRequests==0&&tokens==0,"History must load in one read-only request without the setup snapshot.");
down=true;
Check((await remote.History())!.Sessions.Single().Progress.Run.Id==run.Id&&remote.ConnectionError is not null,"History must retain its last snapshot through an outage.");
down=false;historyState=historyState with{Sessions=[new(new(run with{Status="completed"},2,0,0,0,0,0),[new("A","B",2,1,1,0)],[])]};
Check((await remote.History())!.Sessions.Single().Progress.Completed==2&&remote.ConnectionError is null,"History must refresh counters and recover after an outage.");
using(var cancelled=new CancellationTokenSource()){
    cancelled.Cancel();
    try{await remote.History(cancelled.Token);throw new Exception("Cancelled history request was not stopped.");}catch(OperationCanceledException){}
}
await remote.Refresh("run");
var originalSlots=remote.Slots("run");await remote.Refresh("run");
Check(ReferenceEquals(originalSlots,remote.Slots("run"))&&requestedSnapshotTag==state.ETag(),"Unchanged sessions must keep the same slots and avoid rebuilding chart data.");
using(var cancelledRefresh=new CancellationTokenSource()){
    cancelledRefresh.Cancel();
    try{await remote.Refresh("run",cancelledRefresh.Token);throw new Exception("Cancelled session refresh was not stopped.");}catch(OperationCanceledException){}
}
Check((await remote.RunResults("run")).Run.Id==run.Id&&tokens==0&&mutations==0,"History must retrieve session results through the read-only summary API.");
Check(remote.ConnectionError is null&&remote.Run("run").Status=="running"&&remote.ActiveSessionProgress().Single().Running==1,"UI must read active state over the API.");
Check(remote.CachedGameKeys(config).Contains(key)&&remote.CachedGameKeys(config with{RootSeed=99}).Count==0,"Cache keys must retain seed/engine identity.");
Check(remote.CachedGameKeys(config with{PerformanceMode=true}).Count==0,"UI cache snapshots must separate Forge performance modes.");
Check(!Store.CompatibleSession(config,config with{PerformanceMode=true}),"Sessions with different Forge performance modes must remain separate.");
down=true;await remote.Refresh("run");
Check(remote.ConnectionError is not null&&remote.Run("run").Id=="run","A service outage must retain the last results with a connection warning.");
down=false;await remote.Refresh("run");
Check(remote.ConnectionError is null&&ReferenceEquals(originalSlots,remote.Slots("run")),"An unchanged conditional session response must clear an outage warning.");
state=state with{Runs=[run with{Status="completed"}]};await remote.Refresh("run");
Check(!ReferenceEquals(originalSlots,remote.Slots("run")),"Changed session state must replace the slots and rebuild results.");
state=state with{RunId="other",Runs=[run with{Id="other"}]};await remote.Refresh("other");
Check(string.IsNullOrEmpty(requestedSnapshotTag)&&remote.Run("other").Id=="other","Navigating to another session must not send the previous session's tag.");
state=state with{RunId="run",Runs=[run with{Status="completed"}]};await remote.Refresh("run");
Check(remote.ConnectionError is null&&remote.Run("run").Status=="completed","The UI must recover when the service returns.");
await remote.Cancel("run");await remote.Resume("run");
Check(tokens==2&&mutations==2,"Service restarts must not leave mutations with an old token.");
Check(await remote.Create(new(["A","B"]),"request-key",true)=="new-run"&&createQuery=="?requireIdle=true","The UI must forward the active-session startup guard.");
await remote.Replay(new("game","run","A","B","A",1,0,"completed",[]),0,100,true);
Check(replayQuery!.Contains("audit=true"),"Audit requests must preserve the explicit audit flag.");
legacyWorkshop=true;
Check((await remote.WorkshopState())!.Runs.Single().Status=="completed"&&remote.ConnectionError is null,"Workshop must stay usable while an older service finishes its active games.");
legacyLibrary=true;
Check((await remote.LibraryState())!.Decks.Count==state.Decks.Count&&remote.ConnectionError is null,"Library must stay usable with an older service during an active run.");
legacySession=true;var broadReads=snapshotRequests;
var olderService=new RemoteUiBackend(http,null!);await olderService.Refresh("run");
Check(olderService.ConnectionError is null&&olderService.Slots("run").Count==205&&legacyPages.SequenceEqual(new[]{0,100,200})&&snapshotRequests==broadReads,"An older service must load every page of the selected session without reading setup state.");
var olderSlots=olderService.Slots("run");await olderService.Refresh("run");
Check(ReferenceEquals(olderSlots,olderService.Slots("run")),"Unchanged legacy session reads must also retain chart data.");
down=true;await olderService.Refresh("run");
Check(olderService.ConnectionError is not null&&ReferenceEquals(olderSlots,olderService.Slots("run")),"Legacy session outages must retain results and show a warning.");
down=false;legacySlots[0]=legacySlots[0] with{Status="running"};await olderService.Refresh("run");
Check(olderService.ConnectionError is null&&olderService.SessionProgress("run").Running==1,"Legacy session reads must recover with fresh live progress.");
Console.WriteLine("Host separation checks passed: API snapshots, identities, outage recovery, fresh credentials, guarded actions, and replay audit.");
await HistoryChecks.Run();
await SessionSnapshotChecks.Run();
static HttpResponseMessage Reply<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value,options:Json.Options)};
sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(reply(request));
}
