using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DeckTester;

public sealed record WorkshopBaseline(string RunId,string DeckId,string OpponentId);

public sealed record RunMatchupNote(string RunId,string DeckA,string DeckB,string Title,string Context,List<RunNoteSection> Sections);
public sealed record RunNoteSection(string Heading,List<string> Paragraphs);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        WriteIndented=false,
        TypeInfoResolver=CreateResolver()
    };
    // The UI and fixture tools may serialize their own anonymous objects. Native
    // publishing substitutes IsReflectionEnabledByDefault=false and removes this
    // fallback; every service payload must exist in CrucibleJsonContext.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming","IL2026",Justification="Reflection fallback is guarded by JsonSerializer.IsReflectionEnabledByDefault and removed in Native AOT.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Aot","IL3050",Justification="Reflection fallback is guarded by JsonSerializer.IsReflectionEnabledByDefault and removed in Native AOT.")]
    private static IJsonTypeInfoResolver CreateResolver()=>JsonSerializer.IsReflectionEnabledByDefault
        ? JsonTypeInfoResolver.Combine(CrucibleJsonContext.Default,new DefaultJsonTypeInfoResolver())
        : CrucibleJsonContext.Default;
    private static readonly JsonSerializerOptions relaxed=new(Options){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public static JsonTypeInfo<T> Info<T>(JsonSerializerOptions? options=null)=>(JsonTypeInfo<T>)(options??Options).GetTypeInfo(typeof(T));
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value,Info<T>());
    public static string EncodeRelaxed<T>(T value)=>JsonSerializer.Serialize(value,Info<T>(relaxed));
    public static JsonElement Element<T>(T value)=>JsonSerializer.SerializeToElement(value,Info<T>());
    public static T Decode<T>(string value) => JsonSerializer.Deserialize(value,Info<T>()) ?? throw new InvalidDataException("Empty JSON document.");
}

