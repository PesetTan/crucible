using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace MtgSimulator;

public sealed class RunCoordinator(Store store,ForgeEngine engine,ILogger<RunCoordinator> logger) : BackgroundService
{
    private readonly Channel<string> queue=Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string,byte> queued=new();
    private readonly ConcurrentDictionary<string,CancellationTokenSource> active=new();
    private readonly SemaphoreSlim mutations=new(1,1);
    private void Enqueue(string id){if(queued.TryAdd(id,0))queue.Writer.TryWrite(id);}
    public async Task<string> Create(RunRequest request,string key)
    {
        var blockers=engine.Preflight(request);if(blockers.Count!=0)throw new InvalidOperationException(string.Join("\n",blockers));
        var capability=engine.Capabilities();var decks=request.DeckIds.Order(StringComparer.Ordinal).ToList();
        var config=new RunConfig(decks,request.GamesPerPair,request.Workers,request.TimeoutSeconds,request.TurnLimit,request.RootSeed,request.Diagnostic,capability.EngineHash,capability.AdapterHash);
        var id=Guid.NewGuid().ToString("N");var slots=new List<GameSlot>();var seeds=new HashSet<long>();
        for(var a=0;a<decks.Count;a++)for(var b=a+1;b<decks.Count;b++)for(var starter=0;starter<2;starter++)for(var index=0;index<request.GamesPerPair/2;index++){
            var digest=SHA256.HashData(Encoding.UTF8.GetBytes(Json.Encode(new{scheme="sha256-v1",rootSeed=request.RootSeed,a=decks[a],b=decks[b],starter,index})));
            var seed=BinaryPrimitives.ReadInt64BigEndian(digest);if(!seeds.Add(seed))throw new InvalidOperationException("Seed collision detected.");
            slots.Add(new(Guid.NewGuid().ToString("N"),id,decks[a],decks[b],decks[starter==0?a:b],seed,index,"pending",[]));
        }
        var run=new SimulationRun(id,DateTimeOffset.UtcNow,"queued",config,slots.Count);
        var actualId=await store.CreateRun(run,slots,key,DeckImporter.Hash(Json.Encode(config)));if(store.Run(actualId).Status=="queued")Enqueue(actualId);return actualId;
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
            var cap=engine.Capabilities();if(!cap.Available||cap.EngineHash!=run.Config.EngineHash||cap.AdapterHash!=run.Config.AdapterHash)throw new InvalidOperationException("Engine/adapter identity changed; create a new run.");
            foreach(var slot in store.Slots(id))if(slot.Result is null&&(slot.Status is "pending" or "cancelled"||retryFailures&&slot.Status=="failed")) {
                if(slot.Attempts.Count>=4)continue;await store.SaveSlot(slot with{Status="pending"});
            }
            if(!store.Slots(id).Any(s=>s.Status=="pending"))throw new InvalidOperationException("No resumable slots; failed attempts require explicit retry (maximum four attempts per slot).");
            await store.SaveRun(run with{Status="queued",Error=null});Enqueue(id);
        }finally{mutations.Release();}
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach(var run in store.Runs().Where(r=>r.Status is "running" or "cancelling" or "queued")) {
            foreach(var slot in store.Slots(run.Id).Where(s=>s.Status=="running")) {
                var attempts=slot.Attempts.Select(a=>a.Status=="running"?a with{Status="interrupted",FinishedAt=DateTimeOffset.UtcNow,Error="Service restarted; explicit resume required."}:a).ToList();
                await store.SaveSlot(slot with{Status="cancelled",Attempts=attempts});
            }
            await store.SaveRun(run with{Status="cancelled",Error="Service restarted; resume explicitly with original seeds."});
        }
        await foreach(var id in queue.Reader.ReadAllAsync(stoppingToken)) {
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await mutations.WaitAsync(stoppingToken);
            try{
                if(store.Run(id).Status!="queued"){queued.TryRemove(id,out _);continue;}
                active[id]=cancellation;
                await store.SaveRun(store.Run(id) with{Status="running"});
            }finally{mutations.Release();}
            try {
                var run=store.Run(id);var cap=engine.Capabilities();
                if(!cap.Available||cap.EngineHash!=run.Config.EngineHash||cap.AdapterHash!=run.Config.AdapterHash)throw new InvalidOperationException("Engine or adapter changed after preflight.");
                await Parallel.ForEachAsync(store.Slots(id).Where(s=>s.Status=="pending"),new ParallelOptions{MaxDegreeOfParallelism=run.Config.Workers,CancellationToken=cancellation.Token},async(slot,ct)=>{
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
                });
                var failed=store.Slots(id).Count(s=>s.Status=="failed");await store.SaveRun(run with{Status=failed==0?"completed":"failed",Error=failed==0?null:$"{failed} slots failed; matrix incomplete. Explicit retry is available."});
            }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){
                foreach(var slot in store.Slots(id).Where(s=>s.Status=="pending"))await store.SaveSlot(slot with{Status="cancelled"});
                await store.SaveRun(store.Run(id) with{Status="cancelled"});
            }catch(Exception error){logger.LogError(error,"Run {Run} failed",id);await store.SaveRun(store.Run(id) with{Status="failed",Error=error.Message});}
            finally{active.TryRemove(id,out _);queued.TryRemove(id,out _);}
        }
    }
}
