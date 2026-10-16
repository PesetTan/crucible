<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { api, poll, loadSaved, save, idempotencyKey } from "../lib/api.ts";
  import { active, deckLabel, date } from "../lib/data.ts";
  import type {
    Catalog,
    Card,
    Deck,
    WorkshopSnapshot,
    Validation,
    WorkshopMatch,
    RunRequest,
  } from "../lib/types.ts";
  import AppSelect from "../lib/AppSelect.svelte";
  import DeckResult from "../lib/DeckResult.svelte";
  import AiNote from "../lib/AiNote.svelte";
  import DeckList from "../lib/DeckList.svelte";
  import CardMana from "../lib/CardMana.svelte";
  import CardRules from "../lib/CardRules.svelte";
  import PageHeader from "../lib/PageHeader.svelte";
  let {
    catalog,
    route,
    navigate,
    refreshCatalog,
  }: {
    catalog: Catalog | undefined;
    route: string;
    navigate: (p: string) => void;
    refreshCatalog: () => Promise<void>;
  } = $props();
  const params = untrack(() => new URLSearchParams(route.split("?")[1]));
  const draftKey = untrack(
    () =>
      "workshop:" +
      ["deck", "opponent", "baseline", "baselineDeck", "result"]
        .map((key) => params.get(key) ?? "")
        .join(":"),
  );
  const saved = loadSaved(draftKey, {
    name: "Custom Deck",
    parentId: "",
    cards: [] as Card[],
    sideboard: [] as Card[],
    opponent: "",
    baseline: "",
    baselineDeck: "",
    latest: "",
    latestDeckId: "",
    savedId: "",
    games: 200,
    workers: 2,
    timeout: 180,
    turnLimit: 150,
    seed: "42",
    performance: false,
    pilot: "",
    stagger: true,
    spacing: 1 as number | null,
    estimatedSeconds: 15,
    leadSeconds: 5,
  });
  let name = $state(saved.name),
    parentId = $state(saved.parentId),
    cards = $state<Card[]>(saved.cards),
    sideboard = $state<Card[]>(saved.sideboard),
    opponent = $state(saved.opponent),
    baseline = $state(saved.baseline),
    baselineDeck = $state(saved.baselineDeck),
    latest = $state(saved.latest),
    latestDeckId = $state(saved.latestDeckId),
    savedId = $state(saved.savedId),
    games = $state(saved.games),
    workers = $state(saved.workers),
    timeout = $state(saved.timeout),
    turnLimit = $state(saved.turnLimit),
    seed = $state(saved.seed),
    performance = $state(saved.performance),
    pilot = $state(saved.pilot),
    stagger = $state(saved.stagger),
    spacing = $state(saved.spacing),
    estimatedSeconds = $state(saved.estimatedSeconds),
    leadSeconds = $state(saved.leadSeconds);
  let data = $state<WorkshopSnapshot>(),
    validation = $state<Validation>(),
    error = $state(""),
    message = $state(""),
    busy = $state(false),
    source = $state(""),
    paste = $state(""),
    query = $state(""),
    results = $state<Card[]>([]),
    issues = $state<string[]>([]),
    baselineMatch = $state<WorkshopMatch>(),
    editedMatch = $state<WorkshopMatch>(),
    comparisonError = $state("");
  let validationTicket = 0;
  let locked = $derived(
    data?.activeSessions.some((p) => active(p.run)) ?? false,
  );
  let request = $derived({
    name,
    parentDeckId: parentId || null,
    cards: cards.map((c) => ({ arenaId: c.arenaId, quantity: c.quantity })),
    sideboard: sideboard.map((c) => ({
      arenaId: c.arenaId,
      quantity: c.quantity,
    })),
  });
  let count = $derived(cards.reduce((n, c) => n + c.quantity, 0));
  $effect(() =>
    save(draftKey, {
      name,
      parentId,
      cards,
      sideboard,
      opponent,
      baseline,
      baselineDeck,
      latest,
      latestDeckId,
      savedId,
      games,
      workers,
      timeout,
      turnLimit,
      seed,
      performance,
      pilot,
      stagger,
      spacing,
      estimatedSeconds,
      leadSeconds,
    }),
  );
  $effect(() => {
    const body = request;
    if (!body.cards.length) return;
    const ticket = ++validationTicket;
    const c = new AbortController();
    const timer = setTimeout(() => {
      void api<Validation>("/api/v1/decks/validate", {
        method: "POST",
        body,
        signal: c.signal,
      })
        .then((v) => {
          if (ticket === validationTicket) validation = v;
        })
        .catch((e) => {
          if (e.name !== "AbortError") error = e.message;
        });
    }, 250);
    return () => {
      clearTimeout(timer);
      c.abort();
    };
  });
  async function load() {
    try {
      data = await api<WorkshopSnapshot>("/api/v1/workshop-state");
      error = "";
      if (latest && latestDeckId && opponent)
        editedMatch = await api(
          `/api/v1/runs/${latest}/workshop?deckId=${latestDeckId}&opponentId=${opponent}`,
        );
    } catch (e) {
      error = (e as Error).message;
    }
  }
  function copy(deck: Deck) {
    source = deck.id;
    parentId = deck.id;
    name = deck.sourceType === "custom" ? deck.name : deck.name + " — Workshop";
    cards = deck.cards
      .filter((c) => c.zone === "MainDeck")
      .map((c) => ({ ...c }));
    sideboard = deck.cards
      .filter((c) => c.zone === "Sideboard")
      .map((c) => ({ ...c }));
    savedId = "";
    validation = undefined;
    issues = [];
    message = "";
  }
  function settings(runId: string) {
    const c = data?.runs.find((r) => r.id === runId)?.config;
    if (c) {
      games = c.gamesPerPair;
      workers = c.workers;
      timeout = c.timeoutSeconds;
      turnLimit = c.turnLimit;
      seed = String(c.rootSeed);
      performance = c.performanceMode;
      pilot = c.pilot;
      stagger = c.staggerStarts;
      spacing = c.startupSpacingSeconds;
      estimatedSeconds = c.estimatedGameSeconds;
      leadSeconds = c.startLeadSeconds;
    }
  }
  async function context() {
    if (cards.length || parentId) {
      await compare();
      return;
    }
    const d = params.get("deck");
    if (d && data?.decks.some((deck) => deck.id === d)) {
      copy(data.decks.find((deck) => deck.id === d)!);
      opponent = params.get("opponent") ?? "";
      baseline = params.get("baseline") ?? "";
      baselineDeck = params.get("baselineDeck") ?? d;
      latest = params.get("result") ?? "";
      if (latest) {
        savedId = d;
        latestDeckId = d;
        settings(latest);
      } else if (baseline) settings(baseline);
      await compare();
    }
  }
  async function compare() {
    comparisonError = "";
    try {
      baselineMatch =
        baseline && baselineDeck && opponent
          ? await api(
              `/api/v1/runs/${baseline}/workshop?deckId=${baselineDeck}&opponentId=${opponent}`,
            )
          : undefined;
      editedMatch =
        latest && latestDeckId && opponent
          ? await api(
              `/api/v1/runs/${latest}/workshop?deckId=${latestDeckId}&opponentId=${opponent}`,
            )
          : undefined;
    } catch (e) {
      comparisonError = (e as Error).message;
    }
  }
  onMount(() => {
    void load().then(context);
    return poll(load);
  });
  async function action(fn: () => Promise<void>) {
    busy = true;
    error = "";
    message = "";
    try {
      await fn();
    } catch (e) {
      error = (e as Error).message;
    } finally {
      busy = false;
    }
  }
  async function resolve() {
    await action(async () => {
      const value = await api<{ cards: Card[]; issues: string[] }>(
        "/api/v1/decks/resolve",
        { method: "POST", body: { text: paste } },
      );
      cards = value.cards.filter((c) => c.zone === "MainDeck");
      sideboard = value.cards.filter((c) => c.zone === "Sideboard");
      parentId = savedId = "";
      issues = value.issues;
      validation = undefined;
    });
  }
  function quantity(id: number, n: number, side = false) {
    const next = (side ? sideboard : cards)
      .map((c) =>
        c.arenaId === id ? { ...c, quantity: Math.max(0, Math.min(60, n)) } : c,
      )
      .filter((c) => c.quantity > 0);
    if (side) sideboard = next;
    else cards = next;
    savedId = "";
    validation = undefined;
  }
  async function search() {
    await action(async () => {
      results = await api<Card[]>(
        "/api/v1/cards/search?q=" + encodeURIComponent(query),
      );
    });
  }
  function add(card: Card, side = false) {
    const list = side ? sideboard : cards;
    const existing = list.find(
      (c) => c.arenaId === card.arenaId || c.name === card.name,
    );
    if (existing) quantity(existing.arenaId, existing.quantity + 1, side);
    else if (side)
      sideboard = [...sideboard, { ...card, quantity: 1, zone: "Sideboard" }];
    else cards = [...cards, { ...card, quantity: 1, zone: "MainDeck" }];
    savedId = "";
    validation = undefined;
  }
  async function persist() {
    const deck = await api<Deck>("/api/v1/decks/custom", {
      method: "POST",
      body: request,
    });
    savedId = deck.id;
    await load();
    await refreshCatalog();
    message = "Saved to Deck Library.";
    return deck;
  }
  async function rerun() {
    await action(async () => {
      await load();
      if (locked)
        throw new Error(
          "A session is active. Wait for it to stop before rerunning.",
        );
      const deck = await persist();
      const body: RunRequest = {
        deckIds: [deck.id, opponent],
        gamesPerPair: games,
        workers,
        timeoutSeconds: timeout,
        turnLimit,
        rootSeed: seed,
        diagnostic: true,
        staggerStarts: stagger && workers > 1,
        estimatedGameSeconds: estimatedSeconds,
        startLeadSeconds: leadSeconds,
        startupSpacingSeconds: spacing,
        performanceMode: performance,
        pilotMode: pilot || catalog?.defaultPilotMode || "",
        workshopDeckId: deck.id,
        ...(baseline && baselineDeck
          ? {
              workshopBaseline: {
                runId: baseline,
                deckId: baselineDeck,
                opponentId: opponent,
              },
            }
          : {}),
      };
      latestDeckId = deck.id;
      latest = (
        await api<{ id: string }>("/api/v1/runs?requireIdle=true", {
          method: "POST",
          body,
          key: idempotencyKey(),
        })
      ).id;
      await load();
      await compare();
      message = "Workshop session started.";
    });
  }
