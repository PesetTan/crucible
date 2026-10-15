<script lang="ts">
  import { pilotLabel } from "../lib/pilot-label.ts";
  import { onMount, onDestroy, untrack } from "svelte";
  import { api, loadSaved, save } from "../lib/api.ts";
  import type {
    Catalog,
    Snapshot,
    ReplayRecord,
    ReplayState,
  } from "../lib/types.ts";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  let {
    runId,
    id,
    catalog,
  }: { runId: string; id: string; catalog: Catalog | undefined } = $props();
  let snapshot = $state<Snapshot>(),
    records = $state<ReplayRecord[]>([]),
    positions = $state<{ turn: number; offset: number }[]>([]),
    offset = $state(0),
    index = $state(0),
    board = $state<ReplayState>(),
    audit = $state(loadSaved("replay-audit", false)),
    playing = $state(false),
    loading = $state(false),
    error = $state(""),
    scrub = $state(0),
    ended = $state(false);
  let beforePage: ReplayState | undefined;
  let revision = 0,
    disposed = false;
  let controller = new AbortController();
  let current = $derived(records[index]);
  let slot = $derived(snapshot?.slots.find((s) => s.id === id));
  let config = $derived(snapshot?.runs[0]?.config);
  const query = new URLSearchParams(location.search);
  const from = query.get("fromLog");
  const back = untrack(() =>
    from && ["hands", "mana", "games"].includes(from)
      ? `/runs/${runId}/logs/${from}`
      : `/runs/${runId}`,
  );
  function choose(i: number) {
    index = i;
    const found = records.slice(0, i + 1).findLast((r) => r.state)?.state;
    board = found ?? beforePage;
    scrub = current?.actualTurn ?? 0;
    ended = current?.kind === "game_finished";
  }
  async function load(nextOffset = offset, nextIndex = 0, keepState = false) {
    const ticket = ++revision;
    controller.abort();
    controller = new AbortController();
    loading = true;
    try {
      const next = await api<ReplayRecord[]>(
        `/api/v1/runs/${runId}/games/${id}/replay?offset=${nextOffset}&limit=100&audit=${audit}`,
        { signal: controller.signal, fresh: true },
      );
      if (ticket !== revision || disposed) return;
      offset = nextOffset;
      records = next;
      beforePage = keepState ? board : undefined;
      board = beforePage;
      choose(Math.min(nextIndex, Math.max(0, next.length - 1)));
      ended = !next.length || current?.kind === "game_finished";
      error = "";
    } catch (e) {
      if ((e as Error).name !== "AbortError") error = (e as Error).message;
      playing = false;
    } finally {
      if (ticket === revision) loading = false;
    }
  }
  async function next() {
    if (loading || ended) return;
    if (index + 1 < records.length) choose(index + 1);
    else await load(offset + records.length, 0, true);
  }
  async function previous() {
    playing = false;
    if (index > 0) choose(index - 1);
    else if (offset > 0) await load(Math.max(0, offset - 100), 99);
  }
  async function jump(turn: number) {
    playing = false;
    const p = positions.find((p) => p.turn >= turn) ?? positions.at(-1);
    if (!p) return;
    await load(Math.floor(p.offset / 100) * 100, p.offset % 100);
  }
  async function play() {
    playing = !playing;
    while (playing && !ended && !disposed) {
      await new Promise((r) => setTimeout(r, 550));
      if (playing && !disposed) await next();
    }
    playing = false;
  }
  async function changeAudit() {
    playing = false;
    save("replay-audit", audit);
    await load(offset, index);
  }
  onMount(() => {
    void (async () => {
      try {
        snapshot = await api(`/api/v1/runs/${runId}/state`);
        positions = await api(`/api/v1/runs/${runId}/games/${id}/replay-turns`);
        await load();
      } catch (e) {
        error = (e as Error).message;
      }
    })();
  });
  onDestroy(() => {
    disposed = true;
    playing = false;
    controller.abort();
  });
  const phaseGroups = [
    ["Beginning", ["UNTAP", "UPKEEP", "DRAW"]],
    ["Precombat main", ["MAIN1"]],
    [
      "Combat",
      [
        "COMBAT_BEGIN",
        "COMBAT_DECLARE_ATTACKERS",
        "COMBAT_DECLARE_BLOCKERS",
        "COMBAT_FIRST_STRIKE_DAMAGE",
        "COMBAT_DAMAGE",
        "COMBAT_END",
      ],
    ],
    ["Postcombat main", ["MAIN2"]],
    ["Ending", ["END_OF_TURN", "CLEANUP"]],
  ] as [string, string[]][];
