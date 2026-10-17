<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { api, poll, loadSaved, save } from "../lib/api.ts";
  import {
    active,
    rate,
    number,
    deckLabel,
    label,
    points,
    runtime,
  } from "../lib/data.ts";
  import type {
    Catalog,
    Snapshot,
    Summary,
    Slot,
    Sample,
  } from "../lib/types.ts";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import DeckResult from "../lib/DeckResult.svelte";
  import DeckMatchup from "../lib/DeckMatchup.svelte";
  import AppSelect from "../lib/AppSelect.svelte";
  import AiNote from "../lib/AiNote.svelte";
  import PageHeader from "../lib/PageHeader.svelte";
  import Provenance from "../lib/Provenance.svelte";
  import SeriesChart from "../lib/SeriesChart.svelte";
  let {
    id,
    catalog,
    navigate,
  }: {
    id: string;
    catalog: Catalog | undefined;
    navigate: (path: string) => void;
  } = $props();
  let snapshot = $state<Snapshot>(),
    summary = $state<Summary>(),
    error = $state(""),
    busy = $state(false),
    triggers = $state<
      {
        gameId: string;
        actualTurns: number;
        events: { actualTurn: number; category: string }[];
      }[]
    >(),
    actionsOpen = $state(false);
  let selected = $state(
      untrack(
        () => loadSaved("result:" + id, { pair: "", unit: "rounds" }).pair,
      ),
    ),
    unit = $state(
      untrack(
        () => loadSaved("result:" + id, { pair: "", unit: "rounds" }).unit,
      ),
    ),
    layout = $state("field");
  let run = $derived(summary?.run);
  let decks = $derived(snapshot?.decks.map((d) => d.manifest) ?? []);
  let pair = $derived(
    summary?.matchups.find((m) => m.deckA + ":" + m.deckB === selected),
  );
  let slots = $derived(
    snapshot?.slots.filter(
      (s) => !pair || (s.deckA === pair.deckA && s.deckB === pair.deckB),
    ) ?? [],
  );
  let pairs = $derived(
    summary?.matchups.filter((m) => !pair || m === pair) ?? [],
  );
  let viewedDecks = $derived(
    decks.filter((d) => !pair || d.id === pair.deckA || d.id === pair.deckB),
  );
  let valid = $derived(
    slots.filter((s) => s.status === "completed" && s.result),
  );
  const metrics: [string, keyof Sample | "winRate", string][] = [
    [
      "Life Totals",
      "life",
      "Life at the checkpoint; changes include damage, life gain, and life payments.",
    ],
    [
      "Board Power",
      "power",
      "Creature power is descriptive. It does not measure attackable power, evasion, or combat strength.",
    ],
    [
      "Creatures in Play",
      "creatures",
      "Creatures present at the recorded checkpoint, including tokens.",
    ],
    [
      "Lands in Play",
      "lands",
      "Land availability, not usable mana or color-specific access.",
    ],
    [
      "Cards in Hand",
      "hand",
      "All cards in hand, including lands and cards that may be uncastable.",
    ],
    [
      "Graveyard Development",
      "graveyard",
      "Cards currently in the graveyard. This is not a card-advantage measure.",
    ],
    [
      "Nonland Cards in Hand",
      "handSpells",
      "Missing hand-composition records are excluded, never counted as zero.",
    ],
    [
      "Win Rate by Game Length",
      "winRate",
      "Wins divided by completed games ending at this length; draws count as nonwins. This is not a live win probability.",
    ],
  ];
  $effect(() => save("result:" + id, { pair: selected, unit }));
  async function load() {
    try {
      const next = await api<Snapshot>(`/api/v1/runs/${id}/state`);
      if (next !== snapshot) {
        snapshot = next;
        summary = await api<Summary>(`/api/v1/runs/${id}/summary`);
      }
      error = "";
    } catch (e) {
      error = (e as Error).message;
    }
  }
  onMount(() => {
    void load();
    return poll(
      async () => {
        if (!run || active(run)) await load();
      },
      () => (active(run!) ? 3000 : 30000),
    );
  });
  async function action(name: string) {
    busy = true;
    error = "";
    try {
      await api(`/api/v1/runs/${id}/${name}`, { method: "POST", body: {} });
      await load();
      actionsOpen = false;
    } catch (e) {
      error = (e as Error).message;
    } finally {
      busy = false;
    }
  }
  async function remove() {
    if (
      !confirm(
        "Permanently delete this session, its results, and associated replay files? This cannot be undone.",
      )
    )
      return;
    busy = true;
    try {
      await api(`/api/v1/runs/${id}`, { method: "DELETE" });
      navigate("/runs");
    } catch (e) {
      error = (e as Error).message;
    } finally {
      busy = false;
    }
  }
  async function loadTriggers() {
    try {
      triggers = await api(`/api/v1/runs/${id}/trigger-summaries`);
    } catch (e) {
      error = (e as Error).message;
    }
  }
  function getPoints(metric: keyof Sample | "winRate") {
    return viewedDecks.map((d) => ({
      id: d.id,
      color: decks.findIndex((x) => x.id === d.id),
      points: points(slots, d.id, metric, unit === "rounds"),
    }));
  }
  function distribution(list: Slot[]) {
    const map = new Map<number, number>();
    for (const s of list) {
      if (!s.result) continue;
      const t =
        unit === "rounds"
          ? Math.ceil(s.result.actualTurns / 2)
          : s.result.actualTurns;
      map.set(t, (map.get(t) ?? 0) + 1);
    }
    return [...map].sort(([a], [b]) => a - b);
  }
