using DeckTester;

static DeckManifest Deck(string hash,string? subtype,params string[] names) => new(hash,"Reanimator","custom","Edited list","source","snapshot",null,hash,hash,DateTimeOffset.UtcNow,
    names.Select(name=>new CardEntry(1,name,1,"MainDeck","","Creature","","",true,"")).ToList(),""){Subtype=subtype,Strategy="control-combo"};
static void Check(bool condition,string message){if(!condition)throw new Exception(message);}

Check(ReanimatorPilots.Profile(Deck("edited-4color","Reanimator","Terror of the Peaks"))==ReanimatorPilots.PublicFourColor,"Terror variant");
Check(ReanimatorPilots.Profile(Deck("edited-sultai","reanimator","Jadzi, Steward of Fate"))==ReanimatorPilots.PublicSultai,"Sultai variant");
Check(ReanimatorPilots.Profile(Deck("edited-personal","Reanimator","Seasoned Cryomancer"))==ReanimatorPilots.Personal,"Cryomancer variant");
Check(ReanimatorPilots.Profile(Deck("new-archetype","Reanimator","Formidable Speaker"))==ReanimatorPilots.Shared,"Generic Reanimator profile");
Check(ReanimatorPilots.Profile(Deck("old-exact-hash",null,"Terror of the Peaks"))==ReanimatorPilots.Default,"Missing subtype must not inherit pilot");
Check(ReanimatorPilots.Profile(Deck("other","Auras","Terror of the Peaks"))==ReanimatorPilots.Default,"Other subtype uses Forge Default");
Check(ReanimatorPilots.Profile(Deck("edited-4color","Reanimator","Terror of the Peaks"),ReanimatorPilots.Baseline)==ReanimatorPilots.Default,"Baseline uses Forge Default");

var adaptive=new RunConfig(["A","B"],2,1,180,100,42,true,"engine","adapter",Pilot:ReanimatorPilots.Adaptive);
var baseline=adaptive with{Pilot=ReanimatorPilots.Baseline};
Check(!Store.CompatibleSession(adaptive,adaptive with{Pilot="reanimator-adaptive-v8"}),"Older pilot revisions must remain separate for session reuse");
Check(!Store.CompatibleSession(adaptive,baseline),"Policy modes must remain separate for session reuse");
Check(!Store.CompatibleSession(adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.Shared,["B"]=ReanimatorPilots.Default}},
    adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.PublicFourColor,["B"]=ReanimatorPilots.Default}}),"Changed classification must remain separate for session reuse");
var metadataDeck=Deck("metadata","Reanimator","Unfamiliar Card") with{Cards=[new CardEntry(1,"Unfamiliar Card",1,"MainDeck","","Creature","","",true,""){Attributes=CardAttributes.Parse("{\"pilot\":{\"variant\":\"sultai\"}}")}]};
Check(ReanimatorPilots.Profile(metadataDeck)==ReanimatorPilots.PublicSultai,"Metadata selects the variant without naming a known card");
Console.WriteLine("Pilot identity checks passed.");

var control = Deck("control", "Spell Control", "Unfamiliar Card") with { Strategy="CoNtRoL" };
Check(ReanimatorPilots.Profile(control,ReanimatorPilots.ControlMode)==ReanimatorPilots.Control,"Case insensitive broad Control assignment");
Check(ReanimatorPilots.Profile(control,ReanimatorPilots.Adaptive)==ReanimatorPilots.Default,"Historical v9 mode retains its assignment");
Check(ReanimatorPilots.Profile(control with{Subtype="reanimator"},ReanimatorPilots.ControlMode)==ReanimatorPilots.Shared,"Reanimator subtype wins an overlapping Control label");
Check(ReanimatorPilots.Profile(control with{Strategy="control-combo",Subtype="Artifacts"},ReanimatorPilots.ControlMode)==ReanimatorPilots.Default,"Control-Combo is not exact Control");
Check(ReanimatorPilots.Profile(control with{Strategy=null,Subtype="Control"},ReanimatorPilots.ControlMode)==ReanimatorPilots.Default,"Subtype is not broad strategy");
Check(ReanimatorPilots.Profile(control,ReanimatorPilots.Baseline)==ReanimatorPilots.Default,"Baseline controls use Default");
Check(!Store.CompatibleSession(adaptive,adaptive with{Pilot=ReanimatorPilots.ControlMode}),"Combined policy mode separates old sessions");
Check(!Store.CompatibleSession(adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.Default}},adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.Control}}),"Changed Control profile separates cache groups");

