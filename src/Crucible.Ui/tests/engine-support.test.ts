import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import { compile } from "svelte/compiler";
import { render } from "svelte/server";
import {
  supportedCards,
  supportedMatchups,
  forgeSupport,
  supportsSelection,
  engineLabel,
} from "../src/lib/engine-support.ts";
import type { Coverage, Deck, EngineSupport } from "../src/lib/types.ts";
const deck = { id: "library", listHash: "exact", sideCount: 0 } as Deck;
const catalog = {
  providerId: "fixture",
  engineVersion: "1",
  decks: [{ ...deck, id: "accepted" }],
  cards: [
    { name: "Test Basic", kind: "Card" },
    { name: "Test Token", kind: "Token" },
  ],
  matchups: [{ id: "pair", deckA: "accepted", deckB: "other" }],
} as EngineSupport;
test("exact list coverage survives provenance aliases but excludes edits and sideboards", () => {
  assert.equal(supportedMatchups(deck, catalog).length, 1);
  assert.equal(
    supportedMatchups({ ...deck, listHash: "edited" }, catalog).length,
    0,
  );
  assert.equal(supportedMatchups({ ...deck, sideCount: 1 }, catalog).length, 0);
});
test("Forge distinguishes missing scripts from exploratory coverage", () => {
  assert.equal(
    forgeSupport({ status: "blocked" } as Coverage).label,
    "Unsupported",
  );
  assert.equal(
    forgeSupport({ status: "exploratory" } as Coverage).label,
    "Exploratory",
  );
  assert.equal(forgeSupport().label, "Coverage unavailable");
  assert.equal(engineLabel("pyre", "1"), "Pyre 1");
});
test("a selected field requires every exact pair to be validated", () => {
  const b = { ...deck, id: "b", listHash: "b" };
  const c = { ...deck, id: "c", listHash: "c" };
  const pairs = {
    ...catalog,
    decks: [...catalog.decks, b, c],
    matchups: [
      { id: "ab", deckA: "accepted", deckB: "b" },
      { id: "ac", deckA: "accepted", deckB: "c" },
    ],
  } as EngineSupport;
  assert.equal(supportsSelection([deck, b], pairs), true);
  assert.equal(supportsSelection([b, deck], pairs), true);
  assert.equal(supportsSelection([deck], pairs), false);
  assert.equal(supportsSelection([b, c], pairs), false);
  assert.equal(supportsSelection([deck, b, c], pairs), false);
  assert.equal(supportsSelection([{ ...deck, sideCount: 1 }, b], pairs), false);
  assert.equal(
    supportsSelection([{ ...deck, listHash: "edited" }, b], pairs),
    false,
  );
  pairs.matchups.push({
    id: "bc",
    deckA: "b",
    deckB: "c",
    label: "B vs C",
    eligibilityAsOf: "2026-01-01",
  });
  assert.equal(supportsSelection([deck, b, c], pairs), true);
});
test("supported cards do not imply arbitrary matchup coverage and tokens stay separate", () => {
  assert.deepEqual(supportedCards([catalog])["Test Basic"], ["fixture 1"]);
  assert.equal(supportedCards([catalog])["Test Token"], undefined);
  assert.equal(
    supportedMatchups(
      { ...deck, listHash: "all-supported-cards-new-list" },
      catalog,
    ).length,
    0,
  );
});

test("deck badges render explicit engine, version, unsupported and unavailable states", async () => {
  const source = await readFile(
    new URL("../src/lib/EngineSupportBadges.svelte", import.meta.url),
    "utf8",
  );
  const helper = new URL("../src/lib/engine-support.ts", import.meta.url).href;
  const compiled = compile(
    source.replace('"./engine-support.ts"', JSON.stringify(helper)),
    { generate: "server" },
  );
  const directory = await mkdtemp(
    fileURLToPath(new URL("../node_modules/.badge-test-", import.meta.url)),
  );
  try {
    const path = directory + "/Badge.mjs";
    await writeFile(path, compiled.js.code);
    const { default: Badge } = await import(pathToFileURL(path).href);
    const engine = {
      ...catalog,
      id: "pyre-test",
      providerId: "pyre",
      engineVersion: "test-1",
      matchups: [
        {
          ...catalog.matchups[0],
          label: "Fixture A vs B",
          eligibilityAsOf: "2026-01-01",
        },
      ],
    };
    const props = {
      deck: { ...deck, name: "Fixture deck" },
      coverage: { status: "exploratory" } as Coverage,
      engines: [engine],
      details: true,
    };
    const supported = render(Badge, { props }).body;
    assert.match(supported, /Forge: Exploratory/);
    assert.match(supported, /Pyre test-1: Supported matchups/);
    assert.match(supported, /Fixture A vs B · eligibility snapshot 2026-01-01/);
    const unsupported = render(Badge, {
      props: {
        ...props,
        deck: { ...props.deck, listHash: "edited" },
        coverage: { status: "blocked" } as Coverage,
      },
    }).body;
    assert.match(unsupported, /Forge: Unsupported/);
    assert.match(unsupported, /Pyre test-1: Unsupported list/);
    assert.doesNotMatch(unsupported, /Supported matchups/);
    assert.match(
      render(Badge, { props: { ...props, engines: [] } }).body,
      /Pyre: coverage unavailable/,
    );
    assert.match(
      render(Badge, { props: { ...props, engines: undefined } }).body,
      /Pyre: checking coverage/,
    );
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