</script>

<svelte:head><title>Deck Workshop — Crucible</title></svelte:head>
<div class="workshop-page">
  <PageHeader
    title="Deck Workshop."
    eyebrow="CHANGE A LIST / TEST A MATCHUP"
    description="Make a change. Play it across the table."
  />
  <AiNote subject="workshop" />{#if error}<p class="error" role="alert">
      {error}
    </p>{/if}{#if message}<p role="status">{message}</p>{/if}{#if locked}<p
      class="notice"
    >
      A session is active. You can edit a draft; rerunning waits until the
      active session stops.
    </p>{/if}
  <section class="panel workshop-start-panel">
    <div class="toolbar">
      <div>
        <label for="workshop-source">Start from a saved deck</label><AppSelect
          id="workshop-source"
          label="Start from a saved deck"
          bind:value={source}
          options={[
            { value: "", label: "Choose a deck" },
            ...(data?.decks ?? []).map((d) => ({
              value: d.id,
              label: deckLabel(d, catalog),
            })),
          ]}
        />
      </div>
      <button
        disabled={!source}
        onclick={() => {
          const d = data?.decks.find((d) => d.id === source);
          if (d) copy(d);
        }}>Copy Deck</button
      >
    </div>
    <details>
      <summary>Paste an Arena Deck List</summary><label
        >Deck List<textarea
          rows="8"
          bind:value={paste}
          placeholder="Deck&#10;4 Card Name"></textarea></label
      ><button disabled={busy || !paste.trim()} onclick={resolve}
        >Resolve Cards</button
      >{#each issues as issue}<p class="error">{issue}</p>{/each}
    </details>
  </section>
  <div class="workshop-grid">
    <section class="panel workshop-editor">
      <label
        >Deck name<input
          bind:value={name}
          oninput={() => (savedId = "")}
        /></label
      >
      <h2>Deck List <span class="small muted">· {count} main</span></h2>
      {#each [["Main deck", cards, false], ["Sideboard", sideboard, true]] as entry}{@const list =
          entry[1] as Card[]}{@const side = entry[2] as boolean}{#if side}<h3>
            Sideboard · {list.reduce((n, c) => n + c.quantity, 0)}
          </h3>{/if}
        {#snippet editCard(card: Card)}<div class="workshop-card">
            <input
              aria-label={"Quantity of " +
                card.name +
                (side ? " in sideboard" : "")}
              type="number"
              min="0"
              max="60"
              value={card.quantity}
              onchange={(e) =>
                quantity(card.arenaId, +e.currentTarget.value, side)}
            />
            <details>
              <summary
                ><span class="workshop-card-name">{card.name}</span><span
                  class="workshop-card-mana"><CardMana {card} /></span
                ></summary
              >
              <p>{card.type}</p>
              <p class="rules-text"><CardRules text={card.text} /></p>
            </details>
            <button
              aria-label={"Remove " +
                card.name +
                (side ? " from sideboard" : "")}
              onclick={() => quantity(card.arenaId, 0, side)}>×</button
            >
          </div>{/snippet}
        <DeckList
          cards={list}
          manaValues={data?.manaValues}
          zone={side ? "Sideboard" : "MainDeck"}
          row={editCard}
          headingLevel={side ? 4 : 3}
        />{/each}
      <div class="actions">
        <button
          disabled={busy || validation?.canSave !== true}
          onclick={() =>
            action(async () => {
              await persist();
            })}>Save to Library</button
        >{#if savedId}<a href={"/decks?deck=" + savedId}>Open Saved Deck →</a
          >{/if}
      </div>
      {#each validation?.errors ?? [] as message}<p class="error">
          {message}
        </p>{/each}{#each validation?.blockers ?? [] as message}<p
          class="notice"
        >
          {message}
        </p>{/each}
    </section>
    <section class="panel workshop-search-panel">
      <h2>Find a Card</h2>
      <form
        class="toolbar workshop-search-form"
        onsubmit={(e) => {
          e.preventDefault();
          void search();
        }}
      >
        <label
          >Search card catalog<input type="search" bind:value={query} /></label
        ><button disabled={busy || query.trim().length < 2}>Search</button>
      </form>
      {#each results as card}<article class="search-card">
          <details>
            <summary><strong>{card.name}</strong> · <CardMana {card} /></summary
            >
            <p>{card.type}</p>
            <p class="rules-text"><CardRules text={card.text} /></p>
          </details>
          <div class="actions">
            <button onclick={() => add(card)}>Add to Deck</button><button
              onclick={() => add(card, true)}>Add to Sideboard</button
            >
          </div>
        </article>{/each}
    </section>
  </div>
  <section class="panel">
    <h2>Test the Matchup</h2>
    <div class="config-grid">
      <div>
        <label for="workshop-opponent">Opponent</label><AppSelect
          id="workshop-opponent"
          label="Opponent"
          bind:value={opponent}
          onchange={() => {
            baseline = baselineDeck = latest = "";
            baselineMatch = editedMatch = undefined;
          }}
          options={[
            { value: "", label: "Choose an opponent" },
            ...(data?.decks.filter((d) => d.id !== savedId) ?? []).map((d) => ({
              value: d.id,
              label: deckLabel(d, catalog),
            })),
          ]}
        />
      </div>
      <div>
        <label for="workshop-baseline">Baseline session</label><AppSelect
          id="workshop-baseline"
          label="Baseline session"
          value={baseline && baselineDeck ? baseline + ":" + baselineDeck : ""}
          onchange={(v) => {
            [baseline, baselineDeck] = v.split(":");
            if (baseline) settings(baseline);
            void compare();
          }}
          options={[
            { value: "", label: "No baseline" },
            ...(
              data?.runs.filter((r) => r.config.deckIds.includes(opponent)) ??
              []
            ).flatMap((r) =>
              r.config.deckIds
                .filter((d) => d !== opponent)
                .map((d) => ({
                  value: r.id + ":" + d,
                  label:
                    deckLabel(
                      data?.decks.find((x) => x.id === d),
                      catalog,
                    ) +
                    " · " +
                    date(r.createdAt),
                })),
            ),
          ]}
        />
      </div>
      <label
        >Games per matchup<input
          type="number"
          bind:value={games}
          min="2"
          max="1000"
          step="2"
        /></label
      ><label
        >Parallel games<input
          type="number"
          bind:value={workers}
          min="1"
          max="4"
        /></label
      ><label>Root seed<input bind:value={seed} inputmode="numeric" /></label
      ><label
        >Time limit (seconds)<input
          type="number"
          bind:value={timeout}
          min="10"
          max="1800"
        /></label
      ><label
        >Actual player turn limit<input
          type="number"
          bind:value={turnLimit}
          min="10"
          max="1000"
        /></label
      >
      <div>
        <label for="workshop-pilot">Pilot policy</label><AppSelect
          id="workshop-pilot"
          label="Pilot policy"
          options={catalog?.modes ?? []}
          value={pilot || catalog?.defaultPilotMode || ""}
          onchange={(v) => (pilot = v)}
        />
      </div>
      <label
        ><input type="checkbox" bind:checked={performance} /> Performance Mode</label
      >
    </div>
    <div class="actions workshop-match-actions">
      <button
        class="primary"
        disabled={busy ||
          locked ||
          !opponent ||
          validation?.canSave !== true ||
          !!validation?.blockers.length}
        onclick={rerun}>Save &amp; Rerun Matchup</button
      >{#if latest}<a class="button" href={"/runs/" + latest}
          >Open Session Results →</a
        >{/if}
    </div>
  </section>
  {#if baselineMatch || editedMatch}<section class="panel">
      <h2>Workshop Comparison</h2>
      <p class="small muted">
        These are exploratory aggregate differences. Deck IDs participate in
        derived game seeds, so edited lists are not paired causal comparisons.
      </p>
      {#if comparisonError}<p class="error">{comparisonError}</p>{/if}
      <div class="matchup-cards">
        {#each [["Baseline", baselineMatch], ["Edited", editedMatch]] as entry}{@const match =
            entry[1] as WorkshopMatch | undefined}
          {#if match}<DeckResult
              heading={entry[0] as string}
              deck={match.deck}
              {catalog}
              winRate={match.winRate}
              wins={match.wins}
              losses={match.losses}
              draws={match.draws}
              interval={match.interval}
            >
              {#snippet actions()}<a href={"/runs/" + match.run.id}
                  >Session evidence →</a
                >{/snippet}
            </DeckResult>
          {:else}<div>
              <h3>{entry[0] as string}</h3>
              <p class="muted">No results yet.</p>
            </div>{/if}{/each}
      </div>
      {#if baselineMatch?.winRate != null && editedMatch?.winRate != null}<p>
          Observed difference: {(
            (editedMatch.winRate - baselineMatch.winRate) *
            100
          ).toFixed(1)} percentage points. Check build, pilot, and settings before
          comparing.
        </p>{/if}
    </section>{/if}
</div>
