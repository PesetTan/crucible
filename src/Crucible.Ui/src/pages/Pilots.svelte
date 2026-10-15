<script lang="ts">
  import { pilotLabel as formatPilotLabel } from "../lib/pilot-label.ts";
  import { onMount } from "svelte";
  import { api } from "../lib/api.ts";
  import { date, deckLabel, pilotRates, active } from "../lib/data.ts";
  import type { Catalog, Library, History } from "../lib/types.ts";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import AppSelect from "../lib/AppSelect.svelte";
  import AiNote from "../lib/AiNote.svelte";
  interface Check {
    id: string;
    origin: string;
    kind: string;
    expected: string;
    actual: string;
    status: string;
  }
  interface Dimension {
    key: string;
    label: string;
    scope: string;
    measured: boolean;
    kind: string;
    passed: number;
    total: number;
    errors: number;
    percent: number | null;
    origins: string[];
    cases: Check[];
  }
  interface Evaluation {
    current: boolean;
    error?: string;
    report?: {
      id: string;
      completedAt: string;
      engineHash: string;
      adapterHash: string;
      bundleId: string;
      fixtureSeed: number | string;
      baseProfile: string;
      settings: Record<string, string>;
      sourceHashes: Record<string, string>;
      profiles: { profile: string; dimensions: Dimension[] }[];
    };
  }
  let { catalog }: { catalog: Catalog | undefined } = $props();
  let library = $state<Library>(),
    history = $state<History>(),
    evaluation = $state<Evaluation>(),
    error = $state(""),
    query = $state(""),
    mode = $state(""),
    older = $state(false),
    loading = $state(true),
    source = $state<{ file: string; text: string; hash: string }>(),
    sourceError = $state("");
  let rates = $derived(pilotRates(history?.sessions ?? []));
  let currentMode = $derived(mode || catalog?.defaultPilotMode || "");
  const pilotLabel = (profile: string) => formatPilotLabel(profile, catalog, true);
  let groups = $derived(
    [...new Set(catalog?.decks.map((d) => d.profiles[currentMode]) ?? [])]
      .filter(
        (p) =>
          p &&
          ((catalog?.pilotNames[p] ?? p)
            .toLowerCase()
            .includes(query.toLowerCase()) ||
            catalog?.decks.some(
              (d) =>
                d.profiles[currentMode] === p &&
                d.label.toLowerCase().includes(query.toLowerCase()),
            )),
      )
      .sort(),
  );
  let profiles = $derived(
    [
      ...new Set([
        ...(catalog?.currentProfiles ?? groups),
        ...(older
          ? [...Object.keys(rates), ...Object.keys(catalog?.pilotNames ?? {})]
          : []),
      ]),
    ]
      .filter(
        (profile) =>
          !query.trim() ||
          `${profile} ${pilotLabel(profile)}`
            .toLowerCase()
            .includes(query.trim().toLowerCase()) ||
          catalog?.decks.some(
            (deck) =>
              deck.profiles[currentMode] === profile &&
              deck.label.toLowerCase().includes(query.trim().toLowerCase()),
          ),
      )
      .sort(
        (a, b) =>
          Number(!!rates[b]?.length) - Number(!!rates[a]?.length) ||
          pilotLabel(a).localeCompare(pilotLabel(b)),
      ),
  );
  const pilotRate = (wins: number, losses: number, draws: number) =>
    `${((100 * wins) / (wins + losses + draws)).toFixed(1)}%`;
  const shortDate = (value: string) =>
    new Date(value).toLocaleString(undefined, {
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit",
    });
  const matchesBuild = (
    entry: NonNullable<typeof history>["sessions"][number]["progress"]["run"],
  ) =>
    entry.config.engineHash === history?.capabilities.engineHash &&
    entry.config.adapterHash === history?.capabilities.adapterHash &&
    entry.config.attributesHash === history?.capabilities.attributesHash;
  async function load() {
    loading = true;
    try {
      [library, history, evaluation] = await Promise.all([
        api<Library>("/api/v1/library-state"),
        api<History>("/api/v1/run-history"),
        api<Evaluation>("/api/v1/pilot-scorecards"),
      ]);
      error = "";
    } catch (e) {
      error = (e as Error).message;
    } finally {
      loading = false;
    }
  }
  async function showSource(file: string) {
    try {
      source = await api(
        `/api/v1/presentation/pilot-source?report=${evaluation?.report?.id}&file=${encodeURIComponent(file)}`,
      );
      sourceError = "";
    } catch (e) {
      sourceError = (e as Error).message;
    }
  }
  onMount(() => {
    void load();
  });
