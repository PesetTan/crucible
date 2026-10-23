<script lang="ts">
  import type { Coverage, Deck, EngineSupport } from "./types.ts";
  import {
    engineLabel,
    forgeSupport,
    supportedMatchups,
  } from "./engine-support.ts";
  let {
    deck,
    coverage,
    engines,
    details = false,
  }: {
    deck: Deck;
    coverage?: Coverage;
    engines?: EngineSupport[];
    details?: boolean;
  } = $props();
  let forge = $derived(forgeSupport(coverage));
</script>

<div
  class="engine-support-badges"
  role="group"
  aria-label={"Engine support for " + deck.name}
>
  <span class="engine-badge" data-status={forge.kind} title={forge.reason}
    >Forge: {forge.label}</span
  >
  {#if !engines}<span class="engine-badge" data-status="unknown"
      >Pyre: checking coverage</span
    >
  {:else if !engines.length}<span
      class="engine-badge"
      data-status="unknown"
      title="No Pyre acceptance catalog is available."
      >Pyre: coverage unavailable</span
    >
  {:else}{#each engines as engine (engine.id)}{@const matchups =
        supportedMatchups(deck, engine)}
      <span
        class="engine-badge"
        data-status={matchups.length ? "supported" : "unsupported"}
        title={matchups.length
          ? "Exact list validated only for: " +
            matchups.map((m) => m.label).join("; ")
          : "This exact list has no validated matchup in this engine version."}
      >
        {engineLabel(engine.providerId, engine.engineVersion)}: {matchups.length
          ? "Supported matchups"
          : "Unsupported list"}
      </span>
      {#if details && matchups.length}<ul
          class="engine-matchups"
          aria-label={engineLabel(engine.providerId) + " validated matchups"}
        >
          {#each matchups as matchup (matchup.id)}<li>
              {matchup.label} · eligibility snapshot {matchup.eligibilityAsOf}
            </li>{/each}
        </ul>{/if}
    {/each}{/if}
</div>

<style>
  .engine-support-badges {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px;
    margin: 10px 0;
  }
  .engine-badge {
    display: inline-block;
    border: 1px solid var(--line);
    border-radius: 20px;
    padding: 4px 9px;
    font-size: 12px;
    line-height: 1.4;
    color: var(--muted);
  }
  .engine-badge[data-status="supported"] {
    border-color: var(--accent);
    color: var(--accent);
  }
  .engine-badge[data-status="limited"] {
    color: var(--theme-gold, var(--accent));
  }
  .engine-badge[data-status="blocked"] {
    color: var(--theme-error);
  }
  .engine-badge[data-status="unknown"] {
    border-style: dashed;
  }
  .engine-matchups {
    flex-basis: 100%;
    margin: 4px 0;
    padding-left: 20px;
    font-size: 12px;
    color: var(--muted);
  }
</style>
