import type {
  Catalog,
  Deck,
  HistoryRun,
  Matchup,
  Run,
  Slot,
  Sample,
  Timing,
} from "./types.ts";
export const active = (run: Run | undefined) =>
  !!run && ["running", "queued", "cancelling"].includes(run.status);
export const rate = (value: number | null | undefined) =>
  value == null ? "—" : `${Math.round(value * 100)}%`;
export const number = (value: number | null | undefined) =>
  value == null
    ? "—"
    : Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value);
export const date = (value: string | undefined) =>
  value
    ? new Date(value).toLocaleString(undefined, {
        dateStyle: "medium",
        timeStyle: "short",
      })
    : "—";
export const label = (value: string) =>
  value === "cancelled"
    ? "Paused"
    : value === "cancelling"
      ? "Pausing"
      : value.replace(/[-_]/g, " ").replace(/^./, (s) => s.toUpperCase());
export const duration = (seconds: number) =>
  seconds < 60
    ? `${Math.max(0, Math.ceil(seconds))} sec`
    : seconds < 3600
      ? `${Math.ceil(seconds / 60)} min`
      : `${Math.floor(seconds / 3600)} hr ${Math.ceil((seconds % 3600) / 60)} min`;
export function runtime(timings: Timing[]) {
  return timings.length
    ? "Active runtime: " +
        duration(
          timings.reduce(
            (n, t) =>
              n +
              t.activeSeconds +
              (t.status === "running"
                ? Math.max(0, (Date.now() - Date.parse(t.updatedAt)) / 1000)
                : 0),
            0,
          ),
        )
    : "Runtime not recorded";
}
export function deckLabel(
  deck: Deck | undefined,
  catalog: Catalog | undefined,
) {
  if (!deck) return "Unavailable deck version";
  const ui = catalog?.decks.find((d) => d.id === deck.id);
  return (
    ui?.label ??
    `${deck.subtype ?? deck.name} · ${deck.friendlyName ?? deck.variant}`
  );
}
export function performance(session: HistoryRun) {
  return session.progress.run.config.deckIds
    .map((id) => {
      const pairs = session.matchups.filter(
        (m) => (m.deckA === id || m.deckB === id) && m.valid > 0,
      );
      const rates = pairs
        .map((m) => (m.deckA === id ? m.winRateA : m.winRateB))
        .filter((r): r is number => r != null);
      return {
        id,
        rate: rates.length
          ? rates.reduce((a, b) => a + b, 0) / rates.length
          : null,
        wins: pairs.reduce(
          (n, m) => n + (m.deckA === id ? m.winsA : m.winsB),
          0,
        ),
        losses: pairs.reduce(
          (n, m) => n + (m.deckA === id ? m.winsB : m.winsA),
          0,
        ),
        draws: pairs.reduce((n, m) => n + m.draws, 0),
        opponents: rates.length,
      };
    })
    .sort((a, b) => (b.rate ?? -1) - (a.rate ?? -1));
}
export function pairFor(matchups: Matchup[], a: string, b: string) {
  return matchups.find(
    (m) => (m.deckA === a && m.deckB === b) || (m.deckA === b && m.deckB === a),
  );
}
export interface Point {
  turn: number;
  value: number;
  count: number;
}
export function points(
  slots: Slot[],
  deckId: string,
  metric: keyof Sample | "winRate",
  rounds: boolean,
): Point[] {
  const groups = new Map<number, number[]>();
  for (const slot of slots) {
    if (
      slot.status !== "completed" ||
      !slot.result ||
      (slot.deckA !== deckId && slot.deckB !== deckId)
    )
      continue;
    if (metric === "winRate") {
      const turn = rounds
        ? Math.ceil(slot.result.actualTurns / 2)
        : slot.result.actualTurns;
      if (turn > 0)
        groups.set(turn, [
          ...(groups.get(turn) ?? []),
          !slot.result.draw && slot.result.winner === deckId ? 100 : 0,
        ]);
      continue;
    }
    for (const s of slot.result.samples) {
      if (s.player !== (slot.deckA === deckId ? "A" : "B")) continue;
      const v = s[metric];
      if (typeof v !== "number") continue;
      const turn = rounds ? Math.ceil(s.actualTurn / 2) : s.actualTurn;
      groups.set(turn, [...(groups.get(turn) ?? []), v]);
    }
  }
  return [...groups]
    .sort(([a], [b]) => a - b)
    .map(([turn, values]) => ({
      turn,
      value: values.reduce((a, b) => a + b, 0) / values.length,
      count: values.length,
    }));
}
export function interval(value: number[] | null | undefined) {
  return value ? `${rate(value[0])}–${rate(value[1])}` : "—";
}
export function pilotRates(sessions: HistoryRun[]) {
  const result: Record<
    string,
    {
      run: Run;
      wins: number;
      losses: number;
      draws: number;
      decks: Set<string>;
    }[]
  > = {};
  for (const session of [...sessions].sort((a, b) =>
    b.progress.run.createdAt.localeCompare(a.progress.run.createdAt),
  )) {
    const run = session.progress.run;
    if (!run.config.pilotProfiles) continue;
    const grouped: Record<
      string,
      { wins: number; losses: number; draws: number; decks: Set<string> }
    > = {};
    for (const m of session.matchups) {
      if (m.valid <= 0 || m.winsA + m.winsB + m.draws !== m.valid) continue;
      for (const [deck, wins, losses] of [
        [m.deckA, m.winsA, m.winsB],
        [m.deckB, m.winsB, m.winsA],
      ] as [string, number, number][]) {
        const profile = run.config.pilotProfiles[deck];
        if (!profile) continue;
        const g = (grouped[profile] ??= {
          wins: 0,
          losses: 0,
          draws: 0,
          decks: new Set(),
        });
        g.wins += wins;
        g.losses += losses;
        g.draws += m.draws;
        g.decks.add(deck);
      }
    }
    for (const [profile, g] of Object.entries(grouped))
      (result[profile] ??= []).push({ run, ...g });
  }
  return result;
}
