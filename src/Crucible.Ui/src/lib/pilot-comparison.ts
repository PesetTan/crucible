import type { HistoryRun, Run } from "./types.ts";
export interface DefaultPilotResult {
  run: Run; profile: string; deck: string; opponent: string;
  wins: number; losses: number; draws: number; games: number;
}
const defaults = new Set(["forge-default", "forge-default-v1"]);
export function defaultPilotResults(sessions: HistoryRun[]): DefaultPilotResult[] {
  const rows: DefaultPilotResult[] = [];
  for (const session of sessions) {
    const run = session.progress.run;
    const profiles = run.config.pilotProfiles;
    if (!profiles) continue;
    for (const m of session.matchups) {
      if (m.valid <= 0 || m.winsA + m.winsB + m.draws !== m.valid) continue;
      for (const [deck, opponent, wins, losses] of [
        [m.deckA, m.deckB, m.winsA, m.winsB],
        [m.deckB, m.deckA, m.winsB, m.winsA],
      ] as const) {
        const profile = profiles[deck];
        if (!profile || defaults.has(profile) || !defaults.has(profiles[opponent])) continue;
        rows.push({ run, profile, deck, opponent, wins, losses, draws: m.draws, games: m.valid });
      }
    }
  }
  return rows.sort((a, b) => b.run.createdAt.localeCompare(a.run.createdAt));
}
