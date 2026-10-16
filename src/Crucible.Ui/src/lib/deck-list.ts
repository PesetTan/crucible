import type { Card } from "./types.ts";

export const deckListCategories = [
  "Creatures",
  "Non-Creatures",
  "Lands",
] as const;
export type DeckListCategory = (typeof deckListCategories)[number];
export interface DeckListGroup {
  label: DeckListCategory;
  cards: Card[];
  quantity: number;
}
const names = new Intl.Collator("en", { sensitivity: "base" });

export function deckListCategory(card: Card): DeckListCategory {
  // Only the front face's card types determine its section. A land creature
  // belongs with lands; artifact/enchantment creatures belong with creatures.
  const types = card.type.split("//", 1)[0].split(/[—–]/, 1)[0];
  if (/\bLand\b/i.test(types)) return "Lands";
  if (/\bCreature\b/i.test(types)) return "Creatures";
  return "Non-Creatures";
}

export function groupDeckList(
  cards: readonly Card[],
  manaValues: Readonly<Record<string, number>> = {},
  zone = "MainDeck",
): DeckListGroup[] {
  const manaValue = (card: Card) => {
    const value = manaValues[card.arenaId];
    return Number.isFinite(value) && value >= 0 ? value : Infinity;
  };
  return deckListCategories
    .map((label) => {
      const entries = cards
        .filter(
          (card) => card.zone === zone && deckListCategory(card) === label,
        )
        .sort(
          (a, b) =>
            manaValue(a) - manaValue(b) ||
            names.compare(a.name, b.name) ||
            a.arenaId - b.arenaId,
        );
      return {
        label,
        cards: entries,
        quantity: entries.reduce((sum, card) => sum + card.quantity, 0),
      };
    })
    .filter((group) => group.cards.length > 0);
}
