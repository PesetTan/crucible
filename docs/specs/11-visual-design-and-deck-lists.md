# 11 — Visual design and Deck Lists

Updated October 15, 2026 (America/Los_Angeles).

This is the shared presentation contract for the active Svelte desktop and browser interface. It extends [06 — Interface and local API](06-interface-and-local-api.md). Interface checks do not certify engine rules, pilot quality, or Arena performance. See [implementation status](../implementation-status.md) for those boundaries.

## Shared layout and visual hierarchy

- Preserve Crucible's graphite/gold design, approved typography, existing navigation, and semantic colors from `branding.json`.
- Use `PageHeader.svelte` for page titles, optional eyebrow/description, and secondary actions across setup, library, Workshop, pilots, history, results, logs, and replay. Keep one page-level heading and consistent spacing; allow actions to wrap on narrow screens.
- Keep spacing in `src/Crucible.Ui/src/spacing.css`, imported after the compatibility styles. Use the shared 4, 8, 12, 16, 20, and 24px spacing scale. Desktop page gutters are 32px, 24px at intermediate widths, and 18px on mobile. Panels use a 16px inset. Avoid stacked toolbar/panel margins and duplicate per-page control styles.
- Standard text, numeric, and dropdown controls are at least 40px tall; labels use the same 6px gap and align controls within a row. Long dropdown labels may wrap. Dense repeated editing controls may use 32px heights; Workshop rows use approximately 44px including vertical spacing.
- Use the existing `AppSelect`, `DeckIdentity`, and `AiNote` components. Keep keyboard focus, disclosure chevrons, readable labels, loading/error/empty states, and normal disabled behavior.
- Establish sections through headings, spacing, and panel outlines. Deck List rows and their nested rules disclosures do not draw horizontal borders. Retain meaningful table and major section separators; do not stack parent and child divider lines around the same item. Hover and focus states identify interactive rows.
- Verify the rendered app at a desktop width and 390 CSS pixels. Ordinary content must not create page-level horizontal overflow; wide evidence tables may scroll within their own container. Source-only CSS checks do not establish visual acceptance.

## Forge Pilots layout

Keep four visible, descriptive view controls: **Pilots & results**, **Deck assignments**, **Engagement protocols**, and **Attribute checks**. Start with pilots assigned to saved decks under the previewed policy; keep all-profile, measured-profile, search, and older-version controls discoverable. Pilot actions open the relevant Deck assignments or Attribute checks view and move keyboard focus to its heading. Exact profile identity, recorded sessions, frozen source, and candidate identities remain available in details. Assignment previews do not change session settings. Preserve exact-profile W/L/D, samples, historical-build warnings, and unmeasured states; never turn the presentation into an overall skill ranking.

## Run-setup deck-selection order

The **Choose the Decks for a Round Robin** section uses a fixed disclosure sequence: **Tier A → Tier B → Tier C → Your Deck → Custom Decks → Unrated**. Omit empty groups. Unfamiliar future labels follow these groups alphabetically until explicitly specified. Do not derive group order from API/source order, import time, current selections, or which group first contains a deck; keep tiers together.

Group personal imports under Your Deck and Workshop/custom versions under Custom Decks even when they retain an inherited public tier. Public versions use their published tier or Unrated when absent. This is the order of deck-selection groups, separate from the Creatures/Non-Creatures/Lands grouping inside each Deck List. Within each group, sort decks alphabetically by the canonical mana/color identity name displayed by the deck identity (for example Azorius, Chaos, Jeskai, Sultai), case-insensitively. Break ties by the full displayed deck label (subtype and variant), then exact deck ID. Use the shared presentation catalog's color name, rather than import order, rendered mana glyph strings, or selected state. This sorts deck choices only; it does not alter the card mana-value order inside a Deck List. Preserve deck counts and selection limits.

