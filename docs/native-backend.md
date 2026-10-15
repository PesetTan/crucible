# Native backend

Crucible's backend remains C# and publishes to a native macOS executable using .NET Native AOT. The Blazor frontend still runs on .NET, and Forge rules execution remains in Java workers. Native compilation reduces coordinator memory and startup costs; simulation CPU use still depends on worker count and game complexity.

## Projects and compatibility

- `Crucible.Core` shares backend models, storage, endpoints, scheduling, and engine integration. It links existing backend source files under `src/DeckTester` to keep their paths stable.
- `Crucible.Service` is the small backend host. It has no Blazor dependency, uses workstation GC, and disables reflection-based JSON serialization.
- `DeckTester` retains the frontend and legacy combined/service modes for development and rollback.

`CrucibleJsonContext` supplies generated serialization metadata. Named transport records preserve existing JSON shapes and property order, including deck, seed, cache, and workshop fingerprints. Compatibility checks compare exact serialized bytes. Native health reports `runtimeMode: native-aot` and `reflectionJsonEnabled: false`.

## Build and run

Use the SDK selected by `global.json` and Apple's Command Line Tools. On Apple Silicon:

```sh
dotnet publish src/Crucible.Service/Crucible.Service.csproj -c Release -r osx-arm64 -o data/monitor/builds/my-native/service --disable-build-servers
data/monitor/builds/my-native/service/CrucibleBackend --check-runtime
```

Use `osx-x64` on Intel. The self-check must print `{"id":"native-aot"}`. The project pins Microsoft's Native AOT runtime pack to 10.0.11, matching this checkout's compiler runtime; review that pin when updating the SDK. This avoids linking the Homebrew SDK's optional OpenSSL/Brotli dylibs. SQLite's native library is shipped alongside the executable.

Keep the executable name `CrucibleBackend`, without a `.Service` suffix. A same-binary launch test on this Mac failed before entry when the executable was named `Crucible.Service` and succeeded when renamed extensionless.

Prefer Crucible Monitor for production launches. It supplies the project root, port, tool paths, and persistent UI state directory, verifies ownership, and prevents a second backend against the same database. `scripts/run-service.sh` uses `dotnet run` for development and therefore runs JIT code. A direct native launch needs `MTG_PROJECT_ROOT` and the same tool configuration; stop the existing backend through the monitor first.

The monitor's **Build & Restart → Backend** publishes and self-checks the native executable before stopping the current backend. Its runtime selector recognizes native, extracted JIT, and legacy combined outputs. **Roll Back → Backend** retains compatibility with the preceding runtime. Updating only the backend leaves the frontend running.

## Recorded local comparison

The final performance evidence is `data/reports/native-aot/performance/summary.json` (ignored local artifacts, not distributed with Git). A frozen database held 36 runs, 14,628 slots, and 67 decks. Both extracted hosts used the same source, generated JSON, GC setting, data, and immutable engine bundle. The final serial comparison had no compiler running and sampled each idle host for 25 seconds after loading API state. Earlier samples in the report root are separate and should not be substituted for these final measurements.

| Measurement | JIT backend | Native backend |
| --- | ---: | ---: |
| Ready after launch | 1.34 s | 1.08 s |
| Cold resident memory | 166.9 MiB | 81.1 MiB |
| Resident memory after API reads | 199.6 MiB | 127.9 MiB |
| Idle process CPU in sample | 1.92% | 0.04% |

Memory after reads fell 36%; startup fell 19% in this sample. These are local observations, not guarantees about temperature or game throughput. The earlier conditional-read/cache change separately removed the high idle polling load.

Read endpoints, conditional 304 responses, local API guards, and disposable note/deck/card-attribute mutations passed; the mutation evidence is in `data/reports/native-aot/api-contract/summary.json`. The separate `paired-repeat/summary.json` and its normalized replays record two matched complete games per backend with identical game results, 953 replay records, and 657 state checkpoints. Replay comparison removes only per-attempt IDs and the terminal elapsed-time diagnostic. Historical run and slot payload hashes remained unchanged in fixture startup checks.

To repeat the startup, API-read, and idle measurements, publish into two separate output folders. The script copies databases into disposable fixture roots:

```sh
dotnet publish src/Crucible.Service/Crucible.Service.csproj -c Release -p:PublishAot=false -p:UseAppHost=false -o data/monitor/builds/my-jit --disable-build-servers
python3 scripts/benchmark-service.py --jit-runtime data/monitor/builds/my-jit --native-runtime data/monitor/builds/my-native/service --output data/reports/native-aot/my-comparison --idle-seconds 25
```

The matched-game and guarded-mutation evidence above is a separate recorded validation; this measurement command does not repeat those games. The deployment record is `data/reports/native-aot/deployed.json`. It records a native backend, an unchanged frontend and active engine pointer, and preserved historical payloads. Use `/api/v1/health` to inspect the currently running runtime rather than inferring it from the selected project or an old report.
