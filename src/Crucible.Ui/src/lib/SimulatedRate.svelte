<script lang="ts">
  import type { SimulatedRate } from "./simulated-rates.ts";
  import { date } from "./data.ts";
  let { result }: { result: SimulatedRate | undefined } = $props();
</script>

{#if result}
  <p class="simulated-rate small">
    <a
      href={"/runs/" + result.run.id}
      title={`${result.wins}W / ${result.losses}L / ${result.draws}D · ${date(result.run.createdAt)}. Completed games only; draws count in the total. Uses this session's recorded pilots, build, and opponents.`}
    >Simulated: {(result.rate * 100).toFixed(1)}% · {result.games.toLocaleString()} {result.games === 1 ? "game" : "games"}</a>
    <span class="muted">Latest session · {result.opponents} {result.opponents === 1 ? "opponent" : "opponents"}</span>
  </p>
{/if}

<style>
  .simulated-rate {
    display: grid;
    gap: 4px;
  }
  .simulated-rate a {
    color: inherit;
    text-decoration: underline;
    text-decoration-color: var(--line);
    text-underline-offset: 3px;
  }
  .simulated-rate a:hover {
    color: var(--accent);
  }
  .simulated-rate span {
    font-size: 12px;
  }
</style>
