<script lang="ts">
  import { manaStyle, toggleManaStyle } from "./mana-style.ts";
  import { manaGlyphs } from "./mana.ts";
</script>

<button
  class="mana-style-switch"
  type="button"
  aria-label="Letter mana symbols"
  aria-pressed={$manaStyle === "letters"}
  title={$manaStyle === "letters" ? "Use mana symbols" : "Use mana letters"}
  onclick={toggleManaStyle}
>
  <span aria-hidden="true" class:mana-font={$manaStyle === "symbols"}
    >{$manaStyle === "letters" ? "W" : manaGlyphs.W.glyph}</span
  >
</button>

<style>
  .mana-style-switch {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex: none;
    width: 32px;
    height: 32px;
    padding: 0;
    background: transparent;
    border: 1px solid transparent;
    color: var(--muted);
    border-radius: 6px;
  }
  .mana-style-switch:hover {
    background: var(--theme-button-hover);
    color: var(--text);
  }
  .mana-style-switch[aria-pressed="true"] {
    color: var(--accent);
    border-color: var(--line);
  }
  span {
    font:
      650 16px/1 Inter,
      sans-serif;
  }
  .mana-font {
    font-family: "Crucible Mana";
    font-size: 20px;
    font-weight: 400;
    font-synthesis: none;
  }
  @media (max-width: 700px) {
    .mana-style-switch {
      width: 40px;
      height: 40px;
    }
  }
</style>
