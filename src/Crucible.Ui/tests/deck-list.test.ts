import test from "node:test";
import assert from "node:assert/strict";
import { deckListCategory, groupDeckList } from "../src/lib/deck-list.ts";
import type { Card } from "../src/lib/types.ts";

const card = (
  arenaId: number,
  name: string,
  type: string,
  quantity = 1,
  zone = "MainDeck",
): Card => ({
  arenaId,
  name,
  type,
  quantity,
  zone,
  cost: "{X}{W}",
  set: "FIXTURE",
  text: "",
  linkedFaces: "",
  locallyLegal: true,
});

test("Deck List groups front-face card types with land precedence", () => {
  assert.equal(
    deckListCategory(card(1, "Mixed", "Artifact Creature — Golem")),
    "Creatures",
  );
  assert.equal(
    deckListCategory(card(2, "Mixed", "Enchantment Creature — Spirit")),
    "Creatures",
  );
  assert.equal(
    deckListCategory(card(3, "Mixed", "Land Creature — Forest Dryad")),
    "Lands",
  );
  assert.equal(
    deckListCategory(card(4, "Front spell", "Sorcery // Land")),
    "Non-Creatures",
  );
  assert.equal(
    deckListCategory(card(5, "Front creature", "Creature — Human // Land")),
    "Creatures",
  );
  assert.equal(
    deckListCategory(card(6, "Front land", "Land // Creature — Beast")),
    "Lands",
  );
});

test("Deck List orders numeric mana values, then names, then exact identity", () => {
  const cards = [
    card(7, "Unknown", "Creature"),
    card(4, "Ten", "Creature"),
    card(3, "beta", "Creature"),
    card(2, "Alpha", "Creature"),
    card(1, "Alpha", "Creature"),
    card(6, "Free", "Creature"),
  ];
  const groups = groupDeckList(cards, { 1: 2, 2: 2, 3: 2, 4: 10, 6: 0 });
  assert.deepEqual(
    groups[0].cards.map((c) => c.arenaId),
    [6, 1, 2, 3, 4, 7],
  );
  assert.deepEqual(
    groupDeckList([card(1, "Z", "Land"), card(2, "A", "Land")], {
      1: 0,
      2: 0,
    })[0].cards.map((c) => c.name),
    ["A", "Z"],
  );
});

test("Deck List preserves quantity totals, source order, and zone boundaries", () => {
  const cards = [
    card(1, "Land", "Basic Land", 20),
    card(2, "Spell", "Instant", 4),
    card(3, "Creature", "Creature", 2),
    card(4, "Sideboard creature", "Creature", 3, "Sideboard"),
  ];
  const groups = groupDeckList(cards);
  assert.deepEqual(
    groups.map((g) => [g.label, g.quantity]),
    [
      ["Creatures", 2],
      ["Non-Creatures", 4],
      ["Lands", 20],
    ],
  );
  assert.deepEqual(
    cards.map((c) => c.arenaId),
    [1, 2, 3, 4],
  );
  assert.equal(groups[0].cards[0], cards[2]);
  assert.deepEqual(
    groupDeckList(cards, {}, "Sideboard").map((g) => [g.label, g.quantity]),
    [["Creatures", 3]],
  );
  assert.deepEqual(groupDeckList([]), []);
});

test("Deck List reacts to edits and puts unknown or invalid mana values last", () => {
  const cards = [
    card(1, "A", "Creature", 2),
    card(2, "B", "Creature", 4),
    card(3, "C", "Creature", 1),
  ];
  assert.deepEqual(
    groupDeckList(cards, { 1: -1, 2: 4, 3: NaN })[0].cards.map(
      (c) => c.arenaId,
    ),
    [2, 1, 3],
  );
  assert.equal(
    groupDeckList(
      cards.map((c) => (c.arenaId === 1 ? { ...c, quantity: 5 } : c)),
    )[0].quantity,
    10,
  );
  assert.equal(
    groupDeckList(cards.filter((c) => c.arenaId !== 1))[0].quantity,
    5,
  );
});
