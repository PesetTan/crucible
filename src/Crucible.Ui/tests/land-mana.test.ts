import test from "node:test";
import assert from "node:assert/strict";
import { landProduction } from "../src/lib/land-mana.ts";

const land = (text = "", type = "Land") => ({ type, text });

test("land production derives intrinsic types and normalizes Arena output", () => {
  assert.deepEqual(landProduction(land("", "Basic Land — Forest"))?.symbols, [
    "G",
  ]);
  const pool = landProduction(
    land("{oT}: Add {oU}.\\n{oT}: Add {oG}.", "Land — Forest Island"),
  );
  assert.deepEqual(pool?.symbols, ["U", "G"]);
  assert.equal(pool?.status, "produces");
  assert.equal(pool?.conditional, false);
  assert.match(pool!.description, /^Can produce blue or green mana\./);
  assert.equal(
    landProduction(land("{T}: Add {C}."))?.description.startsWith(
      "Produces colorless mana.",
    ),
    true,
  );
});

test("land production never reads casting costs or another face", () => {
  assert.equal(
    landProduction(land("{T}: Add {G}.", "Creature — Forest Dryad")),
    null,
  );
  assert.equal(
    landProduction(land("Draw a card. // {T}: Add {G}.", "Sorcery // Land")),
    null,
  );
  assert.deepEqual(
    landProduction(land("{T}: Add {U}. // {T}: Add {R}.", "Land // Land"))
      ?.symbols,
    ["U"],
  );
  const front = Object.freeze({ type: "Land", text: "{2}{R}, {T}: Add {G}." });
  assert.deepEqual(landProduction(front)?.symbols, ["G"]);
  assert.equal(landProduction(front)?.conditional, true);
});

test("land production ignores reminder examples and abilities granted to others", () => {
  const result = landProduction(
    land(
      'Lands you control have "{T}: Add {R}."\n{T}: Add {C}.\n(For example, add {G}.)',
    ),
  );
  assert.deepEqual(result?.symbols, ["C"]);
  assert.equal(result?.conditional, false);
  assert.equal(result?.status, "produces");
  assert.deepEqual(
    landProduction(
      land("Enchanted creature has “{T}: Add {U}.”\n{T}: Add {W}."),
    )?.symbols,
    ["W"],
  );
});

test("land production distinguishes alternatives, restrictions, and entry conditions", () => {
  const verge = landProduction(
    land(
      "{oT}: Add {oU}.\n{oT}: Add {oB}. Activate only if you control an Island or a Swamp.",
    ),
  );
  assert.deepEqual(verge?.symbols, ["U", "B"]);
  assert.equal(verge?.conditional, true);
  assert.match(verge!.description, /Island or a Swamp/);
  const fast = landProduction(
    land(
      "This land enters tapped unless you control two or fewer other lands.\n{T}: Add {B} or {G}.",
    ),
  );
  assert.deepEqual(fast?.symbols, ["B", "G"]);
  assert.equal(fast?.conditional, false);
  assert.equal(
    landProduction(land("{T}, Pay 1 life: Add {R}."))?.conditional,
    true,
  );
});

test("any-color production excludes colorless unless separately produced", () => {
  const colors = landProduction(land("{T}: Add one mana of any color."));
  assert.deepEqual(colors?.symbols, [..."WUBRG"]);
  assert.equal(colors?.conditional, false);
  assert.match(colors!.description, /^Can produce any color mana\./);
  const cavern = landProduction(
    land(
      "{T}: Add {C}.\n{T}: Add one mana of any color. Spend this mana only to cast a creature spell of the chosen type.",
    ),
  );
  assert.deepEqual(cavern?.symbols, [..."WUBRGC"]);
  assert.equal(cavern?.conditional, true);
  assert.deepEqual(
    landProduction(land("{T}: Add two mana in any combination of colors."))
      ?.symbols,
    [..."WUBRG"],
  );
  assert.deepEqual(
    landProduction(
      land(
        "As this land enters, choose a basic land type.\nThis land is the chosen type.",
      ),
    )?.symbols,
    [..."WUBRG"],
  );
});

test("station metadata contributes gated production without an unknown output", () => {
  const station = landProduction(
    land(
      "{oT}: Add {oU}.\nSTATIONLEVEL [12+] [] [{oU}, {oT}: Add {oU} for each artifact you control.]\n{oU}, {oT}: Add {oU} for each artifact you control.",
    ),
  );
  assert.deepEqual(station?.symbols, ["U"]);
  assert.equal(station?.conditional, true);
  assert.equal(station?.status, "produces");
});

test("fetch-only, nonproducing and unavailable lands have different states", () => {
  const fetch = landProduction(
    land(
      "{T}, Sacrifice this land: Search your library for a basic land card, put it onto the battlefield tapped, then shuffle.",
    ),
  );
  assert.equal(fetch?.status, "finds-land");
  assert.deepEqual(fetch?.symbols, []);
  assert.equal(
    landProduction(land("{T}: Target creature can't be blocked this turn."))
      ?.status,
    "none",
  );
  assert.equal(landProduction(land())?.status, "unknown");
  assert.equal(landProduction(land("Unavailable"))?.status, "unknown");
  assert.equal(
    landProduction(land('This land gains "{T}: Add {U}."'))?.status,
    "unknown",
  );
  assert.equal(
    landProduction(land("{T}: Add {W}. {T}: Add {U}."))?.status,
    "unknown",
  );
  assert.equal(
    landProduction(
      land(
        "{T}: Add one mana of a color that a land an opponent controls could produce.",
      ),
    )?.status,
    "unknown",
  );
  assert.equal(
    landProduction(land("This land becomes a copy of another land."))?.status,
    "unknown",
  );
  const mixed = landProduction(
    land("{T}: Add {C}.\n{T}: Add one mana of the chosen color."),
  );
  assert.deepEqual(mixed?.symbols, ["C"]);
  assert.equal(mixed?.status, "unknown");
  assert.match(mixed!.description, /Additional mana production unknown/);
});
