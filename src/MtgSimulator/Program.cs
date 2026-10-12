using System.Security.Cryptography;
using System.Text;
using System.Net;
using MtgSimulator;
using MtgSimulator.Components;
using Microsoft.AspNetCore.DataProtection;

if(args.Contains("--setup",StringComparer.Ordinal))
{
    var directory=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT")??Directory.GetCurrentDirectory();
    while(!Directory.Exists(Path.Combine(directory,"docs","specs"))){directory=Path.GetDirectoryName(directory)??throw new ArgumentException("Run setup from the mtg-simulator project directory.");}
    await EngineSetup.Install(directory);return;
}

var mode=Environment.GetEnvironmentVariable("MTG_HOST_MODE")??"combined";
if(mode is not ("ui" or "service" or "combined"))throw new ArgumentException("MTG_HOST_MODE must be ui, service, or combined.");
var port=int.TryParse(Environment.GetEnvironmentVariable("MTG_PORT"),out var configuredPort)?configuredPort:mode=="service"?5079:5077;
if(port is <1024 or >65535)throw new ArgumentException("MTG_PORT must be between 1024 and 65535.");
var builder=WebApplication.CreateBuilder(args);
builder.Services.AddResponseCompression(options=>{options.EnableForHttps=true;options.MimeTypes=Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);});
builder.Configuration.AddJsonFile("branding.json", optional:false, reloadOnChange:true);
builder.Services.AddSingleton<BrandSettings>();
var lanAddress=mode=="service"?null:Environment.GetEnvironmentVariable("MTG_LAN_ADDRESS");
if(lanAddress is not null && (!IPAddress.TryParse(lanAddress,out var lanIp)||lanIp.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||
    !(lanIp.GetAddressBytes()[0]==10 || (lanIp.GetAddressBytes()[0]==172 && lanIp.GetAddressBytes()[1] is >=16 and <=31) || (lanIp.GetAddressBytes()[0]==192 && lanIp.GetAddressBytes()[1]==168))))
    throw new ArgumentException("MTG_LAN_ADDRESS must be a private IPv4 address assigned to this computer.");
