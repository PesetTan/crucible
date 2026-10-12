# Gauntlet — local Magic Standard Bo1 simulator

A C#/.NET 10 application with independently running UI and simulation hosts. The UI uses Blazor Server and calls a loopback API; the simulation service owns SQLite storage and full opposed games resolved by the pinned Forge 2.0.15 engine. Games run locally without language-model calls. The small Java file in `engine/` is the interoperability shim for the existing Java engine; application and interface logic are C#.

## Why Gauntlet?

A gauntlet is both an armored glove and a demanding series of challenges. To **throw down the gauntlet** is to challenge an opponent; to **run the gauntlet** is to face a succession of tests. Gauntlet brings those meanings together: select decks, pit them against one another, and see how each withstands the field. The armored fist assembles from separate plates on each page load, then holds its finished pose. Five metallic colorways rotate in a fixed order: silver, steel blue, cobalt steel, verdigris, and red copper.

## Repository contents

This repository contains the application, Forge interop source, assets, and documentation. Local decks, simulation results, replays, downloaded engines, Java runtime, and build outputs are excluded. The source Arena database is separate and must be supplied at `../../standard_cards.sqlite` relative to the project directory, with the frozen public snapshot and saved-deck tables described in the importer. A fresh clone needs that database and the engine setup below before use.

## Start

From this project directory:

```sh
dotnet restore src/MtgSimulator/MtgSimulator.csproj
dotnet run --project src/MtgSimulator/MtgSimulator.csproj -- --setup
./scripts/run-service.sh
```

In a second terminal, start the UI:

```sh
./scripts/run-ui.sh
```

