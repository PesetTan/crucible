<script lang="ts">
  import { untrack } from "svelte";
  import type { Deck } from "./types.ts";
  import CardList from "./CardList.svelte";
  import AiNote from "./AiNote.svelte";
  let {
    deck,
    manaValues = {},
    initialOpen = false,
  }: {
    deck: Deck;
    manaValues?: Record<string, number>;
    initialOpen?: boolean;
  } = $props();
  let open = $state(untrack(() => initialOpen));
</script>

<details bind:open class="library-disclosure">
  <summary
    >Card list · {deck.mainCount} main{deck.sideCount
      ? " / " + deck.sideCount + " sideboard"
      : ""}</summary
  >{#if open}<AiNote
      scope="deck"
      subject={deck.id}
      context={deck.manifestHash}
    /><CardList cards={deck.cards} {manaValues} />{#if deck.sideCount}<h4>
        Sideboard
      </h4>
      <CardList cards={deck.cards} {manaValues} zone="Sideboard" />{/if}{/if}
</details>