var aggro=control with {Strategy="AgGrO",Subtype="Burn"};
Check(ReanimatorPilots.Profile(aggro,ReanimatorPilots.AggroMode)==ReanimatorPilots.Aggro,"Exact case insensitive Aggro assignment");
Check(ReanimatorPilots.Profile(aggro,ReanimatorPilots.ControlMode)==ReanimatorPilots.Default,"Historical combined mode preserves Default Aggro");
Check(ReanimatorPilots.Profile(aggro,ReanimatorPilots.Baseline)==ReanimatorPilots.Default,"Baseline Aggro uses Default");
Check(ReanimatorPilots.Profile(aggro with{Strategy="aggro-combo"},ReanimatorPilots.AggroMode)==ReanimatorPilots.Default,"Hybrid Aggro labels are not exact Aggro");
Check(ReanimatorPilots.Profile(aggro with{Subtype="Reanimator"},ReanimatorPilots.AggroMode)==ReanimatorPilots.Shared,"Reanimator subtype retains precedence over Aggro");
Check(ReanimatorPilots.Profile(control,ReanimatorPilots.AggroMode)==ReanimatorPilots.Control,"Control profile preserved in new mode");
Check(!Store.CompatibleSession(adaptive with{Pilot=ReanimatorPilots.ControlMode},adaptive with{Pilot=ReanimatorPilots.AggroMode}),"New assignment mode isolates old sessions");
Check(!Store.CompatibleSession(adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.Default}},adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.Aggro}}),"Aggro profiles isolate cache");
Console.WriteLine("Aggro identity checks passed.");

Check(SubtypePilots.Profiles.Count==30,"All remaining library subtypes are registered");
foreach(var (subtype,profile) in SubtypePilots.Profiles) {
    var deck=control with{Subtype=subtype.ToUpperInvariant()};
    Check(ReanimatorPilots.Profile(deck,SubtypePilots.Mode)==profile,"Exact case insensitive subtype "+subtype);
    Check(ReanimatorPilots.Profile(deck,ReanimatorPilots.AggroMode)==ReanimatorPilots.Control,"Legacy broad assignment preserved "+subtype);
    Check(ReanimatorPilots.Profile(deck,ReanimatorPilots.Baseline)==ReanimatorPilots.Default,"Baseline unchanged "+subtype);
    Check(!Store.CompatibleSession(adaptive with{PilotProfiles=new(){["A"]=profile}},adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.Default}}),"Subtype cache identity "+subtype);
}
Check(ReanimatorPilots.Profile(control with{Subtype="Reanimator"},SubtypePilots.Mode)==ReanimatorPilots.Shared,"Reanimator precedence in subtype mode");
Check(ReanimatorPilots.Profile(control with{Subtype="Unknown Auras Hybrid",Strategy="aggro-combo"},SubtypePilots.Mode)==ReanimatorPilots.Default,"Unknown hybrid not substring matched");
Check(!Store.CompatibleSession(adaptive with{Pilot=SubtypePilots.Mode},adaptive with{Pilot=ReanimatorPilots.AggroMode}),"Versioned mode isolation");
Check(ReanimatorPilots.ValidMode(SubtypePilots.Mode),"Subtype mode accepted");
Console.WriteLine("Subtype identity checks passed: 30 exact assignments, legacy policies and cache isolation.");

Check(ReanimatorPilots.Profile(Deck("v10","Reanimator","Terror of the Peaks"),ReanimatorPilots.LegacySubtypeModeV10)=="reanimator-terror-v10","Current subtype mode assigns v10");
Check(ReanimatorPilots.Profile(Deck("v9","Reanimator","Terror of the Peaks"),SubtypePilots.Mode)=="reanimator-terror-v9","Historical subtype mode retains v9");
Check(ReanimatorPilots.Profile(Deck("v10","Reanimator","Seasoned Cryomancer"),ReanimatorPilots.LegacyAggroModeV10)=="reanimator-cryomancer-v10","Cryomancer precedence preserved");
Check(ReanimatorPilots.Profile(Deck("v10","Reanimator","Jadzi, Steward of Fate"),ReanimatorPilots.LegacySubtypeModeV10)=="reanimator-sultai-v10","Sultai variant preserved");
Check(ReanimatorPilots.Profile(control with{Subtype="Auras"},ReanimatorPilots.CurrentSubtypeMode)=="auras-v001","Opponent subtype policy preserved");
Check(ReanimatorPilots.Profile(control,ReanimatorPilots.CurrentAggroMode)==ReanimatorPilots.Control,"Control policy preserved");
Check(ReanimatorPilots.Profile(aggro,ReanimatorPilots.CurrentAggroMode)==ReanimatorPilots.Aggro,"Aggro policy preserved");
Check(ReanimatorPilots.ValidMode(ReanimatorPilots.CurrentSubtypeMode)&&ReanimatorPilots.ValidMode(ReanimatorPilots.CurrentAggroMode),"New modes accepted");
Check(!Store.CompatibleSession(adaptive with{Pilot=SubtypePilots.Mode},adaptive with{Pilot=ReanimatorPilots.CurrentSubtypeMode}),"v9 and v10 sessions cannot be reused");
Check(!Store.CompatibleSession(adaptive with{PilotProfiles=new(){["A"]="reanimator-terror-v9"}},adaptive with{PilotProfiles=new(){["A"]="reanimator-terror-v10"}}),"v9 and v10 profile cache isolation");
Console.WriteLine("Reanimator v10 assignment and cache isolation checks passed.");

