// Keep native disclosure behavior and saved preferences; animate only user-opened content.
(() => {
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const requested = new WeakSet();
    document.addEventListener('click', event => {
        if (event.defaultPrevented || !(event.target instanceof Element)) return;
        const summary = event.target.closest('summary');
        const details = summary?.parentElement;
        if (!(details instanceof HTMLDetailsElement) || details.querySelector(':scope > summary') !== summary) return;
        if (event.target.closest('a,button,input,select,textarea')) return;
        requested.add(details);
    });
    document.addEventListener('toggle', event => {
        const details = event.target;
        if (!(details instanceof HTMLDetailsElement) || !requested.has(details)) return;
        requested.delete(details);
        if (!details.open || reduced.matches) return;
        for (const child of details.children) {
            if (child.tagName === 'SUMMARY' || typeof child.animate !== 'function') continue;
            child.animate([
                { opacity: .75, transform: 'translateY(-2px)' },
                { opacity: 1, transform: 'translateY(0)' }
            ], { duration: 180, easing: 'cubic-bezier(.2,.7,.3,1)' });
        }
    }, true);
})();
