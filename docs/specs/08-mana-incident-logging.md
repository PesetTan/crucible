# 08 — Mana incident logging

Date: October 11, 2026 (America/Los_Angeles)

Status: Implemented exploratory diagnostics, requested during C# implementation. The user selected the term **mana-starved**.

## Detection profile

Version: `land-availability-v1`.

Evaluate each player's own completed turn-end snapshots separately. A flag requires two consecutive own-turn indices satisfying its condition; missing data or a gap breaks the streak.

| Flag | Conditions at each checkpoint |
|---|---|
| Mana-starved | Own turn 4 or later; fewer than 3 lands in play; no lands in hand; at least 2 nonland cards in hand |
| Mana-flooded | Own turn 5 or later; at least 5 lands in play; at least 3 lands in hand; at most 1 nonland card in hand |

Combine a continuous qualifying streak into one incident. Log player identity, first/last own-turn index, first/last actual-turn ordinal, exact board/hand counts at each qualifying checkpoint, and definition version. A game can contain incidents for either or both players, including the winner. Run-level game counts count a game once per flag kind, regardless of the number of incidents or affected players.

These are conservative land-availability indicators, not causal loss conclusions or a complete measurement of usable mana. Nonland mana production, restricted spending, colored-source availability, sequencing mistakes, and a deck's desired mana curve are not resolved by this profile. Unknown hand composition is not interpreted as zero. A short game with no qualifying streak receives no flag; that does not prove its mana was satisfactory.

## Persistence and presentation

Store incidents in the accepted game's result in the app SQLite database. Derive them from actual engine snapshots without model calls. For existing complete replays, the app can backfill these derived measurements without changing the original game outcome, deck/configuration identities, or raw replay.

Show counts and detection definitions on the run page, support mana-starved/mana-flooded game filters, and show player-specific incident evidence in the game row and replay. Replay links jump to the first affected actual turn. `/api/v1/runs/{id}/mana-events` supplies a compact incident log with game and deck identities and supports an optional `kind` filter. Summary JSON includes incident game counts; game-result API records contain the individual incidents.

The session actions menu opens a dedicated Mana Incident Log page. It shows the definition text and version, a deck filter, sortable game/deck/condition/turn/outcome columns, full incident evidence, replay links, and pagination. The two incident kinds are grouped by the Condition column sort, so this log has no separate condition dropdown. The session game list retains its incident-kind filter because its Mana Flags column sorts by incident count. The log's session label returns to session details. A selected log menu links to the Hand and Runtime logs; Raw JSON remains available separately. An empty filter result is distinguished from unanalysed games, which are not treated as incident-free evidence.

## Required behavior

- One isolated checkpoint is insufficient; two consecutive qualifying checkpoints produce one incident.
- Distinct players and separate streaks remain distinct incidents.
- Flags are recorded for wins, losses, and rules draws, not just losing players.
- No unknown hand-count field becomes a fabricated zero or flag.
- Technical failures without a complete result are not included in completed-game incident totals.
- Changing the detection profile requires a new version and explicit recomputation of derived diagnostics.

No automated tests were added, as requested by the user. These behavioral requirements are documented for future verification.