var optimized=adaptive with{OptimizerPolicy=new OptimizerPolicy(1,new string('a',64),"control-shared-v1",new(){["Example|control.keepPriority"]=60},"tuning","engine","adapter")};
Check(!Store.CompatibleSession(adaptive,optimized),"Optimized and untuned runs cannot reuse sessions");
Check(!Store.CompatibleSession(optimized,optimized with{OptimizerPolicy=optimized.OptimizerPolicy! with{Id=new string('b',64)}}),"Different source/tuning identities isolate sessions");
Check(Store.CompatibleSession(optimized,optimized with{}),"Identical optimized identities remain compatible");
Check(Json.Decode<RunConfig>(Json.Encode(adaptive)).OptimizerPolicy is null,"Historical configs default to no optimizer policy");
var executionGate=new SimulationExecutionGate();
using(var first=await executionGate.Acquire(CancellationToken.None)){
    using var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    try{using var second=await executionGate.Acquire(timeout.Token);throw new Exception("Overlapping execution accepted");}
    catch(OperationCanceledException)when(timeout.IsCancellationRequested){}
}
using(var after=await executionGate.Acquire(CancellationToken.None)){}
Console.WriteLine("Optimizer identity and exclusive execution checks passed.");

var temporary=Path.Combine(Path.GetTempPath(),"optimizer-cache-"+Guid.NewGuid().ToString("N"));
var previousRoot=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
try{
    Directory.CreateDirectory(Path.Combine(temporary,"docs","specs"));
    Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",temporary);
    var paths=new AppPaths(new OptimizerCheckEnvironment());
    var cacheStore=new Store(paths);
    var profiles=new Dictionary<string,string>{["A"]="control-shared-v1",["B"]="forge-default"};
    var untuned=adaptive with{PilotProfiles=profiles};
    var tuned=optimized with{PilotProfiles=profiles};
    var protocolConfig=untuned with{ProtocolSet=new ProtocolCatalog("v1",ReanimatorPilots.Engagement,"protocol-hash",System.Text.Json.JsonSerializer.SerializeToElement(new{maximumDelayTurns=1}),[])};
    using(var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+Path.Combine(paths.Data,"simulator.sqlite"))){
        db.Open();
        foreach(var (id,cfg) in new[]{("untuned",untuned),("tuned",tuned),("protocol",protocolConfig)}){
            var sampleRun=new SimulationRun(id,DateTimeOffset.UtcNow,"completed",cfg,1);
            var sampleSlot=new GameSlot(id+"-game",id,"A","B","A",1,0,"completed",
                [new Attempt(1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"completed","replay.gz","checksum",null)],
                new GameResult("A",false,"fixture",1,[],1,[]));
            using var command=db.CreateCommand();
            command.CommandText="INSERT INTO runs VALUES($id,$run); INSERT INTO slots VALUES($slot,$id,$payload)";
            command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$run",Json.Encode(sampleRun));
            command.Parameters.AddWithValue("$slot",sampleSlot.Id);command.Parameters.AddWithValue("$payload",Json.Encode(sampleSlot));command.ExecuteNonQuery();
        }
    }
    Check(cacheStore.CachedGames(untuned).Values.Single().RunId=="untuned","SQL cache lookup excludes optimized results from the untuned arm");
    Check(cacheStore.CachedGames(tuned).Values.Single().RunId=="tuned","SQL cache lookup requires the exact optimizer identity");
    Check(cacheStore.CachedGameKeys(tuned with{OptimizerPolicy=tuned.OptimizerPolicy! with{Id=new string('c',64)}}).Count==0,"Unknown optimizer identities cannot reuse games");
    Check(cacheStore.CachedGames(protocolConfig).Values.Single().RunId=="protocol","SQL cache requires the saved protocol hash");
    Check(cacheStore.CachedGameKeys(protocolConfig with{ProtocolSet=protocolConfig.ProtocolSet! with{Hash="changed"}}).Count==0,"Unknown protocol hash cannot reuse cached games");
    Console.WriteLine("Optimizer and protocol SQL cache isolation checks passed.");
}finally{
    Environment.SetEnvironmentVariable("MTG_PROJECT_ROOT",previousRoot);
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if(Directory.Exists(temporary))Directory.Delete(temporary,true);
}

