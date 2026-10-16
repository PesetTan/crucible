using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Diagnostics;

namespace DeckTester;

public sealed class RunCoordinator(Store store,ForgeEngine engine,ILogger<RunCoordinator> logger,AppPaths paths,CardAttributes? attributes = null,SimulationExecutionGate? executionGate = null) : BackgroundService
{
    private readonly Channel<string> queue=Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string,byte> queued=new();
    private readonly ConcurrentDictionary<string,CancellationTokenSource> active=new();
    private readonly SemaphoreSlim mutations=new(1,1);
    private int ready;
    public bool IsReady=>Volatile.Read(ref ready)==1;
    public int ActiveCount=>active.Count;
    public int QueuedCount=>Math.Max(0,queued.Count-active.Count);
    private void Enqueue(string id){if(queued.TryAdd(id,0))queue.Writer.TryWrite(id);}
    public async Task<string> Create(RunRequest request,string key,bool requireIdle=false)
    {
        await mutations.WaitAsync();try {
        if(requireIdle&&(active.Count>0||store.Runs().Any(r=>r.Status is "queued" or "running" or "cancelling")))
            throw new InvalidOperationException("A session is active. Wait for it to finish or pause it from session details before setting up another run.");
        var blockers=engine.Preflight(request);if(blockers.Count!=0)throw new InvalidOperationException(string.Join("\n",blockers));
        if(request.WorkshopDeckId is {} workshopDeck&&(request.DeckIds.Count!=2||!request.DeckIds.Contains(workshopDeck)))
            throw new ArgumentException("Workshop deck must belong to a two-deck session.");
        if(request.WorkshopBaseline is {} baseline){
            var original=store.Run(baseline.RunId);
            if(baseline.DeckId==baseline.OpponentId||!original.Config.DeckIds.Contains(baseline.DeckId)||!original.Config.DeckIds.Contains(baseline.OpponentId)||request.DeckIds.Count!=2||!request.DeckIds.Contains(baseline.OpponentId))
                throw new ArgumentException("Workshop baseline must identify the original matchup and the retained opponent.");
        }
        var capability=engine.Capabilities();var decks=request.DeckIds.Order(StringComparer.Ordinal).ToList();
        var attributeSnapshot=attributes?.Snapshot(decks.Select(store.Deck));
        var optimizerPolicy=engine.OptimizerPolicy(request);
        var config=new RunConfig(decks,request.GamesPerPair,request.Workers,request.TimeoutSeconds,request.TurnLimit,request.RootSeed,request.Diagnostic,capability.EngineHash,capability.AdapterHash,
            Pilot:request.PilotMode,StaggerStarts:request.StaggerStarts,EstimatedGameSeconds:request.EstimatedGameSeconds,StartLeadSeconds:request.StartLeadSeconds,
            StartupSpacingSeconds:request.StartupSpacingSeconds,PerformanceMode:request.PerformanceMode,
            PilotProfiles:decks.ToDictionary(deck=>deck,deck=>ReanimatorPilots.Profile(store.Deck(deck) with{Cards=store.Deck(deck).Cards.Select(c=>c with{Attributes=attributeSnapshot?.Cards.GetValueOrDefault(c.Name)??c.Attributes}).ToList()},request.PilotMode)))
            {CardAttributes=attributeSnapshot?.Cards,AttributesHash=attributeSnapshot?.Hash,OptimizerPolicy=optimizerPolicy,
                ProtocolSet=request.PilotMode==ReanimatorPilots.EngagementMode||optimizerPolicy is not null&&capability.Protocols?.SupportsOptimizerProfile(optimizerPolicy.Profile)==true?capability.Protocols:null};
        var id=Guid.NewGuid().ToString("N");var slots=new List<GameSlot>();var seeds=new HashSet<long>();
        for(var a=0;a<decks.Count;a++)for(var b=a+1;b<decks.Count;b++)for(var starter=0;starter<2;starter++)for(var index=0;index<request.GamesPerPair/2;index++){
            var digest=SHA256.HashData(Encoding.UTF8.GetBytes(Json.Encode(new SeedFingerprint("sha256-v1",request.RootSeed,decks[a],decks[b],starter,index))));
            var seed=BinaryPrimitives.ReadInt64BigEndian(digest);if(!seeds.Add(seed))throw new InvalidOperationException("Seed collision detected.");
            slots.Add(new(Guid.NewGuid().ToString("N"),id,decks[a],decks[b],decks[starter==0?a:b],seed,index,"pending",[]));
        }
        var cache=store.CachedGames(config);
        slots=slots.Select(slot=>Reuse(slot,cache)).ToList();
        var run=new SimulationRun(id,DateTimeOffset.UtcNow,slots.All(slot=>slot.Status=="completed")?"completed":"queued",config,slots.Count){WorkshopBaseline=request.WorkshopBaseline,WorkshopDeckId=request.WorkshopDeckId};
        var actualId=await store.CreateRun(run,slots,key,DeckImporter.Hash(request.WorkshopDeckId is null&&request.WorkshopBaseline is null?Json.Encode(config):Json.Encode(new WorkshopRunFingerprint(config,request.WorkshopBaseline,request.WorkshopDeckId))));
        if(actualId==id&&run.Status=="completed"){
            var now=DateTimeOffset.UtcNow;
            await store.SaveSessionTiming(new(Guid.NewGuid().ToString("N"),id,now,now,now,0,0,0,"completed"));
        }
        if(store.Run(actualId).Status=="queued")Enqueue(actualId);return actualId;
        }finally{mutations.Release();}
    }
    public async Task<bool> Delete(string id)
    {
        await mutations.WaitAsync();
        try {
            if(active.ContainsKey(id))throw new InvalidOperationException("Pause the session and wait for it to stop before deleting it.");
            var files=await store.DeleteRun(id);queued.TryRemove(id,out _);var cleanupComplete=true;
            foreach(var file in files){
                if(Path.GetFileName(file)!=file){cleanupComplete=false;continue;}
                try{File.Delete(Path.Combine(paths.Replays,file));}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException){
                    cleanupComplete=false;logger.LogWarning(error,"Session {Run} was deleted but replay file {File} could not be removed",id,file);
                }
            }
            return cleanupComplete;
        }finally{mutations.Release();}
    }
    private GameSlot Reuse(GameSlot slot,Dictionary<CachedGameKey,GameSlot> cache)
    {
        if(!cache.TryGetValue(Store.CacheKey(slot),out var source)||!engine.HasReusableReplay(source))return slot;
        return slot with{Status="completed",Result=source.Result,Attempts=[source.Attempts.Last(a=>a.Status=="completed")],
            CachedFromRunId=source.CachedFromRunId??source.RunId,CachedFromGameId=source.CachedFromGameId??source.Id};
    }
    public async Task Cancel(string id)
    {
        await mutations.WaitAsync();try {
            var run=store.Run(id);if(run.Status is "completed" or "cancelled" or "failed")return;
            if(active.TryGetValue(id,out var cancellation)){await store.SaveRun(run with{Status="cancelling"});cancellation.Cancel();}
            else {foreach(var slot in store.Slots(id).Where(s=>s.Status=="pending"))await store.SaveSlot(slot with{Status="cancelled"});await store.SaveRun(run with{Status="cancelled"});}
        }finally{mutations.Release();}
    }
    public async Task Resume(string id,bool retryFailures=false)
    {
        await mutations.WaitAsync();try {
            var run=store.Run(id);if(active.ContainsKey(id)||run.Status is "running" or "queued" or "cancelling")throw new InvalidOperationException("Run is already active.");
            if(!engine.HasBundle(run.Config))throw new InvalidOperationException("The run's pinned Forge bundle is unavailable.");
            foreach(var slot in store.Slots(id))if(slot.Result is null&&(slot.Status is "pending" or "cancelled"||retryFailures&&slot.Status=="failed")) {
                if(!AttemptPolicy.CanResume(slot))continue;await store.SaveSlot(slot with{Status="pending"});
            }
            if(!store.Slots(id).Any(s=>s.Status=="pending"))throw new InvalidOperationException("No resumable slots; failed attempts require explicit retry (maximum four failed attempts per game).");
            await store.SaveRun(run with{Status="queued",Error=null});Enqueue(id);
        }finally{mutations.Release();}
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach(var run in store.Runs()) {
            foreach(var timing in store.SessionTimings(run.Id).Where(t=>t.Status=="running"))
                await store.SaveSessionTiming(timing with{Status="interrupted",FinishedAt=timing.UpdatedAt});
            var slots=store.Slots(run.Id);
            // A stale terminal run status must not hide abandoned work from recovery.
            if(run.Status is not ("running" or "cancelling" or "queued")&&!slots.Any(s=>s.Status=="running")&&
                !(run.Status=="completed"&&(slots.Count!=run.Total||slots.Any(s=>s.Status!="completed"||s.Result is null))))continue;
            foreach(var slot in slots.Where(s=>s.Status=="running")) {
                var attempts=slot.Attempts.Select(a=>a.Status=="running"?a with{Status="interrupted",FinishedAt=DateTimeOffset.UtcNow,Error="Service restarted; explicit resume required."}:a).ToList();
                await store.SaveSlot(slot with{Status="cancelled",Attempts=attempts});
            }
            await store.SaveRun(run with{Status="cancelled",Error="Service restarted; resume explicitly with original seeds."});
        }
        Volatile.Write(ref ready,1);
        await foreach(var id in queue.Reader.ReadAllAsync(stoppingToken)) {
            using var executionLease=executionGate is null?null:await executionGate.Acquire(stoppingToken);
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await mutations.WaitAsync(stoppingToken);
            try{
                var waiting=store.Runs().FirstOrDefault(run=>run.Id==id);
                if(waiting is null||waiting.Status!="queued"){queued.TryRemove(id,out _);continue;}
                active[id]=cancellation;
                await store.SaveRun(store.Run(id) with{Status="running"});
            }finally{mutations.Release();}
            var timingStarted=DateTimeOffset.UtcNow;
            var timing=new SessionTiming(Guid.NewGuid().ToString("N"),id,timingStarted,timingStarted,null,0,0,0,"running");
            var elapsed=Stopwatch.StartNew();var attemptDurations=new List<(long Order,double Seconds)>();using var timingWriter=new SemaphoreSlim(1,1);
            using var timingStop=new CancellationTokenSource();var checkpoints=Task.CompletedTask;
            var timingOutcome="failed";
            async Task RecordTiming(string status="running"){
                await timingWriter.WaitAsync();try{
                    var now=DateTimeOffset.UtcNow;
                    timing=timing with{UpdatedAt=now,FinishedAt=status=="running"?null:now,ActiveSeconds=elapsed.Elapsed.TotalSeconds,
                        AttemptCount=attemptDurations.Count,ScheduledSeconds=RuntimeEstimate.MeasuredSchedule(attemptDurations.OrderBy(a=>a.Order).Select(a=>a.Seconds),store.Run(id).Config),Status=status};
                    await store.SaveSessionTiming(timing);
                }finally{timingWriter.Release();}
            }
            try {
                await RecordTiming();
                checkpoints=CheckpointTiming();
                async Task CheckpointTiming(){
                    using var timer=new PeriodicTimer(TimeSpan.FromSeconds(10));
                    while(await timer.WaitForNextTickAsync(timingStop.Token))await RecordTiming();
                }
                var run=store.Run(id);
                if(!engine.HasBundle(run.Config))throw new InvalidOperationException("The run's pinned Forge bundle is unavailable.");
                async Task PlaySlot(GameSlot slot,CancellationToken ct){
                    var attemptOrder=Stopwatch.GetTimestamp();var attemptClock=Stopwatch.StartNew();
                    var attempts=slot.Attempts.ToList();var number=attempts.Count+1;
                    var started=new Attempt(number,DateTimeOffset.UtcNow,null,"running",null,null,null);attempts.Add(started);
                    await store.SaveSlot(slot with{Status="running",Attempts=attempts});
                    try {
                        var result=await engine.Play(run,slot,number,ct);
                        attempts[^1]=started with{Status="completed",FinishedAt=DateTimeOffset.UtcNow,Replay=result.Replay,ReplayHash=result.Hash};
                        await store.SaveSlot(slot with{Status="completed",Attempts=attempts,Result=result.Result});
                    }catch(OperationCanceledException)when(ct.IsCancellationRequested){
                        attempts[^1]=started with{Status="cancelled",FinishedAt=DateTimeOffset.UtcNow,Error="Administrative cancellation."};
                        await store.SaveSlot(slot with{Status="cancelled",Attempts=attempts});
                    }catch(Exception error){
                        logger.LogWarning("Game {Game} failed: {Message}",slot.Id,error.Message);
                        attempts[^1]=started with{Status="failed",FinishedAt=DateTimeOffset.UtcNow,Error=error.Message[..Math.Min(error.Message.Length,16000)]};
                        await store.SaveSlot(slot with{Status="failed",Attempts=attempts});
                    }
                    finally{
                        await timingWriter.WaitAsync();try{attemptDurations.Add((attemptOrder,attemptClock.Elapsed.TotalSeconds));}finally{timingWriter.Release();}
                        await RecordTiming();
                    }
                }
                while(true){
                // An earlier queued session may have finished these games since this session was created.
                await mutations.WaitAsync(cancellation.Token);try {
                var cache=store.CachedGames(run.Config);
                foreach(var slot in store.Slots(id).Where(s=>s.Status=="pending")) {
                    var reused=Reuse(slot,cache);if(reused.CachedFromRunId is not null)await store.SaveSlot(reused);
                }
                }finally{mutations.Release();}
                var pending=store.Slots(id).Where(s=>s.Status=="pending").ToList();
                if(run.Config.StaggerStarts&&run.Config.StartupSpacingSeconds is null){
                    // Resume old saved sessions with their original scheduling policy.
                    using var pacer=new StartPacer(TimeSpan.FromSeconds(run.Config.EstimatedGameSeconds-run.Config.StartLeadSeconds));
                    await Parallel.ForEachAsync(pending,new ParallelOptions{MaxDegreeOfParallelism=run.Config.Workers,CancellationToken=cancellation.Token},async(slot,ct)=>{
                        await pacer.WaitAsync(ct);await PlaySlot(slot,ct);
                    });
                }else{
                    var spacing=run.Config.StaggerStarts?TimeSpan.FromSeconds(run.Config.StartupSpacingSeconds??1):TimeSpan.Zero;
                    await GameScheduler.RunAsync(pending,run.Config.Workers,spacing,PlaySlot,cancellation.Token);
                }
                var repeat=false;
                await mutations.WaitAsync(stoppingToken);
                try {
                    repeat=store.Slots(id).Any(slot=>slot.Status=="pending");
                    if(!repeat){
                        var current=store.Run(id);var finished=store.Slots(id);
                        var complete=finished.Count==current.Total&&finished.All(s=>s.Status=="completed"&&s.Result is not null);
                        var cancelled=finished.Any(s=>s.Status=="cancelled");
                        timingOutcome=complete?"completed":cancelled?"cancelled":"failed";
                        await store.SaveRun(current with{Status=timingOutcome,
                            Error=complete?null:"Schedule incomplete; unfinished games require explicit resume or retry."});
                    }
                }finally{mutations.Release();}
                if(!repeat)break;
                }
            }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){
                timingOutcome="cancelled";
                foreach(var slot in store.Slots(id).Where(s=>s.Status=="pending"))await store.SaveSlot(slot with{Status="cancelled"});
                await store.SaveRun(store.Run(id) with{Status="cancelled"});
            }catch(Exception error){logger.LogError(error,"Run {Run} failed",id);await store.SaveRun(store.Run(id) with{Status="failed",Error=error.Message});}
            finally{
                await engine.ReleaseWorkers(id);
                timingStop.Cancel();
                try{await checkpoints;}catch(OperationCanceledException)when(timingStop.IsCancellationRequested){}
                elapsed.Stop();await RecordTiming(timingOutcome);
                await mutations.WaitAsync();try{
                    active.TryRemove(id,out _);queued.TryRemove(id,out _);
                    if(store.Runs().FirstOrDefault(run=>run.Id==id)?.Status=="queued")Enqueue(id);
                }finally{mutations.Release();}
            }
        }
    }
}
