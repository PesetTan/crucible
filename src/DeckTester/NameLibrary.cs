namespace DeckTester;

public static class NameLibrary
{
    public static IReadOnlyList<string> Adjectives { get; } = ["amber","arcane","bold","bright","brisk","clever","cosmic","curious","dancing","daring","dusky","eager","ember","fabled","fierce","gentle","glimmering","golden","hidden","jolly","lucky","lunar","merry","mystic","nimble","radiant","silver","sparkling","swift","verdant","vivid","wandering"];
    public static IReadOnlyList<string> Nouns { get; } = LoadNouns();
    public static IReadOnlyList<string> Words { get; } = Adjectives.Concat(Nouns).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<string> LoadNouns()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "naming", "nouns.txt");
        var nouns = File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (nouns.Length == 0 || nouns.Any(noun => !noun.All(char.IsLetter)))
            throw new InvalidDataException($"The noun file must contain single-word names using letters only: {path}");
        return Array.AsReadOnly(nouns);
    }
}
