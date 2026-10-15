using System.Collections.Frozen;
using System.Text.Json;

namespace DeckTester;

// Display aliases only. Assignment, saved evidence and caches retain exact profile IDs.
public static class PilotNames
{
    public static IReadOnlyDictionary<string, string> ById { get; } = Load();

    public static string? Name(string? profile) => profile is not null && ById.TryGetValue(profile, out var name) ? name : null;

    private static IReadOnlyDictionary<string, string> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "naming", "pilots.json");
        var names = Json.Decode<Dictionary<string, string>>(File.ReadAllText(path));
        if (names.Count == 0 || names.Any(entry => string.IsNullOrWhiteSpace(entry.Key)
            || !NameLibrary.Nouns.Contains(entry.Value, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Pilot names must come from naming/nouns.txt: {path}");
        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
