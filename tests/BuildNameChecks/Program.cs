using DeckTester;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
var directory=Path.Combine(Path.GetTempPath(),"crucible-build-names-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try{
    var path=Path.Combine(directory,"names.json");
    var names=new BuildNames(path);
    var engine=new string('a',64);
    var other=engine[..63]+"b";
    var engineName=names.Engine(engine);
    var bridgeName=names.Bridge(engine);
    Check(engineName.All(char.IsLetter)&&NameLibrary.Words.Contains(engineName,StringComparer.OrdinalIgnoreCase),"Build labels must be single words from the shared name library.");
    Check(engineName!=bridgeName,"Engine and bridge builds receive distinct names even when their hashes match.");
    Check(names.Engine(other)!=engineName,"Different full hashes must remain distinct even when their short identifiers match.");
    Check(names.Engine(engine.ToUpperInvariant())==engineName,"Hexadecimal letter casing must not allocate another alias.");
    var restarted=new BuildNames(path);
    Check(restarted.Engine(engine)==engineName&&restarted.Bridge(engine)==bridgeName,"Build names must survive UI restarts.");
    Check(new BuildNames(path).Engine(other)==names.Engine(other),"Independent readers must preserve assignments.");
    var hashes=Enumerable.Range(0,200).Select(i=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("build-"+i)))).ToList();
    foreach(var hash in hashes.Take(20))restarted.Bridge(hash);
    Check(restarted.Engine(engine)==engineName,"New bridge builds must not rename existing engines.");
    var saved=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))!;
    Check(saved.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count()==saved.Count,"Saved build aliases must be unique.");
    Check(saved.Keys.Contains("engine:"+engine)&&saved.Keys.Contains("bridge:"+engine),"Assignments must retain full build hashes and their roles.");
    var firstProcess=new BuildNames(path);var secondProcess=new BuildNames(path);
    Parallel.ForEach(hashes.Skip(20).Take(12),(hash)=>{var source=hash[0]<'8'?firstProcess:secondProcess;source.Bridge(hash);});
    foreach(var hash in hashes.Skip(20).Take(12))firstProcess.Bridge(hash);
    saved=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))!;
    Check(saved.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count()==saved.Count,"Concurrent registry users must not assign duplicate names.");
    Check(names.Engine("")=="Unknown"&&names.Bridge("UNKNOWN")=="Unknown","Unrecorded builds must not receive a fabricated name.");
    var corrupt=Path.Combine(directory,"corrupt.json");File.WriteAllText(corrupt,"null");
    Check(new BuildNames(corrupt).Engine(engine)==RunBuildIdentity.Short(engine)&&File.ReadAllText(corrupt)=="null","A corrupt registry must preserve evidence and fall back to the build identifier.");
    var full=Path.Combine(directory,"full.json");File.WriteAllText(full,JsonSerializer.Serialize(NameLibrary.Words.Select((word,index)=>(Key:"bridge:old-"+index,Name:word)).ToDictionary(x=>x.Key,x=>x.Name)));
    Check(new BuildNames(full).Engine(engine)==RunBuildIdentity.Short(engine),"Exhaustion must preserve an unambiguous build identifier rather than reuse a name.");
    Console.WriteLine("Build name checks passed: library words, unique full identities, persistence, concurrent assignment, missing builds, and safe fallback.");
}finally{Directory.Delete(directory,true);}
