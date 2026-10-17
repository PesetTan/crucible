using System.Text;

namespace DeckTester;

public static class BackendHosting
{
    private static IResult Reply<T>(T value)=>Results.Json(value,Json.Info<T>());
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<CardAttributes>();services.AddSingleton<Store>();services.AddSingleton<DeckImporter>();services.AddSingleton<ForgeEngine>();
        services.AddSingleton<PyreEngine>();services.AddSingleton<EngineProviders>();
        services.AddSingleton<SimulationExecutionGate>();services.AddSingleton<DeckWorkshop>();services.AddSingleton<ResourceCapacity>();
        services.AddSingleton<RunCoordinator>();services.AddHostedService(p=>p.GetRequiredService<RunCoordinator>());
        services.AddSingleton<PilotOptimizer>();services.AddHostedService(p=>p.GetRequiredService<PilotOptimizer>());
        services.AddSingleton<LocalUiBackend>();services.AddSingleton<IUiBackend>(p=>p.GetRequiredService<LocalUiBackend>());
        services.AddSingleton<UiReadCache>();
    }
    public static void MapAnnotations(WebApplication app)
    {
        app.MapGet("/api/v1/pilot-scorecards",(PilotEvaluationStore evaluations)=>evaluations.Read());
        app.MapGet("/api/v1/ai-notes",(string scope,string subject,AiNoteStore notes)=>notes.Read(scope,subject));
        app.MapPost("/api/v1/ai-notes",(AiNoteRecord note,AiNoteStore notes)=>{notes.Append(note);return Results.Created($"/api/v1/ai-notes?scope={Uri.EscapeDataString(note.Scope)}&subject={Uri.EscapeDataString(note.Subject)}",note);});
    }
    public static void Map(WebApplication app,string instanceToken)
    {
        async Task<IResult> CachedUiRead<T>(string key,Func<CancellationToken,Task<T>> build,UiReadCache cache,HttpContext context){
            var read=await cache.Read(key,build,context.RequestAborted);
            context.Response.Headers.ETag=read.Tag;context.Response.Headers.CacheControl="private, no-cache";
            if(context.Request.Headers.IfNoneMatch.ToString().Split(',').Any(value=>value.Trim()==read.Tag||value.Trim()=="W/"+read.Tag||value.Trim()=="*"))
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Json(read.Value,Json.Info<T>());
        }
        Task<IResult> ReadUiState(string? runId,LocalUiBackend backend,UiReadCache cache,HttpContext context)=>
            CachedUiRead("state:"+(runId??""),token=>backend.Snapshot(runId,token),cache,context);
        app.MapGet("/api/v1/ui-state",ReadUiState);
        app.MapGet("/api/v1/runs/{runId}/state",ReadUiState);
        app.MapGet("/api/v1/run-history",(LocalUiBackend backend,UiReadCache cache,HttpContext context)=>CachedUiRead("history",backend.History,cache,context));
        app.MapGet("/api/v1/workshop-state",(LocalUiBackend backend,HttpContext context)=>backend.WorkshopState(context.RequestAborted));
        app.MapGet("/api/v1/library-state",(LocalUiBackend backend,UiReadCache cache,HttpContext context)=>CachedUiRead("library",backend.LibraryState,cache,context));
        app.MapGet("/api/v1/resources",(ResourceCapacity resources,HttpContext context)=>resources.Sample(context.RequestAborted));
        app.MapGet("/api/v1/session",()=>new InstanceSession(instanceToken));
        PilotOptimizer.MapEndpoints(app);
        app.MapGet("/api/v1/capabilities",(ForgeEngine engine)=>engine.Capabilities());
        app.MapGet("/api/v1/providers",(EngineProviders engines)=>engines.Discover());
        app.MapGet("/api/v1/decks",(Store store,ForgeEngine engine)=>store.Decks().Select(d=>new DeckSnapshot(d,engine.Coverage(d))).ToList());
        app.MapGet("/api/v1/decks/{id}",(string id,Store store,ForgeEngine engine)=>new DeckSnapshot(store.Deck(id),engine.Coverage(store.Deck(id))));
        app.MapGet("/api/v1/cards/search",(string q,DeckWorkshop workshop)=>workshop.Search(q));
        app.MapGet("/api/v1/cards/{id:int}/attributes",(int id,CardAttributes attributes)=>attributes.Read(id));
        app.MapPost("/api/v1/cards/{id:int}/attributes",(int id,AttributesAppend request,CardAttributes attributes)=>attributes.Append(id,request));
        app.MapPost("/api/v1/decks/resolve",(ArenaListRequest request,DeckWorkshop workshop)=>workshop.Resolve(request.Text));
        app.MapPost("/api/v1/decks/validate",(CustomDeckRequest request,DeckWorkshop workshop)=>workshop.Validate(request));
        app.MapPost("/api/v1/decks/custom",async(CustomDeckRequest request,DeckWorkshop workshop)=>await workshop.Save(request));
        app.MapGet("/api/v1/runs/{id}/workshop",(string id,string deckId,string opponentId,DeckWorkshop workshop)=>workshop.Matchup(id,deckId,opponentId));
        app.MapPost("/api/v1/imports",async(DeckImporter importer)=>await importer.Import());
        app.MapPost("/api/v1/runs/preflight",(RunRequest request,ForgeEngine engine,EngineProviders engines,Store store,CardAttributes attributes)=>{
            if(request.ProviderId!="forge")return new PreflightResponse(engines.Preflight(request),engines.Capabilities(request.ProviderId).Limitations,0,null,null);
            var capabilities=engine.Capabilities();
            var attributeSnapshot=attributes.Snapshot(request.DeckIds.Select(store.Deck));
            var config=new RunConfig(request.DeckIds,request.GamesPerPair,request.Workers,request.TimeoutSeconds,request.TurnLimit,request.RootSeed,request.Diagnostic,capabilities.EngineHash,capabilities.AdapterHash,
                Pilot:request.PilotMode,PerformanceMode:request.PerformanceMode,
                PilotProfiles:request.DeckIds.ToDictionary(id=>id,id=>ReanimatorPilots.Profile(store.Deck(id) with{Cards=store.Deck(id).Cards.Select(c=>c with{Attributes=attributeSnapshot.Cards.GetValueOrDefault(c.Name)}).ToList()},request.PilotMode))){AttributesHash=attributeSnapshot.Hash,OptimizerPolicy=engine.OptimizerPolicy(request),ProtocolSet=request.PilotMode==ReanimatorPilots.EngagementMode?capabilities.Protocols:null};
            var cached=store.CachedGameKeys(config).Count(key=>request.DeckIds.Contains(key.DeckA)&&request.DeckIds.Contains(key.DeckB)&&key.Index<request.GamesPerPair/2);
            var existing=store.ExistingSession(config);
            return new PreflightResponse(engine.Preflight(request),capabilities.Limitations,cached,existing?.Id,existing?.Config.GamesPerPair);
        });
        app.MapPost("/api/v1/runs",async(RunRequest request,HttpContext context,RunCoordinator coordinator,bool requireIdle=false)=>{
            var key=context.Request.Headers["Idempotency-Key"].ToString();if(key.Length is <1 or >128)throw new ArgumentException("Provide an Idempotency-Key of 1–128 characters.");
            return Reply(new CreatedRun(await coordinator.Create(request,key,requireIdle)));
        });
        app.MapGet("/api/v1/runs",(Store store,int offset=0,int limit=50)=>{if(offset<0||limit is <1 or >100)throw new ArgumentException("Invalid page range.");return store.Runs().Skip(offset).Take(limit).ToList();});
        app.MapGet("/api/v1/runs/{id}",(string id,Store store)=>store.Run(id));
        app.MapGet("/api/v1/runs/{id}/timings",(string id,Store store)=>{_ = store.Run(id);return store.SessionTimings(id);});
        app.MapGet("/api/v1/runs/{id}/provider-resources",(string id,Store store,PyreEngine engine)=>{var run=store.Run(id);return run.Config.ProviderId=="pyre"?engine.Resources(id):null;});
        app.MapGet("/api/v1/runs/{id}/notes",(string id,Store store)=>store.RunMatchupNotes(id));
        app.MapDelete("/api/v1/runs/{id}",async(string id,RunCoordinator coordinator)=>Reply(new DeletedRun(await coordinator.Delete(id))));
        app.MapPost("/api/v1/runs/{id}/cancel",async(string id,RunCoordinator coordinator)=>{await coordinator.Cancel(id);return Results.Accepted();});
        app.MapPost("/api/v1/runs/{id}/resume",async(string id,RunCoordinator coordinator)=>{await coordinator.Resume(id);return Results.Accepted();});
        app.MapPost("/api/v1/runs/{id}/retry",async(string id,RunCoordinator coordinator)=>{await coordinator.Resume(id,true);return Results.Accepted();});
        app.MapGet("/api/v1/runs/{id}/summary",(string id,Store store)=>Metrics.Summarize(store.Run(id),store.Slots(id)));
        app.MapGet("/api/v1/runs/{id}/mana-events",async(string id,LocalUiBackend backend,string? kind=null)=>{
            if(kind is not (null or "mana-starved" or "mana-flooded"))throw new ArgumentException("Unknown mana flag.");
            var log=await backend.ManaEvents(id);
            return log with{Incidents=log.Incidents.Where(row=>kind is null||row.Flag.Kind==kind).ToList()};
        });
        app.MapGet("/api/v1/runs/{id}/opening-hands",(string id,LocalUiBackend backend)=>backend.OpeningHands(id));
        app.MapGet("/api/v1/runs/{id}/games",(string id,Store store,int offset=0,int limit=50,string? status=null,string? mana=null)=>{
            if(offset<0||limit is <1 or >100)throw new ArgumentException("Invalid page range.");_ = store.Run(id);
            if(mana is not (null or "mana-starved" or "mana-flooded"))throw new ArgumentException("Unknown mana flag.");
            return store.Slots(id).Where(s=>(status is null||s.Status==status)&&(mana is null||s.Result?.ManaFlags?.Any(f=>f.Kind==mana)==true)).Skip(offset).Take(limit).ToList();
        });
        app.MapGet("/api/v1/runs/{run}/games/{id}/replay",async(string run,string id,Store store,ForgeEngine engine,int offset=0,int limit=100,bool audit=false)=>{
            var records=await engine.Replay(store.Slot(run,id),offset,limit);
            return audit?records:records.Select(r=>r with{Detail=r.Event is null?r.Detail:null,StateHash=null,State=r.State is null?null:r.State with{Players=r.State.Players.Select(p=>p with{Hand=[]}).ToList()}}).ToList();
        });
        app.MapGet("/api/v1/runs/{run}/games/{id}/replay-turns",async(string run,string id,Store store,ForgeEngine engine)=>
            await engine.ReplayTurns(store.Slot(run,id)));
        app.MapGet("/api/v1/runs/{run}/games/{id}/evidence",(string run,string id,Store store,PyreEngine engine)=>engine.Evidence(store.Slot(run,id)));
        app.MapGet("/api/v1/runs/{run}/games/{id}/pyre-trace",(string run,string id,Store store,PyreEngine engine)=>Results.Json(engine.ReadTrace(store.Slot(run,id)),Json.Info<System.Text.Json.JsonElement>()));
        app.MapGet("/api/v1/runs/{id}/trigger-summaries",async(string id,Store store,ForgeEngine engine)=>{
            return await engine.TriggerSummaries(store.Slots(id).Where(s=>s.Status=="completed"&&s.Result is not null));
        });
        app.MapGet("/api/v1/runs/{id}/export",(string id,Store store,string format="json")=>{
            var summary=Metrics.Summarize(store.Run(id),store.Slots(id));
            if(format=="json")return Results.File(Encoding.UTF8.GetBytes(Json.Encode(summary)),"application/json",$"{id}-exploratory-summary.json");
            if(format!="csv")throw new ArgumentException("Format must be json or csv.");
            string Q(string text)=>"\""+text.Replace("\"","\"\"")+"\"";
            var csv=new StringBuilder("run,engine_hash,adapter_hash,opening_policy,diagnostic,deck_a,deck_b,scheduled,valid,wins_a,wins_b,draws,failed,cancelled,play_a,play_b,win_rate_a,win_rate_b,attempt_failures\n");
            foreach(var m in summary.Matchups)csv.AppendLine(string.Join(",",Q(id),Q(summary.Run.Config.EngineHash),Q(summary.Run.Config.AdapterHash),Q(summary.Run.Config.OpeningPolicy),"true",Q(store.Deck(m.DeckA).Name+" / "+store.Deck(m.DeckA).SourceType),Q(store.Deck(m.DeckB).Name+" / "+store.Deck(m.DeckB).SourceType),m.Scheduled,m.Valid,m.WinsA,m.WinsB,m.Draws,m.Failed,m.Cancelled,m.PlayA,m.PlayB,m.WinRateA?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"",m.WinRateB?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"",m.AttemptFailures));
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv",$"{id}-exploratory-summary.csv");
        });
        app.MapGet("/api/v1/runs/{run}/games/{id}/replay-export",(string run,string id,Store store,AppPaths paths)=>{
            var slot=store.Slot(run,id);var attempt=slot.Attempts.LastOrDefault(a=>a.Status=="completed")??throw new InvalidOperationException("No complete replay.");
            var path=Path.Combine(paths.Replays,attempt.Replay!);if(!File.Exists(path)||ForgeEngine.FileHash(path)!=attempt.ReplayHash)throw new InvalidDataException("Replay is missing or corrupt.");
            return Results.File(path,"application/gzip",attempt.Replay);
        });
    }
    public static async Task Initialize(WebApplication app)
    {
        await app.Services.GetRequiredService<DeckImporter>().Import();
        var startupStore=app.Services.GetRequiredService<Store>();var startupEngine=app.Services.GetRequiredService<ForgeEngine>();
        foreach(var run in startupStore.Runs())foreach(var slot in startupStore.Slots(run.Id).Where(s=>s.Result is not null&&(s.Result.ManaDetectionVersion!=ManaAnalyzer.Version||s.Result.OpeningTrackingVersion is null))){
            try{await startupEngine.AnalyzeStoredDiagnostics(slot);}catch(Exception error)when(error is IOException or InvalidDataException){app.Logger.LogWarning("Stored diagnostics unavailable for {Game}: {Reason}",slot.Id,error.Message);}
        }
    }
}