public sealed record CardEntry(int ArenaId, string Name, int Quantity, string Zone, string Set, string Type, string Cost, string Text, bool LocallyLegal, string LinkedFaces)
{
    public JsonElement? Attributes { get; init; }
}
public sealed record DeckManifest(string Id, string Name, string SourceType, string Variant, string SourceDeckId, string Snapshot, string? SourceUrl,
    string ListHash, string ManifestHash, DateTimeOffset ImportedAt, List<CardEntry> Cards, string FreshnessNote)
{
    public string? FriendlyName { get; init; }
    public string? ParentDeckId { get; init; }
    public string? ArchetypeTier { get; init; }
    public string? Strategy { get; init; }
    public string? Subtype { get; init; }
    public string? TierSourceUrl { get; init; }
    public string? TierSnapshotDate { get; init; }
    public double? PublishedWinRatePercent { get; init; }
    public int? PublishedMatchesApprox { get; init; }
    public string? PublishedMatchesDisplayed { get; init; }
    public int MainCount => Cards.Where(c => c.Zone == "MainDeck").Sum(c => c.Quantity);
    public int CreatureCount => Cards.Where(c => c.Zone == "MainDeck" && c.Type.Contains("Creature", StringComparison.Ordinal)).Sum(c => c.Quantity);
    public int LandCount => Cards.Where(c => c.Zone == "MainDeck" && c.Type.Contains("Land", StringComparison.Ordinal)).Sum(c => c.Quantity);
    public int SideCount => Cards.Where(c => c.Zone == "Sideboard").Sum(c => c.Quantity);
}
public sealed record CoverageItem(string Name, string Status, string? Script, string? Reason);
public sealed record DeckCoverage(string DeckId, string Status, List<CoverageItem> Cards, List<string> Limitations);
public sealed record Capabilities(bool Available, string Engine, string EngineHash, string AdapterHash, string JavaPath, List<string> Blockers, List<string> Limitations)
{
    public string? AttributesHash { get; init; }
    public ProtocolCatalog? Protocols { get; init; }
}
public sealed record EngagementProtocol(string Id,string Name,string Condition,string Response,int Priority,int MaximumDelayTurns,Dictionary<string,int> Preferences);
public sealed record ProtocolCatalog(string Version,string Profile,string Hash,JsonElement Settings,List<EngagementProtocol> Definitions);
public sealed record RunRequest(List<string> DeckIds, int GamesPerPair = 200, int Workers = 2, int TimeoutSeconds = 180, int TurnLimit = 150, long RootSeed = 42, bool Diagnostic = true,
    bool StaggerStarts = true, int EstimatedGameSeconds = 15, int StartLeadSeconds = 5, double? StartupSpacingSeconds = 1, bool PerformanceMode = false, string PilotMode = ReanimatorPilots.CurrentAggroMode)
{
    public string? OptimizerPolicyId { get; init; }
    public WorkshopBaseline? WorkshopBaseline { get; init; }
    public string? WorkshopDeckId { get; init; }
}
public sealed record RunConfig(List<string> DeckIds, int GamesPerPair, int Workers, int TimeoutSeconds, int TurnLimit, long RootSeed, bool Diagnostic, string EngineHash, string AdapterHash,
    string Pilot = ReanimatorPilots.Adaptive, string OpeningPolicy = "London mulligan; Arena Bo1 opening hands approximated", string SeedScheme = "sha256-v1",
    bool StaggerStarts = false, int EstimatedGameSeconds = 15, int StartLeadSeconds = 5, double? StartupSpacingSeconds = null, bool PerformanceMode = false,
    Dictionary<string,string>? PilotProfiles = null)
{
    public ProtocolCatalog? ProtocolSet { get; init; }
    public OptimizerPolicy? OptimizerPolicy { get; init; }
    public Dictionary<string,JsonElement>? CardAttributes { get; init; }
    public string? AttributesHash { get; init; }
}
public sealed record OptimizerPolicy(int Version,string Id,string Profile,Dictionary<string,int> Tuning,string TuningHash,string EngineHash,string AdapterHash);
public sealed record SimulationRun(string Id, DateTimeOffset CreatedAt, string Status, RunConfig Config, int Total, string? Error = null, List<RunExtension>? Extensions = null, DateTimeOffset? UpdatedAt = null)
{
    public WorkshopBaseline? WorkshopBaseline { get; init; }
    public string? WorkshopDeckId { get; init; }
}
public sealed record SessionTiming(string Id,string RunId,DateTimeOffset StartedAt,DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt,double ActiveSeconds,int AttemptCount,double ScheduledSeconds,string Status);
public sealed record ActiveSessionProgress(SimulationRun Run,int Completed,int Failed,int Cancelled,int Running,int Pending,int Reused)
{
    public int Processed=>Completed+Failed+Cancelled;
    public double Percent=>Run.Total==0?0:Math.Clamp(100.0*Processed/Run.Total,0,100);
}
public sealed record SessionTurnStats(int Sessions,int Games,int LongestGame)
{
    public int SuggestedLimit=>((Math.Clamp(LongestGame,10,1000)+9)/10)*10;
}
public sealed record Attempt(int Number, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, string? Error, string? Replay, string? ReplayHash);
public sealed record TurnSample(int ActualTurn, int PlayerTurn, string Player, int Life, int Hand, int Lands, int Creatures, int Power, int Graveyard, int Library, int Artifacts, int Enchantments, int? HandLands = null, int? HandSpells = null);
public sealed record ManaFlag(string Kind,string Player,int FirstOwnTurn,int LastOwnTurn,int FirstActualTurn,int LastActualTurn,string Evidence,string DefinitionVersion);
public sealed record OpeningHand(string Player,int Mulligans,int KeptHandSize,bool? PolicyAccepted,string Evidence);
public sealed record GameResult(string? Winner, bool Draw, string TerminalReason, int ActualTurns, Dictionary<string, int> PersonalTurns, long DurationMs, List<TurnSample> Samples, List<ManaFlag>? ManaFlags = null, string? ManaDetectionVersion = null,List<OpeningHand>? OpeningHands=null,string? OpeningTrackingVersion=null);
public sealed record GameSlot(string Id, string RunId, string DeckA, string DeckB, string Starter, long Seed, int Index, string Status, List<Attempt> Attempts, GameResult? Result = null, string? CachedFromRunId = null, string? CachedFromGameId = null);
public sealed record MatchupSummary(string DeckA, string DeckB, int Scheduled, int Valid, int WinsA, int WinsB, int Draws, int Failed, int Cancelled, int Pending, int Running, int AttemptFailures,
    int PlayA, int PlayB, double? WinRateA, double? WinRateB, double? DrawRate, double[]? IntervalA, double[]? IntervalB, int WinsAOnPlay, int WinsAOnDraw, int WinsBOnPlay, int WinsBOnDraw);
