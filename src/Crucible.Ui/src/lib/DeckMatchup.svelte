<script lang="ts">
  import type { Deck, Catalog } from "./types.ts";
  import DeckIdentity from "./DeckIdentity.svelte";
  let {
    deckA,
    deckB,
    catalog,
    compact = false,
  }: {
    deckA: Deck | undefined;
    deckB: Deck | undefined;
    catalog: Catalog | undefined;
    compact?: boolean;
  } = $props();
</script>

<span class="deck-matchup">
  <DeckIdentity deck={deckA} {catalog} {compact} />
  <span class="matchup-separator">vs</span>
  <DeckIdentity deck={deckB} {catalog} {compact} />
</span>

<style>
  .deck-matchup {
    display: inline-flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px 12px;
    min-width: 0;
    max-width: 100%;
    vertical-align: middle;
  }
  .matchup-separator {
    flex: none;
    color: var(--muted);
    font-size: 0.8em;
    font-weight: 400;
  }
</style>
