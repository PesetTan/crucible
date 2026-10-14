# 02 — Deck data and coverage

Date: October 11, 2026 (America/Los_Angeles)

Status: Proposed. Parent: [MVP specification index](README.md).

## Source handling

Open `standard_cards.sqlite` with SQLite URI `mode=ro`. Inspect the actual schema before importing. Use `untapped_snapshots`, `untapped_decks`, and `untapped_deck_cards` for public candidates; use `decks` and `deck_cards` for the personal deck. Resolve display names through `card_catalog` and rules profiles through `cards`. Compare raw quantities against joined quantities so missing card joins cannot silently reduce a deck.

`reanimator.sqlite` is public archive data, not personal data. Backup databases are excluded as defaults. Importing a deck does not establish card ownership or permission to alter a saved Arena deck.

Use the five exact lists in the [plan](../plans/2026-10-11-crucible-plan.md). Verify their fingerprints against the frozen October 11 snapshot; do not silently select newer or different highest-volume lists if the source database changes. A mismatch requires an explicit new deck version with its own provenance. Public Reanimator is specifically the selected 4-color list. A merged Sultai/4-color label cannot be used as a deck identity.

The personal deck ID is `75c0271e-f980-47b3-bb01-a6b305f2430b`. Preserve its captured snapshot and flag the changed source-log evidence. Before using it as the current personal candidate, check current account evidence through the appropriate refresh workflow after implementation is authorized; never mutate the source database merely as a side effect of import.

## Immutable manifest

Each deck version stores:

| Field | Contract |
|---|---|
| `deck_version_id` | App-generated stable identifier |
| `display_name`, `variant`, `source_type` | Human name, exact variant, and `public` or `personal` |
| `source_db`, `source_snapshot_id`, `source_deck_id` | Original source identifiers; null only when source lacks that field |
| `source_capture_time`, `import_time`, `source_url` | Separate source freshness from import time |
| `format`, `best_of`, `period_id` | Standard, one game, and applicable legality window |
| `source_context` | Public rank/window/sample metadata or personal capture evidence |
| `cards` | Zone, Arena ID, title ID when available, name, printing, positive integer quantity |
| `rules_profiles` | Costs, types, text, faces, linked identities, and source/version evidence |
| `main_count`, `land_count`, `side_count` | Computed counts; distinguish modal land faces |
| `list_hash`, `manifest_hash` | Full hashes of main-deck composition and full manifest |
| `legality_report_id`, `coverage_report_id` | Versioned evidence links |

For compatibility with the plan, calculate `list_hash` from sorted `(card_name, quantity)` pairs grouped by name in MainDeck, as UTF-8 compact JSON (`ensure_ascii=False`, separators `(',', ':')`). Store the full SHA-256; show its first 12 hexadecimal characters when helpful. A separate canonical manifest hash includes printings, faces, zones, and rules evidence. Main-deck fingerprints alone are not engine cache keys.

Once referenced by a run, a manifest cannot change. Refreshing an import creates another version. Sideboards are captured separately and never added to the 60-card main-deck count.

## Validation and legality

Check 60 cards for these exact MVP lists; positive quantities; valid identities; combined main/sideboard copy limits with card-text/basic-land exceptions; sideboard maximum; and complete joins. Standard generally permits decks larger than 60, but that does not change these fixed candidate lists.

Local legality flags are evidence, not the final legality oracle. Validate primary printings, linked faces, bans, and the pinned Standard period against official references. A prepared spell such as Reanimate does not legalize unrelated standalone printings. Retain reference URLs, capture/effective dates, and exact evidence used. Unknown or stale evidence produces a visible preflight issue rather than an automatic claim of current legality.

## Coverage report

Coverage is per engine/adapter/rules revision and exact manifest. Inventory each main-deck card plus reverse faces, tokens, copied characteristics, alternate costs, and abilities it can create or access. Track:

- Engine identifier and mapping evidence.
- Card status: `missing`, `present_unvalidated`, `validated`, or `known_incorrect`.
- Required mechanics and relevant fixture IDs.
- Blocking reason and last check date.

Name matching alone is insufficient. Resolve punctuation variants and Universes Within mappings only with explicit identity evidence. Never map a missing card to a generic creature or a superficially similar card.

Deck eligibility requires valid composition, accepted legality evidence, complete required coverage, and passed rules/pilot gates. Missing or known-incorrect support blocks the affected deck. The five-public-deck release cannot be declared complete by quietly dropping one of its decks. The personal deck may remain separately blocked without misrepresenting the public matrix's scope.

## Acceptance examples

- All five public manifests reproduce the plan's quantities, counts, and fingerprints.
- The personal snapshot imports as 60 main-deck cards, 21 lands, and 13 sideboard cards.
- An unresolved card join rejects import with the exact missing identity.
- A prepared face cannot be imported as an independently legal standalone spell without separate evidence.
- Changing the source snapshot creates a new version and leaves completed runs unchanged.
- Import and simulation leave both source SQLite files byte-for-byte unchanged.

## Deck Workshop custom versions

Deck Workshop accepts a copy of any library manifest or an Arena text list. Card resolution and search use the service's read-only local catalog and rules profiles. Resolve exact printing identifiers when supplied; ambiguous or unknown identities are explicit issues and must never be silently omitted. Secondary faces are resolved only through their primary identity.

Browser drafts may be incomplete. Saving requires a named, resolvable 60-card main deck with positive integer quantities and applicable copy limits. The service hydrates card text and legality evidence from its catalog rather than trusting browser card metadata. Missing local legality evidence or engine support blocks simulation but does not prevent saving a structurally valid custom version.

Saved custom versions have source type `custom`, a parent version ID when copied, original source provenance through that parent, a creation timestamp, and immutable composition/manifest hashes. They do not inherit public observed rates or archetype tiers. Repeated saves of identical inputs resolve to the same version; edits create another version. Sideboards are retained as provenance and excluded from editing and Bo1 games. Original manifests and Arena SQLite sources remain unchanged.

For custom versions, captured sideboard rows are provenance only, so copy-limit validation applies to the playable main deck. Custom creation retains resolved pasted sideboard provenance as well as inherited sideboards.
