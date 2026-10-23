<script lang="ts">
  import { onMount, tick, untrack } from "svelte";
  import { api, poll } from "../lib/api.ts";
  import { deckLabel, date, performance, rate } from "../lib/data.ts";
  import type {
    Catalog,
    Library,
    History,
    EngineSupport,
  } from "../lib/types.ts";
  import {
    engineLabel,
    loadEngineSupport,
    supportedCards,
    supportedMatchups,
  } from "../lib/engine-support.ts";
  import EngineSupportBadges from "../lib/EngineSupportBadges.svelte";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import LazyDeckList from "../lib/LazyDeckList.svelte";
  import AppSelect from "../lib/AppSelect.svelte";
  import AiNote from "../lib/AiNote.svelte";
  import PageHeader from "../lib/PageHeader.svelte";
  import SimulatedRate from "../lib/SimulatedRate.svelte";
  import { latestSimulatedRates } from "../lib/simulated-rates.ts";
  let {
    catalog,
    route,
    refreshCatalog,
  }: {
    catalog: Catalog | undefined;
    route: string;
    refreshCatalog: () => Promise<void>;
  } = $props();
  let library = $state<Library>(),
    history = $state<History>(),
    error = $state(""),
    message = $state(""),
    query = $state(""),
    choice = $state(
      untrack(() => new URLSearchParams(route.split("?")[1]).get("deck") ?? ""),
    ),
    busy = $state(false);
  let engines = $state<EngineSupport[]>(),
    engineFilter = $state("all"),
    cardQuery = $state("");
  let cardSupport = $derived(supportedCards(engines ?? []));
  let simulatedRates = $derived(latestSimulatedRates(history?.sessions ?? []));
  let rows = $derived(
    (library?.decks ?? [])
      .filter(
        (r) =>
          engineFilter === "all" ||
          (engineFilter === "forge"
            ? r.coverage.status === "exploratory"
            : (engines ?? []).some(
                (e) =>
                  e.id === engineFilter &&
                  supportedMatchups(r.manifest, e).length > 0,
              )),
      )
      .filter((r) =>
        (deckLabel(r.manifest, catalog) + " " + r.manifest.name)
          .toLowerCase()
          .includes(query.toLowerCase()),
      )
      .toSorted((a, b) =>
        deckLabel(a.manifest, catalog).localeCompare(
          deckLabel(b.manifest, catalog),
        ),
      ),
  );
  async function load() {
    try {
      [library, history, engines] = await Promise.all([
        api<Library>("/api/v1/library-state"),
        api<History>("/api/v1/run-history"),
        loadEngineSupport(),
      ]);
      error = "";
    } catch (e) {
      error = (e as Error).message;
    }
  }
  async function jump(value: string) {
    choice = value;
    await tick();
    document
      .getElementById("deck-" + value)
      ?.scrollIntoView({ block: "start", behavior: "smooth" });
  }
  async function refresh() {
    busy = true;
    try {
      await api("/api/v1/imports", { method: "POST", body: {} });
      await load();
      await refreshCatalog();
      message = "Source lists checked. Historical manifests remain unchanged.";
    } catch (e) {
      error = (e as Error).message;
    } finally {
      busy = false;
    }
  }
  onMount(() => {
    void load().then(() => {
      if (choice) void jump(choice);
    });
    return poll(load);
  });
</script>

