# Independent UI and simulation hosts

Gauntlet now runs in two processes from a shared .NET project:

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

`GET /api/v1/ui-state?runId=...` supplies a versioned snapshot of deck manifests and coverage, run metadata, timing records, active progress, resource capacity, mana values, reusable-game keys, and the requested session's game records. JSON responses are compressed. The UI computes display metrics and sorts from that snapshot, without opening SQLite or reading replay files.

Actions use the existing import, run creation, pause (`/cancel` for wire compatibility), resume, retry, and delete endpoints. Run creation forwards `requireIdle=true` for the home-page startup guard. Each action obtains the current service token; credentials are kept inside the UI process and are not persisted. A service restart does not require restarting the UI to refresh its token.

Replay records use the replay API, with `audit=true` only when explicitly enabled. The UI proxies GET session exports, replay downloads, and logs back to the browser at its own origin. Host/origin checks, API mutation tokens, Blazor antiforgery, the four-failure allowance, exact engine/adapter resume checks, and deletion confirmation are preserved.

## Reconnection and restarts

The UI polls the service asynchronously. If it is unavailable, the existing page retains its last snapshot, shows a connection warning, and disables session mutations. Pages retry and recover automatically. A fresh page with no snapshot cannot fabricate prior results. Pausing or restarting the **service** still stops unfinished attempts; recorded completed games are retained and explicit resume uses the original seeds. Restarting only the **UI** does not pause sessions, interrupt Java engines, or perform startup recovery against SQLite.

The first migration from an already running combined host requires one controlled pause, stopping that host, starting the service, and resuming unfinished games. Do not run a new service alongside an older combined host that predates the ownership lock. Thereafter, UI changes need only a UI restart; changes to engine/service code still need a service restart.

## Verification

`tests/HostSeparationChecks` exercises the remote snapshot contract, cache identity, outage/recovery, fresh per-action credentials, guarded creation, and replay audit. Scheduling, sorting, and session-deletion checks continue to use temporary fixtures. Live verification also checks pages, downloads, and that the simulation-service process remains alive while the UI is restarted.

This remains a local Blazor Server interface. The separation does not make it a static frontend or enable cloud hosting by itself.
