# Pilot Optimizer

The local CLI and service APIs optimize existing Forge pilots through registered numeric priorities and bounded code revisions. They run real opposed games without model calls. GPT reviews compact evidence and relevant source excerpts; it does not score or generate game outcomes.

```sh
python3 scripts/pilot-optimizer.py inspect
python3 scripts/pilot-optimizer.py create --profile reanimator-v011
python3 scripts/pilot-optimizer.py start EXPERIMENT_ID --screen-only
python3 scripts/pilot-optimizer.py status EXPERIMENT_ID
python3 scripts/pilot-optimizer.py evidence EXPERIMENT_ID
python3 scripts/pilot-optimizer.py resume EXPERIMENT_ID --no-search
python3 scripts/pilot-optimizer.py report EXPERIMENT_ID
```

The current Reanimator optimizer target is the single `reanimator-v011` profile across library variants. Historical experiments retain their pinned profiles and backend.

Defaults are two hours of cumulative local subprocess computation, three code revisions, 12 candidates, 10 screening pairs per candidate and 40 distinct holdout pairs for one finalist. Starts are balanced, execution order alternates, and opponents are stratified by persisted strategy/subtype. The deterministic parameter sweep reserves capacity for code revisions. `--no-search` uses only staged candidates. Code revisions require a focused `OptimizerCandidateChecks.java` snapshot via `stage --fixtures`.

Experiments freeze deck versions, annotations, opponent policies, engine resources, source/fixture hashes and actual seeds. Source runs can supply historical identities and seeds; outcomes are recomputed against a fresh matched baseline. Mirrors need a separate workflow. Missing engine cards block creation. The service shares an execution gate with ordinary sessions, runs experiment games serially, and persists checkpoints/results. Pause, service interruption and budget exhaustion preserve completed evidence. Experiment actions use the original verified backend snapshot after upgrades.

Candidates must preserve legacy diagnostic decisions and pass existing guards plus any focused change fixtures. A worse holdout win/draw/loss outcome rejects the candidate. Technical failures and incomplete games never become losses. Default-sized completed comparisons are required for promotion; small smoke samples stay diagnostic. Zero observed regressions is a finite-sample guard, not general non-inferiority or an Arena forecast.

`profile EXPERIMENT_ID --candidate CANDIDATE_ID` uses bundled Java Flight Recorder and returns sampled hotspots. Raw JVM output is retained; known JFR startup messages are isolated before strict replay validation. Profiled games are excluded from timing rankings and comparisons.

`promote` is explicit. It audits raw game and fixture evidence, creates a checksummed immutable bundle, runs two smoke games against that bundle, then switches the active pointer. Failed promotion restores the previous pointer. The returned policy is opt-in through `optimizerPolicyId` in new run/preflight requests; legacy defaults and saved sessions remain unchanged. Published policy identity separates cache/session reuse and default timing samples.

APIs live at `/api/v1/pilot-optimizations` on service port 5079: discovery, creation, candidate staging, start/pause/resume, status/report, bounded evidence/detail/source, profiling and promotion. Mutations require the existing instance token; creation requires an idempotency key. No API accepts arbitrary shell commands or artifact paths. New `pilot_optimizer_*` tables are additive; artifacts are under `data/pilot-optimizations/<id>/`.

Validation commands:

```sh
dotnet build src/DeckTester/DeckTester.csproj --no-restore
dotnet run --project tests/PilotIdentityChecks/PilotIdentityChecks.csproj --no-restore
python3 -m unittest discover -s tests -p 'test_pilot_optimizer.py'
python3 tests/smoke_pilot_optimizer.py
python3 tests/smoke_optimizer_tuning.py
```

The real smoke checks use small diagnostic schedules and never promote. The installed `$pilot-optimizer` skill supplies the operating workflow and focused references. Runtime scripts use Python's standard library, SQLite and bundled Java; no paid external tools or model API keys are needed. GPT activity uses the existing Codex allowance; local elapsed hours do not establish its token or dollar cost.

## Profile Discovery and Compiled Protocol Catalogs

Fresh experiments may target an exact broad Aggro/Control assignment even when the same deck also has a subtype profile. `inspect` exposes `targetProfiles`; choosing the target changes only that side, while opponent profiles retain their discovered subtype assignments. Source-run experiments continue to pin their original profiles. Hybrid strategy labels are not substring matches.

When compiled bridge classes support `--protocol-catalog`, the backend saves their generated catalog and its file hash with the candidate. Forge Pilots can inspect this inactive catalog without publication. Replay evidence caches are partitioned by exact profile as well as replay hash. See [the non-Reanimator protocol evaluation](engagement-protocols-all-pilots.md) for the practical skill test, batch computation cap and completion report.

Catalog-aware publication copies that verified candidate catalog with its classes and rejects a pre-existing immutable bundle with different definitions. Frozen experiments created before this support cannot promote a changed catalog through their older publisher; the fixed entry point blocks that stale-catalog path without rewriting their evidence. Catalog-preserving legacy promotions retain their existing behavior.
