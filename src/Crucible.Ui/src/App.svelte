<script lang="ts">
  import { onMount } from "svelte";
  import { api, clearCache } from "./lib/api.ts";
  import type { Catalog } from "./lib/types.ts";
  import Home from "./pages/Home.svelte";
  import Decks from "./pages/Decks.svelte";
  import Runs from "./pages/Runs.svelte";
  import RunDetail from "./pages/RunDetail.svelte";
  import Workshop from "./pages/Workshop.svelte";
  import Pilots from "./pages/Pilots.svelte";
  import SessionLog from "./pages/SessionLog.svelte";
  import Replay from "./pages/Replay.svelte";
  let route = $state(location.pathname + location.search),
    catalog = $state<Catalog>(),
    error = $state(""),
    dark = $state(document.documentElement.dataset.theme === "dark");
  const nav = [
    ["/", "Deck Tester", "simulator"],
    ["/decks", "Deck Library", "decks"],
    ["/workshop", "Deck Workshop", "workshop"],
    ["/pilots", "Forge Pilots", "pilots"],
    ["/runs", "Run History", "history"],
  ];
  function navigate(path: string) {
    if (path === route) return;
    clearCache();
    history.pushState({}, "", path);
    route = location.pathname + location.search;
    window.scrollTo({ top: 0 });
  }
  function click(e: MouseEvent) {
    if (
      e.defaultPrevented ||
      e.button !== 0 ||
      e.metaKey ||
      e.ctrlKey ||
      e.altKey ||
      e.shiftKey
    )
      return;
    const a = (e.target as Element).closest("a");
    if (!a || a.target || a.hasAttribute("download")) return;
    const url = new URL(a.href);
    if (
      url.origin !== location.origin ||
      url.pathname.startsWith("/api/") ||
      url.hash
    )
      return;
    e.preventDefault();
    navigate(url.pathname + url.search);
  }
  async function loadCatalog() {
    try {
      catalog = await api<Catalog>("/api/v1/presentation/catalog", {
        fresh: true,
      });
      error = "";
    } catch (e) {
      error = (e as Error).message;
    }
  }
  function theme() {
    dark = !dark;
    document.documentElement.dataset.theme = dark ? "dark" : "light";
    try {
      localStorage.setItem("crucible:theme:v1", dark ? "dark" : "light");
    } catch {}
  }
  onMount(() => {
    void loadCatalog();
    const pop = () => {
      clearCache();
      route = location.pathname + location.search;
    };
    window.addEventListener("popstate", pop);
    document.addEventListener("click", click);
    return () => {
      window.removeEventListener("popstate", pop);
      document.removeEventListener("click", click);
    };
  });
  let path = $derived(route.split("?")[0]);
  let segments = $derived(path.split("/").filter(Boolean));
</script>

<a class="skip-link" href="#main-content">Skip to content</a>
<div class="shell">
  <aside class="sidebar">
    <a class="brand" href="/" aria-label="Crucible home"
      ><span class="brand-logo-wrap"
        ><span class="brand-logo" aria-hidden="true"
          ><span
            class="brand-logo-bowl"
            style="--brand-mask:url('/assets/brand/crucible/bowl.svg')"
          ></span><span
            class="brand-logo-flame"
            style="--brand-mask:url('/assets/brand/crucible/flame.svg')"
          ></span></span
        ></span
      ><span>Crucible<small>STANDARD · BO1</small></span></a
    >
    <div class="nav-label">WORKSPACE</div>
    <nav aria-label="Workspace">
      {#each nav as [href, title, icon]}<a
          {href}
          class:active={href === "/" ? path === "/" : path.startsWith(href)}
          aria-current={(href === "/" ? path === "/" : path.startsWith(href))
            ? "page"
            : undefined}
          ><i
            class="workspace-icon"
            style={`--workspace-icon:url('/assets/workspace/${icon}.svg')`}
            aria-hidden="true"
          ></i><span>{title}</span></a
        >{/each}
    </nav>
    <div class="sidebar-utilities">
      <div class="sidebar-note">
        <strong><span class="status-dot"></span> Local Execution</strong>
        <p>Full games. Turns logged.<br />No model calls during play.</p>
      </div>
      <button
        class="theme-switch"
        type="button"
        role="switch"
        aria-label="Dark theme"
        aria-checked={dark}
        title={dark ? "Switch to light theme" : "Switch to dark theme"}
        onclick={theme}
        ><svg
          class="theme-icon-moon"
          aria-hidden="true"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.7"
          stroke-linecap="round"
          stroke-linejoin="round"
          ><path
            d="M20.5 13.1A8.5 8.5 0 0 1 10.9 3.5 8.5 8.5 0 1 0 20.5 13.1Z"
          /></svg
        ><svg
          class="theme-icon-sun"
          aria-hidden="true"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.7"
          stroke-linecap="round"
          ><circle cx="12" cy="12" r="4" /><path
            d="M12 2v2m0 16v2M2 12h2m16 0h2M4.9 4.9l1.4 1.4m11.4 11.4 1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"
          /></svg
        ></button
      >
    </div>
  </aside>
  <div class="main">
    <main id="main-content">
      {#if error}<div class="error" role="alert">
          {error}<button onclick={loadCatalog}>Reconnect</button>
        </div>{/if}{#key route}{#if path === "/"}<Home
            {catalog}
            {navigate}
            refreshCatalog={loadCatalog}
          />{:else if path === "/decks"}<Decks
            {catalog}
            {route}
            refreshCatalog={loadCatalog}
          />{:else if path === "/runs"}<Runs
            {catalog}
          />{:else if path === "/workshop"}<Workshop
            {catalog}
            {route}
            {navigate}
            refreshCatalog={loadCatalog}
          />{:else if path === "/pilots"}<Pilots
            {catalog}
          />{:else if segments[0] === "runs" && segments[2] === "logs"}<SessionLog
            id={segments[1]}
            kind={segments[3]}
            {catalog}
          />{:else if segments[0] === "runs" && segments[2] === "games"}<Replay
            runId={segments[1]}
            id={segments[3]}
            {catalog}
          />{:else if segments[0] === "runs" && segments.length === 2}<RunDetail
            id={segments[1]}
            {catalog}
            {navigate}
          />{:else}<h1>Page Not Found.</h1>
          <a href="/">Return to Deck Tester</a>{/if}{/key}
    </main>
    <footer>
      Forge Rules Engine · London Mulligans · Automated Pilot Results
    </footer>
  </div>
</div>
