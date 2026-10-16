<script lang="ts">
  import ManaSymbol from "./ManaSymbol.svelte";
  import { manaSegments } from "./mana.ts";
  let { text }: { text?: string | null } = $props();
  let segments = $derived(manaSegments((text ?? "").replaceAll("\\n", "\n")));
</script>

<span class="card-rules"
  >{#each segments as segment}{#if segment.kind === "symbol"}<ManaSymbol
        symbol={segment.value}
      />{:else}{segment.value}{/if}{/each}</span
>

<style>
  .card-rules {
    white-space: pre-line;
    overflow-wrap: anywhere;
  }
</style>
