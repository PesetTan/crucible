# 06 — Interface and local API

Date: October 11, 2026 (America/Los_Angeles)

Status: Proposed. Parent: [MVP specification index](README.md).

## User flows

1. **Choose decks:** show public/personal origin, exact variant, count, capture date, eligibility, and a readable list. Public 4-Color Reanimator and personal Reanimator are separate cards. Explain each blocked deck with the affected card, mechanic, or pilot fixture.
2. **Configure a run:** choose all pairings or one matchup; default to 200 games per pairing and balanced play/draw. Show total games, opening-policy limitation, tested pilot identity, and measured runtime estimate when available.
3. **Run locally:** show valid completed games, total scheduled games, failures, active workers, elapsed time, and remaining estimate. Provide cancel and later resume. Do not display assumed win rates while no valid games have finished.
4. **Compare results:** matchup matrix, W/L/D totals, play/draw splits, outcome-conditioned turn distributions, life/board charts, active-game denominators, and validation limitations. Unvalidated diagnostic runs receive a persistent diagnostic label.
5. **Inspect a game:** filter by matchup, starter, outcome, duration, or failure. Open a replay and navigate actual turns, turn cycles, phases, or events. Show incomplete cycles, extra/skipped turns, and last valid checkpoint for failures.
6. **Inspect session evidence:** open dedicated Hand Log, Mana Incident Log, Runtime Log, and Game Evidence pages from the session actions menu. Each page has a clear title, readable labels, filters relevant to its records, sortable columns, and 25-record pagination. Show loading, empty, and error states.

The session status pill sits on the same line as `SESSION / {short id}` above the results title. The frontend has no export, download, or raw API links. Summary and replay export endpoints remain available on the loopback simulation service for backend use; the UI host does not proxy those export endpoints.

In the matchup matrix, a small gold trophy beside a row deck name marks the highest mean opponent win rate, with equal weight for each shown opponent. Show the trophy only once every shown matchup has at least one valid completed game. Equal leaders each receive it. Recompute it when results or the selected matchup change; sorting rows does not change the leader.
Each row deck name also has a compact pill showing that deck's equal-weighted mean opponent win rate, using the same calculation as Overall Performance. Show a coverage fraction in the pill when only some shown opponents have valid results; omit the pill when none do. Update the pill as results or the selected matchup change.

Use plain labels and tooltips. Do not expose process arguments or storage schemas in the ordinary flow. Display the opening policy as “London mulligan; Arena Bo1 opening hands approximated” unless a verified Arena profile is implemented later. Rates are “simulated pilot win rate,” never a prediction of the user's Arena win rate.

Increase every app font size by one point from the prior typography scale, including headings, navigation, controls, tables, tooltips, and responsive variants. Keep layout and text wrapping usable at narrow widths.

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
| `GET /runs/{id}/opening-hands` | Recorded player opening hands, mulligans, kept sizes, decisions, evidence, and tracking versions |
| `GET /runs/{id}/mana-events` | Player-specific mana incidents, outcome, evidence, and definition versions; optional kind filter |
| `GET /runs/{id}/timings` | Recorded session processing periods, active/scheduled duration, attempt counts, and status |
| `GET /runs/{run}/games/{id}/replay-export` | Explicit replay download |

Use a consistent error envelope with code, readable message, and precise blocking items. Invalid inputs return 422; blocked capability/state conflicts return 409; missing IDs return 404. No response invents unknown metrics. Return null plus an explanation for unavailable measurements. Pagination and replay-range limits keep large logs out of ordinary responses.

Preflight and run creation both verify capability/version hashes so a stale preflight cannot authorize a changed setup. An idempotency key reused with a different payload returns a conflict. Paths are resolved server-side from known IDs; the browser cannot select arbitrary filesystem output paths. All mutation endpoints require the local instance token.

Progress polling may use short compact responses while running. Stop polling terminal runs. Detailed replay data is loaded only on demand and never implicitly sent to chat or an external service.

## Session log pages and navigation

The UI serves `/runs/{id}/logs/hands`, `/runs/{id}/logs/mana`, `/runs/{id}/logs/runtime`, and `/runs/{id}/logs/games`. Its backend client reads the corresponding service API responses or requested session snapshot; the UI host does not inspect SQLite, engine files, or replay files. The UI host also proxies raw session log and paginated game GET requests at its own origin. A UI restart can publish these pages without stopping an active simulation service.

On each log page, the `SESSION / {short id} / EVIDENCE` label links to the main session details page. Immediately below that label, a session-log navigation menu shows Hand Log, Mana Incident Log, Runtime Log, and Game Evidence with the current page selected, using the same selected-menu treatment as the main app navigation. At narrow widths the menu remains usable by horizontal scrolling. Refresh is a secondary control outside that menu. Game IDs in hand and mana rows link to the corresponding replay.

Hand records can be filtered by deck and keep decision, then sorted by game, deck, position, mulligans, kept size, or decision. Mana incidents can be filtered by deck, then sorted by game, deck, condition, first/last player turn, or outcome. Position and condition each have two values, so their column sorts group either value first without a duplicate dropdown. Runtime periods can be sorted by start, finish/last recorded, active duration, scheduled duration, attempts, or status; show a status filter only when more than two distinct statuses exist. Sorting applies before pagination; changing a filter or sort returns to page one. Evidence text and version values remain visible, and unknown values remain explicitly unknown.

Other selectors remain where sorting cannot replace their action: the Deck Library selector jumps to a deck; the session matchup selector applies across result sections; Rounds/Turns changes the measure shown; outcome and keep-decision filters have more than two categories; and the session game-list mana filter selects a specific incident kind while its Mana Flags column sorts by incident count. The Runtime status filter is omitted for zero, one, or two distinct statuses and reappears if a session records more.

The Game Evidence table formerly in section 4 of session results lives on the Game Evidence page. The session results page links to it and retains its matchup and rounds controls for the remaining results sections. Game Evidence has its own matchup, outcome, mana-incident, and rounds/turns controls; sortable matchup, starter, outcome, length, mulligan, mana-flag count, evidence, and replay columns; 25-game pagination; stored-source and replay links; and explicit unknown or not-analyzed values. Each log page persists its selections separately across navigation and refresh.

The Pause Session action shows its explanation in the existing popup on hover or keyboard focus. It has no browser-native title tooltip, so only one explanation appears.
In the session actions menu, Pause Session appears in the bottom action group directly above Delete Session while the session is running, queued, or pausing.
When a session fails, its red error banner contains a Retry Session button that starts the existing bounded retry using the original seeds. Disable the button while retrying, when the service is unavailable, or when no unfinished game remains eligible; explain the last case and the four-failure limit in the banner. Show action errors separately from the recorded session failure so neither message is hidden.

## UI acceptance

- A blocked deck explains exactly why it cannot run.
- The 5-deck and 6-deck defaults show 2,000 and 3,000 scheduled games respectively.
- Empty results show no invented percentage, median, or interval.
- Draws and technical failures are visually separate.
- Changing selected opponents exposes the aggregate weighting change.
- Turn charts show observed-game counts and the checkpoint definition.
- Replay controls work with keyboard navigation and do not rely on color alone.
- A user can inspect the full list, pilot identity, and opening-policy limitation from a result.
- Each session log page can return to session details through its session label; the active log is identifiable in the menu at desktop and narrow widths.
- Log filters, sort direction, pagination, replay links, raw JSON access, and empty/loading/error states work without interrupting an active simulation.
