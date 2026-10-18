# Crucible

Crucible is a local **Standard Bo1 deck-testing workspace**. It schedules head-to-head games between exact decklists, then organizes matchup results, turn logs, and replays. **Forge** is the rules engine and automated pilot; Crucible does not simulate Magic rules itself. Results describe Forge pilot games, not expected Arena win rates.

The interface is Svelte + TypeScript (`Crucible.Ui`), served by a small Native AOT host (`Crucible.UiHost`) and available in a Tauri desktop window or a self-hosted browser. The [solution](Crucible.slnx) also contains the shared backend (`Crucible.Core`), standalone backend host (`Crucible.Service`), and retained Blazor implementation (`DeckTester`) for rollback. Forge games run in Java workers. See [desktop and UI setup](docs/svelte-desktop.md) and the [documentation index](docs/README.md).

## License

Copyright (C) 2026 Crucible contributors.

Crucible's original source code, documentation, and original assets are free software: you can redistribute and/or modify them under the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version (**GPL-3.0-or-later**). Crucible is distributed without any warranty; see [LICENSE](LICENSE) for the full terms.

Third-party software and third-party content retain their own licenses and rights. This grant does not cover Wizards of the Coast card text, card data, artwork, trademarks, or third-party deck/source material, including such content in the starter catalog, examples, and screenshots. Crucible is unofficial and is not approved, endorsed, or sponsored by Wizards of the Coast. Portions of the materials used are property of Wizards of the Coast. © Wizards of the Coast LLC.

See [third-party notices](THIRD_PARTY.md) and the [license review and distribution limits](docs/license-review.md). The software license does not establish permission to redistribute the bundled card catalog. Optional source packs are private inputs; see [private data and public source export](docs/private-data.md).

