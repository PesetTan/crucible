using System.Collections.Concurrent;

namespace DeckTester;

// Reuse completed comparisons only while the recorded session version is unchanged.
public sealed class WorkshopComparisonCache
{
    private readonly ConcurrentDictionary<(string Run,string Deck,string Opponent),(DateTimeOffset? Updated,int Total,WorkshopMatchup Match)> completed=new();
    public async Task<WorkshopMatchup> Load(SimulationRun run,string deck,string opponent,
        Func<CancellationToken,Task<WorkshopMatchup>> fetch,CancellationToken cancellation=default,bool force=false)
    {
        cancellation.ThrowIfCancellationRequested();
        var key=(run.Id,deck,opponent);
        if(!force&&run.Status=="completed"&&completed.TryGetValue(key,out var cached)&&cached.Updated==run.UpdatedAt&&cached.Total==run.Total)
            return cached.Match;
        var match=await fetch(cancellation);
        cancellation.ThrowIfCancellationRequested();
        if(run.Status=="completed"&&match.Run.Status=="completed"&&match.Run.UpdatedAt==run.UpdatedAt&&match.Run.Total==run.Total)
            completed[key]=(run.UpdatedAt,run.Total,match);
        else completed.TryRemove(key,out _);
        return match;
    }
}
