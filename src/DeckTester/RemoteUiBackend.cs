using System.Net.Http.Json;
using System.Text.Json;

namespace DeckTester;

// Scoped to one UI circuit. All synchronous getters use a previously fetched snapshot;
// no database or engine services are registered in the UI host.
public sealed class RemoteUiBackend(HttpClient http,AppPaths paths):IUiBackend
{
    private UiSnapshot? snapshot;
    private HistorySnapshot? history;
    private WorkshopSnapshot? workshopState;
    private LibrarySnapshot? libraryState;
    private string? snapshotTag;
    private string? libraryTag;
    private string? historyTag;
    private readonly SemaphoreSlim refreshGate=new(1,1);
    public string? ConnectionError{get;private set;}
    public async Task<LibrarySnapshot?> LibraryState(CancellationToken cancellation=default){
        try{
            using var request=new HttpRequestMessage(HttpMethod.Get,"api/v1/library-state");
            if(libraryTag is not null)request.Headers.TryAddWithoutValidation("If-None-Match",libraryTag);
            using var response=await http.SendAsync(request,cancellation);
            if(response.StatusCode==System.Net.HttpStatusCode.NotModified&&libraryState is not null){ConnectionError=null;return libraryState;}
            LibrarySnapshot next;
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound){
                // Keep the UI available while an older service finishes active games.
                var previous=await http.GetFromJsonAsync<UiSnapshot>("api/v1/ui-state",Json.Options,cancellation)
                    ??throw new InvalidDataException("The simulation service returned empty state.");
                if(previous.Version!=1)throw new InvalidDataException("Incompatible service API version.");
                next=new(1,previous.Decks,previous.ManaValues);
            }else{
                response.EnsureSuccessStatusCode();
                next=await response.Content.ReadFromJsonAsync<LibrarySnapshot>(Json.Options,cancellation)
                    ??throw new InvalidDataException("The simulation service returned empty library state.");
            }
            if(next.Version!=1)throw new InvalidDataException("The UI and service use incompatible library API versions.");
            libraryState=next;libraryTag=response.Headers.ETag?.ToString();ConnectionError=null;
        }catch(Exception e)when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException){
            if(cancellation.IsCancellationRequested)throw;
            ConnectionError="The simulation service is unavailable. Reconnecting automatically."+(libraryState is null?"":" Showing the last recorded library.");
        }
        return libraryState;
    }
    public async Task<WorkshopSnapshot?> WorkshopState(CancellationToken cancellation=default){
        try{
            using var response=await http.GetAsync("api/v1/workshop-state",cancellation);
            WorkshopSnapshot next;
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound){
                // An older service may still own active games while the UI is updated.
                var previous=await http.GetFromJsonAsync<UiSnapshot>("api/v1/ui-state",Json.Options,cancellation)
                    ??throw new InvalidDataException("The simulation service returned empty state.");
                if(previous.Version!=1)throw new InvalidDataException("Incompatible service API version.");
                next=new(1,previous.Decks.Select(d=>d.Manifest).ToList(),previous.Runs,previous.ActiveSessions,previous.TurnStats,previous.ManaValues,previous.Resources);
            }else{
                response.EnsureSuccessStatusCode();
                next=await response.Content.ReadFromJsonAsync<WorkshopSnapshot>(Json.Options,cancellation)
                    ??throw new InvalidDataException("The simulation service returned empty Workshop state.");
            }
            if(next.Version!=1)throw new InvalidDataException("The UI and simulation service use incompatible Workshop API versions.");
            workshopState=next;ConnectionError=null;
        }catch(Exception e)when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException){
            if(cancellation.IsCancellationRequested)throw;
            ConnectionError="The simulation service is unavailable. Session controls are disabled. Reconnecting automatically."+(workshopState is null?"":" Showing the last recorded Workshop state.");
        }
        return workshopState;
    }
    public async Task<HistorySnapshot?> History(CancellationToken cancellation=default){
        try{
            using var request=new HttpRequestMessage(HttpMethod.Get,"api/v1/run-history");
            if(historyTag is not null)request.Headers.TryAddWithoutValidation("If-None-Match",historyTag);
            using var response=await http.SendAsync(request,cancellation);
            if(response.StatusCode==System.Net.HttpStatusCode.NotModified&&history is not null){ConnectionError=null;return history;}
            response.EnsureSuccessStatusCode();
            var next=await response.Content.ReadFromJsonAsync<HistorySnapshot>(Json.Options,cancellation)
                ??throw new InvalidDataException("The simulation service returned empty history.");
            if(next.Version!=1)throw new InvalidDataException("The UI and simulation service use incompatible history API versions.");
            history=next;historyTag=response.Headers.ETag?.ToString();ConnectionError=null;
        }catch(Exception e)when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException){
            if(cancellation.IsCancellationRequested)throw;
            ConnectionError="The simulation service is unavailable. Reconnecting automatically."+(history is null?"":" Showing the last recorded results.");
        }
        return history;
    }
    private bool legacySessionState;
    private async Task<UiSnapshot> ReadLegacySession(string runId,CancellationToken cancellation){
        // Update the UI while an older simulation service keeps its active games.
        var prefix=$"api/v1/runs/{Uri.EscapeDataString(runId)}";
        var run=await http.GetFromJsonAsync<SimulationRun>(prefix,Json.Options,cancellation)??throw new InvalidDataException("Empty session.");
        var capabilityTask=http.GetFromJsonAsync<Capabilities>("api/v1/capabilities",Json.Options,cancellation);
        var timingTask=http.GetFromJsonAsync<List<SessionTiming>>(prefix+"/timings",Json.Options,cancellation);
        var deckTasks=run.Config.DeckIds.Select(id=>http.GetFromJsonAsync<DeckSnapshot>($"api/v1/decks/{Uri.EscapeDataString(id)}",Json.Options,cancellation)).ToArray();
        await Task.WhenAll(deckTasks.Cast<Task>().Append(capabilityTask).Append(timingTask));
        var slots=new List<GameSlot>();
        // The existing games endpoint permits at most 100 slots per page.
        for(var offset=0;offset<run.Total;offset+=100)
            slots.AddRange(await http.GetFromJsonAsync<List<GameSlot>>($"{prefix}/games?offset={offset}&limit=100",Json.Options,cancellation)??throw new InvalidDataException("Empty game page."));
        var decks=await Task.WhenAll(deckTasks);
        return UiSnapshot.ForSession(run,decks.Select(d=>d??throw new InvalidDataException("Empty deck.")).ToList(),
            await capabilityTask??throw new InvalidDataException("Empty capabilities."),slots,await timingTask??throw new InvalidDataException("Empty timings."));
    }
    public async Task Refresh(string? runId=null,CancellationToken cancellation=default){
        await refreshGate.WaitAsync(cancellation);
        try{
            if(runId is not null&&legacySessionState){
                var legacy=await ReadLegacySession(runId,cancellation);var tag=legacy.ETag();
                if(snapshot?.RunId!=runId||snapshotTag!=tag){snapshot=legacy;snapshotTag=tag;}
                ConnectionError=null;return;
            }
            using var request=new HttpRequestMessage(HttpMethod.Get,runId is null?"api/v1/ui-state":$"api/v1/runs/{Uri.EscapeDataString(runId)}/state");
            if(snapshot is not null&&snapshot.RunId==runId&&snapshotTag is not null)
                request.Headers.TryAddWithoutValidation("If-None-Match",snapshotTag);
            using var response=await http.SendAsync(request,cancellation);
            if(runId is not null&&response.StatusCode==System.Net.HttpStatusCode.NotFound){
                var legacy=await ReadLegacySession(runId,cancellation);snapshot=legacy;snapshotTag=legacy.ETag();legacySessionState=true;
                ConnectionError=null;return;
            }
            if(response.StatusCode==System.Net.HttpStatusCode.NotModified&&snapshot is not null&&snapshot.RunId==runId){ConnectionError=null;return;}
            response.EnsureSuccessStatusCode();
            var next=await response.Content.ReadFromJsonAsync<UiSnapshot>(Json.Options,cancellation)
                ??throw new InvalidDataException("The simulation service returned an empty snapshot.");
            if(next.Version!=1)throw new InvalidDataException("The UI and simulation service use incompatible API versions.");
            snapshot=next;snapshotTag=response.Headers.ETag?.ToString();ConnectionError=null;
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
    private static bool SameCache(RunConfig a,RunConfig b)=>a.RootSeed==b.RootSeed&&a.EngineHash==b.EngineHash&&a.AdapterHash==b.AdapterHash&&a.PerformanceMode==b.PerformanceMode&&a.Pilot==b.Pilot&&a.OpeningPolicy==b.OpeningPolicy&&a.SeedScheme==b.SeedScheme&&a.AttributesHash==b.AttributesHash&&a.ProtocolSet?.Hash==b.ProtocolSet?.Hash;
    public List<TimingSample> TimingSamples(string engine,string adapter)=>snapshot?.Capabilities.EngineHash==engine&&snapshot.Capabilities.AdapterHash==adapter?snapshot.TimingSamples:[];
    public List<SessionTimingSample> SessionTimingSamples(string engine,string adapter)=>snapshot?.Capabilities.EngineHash==engine&&snapshot.Capabilities.AdapterHash==adapter?snapshot.SessionSamples:[];
    public Capabilities Capabilities()=>snapshot?.Capabilities??new(false,"Unavailable","missing","missing","",[],[]);
    public DeckCoverage Coverage(DeckManifest deck)=>snapshot?.Decks.Single(d=>d.Manifest.Id==deck.Id).Coverage??throw new InvalidOperationException("Deck coverage unavailable.");
    public Dictionary<int,int> ReadManaValues()=>snapshot?.ManaValues??[];
    public async Task<ResourceSnapshot> Sample(CancellationToken cancellation=default){
        try{
            using var response=await http.GetAsync("api/v1/resources",cancellation);
            // Older services include capacity in the setup snapshot.
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return snapshot?.Resources??new(0,null);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ResourceSnapshot>(Json.Options,cancellation)??new(0,null);
        }catch(Exception e)when(e is HttpRequestException or TaskCanceledException or JsonException){
            if(cancellation.IsCancellationRequested)throw;
            return new(0,null);
        }
    }
    private async Task<HttpResponseMessage> Mutation(HttpMethod method,string path,object? body=null,string? key=null){
        // Acquire the current service token per action so a service restart does not
        // leave an otherwise healthy UI with an expired instance credential.
        using var session=await http.GetFromJsonAsync<JsonDocument>("api/v1/session");
        using var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-Instance-Token",session!.RootElement.GetProperty("instanceToken").GetString());
        if(key is not null)request.Headers.Add("Idempotency-Key",key);
        if(body is not null)request.Content=JsonContent.Create(body,options:Json.Options);
        var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode){var text=await response.Content.ReadAsStringAsync();response.Dispose();try{using var error=JsonDocument.Parse(text);text=error.RootElement.GetProperty("message").GetString()??text;}catch(JsonException){}throw new InvalidOperationException(text);}
        return response;
    }
    public async Task<List<DeckManifest>> Import(){using var response=await Mutation(HttpMethod.Post,"api/v1/imports");return (await response.Content.ReadFromJsonAsync<List<DeckManifest>>(Json.Options))!;}
    public async Task<string> Create(RunRequest request,string key,bool requireIdle=false){using var response=await Mutation(HttpMethod.Post,"api/v1/runs?requireIdle="+requireIdle.ToString().ToLowerInvariant(),request,key);using var result=await response.Content.ReadFromJsonAsync<JsonDocument>();return result!.RootElement.GetProperty("id").GetString()!;}
    public async Task Cancel(string id){using var response=await Mutation(HttpMethod.Post,$"api/v1/runs/{Uri.EscapeDataString(id)}/cancel");}
    public async Task Resume(string id,bool retryFailures=false){using var response=await Mutation(HttpMethod.Post,$"api/v1/runs/{Uri.EscapeDataString(id)}/{(retryFailures?"retry":"resume")}");}
    public async Task<bool> Delete(string id){using var response=await Mutation(HttpMethod.Delete,$"api/v1/runs/{Uri.EscapeDataString(id)}");using var result=await response.Content.ReadFromJsonAsync<JsonDocument>();return result!.RootElement.GetProperty("replayCleanupComplete").GetBoolean();}
    public async Task<List<ReplayRecord>> Replay(GameSlot slot,int offset,int limit,bool audit=false)=>
        await http.GetFromJsonAsync<List<ReplayRecord>>($"api/v1/runs/{Uri.EscapeDataString(slot.RunId)}/games/{Uri.EscapeDataString(slot.Id)}/replay?offset={offset}&limit={limit}&audit={audit.ToString().ToLowerInvariant()}",Json.Options)??[];
    public Task<List<ReplayTurnPosition>> ReplayTurns(GameSlot slot)=>ForgeEngine.ReadReplayTurns(paths.Replays,slot);
    public Task<List<TriggerGameSummary>> TriggerSummaries(string run,IReadOnlyCollection<string> gameIds)=>ForgeEngine.ReadTriggerSummaries(paths.Replays,Slots(run).Where(s=>gameIds.Contains(s.Id)&&s.Status=="completed"&&s.Result is not null));
    public async Task<HandLog> OpeningHands(string run)=>await http.GetFromJsonAsync<HandLog>($"api/v1/runs/{Uri.EscapeDataString(run)}/opening-hands",Json.Options)??throw new InvalidDataException("Empty hand log.");
    public async Task<ManaLog> ManaEvents(string run)=>await http.GetFromJsonAsync<ManaLog>($"api/v1/runs/{Uri.EscapeDataString(run)}/mana-events",Json.Options)??throw new InvalidDataException("Empty mana log.");
    public async Task<List<SessionTiming>> RuntimeLog(string run)=>await http.GetFromJsonAsync<List<SessionTiming>>($"api/v1/runs/{Uri.EscapeDataString(run)}/timings",Json.Options)??throw new InvalidDataException("Empty runtime log.");
    public async Task<List<RunMatchupNote>> RunMatchupNotes(string run)=>await http.GetFromJsonAsync<List<RunMatchupNote>>($"api/v1/runs/{Uri.EscapeDataString(run)}/notes",Json.Options)??throw new InvalidDataException("Empty run notes.");
    public async Task<RunSummary> RunResults(string run,CancellationToken cancellation=default)=>await http.GetFromJsonAsync<RunSummary>($"api/v1/runs/{Uri.EscapeDataString(run)}/summary",Json.Options,cancellation)??throw new InvalidDataException("Empty run summary.");
    public async Task<List<CardEntry>> SearchCards(string query)=>await http.GetFromJsonAsync<List<CardEntry>>("api/v1/cards/search?q="+Uri.EscapeDataString(query),Json.Options)??[];
    public async Task<DeckResolution> ResolveDeck(string text){using var response=await Mutation(HttpMethod.Post,"api/v1/decks/resolve",new ArenaListRequest(text));return (await response.Content.ReadFromJsonAsync<DeckResolution>(Json.Options))!;}
    public async Task<DeckValidation> ValidateDeck(CustomDeckRequest request){using var response=await Mutation(HttpMethod.Post,"api/v1/decks/validate",request);return (await response.Content.ReadFromJsonAsync<DeckValidation>(Json.Options))!;}
    public async Task<DeckManifest> SaveCustomDeck(CustomDeckRequest request){using var response=await Mutation(HttpMethod.Post,"api/v1/decks/custom",request);return (await response.Content.ReadFromJsonAsync<DeckManifest>(Json.Options))!;}
    public async Task<WorkshopMatchup> WorkshopMatchup(string run,string deck,string opponent,CancellationToken cancellation=default)=>await http.GetFromJsonAsync<WorkshopMatchup>($"api/v1/runs/{Uri.EscapeDataString(run)}/workshop?deckId={Uri.EscapeDataString(deck)}&opponentId={Uri.EscapeDataString(opponent)}",Json.Options,cancellation)??throw new InvalidDataException("Empty workshop comparison.");

}
