<script lang="ts">
  import { pilotLabel } from "./pilot-label.ts";
  import AppSelect from "./AppSelect.svelte";
  import type { ProtocolSet } from "./types.ts";
  let {
    catalog,
    labels = {},
    title = "Engagement Protocols",
    note = "Definitions from the recorded immutable catalog.",
  }: {
    catalog: ProtocolSet;
    labels?: Record<string, string>;
    title?: string;
    note?: string;
  } = $props();
  let profile = $state("");
  let selected = $derived(
    profile && (profile === catalog.profile || catalog.sets?.[profile])
      ? profile
      : catalog.profile,
  );
  let definitions = $derived(
    selected === catalog.profile
      ? catalog.definitions
      : (catalog.sets?.[selected]?.definitions ?? []),
  );
  let options = $derived(
    Object.keys(catalog.sets ?? { [catalog.profile]: {} }).map((p) => ({
      value: p,
      label: pilotLabel(p, { pilotNames: {}, pilotLabels: labels }, true),
    })),
  );
</script>

<section
  class="panel pilot-protocol-overview"
  id="engagement-protocols"
  aria-label={title}
>
  <h2>{title}</h2>
  <p>{note}</p>
  <p class="small muted">
    Public observations activate compatible protocols. Forge supplies legal
    actions, payment and resolution. General combat, mulligans and unsupported
    choices retain inherited behavior.
  </p>
  {#if options.length > 1}<label for="protocol-profile"
      >Pilot protocol set</label
    ><AppSelect
      id="protocol-profile"
      label="Pilot protocol set"
      {options}
      value={selected}
      onchange={(value) => (profile = value)}
    />{/if}
  {#if catalog.sets?.[selected]}<p class="small muted">
      {catalog.sets[selected].activation === "baseline-only"
        ? "Inherited Forge baseline; no new override."
        : catalog.sets[selected].activation === "optimizer-policy"
          ? "Requires an explicitly selected evaluated optimizer policy."
          : "Available through the existing Reanimator preview mode."} Parent: {catalog
        .sets[selected].parent}.
    </p>{/if}
  <ol class="protocol-cards">
    {#each [...definitions].sort((a, b) => b.priority - a.priority) as protocol, i (protocol.id)}
      <li class="protocol-card">
        <div class="protocol-card-heading">
          <span class="protocol-order">{String(i + 1).padStart(2, "0")}</span
          ><strong>{protocol.name}</strong>
        </div>
        <dl>
          <dt>When</dt>
          <dd>{protocol.condition}</dd>
          <dt>Response</dt>
          <dd>{protocol.response}</dd>
        </dl>
      </li>
    {/each}
  </ol>
  <details>
    <summary>Recorded catalog</summary>
    <p class="small muted">Exact profile: <code>{selected}</code></p>
    <p class="small muted">{catalog.version}</p>
    <code class="protocol-catalog-hash">{catalog.hash}</code>
  </details>
</section>

<style>
  label {
    display: block;
    font-size: 12px;
    color: var(--muted);
    margin: 16px 0 7px;
  }
  .protocol-catalog-hash {
    overflow-wrap: anywhere;
  }
</style>
