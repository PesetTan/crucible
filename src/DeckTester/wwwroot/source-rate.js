// Native popovers keep source details above scrollable tables and menu containers.
(() => {
    let active;
    let closeTimer;
    const anchorFor = target => target instanceof Element ? target.closest("[data-source-rate]") : null;

    function close() {
        clearTimeout(closeTimer);
        if (!active) return;
        const popup = active.querySelector(".source-rate-popup");
        if (popup?.matches(":popover-open")) popup.hidePopover();
        popup?.classList.remove("is-open");
        active = null;
    }

    function open(anchor) {
        if (!anchor) return;
        clearTimeout(closeTimer);
        if (active === anchor) return;
        close();
        const popup = anchor.querySelector(".source-rate-popup");
        if (!popup) return;
        active = anchor;
        if (typeof popup.showPopover === "function") popup.showPopover();
        else popup.classList.add("is-open");
        position(anchor, popup);
    }

    function position(anchor, popup) {
        const box = anchor.getBoundingClientRect();
        const size = popup.getBoundingClientRect();
        const left = Math.max(12, Math.min(box.left, innerWidth - size.width - 12));
        const below = box.bottom + 8;
        const top = below + size.height <= innerHeight - 12 ? below : Math.max(12, box.top - size.height - 8);
        popup.style.left = `${left}px`;
        popup.style.top = `${top}px`;
    }

    document.addEventListener("pointerover", event => open(anchorFor(event.target)));
    document.addEventListener("focusin", event => {
        const anchor = anchorFor(event.target);
        if (anchor && event.target !== anchor)
            event.target.setAttribute("aria-describedby", anchor.querySelector(".source-rate-popup").id);
        open(anchor);
    });
    document.addEventListener("pointerout", event => {
        if (anchorFor(event.target) === active && anchorFor(event.relatedTarget) !== active)
            closeTimer = setTimeout(() => { if (!active?.contains(document.activeElement)) close(); }, 120);
    });
    document.addEventListener("focusout", event => {
        if (anchorFor(event.target) === active && anchorFor(event.relatedTarget) !== active) close();
    });
    document.addEventListener("keydown", event => { if (event.key === "Escape") close(); });
    document.addEventListener("click", event => {
        const anchor = anchorFor(event.target);
        if (anchor) open(anchor); else close();
    });
    window.addEventListener("resize", close);
    document.addEventListener("scroll", () => {
        if (active?.contains(document.activeElement)) position(active, active.querySelector(".source-rate-popup"));
        else close();
    }, true);
})();
