import type { HistoryRun, Run } from "./types.ts";

export interface SimulatedRate {
  run: Run;
  wins: number;
  losses: number;
  draws: number;
  games: number;
  opponents: number;
  rate: number;
}

// Keep each exact deck's latest recorded sample together. Summing sessions would
// double-count reused games and silently pool different pilots and engine builds.
export function latestSimulatedRates(sessions: HistoryRun[]) {
  const result = new Map<string, SimulatedRate>();
  const latest = [...sessions].sort(
    (a, b) =>
      Date.parse(b.progress.run.createdAt) - Date.parse(a.progress.run.createdAt) ||
      b.progress.run.id.localeCompare(a.progress.run.id),
  );
  for (const session of latest) {
    for (const id of session.progress.run.config.deckIds) {
      if (result.has(id)) continue;
      const pairs = session.matchups.filter(
        (m) =>
          (m.deckA === id || m.deckB === id) &&
          m.valid > 0 &&
          m.winsA + m.winsB + m.draws === m.valid,
      );
      const games = pairs.reduce((n, m) => n + m.valid, 0);
      if (!games) continue;
      const wins = pairs.reduce(
        (n, m) => n + (m.deckA === id ? m.winsA : m.winsB),
        0,
      );
      result.set(id, {
        run: session.progress.run,
        wins,
        losses: pairs.reduce(
          (n, m) => n + (m.deckA === id ? m.winsB : m.winsA),
          0,
        ),
        draws: pairs.reduce((n, m) => n + m.draws, 0),
        games,
        opponents: new Set(pairs.map((m) => m.deckA === id ? m.deckB : m.deckA)).size,
        rate: wins / games,
      });
    }
  }
  return result;
}
