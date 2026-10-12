export function jumpToDeck(id) {
    const deck = document.getElementById(id);
    if (!deck) return;
    deck.scrollIntoView({ block: 'start', behavior: 'instant' });
}
