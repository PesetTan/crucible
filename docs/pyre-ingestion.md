# Pyre Spark ingestion v1

This is an offline MVP 1 consumer of actual `spark-result-v1` native output. It
uses the existing Store and analysis models. It does not launch a host or a game
engine and does not make Pyre selectable through the coordinator.

Build `tools/PyreImport/PyreImport.csproj` with .NET 10, then invoke:

```sh
dotnet tools/PyreImport/bin/Debug/net10.0/PyreImport.dll \
  --root /absolute/path/to/isolated-crucible-root \
  --result /absolute/path/to/result.json \
  --metrics /absolute/path/to/pyre/kernel/metric-definitions.json \
  --controller-metrics /absolute/path/to/pyre/spark/controller-metrics.json
```

The root must be a caller-owned Crucible root with `docs/specs` present. Acceptance
uses new temporary roots, never the live service or Arena database. A JSONL batch
can replace `--result FILE` with `--results-jsonl FILE`. The tool validates the
whole input before opening storage, then commits each record atomically. A batch
failure can leave earlier committed records; replaying the same file is idempotent.
Its timing includes validation, Store initialization and durable commits. It emits
counts, duplicates and elapsed time; it does not delete an existing store.

The importer checks versioned definition hashes, canonical payload/manifest/slot/
attempt/record identities, frozen deck provenance, raw/projected metric agreement,
controller identity and failure flags, outcomes, turn axes and capture status.
Raw JSON and exact definitions remain retained. Checksums establish consistency,
not publisher authentication. Only the supported v1 metric semantics are mapped.

Each experiment is a provider-pinned run. Paired slots share seed identity across
experiments; their global Store IDs also include experiment identity. Attempts
retain their immutable raw records. One metric-complete clean rules terminal is
accepted per experiment/slot. Duplicate delivery adds nothing; conflicting payloads
or a second accepted terminal fail. A pilot-failed/stopped/technical result remains
excluded. A later valid retry can fill that slot; late failures preserve the
accepted result and add attempt history.

Existing summaries consume winner/draw, starter and denominators. Turn charts map
actual game/player turns and measured life, hand, library, graveyard, lands,
creatures/power, artifacts, enchantments and hand land/nonland values. The legacy
chart has one row per player/turn, so it takes the last actual capture point;
all initial/boundary/terminal points remain in raw evidence. Mulligan count and
kept size are actual opening measurements. Policy acceptance remains unknown.
Direct counters with richer meanings are retained rather than relabeled as Forge
snapshot counters. Summary results have no replay; full-trace artifact retrieval
is outside this importer.

Legacy records with no provider field read as Forge, with no serialized shape
change. Cache compatibility includes provider and Pyre experiment. Forge dispatch,
resume and startup recovery cannot execute imported Pyre records. Integer legacy
seed fields are explicitly versioned labels; the authoritative full Pyre stream
identity is retained in raw manifests. Deleting a run removes its raw delivery
records in the same transaction; retained definitions may be reused.

`tests/PyreImportChecks` exercises real Pyre records, existing analysis/charts,
idempotence, conflicts, balanced starters, global experiment isolation, failure/
retry/late-delivery behavior, dispatch guards and deletion/reimport. Synthetic
rehashed delivery fixtures are labeled in the test and are not gameplay samples.
Selectable providers, UI discovery, worker recovery, optimization and complete
provider conformance remain later Convergence work.
