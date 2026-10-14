using DeckTester;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
var sandbox=Path.Combine(Path.GetTempPath(),"crucible-workshop-"+Guid.NewGuid().ToString("N"));
var root=Path.Combine(sandbox,"projects","mtg-simulator");Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
var previous=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",root);
try{
    using(var db=new SqliteConnection("Data Source="+Path.Combine(sandbox,"standard_cards.sqlite"))){
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="""
        CREATE TABLE cards(arena_id INTEGER PRIMARY KEY,title_id INTEGER,name TEXT,set_code TEXT,collector_number TEXT,type_line TEXT,mana_cost TEXT,rules_text TEXT,standard_legal INTEGER,linked_face_arena_ids TEXT,is_primary_card INTEGER);
        INSERT INTO cards VALUES(1,1,'Forest','TST','1','Basic Land — Forest','','',1,'',1);
        INSERT INTO cards VALUES(2,2,'Test Creature','TST','2','Creature','{G}','',1,'',1);
        INSERT INTO cards VALUES(3,2,'Test Creature','OLD','3','Creature','{G}','',1,'',1);
        INSERT INTO cards VALUES(4,4,'Ambiguous','TST','4','Creature','{G}','',1,'',1);
        INSERT INTO cards VALUES(5,5,'Ambiguous','TST','5','Creature','{G}','',1,'',1);
        INSERT INTO cards VALUES(6,6,'Front // Back','TST','6','Creature','{G}','',1,'7',1);
        INSERT INTO cards VALUES(7,6,'Back','TST','7','Land','','',0,'6',0);
        INSERT INTO cards VALUES(8,8,'Unlimited','TST','8','Creature','{G}','A deck can have any number of cards named Unlimited.',1,'',1);
        INSERT INTO cards VALUES(9,9,'Nine','TST','9','Creature','{G}','A deck can have up to nine cards named Nine.',1,'',1);
        INSERT INTO cards VALUES(10,10,'Not Legal','TST','10','Creature','{G}','',0,'',1);
        ALTER TABLE cards ADD COLUMN mana_value INTEGER DEFAULT 0;
        UPDATE cards SET mana_value=1 WHERE mana_cost<>'';
        """;cmd.ExecuteNonQuery();
    }
    _=new CardAttributes(Path.Combine(sandbox,"standard_cards.sqlite"));
    var paths=new AppPaths(new TestEnvironment{ContentRootPath=root});var store=new Store(paths);var engine=new ForgeEngine(paths,store);var workshop=new DeckWorkshop(paths,store,engine);
    var initialSourceHash=ForgeEngine.FileHash(paths.Source);
    var resolved=workshop.Resolve("Deck\n56 Forest (TST) 1\n2 Test Creature\n2 Test Creature (TST) 2\nSideboard\n1 Front (TST) 6");
    Check(resolved.Issues.Count==0&&resolved.Cards.Where(c=>c.Zone=="MainDeck").Sum(c=>c.Quantity)==60,"Resolve printing, unqualified reprints, duplicate rows, and sideboards.");
    Check(resolved.Cards.Single(c=>c.Zone=="Sideboard").Name=="Front // Back","Front face resolves through primary identity.");
    Check(workshop.Resolve("1 Back").Issues.Count==1,"Secondary face must not resolve as standalone.");
    Check(workshop.Resolve("1 Ambiguous").Issues.Count==1&&workshop.Resolve("1 Ambiguous (TST) 4").Issues.Count==0,"Ambiguous names require exact printing.");
    Check(workshop.Resolve("0 Forest\ninvalid\n1 Unknown").Issues.Count==3,"Malformed and unknown rows remain explicit.");
    Check(workshop.Resolve("1 Forest (BAD) 9").Issues.Count==1,"Printing mismatch cannot silently fall back.");
    Check(workshop.Search("Test").Count==1,"Search deduplicates reprints by primary identity.");
    var original=await workshop.Save(new("Original",[new(1,56),new(2,4)],Sideboard:[new(6,1)]));
    var before=Json.Encode(store.Deck(original.Id));
    var edited=await workshop.Save(new("Edited",[new(1,57),new(2,3)],original.Id));
    Check(edited.Id!=original.Id&&edited.ListHash!=original.ListHash&&edited.ParentDeckId==original.Id,"Changes create exact lineage and fingerprints.");
    Check(edited.SideCount==1&&edited.PublishedWinRatePercent is null&&edited.ArchetypeTier is null,"Preserve sideboard provenance without inheriting public performance.");
    Check(Json.Encode(store.Deck(original.Id))==before,"Original manifest stays immutable.");
    var duplicate=await workshop.Save(new("Edited",[new(1,57),new(2,3)],original.Id));
    Check(duplicate.Id==edited.Id&&duplicate.ImportedAt==edited.ImportedAt,"Repeated saves preserve the immutable version timestamp.");
    var reanimator=await workshop.Save(new("Reanimator",[new(1,56),new(2,4)]));
    var renamed=await workshop.Save(new("Renamed combo draft",[new(1,57),new(2,3)],reanimator.Id));
    Check(renamed.Strategy==reanimator.Strategy&&renamed.Subtype=="Reanimator","Renamed Workshop copies inherit parent classification.");
    Check(ReanimatorPilots.Profile(renamed,ReanimatorPilots.ControlMode)==ReanimatorPilots.Shared,"Workshop Reanimator keeps its subtype pilot in combined adaptive mode.");
    var nested=await workshop.Save(new("Nested combo draft",[new(1,58),new(2,2)],renamed.Id));
    var control=await workshop.Save(new("4-Color Control",[new(1,56),new(2,4)]));
    var controlCopy=await workshop.Save(new("Renamed control draft",[new(1,57),new(2,3)],control.Id));
    Check(controlCopy.Strategy=="control"&&ReanimatorPilots.Profile(controlCopy,ReanimatorPilots.ControlMode)==ReanimatorPilots.Control,"Control classification also survives Workshop renaming.");
    var immutableRenamed=Json.Encode(store.Deck(renamed.Id));
    using(var db=new SqliteConnection("Data Source="+Path.Combine(paths.Data,"simulator.sqlite"))){
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE decks SET strategy=NULL,subtype=NULL WHERE id IN ($child,$nested);";
        cmd.Parameters.AddWithValue("$child",renamed.Id);cmd.Parameters.AddWithValue("$nested",nested.Id);cmd.ExecuteNonQuery();
    }
    var repaired=new Store(paths);
    Check(repaired.Deck(nested.Id).Subtype=="Reanimator"&&repaired.Deck(nested.Id).Strategy==reanimator.Strategy,"Startup repairs multi-generation legacy Workshop annotations.");
    Check(Json.Encode(repaired.Deck(renamed.Id))==immutableRenamed,"Classification repair leaves manifest/provenance unchanged.");
    Check(repaired.Deck(original.Id).Strategy is null&&repaired.Deck(original.Id).Subtype is null,"Unclassified scratch decks stay unclassified.");
    var invalid=workshop.Validate(new("Bad",[new(1,55),new(2,5)]));Check(!invalid.CanSave&&invalid.Errors.Any(x=>x.Contains("copy")),"Copy limit blocks saving.");
    Check(!workshop.Validate(new("Short",[new(1,59)])).CanSave,"Exactly 60 cards are required.");
    Check(!workshop.Validate(new("Unknown",[new(999,60)])).CanSave,"Unknown IDs cannot be silently dropped on save.");
    Check(workshop.Validate(new("Unlimited",[new(8,60)])).CanSave,"Card-text unlimited exception.");
    Check(workshop.Validate(new("Nine",[new(1,51),new(9,9)])).CanSave&&!workshop.Validate(new("Ten",[new(1,50),new(9,10)])).CanSave,"Numeric word copy exception.");
    var unsupported=workshop.Validate(new("Illegal",[new(1,56),new(10,4)]));Check(unsupported.CanSave&&unsupported.Blockers.Any(b=>b.Contains("legality"))&&unsupported.Blockers.Any(b=>b.Contains("unsupported")),"Saving and simulation blockers have separate semantics.");
    Check(ForgeEngine.FileHash(paths.Source)==initialSourceHash,"Source SQLite remains byte-for-byte unchanged.");
    var config=new RunConfig([original.Id,edited.Id],4,2,180,150,42,true,"engine","adapter");
    var run=new SimulationRun("run",DateTimeOffset.UtcNow,"completed",config,4);
    GameSlot Slot(string id,string starter,string? winner,bool draw,int turns,bool analyzed=true)=>new(id,"run",original.Id,edited.Id,starter,1,0,"completed",[],new(winner,draw,"rules",turns,[],0,[],[new("mana-starved","B",1,2,1,4,"evidence",ManaAnalyzer.Version)],analyzed?ManaAnalyzer.Version:null,[new("B",2,5,true,"keep")],"v1"));
    var slots=new List<GameSlot>{Slot("1",edited.Id,edited.Id,false,5),Slot("2",original.Id,original.Id,false,8),Slot("3",original.Id,null,true,9),Slot("4",edited.Id,edited.Id,false,10,false)};
    var match=DeckWorkshop.Summarize(run,edited,original,slots);
    Check(match.Wins==2&&match.Losses==1&&match.Draws==1&&match.WinRate==0.5,"Orient W/L/D and rate to edited deck in position B.");
    Check(match.OnPlay==2&&match.WinsOnPlay==2&&match.OnDraw==2&&match.WinsOnDraw==0,"Correct play/draw denominators.");
    Check(match.ManaAnalyzed==3&&match.Starved==3&&match.Flooded==0,"Scope mana incidents to deck and analyzed denominator.");
    Check(match.Mulligans?.TotalMulligans==8&&match.Mulligans.RecordedPlayers==4,"Scope recorded mulligans.");
    Check(match.MedianRounds==4.5&&match.WinTurns.SequenceEqual([5,10]),"Actual-turn rounding and outcome distributions.");
    var multiRun=run with{Config=config with{DeckIds=[original.Id,edited.Id,nested.Id]},Total=6};
    await store.SaveRun(multiRun);
    foreach(var slot in slots)await store.SaveSlot(slot);
    var other=slots[0] with{Id="other-matchup",DeckB=nested.Id};
    await store.SaveSlot(other);
    Check(store.MatchupSlots(run.Id,edited.Id,original.Id).Count==4&&store.MatchupSlots(run.Id,original.Id,edited.Id).Count==4,"Read only the selected matchup in either deck orientation.");
    var scoped=workshop.Matchup(run.Id,edited.Id,original.Id);
    var unscoped=DeckWorkshop.Summarize(multiRun,store.Deck(edited.Id),store.Deck(original.Id),slots.Append(other).ToList());
    Check(Json.Encode(scoped)==Json.Encode(unscoped),"Scoped matchup loading must preserve rates, diagnostics, lengths and replay links.");
    var comparisonCache=new WorkshopComparisonCache();var comparisonCalls=0;
    Task<WorkshopMatchup> Fetch(CancellationToken ct){ct.ThrowIfCancellationRequested();comparisonCalls++;return Task.FromResult(match);}
    await comparisonCache.Load(run,edited.Id,original.Id,Fetch);
    await comparisonCache.Load(run,edited.Id,original.Id,Fetch);
    Check(comparisonCalls==1,"An unchanged completed comparison must be reused.");
    await comparisonCache.Load(run,edited.Id,original.Id,Fetch,force:true);
    Check(comparisonCalls==2,"Explicit Refresh must fetch completed results again.");
    var extended=run with{UpdatedAt=run.CreatedAt.AddMinutes(1),Total=6};
    await comparisonCache.Load(extended,edited.Id,original.Id,ct=>{comparisonCalls++;return Task.FromResult(match with{Run=extended});});
    Check(comparisonCalls==3,"An extended or revised session must invalidate its comparison.");
    var active=extended with{Status="running"};
    await comparisonCache.Load(active,edited.Id,original.Id,Fetch);
    await comparisonCache.Load(active,edited.Id,original.Id,Fetch);
    Check(comparisonCalls==5,"In-progress comparisons must keep fetching live results.");
    using(var cancelledComparison=new CancellationTokenSource()){
        cancelledComparison.Cancel();
        try{await comparisonCache.Load(run,edited.Id,original.Id,Fetch,cancelledComparison.Token);throw new Exception("Comparison cancellation was ignored.");}catch(OperationCanceledException){}
    }
    var zero=DeckWorkshop.Summarize(run,edited,original,slots.Select(s=>s with{Status="pending",Result=null}).ToList());
    Check(zero.WinRate is null&&zero.Interval is null&&zero.MedianRounds is null&&zero.Mulligans?.RecordedPlayers==0&&zero.ManaAnalyzed==0,"No games means unknown rates and diagnostics.");
    Check(Store.CacheKey(slots[0])!=Store.CacheKey(slots[0] with{DeckB="different-version"}),"Deck-version changes isolate cache keys.");
    var legacy=Json.Decode<SimulationRun>("{\"id\":\"old\",\"createdAt\":\"2026-10-11T00:00:00Z\",\"status\":\"completed\",\"config\":"+Json.Encode(config)+",\"total\":4}");
    Check(legacy.WorkshopBaseline is null&&legacy.WorkshopDeckId is null,"Legacy sessions deserialize without workshop metadata.");
    var draft=new DeckTester.Components.WorkshopDraft("Draft",original.Id,edited.Id,original.Id,edited.Cards,[],original.Cards,"",[],"run",original.Id,"run",edited.Id,2,2,180,150,42,false,true,1,15,5,true);
    var draftJson=System.Text.Json.JsonSerializer.Serialize(draft);
    var restored=System.Text.Json.JsonSerializer.Deserialize<DeckTester.Components.WorkshopDraft>(draftJson)!;
    Check(restored.Name=="Draft"&&restored.Cards.Sum(c=>c.Quantity)==61&&restored.HasDraft,"Browser draft round-trip preserves cards, metadata, and loaded state.");
    Console.WriteLine("Deck Workshop checks passed.");
}finally{Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",previous);SqliteConnection.ClearAllPools();Directory.Delete(sandbox,true);}

sealed class TestEnvironment:IHostEnvironment
{
    public string EnvironmentName {get;set;}="Test";public string ApplicationName {get;set;}="Test";
    public string ContentRootPath {get;set;}="";public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
}
