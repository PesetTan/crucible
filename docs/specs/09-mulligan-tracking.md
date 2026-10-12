# 09 — Mulligans before keep

Date: October 11, 2026 (America/Los_Angeles)

Status: Implemented diagnostics requested during C# implementation.

Record how many times each synthetic player mulligans before keeping a hand, along with its kept hand size. “Playable” in the interface means accepted by the pilot's keep policy; keeping a hand does not establish that it will perform well.

## New game capture

Version: `london-opening-v1`.

The Forge interop shim records each pilot keep/mulligan decision. At the first actual turn, after opening choices, emit one final opening-hand record per player using Forge's authoritative `PlayerStatistics.getMulliganCount()` and `getOpeningHandSize()`. These engine statistics preserve the kept size before pregame actions such as putting a Leyline onto the battlefield. Never infer the kept size from the player's first draw-step hand count.

Record player ID, mulligan count, kept size, whether the pilot accepted the hand, and the tracking version. A keep forced by the rules rather than an accepted policy decision is identified separately. Validate that both final records are present before accepting a completed new game. Keep the individual decisions in its replay.

## Existing recorded games

Version: `london-legacy-events-v1`.

For complete, integrity-checked replays produced under this app's fixed two-player London profile, count `GameEventMulligan` events separately by their exact player identity. The kept size is seven minus that count under this profile. If any mulligan event cannot be mapped unambiguously, leave the opening-hand result unknown. The legacy capture did not record whether a keep was policy-accepted or rules-forced; retain that uncertainty. Distinguish this derivation from new authoritative records.

## Presentation

- Per-game and replay views: each player's count before keep, final kept size, and evidence/forced-keep qualification.
- Per-deck summaries: recorded player appearances, total and mean mulligans, median, maximum, frequencies of 0/1/2/3+, mean kept size, and mean mulligans on play/draw.
- Count a kept opening seven as zero mulligans, not one hand attempt. Missing capture is unknown rather than zero.
- Aggregates use completed games with recorded opening-hand data; publish the recorded-player denominator.
- `/api/v1/runs/{id}/opening-hands` exposes compact game/player records. Summary JSON exports include the per-deck aggregate records.
- The session actions menu opens a dedicated Hand Log page with deck and keep-decision filters; sortable game, deck, position, mulligan, kept-size, and decision columns; evidence and tracking versions; replay links; and pagination. The Position column groups on-play and on-draw records in either order, so the log has no separate position dropdown. Its session label returns to session details, while a selected log menu switches among Hand, Mana Incident, and Runtime logs. Raw JSON remains a separate option.

Changes to the pilot keep policy, opening-hand procedure, or tracking method require a new relevant version. No language-model calls are used to decide or analyze mulligans. No automated tests were added, as requested by the user.
