# 07 — Validation and acceptance

Date: October 11, 2026 (America/Los_Angeles)

Status: Design contract with outstanding validation gates. Parent: [specification index](README.md). Current behavior and recorded evidence: [implementation status](../implementation-status.md).

## Evidence record

Every validation report identifies the engine, adapter, pilot, rules snapshot, manifests, test inputs, expected results, actual results, and pass/fail status. A changed relevant version invalidates the prior gate until the affected checks are rerun. Store fixture IDs and rules references next to each test. Use the root project's archived `docs/mtg` references and official card release notes; verify exact expectations before encoding a fixture.

Fixtures assert material game behavior and invariants, not implementation details or a pilot's arbitrary choice among equivalent moves. A passing parser test alone cannot validate a card's rules behavior.

## Required rules fixtures

| Area | Required scenarios and evidence |
|---|---|
| Spider-Man/Bringer | Cast copy versus noncast return; copy-exile reflexive trigger ordering; both players' pre-existing graveyards; creatures sacrificed by Bringer excluded from its return; retained Spider-Man name/types/base stats; multiple simultaneous ETB triggers |
| Impending | Normal and alternative casting routes; spell versus battlefield creature status; counters removed at controller end steps including casting turn; last-counter change; fresh object on return without inherited impending history |
| Evoke and mana spent | Deceit/Wistfulness homogeneous and mixed hybrid payment; normal casting with both qualifying colors; extra total-cost mana where relevant; sacrifice ordering; noncast return without earlier payment history |
| Restricted and conditional mana | Cavern restrictions and uncounterability; verge activation conditions; shock-land life/tapped choice; Starting Town timing; Enigma Jewel mana limited to abilities |
| Auras and protection | Legal attachment; flash timing; ward payment/decline; hexproof duration; exile-return on aura departure; state-based attachment cleanup |
| Combat and life | First/double strike; flying/reach; lifelink; multiple life-gain replacement effects; simultaneous damage; relevant counters and changing creature status |
| Graveyards and copies | Mill/selection; owner/controller identity; legendary state-based actions; token/copy exceptions; Ardyn and Terror sequencing |
| Lifegain engines | Leyline pregame choices; once-per-turn triggers; explore and mandatory loop handling; warp exile/return casting permissions |
| Artifacts | Repurposing Bay exact mana-value search; Synthesizer token sizing; Pinnacle Starcage exile and departure/activation; craft faces; United Battlefront eligibility |
| FRA and other required cards | Cryogen Relic, Seasoned Cryomancer, and each remaining required unvalidated card's complete relevant text and generated objects |
| Outcomes | Life loss, simultaneous rules draw, empty-library draw loss, card-defined outcomes when relevant, and distinction from an administrative stop |
| Turn structure | Ordinary cycles; extra turn; skipped scheduled turn; game ending midcycle; actual-turn and personal-turn numbering |

Do not assert an exact nuanced outcome from memory. Read the applicable card text, comprehensive rules, and official rulings when authoring each fixture. Tests must include both successful use and disrupted/negative cases for decisive interactions.

## Pilot and trace validation

Pass all required elementary pilot fixtures from [04](04-automated-pilots.md). Inspect information access, including in-engine baseline AI, and run hidden-state permutation fixtures.

Small-batch default: 20 slots per pairing, split 10/10 by starter. This produces 200 public-deck games or 300 games with the personal candidate. Freeze the seed schedule before observing results. Review at least three completed traces per pairing: one with each starter and one chosen from the longest games or an unusual outcome. Review every distinct failure signature and every observed decisive-interaction anomaly. If the pairing lacks enough valid games, its audit is incomplete.

For each reviewed trace, verify opening policy, legal mana/casting routes, relevant triggers, combat, terminal evidence, pilot central-strategy behavior, and replay checkpoints. Record the reviewed game IDs and notes locally. This is a bounded audit, not proof that all unreviewed games are correct. Only aggregate findings need enter chat.

## Reproducibility and integrity

- Repeat representative seeds across fresh game processes and compare normalized traces/final states.
- Repeat a subset under sequential and concurrent scheduling; results must not depend on worker interleaving.
- Reconstruct every small-batch replay against stored checkpoint hashes.
- Verify imported quantities/fingerprints and source database immutability.
- Force timeout, crash, cancellation, truncated output, invalid protocol, and interrupted persistence; no case becomes a rules draw or duplicate accepted game.
- Cancel a run while games are active, then explicitly resume it. All cancelled unfinished slots return to pending with their original seeds/starters, completed slots remain untouched, and attempt history is retained. Repeat after a service restart with interrupted workers; separately verify that genuine failed slots require explicit retry.
- Recompute summaries from accepted records and compare counts and distributions.
- Test zero-game, all-draw, all-failed, and mixed-result fixtures for null handling and denominator correctness.
- Verify matrix reverse cells, play/draw splits, and equal-opponent weighting against known synthetic result records. Clearly label these as test data.

## Full MVP release criteria

The five-public-deck MVP is accepted only when all five exact manifests pass coverage, legality, rules, and pilot gates; the audited small batch passes; the interface and export acceptance checks pass; and the full matrix contains 200 valid completed games per pairing with 100 verified starts per deck (2,000 valid games total), with no unresolved systematic correctness defect. A rules draw is a valid completed game. Technical failures and cancellations are not. The original schedule must be fully accounted for; bounded explicit retries may complete its failed or cancelled slots using their original seeds and configuration. Preserve all prior attempt failures and disclose their frequency, reasons, and potential implications. A terminal schedule with any unresolved invalid slots is an accounted-for incomplete run, not acceptance of the full matrix.

The six-deck expansion additionally requires the current/captured personal-list distinction to be resolved, personal-specific gates to pass, and 200 valid completed games in each of its 15 pairings with 100 verified starts per deck (3,000 valid games total), under the same retry and accounting requirements. If the personal deck is unsupported, document the blocker and keep it excluded explicitly; do not substitute cards or declare that expansion complete.

Reports expose engine/pilot versions, list identities, sample sizes, draws/failures, opening-policy approximation, observed limitations, and interval meaning. No Arena win-rate forecast or causal-loss claim is required for acceptance.

## Independent review

Review the specifications independently before implementation. Check cross-document consistency, requirements that cannot be fulfilled by the selected engine, exact metric definitions, outcome/recovery integrity, information boundaries, and measurable acceptance criteria. Return discrete actionable findings with file locations; a clean review is valid. Resolve accepted findings in the documents and record the result before seeking implementation approval.

After implementation is approved and built, review the actual implementation and evidence separately. A document review does not pass rules, pilot, replay, or runtime gates.

## Deck Workshop acceptance

Verify copying library and custom versions, Arena text with/without printing IDs, split faces, repeated rows, malformed lines, unknown/ambiguous identities, quantity changes/removal, 60-card validation, copy exceptions, and unsupported/local-illegal cards. Unknown entries cannot disappear during resolution. Structural validation runs again on save and preflight.

Verify browser draft restoration, explicit-entry precedence, sideboard preservation, source-database immutability, immutable original manifests, new edit fingerprints, legacy payload compatibility, and cache isolation across deck versions. Validate baseline matchup metadata and single-matchup rerun settings.

Exercise the visible flow from Library and results: edit, save, rerun, compare against the stored scoped baseline, choose a prior baseline, and open both sessions' replays. Check incomplete/zero-result/missing-diagnostic cases, deleted baselines, service outages, active-session restrictions, and desktop/narrow layouts. Comparison counts, play/draw orientation, lengths, mulligans, and mana incidents must agree with stored records. Results remain exploratory Forge pilot results.
