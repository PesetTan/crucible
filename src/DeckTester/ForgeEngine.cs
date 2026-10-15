using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DeckTester;

public sealed class ForgeEngine(AppPaths paths, Store store, CardAttributes? attributes = null) : IAsyncDisposable
{
    private readonly EngineBundleManager bundles = new(paths.Root,paths.Assets);
    private readonly ConcurrentDictionary<string, ForgeWorkerPool> workerPools = new();
    public async Task ReleaseWorkers(string runId)
    {
        if(workerPools.TryRemove(runId,out var pool))await pool.DisposeAsync();
    }
    public async ValueTask DisposeAsync()
    {
        foreach(var runId in workerPools.Keys)await ReleaseWorkers(runId);
    }
    private readonly ConcurrentDictionary<string,Dictionary<string,string>> scriptCache = new();
    public OptimizerPolicy? OptimizerPolicy(RunRequest request)
    {
        if(request.OptimizerPolicyId is null)return null;
        var policy=store.PublishedOptimizerPolicy(request.OptimizerPolicyId);
        var bundle=bundles.Active()??throw new InvalidOperationException("No active Forge bundle.");
        if(policy.EngineHash!=bundle.EngineHash||policy.AdapterHash!=bundle.AdapterHash)
            throw new InvalidOperationException("Published optimizer policy belongs to a different active bundle.");
        var decks=request.DeckIds.Select(store.Deck).ToList();var snapshot=attributes?.Snapshot(decks);
        if(!decks.Any(d=>ReanimatorPilots.Profile(d with{Cards=d.Cards.Select(c=>c with{Attributes=snapshot?.Cards.GetValueOrDefault(c.Name)??c.Attributes}).ToList()},request.PilotMode)==policy.Profile))
            throw new ArgumentException("No selected deck uses the optimized target profile.");
        return policy;
    }
    public static string FileHash(string path) { using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static Dictionary<string,string> LoadScripts(EngineBundle bundle)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var folder=Path.Combine(bundle.Resources,"cardsfolder");
        if(!Directory.Exists(folder))return result;
        var archive=Path.Combine(folder,"cardsfolder.zip");
        if(File.Exists(archive)){
            using var zip=ZipFile.OpenRead(archive);
            foreach(var entry in zip.Entries.Where(e=>e.FullName.EndsWith(".txt",StringComparison.Ordinal))){
                using var reader=new StreamReader(entry.Open());string? line;
                while((line=reader.ReadLine()) is not null){if(line.StartsWith("Name:",StringComparison.Ordinal)){result.TryAdd(line[5..].Trim(),archive+"!"+entry.FullName);break;}}
            }
        }
        foreach(var file in Directory.EnumerateFiles(folder,"*.txt",SearchOption.AllDirectories)) {
            using var reader=new StreamReader(file); var first=reader.ReadLine();
            if(first?.StartsWith("Name:",StringComparison.Ordinal)==true)result.TryAdd(first[5..].Trim(),file);
        }return result;
    }
    private Dictionary<string,string> Scripts(EngineBundle bundle)
    {
        return scriptCache.GetOrAdd(bundle.Id, _ => LoadScripts(bundle));
    }
    public bool HasBundle(RunConfig config) => bundles.Find(config.EngineHash, config.AdapterHash) is not null && File.Exists(paths.Java);
    public Capabilities Capabilities()
    {
        var blockers=new List<string>(); if(!File.Exists(paths.Java))blockers.Add("Project-local Java runtime is missing. See engine setup instructions.");
        var active=bundles.Active();
        if(active is null)blockers.Add("No active Forge bundle. Run scripts/activate-forge.sh.");
        var engineHash=active?.EngineHash??"missing";
        var adapterHash=active?.AdapterHash??"missing";
        return new(blockers.Count==0,active?.EngineName??"Forge unavailable",engineHash,adapterHash,paths.Java,blockers,
            ["Forge Default AI is unvalidated for these decks; combo sequencing and hidden-information behavior have not been independently audited.",
             "Card scripts are present, but required interaction and generated-object coverage is unvalidated.",
             "London mulligans approximate Arena Bo1 opening hands. No hand smoothing is guessed.",
             "Replay captures engine snapshots; event narratives are engine diagnostics, not a complete normalized decision protocol.",
             "Exact skipped-turn cycle grouping and mana-pool replay are unavailable; actual turns are authoritative.",
             "Legality uses the dated local snapshot and is not independently refreshed by this app."]){AttributesHash=attributes?.CatalogHash(),
                Protocols=active is not null&&File.Exists(Path.Combine(active.Directory,"protocol-catalog.json"))?Json.Decode<ProtocolCatalog>(File.ReadAllText(Path.Combine(active.Directory,"protocol-catalog.json"))):null};
    }
    private string ForgeCardName(string name, Dictionary<string,string> available)
    {
        if (available.ContainsKey(name)) return name;
        var firstFace = name.Split(" // ", 2, StringSplitOptions.None)[0];
        return firstFace != name && available.ContainsKey(firstFace) ? firstFace : name;
    }
    public DeckCoverage Coverage(DeckManifest deck)
    {
        var active=bundles.Active();var available=active is null?new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase):Scripts(active);
        var cards=deck.Cards.Where(c=>c.Zone=="MainDeck").Select(c=>available.TryGetValue(ForgeCardName(c.Name,available),out var script)?
            new CoverageItem(c.Name,"present_unvalidated",active!.Id+"/"+Path.GetRelativePath(active.Directory,script),null):new CoverageItem(c.Name,"missing",null,"No exact Forge card identity found.")).ToList();
        return new(deck.Id,cards.Any(c=>c.Status=="missing")?"blocked":"exploratory",cards,
            ["Faces, generated tokens, copy interactions, rules fixtures, and pilot behavior remain unvalidated."]);
    }
    public List<string> Preflight(RunRequest request)
    {
        var blockers=Capabilities().Blockers.ToList();
        if(!ReanimatorPilots.ValidMode(request.PilotMode))blockers.Add("Unknown pilot mode.");
        if(request.PilotMode==ReanimatorPilots.EngagementMode&&Capabilities().Protocols?.Profile!=ReanimatorPilots.Engagement)
            blockers.Add("The active engine bundle does not include Engagement Protocols. Activate the updated bundle first.");
        if(!request.Diagnostic)blockers.Add("Validated runs are unavailable until rules, pilot, and information-boundary gates pass. Use an explicitly exploratory run.");
        if(request.DeckIds.Distinct().Count()!=request.DeckIds.Count||request.DeckIds.Count is <2 or >SimulationLimits.MaximumDecks)blockers.Add($"Select 2 to {SimulationLimits.MaximumDecks} distinct deck versions.");
        if(request.GamesPerPair<2||request.GamesPerPair>1000||request.GamesPerPair%2!=0)blockers.Add("Choose an even number of games per matchup between 2 and 1,000.");
        if(request.Workers is <1 or >4)blockers.Add("Worker count must be between 1 and 4; each uses up to 1.2 GB.");
        if(request.StaggerStarts){
            if(request.Workers<2)blockers.Add("Staggered starts require at least two parallel games to allow overlap.");
            if(request.StartupSpacingSeconds is {} spacing){
                if(!double.IsFinite(spacing)||spacing is <0 or >5)blockers.Add("Initial engine spacing must be 0–5 seconds.");
            }else{
                if(request.EstimatedGameSeconds is <1 or >1800)blockers.Add("Estimated game duration must be 1–1,800 seconds.");
                if(request.StartLeadSeconds<0||request.StartLeadSeconds>=request.EstimatedGameSeconds)blockers.Add("The head start must be zero or more seconds and shorter than the estimated game duration.");
            }
        }
        if(request.TimeoutSeconds is <10 or >1800)blockers.Add("Time limit must be 0.17–30 minutes per attempt.");
        if(request.TurnLimit is <10 or >1000)blockers.Add("Turn limit must be 10–1,000 actual player turns.");
        foreach(var id in request.DeckIds) {
            try { var deck=store.Deck(id);var coverage=Coverage(deck);
                if(deck.MainCount!=60||deck.Cards.Any(c=>c.Quantity<=0))blockers.Add($"{deck.Name}: invalid deck composition.");
                foreach(var group in deck.Cards.Where(c=>c.Zone=="MainDeck").GroupBy(c=>c.Name))
                    if(group.Sum(c=>c.Quantity)>DeckWorkshop.CopyLimit(group.First()))blockers.Add($"{deck.Name}: copy limit exceeded for {group.Key}.");blockers.AddRange(coverage.Cards.Where(c=>c.Status=="missing").Select(c=>$"{deck.Name}: {c.Name} is unsupported."));
                blockers.AddRange(deck.Cards.Where(c=>c.Zone=="MainDeck"&&!c.LocallyLegal).Select(c=>$"{deck.Name}: {c.Name} lacks local Standard legality evidence."));
            }catch(KeyNotFoundException){blockers.Add("A selected deck version no longer exists.");}
        }return blockers;
    }
    public bool HasReusableReplay(GameSlot slot)
    {
        var attempt=slot.Attempts.LastOrDefault(a=>a.Status=="completed");
        if(slot.Result is null||attempt?.Replay is null||attempt.ReplayHash is null)return false;
        try { var path=Path.Combine(paths.Replays,attempt.Replay);return File.Exists(path)&&FileHash(path)==attempt.ReplayHash; }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return false;}
    }
    public async Task<(GameResult Result,string Replay,string Hash)> Play(SimulationRun run, GameSlot slot, int attemptNumber, CancellationToken cancellation)
    {
        var bundle=bundles.Find(run.Config.EngineHash,run.Config.AdapterHash)??throw new InvalidOperationException("Pinned Forge bundle is unavailable.");
        var attemptId=$"{slot.Id}-{attemptNumber}";
        var outputPath=Path.Combine(paths.Replays,attemptId+".jsonl.gz");var temp=outputPath+".partial";
        var timer=Stopwatch.StartNew();
        var request=new ForgeGameRequest(attemptId,slot.Seed,slot.Starter==slot.DeckA?0:1,run.Config.TurnLimit,run.Config.PerformanceMode,
            run.Config.OptimizerPolicy is {} policyA&&run.Config.PilotProfiles?.GetValueOrDefault(slot.DeckA)==policyA.Profile?policyA:null,
            run.Config.OptimizerPolicy is {} policyB&&run.Config.PilotProfiles?.GetValueOrDefault(slot.DeckB)==policyB.Profile?policyB:null,
            run.Config.Pilot,
            run.Config.PilotProfiles?.GetValueOrDefault(slot.DeckA)??ReanimatorPilots.Profile(store.Deck(slot.DeckA),run.Config.Pilot),
            run.Config.PilotProfiles?.GetValueOrDefault(slot.DeckB)??ReanimatorPilots.Profile(store.Deck(slot.DeckB),run.Config.Pilot),
            store.Deck(slot.DeckA).Cards.Where(c=>c.Zone=="MainDeck").Select(c=>new ForgeDeckCard(ForgeCardName(c.Name,Scripts(bundle)),c.Quantity,run.Config.CardAttributes?.GetValueOrDefault(c.Name)??c.Attributes)).ToList(),
            store.Deck(slot.DeckB).Cards.Where(c=>c.Zone=="MainDeck").Select(c=>new ForgeDeckCard(ForgeCardName(c.Name,Scripts(bundle)),c.Quantity,run.Config.CardAttributes?.GetValueOrDefault(c.Name)??c.Attributes)).ToList());
        var encodedRequest=Json.Encode(request);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(run.Config.TimeoutSeconds));
        var pool=bundle.WorkerProtocolVersion==1
            ?workerPools.GetOrAdd(run.Id,_=>new ForgeWorkerPool(paths,bundle,run.Config.Workers)):null;
        var worker=pool is null?new ForgeWorker(paths,bundle,persistent:false):await pool.Rent(timeout.Token);
        var process=worker.Process;worker.ClearErrors();var healthy=false;var boundary=false;
        using var registration=timeout.Token.Register(worker.Kill);
        GameResult? result=null;var samples=new List<TurnSample>();var openingHands=new Dictionary<string,OpeningHand>();var personalTurns=new Dictionary<string,int>();long sequence=0;long bytes=0;bool started=false;
        string? failure=null;int actualTurns=0;
        try {
            await process.StandardInput.WriteLineAsync(encodedRequest.AsMemory(),timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            if(!worker.Persistent)process.StandardInput.Close();
            await using(var file=File.Create(temp))await using(var gzip=new GZipStream(file,CompressionLevel.Fastest))await using(var writer=new StreamWriter(gzip,new UTF8Encoding(false))) {
                while(await process.StandardOutput.ReadLineAsync(timeout.Token) is {} line) {
                    bytes+=Encoding.UTF8.GetByteCount(line);if(bytes>256*1024*1024)throw new InvalidDataException("Replay size limit reached.");
                    var record=Json.Decode<ReplayRecord>(line);
                    if(worker.Persistent&&record.Kind=="worker_complete") {
                        using var transport=System.Text.Json.JsonDocument.Parse(line);
                        if(record.AttemptId!=attemptId||result is null||failure is not null||
                            transport.RootElement.GetProperty("protocolVersion").GetInt32()!=1)
                            throw new InvalidDataException("Invalid Forge worker completion boundary.");
                        worker.HeapUsedBytes=transport.RootElement.GetProperty("heapUsedBytes").GetInt64();
                        worker.CompletedGames++;boundary=true;break;
                    }
                    if(result is not null)throw new InvalidDataException("Engine emitted records after its terminal result.");
                    if(record.State is not null){using var document=System.Text.Json.JsonDocument.Parse(line);var raw=document.RootElement.GetProperty("state").GetRawText();if(DeckImporter.Hash(raw)!=record.StateHash)throw new InvalidDataException("Engine state checksum mismatch.");}
                    if(record.AttemptId!=attemptId||record.Sequence!=sequence+1)throw new InvalidDataException("Invalid replay sequence or attempt identity.");sequence=record.Sequence;
                    await writer.WriteLineAsync(line);actualTurns=Math.Max(actualTurns,record.ActualTurn);
                    if(record.Kind=="game_started") { if(started||record.Starter!=(slot.Starter==slot.DeckA?0:1))throw new InvalidDataException("Engine starting player does not match the schedule.");started=true; }
                    if(record.Kind=="attempt_failed")failure=record.Reason??"Engine attempt failed.";
                    if(record.Kind=="opening_hand"){var opening=OpeningHandTracker.FromRecord(record);if(!openingHands.TryAdd(opening.Player,opening))throw new InvalidDataException("Duplicate final opening-hand record.");}
                    if(record.State is {} state) {
                        foreach(var p in state.Players)personalTurns[p.Id]=p.PersonalTurn;
                        if(record.Event=="GameEventTurnEnded"&&state.ActivePlayer is {} active) {
                            var p=state.Players.Single(x=>x.Id==active);
                            samples.Add(Sample(record,p));
                        }
                    }
                    if(record.Kind=="game_finished") {
                        if(result is not null||!started||record.Draw is null||record.Reason is null||(!record.Draw.Value&&record.Winner is not ("A" or "B")))throw new InvalidDataException("Missing or contradictory engine terminal evidence.");
                        if(openingHands.Count!=2)throw new InvalidDataException("Missing final opening-hand accounting.");
                        result=new(record.Draw.Value?null:record.Winner=="A"?slot.DeckA:slot.DeckB,record.Draw.Value,record.Reason,actualTurns,new(personalTurns),timer.ElapsedMilliseconds,samples,ManaAnalyzer.Detect(samples),ManaAnalyzer.Version,openingHands.Values.OrderBy(h=>h.Player).ToList(),OpeningHandTracker.Version);
                    }
                }
            }
            if(!worker.Persistent){await process.WaitForExitAsync(timeout.Token);await worker.DrainErrors;}
            if((worker.Persistent&&!boundary)||(!worker.Persistent&&process.ExitCode!=0)||failure is not null||result is null)
                throw new InvalidOperationException(failure??$"Engine returned without a complete result and worker boundary. {worker.Errors}");
            cancellation.ThrowIfCancellationRequested();
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(temp,outputPath,overwrite:false);
            var replayHash=FileHash(outputPath);healthy=true;
            return(result,Path.GetFileName(outputPath),replayHash);
        }catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){throw new TimeoutException($"Attempt exceeded {run.Config.TimeoutSeconds/60.0:0.##} minutes; not a rules draw.");}
        finally {
            // Unregister before returning the lease: late cancellation must never kill
            // the next game using this process.
            await registration.DisposeAsync();
            try {
                if(!healthy){worker.Kill();await process.WaitForExitAsync();await worker.DrainErrors;}
                await File.WriteAllTextAsync(Path.Combine(paths.Replays,attemptId+".log"),worker.Errors);
                if(File.Exists(temp))File.Move(temp,Path.Combine(paths.Replays,attemptId+".incomplete.gz"),overwrite:true);
            } finally {
                if(pool is null)await worker.DisposeAsync();else await pool.Return(worker,healthy);
            }
        }
    }
    public async Task<List<ReplayRecord>> Replay(GameSlot slot,int offset=0,int limit=200)
    {
        if(offset<0||limit is <1 or >500)throw new ArgumentException("Invalid replay range.");
        var attempt=slot.Attempts.LastOrDefault(a=>a.Status=="completed")??throw new InvalidOperationException("This slot has no complete replay.");
        var path=Path.Combine(paths.Replays,attempt.Replay??throw new InvalidDataException("Missing replay reference."));
        if(!File.Exists(path)||FileHash(path)!=attempt.ReplayHash)throw new InvalidDataException("Replay file is missing or corrupt.");
        await using var file=File.OpenRead(path);await using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
        var records=new List<ReplayRecord>();int index=0;while(await reader.ReadLineAsync() is {} line){if(index++<offset)continue;records.Add(Json.Decode<ReplayRecord>(line));if(records.Count==limit)break;}return records;
    }
    public Task<List<ReplayTurnPosition>> ReplayTurns(GameSlot slot)=>ReadReplayTurns(paths.Replays,slot);
    public static async Task<List<ReplayTurnPosition>> ReadReplayTurns(string replayDirectory,GameSlot slot)
    {
        var attempt=slot.Attempts.LastOrDefault(a=>a.Status=="completed")??throw new InvalidOperationException("This slot has no complete replay.");
        var path=Path.Combine(replayDirectory,attempt.Replay??throw new InvalidDataException("Missing replay reference."));
        if(!File.Exists(path)||FileHash(path)!=attempt.ReplayHash)throw new InvalidDataException("Replay file is missing or corrupt.");
        await using var file=File.OpenRead(path);await using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
        var positions=new List<ReplayTurnPosition>();int offset=0;
        while(await reader.ReadLineAsync() is {} line){
            var record=Json.Decode<ReplayRecord>(line);
            if(record.ActualTurn>0&&record.State is not null&&positions.All(p=>p.Turn!=record.ActualTurn))positions.Add(new(record.ActualTurn,offset));
            offset++;
        }
        return positions;
    }
    public Task<TriggerGameSummary?> TriggerSummary(GameSlot slot)=>ReadTriggerSummary(paths.Replays,slot);
    public static async Task<TriggerGameSummary?> ReadTriggerSummary(string replayDirectory,GameSlot slot)
    {
        var attempt=slot.Attempts.LastOrDefault(a=>a.Status=="completed");
        if(attempt?.Replay is null)return null;
        var path=Path.Combine(replayDirectory,attempt.Replay);
        if(!File.Exists(path)||FileHash(path)!=attempt.ReplayHash)return null;
        await using var file=File.OpenRead(path);await using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
        var events=new List<TriggerEventSummary>();var marked=false;
        while(await reader.ReadLineAsync() is {} line){
            var record=Json.Decode<ReplayRecord>(line);
            if(record.TriggerCaptureVersion is not null)marked=true;
            if(record.Kind=="trigger")events.Add(new TriggerEventSummary(record.ActualTurn,record.TriggerCategory??"Other or unclassified"));
        }
        return marked||events.Count>0?new TriggerGameSummary(slot.Id,slot.Result?.ActualTurns??0,events):null;
    }
    public Task<List<TriggerGameSummary>> TriggerSummaries(IEnumerable<GameSlot> slots)=>ReadTriggerSummaries(paths.Replays,slots);
    public static async Task<List<TriggerGameSummary>> ReadTriggerSummaries(string replayDirectory,IEnumerable<GameSlot> slots)
    {
        var results=new System.Collections.Concurrent.ConcurrentBag<TriggerGameSummary>();
        await Parallel.ForEachAsync(slots,new ParallelOptions{MaxDegreeOfParallelism=4},async (slot,_)=>{
            var summary=await ReadTriggerSummary(replayDirectory,slot);
            if(summary is not null)results.Add(summary);
        });
        return results.ToList();
    }
    private static TurnSample Sample(ReplayRecord record,ReplayPlayer p)
    {
        var board=p.Battlefield;var handLands=p.Hand.Count(c=>c.Type.Contains("Land",StringComparison.Ordinal));
        return new(record.ActualTurn,p.PersonalTurn,p.Id,p.Life,p.HandCount,board.Count(c=>c.Type.Contains("Land",StringComparison.Ordinal)),
            board.Count(c=>c.Power is not null),board.Sum(c=>c.Power??0),p.Graveyard.Count,p.LibraryCount,board.Count(c=>c.Type.Contains("Artifact",StringComparison.Ordinal)),board.Count(c=>c.Type.Contains("Enchantment",StringComparison.Ordinal)),handLands,p.Hand.Count-handLands);
    }
    public async Task AnalyzeStoredDiagnostics(GameSlot slot)
    {
        if(slot.Result is null||slot.Result.ManaDetectionVersion==ManaAnalyzer.Version&&slot.Result.OpeningTrackingVersion==OpeningHandTracker.Version)return;
        var attempt=slot.Attempts.LastOrDefault(a=>a.Status=="completed");if(attempt?.Replay is null)return;
        var path=Path.Combine(paths.Replays,attempt.Replay);if(!File.Exists(path)||FileHash(path)!=attempt.ReplayHash)return;
        var samples=new List<TurnSample>();var openings=new Dictionary<string,OpeningHand>();var legacyCounts=new Dictionary<string,int>{{"A",0},{"B",0}};var legacyReliable=true;
        await using var file=File.OpenRead(path);await using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
        while(await reader.ReadLineAsync() is {} line){var r=Json.Decode<ReplayRecord>(line);if(r.Event=="GameEventTurnEnded"&&r.State?.ActivePlayer is {} active){samples.Add(Sample(r,r.State.Players.Single(p=>p.Id==active)));}
            if(r.Kind=="opening_hand"){var opening=OpeningHandTracker.FromRecord(r);openings[opening.Player]=opening;}
            if(r.Event=="GameEventMulligan"){
                var match=System.Text.RegularExpressions.Regex.Match(r.Detail??"",@"^GameEventMulligan\[player=([AB])\]$");
                if(match.Success)legacyCounts[match.Groups[1].Value]++;else legacyReliable=false;
            }
        }
        var openingVersion=OpeningHandTracker.Version;
        if(openings.Count==0&&legacyReliable&&legacyCounts.Values.All(n=>n<=7)){
            foreach(var (player,count) in legacyCounts)openings[player]=new(player,count,7-count,null,"Derived from complete legacy GameEventMulligan records under the fixed two-player London profile; pilot acceptance reason was not recorded.");
            openingVersion="london-legacy-events-v1";
        }
        await store.SaveSlot(slot with{Result=slot.Result with{Samples=samples,ManaFlags=ManaAnalyzer.Detect(samples),ManaDetectionVersion=ManaAnalyzer.Version,
            OpeningHands=openings.Count==2?openings.Values.OrderBy(h=>h.Player).ToList():null,OpeningTrackingVersion=openings.Count==2?openingVersion:null}});
    }
}
