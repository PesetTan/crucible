using DeckTester;
using Microsoft.Data.Sqlite;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static async Task Reject(Func<Task> action){try{await action();}catch(InvalidDataException){return;}throw new Exception("Expected rejected catalog.");}
static DeckManifest Deck(string name,string card){
    List<CardEntry> cards=[new(1,card,60,"MainDeck","TST","Basic Land","","",true,"")];
    var hash=DeckImporter.Hash(Json.Encode(new DeckFingerprint(name,"public",name,"2000-01-01",null,cards)));
    return new(hash,name,"public","Synthetic fixture",name,"2000-01-01",null,DeckImporter.Hash(Json.EncodeRelaxed(new List<object[]>{new object[]{card,60}})),hash,DateTimeOffset.UnixEpoch,cards,"Synthetic fixture only");
}
var root=Path.Combine(Path.GetTempPath(),"crucible-support-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"docs/specs"));
try{
    var store=new Store(new AppPaths(root));var a=Deck("First","Test Basic");var b=Deck("Second","Other Basic");
    await store.SaveDeck(a);var before=Json.Encode(store.Deck(a.Id));
    var historical=new SimulationRun("historical",DateTimeOffset.UnixEpoch,"completed",new([a.Id],2,1,180,150,42,true,"old-engine","old-adapter"),0);
    await store.SaveRun(historical);var historicalPayload=Json.Encode(store.Run(historical.Id));
    var acceptedA=Deck("Accepted source for first list","Test Basic");var anotherA=Deck("Another capture of first list","Test Basic");
    EngineSupportPack pack=new(1,"fixture-engine","1.0",new('a',64),new('b',64),new('c',64),"test-target",
        [new("Test Basic","Card",1),new("Other Basic","Card",2),new("Test Token","Token",null)],
        [new("pair","Synthetic matchup",acceptedA.Id,b.Id,"2000-01-01"),new("second-capture","Second capture",anotherA.Id,b.Id,"2000-01-01")],[acceptedA,anotherA,b]);
    await Reject(async()=>{await store.ImportEngineSupport(pack with{Matchups=[pack.Matchups[0] with{DeckB="missing"}]});});
    Check(store.Decks().Count==1&&store.EngineSupport().Count==0,"Invalid later reference must not import any decks or catalogs.");
    await Reject(async()=>{await store.ImportEngineSupport(pack with{Decks=[a,b with{ListHash="bad"}]});});
    await Reject(async()=>{await store.ImportEngineSupport(pack with{Cards=[null!]});});
    await Reject(async()=>{await store.ImportEngineSupport(pack with{Matchups=[null!]});});
    var first=await store.ImportEngineSupport(pack);Check(first.AddedDecks==1&&!first.Duplicate,"Reuse existing exact lists and deduplicate source captures within the same pack.");
    Check(store.Decks().Count==2&&store.Decks().All(d=>d.Id!=acceptedA.Id&&d.Id!=anotherA.Id),"Source-specific IDs belong in evidence, not duplicate library rows.");
    Check(store.EngineSupport().Single().Decks.Count==3,"Every acceptance source version remains in support evidence.");
    Check(Json.Encode(store.Deck(a.Id))==before,"Existing payload, friendly name and annotations remain unchanged.");
    var second=await store.ImportEngineSupport(pack);Check(second.Duplicate&&second.AddedDecks==0&&store.Decks().Count==2,"Repeat import is idempotent.");
    await Reject(async()=>{await store.ImportEngineSupport(pack with{RuntimeSha256=new('d',64)});});
    Check(store.EngineSupport().Single().RuntimeSha256==pack.RuntimeSha256,"Conflicting immutable support identity cannot replace evidence.");
    await Reject(async()=>{await store.ImportEngineSupport(pack with{Cards=[new("Test Basic","Card",1),new("Unknown","Card",9)]});});
    Check(store.Runs().Count==1&&Json.Encode(store.Run(historical.Id))==historicalPayload,"Catalog import preserves historical sessions and creates no new ones.");
    // A database failure after inserting a new deck must roll that deck back too.
    using(var db=new SqliteConnection("Data Source="+Path.Combine(root,"data/simulator.sqlite"))){
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TRIGGER fail_support BEFORE INSERT ON engine_support_cards BEGIN SELECT RAISE(ABORT,'injected failure'); END";cmd.ExecuteNonQuery();
    }
    var third=Deck("Third","New Basic");
    try{await store.ImportEngineSupport(pack with{EngineVersion="2.0",Cards=[..pack.Cards,new("New Basic","Card",3)],Decks=[a,third],Matchups=[new("new","New synthetic matchup",a.Id,third.Id,"2000-01-01")]});throw new Exception("Expected injected SQLite failure.");}catch(SqliteException){}
    Check(store.Decks().Count==2&&store.EngineSupport().Count==1,"SQL failure rolls back new deck rows and support evidence together.");
    Console.WriteLine("Engine support checks passed: complete validation, atomic import/rollback, preserved history, idempotency and immutable evidence.");
}finally{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
