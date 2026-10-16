<script lang="ts">
  import { onMount } from "svelte";
  import { api, poll } from "../lib/api.ts";
  import {
    active,
    date,
    deckLabel,
    label,
    performance,
    rate,
    runtime,
  } from "../lib/data.ts";
  import type { Catalog, History } from "../lib/types.ts";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import AppSelect from "../lib/AppSelect.svelte";
  import Provenance from "../lib/Provenance.svelte";
  import AiNote from "../lib/AiNote.svelte";
  import PageHeader from "../lib/PageHeader.svelte";
  let { catalog }: { catalog: Catalog | undefined } = $props();
  let data = $state<History>(),
    error = $state(""),
    search = $state(""),
    filter = $state("all"),
    sort = $state("newest"),
    page = $state(0);
  let filtered = $derived(
    (data?.sessions ?? [])
      .filter((s) => {
        const run = s.progress.run;
        return (
          (
            run.id +
            " " +
            run.config.deckIds
              .map((id) => {
                const d = data?.decks.find((d) => d.id === id);
                return deckLabel(d, catalog) + " " + d?.name;
              })
              .join(" ")
          )
            .toLowerCase()
            .includes(search.toLowerCase()) &&
          (filter === "all" ||
            (filter === "active" && active(run)) ||
            (filter === "completed" && run.status === "completed") ||
            (filter === "paused" && run.status === "cancelled") ||
            (filter === "attention" &&
              (run.status === "failed" || s.progress.failed > 0)))
        );
      })
      .toSorted((a, b) =>
        sort === "oldest"
          ? a.progress.run.createdAt.localeCompare(b.progress.run.createdAt)
          : sort === "results"
            ? b.progress.completed - a.progress.completed
            : b.progress.run.createdAt.localeCompare(a.progress.run.createdAt),
      ),
  );
  $effect(() => {
    search;
    filter;
    sort;
    page = 0;
  });
  async function load() {
    try {
      data = await api<History>("/api/v1/run-history");
      error = "";
    } catch (e) {
      error = (e as Error).message;
    }
  }
  onMount(() => {
    void load();
    return poll(load);
  });
</script>

