// Apply the saved preference before CSS paints; theme changes stay local to this browser.
(() => {
    const storageKey = "crucible:theme:v1";
    const root = document.documentElement;
    const normalize = value => value === "dark" ? "dark" : "light";
    let theme = "light";
    try { theme = normalize(localStorage.getItem(storageKey)); } catch { /* Storage may be disabled. */ }

    function syncControls() {
        document.querySelectorAll("[data-theme-switch]").forEach(button => {
            const checked = String(theme === "dark");
            if (button.getAttribute("aria-checked") !== checked) button.setAttribute("aria-checked", checked);
            const title = theme === "dark" ? "Switch to light theme" : "Switch to dark theme";
            if (button.title !== title) button.title = title;
        });
    }

    function sync() {
        root.dataset.theme = theme;
        syncControls();
        const meta = document.querySelector('meta[name="theme-color"]');
        const background = getComputedStyle(root).getPropertyValue("--theme-bg").trim();
        if (meta && background) meta.content = background;
    }

    root.dataset.theme = theme;
    document.addEventListener("click", event => {
        if (!(event.target instanceof Element) || !event.target.closest("[data-theme-switch]")) return;
        theme = theme === "dark" ? "light" : "dark";
        try { localStorage.setItem(storageKey, theme); } catch { /* The switch still works without storage. */ }
        sync();
    });
    window.addEventListener("storage", event => {
        if (event.key === storageKey || event.key === null) {
            theme = normalize(event.newValue);
            sync();
        }
    });
    document.addEventListener("DOMContentLoaded", () => {
        sync();
        // Blazor replaces prerendered controls when the interactive circuit connects.
        // Keep the client-owned switch state accurate during hydration and navigation.
        new MutationObserver(syncControls).observe(document.body, {
            childList: true, subtree: true, attributes: true, attributeFilter: ["aria-checked"]
        });
        window.Blazor?.addEventListener("enhancedload", sync);
    });
})();
