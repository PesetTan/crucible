# Mana logos

Six original vector marks for White (sun), Blue (drop), Black (skull), Red (flame), Green (tree), and Colorless (diamond). Geometric construction preserves the familiar mana subjects using straight edges, broad facets, and simple negative space. These are custom interpretations, not official Wizards logo artwork.

The current Crucible SVGs use a 100 × 100 viewBox, transparent background, and currentColor fill. `ManaLogo` renders their outlines through the locally hosted `crucible-mana.woff2` font; colored circles provide a consistent footprint. Apply `mana-monochrome` to a parent to inherit its foreground color. Labels remain available to assistive technology. Deck names use inline glyphs followed by the subtype; color names remain in accessible labels and search text.

Regenerate the font with `python3 scripts/build-mana-font.py` from the project root, using Python with `fonttools` and `brotli` installed. The builder preserves group transforms and SVG even-odd holes. Private-use codepoints U+E600–U+E606 map to W, U, B, R, G, C, and tap, respectively. The committed WOFF2 is the runtime asset; no Python packages or network font service are required by the app.

The Svelte UI uses `src/Crucible.Ui/src/lib/mana.ts` as its glyph/label mapping and notation parser. Use `ManaSymbol.svelte` for a single symbol, `ManaCost.svelte` for costs, and `CardRules.svelte` for inline rules text. These components are shared by deck identities, Deck Lists, Workshop, and stored replays. They accept canonical `{2}{U}`, Arena `{o2oU}` / `{o(G/U)}`, and (for costs) Forge `2 U` notation. Keep card source strings unchanged.

This local font contains seven glyphs, not the full Magic symbol set. Numeric and variable costs use readable text inside neutral circles; hybrids group their alternatives using the same mapped glyphs. Symbols absent from the font retain readable notation and accessible labels. Add future glyphs to both the font builder and `mana.ts`; never map an unknown symbol to colorless. The mapping and parser checks live in `src/Crucible.Ui/tests/mana.test.ts`.

The mana toggle beside the theme control switches all shared components between font glyphs and letter badges. `mana-style.ts` owns this preference and persists it per browser/WebView in `crucible:mana-style:v1`, defaulting to symbols. Labels, colors, and underlying card data stay the same in either mode.

Open preview.html to compare the complete family at 88px and 24px across four combinations. Recommended UI size: 32–40px; minimum: 24px. DeckColors displays main-deck spell colors from actual mana costs in WUBRG order, with C for no colored costs. This describes spell colors, not Commander color identity or land-source availability.

Created directly as native SVG vectors. No image-generation prompt or raster generation was used.
