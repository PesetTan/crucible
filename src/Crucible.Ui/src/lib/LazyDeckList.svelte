<script lang="ts">
  import { untrack } from "svelte";
  import type { Deck } from "./types.ts";
  import DeckList from "./DeckList.svelte";
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
    >Deck List · {deck.mainCount} main{deck.sideCount
      ? " / " + deck.sideCount + " sideboard"
      : ""}</summary
  >{#if open}<AiNote
      scope="deck"
      subject={deck.id}
      context={deck.manifestHash}
    /><DeckList cards={deck.cards} {manaValues} />{#if deck.sideCount}<h3>
        Sideboard
      </h3>
      <DeckList
        cards={deck.cards}
        {manaValues}
        zone="Sideboard"
        headingLevel={4}
      />{/if}{/if}
</details>
