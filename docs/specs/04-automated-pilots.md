# 04 — Automated pilot requirements

Date: October 11, 2026 (America/Los_Angeles)

Status: Proposed. Parent: [MVP specification index](README.md).

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
