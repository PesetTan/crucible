# Magic Standard Arena Bo1 Simulator Plan

Historical plan. For the current architecture, ten-deck selection limit, pilot modes, and recorded verification, see [implementation status](../implementation-status.md) and the [documentation index](../README.md). Original scope and estimates below are retained as planning history.

Date: October 11, 2026 (America/Los_Angeles)

Status: Implementation authorized in C# on October 11, 2026; automated tests waived by the user. See [implementation status](../implementation-status.md) for delivered behavior and outstanding validation gates.

Project location: `/Users/pesettan/Documents/Magic Arena/projects/crucible`

## Objective

Build a local app around an existing Magic rules engine to simulate hundreds of complete head-to-head Standard Bo1 games, with turn-by-turn performance and game replays. Card coverage and automated pilot validation are the first approval gate. Results describe the tested automated pilots; their credibility depends on correct rules resolution and reasonable play.

Repeated games must make zero language-model calls. Simulation, aggregation, and replay storage happen locally. Chat receives compact summaries rather than full game logs. Opening-hand testing or unopposed goldfishing does not satisfy this objective.

## Verified starting decks

Select the highest-volume exact public list for each requested archetype from the October 11, 2026 snapshot in `standard_cards.sqlite`: Standard Bo1, Bronze–Platinum, September 29–October 11. Keep personal saved decks distinct from public lists.

| Candidate | Main deck | Lands | Approximate public sample | List fingerprint |
|---|---:|---:|---:|---|
| Mono-White Auras | 60 | 20 | 29,000 games | `51a64417db83` |
| 4-Color Reanimator | 60 | 24 | 4,700 games | `7db2253158dc` |
| Mono-White Lifegain | 60 | 20 | 5,800 games | `d79a6ecc8a28` |
| Abzan Lifegain | 60 | 21 | 1,500 games | `0b27dc67ddd3` |
| Jeskai Artifacts | 60 | 22 | 3,500 games | `85eb10944e9e` |
| Personal saved Reanimator | 60 | 21 | Personal deck | Saved ID `75c0271e-f980-47b3-bb01-a6b305f2430b` |

Fingerprints are the first 12 hexadecimal characters of SHA-256 over sorted main-deck `(card_name, quantity)` pairs, serialized as UTF-8 JSON with `ensure_ascii=False` and separators `(',', ':')`.

