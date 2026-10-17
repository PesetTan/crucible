namespace DeckTester;

public sealed record ProviderDescriptor(string Id,Capabilities Capabilities,List<string> Capture,List<string> Evidence){public List<string> Cohorts {get;init;}=[];}
public sealed record ProviderExecution(GameResult Result,string? Replay,string? Hash,string? Raw=null);

/// <summary>The coordinator owns slots and durability; adapters own backend transport.</summary>
public sealed class EngineProviders(ForgeEngine forge,PyreEngine? pyre=null)
{
    public List<ProviderDescriptor> Discover()=>[
        new("forge",forge.Capabilities(),["full"],["forge-snapshot-playback"]),
        new("pyre",pyre?.Capabilities()??new(false,"Pyre unavailable","missing","missing","",["No native Pyre installation is registered."],[]),["summary","full"],["pyre-snapshot-playback","deterministic-resimulation","checkpoint-continuation"]){Cohorts=pyre?.Cohorts()??[]}
    ];
    void Known(string id){if(id!="forge"&&(id!="pyre"||pyre is null))throw new InvalidOperationException("Engine provider is unavailable: "+id);}
    public Capabilities Capabilities(string id="forge"){Known(id);return id=="forge"?forge.Capabilities():pyre!.Capabilities();}
    public List<string> Preflight(RunRequest request){if(request.ProviderId!="forge"&&(request.ProviderId!="pyre"||pyre is null))return ["Engine provider is unavailable: "+request.ProviderId];return request.ProviderId=="forge"?forge.Preflight(request):pyre!.Preflight(request);}
    public OptimizerPolicy? OptimizerPolicy(RunRequest request)=>request.ProviderId=="forge"?forge.OptimizerPolicy(request):null;
    public RunConfig Configure(RunRequest request,RunConfig original,string key)=>request.ProviderId=="forge"?original:pyre!.Configure(request,key);
    public bool HasBundle(RunConfig config)=>config.ProviderId=="forge"?forge.HasBundle(config):config.ProviderId=="pyre"&&pyre?.HasBundle(config)==true;
    public bool CanExecute(RunConfig config)=>config.ProviderId=="forge"||config.ProviderId=="pyre"&&config.Pyre is not null;
    public bool HasReusableReplay(GameSlot slot)=>slot.RunId.StartsWith("pyre-",StringComparison.Ordinal)?false:forge.HasReusableReplay(slot);
    public async Task<ProviderExecution> Play(SimulationRun run,GameSlot slot,int attempt,CancellationToken token){
        Known(run.Config.ProviderId);
        if(run.Config.ProviderId=="pyre")return await pyre!.Play(run,slot,attempt,token);
        var result=await forge.Play(run,slot,attempt,token);return new(result.Result,result.Replay,result.Hash);
    }
    public async Task Commit(Store store,SimulationRun run,GameSlot slot,ProviderExecution result){
        if(result.Raw is null){await store.SaveSlot(slot);return;}
        await pyre!.Commit(run,slot,result.Raw);
    }
    public async Task ReleaseWorkers(string runId){await forge.ReleaseWorkers(runId);if(pyre is not null)await pyre.ReleaseWorkers(runId);}
}