var allowedHosts=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"127.0.0.1","localhost"};
if(lanAddress is not null)allowedHosts.Add(lanAddress);
builder.WebHost.UseUrls(lanAddress is null ? [$"http://127.0.0.1:{port}"] : [$"http://127.0.0.1:{port}",$"http://{lanAddress}:{port}"]);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
FileStream? serviceOwnership=null;
if(mode=="ui"){
    builder.Services.Configure<HostOptions>(options=>options.ShutdownTimeout=TimeSpan.FromSeconds(5));
    var backendUri=new Uri(Environment.GetEnvironmentVariable("MTG_BACKEND_URL")??"http://127.0.0.1:5079/");
    if(backendUri.Scheme!="http"||!backendUri.IsLoopback||backendUri.AbsolutePath!="/"||backendUri.Query.Length>0||backendUri.UserInfo.Length>0)
        throw new ArgumentException("MTG_BACKEND_URL must be an HTTP loopback origin.");
    builder.Services.AddHttpClient("simulation",client=>{client.BaseAddress=backendUri;client.Timeout=TimeSpan.FromSeconds(90);})
        .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All});
    builder.Services.AddScoped<IUiBackend>(services=>new RemoteUiBackend(services.GetRequiredService<IHttpClientFactory>().CreateClient("simulation")));
    var uiKeys=Path.Combine(builder.Environment.ContentRootPath,"ui-state","protection-keys");
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(uiKeys));
}else{
    var paths=new AppPaths(builder.Environment);
    try{serviceOwnership=new FileStream(Path.Combine(paths.Data,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    catch(IOException error){throw new InvalidOperationException("Another simulation host already owns this data directory. Stop that host before starting a replacement.",error);}
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Data,"protection-keys")));
    builder.Services.AddSingleton(paths);builder.Services.AddSingleton<Store>();builder.Services.AddSingleton<DeckImporter>();builder.Services.AddSingleton<ForgeEngine>();
    builder.Services.AddSingleton<ResourceCapacity>();builder.Services.AddSingleton<RunCoordinator>();builder.Services.AddHostedService(p=>p.GetRequiredService<RunCoordinator>());
    builder.Services.AddSingleton<LocalUiBackend>();builder.Services.AddSingleton<IUiBackend>(p=>p.GetRequiredService<LocalUiBackend>());
}
builder.Services.ConfigureHttpJsonOptions(options=>{options.SerializerOptions.PropertyNamingPolicy=Json.Options.PropertyNamingPolicy;});
var app=builder.Build();
app.Lifetime.ApplicationStopped.Register(()=>serviceOwnership?.Dispose());
app.UseResponseCompression();
app.MapGet("/branding.css", (BrandSettings branding, HttpContext context) => {
    context.Response.Headers.CacheControl="no-store";
    return Results.Text(branding.StyleSheet(), "text/css");
});
var instanceToken=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
app.Use(async(context,next)=>{
    if(!allowedHosts.Contains(context.Request.Host.Host)){context.Response.StatusCode=403;return;}
    var origin=context.Request.Headers.Origin.ToString();
    if(origin.Length>0&&(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||!string.Equals(uri.Scheme,context.Request.Scheme,StringComparison.OrdinalIgnoreCase)||!string.Equals(uri.Host,context.Request.Host.Host,StringComparison.OrdinalIgnoreCase)||uri.Port!=context.Request.Host.Port)) {context.Response.StatusCode=403;return;}
    if(context.Request.Path.StartsWithSegments("/api")&&context.Request.Method is not ("GET" or "HEAD")&&context.Request.Headers["X-Instance-Token"]!=instanceToken){context.Response.StatusCode=403;return;}
    try{await next();}
    catch(Exception error)when(error is KeyNotFoundException or ArgumentException or InvalidOperationException or InvalidDataException){
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=error is KeyNotFoundException?404:error is ArgumentException?422:409;
        await context.Response.WriteAsJsonAsync(new{code=context.Response.StatusCode,message=error.Message});
    }
});
app.UseStaticFiles();app.UseAntiforgery();app.MapStaticAssets();
if(mode=="ui"){
    app.MapGet("/api/{**path}",async(HttpContext context,IHttpClientFactory factory)=>{
        var path=context.Request.Path.Value!;
        if(!path.StartsWith("/api/v1/runs/",StringComparison.Ordinal)){context.Response.StatusCode=404;return;}
        if(path.EndsWith("/export",StringComparison.OrdinalIgnoreCase)||path.EndsWith("/replay-export",StringComparison.OrdinalIgnoreCase)){context.Response.StatusCode=404;return;}
        try{
            using var response=await factory.CreateClient("simulation").GetAsync(path.TrimStart('/')+context.Request.QueryString,HttpCompletionOption.ResponseHeadersRead,context.RequestAborted);
            context.Response.StatusCode=(int)response.StatusCode;
            context.Response.ContentType=response.Content.Headers.ContentType?.ToString();
            if(response.Content.Headers.ContentDisposition is {} disposition)context.Response.Headers.ContentDisposition=disposition.ToString();
            await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);
        }catch(HttpRequestException){context.Response.StatusCode=503;await context.Response.WriteAsync("Simulation service unavailable.");}
    });
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    await app.RunAsync();return;
}
app.MapGet("/api/v1/ui-state",async(string? runId,LocalUiBackend backend,HttpContext context)=>await backend.Snapshot(runId,context.RequestAborted));
app.MapGet("/api/v1/session",()=>new{instanceToken});
app.MapGet("/api/v1/capabilities",(ForgeEngine engine)=>engine.Capabilities());
app.MapGet("/api/v1/decks",(Store store,ForgeEngine engine)=>store.Decks().Select(d=>new{manifest=d,coverage=engine.Coverage(d)}));
app.MapGet("/api/v1/decks/{id}",(string id,Store store,ForgeEngine engine)=>new{manifest=store.Deck(id),coverage=engine.Coverage(store.Deck(id))});
app.MapPost("/api/v1/imports",async(DeckImporter importer)=>await importer.Import());
app.MapPost("/api/v1/runs/preflight",(RunRequest request,ForgeEngine engine,Store store)=>{
    var capabilities=engine.Capabilities();
    var config=new RunConfig(request.DeckIds,request.GamesPerPair,request.Workers,request.TimeoutSeconds,request.TurnLimit,request.RootSeed,request.Diagnostic,capabilities.EngineHash,capabilities.AdapterHash);
    var cached=store.CachedGameKeys(config).Count(key=>request.DeckIds.Contains(key.DeckA)&&request.DeckIds.Contains(key.DeckB)&&key.Index<request.GamesPerPair/2);
    var existing=store.ExistingSession(config);
    return new{blockers=engine.Preflight(request),limitations=capabilities.Limitations,cachedGames=cached,existingSessionId=existing?.Id,existingGamesPerPair=existing?.Config.GamesPerPair};
});
app.MapPost("/api/v1/runs",async(RunRequest request,HttpContext context,RunCoordinator coordinator,bool requireIdle=false)=>{
    var key=context.Request.Headers["Idempotency-Key"].ToString();if(key.Length is <1 or >128)throw new ArgumentException("Provide an Idempotency-Key of 1–128 characters.");
    return Results.Ok(new{id=await coordinator.Create(request,key,requireIdle)});
});
app.MapGet("/api/v1/runs",(Store store,int offset=0,int limit=50)=>{if(offset<0||limit is <1 or >100)throw new ArgumentException("Invalid page range.");return store.Runs().Skip(offset).Take(limit);});
app.MapGet("/api/v1/runs/{id}",(string id,Store store)=>store.Run(id));
app.MapGet("/api/v1/runs/{id}/timings",(string id,Store store)=>{_ = store.Run(id);return store.SessionTimings(id);});
app.MapDelete("/api/v1/runs/{id}",async(string id,RunCoordinator coordinator)=>Results.Ok(new{replayCleanupComplete=await coordinator.Delete(id)}));
app.MapPost("/api/v1/runs/{id}/cancel",async(string id,RunCoordinator coordinator)=>{await coordinator.Cancel(id);return Results.Accepted();});
app.MapPost("/api/v1/runs/{id}/resume",async(string id,RunCoordinator coordinator)=>{await coordinator.Resume(id);return Results.Accepted();});
app.MapPost("/api/v1/runs/{id}/retry",async(string id,RunCoordinator coordinator)=>{await coordinator.Resume(id,true);return Results.Accepted();});
app.MapGet("/api/v1/runs/{id}/summary",(string id,Store store)=>Metrics.Summarize(store.Run(id),store.Slots(id)));
app.MapGet("/api/v1/runs/{id}/mana-events",(string id,Store store,string? kind=null)=>{
    _ = store.Run(id);if(kind is not (null or "mana-starved" or "mana-flooded"))throw new ArgumentException("Unknown mana flag.");
    return new{definitionVersion=ManaAnalyzer.Version,manaStarved=ManaAnalyzer.StarvedDefinition,manaFlooded=ManaAnalyzer.FloodedDefinition,
        incidents=store.Slots(id).Where(s=>s.Result is not null).SelectMany(s=>(s.Result!.ManaFlags??[]).Where(f=>kind is null||f.Kind==kind).Select(f=>new{gameId=s.Id,deckId=f.Player=="A"?s.DeckA:s.DeckB,flag=f,winner=s.Result.Winner,draw=s.Result.Draw}))};
});
app.MapGet("/api/v1/runs/{id}/opening-hands",(string id,Store store)=>{
    var run=store.Run(id);return new{openingPolicy=run.Config.OpeningPolicy,interpretation="Mulligans before the pilot kept a hand; this is not a guarantee of playability.",
        players=store.Slots(id).Where(s=>s.Result?.OpeningHands is not null).SelectMany(s=>s.Result!.OpeningHands!.Select(h=>new{gameId=s.Id,deckId=h.Player=="A"?s.DeckA:s.DeckB,onPlay=s.Starter==(h.Player=="A"?s.DeckA:s.DeckB),openingHand=h,trackingVersion=s.Result.OpeningTrackingVersion}))};
});
app.MapGet("/api/v1/runs/{id}/games",(string id,Store store,int offset=0,int limit=50,string? status=null,string? mana=null)=>{
    if(offset<0||limit is <1 or >100)throw new ArgumentException("Invalid page range.");_ = store.Run(id);
    if(mana is not (null or "mana-starved" or "mana-flooded"))throw new ArgumentException("Unknown mana flag.");
    return store.Slots(id).Where(s=>(status is null||s.Status==status)&&(mana is null||s.Result?.ManaFlags?.Any(f=>f.Kind==mana)==true)).Skip(offset).Take(limit);
});
app.MapGet("/api/v1/runs/{run}/games/{id}/replay",async(string run,string id,Store store,ForgeEngine engine,int offset=0,int limit=100,bool audit=false)=>{
    var records=await engine.Replay(store.Slot(run,id),offset,limit);
    return audit?records:records.Select(r=>r with{Detail=r.Event is null?r.Detail:null,StateHash=null,State=r.State is null?null:r.State with{Players=r.State.Players.Select(p=>p with{Hand=[]}).ToList()}}).ToList();
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
if(mode=="combined")app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.Services.GetRequiredService<DeckImporter>().Import();
var startupStore=app.Services.GetRequiredService<Store>();var startupEngine=app.Services.GetRequiredService<ForgeEngine>();
foreach(var run in startupStore.Runs())foreach(var slot in startupStore.Slots(run.Id).Where(s=>s.Result is not null&&(s.Result.ManaDetectionVersion!=ManaAnalyzer.Version||s.Result.OpeningTrackingVersion is null))){
    try{await startupEngine.AnalyzeStoredDiagnostics(slot);}catch(Exception error)when(error is IOException or InvalidDataException){app.Logger.LogWarning("Stored diagnostics unavailable for {Game}: {Reason}",slot.Id,error.Message);}
}
await app.RunAsync();
