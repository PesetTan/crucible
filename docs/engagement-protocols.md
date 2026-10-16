# Engagement Protocols

The opt-in **Standard + Engagement Protocols (Preview)** mode assigns `reanimator-v012` to exact, case-insensitive Reanimator subtypes. Other assignments match Standard; Standard itself stays on `reanimator-v011`. Historical profiles, sessions and immutable bundles remain executable.

## Declarative rules

`ForgeBridge.EngagementProtocols` is the shared evaluator. Its inputs are immutable `DecisionContext` and `CandidateFeatures` records, with no Forge objects. A pilot registry supplies typed `ProtocolDefinition` entries. Each entry declares an activation predicate, ordered priority, feature preferences and optional delay allowance. The evaluator ranks candidates lexicographically by active protocol priority, then by the existing pilot score; equal ranks preserve offered order.

The five Reanimator definitions are Standard Execution, Counterplay, Graveyard Security, Stabilize and Race. Visible interference takes precedence over survival, racing, discretionary counterplay and ordinary priorities. Race and urgency disable optional counterplay delays. Adding a protocol using existing observations/features requires another table entry and fixtures, without changes to the evaluator. New mechanics require reviewed observation/choice adapters, not a matchup-specific evaluator branch.

`PROTOCOL_SETTINGS` centralizes low life (8), maximum delay (1 own turn per card instance), and hand search bound (24). Priorities and feature preferences live in the same typed table. Explanations and the catalog are generated from that table with `ForgeBridge --protocol-catalog`; publication saves the catalog beside the compiled bridge. Configuration changes require a new immutable bundle. Run provenance saves the catalog/version/hash; session and SQL cache compatibility compare its hash as well as the profile and adapter identity.

## Observation and execution boundaries

Opponent assessment begins unknown. Observations use cast history and face-up public zones. They never enumerate the scheduled opponent's manifest or concealed cards. Annotation lookup for opponent permanents follows an already-visible card. Counterplay requires an observed ordinary counter route with compatible spell restrictions and sufficient untapped basic-land mana. Conditional/nonbasic mana, alternative counter costs, complex counter shapes and unsupported target restrictions do not justify waiting. These limits can miss risks; they are not a claim that the opponent cannot counter.

Board pressure is deliberately conservative: life at most 8 with a visible opposing creature, or visible opposing creature power totaling at least current life. It is not a lethal prediction.

Casting, tutor, discard, recovery and copy adapters describe reviewed choices using shared features. Existing copy-payment gates, mandatory discard counts, early-land protection and annotated sole-copier preservation remain authoritative. Forge still supplies timing, payment, targets, resolution and outcomes. The bounded wait records the first deferred own turn per card; repeated priority calls and protocol changes cannot renew it. Per-game memory is discarded between persistent-worker games. Unsupported actions, combat, mulligans and general protection sequencing retain inherited behavior.

## Inspection and checks

Forge Pilots has a visible Engagement Protocols section above the win-rate table, including under Standard assignments. It shows the public-observation flow and each protocol's activation condition and response in priority order, using the active bundle's definitions. **Preview protocol assignments** switches only this workspace's assignment preview; select the policy in Deck Tester to use it for a new run. The Reanimator v012 profile links back to its protocol set. Cards stack into one column on narrow screens.

Saved-run details reuse the same protocol cards with the run's frozen catalog, and replay records expose public protocol transitions, active IDs and bounded-delay decisions. Private offered choices retain the replay audit boundary. Old records have no implied protocol configuration.

`tests/EngagementProtocolChecks.java` covers table extensibility, composition, mana evidence, cast timing/payment, deferral expiry, real tutor/copy/discard choices, hidden-card/metadata invariance and worker reset. Existing Reanimator copy-payment, Aggro, Control, subtype and .NET identity/cache suites remain required.

`scripts/verify-engagement-protocols.py --freeze` freezes 48 schedules: three Reanimator variants against counters, aggression, combo and graveyard interference, two seeds per starting position. Running without `--freeze` performs 96 complete serial games across fresh baseline and candidate arms. It retains exact requests, hashes, checkpoints, terminal results and paired gains/regressions. `CRUCIBLE_PROTOCOL_REPORT` selects an isolated report directory. Class hashes are checked before and after execution. Preliminary or interrupted cohorts must remain separate from the final comparison.

This is an exploratory protocol implementation. Its focused information-boundary checks do not validate all inherited Forge behavior or establish Arena matchup rates.

## Release evidence

The final comparison is in `data/reports/engagement-protocols-v1-release/`: 48 paired schedules and 96 complete serial executions, with 29 baseline wins and 32 candidate wins (four gains, one regression, 43 unchanged outcomes). All 36 original run payloads remained unchanged. The declaration-driven registry/delay refinement preserved all 32,043 candidate state checkpoints across the same 48 schedules; its final production classes were frozen and rerun rather than inferred equivalent.

There are 64 focused protocol checks, 60 real-engine copy/payment positions and 215 subtype positions, plus the existing Reanimator, Aggro, Control and .NET assignment/cache regressions. The release audit checks selected casts against actual subsequent engine events. Graveyard Security is exercised in focused fixtures; the sampled complete games did not activate it, so the cohort does not independently establish that protocol's matchup effect.

The published bundle is `656092f7e33fb10bd16be765879d566e5c9800f0413293a8436249004f59dc43-789bc5954985e11317705ff87f621f2f7a5a2411d0536dafcadb4d33a5f99acf`. Workspace descriptions, saved-run provenance and replay transitions were inspected in the running UI. At 390px the document is also 390px wide and the protocol list uses one column. The default policy remains unchanged.

## Non-Reanimator optimizer candidates

Inactive protocol candidates now register broad Aggro/Control and all 30 current subtype profiles with the shared evaluator and immutable catalog, with Forge Default explicitly described as inherited behavior. Ordinary assignments and the existing Reanimator preview remain unchanged. See [the all-pilot implementation and skill evaluation](engagement-protocols-all-pilots.md) for supported adapters, limits, the two bounded revisions, responsive candidate browsing, and exact per-profile screening/holdout coverage. These candidates are not activated or promoted by this work.
