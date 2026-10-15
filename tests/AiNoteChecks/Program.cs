using DeckTester;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){return;}throw new Exception("Expected rejection.");}
var root=Path.Combine(Path.GetTempPath(),"crucible-notes-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
var previous=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",root);
try{
    var paths=new AppPaths(new TestEnvironment{ContentRootPath=root});
    using(var db=new SqliteConnection("Data Source="+Path.Combine(paths.Data,"simulator.sqlite"))){
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TABLE decks(id TEXT PRIMARY KEY);CREATE TABLE runs(id TEXT PRIMARY KEY);INSERT INTO decks VALUES('exact-list');INSERT INTO runs VALUES('exact-session');";cmd.ExecuteNonQuery();
    }
    var store=new AiNoteStore(paths);
    var original=new AiNoteRecord("first","deck","exact-list","Summary","<script>plain text</script>","Test Author","Test Model",DateTimeOffset.UtcNow.AddMinutes(-2),"Exact manifest inspected","manifest-1",ExactModelId:"test-model-id");
    store.Append(original);store.Append(original);
    Check(store.Read("deck","exact-list").Single()==original,"Round-trip signature, text, evidence, and exact model; retry is idempotent.");
    Reject(()=>store.Append(original with{Body="Attempt to rewrite history"}));
    var revision=original with{Id="second",WrittenAt=DateTimeOffset.UtcNow.AddMinutes(-1),Body="Revised reasoning",Model="Another Model"};store.Append(revision);
    Check(store.Read("deck","exact-list").SequenceEqual(new[]{revision,original}),"New revisions retain earlier authors and sort newest first.");
    Check(store.Read("deck","another-list").Count==0,"No cross-version leakage.");
    Reject(()=>store.Append(original with{Id="bad-model",Model=""}));
    Reject(()=>store.Append(original with{Id="bad-date",WrittenAt=default}));
    Reject(()=>store.Append(original with{Id="missing-deck",Subject="missing"}));
    Reject(()=>store.Append(original with{Id="bad-run",Scope="run",RunId="exact-session"}));
    var runNote=original with{Id="run-note",Scope="run",Subject="exact-session",RunId="exact-session"};store.Append(runNote);
    Directory.CreateDirectory(Path.Combine(root,"src","DeckTester","notes"));var seed=Path.Combine(root,"src","DeckTester","notes","editorial.json");
    File.WriteAllText(seed,Json.Encode(new[]{original,revision,original with{Id="absent",Subject="absent-list"}}));
    var reopened=new AiNoteStore(paths);
    Check(reopened.Read("deck","exact-list").Count==2,"Seed import is repeatable and does not duplicate or overwrite notes.");
    File.WriteAllText(seed,Json.Encode(new[]{original with{Id="rollback"},original with{Body="Conflicting seed"}}));
    Reject(()=>{_=new AiNoteStore(paths);});
    Check(reopened.Read("deck","exact-list").Count==2,"A failed seed batch rolls back all inserted notes.");
    using(var db=new SqliteConnection("Data Source="+Path.Combine(paths.Data,"simulator.sqlite"))){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA foreign_keys=ON;DELETE FROM runs WHERE id='exact-session'";cmd.ExecuteNonQuery();}
    Check(store.Read("run","exact-session").Count==0&&store.Read("deck","exact-list").Count==2,"Session deletion cascades only its attached notes.");
    Console.WriteLine("AI note checks passed: signed persistence, immutable revisions, isolation, validation, import rollback, and session cleanup.");
}finally{Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",previous);SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
sealed class TestEnvironment:IHostEnvironment
{
    public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="AiNoteChecks";public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
}
