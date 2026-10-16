import test from "node:test";
import assert from "node:assert/strict";
import { latestSimulatedRates } from "../src/lib/simulated-rates.ts";
import type { HistoryRun, Matchup } from "../src/lib/types.ts";

const pair = (a: string, b: string, winsA: number, winsB: number, draws = 0) =>
  ({ deckA: a, deckB: b, winsA, winsB, draws, valid: winsA + winsB + draws }) as Matchup;
const session = (id: string, createdAt: string, matchups: Matchup[]) =>
  ({
    progress: { run: { id, createdAt, config: { deckIds: ["a", "b", "c"] } } },
    matchups,
  }) as HistoryRun;

test("Simulated rate uses completed games, both seats, and draws in the denominator", () => {
  const rates = latestSimulatedRates([
    session("sample", "2026-10-15", [pair("a", "b", 1, 0), pair("c", "a", 7, 1, 1)]),
  ]);
  assert.equal(rates.get("a")?.games, 10);
  assert.equal(rates.get("a")?.wins, 2);
  assert.equal(rates.get("a")?.losses, 7);
  assert.equal(rates.get("a")?.draws, 1);
  assert.equal(rates.get("a")?.rate, 0.2);
  assert.equal(rates.get("a")?.opponents, 2);
  assert.equal(rates.get("b")?.rate, 0);
});

test("Latest sample is per exact deck, skips empty results, and never pools sessions", () => {
  const older = session("older", "2026-10-13", [pair("a", "b", 8, 2), pair("c", "b", 4, 6)]);
  const newer = session("newer", "2026-10-14", [pair("a", "b", 1, 1)]);
  const empty = session("empty", "2026-10-15", [pair("a", "b", 0, 0)]);
  const rates = latestSimulatedRates([older, empty, newer]);
  assert.equal(rates.get("a")?.run.id, "newer");
  assert.equal(rates.get("a")?.games, 2);
  assert.equal(rates.get("c")?.run.id, "older");
  assert.equal(rates.has("edited-a"), false);
  assert.equal(latestSimulatedRates([]).size, 0);
});

test("Incomplete and inconsistent outcomes cannot become a simulated score", () => {
  const broken = { ...pair("a", "b", 3, 2), valid: 6 };
  assert.equal(latestSimulatedRates([session("broken", "2026-10-15", [broken])]).size, 0);
});
