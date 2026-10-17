<script lang="ts">
  import { onMount } from "svelte";
  import { api, poll, loadSaved, save, idempotencyKey } from "../lib/api.ts";
  import { active, deckLabel, duration, label } from "../lib/data.ts";
  import type {
    Catalog,
    Snapshot,
    Resource,
    RunRequest,
    Estimate,
    History,
  } from "../lib/types.ts";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import AppSelect from "../lib/AppSelect.svelte";
  import AiNote from "../lib/AiNote.svelte";
  import PageHeader from "../lib/PageHeader.svelte";
  import DeckList from "../lib/DeckList.svelte";
  import SimulatedRate from "../lib/SimulatedRate.svelte";
  import { pilotLabel } from "../lib/pilot-label.ts";
  import { latestSimulatedRates } from "../lib/simulated-rates.ts";
  let {
    catalog,
    navigate,
    refreshCatalog,
  }: {
    catalog: Catalog | undefined;
    navigate: (path: string) => void;
    refreshCatalog: () => Promise<void>;
  } = $props();
  let snapshot = $state<Snapshot>(),
    history = $state<History>(),
    resources = $state<Resource>(),
    error = $state(""),
    busy = $state(false),
    loading = $state(true),
    estimate = $state<Estimate>(),
    preview = $state("");
  let providers = $state<{id: string; capabilities: {available: boolean; blockers: string[]}; cohorts: string[]}[]>([]);
  let providerId = $state("forge"), pyreCohort = $state(""), capture = $state("summary");
  type NativeCoverage = {deckIds: string[]; pilots: string[]; legalityAsOf: string};
  let nativeCoverage = $state<NativeCoverage>();
  let inventoryKey = $derived(snapshot?.decks.map((d) => d.manifest.id).sort().join("|") ?? "");
  const coverageBlocked = (id: string) => providerId === "pyre" ? !nativeCoverage?.deckIds.includes(id) : snapshot?.decks.find((d) => d.manifest.id === id)?.coverage.status === "blocked";
  $effect(() => {
    const cohort = pyreCohort, engine = providerId, inventory = inventoryKey;
    nativeCoverage = undefined;
    if (engine !== "pyre" || !cohort || !inventory) return;
    const controller = new AbortController();
    void api<NativeCoverage>(`/api/v1/providers/pyre/cohorts/${encodeURIComponent(cohort)}/coverage`, {signal:controller.signal})
      .then((value) => { nativeCoverage = value; })
      .catch((e) => { if (e.name !== "AbortError") error = e.message; });
    return () => controller.abort();
  });
  let maximumDecks = $derived(providerId === "pyre" ? 2 : catalog?.maximumDecks ?? 10);
  let providerCapability = $derived(providerId === "forge" ? snapshot?.capabilities : providers.find((p) => p.id === providerId)?.capabilities);
  let expandedGroups = $state<Record<string, boolean>>({});
  let simulatedRates = $derived(latestSimulatedRates(history?.sessions ?? []));
  const saved = loadSaved("home", {
    selected: [] as string[],
    games: 200,
    workers: 2,
    timeout: 180,
    turnLimit: 150,
    seed: "42",
    performance: false,
    pilot: "",
  });
  let selected = $state<string[]>(saved.selected),
    games = $state(saved.games),
    workers = $state(saved.workers),
    timeout = $state(saved.timeout),
    turnLimit = $state(saved.turnLimit),
    seed = $state(saved.seed),
    performance = $state(saved.performance),
    pilot = $state(saved.pilot);
  let locked = $derived(
    snapshot?.activeSessions.some((p) => active(p.run)) ?? false,
  );
  let total = $derived(((selected.length * (selected.length - 1)) / 2) * games);
  let validSettings = $derived(
    games >= 2 &&
      games <= 1000 &&
      games % 2 === 0 &&
      workers >= 1 &&
      workers <= 4 &&
      timeout >= 10 &&
      timeout <= 1800 &&
      turnLimit >= 10 &&
      turnLimit <= 1000 &&
      /^-?\d+$/.test(seed) &&
      BigInt(seed) >= -9223372036854775808n &&
      BigInt(seed) <= 9223372036854775807n,
  );
  let request = $derived<RunRequest>({
    deckIds: selected,
    providerId,
    pyreCohort: pyreCohort || undefined,
    capture,
    gamesPerPair: games,
    workers,
    timeoutSeconds: Math.round(timeout),
    turnLimit,
    rootSeed: seed,
    diagnostic: true,
    staggerStarts: workers > 1,
    estimatedGameSeconds: 15,
    startLeadSeconds: 5,
    startupSpacingSeconds: 1,
    performanceMode: providerId === "forge" && performance,
    pilotMode: pilot || catalog?.defaultPilotMode || "",
  });
  const group = (source: string, tier?: string) =>
    source === "custom"
      ? "Custom Decks"
      : source === "personal"
        ? "Your Deck"
        : tier
          ? "Tier " + tier
          : "Unrated";
  const groupOrder = new Map(
    ["Tier A", "Tier B", "Tier C", "Your Deck", "Custom Decks", "Unrated"].map(
      (name, index) => [name, index],
    ),
  );
  let groups = $derived(
    [
      ...new Set(
        snapshot?.decks.map((d) =>
          group(d.manifest.sourceType, d.manifest.archetypeTier),
        ) ?? [],
      ),
    ].sort(
      (a, b) =>
        (groupOrder.get(a) ?? Infinity) - (groupOrder.get(b) ?? Infinity) ||
        a.localeCompare(b),
    ),
  );
  let orderedDecks = $derived(
    [...(snapshot?.decks ?? [])].sort((a, b) => {
      const first = catalog?.decks.find((d) => d.id === a.manifest.id);
      const second = catalog?.decks.find((d) => d.id === b.manifest.id);
      return (first?.colors ?? "Unknown colors").localeCompare(
        second?.colors ?? "Unknown colors", undefined, { sensitivity: "base" },
      ) || deckLabel(a.manifest, catalog).localeCompare(
        deckLabel(b.manifest, catalog), undefined, { sensitivity: "base" },
      ) || a.manifest.id.localeCompare(b.manifest.id);
    }),
  );
  $effect(() =>
    save("home", {
      selected,
      games,
      workers,
      timeout,
      turnLimit,
      seed,
      performance,
      pilot,
    }),
  );
  $effect(() => {
    const payload = request;
    estimate = undefined;
    if (
      payload.deckIds.length < 2 ||
      !validSettings ||
      locked ||
      (payload.providerId === "forge" && !payload.pilotMode)
    )
      return;
    const controller = new AbortController();
    const timer = setTimeout(
      () =>
        void api<Estimate>("/api/v1/presentation/estimate", {
          method: "POST",
          body: payload,
          signal: controller.signal,
        })
          .then((v) => (estimate = v))
          .catch((e) => {
            if (e.name !== "AbortError") error = e.message;
          }),
      450,
    );
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  });
  async function load() {
    try {
      [snapshot, resources, history, providers] = await Promise.all([
        api<Snapshot>("/api/v1/ui-state"),
        api<Resource>("/api/v1/resources"),
        api<History>("/api/v1/run-history"),
        api<typeof providers>("/api/v1/providers").catch(() => []),
      ]);
      const filtered = selected
        .filter((id) =>
          snapshot?.decks.some(
            (d) => d.manifest.id === id && !coverageBlocked(id),
          ),
        )
        .slice(0, maximumDecks);
      if (filtered.join("|") !== selected.join("|")) selected = filtered;
      for (const row of snapshot.decks) {
        const name = group(row.manifest.sourceType, row.manifest.archetypeTier);
        if (!(name in expandedGroups))
          expandedGroups[name] = snapshot.decks.some((d) =>
            selected.includes(d.manifest.id) &&
            group(d.manifest.sourceType, d.manifest.archetypeTier) === name);
      }
      error = "";
    } catch (e) {
      error = (e as Error).message;
    } finally {
      loading = false;
    }
  }
  onMount(() => {
    void load();
    return poll(load);
  });
  function toggle(id: string) {
    if (locked || coverageBlocked(id)) return;
    if (selected.includes(id)) selected = selected.filter((d) => d !== id);
    else if (selected.length < (maximumDecks))
      selected = [...selected, id];
  }
  async function start() {
    if (busy || locked) return;
    busy = true;
    error = "";
    try {
      const result = await api<{ id: string }>(
        "/api/v1/runs?requireIdle=true",
        { method: "POST", body: request, key: idempotencyKey() },
      );
      navigate("/runs/" + result.id);
    } catch (e) {
      error = (e as Error).message;
    } finally {
      busy = false;
    }
  }
