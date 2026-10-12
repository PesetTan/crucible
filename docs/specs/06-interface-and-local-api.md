# 06 — Interface and local API

Date: October 11, 2026 (America/Los_Angeles)

Status: Proposed. Parent: [MVP specification index](README.md).

## User flows

1. **Choose decks:** show public/personal origin, exact variant, count, capture date, eligibility, and a readable list. Public 4-Color Reanimator and personal Reanimator are separate cards. Explain each blocked deck with the affected card, mechanic, or pilot fixture.
2. **Configure a run:** choose all pairings or one matchup; default to 200 games per pairing and balanced play/draw. Show total games, opening-policy limitation, tested pilot identity, and measured runtime estimate when available.
3. **Run locally:** show valid completed games, total scheduled games, failures, active workers, elapsed time, and remaining estimate. Provide cancel and later resume. Do not display assumed win rates while no valid games have finished.
4. **Compare results:** matchup matrix, W/L/D totals, play/draw splits, outcome-conditioned turn distributions, life/board charts, active-game denominators, and validation limitations. Unvalidated diagnostic runs receive a persistent diagnostic label and no validated-results export.
5. **Inspect a game:** filter by matchup, starter, outcome, duration, or failure. Open a replay and navigate actual turns, turn cycles, phases, or events. Show incomplete cycles, extra/skipped turns, and last valid checkpoint for failures.
6. **Export:** summary CSV/JSON by default; explicit separate replay export.

Use plain labels and tooltips. Do not expose process arguments or storage schemas in the ordinary flow. Display the opening policy as “London mulligan; Arena Bo1 opening hands approximated” unless a verified Arena profile is implemented later. Rates are “simulated pilot win rate,” never a prediction of the user's Arena win rate.

## Replay presentation

Show both players, life, board, hand counts, graveyards, exile, relevant counters/attachments, active player, phase, and stack when applicable. Allow a public-information view and explicit omniscient audit view. Plain card names and text are sufficient; artwork is not required for MVP acceptance.

Provide previous/next event, previous/next turn, jump to cycle, and autoplay controls. A cycle summarizes its included actual turns; unsupported cycle grouping must fall back visibly to actual turns. Selecting a decisive loss event highlights the relevant objects where evidence permits. Failed games show failure status rather than a fabricated winner.

## App-owned HTTP contract

Use `/api/v1`, JSON, stable IDs, and ISO 8601 timestamps with offsets. The following endpoints are proposed app APIs, not existing engine interfaces:

| Method and route | Contract |
|---|---|
| `GET /capabilities` | Engine identity, verified capabilities, runtime requirements, gate status |
| `GET /decks` | Immutable deck versions, provenance, counts, eligibility |
| `GET /decks/{id}` | Full manifest and applicable validation reports |
| `POST /imports` | Import selected known source decks read-only; create new versions as needed |
| `POST /runs/preflight` | Validate an immutable requested configuration and return blockers/warnings |
| `POST /runs` | Create and queue a validated configuration; require idempotency key |
| `GET /runs` | Paginated run history and status |
| `GET /runs/{id}` | Configuration, progress, gate status, and failure counts |
| `POST /runs/{id}/cancel` | Idempotent cancellation request |
| `POST /runs/{id}/resume` | Resume eligible unfinished slots without changing inputs |
| `POST /runs/{id}/retry` | Explicit bounded retry of selected failed slots with original seeds |
| `GET /runs/{id}/summary` | Qualified statistics under the metric definitions |
| `GET /runs/{id}/games` | Paginated/filterable slots and accepted results |
| `GET /runs/{run}/games/{id}/replay` | Checkpoint/event metadata and bounded event range |
| `GET /runs/{id}/export` | Summary CSV or JSON |
| `GET /runs/{run}/games/{id}/replay-export` | Explicit replay download |

Use a consistent error envelope with code, readable message, and precise blocking items. Invalid inputs return 422; blocked capability/state conflicts return 409; missing IDs return 404. No response invents unknown metrics. Return null plus an explanation for unavailable measurements. Pagination and replay-range limits keep large logs out of ordinary responses.

Preflight and run creation both verify capability/version hashes so a stale preflight cannot authorize a changed setup. An idempotency key reused with a different payload returns a conflict. Paths are resolved server-side from known IDs; the browser cannot select arbitrary filesystem output paths. All mutation endpoints require the local instance token.

Progress polling may use short compact responses while running. Stop polling terminal runs. Detailed replay data is loaded only on demand and never implicitly sent to chat or an external service.

## UI acceptance

- A blocked deck explains exactly why it cannot run.
- The 5-deck and 6-deck defaults show 2,000 and 3,000 scheduled games respectively.
- Empty results show no invented percentage, median, or interval.
- Draws and technical failures are visually separate.
- Changing selected opponents exposes the aggregate weighting change.
- Turn charts show observed-game counts and the checkpoint definition.
- Replay controls work with keyboard navigation and do not rely on color alone.
- A user can inspect the full list, pilot identity, and opening-policy limitation from a result.