Open [Gauntlet](http://127.0.0.1:5077). Setup downloads Forge and a project-local Temurin Java 17 runtime, verifies published archive checksums when supplied, and compiles the interop shim. Automatic runtime setup supports macOS and Linux. `tar` and the .NET 10 SDK are required.

The service listens on `127.0.0.1:5079`; the UI listens on `127.0.0.1:5077`. Restart or rebuild only the UI when changing pages: the simulation service continues running. The UI does not open the card database, simulator database, or Java engine. Both hosts are independently launched from the shared project. See [host separation](docs/host-separation.md).

The default source is `../../standard_cards.sqlite` relative to this project. Set `MTG_PROJECT_ROOT` if launching from another content directory; set `MTG_JAVA` to use another compatible Java executable. The project-local runtime is the default. Stop the app before rerunning engine setup.

For access from an iPad on the same Wi-Fi, set `MTG_LAN_ADDRESS` to this Mac's private IPv4 address when starting the app, for example `MTG_LAN_ADDRESS=192.168.1.246 ./scripts/run-ui.sh`. Open `http://192.168.1.246:5077` on the iPad and allow incoming connections for the Gauntlet executable in the Mac firewall. The Mac must stay awake. Devices on that network can use the app, including starting simulations and viewing stored records. The default remains local access only; the network address may change when reconnecting to Wi-Fi.

## Use

1. Select two to five exact deck versions. Every selected deck plays every other selected deck. The library contains the 33 published A- and B-tier representatives, every available exact Tier A and C list from the frozen public snapshot (including variants), and the separately labeled personal Reanimator snapshot. Published exact-list win rates and approximate match counts are stored with their source snapshot for later comparison with simulated results. Duplicate archetypes show a numbered variant and list fingerprint.
2. Choose an even game count per matchup. The default 200 per matchup schedules 2,000 games for five selected decks, evenly split by starting player. Choose your field from the 34-deck library.
3. Start an exploratory run. Click a matrix win-rate cell to jump to its matchup detail directly below the matrix. Choose any pairing from the detail's sticky **Choose Matchup** selector; **Games for Matchup** provides the same selection beside the replay list. Both selectors update the rates, outcome distributions, and game list together. Inspect life/board/hand/graveyard development in the field charts below; later-turn samples show their active-game counts.
The eight field charts include **Nonland Cards in Hand** and **Win Rate by Game Length**. Every field chart and outcome histogram has an **i** icon beside its title; click it (or focus it and press Enter) for the metric's purpose, interpretation, and limitations. Nonland hand means exclude missing composition records. Game-length win rates use completed games ending in each round, with draws counted as nonwins, a fixed 0–100% scale, and per-length sample counts. A round groups two actual player turns, matching the outcome histograms; extra/skipped turns can change that pairing. The game-length chart is conditional on the game's ending length, not a forecast for a game still in progress. Both additions follow the matchup filter and keep each deck's existing color.

4. Open a completed game's replay to step through engine events and snapshots. Private hands are hidden unless the audit view is enabled.

Mana-starved and mana-flooded incidents are logged per player, with own/actual turn numbers and supporting board/hand counts. Filter games by either flag and jump to the first affected turn in a replay. The run page explains the conservative land-based detection rules; the compact incident log is at `/api/v1/runs/{id}/mana-events`. Flags are observed conditions, not proof of why a player lost. See [the detection specification](docs/specs/08-mana-incident-logging.md).

Each synthetic player's **mulligans before keep** and **kept hand size** appear in game records and replays. Per-deck summaries include mean/median/maximum counts, 0/1/2/3+ frequencies, and play/draw splits. The pilot's keep decision defines an accepted hand; a rules-forced keep is identified separately. Older compatible complete replays are analyzed from their actual mulligan events, with their evidence source disclosed. See [mulligan tracking](docs/specs/09-mulligan-tracking.md).

New sessions stagger initial engine starts by one second. With four workers, starts occur at 0, 1, 2, and 3 seconds. Each worker then starts its next game immediately after the previous one finishes, keeping available slots busy. This spreads startup load without pacing every subsequent game; faster completion depends on the machine and is not assumed in the runtime estimate. The parallel-game setting remains the active-engine cap, with two to four workers available in the UI. Cancelling stops workers still waiting to start; a short queue does not wait for unused worker offsets. Older saved sessions retain their original scheduling policy when resumed.

Run `dotnet run --project tests/SchedulingChecks/SchedulingChecks.csproj` to check immediate slot refill, the parallel-game cap, cancellation during startup, short queues, runtime estimates, and compatibility with older saved sessions. These checks use simulated work and do not launch Forge or modify stored sessions.

**Maximum Actual Player Turns** defaults to the longest completed game across all sessions, rounded up to the next multiple of 10 within the 10–1,000 turn range (for example, 64 becomes 70). This includes completed games in sessions still in progress. The displayed maximum refreshes every 10 seconds. Editing the limit disables automatic changes; enable **Update from the Longest Game** to follow the maximum again. Failed or incomplete games are excluded, and the fallback before any completed games is 150 turns. Run `python3 tests/check_session_turns.py` to check the aggregation using a temporary in-memory database.

**Pause Session** saves completed results and stops unfinished games. Its hover/focus help explains that resuming restarts unfinished games with the same seeds and requires the same engine/adapter.

**Delete Session** is in the three-dot **Session actions** menu beside the status at the top right of session details and asks for confirmation before removing its game records and results. Running or queued sessions must be paused and stopped first. Replay files used by another session are preserved; unused attempt artifacts and request keys are removed. After deletion, the app returns to **Run History** and reports any replay files that could not be removed. Run `dotnet run --project tests/DeleteSessionChecks/DeleteSessionChecks.csproj` to check confirmation, deletion, shared replay preservation, and scheduler protection using temporary fixtures.

The capacity badge estimates whether the selected parallel-game setting fits the local machine, refreshing every 10 seconds. Hover or focus it for a short explanation. It compares available/reclaimable memory and logical CPU count with a budget of 1,600 MiB per game plus 1 GiB spare. macOS uses free, inactive, and speculative pages; Linux uses `MemAvailable`. Unsupported or unreadable memory reports “Check unavailable.” This is advisory and does not guarantee speed or prevent starting a run.

Start with two workers. Each Java process can use up to 1.2 GB of heap. Startup is included in per-attempt limits and measured completion estimates. Cancelled unfinished slots resume with their original seeds; genuine failed slots require explicit retry. Each game permits up to four genuine failed attempts; deliberate pauses and service interruptions do not consume that allowance. Administrative turn limits, timeouts, crashes, and corrupt replays never count as rules draws.

Each session records active runtime in `session_timings` inside `data/simulator.sqlite`. Run History and session details show the accumulated time; details include each execution period and `/api/v1/runs/{id}/timings` exposes the timing log. Queue time and pauses between executions are excluded. New periods are added on resume or extension, and checkpointed every 10 seconds and after game attempts. After a service interruption, only the last checkpoint is retained and labeled; cancelled, failed, interrupted, and cache-only periods do not calibrate estimates. Older sessions without these measurements remain explicitly unrecorded.

Future runtime ranges still use individual game-attempt measurements, then adjust for observed session overhead using up to 20 recent completed periods with at least five new attempts and the same deck versions, worker count, timeout, turn limit, and stagger policy (also matching engine/adapter). The adjustment compares measured active time with the modeled parallel schedule of that period's actual attempt durations. Stored games are excluded from new-work estimates. These remain planning ranges, not guaranteed completion times; the home page refreshes timing evidence every 10 seconds.

Deck selections and run settings are remembered in this browser across page navigation and refreshes. Each session also remembers its selected matchup (including deck order), outcome and mana filters, and game-list page. The replay audit preference follows you between replays. Clearing browser site data resets these preferences. Removed or unsupported decks are dropped from restored selections, and automatic turn limits continue to follow the longest completed game when enabled.

Every table column has a clickable heading that toggles ascending and descending order, with an arrow showing the selected direction. Each table keeps its own sort while results refresh. Numbers and timestamps use their underlying values, and unknown measurements stay last in both directions. Game evidence sorts all filtered games before pagination and returns to the first page when its sort changes. Matrix opponent headings sort row decks by win rate against that opponent. Compound mulligan summaries sort by median then maximum, or on-play mean then on-draw mean; game mulligans sort by both players’ total and mana flags by incident count. Run `dotnet run --project tests/TableSortChecks/TableSortChecks.csproj` for focused sorting checks.

The three-dot **Session actions** menu also contains pause/resume/retry controls and links to the hand, mana-incident, runtime, and game-evidence pages. The session status appears beside the SESSION label above the results title. Unavailable actions are disabled. Click outside or press Escape to close the menu; Escape returns focus to the menu button.

The home page disables deck selection and run setup while any session is queued, running, or cancelling. A progress bar and waiting → running → completed diagram show the active workload and busy worker slots, alongside session status, counts, and a link to session details. Processed counts include completed, failed, and cancelled games; reused results are identified separately. Status refreshes every 10 seconds and setup unlocks after the active sessions finish or stop. The home-page start operation also checks the active-session state while holding the coordinator lock to prevent competing browser tabs from starting another session.

## Result fidelity

This implementation exposes **exploratory automated-pilot results**. Forge provides actual rules-resolved games; card scripts are present for all six imported lists. Script presence and an end-to-end run do not establish correct resolution of every required interaction or competitive pilot quality.

The user requested no automated tests. No test project or fixture suite has been added. Consequently, the plan's rules/pilot validation gates remain unpassed and validated-result mode is blocked. In particular:

- Forge Default AI has known strategic limitations; combo sequencing and its hidden-information behavior have not been independently audited here.
- London mulligans approximate Arena Bo1 opening-hand behavior; no guessed hand smoother is used.
- Seeded runs record inputs, but independent trace-determinism validation remains outstanding.
- Replays retain hashed full captured-state checkpoints and engine event descriptions. They do not provide a complete normalized legal-decision/payment protocol, object-lineage reconstruction, mana-pool state, or library-order audit.
- Actual player turns and each player's own turns are authoritative. Extra turns are tagged. Exact skipped-turn cycle grouping is unavailable and is not fabricated.
- Legality and the personal deck are dated local snapshots. The app never refreshes account logs, modifies source databases, infers ownership, or treats public rates as simulated outcomes.
- Loss evidence describes the engine's terminal condition; causal explanations remain unknown unless separately established.

## Storage and API

`data/simulator.sqlite` holds immutable deck manifests, saved run configuration, slots, attempt history, and dated session extensions. `data/replays/` stores compressed completed replays and incomplete attempt evidence. Engine preferences, cache, and ASP.NET protection keys remain under `data/`. Generated data and engine binaries are excluded from Git.

The simulation service binds only to `127.0.0.1:5079`, checks host/origin, and requires a service instance token for API mutations. The UI host uses port 5077 and can additionally bind to the configured private Wi-Fi address. It validates host/origin and forwards actions over loopback with a fresh service token. The frontend has no export or download controls, and the UI host does not proxy export endpoints. The simulation service retains summary and replay export endpoints for backend use. `/api/v1/session` supplies the token to a same-origin local client. Run creation also requires an `Idempotency-Key`. Ordinary replay API responses omit private hands; `audit=true` explicitly enables them. Summary export labels results exploratory.

See [specifications](docs/specs/README.md), [implementation status](docs/implementation-status.md), and [third-party notices](THIRD_PARTY.md). The specifications describe the intended validated MVP; the status document distinguishes the implemented exploratory app from its outstanding evidence gates.

Run History names each session’s deck combination. With the same root seed, completed games are reused across subsets, overlapping combinations, and larger game counts. Reuse requires the same deck versions, per-game seed/index/starter, engine, adapter, pilot, and opening/seed policies, plus an intact replay. Failed or cancelled games are rerun. New timeout/turn limits apply to missing games; existing completed results are reused unchanged. Each reused game stores its original session/game identity and links to its source session. Cached attempts are excluded from runtime and finished-game-length observations.

Selecting the same compatible deck combination and seed continues the existing session instead of creating another one. A higher requested game count appends missing deterministic slots, increases `GamesPerPair` and `Total`, and records a dated extension entry with previous/target counts and added/reused games. Completed slots and replay sources are retained. Existing execution settings remain in effect; limits/settings are not rewritten for previously played games. An active session drains added pending games before completing. Smaller requests open the existing session at its current count.

## Archetype tiers

The deck picker and library show Untapped.gg A–D archetype tiers from the October 11, 2026 Standard Bo1 Bronze–Platinum snapshot (September 29–October 11). Source and capture date are displayed with the ratings. Personal lists and archetypes absent from the published tier list remain unrated. Tiers describe public archetypes, not simulator performance. SQLite stores `archetype_tier`, `tier_source_url`, and `tier_snapshot_date` separately from immutable simulation manifests. Run `python3 ../../scripts/import_archetype_tiers.py` from this project to reapply the dated source annotations.

The 33 A/B representatives comprise 31 lists from the 200-match curated page and two visible 100+ match archetype-page lists for Selesnya Landfall and Selesnya Ouroboroid. The latter have smaller samples. The room card Unholy Annex // Ritual Chamber is passed to Forge by its loaded first-face name while the source deck retains its printed combined name.
