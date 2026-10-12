# Expanded deck library

Authorized October 11, 2026: eleven public representatives plus the existing captured personal Reanimator, twelve decks total. This expands the original five-public-deck scope; historical plans and run manifests remain unchanged.

Use the same frozen `standard-bo1-bronze-platinum-2026-10-11` source snapshot: Standard Bo1, Bronze–Platinum, September 29–October 11. Rank curated archetype families by the approximate 95% Wilson lower estimate of their match-weighted win rate, using z = 1.96. Combine Sultai and 4-Color Reanimator for family ranking while retaining the original 4-Color exact list for simulation. Rounded public data and selected lists are a ranking heuristic, not full archetype statistics or simulated results.

Keep the existing five public manifests. Add the highest-volume exact list of each of the next six families, breaking volume ties by source deck ID. Freeze their main-deck fingerprints using the same serialization as the original plan:

| Added representative | Main-deck fingerprint |
|---|---|
| Orzhov Skeletons | `b99bc553c31d` |
| Azorius Control | `4b4d0146aa2e` |
| Orzhov Sacrifice | `f7b0b02c30d3` |
| Boros Dragons | `5bfccf8bc7d5` |
| Orzhov Lifegain | `13e423adf8d4` |
| Boros Dwarf | `70e6f5fdb1be` |

Each added list must contain 60 main-deck cards with complete local rules profiles and legality flags. The existing engine coverage and preflight checks apply to every selected deck; missing engine scripts block that deck. Script presence does not validate card interactions or pilot quality. The application remains exploratory.

Allow two to twelve distinct deck versions per run. Eleven public decks produce 55 pairings and 11,000 games at the default 200 per matchup; adding the personal deck produces 66 pairings and 13,200 games. Worker and per-game resource limits are unchanged.
