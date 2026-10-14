# Programmatic pilot attribute evaluation

Run `python3 scripts/evaluate-pilots.py` from the simulator directory. It tests the active immutable Forge bundle without editing the bridge, engine, catalog, decks, sessions, or active bundle. `--bundle <existing-id>` evaluates a historical bundle. Each run saves compiled fixture classes, source hashes and source snapshots, a frozen fixture registry, raw position results, logs, and a scorecard in `data/pilot-evaluations/<id>/`. Only a complete evaluation updates `latest.json`, atomically. Concurrent source changes prevent publication.

The evaluator verifies engine resources and bridge checksums before using the bundle. It includes only Reanimator revisions explicitly recognized in the pinned bridge; staged source revisions are not assumed to be available in a published engine. Fixture metadata is constructed explicitly and frozen by source/inventory hashes. These scores do not describe the current catalog annotations, a particular saved deck, complete opposed games, or Arena play.

Forge Pilots (`/pilots`) and the read-only `GET /api/v1/pilot-scorecards` endpoint show exact-profile results, test counts, behavior source, scope, individual expected/observed decisions, errors, settings, and build identity. Refresh reloads the latest completed report. A different active engine/bridge or modified evaluation sources marks the report historical. A checksum mismatch shows an error instead of scores. An unsupported or unevaluated profile remains unmeasured; results are never copied from another revision.

Each metric's View code button opens the source saved with that evaluation. Position definitions, scoring calculation, and applicable engine fixtures are available with line numbers and a focused excerpt or full-file view. Metrics/Code controls retain the profile and selected metric; Back to metrics returns to the measurements. The code view is read-only and checks its source hash. It does not misrepresent the current editable file as the evaluated source.

Pilot implementation source is exposed only when compiling its frozen snapshot reproduces every class in the tested bridge byte-for-byte. If the workspace implementation differs from the pinned bundle, it is omitted. The inherited Forge implementation stays identified by the pinned jar and base profile; the bridge source shows where inheritance is configured, rather than claiming to contain Forge's combat implementation.

## Attribute definitions

| Attribute | Measurement | Limits |
|---|---|---|
| Aggression | Whether the actual inherited Forge controller attacks in one equal-trade position | A tendency observation, not a correct/incorrect judgment or a population estimate |
| Combat judgment | Acceptable decisions in unblocked, immediate-lethal, and losing-attack positions | Inherited Forge; no blocker-selection rating |
| Resource management | Applicable payment/copy gates, unpaid fallbacks, or friendly-casualty preservation | Fixed reviewed hooks; general land sequencing and mulligans remain unmeasured |
| Interaction timing | Applicable burn, public sweeper/exile, or own-main permanent development | Hook-specific timing and targeting; not general removal/counter/protection expertise |
| Combo choices | Actual printed copier choice for cast versus uncast payoff/value | Reanimator only; full combo execution remains unmeasured |
| Adaptation | Public opponent classification for unknown, aggro, and control evidence | Reanimator policy guards; not a complete-game adaptation rating |

Each decision-check percentage is passed / (passed + failed) × 100. Evaluation exceptions are shown separately and excluded from that denominator. An attribute with no completed checks has a null percentage and reads Unmeasured. Aggression uses attack observations / completed observations instead. The suite seed is fixed and reset before each position. Small curated suites are diagnostic, never calibrated skill ratings; no overall or radar score is generated.

Revisions face the same desired decisions. In the copy-payment positions, selecting an inactive entry-effect copy over the configured useful alternative is a failed value-choice check, including on legacy v9. A legacy compatibility assertion that merely preserves that choice is not an ability score. This suite can reveal that bounded weakness without changing legacy behavior.

The engine controller is called directly for inherited attack decisions, exact-profile Control targets/discards and copier choices. Aggro and subtype tests exercise the actual engine-guarded hooks. Adaptation tests exercise classifier policy guards. Existing Aggro/Control fixture helpers retain their original entry points and additionally accept exact profile IDs so inherited hooks can be checked under each subtype. These positions do not change or stand in for complete game outcomes.

All profiles inherit the resolved Default AI settings from the tested bundle. Configured chances and thresholds are displayed separately from measurements. Scoring a live deck or adding broad combat, sequencing, interaction and full combo execution coverage requires additional defined positions and complete-game evidence; unknown coverage must stay explicit.
