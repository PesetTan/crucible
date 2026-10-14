# Changing the logo and colors

Edit `src/DeckTester/branding.json`. It is the central palette for the interface; layout, spacing, and typography stay in `wwwroot/app.css` and component styles.

The `Branding` object controls:

- `Logo`: the sidebar logo, relative to `wwwroot`. Place SVG, PNG, or WebP artwork in `wwwroot/assets/brand/`. The sidebar uses the image's silhouette as a mask and colors it with the theme's `logo` token, so it stays readable in both themes. Use artwork with a transparent background.
- `Icon`: the browser-tab icon. This retains the artwork's own colors. Omit it to use the logo.
- `Themes.Light` and `Themes.Dark`: named color tokens for each appearance. `bg`, `panel`, `text`, `muted`, `line`, and `accent` set the foundation. The remaining names describe their purpose, such as `input`, `card-selected`, `notice-text`, `error-bg`, `success`, `chart-grid`, and `logo`.
- `SharedColors`: mana colors and shadow colors shared by both themes. `mana-ink` controls the glyphs; `mana-white` through `mana-green` and `mana-neutral` control their circles.
- `ChartColors`: exactly five entries. `Name` supplies the legend label, `LightColor` supplies the color in the light theme, and `Color` supplies the color in the dark theme. Omitting `LightColor` uses `Color` for both. Their order assigns a stable color identity to each deck within a run.

For example, change `Themes.Dark.accent` to adjust links, focus outlines, and progress indicators in the dark theme. Change `Themes.Dark.primary` and `primary-text` to customize the main action button. Edit the same keys under `Themes.Light` to customize the light theme.

Use hex colors such as `#DFBD83` (three, four, six, or eight hex digits). Save valid JSON and refresh the page. The settings reload without a rebuild or app restart. An already-open browser tab may need a full refresh to update its icon; a new logo filename avoids cached image versions.

`BrandSettings.StyleSheet()` validates the token names and hex values and exposes `/branding.css`. Light colors and shared colors apply to `:root`; dark colors apply to `:root[data-theme="dark"]`. Component styles consume `--theme-<name>` variables rather than numbered palette tones or embedded colors. Keep both theme objects populated with matching token names when adding new roles.

The Dark theme switch sits directly below the sidebar logo, including in the mobile header. It uses a native keyboard-operable button with `role="switch"` and `aria-checked`. The light theme is the default; explicit choices are saved locally under `crucible:theme:v1`, restored before the page paints, and synchronized across browser tabs. If browser storage is disabled, the switch still works for the current page.

## Active Crucible identity

The selected Crucible master is `wwwroot/assets/brand/crucible/master.svg`. The browser favicon uses `small.svg`, an optical variant with fewer interior details and a light fill on dark browser chrome. The logo is static.

The shared `ManaLogo` component renders the matching sun, drop, skull, flame, tree, colorless diamond, and tap arrow using the bundled Crucible Mana font. Full and small SVG variants are saved in `wwwroot/assets/mana/`; the approved drawings are preserved in `assets/brand/concepts/full-and-small/01/`. The colored circles and glyph ink come from `SharedColors`, so their identities remain consistent across themes. Numbered, variable, and hybrid costs retain their labels and grouping; monochrome and forced-color modes are supported.

`App.razor` uses fingerprinted assets for `app.css` and `theme.js`. `brand-logo.js` remains an inert compatibility asset for older pages.

## Workspace navigation icons

The approved trio uses a flask for Simulator, fanned cards for Deck Library, and a rewind clock for Run History. Production SVGs live in `wwwroot/assets/workspace/`. The sidebar displays them as 20 px CSS masks inheriting the link color for default, selected, hover, and focus states. The text labels supply accessible names; icon spans are decorative. The compact mobile header uses text-only navigation. The approval sheet and source vectors are archived under `assets/brand/concepts/workspace-selected/`.
