<script lang="ts">
  import type { Deck, Catalog } from "./types.ts";
  import ManaSymbol from "./ManaSymbol.svelte";
  import { manaColors } from "./mana.ts";
  let {
    deck,
    catalog,
    compact = false,
  }: {
    deck: Deck | undefined;
    catalog: Catalog | undefined;
    compact?: boolean;
  } = $props();
  let ui = $derived(catalog?.decks.find((d) => d.id === deck?.id));
  let colors = $derived(
    manaColors(
      (deck?.cards ?? [])
        .filter((card) => card.zone === "MainDeck")
        .map((card) => card.cost),
    ),
  );
</script>

<span
  class="deck-identity"
  class:compact
  title={ui?.label ?? deck?.name ?? "Unavailable deck version"}
>
  <span
    class="color-symbols"
    aria-label={ui?.colors ?? "Deck colors"}
    role="img"
    >{#each colors.length ? colors : ["C"] as color}<ManaSymbol
        symbol={color}
        decorative
      />{/each}</span
  >
  <span class="deck-identity-copy"
    ><strong
      >{ui?.subtype ??
        deck?.subtype ??
        deck?.name ??
        "Unavailable deck version"}</strong
    ><small>{ui?.variant ?? deck?.friendlyName ?? deck?.variant ?? ""}</small
    ></span
  >
</span>
