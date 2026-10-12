namespace DeckTester;

public static class Metrics
{
    public static List<ChartPoint> WinRatesByLength(IEnumerable<GameSlot> slots,string deckId,bool rounds=true)=>slots
        .Where(s=>s.Status=="completed"&&s.Result is not null&&s.Result.ActualTurns>0&&(s.DeckA==deckId||s.DeckB==deckId))
        .GroupBy(s=>rounds?(s.Result!.ActualTurns+1)/2:s.Result!.ActualTurns).OrderBy(g=>g.Key)
        .Select(g=>new ChartPoint(g.Key,100.0*g.Count(s=>!s.Result!.Draw&&s.Result.Winner==deckId)/g.Count(),g.Count())).ToList();

    public static double[]? Wilson(int wins,int n)
    {
        if(n==0)return null;const double z=1.96;var p=(double)wins/n;var denominator=1+z*z/n;
        var center=(p+z*z/(2*n))/denominator;var half=z*Math.Sqrt(p*(1-p)/n+z*z/(4.0*n*n))/denominator;return [center-half,center+half];
    }
    public static RunSummary Summarize(SimulationRun run,List<GameSlot> slots)
    {
        var matchups=slots.GroupBy(s=>(s.DeckA,s.DeckB)).Select(g=>{
            var valid=g.Where(s=>s.Status=="completed"&&s.Result is not null).ToList();var n=valid.Count;
            var wa=valid.Count(s=>s.Result!.Winner==g.Key.DeckA);var wb=valid.Count(s=>s.Result!.Winner==g.Key.DeckB);var d=valid.Count(s=>s.Result!.Draw);
            return new MatchupSummary(g.Key.DeckA,g.Key.DeckB,g.Count(),n,wa,wb,d,g.Count(s=>s.Status=="failed"),g.Count(s=>s.Status=="cancelled"),g.Count(s=>s.Status=="pending"),g.Count(s=>s.Status=="running"),
                g.Sum(s=>s.Attempts.Count(a=>a.Status is "failed" or "interrupted")),valid.Count(s=>s.Starter==g.Key.DeckA),valid.Count(s=>s.Starter==g.Key.DeckB),n==0?null:(double)wa/n,n==0?null:(double)wb/n,n==0?null:(double)d/n,
                Wilson(wa,n),Wilson(wb,n),valid.Count(s=>s.Result!.Winner==g.Key.DeckA&&s.Starter==g.Key.DeckA),valid.Count(s=>s.Result!.Winner==g.Key.DeckA&&s.Starter==g.Key.DeckB),
                valid.Count(s=>s.Result!.Winner==g.Key.DeckB&&s.Starter==g.Key.DeckB),valid.Count(s=>s.Result!.Winner==g.Key.DeckB&&s.Starter==g.Key.DeckA));
        }).ToList();
        return new(run,slots.Count(s=>s.Status=="completed"),slots.Count(s=>s.Status=="failed"),slots.Count(s=>s.Status=="cancelled"),slots.Count(s=>s.Status=="running"),slots.Count(s=>s.Status=="pending"),
            matchups.All(m=>m.Valid==run.Config.GamesPerPair&&m.PlayA==run.Config.GamesPerPair/2&&m.PlayB==run.Config.GamesPerPair/2),matchups,
            slots.Count(s=>s.Result?.ManaFlags?.Any(f=>f.Kind=="mana-starved")==true),slots.Count(s=>s.Result?.ManaFlags?.Any(f=>f.Kind=="mana-flooded")==true),slots.Count(s=>s.Result?.ManaDetectionVersion==ManaAnalyzer.Version),OpeningHandTracker.Summaries(run,slots),slots.Count(s=>s.CachedFromRunId is not null),slots.Where(s=>s.CachedFromRunId is not null).Select(s=>s.CachedFromRunId!).Distinct().ToList());
    }
    public static string Rate(double? rate)=>rate is null?"—":$"{rate:P1}";
    public static double? Median(IEnumerable<int> values){var v=values.Order().ToArray();return v.Length==0?null:v.Length%2==1?v[v.Length/2]:(v[v.Length/2-1]+v[v.Length/2])/2.0;}
}
