# 05 — Run orchestration and metrics

Date: October 11, 2026 (America/Los_Angeles)

Status: Proposed. Parent: [MVP specification index](README.md).

## Immutable run configuration

Store a run ID, creation time, exact deck versions, engine/adapter/pilot/rules identities, opening policy, coverage and validation reports, selected pairings, games per pairing, root seed, seed scheme version, limits, and worker configuration. Run labels can change; scientific inputs cannot. Changing an input creates another run.

Default schedule: all unordered distinct-deck pairings, 200 slots per pairing, 100 starts for each deck. No mirror matches in the default matrix. Five decks produce 10 pairings and 2,000 slots; six produce 15 pairings and 3,000. A single-matchup run uses the same contract. Require an even slot count when requesting balanced play/draw.

Derive each slot's seed from SHA-256 over a versioned canonical encoding of root seed, pairing's ordered deck-version IDs, requested starter, and index within that starter's allocation. Record the full digest and the exact conversion to the pinned engine's accepted seed range. Audit collisions and reject a schedule with duplicate effective seeds. Retries retain the same slot seed; a fresh sampled game is a new slot, not a retry. Separate pilot streams where supported and document when a global engine stream is used.

## Persistence model

| Entity | Key content |
|---|---|
| `deck_versions` | Immutable manifests and provenance |
| `validation_reports` | Rules, pilot, legality, and coverage evidence with version hashes |
| `runs` | Configuration and lifecycle state |
| `game_slots` | Pairing, starter, seed, schedule index, final disposition |
| `game_attempts` | Attempt number, process timing, status, failure evidence, replay reference |
| `game_results` | Exactly one accepted result per slot; W/L/D, terminal evidence, turns, hashes |
| `turn_samples` | Per-game state measurements with active player and checkpoint identity |
| `run_summaries` | Versioned derived summaries; reproducible from accepted results |

Use foreign keys and uniqueness for accepted result per slot. Attempts remain immutable history. A retry that succeeds does not erase prior failures. Do not count multiple attempts as multiple games.

## Lifecycle and recovery

Runs move through `queued`, `preflight`, `running`, `cancelling`, `cancelled`, `completed`, or `failed`. Slots independently record `pending`, `running`, `completed`, `failed`, or `cancelled`. Completion of a schedule does not mean all slots produced valid results: report the terminal disposition of every slot and distinguish successful completion from completion with failures.

Preflight rejects unsupported manifests or mismatched validation identities. Dispatch uses bounded concurrency. Cancellation stops new dispatch, requests graceful worker shutdown, then terminates after a recorded grace period. Completed results persist; interrupted attempts are administrative cancellations, not rules draws.

On restart, mark abandoned running attempts interrupted and their slots cancelled with reason `restart_interrupted`, unless reconciliation recovers an already committed accepted result. Reconcile replay files and SQLite references before changing slot state.

On explicit resume, requeue pending slots and transition every cancelled slot without an accepted result back to pending, including slots stopped during active work and slots marked `restart_interrupted`. Preserve the original seed, requested starter, immutable configuration, and all previous attempt records; create a new attempt when dispatching. Never requeue a completed slot. Genuine engine/protocol/timeout failures remain failed until an explicit bounded retry request; resume does not silently retry them. Deterministic failures must not trigger endless retries. SQLite acceptance transactions and result uniqueness prevent double-counting after crashes.

An accounted-for terminal schedule with failed or cancelled slots is an incomplete matrix, even when the run lifecycle is terminal. Full-matrix acceptance requires all 200 scheduled slots per pairing to have valid results, with 100 verified starts per deck. Explicit retries can complete those original slots; creating replacement seeds to conceal failures is prohibited. Prior attempt failures remain visible after a successful retry.

## Outcome accounting

Only a completed engine outcome with valid terminal evidence is eligible for W/L/D. Represent winner by deck/player identity or an engine rules draw. A wall-clock limit, administrative turn cap, worker crash, unresolved loop, illegal adapter request, or replay integrity failure is a separate technical/administrative outcome. In particular, Forge CLI timeout-as-draw behavior must be intercepted or avoided so it cannot enter rules-draw counts.

For every matchup show scheduled slots, completed valid games, pending/running slots, failed slots, cancelled slots, attempt failures, and rules draws. Never hide the invalid fraction or automatically sample replacements until a preferred result appears. Missing games can bias estimates; reports with failures remain visibly incomplete/qualified.

## Metric definitions

For a deck and a set of valid completed games, let `n = W + L + D`:

- Win rate: `W / n`; loss rate: `L / n`; rules-draw rate: `D / n`.
- Optional decisive-game win rate: `W / (W + L)`, clearly labeled and unavailable if there are no decisive games.
- Optional score: `(W + 0.5 D) / n`, labeled separately from win rate.
- Play/draw splits use actual verified starting player, not UI position.
- A matrix cell is the row deck's win rate against the column deck; reverse cells need not sum to 100% because draws exist. Diagonal is unused unless an explicit mirror run is requested.
- Default deck aggregate gives each selected opponent equal weight. Show both that macro average and pooled counts when requested; custom subsets must disclose weighting.

For each individual matchup win proportion, display an approximate 95% Wilson interval with `z = 1.96`, treating draws as nonwins. This estimates Monte Carlo variation under the fixed setup, not human Arena performance or engine/pilot bias. Do not attach that binomial interval to score, a macro average, a policy difference, or a mixture of configurations without a separately appropriate method. For `n = 0`, return null rates and no interval.

Turn-to-win/loss distributions condition on outcome. Report terminal actual-turn ordinal and the winning/losing player's own turns taken separately. Draws have their own distribution; administrative stops are not inserted as terminal game turns. Show histogram, median, quartiles, sample size, and incomplete-cycle labels. Extra-turn games retain actual turns rather than a guessed round conversion.

## Turn measurements

Capture life, hand count, land count, creature count, creature power sum, artifact/enchantment counts, graveyard size, and available mana at explicit checkpoints. Only current creatures contribute to creature power; impending noncreatures do not. Treat unknown values as unknown, not zero. Power sum is descriptive and does not claim combat strength or account for evasion as a scalar score.

Default charts use each player's own completed turn-end checkpoints; compare life and board measurements at matching own-turn indices. Show mean/median where useful, counts of observed games, and active-game denominators. Do not carry terminal states forward into later turns unless presenting a separately labeled cumulative series. Ended games create survivor selection, which must be visible.

## Loss evidence

Store an engine terminal reason and links to decisive events. Terminal tags may include life-total loss, empty-library draw loss, poison loss, card-defined win/loss, concession if an explicitly permitted policy uses one, or unknown. Distinguish direct damage evidence from a bare life-total outcome only when the trace supports it.

Optional contributing-condition tags are descriptive: missed land drops, available colors, cards stranded in hand, or board presence. Each tag requires a published definition, timing, evidence, and version. Do not label an observed missed land drop as the cause of a loss or claim a counterfactual without a separate validated analysis. Ambiguous causes remain unknown. No free-form model diagnosis runs automatically per game.

## Exports

The loopback simulation service can export compact CSV/JSON summaries with manifests' hashes, configuration identities, rates/denominators, interval definition, failure counts, and qualified status. Replays are separate explicit backend downloads. The frontend has no export or download controls, and the UI host does not proxy export endpoints. Ordinary exports exclude raw personal account logs and private trace data.

Record session processing periods separately from queued and paused time. A Runtime Log page presents each period's start, finish or last recorded update, active and scheduled duration, new attempt count, and status with filtering, sorting, and pagination. An unfinished period is labeled by its last recorded update. The page keeps raw timing JSON available and links back to the session details through its session label.
