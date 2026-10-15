using DeckTester;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

static class UiReadCacheChecks
{
    public static async Task Run(){
        var temporary=Path.Combine(Path.GetTempPath(),"crucible-read-cache-"+Guid.NewGuid().ToString("N"));
        var root=Path.Combine(temporary,"projects","crucible");
        Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
        var previous=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
        Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",root);
        try{
            var paths=new AppPaths(new TestEnvironment{ContentRootPath=root});var store=new Store(paths);
            using(var source=new SqliteConnection($"Data Source={paths.Source}")){
                source.Open();using var command=source.CreateCommand();command.CommandText="CREATE TABLE cards(value INTEGER); INSERT INTO cards VALUES(1)";command.ExecuteNonQuery();
            }
            using var cache=new UiReadCache(paths);
            var builds=0;
            Task<string> Build(CancellationToken token){token.ThrowIfCancellationRequested();builds++;return Task.FromResult("version-"+builds);}
            var first=await cache.Read("setup",Build);
            Check(ReferenceEquals(first,await cache.Read("setup",Build))&&builds==1,"Idle refreshes must reuse the prepared object and validator without rebuilding or serializing it.");
            var config=new RunConfig(["A","B"],2,1,180,150,42,true,"engine","adapter");
            var run=new SimulationRun("run",DateTimeOffset.UtcNow,"running",config,2);
            await store.SaveRun(run);
            var changed=await cache.Read("setup",Build);
            Check(changed.Tag!=first.Tag&&builds==2,"A Store commit must invalidate the shared read immediately.");
            using(var writer=new SqliteConnection($"Data Source={Path.Combine(paths.Data,"simulator.sqlite")}")){
                writer.Open();using var command=writer.CreateCommand();command.CommandText="UPDATE runs SET payload=payload || ' ' WHERE id='run'";command.ExecuteNonQuery();
            }
            await cache.Read("setup",Build);
            Check(builds==3,"External database writes must also invalidate reads.");
            using(var source=new SqliteConnection($"Data Source={paths.Source}")){
                source.Open();using var command=source.CreateCommand();command.CommandText="UPDATE cards SET value=2";command.ExecuteNonQuery();
            }
            await cache.Read("setup",Build);
            Check(builds==4,"Card catalog commits must invalidate capability and library reads.");
            var bundleFolder=Path.Combine(paths.Data,"engine-bundles");Directory.CreateDirectory(bundleFolder);
            await File.WriteAllTextAsync(Path.Combine(bundleFolder,"active.json"),"new engine");
            await cache.Read("setup",Build);
            Check(builds==5,"Engine activation must invalidate reads without a database write.");
            var concurrentBuilds=0;
            var concurrent=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>cache.Read("session:one",async token=>{
                concurrentBuilds++;await Task.Delay(10,token);return "same selected session";
            })));
            Check(concurrentBuilds==1&&concurrent.All(value=>ReferenceEquals(value,concurrent[0])),"Concurrent UI circuits must share one build rather than repeat heavy queries.");
            Check((await cache.Read("session:two",_=>Task.FromResult("other session"))).Value=="other session","Selected sessions must retain separate cache keys.");
            var racingBuilds=0;
            async Task<string> Racing(CancellationToken token){
                racingBuilds++;
                if(racingBuilds==1)await store.SaveRun(run with{Status="completed"});
                return "racing-"+racingBuilds;
            }
            await cache.Read("racing",Racing);await cache.Read("racing",Racing);
            Check(racingBuilds==2,"A write during assembly must prevent caching a mixed revision as current.");
            await cache.Read("racing",Racing);
            Check(racingBuilds==2,"A stable replacement read should be retained.");
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            try{await cache.Read("setup",Build,cancellation.Token);throw new Exception("Cancelled cache read continued.");}catch(OperationCanceledException){}
            for(var index=0;index<20;index++)await cache.Read("bounded:"+index,_=>Task.FromResult(index));
            var rebuilt=false;
            await cache.Read("session:one",_=>{rebuilt=true;return Task.FromResult("rebuilt");});
            Check(rebuilt,"Prepared session reads must have bounded retention.");
            Console.WriteLine("UI read cache checks passed: idle reuse, concurrent readers, Store/external/catalog/engine invalidation, racing writes, cancellation and bounded retention.");
        }finally{
            Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",previous);
            SqliteConnection.ClearAllPools();Directory.Delete(temporary,true);
        }
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class TestEnvironment:IHostEnvironment{
        public string EnvironmentName{get;set;}="Test";
        public string ApplicationName{get;set;}="UiReadCacheChecks";
        public string ContentRootPath{get;set;}="";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
}
