namespace DeckTester;

public sealed record TimingSample(string DeckA, string DeckB, int Workers, int TimeoutSeconds, int TurnLimit, double Seconds,
    bool StaggerStarts = false, double? StartupSpacingSeconds = null);
public sealed record SessionTimingSample(RunConfig Config,double ActiveSeconds,double ScheduledSeconds,int AttemptCount);
public sealed record RuntimeRange(double LowerSeconds, double UpperSeconds, int Observations, bool Extrapolated,int SessionObservations = 0);

public static class RuntimeEstimate
{
    public static RuntimeRange? Calculate(List<string> deckIds, int games, int workers, int timeout, int turnLimit,
        bool stagger, int estimatedGameSeconds, int leadSeconds, IReadOnlyList<TimingSample> history, double? startupSpacingSeconds = 1, IReadOnlySet<CachedGameKey>? cachedGames = null,IReadOnlyList<SessionTimingSample>? sessions = null)
    {
        if(deckIds.Count<2||games is <2 or >1000||games%2!=0||workers is <1 or >4||timeout is <10 or >1800||turnLimit is <10 or >1000
            ||stagger&&(workers<2||(startupSpacingSeconds is {} spacing
                ?!double.IsFinite(spacing)||spacing is <0 or >5
                :estimatedGameSeconds is <1 or >1800||leadSeconds<0||leadSeconds>=estimatedGameSeconds)))return null;
        var ids=deckIds.Order(StringComparer.Ordinal).ToList();
        var durations=new List<double>();var observations=new HashSet<TimingSample>(ReferenceEqualityComparer.Instance);var extrapolated=false;
        var valid=history.Where(s=>double.IsFinite(s.Seconds)&&s.Seconds>0).ToList();
        for(var a=0;a<ids.Count;a++)for(var b=a+1;b<ids.Count;b++){
            var missing=games-(cachedGames?.Count(key=>key.DeckA==ids[a]&&key.DeckB==ids[b]&&key.Index<games/2)??0);
            if(missing<=0)continue;
            var pair=valid.Where(s=>s.DeckA==ids[a]&&s.DeckB==ids[b]||s.DeckA==ids[b]&&s.DeckB==ids[a]).ToList();
            bool Comparable(TimingSample s)=>s.Workers==workers&&s.TimeoutSeconds==timeout&&s.TurnLimit==turnLimit
                &&s.StaggerStarts==stagger&&(!stagger||s.StartupSpacingSeconds==startupSpacingSeconds);
            var exact=pair.Where(Comparable).ToList();
            var samples=exact.Count>=5?exact:pair.Count>=5?pair:valid;
            var comparable=samples.Where(Comparable).ToList();
            if(comparable.Count>=5)samples=comparable;
            var measured=samples.Count>=5;
            extrapolated|=!measured||exact.Count<5;
            // Higher concurrency can slow each engine down. Avoid promising linear speedups
            // when this machine has only been measured with fewer parallel games.
            var seconds=measured?samples.Average(s=>Math.Min(timeout,s.Seconds*Math.Max(1.0,(double)workers/Math.Max(1,s.Workers))))
                :Math.Min(timeout,Math.Max(1,estimatedGameSeconds));
            if(measured)foreach(var sample in samples)observations.Add(sample);
            for(var game=0;game<missing;game++)durations.Add(seconds);
        }
        var legacy=stagger&&startupSpacingSeconds is null;
        var interval=stagger?startupSpacingSeconds??estimatedGameSeconds-leadSeconds:0;
        // These are planning ranges, not statistical confidence intervals.
        var lower=Schedule(durations,workers,interval,legacy,extrapolated ? .6 : .75);
        var upper=Schedule(durations,workers,interval,legacy,extrapolated?1.8:1.4);
        var comparableSessions=(sessions??[]).Where(s=>s.AttemptCount>=5&&double.IsFinite(s.ActiveSeconds)&&s.ActiveSeconds>0
            &&double.IsFinite(s.ScheduledSeconds)&&s.ScheduledSeconds>0&&StoreCombination(s.Config.DeckIds,ids)
            &&s.Config.Workers==workers&&s.Config.TimeoutSeconds==timeout&&s.Config.TurnLimit==turnLimit
            &&s.Config.StaggerStarts==stagger&&(!stagger||(s.Config.StartupSpacingSeconds==startupSpacingSeconds
                &&(startupSpacingSeconds is not null||s.Config.EstimatedGameSeconds==estimatedGameSeconds&&s.Config.StartLeadSeconds==leadSeconds)))).Take(20).ToList();
        var adjustment=comparableSessions.Count==0?1:comparableSessions.Sum(s=>s.ActiveSeconds)/comparableSessions.Sum(s=>s.ScheduledSeconds);
        return new(lower*adjustment,upper*adjustment,observations.Count,extrapolated,comparableSessions.Count);
    }

    private static bool StoreCombination(IEnumerable<string> left,IEnumerable<string> right)=>left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal),StringComparer.Ordinal);
    public static double MeasuredSchedule(IEnumerable<double> durations,RunConfig config)=>Schedule(durations.ToList(),config.Workers,
        config.StaggerStarts?config.StartupSpacingSeconds??config.EstimatedGameSeconds-config.StartLeadSeconds:0,
        config.StaggerStarts&&config.StartupSpacingSeconds is null,1);
    public static string SessionDuration(IEnumerable<SessionTiming> timings)
    {
        var records=timings.ToList();if(records.Count==0)return "Runtime not recorded";
        var seconds=records.Sum(t=>t.ActiveSeconds+(t.Status=="running"?Math.Max(0,(DateTimeOffset.UtcNow-t.UpdatedAt).TotalSeconds):0));
        return $"Active Runtime: {(seconds==0?"0 sec":Duration(seconds))}"+(records.Any(t=>t.Status=="interrupted")?" · interrupted periods include only recorded time":"");
    }

    private static double Schedule(List<double> durations,int workers,double interval,bool legacy,double factor)
    {
        var free=Enumerable.Range(0,workers).Select(i=>legacy?0:i*interval).ToArray();
        var lastStart=-interval;var finished=0.0;
        foreach(var duration in durations){
            var worker=Array.IndexOf(free,free.Min());
            var start=legacy?Math.Max(free[worker],lastStart+interval):free[worker];
            free[worker]=start+duration*factor;lastStart=start;
            finished=Math.Max(finished,free[worker]);
        }
        return finished;
    }

    public static string Duration(double seconds)
    {
        if(seconds<60)return $"{Math.Max(1,(int)Math.Ceiling(seconds))} sec";
        if(seconds<3600)return $"{(int)Math.Ceiling(seconds/60)} min";
        var minutes=(int)Math.Ceiling(seconds/60);
        return minutes%60==0?$"{minutes/60} hr":$"{minutes/60} hr {minutes%60} min";
    }
}
