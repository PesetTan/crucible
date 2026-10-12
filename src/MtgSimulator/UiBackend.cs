using System.Net.Http.Json;
using System.Text.Json;

namespace MtgSimulator;

public sealed record DeckSnapshot(DeckManifest Manifest,DeckCoverage Coverage);
public sealed record CacheSnapshot(RunConfig Config,List<CachedGameKey> Keys);
public sealed record HandLog(string OpeningPolicy,string Interpretation,List<HandLogRow> Players);
public sealed record HandLogRow(string GameId,string DeckId,bool OnPlay,OpeningHand OpeningHand,string? TrackingVersion);
public sealed record ManaLog(string DefinitionVersion,string ManaStarved,string ManaFlooded,List<ManaLogRow> Incidents);
public sealed record ManaLogRow(string GameId,string DeckId,ManaFlag Flag,string? Winner,bool Draw);
public sealed record UiSnapshot(int Version,List<DeckSnapshot> Decks,Capabilities Capabilities,List<SimulationRun> Runs,
    Dictionary<string,List<SessionTiming>> Timings,List<ActiveSessionProgress> ActiveSessions,SessionTurnStats? TurnStats,
    List<TimingSample> TimingSamples,List<SessionTimingSample> SessionSamples,Dictionary<int,int> ManaValues,
    List<CacheSnapshot> Caches,List<ActiveSessionProgress> History,string? RunId,List<GameSlot> Slots,ResourceSnapshot Resources);

public interface IUiBackend
{
    string? ConnectionError{get;}
    Task Refresh(string? runId=null,CancellationToken cancellation=default);
    List<DeckManifest> Decks();DeckManifest Deck(string id);List<SimulationRun> Runs();SimulationRun Run(string id);
    List<GameSlot> Slots(string run);GameSlot Slot(string run,string id);
    ActiveSessionProgress SessionProgress(string run);List<SessionTiming> SessionTimings(string run);List<ActiveSessionProgress> ActiveSessionProgress();
    SessionTurnStats? SessionTurnStatistics();HashSet<CachedGameKey> CachedGameKeys(RunConfig config);
    List<TimingSample> TimingSamples(string engine,string adapter);List<SessionTimingSample> SessionTimingSamples(string engine,string adapter);
    Capabilities Capabilities();DeckCoverage Coverage(DeckManifest deck);Dictionary<int,int> ReadManaValues();
    Task<List<DeckManifest>> Import();Task<ResourceSnapshot> Sample(CancellationToken cancellation=default);
    Task<string> Create(RunRequest request,string key,bool requireIdle=false);Task Cancel(string id);Task Resume(string id,bool retryFailures=false);Task<bool> Delete(string id);
    Task<List<ReplayRecord>> Replay(GameSlot slot,int offset,int limit,bool audit=false);
    Task<HandLog> OpeningHands(string run);Task<ManaLog> ManaEvents(string run);Task<List<SessionTiming>> RuntimeLog(string run);
}

