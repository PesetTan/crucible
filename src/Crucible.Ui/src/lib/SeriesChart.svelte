<script lang="ts">
  import type { Deck, Catalog } from "./types.ts";
  import type { Point } from "./data.ts";
  import { number, deckLabel } from "./data.ts";
  let {
    title,
    series,
    decks,
    catalog,
    rounds = true,
    percent = false,
    help = "Later checkpoints include only completed games that reached that point. Check the observed counts.",
  }: {
    title: string;
    series: { id: string; color: number; points: Point[] }[];
    decks: Deck[];
    catalog: Catalog | undefined;
    rounds?: boolean;
    percent?: boolean;
    help?: string;
  } = $props();
  let focus = $state<number | null>(null);
  let all = $derived(series.flatMap((s) => s.points));
  let turns = $derived(
    [...new Set(all.map((p) => p.turn))].sort((a, b) => a - b),
  );
  let maxX = $derived(Math.max(1, ...turns));
  let minY = $derived(Math.min(0, ...all.map((p) => p.value)));
  let maxY = $derived(percent ? 100 : Math.max(1, ...all.map((p) => p.value)));
  let x = (t: number) => 20 + (460 * (t - 1)) / Math.max(1, maxX - 1),
    y = (v: number) => 145 - (130 * (v - minY)) / Math.max(1, maxY - minY);
  function path(points: Point[]) {
    return points
      .map((p, i) => `${i ? "L" : "M"}${x(p.turn)},${y(p.value)}`)
      .join(" ");
  }
</script>

<section class="chart">
  <div class="chart-title">
    <h3>{title}</h3>
    <details>
      <summary aria-label={"About " + title}>?</summary>
      <p class="small muted">{help}</p>
    </details>
  </div>
  {#if !all.length}<p class="muted">No recorded observations.</p>{:else}<div
      class="chart-range"
    >
      {number(maxY)}{percent ? "%" : ""}
    </div>
    <svg
      viewBox="0 0 500 160"
      role="img"
      aria-label={title +
        " over " +
        (rounds ? "rounds" : "actual player turns")}
      ><line class="gridline" x1="20" y1="145" x2="480" y2="145" /><line
        class="gridline"
        x1="20"
        y1="80"
        x2="480"
        y2="80"
      />{#each series as s}<path
          d={path(s.points)}
          fill="none"
          stroke={"var(--theme-chart-" + (s.color + 1) + ")"}
          stroke-width="2.5"
        />{#each s.points as p}<circle
            cx={x(p.turn)}
            cy={y(p.value)}
            r={focus === p.turn ? 4 : 2}
            fill={"var(--theme-chart-" + (s.color + 1) + ")"}
            ><title
              >{deckLabel(
                decks.find((d) => d.id === s.id),
                catalog,
              )} · {rounds ? "Round" : "Turn"}
              {p.turn}: {number(p.value)} · {p.count} observations</title
            ></circle
          >{/each}{/each}</svg
    >
    <div class="chart-axis">
      <span>{rounds ? "Round" : "Turn"} 1</span><span>{maxX}</span>
    </div>
    <div class="chart-scrub">
      <label
        >Explore {rounds ? "rounds" : "turns"}<input
          type="range"
          min="0"
          max={Math.max(0, turns.length - 1)}
          value={Math.max(0, turns.indexOf(focus ?? turns[0]))}
          oninput={(e) => (focus = turns[+e.currentTarget.value])}
        /></label
      >{#if focus}<div aria-live="polite">
          {#each series as s}{@const p = s.points.find(
              (p) => p.turn === focus,
            )}<span
              >{deckLabel(
                decks.find((d) => d.id === s.id),
                catalog,
              )}: {p
                ? number(p.value) +
                  (percent ? "%" : "") +
                  " · " +
                  p.count +
                  " observations"
                : "No observation"}</span
            >{/each}
        </div>{/if}
    </div>
    <div class="legend">
      {#each series as s}<span
          ><i style:background={"var(--theme-chart-" + (s.color + 1) + ")"}
          ></i>{deckLabel(
            decks.find((d) => d.id === s.id),
            catalog,
          )}</span
        >{/each}
    </div>
    <details>
      <summary>Measurements and observed counts</summary>
      <div class="table-scroll">
        <table>
          <thead
            ><tr
              ><th>{rounds ? "Round" : "Turn"}</th>{#each series as s}<th
                  >{deckLabel(
                    decks.find((d) => d.id === s.id),
                    catalog,
                  )}</th
                >{/each}</tr
            ></thead
          ><tbody
            >{#each turns as turn}<tr
                ><th>{turn}</th>{#each series as s}{@const p = s.points.find(
                    (p) => p.turn === turn,
                  )}<td>{p ? `${number(p.value)} · n=${p.count}` : "—"}</td
                  >{/each}</tr
              >{/each}</tbody
          >
        </table>
      </div>
    </details>{/if}
</section>
