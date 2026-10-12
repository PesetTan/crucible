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

## Active animated logo

The sidebar now uses once-only armor assembly SVGs from wwwroot/assets/brand/assembly/. Each animation accelerates like magnetic attraction, completes in 0.8 seconds, and stays assembled. Reduced-motion preferences show the completed mark immediately. The matching browser icon is static.

Each full document load advances one step through silver → steel blue → cobalt steel → verdigris → red copper → silver. The next index is saved in browser local storage under gauntlet.logo.next-color.v1. App navigation and hydration reuse the current document color. If storage is unavailable, silver is used. The configured Logo and Icon are the silver fallback; brand-logo.js selects the active colorway. Generated assembly assets can be rebuilt with concepts/build_logo_assets.py.

The wider assembly starts plates three times farther from their final positions. An expanded transparent viewport preserves the finished logo size and leaves room for the approach. The motion still accelerates magnetically, plays once, and holds; the five-color rotation remains unchanged.
