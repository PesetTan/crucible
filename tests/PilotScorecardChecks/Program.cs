using DeckTester;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
// Synthetic evidence makes this test independent of private evaluations and of
// whichever source revision or production bundle happens to be active locally.
var id="synthetic-scorecard-fixture";
var temporary=Path.Combine(Path.GetTempPath(),"pilot-scorecards-"+Guid.NewGuid().ToString("N"));
try{
    Directory.CreateDirectory(Path.Combine(temporary,"docs/specs"));
    var paths=new AppPaths(new FixtureEnvironment{ContentRootPath=Path.Combine(temporary,"src/DeckTester")});
    Check(new PilotEvaluationStore(paths).Read().Report is null,"Missing evaluation stays unknown.");
    var destination=Path.Combine(paths.Data,"pilot-evaluations",id);Directory.CreateDirectory(destination);
    var sourceFile="tests/PilotAttributeChecks.java";
    var frozenSource=Path.Combine(destination,"sources",sourceFile);Directory.CreateDirectory(Path.GetDirectoryName(frozenSource)!);
    File.WriteAllText(frozenSource,"// Synthetic evaluated source\nvoid declareAttackers() {}\n");
    var currentSource=Path.Combine(temporary,sourceFile);Directory.CreateDirectory(Path.GetDirectoryName(currentSource)!);File.Copy(frozenSource,currentSource);
    foreach(var file in new[]{"positions.json","registry.json","execution.log"})File.WriteAllText(Path.Combine(destination,file),"synthetic fixture evidence: "+file);
    var engineHash=new string('a',64);var adapterHash=new string('b',64);
    var diagnostic=new PilotEvaluationCase("fixture","combat","unblocked","Synthetic","engine position","attack","attack","passed");
    var dimension=new PilotDimension("combat","Combat","Synthetic fixture",true,"engine position",1,1,0,100,["Synthetic"],[diagnostic]);
    var report=new PilotEvaluationReport(1,id,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,engineHash,adapterHash,
        engineHash+"-"+adapterHash,1,"fixture",new(),new(){{sourceFile,Hash(frozenSource)}},
        Hash(Path.Combine(destination,"registry.json")),Hash(Path.Combine(destination,"positions.json")),Hash(Path.Combine(destination,"execution.log")),
        "Synthetic store validation; no gameplay or pilot quality claim.",[new("fixture",[dimension])]);
    var reportPath=Path.Combine(destination,"scorecard.json");File.WriteAllText(reportPath,Json.Encode(report));
    var latest=new JsonObject{["directory"]=id,["sha256"]=Hash(reportPath)};
    var pointer=Path.Combine(paths.Data,"pilot-evaluations/latest.json");File.WriteAllText(pointer,latest.ToJsonString());
    Directory.CreateDirectory(Path.Combine(paths.Data,"engine-bundles"));var active=Path.Combine(paths.Data,"engine-bundles/active.json");
    File.WriteAllText(active,Json.Encode(new{Id=report.BundleId}));
    var store=new PilotEvaluationStore(paths);var measured=store.Read();
    Check(measured.Report?.Profiles.Count==report.Profiles.Count&&measured.Current&&measured.Error is null,"Report and exact active build validate.");
    var source=store.ReadSource(report,sourceFile);
    Check(source.Text.Contains("declareAttackers")&&source.Hash==report.SourceHashes[sourceFile],"Code view reads the exact evaluated source.");
    File.AppendAllText(Path.Combine(temporary,sourceFile),"\n// changed current source\n");
    Check(!store.Read().Current&&store.Read().Report is not null,"Modified suite stays visible as historical.");
    Check(store.ReadSource(report,sourceFile).Text==source.Text,"Current edits do not change evaluated code.");
    File.WriteAllText(currentSource,source.Text);Check(store.Read().Current,"Restored exact source validates again.");
    File.WriteAllText(active,Json.Encode(new{Id="different-build"}));Check(!store.Read().Current,"Changed build cannot reuse ratings as current.");
    File.WriteAllText(active,Json.Encode(new{Id=report.BundleId}));Check(store.Read().Current,"Restored exact build validates again.");
    try{store.ReadSource(report,"../../outside");throw new Exception("Traversal was accepted.");}catch(ArgumentException){}
    File.AppendAllText(Path.Combine(destination,"sources",sourceFile),"corruption");
    try{store.ReadSource(report,sourceFile);throw new Exception("Source corruption was accepted.");}catch(InvalidDataException){}
    var node=JsonNode.Parse(File.ReadAllText(Path.Combine(destination,"scorecard.json")))!;
    node["profiles"]![0]!["dimensions"]![0]!["total"]=999;
    File.WriteAllText(reportPath,node.ToJsonString());
    latest["sha256"]=Hash(reportPath);File.WriteAllText(pointer,latest.ToJsonString());
    Check(store.Read().Report is null&&store.Read().Error!.Contains("denominator"),"Inconsistent score denominators are rejected.");
    latest["sha256"]="wrong";File.WriteAllText(pointer,latest.ToJsonString());
    Check(store.Read().Report is null&&store.Read().Error!.Contains("checksum"),"Bad report checksum cannot publish a score.");
    latest["directory"]="../outside";File.WriteAllText(pointer,latest.ToJsonString());
    Check(store.Read().Report is null&&store.Read().Error is not null,"Invalid report path is rejected.");
    Console.WriteLine("Pilot scorecard checks passed: missing data, build/suite isolation, immutable code, checksums, denominators, and path guards.");
}finally{Directory.Delete(temporary,true);}

sealed class FixtureEnvironment:IHostEnvironment
{
    public string EnvironmentName{get;set;}="Test";
    public string ApplicationName{get;set;}="Checks";
    public string ContentRootPath{get;set;}="";
    public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
