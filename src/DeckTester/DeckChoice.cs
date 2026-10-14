namespace DeckTester;

public sealed record DeckChoice(string Value, DeckManifest? Deck, DeckManifest? Opponent = null, string? Context = null, DateTimeOffset? SessionDate = null)
{
    public string Group => Deck is null ? "Unavailable decks" : DeckDisplay.Colors(Deck);
    public string Label => Deck is null ? "Unavailable deck version" : DeckDisplay.Text(Deck) + (Opponent is null ? "" : " vs " + DeckDisplay.Text(Opponent)) + (Context is null ? "" : " · " + Context);
}

public static class DeckChoiceGroups
{
    public static List<IGrouping<string, DeckChoice>> Group(IEnumerable<DeckChoice> choices) => choices
        .OrderBy(choice => choice.Deck is null ? 1 : 0).ThenBy(choice => choice.Group, StringComparer.OrdinalIgnoreCase)
        .ThenBy(choice => choice.Deck is null ? "" : DeckDisplay.Text(choice.Deck), StringComparer.OrdinalIgnoreCase)
        .ThenBy(choice => choice.Opponent is null ? "" : DeckDisplay.Text(choice.Opponent), StringComparer.OrdinalIgnoreCase)
        .ThenByDescending(choice => choice.SessionDate).ThenBy(choice => choice.Value, StringComparer.Ordinal)
        .GroupBy(choice => choice.Group).ToList();
}
