# Windows desktop beta

Crucible targets **Windows 11 x64** with the same Svelte interface, native .NET hosts, and Java Forge workers used on macOS. Develop on your Mac and let the **Windows Desktop Beta** GitHub Actions workflow build and exercise the Windows app.

**Current evidence:** the Windows implementation and CI workflow are present. Shared installation checks, native Mac host checks, and Windows Rust cross-target checking can run from a Mac. A Windows installer build and installed-app test must pass on the Windows runner before distributing an artifact. Hosted `windows-2025` is a Windows Server image; it does not establish hands-on Windows 11 desktop compatibility. Keep the release labeled **Windows Beta — Automated Windows Validation** after CI passes, and record human Windows 11 installation/display-scaling checks before calling it fully supported.

## Using the beta

After the Windows workflow succeeds, download `crucible-windows-x64-beta` from that run's artifacts and run its `*-setup.exe`. This is a per-user, unsigned beta installer, so publisher/reputation warnings can appear. It is not a signed public release or an automatic updater.

The default program folder is `%LOCALAPPDATA%\Crucible Desktop`. Mutable data lives separately under `%LOCALAPPDATA%\Crucible`:

| Location | Contents |
| --- | --- |
| `catalog.sqlite` | Your writable card catalog, initially seeded from the clean starter |
| `vendor` | Downloaded Forge and a private Temurin Java 17 JDK |
| `data\simulator.sqlite` | Custom deck versions, sessions, attempts, and results |
| `data\engine-bundles` | Immutable engine versions, retained for historical sessions |
| `data\replays` | Saved replay evidence |
| `data\desktop\logs` | Setup and native-host logs |
| `ui-state` | Presentation state such as build names |
| `webview` | The Windows window's local browser profile |

The installer includes the WebView2 bootstrapper. Initial engine setup needs an internet connection; it downloads and verifies Forge and Java, compiles the packaged bridge, and publishes an immutable bundle. Windows 11's built-in `System32\tar.exe` extracts the pinned Forge archive; Java ZIP extraction uses .NET. Users do not install .NET, Node, Rust, Python, or Java separately. Once setup completes, normal simulation use is local.

Closing the window exits Crucible. During active or queued work, choose **Keep Open** or **Stop And Exit**. Completed games and replays remain saved; interrupted work requires explicit resume after reopening. The launcher stops only hosts it started. Externally managed compatible hosts remain externally managed. There is no Windows tray app or launch-at-login setting in this beta.

Setup displays progress and offers retry or access to logs when it fails. A changed packaged bridge is activated before starting an absent backend on the next launch, retaining older engine bundles. Catalog updates never replace an existing user catalog. Close Crucible before upgrading. Uninstall removes the application and retains the separate user-data folder.

## Building from Windows

Build prerequisites are the SDK in `global.json`, Node 24/npm, Python 3 for checks, Rust stable with the MSVC x64 target, and Visual Studio's Desktop development with C++ workload. These are developer tools, not installed-app dependencies.

```powershell
./scripts/build-windows.ps1
```

The script checks the starter catalog and frontend, runs shared installation/worker checks, publishes both hosts using `win-x64` Native AOT, verifies `--check-runtime`, and creates a Tauri NSIS installer. Outputs and SHA-256 sums go to `dist/windows`. Packaged resources are rebuilt explicitly; no Mac checkout path, private database, editorial notes, or historical simulation evidence is included. Existing Mac runtime-pack pins are retained; Windows uses the SDK's standard Native AOT toolchain.

The native backend also supports `CrucibleBackend.exe --setup` using the installed paths. Setup takes the same exclusive service lock as simulations; it cannot run against an active backend.

| Override | Meaning |
| --- | --- |
| `MTG_PROJECT_ROOT` | Writable installation/state root in installed mode; retains its checkout-root meaning in development |
| `MTG_ASSET_ROOT` | Read-only packaged assets; supplied by the Windows launcher |
| `MTG_CARD_DB` | Optional existing catalog path; a missing explicit path is an error |
| `CRUCIBLE_UI_PORT`, `CRUCIBLE_BACKEND_PORT` | Alternate desktop ports for isolated testing |

