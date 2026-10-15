using System.Text.Json.Serialization;
using DeckTester;

namespace Crucible.UiHost;

public sealed record UiMode(string Value, string Label, string Description);
public sealed record UiDeck(string Id, string Colors, string Type, string Subtype, string Variant, string Label, Dictionary<string, string> Profiles);
public sealed record UiCatalog(int MaximumDecks, string DefaultPilotMode, List<UiMode> Modes, List<UiDeck> Decks, Dictionary<string, string> PilotNames, Dictionary<string, string> PilotLabels, List<string> CurrentProfiles);
public sealed record UiBuild(string Engine, string Bridge);
public sealed record EstimateReply(PreflightResponse Preflight, RuntimeRange? Runtime);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UiCatalog))]
[JsonSerializable(typeof(UiBuild))]
[JsonSerializable(typeof(EstimateReply))]
internal partial class UiJsonContext : JsonSerializerContext;

public static class Presentation
{
    private static readonly List<string> CurrentProfiles = new[] { ReanimatorPilots.Default, ReanimatorPilots.Unified, ReanimatorPilots.Engagement, ReanimatorPilots.Aggro, ReanimatorPilots.Control }.Concat(SubtypePilots.Profiles.Values).Distinct().ToList();
    private static string PilotLabel(string profile)
    {
        var role = profile switch
        {
            ReanimatorPilots.Default => "Forge Default",
            ReanimatorPilots.Control => "Control v001",
            _ => string.Join(" ", profile.Split('-').Select(word => word.Length == 0 || word[0] == 'v' && word.Length > 1 && char.IsDigit(word[1]) ? word : char.ToUpperInvariant(word[0]) + word[1..]))
        };
        return PilotNames.Name(profile) is { } name ? $"{name} · {role}" : role;
    }
    public static readonly List<UiMode> Modes = [
        new(ReanimatorPilots.CurrentAggroMode,"Standard (default)","Aggro, Control, Reanimator, or Forge Default according to the saved deck."),
        new(ReanimatorPilots.EngagementMode,"Standard + Engagement Protocols (Preview)","Reanimator combines public observations and bounded Engagement Protocols. Opt-in."),
        new(ReanimatorPilots.CurrentSubtypeMode,"By deck subtype","Reviewed subtype policies, with Reanimator taking precedence."),
        new(ReanimatorPilots.CurrentControlMode,"Control + Reanimator","Control and Reanimator policies; other decks use Forge Default."),
        new(ReanimatorPilots.Baseline,"Forge Default","Forge's built-in pilot for all decks.")
    ];
    public static UiCatalog Catalog(LibrarySnapshot library) => new(SimulationLimits.MaximumDecks, ReanimatorPilots.CurrentAggroMode, Modes,
        library.Decks.Select(row => { var d = row.Manifest; return new UiDeck(d.Id, DeckDisplay.Colors(d), DeckDisplay.Type(d), DeckDisplay.Subtype(d), DeckDisplay.Variant(d), DeckDisplay.Text(d), Modes.ToDictionary(m => m.Value, m => ReanimatorPilots.Profile(d, m.Value))); }).ToList(), PilotNames.ById.ToDictionary(), PilotNames.ById.Keys.Concat(CurrentProfiles).Distinct().ToDictionary(p => p, PilotLabel), CurrentProfiles);
}
