using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckTester;

public sealed record PyreInstallation(string Executable,string ExecutableHash,string Metrics,string ControllerMetrics,Dictionary<string,string> Cohorts,int WorkerRssMiB=512,string? Runner=null,string? RunnerHash=null);
public sealed record PyreEvidenceInfo(bool Available,string Capture,string? RecordId,string? Checksum,string? TraceUrl,string Description);
public sealed record ProviderResources(string ProviderId,string RunId,string Measurement,int Workers,long PeakWorkerRssBytes,long PeakGroupRssBytes,int CompletedGames);
public sealed record NativeWorkerFailure(string Slot,int Attempt,string ExecutableHash,string Error,string Diagnostics);
public sealed record PyreCohortCoverage(string Cohort,string LegalityAsOf,List<string> DeckIds,List<string> Pilots);

/// <summary>Native whole-game adapter. Installation paths are local configuration, never request arguments.</summary>
public sealed class PyreEngine(AppPaths paths,Store store) : IAsyncDisposable
{
    public const string ProviderVersion="convergence-native-v1";
    const int MaxManifest=4*1024*1024,MaxFrame=144*1024*1024;
    readonly ConcurrentDictionary<string,Lazy<Pool>> pools=new();
    public static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public List<string> Cohorts(){try{return Installation().Installation.Cohorts.Keys.Order(StringComparer.Ordinal).ToList();}catch(IOException){return [];}}
    (PyreInstallation Installation,string Identity) Installation(){
        var file=Path.Combine(paths.Data,"pyre-provider.json");
        var text=File.ReadAllText(file);if(text.Length>64*1024)throw new InvalidDataException("Oversized Pyre installation.");
        var installation=Json.Decode<PyreInstallation>(text);
        if(!Path.IsPathFullyQualified(installation.Executable)||ForgeEngine.FileHash(installation.Executable)!=installation.ExecutableHash)
            throw new InvalidDataException("Pinned native Pyre executable is unavailable or changed.");
        if(installation.WorkerRssMiB is <32 or >2048||installation.Cohorts.Count is <1 or >16)throw new InvalidDataException("Invalid Pyre resource/cohort registration.");
        return(installation,Hash(text));
    }
    static async Task<string> ReadText(StreamReader reader,int maximum,CancellationToken token){
        var output=new StringBuilder();var buffer=new char[8192];int count;
        while((count=await reader.ReadAsync(buffer.AsMemory(),token))>0){if(output.Length+count>maximum)throw new InvalidDataException("Native output exceeds bounded response size.");output.Append(buffer,0,count);}
        return output.ToString();
    }
    static Process Start(string executable,params string[] args){
        var info=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)info.ArgumentList.Add(arg);
        return Process.Start(info)??throw new IOException("Could not launch native Pyre.");
    }
    static void Kill(Process process){try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){} }
    static string Invoke(PyreInstallation installation,params string[] args){
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));using var process=Start(installation.Executable,args);
        using var registration=deadline.Token.Register(()=>Kill(process));
        try{
            var output=ReadText(process.StandardOutput,MaxManifest,deadline.Token);
            var errors=ReadText(process.StandardError,64*1024,deadline.Token);
            process.StandardInput.Close();process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
            var text=output.GetAwaiter().GetResult();var error=errors.GetAwaiter().GetResult();
            if(process.ExitCode!=0)throw new InvalidDataException("Pyre preflight: "+error[..Math.Min(error.Length,2000)]);
            return text;
        }finally{Kill(process);}
    }
    public Capabilities Capabilities(){
        try{var(i,_)=Installation();using var doc=JsonDocument.Parse(Invoke(i,"capabilities"));var root=doc.RootElement;
            if(root.GetProperty("contract").GetString()!="spark-cli-v1"||root.GetProperty("artifact_sha256").GetString()!=i.ExecutableHash)throw new InvalidDataException("Native capability identity mismatch.");
            return new(true,"Pyre",i.ExecutableHash,Store.PyreAdapterHash,"",[],["Only registered exact frozen cohorts and their typed pilots are supported.","Summary games have no replay. Native traces use Pyre evidence APIs.","Baseline pilot policies do not establish competitive quality."]);
        }catch(Exception error)when(error is IOException or InvalidDataException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception){return new(false,"Pyre unavailable","missing",Store.PyreAdapterHash,"",[error.Message],[]);}
    }
    static Dictionary<string,int> Cards(DeckManifest deck)=>deck.Cards.Where(c=>c.Zone is "MainDeck" or "Main" or "main").GroupBy(c=>c.Name,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Sum(c=>c.Quantity),StringComparer.Ordinal);
    static bool SameCards(Dictionary<string,int> actual,JsonElement list)=>actual.OrderBy(k=>k.Key,StringComparer.Ordinal).Select(k=>(k.Key,k.Value)).SequenceEqual(list.EnumerateArray().Select(c=>(c.GetProperty("name").GetString()!,c.GetProperty("count").GetInt32())).OrderBy(c=>c.Item1,StringComparer.Ordinal));
    public PyreCohortCoverage Coverage(string cohort){
        var installation=Installation().Installation;
        if(!installation.Cohorts.TryGetValue(cohort,out var template))throw new ArgumentException("Unknown registered Pyre cohort.");
        using var doc=JsonDocument.Parse(Invoke(installation,"preflight","--manifest",template));var root=doc.RootElement;
        var lists=root.GetProperty("draft").GetProperty("decks").EnumerateArray().ToArray();
        return new(cohort,root.GetProperty("legality_as_of").GetString()!,store.Decks().Where(d=>lists.Any(list=>SameCards(Cards(d),list))).Select(d=>d.Id).ToList(),root.GetProperty("pilots").EnumerateArray().Select(p=>p.GetProperty("id").GetString()!).ToList());
    }
    public RunConfig Configure(RunRequest request,string key="preflight"){
        var(i,installationHash)=Installation();
        if(request.DeckIds.Count!=2||request.DeckIds.Distinct().Count()!=2)throw new InvalidDataException("Pyre requires two distinct exact frozen decks.");
        if(request.PyreCohort is null||!i.Cohorts.TryGetValue(request.PyreCohort,out var template))throw new InvalidDataException("Select a registered frozen Pyre cohort.");
        if(request.PyrePilotPolicyId is {} selectedPolicy){
            var policy=store.PublishedPyrePolicy(selectedPolicy);using var original=JsonDocument.Parse(File.ReadAllText(template));
            if(policy.Cohort!=request.PyreCohort||policy.EngineHash!=i.ExecutableHash||policy.BaselineTemplateHash!=Convert.ToHexStringLower(SHA256.HashData(PyreRecord.Canonical(original.RootElement))))throw new InvalidDataException("Published Pyre policy belongs to different frozen engine/cohort inputs.");
            template=Path.Combine(paths.Data,"pyre-optimizer-publications",selectedPolicy+".json");
            if(ForgeEngine.FileHash(template)!=policy.SourceHash)throw new InvalidDataException("Published Pyre content changed.");
        }
        if(request.OptimizerPolicyId is not null||request.PerformanceMode)throw new InvalidDataException("Forge policy/mode cannot be applied to Pyre.");
        if(request.Capture is not ("summary" or "full"))throw new InvalidDataException("Unknown Pyre capture profile.");
        var bytes=File.ReadAllBytes(template);if(bytes.Length>MaxManifest)throw new InvalidDataException("Oversized Pyre template.");
        using var normalized=JsonDocument.Parse(Invoke(i,"preflight","--manifest",template));
        var draft=JsonNode.Parse(normalized.RootElement.GetProperty("draft").GetRawText())!.AsObject();
        // Templates hold full Module graphs and typed pilot configuration; only run coordinates and limits change here.
        draft["namespace"]="crucible-convergence-v1/"+Hash(key);draft["master_seed"]=request.RootSeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        draft["capture"]=request.Capture;draft["metric_profile"]="standard-v1";
        draft["limits"]!["max_game_turns"]=request.TurnLimit;draft["limits"]!["max_elapsed_ms"]=(long)request.TimeoutSeconds*1000;
        using var listsDoc=JsonDocument.Parse(draft["decks"]!.ToJsonString());var lists=listsDoc.RootElement.EnumerateArray().ToArray();
        var remaining=request.DeckIds.Select(store.Deck).ToList();var ordered=new List<string>();
        foreach(var list in lists){var deck=remaining.SingleOrDefault(d=>SameCards(Cards(d),list))??throw new InvalidDataException("Selected deck does not match the registered frozen cohort.");ordered.Add(deck.Id);remaining.Remove(deck);}
        if(ordered.Count!=2)throw new InvalidDataException("Invalid frozen cohort registration.");
        var directory=Path.Combine(paths.Data,"pyre-preflight");Directory.CreateDirectory(directory);var temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");
        string manifest;try{File.WriteAllText(temporary,draft.ToJsonString());manifest=Invoke(i,"preflight","--manifest",temporary);}finally{File.Delete(temporary);}
        using var doc=JsonDocument.Parse(manifest);var resolved=doc.RootElement;
        if(resolved.GetProperty("artifact_sha256").GetString()!=i.ExecutableHash)throw new InvalidDataException("Preflight artifact mismatch.");
        var experiment=resolved.GetProperty("experiment_id").GetString()!;
        return new(ordered,request.GamesPerPair,request.Workers,request.TimeoutSeconds,request.TurnLimit,request.RootSeed,request.Diagnostic,i.ExecutableHash,Store.PyreAdapterHash,
            "pyre-selected",resolved.GetProperty("opening_policy").GetString()!,"pyre-full-seed-in-raw;legacy-int64-label-v1",request.StaggerStarts,request.EstimatedGameSeconds,request.StartLeadSeconds,request.StartupSpacingSeconds,
            PilotProfiles:ordered.Select((id,n)=>(id,pilot:resolved.GetProperty("pilots")[n].GetProperty("id").GetString()!)).ToDictionary(x=>x.id,x=>x.pilot)){
            Provider=new("pyre",ProviderVersion,resolved.GetProperty("metric_definition_hash").GetString()!,resolved.GetProperty("controller_definition_hash").GetString()!,experiment),
            Pyre=new(installationHash,manifest,Hash(manifest),request.Capture){PolicyId=request.PyrePilotPolicyId}};
    }
    public List<string> Preflight(RunRequest request){
        var errors=new List<string>();
        if(request.GamesPerPair<2||request.GamesPerPair>1068||request.GamesPerPair%2!=0)errors.Add("Pyre games per pair must be even, between 2 and 1068.");
        if(request.Workers is <1 or >4)errors.Add("Pyre supports one to four workers.");
        if(request.TimeoutSeconds is <10 or >1800||request.TurnLimit is <10 or >1000)errors.Add("Invalid Pyre time/turn limit.");
        if(request.StaggerStarts&&(request.Workers<2||request.StartupSpacingSeconds is not {} spacing||!double.IsFinite(spacing)||spacing is <0 or >5))errors.Add("Invalid Pyre startup spacing.");
        try{_=Configure(request);}catch(Exception error)when(error is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception){errors.Add(error.Message);}
        return errors;
    }
    public bool HasBundle(RunConfig config){try{var(i,id)=Installation();return config.Pyre is {} locked&&locked.InstallationHash==id&&Hash(locked.Manifest)==locked.ManifestHash&&config.EngineHash==i.ExecutableHash&&config.AdapterHash==Store.PyreAdapterHash;}catch(Exception error)when(error is IOException or InvalidDataException){return false;}}
    public async Task<ProviderExecution> Play(SimulationRun run,GameSlot slot,int attempt,CancellationToken token){
        if(!HasBundle(run.Config))throw new InvalidDataException("Pinned Pyre installation/run lock is unavailable.");
        var pending=pools.GetOrAdd(run.Id,_=>new Lazy<Pool>(()=>new Pool(paths,run,Installation().Installation)));
        Pool pool;try{pool=pending.Value;}catch{pools.TryRemove(new KeyValuePair<string,Lazy<Pool>>(run.Id,pending));throw;}
        string? payload=null;PyreRecord? validated=null;
        var installation=Installation().Installation;
        void Verify(JsonElement frame){
            payload=frame.GetProperty("canonical_result").GetString()!;
            validated=PyreRecord.Parse(payload,File.ReadAllText(installation.Metrics),File.ReadAllText(installation.ControllerMetrics));
            using var document=JsonDocument.Parse(payload);var root=document.RootElement;
            if(root.GetProperty("manifest").GetProperty("experiment_id").GetString()!=run.Config.Provider!.ExperimentId||root.GetProperty("starter").GetInt32()!=(slot.Starter==run.Config.DeckIds[0]?0:1)||root.GetProperty("replicate").GetInt32()!=slot.Index||root.GetProperty("attempt").GetInt32()!=attempt-1)throw new InvalidDataException("Pyre response does not belong to the requested attempt.");
            if(validated.Slot.Result is null){
                var failures=Path.Combine(paths.Data,"pyre-failures");Directory.CreateDirectory(failures);File.WriteAllText(Path.Combine(failures,validated.RecordId+".json"),payload);
                throw new InvalidDataException("Pyre returned an excluded rules/metric/pilot outcome; raw failure retained.");
            }
        }
        var response=await pool.Play(slot,attempt,token,Verify);
        var parsed=validated??throw new InvalidDataException("Native result was not validated.");
        var result=parsed.Slot.Result!;
        var winner=result.Winner==parsed.Slot.DeckA?run.Config.DeckIds[0]:result.Winner==parsed.Slot.DeckB?run.Config.DeckIds[1]:null;
        if(response.TryGetProperty("trace",out var trace)&&trace.ValueKind!=JsonValueKind.Null){
            var evidence=Path.Combine(paths.Data,"pyre-evidence");Directory.CreateDirectory(evidence);
            var target=Path.Combine(evidence,parsed.RecordId+".json");var raw=trace.GetRawText();
            if(File.Exists(target)){if(Hash(File.ReadAllText(target))!=Hash(raw))throw new InvalidDataException("Conflicting immutable Pyre trace.");}else File.WriteAllText(target,raw);
        }
        return new(result with{Winner=winner},null,null,payload!);
    }
    public Task Commit(SimulationRun run,GameSlot slot,string payload){var i=Installation().Installation;return store.ImportPyreResult(payload,File.ReadAllText(i.Metrics),File.ReadAllText(i.ControllerMetrics),run,slot);}
    public PyreEvidenceInfo Evidence(GameSlot slot){
        var payload=store.PyreDeliveryForSlot(slot);if(payload is null)return new(false,"unavailable",null,null,null,"No complete native delivery is retained.");
        using var doc=JsonDocument.Parse(payload);var r=doc.RootElement;var id=r.GetProperty("record_id").GetString()!;
        if(!r.GetProperty("replay_available").GetBoolean())return new(false,"summary",id,null,null,"Summary capture has no replay; standard metrics remain available.");
        if(!File.Exists(Path.Combine(paths.Data,"pyre-evidence",id+".json")))return new(false,"full",id,r.GetProperty("replay_hash").GetString(),null,"Full capture is recorded, but its trace artifact is unavailable.");
        var trace=ReadTrace(slot);return new(true,"full",id,r.GetProperty("replay_hash").GetString(),$"/api/v1/runs/{slot.RunId}/games/{slot.Id}/pyre-trace","Native Pyre snapshot playback, deterministic re-simulation and checkpoint continuation use this trace and the pinned executable.");
    }
    public JsonElement ReadTrace(GameSlot slot){
        var payload=store.PyreDeliveryForSlot(slot)??throw new KeyNotFoundException("No native delivery.");using var result=JsonDocument.Parse(payload);var r=result.RootElement;
        if(!r.GetProperty("replay_available").GetBoolean())throw new InvalidOperationException("Summary capture has no replay.");
        var id=r.GetProperty("record_id").GetString()!;if(id.Length!=64||id.Any(c=>!char.IsAsciiHexDigit(c)))throw new InvalidDataException("Invalid trace identity.");
        var path=Path.Combine(paths.Data,"pyre-evidence",id+".json");var text=File.ReadAllText(path);var node=JsonNode.Parse(text)!.AsObject();var checksum=node["checksum"]!.GetValue<string>();node["checksum"]="";
        using var d=JsonDocument.Parse(node.ToJsonString());if(checksum!=r.GetProperty("replay_hash").GetString()||PyreRecord.SemanticHash("spark-trace-v1",PyreRecord.Canonical(d.RootElement))!=checksum)throw new InvalidDataException("Native trace checksum mismatch.");
        using var trace=JsonDocument.Parse(text);return trace.RootElement.Clone();
    }
    public ProviderResources? Resources(string run){
        if(pools.TryGetValue(run,out var pool)&&pool.IsValueCreated)return pool.Value.Resources();
        var path=Path.Combine(paths.Data,"pyre-runs",run,"resources.json");return File.Exists(path)?Json.Decode<ProviderResources>(File.ReadAllText(path)):null;
    }
    public async Task ReleaseWorkers(string run){if(pools.TryRemove(run,out var pending)&&pending.IsValueCreated){
        var pool=pending.Value;var snapshot=pool.Resources();await pool.DisposeAsync();File.WriteAllText(Path.Combine(paths.Data,"pyre-runs",run,"resources.json"),Json.Encode(snapshot));
    }}
    public async ValueTask DisposeAsync(){foreach(var run in pools.Keys)await ReleaseWorkers(run);}

    sealed class Pool : IAsyncDisposable
    {
        readonly SemaphoreSlim capacity;readonly ConcurrentBag<Worker> idle=new();readonly ConcurrentDictionary<Worker,byte> all=new();
        readonly string manifest;readonly PyreInstallation installation;readonly SimulationRun run;int disposed;int launched;int completed;long peakWorker;long peakGroup;
        readonly object readings=new();
        void Sample(){lock(readings){peakWorker=Math.Max(peakWorker,all.Keys.Select(w=>w.PeakRss).DefaultIfEmpty().Max());using var parent=Process.GetCurrentProcess();peakGroup=Math.Max(peakGroup,parent.WorkingSet64+all.Keys.Sum(w=>w.CurrentRss));}}
        public ProviderResources Resources(){Sample();return new("pyre",run.Id,"sampled-process-rss-v1; coordinator plus native workers; transient peaks may be missed",launched,peakWorker,peakGroup,completed);}
        public Pool(AppPaths paths,SimulationRun run,PyreInstallation installation){
            this.run=run;this.installation=installation;capacity=new(run.Config.Workers);
            var folder=Path.Combine(paths.Data,"pyre-runs",run.Id);Directory.CreateDirectory(folder);manifest=Path.Combine(folder,"manifest.json");
            var text=run.Config.Pyre!.Manifest;
            if(File.Exists(manifest)){if(File.ReadAllText(manifest)!=text)throw new InvalidDataException("Pinned run manifest changed.");}
            else{
                var temporary=Path.Combine(folder,".manifest-"+Guid.NewGuid().ToString("N")+".partial");
                try{using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(Encoding.UTF8.GetBytes(text));file.Flush(flushToDisk:true);}File.Move(temporary,manifest);}
                finally{File.Delete(temporary);}
            }
        }
        public async Task<JsonElement> Play(GameSlot slot,int attempt,CancellationToken token,Action<JsonElement> verify){
            await capacity.WaitAsync(token);Worker? worker=null;var healthy=false;
            try{
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed)!=0,this);
                if(!idle.TryTake(out worker)){worker=new(installation,manifest,Sample);all.TryAdd(worker,0);Interlocked.Increment(ref launched);await worker.Handshake(run,token);}
                var value=await worker.Simulate(slot.Starter==run.Config.DeckIds[0]?0:1,slot.Index,attempt-1,run.Config.TimeoutSeconds,token);verify(value);Interlocked.Increment(ref completed);Sample();healthy=true;return value;
            }catch(Exception error){
                var folder=Path.Combine(Path.GetDirectoryName(manifest)!,"failures");Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,slot.Id+"-"+attempt+".json"),Json.Encode(new NativeWorkerFailure(slot.Id,attempt,installation.ExecutableHash,error.Message,worker?.Diagnostics??"No worker was launched.")));
                throw;
            }finally{if(worker is not null){if(healthy&&Volatile.Read(ref disposed)==0)idle.Add(worker);else{all.TryRemove(worker,out _);await worker.DisposeAsync();}}capacity.Release();}
        }
        public async ValueTask DisposeAsync(){Interlocked.Exchange(ref disposed,1);foreach(var worker in all.Keys)await worker.DisposeAsync();all.Clear();}
    }
    sealed class Worker : IAsyncDisposable
    {
        readonly Process process;readonly PyreInstallation installation;readonly Task<string> errors;readonly CancellationTokenSource stop=new();readonly Task watchdog;string? resourceFailure;
        public long CurrentRss;public long PeakRss;readonly Action sampled;
        public string Diagnostics=>errors.IsCompletedSuccessfully?errors.Result:errors.IsFaulted?"Worker stderr exceeded its bound or failed to decode.":"Worker stderr was still open at failure.";
        public Worker(PyreInstallation installation,string manifest,Action sampled){
            this.sampled=sampled;
            this.installation=installation;process=Start(installation.Executable,"worker","--manifest",manifest);errors=ReadText(process.StandardError,64*1024,stop.Token);
            watchdog=Watch();
        }
        async Task Watch(){try{while(!stop.IsCancellationRequested&&!process.HasExited){process.Refresh();CurrentRss=process.WorkingSet64;PeakRss=Math.Max(PeakRss,CurrentRss);sampled();if(CurrentRss>(long)installation.WorkerRssMiB*1024*1024){resourceFailure="worker_memory_limit";Kill(process);break;}await Task.Delay(100,stop.Token);}}catch(OperationCanceledException){}catch(InvalidOperationException){} }
        async Task<JsonElement> Receive(CancellationToken token){
            var header=new byte[4];await process.StandardOutput.BaseStream.ReadExactlyAsync(header,token);var length=BinaryPrimitives.ReadUInt32BigEndian(header);
            if(length==0||length>MaxFrame)throw new InvalidDataException("Native worker frame size limit.");
            var bytes=new byte[(int)length];await process.StandardOutput.BaseStream.ReadExactlyAsync(bytes,token);
            using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=96});var value=doc.RootElement;
            if(value.GetProperty("protocol").GetString()!="furnace-worker-v1")throw new InvalidDataException("Unsupported Pyre worker protocol.");return value.Clone();
        }
        public async Task Handshake(SimulationRun run,CancellationToken token){
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var frame=await Receive(timeout.Token);var m=frame.GetProperty("manifest");
            using var expected=JsonDocument.Parse(run.Config.Pyre!.Manifest);
            if(!PyreRecord.Canonical(m).AsSpan().SequenceEqual(PyreRecord.Canonical(expected.RootElement))||frame.GetProperty("max_in_flight").GetInt32()!=1||frame.GetProperty("pid").GetInt32()!=process.Id||frame.GetProperty("max_request_bytes").GetInt32()!=MaxManifest||frame.GetProperty("max_response_bytes").GetInt32()!=MaxFrame)throw new InvalidDataException("Native handshake identity/limits mismatch.");
        }
        public async Task<JsonElement> Simulate(int starter,int replicate,int attempt,int seconds,CancellationToken token){
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            using var registration=timeout.Token.Register(()=>Kill(process));
            try{
                var bytes=Encoding.UTF8.GetBytes($"{{\"op\":\"simulate\",\"starter\":{starter},\"replicate\":{replicate},\"attempt\":{attempt}}}");var header=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(header,(uint)bytes.Length);
                await process.StandardInput.BaseStream.WriteAsync(header,timeout.Token);await process.StandardInput.BaseStream.WriteAsync(bytes,timeout.Token);await process.StandardInput.BaseStream.FlushAsync(timeout.Token);
                return await Receive(timeout.Token);
            }catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new TimeoutException("Native Pyre attempt time limit.");}
            catch(Exception error)when(error is IOException or EndOfStreamException){throw new IOException(resourceFailure??"Native worker ended before terminal response.",error);}
        }
        public async ValueTask DisposeAsync(){Kill(process);stop.Cancel();try{await process.WaitForExitAsync();await watchdog;await errors;}catch(OperationCanceledException){}finally{process.Dispose();stop.Dispose();}}
    }
}