<svelte:head><title>Run History — Crucible</title></svelte:head>
{#snippet historyActions()}<a class="button primary" href="/">New Session</a
  >{/snippet}
<PageHeader
  title="Run History."
  eyebrow="YOUR LOCAL PLAYTESTS"
  description="Track your sessions. Compare what happened."
  actions={historyActions}
/>
<p class="small muted">
  Exploratory Forge results · Win rates describe these local games, not Arena
  forecasts.
</p>
<AiNote subject="history" />{#if error}<p class="error" role="alert">
    {error}
  </p>{/if}
<div class="history-overview">
  <div>
    <strong>{data?.sessions.length ?? "—"}</strong><span>Sessions</span>
  </div>
  <div>
    <strong
      >{data?.sessions.filter((s) => active(s.progress.run)).length ??
        "—"}</strong
    ><span>In progress</span>
  </div>
  <div>
    <strong
      >{data?.sessions.filter((s) => s.progress.run.status === "completed")
        .length ?? "—"}</strong
    ><span>Completed</span>
  </div>
  <div>
    <strong
      >{data?.sessions.filter(
        (s) => s.progress.failed > 0 || s.progress.run.status === "failed",
      ).length ?? "—"}</strong
    ><span>Need attention</span>
  </div>
</div>
<div class="toolbar">
  <label
    >Find a session<input
      type="search"
      bind:value={search}
      placeholder="Colors, subtype, variant, or session ID"
    /></label
  >
  <div>
    <label for="history-status">Status</label><AppSelect
      id="history-status"
      label="Status"
      bind:value={filter}
      options={[
        ["all", "All sessions"],
        ["active", "In progress"],
        ["completed", "Completed"],
        ["paused", "Paused"],
        ["attention", "Needs attention"],
      ].map(([value, label]) => ({ value, label }))}
    />
  </div>
  <div>
    <label for="history-sort">Sort</label><AppSelect
      id="history-sort"
      label="Sort"
      bind:value={sort}
      options={[
        ["newest", "Newest first"],
        ["oldest", "Oldest first"],
        ["results", "Most valid games"],
      ].map(([value, label]) => ({ value, label }))}
    />
  </div>
</div>
<p class="small muted">
  {filtered.length} of {data?.sessions.length ?? 0} sessions
</p>
{#if !data}<p role="status">
    Loading session history…
  </p>{:else if !filtered.length}<section class="empty">
    <h2>No Matching Sessions.</h2>
    <a href="/">Set up a run →</a>
  </section>{/if}
{#each filtered.slice(page * 8, page * 8 + 8) as session (session.progress.run.id)}{@const p =
    session.progress}{@const run = p.run}
  <article class="history-session">
    <div class="history-session-head">
      <div class="history-identity">
        <div class="history-date">
          {date(run.createdAt)} · {run.workshopDeckId ? "Workshop" : "Playtest"}
        </div>
        <h2>
          <a href={"/runs/" + run.id}
            >{run.config.deckIds.length === 2 ? "Head-to-head" : "Round robin"}
            <span class="muted">· {run.config.deckIds.length} decks</span></a
          >
        </h2>
        <p>
          {(run.config.deckIds.length * (run.config.deckIds.length - 1)) / 2} matchups
          · {run.config.gamesPerPair} games per matchup
        </p>
      </div>
      <span class="pill">{label(run.status)}</span>
    </div>
    <div class="history-session-body">
      <div>
        <div class="history-results-count">
          <strong
            >{p.completed.toLocaleString()}<small>
              / {run.total.toLocaleString()}</small
            ></strong
          ><span>valid games</span>
        </div>
        <progress max={Math.max(1, run.total)} value={p.completed}></progress>
        <p class="small muted">
          {p.running} running · {p.pending} waiting · {p.failed} failed · {p.cancelled}
          paused
        </p>
        <p>{runtime(session.timings)}</p>
        {#if p.reused}<p class="small muted">
            Includes {p.reused} reused results
          </p>{/if}
      </div>
      <div class="table-scroll">
        <table class="history-results-table">
          <thead
            ><tr
              ><th>Deck</th><th
                >{run.config.deckIds.length > 2
                  ? "Mean opponent rate"
                  : "Win rate"}</th
              ><th>W / L / D</th></tr
            ></thead
          ><tbody
            >{#each performance(session) as deck}<tr
                ><th
                  ><DeckIdentity
                    deck={data?.decks.find((d) => d.id === deck.id)}
                    {catalog}
                    compact
                  /></th
                ><td
                  ><div class="history-rate">
                    <div class="history-win-track">
                      <span style:width={(deck.rate ?? 0) * 100 + "%"}></span>
                    </div>
                    <strong>{rate(deck.rate)}</strong>
                  </div></td
                ><td>{deck.wins} / {deck.losses} / {deck.draws}</td></tr
              >{/each}</tbody
          >
        </table>
        {#if run.config.deckIds.length > 2}<p class="small muted">
            Each observed opponent has equal weight. Missing matchups are
            excluded.
          </p>{/if}
      </div>
    </div>
    {#if run.error}<p class="error">{run.error}</p>{/if}
    <div class="history-session-footer">
      <Provenance
        {run}
        {catalog}
        decks={data?.decks ?? []}
        capability={data?.capabilities}
      /><a href={"/runs/" + run.id}
        >View {active(run) ? "Progress" : "Results"} →</a
      >
    </div>
  </article>{/each}
{#if filtered.length > 8}<div class="pagination">
    <button disabled={page === 0} onclick={() => page--}>Previous</button><span
      >Page {page + 1} of {Math.ceil(filtered.length / 8)}</span
    ><button disabled={(page + 1) * 8 >= filtered.length} onclick={() => page++}
      >Next</button
    >
  </div>{/if}
