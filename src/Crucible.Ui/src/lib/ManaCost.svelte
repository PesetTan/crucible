<script lang="ts">
  import ManaSymbol from "./ManaSymbol.svelte";
  import { costSegments, costLabel } from "./mana.ts";
  let { cost, label }: { cost?: string | null; label?: string } = $props();
  let segments = $derived(costSegments(cost));
</script>

<span
  class="mana-cost-display"
  role="img"
  aria-label={label ?? costLabel(segments)}
  title={cost ?? undefined}
>
  <span class="mana-cost-content" aria-hidden="true">
    {#each segments as segment}{#if segment.kind === "symbol"}<ManaSymbol
          symbol={segment.value}
          decorative
        />{:else}{segment.value}{/if}{:else}—{/each}
  </span>
</span>

<style>
  .mana-cost-display {
    display: inline-block;
    flex-shrink: 0;
    vertical-align: middle;
    font-size: 16px;
    line-height: 1.25;
    max-width: 100%;
  }
  .mana-cost-content {
    display: inline-flex;
    align-items: center;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 2px;
  }
</style>
