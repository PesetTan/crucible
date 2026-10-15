# Development and checks

Run commands from the repository root. Use the .NET SDK selected by `global.json`. Forge checks require the project-local engine/Java setup and an available immutable bundle; see [setup](../README.md#start). Python tools use Python 3. The macOS Monitor requires macOS 13+ and Apple's Command Line Tools.

## Project layout

| Path | Responsibility |
| --- | --- |
| `src/DeckTester` | Blazor frontend, setup entry point, and legacy combined host; backend source paths are retained here |
| `src/Crucible.Core` | Shared backend assembly, linked backend sources, endpoints, transport records, and generated JSON metadata |
| `src/Crucible.Service` | Standalone service host and native `CrucibleBackend` publishing |
| `engine/ForgeBridge.java` | Forge interoperability, replay capture, and reviewed pilot hooks |
| `macos/CrucibleMonitor` | Native menu bar supervisor and its check runner |
| `scripts` / `tests` | Publication, diagnostics, fixtures, and matched-game tools |

```sh
dotnet restore Crucible.slnx
dotnet build Crucible.slnx --no-restore
```

UI changes require only a UI rebuild/restart. Service changes need a service restart; pause active work first and resume explicitly afterward. Monitor's component builds publish before stopping the selected host. Do not launch another backend against the live database. See [host separation](host-separation.md) and [Monitor](crucible-monitor.md).

Backend JSON uses `CrucibleJsonContext` and named transport records. Preserve exact serialized identities used for seeds, deck/version fingerprints, cache keys, and historical sessions. Build and run the focused serialization/identity checks before publishing backend changes. [Native backend](native-backend.md) documents AOT publishing and its runtime self-check.

## Focused verification

The .NET checks are executable assertion projects. Restore the chosen project before using `--no-restore`; a solution restore covers the application projects, not every test project. For example:

```sh
dotnet run --project tests/HostSeparationChecks/HostSeparationChecks.csproj
dotnet run --project tests/PilotIdentityChecks/PilotIdentityChecks.csproj
dotnet run --project tests/WorkshopChecks/WorkshopChecks.csproj
```

| Change area | Relevant checks or guide |
| --- | --- |
| Scheduling, sorting, deletion | `tests/SchedulingChecks`, `tests/TableSortChecks`, `tests/DeleteSessionChecks` |
| Service boundary, snapshots, read caches, serialization | `tests/HostSeparationChecks` |
| Workshop and land ordering | `tests/WorkshopChecks`, `tests/LandManaChecks` |
| Profiles, measured rates, scorecards, build names | `tests/PilotIdentityChecks`, `tests/PilotWinRateChecks`, `tests/PilotScorecardChecks`, `tests/BuildNameChecks` |
| Notes and card annotations | `tests/AiNoteChecks`, `tests/CardAttributesChecks` |
| Worker lifecycle and complete-game equivalence | `tests/ForgeWorkerChecks`, `tests/forge_worker_reuse.py`; [engine contract](specs/03-engine-and-replay.md) |
| Pilot decision fixtures | [Programmatic evaluator](pilot-attributes.md), `tests/*PilotChecks.java`, `tests/EngagementProtocolChecks.java`, and `tests/ReanimatorCopyManaChecks.java` |
| Protocol matched comparison | [Engagement Protocols](engagement-protocols.md#inspection-and-checks) and `scripts/verify-engagement-protocols.py` |
| Optimizer | [Pilot Optimizer](pilot-optimizer.md) and its focused Python/smoke checks |
| Native supervisor and published hosts | [Monitor checks](crucible-monitor.md#checks) |

UI changes should also be checked in the running app at desktop and 390px: verify the serving build, controls, wrapping, overflow, and loading/empty/error states. A source diff or successful build does not establish rendered behavior. Existing screenshots in `docs/screenshots/` show the matchup explorer, Workshop, and protocol inspection; refresh them when those surfaces change.

## Windows beta

See [Windows desktop beta](windows-beta.md) for PowerShell builds, starter-catalog provenance, isolated installation checks, and the Windows CI release gate. `tests/WindowsBetaChecks` verifies installed-path behavior and interrupted downloads on Mac and Windows; `tests/ForgeWorkerChecks` now uses a compiled portable transport fixture.

## Data and evidence

`data/`, `vendor/`, compiled bridge classes, published apps, and local UI state are ignored. A fresh clone needs its own catalog and engine setup. Imports read the source catalog; explicit card-attribute tools can append annotations to it. Run fixtures against disposable databases rather than editing historical payloads. Preserve exact deck IDs, seeds, profiles, attributes, protocol/optimizer identities, and engine/adapter hashes in comparisons.

Publish Forge changes with `scripts/activate-forge.sh`; never overwrite an already published bundle. Old sessions need their pinned bundles to resume. Inspect active work and lock ownership before changing service processes, and retain rollback artifacts. The experimental evidence guides describe required frozen inputs and artifact locations; tests and sample counts do not establish Arena performance.
