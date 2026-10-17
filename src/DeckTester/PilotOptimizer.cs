using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace DeckTester;

// The fixed local backend performs deterministic extraction/search. This host owns all JVM work.
public sealed class PilotOptimizer(AppPaths paths, Store store, CardAttributes attributes, ForgeEngine engine,
    SimulationExecutionGate executionGate, ILogger<PilotOptimizer> logger) : BackgroundService
{
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>();
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly HashSet<string> pending = [];
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int pendingCount;
    private int workingCount;
    public bool IsReady => ready.Task.IsCompletedSuccessfully;
    public int ActiveCount => Volatile.Read(ref workingCount);
    public int QueuedCount => Math.Max(0, Volatile.Read(ref pendingCount) - ActiveCount);

    private List<OptimizerCatalogEntry> Catalog()
    {
        var decks = store.Decks();
        var snapshot = attributes.Snapshot(decks);
        return decks.Select(deck => {
            var manifest = deck with { Cards = deck.Cards.Select(c => c with { Attributes = snapshot.Cards.TryGetValue(c.Name,out var value)?value:c.Attributes }).ToList() };
            return new OptimizerCatalogEntry(manifest,ReanimatorPilots.Profile(manifest,ReanimatorPilots.CurrentSubtypeMode),engine.Coverage(manifest).Status);
        }).ToList();
    }

    private async Task<JsonElement> Invoke(string action, object request, CancellationToken cancellation = default)
    {
        if(paths.Installed)throw new InvalidOperationException("Pilot optimization is not available in this desktop beta.");
        var isWork = action is "work" or "profile" or "promote";
        if (isWork) Interlocked.Increment(ref workingCount);
        try {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("MTG_PYTHON") ?? "python3") {
            WorkingDirectory = paths.Root, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.Environment["MTG_PROJECT_ROOT"] = paths.Root;
        info.ArgumentList.Add(Path.Combine(paths.Root, "scripts", "pilot-optimizer.py"));
        info.ArgumentList.Add("--internal"); info.ArgumentList.Add(action);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Could not start local optimizer backend.");
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
        var stderr = process.StandardError.ReadToEndAsync(cancellation);
        await process.StandardInput.WriteLineAsync(Json.Encode(request)); process.StandardInput.Close();
        await process.WaitForExitAsync(cancellation);
        var output = await stdout; var errors = await stderr;
        if (output.Length > 4 * 1024 * 1024) throw new InvalidDataException("Optimizer output exceeded the response limit.");
        JsonElement reply;
        try { reply = JsonDocument.Parse(output).RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException("Optimizer backend failed: " + errors[..Math.Min(errors.Length, 1000)]); }
        if (!reply.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(reply.GetProperty("error").GetString());
        return reply.GetProperty("result").Clone();
        } finally { if (isWork) Interlocked.Decrement(ref workingCount); }
    }

    public async Task<JsonElement> Read(string action, object request, CancellationToken cancellation = default)
    {
        await ready.Task.WaitAsync(cancellation);
        return await Invoke(action, request, cancellation);
    }

    public async Task<JsonElement> Inspect(CancellationToken cancellation,string providerId="forge") => await Read("inspect", new OptimizerInspect(providerId=="forge"?Catalog():[]){ProviderId=providerId}, cancellation);

    public async Task<JsonElement> Create(JsonElement request, string key, CancellationToken cancellation)
    {
        await ready.Task.WaitAsync(cancellation);
        var body = Json.Decode<Dictionary<string, JsonElement>>(request.GetRawText()) ?? throw new ArgumentException("Provide an optimizer request.");
        body["key"] = Json.Element(key);
        body["catalog"] = Json.Element(body.TryGetValue("providerId",out var provider)&&provider.GetString()=="pyre"?new List<OptimizerCatalogEntry>():Catalog());
        return await Invoke("create", body, cancellation);
    }

    public async Task<JsonElement> Stage(string id, JsonElement request, CancellationToken cancellation)
    {
        await ready.Task.WaitAsync(cancellation);
        await lifecycle.WaitAsync(cancellation);
        try {
            var body = Json.Decode<Dictionary<string, JsonElement>>(request.GetRawText()) ?? [];
            body["id"] = Json.Element(id);
            return await Invoke("stage", body, cancellation);
        } finally { lifecycle.Release(); }
    }

    public async Task<JsonElement> Start(string id, JsonElement request, CancellationToken cancellation)
    {
        await ready.Task.WaitAsync(cancellation);
        await lifecycle.WaitAsync(cancellation);
        try {
            if (pending.Contains(id)) throw new InvalidOperationException("Experiment is already queued or running.");
            var body = Json.Decode<Dictionary<string, JsonElement>>(request.GetRawText()) ?? [];
            body["id"] = Json.Element(id);
            var result = await Invoke("prepare", body, cancellation);
            pending.Add(id); Volatile.Write(ref pendingCount,pending.Count); queue.Writer.TryWrite(id);
            return result;
        } finally { lifecycle.Release(); }
    }

    public async Task<JsonElement> Promote(string id, string candidateId, CancellationToken cancellation)
    {
        await ready.Task.WaitAsync(cancellation);
        await lifecycle.WaitAsync(cancellation);
        try {
            if (pending.Count != 0) throw new InvalidOperationException("Wait for queued optimizer work before promotion.");
            using var lease = await executionGate.Acquire(cancellation);
            return await Invoke("promote", new OptimizerCandidate(id,candidateId), cancellation);
        } finally { lifecycle.Release(); }
    }

    public async Task<JsonElement> Profile(string id, string candidateId, CancellationToken cancellation)
    {
        await ready.Task.WaitAsync(cancellation);
        await lifecycle.WaitAsync(cancellation);
        try {
            if(pending.Count!=0)throw new InvalidOperationException("Wait for queued optimizer work before profiling.");
            using var lease=await executionGate.Acquire(cancellation);
            return await Invoke("profile",new OptimizerCandidate(id,candidateId),cancellation);
        } finally { lifecycle.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(paths.Installed){ready.TrySetResult();return;}
        try { await Invoke("recover", new EmptyRequest(), stoppingToken); ready.TrySetResult(); }
        catch (Exception error) { ready.TrySetException(error); throw; }
        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken)) {
            try {
                using var lease = await executionGate.Acquire(stoppingToken);
                await Invoke("work", new OptimizerId(id), stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Optimizer experiment {Id} failed", id); }
            finally {
                await lifecycle.WaitAsync(CancellationToken.None);
                try { pending.Remove(id); Volatile.Write(ref pendingCount,pending.Count); } finally { lifecycle.Release(); }
            }
        }
    }

    public static void MapEndpoints(WebApplication app)
    {
        const string prefix = "/api/v1/pilot-optimizations";
        app.MapGet(prefix + "/inspect", (PilotOptimizer optimizer, HttpContext context,string? providerId) => optimizer.Inspect(context.RequestAborted,providerId??"forge"));
        app.MapGet(prefix, (PilotOptimizer optimizer, HttpContext context) => optimizer.Read("list", new EmptyRequest(), context.RequestAborted));
        app.MapPost(prefix, (JsonElement request, PilotOptimizer optimizer, HttpContext context) => optimizer.Create(request, context.Request.Headers["Idempotency-Key"].ToString(), context.RequestAborted));
        app.MapGet(prefix + "/{id}", (string id, PilotOptimizer optimizer, HttpContext context) => optimizer.Read("status", new OptimizerId(id), context.RequestAborted));
        app.MapGet(prefix + "/{id}/report", (string id, PilotOptimizer optimizer, HttpContext context) => optimizer.Read("report", new OptimizerId(id), context.RequestAborted));
        app.MapGet(prefix + "/{id}/evidence", (string id, int? limit, PilotOptimizer optimizer, HttpContext context) => optimizer.Read("evidence", new OptimizerEvidence(id,limit??8), context.RequestAborted));
        app.MapGet(prefix + "/{id}/detail", (string id, string replay, int sequence, PilotOptimizer optimizer, HttpContext context) => optimizer.Read("detail", new OptimizerDetail(id,replay,sequence), context.RequestAborted));
        app.MapGet(prefix + "/{id}/source", (string id, string term, string? candidateId, PilotOptimizer optimizer, HttpContext context) => optimizer.Read("source", new OptimizerSource(id,term,candidateId), context.RequestAborted));
        app.MapPost(prefix + "/{id}/candidates", (string id, JsonElement request, PilotOptimizer optimizer, HttpContext context) => optimizer.Stage(id, request, context.RequestAborted));
        app.MapPost(prefix + "/{id}/stage", (string id, JsonElement request, PilotOptimizer optimizer, HttpContext context) => optimizer.Stage(id, request, context.RequestAborted));
        app.MapPost(prefix + "/{id}/start", (string id, JsonElement request, PilotOptimizer optimizer, HttpContext context) => optimizer.Start(id, request, context.RequestAborted));
        app.MapPost(prefix + "/{id}/resume", (string id, JsonElement request, PilotOptimizer optimizer, HttpContext context) => optimizer.Start(id, request, context.RequestAborted));
        app.MapPost(prefix + "/{id}/pause", (string id, PilotOptimizer optimizer, HttpContext context) => optimizer.Read("pause", new OptimizerId(id), context.RequestAborted));
        app.MapPost(prefix + "/{id}/promote", (string id, OptimizerPromotion request, PilotOptimizer optimizer, HttpContext context) => optimizer.Promote(id, request.CandidateId, context.RequestAborted));
        app.MapPost(prefix + "/{id}/profile", (string id, OptimizerPromotion request, PilotOptimizer optimizer, HttpContext context) => optimizer.Profile(id, request.CandidateId, context.RequestAborted));
        app.MapPost(prefix + "/{id}/rollback",(string id,PilotOptimizer optimizer,HttpContext context)=>optimizer.Read("rollback",new OptimizerId(id),context.RequestAborted));
    }
}

public sealed record OptimizerPromotion(string CandidateId);
