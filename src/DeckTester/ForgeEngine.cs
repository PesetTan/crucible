using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DeckTester;

public sealed class ForgeEngine(AppPaths paths, Store store)
{
    private readonly Lazy<Dictionary<string,string>> scripts = new(() => LoadScripts(paths));
    public static string FileHash(string path) { using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static Dictionary<string,string> LoadScripts(AppPaths paths)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var folder=Path.Combine(paths.Vendor,"res","cardsfolder");
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
    public Capabilities Capabilities()
    {
        var blockers=new List<string>(); if(!File.Exists(paths.Java))blockers.Add("Project-local Java runtime is missing. See engine setup instructions.");
        if(!File.Exists(paths.Jar))blockers.Add("Pinned Forge 2.0.15 engine is missing.");
        if(!File.Exists(Path.Combine(paths.Bridge,"ForgeBridge.class")))blockers.Add("The engine bridge has not been compiled.");
        if(!Directory.Exists(Path.Combine(paths.Vendor,"res","cardsfolder")))blockers.Add("Forge card resources are missing.");
        var folders=new[]{"cardsfolder","tokenscripts","editions","ai","formats","effects","defaults"};
        var resources=folders.Select(f=>Path.Combine(paths.Vendor,"res",f)).Where(Directory.Exists).SelectMany(d=>Directory.EnumerateFiles(d,"*",SearchOption.AllDirectories));
        var engineFiles=new[]{paths.Jar}.Concat(resources).Where(File.Exists).Order(StringComparer.Ordinal).ToArray();
        var engineHash=File.Exists(paths.Jar)?DeckImporter.Hash(string.Join("\n",engineFiles.Select(f=>Path.GetRelativePath(paths.Vendor,f)+":"+FileHash(f)))):"missing";
        var adapterHash=Directory.Exists(paths.Bridge)?DeckImporter.Hash(string.Join("\n",Directory.EnumerateFiles(paths.Bridge,"*.class").Order(StringComparer.Ordinal).Select(FileHash))):"missing";
        return new(blockers.Count==0,"Forge 2.0.15",engineHash,adapterHash,paths.Java,blockers,
            ["Forge Default AI is unvalidated for these decks; combo sequencing and hidden-information behavior have not been independently audited.",
             "Card scripts are present, but required interaction and generated-object coverage is unvalidated.",
             "London mulligans approximate Arena Bo1 opening hands. No hand smoothing is guessed.",
             "Replay captures engine snapshots; event narratives are engine diagnostics, not a complete normalized decision protocol.",
             "Exact skipped-turn cycle grouping and mana-pool replay are unavailable; actual turns are authoritative.",
             "Legality uses the dated local snapshot and is not independently refreshed by this app."]);
    }
    private string ForgeCardName(string name)
    {
        if (scripts.Value.ContainsKey(name)) return name;
        var firstFace = name.Split(" // ", 2, StringSplitOptions.None)[0];
        return firstFace != name && scripts.Value.ContainsKey(firstFace) ? firstFace : name;
    }
    public DeckCoverage Coverage(DeckManifest deck)
    {
        var cards=deck.Cards.Where(c=>c.Zone=="MainDeck").Select(c=>scripts.Value.TryGetValue(ForgeCardName(c.Name),out var script)?
            new CoverageItem(c.Name,"present_unvalidated",Path.GetRelativePath(paths.Vendor,script),null):new CoverageItem(c.Name,"missing",null,"No exact Forge card identity found.")).ToList();
        return new(deck.Id,cards.Any(c=>c.Status=="missing")?"blocked":"exploratory",cards,
            ["Faces, generated tokens, copy interactions, rules fixtures, and pilot behavior remain unvalidated."]);
    }
    public List<string> Preflight(RunRequest request)
    {
        var blockers=Capabilities().Blockers.ToList();
        if(!request.Diagnostic)blockers.Add("Validated runs are unavailable until rules, pilot, and information-boundary gates pass. Use an explicitly exploratory run.");
        if(request.DeckIds.Distinct().Count()!=request.DeckIds.Count||request.DeckIds.Count is <2 or >ChartPalette.MaximumDecks)blockers.Add("Select two to five distinct deck versions.");
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
            try { var deck=store.Deck(id);var coverage=Coverage(deck);blockers.AddRange(coverage.Cards.Where(c=>c.Status=="missing").Select(c=>$"{deck.Name}: {c.Name} is unsupported."));
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
        var attemptId=$"{slot.Id}-{attemptNumber}";
        var outputPath=Path.Combine(paths.Replays,attemptId+".jsonl.gz");var temp=outputPath+".partial";
        var info=new ProcessStartInfo(paths.Java) { WorkingDirectory=paths.Vendor,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false };
        foreach(var arg in new[]{"-Xmx1200m","-Djava.awt.headless=true","-Duser.home="+Path.Combine(paths.Data,"engine-home"),"-cp",paths.Bridge+Path.PathSeparator+paths.Jar,"ForgeBridge"})info.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=info}; var timer=Stopwatch.StartNew();
        if(!process.Start())throw new InvalidOperationException("Could not launch the rules engine.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(run.Config.TimeoutSeconds));
        using var registration=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){} });
        var stderr=new StringBuilder();var stderrTask=Task.Run(async()=>{while(await process.StandardError.ReadLineAsync() is {} line){stderr.AppendLine(line);if(stderr.Length>16000)stderr.Remove(0,stderr.Length-16000);}},CancellationToken.None);
        var request=new {attemptId,seed=slot.Seed,starter=slot.Starter==slot.DeckA?0:1,turnLimit=run.Config.TurnLimit,
            deckA=store.Deck(slot.DeckA).Cards.Where(c=>c.Zone=="MainDeck").Select(c=>new{Name=ForgeCardName(c.Name),c.Quantity}),deckB=store.Deck(slot.DeckB).Cards.Where(c=>c.Zone=="MainDeck").Select(c=>new{Name=ForgeCardName(c.Name),c.Quantity})};
        GameResult? result=null;var samples=new List<TurnSample>();var openingHands=new Dictionary<string,OpeningHand>();var personalTurns=new Dictionary<string,int>();long sequence=0;long bytes=0;bool started=false;
        string? failure=null;int actualTurns=0;
        try {
            await process.StandardInput.WriteLineAsync(Json.Encode(request));process.StandardInput.Close();
            await using(var file=File.Create(temp))await using(var gzip=new GZipStream(file,CompressionLevel.Fastest))await using(var writer=new StreamWriter(gzip,new UTF8Encoding(false))) {
                while(await process.StandardOutput.ReadLineAsync(timeout.Token) is {} line) {
                    bytes+=Encoding.UTF8.GetByteCount(line);if(bytes>256*1024*1024)throw new InvalidDataException("Replay size limit reached.");
                    var record=Json.Decode<ReplayRecord>(line);
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
            await process.WaitForExitAsync(timeout.Token);await stderrTask;
            if(process.ExitCode!=0||failure is not null||result is null)throw new InvalidOperationException(failure??$"Engine exited {process.ExitCode} without a complete result. {stderr}");
            File.Move(temp,outputPath,overwrite:false);return(result,Path.GetFileName(outputPath),FileHash(outputPath));
        }catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){throw new TimeoutException($"Attempt exceeded {run.Config.TimeoutSeconds/60.0:0.##} minutes; not a rules draw.");}
        finally {
            try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}
            await process.WaitForExitAsync(CancellationToken.None);await stderrTask;
            await File.WriteAllTextAsync(Path.Combine(paths.Replays,attemptId+".log"),stderr.ToString());
            if(File.Exists(temp))File.Move(temp,Path.Combine(paths.Replays,attemptId+".incomplete.gz"),overwrite:true);
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
