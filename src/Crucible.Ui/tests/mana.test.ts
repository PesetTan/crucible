import test from "node:test";
import assert from "node:assert/strict";
import {
  normalizeMana,
  manaSegments,
  costSegments,
  costLabel,
  hybridParts,
  manaColors,
} from "../src/lib/mana.ts";

test("Arena grouped costs and hybrid rules normalize without altering prose", () => {
  assert.equal(
    normalizeMana("Pay {o2oB}, {oT}: Add {o(G/U)o(G/U)}."),
    "Pay {2}{B}, {T}: Add {G/U}{G/U}.",
  );
  assert.deepEqual(manaSegments("{oT}: Add {oG}.\\n"), [
    { kind: "symbol", value: "T" },
    { kind: "text", value: ": Add " },
    { kind: "symbol", value: "G" },
    { kind: "text", value: ".\\n" },
  ]);
});

test("unfamiliar and malformed symbols preserve source evidence", () => {
  assert.equal(
    normalizeMana("{oBunknown} {CHAOS} {o(G/U)}"),
    "{oBunknown} {CHAOS} {G/U}",
  );
  assert.deepEqual(manaSegments("Pay {CHAOS} then {oops"), [
    { kind: "text", value: "Pay " },
    { kind: "symbol", value: "CHAOS" },
    { kind: "text", value: " then " },
    { kind: "text", value: "{" },
    { kind: "text", value: "oops" },
  ]);
  assert.deepEqual(hybridParts("G/UNKNOWN"), []);
});

test("catalog, Arena and Forge replay costs share one interpretation", () => {
  const expected = ["12", "U", "U", "2/G", "B/P"].map((value) => ({
    kind: "symbol",
    value,
  }));
  assert.deepEqual(costSegments("{12}{U}{U}{2/G}{B/P}"), expected);
  assert.deepEqual(costSegments("12 U U 2/G B/P"), expected);
  assert.deepEqual(costSegments("12UU2/GB/P"), expected);
  assert.deepEqual(costSegments("{o12oUoUo(2/G)o(B/P)}"), expected);
  assert.deepEqual(costSegments("no cost"), []);
  assert.deepEqual(costSegments(null), []);
  assert.notDeepEqual(costSegments("{0}"), []);
  assert.deepEqual(costSegments("unavailable"), [
    { kind: "text", value: "unavailable" },
  ]);
});

test("accessible costs distinguish colorless, generic, hybrid, variable and tap", () => {
  assert.equal(
    costLabel(costSegments("{2}{C}{G/U}{X}{T}")),
    "Mana cost: 2 generic mana, Colorless mana, Green mana or Blue mana, X generic mana, Tap",
  );
  assert.equal(costLabel(costSegments("")), "No mana cost");
  assert.deepEqual(hybridParts("u/b/p"), ["U", "B", "P"]);
});

test("deck colors come from parsed mana tokens in WUBRG order", () => {
  assert.deepEqual(manaColors(["{o(G/U)oB}", "{2/W}{R/P}", "{C}"]), [
    ..."WUBRG",
  ]);
  assert.deepEqual(
    manaColors(["{CHAOS}", "no cost", "UNKNOWN", "{C}{X}{2}"]),
    [],
  );
});
