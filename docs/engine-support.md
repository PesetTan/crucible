# Engine support in the library

The Deck Library can display an owner's versioned engine support catalog alongside
the existing Forge coverage. It shows exact supported lists, their validated
matchups and eligibility dates, supported card definitions and generated tokens.
The filter matches exact main-deck list hashes and excludes sideboards. A Workshop
edit loses the exact-list label when its composition changes. Having individually
supported cards does not establish support for a new matchup.

Every library entry and run-selection tile displays separate engine badges.
Forge is labeled **Exploratory** when its scripts are present and **Unsupported**
when required scripts are missing. Imported engines show the engine version and
either **Supported matchups** or **Unsupported list**; expanded library entries
name the validated matchups and their eligibility snapshots. Missing catalog
data is labeled **coverage unavailable**, rather than declaring a list unsupported.
The library's engine filter selects Forge script coverage or a specific imported
engine version's exact validated lists.

Run setup explicitly identifies Forge as the current execution engine. Imported
coverage does not activate another engine. For a selected field, the UI checks
every pair against one engine version's matchup records before reporting that
all selected matchups are validated.

Support is stored in `data/simulator.sqlite`, in `engine_support_catalogs` and
`engine_support_cards`. Card rules, ownership and the source Arena database are
unchanged. Imports reuse existing exact lists and add a deck only when its composition
is missing. Source versions remain in acceptance metadata rather than creating
duplicate library entries. Imports retain existing decks,
annotations, runs and results. Reimporting an identical catalog is a no-op;
conflicting content under the same provider/version/source identity is rejected.

## Import a private catalog

Source-specific adapters and their inputs belong in the owner's private workspace.
The application ships no curated engine catalog or source deck data. A version 1
`EngineSupportPack` contains provider/version/target, SHA-256 pins for source,
runtime and acceptance evidence, card identities, exact deck manifests and matchup
references. These are owner-supplied coverage records; the adapter must verify its
engine release and acceptance evidence before producing a pack. Import validation
checks composition, fingerprints, complete references and immutable identities.

An adapter can supply initial deck manifests with empty fingerprints and use
source deck IDs as matchup references. Prepare a validated pack with Crucible's
own fingerprint encoder:

```sh
dotnet run --project tools/EngineSupportImport -- \
  --prepare /absolute/private/input.json --output /absolute/private/catalog.json
```

With the service stopped, import into the intended Crucible project/data root:

```sh
dotnet run --project tools/EngineSupportImport -- \
  --root /absolute/path/to/crucible --pack /absolute/private/catalog.json
```

The tool acquires the same ownership lock as the service, creates a consistent
SQLite backup in `data/backups/`, then atomically imports decks and support rows.
It refuses to share a live service's database. An updated running service can
instead import a complete prepared pack through the authenticated
`POST /api/v1/engine-support/imports` endpoint; reads use
`GET /api/v1/engine-support`. The UI refreshes coverage when it reloads the library.

This updates the library and coverage records. It does not install an engine,
switch the selected simulation provider, resume sessions or establish current
format legality. Existing installations need the usual deliberate application
update to display this new interface.