Source deck lists, published statistics, tier snapshots, and source-derived screenshots are supplied separately from this source tree. Existing Git history and old release artifacts may still contain earlier bundles. Use the [public source export](docs/private-data.md#public-source-export) for a new publication without that history. Card-content and binary-distribution limits remain recorded in the [license review](docs/license-review.md).

## Supported operating systems

**macOS on Apple Silicon is the currently supported and verified host platform.** The desktop app requires macOS 13 or later. Platform status refers to the current Svelte/Tauri and Native AOT implementation:

| Host platform | Current status |
| --- | --- |
| **macOS 13+ · Apple Silicon (arm64)** | Supported. Native desktop, self-hosted browser UI, simulation backend, and Crucible Monitor have been built and tested locally. |
| **macOS 13+ · Intel (x64)** | Source-build target exists, but has not been validated on an Intel Mac. |
| **Linux · x64 / arm64** | Experimental self-hosting. Automatic Forge/Java setup supports Linux; the current UI/backend combination and desktop packaging have not been validated there. |
| **Windows 11 · x64** | Desktop beta implementation and Windows CI pipeline are available. The first Windows installer/installed-app validation run is pending; human Windows 11 verification remains required. See [Windows beta](docs/windows-beta.md). |

Phones, tablets, and other computers can use the self-hosted browser UI over a private network; simulations run on the host computer. There is no native iOS/iPadOS or Android app. Crucible Monitor is macOS-only. Desktop packaging produces a local macOS build and has a Windows beta installer workflow; Linux desktop releases are not provided. See [build prerequisites and platform limits](docs/svelte-desktop.md#build-and-run).

The [release pipeline](docs/releases.md) validates version tags, runs the complete automated checks, and assembles Windows beta installers with matching source, checksums, and provenance. Successful tag builds create a draft prerelease for distribution review.

## Interface

**Native desktop** — The Svelte/Tauri interface keeps Crucible's graphite, gold, and silver theme and measured pilot evidence.

**Matchup explorer** — Compare head-to-head results across a ten-deck field, including per-deck averages. The rates shown describe local Forge pilot games.

**Deck Workshop** — Edit card quantities, preview assigned pilots, and configure a matchup rerun.

**Forge Pilots** — Browse pilot profiles and inspect Reanimator's opt-in Engagement Protocols, driven by public opponent observations.

## Start

You need the .NET 10 SDK selected by [`global.json`](global.json), Node 22.12+ for building the interface, `tar`, and a local Arena card database at `../../standard_cards.sqlite` relative to this repository. Automatic engine setup supports macOS, Linux, and the Windows desktop beta and needs network access for its initial downloads. The database, downloaded engine, Java runtime, results, and replays are not in Git. A development clone alone does not include your local card catalog or account data. Windows desktop packages include a separate card-only starter catalog and an empty deck library; see [Windows setup and validation](docs/windows-beta.md).

The owner can obtain a private catalog snapshot from [static-data](docs/static-data.md), including Codex Cloud setup through `MTG_CARD_DB`. The existing local catalog stays at its current path.

From the repository root, run setup once, then start the service:

```sh
dotnet restore Crucible.slnx
dotnet run --project src/DeckTester/DeckTester.csproj -- --setup
./scripts/run-service.sh
```

In a second terminal, start the interface:

```sh
./scripts/run-ui.sh
```

Open [Crucible](http://127.0.0.1:5077). The service listens on `127.0.0.1:5079`; the UI listens on `127.0.0.1:5077`. Interface changes need only a UI restart. Keep the service running while games are active. Setup downloads the pinned Forge engine and a project-local Java 17 runtime, then builds the interop shim.

On macOS, [Crucible Monitor](docs/crucible-monitor.md) provides a native menu bar app that starts both hosts, reports health, requests recovery approval, and opens at login. Build it with `./scripts/package-monitor.sh`, then install the generated app in your Applications folder using the linked instructions.

The monitor runs both hosts as native .NET AOT executables. The [Tauri desktop app](docs/svelte-desktop.md) uses the system WebView, adopts verified native hosts, and can start bundled hosts when absent. Node and Rust are build tools only. Forge JVM workers still perform simulations. Shell launch scripts use JIT development hosts. Use one service owner at a time; the service ownership lock prevents two backends from sharing the same data directory.

To hot-swap Forge card resources, the jar, or the pilot bridge after editing them, run `./scripts/activate-forge.sh`. It compiles the bridge, copies the engine into an immutable bundle under `data/engine-bundles/`, verifies its hashes, and atomically selects it for new sessions. If more than one `forge-gui-desktop-*-jar-with-dependencies.jar` is in `vendor/`, set `MTG_FORGE_JAR` to the one to activate. Crucible and its simulation service stay running. Each session keeps its original bundle, including after pause and resume; games already in progress finish with that bundle. Old bundles remain on disk so older sessions can resume. A change to Crucible's .NET code still requires restarting its relevant host.

For access from another device on your private network, start the UI with `MTG_LAN_ADDRESS=<this-computer's-private-IPv4> ./scripts/run-ui.sh`. The service stays on loopback. The existing `MTG_*` environment variables remain supported; see [host setup](docs/host-separation.md).

## Use

1. Select two to ten exact deck versions from the library.
2. Choose an even number of games per matchup and two to four parallel games. New runs stagger initial engine starts by one second. Advanced Options includes Forge Performance Mode, which is off by default.
3. Start a run. Compare decks in the Field matrix or By deck view, filter game evidence, and open replays to inspect turns and board state. Ten decks produce 45 matchups and 9,000 scheduled games at the default 200 games per matchup.
4. Pause or resume a run without discarding completed games. Compatible completed games can be reused when a later run has the same decks and seed.

Crucible records mulligans, mana incidents, turn samples, attempt status, active runtime, and the Forge Performance Mode used for each session. Browser selections and result filters persist across navigation and refreshes. Cached games and runtime estimates stay separate by performance mode. The UI labels partial data and excludes failed or unfinished games from win rates.

Use **Deck Workshop** to save and compare custom deck versions. **Forge Pilots** shows pilot assignments, programmatic decision checks, and measured win rates from saved local sessions, with sample sizes and links to the underlying evidence. Historical pilot versions and sessions retain their recorded identities.

The default policy combines Aggro v001, Control v1, unified Reanimator v011, and Forge Default for other decks. Advanced Options offers **Standard + Engagement Protocols (Preview)**, which assigns Reanimator v012 and its five public-observation protocols, and an opt-in subtype policy with 30 additional profiles. The assignment selector in Forge Pilots only previews policies; choose the run policy in setup. [Engagement Protocols](docs/engagement-protocols.md), [subtype pilots](docs/subtype-pilots.md), and the [pilot requirements](docs/specs/04-automated-pilots.md) describe the bounded behavior and evidence.

Saved [AI notes](docs/ai-notes.md) provide attributed editorial commentary where available. They are authored records; simulations and aggregation make no model calls. The [Pilot Optimizer](docs/pilot-optimizer.md) is a separate CLI/API workflow for frozen experiments and explicit opt-in promotion.

New worker-capable Forge bundles reuse initialized Java processes within each session, up to its selected parallel-game count. Every game gets fresh match/pilot state and its own seed and replay. Workers are replaced after failures or cancellation, recycled after 32 games or high heap use, and stopped when the session ends. Older pinned bundles continue using their original fresh-process protocol. See [engine and replay contract](docs/specs/03-engine-and-replay.md) for isolation and verification details.

## Read results carefully

Runs are **exploratory**. Forge resolves complete games, but its automated decisions and every card interaction have not been independently validated for competitive accuracy. Arena Bo1 opening hands are approximated without guessing a hand smoother. Public archetype tiers, personal decks, and simulated results have separate sources; none establishes card ownership or an Arena win rate. See [implementation status](docs/implementation-status.md) and the [specifications](docs/specs/README.md) for the detailed limits.

Forge Performance Mode skips selected rules checks inside Forge. Its effect on these decks has not been validated, so compare it as a separate experimental setting. Session details and history show the saved mode; older sessions are treated as normal mode.

## Data and checks

`data/simulator.sqlite` stores deck manifests, sessions, attempts, results, and editorial notes; `data/replays/` stores game evidence. Both are excluded from Git. Simulation data and notes go through the service API; the frontend reads saved pilot scorecards and persists display aliases separately. It does not schedule games or run Forge. The backend generates summary and replay exports, available through the UI proxy. See [host separation](docs/host-separation.md) and [third-party notices](THIRD_PARTY.md).

Run focused checks from the repository root:

```sh
dotnet run --project tests/SchedulingChecks/SchedulingChecks.csproj
dotnet run --project tests/HostSeparationChecks/HostSeparationChecks.csproj
dotnet run --project tests/TableSortChecks/TableSortChecks.csproj
dotnet run --project tests/DeleteSessionChecks/DeleteSessionChecks.csproj
python3 tests/check_session_turns.py
```

The [development guide](docs/development.md) maps the additional Workshop, pilot, worker, serialization, note, and Monitor checks to their prerequisites. [Implementation status](docs/implementation-status.md) separates implemented features and recorded checks from the remaining validation gates.

Pyre Spark offline result ingestion is documented in [docs/pyre-ingestion.md](docs/pyre-ingestion.md).
