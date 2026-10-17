# Forge and Pyre providers

New run requests select `providerId: "forge"` or `"pyre"`. Omission retains
Forge. Both adapters use the existing RunCoordinator, scheduler, Store, summaries
and charts. The run pins its provider for every slot. Unsupported native requests
fail preflight; switching providers requires a new selection.

`GET /api/v1/providers` reports availability, supported capture, native evidence
modes and registered cohort names. `GET /api/v1/providers/pyre/cohorts/{cohort}/coverage`
resolves exact matching saved deck IDs and the dated pilot selections. Setup disables
decks outside the selected cohort. Configure a local native installation in
`data/pyre-provider.json` with absolute `executable`, its SHA-256 `executableHash`,
`metrics` and `controllerMetrics` definition files, a `cohorts` map of names to
native draft files, and a 32–2048 MiB `workerRssMiB` limit. Registration is local
configuration; no HTTP endpoint accepts executable or draft paths. Do not point
an unverified executable at production data.

Pyre requests select `pyreCohort` and `capture` (`summary` by default, or `full`).
Exactly two frozen lists must match the registered cohort. Native preflight
resolves defaults and the complete typed Module graph. The coordinator retains
the resolved bytes, checksum, installation identity, metric definitions and
exact independently selected pilots. Source/build and seed identities remain in
the native manifest. Existing imported records without an executable run lock
remain readable and cannot dispatch through either engine.

One native worker executes one whole game at a time and reuses only immutable
prepared inputs. Pool initialization publishes its immutable manifest atomically and occurs once
even when slots start together. A worker returns to the pool only after full
result and coordinate validation. Framed requests/responses, handshake identity, attempt
coordinates and output sizes are checked. Timeout and cancellation kill the
isolated worker process tree. A sampled RSS watchdog rejects an over-budget
worker; transient overshoot is possible. Failed attempts remain excluded, with
bounded diagnostics. Recovery never resumes work automatically. Explicit resume
keeps original slots and limits; a last-game commit recovered before run-status
publication remains complete.

Validated raw delivery, definitions and its coordinator slot commit atomically.
Redelivery is idempotent and another slot/attempt cannot claim the result. Legacy
null-provider records keep the explicit Forge reader rule. Caches include
provider and experiment identities. Pyre has no cross-run replay cache or calibrated
UI runtime estimate yet. Summary capture has complete standard metrics and no
replay. Full native traces remain distinct from Forge replay files.

`GET /api/v1/runs/{run}/games/{slot}/evidence` reports native trace availability;
`/pyre-trace` retrieves checksum-verified native evidence. The replay page explains
summary unavailability and offers full trace download. Native playback,
re-simulation and checkpoint tools consume that trace with its pinned executable.
`GET /api/v1/runs/{run}/provider-resources` reports sampled coordinator-plus-worker
RSS and completion counts; native RSS is never labeled JVM heap.

Pyre's chart projection preserves actual game/player turn axes, measured board
counts, signed life, opening observations and the last capture point per
player/turn. Initial and all boundary/terminal samples remain in raw records.
Missing capture is not zero. Rich direct counters and trigger meanings retain
their own definitions; they are not silently relabeled as Forge snapshot metrics.
Extra/skipped-turn transport fixtures establish mapping, not new engine mechanics.

The existing optimizer endpoints accept `providerId: "pyre"` and `pyreCohort`
for fresh typed Aggro Module experiments. Register the local `tools/ProviderRun`
assembly with `runner` and `runnerHash` for this developer workflow. Its dependent
assemblies and configuration are frozen too. Numeric candidates replace only the
target Pilot artifact; decks, Card/Mechanic artifacts and opponent policy stay
fixed. Code revisions and arbitrary features are unsupported. Screening and
holdout keep balanced seed blocks together, pin one finalist, enforce budgets,
retain rejected outcomes and use paired analysis. No model calls occur during play.

The same optimizer queue and execution gate own Forge and Pyre work. A frozen
Pyre backend/content adapter survives later source changes through verified
snapshots. Native JFR and Forge-specific replay/source views report unsupported;
raw native evidence remains available. Production Pyre activation is blocked by
this developer adapter. Tests exercise publication and rollback only in marked
owned disposable roots, including two smoke games through the published bytes.
Published policies are immutable and opt in through `pyrePilotPolicyId` on a new
native run. A default selection or historical run never changes implicitly.

Phase acceptance and private gameplay evidence belong to the separate Pyre
repository. This document and source changes do not deploy hosts, change provider
defaults, or establish general Magic coverage, competitive pilot quality, native
distribution or a cross-engine performance advantage.
