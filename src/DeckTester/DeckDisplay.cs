namespace DeckTester;

// Presentation only: preserve source names, manifests, and version IDs.
public static class DeckDisplay
{
    public static char[] Symbols(DeckManifest deck)
    {
        var costs = string.Join(" ", deck.Cards.Where(c => c.Zone == "MainDeck" && c.Quantity > 0).Select(c => c.Cost));
        var colors = "WUBRG".Where(costs.Contains).ToArray();
        return colors.Length == 0 ? ['C'] : colors;
    }

    public static string Colors(DeckManifest deck) => new string(Symbols(deck)) switch
    {
        "W" => "White", "U" => "Blue", "B" => "Black", "R" => "Red", "G" => "Green", "C" => "Colorless",
        "WU" => "Azorius", "WB" => "Orzhov", "WR" => "Boros", "WG" => "Selesnya",
        "UB" => "Dimir", "UR" => "Izzet", "UG" => "Simic", "BR" => "Rakdos", "BG" => "Golgari", "RG" => "Gruul",
        "WUB" => "Esper", "WUR" => "Jeskai", "WUG" => "Bant", "WBR" => "Mardu", "WBG" => "Abzan", "WRG" => "Naya",
        "UBR" => "Grixis", "UBG" => "Sultai", "URG" => "Temur", "BRG" => "Jund",
        "WUBR" => "Artifice", "WUBG" => "Growth", "WURG" => "Altruism", "WBRG" => "Aggression", "UBRG" => "Chaos",
        _ => "Five-Color"
    };

    private static string ClassificationName(DeckManifest deck) => deck.Name.EndsWith(" — Workshop", StringComparison.Ordinal) ? deck.Name[..^11] : deck.Name;
    // Broad strategy remains available for grouping, separate from the deck label.
    public static string Type(DeckManifest deck) => DeckStrategy.Label(deck.Strategy ?? DeckStrategy.ForName(ClassificationName(deck)));
    public static string Subtype(DeckManifest deck) => deck.Subtype ?? DeckStrategy.SubtypeForName(ClassificationName(deck)) ?? "Unclassified";
    public static string Primary(DeckManifest deck) => $"{Colors(deck)} · {Subtype(deck)}";
    public static string Variant(DeckManifest deck) => string.IsNullOrWhiteSpace(deck.FriendlyName) ? $"Version {deck.Id[..Math.Min(8, deck.Id.Length)]}" : deck.FriendlyName;
    public static string Text(DeckManifest deck) => $"{Primary(deck)} · {Variant(deck)}";
}
