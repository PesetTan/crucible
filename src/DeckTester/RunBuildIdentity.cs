namespace DeckTester;

public static class RunBuildIdentity
{
    public static string Short(string hash) => string.IsNullOrEmpty(hash) ? "unknown" : hash[..Math.Min(8, hash.Length)];

    public static string Difference(RunConfig run, Capabilities active)
    {
        var defaults = new RunConfig([], 2, 2, 180, 150, 42, true, active.EngineHash, active.AdapterHash, Pilot:ReanimatorPilots.CurrentAggroMode);
        if (run.EngineHash != active.EngineHash) return "Forge engine changed";
        if (run.AdapterHash != active.AdapterHash) return "Pilot bridge changed";
        if (run.Pilot != defaults.Pilot) return "Pilot policy changed";
        if (run.OpeningPolicy != defaults.OpeningPolicy || run.SeedScheme != defaults.SeedScheme) return "Run policy changed";
        return "Matches active build";
    }

    public static bool MatchesActive(RunConfig run, Capabilities active) => Difference(run, active) == "Matches active build";
}
