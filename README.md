# Crucible

Crucible is a local **Standard Bo1 deck-testing workspace**. It schedules head-to-head games between exact decklists, then organizes matchup results, turn logs, and replays. **Forge** is the rules engine and automated pilot; Crucible does not simulate Magic rules itself. Results describe Forge pilot games, not expected Arena win rates.

The .NET project is [`src/DeckTester/DeckTester.csproj`](src/DeckTester/DeckTester.csproj). The public repository is [PesetTan/crucible](https://github.com/PesetTan/crucible), and the local workspace is `projects/crucible`. Build the application through `Crucible.slnx` or its .NET project.

## Start

You need the .NET 10 SDK, `tar`, and a local Arena card database at `../../standard_cards.sqlite` relative to this repository. The database, downloaded engine, Java runtime, results, and replays are not in Git.

From the repository root, run setup once, then start the service:

```sh
dotnet restore src/DeckTester/DeckTester.csproj
dotnet run --project src/DeckTester/DeckTester.csproj -- --setup
./scripts/run-service.sh
```

In a second terminal, start the interface:

```sh
./scripts/run-ui.sh
```

Open [Crucible](http://127.0.0.1:5077). The service listens on `127.0.0.1:5079`; the UI listens on `127.0.0.1:5077`. Interface changes need only a UI restart. Keep the service running while games are active. Setup downloads the pinned Forge engine and a project-local Java 17 runtime, then builds the interop shim.

To hot-swap Forge card resources, the jar, or the pilot bridge after editing them, run `./scripts/activate-forge.sh`. It compiles the bridge, copies the engine into an immutable bundle under `data/engine-bundles/`, verifies its hashes, and atomically selects it for new sessions. If more than one `forge-gui-desktop-*-jar-with-dependencies.jar` is in `vendor/`, set `MTG_FORGE_JAR` to the one to activate. Crucible and its simulation service stay running. Each session keeps its original bundle, including after pause and resume; games already in progress finish with that bundle. Old bundles remain on disk so older sessions can resume. A change to Crucible's .NET code still requires restarting its relevant host.

For access from another device on your private network, start the UI with `MTG_LAN_ADDRESS=<this-computer's-private-IPv4> ./scripts/run-ui.sh`. The service stays on loopback. The existing `MTG_*` environment variables remain supported; see [host setup](docs/host-separation.md).

## Use

1. Select two to ten exact deck versions from the library.
2. Choose an even number of games per matchup and two to four parallel games. New runs stagger initial engine starts by one second. Advanced Options includes Forge Performance Mode, which is off by default.
3. Start a run. Compare decks in the matchup matrix, filter game evidence, and open replays to inspect turns and board state.
4. Pause or resume a run without discarding completed games. Compatible completed games can be reused when a later run has the same decks and seed.

Crucible records mulligans, mana incidents, turn samples, attempt status, active runtime, and the Forge Performance Mode used for each session. Browser selections and result filters persist across navigation and refreshes. Cached games and runtime estimates stay separate by performance mode. The UI labels partial data and excludes failed or unfinished games from win rates.

Use **Deck Workshop** to save and compare custom deck versions. **Forge Pilots** shows pilot assignments, programmatic decision checks, and measured win rates from saved local sessions, with sample sizes and links to the underlying evidence. Historical pilot versions and sessions retain their recorded identities.

New worker-capable Forge bundles reuse initialized Java processes within each session, up to its selected parallel-game count. Every game gets fresh match/pilot state and its own seed and replay. Workers are replaced after failures or cancellation, recycled after 32 games or high heap use, and stopped when the session ends. Older pinned bundles continue using their original fresh-process protocol. See [engine and replay contract](docs/specs/03-engine-and-replay.md) for isolation and verification details.

## Read results carefully

Runs are **exploratory**. Forge resolves complete games, but its automated decisions and every card interaction have not been independently validated for competitive accuracy. Arena Bo1 opening hands are approximated without guessing a hand smoother. Public archetype tiers, personal decks, and simulated results have separate sources; none establishes card ownership or an Arena win rate. See [implementation status](docs/implementation-status.md) and the [specifications](docs/specs/README.md) for the detailed limits.

Forge Performance Mode skips selected rules checks inside Forge. Its effect on these decks has not been validated, so compare it as a separate experimental setting. Session details and history show the saved mode; older sessions are treated as normal mode.

## Data and checks

`data/simulator.sqlite` stores deck manifests, sessions, attempts, and results; `data/replays/` stores game evidence. Both are excluded from Git. The UI talks to the local service over HTTP and does not open the database or run Forge itself. See [host separation](docs/host-separation.md) and [third-party notices](THIRD_PARTY.md).

Run focused checks from the repository root:

```sh
dotnet run --project tests/SchedulingChecks/SchedulingChecks.csproj
dotnet run --project tests/HostSeparationChecks/HostSeparationChecks.csproj
dotnet run --project tests/TableSortChecks/TableSortChecks.csproj
dotnet run --project tests/DeleteSessionChecks/DeleteSessionChecks.csproj
python3 tests/check_session_turns.py
```