public sealed record DeckMulliganSummary(string DeckId,int RecordedPlayers,int TotalMulligans,double? Mean,double? Median,int? Maximum,int Zero,int One,int Two,int ThreePlus,double? MeanKeptSize,double? MeanOnPlay,double? MeanOnDraw);
public sealed record RunSummary(SimulationRun Run, int Valid, int Failed, int Cancelled, int Running, int Pending, bool FullMatrix, List<MatchupSummary> Matchups, int ManaStarvedGames, int ManaFloodedGames, int ManaAnalyzedGames,List<DeckMulliganSummary> Mulligans, int CachedGames = 0, List<string>? CachedSourceRunIds = null);
public sealed record ReplayPlayer(string Id, int Life, int HandCount, int LibraryCount, int PersonalTurn, List<ReplayCard> Battlefield, List<ReplayCard> Graveyard, List<ReplayCard> Exile, List<ReplayCard> Hand);
public sealed record ReplayCard(int Id, string Name, string Type, bool Tapped, int? Power, int? Toughness, string Counters, string? AttachedTo, string Owner, string Controller, string Cost, string Text);
public sealed record ReplayState(string? ActivePlayer, string? Phase, int EngineTurn, List<ReplayPlayer> Players, List<string> Stack);
public sealed record ReplayTurnPosition(int Turn,int Offset);
public sealed record ReplayRecord(string Kind, long Sequence, int ActualTurn, string? Event, string? Detail, ReplayState? State, string? StateHash,
    string? AttemptId, string? Winner, bool? Draw, string? Reason, int? Starter, bool? ExtraTurn,string? Player=null,int? Mulligans=null,int? KeptHandSize=null,bool? PolicyAccepted=null,
    string? TriggerCategory=null,string? TriggerText=null,string? TriggerSource=null,string? TriggerCaptureVersion=null,
    string? PilotProfile=null,string? PilotVersion=null,string? Decision=null,List<string>? Options=null,string? Selected=null,
    string? OpponentClass=null,int? OpponentConfidence=null,List<string>? VisibleEvidence=null,
    List<string>? ActiveProtocols=null,string? ProtocolSetVersion=null,bool? ProtocolDelay=null);
public sealed record TriggerEventSummary(int ActualTurn,string Category);
public sealed record TriggerGameSummary(string GameId,int ActualTurns,List<TriggerEventSummary> Events);
public sealed record ChartPoint(int Turn,double Value,int Count);
public sealed record ChartSeries(string Name,string Color,List<ChartPoint> Points)
{
    public DeckManifest? Deck { get; init; }
}

public sealed record CachedGameKey(string DeckA, string DeckB, string Starter, long Seed, int Index);

public sealed record RunExtension(DateTimeOffset RequestedAt, int PreviousGamesPerPair, int GamesPerPair, int AddedGames, int ReusedGames);
