# 06 — Interface and local API

Date: October 11, 2026 (America/Los_Angeles)

Status: Design contract with outstanding validation gates. Parent: [specification index](README.md). Current behavior and recorded evidence: [implementation status](../implementation-status.md).

## User flows

Run setup presents deck-selection groups in the fixed order Tier A, Tier B, Tier C, Your Deck, Custom Decks, then Unrated, omitting empty groups. Source payload/import order must not interleave personal/custom groups with the tiers. Classification and future-label handling follow [11 — Run-setup deck-selection order](11-visual-design-and-deck-lists.md#run-setup-deck-selection-order).

The workspace sidebar includes Forge Pilots at `/pilots`. Four visible views separate Pilots & results, Deck assignments, Engagement protocols, and Attribute checks, as specified in [11 — Visual design and Deck Lists](11-visual-design-and-deck-lists.md). The initial overview shows assigned profiles with explicit deck/details actions; search, all/measured profiles, and older versions remain discoverable. A policy selector in Deck assignments previews the shared registry under each available run policy; it does not change session settings. Show service, loading, and empty states, and provide refresh. Saved sessions retain their recorded pilot identities.

Forge Pilots also shows programmatic attribute evaluations from immutable engine fixtures. Separate configured Forge preferences, observed aggression and acceptable-decision checks. Show exact-profile numerators/denominators, inheritance, scope, expected/observed cases and build identity. Untested attributes remain unmeasured; evaluation errors are separate from failed checks; incompatible build or suite reports are historical. Each metric links to frozen evaluation code, with navigation back preserving the profile. Never infer overall pilot skill or Arena performance. See [pilot attributes](../pilot-attributes.md).

Pilot match win rates use saved `PilotProfiles` and completed outcomes from the focused history API. Show each exact profile's latest measured session, its W/L/D record and completed player appearances. Draws remain in the denominator; games with identical pilots contribute a record for each side. Keep sessions separate to avoid pooling reused results, builds, attributes or experiments. Expand evidence for source-session links and recorded build, attributes, policy, optimizer tuning, Performance Mode and seed identity. Older versions are opt-in; current profiles with no recorded games stay unmeasured. Do not infer missing legacy assignments from current deck classifications. These exploratory rates reflect the decks and opponents tested, not isolated pilot ability or Arena forecasts.

1. **Choose decks:** select two to ten distinct exact versions; show public/personal origin, exact variant, count, capture date, eligibility, and a readable list. Public 4-Color Reanimator and personal Reanimator are separate cards. Explain each blocked deck with the affected card, mechanic, or pilot fixture.
2. **Configure a run:** choose all pairings or one matchup; default to 200 games per pairing and balanced play/draw. Show total games, opening-policy limitation, tested pilot identity, and measured runtime estimate when available.
3. **Run locally:** show valid completed games, total scheduled games, failures, active workers, elapsed time, and remaining estimate. Provide cancel and later resume. Do not display assumed win rates while no valid games have finished.
4. **Compare results:** matchup matrix, W/L/D totals, play/draw splits, outcome-conditioned turn distributions, life/board charts, active-game denominators, and validation limitations. Unvalidated diagnostic runs receive a persistent diagnostic label.
5. **Inspect a game:** filter by matchup, starter, outcome, duration, or failure. Open a replay and navigate actual turns, turn cycles, phases, or events. Show incomplete cycles, extra/skipped turns, and last valid checkpoint for failures.
6. **Inspect session evidence:** open dedicated Hand Log, Mana Incident Log, Runtime Log, and Game Evidence pages from the session actions menu. Each page has a clear title, readable labels, filters relevant to its records, sortable columns, and 25-record pagination. Show loading, empty, and error states.

The session status pill sits on the same line as `SESSION / {short id}` above the results title. The frontend has no export, download, or raw API links. Summary and replay export endpoints remain available on the loopback simulation service for backend use; the UI host does not proxy those export endpoints.

In the matchup matrix, a small gold trophy beside a row deck name marks the highest mean opponent win rate, with equal weight for each shown opponent. Show the trophy only once every shown matchup has at least one valid completed game. Equal leaders each receive it. Recompute it when results or the selected matchup change; sorting rows does not change the leader.
Each row deck name also has a compact pill showing that deck's equal-weighted mean opponent win rate, using the same calculation as Overall Performance. Show a coverage fraction in the pill when only some shown opponents have valid results; omit the pill when none do. Update the pill as results or the selected matchup change.

Use plain labels and tooltips. Do not expose process arguments or storage schemas in the ordinary flow. Display the opening policy as “London mulligan; Arena Bo1 opening hands approximated” unless a verified Arena profile is implemented later. Rates are “simulated pilot win rate,” never a prediction of the user's Arena win rate. Hovering or focusing a displayed simulated rate opens source context: the exact list's published win rate, approximate source match count, provider, tier, and captured observation window where recorded. Public source rates describe their recorded field, not the selected simulated matchup. Workshop descendants may show a published ancestor only as an explicitly labeled original-list reference; edited or personal lists without source statistics remain unrated. Apply the shared popup to matrix cells and averages, matchup and play/draw rates, history, Workshop comparisons, and game-length chart legends and table values. Popups must remain visible above scrollable tables and fit narrow viewports.

Use the approved typography and semantic theme tokens with the shared page headers, spacing, field alignment, and restrained separators in [11](11-visual-design-and-deck-lists.md). Keep layout and text wrapping usable at narrow widths; avoid separate page-specific increments to the migrated typography scale.

## Replay presentation

Show both players, life, board, hand counts, graveyards, exile, relevant counters/attachments, active player, phase, and stack when applicable. Allow a public-information view and explicit omniscient audit view. Plain card names and text are sufficient; artwork is not required for MVP acceptance.

Provide previous/next event, previous/next turn, jump to cycle, and autoplay controls. A cycle summarizes its included actual turns; unsupported cycle grouping must fall back visibly to actual turns. Selecting a decisive loss event highlights the relevant objects where evidence permits. Failed games show failure status rather than a fabricated winner.

## Signed AI commentary

Use the shared `AiNote` disclosure for saved deck summaries, pilot descriptions, chart guidance, and exact-session observations. Persist commentary in SQLite with its author, model family, optional exact model ID, actual writing time, and evidence/context. Revisions append new records and preserve earlier attribution. Never infer an unavailable runtime model ID or silently author new commentary during rendering. Scope deck notes to immutable versions and measured chart observations to the exact run and field; selected matchups fall back to general metric guidance. See [AI notes](../ai-notes.md).

Editorial annotations are an explicit exception to the simulation data boundary: the UI and service may use the separate `AiNoteStore` against the same database for short annotation transactions. This permits signed editorial updates without restarting the simulation service. Simulation state, slots, and replays continue through the existing backend. `GET /ai-notes?scope=...&subject=...` reads revisions; instance-token-protected `POST /ai-notes` appends a signed record. Existing matchup analysis retains its original provenance.

## App-owned HTTP contract

Use `/api/v1`, JSON, stable IDs, and ISO 8601 timestamps with offsets. These are app-owned service routes rather than Forge interfaces. Validation/gate requirements remain design targets where [implementation status](../implementation-status.md) records an outstanding limitation:

| Method and route | Contract |
|---|---|
| `GET /health` | Host readiness, role, ownership identity, active work, runtime mode, and JSON reflection status; see [Monitor](../crucible-monitor.md) |
| `GET /capabilities` | Engine identity, verified capabilities, runtime requirements, gate status |
| `GET /decks` | Immutable deck versions, provenance, counts, eligibility |
| `GET /decks/{id}` | Full manifest and applicable validation reports |
| `POST /imports` | Import selected known source decks read-only; create new versions as needed |
| `POST /runs/preflight` | Validate an immutable requested configuration and return blockers/warnings |
| `POST /runs` | Create and queue a validated configuration; require idempotency key |
| `GET /runs` | Paginated run history and status |
| `GET /run-history` | Versioned history view: aggregate matchup outcomes, progress, runtimes, build identity and referenced decks; excludes setup caches and game evidence |
| `GET /ui-state` | Setup snapshot with conditional ETag revalidation; reuse prepared data while its databases and engine selection are unchanged |
| `GET /runs/{runId}/state` | Focused session snapshot with conditional ETag revalidation |
| `GET /resources` | Current advisory processor/memory capacity, sampled independently of cached setup data |
| `GET /workshop-state` | Versioned editor state: library manifests, session metadata, active guards, mana values, turn guidance and capacity; excludes setup caches and game evidence |
| `GET /library-state` | Versioned library manifests, coverage and mana sorting; conditional ETag revalidation returns 304 for unchanged data |
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

Setup, focused session, history, and library reads share a bounded cache across UI circuits. SQLite `data_version` is compared on persistent read-only connections to both the simulation and source-card databases; commits from any other connection invalidate prepared reads. Engine selection and Java availability changes also invalidate them. No read transaction stays open between requests. A write during assembly prevents retaining that response as current. Unchanged conditional requests return 304 before rebuilding, hashing, or serializing the payload. Capacity remains a separate live read; cached setup capacity is only a fallback when connecting to older services. Active simulations still invalidate the cache as they progress; this cache chiefly removes repeated idle work.

Preflight and run creation both verify capability/version hashes so a stale preflight cannot authorize a changed setup. An idempotency key reused with a different payload returns a conflict. Paths are resolved server-side from known IDs; the browser cannot select arbitrary filesystem output paths. All mutation endpoints require the local instance token.

Results poll focused state every three seconds while active and every ten seconds when stopped, with conditional ETags retaining unchanged state. Detailed replay data is loaded only on demand and never implicitly sent to chat or an external service.

## Session log pages and navigation

The UI serves `/runs/{id}/logs/hands`, `/runs/{id}/logs/mana`, `/runs/{id}/logs/runtime`, and `/runs/{id}/logs/games`. Its backend client reads the corresponding service API responses or requested session snapshot; simulation logs and replays do not use direct UI database/file reads. Editorial notes and pilot scorecards retain the separate local access described above. The UI host also proxies raw session log and paginated game GET requests at its own origin. A UI restart can publish these pages without stopping an active simulation service.

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

## Deck Workshop

Add `Deck Workshop` between Deck Library and Run History in the sidebar, at `/workshop`. Deck Library offers `Edit a copy`. Each deck in a session matchup offers `Modify & Rerun`, linking with explicit deck, opponent, and baseline session IDs. Workshop session results link back to their comparison.

The workspace supports choosing a library/custom version or pasting Arena text, card search, quantity controls, removal, rules text, a main-deck total, and added/removed changes. Present editable cards as a Deck List using the common category, quantity-total, front-face classification, and ascending mana-value/name order in [11](11-visual-design-and-deck-lists.md). Preserve drafts and workshop selections in browser storage. Explicit entry links take precedence over a restored draft from a different entry context. Incomplete drafts are allowed; save and run errors identify the affected cards. Sideboard provenance is collapsed and read-only.

Keep the baseline and exact version visible. Library entry requires an opponent and starts with existing run defaults. Results entry copies the recorded matchup settings. Editing is available during active simulations; the service enforces the existing idle requirement for launching another run. `Rerun Matchup` saves the current valid version and starts a new session. Comparison remains in the workspace, refreshes while active, and links to both sessions, Game Evidence, and recorded game replays through `Open Replay`. Users can choose a prior matching matchup as the new baseline.

Add service contracts: `GET /api/v1/cards/search?q=...` (bounded catalog results); `POST /api/v1/decks/resolve` (Arena text to cards plus line issues); `POST /api/v1/decks/validate` (structural errors and simulation blockers); `POST /api/v1/decks/custom` (immutable custom version); and `GET /api/v1/runs/{id}/workshop?deckId=...&opponentId=...` (scoped summary and diagnostic/replay evidence). Extend IUiBackend for these operations. Deck creation accepts name, optional parent ID, and main-deck identity/quantity pairs; all card metadata is resolved service-side. Mutation requests retain local instance-token protection. Existing manifests/sessions deserialize with optional lineage/baseline metadata absent.

### Consistent deck labels

Within each setup group, order decks alphabetically by the canonical mana/color identity name, with full deck label and exact ID as tie-breakers. Setup deck tiles toggle selection directly, with no selection checkbox and with an accessible pressed state. Only the group title toggles its disclosure; selection, polling, and preview actions preserve group open state. All displayed pilot names begin with `Pilot: `. See [visual design and Deck Lists](11-visual-design-and-deck-lists.md) for interaction and labeling requirements.

Use the shared `DeckIdentity` component throughout deck selection, library, workshop, history, results, charts, logs, and replay. Display colors (guild/shard/wedge names, mono-color, or colorless) and subtype; show the persistent variant friendly name beneath in smaller text. Preserve source names and exact IDs in stored manifests. The component wraps within its available width, with a compact mode for dense tables, legends, and actions. Deck selectors use the shared `DeckChoiceLabel` renderer and presentation helper; accessibility text uses `DeckDisplay.Text`. Missing classifications remain explicit; older named workshop copies can use their existing strategy mapping.

Deck dropdowns group choices alphabetically by color name (such as Abzan, Dimir, and Red), with subtype and friendly variant text retained. Use this same grouping and text-only choice renderer for library, workshop, matchup, baseline, and log selectors; do not repeat mana icons inside selector choices or the selected value.

Matchup dropdown choices and selected values render both deck labels side by side in one `Deck A vs Deck B` row, using the shared `DeckIdentity` component. Each friendly variant name stays beneath its corresponding deck, and optional session context appears beneath the matchup. On narrow screens the deck text may wrap within its side of the row; the opponent must not become a separate row.

All dropdowns use the shared `AppSelect` combobox implementation, including pilot policy, history status and sort, time units, log filters, and trigger categories. `DeckSelect` supplies color grouping and the rich deck/matchup template through this same control. Plain choices retain their declared order and labels; optional all/empty choices are explicit. Preserve change callbacks, keyboard navigation, type-ahead, Escape/Tab and outside dismissal, disabled controls, popup placement, and mobile sizing.

### Compact Deck Library

Deck Library uses compact deck rows with full lists collapsed initially. Show the exact version identity, source, main-deck composition, script coverage, default-policy pilot, and the latest linked session with status and deck-specific valid W/L/D counts. Deck List, Previous runs, and Source & details expand independently. Group and sort every Deck List using [11](11-visual-design-and-deck-lists.md); keep the category order and one readable sequence on narrow screens. Preserve card rules expansion, missing-script markers, sideboard provenance, public observed statistics, lineage, and Edit a copy.

Read previous sessions through the existing aggregate run-history API; link only sessions whose recorded deck IDs include this immutable version. List all matching sessions newest first with their recorded pilot (or Not recorded), status, per-deck valid results, session link, and Game Evidence link. Keep historical pilots separate from the pilot selected by the default new-session policy. Refresh history every ten seconds even when the library ETag is unchanged; unavailable history is distinct from zero prior sessions.

### Compact Deck Workshop

Keep the editor and save action prominent. Collapse source selection after a draft loads; retain the source identity and Original list link beside the draft. Use dense quantity/name/remove rows, expandable card rules, and the shared Deck List groups with live quantity totals from [11](11-visual-design-and-deck-lists.md). Show draft changes in a collapsed disclosure. On wide screens place matchup settings beside the editor; stack them on narrow screens with compact controls. Preserve source/paste workflows, browser drafts, validation, immutable saves, run settings and active-session guards.

Show a compact `Pilots for next run` summary for the current draft and selected opponent beside the matchup settings. Resolve the draft from its current cards and inherited source classification using the same default policy submitted by Workshop reruns. Each comparison shows both `Pilots used` from the session's saved profile mapping; missing historical profiles read `Not recorded`.

All Workshop Deck List categories, including Lands, follow the shared ascending mana-value and name order in [11](11-visual-design-and-deck-lists.md). Do not use a separate mana-production or color order for lands. Every land row visibly shows its mana-production symbols or explicit production state while collapsed, following the land-production contract in [11](11-visual-design-and-deck-lists.md); an absent casting cost is not a substitute for production information.

Compare results starts collapsed except when explicitly entering a result comparison. Its summary indicates available results or the edited-versus-baseline delta. Within expanded results keep rates, W/L/D, denominators and session/evidence links visible; put longer statistics and build details in a disclosure. Advanced settings, sideboard provenance and replay/distribution sections remain expandable and keyboard accessible.

## Pilot optimization API

Service-only `/api/v1/pilot-optimizations` supplies CLI discovery, idempotent experiment creation, controlled source/fixture staging, start/pause/resume, status/report, bounded evidence/source excerpts, JFR diagnostics and explicit promotion. All mutations use existing instance-token/origin checks. Experiment execution shares scheduling exclusion with ordinary sessions. New run/preflight requests optionally accept `optimizerPolicyId`; saved configs freeze the published policy and isolate reuse/timing by its immutable identity. No optimizer UI is added. See [Pilot Optimizer](../pilot-optimizer.md) for the complete workflow and safeguards.

### Results section disclosure preferences

All session results sections start collapsed: deck pilots, mulligans, matchup explorer, matchup details, and field-over-time charts. Each section and nested disclosure remembers its open/closed choice in this browser across refreshes, navigation, and other sessions. Stable section keys preserve choices when matchup filters change the heading. New sections default to collapsed. Browser storage failures leave the native keyboard-accessible disclosures usable. Status, filters, progress, and errors remain visible.