</script>

<svelte:head><title>Session Results — Crucible</title></svelte:head>
{#if error}<div class="error" role="alert">
    {error}<button onclick={load}>Refresh</button>
  </div>{/if}
{#if !run}<h1>Loading Session…</h1>{:else}
  {#snippet resultActions()}
    <details class="session-actions" bind:open={actionsOpen}>
      <summary aria-label="Session actions">⋯</summary>
      <div class="actions">
        {#if active(run)}<button
            disabled={busy || run.status === "cancelling"}
            onclick={() => action("cancel")}>Pause Session</button
          >{:else}<button
            disabled={busy ||
              !snapshot?.slots.some(
                (s) => !s.result && ["pending", "cancelled"].includes(s.status),
              )}
            onclick={() => action("resume")}>Resume Unfinished Games</button
          ><button
            disabled={busy ||
              !snapshot?.slots.some((s) => !s.result && s.status === "failed")}
            onclick={() => action("retry")}>Retry Failed Attempts</button
          >{/if}{#each [["hands", "Hand Log"], ["mana", "Mana Incident Log"], ["runtime", "Runtime Log"], ["games", "Game Evidence"]] as [kind, title]}<a
            href={`/runs/${id}/logs/${kind}`}>{title}</a
          >{/each}<a href={`/api/v1/runs/${id}/export?format=json`} download
          >Export JSON</a
        ><a href={`/api/v1/runs/${id}/export?format=csv`} download>Export CSV</a
        ><button class="danger" disabled={busy || active(run)} onclick={remove}
          >Delete Session</button
        >
      </div>
    </details>
  {/snippet}
  <PageHeader
    title="Across the Table."
    eyebrow={`SESSION / ${run.id.slice(0, 8)} · ${label(run.status)}`}
    description={`${run.config.deckIds.length} decks · ${run.total} scheduled games · Root Seed ${run.config.rootSeed}`}
    actions={resultActions}
  />
  <AiNote subject="results" /><AiNote
    scope="run"
    subject={id}
    context={summary?.valid + ":" + run.status}
  /><Provenance {run} {catalog} {decks} capability={snapshot?.capabilities} />
  <div class="toolbar">
    <div>
      <label for="results-matchup">Matchup</label><AppSelect
        id="results-matchup"
        label="Matchup"
        bind:value={selected}
        options={[
          { value: "", label: "All matchups" },
          ...(summary?.matchups ?? []).map((m) => ({
            value: m.deckA + ":" + m.deckB,
            label:
              deckLabel(
                decks.find((d) => d.id === m.deckA),
                catalog,
              ) +
              " vs " +
              deckLabel(
                decks.find((d) => d.id === m.deckB),
                catalog,
              ),
          })),
        ]}
      />
    </div>
    <div>
      <label for="results-time-unit">View by</label><AppSelect
        id="results-time-unit"
        label="View by"
        bind:value={unit}
        options={[
          { value: "rounds", label: "Rounds" },
          { value: "turns", label: "Actual player turns" },
        ]}
      />
    </div>
    <button onclick={load}>Refresh</button>
  </div>
  <p><strong>{runtime(snapshot?.timings[id] ?? [])}</strong></p>
  <div class="notice">
    <strong>Exploratory Pilot Results</strong><span
      >{run.config.openingPolicy}. {run.config.provider?.id === "pyre" ? "Results apply to these exact decks and baseline policies. They do not forecast Arena performance." : "Rules and pilot gates remain unvalidated. Extra/skipped turn grouping and mana-pool replay are unavailable."}</span
    >
  </div>
  {#if run.error}<p class="error">{run.error}</p>{/if}
  <div class="stats-grid">
    <div>
      <span>VALID GAMES</span><strong
        >{valid.length}<small> / {slots.length}</small></strong
      >
    </div>
    <div>
      <span>IN PROGRESS</span><strong
        >{slots.filter((s) => s.status === "running").length}</strong
      >
    </div>
    <div>
      <span>TECHNICAL FAILURES</span><strong
        >{slots.filter((s) => s.status === "failed").length}</strong
      >
    </div>
    <div>
      <span>SCHEDULE</span><strong class="stat-word"
        >{valid.length === slots.length ? "Complete" : "Incomplete"}</strong
      >
    </div>
  </div>
  <p class="small muted">
    {slots.filter((s) => s.cachedFromRunId).length} reused games · {slots.filter(
      (s) => s.status === "cancelled",
    ).length} paused · {slots.filter((s) => s.status === "pending").length} waiting
  </p>
  {#if summary}<div class="notice">
      <strong>Mana Incidents</strong><span
        >{summary.manaStarvedGames} games mana-starved · {summary.manaFloodedGames}
        mana-flooded · {summary.manaAnalyzedGames} analyzed across the full session.
        These indicators do not establish the cause of a loss.</span
      >
    </div>
    <details class="panel">
      <summary>Mulligans Before Keep</summary>
      <div class="table-scroll">
        <table>
          <thead
            ><tr
              ><th>Deck</th><th>Recorded players</th><th>Mean</th><th
                >Median / Max</th
              ><th>0 / 1 / 2 / 3+</th><th>Kept size</th></tr
            ></thead
          ><tbody
            >{#each summary.mulligans.filter( (m) => viewedDecks.some((d) => d.id === m.deckId) ) as m}<tr
                ><th
                  ><DeckIdentity
                    deck={decks.find((d) => d.id === m.deckId)}
                    {catalog}
                  /></th
                ><td>{m.recordedPlayers}</td><td>{number(m.mean)}</td><td
                  >{number(m.median)} / {number(m.maximum)}</td
                ><td>{m.zero} / {m.one} / {m.two} / {m.threePlus}</td><td
                  >{number(m.meanKeptSize)}</td
                ></tr
              >{/each}</tbody
          >
        </table>
      </div>
      <p class="small muted">
        Completed games only. Missing opening-hand records remain unknown.
      </p>
    </details>{/if}
  <section class="panel matchup-explorer">
    <div class="section-head">
      <div>
        <div class="eyebrow">01 / THE MATCHUPS</div>
        <h2>Who Wins Against Whom?</h2>
      </div>
      <div class="actions">
        <button
          aria-pressed={layout === "field"}
          onclick={() => (layout = "field")}>Field</button
        ><button
          aria-pressed={layout === "deck"}
          onclick={() => (layout = "deck")}>By deck</button
        >
      </div>
    </div>
    <p class="small muted">
      Select a rate to filter every results section. Row deck's win rate.
    </p>
    {#if layout === "field"}<div class="table-scroll">
        <table class="field-matrix">
          <thead
            ><tr
              ><th>Deck / Opponent →</th>{#each decks as d, i}<th
                  title={deckLabel(d, catalog)}
                  >{i + 1}<small
                    >{catalog?.decks.find((x) => x.id === d.id)?.subtype ??
                      d.subtype}</small
                  ></th
                >{/each}</tr
            ></thead
          ><tbody
            >{#each decks as d, i}<tr
                ><th>{i + 1} <DeckIdentity deck={d} {catalog} compact /></th
                >{#each decks as other}{@const m = summary?.matchups.find(
                    (m) =>
                      (m.deckA === d.id && m.deckB === other.id) ||
                      (m.deckB === d.id && m.deckA === other.id),
                  )}<td
                    >{#if d.id === other.id}·{:else if m}<button
                        class="matrix-rate"
                        aria-label={deckLabel(d, catalog) +
                          " vs " +
                          deckLabel(other, catalog) +
                          " · " +
                          rate(m.deckA === d.id ? m.winRateA : m.winRateB) +
                          " · " +
                          m.valid +
                          " valid games"}
                        onclick={() =>
                          (selected =
                            selected === m.deckA + ":" + m.deckB
                              ? ""
                              : m.deckA + ":" + m.deckB)}
                        >{rate(
                          m.deckA === d.id ? m.winRateA : m.winRateB,
                        )}<small>{m.valid} games</small></button
                      >{:else}—{/if}</td
                  >{/each}</tr
              >{/each}</tbody
          >
        </table>
      </div>{:else}{#each decks as d}<details>
          <summary><DeckIdentity deck={d} {catalog} /></summary
          >{#each summary?.matchups.filter((m) => m.deckA === d.id || m.deckB === d.id) ?? [] as m}<button
              class="by-deck-match"
              onclick={() => (selected = m.deckA + ":" + m.deckB)}
              >{deckLabel(
                decks.find(
                  (x) => x.id === (m.deckA === d.id ? m.deckB : m.deckA),
                ),
                catalog,
              )} · {rate(m.deckA === d.id ? m.winRateA : m.winRateB)} · {m.valid}
              games</button
            >{/each}
        </details>{/each}{/if}
  </section>
  <details
    class="panel"
    ontoggle={(e) => {
      if (e.currentTarget.open && !triggers) void loadTriggers();
    }}
  >
    <summary>02 / Matchup Details</summary>{#each pairs as m}<article
        class="matchup-entry"
      >
        <h2>
          <DeckMatchup
            deckA={decks.find((d) => d.id === m.deckA)}
            deckB={decks.find((d) => d.id === m.deckB)}
            {catalog}
          />
        </h2>
        <div class="matchup-cards">
          {#each [m.deckA, m.deckB] as deckId}{@const a = deckId === m.deckA}
            <DeckResult
              deck={decks.find((d) => d.id === deckId)}
              {catalog}
              winRate={a ? m.winRateA : m.winRateB}
              wins={a ? m.winsA : m.winsB}
              losses={a ? m.winsB : m.winsA}
              draws={m.draws}
              interval={a ? m.intervalA : m.intervalB}
            >
              <p>
                On play: {rate(
                  (a ? m.playA : m.playB)
                    ? (a ? m.winsAOnPlay : m.winsBOnPlay) /
                        (a ? m.playA : m.playB)
                    : null,
                )} · On draw: {rate(
                  (a ? m.playB : m.playA)
                    ? (a ? m.winsAOnDraw : m.winsBOnDraw) /
                        (a ? m.playB : m.playA)
                    : null,
                )}
              </p>
              {#snippet actions()}<a
                  class="button"
                  href={`/workshop?deck=${deckId}&opponent=${a ? m.deckB : m.deckA}&baseline=${id}`}
                  >Modify &amp; Rerun</a
                >{/snippet}
            </DeckResult>{/each}
        </div>
        <p class="small muted">
          {m.valid} valid / {m.scheduled} scheduled · {m.failed} failed · {m.attemptFailures}
          historical failed attempts. Draws count as nonwins.
        </p>
        <div class="chart-grid">
          {#each [["Wins for first deck", m.deckA], ["Wins for second deck", m.deckB], ["Rules draws", "draw"]] as [title, winner]}<div
              class="chart"
            >
              <h3>{title}</h3>
              <div class="histogram">
                {#each distribution(slots.filter((s) => s.deckA === m.deckA && s.deckB === m.deckB && s.result && (winner === "draw" ? s.result.draw : s.result.winner === winner))) as [turn, count]}<div
                    class="histogram-bin"
                  >
                    <strong>{count}</strong>
                    <div style:height={Math.min(120, count * 15) + "px"}></div>
                    <span>{turn}</span>
                  </div>{/each}
              </div>
              <p class="small muted">
                Game ending {unit === "rounds" ? "round" : "actual turn"}
              </p>
            </div>{/each}
        </div>
        <details>
          <summary>Triggered abilities · recorded observations</summary
          >{#if !triggers}<p>
              Loading trigger summaries…
            </p>{:else}{#each triggers.filter( (t) => slots.some((s) => s.id === t.gameId) ) as t}<p
              >
                <a href={`/runs/${id}/games/${t.gameId}`}
                  >Game {t.gameId.slice(0, 8)}</a
                >
                · {t.events.length} recorded triggers
              </p>
              {#each t.events as event}<span class="tag"
                  >Turn {event.actualTurn} · {event.category}</span
                >{/each}{/each}
            <p class="small muted">
              Missing tracking remains unknown. Trigger counts alone do not
              measure value or pilot quality.
            </p>{/if}
        </details>
      </article>{/each}
  </details>
  <details class="panel" open>
    <summary>03 / The Field Over Time</summary>
    <p class="small muted">
      Later checkpoints include only completed games that reached them. Deck
      colors stay consistent across charts.
    </p>
    <div class="chart-grid">
      {#each metrics as [title, metric, help]}<SeriesChart
          {title}
          series={getPoints(metric)}
          {decks}
          {catalog}
          rounds={unit === "rounds"}
          percent={metric === "winRate"}
          help={help +
            " Later points can describe a smaller, different set of games."}
        />{/each}
    </div>
  </details>
  <a class="button" href={`/runs/${id}/logs/games`}>Open Game Evidence →</a
  >{/if}
