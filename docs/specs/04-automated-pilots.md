# 04 — Automated pilot requirements

Date: October 11, 2026 (America/Los_Angeles)

Status: Design contract with outstanding validation gates. Parent: [specification index](README.md). Current behavior and recorded evidence: [implementation status](../implementation-status.md).

## Engagement Protocols preview

The opt-in `aggro-v001+control-v1+reanimator-v012` mode adds typed, declarative Engagement Protocols for Reanimator. A shared evaluator combines public observations and candidate features across supported decisions; other assignments and Standard defaults remain unchanged. See [Engagement Protocols](../engagement-protocols.md) for configuration, information boundaries, inspection and the frozen matched comparison workflow.

## Bounded pilot optimization

The [Pilot Optimizer](../pilot-optimizer.md) adds frozen, resumable CLI/API experiments for existing pilots. Defaults are two hours of local computation, three scoped code revisions with focused real-engine fixtures, 12 candidates, 10 screening pairs and 40 untouched holdout pairs. Registered numeric overlays preserve base annotations and ability eligibility. New required-fixture failures, changed legacy decisions and worse holdout outcomes reject candidates. Technical failures are not losses. Promotion is explicit, requires complete evidence, and publishes an immutable opt-in policy; historical runs remain pinned. General pilot validation gates below remain applicable.

## Unified Reanimator v011

Current Standard, Control-only, and By deck subtype modes assign every exact case-insensitive Reanimator subtype to one profile, `reanimator-v011` (Liliana). New modes are `aggro-v001+control-v1+reanimator-v011` and `subtypes-v001+aggro-v001+control-v1+reanimator-v011`. The Control-only mode is `control-adaptive-v1+reanimator-v011`; it keeps Aggro on Forge Default. Other archetypes retain their existing policies.

One decision implementation retains v10 copy-payment gates and existing priorities. The pilot selects its priority table from its own frozen annotations, with Cryomancer, Sultai, then Terror precedence and the historical own-card fallback. This internal priority choice is logged and is not a separate assignment or optimizer target. Opposing configured decks and concealed cards do not enter this selection. Unannotated actions retain Forge fallback. Legacy v9/v10 modes and profiles remain executable for saved runs; their evidence and identities are not rewritten. New identities isolate cache/session reuse.

Consolidation verification: six complete balanced legacy-versus-unified pairs retained identical terminal outcomes and state checkpoints. The unified profile passed 12 diagnostic positions and a 44-position focused copy/priority suite. Experiment `0b5cdc232a4d46d68fa997061bb18064` screened the control and three numeric variants in 40 paired comparisons, then completed 40 distinct holdout pairs. The finalist retained 16 wins and 24 losses with zero gains/regressions; no tuned policy was promoted. The report and raw audit are in `data/reports/reanimator-unified-v011/`. This is finite exploratory evidence.

## Historical Reanimator policy releases

The historical v10 release used `aggro-v001+control-v1+reanimator-v10` and `subtypes-v001+aggro-v001+control-v1+reanimator-v10`. New sessions now use the v011 modes above by default, or v012 through the explicit Engagement Protocols preview. Forge Default remains available as a baseline, and all previously supported v9 modes retain their assignments. Exact case-insensitive `subtype=Reanimator` takes precedence. Variant priorities remain Cryomancer, then Sultai, then Terror, then shared; broad Aggro/Control and the 30 subtype profiles retain their existing behavior. The new mode/profile IDs and bridge/attributes hashes isolate caches and session reuse. Saved runs remain pinned to their recorded profiles, frozen annotations and immutable bundles.

Version 10 adds reviewed copy-payment gates. Wistfulness and Deceit keep defensive value only when the copier's actual recorded mana payment satisfies at least one annotated entry-effect color condition. The bridge verifies those gates against the offered creature's printed entry triggers; absent or mismatched annotations and cloned offered cards retain their previous scores. Non-copy retrieval, casting, mana selection and combat retain existing behavior. See [card attributes](../card-attributes.md). The c17ea3ec audit verified all 1,000 source replays. Seventy stratified matched pairs retained identical outcomes; eight source-selected copy failure cases improved from one to three wins, with two gains and no lost win. This targeted diagnostic result is not a matchup-wide estimate. Complete requests, replays and the review are retained under `data/reports/pilot-review-c17ea3ec/`.

Version 3 prioritizes a missing Superior Spider-Man for a Formidable Speaker tutor when Bringer of the Last Gift is already in the pilot's graveyard. The matched 100-game Auras audit improved from 30 to 33 wins, with three gained wins and no lost wins; this is a small exploratory sample, not a calibrated gain. Historical sessions retain their pinned policy and engine bundle.

Version 4 reads extensible card `attributes` JSON for roles and configured priorities. It limits the cast-only reanimation bonus to a cast clone entering the battlefield, protects missing land drops during permanent recovery, and can decline optional recovery of the sole payoff when a copier is in hand. Each new run freezes its attributes map and catalog hash; appended annotations cannot affect a paused or historical run, and changed hashes prevent cache reuse. See [card attributes](../card-attributes.md).

