using System.Diagnostics;
using System.Text.Json;
using DeckTester;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var originalRoot=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
var originalJava=Environment.GetEnvironmentVariable("MTG_JAVA");
var root=Path.Combine(Path.GetTempPath(),"forge-worker-checks-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
var fake=Path.Combine(root,"fake-forge.py");File.Copy(Path.Combine(AppContext.BaseDirectory,"fake-forge.py"),fake);
if(!OperatingSystem.IsWindows())File.SetUnixFileMode(fake,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",root);Environment.SetEnvironmentVariable("MTG_JAVA",fake);
var engineHash=new string('a',64);var adapterHash=new string('b',64);var legacyHash=new string('c',64);
string MakeBundle(string adapter,int? protocol){
    var path=Path.Combine(root,"data","engine-bundles",engineHash+"-"+adapter);
    Directory.CreateDirectory(Path.Combine(path,"res"));Directory.CreateDirectory(Path.Combine(path,"bridge"));
    File.WriteAllText(Path.Combine(path,"bridge","ForgeBridge.class"),"");File.WriteAllText(Path.Combine(path,"forge-gui-desktop-2.0.15-jar-with-dependencies.jar"),"");
    var manifest=new Dictionary<string,object>{{"engineHash",engineHash},{"adapterHash",adapter},{"jarName","forge-gui-desktop-2.0.15-jar-with-dependencies.jar"}};
    if(protocol is not null)manifest["workerProtocolVersion"]=protocol;
    File.WriteAllText(Path.Combine(path,"bundle.json"),Json.Encode(manifest));return path;
}
var bundle=MakeBundle(adapterHash,1);var legacy=MakeBundle(legacyHash,null);
var paths=new AppPaths(new TestEnvironment());var store=new Store(paths);
foreach(var id in new[]{"A","B"})await store.SaveDeck(new(id,id,"fixture","fixture",id,"frozen",null,"hash","hash",DateTimeOffset.UtcNow,[new(1,"Plains",60,"MainDeck","TST","Land","","",true,"")],"fixture"));
await using var engine=new ForgeEngine(paths,store);
var config=new RunConfig(["A","B"],2,1,2,150,42,true,engineHash,adapterHash,Pilot:ReanimatorPilots.Default);
var run=new SimulationRun("reuse",DateTimeOffset.UtcNow,"running",config,2);
var index=0;
async Task<GameResult> Play(SimulationRun target,long seed=1,CancellationToken cancellation=default){
    var slot=new GameSlot("test-"+index++,target.Id,"A","B","A",seed,0,"pending",[]);
    var result=await engine.Play(target,slot,1,cancellation);
    Check(File.Exists(Path.Combine(paths.Replays,result.Replay))&&ForgeEngine.FileHash(Path.Combine(paths.Replays,result.Replay))==result.Hash,"Valid replay and checksum must be retained.");
    Check(result.Result.Winner=="A","Transport must preserve the terminal result.");return result.Result;
}
List<int> Pids(string folder)=>File.ReadAllLines(Path.Combine(folder,"requests.log")).Select(line=>JsonDocument.Parse(line).RootElement.GetProperty("pid").GetInt32()).ToList();
async Task Fault(long seed,Type expected,CancellationToken cancellation=default){
    try{await Play(run,seed,cancellation);throw new Exception("Fault was accepted.");}
    catch(Exception error)when(expected.IsInstanceOfType(error)){}
    await Play(run);
    var ids=Pids(bundle);Check(ids[^1]!=ids[^2],"A failed or cancelled worker must be replaced.");
}
try {
    using(var completed=new CancellationTokenSource()){
        await Play(run,cancellation:completed.Token);completed.Cancel();await Play(run);
    }
    Check(Pids(bundle).Distinct().Count()==1,"Successful games reuse the same process; late cancellation must not kill the next lease.");
    foreach(var seed in new long[]{2,3,4,7})await Fault(seed,seed is 3 or 4?typeof(InvalidDataException):typeof(InvalidOperationException));
    await Fault(5,typeof(TimeoutException));
    using(var cancelled=new CancellationTokenSource(200))await Fault(6,typeof(OperationCanceledException),cancelled.Token);
    await Play(run,8);await Play(run);Check(Pids(bundle)[^1]!=Pids(bundle)[^2],"High-memory workers must be retired.");
    await engine.ReleaseWorkers(run.Id);
    var recycle=run with{Id="recycle"};var offset=Pids(bundle).Count;
    for(var i=0;i<33;i++)await Play(recycle);
    var recycled=Pids(bundle).Skip(offset).ToList();
    Check(recycled.Take(32).Distinct().Count()==1&&recycled[32]!=recycled[31],"Recycle after 32 complete games.");
    await engine.ReleaseWorkers(recycle.Id);
    var concurrent=run with{Id="parallel",Config=config with{Workers=4}};offset=Pids(bundle).Count;
    await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Play(concurrent,9)));
    var concurrentPids=Pids(bundle).Skip(offset).Distinct().ToList();Check(concurrentPids.Count==4,"Parallel requests must honor the four-worker capacity.");
    await engine.ReleaseWorkers(concurrent.Id);
    foreach(var pid in concurrentPids){try{using var process=Process.GetProcessById(pid);Check(process.HasExited,"Released workers must exit.");}catch(ArgumentException){}}
    var old=run with{Id="legacy",Config=config with{AdapterHash=legacyHash}};
    await Play(old);await Play(old);Check(Pids(legacy).Distinct().Count()==2,"Historical bundles retain fresh-process execution.");
    Console.WriteLine("Forge worker lifecycle checks passed: reuse, cancellation, faults, timeout, recycling, parallel capacity, shutdown and legacy fallback.");
} finally {
    await engine.DisposeAsync();Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",originalRoot);Environment.SetEnvironmentVariable("MTG_JAVA",originalJava);
    Directory.Delete(root,true);
}
sealed class TestEnvironment:IHostEnvironment {
    public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="Test";
    public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
