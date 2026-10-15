# Implementation status

Updated October 14, 2026 (America/Los_Angeles). This describes the current source and recorded local evidence. The [specifications](specs/README.md) also contain validation targets that have not passed; their presence is not a release certification.

## Runtime and operation

Crucible uses .NET 10, ASP.NET Core, Blazor Server, SQLite, and Forge 2.0.15 through a Java bridge. `Crucible.Core` shares backend implementation; `Crucible.Service` hosts the service independently of the `DeckTester` interface. The macOS Monitor packages a Native AOT backend with generated JSON serialization and a .NET frontend. Shell scripts run JIT development hosts. Legacy combined/service modes remain available in `DeckTester` for compatibility.

The service owns simulations, imports, run storage, engine workers, and replays on loopback port 5079. The UI uses service APIs on port 5077; editorial notes are a separate local SQLite exception, and pilot scorecards are read from local report artifacts. A UI restart preserves active simulations. [Crucible Monitor](crucible-monitor.md) manages startup, health, ownership, approved recovery, component builds, and rollback. It never automatically resumes interrupted sessions.

Immutable Forge bundles pin engine resources and compiled pilots per session. Worker-capable bundles reuse initialized JVMs within one session with fresh game/pilot state, seed, and replay per game. Faults destroy workers; normal recycling and session shutdown release them. Historical bundles retain their original execution protocol. See [host separation](host-separation.md) and [engine/replay contract](specs/03-engine-and-replay.md).

## Implemented workspace

| Area | Current behavior |
| --- | --- |
| Deck library | Exact public, captured personal, and custom versions; composition/source identity, coverage, friendly variants, collapsed lists, lineage, and linked past sessions |
| Run setup | Two to ten exact versions, balanced starts, even games per matchup, two to four parallel games, fixed seeds, preflight, and optional Forge Performance Mode |
| Lifecycle | Persistent slots and attempts, pause/resume, bounded failure retry, restart recovery, deletion, idempotent creation, and compatible completed-game reuse |
| Results | W/L/D, play/draw splits, Wilson intervals, equal-opponent averages, ten-deck Field/By deck explorer, and turn-end charts with contributing observations |
| Evidence | Separate Hand, Mana Incident, Runtime, and Game Evidence logs; filters, sorting, pagination, and replay links |
| Replay | Compressed engine events/checkpoints, hashes, turn navigation, triggers, public pilot explanations, and an explicit private-hand audit view |
| Workshop | Catalog search, Arena text resolution, browser drafts, grouped quantity editing, immutable saves, pilot previews, matchup reruns, and scoped baseline comparisons |
| Forge Pilots | Assignment preview, exact and historical profiles, frozen fixture scorecards/source, per-session measured rates, and protocol inspection |
| Editorial notes | Attributed, append-only saved commentary, seeded from an authored catalog; no automatic per-game model calls |
| Optimizer | CLI/API experiments with frozen inputs, bounded search/revisions, distinct holdout evidence, profiling, and explicit immutable opt-in promotion |

The source catalog and saved library can grow independently of the run limit. Ten selected decks produce 45 pairings and 9,000 games at the 200-game default. Older five/six/twelve-deck planning examples do not set the current selection cap.

Focused snapshots and conditional reads keep setup, selected sessions, library, and history separate. Server-side prepared reads avoid rebuilding unchanged responses; resource capacity is sampled separately. The UI retains its last snapshot during service outages and reconnects. Browser controls, disclosure state, and selections persist locally. Theme tokens, shared deck labels, and shared comboboxes support desktop and narrow layouts.

## Current pilots

Standard uses Aggro v001, Control v1, unified Reanimator v011, and Forge Default fallback. Exact Reanimator subtype takes precedence. The opt-in subtype mode adds 30 bounded permanent-development profiles and keeps Reanimator v011. **Standard + Engagement Protocols (Preview)** instead uses Reanimator v012 with Standard Execution, Counterplay, Graveyard Security, Stabilize, and Race. Other Standard assignments stay the same.

Protocols share typed definitions, permitted observations, candidate features, and one deterministic evaluator. Supported casting, tutor, discard, recovery, and copy hooks retain Forge legality/payment checks. Counterplay waits at most one own turn per critical card instance; Race and urgent pressure disable discretionary waiting. Combat, mulligans, general protection sequencing, and unsupported actions retain inherited behavior. Saved sessions freeze profiles, annotations, protocol catalogs, and bundle identity; new modes do not rewrite historical evidence.

See [Engagement Protocols](engagement-protocols.md), [subtype pilots](subtype-pilots.md), [Aggro](aggro-pilot.md), [Control](control-pilot.md), and [pilot requirements/history](specs/04-automated-pilots.md) for exact scope and recorded cohorts.

## Verification and recorded evidence

The repository now includes .NET executable check projects, Java real-engine positions, Python checks and matched-game runners, and Swift Monitor checks. The [development guide](development.md) lists entry points and prerequisites. The initial October 11 startup checks and decision to waive tests describe that initial implementation only; they no longer describe the repository's verification coverage.

- Engagement Protocols: 64 focused checks, 60 copy/payment positions, and a frozen 48-schedule comparison (96 serial executions). Baseline/candidate wins were 29/32, with four gains, one regression, and 43 unchanged outcomes. The final refinement preserved 32,043 candidate checkpoints. The workspace and replay inspection were checked at desktop and 390px. Graveyard Security has fixture evidence but was not activated by the complete-game cohort.
- Subtype pilots: 60 paired schedules across 30 representatives (120 executions), 29/28 baseline/candidate wins, one gain and two regressions. This limited opponent set does not establish general improvement.
- Native backend: the recorded serial JIT/AOT comparison measured 199.6/127.9 MiB resident memory after API reads and 1.34/1.08 seconds to readiness. Two matched complete games per backend retained identical normalized outcomes/replays, with 953 records and 657 checkpoints. These measurements are local, finite observations; see [native backend](native-backend.md) for conditions and artifacts.
- Host, Workshop, scheduling, cache identity, generated serialization, notes, worker lifecycle, and Monitor checks cover their focused contracts. These checks do not certify every card, pilot, or complete rules interaction.

Reports, exact requests, replay evidence, and benchmark artifacts live in ignored `data/` directories; they are not included with a clone. Feature guides distinguish their historical release cohorts from the currently selected bundle. Updating this documentation does not rerun those experiments.

## Remaining validation limits

Only **Exploratory Mode** is enabled. Full rules/generated-object coverage, deck-specific strategic fixtures, the inherited Forge hidden-information audit, and deterministic trace acceptance remain incomplete. Script presence does not establish correctness. Custom reviewed hooks use own information and public observations; this does not validate all inherited Forge decisions.

The replay is a viewer of captured checkpoints and engine diagnostics. It lacks a complete normalized legal-decision/payment trace, private library order, full zone-change lineage, mana-pool visualization, and exact skipped-turn cycle grouping. Actual turns remain authoritative. Technical stops and incomplete games remain separate from rules draws and win-rate denominators.

Arena hand-smoothing parity, independently refreshed legality, calibrated pilot skill, causal loss attribution, and Arena matchup forecasts are not claimed. Forge Performance Mode remains a separate experimental setting. Large schedules and completed samples do not remove engine or pilot bias.
