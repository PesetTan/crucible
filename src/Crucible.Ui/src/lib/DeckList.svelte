<script lang="ts">
  import type { Snippet } from "svelte";
  import type { Card } from "./types.ts";
  import { groupDeckList } from "./deck-list.ts";
  import CardMana from "./CardMana.svelte";
  import CardRules from "./CardRules.svelte";
  let {
    cards,
    manaValues = {},
    zone = "MainDeck",
    row,
    headingLevel = 3,
  }: {
    cards: Card[];
    manaValues?: Record<string, number>;
    zone?: string;
    row?: Snippet<[Card]>;
    headingLevel?: 3 | 4;
  } = $props();
  let groups = $derived(groupDeckList(cards, manaValues, zone));
</script>

<div
  class="deck-list card-list"
  role="group"
  aria-label={zone === "Sideboard"
    ? "Deck List · Sideboard"
    : "Deck List · Main deck"}
>
  {#each groups as group (group.label)}
    <section class="deck-list-group" aria-label={group.label}>
      <svelte:element
        this={headingLevel === 4 ? "h4" : "h3"}
        class="deck-list-group-heading"
      >
        {group.label}<span class="muted"> · {group.quantity}</span>
      </svelte:element>
      {#each group.cards as card (card.arenaId)}
        {#if row}{@render row(card)}{:else}
          <details class="card-row">
            <summary
              ><span class="card-quantity">{card.quantity}</span><strong
                >{card.name}</strong
              ><span class="card-cost"><CardMana {card} /></span></summary
            >
            <div class="card-detail">
              <p>{card.type} · {card.set}</p>
              <p class="rules-text"><CardRules text={card.text} /></p>
              <p class="small muted">
                Arena card {card.arenaId} · {card.locallyLegal
                  ? "Locally legal"
                  : "Legality requires review"}
              </p>
            </div>
          </details>
        {/if}
      {/each}
    </section>
  {:else}<p class="small muted">No cards in this Deck List.</p>{/each}
</div>
