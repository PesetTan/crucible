using System.Net.Http.Json;
using System.Text.Json;

namespace DeckTester;

public sealed record DeckSnapshot(DeckManifest Manifest,DeckCoverage Coverage);
public sealed record CacheSnapshot(RunConfig Config,List<CachedGameKey> Keys);
public sealed record HandLog(string OpeningPolicy,string Interpretation,List<HandLogRow> Players);
public sealed record HandLogRow(string GameId,string DeckId,bool OnPlay,OpeningHand OpeningHand,string? TrackingVersion);
public sealed record ManaLog(string DefinitionVersion,string ManaStarved,string ManaFlooded,List<ManaLogRow> Incidents);
public sealed record ManaLogRow(string GameId,string DeckId,ManaFlag Flag,string? Winner,bool Draw);
public sealed record UiSnapshot(int Version,List<DeckSnapshot> Decks,Capabilities Capabilities,List<SimulationRun> Runs,
    Dictionary<string,List<SessionTiming>> Timings,List<ActiveSessionProgress> ActiveSessions,SessionTurnStats? TurnStats,
    List<TimingSample> TimingSamples,List<SessionTimingSample> SessionSamples,Dictionary<int,int> ManaValues,
    List<CacheSnapshot> Caches,List<ActiveSessionProgress> History,string? RunId,List<GameSlot> Slots,ResourceSnapshot Resources)
{
    public string ETag()=>"\""+DeckImporter.Hash(Json.Encode(this))+"\"";
    public static UiSnapshot ForSession(SimulationRun run,List<DeckSnapshot> decks,Capabilities capability,List<GameSlot> slots,List<SessionTiming> timings){
        var progress=new ActiveSessionProgress(run,slots.Count(s=>s.Status=="completed"),slots.Count(s=>s.Status=="failed"),
            slots.Count(s=>s.Status=="cancelled"),slots.Count(s=>s.Status=="running"),slots.Count(s=>s.Status=="pending"),slots.Count(s=>s.CachedFromRunId is not null));
        return new(1,decks,capability,[run],new(){[run.Id]=timings},run.Status is "queued" or "running" or "cancelling"?[progress]:[],null,
            [],[],[],[],[progress],run.Id,slots,new(0,null));
    }
}

public interface IUiBackend
{
    string? ConnectionError{get;}
    Task Refresh(string? runId=null,CancellationToken cancellation=default);
    Task<HistorySnapshot?> History(CancellationToken cancellation=default);
    Task<WorkshopSnapshot?> WorkshopState(CancellationToken cancellation=default);
    Task<LibrarySnapshot?> LibraryState(CancellationToken cancellation=default);
    List<DeckManifest> Decks();DeckManifest Deck(string id);List<SimulationRun> Runs();SimulationRun Run(string id);
    List<GameSlot> Slots(string run);GameSlot Slot(string run,string id);
    ActiveSessionProgress SessionProgress(string run);List<SessionTiming> SessionTimings(string run);List<ActiveSessionProgress> ActiveSessionProgress();
    SessionTurnStats? SessionTurnStatistics();HashSet<CachedGameKey> CachedGameKeys(RunConfig config);
    List<TimingSample> TimingSamples(string engine,string adapter);List<SessionTimingSample> SessionTimingSamples(string engine,string adapter);
    Capabilities Capabilities();DeckCoverage Coverage(DeckManifest deck);Dictionary<int,int> ReadManaValues();
    Task<List<DeckManifest>> Import();Task<ResourceSnapshot> Sample(CancellationToken cancellation=default);
    Task<string> Create(RunRequest request,string key,bool requireIdle=false);Task Cancel(string id);Task Resume(string id,bool retryFailures=false);Task<bool> Delete(string id);
    Task<List<ReplayRecord>> Replay(GameSlot slot,int offset,int limit,bool audit=false);
    Task<List<ReplayTurnPosition>> ReplayTurns(GameSlot slot);
    Task<List<TriggerGameSummary>> TriggerSummaries(string run,IReadOnlyCollection<string> gameIds);
    Task<HandLog> OpeningHands(string run);Task<ManaLog> ManaEvents(string run);Task<List<SessionTiming>> RuntimeLog(string run);
    Task<List<RunMatchupNote>> RunMatchupNotes(string run);
    Task<RunSummary> RunResults(string run,CancellationToken cancellation=default);
    Task<List<CardEntry>> SearchCards(string query);Task<DeckResolution> ResolveDeck(string text);
    Task<DeckValidation> ValidateDeck(CustomDeckRequest request);Task<DeckManifest> SaveCustomDeck(CustomDeckRequest request);
    Task<WorkshopMatchup> WorkshopMatchup(string run,string deck,string opponent,CancellationToken cancellation=default);

}

