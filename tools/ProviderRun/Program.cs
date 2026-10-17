using DeckTester;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;

if(args.Length!=4||args[0]!="--root"||args[2]!="--input")throw new ArgumentException("Use --root OWNED_ROOT --input REQUEST_FILE.");
var paths=new AppPaths(args[1]);var store=new Store(paths);using var input=JsonDocument.Parse(File.ReadAllText(args[3]));var root=input.RootElement;
var request=Json.Decode<RunRequest>(root.GetProperty("request").GetRawText());
var key=root.GetProperty("key").GetString()!;
if(request.ProviderId=="pyre"&&request.DeckIds.Count==0){
    var installation=Json.Decode<PyreInstallation>(File.ReadAllText(Path.Combine(paths.Data,"pyre-provider.json")));
    var info=new ProcessStartInfo(installation.Executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var arg in new[]{"preflight","--manifest",installation.Cohorts[request.PyreCohort!]})info.ArgumentList.Add(arg);
    using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
    if(process.ExitCode!=0)throw new InvalidDataException(await errors);
    using var resolved=JsonDocument.Parse(await output);var lists=resolved.RootElement.GetProperty("draft").GetProperty("decks");var ids=new List<string>();
    for(var n=0;n<2;n++){
        var list=lists[n];var id="pyre-deck-"+PyreRecord.SemanticHash("spark-deck-v1",PyreRecord.Canonical(list));ids.Add(id);
        var cards=list.EnumerateArray().Select(c=>new CardEntry(0,c.GetProperty("name").GetString()!,c.GetProperty("count").GetInt32(),"MainDeck","","","","",true,"")).ToList();
        await store.SaveDeck(new(id,$"{request.PyreCohort} {(n==0?"A":"B")}","pyre-frozen","frozen",id,resolved.RootElement.GetProperty("legality_as_of").GetString()!,null,id,id,DateTimeOffset.UnixEpoch,cards,"Exact dated Pyre cohort; metadata not supplied by this bootstrap."));
    }
    request=request with{DeckIds=ids};
}
await using var forge=new ForgeEngine(paths,store);await using var pyre=new PyreEngine(paths,store);var providers=new EngineProviders(forge,pyre);
using var coordinator=new RunCoordinator(store,forge,NullLogger<RunCoordinator>.Instance,paths,providers:providers);
await coordinator.StartAsync(CancellationToken.None);
while(!coordinator.IsReady)await Task.Delay(10);
var clock=Stopwatch.StartNew();var idRun=await coordinator.Create(request,key);
while(store.Run(idRun).Status is "queued" or "running" or "cancelling")await Task.Delay(20);
await coordinator.StopAsync(CancellationToken.None);
var run=store.Run(idRun);var slots=store.Slots(idRun);var summary=Metrics.Summarize(run,slots);
Console.WriteLine(Json.Encode(new{runId=idRun,status=run.Status,summary.Valid,summary.Failed,summary.Cancelled,seconds=clock.Elapsed.TotalSeconds,config=run.Config,slots,resources=pyre.Resources(idRun)}));
if(run.Status!="completed")Environment.ExitCode=1;
