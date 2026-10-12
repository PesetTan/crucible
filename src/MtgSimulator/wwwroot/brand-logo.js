(() => {
    // Interactive rendering can evaluate a page script again. Advance the
    // rotation exactly once for this document, regardless of hydration.
    if (document.documentElement.hasAttribute('data-logo-cycle-initialized')) return;
    document.documentElement.setAttribute('data-logo-cycle-initialized', '');
    const colors = ['silver', 'steel-blue', 'cobalt-steel', 'verdigris', 'red-copper'];
    const cursorKey = 'gauntlet.logo.next-color.v1';
    let selected;

    function applyLogo() {
        const logos = document.querySelectorAll('[data-gauntlet-logo]');
        if (!logos.length) return;
        if (!selected) {
            let cursor = 0;
            try {
                const saved = Number(localStorage.getItem(cursorKey));
                if (Number.isInteger(saved) && saved >= 0 && saved < colors.length) cursor = saved;
                localStorage.setItem(cursorKey, String((cursor + 1) % colors.length));
            } catch {
                // The silver fallback remains usable when storage is unavailable.
            }
            selected = colors[cursor];
        }
        const animated = `assets/brand/assembly/${selected}.svg?v=wide-2`;
        for (const logo of logos) {
            if (logo.getAttribute('src') !== animated) logo.setAttribute('src', animated);
            logo.dataset.color = selected;
        }
        const icon = document.querySelector('link[rel="icon"]');
        if (icon) {
            icon.setAttribute('href', `assets/brand/assembly/${selected}-static.svg`);
            icon.setAttribute('type', 'image/svg+xml');
        }
    }

    applyLogo();
    // Blazor hydration/enhanced navigation may replace the logo node. Reuse this
    // document's choice without consuming another color in the rotation.
    new MutationObserver(applyLogo).observe(document.body, {childList: true, subtree: true});
})();
