# Changing the logo and colors

Edit `src/MtgSimulator/branding.json`. The `Branding` object controls:

- `Logo`: the sidebar logo. Put a new SVG, PNG, or WebP in `wwwroot/assets/brand/`, then set its path relative to `wwwroot`, for example `assets/brand/new-logo.svg`.
- `Icon`: the browser-tab icon. Use an SVG or PNG. Omit this setting to use the logo.
- `UiColors`: the main page colors. `bg`, `panel`, `text`, `muted`, `line`, and `accent` set the foundation; the other named entries control buttons, cards, navigation, and menus.
- `ChartColors`: exactly five entries, each with a `Name` and hex `Color`. Their order assigns colors to decks within a run. Keep five entries; the deck limit remains five.
- `DetailColors`: secondary colors retained from the existing design, including mana-symbol and status styling. These normally do not need changing.

Use hex colors, such as `#7EA3FF`. Save valid JSON and refresh the page. Once the branding settings feature is running, no rebuild or app restart is required for these changes. An already-open browser tab may need a full refresh to update its icon. A new logo filename avoids cached image versions.

The logo artwork keeps its own colors. To recolor the logo itself, edit its SVG fills or supply replacement artwork. Page and chart colors do not alter an image's contents.

Layout, spacing, and typography remain in `wwwroot/app.css`. The styling includes defaults so a settings request failure does not leave the page unstyled.

## Active Crucible identity

The selected Crucible master is `wwwroot/assets/brand/crucible/master.svg`. The browser favicon uses `small.svg`, an optical variant with fewer interior details and a light fill on dark browser chrome. The logo is static and no longer rotates through armor colors.

The shared `ManaLogo` component renders the matching sun, drop, skull, flame, tree, colorless diamond, and tap arrow across deck selection, deck library, results, and replays. CSS uses `assets/mana/crucible-*-small.svg` masks for the 16–24 px interface. Full glyph variants are saved alongside them as `crucible-*-master.svg`. These masks contain only the glyph; the colored circle comes from CSS so monochrome and forced-color modes continue working. Numbered, variable, and hybrid costs retain their labels and grouping.

The approved master and small drawings are preserved in `assets/brand/concepts/full-and-small/01/`. Palette tones 49–56 in `branding.json` supply the selected neutral and mana colors. Update the `app.css` version in `App.razor` when changing cached styling. `brand-logo.js` remains an inert compatibility asset for older pages that still reference it.

## Workspace navigation icons

The approved trio uses a flask for Simulator, fanned cards for Deck Library, and a rewind clock for Run History. Production SVGs live in `wwwroot/assets/workspace/`. The sidebar displays them as 20 px CSS masks inheriting the link color for default, selected, hover, and focus states. The text labels supply accessible names; icon spans are decorative. The compact mobile header keeps its existing text-only navigation. The approval sheet and source vectors are archived under `assets/brand/concepts/workspace-selected/`.