</script>

<svelte:head><title>Forge Pilots — Crucible</title></svelte:head>
<div class="eyebrow">AUTOMATED PLAY / PILOT PROFILES</div>
<h1>Forge Pilots.</h1>
<p class="lead">Explore the automated pilots available for your decks.</p>
<AiNote subject="pilots" />
<p class="small muted">
  Profiles supplement Forge's decisions. Results describe exploratory pilot
  games. Each session keeps its recorded pilot settings.
</p>
{#if error}<p class="error" role="alert">{error}</p>{/if}
<div class="pilot-toolbar">
  <div>
    <label for="pilot-view-policy">Assignment mode</label><AppSelect
      id="pilot-view-policy"
      label="Assignment mode"
      value={currentMode}
      onchange={(v) => (mode = v)}
      options={catalog?.modes ?? []}
    />
  </div>
  <div>
    <label for="pilot-search">Find a pilot or deck</label><input
      id="pilot-search"
      type="search"
      bind:value={query}
      placeholder="Search profiles, subtypes, or decks…"
    />
  </div>
  <button disabled={loading} onclick={load}
    >{loading ? "Refreshing…" : "Refresh"}</button
  >
</div>
<p class="small muted">
  {catalog?.modes.find((m) => m.value === currentMode)?.description}
  Preview only. Set the session's pilot policy in Deck Tester.
</p>
<section class="pilot-win-rates" aria-label="Pilot match win rates">
  <div class="pilot-heading">
    <h2>Pilot win rates</h2>
    <label class="pilot-history-toggle"
      ><input type="checkbox" bind:checked={older} /> Include older pilot versions</label
    >
  </div>
  <p class="small muted">
    Latest measured session for each exact pilot · Wins / completed player
    appearances, including draws. Decks and opponents differ, so these rates do
    not rank pilot skill. Exploratory Forge results, not Arena forecasts.
  </p>
  <div class="table-scroll">
    <table class="pilot-rates-table">
      <thead
        ><tr
          ><th>Pilot</th><th>Win rate</th><th>W / L / D</th><th
            >Sample / evidence</th
          ></tr
        ></thead
      ><tbody
        >{#each profiles as profile}{@const entries =
            rates[profile] ?? []}{@const latest = entries[0]}<tr
            ><th
              >{pilotLabel(profile)}<small><code>{profile}</code></small><AiNote
                scope="pilot"
                subject={profile}
              /></th
            ><td
              ><strong
                >{latest
                  ? pilotRate(latest.wins, latest.losses, latest.draws)
                  : "Unmeasured"}</strong
              ></td
            ><td class="pilot-record"
              >{latest
                ? `${latest.wins} / ${latest.losses} / ${latest.draws}`
                : "—"}</td
            ><td
              >{#if latest}<span class="small"
                  >{(
                    latest.wins +
                    latest.losses +
                    latest.draws
                  ).toLocaleString()} player appearances · {latest.decks.size} deck
                  {latest.decks.size === 1 ? "version" : "versions"}</span
                >
                {#if !matchesBuild(latest.run)}<span
                    class="small muted pilot-rate-note"
                    >Historical build or attributes</span
                  >{/if}
                <details class="pilot-match-evidence">
                  <summary
                    >Latest session · {shortDate(latest.run.createdAt)}{active(
                      latest.run,
                    )
                      ? " · Partial"
                      : ""}</summary
                  >{#each entries as entry}<p>
                      <a href={"/runs/" + entry.run.id}
                        >{date(entry.run.createdAt)} · {entry.wins}W / {entry.losses}L
                        / {entry.draws}D</a
                      >
                    </p>
                    <p class="small muted">
                      {matchesBuild(entry.run)
                        ? "Current build and attributes"
                        : "Historical build or attributes"} · Seed {entry.run
                        .config.rootSeed}
                    </p>{/each}
                </details>{:else}<span class="muted"
                  >No measured games with this recorded profile.</span
                >{/if}</td
            ></tr
          >{/each}</tbody
      >
    </table>
  </div>
</section>
{#if history?.capabilities.protocols}<details class="panel">
    <summary>Engagement Protocols · Preview</summary>
    <p class="small muted">
      Public observations select bounded responses. These definitions come from
      the active immutable engine bundle.
    </p>
    <ol class="protocol-cards">
      {#each [...history.capabilities.protocols.definitions].sort((a, b) => b.priority - a.priority) as protocol, i}<li
          class="protocol-card"
        >
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
        </li>{/each}
    </ol>
  </details>{/if}
{#if evaluation?.error}<p class="error">
    {evaluation.error}
  </p>{/if}{#if evaluation?.report}<section class="panel">
    <h2>Programmatic Attribute Evaluation</h2>
    <p>
      {evaluation.report.profiles.length} profiles · {date(
        evaluation.report.completedAt,
      )}
    </p>
    <p class="small muted">
      Check rates describe specific frozen positions; aggression is an observed
      tendency. No overall skill score is inferred.
    </p>
    {#if !evaluation.current}<p class="notice">
        Historical report: engine build or evaluation suite differs from
        current.
      </p>{/if}
    <details>
      <summary>Inherited settings and frozen source</summary>
      <p>
        Forge base profile: {evaluation.report.baseProfile} · Fixture seed {evaluation
          .report.fixtureSeed}
      </p>
      <dl class="metadata">
        {#each Object.entries(evaluation.report.settings) as [key, value]}<dt>
            {key}
          </dt>
          <dd>{value}</dd>{/each}
      </dl>
      <div class="actions">
        {#each Object.keys(evaluation.report.sourceHashes) as file}<button
            onclick={() => showSource(file)}>{file}</button
          >{/each}
      </div>
    </details>
    {#if sourceError}<p class="error">{sourceError}</p>{/if}{#if source}<details
        open
      >
        <summary>Frozen source: {source.file}</summary>
        <p class="small muted">SHA256 <code>{source.hash}</code></p>
        <textarea
          class="pilot-source"
          aria-label="Frozen evaluation source"
          readonly
          rows="20"
          value={source.text}></textarea>
      </details>{/if}
  </section>{/if}
{#each groups as profile}{@const score = evaluation?.report?.profiles.find(
    (p) => p.profile === profile,
  )}
  <section class="panel pilot-profile-card">
    <h2>{pilotLabel(profile)}</h2>
    <p class="small muted">{profile}</p>
    <AiNote scope="pilot" subject={profile} />
    <details>
      <summary
        >Assigned decks · {catalog?.decks.filter(
          (d) => d.profiles[currentMode] === profile,
        ).length}</summary
      >
      <div class="assigned-decks">
        {#each library?.decks.filter((row) => catalog?.decks.find((d) => d.id === row.manifest.id)?.profiles[currentMode] === profile) ?? [] as row}<a
            href={"/decks?deck=" + row.manifest.id}
            ><DeckIdentity deck={row.manifest} {catalog} /></a
          >{/each}
      </div>
    </details>
    {#if score}<div class="table-scroll">
        <table>
          <thead
            ><tr
              ><th>Attribute</th><th>Measurement</th><th>Behavior source</th><th
                >What was checked</th
              ></tr
            ></thead
          ><tbody
            >{#each score.dimensions as dimension}<tr
                ><th>{dimension.label}</th><td
                  >{dimension.measured
                    ? `${dimension.passed} / ${dimension.total} ${dimension.key === "aggression" ? "attacks" : "passed"} · ${dimension.percent?.toFixed(1)}%`
                    : "Unmeasured"}{#if dimension.errors}<small class="error"
                      >{dimension.errors} evaluation errors</small
                    >{/if}</td
                ><td>{dimension.origins.join(" · ") || "No checks recorded"}</td
                ><td
                  >{dimension.scope}
                  <details>
                    <summary>Individual observations</summary
                    >{#each dimension.cases as check}<article>
                        <strong>{check.id} · {check.status}</strong>
                        <p>
                          Expected: {check.expected}<br />Observed: {check.actual}
                        </p>
                        <small>{check.origin} · {check.kind}</small>
                      </article>{/each}
                  </details></td
                ></tr
              >{/each}</tbody
          >
        </table>
      </div>{:else}<p class="small muted">
        No attribute evaluation recorded for this exact profile.
      </p>{/if}
  </section>{/each}
