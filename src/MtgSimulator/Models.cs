using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgSimulator;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Empty JSON document.");
}

public sealed record CardEntry(int ArenaId, string Name, int Quantity, string Zone, string Set, string Type, string Cost, string Text, bool LocallyLegal, string LinkedFaces);
public sealed record DeckManifest(string Id, string Name, string SourceType, string Variant, string SourceDeckId, string Snapshot, string? SourceUrl,
    string ListHash, string ManifestHash, DateTimeOffset ImportedAt, List<CardEntry> Cards, string FreshnessNote)
{
    public int MainCount => Cards.Where(c => c.Zone == "MainDeck").Sum(c => c.Quantity);
    public int CreatureCount => Cards.Where(c => c.Zone == "MainDeck" && c.Type.Contains("Creature", StringComparison.Ordinal)).Sum(c => c.Quantity);
    public int LandCount => Cards.Where(c => c.Zone == "MainDeck" && c.Type.Contains("Land", StringComparison.Ordinal)).Sum(c => c.Quantity);
    public int SideCount => Cards.Where(c => c.Zone == "Sideboard").Sum(c => c.Quantity);
}
public sealed record CoverageItem(string Name, string Status, string? Script, string? Reason);
public sealed record DeckCoverage(string DeckId, string Status, List<CoverageItem> Cards, List<string> Limitations);
public sealed record Capabilities(bool Available, string Engine, string EngineHash, string AdapterHash, string JavaPath, List<string> Blockers, List<string> Limitations);
public sealed record RunRequest(List<string> DeckIds, int GamesPerPair = 200, int Workers = 1, int TimeoutSeconds = 180, int TurnLimit = 150, long RootSeed = 42, bool Diagnostic = true);
public sealed record RunConfig(List<string> DeckIds, int GamesPerPair, int Workers, int TimeoutSeconds, int TurnLimit, long RootSeed, bool Diagnostic, string EngineHash, string AdapterHash,
    string Pilot = "Forge Default / forced starter v1", string OpeningPolicy = "London mulligan; Arena Bo1 opening hands approximated", string SeedScheme = "sha256-v1");
public sealed record SimulationRun(string Id, DateTimeOffset CreatedAt, string Status, RunConfig Config, int Total, string? Error = null);
public sealed record Attempt(int Number, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, string? Error, string? Replay, string? ReplayHash);
public sealed record TurnSample(int ActualTurn, int PlayerTurn, string Player, int Life, int Hand, int Lands, int Creatures, int Power, int Graveyard, int Library, int Artifacts, int Enchantments, int? HandLands = null, int? HandSpells = null);
public sealed record ManaFlag(string Kind,string Player,int FirstOwnTurn,int LastOwnTurn,int FirstActualTurn,int LastActualTurn,string Evidence,string DefinitionVersion);
public sealed record OpeningHand(string Player,int Mulligans,int KeptHandSize,bool? PolicyAccepted,string Evidence);
public sealed record GameResult(string? Winner, bool Draw, string TerminalReason, int ActualTurns, Dictionary<string, int> PersonalTurns, long DurationMs, List<TurnSample> Samples, List<ManaFlag>? ManaFlags = null, string? ManaDetectionVersion = null,List<OpeningHand>? OpeningHands=null,string? OpeningTrackingVersion=null);
public sealed record GameSlot(string Id, string RunId, string DeckA, string DeckB, string Starter, long Seed, int Index, string Status, List<Attempt> Attempts, GameResult? Result = null);
public sealed record MatchupSummary(string DeckA, string DeckB, int Scheduled, int Valid, int WinsA, int WinsB, int Draws, int Failed, int Cancelled, int Pending, int Running, int AttemptFailures,
    int PlayA, int PlayB, double? WinRateA, double? WinRateB, double? DrawRate, double[]? IntervalA, double[]? IntervalB, int WinsAOnPlay, int WinsAOnDraw, int WinsBOnPlay, int WinsBOnDraw);
public sealed record DeckMulliganSummary(string DeckId,int RecordedPlayers,int TotalMulligans,double? Mean,double? Median,int? Maximum,int Zero,int One,int Two,int ThreePlus,double? MeanKeptSize,double? MeanOnPlay,double? MeanOnDraw);
public sealed record RunSummary(SimulationRun Run, int Valid, int Failed, int Cancelled, int Running, int Pending, bool FullMatrix, List<MatchupSummary> Matchups, int ManaStarvedGames, int ManaFloodedGames, int ManaAnalyzedGames,List<DeckMulliganSummary> Mulligans);
public sealed record ReplayPlayer(string Id, int Life, int HandCount, int LibraryCount, int PersonalTurn, List<ReplayCard> Battlefield, List<ReplayCard> Graveyard, List<ReplayCard> Exile, List<ReplayCard> Hand);
public sealed record ReplayCard(int Id, string Name, string Type, bool Tapped, int? Power, int? Toughness, string Counters, string? AttachedTo, string Owner, string Controller, string Cost, string Text);
public sealed record ReplayState(string? ActivePlayer, string? Phase, int EngineTurn, List<ReplayPlayer> Players, List<string> Stack);
public sealed record ReplayRecord(string Kind, long Sequence, int ActualTurn, string? Event, string? Detail, ReplayState? State, string? StateHash,
    string? AttemptId, string? Winner, bool? Draw, string? Reason, int? Starter, bool? ExtraTurn,string? Player=null,int? Mulligans=null,int? KeptHandSize=null,bool? PolicyAccepted=null);
public sealed record ChartPoint(int Turn,double Value,int Count);
public sealed record ChartSeries(string Name,string Color,List<ChartPoint> Points);
