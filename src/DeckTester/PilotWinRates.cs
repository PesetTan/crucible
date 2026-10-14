namespace DeckTester;

public sealed record PilotSessionRate(string Profile, SimulationRun Run, int Wins, int Losses, int Draws, int DeckVersions, int Matchups)
{
    // Count player appearances: a game with the same pilot on both sides contributes two records.
    public int Games => Wins + Losses + Draws;
    public double Rate => (double)Wins / Games;
    public bool Partial => Run.Status != "completed";
    public bool MatchesBuild(Capabilities active) => Run.Config.EngineHash == active.EngineHash
        && Run.Config.AdapterHash == active.AdapterHash && Run.Config.AttributesHash == active.AttributesHash;
}

public static class PilotWinRates
{
    // Keep each session separate. Pooling sessions would repeat cached games and mix experiments.
    // Never reconstruct historical assignments from today's deck classification or pilot policy.
    public static Dictionary<string, List<PilotSessionRate>> FromHistory(HistorySnapshot history)
    {
        var records = new List<PilotSessionRate>();
        foreach (var session in history.Sessions)
        {
            var run = session.Progress.Run;
            var profiles = run.Config.PilotProfiles;
            if (profiles is null) continue;
            var sides = session.Matchups.Where(m => m.Valid > 0 && m.WinsA + m.WinsB + m.Draws == m.Valid)
                .SelectMany(m => new[] {
                    new { Deck = m.DeckA, Opponent = m.DeckB, Wins = m.WinsA, Losses = m.WinsB, m.Draws },
                    new { Deck = m.DeckB, Opponent = m.DeckA, Wins = m.WinsB, Losses = m.WinsA, m.Draws }
                }).Where(s => !string.IsNullOrWhiteSpace(profiles.GetValueOrDefault(s.Deck)))
                .GroupBy(s => profiles[s.Deck]);
            foreach (var group in sides)
                records.Add(new(group.Key, run, group.Sum(s => s.Wins), group.Sum(s => s.Losses), group.Sum(s => s.Draws),
                    group.Select(s => s.Deck).Distinct().Count(), group.Select(s => string.Join("/", new[] { s.Deck, s.Opponent }.Order())).Distinct().Count()));
        }
        return records.GroupBy(r => r.Profile).ToDictionary(g => g.Key,
            g => g.OrderByDescending(r => r.Run.CreatedAt).ThenBy(r => r.Run.Id, StringComparer.Ordinal).ToList());
    }
}
