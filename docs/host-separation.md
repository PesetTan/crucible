# Independent UI and simulation hosts

Crucible runs as two independent hosts sharing `Crucible.Core`: the Svelte frontend host (`Crucible.UiHost`) and standalone backend (`Crucible.Service`). [Crucible Monitor](crucible-monitor.md) packages both as Native AOT executables on macOS; the shell scripts below run JIT development hosts. [Svelte desktop](svelte-desktop.md) covers the Tauri window and UI migration. The preceding Blazor implementation remains in `DeckTester` for rollback.

- **Simulation service:** `scripts/run-service.sh`, loopback port 5079. Owns deck imports, SQLite, game scheduling, Java engines, replays, timing evidence, and resource measurements. A file lock prevents a second simulation host from opening the same data directory. The service exposes API routes and does not render the interface.
- **UI host:** `scripts/run-ui.sh`, port 5077. Serves Svelte assets, branding and presentation metadata, and proxies the versioned backend API. Browser preferences stay in local storage. It reads frozen local pilot scorecards and persists build aliases in its UI state directory. It has no simulation database or scheduler. `MTG_LAN_ADDRESS` enables iPad access to this host; the service stays on loopback. The legacy Blazor frontend additionally retains its separate protection keys and editorial-note store for rollback.

```sh
# Terminal 1: leave this running during simulations.
./scripts/run-service.sh

# Terminal 2: rebuild/restart this process to update the interface.
MTG_LAN_ADDRESS=192.168.1.246 ./scripts/run-ui.sh
```

`MTG_SERVICE_PORT` and `MTG_UI_PORT` override the script ports. `MTG_BACKEND_URL` overrides the UI's loopback service origin. `MTG_LAN_ADDRESS` must be a private IPv4 address assigned to this computer; replace the example with yours. Direct `DeckTester` launches support `MTG_HOST_MODE=service|ui|combined` and `MTG_PORT`; `Crucible.Service` accepts service mode only. The old combined mode couples both lifetimes and is retained for compatibility. Use Monitor or shell launches, with one backend owning the data directory. Native publishing and runtime checks are described in [Native backend](native-backend.md).

## API boundary

`GET /api/v1/ui-state` supplies the setup snapshot: deck manifests and coverage, run metadata, timing calibration, active progress, resource capacity, mana values and reusable-game keys. JSON responses are compressed.

Session results, logs and replays use `GET /api/v1/runs/{runId}/state` (also available through `ui-state?runId=...`). This focused snapshot includes only the selected run, its exact deck manifests and coverage, slots, timing records and active build identity. It does not scan other sessions, setup cache keys, global timing samples or resources. Single-run and single-deck lookups use their primary keys. Conditional ETags cover the complete selected-session payload. Svelte results poll every three seconds while active and stop fetching completed sessions; explicit Refresh remains available. Live progress, saved pilot identity, filters, diagnostics and replay evidence retain their existing semantics.

The retained Blazor client has a compatibility fallback for services without the focused endpoint. The Svelte client requires the current versioned API, so update older backend installations during an idle maintenance window before switching their frontend. This checkout's existing Native AOT backend already supplies that API.

Run History uses `GET /api/v1/run-history`: one versioned response containing session progress, aggregate matchup outcomes, runtime records, active build identity, and only the decks referenced by those sessions. A single database read transaction projects game outcomes without deserializing attempts or turn samples. History does not load setup caches, resource sampling, card coverage, mana values, or separate per-session summaries. Search, sorting and pagination operate on the loaded history; the same focused request refreshes it every 10 seconds.

Setup, selected-session, library, and history reads share a bounded server-side prepared-response cache. SQLite commit versions on persistent read-only connections, the active engine pointer, and Java availability invalidate it. Conditional requests can return 304 before rebuilding or serializing an unchanged payload. Resource capacity uses a separate live read; active simulations continue to invalidate result snapshots as they progress.

Deck Workshop uses `GET /api/v1/workshop-state` for manifests, session metadata, active-session guards, mana-value sorting, turn-limit guidance and resource capacity. It excludes setup caches, timing calibration, coverage scans and game records. Empty drafts are not validated; loaded drafts fetch only their requested Arena card IDs during validation. Comparisons read only the selected matchup and load independently in parallel. Completed comparisons are reused while session timestamp and total are unchanged; active sessions and explicit Refresh fetch fresh results. Browser drafts, immutable saves, and the service-side idle guard remain authoritative.

Deck Library uses `GET /api/v1/library-state` for exact manifests, card coverage and mana-value ordering. It excludes session, timing, capacity and setup-cache data. Conditional ETags cover the full library payload, including metadata and active-bundle coverage; unchanged polls return 304 and retain the same UI snapshot. Previous runs use the aggregate history endpoint, and unchanged session data does not rebuild deck histories or rerender the library. Card rows are created when a deck's card list opens, and rules markup is created when a card row opens. Imports refresh the focused view. During a rolling update, an older service remains usable through the existing snapshot API until it can be restarted safely.

Actions use the existing import, run creation, pause (`/cancel` for wire compatibility), resume, retry, and delete endpoints. Run creation forwards `requireIdle=true` for the home-page startup guard. Each action obtains the current service token; credentials are kept inside the UI process and are not persisted. A service restart does not require restarting the UI to refresh its token.

Replay records use the replay API, with `audit=true` only when explicitly enabled. Svelte proxies session log, game, replay and read-only export/download requests at its own origin. Exports are generated by the backend. Host/origin checks, separate UI/backend mutation tokens, the four-failure allowance, exact engine/adapter resume checks, and deletion confirmation remain enforced. The retained Blazor frontend additionally uses antiforgery and excludes export proxy routes.

## Reconnection and restarts

The UI polls the service asynchronously. If it is unavailable, the existing page retains its last snapshot, shows a connection warning, and disables session mutations. Pages retry and recover automatically. A fresh page with no snapshot cannot fabricate prior results. Pausing or restarting the **service** still stops unfinished attempts; recorded completed games are retained and explicit resume uses the original seeds. Restarting only the **UI** does not pause sessions, interrupt Java engines, or perform startup recovery against SQLite.

The first migration from an already running combined host requires one controlled pause, stopping that host, starting the service, and resuming unfinished games. Do not run a new service alongside an older combined host that predates the ownership lock. Thereafter, UI changes need only a UI restart; .NET service code changes still need a service restart.

## Forge hot swap

`scripts/activate-forge.sh` compiles the Java bridge, snapshots the Forge jar, card resources, and class files into `data/engine-bundles/<engine-hash>-<adapter-hash>/`, verifies the copied hashes, and atomically replaces `active.json`. Set `MTG_FORGE_JAR` to a jar inside `vendor/` when more than one Forge version is installed. Activation can run while both hosts stay up. The service reads the active pointer on each capabilities request and pins the bundle hashes in every new session. Each game launches Java from its session's bundle; swapping during a run does not change that run's engine. Paused runs can resume while their pinned bundle remains available. Do not edit or delete published bundles. New bundles retain a full copy of the approximately 330 MB Forge resources, so periodically review disk use before publishing many versions.

## Verification

`tests/HostSeparationChecks` exercises the remote snapshot contract, cache identity, outage/recovery, fresh per-action credentials, guarded creation, and replay audit. Scheduling, sorting, and session-deletion checks continue to use temporary fixtures. Live verification also checks pages, downloads, and that the simulation-service process remains alive while the UI is restarted.

The Svelte frontend is static browser code with a local native API proxy. A Tauri window does not make the backend publicly reachable or add cloud ingestion.
