# Gauntlet — local Magic Standard Bo1 simulator

A C#/.NET 10 application with a Blazor Server interface, SQLite storage, and full opposed games resolved by the pinned Forge 2.0.15 engine. Games run locally without language-model calls. The small Java file in `engine/` is the interoperability shim for the existing Java engine; application and interface logic are C#.

## Why Gauntlet?

A gauntlet is both an armored glove and a demanding series of challenges. To **throw down the gauntlet** is to challenge an opponent; to **run the gauntlet** is to face a succession of tests. Gauntlet brings those meanings together: select decks, pit them against one another, and see how each withstands the field. The two-flask logo represents the paired decks under examination.

## Repository contents

This repository contains the application, Forge interop source, assets, and documentation. Local decks, simulation results, replays, downloaded engines, Java runtime, and build outputs are excluded. The source Arena database is separate and must be supplied at `../../standard_cards.sqlite` relative to the project directory, with the frozen public snapshot and saved-deck tables described in the importer. A fresh clone needs that database and the engine setup below before use.

## Start

From this project directory:

```sh
dotnet restore src/MtgSimulator/MtgSimulator.csproj
dotnet run --project src/MtgSimulator/MtgSimulator.csproj -- --setup
dotnet run --project src/MtgSimulator/MtgSimulator.csproj
```

Open [Gauntlet](http://127.0.0.1:5077). Setup downloads Forge and a project-local Temurin Java 17 runtime, verifies published archive checksums when supplied, and compiles the interop shim. Automatic runtime setup supports macOS and Linux. `tar` and the .NET 10 SDK are required.

The default source is `../../standard_cards.sqlite` relative to this project. Set `MTG_PROJECT_ROOT` if launching from another content directory; set `MTG_JAVA` to use another compatible Java executable. The project-local runtime is the default. Stop the app before rerunning engine setup.

## Use

1. Select two to five exact deck versions. Every selected deck plays every other selected deck. The initial library contains eleven public representatives and the separately labeled personal Reanimator snapshot.
2. Choose an even game count per matchup. The default 200 per matchup schedules 2,000 games for five selected decks, evenly split by starting player. Choose your field from the twelve-deck library.
3. Start an exploratory run. Inspect the matrix, play/draw rates, outcome-conditioned turn distributions, and life/board/hand/graveyard development. Later-turn chart samples show their active-game counts.
4. Open a completed game's replay to step through engine events and snapshots. Private hands are hidden unless the audit view is enabled. Export a compact summary or explicitly download a replay.

Mana-starved and mana-flooded incidents are logged per player, with own/actual turn numbers and supporting board/hand counts. Filter games by either flag and jump to the first affected turn in a replay. The run page explains the conservative land-based detection rules; the compact incident log is at `/api/v1/runs/{id}/mana-events`. Flags are observed conditions, not proof of why a player lost. See [the detection specification](docs/specs/08-mana-incident-logging.md).

Each synthetic player's **mulligans before keep** and **kept hand size** appear in game records and replays. Per-deck summaries include mean/median/maximum counts, 0/1/2/3+ frequencies, and play/draw splits. The pilot's keep decision defines an accepted hand; a rules-forced keep is identified separately. Older compatible complete replays are analyzed from their actual mulligan events, with their evidence source disclosed. See [mulligan tracking](docs/specs/09-mulligan-tracking.md).

Start with one worker. Each Java process can use up to 1.2 GB of heap. Startup is included in per-attempt limits and measured completion estimates. Cancelled unfinished slots resume with their original seeds; genuine failed slots require explicit retry. No slot permits more than four attempts. Administrative turn limits, timeouts, crashes, and corrupt replays never count as rules draws.

## Result fidelity

This implementation exposes **exploratory automated-pilot results**. Forge provides actual rules-resolved games; card scripts are present for all six imported lists. Script presence and an end-to-end run do not establish correct resolution of every required interaction or competitive pilot quality.

The user requested no automated tests. No test project or fixture suite has been added. Consequently, the plan's rules/pilot validation gates remain unpassed and validated-result mode is blocked. In particular:

- Forge Default AI has known strategic limitations; combo sequencing and its hidden-information behavior have not been independently audited here.
- London mulligans approximate Arena Bo1 opening-hand behavior; no guessed hand smoother is used.
- Seeded runs record inputs, but independent trace-determinism validation remains outstanding.
- Replays retain hashed full captured-state checkpoints and engine event descriptions. They do not provide a complete normalized legal-decision/payment protocol, object-lineage reconstruction, mana-pool state, or library-order audit.
- Actual player turns and each player's own turns are authoritative. Extra turns are tagged. Exact skipped-turn cycle grouping is unavailable and is not fabricated.
- Legality and the personal deck are dated local snapshots. The app never refreshes account logs, modifies source databases, infers ownership, or treats public rates as simulated outcomes.
- Loss evidence describes the engine's terminal condition; causal explanations remain unknown unless separately established.

## Storage and API

`data/simulator.sqlite` holds immutable manifests, run configuration, slots, and attempt history. `data/replays/` stores compressed completed replays and incomplete attempt evidence. Engine preferences, cache, and ASP.NET protection keys remain under `data/`. Generated data and engine binaries are excluded from Git.

The service binds only to `127.0.0.1:5077`, checks host/origin, and requires a local instance token for API mutations. `/api/v1/session` supplies the token to a same-origin local client. Run creation also requires an `Idempotency-Key`. Ordinary replay API responses omit private hands; `audit=true` explicitly enables them. Summary export labels results exploratory.

See [specifications](docs/specs/README.md), [implementation status](docs/implementation-status.md), and [third-party notices](THIRD_PARTY.md). The specifications describe the intended validated MVP; the status document distinguishes the implemented exploratory app from its outstanding evidence gates.
