using DeckTester;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

static class SessionSnapshotChecks
{
    public static async Task Run(){
        var root=Path.Combine(Path.GetTempPath(),"crucible-session-state-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
        var previous=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
        Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",root);
        try{
            var paths=new AppPaths(new TestEnvironment{ContentRootPath=root});var store=new Store(paths);
            var config=new RunConfig(["A","B"],10,2,180,150,42,true,"engine","adapter");
            var run=new SimulationRun("selected",DateTimeOffset.UtcNow,"running",config,10);
            await store.SaveRun(run);
            foreach(var id in config.DeckIds)await store.SaveDeck(new(id,"Saved Deck "+id,"personal","Saved","source","snapshot",null,"hash","hash",run.CreatedAt,[],"Exact evidence"));
            GameResult Result()=>new("A",false,"test",10,[],10,[]);
            var slots=new List<GameSlot>{
                new("win",run.Id,"A","B","A",1,0,"completed",[],Result()),
                new("cached",run.Id,"A","B","B",2,1,"completed",[],Result(),"source"),
                new("failed",run.Id,"A","B","A",3,2,"failed",[]),
                new("paused",run.Id,"A","B","B",4,3,"cancelled",[]),
                new("running",run.Id,"A","B","A",5,4,"running",[]),
                new("pending",run.Id,"A","B","B",6,5,"pending",[]),
            };
            foreach(var slot in slots)await store.SaveSlot(slot);
            var timing=new SessionTiming("timing",run.Id,run.CreatedAt,run.CreatedAt,run.CreatedAt,120,4,100,"running");
            await store.SaveSessionTiming(timing);
            var expectedProgress=store.SessionProgress(run);
            // These records deliberately cannot be deserialized. A session request
            // must not scan other runs, decks, cache keys or global timing evidence.
            using(var db=new SqliteConnection($"Data Source={Path.Combine(paths.Data,"simulator.sqlite")}")){
                db.Open();using var cmd=db.CreateCommand();
                cmd.CommandText="INSERT INTO runs VALUES('unrelated','invalid'); INSERT INTO decks(id,payload) VALUES('unrelated','invalid'); INSERT INTO slots VALUES('unrelated','unrelated','invalid');";
                cmd.ExecuteNonQuery();
            }
            var backend=new LocalUiBackend(store,new ForgeEngine(paths,store),null!,new DeckImporter(paths,store),new ResourceCapacity(),null!);
            var state=await backend.Snapshot(run.Id,CancellationToken.None);
            Check(Json.Encode(state.Runs.Single())==Json.Encode(run)&&state.Decks.Select(d=>d.Manifest.Id).SequenceEqual(config.DeckIds),"A focused snapshot must preserve the selected session and its exact deck identities.");
            Check(Json.Encode(state.Slots)==Json.Encode(slots)&&state.Timings[run.Id].Single()==timing,"All selected slots, results, attempts and active timing must be retained.");
            Check(Json.Encode(state.History.Single())==Json.Encode(expectedProgress)&&state.ActiveSessions.Single().Running==1,"Focused counters must match stored progress, including reuse and unfinished games.");
            Check(state.Caches.Count==0&&state.TimingSamples.Count==0&&state.SessionSamples.Count==0&&state.ManaValues.Count==0&&state.TurnStats is null,"Results must omit unrelated setup and calibration data.");
            Check(state.Decks.All(d=>d.Manifest.FriendlyName is not null),"Targeted deck reads must keep library annotations.");
            Check(state.ETag()==(await backend.Snapshot(run.Id,CancellationToken.None)).ETag(),"Unchanged snapshots must have a stable validator.");
            await store.SaveSlot(slots[5] with{Status="completed",Result=Result()});
            var updated=await backend.Snapshot(run.Id,CancellationToken.None);
            Check(updated.ETag()!=state.ETag()&&updated.History.Single().Completed==3,"A newly completed game must invalidate the snapshot and update counters.");
            await store.SaveRun(run with{Status="completed"});
            Check((await backend.Snapshot(run.Id,CancellationToken.None)).ActiveSessions.Count==0,"Terminal sessions must stop reporting active progress.");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            try{await backend.Snapshot(run.Id,cancelled.Token);throw new Exception("Cancelled session snapshot did not stop.");}catch(OperationCanceledException){}
            try{await backend.Snapshot("missing",CancellationToken.None);throw new Exception("Missing session did not fail.");}catch(KeyNotFoundException){}
            Console.WriteLine("Session snapshot checks passed: isolated reads, exact records, mixed progress, timing, validators and cancellation.");
        }finally{
            Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",previous);
            SqliteConnection.ClearAllPools();Directory.Delete(root,true);
        }
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class TestEnvironment:IHostEnvironment{
        public string EnvironmentName{get;set;}="Test";
        public string ApplicationName{get;set;}="SessionSnapshotChecks";
        public string ContentRootPath{get;set;}="";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
}
