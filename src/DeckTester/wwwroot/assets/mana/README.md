# Mana logos

Six original vector marks for White (sun), Blue (drop), Black (skull), Red (flame), Green (tree), and Colorless (diamond). Geometric construction preserves the familiar mana subjects using straight edges, broad facets, and simple negative space. These are custom interpretations, not official Wizards logo artwork.

The current Crucible SVGs use a 100 × 100 viewBox, transparent background, and currentColor fill. `ManaLogo` renders their outlines through the locally hosted `crucible-mana.woff2` font; colored circles provide a consistent footprint. Apply `mana-monochrome` to a parent to inherit its foreground color. Labels remain available to assistive technology. Deck names use inline glyphs followed by the subtype; color names remain in accessible labels and search text.

Regenerate the font with `python3 scripts/build-mana-font.py` from the project root, using Python with `fonttools` and `brotli` installed. The builder preserves group transforms and SVG even-odd holes. Private-use codepoints U+E600–U+E606 map to W, U, B, R, G, C, and tap, respectively. The committed WOFF2 is the runtime asset; no Python packages or network font service are required by the app.

Open preview.html to compare the complete family at 88px and 24px across four combinations. Recommended UI size: 32–40px; minimum: 24px. DeckColors displays main-deck spell colors from actual mana costs in WUBRG order, with C for no colored costs. This describes spell colors, not Commander color identity or land-source availability.

Created directly as native SVG vectors. No image-generation prompt or raster generation was used.
