<script lang="ts">
  import type { Deck, Catalog } from "./types.ts";
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
    "WUBRG"
      .split("")
      .filter((c) =>
        deck?.cards.some(
          (card) => card.zone === "MainDeck" && card.cost.includes(c),
        ),
      ),
  );
</script>

<span
  class="deck-identity"
  class:compact
  title={ui?.label ?? deck?.name ?? "Unavailable deck version"}
>
  <span class="color-symbols" aria-label={ui?.colors ?? "Deck colors"}
    >{#each colors as color}<i class={"mana mana-" + color} aria-hidden="true"
        >{color}</i
      >{/each}{#if !colors.length}<i class="mana mana-C" aria-hidden="true">C</i
      >{/if}</span
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