<svelte:head><title>Deck Library — Crucible</title></svelte:head>
{#snippet libraryActions()}<button disabled={busy} onclick={refresh}
    >{busy ? "Checking…" : "Refresh Sources"}</button
  >{/snippet}
<PageHeader
  title="Deck Library."
  eyebrow="THE LIBRARY / EXACT DECK VERSIONS"
  description="Know what is across the table."
  actions={libraryActions}
/>
<AiNote subject="decks" />{#if error}<p class="error" role="alert">
    {error}
  </p>{/if}{#if message}<p role="status">{message}</p>{/if}
<p class="small muted">
  Each deck shows coverage by engine. Forge exploratory coverage means card
  scripts are present. Pyre support validates the exact list only for the
  matchups shown. The run screen currently executes through Forge.
</p>
{#if engines?.length}<section class="panel" aria-label="Engine support">
    <h2>Engine support</h2>
    {#each engines as engine (engine.id)}<p>
        <strong>{engineLabel(engine.providerId, engine.engineVersion)}</strong>
        · {engine.cards.filter((c) => c.kind === "Card").length} card definitions
        · {engine.cards.filter((c) => c.kind === "Token").length} generated tokens
        · {engine.matchups.length} validated matchups ·
        {engine.target}
      </p>
      <details>
        <summary>Validated matchups and dates</summary
        >{#each engine.matchups as matchup (matchup.id)}<p>
            {matchup.label} · eligibility snapshot {matchup.eligibilityAsOf}
          </p>{/each}
      </details>
      <details>
        <summary>Supported cards and generated tokens</summary>
        <label
          >Find a supported card<input
            type="search"
            bind:value={cardQuery}
          /></label
        >
        <ul>
          {#each engine.cards.filter((c) => c.name
              .toLowerCase()
              .includes(cardQuery.toLowerCase())) as card (card.name)}<li>
              {card.name}{card.kind === "Token" ? " · generated token" : ""}
            </li>{/each}
        </ul>
      </details>
    {/each}
    <p class="small muted">
      Coverage applies to these exact lists and matchup interactions. Engine
      execution is configured separately; card coverage does not validate every
      deck combination.
    </p>
  </section>{/if}
<div class="toolbar">
  <div>
    <label for="library-engine">Filter by engine</label>
    <AppSelect
      id="library-engine"
      label="Filter by engine"
      value={engineFilter}
      onchange={(value) => (engineFilter = value)}
      options={[
        { value: "all", label: "All decks · all engines" },
        { value: "forge", label: "Forge · exploratory script coverage" },
        ...(engines ?? []).map((e) => ({
          value: e.id,
          label:
            engineLabel(e.providerId, e.engineVersion) +
            " · validated matchups",
        })),
      ]}
    />
  </div>
  <label
    >Find a deck<input
      type="search"
      bind:value={query}
      placeholder="Colors, subtype, variant, or source name"
    /></label
  >
  <div>
    <label for="library-jump">Jump to deck</label><AppSelect
      id="library-jump"
      label="Jump to deck"
      value={choice}
      onchange={jump}
      options={[
        { value: "", label: "Choose a deck" },
        ...rows.map((r) => ({
          value: r.manifest.id,
          label: deckLabel(r.manifest, catalog),
        })),
      ]}
    />
  </div>
</div>
{#if !library}<p role="status">
    Loading library…
  </p>{/if}{#each rows as { manifest: deck, coverage } (deck.id)}<section
    class="panel library-deck"
    id={"deck-" + deck.id}
  >
    <div class="section-head">
      <h2><DeckIdentity {deck} {catalog} /></h2>
      <a class="button" href={"/workshop?deck=" + deck.id}>Modify in Workshop</a
      >
    </div>
    <p class="small muted">
      {deck.sourceType} · {deck.mainCount} main / {deck.sideCount} sideboard · {deck.landCount}
      lands · {deck.creatureCount} creatures{deck.archetypeTier
        ? " · Tier " + deck.archetypeTier
        : ""}
    </p>
    {#if deck.publishedWinRatePercent != null}<details>
        <summary
          >Published source win rate: {deck.publishedWinRatePercent}%</summary
        >
        <p>
          {deck.publishedMatchesDisplayed ?? "Unknown sample"} matches · {deck.tierSnapshotDate ??
            deck.snapshot}. This source rate is separate from local exploratory
          head-to-head games.
        </p>
        {#if deck.tierSourceUrl}<a
            href={deck.tierSourceUrl}
            target="_blank"
            rel="noreferrer">Source →</a
          >{/if}
      </details>{/if}
    <SimulatedRate result={simulatedRates.get(deck.id)} />
    <p class="small">{deck.freshnessNote}</p>
    <EngineSupportBadges {deck} {coverage} {engines} details />
    {#if coverage.status === "blocked"}<div class="error">
        <p>Forge card support missing</p>
        {#each coverage.cards.filter((c) => c.status === "missing") as card}<p>
            {card.name}: {card.reason}
          </p>{/each}
      </div>{/if}<LazyDeckList
      {deck}
      manaValues={library?.manaValues}
      initialOpen={choice === deck.id}
      engineSupport={cardSupport}
    />
    <details>
      <summary>Local sessions and exact source</summary
      >{#each history?.sessions.filter( (s) => s.progress.run.config.deckIds.includes(deck.id) ) ?? [] as session}{@const p =
          performance(session).find((p) => p.id === deck.id)}
        <p>
          <a href={"/runs/" + session.progress.run.id}
            >{date(session.progress.run.createdAt)} · {rate(p?.rate)} · {p?.wins ??
              0}W / {p?.losses ?? 0}L / {p?.draws ?? 0}D</a
          >
        </p>{/each}
      <dl class="metadata">
        <dt>Source name</dt>
        <dd>{deck.name}</dd>
        <dt>Exact deck version</dt>
        <dd><code>{deck.id}</code></dd>
        <dt>List hash</dt>
        <dd><code>{deck.listHash}</code></dd>
        <dt>Manifest hash</dt>
        <dd><code>{deck.manifestHash}</code></dd>
        <dt>Imported</dt>
        <dd>{date(deck.importedAt)}</dd>
      </dl>
    </details>
  </section>{/each}
{#if library && !rows.length}<p role="status">
    No decks match this search and engine filter.
  </p>{/if}