public sealed class LocalUiBackend(Store store,ForgeEngine engine,RunCoordinator coordinator,DeckImporter importer,ResourceCapacity resources):IUiBackend
{
    private readonly Lazy<Dictionary<int,int>> manaValues=new(importer.ReadManaValues);
    private readonly object capabilityGate=new();
    private Capabilities? snapshotCapabilities;
    private DateTimeOffset capabilitiesChecked;
    private Capabilities SnapshotCapabilities(){lock(capabilityGate){
        if(snapshotCapabilities is null||DateTimeOffset.UtcNow-capabilitiesChecked>TimeSpan.FromSeconds(30)){
            snapshotCapabilities=engine.Capabilities();capabilitiesChecked=DateTimeOffset.UtcNow;
        }
        return snapshotCapabilities;
    }}
    public string? ConnectionError=>null;
    public Task Refresh(string? runId=null,CancellationToken cancellation=default)=>Task.CompletedTask;
    public List<DeckManifest> Decks()=>store.Decks();public DeckManifest Deck(string id)=>store.Deck(id);
    public List<SimulationRun> Runs()=>store.Runs();public SimulationRun Run(string id)=>store.Run(id);
    public List<GameSlot> Slots(string run)=>store.Slots(run);public GameSlot Slot(string run,string id)=>store.Slot(run,id);
    public ActiveSessionProgress SessionProgress(string run)=>store.SessionProgress(run);
    public List<SessionTiming> SessionTimings(string run)=>store.SessionTimings(run);
    public List<ActiveSessionProgress> ActiveSessionProgress()=>store.ActiveSessionProgress();
    public SessionTurnStats? SessionTurnStatistics()=>store.SessionTurnStatistics();
    public HashSet<CachedGameKey> CachedGameKeys(RunConfig config)=>store.CachedGameKeys(config);
    public List<TimingSample> TimingSamples(string engineHash,string adapter)=>store.TimingSamples(engineHash,adapter);
    public List<SessionTimingSample> SessionTimingSamples(string engineHash,string adapter)=>store.SessionTimingSamples(engineHash,adapter);
    public Capabilities Capabilities()=>engine.Capabilities();public DeckCoverage Coverage(DeckManifest deck)=>engine.Coverage(deck);
    public Dictionary<int,int> ReadManaValues()=>manaValues.Value;public Task<List<DeckManifest>> Import()=>importer.Import();
    public Task<ResourceSnapshot> Sample(CancellationToken cancellation=default)=>resources.Sample(cancellation);
    public Task<string> Create(RunRequest request,string key,bool requireIdle=false)=>coordinator.Create(request,key,requireIdle);
    public Task Cancel(string id)=>coordinator.Cancel(id);public Task Resume(string id,bool retryFailures=false)=>coordinator.Resume(id,retryFailures);
    public Task<bool> Delete(string id)=>coordinator.Delete(id);
    public async Task<List<ReplayRecord>> Replay(GameSlot slot,int offset,int limit,bool audit=false){
        var records=await engine.Replay(slot,offset,limit);
        return audit?records:records.Select(r=>r with{Detail=r.Event is null?r.Detail:null,StateHash=null,State=r.State is null?null:r.State with{Players=r.State.Players.Select(p=>p with{Hand=[]}).ToList()}}).ToList();
    }
    public Task<HandLog> OpeningHands(string run){
        var policy=store.Run(run).Config.OpeningPolicy;
        var rows=store.Slots(run).Where(s=>s.Result?.OpeningHands is not null).SelectMany(s=>s.Result!.OpeningHands!.Select(h=>
            new HandLogRow(s.Id,h.Player=="A"?s.DeckA:s.DeckB,s.Starter==(h.Player=="A"?s.DeckA:s.DeckB),h,s.Result.OpeningTrackingVersion))).ToList();
        return Task.FromResult(new HandLog(policy,"Mulligans before the pilot kept a hand; this is not a guarantee of playability.",rows));
    }
    public Task<ManaLog> ManaEvents(string run){
        _=store.Run(run);
        var rows=store.Slots(run).Where(s=>s.Result is not null).SelectMany(s=>(s.Result!.ManaFlags??[]).Select(f=>
            new ManaLogRow(s.Id,f.Player=="A"?s.DeckA:s.DeckB,f,s.Result.Winner,s.Result.Draw))).ToList();
        return Task.FromResult(new ManaLog(ManaAnalyzer.Version,ManaAnalyzer.StarvedDefinition,ManaAnalyzer.FloodedDefinition,rows));
    }
    public Task<List<SessionTiming>> RuntimeLog(string run){_=store.Run(run);return Task.FromResult(store.SessionTimings(run));}
    public async Task<UiSnapshot> Snapshot(string? runId,CancellationToken cancellation){
        var capability=SnapshotCapabilities();var runs=store.Runs();
        var caches=runs.GroupBy(r=>(r.Config.RootSeed,r.Config.EngineHash,r.Config.AdapterHash,r.Config.Pilot,r.Config.OpeningPolicy,r.Config.SeedScheme))
            .Select(g=>new CacheSnapshot(g.First().Config,store.CachedGameKeys(g.First().Config).ToList())).ToList();
        return new(1,store.Decks().Select(d=>new DeckSnapshot(d,engine.Coverage(d))).ToList(),capability,runs,
            runs.ToDictionary(r=>r.Id,r=>store.SessionTimings(r.Id)),store.ActiveSessionProgress(),store.SessionTurnStatistics(),
            store.TimingSamples(capability.EngineHash,capability.AdapterHash),store.SessionTimingSamples(capability.EngineHash,capability.AdapterHash),
            ReadManaValues(),caches,runs.Select(store.SessionProgress).ToList(),runId,runId is null?[]:store.Slots(runId),await resources.Sample(cancellation));
    }
}

