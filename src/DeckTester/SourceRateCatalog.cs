namespace DeckTester;

// One shared library read per UI circuit, only when a Workshop ancestor is needed.
public sealed class SourceRateCatalog(IUiBackend backend)
{
    private Task<LibrarySnapshot?>? library;

    public async Task<DeckManifest?> Resolve(DeckManifest? deck)
    {
        if (deck is null || deck.PublishedWinRatePercent is not null) return deck;
        if (deck.ParentDeckId is null) return null;
        var available = backend.Decks();
        var visited = new HashSet<string> { deck.Id };
        while (deck.ParentDeckId is {} parentId && visited.Add(parentId))
        {
            var parent = available.FirstOrDefault(d => d.Id == parentId);
            if (parent is null)
            {
                available = (await (library ??= backend.LibraryState()))?.Decks.Select(d => d.Manifest).ToList() ?? available;
                parent = available.FirstOrDefault(d => d.Id == parentId);
            }
            if (parent is null) return null;
            if (parent.PublishedWinRatePercent is not null) return parent;
            deck = parent;
        }
        return null;
    }
}