The deck tile itself selects or deselects the deck. Do not show deck-selection checkboxes. Use a native toggle button with the deck identity as its accessible name, `aria-pressed` for its selected state, a visible Selected/Select deck label, selected styling, and a visible keyboard focus ring. Enter and Space perform the same selection as a click. Hover, selection, and keyboard focus apply to the whole deck tile. The internal selection button remains transparent in every interaction state; do not render an extra grey button background or inset box around the deck identity. Blocked decks, the maximum-deck limit, and active-session locking retain their existing restrictions; selected decks remain removable at the limit.

Selection and disclosure are independent. Only the group title/summary expands or collapses that group. Selecting or deselecting its first or last deck, refreshing polled data, or reaching the selection limit must not change its open state. A group containing restored selections may start open on initial load; subsequent changes follow the user's group-title action. Deck List preview controls and nested card-rule disclosures act independently and never select a deck or toggle its containing group. Linked session results remain independently actionable.

## Pilot name labels

Prefix every displayed pilot name with the literal `Pilot: `, for example **Pilot: Liliana** or **Pilot: Urza · Forge Default**. Apply this consistently to setup deck tiles, Forge Pilots headings and selectors, assignments and attribute checks, Engagement Protocol selectors, saved session provenance, and replay decisions. Detailed labels may retain strategy/version text after the name. Unknown profiles use `Pilot: <exact profile ID>`; missing saved assignments use `Pilot: Not recorded`. Add the prefix exactly once, including when an upstream label already has it. Pilot-policy mode names and exact IDs shown as evidence are separate from display aliases. Keep naming files, profile IDs, recorded assignments, cache identity, and historical evidence unchanged.

## Deck List terminology and scope

Use **Deck List** as the visible title or action for a deck's card contents. Replace `List`, `Card list`, and `Main-Deck Cards` when they name that interface. Labels may append a count or zone, for example `Deck List · 60 main / 15 sideboard`. Use `Paste an Arena Deck List` for the paste workflow. Composition counts, card-search results, deck-selection tiles, and individual replay zones keep their ordinary names; they are not renamed to Deck List.

Apply the same component and ordering policy to every Deck List in run-setup previews, Deck Library, Workshop, and future result/detail surfaces that display a full deck. Main deck and sideboard remain separate zones; group and sort each independently. Presentation does not change exact card IDs, quantities, source order in stored manifests, hashes, legality, or simulation behavior.

## Grouping and sorting

1. Render nonempty groups in this fixed order: **Creatures**, **Non-Creatures**, **Lands**. Each heading shows its quantity total, including multiple copies. Omit empty groups; an entirely empty list has an explicit empty state.
2. Classify by the front face's card types, before the subtype separator. Artifact creatures and enchantment creatures belong in Creatures. A card with the Land type belongs in Lands, including land creatures. A spell/creature front with a land back remains in its front-face category. All other card types belong in Non-Creatures.
3. Within every group, sort by ascending numeric **mana value**, the numeric interpretation of the requested mana-cost order. Use the source catalog's `manaValues` metadata, including zero. Do not sort rendered cost strings or estimate an alternative casting price from rules text. This rule also applies to Lands; do not substitute mana-production/color sorting.
4. Break equal mana-value ties alphabetically by name, case-insensitively; exact Arena ID breaks remaining ties. Missing, negative, or nonfinite mana values follow known values and use the same name/ID tie-breakers. Do not silently invent a value.
5. Reuse `deck-list.ts` and `DeckList.svelte` for read-only lists and editable Workshop rows. Every caller supplies the available catalog mana-value map, including setup previews. Quantity edits/removal update group totals and rendering immediately while preserving draft and save behavior.

Keep group headings compact and border-free. Deck Library may present the groups as columns when there is sufficient width. Use one readable vertical sequence on narrow screens, with quantity, card name, cost, and any edit action contained within the available width. Card rules remain individually expandable. Sideboard disclosure and editing permissions follow the existing workflow, independently of display grouping.

## Land mana production

