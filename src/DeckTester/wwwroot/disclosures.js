// Native disclosures retain browser-local preferences within an opted-in page scope.
(() => {
    const bound = new WeakMap();
    const prefix = "crucible:disclosures:v1:";
    const selector = "[data-disclosure-scope] details";
    const clean = text => text?.replace(/\s+/g, " ").trim() ?? "";

    function keyFor(details) {
        const scope = details.closest("[data-disclosure-scope]");
        const parts = [];
        for (let node = details; node && node !== scope; node = node.parentElement) {
            if (node.matches("details")) {
                const summary = node.querySelector(":scope > summary");
                parts.unshift(node.dataset.disclosureKey || summary?.getAttribute("aria-label") || clean(summary?.textContent));
            } else if (node.matches(".chart")) {
                parts.unshift(clean(node.querySelector("h3")?.textContent));
            }
        }
        return prefix + scope.dataset.disclosureScope + ":" + JSON.stringify(parts);
    }

    function restore() {
        document.querySelectorAll(selector).forEach(details => {
            const key = keyFor(details);
            if (bound.get(details)?.key === key) return;
            let open = false;
            try { open = localStorage.getItem(key) === "true"; } catch { /* Storage is optional. */ }
            bound.set(details, { key, open });
            details.open = open;
        });
    }

    document.addEventListener("toggle", event => {
        const details = event.target;
        if (!(details instanceof HTMLDetailsElement) || !details.matches(selector)) return;
        const state = bound.get(details);
        // Restoring a saved choice also emits toggle; only save actual changes.
        if (!state || state.open === details.open) return;
        state.open = details.open;
        try { localStorage.setItem(state.key, String(details.open)); } catch { /* Keep native toggling usable. */ }
        // Repeated charts with the same meaning share the same preference.
        document.querySelectorAll(selector).forEach(peer => {
            const peerState = bound.get(peer);
            if (peer !== details && peerState?.key === state.key) {
                peerState.open = details.open;
                peer.open = details.open;
            }
        });
    }, true);

    // Blazor can mount sections after the initial page load or update chart content.
    new MutationObserver(restore).observe(document.body, { childList: true, subtree: true });
    restore();
})();
