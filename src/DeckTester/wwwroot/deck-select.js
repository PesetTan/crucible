const bindings = new WeakMap();
export function attach(root, dotnet) {
    const closeOutside = event => { if (!root.contains(event.target)) dotnet.invokeMethodAsync('Dismiss').catch(() => {}); };
    const keyGuard = event => {
        if (event.target.getAttribute('role') === 'combobox' && ['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter', ' ', 'Escape'].includes(event.key)) event.preventDefault();
    };
    document.addEventListener('pointerdown', closeOutside);
    document.addEventListener('focusin', closeOutside);
    root.addEventListener('keydown', keyGuard);
    const reposition = () => position(root);
    window.addEventListener("resize", reposition);
    document.addEventListener("scroll", reposition, true);
    bindings.set(root, { closeOutside, keyGuard, reposition });
}
export function position(root) {
    const list = root.querySelector('[role="listbox"]');
    if (!list) return;
    const rect = root.getBoundingClientRect();
    const below = window.innerHeight - rect.bottom - 12;
    const above = rect.top - 12;
    const upwards = below < 180 && above > below;
    list.style.top = upwards ? "auto" : "calc(100% + 5px)";
    list.style.bottom = upwards ? "calc(100% + 5px)" : "auto";
    list.style.maxHeight = `${Math.max(80, Math.min(420, upwards ? above : below))}px`;
}
export function scrollActive(root, id) {
    const list = root.querySelector('[role="listbox"]');
    const option = document.getElementById(id);
    if (!list || !option) return;
    const top = option.offsetTop;
    if (top < list.scrollTop) list.scrollTop = top;
    else if (top + option.offsetHeight > list.scrollTop + list.clientHeight) list.scrollTop = top + option.offsetHeight - list.clientHeight;
}
export function detach(root) {
    const binding = bindings.get(root);
    if (!binding) return;
    document.removeEventListener('pointerdown', binding.closeOutside);
    document.removeEventListener('focusin', binding.closeOutside);
    root.removeEventListener('keydown', binding.keyGuard);
    window.removeEventListener("resize", binding.reposition);
    document.removeEventListener("scroll", binding.reposition, true);
    bindings.delete(root);
}