foreach(var names in new[]{new[]{"Seasoned Cryomancer"},new[]{"Jadzi, Steward of Fate"},new[]{"Terror of the Peaks"},new[]{"Formidable Speaker"}}) {
    foreach(var mode in new[]{ReanimatorPilots.CurrentAggroMode,ReanimatorPilots.CurrentSubtypeMode,ReanimatorPilots.CurrentControlMode})
        Check(ReanimatorPilots.Profile(Deck("unified","rEaNiMaToR",names),mode)==ReanimatorPilots.Unified,"All Reanimator lists share one current pilot");
}
Check(!Store.CompatibleSession(adaptive with{Pilot=ReanimatorPilots.LegacySubtypeModeV10},adaptive with{Pilot=ReanimatorPilots.CurrentSubtypeMode}),"Unified mode isolates historical sessions");
Check(!Store.CompatibleSession(adaptive with{PilotProfiles=new(){["A"]="reanimator-sultai-v10"}},adaptive with{PilotProfiles=new(){["A"]=ReanimatorPilots.Unified}}),"Unified profile isolates cache");
Check(ReanimatorPilots.Profile(aggro,ReanimatorPilots.CurrentControlMode)==ReanimatorPilots.Default,"Current Control-only mode preserves Default Aggro");
Check(ReanimatorPilots.Profile(control,ReanimatorPilots.CurrentControlMode)==ReanimatorPilots.Control,"Current Control-only mode preserves Control");
Check(ReanimatorPilots.ValidMode(ReanimatorPilots.CurrentControlMode),"Unified Control-only mode accepted");
Console.WriteLine("Unified Reanimator assignment and legacy/cache checks passed.");


Check(PilotNames.Name(ReanimatorPilots.Engagement)=="Liliana","Protocol pilot alias belongs to the lore vocabulary");
Check(ReanimatorPilots.ValidMode(ReanimatorPilots.EngagementMode),"Protocol opt-in mode is valid");
Check(ReanimatorPilots.Profile(metadataDeck,ReanimatorPilots.EngagementMode)==ReanimatorPilots.Engagement,"Protocol mode assigns v012 to Reanimator");
Check(ReanimatorPilots.Profile(aggro,ReanimatorPilots.EngagementMode)==ReanimatorPilots.Aggro,"Protocol mode retains Aggro");
Check(ReanimatorPilots.Profile(control,ReanimatorPilots.EngagementMode)==ReanimatorPilots.Control,"Protocol mode retains Control");
Check(ReanimatorPilots.Profile(metadataDeck,ReanimatorPilots.CurrentAggroMode)==ReanimatorPilots.Unified,"Standard remains v011");
var protocols=new ProtocolCatalog("v1",ReanimatorPilots.Engagement,"hash",System.Text.Json.JsonSerializer.SerializeToElement(new{maximumDelayTurns=1}),[]);
Check(!Store.CompatibleSession(adaptive,adaptive with{ProtocolSet=protocols}),"Protocol snapshot isolates compatibility");
Check(!Store.CompatibleSession(adaptive with{ProtocolSet=protocols},adaptive with{ProtocolSet=protocols with{Hash="changed"}}),"Changed protocol hash cannot reuse a session");
Check(Json.Decode<RunConfig>(Json.Encode(adaptive)).ProtocolSet is null,"Historical runs have no implied protocols");
var replay=Json.Decode<ReplayRecord>("{\"kind\":\"protocol_state\",\"sequence\":1,\"actualTurn\":2,\"activeProtocols\":[\"counterplay\"],\"protocolSetVersion\":\"v1\"}");
Check(replay.ActiveProtocols?.Single()=="counterplay","Replay retains protocol transitions");
Console.WriteLine("Engagement Protocol identity checks passed.");

sealed class OptimizerCheckEnvironment:Microsoft.Extensions.Hosting.IHostEnvironment
{
    public string EnvironmentName{get;set;}="Test";
    public string ApplicationName{get;set;}="Checks";
    public string ContentRootPath{get;set;}=Directory.GetCurrentDirectory();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider{get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
}
