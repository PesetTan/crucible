using DeckTester;
using Microsoft.Data.Sqlite;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
static async Task Reject(Func<Task> action){try{await action();}catch(Exception e)when(e is InvalidDataException or FileNotFoundException or System.Text.Json.JsonException){return;}throw new Exception("Expected invalid pack rejection.");}
static DeckManifest Fixture(string name,string sourceId){
    List<CardEntry> cards=[new(1,"Test Basic",60,"MainDeck","TST","Basic Land","","",true,"")];
    var list=DeckImporter.Hash(Json.EncodeRelaxed(new List<object[]>{new object[]{"Test Basic",60}}));
    var hash=DeckImporter.Hash(Json.Encode(new DeckFingerprint(name,"public",sourceId,"fixture-v1",null,cards)));
    return new(hash,name,"public","Test source",sourceId,"fixture-v1",null,list,hash,DateTimeOffset.UtcNow,cards,"Synthetic fixture"){
        ArchetypeTier="A",TierSourceUrl="https://fixture.invalid/rating",TierSnapshotDate="2000-01-01",PublishedWinRatePercent=50,PublishedMatchesApprox=100};
}
var root=Path.Combine(Path.GetTempPath(),"crucible-pack-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
var previous=Environment.GetEnvironmentVariable("CRUCIBLE_DECK_PACK");
var previousNotes=Environment.GetEnvironmentVariable("CRUCIBLE_NOTES_FILE");
try{
    Environment.SetEnvironmentVariable("CRUCIBLE_DECK_PACK",null);
    var paths=new AppPaths(root);var store=new Store(paths);var importer=new DeckImporter(paths,store);
    Check((await importer.Import()).Count==0&&store.Decks().Count==0,"No pack must require neither source-specific tables nor a card database.");
    var pack=Path.Combine(root,"private.json");Environment.SetEnvironmentVariable("CRUCIBLE_DECK_PACK",pack);
    await Reject(async()=>{await importer.Import();});
    var first=Fixture("First test list","one");var second=Fixture("Second test list","two");
    async Task Write(DeckPack value){await File.WriteAllTextAsync(pack,Json.Encode(value));}
    await Write(new(1,[first,second with{ListHash="bad"}]));
    await Reject(async()=>{await importer.Import();});
    Check(store.Decks().Count==0,"Invalid later fingerprint must leave the entire batch unimported.");
    await Write(new(2,[first]));await Reject(async()=>{await importer.Import();});
    await Write(new(1,[first,first]));await Reject(async()=>{await importer.Import();});
    await Write(new(1,[first]));var imported=(await importer.Import()).Single();
    Check(imported.Id==first.Id&&imported.ListHash==first.ListHash&&imported.ManifestHash==first.ManifestHash,"Import must preserve exact provenance and identity.");
    var saved=store.Deck(first.Id);
    Check(saved.ArchetypeTier=="A"&&saved.TierSourceUrl==first.TierSourceUrl&&saved.PublishedWinRatePercent==50,"Optional source metadata must survive a store round trip.");
    var frozen=Json.Encode(saved);
    await Write(new(1,[first with{PublishedWinRatePercent=90,ImportedAt=DateTimeOffset.UtcNow}]));
    await importer.Import();Check(Json.Encode(store.Deck(first.Id))==frozen,"Repeating import must preserve existing immutable payloads and timestamps.");
    Environment.SetEnvironmentVariable("CRUCIBLE_DECK_PACK",null);
    Check((await importer.Import()).Count==0&&Json.Encode(store.Deck(first.Id))==frozen,"Removing a pack must retain saved decks.");
    Environment.SetEnvironmentVariable("CRUCIBLE_DECK_PACK",pack);
    var legacy=Fixture("Legacy test list","legacy");
    var historical=System.Text.Json.Nodes.JsonNode.Parse(Json.Encode(new DeckFingerprint(legacy.Name,legacy.SourceType,legacy.SourceDeckId,legacy.Snapshot,legacy.SourceUrl,legacy.Cards)))!;
    foreach(var card in historical["cards"]!.AsArray())card!.AsObject().Remove("attributes");
    var legacyHash=DeckImporter.Hash(historical.ToJsonString());legacy=legacy with{Id=legacyHash,ManifestHash=legacyHash};
    await Write(new(1,[legacy]));
    Check((await importer.Import()).Single().Id==legacyHash,"Historical cards without Attributes must retain their original manifest identity.");
    var injected=legacy with{Cards=legacy.Cards.Select(c=>c with{Attributes=Json.Element(new Dictionary<string,string>{{"injected","data"}})}).ToList()};
    await Write(new(1,[injected]));await Reject(async()=>{await importer.Import();});
    Check(store.Deck(legacyHash).Cards.All(c=>c.Attributes is null),"Legacy validation must never discard supplied attributes to make an old hash pass.");
    var notePath=Path.Combine(root,"private-notes.json");
    var note=new AiNoteRecord("synthetic-note","deck",first.Id,"Test note","Synthetic private commentary","Test","Fixture",DateTimeOffset.UtcNow.AddMinutes(-1),"Synthetic fixture inspected");
    await File.WriteAllTextAsync(notePath,Json.Encode(new List<AiNoteRecord>{note}));
    Environment.SetEnvironmentVariable("CRUCIBLE_NOTES_FILE",notePath);
    Check(new AiNoteStore(paths).Read("deck",first.Id).Single()==note,"Private notes must load without a bundled editorial seed.");
    File.Delete(notePath);
    await Reject(()=>Task.Run(()=>{_=new AiNoteStore(paths);}));
    Environment.SetEnvironmentVariable("CRUCIBLE_NOTES_FILE",null);
    Check(new AiNoteStore(paths).Read("deck",first.Id).Single()==note,"Removing a private notes file must retain saved commentary.");
    if(args.Length>0){
        Environment.SetEnvironmentVariable("CRUCIBLE_DECK_PACK",Path.GetFullPath(args.Single()));
        var input=Json.Decode<DeckPack>(File.ReadAllText(args.Single()));
        var actual=await importer.Import();
        Check(actual.Count==input.Decks.Count&&actual.Zip(input.Decks).All(pair=>pair.First.Id==pair.Second.Id&&pair.First.ListHash==pair.Second.ListHash&&pair.First.ManifestHash==pair.Second.ManifestHash),"Private migration must preserve every manifest identity.");
        Console.WriteLine($"Private pack integration passed for {actual.Count} manifests.");
    }
    Console.WriteLine("Deck pack checks passed: clean empty import, explicit failure, whole-input validation, metadata, exact identities, repeat import, and retained history.");
}finally{
    Environment.SetEnvironmentVariable("CRUCIBLE_DECK_PACK",previous);
    Environment.SetEnvironmentVariable("CRUCIBLE_NOTES_FILE",previousNotes);
    SqliteConnection.ClearAllPools();Directory.Delete(root,true);
}
