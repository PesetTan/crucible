using DeckTester;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var capability = new Capabilities(true, "Forge", "engine", "bridge", "java", [], []) { AttributesHash = "attributes" };
HistoryRun Session(string id, int days, Dictionary<string, string>? profiles, List<HistoryMatchup> matches,
    string status = "completed", string bridge = "bridge", int reused = 0) {
    var config = new RunConfig(["a", "b", "c"], 100, 2, 180, 150, 42, true, "engine", bridge, PilotProfiles: profiles) { AttributesHash = "attributes" };
    var run = new SimulationRun(id, DateTimeOffset.Parse("2026-10-14T12:00:00Z").AddDays(days), status, config, 300);
    return new(new(run, matches.Sum(m => m.Valid), 10, 0, 1, 5, reused), matches, []);
}
var sessions = new List<HistoryRun> {
    Session("older", -1, new() { ["a"] = "pilot-v1", ["b"] = "opponent" }, [new("a", "b", 100, 70, 25, 5)], bridge: "older-bridge"),
    Session("latest", 0, new() { ["a"] = "pilot-v1", ["b"] = "opponent", ["c"] = "pilot-v1" },
        [new("a", "b", 10, 4, 5, 1), new("a", "c", 10, 8, 2, 0), new("b", "c", 20, 5, 15, 0)], "running", reused: 20),
    Session("legacy", 1, null, [new("a", "b", 100, 100, 0, 0)]),
    Session("no-games", 2, new() { ["a"] = "pilot-v1", ["b"] = "opponent" }, [new("a", "b", 0, 0, 0, 0)]),
    Session("new-version", 0, new() { ["a"] = "pilot-v2", ["b"] = "opponent" }, [new("a", "b", 10, 9, 1, 0)]),
    Session("bad-outcomes", 3, new() { ["a"] = "pilot-v1" }, [new("a", "b", 5, 3, 3, 0)])
};
var rates = PilotWinRates.FromHistory(new(1, [], capability, sessions));
var latest = rates["pilot-v1"][0];
Check(latest.Run.Id == "latest" && latest.Games == 50, "Latest measured session includes only valid completed appearances, without pooling reused sessions.");
Check(latest.Wins == 29 && latest.Losses == 20 && latest.Draws == 1 && latest.Rate == 0.58, "Both player sides, multiple decks, same-pilot games and draws use the correct denominator.");
Check(latest.DeckVersions == 2 && latest.Matchups == 3 && latest.Partial, "Partial session and exact deck/matchup coverage are visible.");
Check(rates["pilot-v1"].Count == 2 && !rates["pilot-v1"][1].MatchesBuild(capability) && latest.MatchesBuild(capability), "Builds stay separate and historical evidence is labeled.");
Check(rates["pilot-v2"][0].Rate == 0.9 && rates["pilot-v1"][1].Games == 100, "Versions and historical sample sizes stay separate.");
Check(PilotWinRates.FromHistory(new(1, [], capability, [sessions[2]])).Count == 0, "Missing assignments remain unmeasured rather than inferred.");
Console.WriteLine("Pilot win-rate checks passed: exact assignments, versions/builds, latest measured session, cached-session isolation, partial results, both sides, mirror games and draws.");
