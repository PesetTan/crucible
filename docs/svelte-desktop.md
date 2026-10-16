# Svelte UI and Tauri desktop

Crucible's UI is Svelte + TypeScript. `Crucible.UiHost` serves the built static assets and a same-origin API proxy as a small .NET Native AOT executable, `CrucibleUi`. It does not own the simulation database or schedule Forge workers. `Crucible.Service` remains the backend, and Forge remains Java. The Tauri application is a Rust window using the system WebView; no Node runtime or bundled Chromium is needed when running the published app.

The same UI runs at `http://127.0.0.1:5077` in a browser. Its fixed backend is `http://127.0.0.1:5079`. Desktop startup validates the application, role, canonical project root, readiness, and native runtime before adopting an existing host. Occupied or incompatible ports cause startup to fail without stopping their owner. Absent hosts start from the bundle; the backend's exclusive service lock still applies. Startup never resumes interrupted simulations.

Crucible is a regular macOS application with a Dock icon and no separate menu-bar icon. Closing the window hides it; reopen it from the Dock or the Monitor's **Open Crucible** action. **Crucible → Quit Crucible** (Command-Q) exits only the desktop app and leaves simulations running. Crucible Monitor is the single menu-bar entry and remains the macOS lifecycle manager; quitting the Monitor can stop hosts it owns, following its existing confirmations.

The Monitor opens a verified `Crucible.app` installed alongside it, in `~/Applications`, or in `/Applications`, using its configured project root and ports. It ensures the hosts are ready first and launches the window with `--existing-hosts-only`, so that launch cannot create a second backend. If the desktop app is not installed, **Open Crucible** opens the browser UI. **Advanced → Open in Browser** is always available.

## Build and run

Use the pinned .NET SDK, Node 22.12+, and npm. Native AOT also needs the platform C compiler/toolchain. On macOS use Command Line Tools and Rust stable for the desktop shell. Build tools are not runtime dependencies of published hosts. `MTG_NODE` and `MTG_DOTNET` can select executables; the scripts also recognize project-local `data/toolchains/node` and Rust toolchains.

```sh
# Development hosts, in separate terminals after engine/database setup:
./scripts/run-service.sh
./scripts/run-ui.sh

# An immutable production UI runtime:
./scripts/publish-ui.sh "$PWD/data/monitor/builds/my-svelte-build" osx-arm64

# Build and locally sign a desktop bundle, reusing a verified native backend:
./scripts/build-desktop.sh "$PWD/data/monitor/builds/native-backend-20261014"
```

The desktop build script prints a unique `dist/desktop-<timestamp>/Crucible.app` path. Copy it with `ditto --norsrc --noextattr` to `~/Applications/Crucible.app`. This development bundle is ad hoc signed, not a notarized public release, and its saved project root points at this checkout. Other users need their own local setup. The [Windows beta](windows-beta.md) adds Windows-specific Native AOT publishing, NSIS packaging, first-launch setup, window-owned process lifecycle, and CI checks. Its Windows execution and human verification gates are tracked separately. Linux desktop packaging remains experimental.

To run a published UI directly, set `MTG_PROJECT_ROOT`, `MTG_PORT`, `MTG_BACKEND_URL`, and optionally `MTG_UI_STATE_ROOT`, then execute `CrucibleUi` from its published directory. Private-LAN browser hosting uses the existing `MTG_LAN_ADDRESS` setting; the backend remains on loopback. The desktop shell uses loopback only. For isolated desktop testing, set `MTG_PROJECT_ROOT` to a copied installation and use separate `CRUCIBLE_UI_PORT` and `CRUCIBLE_BACKEND_PORT` values. Never start a second service against the live database.

## Data and compatibility

Deck identities, pilot assignments, exact 64-bit seeds, idempotency keys, saved sessions, replay exports, and engine bundles retain the backend's contracts. Unsafe JSON integers become decimal strings in the browser rather than rounded numbers. The proxy forwards original JSON bytes and backend validators. It refreshes backend instance tokens for mutations; the UI has its own same-origin token. Web content has no shell/filesystem Tauri capabilities. External HTTP(S) links open in the system browser.

The migrated routes cover setup, deck library, Workshop, pilots and frozen scorecards, history, results/charts, session logs, and stored replay. Deck Lists mount on expansion, logs paginate, replay fetches bounded event pages, the client cache has size/count bounds, and polling pauses in hidden windows. Completed results do not poll the full session. Historical Blazor selection/draft/theme keys are read where compatible; different browser/WebView profiles have separate local storage. Source-rate details, card layouts, charts, and logs use the new components and are not pixel-identical to Blazor. Runtime estimates are advisory and conservatively include cached games; exact reuse counts come from backend preflight.

The shared `branding.json`, `app.css`, and `theme-polish.css` supply theme tokens and compatibility styling. `src/Crucible.Ui/src/spacing.css` and `PageHeader.svelte` define the common layout rhythm. The desktop and browser use the same compact spacing, optical bowl/flame logo, gold navigation states, AI-note disclosure, and four-view Forge Pilots workspace. Deck Lists share `DeckList.svelte` and `deck-list.ts` for Creatures, Non-Creatures, and Lands in ascending mana-value/name order, including setup previews and editable Workshop rows. `CardMana.svelte` and `land-mana.ts` share visible land-production symbols, conditional markers, and explicit fetch-only/nonproducing/unknown states; other cards retain casting costs. Production interpretation uses the displayed face's own abilities and basic land types, with unresolved output kept explicit. See [visual design and Deck Lists](specs/11-visual-design-and-deck-lists.md). The current pilot catalog is supplied by the shared C# policy definitions, independently of the assignment preview filter. Pilot rates retain one decimal place and exact recorded profile/sample identities.

`src/DeckTester` remains available for rollback and engine setup. `MTG_LEGACY_UI=1 ./scripts/run-ui.sh` starts its development frontend. Monitor **Roll Back → Frontend** selects the saved preceding runtime without restarting the backend. Build & Restart publishes and self-checks a new native frontend before any host stop.

## Verification and resource evidence

```sh
npm --prefix src/Crucible.Ui run check
npm --prefix src/Crucible.Ui test
python3 tests/check_ui_host.py --runtime data/monitor/builds/my-svelte-build
swift run --package-path macos/CrucibleMonitor MonitorChecks
```

The native host fixture checks exact JSON forwarding, UI/backend credential isolation, Host/Origin guards, body limits, conditional reads, downloads, SPA routing, presentation metadata, and recoverable backend outages. Client checks cover integer parsing, result denominators/grouping, recorded pilot evidence, legacy drafts, bounded cache retention, and token refresh/idempotency. Monitor fixtures cover native frontend selection and ownership alongside legacy runtimes.

The October 15 isolated desktop measurement attributed shell and WebKit processes using the process delta when relaunching only this shell. Over 25 seconds it measured **300.6 MiB summed RSS and 0.84% summed idle CPU**, including its native UI host (38.0 MiB RSS). Summed physical footprint was 170.6 MiB; RSS and physical footprint are different metrics and shared pages can be counted more than once. The simulation backend and Forge workers are excluded. These are local snapshots, not a temperature or simulation-speed guarantee. Evidence lives in ignored `data/reports/svelte-ui/desktop-resources.json`, with native-host checks and deployment records alongside it.

Browser/desktop verification used a copied SQLite database, separate ports and service lock, stored replay, and a two-game Workshop run. Shell exit preserved both test hosts and their instance identities. The production migration changes the frontend only; the running backend and frozen engine selection are preserved.
