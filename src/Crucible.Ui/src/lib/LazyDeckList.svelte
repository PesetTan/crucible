<script lang="ts">
  import { untrack } from "svelte";
  import type { Deck } from "./types.ts";
  import DeckList from "./DeckList.svelte";
  import AiNote from "./AiNote.svelte";
  let {
    deck,
    manaValues = {},
    initialOpen = false,
    engineSupport = {},
  }: {
    deck: Deck;
    manaValues?: Record<string, number>;
    initialOpen?: boolean;
    engineSupport?: Record<string, string[]>;
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
    /><DeckList
      cards={deck.cards}
      {manaValues}
      {engineSupport}
    />{#if deck.sideCount}<h3>Sideboard</h3>
      <DeckList
        cards={deck.cards}
        {manaValues}
        zone="Sideboard"
        headingLevel={4}
        {engineSupport}
      />{/if}{/if}
</details>
