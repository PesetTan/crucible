# Card attributes

The local `standard_cards.sqlite` catalog has an `attributes` JSON object on every `cards` entry, defaulting to `{}`. These are local pilot annotations, separate from rules text and legality. Annotations cover core Reanimator pieces, reviewed Control choices and relevant graveyard interaction; other cards can be annotated later.

The object is extensible: unknown keys and nested objects are retained. Appends recursively merge objects and add distinct array values. Existing values are preserved unless `replaceExisting` is explicitly enabled. Changes are recorded in `card_attributes_history`; the first migration makes an online backup at `standard_cards.sqlite.before-attributes.sqlite`.

```json
{
  "schemaVersion": 1,
  "roles": ["graveyard_copier"],
  "copy": {"origin": "graveyard", "power": 4, "toughness": 4},
  "pilot": {"priority": 80, "opponentBonuses": {"combo": 30}}
}
```

The current pilot reads `roles`, `pilot.priority`, `pilot.variant`, `pilot.variantPriorities`, `pilot.opponentBonuses`, `pilotDestinationPriorities`, duplicate priorities, the cast-only payoff fields, `pilotCopyPriority`, `pilot.protectLastInHand`, `copy.optional`, and `pilot.enableDiscardSetup`. `pilot.discardForHeldCopier` additionally enables a fixed-count loot discard to put an annotated mass-reanimation setup target into the graveyard when a copier is held, the payoff is missing from the graveyard, and public graveyard interference is absent; legal count and sole-copier protection still apply. `pilot.enableDiscardSetup` lets an annotated tutor accept its discard/tutor trigger even with a copier held when a setup target is in hand, no payoff is in the graveyard, and no public graveyard interference is detected. For cards with `pilotDuplicateHandPriority`, a same-name copy already in the pilot's hand caps further hand retrieval at that configured value after opponent bonuses. This applies to any annotated card; it does not affect battlefield copy targets or unannotated cards. Decision logs include each legal candidate's score and copies in the pilot's own hand. A copy can have a different priority from fetching that same card to hand. Protection preserves a sole annotated copier during a fixed-count discard when enough other legal cards exist and the deck contains a mass-reanimation route. An optional copy can be declined when all offered cards are low-priority copiers. It uses the actual Forge ability API and destination for copy versus recovery decisions. `castingRoutes` and additional descriptive attributes may be stored for future behavior; new, unrecognized keys do not automatically create new decision rules.

## Append attributes

From the simulator project directory:

```sh
python3 scripts/card-attributes.py --name 'Superior Spider-Man' --json '{"roles":["value"],"notes":{"reviewed":true}}'
```

Using a name updates all exact-name printing entries consistently. `--arena-id` targets one printing. `--replace-existing` explicitly permits changing existing values, including replacing an array. Reapplying the seed file is idempotent:

```sh
python3 scripts/card-attributes.py --seed docs/card-attributes.seed.json
```

The local service also exposes `GET /api/v1/cards/{arenaId}/attributes` and `POST /api/v1/cards/{arenaId}/attributes`. POST uses the existing `X-Instance-Token` protection and accepts:

```json
{"attributes":{"roles":["value"],"newNamespace":{"newField":true}},"replaceExisting":false}
```

The API targets one printing. If different printings of the same named card have conflicting annotations in a selected deck pair, preflight rejects the ambiguous snapshot rather than silently picking one.

## Reproducibility

New workshop manifests include attributes. Existing immutable manifests and saved sessions are retained. On creating a session, the service reads the current catalog annotations in one SQLite transaction and saves the selected cards' complete objects in `RunConfig.CardAttributes`, along with `AttributesHash`. The bridge receives those saved objects with card names and quantities; it never reads the live catalog during a game.

The compatibility hash conservatively covers the annotated catalog. Appending any attributes creates a different compatibility group, including annotations not yet used by the pilot. Old sessions keep their own snapshot and immutable engine bundle after pause/resume. Legacy bundles ignore the new fields. Library imports do not duplicate an unchanged deck merely because its annotations changed.

