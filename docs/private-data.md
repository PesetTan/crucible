# Private data and public source

Crucible reads card metadata independently from source decks. The bundled starter contains `cards`, `card_catalog`, and empty `decks`/`deck_cards` compatibility tables. It contains no source deck lists, published statistics, or tiers. A clean installation starts with an empty library; use Deck Workshop to paste an Arena list or configure an optional pack. The application does not query provider-specific tables or fetch source lists.

## Optional private packs

Set `CRUCIBLE_DECK_PACK` to the absolute path of a private UTF-8 JSON file before launching the service. Without this variable, source import is a no-op, even if the card database contains other tables. An explicitly configured missing or invalid pack fails visibly. The Import action rereads the configured pack; it never deletes saved decks when the pack is absent.

A version 1 pack is an object with `version: 1` and `decks: [...]`, where each entry is a complete `DeckManifest`. Source adapters live in the owner's private workspace and emit these manifests; the application has no built-in adapter or curated cohort. Packs support `public` and `personal` source types, with 60 main-deck cards, positive quantities, no more than 15 sideboard cards, and the existing four-copy/basic-land validation. Workshop has its separate custom-version contract.

Preserve source IDs, snapshot, URLs, cards, list hash, manifest hash, ID, and capture/import provenance. `listHash` uses the sorted main-deck name/quantity pairs; `manifestHash` and `id` use Crucible's `DeckFingerprint` serialization. All hashes are verified before any deck is saved. Historical manifests created before card `attributes` existed retain their original fingerprint encoding when every attribute is absent/null; supplied attribute payloads always require the current complete hash. See `DeckImporter.Validate` and [deck data](specs/02-deck-data-and-coverage.md). This validates composition and identity; it does not establish rights, current legality, or engine coverage.

Published rates and tier/source metadata are optional manifest fields and render only when supplied. No source branding or dated tier snapshot is built into the UI. `CRUCIBLE_NOTES_FILE` can point to a private array of signed `AiNoteRecord` entries; the bundled editorial array is empty. Existing saved annotations and notes are retained.

For a private session, set the variables in your launch environment, for example:

```sh
export CRUCIBLE_DECK_PACK=/absolute/path/to/private-data/decks.json
export CRUCIBLE_NOTES_FILE=/absolute/path/to/private-data/editorial.json
./scripts/run-service.sh
```

Use the launcher that owns your existing service when changing its environment. Existing services do not acquire new environment variables until a deliberate restart. Never start a second service against live state. Do not place private packs, catalogs, or screenshots in tracked files. `private/`, application `data/`, and raw SQLite databases are ignored; external paths are preferred. Release packaging does not copy either configured file.

Versioned engine coverage and its exact private deck lists can also be imported
through the [engine support catalog](engine-support.md). Keep these input packs
outside tracked source files; engine support is independent of card ownership and
the selected simulation provider.

## Public source export

Deleting current files does not remove them from prior commits. Earlier branches, tags, releases, installed packages, and screenshots can retain old data. Do not make the existing repository public assuming this change cleaned its history.

Run the boundary check, then export a fresh directory:

```sh
python3 scripts/export-public-source.py
python3 scripts/export-public-source.py --output /absolute/path/to/new-public-source
```

The exporter selects tracked and nonignored source files from the working tree, rejects provider references and unreviewed documentation screenshots, verifies the starter's schema/checksum/empty deck tables, and copies files without `.git`, local state, private packs, dependencies, or old builds. It refuses to overwrite a destination. Deleted files are excluded. Review the exported files and initialize a **new** Git history there for publication. Never copy the old `.git` directory. No remote or repository visibility is changed by this script.

This is a source-data boundary check, not universal secret detection or legal clearance. Card metadata and rules text remain in the starter; the [license review](license-review.md) records separate remaining distribution limits. New public screenshots should use synthetic fixtures. Regenerate installers from the cleaned source; old build artifacts remain unchanged and must not be republished as cleaned releases.