public sealed class LocalUiBackend(Store store,ForgeEngine engine,RunCoordinator coordinator,DeckImporter importer,ResourceCapacity resources,DeckWorkshop workshop,EngineProviders? providers=null):IUiBackend
{
    private readonly Lazy<Dictionary<int,int>> manaValues=new(importer.ReadManaValues);
    private Capabilities SnapshotCapabilities()=>engine.Capabilities();
    public string? ConnectionError=>null;
    public Task Refresh(string? runId=null,CancellationToken cancellation=default)=>Task.CompletedTask;
    public Task<LibrarySnapshot?> LibraryState(CancellationToken cancellation=default){
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult<LibrarySnapshot?>(new(1,store.Decks().Select(d=>new DeckSnapshot(d,engine.Coverage(d))).ToList(),ReadManaValues()));
    }
    public async Task<WorkshopSnapshot?> WorkshopState(CancellationToken cancellation=default){
        cancellation.ThrowIfCancellationRequested();
        var runs=store.Runs();
        return new(1,store.Decks(),runs,runs.Where(r=>r.Status is "queued" or "running" or "cancelling").Select(store.SessionProgress).ToList(),
            store.SessionTurnStatistics(),ReadManaValues(),await resources.Sample(cancellation));
    }
    public Task<HistorySnapshot?> History(CancellationToken cancellation=default){
        cancellation.ThrowIfCancellationRequested();
        var sessions=store.RunHistory();
        var deckIds=sessions.SelectMany(s=>s.Progress.Run.Config.DeckIds).ToHashSet();
        return Task.FromResult<HistorySnapshot?>(new(1,store.Decks().Where(d=>deckIds.Contains(d.Id)).ToList(),SnapshotCapabilities(),sessions));
    }
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
        return audit?records:records.Select(r=>r with{Detail=r.Event is null?r.Detail:null,Options=null,StateHash=null,State=r.State is null?null:r.State with{Players=r.State.Players.Select(p=>p with{Hand=[]}).ToList()}}).ToList();
    }
    public Task<List<ReplayTurnPosition>> ReplayTurns(GameSlot slot)=>engine.ReplayTurns(slot);
    public Task<List<TriggerGameSummary>> TriggerSummaries(string run,IReadOnlyCollection<string> gameIds)=>engine.TriggerSummaries(store.Slots(run).Where(s=>gameIds.Contains(s.Id)&&s.Status=="completed"&&s.Result is not null));
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
    public Task<List<RunMatchupNote>> RunMatchupNotes(string run)=>Task.FromResult(store.RunMatchupNotes(run));
    public Task<RunSummary> RunResults(string run,CancellationToken cancellation=default)=>Task.FromResult(Metrics.Summarize(store.Run(run),store.Slots(run)));
    public Task<List<CardEntry>> SearchCards(string query)=>Task.FromResult(workshop.Search(query));
    public Task<DeckResolution> ResolveDeck(string text)=>Task.FromResult(workshop.Resolve(text));
    public Task<DeckValidation> ValidateDeck(CustomDeckRequest request)=>Task.FromResult(workshop.Validate(request));
    public Task<DeckManifest> SaveCustomDeck(CustomDeckRequest request)=>workshop.Save(request);
    public Task<WorkshopMatchup> WorkshopMatchup(string run,string deck,string opponent,CancellationToken cancellation=default){cancellation.ThrowIfCancellationRequested();return Task.FromResult(workshop.Matchup(run,deck,opponent));}
    public async Task<UiSnapshot> Snapshot(string? runId,CancellationToken cancellation){
        cancellation.ThrowIfCancellationRequested();
        var capability=SnapshotCapabilities();
        if(runId is not null){
            // Results, logs and replay pages need only this session. Setup cache keys,
            // timing calibration and other sessions must not be loaded on this path.
            var run=store.Run(runId);var slots=store.Slots(runId);
            if(run.Config.ProviderId=="pyre"&&providers is not null)capability=providers.Capabilities("pyre");
            return UiSnapshot.ForSession(run,run.Config.DeckIds.Select(store.Deck).Select(d=>new DeckSnapshot(d,engine.Coverage(d))).ToList(),
                capability,slots,store.SessionTimings(runId));
        }
        var runs=store.Runs();
        var caches=runs.GroupBy(r=>(r.Config.ProviderId,r.Config.Provider?.ExperimentId,r.Config.Provider?.MetricDefinitionHash,r.Config.Provider?.ControllerDefinitionHash,r.Config.RootSeed,r.Config.EngineHash,r.Config.AdapterHash,r.Config.PerformanceMode,r.Config.Pilot,r.Config.OpeningPolicy,r.Config.SeedScheme,r.Config.AttributesHash))
            .Select(g=>new CacheSnapshot(g.First().Config,store.CachedGameKeys(g.First().Config).ToList())).ToList();
        return new(1,store.Decks().Select(d=>new DeckSnapshot(d,engine.Coverage(d))).ToList(),capability,runs,
            runs.ToDictionary(r=>r.Id,r=>store.SessionTimings(r.Id)),store.ActiveSessionProgress(),store.SessionTurnStatistics(),
            store.TimingSamples(capability.EngineHash,capability.AdapterHash),store.SessionTimingSamples(capability.EngineHash,capability.AdapterHash),
            ReadManaValues(),caches,runs.Select(store.SessionProgress).ToList(),runId,runId is null?[]:store.Slots(runId),await resources.Sample(cancellation));
    }
}
