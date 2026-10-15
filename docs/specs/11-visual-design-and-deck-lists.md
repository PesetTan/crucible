# 11 — Deck Tester selection and pilot labels

Date: October 15, 2026 (America/Los_Angeles)

## Run-setup deck-selection order

The **Choose the Decks for a Round Robin** section uses a fixed disclosure sequence: **Tier A → Tier B → Tier C → Your Deck → Custom Decks → Unrated**. Omit empty groups. Unfamiliar future labels follow these groups alphabetically until explicitly specified. Do not derive group order from API/source order, import time, current selections, or which group first contains a deck; keep tiers together.

Group personal imports under Your Deck and Workshop/custom versions under Custom Decks even when they retain an inherited public tier. Public versions use their published tier or Unrated when absent. This is the order of deck-selection groups, separate from the Creatures/Non-Creatures/Lands grouping inside each Deck List. Within each group, sort decks alphabetically by the canonical mana/color identity name displayed by the deck identity (for example Azorius, Chaos, Jeskai, Sultai), case-insensitively. Break ties by the full displayed deck label (subtype and variant), then exact deck ID. Use the shared presentation catalog's color name, rather than import order, rendered mana glyph strings, or selected state. This sorts deck choices only; it does not alter the card mana-value order inside a Deck List. Preserve deck counts and selection limits.

The deck tile itself selects or deselects the deck. Do not show deck-selection checkboxes. Use a native toggle button with the deck identity as its accessible name, `aria-pressed` for its selected state, a visible Selected/Select deck label, selected styling, and a visible keyboard focus ring. Enter and Space perform the same selection as a click. Blocked decks, the maximum-deck limit, and active-session locking retain their existing restrictions; selected decks remain removable at the limit.

Selection and disclosure are independent. Only the group title/summary expands or collapses that group. Selecting or deselecting its first or last deck, refreshing polled data, or reaching the selection limit must not change its open state. A group containing restored selections may start open on initial load; subsequent changes follow the user's group-title action. Deck List preview controls and nested card-rule disclosures act independently and never select a deck or toggle its containing group. Linked session results remain independently actionable.

## Pilot name labels

Prefix every displayed pilot name with the literal `Pilot: `, for example **Pilot: Liliana** or **Pilot: Urza · Forge Default**. Apply this consistently to setup deck tiles, Forge Pilots headings and selectors, assignments and attribute checks, Engagement Protocol selectors, saved session provenance, and replay decisions. Detailed labels may retain strategy/version text after the name. Unknown profiles use `Pilot: <exact profile ID>`; missing saved assignments use `Pilot: Not recorded`. Add the prefix exactly once, including when an upstream label already has it. Pilot-policy mode names and exact IDs shown as evidence are separate from display aliases. Keep naming files, profile IDs, recorded assignments, cache identity, and historical evidence unchanged.

## Acceptance checks

- Verify alphabetical mana/color identity ordering within every setup group, including multiple variants of the same color identity and after data refresh. Group order remains Tier A through Unrated.
- Setup has no deck-selection checkboxes. Click deck identity, metadata, and tile whitespace; use Enter/Space; verify visible selected state, count, selection persistence, blocked/locked states, and maximum-deck behavior. Deck List previews, card rules, and session links remain independent.
- Deselect the last deck in an open group and select the first in another; neither action changes disclosure state. Collapse a selected group via its title and verify polling does not reopen it. Exercise this at desktop and 390px.
- Every pilot display alias starts with `Pilot: ` exactly once, including fallback, detailed, saved-session, and replay labels. Exact identity values and policy-mode choices retain their meaning.
