<script lang="ts">
  import { onMount, tick, untrack } from "svelte";
  import { api, poll } from "../lib/api.ts";
  import { deckLabel, date, performance, rate } from "../lib/data.ts";
  import type { Catalog, Library, History } from "../lib/types.ts";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import LazyCardList from "../lib/LazyCardList.svelte";
  import AppSelect from "../lib/AppSelect.svelte";
  import AiNote from "../lib/AiNote.svelte";
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
  let rows = $derived(
    (library?.decks ?? [])
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
      [library, history] = await Promise.all([
        api<Library>("/api/v1/library-state"),
        api<History>("/api/v1/run-history"),
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
<div class="eyebrow">THE LIBRARY / EXACT DECK VERSIONS</div>
<div class="section-head">
  <div>
    <h1>Deck Library.</h1>
    <p class="lead">Know what is across the table.</p>
  </div>
  <button disabled={busy} onclick={refresh}
    >{busy ? "Checking…" : "Refresh Sources"}</button
  >
</div>
<AiNote subject="decks" />{#if error}<p class="error" role="alert">
    {error}
  </p>{/if}{#if message}<p role="status">{message}</p>{/if}
<div class="toolbar">
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
    <p class="small">{deck.freshnessNote}</p>
    {#if coverage.status === "blocked"}<div class="error">
        {#each coverage.cards.filter((c) => c.status === "blocked") as card}<p>
            {card.name}: {card.reason}
          </p>{/each}
      </div>{/if}<LazyCardList
      {deck}
      manaValues={library?.manaValues}
      initialOpen={choice === deck.id}
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
