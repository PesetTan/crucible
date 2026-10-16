<script lang="ts">
  import {
    manaGlyphs,
    symbolLabel,
    isGenericSymbol,
    hybridParts,
  } from "./mana.ts";
  import { manaStyle } from "./mana-style.ts";
  let { symbol, decorative = false }: { symbol: string; decorative?: boolean } =
    $props();
  let token = $derived(symbol.toUpperCase());
  let mark = $derived(manaGlyphs[token]);
  let parts = $derived(hybridParts(token));
</script>

<span
  class="mana-mark"
  class:mana-mark-hybrid={parts.length > 0}
  class:mana-mark-fallback={!mark && !isGenericSymbol(token) && !parts.length}
  role={decorative ? undefined : "img"}
  aria-label={decorative ? undefined : symbolLabel(token)}
  aria-hidden={decorative ? true : undefined}
  title={decorative ? undefined : symbolLabel(token)}
>
  {#snippet face(value: string)}
    {@const glyph = manaGlyphs[value]}
    <span
      class={`mana-face mana-${glyph?.name ?? "colorless"}`}
      class:mana-font={!!glyph && $manaStyle === "symbols"}
    >
      <span class="mana-character"
        >{glyph && $manaStyle === "symbols" ? glyph.glyph : value}</span
      >
    </span>
  {/snippet}
  {#if mark || isGenericSymbol(token)}
    {@render face(token)}
  {:else if parts.length}
    {#each parts as part, i}{#if i}<span class="mana-slash">/</span
        >{/if}{@render face(part)}{/each}
  {:else}<span class="mana-fallback">{`{${symbol}}`}</span>{/if}
</span>

<style>
  .mana-mark {
    display: inline-flex;
    flex: none;
    align-items: center;
    vertical-align: middle;
    font-size: inherit;
    line-height: 1;
    white-space: nowrap;
  }
  .mana-face {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex: none;
    width: 1.15em;
    height: 1.15em;
    border: 1px solid var(--theme-mana-border, var(--line));
    border-radius: 50%;
    background: var(--mana-tint, var(--theme-mana-neutral, #bdbdbd));
    color: var(--theme-mana-ink, #161616);
    font-family: Inter, sans-serif;
    font-weight: 650;
    font-style: normal;
    font-size: inherit;
    line-height: 1;
    letter-spacing: 0;
    text-transform: none;
  }
  .mana-font {
    font-family: "Crucible Mana";
    font-weight: 400;
    font-synthesis: none;
  }
  /* Size the text independently so padding never changes the badge diameter. */
  .mana-character {
    display: block;
    font-size: 0.72em;
    line-height: 1;
  }
  .mana-font .mana-character {
    font-size: 1em;
  }
  .mana-mark-hybrid {
    gap: 1px;
    padding: 1px;
    border: 1px solid var(--theme-mana-border, var(--line));
    border-radius: 1em;
  }
  .mana-mark-hybrid .mana-face {
    width: 0.95em;
    height: 0.95em;
    border: 0;
  }
  .mana-slash {
    font:
      0.75em/1 Inter,
      sans-serif;
    color: var(--muted);
  }
  .mana-fallback {
    font:
      0.8em/1.2 Inter,
      sans-serif;
    color: var(--text);
  }
  @media (forced-colors: active) {
    .mana-face {
      background: Canvas;
      color: CanvasText;
      border-color: CanvasText;
      forced-color-adjust: none;
    }
  }
</style>
