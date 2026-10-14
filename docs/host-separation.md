# Independent UI and simulation hosts

Crucible now runs in two processes from a shared .NET project:

- **Simulation service:** `scripts/run-service.sh`, loopback port 5079. Owns deck imports, SQLite, game scheduling, Java engines, replays, timing evidence, and resource measurements. A file lock prevents a second simulation host from opening the same data directory. The service exposes API routes and does not render the interface.
- **UI host:** `scripts/run-ui.sh`, port 5077. Owns Blazor pages, branding, navigation, browser preferences, and its separate protection keys. Registers an API client rather than the database, importer, coordinator, engine, or machine-resource sampler. It has no background simulation scheduler. `MTG_LAN_ADDRESS` enables iPad access to this host; the service stays on loopback.

```sh
# Terminal 1: leave this running during simulations.
./scripts/run-service.sh

# Terminal 2: rebuild/restart this process to update the interface.
MTG_LAN_ADDRESS=192.168.1.246 ./scripts/run-ui.sh
```

`MTG_SERVICE_PORT` and `MTG_UI_PORT` override the script ports. `MTG_BACKEND_URL` overrides the UI's loopback service origin. Direct launches support `MTG_HOST_MODE=service|ui|combined` and `MTG_PORT`. The old combined mode is retained for compatibility; it couples both lifetimes and is not the recommended launch mode.

## API boundary

`GET /api/v1/ui-state` supplies the setup snapshot: deck manifests and coverage, run metadata, timing calibration, active progress, resource capacity, mana values and reusable-game keys. JSON responses are compressed.

Session results, logs and replays use `GET /api/v1/runs/{runId}/state` (also available through `ui-state?runId=...`). This focused snapshot includes only the selected run, its exact deck manifests and coverage, slots, timing records and active build identity. It does not scan other sessions, setup cache keys, global timing samples or resources. Single-run and single-deck lookups use their primary keys. Conditional ETags cover the complete selected-session payload; unchanged responses retain the same slot list and do not rebuild metrics or charts. Results poll every three seconds while active and every ten seconds when stopped. Live progress, saved pilot identity, filters, diagnostics and replay evidence retain their existing semantics.

An updated UI can use an older service without restarting active games: if the focused endpoint is absent, it composes the same session state from the existing run, deck, capability, timing and paginated game endpoints. It reads all game pages, with a maximum of 100 per request, and retains unchanged state locally. The full setup snapshot is never used as this fallback.

Run History uses `GET /api/v1/run-history`: one versioned response containing session progress, aggregate matchup outcomes, runtime records, active build identity, and only the decks referenced by those sessions. A single database read transaction projects game outcomes without deserializing attempts or turn samples. History does not load setup caches, resource sampling, card coverage, mana values, or separate per-session summaries. Search, sorting and pagination operate on the loaded history; the same focused request refreshes it every 10 seconds.

Deck Workshop uses `GET /api/v1/workshop-state` for manifests, session metadata, active-session guards, mana-value sorting, turn-limit guidance and resource capacity. It excludes setup caches, timing calibration, coverage scans and game records. Empty drafts are not validated; loaded drafts fetch only their requested Arena card IDs during validation. Comparisons read only the selected matchup and load independently in parallel. Completed comparisons are reused while session timestamp and total are unchanged; active sessions and explicit Refresh fetch fresh results. Browser drafts, immutable saves, and the service-side idle guard remain authoritative.

Deck Library uses `GET /api/v1/library-state` for exact manifests, card coverage and mana-value ordering. It excludes session, timing, capacity and setup-cache data. Conditional ETags cover the full library payload, including metadata and active-bundle coverage; unchanged polls return 304 and retain the same UI snapshot. Previous runs use the aggregate history endpoint, and unchanged session data does not rebuild deck histories or rerender the library. Card rows are created when a deck's card list opens, and rules markup is created when a card row opens. Imports refresh the focused view. During a rolling update, an older service remains usable through the existing snapshot API until it can be restarted safely.

Actions use the existing import, run creation, pause (`/cancel` for wire compatibility), resume, retry, and delete endpoints. Run creation forwards `requireIdle=true` for the home-page startup guard. Each action obtains the current service token; credentials are kept inside the UI process and are not persisted. A service restart does not require restarting the UI to refresh its token.

Replay records use the replay API, with `audit=true` only when explicitly enabled. The UI proxies GET session exports, replay downloads, and logs back to the browser at its own origin. Host/origin checks, API mutation tokens, Blazor antiforgery, the four-failure allowance, exact engine/adapter resume checks, and deletion confirmation are preserved.

## Reconnection and restarts

The UI polls the service asynchronously. If it is unavailable, the existing page retains its last snapshot, shows a connection warning, and disables session mutations. Pages retry and recover automatically. A fresh page with no snapshot cannot fabricate prior results. Pausing or restarting the **service** still stops unfinished attempts; recorded completed games are retained and explicit resume uses the original seeds. Restarting only the **UI** does not pause sessions, interrupt Java engines, or perform startup recovery against SQLite.

The first migration from an already running combined host requires one controlled pause, stopping that host, starting the service, and resuming unfinished games. Do not run a new service alongside an older combined host that predates the ownership lock. Thereafter, UI changes need only a UI restart; .NET service code changes still need a service restart.

## Forge hot swap

`scripts/activate-forge.sh` compiles the Java bridge, snapshots the Forge jar, card resources, and class files into `data/engine-bundles/<engine-hash>-<adapter-hash>/`, verifies the copied hashes, and atomically replaces `active.json`. Set `MTG_FORGE_JAR` to a jar inside `vendor/` when more than one Forge version is installed. Activation can run while both hosts stay up. The service reads the active pointer on each capabilities request and pins the bundle hashes in every new session. Each game launches Java from its session's bundle; swapping during a run does not change that run's engine. Paused runs can resume while their pinned bundle remains available. Do not edit or delete published bundles. New bundles retain a full copy of the approximately 330 MB Forge resources, so periodically review disk use before publishing many versions.

## Verification

`tests/HostSeparationChecks` exercises the remote snapshot contract, cache identity, outage/recovery, fresh per-action credentials, guarded creation, and replay audit. Scheduling, sorting, and session-deletion checks continue to use temporary fixtures. Live verification also checks pages, downloads, and that the simulation-service process remains alive while the UI is restarted.

This remains a local Blazor Server interface. The separation does not make it a static frontend or enable cloud hosting by itself.
