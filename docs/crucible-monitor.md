# Crucible Monitor

Crucible Monitor is a native macOS menu bar app for this local checkout. It starts the backend, waits for readiness, then starts the frontend. It registers to open at Mac login, and starts both hosts when it opens. Recovery always requires approval; interrupted simulations still require explicit resume in Crucible.

## Build and open

Requires macOS 13+, Apple's Command Line Tools (`xcode-select --install`), the .NET 10 SDK, and Node 22.12+ for building the Svelte assets. Full Xcode and external Swift packages are unnecessary. Crucible's existing card database, engine setup, Python tools, and checkout are still required for simulations.

```sh
./scripts/package-monitor.sh
mkdir -p "$HOME/Applications"
ditto --norsrc --noextattr 'dist/Crucible Monitor.app' "$HOME/Applications/Crucible Monitor.app"
open "$HOME/Applications/Crucible Monitor.app"
```

The app bundles a Native AOT backend (`CrucibleBackend`) and Svelte UI host (`CrucibleUi`). Both run directly as native executables. Runtime selection also supports preceding JIT/Blazor builds for rollback. See [Native backend](native-backend.md) and [Svelte desktop](svelte-desktop.md) for build commands, measurements, and compatibility details. It copies that payload to an immutable project build directory on first launch, so replacing the app cannot overwrite a running host's files. It is locally ad hoc signed. Install it in `~/Applications` and keep the checkout in a stable location for login startup. The packaging script also creates `dist/Crucible Monitor.zip` for transfer without Finder metadata. Connection Settings includes the actual login-item status and a link to macOS Login Items if system approval is needed. Turning off **Launch at Mac Login** unregisters it.

The panel shows health, active work, last successful checks, and managed versus observed ownership. **Open Crucible** opens the browser. **View Logs** opens the monitor's configuration/log directory. **Advanced** holds connection settings, Build & Restart, and Roll Back. Each update can target the frontend, backend, or both. Backend builds include a native executable self-check before stopping a running host. Build failures leave running components untouched; restart failure retains the previous runtime as an explicit rollback option.

Quitting gracefully stops managed hosts, with confirmation for active or unknown backend work. A graceful-stop timeout requires a second, separate Force Stop approval. Cancelling keeps the app open and ownership tracked. Existing hosts remain observed until **Take Ownership** is selected. Older hosts without the health endpoint and unrelated port owners are never terminated automatically.

The menu bar uses Crucible's bowl-and-flame logo with a subdued bowl. A healthy flame uses the selected frontend runtime's warm yellow accent from `branding.json`; an attention state uses orange. The panel and accessible label provide the health status in text.

## Health and storage

`GET /api/v1/health` returns version 1, `application: crucible`, host role, PID, process start time, non-secret instance identity, project root, readiness, database availability, active/queued run and optimizer counts, `runtimeMode` (`native-aot` or `jit`), and `reflectionJsonEnabled`. Ready hosts return 200; initializing hosts or unavailable backend databases return 503. The frontend endpoint remains healthy independently of backend outages. These probes check host/database readiness, not Forge correctness or simulation quality.

Checks run every five seconds, time out after two seconds, and flag persistent failure after three failures. Startup has a 60-second grace period; wake resets failures with a 15-second grace period. Failure notifications are deduplicated until recovery. If notifications are denied, the panel retains the warning. Intentional stops never cause automatic restart prompts.

Configuration, ownership records, instance lock, and logs live in `~/Library/Application Support/Crucible Monitor`. Log files rotate at 5 MiB, retaining the last 1 MiB in `.previous`. Builds live in the project's ignored `data/monitor/builds/<unique-id>` directories; old builds are retained for rollback. `MTG_UI_STATE_ROOT` keeps UI protection keys and saved build names at `src/DeckTester/ui-state` across published builds. Existing shell-launch defaults remain compatible.

For isolated development launches, `CRUCIBLE_MONITOR_STATE_DIR` overrides app configuration/log storage and `CRUCIBLE_MONITOR_PROJECT_ROOT` supplies the initial checkout. Saved configuration takes precedence. Settings changes require stopping managed hosts first. The monitor never removes the backend service lock, resumes sessions automatically, or modifies engine bundles.

## Checks

```sh
swift run --package-path macos/CrucibleMonitor MonitorChecks
dotnet run --project tests/HostSeparationChecks/HostSeparationChecks.csproj --no-restore
python3 tests/check_monitor_hosts.py --runtime 'dist/Crucible Monitor.app/Contents/Resources/runtime' --backend-runtime 'dist/Crucible Monitor.app/Contents/Resources/runtime/service'
```

Swift checks use an executable assertion runner, so they work with Command Line Tools without XCTest. Lifecycle fixtures use temporary fake hosts; published-host checks use disposable databases and ports. No real simulations are interrupted by these checks.

For a read-only login-registration check, run `~/Applications/'Crucible Monitor.app'/Contents/MacOS/CrucibleMonitor --diagnostics`.
