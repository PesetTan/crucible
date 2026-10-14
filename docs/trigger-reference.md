# Magic triggers and simulator records

## What counts as a trigger

A triggered ability has a condition and an effect, usually written with **when**, **whenever**, or **at**. It triggers automatically when a matching event occurs or, for a state trigger, when a specified game state becomes true. It goes on the stack the next time a player would receive priority. A trigger is distinct from an activated ability (usually written as a cost followed by a colon), a static ability, and a replacement effect (often written with **instead** or **as**). See Comprehensive Rules 603.1–603.3, 603.6d, and 603.8.

## Useful ways to classify triggers

These labels answer different questions and can overlap. For example, a delayed trigger can also be a turn or phase trigger.

| Dimension | Types and examples |
| --- | --- |
| Trigger condition | **Event trigger**: something happens, such as a creature entering or attacking. **Turn or phase trigger**: a step or phase begins, such as “At the beginning of your upkeep.” **State trigger**: a game state is true; it will not trigger again while its previous instance remains on the stack. |
| Event subject | **Zone change** (enters, leaves, dies, moves to another zone); **combat** (attacks, blocks, deals combat damage); **spell or ability** (cast, copied, resolved, activated); **resource or action** (draw, discard, sacrifice, life gain, damage, counters, tap/untap); and other card-specific events. These are practical labels, not an exhaustive rules-defined list. |
| When created | **Ordinary** trigger printed on or granted to an object; **delayed** trigger created by another effect for a later event; **reflexive** trigger created during resolution by a “when you do” style instruction. |
| Conditions and choices | **Intervening-if** trigger checks its condition both when it would trigger and when it resolves. **Optional** effects containing “may” still trigger and go on the stack; the choice is made on resolution. |

“Dies” means a creature moved from the battlefield to a graveyard. An “enters tapped” instruction is a static ability, not an enters trigger. A prevented or replaced event does not cause a trigger for that event. See rules 603.2g, 603.4–603.8, and 603.12.

## What Crucible records

For new simulations, the Forge bridge records a `trigger` replay entry when Forge reports a triggered ability placed on the stack. It stores the actual turn, sequence, engine event, source card, ability description, and a broad category inferred from that description: **Zone change**, **Turn or phase**, **Combat**, **Spell or ability**, or **Other or unclassified**. The replay displays that entry alongside the existing stack snapshot. The category is a search aid, not a rules determination. It does not yet identify delayed, reflexive, state, optional, or intervening-if triggers separately, and it does not recover triggers that failed to reach the stack. Earlier replay files remain as recorded.

Source: [Magic Comprehensive Rules](https://magic.wizards.com/en/rules), section 603; local snapshot: `docs/mtg/rules/comprehensive-rules-2026-09-25.txt`.