Every land row in every Deck List must show **which mana it can produce**, visible while the card rules are collapsed. Apply this to setup previews, Library, editable Workshop rows, and sideboard lists through the shared row presentation.

- Use the existing mana-symbol components and semantic colors. Show distinct possible mana types in **W, U, B, R, G, C** display order, with accessible names such as `Produces green mana` or `Can produce green or blue mana`. For example, Forest shows `{G}` and Breeding Pool shows `{U}` / `{G}`. Colorless uses `{C}`; generic cost symbols are not a mana-production type.
- Label the indicator `Produces` in its accessible description and explain it in a tooltip or equivalent focus-accessible text. This is separate from casting cost: a land's absent mana cost must not leave its production slot as a dash or `No mana cost`. Reuse the compact trailing slot; keep quantity, name, production, and edit controls aligned at desktop and 390px without adding row borders.
- Use verified card metadata or a shared, tested interpretation of the displayed face's own mana abilities and basic land types. Do not infer production from deck colors, color identity, reminder examples, or mana symbols appearing only in activation costs or abilities granted to other objects.
- Display alternative outputs as alternatives, not a claim that the land produces all shown mana at once. Mark conditional or state-dependent production as `Conditional`; expose the relevant restriction in accessible explanatory text and the expanded rules. Any-color production may use the five colored symbols with an `Any color` description; do not imply it also produces colorless. The Deck List describes possible production, not current battlefield availability, quantity, or whether the land is untapped.
- A land that only finds another land shows `Finds a land`, rather than claiming the fetched land's colors as its own output. Known nonproducing lands show `No mana production`; unavailable or unresolved production data shows `Mana production unknown`. Keep these states distinct. For double-faced cards, describe the displayed land face without merging outputs from another face.
- Production indicators do not change grouping, mana-value sorting, stored manifests, or simulation behavior. Keep land production logic shared across read-only and editable rows so the same exact card has the same indicators everywhere.

## Acceptance checks

- Verify alphabetical mana/color identity ordering within every setup group, including multiple variants of the same color identity and after data refresh. Group order remains Tier A through Unrated.
- Setup has no deck-selection checkboxes. Click deck identity, metadata, and tile whitespace; use Enter/Space; verify visible selected state, count, selection persistence, blocked/locked states, and maximum-deck behavior. Deck List previews, card rules, and session links remain independent.
- Deselect the last deck in an open group and select the first in another; neither action changes disclosure state. Collapse a selected group via its title and verify polling does not reopen it. Exercise this at desktop and 390px.
- Every pilot display alias starts with `Pilot: ` exactly once, including fallback, detailed, saved-session, and replay labels. Exact identity values and policy-mode choices retain their meaning.

- Verify run-setup disclosure order against the sequence above, including after refreshed or differently ordered source data, with empty groups omitted. Check desktop and 390px; personal/custom versions must not move into inherited tier groups.
- Setup's action, library disclosure, and Workshop heading all say Deck List; no old content-view label remains in the active renderer.
- The same immutable deck has matching groups, quantities, and within-group order in all three entry points. Main-deck totals exclude the sideboard.
- Cover mixed permanent types, land creatures, front/back-face differences, zero/large/equal mana values, unavailable metadata, alphabetical ties, duplicate-name identities, and empty lists. Grouping and sorting do not mutate the input arrays.
- Verify identical visible land-production indicators across all Deck List entry points, including sideboards. Cover basic and multicolor lands, colorless, any-color and conditional production, activation-cost symbols, granted abilities, fetch-only lands, nonproducing lands, unknown metadata, and different front/back faces. Verify accessible descriptions and collapsed-row visibility at desktop and 390px.
- Exercise Workshop quantity changes and removal: totals update, emptied groups disappear, and existing validation/save controls remain intact.
- Check the full three-group view and expanded card rules at desktop and 390px, without extra row dividers or page overflow. Verify keyboard disclosure/focus and the existing client checks.

Recorded verification belongs in the implementation record or local report directory; this specification is the requirement, not the test result.
