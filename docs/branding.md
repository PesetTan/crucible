# Changing the logo and colors

Edit `src/DeckTester/branding.json`. It is the central palette for the interface; the active Svelte UI reuses its theme tokens and compatibility styles, then applies shared layout in `src/Crucible.Ui/src/spacing.css` and `PageHeader.svelte`. See [visual design and Deck Lists](specs/11-visual-design-and-deck-lists.md) for the spacing and content-presentation contract. Legacy Blazor styles remain in `wwwroot/app.css`, `wwwroot/theme-polish.css`, and component styles.

The `Branding` object controls:

- `Logo`: the complete master artwork, relative to `wwwroot`, also used as the icon fallback. Place transparent SVG, PNG, or WebP artwork in `wwwroot/assets/brand/`.
- `LogoBowl` and `LogoFlame`: separate sidebar mask assets. Defaults are `assets/brand/crucible/bowl.svg` and `flame.svg`. The current sidebar colors them with `brand-bowl` and `brand-flame` theme tokens. Changing only `Logo` does not replace these layers.
- `Icon`: the browser-tab icon. This retains the artwork's own colors. Omit it to use the logo.
- `Themes.Light` and `Themes.Dark`: named color tokens for each appearance. `bg`, `panel`, `text`, `muted`, `line`, and `accent` set the foundation. The remaining names describe their purpose, such as `input`, `card-selected`, `notice-text`, `error-bg`, `success`, `chart-grid`, and `logo`.
- `SharedColors`: mana colors and shadow colors shared by both themes. `mana-ink` controls the glyphs; `mana-white` through `mana-green` and `mana-neutral` control their circles.
- `ChartColors`: ten entries, matching `SimulationLimits.MaximumDecks`. `Name` supplies the palette name, `LightColor` supplies the color in the light theme, and `Color` supplies the color in the dark theme. Omitting `LightColor` uses `Color` for both. Their order assigns a stable color identity to each deck within a run.

For example, change `Themes.Dark.accent` to adjust links, focus outlines, and progress indicators in the dark theme. Change `Themes.Dark.primary` and `primary-text` to customize the main action button. Edit the same keys under `Themes.Light` to customize the light theme.

Use hex colors such as `#DFBD83` (three, four, six, or eight hex digits). Save valid JSON and refresh the page. In a source-launched UI the settings reload without a rebuild or app restart. Monitor runs an immutable published frontend, so checkout edits require **Build & Restart → Frontend** to reach that runtime. An already-open browser tab may need a full refresh to update its icon; a new logo filename avoids cached image versions.

`BrandSettings.StyleSheet()` validates the token names and hex values and exposes `/branding.css`. Light colors and shared colors apply to `:root`; dark colors apply to `:root[data-theme="dark"]`. Component styles consume `--theme-<name>` variables rather than numbered palette tones or embedded colors. Keep both theme objects populated with matching token names when adding new roles.

The Dark theme switch sits in the sidebar utilities below Local Execution, with a compact mobile layout. It uses a native keyboard-operable button with `role="switch"` and `aria-checked`. The light theme is the default; explicit choices are saved locally under `crucible:theme:v1`, restored before the page paints, and synchronized across browser tabs. If browser storage is disabled, the switch still works for the current page.

## Active Crucible identity

The selected Crucible master is `wwwroot/assets/brand/crucible/master.svg`; the sidebar uses its bowl/flame layers. The browser favicon uses `small.svg`, an optical variant with fewer interior details and a light fill on dark browser chrome. The flame has a small hover/focus effect; reduced-motion preferences disable it. The native Monitor uses the same bowl/flame identity, with flame color indicating health.

The shared `ManaLogo` component renders the matching sun, drop, skull, flame, tree, colorless diamond, and tap arrow using the bundled Crucible Mana font. Full and small SVG variants are saved in `wwwroot/assets/mana/`; the approved drawings are preserved in `assets/brand/concepts/full-and-small/01/`. The colored circles and glyph ink come from `SharedColors`, so their identities remain consistent across themes. Numbered, variable, and hybrid costs retain their labels and grouping; monochrome and forced-color modes are supported.

`App.razor` uses fingerprinted styles and scripts, including `app.css`, `theme-polish.css`, and `theme.js`. `brand-logo.js` remains an inert compatibility asset for older pages.

## Workspace navigation icons

The sidebar links to Deck Tester, Deck Library, Deck Workshop, Forge Pilots, and Run History. Production SVGs live in `wwwroot/assets/workspace/`; the original approved trio used a flask, fanned cards, and a rewind clock. The sidebar displays decorative CSS masks inheriting the link color for default, selected, hover, and focus states. Text supplies the accessible names. The compact mobile navigation uses text labels. The approval sheet and source vectors are archived under `assets/brand/concepts/workspace-selected/`.
