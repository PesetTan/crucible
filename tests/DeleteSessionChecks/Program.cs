using System.Reflection;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MtgSimulator;
using MtgSimulator.Components.Pages;

static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
const BindingFlags instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
var root=Path.Combine(Path.GetTempPath(),"gauntlet-delete-check-"+Guid.NewGuid().ToString("N"));
var previousRoot=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",root);
try {
    var paths=new AppPaths(new TestEnvironment{ContentRootPath=root});var store=new Store(paths);
    var coordinator=new RunCoordinator(store,new ForgeEngine(paths,store),NullLogger<RunCoordinator>.Instance,paths);
    var config=new RunConfig(["A","B"],2,1,180,150,42,true,"engine","adapter");
    await store.SaveDeck(new("A","Shared Deck","public","Exact Public List","source","snapshot",null,"hash","hash",DateTimeOffset.UtcNow,[],"test"));
    var source=new SimulationRun(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"completed",config,2);
    var game=Guid.NewGuid().ToString("N");var replay=game+"-1.jsonl.gz";
    var attempt=new Attempt(1,DateTimeOffset.UtcNow.AddSeconds(-5),DateTimeOffset.UtcNow,"completed",null,replay,"hash");
    var sourceSlot=new GameSlot(game,source.Id,"A","B","A",123,0,"completed",[attempt]);
    var copy=new SimulationRun(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"completed",config with{DeckIds=["A","B","C"]},6);
    var copied=sourceSlot with{Id=Guid.NewGuid().ToString("N"),RunId=copy.Id,CachedFromRunId=source.Id,CachedFromGameId=game};
    var unrelated=new SimulationRun(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"completed",config with{RootSeed=99},2);
    var otherGame=Guid.NewGuid().ToString("N");var otherReplay=otherGame+"-1.jsonl.gz";
    var otherSlot=sourceSlot with{Id=otherGame,RunId=unrelated.Id,Attempts=[attempt with{Replay=otherReplay}]};
    await store.CreateRun(source,[sourceSlot],"source-key","source-hash");
    await store.CreateRun(copy,[copied],"copy-key","copy-hash");
    await store.CreateRun(unrelated,[otherSlot],"other-key","other-hash");
    var timingNow=DateTimeOffset.UtcNow;
    var timing=new SessionTiming("timing",source.Id,timingNow.AddMinutes(-2),timingNow,timingNow,120,10,100,"completed");
    await store.SaveSessionTiming(timing);
    await store.SaveSessionTiming(timing with{ActiveSeconds=130});
    Check(new Store(paths).SessionTimings(source.Id).Single().ActiveSeconds==130,"Session timing must persist across Store instances and update without duplicate rows.");
    Check(store.SessionTimingSamples("engine","adapter").Count==1,"Completed session periods must be available for calibration.");
    await store.SaveSessionTiming(timing with{Id="cancelled-timing",Status="cancelled"});
    await store.SaveSessionTiming(timing with{Id="interrupted-timing",Status="interrupted"});
    Check(store.SessionTimingSamples("engine","adapter").Count==1,"Cancelled and interrupted periods must not calibrate estimates.");
    Check(store.SessionTimingSamples("other-engine","adapter").Count==0,"Other engine identities must not calibrate estimates.");
    foreach(var file in new[]{replay,game+"-1.log",game+"-1.incomplete.gz",game+"-1.jsonl.gz.partial",otherReplay})File.WriteAllText(Path.Combine(paths.Replays,file),"test replay");

    // Invoke the actual UI handler with a controlled confirmation dialog.
    var page=new RunDetail();var js=new Confirmation();var navigation=new TestNavigation();
    typeof(RunDetail).GetProperty("Id",instance)!.SetValue(page,source.Id);
    typeof(RunDetail).GetProperty("JS",instance)!.SetValue(page,js);
    typeof(RunDetail).GetProperty("Coordinator",instance)!.SetValue(page,coordinator);
    typeof(RunDetail).GetProperty("Navigation",instance)!.SetValue(page,navigation);
    typeof(RunDetail).GetField("summary",instance)!.SetValue(page,Metrics.Summarize(source,[sourceSlot]));
    async Task ClickDelete()=>await (Task)typeof(RunDetail).GetMethod("DeleteSession",instance)!.Invoke(page,null)!;
    await ClickDelete();
    Check(js.Calls==1&&js.Prompt!.Contains(source.Id[..8]),"The UI must ask confirmation for this session.");
    Check(store.Runs().Count==3&&File.Exists(Path.Combine(paths.Replays,replay))&&navigation.Destination is null,"Cancelling confirmation must leave records, replays, and navigation unchanged.");
    js.Approved=true;await ClickDelete();
    Check(js.Calls==2&&navigation.Destination=="/runs","Confirmed deletion must return to Run History.");
    Check(!store.Runs().Any(r=>r.Id==source.Id)&&store.Slots(source.Id).Count==0,"Session and game records must be removed.");
    Check(store.SessionTimings(source.Id).Count==0,"Deleting a session must remove its timing records before the parent row.");
    Check(store.Decks().Count==1&&store.Runs().Count==2&&store.Slots(copy.Id).Count==1,"Shared decks and other sessions must remain.");
    Check(File.Exists(Path.Combine(paths.Replays,replay)),"A replay used by a cached session must remain.");
    foreach(var suffix in new[]{".log",".incomplete.gz",".jsonl.gz.partial"})Check(!File.Exists(Path.Combine(paths.Replays,game+"-1"+suffix)),"Unshared attempt artifacts must be removed.");
    Check(File.Exists(Path.Combine(paths.Replays,otherReplay)),"Unrelated replay must remain.");
    using(var db=new SqliteConnection("Data Source="+Path.Combine(paths.Data,"simulator.sqlite"))){
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT count(*) FROM idempotency WHERE run_id=$id";cmd.Parameters.AddWithValue("$id",source.Id);
        Check((long)cmd.ExecuteScalar()! ==0,"Deleted idempotency keys must not point to a missing session.");
    }
    await coordinator.Delete(copy.Id);
    Check(!File.Exists(Path.Combine(paths.Replays,replay)),"Replay must be removed when its final referencing session is deleted.");

    foreach(var status in new[]{"running","queued","cancelling"}){
        await store.SaveRun(unrelated with{Status=status});
        try{await coordinator.Delete(unrelated.Id);throw new Exception("Active session was deleted.");}catch(InvalidOperationException){}
        Check(store.Run(unrelated.Id).Status==status&&store.Slots(unrelated.Id).Count==1&&File.Exists(Path.Combine(paths.Replays,otherReplay)),"Rejecting active deletion must leave data intact.");
    }
    await store.SaveRun(unrelated);
    try{await coordinator.Delete(Guid.NewGuid().ToString("N"));throw new Exception("Missing session was accepted.");}catch(KeyNotFoundException){}
    Check(store.Runs().Count==1,"Missing-session deletion must not affect other sessions.");
    await store.CreateRun(source,[sourceSlot],"source-key","source-hash");
    Check(store.Run(source.Id).Id==source.Id,"Deleted idempotency keys must allow a fresh session.");
    // Cleanup failures must be surfaced after records are successfully deleted.
    var warningRun=unrelated with{Id=Guid.NewGuid().ToString("N"),Config=config with{RootSeed=100}};
    var warningGame=Guid.NewGuid().ToString("N");var warningReplay=warningGame+"-1.jsonl.gz";
    var warningSlot=otherSlot with{Id=warningGame,RunId=warningRun.Id,Attempts=[attempt with{Replay=warningReplay}]};
    await store.CreateRun(warningRun,[warningSlot],"warning-key","warning-hash");
    Directory.CreateDirectory(Path.Combine(paths.Replays,warningReplay));
    typeof(RunDetail).GetProperty("Id",instance)!.SetValue(page,warningRun.Id);
    typeof(RunDetail).GetField("summary",instance)!.SetValue(page,Metrics.Summarize(warningRun,[warningSlot]));
    await ClickDelete();
    Check(!store.Runs().Any(r=>r.Id==warningRun.Id)&&navigation.Destination=="/runs?replayCleanupIncomplete=true","Cleanup failure must redirect to history with a warning, not silently claim full cleanup.");

    // A cancelled queued session may leave a channel entry after its deletion.
    var queue=(Channel<string>)typeof(RunCoordinator).GetField("queue",instance)!.GetValue(coordinator)!;
    var queued=(ConcurrentDictionary<string,byte>)typeof(RunCoordinator).GetField("queued",instance)!.GetValue(coordinator)!;
    queued.TryAdd(copy.Id,0);queued.TryAdd(source.Id,0);
    queue.Writer.TryWrite(copy.Id);queue.Writer.TryWrite(source.Id);
    await coordinator.StartAsync(CancellationToken.None);
    using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)))while(queued.Count>0)await Task.Delay(10,deadline.Token);
    Check(coordinator.ExecuteTask?.IsFaulted!=true,"A deleted queued entry must not crash the background scheduler.");
    await coordinator.StopAsync(CancellationToken.None);
    Console.WriteLine("Delete-session checks passed: confirmation cancel/accept, atomic records, idempotency cleanup, shared replay protection, unrelated data, active-session rejection, and missing sessions. Only temporary fixtures were used.");
    page.Dispose();coordinator.Dispose();
}finally{
    Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",previousRoot);
    SqliteConnection.ClearAllPools();Directory.Delete(root,true);
}

sealed class Confirmation:IJSRuntime {
    public bool Approved;public int Calls;public string? Prompt;
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args){if(identifier!="confirm")throw new Exception("Unexpected JS call.");Calls++;Prompt=(string?)args![0];return ValueTask.FromResult((TValue)(object)Approved);}
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken ct,object?[]? args)=>InvokeAsync<TValue>(identifier,args);
}
sealed class TestNavigation:NavigationManager {
    public string? Destination;public TestNavigation()=>Initialize("http://localhost/","http://localhost/runs/test");
    protected override void NavigateToCore(string uri,bool forceLoad)=>Destination=uri;
}
sealed class TestEnvironment:IHostEnvironment {
    public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="MtgSimulator";
    public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
