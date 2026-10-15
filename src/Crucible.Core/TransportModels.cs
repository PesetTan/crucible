using System.Text.Json;

namespace DeckTester;

// Property order in fingerprint/seed records is part of the existing identity protocol.
public sealed record DeckFingerprint(string Name,string Type,string SourceId,string Snapshot,string? Url,List<CardEntry> Cards);
public sealed record WorkshopFingerprint(string Name,string SourceType,string? ParentDeckId,List<CardEntry> Cards);
public sealed record SeedFingerprint(string Scheme,long RootSeed,string A,string B,int Starter,int Index);
public sealed record WorkshopRunFingerprint(RunConfig Config,WorkshopBaseline? WorkshopBaseline,string? WorkshopDeckId);
public sealed record ForgeDeckCard(string Name,int Quantity,JsonElement? Attributes);
public sealed record ForgeGameRequest(string AttemptId,long Seed,int Starter,int TurnLimit,bool PerformanceMode,
    OptimizerPolicy? OptimizerPolicyA,OptimizerPolicy? OptimizerPolicyB,string PilotVersion,string PilotA,string PilotB,
    List<ForgeDeckCard> DeckA,List<ForgeDeckCard> DeckB);
public sealed record OptimizerCatalogEntry(DeckManifest Manifest,string Profile,string Coverage);
public sealed record OptimizerInspect(List<OptimizerCatalogEntry> Catalog);
public sealed record OptimizerId(string Id);
public sealed record OptimizerCandidate(string Id,string CandidateId);
public sealed record OptimizerEvidence(string Id,int Limit);
public sealed record OptimizerDetail(string Id,string Replay,int Sequence);
public sealed record OptimizerSource(string Id,string Term,string? CandidateId);
public sealed record EmptyRequest;
public sealed record ApiError(int Code,string Message);
public sealed record InstanceSession(string InstanceToken);
public sealed record CreatedRun(string Id);
public sealed record DeletedRun(bool ReplayCleanupComplete);
public sealed record PreflightResponse(List<string> Blockers,List<string> Limitations,int CachedGames,string? ExistingSessionId,int? ExistingGamesPerPair);
