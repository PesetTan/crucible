# 01 — Scope and architecture

Date: October 11, 2026 (America/Los_Angeles)

Status: Design contract with outstanding validation gates. Parent: [specification index](README.md). Current behavior and recorded evidence: [implementation status](../implementation-status.md).

## Intended outcome

The user selects exact deck versions, runs complete local head-to-head Standard Bo1 games, compares matchup and turn-level results, and inspects any game's replay. The original validated-matrix target has five public decks and 200 games per distinct pairing (2,000 total). The separately identified personal Reanimator expands it to six decks and 3,000 games when its coverage and pilot gates pass. The current exploratory workspace supports two to ten selected versions from the larger saved library; ten decks yield 45 pairings and 9,000 slots at the default 200 per pairing.

## Included and excluded work

Include immutable deck imports; coverage and validation status; automated pilots; controlled play/draw allocation; local job execution; cancellation and restart recovery; W/L/D statistics; turn distributions; life and board charts; searchable game outcomes; full replay; and summary exports.

Exclude deck optimization, collection-aware crafting, automated database refreshes, public hosting, Arena account automation, multiplayer formats, sideboarding between games, machine-learning training, and live language-model decisions. Preserve sideboards as provenance, but activate no between-game sideboarding. Any card that accesses outside-the-game cards requires a separately validated Arena Bo1 rule and accessible-card policy before that deck can run.

## Component choices

| Component | Implementation | Responsibility |
|---|---|---|
| Desktop/browser client | Svelte + TypeScript, native .NET UI host, Tauri desktop shell; Blazor retained for rollback | Deck selection, runs, charts, replay |
| Local service | C# ASP.NET Core on .NET 10 | Import, preflight, scheduling, persistence, aggregation, local HTTP API |
| Engine adapter | Java around a pinned Forge build | Full rules execution, pilot integration, structured events |
| App storage | SQLite plus compressed replay files | Immutable manifests, run history, summaries, detailed evidence |

The implemented app runs its simulation service (`Crucible.Service`) and Svelte frontend (`Crucible.Ui`, served by `Crucible.UiHost`) as separate processes, with shared backend contracts in `Crucible.Core`. Native desktop builds use a Tauri WebView shell; Blazor `DeckTester` remains a rollback interface. The macOS Monitor packages Native AOT hosts; shell scripts also support JIT development hosts. The service owns SQLite, Java engines, replays, and scheduling on loopback port 5079; the UI host serves assets and a same-origin API proxy on port 5077 and may expose the UI on the configured private network. Session log pages fetch structured API records through this boundary. Restarting only the UI preserves an active simulation. See [host separation](../host-separation.md) for launch commands and recovery details.

The user selected C# when approving implementation on October 11, 2026; this supersedes the earlier Python/React proposal. The existing Forge engine remains Java and uses a minimal interop shim. Record compatible runtime and package versions in lockfiles. See [implementation status](../implementation-status.md) for actual capability and validation limits; specifications are not proof that their gates have passed.

Keep app data under `projects/crucible/data/`, engine artifacts under `projects/crucible/vendor/`, and source code under the new project. The source catalog is at `../../standard_cards.sqlite` relative to the Crucible repository and is opened read-only for imports. Explicit [card-attribute](../card-attributes.md) operations can append local pilot metadata; they are separate from import and simulation. Exclude generated engine bundles, replay data, and personal manifests from source control by default; commit documentation, source, fixtures, and dependency manifests intentionally.

## Capability investigation gate

Before committing to Forge integration, produce a local capability report containing:

1. Exact engine revision, binary checksum, runtime, license notices, and any adapter patches.
2. Coverage for all selected main-deck cards and generated objects.
3. A headless completed game with verified starting player and no sideboarding.
4. Seed control and deterministic repeatability demonstrated across fresh processes.
5. Structured priority/decision events and state snapshots sufficient to reconstruct a game.
6. A supported way to prevent pilots from inspecting hidden opponent information.
7. Measured runtime, startup overhead, memory use, and safe worker count on this machine.

If a requirement is unavailable, investigate a bounded adapter change. Do not claim it is supported because the engine has a CLI or exposes one event class. If necessary changes are substantial or full required coverage is unavailable, stop the engine commitment and report options, including XMage investigation. Do not replace the rules engine with a heuristic simulator.

## Runtime configuration

Persist engine and adapter identity, worker count, per-game wall-clock limit, maximum administrative turn limit, seed scheme, mulligan policy, pilot profiles, selected deck versions, and validation report IDs with each run. A changed configuration creates a new run. Benchmark first; worker defaults must respect measured memory use and leave the machine responsive.

Bind the service to loopback. Permit only the app's local origin. A local instance token protects mutation endpoints; worker launches use argument arrays and server-generated paths, not shell interpolation. Game workers must not call remote services. Initial approved dependency installation or source research is separate from game execution.

## Milestones

| Stage | Deliverable | Exit condition |
|---|---|---|
| A | Capability report and immutable manifests | Engine contract is feasible; required coverage is known |
| B | Rules and pilot fixtures | Required fixtures pass for eligible decks |
| C | Small audited batch | Replays reconstruct; traces reviewed; failures accounted for |
| D | Working local interface and full matrix | Full acceptance criteria pass; qualified summaries export |

Runtime estimates are based on completed measurements and include startup and observed timeout costs. The planning estimate of 13 minutes to two hours for 3,000 games is conditional arithmetic, not a performance promise.