All five public lists have complete card-data joins and locally legal flags. Before running, independently check current sets, bans, faces, and copy limits against the [official Standard reference](https://magic.wizards.com/en/formats/standard) and [banned list](https://magic.wizards.com/en/banned-restricted-list).

Use the exact **4-Color Reanimator** variant for actual games. The previous statistical comparison combined Sultai and 4-color Reanimator, but that combined family is not a playable list. The chosen variant contains Superior Spider-Man, Bringer of the Last Gift, Ardyn, Terror of the Peaks, Balemurk, Deceit, and Wistfulness.

The personal saved Reanimator remains a separate candidate. Its captured list includes three Seasoned Cryomancer and a 13-card sideboard. The account log changed after import, so check its latest contents before treating the captured deck as current. `reanimator.sqlite` is a public archetype archive, not personal deck data.

### Exact main-deck quantities

These lists were read from the local database during planning. Preserve source IDs and the full immutable manifest during import.

**Mono-White Auras:** 4 Ethereal Armor; 4 Feather of Flight; 4 Optimistic Scavenger; 4 Origin of Spider-Man; 20 Plains; 4 Seam Rip; 4 Shardmage's Rescue; 4 Sheltered by Ghosts; 4 Skyward Spider; 4 Spellbook Vendor; 4 Veteran Survivor.

**4-Color Reanimator:** 2 Ardyn, the Usurper; 4 Awaken the Honored Dead; 4 Breeding Pool; 4 Bringer of the Last Gift; 2 Cavern of Souls; 2 Deceit; 4 Formidable Speaker; 4 Gloomlake Verge; 2 Kiora, the Rising Tide; 4 Overgrown Tomb; 4 Overlord of the Balemurk; 4 Superior Spider-Man; 2 Terror of the Peaks; 4 Town Greeter; 2 Wastewood Verge; 4 Watery Grave; 4 Willowrush Verge; 4 Wistfulness.

**Mono-White Lifegain:** 4 Ajani's Pridemate; 2 Banishing Light; 4 Essence Channeler; 2 Exemplar of Light; 4 Haliya, Guided by Light; 4 Healer's Hawk; 4 Hinterland Sanctifier; 4 Leonin Vanguard; 4 Leyline of Hope; 20 Plains; 4 Ruin-Lurker Bat; 4 Sheltered by Ghosts.

**Abzan Lifegain:** 4 Amalia Benavides Aguirre; 4 Aunt May; 4 Case of the Uneaten Feast; 4 Essence Channeler; 4 Godless Shrine; 4 Haliya, Guided by Light; 4 Hinterland Sanctifier; 1 Leonardo, Cutting Edge; 4 Lunar Convocation; 4 Multiversal Passage; 1 Overgrown Tomb; 4 Plains; 4 Starscape Cleric; 4 Starting Town; 4 Temple Garden; 2 Tigra, Feline Fury; 4 Virulent Emissary.

**Jeskai Artifacts:** 1 Adagia, Windswept Bastion; 4 Authority of the Consuls; 4 Clay-Fired Bricks; 4 Cryogen Relic; 4 Floodfarm Verge; 1 Fomori Vault; 5 Island; 2 Meticulous Archive; 4 Perilous Snare; 4 Pinnacle Starcage; 5 Plains; 4 Repurposing Bay; 4 Simulacrum Synthesizer; 4 Spring-Loaded Sawblades; 4 Starting Town; 1 The Enigma Jewel; 1 The Fire Crystal; 4 United Battlefront.

**Personal saved Reanimator:** 2 Ardyn, the Usurper; 3 Breeding Pool; 4 Bringer of the Last Gift; 3 Broodspinner; 3 Cavern of Souls; 3 Deceit; 2 Forest; 3 Formidable Speaker; 2 Gloomlake Verge; 3 Ouroboroid; 3 Overgrown Tomb; 4 Overlord of the Balemurk; 3 Rapid Rescue; 3 Rubblebelt Maverick; 3 Seasoned Cryomancer; 4 Superior Spider-Man; 4 Wastewood Verge; 2 Watery Grave; 2 Willowrush Verge; 4 Wistfulness.

## Engine assessment

| Engine | Evidence and integration | Recommendation |
|---|---|---|
| Forge | Java rules engine; documented headless AI-versus-AI batch mode; GPL-3.0. Its documentation says combo play is weak. | First choice, conditional on coverage and pilot tests. |
| XMage | Java rules enforcement with computer opponents; MIT license. A custom batch harness needs investigation. Its FRA tracker lists Seasoned Cryomancer as unimplemented at research time. | Fallback and useful independent validation source. |
| Cockatrice | C++/Qt tabletop client; GPLv2. Inspected documentation does not establish the automated rules engine and batch pilots required here. | Exclude from the MVP. |

Sources: [Forge repository](https://github.com/Card-Forge/forge), [Forge AI and batch documentation](https://github.com/Card-Forge/forge/wiki/AI), [XMage repository](https://github.com/magefree/mage), [XMage license](https://raw.githubusercontent.com/magefree/mage/master/LICENSE.txt), [FRA coverage tracker](https://github.com/magefree/mage/issues/14856), and [Cockatrice documentation](https://github.com/Cockatrice/Cockatrice).

Forge has a [Reality Fracture release](https://github.com/Card-Forge/forge/releases). Scripts were inspected for [Superior Spider-Man](https://raw.githubusercontent.com/Card-Forge/forge/master/forge-gui/res/cardsfolder/s/superior_spider_man.txt), [Balemurk](https://raw.githubusercontent.com/Card-Forge/forge/master/forge-gui/res/cardsfolder/o/overlord_of_the_balemurk.txt), and [Wistfulness](https://raw.githubusercontent.com/Card-Forge/forge/master/forge-gui/res/cardsfolder/w/wistfulness.txt). This establishes implementation evidence, not correctness. Complete coverage—including Cryogen Relic, Seasoned Cryomancer, tokens, and reverse faces—remains unresolved. Failed source fetches do not prove a card is absent.

Pin the selected engine version and retain its license and notices. Resolve packaging and applicable redistribution obligations before distributing an engine bundle; engine licensing does not establish rights to card artwork.

## Architecture and interface

- **Read-only deck importer:** freeze exact lists, provenance, quantities, card identities, and hashes in the app's own database.
- **Local Java workers:** pinned Forge version, full rules resolution, automated pilots, controlled starting player, recorded randomness, cancellation, and resource limits.
- **Small Java adapter:** capture structured decisions, state changes, and outcomes. Forge exposes turn events; complete replay capture needs a feasibility test.
- **Local service and browser interface:** run management, SQLite summaries, compressed replay files, and charts.

The interface includes deck selection, coverage status, batch controls, a matchup matrix, performance charts, and a replay viewer. Each replay shows life, battlefield, hand size, graveyard, exile, mana, counters, attachments, combat, and stack actions.

A **turn** means one player's turn. The user's word **round** maps to a **turn cycle**: normally the starting player's turn followed by the other player's turn. Extra turns, skipped turns, and games ending midway through a cycle remain explicit.

## Validation before meaningful win rates

1. **Coverage gate:** every required card, face, token, casting route, and mechanic must load. Unsupported decks are visibly blocked; no silent substitutions.
2. **Rules fixtures:** check known positions against the archived Comprehensive Rules and official release notes. Essential cases include:
   - Cast Spider-Man copying Bringer versus reanimated Spider-Man; both players' graveyards; sacrifice exclusions; ordering the copy-exile and Bringer triggers.
   - Impending counters, end-step timing, creature status, and reanimation.
   - Evoke sacrifice ordering and actual mana spent for Deceit/Wistfulness, including mixed hybrid payments and returning without casting. The [Lorwyn Eclipsed notes](https://magic.wizards.com/en/news/feature/lorwyn-eclipsed-release-notes) distinguish mana spent across the total cost.
   - Aura removal, ward, first/double strike, lifelink replacement effects, simultaneous entries, artifact exile-return, craft, warp, and FRA cards.
3. **Pilot fixtures:** test whether each pilot finds its central sequences, uses legal information, pays useful colors, protects threats, and handles removal and combat sensibly. Reanimator and Jeskai's artifact search chains deserve particular scrutiny.
4. **Trace audit:** manually inspect sample games from every matchup, compare selected sequences with independently checked traces, and verify replay reconstruction.
5. **Pilot sensitivity:** compare baseline and improved policies before interpreting rankings. A deck whose ranking changes sharply gets a prominent pilot-quality limitation.

Hundreds of games cannot compensate for a broken interaction or a pilot that misses the deck's engine. Use existing repository rules references in `docs/mtg`; no applicable AGENTS.md was found during the planning inspection.

## Bounded MVP and reported results

After validation, run **200 games per distinct matchup**, evenly split by starting player:

- Five public decks: **2,000 games** across ten pairings.
- Including the personal saved Reanimator: **3,000 games** across fifteen pairings.

Report wins, losses, rules draws, sample sizes, uncertainty intervals, play/draw splits, and turns-to-win/loss distributions. Timeouts, crashes, and unresolved loops stay separate from rules draws and remain visible.

Turn charts show life, creatures, board power, mana development, hand size, and graveyard growth. Later-turn charts also show how many games remain active, avoiding misleading averages that only include survivors.

Loss reporting distinguishes the recorded terminal event from evidence-backed contributing conditions. Death to combat damage is observable; attributing a loss to being mana-starved needs a defined supporting measure. Ambiguous attribution stays unknown.

Public win rates are reference context only. They never become simulated outcomes. Aggregate comparisons must disclose matchup weighting and should not imply the selected decks reproduce the full Arena field.

## Stages and runtime

1. Audit coverage, pin an engine, and prove structured replay capture.
2. Pass rules and pilot fixtures; run a small audited batch.
3. Build the interface and complete the full matrix.
4. Improve weak pilots and expand sample sizes where comparisons remain uncertain.

Runtime must be measured first. For illustration, 3,000 games averaging 1–10 seconds each across four effective workers would take roughly **13 minutes to two hours**, excluding startup and overhead. This is a conditional calculation, not a benchmark. Complex games may take much longer. Estimate completion from observed throughput.

## Explicit unresolved questions

- Complete card, token, and face coverage in the pinned engine.
- Reproducible randomness and starting-player control.
- Structured replay instrumentation and reconstruction feasibility.
- Acceptable automated pilot quality, particularly for combo and artifact chains.
- Arena Bo1 opening-hand behavior. Until verified, ordinary London-mulligan games must be explicitly labeled as an approximation of Arena's opening-hand process.
- Whether the latest Arena account log changes the personal saved Reanimator list.

## Approval boundary

The user subsequently authorized C# implementation and waived automated tests. Forge remains the selected engine. Validated-result mode requires the rules and pilot evidence gates; the delivered app currently enables explicitly labeled exploratory runs. Creation of the original dated plan and its containing folders was authorized separately.
