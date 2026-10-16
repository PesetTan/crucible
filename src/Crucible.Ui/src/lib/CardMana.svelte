<script lang="ts">
  import type { Card } from "./types.ts";
  import ManaCost from "./ManaCost.svelte";
  import ManaSymbol from "./ManaSymbol.svelte";
  import { landProduction } from "./land-mana.ts";
  let { card }: { card: Card } = $props();
  let production = $derived(landProduction(card));
</script>

{#if production}
  <span
    class="land-production"
    role="img"
    aria-label={production.description}
    title={production.description}
  >
    <span class="land-production-content" aria-hidden="true">
      {#if production.symbols.length}
        <span class="land-production-symbols">
          {#each production.symbols as symbol}<ManaSymbol
              {symbol}
              decorative
            />{/each}
        </span>
      {/if}
      {#if production.conditional}<span class="land-production-note"
          >Conditional</span
        >{/if}
      {#if production.status === "finds-land"}<span class="land-production-note"
          >Finds a land</span
        >
      {:else if production.status === "none"}<span class="land-production-note"
          >No mana production</span
        >
      {:else if production.status === "unknown"}<span
          class="land-production-note">Mana production unknown</span
        >{/if}
    </span>
  </span>
{:else}<ManaCost cost={card.cost} />{/if}

<style>
  .land-production {
    display: inline-block;
    vertical-align: middle;
    max-width: 100%;
    font-size: 16px;
    line-height: 1.25;
    border-radius: 3px;
  }
  .land-production-content {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    gap: 3px;
  }
  .land-production-symbols {
    display: inline-flex;
    justify-content: flex-end;
    flex-wrap: wrap;
    gap: 2px;
  }
  .land-production-note {
    color: var(--muted);
    font-size: 10px;
    font-weight: 400;
    line-height: 1.35;
    text-align: right;
  }
</style>