Version 5 separates copying value from hand-recovery value, protects the sole annotated copier during fixed-count discards with alternatives, and can decline an optional redundant copy. Ten stratified samples from session `71684da91ecc441ea0efa88c0eba7522` were replayed one at a time with frozen seeds and normal rules. The two wins and eight losses were unchanged; this supports the corrected choices, not a measured win-rate gain.

Version 6 applies the annotated duplicate-hand value after opponent bonuses for any same-name card already held, and logs candidate scores. Bookworm now has an appended duplicate-hand priority of 2. Ten stratified samples from session `7e69297f1fdd4ecf9cf50f457792028b` retained their two wins and eight losses; the changed recovery choice did not produce a demonstrated win-rate gain.

Version 7 adds an opt-in tutor discard-setup path with a held copier, and Ouroboroid receives copy priority 80 through attributes. Twenty stratified samples from session `90716f82b77c4ccab103198aa2184a34` improved from four to six wins without losing an original win. Isolated reruns attribute one gain to Ouroboroid copy value and another to the Speaker setup path. This is an exploratory diagnostic sample, not a calibrated matchup gain.

Version 8 generalizes annotated payoff discard during fixed-count looting when a copier is held and no payoff is in the graveyard, preserving the public-interference guard and legal discard counts. Spellbook Vendor receives copy priority 40 through attributes, enabling useful copy choices from the opponent's graveyard. A stratified random 50-game sample from session `94456900e49b4a81b94d45413920ceb5` improved from 13 to 14 wins with no original wins lost. Isolated reruns attribute the gain to earlier Bringer discard. This is an exploratory local sample, not a calibrated matchup prediction.

Version 9 preserves a copier across multi-card discards, uses metadata to distinguish reviewed battlefield graveyard interference, completes prepared combos during eligible graveyard recovery, and lets annotated stun outlets prefer nonland discards while retaining Forge value ordering. Searching 700 Reanimator-involving games in session `4eb132dcb9974ebca7368bda31e0b3a6` produced 50 targeted/control samples. Against unchanged default opponents, wins improved 24 to 25 across 41 samples with no lost wins. Nine mirrors were tested separately because both pilots changed. No general matchup-rate gain is inferred.

The shared policy currently handles the Formidable Speaker discard/tutor, selected graveyard and effect choices, and Seasoned Cryomancer discard choices. It scores these choices against an opponent estimate derived only from publicly cast cards and revealed cards in battlefield, graveyard, and face-up exile. The estimate begins unknown and may shift among aggro, control, and combo (including a Reanimator subtype). It is a deterministic heuristic, not a calibrated probability. Other actions, including general spell timing, mulligans, and combat, still use Forge Default. Policy overrides are recorded with offered choices, selection, visible evidence, and reason in replay events; the ordinary replay view hides offered choices that may expose private information.

This policy is exploratory. Focused classifier/priority fixtures and small opposed games check integration, but the full elementary fixture set, Forge Default information audit, and central-sequence replay audit below remain validation gates.

## Policy model

Begin with the pinned engine's existing AI as a baseline. Add bounded deterministic policy improvements where required by fixtures. Pilots choose among engine-legal actions; the engine adjudicates every action. A heuristic may guide a pilot decision but must never stand in for resolving an actual game or determining its winner.

Pilot profiles include a stable ID, version/hash, engine compatibility, configuration, search limits, mulligan behavior, tie-breaking seed policy, and passed fixture report. Record every selected action and its legal-action context or sufficient engine evidence to audit legality. Search bounds are configuration; exhausting a bound returns a legal fallback or a typed failure, never a fabricated result.

## Information boundary

Allow the pilot its own hand, public zones, revealed cards, public history, and information explicitly accessible through a legal effect. Do not allow opponent hand contents, unknown library order, future shuffle outcomes, or the omniscient replay state. A search policy may sample hypothetical hidden states from permitted information; it cannot inspect the actual hidden state.

Verify the engine's baseline AI obeys this boundary. Masking the app API alone is insufficient if in-engine AI reads unrestricted game state. A detected hidden-information leak blocks pilot validation until addressed. Fixture pairs with identical permitted observations but different concealed opponent cards must produce identical decisions under the same pilot seed, unless a legal effect reveals the difference.

## Shared behaviors

Fixtures require legal mulligans and bottom choices, meaningful land sequencing, restricted mana handling, useful hybrid payment choices, timing of interaction/protection, legal target selection, reasonable attack/block decisions, and recognition of deterministic immediate lethal or prevention in simple positions. Preserve starting-player draw rules and pregame choices through the engine.

Baseline mulligan policies must be specified per deck using actual casting routes and color needs. Printed mana value alone cannot estimate whether an impending or evoke card is an early play. Avoid unconstrained lookahead and machine-learning training in the MVP.

