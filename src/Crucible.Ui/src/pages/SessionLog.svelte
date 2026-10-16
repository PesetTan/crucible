<script lang="ts">
  import PageHeader from "../lib/PageHeader.svelte";
  import { onMount, untrack } from "svelte";
  import { api, loadSaved, save } from "../lib/api.ts";
  import { date, deckLabel, duration, label, number } from "../lib/data.ts";
  import type { Catalog, Snapshot, Slot, Timing } from "../lib/types.ts";
  import AppSelect from "../lib/AppSelect.svelte";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import DeckMatchup from "../lib/DeckMatchup.svelte";
  let {
    id,
    kind,
    catalog,
  }: { id: string; kind: string; catalog: Catalog | undefined } = $props();
  const storageKey = untrack(() => `log:${id}:${kind}`);
  const saved = loadSaved(storageKey, {
    deck: "",
    status: "",
    decision: "",
    matchup: "",
    mana: "",
    unit: "turns",
    sort: "index",
    descending: false,
  });
  type HandRow = {
    gameId: string;
    deckId: string;
    onPlay: boolean;
    openingHand: { [key: string]: unknown };
    trackingVersion?: string;
  };
  type ManaRow = {
    gameId: string;
    deckId: string;
    flag: { kind: string; [key: string]: unknown };
    winner?: string;
    draw: boolean;
  };
  let snapshot = $state<Snapshot>(),
    hands = $state<{
      openingPolicy: string;
      interpretation: string;
      players: HandRow[];
    }>(),
    mana = $state<{
      manaStarved: string;
      manaFlooded: string;
      definitionVersion: string;
      incidents: ManaRow[];
    }>(),
    timings = $state<Timing[]>([]),
    error = $state(""),
    loading = $state(true),
    deckFilter = $state(saved.deck),
    status = $state(saved.status),
    decision = $state(saved.decision),
    matchup = $state(saved.matchup),
    manaFilter = $state(saved.mana),
    timeUnit = $state(saved.unit),
    page = $state(0),
    sort = $state(saved.sort),
    descending = $state(saved.descending);
  let title = $derived(
    (
      {
        hands: "Hand Log",
        mana: "Mana Incident Log",
        runtime: "Runtime Log",
        games: "Game Evidence",
      } as Record<string, string>
    )[kind] ?? "Session Log",
  );
  let gameRows = $derived(
    (snapshot?.slots ?? [])
      .filter(
        (s) =>
          (!deckFilter || s.deckA === deckFilter || s.deckB === deckFilter) &&
          (!status || s.status === status) &&
          (!matchup || [s.deckA, s.deckB].sort().join(":") === matchup) &&
          (!manaFilter ||
            (manaFilter === "none"
              ? s.result?.manaFlags?.length === 0
              : s.result?.manaFlags?.some((f) => f.kind === manaFilter))),
      )
      .toSorted((a, b) => {
        let result =
          sort === "turns"
            ? (a.result?.actualTurns ?? -1) - (b.result?.actualTurns ?? -1)
            : sort === "status"
              ? a.status.localeCompare(b.status)
              : a.index - b.index || a.id.localeCompare(b.id);
        return descending ? -result : result;
      }),
  );
  let handRows = $derived(
    (hands?.players ?? [])
      .filter(
        (r) =>
          (!deckFilter || r.deckId === deckFilter) &&
          (!decision ||
            (r.openingHand.policyAccepted === true
              ? "accepted"
              : r.openingHand.policyAccepted === false
                ? "forced"
                : "unknown") === decision),
      )
      .toSorted((a, b) => {
        const result =
          sort === "deck"
            ? deckLabel(
                snapshot?.decks.find((d) => d.manifest.id === a.deckId)
                  ?.manifest,
                catalog,
              ).localeCompare(
                deckLabel(
                  snapshot?.decks.find((d) => d.manifest.id === b.deckId)
                    ?.manifest,
                  catalog,
                ),
              )
            : Number(
                a.openingHand[sort === "kept" ? "keptHandSize" : "mulligans"] ??
                  -1,
              ) -
              Number(
                b.openingHand[sort === "kept" ? "keptHandSize" : "mulligans"] ??
                  -1,
              );
        return descending ? -result : result;
      }),
  );
  let manaRows = $derived(
    (mana?.incidents ?? []).filter(
      (r) =>
        (!deckFilter || r.deckId === deckFilter) &&
        (!status || r.flag.kind === status),
    ),
  );
  let runtimeRows = $derived(
    timings
      .filter((t) => !status || t.status === status)
      .toSorted((a, b) => {
        const result =
          sort === "active"
            ? a.activeSeconds - b.activeSeconds
            : sort === "attempts"
              ? a.attemptCount - b.attemptCount
              : a.startedAt.localeCompare(b.startedAt);
        return descending ? -result : result;
      }),
  );
  let count = $derived(
    kind === "hands"
      ? handRows.length
      : kind === "mana"
        ? manaRows.length
        : kind === "runtime"
          ? runtimeRows.length
          : gameRows.length,
  );
  $effect(() => {
    deckFilter;
    status;
    decision;
    matchup;
    manaFilter;
    timeUnit;
    sort;
    descending;
    page = 0;
  });
  $effect(() =>
    save(storageKey, {
      deck: deckFilter,
      status,
      decision,
      matchup,
      mana: manaFilter,
      unit: timeUnit,
      sort,
      descending,
    }),
  );
  const pairKey = (s: Slot) => [s.deckA, s.deckB].sort().join(":");
  let pairs = $derived([...new Set(snapshot?.slots.map(pairKey) ?? [])]);
  async function load() {
    loading = true;
    try {
      snapshot = await api<Snapshot>(`/api/v1/runs/${id}/state`);
      if (kind === "hands")
        hands = await api(`/api/v1/runs/${id}/opening-hands`);
      else if (kind === "mana")
        mana = await api(`/api/v1/runs/${id}/mana-events`);
      else if (kind === "runtime")
        timings = await api(`/api/v1/runs/${id}/timings`);
      error = "";
    } catch (e) {
      error = (e as Error).message;
    } finally {
      loading = false;
    }
  }
  onMount(() => {
    void load();
  });
