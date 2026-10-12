namespace DeckTester;

public static class OpeningHandTracker
{
    public const string Version="london-opening-v1";
    public static OpeningHand FromRecord(ReplayRecord record)
    {
        if(record.Player is not ("A" or "B")||record.Mulligans is null or <0 or >7||record.KeptHandSize is null or <0 or >7||record.PolicyAccepted is null)
            throw new InvalidDataException("Invalid opening-hand record.");
        return new(record.Player,record.Mulligans.Value,record.KeptHandSize.Value,record.PolicyAccepted,
            record.PolicyAccepted.Value?"Engine-reported mulligans before the Forge Default pilot accepted the final hand.":"Engine-reported final hand; rules forced a keep at the mulligan limit.");
    }
    public static List<DeckMulliganSummary> Summaries(SimulationRun run,List<GameSlot> slots)
    {
        var observations=slots.Where(s=>s.Result?.OpeningHands is not null).SelectMany(s=>s.Result!.OpeningHands!.Select(h=>new{deck=h.Player=="A"?s.DeckA:s.DeckB,hand=h,onPlay=s.Starter==(h.Player=="A"?s.DeckA:s.DeckB)})).ToList();
        return run.Config.DeckIds.Select(deck=>{
            var rows=observations.Where(o=>o.deck==deck).ToList();var play=rows.Where(o=>o.onPlay).ToList();var draw=rows.Where(o=>!o.onPlay).ToList();
            return new DeckMulliganSummary(deck,rows.Count,rows.Sum(o=>o.hand.Mulligans),rows.Count==0?null:rows.Average(o=>o.hand.Mulligans),Metrics.Median(rows.Select(o=>o.hand.Mulligans)),rows.Count==0?null:rows.Max(o=>o.hand.Mulligans),
                rows.Count(o=>o.hand.Mulligans==0),rows.Count(o=>o.hand.Mulligans==1),rows.Count(o=>o.hand.Mulligans==2),rows.Count(o=>o.hand.Mulligans>=3),rows.Count==0?null:rows.Average(o=>o.hand.KeptHandSize),play.Count==0?null:play.Average(o=>o.hand.Mulligans),draw.Count==0?null:draw.Average(o=>o.hand.Mulligans));
        }).ToList();
    }
}