## Deck-specific fixture requirements

| Deck | Required policy behaviors |
|---|---|
| Mono-White Auras | Develop an enchantment carrier; apply evasion and scaling; use flash protection in a simple removal response; choose useful exile targets; attack through appropriate first/double-strike lines |
| Mono-White Lifegain | Make legal Leyline pregame choices; sequence creatures and life-gain payoffs; account for replacement effects; use warp only with its immediate and later consequences considered |
| Abzan Lifegain | Sequence life-gain/drain payoffs and colors; handle explore choices and mandatory loops correctly; recognize a simple available drain finish |
| Public 4-Color Reanimator | Set up graveyards; tutor/discard appropriately; find a simple legal Spider-Man/Bringer line; account for both graveyards; choose useful evoke colors; handle Terror/Ardyn trigger and token choices |
| Personal Reanimator | Validate its different setup cards, Cryomancer choices, graveyard activations, and lack of the public list's exact payoff mix; do not reuse public fixtures as proof of this list's quality |
| Jeskai Artifacts | Select legal artifact search chains; avoid a clearly destructive sacrifice when a useful alternative exists; sequence Synthesizer payoffs; handle exile-return, craft, activated-cost restrictions, and haste sources |

Expected decisions in fixtures are a set of acceptable actions, not one arbitrary exact move. Required fixtures test discrete elementary behaviors, not a claim of globally optimal strategy. Each expected set has a rules explanation and board context. Assertions must distinguish a wrong engine resolution from a poor pilot choice.

## Quality gate

For a deck to be validated, all required elementary fixtures pass, no known hidden-information leaks remain, and the audited small batch has no reproducible pilot failure that invalidates the deck's central strategy. Record known weaker decisions and their observed frequency rather than claiming expert play.

If the baseline misses central sequences, keep that deck marked `pilot_unvalidated` until a bounded improvement passes. Debug output may show diagnostic simulations but cannot publish them as validated matchup estimates. A known weak pilot is not repaired by increasing the number of games.

## Sensitivity comparison

Run the baseline and candidate improved profile against the same fixed manifests and a recorded common-seed schedule, with equal play/draw allocations. Store them as separate runs; never pool policy versions. Report matchup changes and reviewed decision examples. Common-seed results are paired evidence, so do not reuse an independent-sample uncertainty claim for their difference.

Display that all resulting rates are conditional on these pilots. Public Arena performance can contextualize a discrepancy but cannot supply expected simulation outcomes or a policy's hidden answer key.

## Exploratory Control policy implementation

New setup defaults to `control-adaptive-v1+reanimator-v9`. Exact case-insensitive broad `strategy=Control` receives `control-shared-v1`; `subtype=Reanimator` takes precedence and retains v9. Hybrid Control–Combo and Aggro–Control classifications retain their existing assignments. The old v9 API mode and historical pinned sessions remain intact. The Control policy uses extensible attributes to improve reviewed variable-sweeper discards and public graveyard exile choices, with Forge accepting legal costs and resolving every action. Other timing, resource and win-condition choices remain Forge's baseline and are instrumented. See [Control pilot](../control-pilot.md) for current classifications, metadata, matched evidence and limitations. This does not satisfy the full pilot validation gate above.

## Exploratory Aggro v001 policy

New setup supports and defaults to `aggro-v001+control-v1+reanimator-v9`. Exact case-insensitive broad `strategy=Aggro` receives `aggro-v001`; Reanimator subtype precedence and the existing Control policy are preserved. Legacy combined and Reanimator modes keep their prior assignments. The new profile fixes evidence-supported opponent-only burn scheduling after a Forge pass through attributes and engine timing/payment checks. Other aggro behavior remains inherited Forge. See [Aggro pilot](../aggro-pilot.md) for frozen dataset evidence, comparison and remaining validation gates.

## Exploratory subtype pilots v001

New sessions offer `subtypes-v001+aggro-v001+control-v1+reanimator-v9` as an explicit exploratory choice; the established Aggro/Control/Reanimator mode remains the default. Thirty exact, case-insensitive non-Reanimator library subtypes have independent profiles and frozen metadata priorities; Reanimator keeps its existing v9 variants and takes precedence. Unknown subtypes retain exact broad Aggro/Control assignment or Forge Default. Legacy modes retain their assignments, and historical runs stay pinned.

The shared subtype hook develops reviewed, untargeted permanent spells from the pilot's own hand only after Forge passes during an empty-stack own main phase. Forge timing/payment checks and resolution remain authoritative. Existing broad Aggro/Control hooks are inherited explicitly for the corresponding subtypes. These are bounded first-release policies: central combo sequencing, combat, mulligans and general interaction remain inherited Forge and unvalidated. See [subtype pilots](../subtype-pilots.md) for assignment, card review, fixtures and complete matched evidence.
