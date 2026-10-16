<script lang="ts">
  import { pilotLabel } from "./pilot-label.ts";
  import type { Run, Catalog, Deck, Capability } from "./types.ts";
  import { api } from "./api.ts";
  import DeckIdentity from "./DeckIdentity.svelte";
  import EngagementProtocols from "./EngagementProtocols.svelte";
  let {
    run,
    catalog,
    decks,
    capability,
  }: {
    run: Run;
    catalog: Catalog | undefined;
    decks: Deck[];
    capability?: Capability;
  } = $props();
  let names = $state<{ engine: string; bridge: string }>();
  $effect(() => {
    const c = new AbortController();
    void api<{ engine: string; bridge: string }>(
      `/api/v1/presentation/build-names?engine=${run.config.engineHash}&bridge=${run.config.adapterHash}`,
      { signal: c.signal },
    )
      .then((v) => (names = v))
      .catch(() => {});
    return () => c.abort();
  });
</script>

<details class="panel provenance">
  <summary>Saved pilot and build details</summary>
  <p class="small muted">
    These settings are recorded for this session. Later changes do not alter
    this evidence.
  </p>
  <dl class="metadata">
    <dt>Policy</dt>
    <dd>
      {catalog?.modes.find((m) => m.value === run.config.pilot)?.label ??
        run.config.pilot}
    </dd>
    <dt>Optimizer policy</dt>
    <dd>{#if run.config.optimizerPolicy}<code>{run.config.optimizerPolicy.id}</code>{#if run.config.optimizerPolicy.profile}<span>{run.config.optimizerPolicy.profile}</span>{/if}{:else}None recorded{/if}</dd>
    {#each run.config.deckIds as id}<dt>
        <DeckIdentity deck={decks.find((d) => d.id === id)} {catalog} compact />
      </dt>
      <dd>
        {pilotLabel(run.config.pilotProfiles?.[id], catalog)}
        <code>{run.config.pilotProfiles?.[id] ?? "No saved assignment"}</code>
      </dd>{/each}
    <dt>Engine</dt>
    <dd>
      {names?.engine ?? "Recorded build"}<code>{run.config.engineHash}</code>
    </dd>
    <dt>Pilot bridge</dt>
    <dd>
      {names?.bridge ?? "Recorded build"}<code>{run.config.adapterHash}</code>
    </dd>
    <dt>Seed / scheme</dt>
    <dd>{run.config.rootSeed} / {run.config.seedScheme}</dd>
    <dt>Attributes</dt>
    <dd><code>{run.config.attributesHash ?? "Not recorded"}</code></dd>
    <dt>Opening policy</dt>
    <dd>{run.config.openingPolicy}</dd>
    <dt>Performance mode</dt>
    <dd>{run.config.performanceMode ? "On" : "Off"}</dd>
  </dl>
  {#if capability}<p class="small muted">
      {capability.engineHash === run.config.engineHash &&
      capability.adapterHash === run.config.adapterHash
        ? "Engine and adapter match the current build."
        : "Historical engine or adapter build."}
    </p>{/if}{#if run.config.protocolSet}<EngagementProtocols catalog={run.config.protocolSet} title="Recorded Engagement Protocols" note="Catalog frozen with this session. Recorded pilots and optimizer policy determine which sets were enabled." />{/if}{#if run.config.cardAttributes}<details>
      <summary
        >Recorded card attributes · {Object.keys(run.config.cardAttributes)
          .length} cards</summary
      >
      <pre>{JSON.stringify(run.config.cardAttributes, null, 2)}</pre>
    </details>{/if}
</details>
