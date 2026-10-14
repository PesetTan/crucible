# MVP implementation specifications

Date: October 11, 2026 (America/Los_Angeles)

Status: Implementation authorized in C# on October 11, 2026. The user waived automated tests. These specifications remain the target for the validated MVP; see [implementation status](../implementation-status.md) for implemented behavior and outstanding evidence gates.

These specifications implement the scope of the [dated simulator plan](../plans/2026-10-11-mtg-simulator-plan.md). They are requirements and proposed design decisions, not claims that the engine capabilities or validation gates have passed.

| File | Responsibility |
|---|---|
| [01 — Scope and architecture](01-scope-and-architecture.md) | Boundaries, components, milestones, configuration, and capability gates |
| [02 — Deck data and coverage](02-deck-data-and-coverage.md) | Read-only imports, immutable manifests, legality evidence, and support status |
| [03 — Engine and replay](03-engine-and-replay.md) | Java adapter contract, game execution, state capture, and replay reconstruction |
| [04 — Automated pilots](04-automated-pilots.md) | Information boundaries, policy requirements, fixtures, and quality gates |
| [05 — Runs and metrics](05-runs-and-metrics.md) | Scheduling, recovery, denominators, statistics, and loss evidence |
| [06 — Interface and local API](06-interface-and-local-api.md) | User flows, endpoints, displays, and exports |
| [07 — Validation and acceptance](07-validation-and-acceptance.md) | Required fixtures, trace audits, release criteria, and review procedure |
| [08 — Mana incident logging](08-mana-incident-logging.md) | Per-player mana-starved/mana-flooded detection, evidence, and filters |
| [09 — Mulligan tracking](09-mulligan-tracking.md) | Each synthetic player's mulligans before keep and kept hand size |

Implementation order: capability investigation → data manifests → rules and pilot fixtures → audited small batch → interface and full batch. The interface may be developed with clearly labeled fixtures, but fixture data must never appear as simulated results.

All files share the following invariants:

- Full, opposed, rules-resolved games; no goldfish or combat approximation substituted for game outcomes.
- Zero language-model calls during repeated simulations or automatic aggregation.
- Public decks and the personal saved deck remain separately identified.
- Unknown support is blocking, not a fallback to generic card behavior.
- Wins, rules draws, technical failures, and administrative stops have separate statuses.
- Results are conditional on engine version, pilot versions, exact decks, rules snapshot, and opening-hand policy.
- No claimed Arena hand-smoothing parity until verified; ordinary London mulligans are explicitly labeled as an approximation.

When documents conflict, resolve the conflict before implementation. The plan defines the intended scope; these specifications supply implementation details. Changes to scope or a failed engine-selection gate require an explicit revised decision, not a silent relaxation of requirements.

## Documentation review record

October 11, 2026: the plan's independent read-only review found no actionable issues. A separate independent review of the initial specifications 01–07 and their index identified two issues: cancelled slots lacked an explicit resume transition, and full-matrix acceptance lacked a minimum valid sample per pairing and starting player. Both were resolved in specifications 05 and 07. The same specification reviewer rechecked the revisions and reported no remaining actionable findings. Dates and local document links were also checked successfully. Specifications 08 and 09 were added during implementation in response to the user's mana and mulligan requests and were not part of that earlier independent review.

This review validates the documentation's consistency; it does not establish engine capability, card correctness, pilot quality, measured runtime, or approval to implement the application.

## Deck Workshop extension

October 12, 2026: Deck Workshop is specified in 02, 05, 06, and 07. It adds custom immutable deck versions, browser drafts, one-matchup reruns, and scoped comparisons. The user authorized implementation of this extension. Specification contracts were checked against the current service/UI separation, exact-version cache keys, and existing seed scheme before feature implementation.