</script>

<svelte:head><title>{title} — Crucible</title></svelte:head><a
  class="button back-link"
  href={"/runs/" + id}>← Back to Session Results</a
>
<nav class="log-navigation" aria-label="Session logs">
  {#each [["hands", "Hand Log"], ["mana", "Mana Incident Log"], ["runtime", "Runtime Log"], ["games", "Game Evidence"]] as [key, text]}<a
      class:active={kind === key}
      href={`/runs/${id}/logs/${key}`}>{text}</a
    >{/each}
</nav>
{#snippet logActions()}<button disabled={loading} onclick={load}>Refresh</button
  >{/snippet}
<PageHeader title={title + "."} actions={logActions} />
{#if error}<p class="error" role="alert">{error}</p>{/if}{#if loading}<p
    role="status"
  >
    Loading {title}…
  </p>{:else}<div class="toolbar">
    {#if kind !== "runtime"}<div>
        <label for="log-deck">Deck</label><AppSelect
          id="log-deck"
          label="Deck"
          bind:value={deckFilter}
          options={[
            { value: "", label: "All decks" },
            ...(snapshot?.decks ?? []).map((r) => ({
              value: r.manifest.id,
              label: deckLabel(r.manifest, catalog),
            })),
          ]}
        />
      </div>{/if}{#if kind === "games"}<div>
        <label for="log-status">Status</label><AppSelect
          id="log-status"
          label="Status"
          bind:value={status}
          options={[
            { value: "", label: "All statuses" },
            ...["completed", "running", "pending", "failed", "cancelled"].map(
              (value) => ({ value, label: label(value) }),
            ),
          ]}
        />
      </div>
      <div>
        <label for="game-sort">Sort games</label><AppSelect
          id="game-sort"
          label="Sort games"
          bind:value={sort}
          options={[
            { value: "index", label: "Game index" },
            { value: "turns", label: "Actual turns" },
            { value: "status", label: "Status" },
          ]}
        />
      </div>
      <button onclick={() => (descending = !descending)}
        >{descending ? "Descending" : "Ascending"}</button
      >{:else if kind === "mana"}<div>
        <label for="mana-kind">Incident</label><AppSelect
          id="mana-kind"
          label="Incident"
          bind:value={status}
          options={[
            { value: "", label: "All incidents" },
            { value: "mana-starved", label: "Mana-starved" },
            { value: "mana-flooded", label: "Mana-flooded" },
          ]}
        />
      </div>{/if}
  </div>
  <div class="toolbar">
    {#if kind === "games"}
      <div>
        <label for="log-matchup">Matchup</label><AppSelect
          id="log-matchup"
          label="Matchup"
          bind:value={matchup}
          options={[
            { value: "", label: "All matchups" },
            ...pairs.map((value) => ({
              value,
              label: value
                .split(":")
                .map((id) =>
                  deckLabel(
                    snapshot?.decks.find((d) => d.manifest.id === id)?.manifest,
                    catalog,
                  ),
                )
                .join(" vs "),
            })),
          ]}
        />
      </div>
      <div>
        <label for="log-mana">Mana flags</label><AppSelect
          id="log-mana"
          label="Mana flags"
          bind:value={manaFilter}
          options={[
            { value: "", label: "All games" },
            { value: "none", label: "Analyzed, no flags" },
            { value: "mana-starved", label: "Mana-starved" },
            { value: "mana-flooded", label: "Mana-flooded" },
          ]}
        />
      </div>
      <div>
        <label for="log-time-unit">View by</label><AppSelect
          id="log-time-unit"
          label="View by"
          bind:value={timeUnit}
          options={[
            { value: "turns", label: "Actual player turns" },
            { value: "rounds", label: "Rounds" },
          ]}
        />
      </div>
    {:else if kind === "hands"}
      <div>
        <label for="keep-decision">Keep decision</label><AppSelect
          id="keep-decision"
          label="Keep decision"
          bind:value={decision}
          options={[
            { value: "", label: "All decisions" },
            { value: "accepted", label: "Accepted" },
            { value: "forced", label: "Forced keep" },
            { value: "unknown", label: "Unknown" },
          ]}
        />
      </div>
      <div>
        <label for="hand-sort">Sort hands</label><AppSelect
          id="hand-sort"
          label="Sort hands"
          bind:value={sort}
          options={[
            { value: "index", label: "Mulligans" },
            { value: "kept", label: "Kept size" },
            { value: "deck", label: "Deck" },
          ]}
        />
      </div>
      <button onclick={() => (descending = !descending)}
        >{descending ? "Descending" : "Ascending"}</button
      >
    {:else if kind === "runtime"}
      <div>
        <label for="runtime-status">Status</label><AppSelect
          id="runtime-status"
          label="Status"
          bind:value={status}
          options={[
            { value: "", label: "All statuses" },
            ...[...new Set(timings.map((t) => t.status))].map((value) => ({
              value,
              label: label(value),
            })),
          ]}
        />
      </div>
      <div>
        <label for="runtime-sort">Sort periods</label><AppSelect
          id="runtime-sort"
          label="Sort periods"
          bind:value={sort}
          options={[
            { value: "index", label: "Started" },
            { value: "active", label: "Active time" },
            { value: "attempts", label: "New attempts" },
          ]}
        />
      </div>
      <button onclick={() => (descending = !descending)}
        >{descending ? "Descending" : "Ascending"}</button
      >
    {/if}
  </div>
  <p class="small muted">
    {count} records · Page {page + 1} of {Math.max(1, Math.ceil(count / 25))}
  </p>
  {#if kind === "hands"}<p class="small muted">
      {hands?.interpretation}
      {hands?.openingPolicy} Missing opening-hand records remain unknown.
    </p>
    {#each handRows.slice(page * 25, page * 25 + 25) as row}<article
        class="panel"
      >
        <h3>
          <DeckIdentity
            deck={snapshot?.decks.find((d) => d.manifest.id === row.deckId)
              ?.manifest}
            {catalog}
          />
        </h3>
        <p>
          {row.onPlay ? "On play" : "On draw"} · Tracking {row.trackingVersion ??
            "unknown"}
        </p>
        <dl class="metadata">
          {#each Object.entries(row.openingHand) as [key, value]}<dt>
              {label(key)}
            </dt>
            <dd>
              {Array.isArray(value)
                ? value
                    .map((v) => (typeof v === "object" ? JSON.stringify(v) : v))
                    .join(", ")
                : typeof value === "object"
                  ? JSON.stringify(value)
                  : String(value ?? "Unknown")}
            </dd>{/each}
        </dl>
        <a href={`/runs/${id}/games/${row.gameId}?fromRun=${id}&fromLog=hands`}
          >Open game replay →</a
        >
      </article>{/each}
  {:else if kind === "mana"}<details>
      <summary>Detection definitions · {mana?.definitionVersion}</summary>
      <p>{mana?.manaStarved}</p>
      <p>{mana?.manaFlooded}</p>
    </details>
    {#each manaRows.slice(page * 25, page * 25 + 25) as row}<article
        class="panel"
      >
        <h3>
          <DeckIdentity
            deck={snapshot?.decks.find((d) => d.manifest.id === row.deckId)
              ?.manifest}
            {catalog}
          /> · {label(row.flag.kind)}
        </h3>
        <dl class="metadata">
          {#each Object.entries(row.flag) as [key, value]}<dt>{label(key)}</dt>
            <dd>
              {typeof value === "object"
                ? JSON.stringify(value)
                : String(value ?? "Unknown")}
            </dd>{/each}
        </dl>
        <a href={`/runs/${id}/games/${row.gameId}?fromRun=${id}&fromLog=mana`}
          >Open game replay →</a
        >
      </article>{/each}
  {:else if kind === "runtime"}<p class="small muted">
      Active time includes engine startup and saving results. Queued time and
      pauses between periods are excluded.
    </p>
    <div class="table-scroll">
      <table>
        <thead
          ><tr
            ><th>Started</th><th>Finished / Last recorded</th><th
              >Active time</th
            ><th>New attempts</th><th>Status</th></tr
          ></thead
        ><tbody
          >{#each runtimeRows.slice(page * 25, page * 25 + 25) as t}<tr
              ><td>{date(t.startedAt)}</td><td
                >{date(t.finishedAt ?? t.updatedAt)}</td
              ><td>{duration(t.activeSeconds)}</td><td>{t.attemptCount}</td><td
                >{label(t.status)}</td
              ></tr
            >{/each}</tbody
        >
      </table>
    </div>
  {:else if kind === "games"}<div class="table-scroll">
      <table class="log-table">
        <thead
          ><tr
            ><th>Game</th><th>Matchup</th><th>Status / outcome</th><th
              >{timeUnit === "rounds" ? "Ending round" : "Actual turns"}</th
            ><th>Seed</th><th>Evidence</th></tr
          ></thead
        ><tbody
          >{#each gameRows.slice(page * 25, page * 25 + 25) as s}<tr
              ><td>{s.index + 1}</td><td
                ><DeckMatchup
                  deckA={snapshot?.decks.find((d) => d.manifest.id === s.deckA)
                    ?.manifest}
                  deckB={snapshot?.decks.find((d) => d.manifest.id === s.deckB)
                    ?.manifest}
                  {catalog}
                  compact
                /></td
              ><td
                >{label(s.status)}{#if s.result}<small
                    >{s.result.draw
                      ? "Rules draw"
                      : deckLabel(
                          snapshot?.decks.find(
                            (d) => d.manifest.id === s.result?.winner,
                          )?.manifest,
                          catalog,
                        ) + " won"}</small
                  >{/if}{#if s.cachedFromRunId}<a
                    class="small"
                    href={"/runs/" + s.cachedFromRunId}>Reused result</a
                  >{/if}</td
              ><td
                >{s.result
                  ? timeUnit === "rounds"
                    ? Math.ceil(s.result.actualTurns / 2)
                    : s.result.actualTurns
                  : "—"}</td
              ><td><code>{s.seed}</code></td><td
                >{#if s.result}<a
                    href={`/runs/${id}/games/${s.id}?fromRun=${id}&fromLog=games`}
                    >Replay →</a
                  ><a
                    class="small"
                    download
                    href={`/api/v1/runs/${id}/games/${s.id}/replay-export`}
                    >Download replay</a
                  >{/if}
                <details>
                  <summary>Attempts · {s.attempts.length}</summary
                  >{#each s.attempts as a}<p>
                      {a.number} · {label(a.status)} · {a.error ?? ""}<code
                        >{a.replayHash ?? ""}</code
                      >
                    </p>{/each}
                </details></td
              ></tr
            >{/each}</tbody
        >
      </table>
    </div>{/if}
  <div class="pagination">
    <button disabled={page === 0} onclick={() => page--}>Previous</button><span
      >Page {page + 1}</span
    ><button disabled={(page + 1) * 25 >= count} onclick={() => page++}
      >Next</button
    >
  </div>
  <a
    class="small"
    href={`/api/v1/runs/${id}/${kind === "hands" ? "opening-hands" : kind === "mana" ? "mana-events" : kind === "runtime" ? "timings" : "games"}`}
    target="_blank">Raw JSON →</a
  >{/if}