The default ports remain UI `5077` and backend `5079`, both on loopback. Packaged assets and writable state must be separate folders. The desktop does not expose filesystem or shell IPC to the web UI.

## Starter catalog and provenance

`resources/starter/catalog.sqlite.gz` is a newly generated allowlist export, not a copy of the account database with tables removed. It contains 9,507 card records, 27,067 catalog identities, and 82 frozen public deck lists. The `decks` and `deck_cards` tables are empty compatibility tables. Account information, ownership quantities, personal decks, logs, source JSON blobs, custom annotations, runs, and replays are excluded.

Public lists retain their source URLs, October 11, 2026 snapshot identities, published samples, and rates. These remain dated source evidence, not fresh legality, current metagame claims, or measured local pilot results. Custom card attributes start empty. The manifest records the uncompressed database checksum and per-table counts. Runtime seeding verifies that checksum before atomically installing the catalog.

Regenerate deliberately from an available source catalog and verify before committing:

```sh
python3 scripts/export-starter-catalog.py --source ../../standard_cards.sqlite
python3 scripts/export-starter-catalog.py --verify
```

The exporter reads the source in SQLite read-only mode and writes only explicitly allowed columns into a fresh database. CI requires only the clean committed starter; it has no dependency on your private database.

## Automated checks and release gate

`.github/workflows/windows-beta.yml` runs on pull requests, pushes to `main`/`master`, and manual dispatch. It builds on `windows-2025`, runs backend/frontend checks, builds the per-user installer, and exercises the installed native WebView using `tauri-driver` and a matching Edge driver. It retains installer checksums, screenshots, and logs as artifacts; it does not publish a GitHub release.

Installed-app coverage includes occupied-port refusal, first-launch engine setup without developer tools on PATH, native-host credentials, duplicate launch, clean public decks, Workshop editing, two complete real Forge games, exact 64-bit seeds, results/replay, native close confirmations, explicit recovery, same-version upgrade, and uninstall retention. Rust tests check canonical Windows paths, random ownership credentials, and Job Object containment. The launch gate prevents host subprocess creation until the launcher has assigned the host to its Job Object; closing that job on a crash stops its owned process tree.

The new shutdown interface is `POST /api/v1/host/shutdown`, requiring both the ordinary instance credential and a separate per-launch `X-Crucible-Owner` credential on loopback. Active work returns `409` unless `stopWork=true` follows the user's close confirmation. The browser receives no owner credential. Mac Monitor ownership and close behavior are unchanged.

Mac regression checks include shared installation tests, portable worker lifecycle checks, and pilot identity tests. For deeper local verification with an existing engine, publish isolated native hosts and run:

```sh
python3 tests/check_ui_host.py --runtime <published-ui> --output <evidence-directory>
python3 tests/check_installed_backend.py --runtime <published-backend> --engine-source . --output <evidence-directory>
```

The latter uses disposable state, a clean starter, and read-only engine setup inputs. It verifies real games and guarded shutdown/recovery without changing the live installation's database or active engine pointer.

Windows ARM, the pilot optimizer, automatic updates, code signing, background operation, and launch at login are deferred. Native Windows 11 installation, display scaling, accessibility, and performance still need human testing. Simulation results remain local Forge pilot evidence rather than Arena win-rate forecasts.

## Recorded Mac validation — October 15, 2026

The native UI and backend published successfully on Apple Silicon with no build warnings. The native UI gateway passed its existing transport/security checks plus owner-authenticated graceful shutdown. An isolated installed-backend test seeded the clean catalog, completed two real Forge games with the maximum signed 64-bit root seed, opened replay data, rejected unconfirmed busy shutdown, stopped on the owner request, and reopened with completed results intact and no automatic resume.

Shared starter/download checks, portable worker lifecycle checks, existing host/serialization checks, Svelte checking, and all eight client tests passed. The Windows lifecycle module and its unit tests passed an MSVC-target type check from the Mac using a test context; that is not a Windows executable or installer run. Windows CI execution is pending authorization to push the prepared branch to `PesetTan/crucible`. No Windows release has been published.