</script>

<svelte:head><title>Crucible — Standard Deck Testing</title></svelte:head>
{#snippet setupHeading()}<h1>
    Put Your Decks<br /><em>Across the Table.</em>
  </h1>{/snippet}
<PageHeader
  title="Put Your Decks Across the Table."
  eyebrow="THE LOCAL PLAYTESTING DESK"
  heading={setupHeading}
  description="Run complete head-to-head games. Follow every turn. Compare what happens when your deck has an opponent."
/>
<AiNote subject="setup" />
<div class="notice">
  <strong>Exploratory Mode</strong><span
    >The selected engine resolves the games. These rates describe the automated pilots, not
    your Arena win rate.</span
  >
</div>
{#if error}<div class="error" role="alert">
    {error}<button onclick={load}>Reconnect</button>
  </div>{/if}{#if loading}<p role="status">Loading your local decks…</p>{/if}
{#each providerCapability?.blockers ?? [] as blocker}<p class="error">
    {blocker}
  </p>{/each}
{#if locked}<section class="panel active-session-status">
    <h2>Session In Progress</h2>
    <p>Setup is locked while the current session finishes or pauses.</p>
    {#each snapshot?.activeSessions ?? [] as p}<h3>
        {label(p.run.status)} · {p.completed} / {p.run.total} completed
      </h3>
      <progress max={p.run.total} value={p.processed}></progress>
      <p>
        {p.running} running · {p.pending} waiting · {p.failed} failed · {p.cancelled}
        paused
      </p>
      <a class="button primary" href={"/runs/" + p.run.id}
        >View Active Session →</a
      >{/each}
  </section>{/if}
<fieldset class="setup-controls" disabled={locked}>
  <section class="panel">
    <div class="section-head">
      <div>
        <div class="eyebrow">01 / THE FIELD</div>
        <h2>Choose the Decks for a Round Robin</h2>
      </div>
      <span
        >{selected.length} / {maximumDecks} decks selected</span
      >
    </div>
    <p class="small muted">
      Every selected deck plays every other selected deck. {(selected.length *
        (selected.length - 1)) /
        2} matchups.
    </p>
    <p class="small muted">Select a deck tile to add or remove it. Use the group title to expand or collapse its decks.</p>
    <div class="chart-palette">
      {#each ["Mint", "Gold", "Sky Blue", "Coral", "Lavender", "Orange", "Rose", "Lime", "Cyan", "Stone"] as color, i}<span
          ><i style:background={"var(--theme-chart-" + (i + 1) + ")"}
          ></i>{color}</span
        >{/each}
    </div>
    {#if selected.length === (maximumDecks)}<p role="status">
        Deselect a deck to choose another.
      </p>{/if}
    {#each groups as name}<details
        class="deck-tier-group"
        bind:open={expandedGroups[name]}
      >
        <summary
          >{name}<span class="muted"
            >{snapshot?.decks.filter(
              (d) =>
                group(d.manifest.sourceType, d.manifest.archetypeTier) === name,
            ).length} decks</span
          ></summary
        >
        <div class="deck-grid">
          {#each orderedDecks.filter((d) => group(d.manifest.sourceType, d.manifest.archetypeTier) === name) as row (row.manifest.id)}{@const deck =
              row.manifest}
            <article
              class="deck-card"
              class:selected={selected.includes(deck.id)}
              class:blocked={coverageBlocked(deck.id)}
            >
              <button class="deck-card-choice"
                  type="button"
                  aria-label={deckLabel(deck, catalog)}
                  aria-pressed={selected.includes(deck.id)}
                  disabled={coverageBlocked(deck.id) ||
                    (!selected.includes(deck.id) &&
                      selected.length >= (maximumDecks))}
                  onclick={() => toggle(deck.id)}
                ><DeckIdentity {deck} {catalog} /><span class="deck-selection-state">{selected.includes(deck.id) ? "Selected" : "Select deck"}</span></button
              >
              <p class="small muted">
                {deck.creatureCount} creatures · {deck.landCount} lands{deck.archetypeTier
                  ? " · Tier " + deck.archetypeTier
                  : ""}
              </p>
              {#if deck.publishedWinRatePercent != null}<p class="small">
                  Published: {deck.publishedWinRatePercent}% · {deck.publishedMatchesDisplayed ??
                    "Unknown sample"} matches
                </p>{/if}
              <SimulatedRate result={simulatedRates.get(deck.id)} />
              <div class="deck-bottom">
                <span class="small"
                  >{coverageBlocked(deck.id)
                    ? providerId === "pyre" ? pyreCohort ? "Outside the selected cohort" : "Choose a Pyre cohort first" : "Missing card support"
                    : providerId === "pyre" ? "Cohort pilot" : pilotLabel(catalog?.decks.find((d) => d.id === deck.id)?.profiles[
                          pilot || catalog.defaultPilotMode
                        ] ?? "forge-default", catalog)}</span
                ><button
                  type="button"
                  aria-expanded={preview === deck.id}
                  aria-controls={"deck-list-" + deck.id}
                  onclick={() => (preview = preview === deck.id ? "" : deck.id)}
                  >Deck List</button
                >
              </div>
              {#if preview === deck.id}<div class="deck-preview" id={"deck-list-" + deck.id}>
                  <DeckList
                    cards={deck.cards}
                    manaValues={snapshot?.manaValues}
                  />
                </div>{/if}
            </article>{/each}
        </div>
      </details>{/each}
  </section>
  <section class="panel">
    <div class="eyebrow">02 / THE SESSION</div>
    <h2>Set Up the Run</h2>
    <p class="small muted">
      {providerId === "pyre" ? "Pyre" : snapshot?.capabilities.engine ?? "Forge"} · Even play / draw split
    </p>
    <div class="config-grid">
      <label
        >Games per Matchup<input
          type="number"
          bind:value={games}
          min="2"
          max="1000"
          step="2"
        /></label
      >
      <div>
        <label for="parallel-games">Parallel Games</label><AppSelect
          id="parallel-games"
          label="Parallel Games"
          value={String(workers)}
          onchange={(v) => (workers = +v)}
          options={[1, 2, 3, 4].map((n) => ({
            value: String(n),
            label: String(n),
          }))}
        /><small
          >{providerId === "forge" ? "Allow about 1.2 GB per game, plus app and engine overhead." : "Native workers use the registered memory limit."} {resources?.logicalProcessors ??
            "—"} logical processors.</small
        >
      </div>
    </div>
    <details>
      <summary>Advanced Options</summary>
      <div class="config-grid">
        <div><label for="engine-provider">Engine</label><AppSelect id="engine-provider" label="Engine" value={providerId}
          onchange={(v) => { providerId = v; selected = []; estimate = undefined; }}
          options={[{value:"forge",label:"Forge"},{value:"pyre",label:"Pyre"}]} />
          {#if providerId === "pyre"}
            <small>{providers.find((p) => p.id === "pyre")?.capabilities.blockers.join(" ") || "Select an exact registered cohort and its two frozen decks."}</small>
          {/if}
        </div>
        {#if providerId === "pyre"}
          <div><label for="pyre-cohort">Supported cohort</label><AppSelect id="pyre-cohort" label="Supported cohort" value={pyreCohort}
            onchange={(v) => (pyreCohort = v)} options={[{value:"",label:"Select a cohort"},...(providers.find((p) => p.id === "pyre")?.cohorts ?? []).map((value) => ({value,label:value}))]} />
            <small>Pilots and Module versions are pinned by the cohort registration.</small></div>
          <div><label for="pyre-capture">Evidence</label><AppSelect id="pyre-capture" label="Evidence" value={capture} onchange={(v) => (capture = v)}
            options={[{value:"summary",label:"Summary metrics"},{value:"full",label:"Full Pyre trace"}]} />
            <small>Summary games have no replay. Full capture uses Pyre diagnostics.</small></div>
        {/if}
        {#if providerId === "forge"}<div>
          <label for="pilot-policy">Pilot policy</label><AppSelect
            id="pilot-policy"
            label="Pilot policy"
            options={catalog?.modes ?? []}
            value={pilot || catalog?.defaultPilotMode || ""}
            onchange={(v) => (pilot = v)}
          /><small
            >{catalog?.modes.find(
              (m) => m.value === (pilot || catalog.defaultPilotMode),
            )?.description}</small
          >
        </div>{/if}
        <label
          >Time limit per game (seconds)<input
            type="number"
            bind:value={timeout}
            min="10"
            max="1800"
          /></label
        ><label>Root Seed<input inputmode="numeric" bind:value={seed} /></label
        ><label
          >Maximum Actual Player Turns<input
            type="number"
            bind:value={turnLimit}
            min="10"
            max="1000"
          /></label
        >{#if providerId === "forge"}<label
          ><input type="checkbox" bind:checked={performance} /> Forge
          Performance Mode<small
            >Skips selected Forge checks. Stored and reused separately.</small
          ></label
        >{/if}
      </div>
      <p class="small muted">
        London mulligans approximate Arena Bo1 opening hands. Timeouts and turn
        limits are incomplete attempts, never rules draws.
      </p>
    </details>
    {#if !validSettings}<p class="error" role="status">
        Use an even game count from 2–1,000, 1–4 workers, 10–1,800 seconds,
        10–1,000 turns, and a signed 64-bit integer seed.
      </p>{/if}
    <div class="runtime-estimate" aria-live="polite">
      {#if estimate?.preflight.cachedGames === total && total > 0}<strong
          >No new games needed</strong
        >
        <p>Stored results are ready.</p>{:else if estimate?.runtime}<span
          >Rough runtime</span
        ><strong
          >{duration(estimate.runtime.lowerSeconds)}–{duration(
            estimate.runtime.upperSeconds,
          )}</strong
        >
        <p class="small muted">
          Advisory range from {estimate.runtime.observations} observed attempts. Conservative
          estimate includes scheduled games; cached results can finish sooner.
        </p>{:else}<p class="small muted">
          {providerId === "pyre" ? "Pyre runtime estimates are not yet calibrated. Preflight checks the selected cohort." : "Choose at least two decks to estimate runtime."}
        </p>{/if}{#each estimate?.preflight.blockers ?? [] as blocker}<p
          class="error"
        >
          {blocker}
        </p>{/each}{#if estimate?.preflight.cachedGames}<p>
          {estimate.preflight.cachedGames} stored games available for these exact
          settings.
        </p>{/if}
    </div>
    <div class="run-bar">
      <div>
        <strong>{(selected.length * (selected.length - 1)) / 2} matchups</strong
        >
        · {total} full games<small
          >{games / 2} starts per deck in each matchup · No sideboarding</small
        >
      </div>
      <button
        class="primary"
        disabled={locked ||
          busy ||
          !validSettings ||
          selected.length < 2 ||
          providerCapability?.available !== true ||
          !estimate ||
          estimate.preflight.blockers.length > 0}
        onclick={start}
        >{busy
          ? "Preparing…"
          : estimate?.preflight.cachedGames === total && total > 0
            ? "Use Stored Results"
            : "Run Exploratory Games"}</button
      >
    </div>
  </section>
</fieldset>
{#if snapshot?.runs[0]}<a
    class="recent-link"
    href={"/runs/" + snapshot.runs[0].id}
    ><span>YOUR LATEST SESSION</span><strong
      >{snapshot.runs[0].total} games · {label(snapshot.runs[0].status)}</strong
    ><span>View Results →</span></a
  >{/if}
