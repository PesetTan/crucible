<script lang="ts">
  import type { Card } from "./types.ts";
  let {
    cards,
    manaValues = {},
    zone = "MainDeck",
  }: {
    cards: Card[];
    manaValues?: Record<string, number>;
    zone?: string;
  } = $props();
  let sorted = $derived(
    cards
      .filter((c) => c.zone === zone)
      .toSorted(
        (a, b) =>
          (manaValues[a.arenaId] ?? 99) - (manaValues[b.arenaId] ?? 99) ||
          a.name.localeCompare(b.name),
      ),
  );
</script>

<div class="card-list">
  {#each sorted as card}<details class="card-row">
      <summary
        ><span class="card-quantity">{card.quantity}</span><strong
          >{card.name}</strong
        ><span class="card-cost"
          >{card.cost === "no cost" ? "—" : card.cost}</span
        ></summary
      >
      <div class="card-detail">
        <p>{card.type} · {card.set}</p>
        <p class="rules-text">{card.text.replaceAll("\\n", "\n")}</p>
        <p class="small muted">
          Arena card {card.arenaId} · {card.locallyLegal
            ? "Locally legal"
            : "Legality requires review"}
        </p>
      </div>
    </details>{/each}
</div>