// Scoped to one UI circuit. All synchronous getters use a previously fetched snapshot;
// no database or engine services are registered in the UI host.
public sealed class RemoteUiBackend(HttpClient http):IUiBackend
{
    private UiSnapshot? snapshot;
    private readonly SemaphoreSlim refreshGate=new(1,1);
    public string? ConnectionError{get;private set;}
    public async Task Refresh(string? runId=null,CancellationToken cancellation=default){
        await refreshGate.WaitAsync(cancellation);
        try{
            var next=await http.GetFromJsonAsync<UiSnapshot>("api/v1/ui-state"+(runId is null?"":"?runId="+Uri.EscapeDataString(runId)),Json.Options,cancellation)
                ??throw new InvalidDataException("The simulation service returned an empty snapshot.");
            if(next.Version!=1)throw new InvalidDataException("The UI and simulation service use incompatible API versions.");
            snapshot=next;ConnectionError=null;
        }catch(Exception e)when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException){
            if(cancellation.IsCancellationRequested)throw;
            ConnectionError="The simulation service is unavailable. Session controls are disabled. Reconnecting automatically."+(snapshot is null?"":" Showing the last recorded results.");
        }finally{refreshGate.Release();}
    }
    public List<DeckManifest> Decks()=>snapshot?.Decks.Select(d=>d.Manifest).ToList()??[];
    public DeckManifest Deck(string id)=>Decks().SingleOrDefault(d=>d.Id==id)??throw new KeyNotFoundException("Deck not found.");
    public List<SimulationRun> Runs()=>snapshot?.Runs??[];public SimulationRun Run(string id)=>Runs().SingleOrDefault(r=>r.Id==id)??throw new KeyNotFoundException("Session not found.");
    public List<GameSlot> Slots(string run)=>snapshot?.RunId==run?snapshot.Slots:[];
    public GameSlot Slot(string run,string id)=>Slots(run).SingleOrDefault(s=>s.Id==id)??throw new KeyNotFoundException("Game not found.");
    public ActiveSessionProgress SessionProgress(string run)=>snapshot?.History.SingleOrDefault(p=>p.Run.Id==run)??throw new KeyNotFoundException("Session not found.");
    public List<SessionTiming> SessionTimings(string run)=>snapshot?.Timings.GetValueOrDefault(run)??[];
    public List<ActiveSessionProgress> ActiveSessionProgress()=>snapshot?.ActiveSessions??[];
    public SessionTurnStats? SessionTurnStatistics()=>snapshot?.TurnStats;
    public HashSet<CachedGameKey> CachedGameKeys(RunConfig config)=>snapshot?.Caches.Where(c=>SameCache(c.Config,config)).SelectMany(c=>c.Keys).ToHashSet()??[];
    private static bool SameCache(RunConfig a,RunConfig b)=>a.RootSeed==b.RootSeed&&a.EngineHash==b.EngineHash&&a.AdapterHash==b.AdapterHash&&a.Pilot==b.Pilot&&a.OpeningPolicy==b.OpeningPolicy&&a.SeedScheme==b.SeedScheme;
    public List<TimingSample> TimingSamples(string engine,string adapter)=>snapshot?.Capabilities.EngineHash==engine&&snapshot.Capabilities.AdapterHash==adapter?snapshot.TimingSamples:[];
    public List<SessionTimingSample> SessionTimingSamples(string engine,string adapter)=>snapshot?.Capabilities.EngineHash==engine&&snapshot.Capabilities.AdapterHash==adapter?snapshot.SessionSamples:[];
    public Capabilities Capabilities()=>snapshot?.Capabilities??new(false,"Unavailable","missing","missing","",[],[]);
    public DeckCoverage Coverage(DeckManifest deck)=>snapshot?.Decks.Single(d=>d.Manifest.Id==deck.Id).Coverage??throw new InvalidOperationException("Deck coverage unavailable.");
    public Dictionary<int,int> ReadManaValues()=>snapshot?.ManaValues??[];
    public Task<ResourceSnapshot> Sample(CancellationToken cancellation=default)=>Task.FromResult(snapshot?.Resources??new(0,null));
    private async Task<HttpResponseMessage> Mutation(HttpMethod method,string path,object? body=null,string? key=null){
        // Acquire the current service token per action so a service restart does not
        // leave an otherwise healthy UI with an expired instance credential.
        using var session=await http.GetFromJsonAsync<JsonDocument>("api/v1/session");
        using var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-Instance-Token",session!.RootElement.GetProperty("instanceToken").GetString());
        if(key is not null)request.Headers.Add("Idempotency-Key",key);
        if(body is not null)request.Content=JsonContent.Create(body,options:Json.Options);
        var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode){var text=await response.Content.ReadAsStringAsync();response.Dispose();throw new InvalidOperationException(text);}
        return response;
    }
    public async Task<List<DeckManifest>> Import(){using var response=await Mutation(HttpMethod.Post,"api/v1/imports");return (await response.Content.ReadFromJsonAsync<List<DeckManifest>>(Json.Options))!;}
    public async Task<string> Create(RunRequest request,string key,bool requireIdle=false){using var response=await Mutation(HttpMethod.Post,"api/v1/runs?requireIdle="+requireIdle.ToString().ToLowerInvariant(),request,key);using var result=await response.Content.ReadFromJsonAsync<JsonDocument>();return result!.RootElement.GetProperty("id").GetString()!;}
    public async Task Cancel(string id){using var response=await Mutation(HttpMethod.Post,$"api/v1/runs/{Uri.EscapeDataString(id)}/cancel");}
    public async Task Resume(string id,bool retryFailures=false){using var response=await Mutation(HttpMethod.Post,$"api/v1/runs/{Uri.EscapeDataString(id)}/{(retryFailures?"retry":"resume")}");}
    public async Task<bool> Delete(string id){using var response=await Mutation(HttpMethod.Delete,$"api/v1/runs/{Uri.EscapeDataString(id)}");using var result=await response.Content.ReadFromJsonAsync<JsonDocument>();return result!.RootElement.GetProperty("replayCleanupComplete").GetBoolean();}
    public async Task<List<ReplayRecord>> Replay(GameSlot slot,int offset,int limit,bool audit=false)=>
        await http.GetFromJsonAsync<List<ReplayRecord>>($"api/v1/runs/{Uri.EscapeDataString(slot.RunId)}/games/{Uri.EscapeDataString(slot.Id)}/replay?offset={offset}&limit={limit}&audit={audit.ToString().ToLowerInvariant()}",Json.Options)??[];
    public async Task<HandLog> OpeningHands(string run)=>await http.GetFromJsonAsync<HandLog>($"api/v1/runs/{Uri.EscapeDataString(run)}/opening-hands",Json.Options)??throw new InvalidDataException("Empty hand log.");
    public async Task<ManaLog> ManaEvents(string run)=>await http.GetFromJsonAsync<ManaLog>($"api/v1/runs/{Uri.EscapeDataString(run)}/mana-events",Json.Options)??throw new InvalidDataException("Empty mana log.");
    public async Task<List<SessionTiming>> RuntimeLog(string run)=>await http.GetFromJsonAsync<List<SessionTiming>>($"api/v1/runs/{Uri.EscapeDataString(run)}/timings",Json.Options)??throw new InvalidDataException("Empty runtime log.");
}
