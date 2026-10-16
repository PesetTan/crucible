<script lang="ts">
  import type { Snippet } from "svelte";
  import type { Deck, Catalog } from "./types.ts";
  import DeckIdentity from "./DeckIdentity.svelte";
  import { rate, interval as formatInterval } from "./data.ts";
  let {
    deck,
    catalog,
    heading,
    winRate,
    wins,
    losses,
    draws,
    interval,
    children,
    actions,
  }: {
    deck: Deck | undefined;
    catalog: Catalog | undefined;
    heading?: string;
    winRate: number | null;
    wins: number;
    losses: number;
    draws: number;
    interval: number[] | null;
    children?: Snippet;
    actions?: Snippet;
  } = $props();
</script>

<article class="deck-result" aria-label={heading}>
  {#if heading}<h3>{heading}</h3>{/if}
  <svelte:element this={heading ? "div" : "h3"} class="result-deck">
    <DeckIdentity {deck} {catalog} />
  </svelte:element>
  <div class="result-measurement">
    <strong class="result-value">{rate(winRate)}</strong>
    <p class="result-record">{wins}W / {losses}L / {draws}D</p>
  </div>
  <p class="result-interval">95% Wilson interval: {formatInterval(interval)}</p>
  {#if children}<div class="result-details">{@render children()}</div>{/if}
  {#if actions}<div class="result-actions">{@render actions()}</div>{/if}
</article>

<style>
  .deck-result {
    display: grid;
    align-content: start;
    gap: 16px;
    min-width: 0;
    padding: 16px;
    border: 1px solid var(--line);
    border-radius: 8px;
  }
  .deck-result h3 {
    margin: 0;
    font-size: 15px;
    line-height: 1.4;
  }
  .result-deck {
    min-width: 0;
    line-height: 1.4;
    font-size: 15px;
  }
  .result-deck :global(.deck-identity-copy strong) {
    color: var(--text);
    font-size: inherit;
    font-weight: 600;
  }
  .result-measurement {
    display: grid;
    gap: 8px;
  }
  .result-value {
    display: block;
    margin: 0;
    font-size: 40px;
    line-height: 1.1;
    font-weight: 500;
    font-variant-numeric: tabular-nums;
    color: var(--accent);
  }
  .deck-result p {
    margin: 0;
    font-size: 13px;
    line-height: 1.6;
  }
  .result-interval {
    color: var(--muted);
  }
  .result-details {
    display: grid;
    gap: 8px;
  }
  .result-details :global(p) {
    margin: 0;
    font-size: 13px;
    line-height: 1.6;
  }
  .result-actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 12px;
  }
  .result-actions :global(a) {
    line-height: 1.5;
  }
  .result-actions :global(.button) {
    min-height: 40px;
    padding: 9px 12px;
    font-size: 14px;
    line-height: 20px;
  }
</style>
