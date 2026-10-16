<script lang="ts">
  import { defaultPilotResults } from "../lib/pilot-comparison.ts";
  import { pilotLabel as formatPilotLabel } from "../lib/pilot-label.ts";
  import { onMount, tick } from "svelte";
  import { api } from "../lib/api.ts";
  import { date, pilotRates, active } from "../lib/data.ts";
  import type { Catalog, Library, History, ProtocolSet } from "../lib/types.ts";
  import EngagementProtocols from "../lib/EngagementProtocols.svelte";
  import DeckIdentity from "../lib/DeckIdentity.svelte";
  import AppSelect from "../lib/AppSelect.svelte";
  import AiNote from "../lib/AiNote.svelte";
  import PageHeader from "../lib/PageHeader.svelte";
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
  let defaultResults = $derived(defaultPilotResults(history?.sessions ?? []));
  let candidateProtocols = $state<ProtocolSet>();
  let protocolOrigin = $state("active");
  let protocolCandidate = $state("");
  let displayedProtocols = $derived(
    protocolOrigin === "candidate"
      ? candidateProtocols
      : history?.capabilities.protocols,
  );
  let currentMode = $derived(mode || catalog?.defaultPilotMode || "");
  const pilotLabel = (profile: string) => formatPilotLabel(profile, catalog, true);
  type View = "pilots" | "assignments" | "protocols" | "evaluation";
  let view = $state<View>("pilots");
  let scope = $state("assigned");
  let selectedProfile = $state("");
  let evidenceProfile = $state("");
  const views: { id: View; label: string; description: string }[] = [
    {
      id: "pilots",
      label: "Pilots & results",
      description: "Profiles and measured games",
    },
    {
      id: "assignments",
      label: "Deck assignments",
      description: "Which pilot plays each deck",
    },
    {
      id: "protocols",
      label: "Engagement protocols",
      description: "When and how pilots respond",
    },
    {
      id: "evaluation",
      label: "Attribute checks",
      description: "Behavior checks and source",
    },
  ];
  let assignedProfiles = $derived(
    [
      ...new Set(catalog?.decks.map((d) => d.profiles[currentMode]) ?? []),
    ].filter(Boolean),
  );
  let allProfiles = $derived(
    [
      ...new Set([
        ...(catalog?.currentProfiles ?? assignedProfiles),
        ...(older
          ? [...Object.keys(rates), ...Object.keys(catalog?.pilotNames ?? {})]
          : []),
      ]),
    ].sort((a, b) => pilotLabel(a).localeCompare(pilotLabel(b))),
  );
  let profiles = $derived(
    allProfiles.filter(
      (profile) =>
        (scope !== "assigned" || assignedProfiles.includes(profile)) &&
        (scope !== "measured" || !!rates[profile]?.length) &&
        (!query.trim() ||
          `${profile} ${pilotLabel(profile)}`
            .toLowerCase()
            .includes(query.trim().toLowerCase()) ||
          catalog?.decks.some(
            (deck) =>
              deck.profiles[currentMode] === profile &&
              deck.label.toLowerCase().includes(query.trim().toLowerCase()),
          )),
    ),
  );
  let assignmentProfile = $derived(
    assignedProfiles.includes(selectedProfile)
      ? selectedProfile
      : (assignedProfiles[0] ?? ""),
  );
  let assignedDecks = $derived(
    catalog?.decks.filter(
      (d) => d.profiles[currentMode] === assignmentProfile,
    ) ?? [],
  );
  let evaluationProfiles = $derived(evaluation?.report?.profiles ?? []);
  let evaluatedProfile = $derived(
    evaluationProfiles.some((p) => p.profile === selectedProfile)
      ? selectedProfile
      : (evaluationProfiles[0]?.profile ?? ""),
  );
  let score = $derived(
    evaluationProfiles.find((p) => p.profile === evaluatedProfile),
  );
  async function inspect(profile: string, next: View) {
    selectedProfile = profile;
    view = next;
    await tick();
    document.querySelector<HTMLElement>("#pilot-view h2")?.focus();
  }
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
      candidateProtocols = undefined;
      protocolCandidate = "";
      // Candidate introspection is read-only and does not activate the candidate.
      try {
        const experiments = await api<
          { id: string; protocolCandidateId?: string }[]
        >("/api/v1/pilot-optimizations");
        const latest = experiments.find((e) => e.protocolCandidateId);
        if (latest) {
          const experiment = await api<{
            status: string;
            candidates: {
              id: string;
              status: string;
              protocolCatalog?: ProtocolSet;
            }[];
          }>(`/api/v1/pilot-optimizations/${latest.id}`);
          const candidate = experiment.candidates.find(
            (c) => c.id === latest.protocolCandidateId,
          );
          if (candidate?.protocolCatalog?.sets) {
            candidateProtocols = candidate.protocolCatalog;
            protocolCandidate = `${latest.id} · ${candidate.id} · experiment: ${experiment.status} · candidate: ${candidate.status}`;
          }
        }
      } catch {
        /* Active catalog remains usable when optimization is unavailable. */
      }
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
<div class="pilots-page">
  {#snippet pilotActions()}<button disabled={loading} onclick={load}
      >{loading ? "Refreshing…" : "Refresh"}</button
    >{/snippet}
  <PageHeader
    title="Forge Pilots."
    eyebrow="AUTOMATED PLAY / PILOT PROFILES"
    description="See who plays your decks, how they respond, and what the results show."
    actions={pilotActions}
  />
  <nav class="pilot-views" aria-label="Pilot workspace">
    {#each views as item}<button
        aria-pressed={view === item.id}
        aria-controls="pilot-view"
        onclick={() => (view = item.id)}
      >
        <strong>{item.label}</strong><span>{item.description}</span>
      </button>{/each}
  </nav>
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if loading && !history}<p role="status">
      Loading pilots and saved evidence…
    </p>{/if}
  <div id="pilot-view" aria-busy={loading}>
    {#if view === "pilots"}
      <div class="section-head pilot-section-heading">
        <div>
          <h2 tabindex="-1">Pilots &amp; results</h2>
          <p class="small muted">
            {assignedProfiles.length} assigned pilots · {catalog
              ?.currentProfiles.length ?? 0} available profiles
          </p>
        </div>
        <a class="button" href="/">Configure a session →</a>
      </div>
      <section class="panel default-comparison" aria-label="Against Forge Default">
        <h3>Against Forge Default</h3>
        <p class="small muted">Completed games where a custom pilot faced Forge Default (<code>forge-default</code> or <code>forge-default-v1</code>). Each row keeps its exact session and deck matchup; cached games are not pooled across sessions.</p>
        <p class="small muted">Winning this matchup measures the deck and pilot together. Proof of pilot improvement requires replay-verified paired trials with the same deck versions, seeds, starts, engine, attributes and opponent policy, changing only the tested pilot.</p>
        {#if defaultResults.length}
          <details><summary>Recorded matchups · {defaultResults.length}</summary><div class="table-scroll"><table>
            <thead><tr><th>Pilot / session</th><th>Deck vs default deck</th><th>Custom W–L–D</th><th>Custom win rate</th><th>Default win rate</th></tr></thead>
            <tbody>{#each defaultResults as row}
              <tr><td>{pilotLabel(row.profile)}<br/><a href={"/runs/" + row.run.id}>{shortDate(row.run.createdAt)} →</a><br/><small>{row.run.status !== "completed" ? "Partial session · " : ""}{matchesBuild(row.run) ? "Current build" : "Historical build / attributes"}</small></td>
              <td>{history?.decks.find(d => d.id === row.deck)?.name ?? row.deck}<br/><span class="muted">vs {history?.decks.find(d => d.id === row.opponent)?.name ?? row.opponent}</span></td>
              <td>{row.wins}–{row.losses}–{row.draws}<br/><small>{row.games} games</small></td>
              <td>{pilotRate(row.wins, row.losses, row.draws)}</td><td>{pilotRate(row.losses, row.wins, row.draws)}</td></tr>
            {/each}</tbody>
          </table></div></details>
        {:else if !loading}<p>No recorded custom-pilot games against Forge Default. Improvement over default is unproven in this dataset.</p>{/if}
        <details><summary>How to read this evidence</summary><p class="small muted">Draws stay in both win-rate denominators. Failed and unfinished games are excluded. Other custom pilots and unknown historical assignments are excluded. Above 50% alone does not establish a pilot advantage: deck strength, starting seat and sample size matter. Candidate-versus-previous-custom-version optimizer tests do not establish improvement over Forge Default.</p></details>
      </section>
      <div class="toolbar pilot-filters">
        <div>
          <label for="pilot-search">Find a pilot or deck</label>
          <input
            id="pilot-search"
            type="search"
            bind:value={query}
            placeholder="Pilot name, profile, or deck"
          />
        </div>
        <div>
          <label for="pilot-scope">Show pilots</label><AppSelect
            id="pilot-scope"
            label="Show pilots"
            value={scope}
            onchange={(value) => (scope = value)}
            options={[
              { value: "assigned", label: "Assigned to decks" },
              { value: "all", label: "All available profiles" },
              { value: "measured", label: "With measured games" },
            ]}
          />
        </div>
      </div>
      <div class="pilot-list-meta">
        <p class="small muted" role="status">
          {profiles.length}
          {profiles.length === 1 ? "pilot" : "pilots"} shown · {catalog?.modes.find(
            (m) => m.value === currentMode,
          )?.label}
        </p>
        <label class="pilot-history-toggle"
          ><input
            type="checkbox"
            bind:checked={older}
            onchange={(event) => {
              if (event.currentTarget.checked) scope = "all";
            }}
          /> Include older versions</label
        >
      </div>
      <p class="small muted pilot-results-note">
        Latest measured session per exact profile. Win rates include draws;
        decks and opponents differ. These exploratory Forge results do not rank
        pilot skill or predict Arena results.
      </p>
      <div class="pilot-cards">
        {#each profiles as profile (profile)}
          {@const entries = rates[profile] ?? []}
          {@const latest = entries[0]}
          {@const deckCount =
            catalog?.decks.filter((d) => d.profiles[currentMode] === profile)
              .length ?? 0}
          <section class="panel pilot-card" aria-label={pilotLabel(profile)}>
            <h3>{pilotLabel(profile)}</h3>
            <div class="pilot-result">
              <strong
                >{latest
                  ? pilotRate(latest.wins, latest.losses, latest.draws)
                  : loading && !history
                    ? "Loading…"
                    : "Unmeasured"}</strong
              >
              <span class="small muted"
                >{latest
                  ? `${latest.wins}W / ${latest.losses}L / ${latest.draws}D`
                  : loading && !history
                    ? "Fetching saved evidence"
                    : "No recorded games"}</span
              >
            </div>
            {#if latest}<p class="small muted">
                {(latest.wins + latest.losses + latest.draws).toLocaleString()} player
                appearances · {latest.decks.size} deck {latest.decks.size === 1
                  ? "version"
                  : "versions"}
                {#if !matchesBuild(latest.run)}<span class="pilot-rate-note"
                    >Historical build or attributes</span
                  >{/if}
              </p>{/if}
            <div class="actions pilot-card-actions">
              {#if deckCount}<button
                  onclick={() => inspect(profile, "assignments")}
                  >View decks · {deckCount}</button
                >{/if}
              <button
                aria-expanded={evidenceProfile === profile}
                aria-controls={"evidence-" + profile}
                onclick={() =>
                  (evidenceProfile =
                    evidenceProfile === profile ? "" : profile)}
                >{evidenceProfile === profile
                  ? "Hide details"
                  : "Pilot details"}</button
              >
            </div>
            {#if evidenceProfile === profile}<div
                class="pilot-card-details"
                id={"evidence-" + profile}
              >
                <p class="small muted">Exact profile: <code>{profile}</code></p>
                <AiNote scope="pilot" subject={profile} />
                <h4>Recorded sessions</h4>
                {#each entries as entry}<p class="small">
                    <a href={"/runs/" + entry.run.id}
                      >{shortDate(entry.run.createdAt)} · {entry.wins}W / {entry.losses}L
                      / {entry.draws}D{active(entry.run) ? " · Partial" : ""} →</a
                    >
                    <small class="muted"
                      >{matchesBuild(entry.run)
                        ? "Current build and attributes"
                        : "Historical build or attributes"} · Seed {entry.run
                        .config.rootSeed}</small
                    >
                  </p>
                {:else}<p class="small muted">
                    No measured games with this exact profile.
                  </p>{/each}
                {#if evaluationProfiles.some((p) => p.profile === profile)}<button
                    onclick={() => inspect(profile, "evaluation")}
                    >View attribute checks →</button
                  >{/if}
              </div>{/if}
          </section>
        {/each}
      </div>
      {#if !profiles.length && !loading}<div class="empty">
          <h3>No pilots match this view.</h3>
          <p>Try another name or show all available profiles.</p>
          <button
            onclick={() => {
              query = "";
              scope = "all";
            }}>Show all profiles</button
          >
        </div>{/if}
      <AiNote subject="pilots" />
    {:else if view === "assignments"}
      <div class="section-head pilot-section-heading">
        <div>
          <h2 tabindex="-1">Deck assignments</h2>
          <p class="small muted">Preview which pilot each saved deck uses.</p>
        </div>
        <a class="button" href="/">Configure a session →</a>
      </div>
      <div class="toolbar">
        <div>
          <label for="pilot-view-policy">Assignment mode</label><AppSelect
            id="pilot-view-policy"
            label="Assignment mode"
            value={currentMode}
            onchange={(value) => (mode = value)}
            options={catalog?.modes ?? []}
          />
        </div>
        <div>
          <label for="assignment-pilot">Assigned pilot</label><AppSelect
            id="assignment-pilot"
            label="Assigned pilot"
            value={assignmentProfile}
            onchange={(value) => (selectedProfile = value)}
            options={assignedProfiles.map((profile) => ({
              value: profile,
              label: `${pilotLabel(profile)} · ${catalog?.decks.filter((d) => d.profiles[currentMode] === profile).length} decks`,
            }))}
          />
        </div>
      </div>
      <p class="small muted">
        {catalog?.modes.find((m) => m.value === currentMode)?.description}
      </p>
      <p class="pilot-preview-note small">
        <strong>Preview only.</strong> Choose the pilot policy in Deck Tester when
        configuring a session. Saved sessions keep their recorded settings.
      </p>
      <section class="panel">
        <h3>
          {assignmentProfile
            ? pilotLabel(assignmentProfile)
            : "No assigned pilots"}
        </h3>
        <p class="small muted">
          {assignedDecks.length} assigned decks · {catalog?.modes.find(
            (m) => m.value === currentMode,
          )?.label}
        </p>
        <div class="assigned-deck-grid">
          {#each assignedDecks as deck (deck.id)}
            {@const manifest = library?.decks.find(
              (row) => row.manifest.id === deck.id,
            )?.manifest}
            <a href={"/decks?deck=" + deck.id}
              >{#if manifest}<DeckIdentity
                  deck={manifest}
                  {catalog}
                />{:else}{deck.label}{/if}<span aria-hidden="true">→</span></a
            >
          {/each}
        </div>
      </section>
    {:else if view === "protocols"}
      <div class="pilot-section-heading">
        <h2 tabindex="-1">Engagement protocols</h2>
        <p class="small muted">
          Browse the situations a pilot recognizes and the decisions it makes.
        </p>
      </div>
      {#if candidateProtocols}<div class="toolbar">
          <div>
            <label for="protocol-origin">Protocol catalog</label><AppSelect
              id="protocol-origin"
              label="Protocol catalog"
              value={protocolOrigin}
              onchange={(value) => (protocolOrigin = value)}
              options={[
                { value: "active", label: "Active engine catalog" },
                {
                  value: "candidate",
                  label: "Evaluation candidate · inactive",
                },
              ]}
            />
          </div>
        </div>{/if}
      {#if displayedProtocols}<EngagementProtocols
          catalog={displayedProtocols}
          labels={catalog?.pilotLabels}
          title="Pilot responses"
          note={protocolOrigin === "candidate"
            ? "Evaluation candidate. Browsing these definitions does not activate a pilot or replace Standard defaults."
            : "Definitions from the active engine. Browsing does not change session settings."}
        />
        {#if protocolOrigin === "candidate"}<details>
            <summary>Candidate evidence identity</summary><code
              >{protocolCandidate}</code
            >
          </details>{/if}
      {:else if !loading}<div class="empty">
          <h3>No protocol catalog available.</h3>
          <p>The active engine has not supplied protocol definitions.</p>
        </div>{/if}
    {:else}
      <div class="pilot-section-heading">
        <h2 tabindex="-1">Attribute checks</h2>
        <p class="small muted">
          Inspect a pilot’s decisions in specific frozen positions. Aggression
          measures a tendency; there is no overall skill score.
        </p>
      </div>
      {#if evaluation?.error}<p class="error" role="alert">
          {evaluation.error}
        </p>{/if}
      {#if evaluation?.report}
        <div class="toolbar">
          <div>
            <label for="evaluation-pilot">Evaluated pilot</label><AppSelect
              id="evaluation-pilot"
              label="Evaluated pilot"
              value={evaluatedProfile}
              onchange={(value) => (selectedProfile = value)}
              options={evaluationProfiles.map((p) => ({
                value: p.profile,
                label: pilotLabel(p.profile),
              }))}
            />
          </div>
        </div>
        <p class="small muted">
          {evaluation.report.profiles.length} evaluated profiles · {date(
            evaluation.report.completedAt,
          )}
        </p>
        {#if !evaluation.current}<p class="notice">
            Historical report: the engine build or evaluation suite differs from
            the current one.
          </p>{/if}
        {#if score}<section class="panel">
            <h3>{pilotLabel(score.profile)}</h3>
            <p class="small muted">
              Exact profile: <code>{score.profile}</code>
            </p>
            <div class="pilot-dimensions">
              {#each score.dimensions as dimension}
                <article class="pilot-dimension">
                  <div class="section-head">
                    <h4>{dimension.label}</h4>
                    <strong class="small"
                      >{dimension.measured
                        ? `${dimension.passed} / ${dimension.total} ${dimension.key === "aggression" ? "attacks" : "passed"} · ${dimension.percent?.toFixed(1)}%`
                        : "Unmeasured"}</strong
                    >
                  </div>
                  <p class="small muted">{dimension.scope}</p>
                  {#if dimension.errors}<p class="error">
                      {dimension.errors} evaluation errors
                    </p>{/if}
                  <details>
                    <summary
                      >Observations &amp; behavior source · {dimension.cases
                        .length}</summary
                    >
                    <p class="small muted">
                      {dimension.origins.join(" · ") || "No checks recorded"}
                    </p>
                    {#each dimension.cases as check}<article>
                        <strong>{check.id} · {check.status}</strong>
                        <p>
                          Expected: {check.expected}<br />Observed: {check.actual}
                        </p>
                        <small>{check.origin} · {check.kind}</small>
                      </article>{/each}
                  </details>
                </article>
              {/each}
            </div>
          </section>{/if}
        <details>
          <summary>Report settings &amp; frozen source</summary>
          <p>
            Forge base profile: {evaluation.report.baseProfile} · Fixture seed {evaluation
              .report.fixtureSeed}
          </p>
          <dl class="metadata">
            {#each Object.entries(evaluation.report.settings) as [key, value]}<dt
              >
                {key}
              </dt>
              <dd>{value}</dd>{/each}
          </dl>
          <div class="actions">
            {#each Object.keys(evaluation.report.sourceHashes) as file}<button
                onclick={() => showSource(file)}>{file}</button
              >{/each}
          </div>
          {#if sourceError}<p class="error" role="alert">{sourceError}</p>{/if}
          {#if source}<h4>Frozen source: {source.file}</h4>
            <p class="small muted">SHA256 <code>{source.hash}</code></p>
            <textarea
              class="pilot-source"
              aria-label="Frozen evaluation source"
              readonly
              rows="20"
              value={source.text}></textarea>{/if}
        </details>
      {:else if !loading && !evaluation?.error}<div class="empty">
          <h3>No attribute evaluation recorded.</h3>
          <p>
            Frozen-position checks will appear here when an evaluation report is
            available.
          </p>
        </div>{/if}
    {/if}
  </div>
</div>

<style>
  .default-comparison { margin: 0 0 20px; padding: 16px; }
  .default-comparison h3 { margin-top: 0; }
  .table-scroll { overflow: auto; max-height: 480px; }
  .default-comparison td { vertical-align: top; min-width: 110px; overflow-wrap: anywhere; }
  #pilot-view {
    display: flow-root;
  }
  #pilot-view > .panel {
    margin: 16px 0;
    padding: 16px;
  }
  #pilot-view :global(.pilot-protocol-overview) {
    margin: 16px 0;
    padding: 16px;
  }
  .pilot-views {
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 8px;
    margin: 20px 0;
  }
  .pilot-views button {
    text-align: left;
    white-space: normal;
    padding: 12px 14px;
    border: 1px solid var(--line);
    background: var(--panel);
    border-radius: 8px;
  }
  .pilot-views strong {
    display: block;
    font-size: 13px;
    font-weight: 600;
  }
  .pilot-views span {
    display: block;
    color: var(--muted);
    font-size: 11px;
    line-height: 1.5;
    margin-top: 4px;
  }
  .pilot-views button[aria-pressed="true"] {
    border-color: var(--accent);
    box-shadow: inset 0 -2px var(--accent);
    color: var(--accent);
  }
  .pilot-section-heading {
    margin: 0 0 16px;
  }
  .pilot-section-heading h2 {
    margin: 0 0 8px;
  }
  .pilot-section-heading p {
    margin: 0;
  }
  .pilot-list-meta {
    display: flex;
    justify-content: space-between;
    align-items: center;
    flex-wrap: wrap;
    gap: 8px 16px;
    min-height: 24px;
    margin-bottom: 12px;
  }
  .pilot-history-toggle input {
    width: 16px;
    height: 16px;
    margin: 0;
  }
  .pilot-list-meta p {
    margin: 0;
  }
  .pilot-results-note {
    margin: 0 0 16px;
    max-width: 75rem;
    line-height: 1.5;
  }
  .pilot-cards {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 12px;
    align-items: stretch;
  }
  .pilot-cards:has(.pilot-card-details) {
    align-items: start;
  }
  .pilot-card {
    display: flex;
    flex-direction: column;
    margin: 0;
    padding: 16px;
    min-width: 0;
  }
  .pilot-card h3 {
    margin: 0 0 12px;
    font-size: 16px;
  }
  .pilot-result {
    display: flex;
    align-items: baseline;
    gap: 12px;
    flex-wrap: wrap;
  }
  .pilot-result > strong {
    font-size: 25px;
    line-height: 1.3;
    font-weight: 500;
  }
  .pilot-card p {
    margin: 8px 0 0;
    line-height: 1.5;
  }
  .pilot-card-actions {
    margin: auto 0 0;
    padding-top: 12px;
    gap: 8px;
  }
  .pilot-card-actions button {
    min-height: 32px;
    padding: 6px 10px;
    font-size: 12px;
  }
  .pilot-card-details {
    border-top: 1px solid var(--line);
    margin-top: 16px;
    padding-top: 6px;
    overflow-wrap: anywhere;
  }
  .pilot-card-details h4 {
    margin: 18px 0 8px;
  }
  .pilot-preview-note {
    padding: 12px 16px;
    border-left: 3px solid var(--accent);
    background: var(--panel);
  }
  .assigned-deck-grid {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 0 24px;
  }
  .assigned-deck-grid a {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 14px 0;
    border-top: 1px solid var(--line);
    min-width: 0;
  }
  .assigned-deck-grid a > span {
    color: var(--muted);
  }
  .pilot-dimension {
    padding: 16px 0;
    border-top: 1px solid var(--line);
  }
  .pilot-dimension .section-head {
    flex-wrap: wrap;
    gap: 8px;
    margin: 0;
  }
  .pilot-dimension h4,
  .pilot-dimension p {
    margin: 0;
  }
  .pilot-dimension p {
    margin-top: 8px;
  }
  .pilot-dimension details {
    margin-top: 10px;
    padding-bottom: 0;
  }
  .pilot-dimension details article {
    margin: 12px 0;
    padding: 10px;
    background: var(--bg);
  }
  @media (max-width: 1050px) {
    .pilot-views {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
  }
  @media (max-width: 700px) {
    .pilot-section-heading.section-head {
      flex-wrap: wrap;
    }
    .pilot-views {
      gap: 8px;
      margin: 20px 0;
    }
    .pilot-views button {
      padding: 12px;
    }
    .pilot-cards,
    .assigned-deck-grid {
      grid-template-columns: minmax(0, 1fr);
    }
  }
</style>
