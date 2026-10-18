# Expanded deck library

Library size is independent of the selection cap. Source-specific cohort definitions and selection statistics are private inputs, supplied through optional packs or Workshop imports. No source cohort is bundled or selected automatically.

Each run supports at most **ten distinct deck versions**, using shared `SimulationLimits.MaximumDecks` across setup, preflight, preferences, and chart colors. Ten selected decks produce 45 pairings and 9,000 games at the default 200 per matchup. Worker and per-game resource limits are unchanged. See [implementation status](../implementation-status.md).

Every selected version retains its own composition, source provenance, and hashes. Existing coverage and preflight gates apply to each deck; missing engine scripts block that deck. Script presence alone does not validate card interactions or pilot quality.
