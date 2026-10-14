using DeckTester;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

static class HistoryChecks
{
    public static async Task Run(){
        var root=Path.Combine(Path.GetTempPath(),"crucible-history-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
        var previous=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
        Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",root);
        try{
            var store=new Store(new AppPaths(new TestEnvironment{ContentRootPath=root}));
            var config=new RunConfig(["A","B","C"],10,2,180,150,42,true,"engine","adapter");
            var run=new SimulationRun("mixed",DateTimeOffset.UtcNow,"running",config,30);
            await store.SaveRun(run);
            await store.SaveRun(run with{Id="empty",CreatedAt=run.CreatedAt.AddSeconds(1),Status="queued"});
            GameResult Result(string? winner,bool draw=false)=>new(winner,draw,"test",10,[],10,[]);
            var slots=new List<GameSlot>{
                new("win-a",run.Id,"A","B","A",1,0,"completed",[],Result("A")),
                new("win-b",run.Id,"A","B","B",2,1,"completed",[],Result("B"),"earlier"),
                new("draw",run.Id,"A","B","A",3,2,"completed",[],Result(null,true)),
                new("no-result",run.Id,"A","B","B",4,3,"completed",[]),
                new("failed",run.Id,"A","B","A",5,4,"failed",[]),
                new("paused",run.Id,"A","B","B",6,5,"cancelled",[]),
                new("running",run.Id,"A","C","A",7,0,"running",[]),
                new("pending",run.Id,"A","C","C",8,1,"pending",[]),
                new("second-pair",run.Id,"A","C","A",9,2,"completed",[],Result("A")),
            };
            foreach(var slot in slots)await store.SaveSlot(slot);
            var timing=new SessionTiming("timing",run.Id,run.CreatedAt,run.CreatedAt,run.CreatedAt,120,4,100,"completed");
            await store.SaveSessionTiming(timing);
            var history=store.RunHistory();
            Check(history[0].Progress.Run.Id=="empty"&&history[0].Progress.Completed==0&&history[0].Matchups.Count==0,"Empty sessions must remain present with zero counts.");
            var item=history.Single(s=>s.Progress.Run.Id==run.Id);
            Check(Json.Encode(item.Progress)==Json.Encode(store.SessionProgress(run)),"History status and reused counts must match existing session progress.");
            Check(item.Timings.Single()==timing,"History must preserve measured active runtime.");
            var expected=Metrics.Summarize(run,slots);
            foreach(var pair in expected.Matchups){
                var actual=item.Matchups.Single(m=>m.DeckA==pair.DeckA&&m.DeckB==pair.DeckB);
                Check(actual.Valid==pair.Valid&&actual.WinsA==pair.WinsA&&actual.WinsB==pair.WinsB&&actual.Draws==pair.Draws&&actual.WinRateA==pair.WinRateA&&actual.WinRateB==pair.WinRateB,"SQL history results must preserve draws, missing results and per-opponent denominators.");
            }
            await store.SaveSlot(slots[7] with{Status="completed",Result=Result("C")});
            Check(store.RunHistory().Single(s=>s.Progress.Run.Id==run.Id).Progress.Completed==6,"A completed game must appear on the next history refresh.");
            await store.SaveRun(run with{Id="empty",CreatedAt=run.CreatedAt.AddSeconds(1),Status="cancelled"});
            await store.DeleteRun("empty");
            Check(store.RunHistory().Count==1,"Deleted sessions must disappear from history.");
            Console.WriteLine("History checks passed: mixed outcomes, partial matchups, reuse, empty sessions, runtime, completion and deletion.");
        }finally{
            Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",previous);
            SqliteConnection.ClearAllPools();Directory.Delete(root,true);
        }
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private sealed class TestEnvironment:IHostEnvironment
    {
        public string EnvironmentName{get;set;}="Test";
        public string ApplicationName{get;set;}="HistoryChecks";
        public string ContentRootPath{get;set;}="";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
}
