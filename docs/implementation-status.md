# Implementation status

Date: October 11, 2026 (America/Los_Angeles)

The user authorized implementation in C# and waived automated tests. The application uses .NET 10, ASP.NET Core, Blazor Server, and SQLite. Forge 2.0.15 remains the existing rules engine behind a minimal Java interoperability shim.

## Implemented

- Read-only imports of eleven exact public lists with fingerprint verification and the separately identified captured personal Reanimator.
- Immutable composition/source identities and local card-support inventory, including scripts packaged inside Forge's card archive.
- Real headless opposed games with forced and verified starting player, ordinary London mulligans, no cheat shuffle, no ante, and no between-game sideboarding.
- Seed schedules, parallel execution, per-attempt limits, persistent slots/attempts, cancellation, explicit resume/retry, restart recovery, and duplicate-run idempotency.
- Typed game outcomes; timeouts and administrative stops separated from rules draws.
- Compressed replay capture with per-state checksums and completed-file integrity checks; turn/event navigation and explicit private-hand audit view.
- W/L/D and play/draw metrics, matchup Wilson intervals, matrix, conditional turn distributions, equal-opponent aggregation, and turn-end life/board/hand/graveyard charts with observed-game counts.
- Local interface, API, qualified CSV/JSON export, project-local engine setup, and loopback-only service configuration.
- Per-player mana-starved/mana-flooded incident logging with versioned definitions, turn-by-turn count evidence, game filters, and replay jump links. Existing completed replays are analyzed locally without rerunning their games.
- Per-player mulligans before keep, kept hand sizes, recorded keep decisions, and per-deck frequency/mean/play-draw summaries. Compatible legacy replays retain a separate derivation label.

## Verification performed

The C# application and Java interoperability shim compiled. The C# build completed with zero warnings and errors. All six source decklists imported with their expected counts, and all required main-deck card names had exact Forge script entries. A two-game opposed integration run between the public 4-Color Reanimator and Mono-White Auras completed with one verified start for each deck, stored terminal results, turn samples, and compressed replays. These are startup/integration checks, not statistical matchup evidence.

A second two-game opposed run between Mono-White Auras and Mono-White Lifegain completed with authoritative per-player opening-hand records under the updated shim. The older replays were analyzed for mana incidents and legacy mulligan accounting. Interactive framework assets are served successfully by the local app.

No automated test suite or fixture project was created. The existing documentation reviews preceded implementation and do not certify its game fidelity.

## Outstanding validated-MVP gates

The rules, generated-object coverage, pilot strategy, hidden-information boundary, and deterministic trace audits in the specifications remain outstanding. Validated-result mode is blocked. Only explicitly labeled exploratory runs are enabled.

The current replay is a viewer of captured full-state checkpoints and engine event descriptions. Complete normalized legal-decision/payment tracing, private library order, zone-change lineage, mana-pool visualization, and exact skipped-turn cycle grouping are not implemented. The app exposes these limitations and does not infer the missing fields. Arena hand-smoothing parity, independently refreshed legality, and causal loss attribution are not claimed.

The default full 2,000/3,000-game matrices are available to schedule locally but have not been run or independently validated during implementation. Full sample sizes do not remove pilot or engine bias.

## Deck Workshop — October 12, 2026

Deck Workshop provides local catalog search and Arena-text resolution, browser-persisted drafts, immutable custom versions with parent provenance, matchup reruns, and scoped baseline comparisons. Library and session matchup links enter the workspace; workshop sessions link back to it. The service owns card validation and deck creation, while the UI communicates through IUiBackend.

Acceptance evidence: build with no warnings; WorkshopChecks cover parsing/printing ambiguity, copy limits/exceptions, sideboards, version immutability, source byte integrity, oriented statistics, missing diagnostics, legacy records, and cache keys. HostSeparationChecks verify service boundaries. Visible Safari verification used a two-game edited Mono-White Auras matchup against Sultai Reanimator and opened its saved replay. This small verification sample establishes the workflow, not a deck recommendation or a measured improvement.

Draft restoration was visibly rechecked after fixing initialization order: a renamed 60-card draft with 21 Plains and 3 Shardmage's Rescue survives refresh. Responsive verification used Safari at 375 pixels; controls and comparison cards fit the narrow layout. Reopening a workshop result retains that result's settings, while its original baseline remains fixed.