Card roles supply facts and heuristic priorities. Forge supplies the legal offered choices and resolves the actions. The pilot uses its own hand, public zones, and legally offered cards; opponent classification still uses the existing public-evidence classifier. The annotations do not establish validated competitive play.

## Additional v9 fields

`pilot.blocksGraveyardSetup` records reviewed battlefield interference: false for self-graveyard abilities on Cryomancer/Maverick and the printed Spider-Man entry ability; true for Ardyn, Veteran Survivor and Soul-Guide Lantern. A cloned permanent uses its current text instead of the printed annotation. Unannotated permanents retain the conservative text fallback.

`pilot.completePreparedCombo` enables a missing-copier retrieval priority for graveyard-to-hand recovery when a payoff is already in the graveyard, at least four lands are on the battlefield, and public interference is absent. Early land recovery keeps its existing priority.

`pilot.discardNonlandsForStun` enables nonland discard preference for an annotated creature-stun/discard outlet against visible enemy creatures. Forge evaluates which nonlands to discard. The opt-in copier protection now retains at least one across multi-card discards, allowing duplicates and respecting unavoidable mandatory counts.

## Control v1 fields

The independent `control` namespace contains `discardMaxManaSweeper`, `landGoal`, `keepPriority`, `prioritizeGraveyardExile`, and `graveyardExilePriority`. These opt into reviewed ability shapes or provide bounded resource/threat priorities; unrecognized fields remain inert. The same session snapshot/hash rules apply. Reanimator's v9 metadata is retained. See [Control pilot behavior and evidence](control-pilot.md).

## Reanimator v10 copy payment gates

`pilotCopyManaGate.conditions` records reviewed entry-trigger conditions such as `CastSA>Count$Adamant_2.Green.2.0`. The v10 policy verifies each condition against the offered creature's actual printed entry triggers and counts the mana recorded on the copier's actual cast ability. When none of those conditions is satisfied, `inactivePriority` caps that creature's copy score after opponent bonuses. Hand retrieval keeps its existing value. An uncast copier has no qualifying cast payment. Missing, malformed or mismatched metadata and cloned offered cards keep the previous scoring behavior.

Wistfulness is annotated for two green or two blue mana; Deceit for two blue or two black. A Spider-Man cast for `{2}{U}{B}` does not automatically satisfy any of those pairs: its actual generic-mana payments matter. Decision records include the source ID, paid colors and each candidate's final score. Legacy v9 profiles ignore the gates even when supplied the same annotations. These annotations do not alter Forge costs, rules or mana selection.

## Aggro v001 field

`aggro.castOpponentBurnOnPass` opts into reviewed constant, untargeted, opponent-only damage spells after Forge passes in the pilot's own main phase. Mana payment, timing and resolution remain engine-controlled. Complex, variable, targeted or friendly-damage shapes retain baseline behavior even if annotated. Current reviewed opt-ins are Boltwave and Tectonic Hazard; unknown cards/fields are inert. See [Aggro pilot](aggro-pilot.md) for source evidence, assignment and validation limits.

## Subtype v001 fields

`subtypePilot.developOnPass` maps exact versioned profile IDs to positive priorities for reviewed permanent development after a Forge pass. The bridge checks spell API, own-hand origin, empty stack, own main phase, engine timing and mana-only payment. Targeted, cloned, variable-cost and subability-bearing spells fall back; existing same-name legendary permanents are protected. Unknown profile keys are inert. The hand search is bounded at 24 cards; equal priorities keep own-hand/ability order. Candidate scores, actual source ID/API, baseline pass, selected action and public opponent evidence are recorded. This metadata supplements the preserved parent policy rather than establishing full subtype strategy validation.

Append the reviewed catalog metadata using `python3 scripts/card-attributes.py --seed docs/subtype-pilots.seed.json`. Both comparison arms must receive identical new annotations, with the baseline bridge/profile retaining old behavior. Historical snapshots are never rewritten.
