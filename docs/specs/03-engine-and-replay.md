# 03 — Engine adapter and replay

Date: October 11, 2026 (America/Los_Angeles)

Status: Proposed. Parent: [MVP specification index](README.md).

## Engine responsibility

The pinned engine resolves priority, phases, casting and payment, targets, triggers, combat, continuous/replacement effects, state-based actions, zones, and game outcomes. The adapter captures evidence and supplies pilots; it does not replace these rules with arithmetic approximations. Human-readable CLI output is diagnostic material, not the sole replay or outcome contract.

The adapter contract below is an app-owned interface to be implemented after feasibility is demonstrated. It is not an assertion that Forge already exposes these commands.

## Process contract

Each worker accepts a versioned JSON request by stdin and emits versioned JSON Lines on stdout. Diagnostic output goes to stderr. Use app-generated paths and IDs. Bundles advertising `workerProtocolVersion=1` reuse initialized JVMs within a session, bounded by its worker count. Older bundles retain a fresh process per attempt. Each JVM handles one game at a time, with a new Match, Game, players/controllers, seeded RNG, attributes and pilot observations. Loaded card resources remain shared. Reset bridge maps, AI caches, coordinated zone-choice state and pinned Forge identity counters between games; no match objects are reused.

The worker transport emits `worker_complete` only after one valid terminal result and state cleanup. Its attempt ID and protocol version must match; it is excluded from the stored replay and game sequence. EOF without this boundary fails a worker attempt. Timeout, cancellation, invalid replay/transport, or an engine error destroys that worker. Successful workers retire after 32 games or a reported used heap of 900 MiB. Session completion, pause/failure and service shutdown release the pool. Each session remains pinned to its immutable adapter/engine identity, keeping historical results and timing calibration separate.

Validate reuse with complete fresh-versus-reused games across matchups, changed seeds/profiles/settings and repeated earlier requests. Compare all normalized records, decisions, terminal outcomes and state hashes; test cancellation, timeout, invalid boundaries, worker replacement, recycling, parallel capacity and historical fallback. `tests/forge_worker_reuse.py` records real-game evidence; `tests/ForgeWorkerChecks` exercises transport/lifecycle faults separately. These focused checks do not establish universal Forge rules or pilot correctness.

Request fields: `protocol_version`, `run_id`, `game_slot_id`, `attempt_id`, engine/adapter revision, two immutable deck manifest references, two pilot profile references, requested starting player, mulligan profile, game seed, pilot seed configuration, and administrative limits.

Response records:

| Record | Required content |
|---|---|
| `ready` | Protocol and engine identity; accepted capability flags |
| `game_started` | Player/deck identities, actual starting player, seed identity, opening policy |
| `event` | Strict sequence number, actual-turn ordinal, active player, phase/step, event kind, normalized payload |
| `snapshot` | State at a named checkpoint, state hash, last included event sequence |
| `game_finished` | Engine outcome, winner if any, terminal evidence, final snapshot, actual turns, duration |
| `attempt_failed` | Typed failure and last complete checkpoint |

Every record includes `attempt_id`. Unknown protocol versions or contradictory starting-player records fail the attempt. One terminal record is allowed. EOF without a valid terminal record is a failure, never an inferred loss.

Randomness must be injectable and recorded for all engine shuffles/random effects and pilot tie-breaking. Define stable seed derivation in the run specification. If the engine uses one global stream, isolate each game and document that implementation. Do not claim separate controllable streams if unavailable. A manifest/engine/pilot/configuration/seed tuple must reproduce normalized decisions and final-state hashes across fresh processes, excluding timing and generated IDs.

## Opening policy

Default MVP profile: ordinary London mulligans with a specified deterministic pilot keep/bottom policy, 20 starting life, one game, and no between-game sideboarding. Record all keep, mulligan, bottom, and pregame choices. Label this profile as an approximation of Arena Bo1 opening-hand behavior. Do not add guessed hand smoothing or calibrate opening hands to observed public win rates.

## State and identity

Use stable per-game object identifiers. Zone changes that create new rules objects must record the new object ID and a lineage link to the physical card, without implying counters, choices, or cast history carry across zones. Identify token creation, copy source, copied characteristics, controller/owner, attachments, counters, damage, tapped state, creature status, and relevant continuous effects.

Snapshots contain both players' life, hand/library counts, zones, permanents, available mana, stack objects and targets, pending relevant choices, active player, phase/step, turn counters, and game status. Store actual library order and hands privately when required for deterministic audit. They are never provided to an opposing pilot. Rules-layer details that cannot be normalized must retain engine evidence; do not invent derived capabilities.

Capture a full initial snapshot, snapshots at turn boundaries and terminal state, and intermediate phase/decision checkpoints sufficient for browsing. Capture events for draws, discards, milling, zone moves, casting routes, actual mana payment, activations, targets, priority passes, trigger ordering, choices, attacks, blocks, damage, life changes, counters, copying, and outcomes.

## Turns and turn cycles

`actual_turn_ordinal` increments when an actual player turn begins. Keep engine-native turn numbers and each player's turn count separately. A turn cycle normally spans the starting player's regular turn and the other player's regular turn. Assign extra turns to their surrounding cycle and label them explicitly. A skipped scheduled turn must be represented as skipped, not as a fabricated played turn. A cycle may be incomplete when a game ends.

Do not derive cycles by blindly dividing actual-turn ordinal by two. The adapter must establish how the engine identifies regular, extra, and skipped scheduled turns. If that distinction cannot be captured, actual turns remain authoritative and cycle grouping must be marked unavailable for those games.

## Replay reconstruction

Store compressed structured events and checkpoints locally. Replay advances from checkpoints through events and never invokes a model. Reconstruction is an evidence viewer, not a new engine implementation. Event application must reproduce checkpoint hashes for all captured fields; unmodeled state can remain an engine snapshot field rather than receiving guessed updates.

The viewer defaults to public battlefield/zone information and each hand's count. An explicit omniscient audit view can reveal private recorded hands and library order after the game. Exports of ordinary summaries contain no private trace data.

Write replay files to an attempt-specific temporary path, validate the terminal record/checksums, and rename to the final path before referencing them in a committed result. Startup reconciliation handles orphan files and interrupted writes. A missing/corrupt replay is surfaced; do not mark replay validation passed for that game.

## Acceptance

Complete opposed games must run headlessly. Requested and actual starting players match. Repeated seeded runs reproduce normalized traces. Replay checkpoint hashes reconstruct. Known cast-versus-return interactions retain correct object history. Technical interruptions produce typed failures, and all pilot decisions obey the information boundary specified in [04](04-automated-pilots.md).