</script>

<svelte:head><title>Game Replay — Crucible</title></svelte:head><a
  class="button"
  href={back}>← Back to {from ? "Session Log" : "Session Results"}</a
>
<div class="eyebrow">STORED GAME / EVENT REPLAY</div>
<h1>At the Table.</h1>
{#if slot}<p>
    <DeckIdentity
      deck={snapshot?.decks.find((d) => d.manifest.id === slot.deckA)?.manifest}
      {catalog}
    /> <span class="muted">vs</span>
    <DeckIdentity
      deck={snapshot?.decks.find((d) => d.manifest.id === slot.deckB)?.manifest}
      {catalog}
    />
  </p>
  <p class="small muted">
    Seed {slot.seed} · {slot.result?.actualTurns ?? "—"} actual player turns · Read-only
    saved evidence
  </p>{/if}{#if error}<p class="error" role="alert">{error}</p>{/if}
<div class="replay-toolbar">
  <button disabled={loading || (offset === 0 && index === 0)} onclick={previous}
    >Previous Event</button
  ><button disabled={loading || ended} onclick={next}>Next Event</button><button
    disabled={loading || ended}
    onclick={play}>{playing ? "Pause" : "Play"}</button
  ><button
    disabled={loading ||
      !positions.some((p) => p.turn < (current?.actualTurn ?? 0))}
    onclick={() =>
      jump(
        positions.filter((p) => p.turn < (current?.actualTurn ?? 0)).at(-1)
          ?.turn ?? 0,
      )}>Previous Turn</button
  ><button
    disabled={loading ||
      !positions.some((p) => p.turn > (current?.actualTurn ?? 0))}
    onclick={() =>
      jump(
        positions.find((p) => p.turn > (current?.actualTurn ?? 0))?.turn ?? 0,
      )}>Next Turn</button
  ><label
    ><input type="checkbox" bind:checked={audit} onchange={changeAudit} /> Omniscient
    Hand Audit</label
  ><small class="muted"
    >Audit reveals both private hands and full event detail.</small
  >
</div>
{#if positions.length > 1}<label class="turn-scrubber"
    >Scrub turns<input
      aria-label="Scrub turns"
      type="range"
      min={positions[0].turn}
      max={positions.at(-1)?.turn ?? 1}
      step="1"
      bind:value={scrub}
      onchange={() => jump(scrub)}
    /><output>Turn {scrub} of {positions.at(-1)?.turn}</output></label
  >{/if}
{#if current}<div class="checkpoint">
    <span class="pill">Actual Turn {current.actualTurn}</span><strong
      >{current.kind === "trigger"
        ? "Triggered Ability"
        : (current.event?.replace("GameEvent", "") ??
          current.kind.replaceAll("_", " "))}</strong
    ><span class="muted">Event {current.sequence}</span>
  </div>
  <ol class="turn-phases">
    {#each phaseGroups as [name, steps]}<li
        class:current={steps.includes(board?.phase ?? "")}
        aria-current={steps.includes(board?.phase ?? "") ? "step" : undefined}
      >
        {name}
      </li>{/each}
  </ol>
  {#if current.kind === "trigger"}<div class="notice">
      <strong>Trigger · {current.triggerCategory ?? "Unclassified"}</strong
      ><span>{current.triggerSource} · {current.triggerText}</span>
    </div>{/if}{#if current.kind === "pilot_decision"}<div class="notice">
      <strong
        >{pilotLabel(current.pilotProfile, catalog)} · Decision</strong
      ><span>{current.decision}: {current.selected}<br />{current.reason}</span>
      <details>
        <summary>Public evidence{audit ? " and legal options" : ""}</summary>
        <p>{current.visibleEvidence?.join(", ") ?? "No evidence recorded."}</p>
        {#if audit}<p>
            Options: {current.options?.join(", ") ?? "Not recorded"}
          </p>{/if}
      </details>
    </div>{/if}{#if current.activeProtocols?.length}<div class="notice">
      <strong>Engagement Protocols · {current.protocolSetVersion}</strong><span
        >{current.activeProtocols
          .map(
            (id) =>
              config?.protocolSet?.definitions.find((p) => p.id === id)?.name ??
              id,
          )
          .join(" · ")}</span
      >{#if current.protocolDelay}<p>
          Bounded caution: the card's waiting allowance has started.
        </p>{/if}
    </div>{/if}
  {#if board}<div class="board-grid">
      {#each board.players as player}<section class="player-board">
          <div class="player-head">
            <div>
              <span class="tag"
                >{board.activePlayer === player.id
                  ? "ACTIVE PLAYER"
                  : "OPPONENT"}</span
              >
              <h2>
                <DeckIdentity
                  deck={snapshot?.decks.find(
                    (d) =>
                      d.manifest.id ===
                      (player.id === "A" ? slot?.deckA : slot?.deckB),
                  )?.manifest}
                  {catalog}
                />
              </h2>
            </div>
            <div class="life-total">{player.life}<small>LIFE</small></div>
          </div>
          <div class="zone-stats">
            <span>Hand <strong>{player.handCount}</strong></span><span
              >Library <strong>{player.libraryCount}</strong></span
            ><span>Graveyard <strong>{player.graveyard.length}</strong></span
            ><span>Personal turn <strong>{player.personalTurn}</strong></span>
          </div>
          <div class="eyebrow">
            BATTLEFIELD · {player.battlefield.length} permanents
          </div>
          <div class="permanents">
            {#each player.battlefield as card (card.id)}<details
                class="permanent"
                class:tapped={card.tapped}
              >
                <summary
                  ><strong>{card.name}</strong><span
                    >{card.power != null
                      ? card.power + "/" + card.toughness
                      : ""} · {card.tapped ? "Tapped" : "Ready"}</span
                  ><small>{card.cost}</small></summary
                >
                <p>{card.type}</p>
                <p class="rules-text">{card.text.replaceAll("\\n", "\n")}</p>
                <p>
                  Counters: {card.counters || "None"}<br />Owner: {card.owner} · Controller:
                  {card.controller}<br />Attached to: {card.attachedTo ?? "—"}
                </p>
              </details>{/each}{#if !player.battlefield.length}<p class="muted">
                No permanents in play.
              </p>{/if}
          </div>
          <details>
            <summary>Graveyard · {player.graveyard.length}</summary
            >{#each player.graveyard as card}<p>{card.name}</p>{/each}
          </details>
          <details>
            <summary>Exile · {player.exile.length}</summary
            >{#each player.exile as card}<p>{card.name}</p>{/each}
          </details>
          {#if audit}<details open>
              <summary>Private Hand — Audit View</summary
              >{#each player.hand as card}<p>
                  {card.name} · {card.cost}
                </p>{/each}
            </details>{/if}
        </section>{/each}
    </div>
    <section class="panel">
      <h3>Stack</h3>
      {#each board.stack as item}<p>{item}</p>{/each}{#if !board.stack.length}<p
          class="muted"
        >
          Empty
        </p>{/if}
    </section>{/if}
  <section class="panel">
    <h3>Event Detail</h3>
    <p class="rules-text">
      {audit || !current.event
        ? (current.detail ?? current.reason ?? current.kind)
        : "Detailed choices are available in audit view."}
    </p>
    <p class="small muted">
      Stored checksum: {current.stateHash ?? "No new checksum on this record."}
    </p>
  </section>{:else if loading}<p role="status">Loading replay…</p>{/if}
<details>
  <summary>Events on This Page</summary>
  <div class="event-list">
    {#each records as record, i}<button
        onclick={() => {
          playing = false;
          choose(i);
        }}
        ><span>Turn {record.actualTurn} · #{record.sequence}</span><strong
          >{record.event?.replace("GameEvent", "") ?? record.kind}</strong
        ></button
      >{/each}
  </div>
</details>
