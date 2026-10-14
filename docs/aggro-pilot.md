# Aggro pilot

Displayed and internal profile: `aggro-v001`. New combined mode: `aggro-v001+control-v1+reanimator-v9`. Exact case-insensitive `decks.strategy=Aggro` receives the profile; the Reanimator subtype retains precedence. Control keeps its existing profile. Hybrid classifications are separate labels, not substring matches. Legacy Control/Reanimator modes retain their previous assignments.

This first release fixes reviewed opponent-only burn spells stranded in hand after Forge passes. General creature sequencing, targeting, interaction, mulligans and combat remain Forge Default. It is an exploratory, bounded policy, not a fully validated aggro player.

## Behavior

The independent `aggro.castOpponentBurnOnPass` card attribute opts into a reviewed simple ability. The implementation uses ability structure, not card names: untargeted constant positive `DealDamage` with `Defined=Player.Opponent`, or `DamageAll` with `ValidPlayers=Player.Opponent` and optional `ValidCards=Creature.OppCtrl`. Spells must have mana-only costs, no subability, and not be cloned. Targeted, variable-damage, complex and friendly-damage spells fall back to Forge.

After Forge chooses no action in the pilot's own main phase and the stack is empty, scan the pilot's own hand deterministically for an eligible spell. Require Forge's `canPlay`, `canCastTiming`, and `ComputerUtilCost.canPayCost` checks and positive predicted opponent damage with life loss allowed. Return the action to Forge for actual payment, resolution and every outcome. A chosen Forge action takes precedence. This policy uses no scheduled opposing list, hidden hand or library.

Current catalog opt-ins are Boltwave and Tectonic Hazard, supported by their pinned engine scripts and the source replays. Attribute snapshots are frozen at run creation; adding metadata does not edit past sessions. Decision records include the actual source/ID, API, phase, printed damage, Forge pass, selected action, public opponent evidence and reason.

## Source evidence and validation

Source session `bbd6047f543849eea80d483f017ae109` contains 100 completed Mono-Red Burn versus 4-Color Control Workshop games, 50 wins each and 50 starts per side. Aggro used Forge Default and the opponent used Control v1. All 100 replay hashes, sequences, terminal events and 63,842 state hashes were independently verified. Classification must be hydrated from the deck table's separate columns; payload annotations alone are null for these manifests.

Replay `605a1dbc2db8476894dabecb5caa1e17-1.jsonl.gz` shows Boltwave/Tectonic Hazard held over multiple own turns with untapped Mountains while opponent life rises. This supports burn scheduling, not a claim that all held burn should be spent in every position.

Evidence, immutable inputs and serial matched runner: `data/reports/aggro-pilot-v001/`. Selection seed 131001 freezes 16 diagnostic games, four per historical outcome and start. Both arms use identical exact slot seeds, deck manifests, attributes, normal Performance Mode setting, turn limit and unchanged `control-shared-v1` opponent. Fresh baseline outcomes are the comparison; historical outcomes only determine diagnostic strata.

Focused integration tests cover real spell shapes, engine payment/timing, missing metadata, no mana, preserved baseline action, wrong phase/player, targeted/self-damage fallback and concealed opponent hand invariance. Existing Control and Reanimator checks are retained. The inherited Forge information audit and full deck-specific fixture gate remain incomplete.

### Completed comparison

All 32 serial executions completed with engine terminal outcomes, continuous sequences and verified replay/state hashes. Exact paired requests preserve deck quantities, attributes, slot seeds, starters, normal rules/Performance Mode and the unchanged Control opponent. Wins changed **8/16 to 11/16**, with **four gains, one regression, eleven unchanged outcomes**. The candidate makes 29 burn overrides in 13 games; actual replay hand/phase and stack-removal records were audited for all 29. All 24 historical run payloads remain hash-identical at the comparison audit.

Gains are samples 1, 5, 6 and 13. Sample 1 changes a turn-19 loss to a turn-18 win after two earlier Tectonic Hazards and a final Boltwave. Sample 3 is a regression: earlier burn changes the opponent's subsequent actions and loses on turn 22, while the baseline retains its burn and wins on turn 17. The comparison demonstrates a useful but imperfect scheduling change; it does not show that spending burn immediately is globally optimal. Some retained wins also take longer. No general matchup-rate gain or broad Aggro quality is inferred from this balanced diagnostic cohort against one Control list.

### Publication and live verification

Published bundle `656092f7e33fb10bd16be765879d566e5c9800f0413293a8436249004f59dc43-04ef5cc5d1ee0a9fdd7e51d07438f2ca870ecd1e212ecc2b7f823c8976534774` has unchanged engine resources and exactly the tested candidate bridge class hashes. It reproduced sample 1's gained win with every state checkpoint and selected action matching.

Live session `d41a4f3322114cef80d0f1f0303da42d` completed two normal-mode single-worker opposed games, one start per deck, recording `aggro-v001` versus unchanged `control-shared-v1`. The service default, attribute snapshot, active hashes, profile-aware cache exclusion, replay/state/terminal integrity, serial timestamps and rendered saved-pilot panel are verified. All 24 original run payloads remain unchanged. The two smoke wins are integration evidence, not an additional matchup estimate.

An initial live check reached the older service default; its two games are preserved separately as session `07068515fa104672b6af6d80ede52c28` and excluded from Aggro validation. During idle host reload, another active chat restarted the service concurrently, and the ownership lock rejected our replacement launch. The healthy owner was inspected and reused after live policy checks; see `host-reconciliation.json`. The new skill now explicitly requires checking actual listener ownership after restart rather than relying only on HTTP health.
