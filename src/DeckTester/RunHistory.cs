namespace DeckTester;

// History needs aggregate outcomes, not game samples, mulligans or replay evidence.
public sealed record HistoryMatchup(string DeckA,string DeckB,int Valid,int WinsA,int WinsB,int Draws)
{
    public double? WinRateA=>Valid==0?null:(double)WinsA/Valid;
    public double? WinRateB=>Valid==0?null:(double)WinsB/Valid;
}
public sealed record HistoryRun(ActiveSessionProgress Progress,List<HistoryMatchup> Matchups,List<SessionTiming> Timings);
public sealed record HistorySnapshot(int Version,List<DeckManifest> Decks,Capabilities Capabilities,List<HistoryRun> Sessions);
