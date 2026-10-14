namespace DeckTester;

public static class ReanimatorPilots
{
    public const string Adaptive = "reanimator-adaptive-v9";
    public const string ControlMode = "control-adaptive-v1+reanimator-v9";
    public const string CurrentControlMode = "control-adaptive-v1+reanimator-v011";
    public const string AggroMode = "aggro-v001+control-v1+reanimator-v9";
    public const string LegacyAggroModeV10 = "aggro-v001+control-v1+reanimator-v10";
    public const string CurrentAggroMode = "aggro-v001+control-v1+reanimator-v011";
    public const string LegacySubtypeModeV10 = "subtypes-v001+aggro-v001+control-v1+reanimator-v10";
    public const string CurrentSubtypeMode = "subtypes-v001+aggro-v001+control-v1+reanimator-v011";
    public const string Unified = "reanimator-v011";
    public const string Aggro = "aggro-v001";
    public const string Control = "control-shared-v1";
    public const string Baseline = "forge-default-v1";
    public const string PublicFourColor = "reanimator-terror-v9";
    public const string PublicSultai = "reanimator-sultai-v9";
    public const string Personal = "reanimator-cryomancer-v9";
    public const string Shared = "reanimator-shared-v9";
    public const string Default = "forge-default";

    public static string DisplayName(string? profile)
    {
        if (profile is null) return "unknown";
        if (PilotNames.Name(profile) is {} name) return name;
        if (profile == CurrentSubtypeMode) return "Subtype pilots v001 + reanimator-v011";
        if (profile == LegacySubtypeModeV10) return "Subtype pilots v001 + reanimator-v010";
        if (profile == CurrentAggroMode) return "aggro-v001 + control-v001 + reanimator-v011";
        if (profile == LegacyAggroModeV10) return "aggro-v001 + control-v001 + reanimator-v010";
        if (profile == SubtypePilots.Mode) return "Subtype pilots v001 + reanimator-v009";
        if (profile == AggroMode) return "aggro-v001 + control-v001 + reanimator-v009";
        if (profile == Control) return "control-v001";
        if (profile == CurrentControlMode) return "control-v001 + reanimator-v011";
        if (profile == ControlMode) return "control-v001 + reanimator-v009";
        if (profile.StartsWith("reanimator-", StringComparison.Ordinal)
            && int.TryParse(profile[(profile.LastIndexOf('v') + 1)..], out var revision))
            return $"reanimator-v{revision:D3}";
        return profile;
    }

    public static string Profile(DeckManifest deck)
    {
        if (!string.Equals(deck.Subtype, "Reanimator", StringComparison.OrdinalIgnoreCase))
            return string.Equals(deck.Strategy, "Control", StringComparison.OrdinalIgnoreCase) ? Control
                : string.Equals(deck.Strategy, "Aggro", StringComparison.OrdinalIgnoreCase) ? Aggro : Default;
        var cards = deck.Cards.Where(card => card.Zone == "MainDeck" && card.Quantity > 0)
            .Select(card => card.Name).ToHashSet(StringComparer.Ordinal);
        var variants=deck.Cards.Where(card=>card.Zone=="MainDeck"&&card.Quantity>0).Select(card=>card.Attributes)
            .Where(attrs=>attrs.HasValue&&attrs.Value.ValueKind==System.Text.Json.JsonValueKind.Object)
            .Select(attrs=>attrs!.Value.TryGetProperty("pilot",out var pilot)&&pilot.ValueKind==System.Text.Json.JsonValueKind.Object&&pilot.TryGetProperty("variant",out var variant)&&variant.ValueKind==System.Text.Json.JsonValueKind.String?variant.GetString():null).ToHashSet();
        if(variants.Contains("cryomancer"))return Personal;
        if(variants.Contains("sultai"))return PublicSultai;
        if(variants.Contains("terror"))return PublicFourColor;
        if (cards.Contains("Seasoned Cryomancer")) return Personal;
        if (cards.Contains("Jadzi, Steward of Fate") || cards.Contains("Oblivious Bookworm")) return PublicSultai;
        if (cards.Contains("Terror of the Peaks")) return PublicFourColor;
        return Shared;
    }

    public static string Profile(DeckManifest deck, string mode)
    {
        if (mode is CurrentSubtypeMode or CurrentAggroMode or CurrentControlMode) {
            if (string.Equals(deck.Subtype, "Reanimator", StringComparison.OrdinalIgnoreCase)) return Unified;
            var current = mode == CurrentSubtypeMode ? SubtypePilots.Profile(deck) : Profile(deck);
            return mode == CurrentControlMode && current == Aggro ? Default : current;
        }
        if (mode is LegacySubtypeModeV10 or LegacyAggroModeV10) {
            var profile = mode == LegacySubtypeModeV10 ? SubtypePilots.Profile(deck) : Profile(deck);
            return profile.StartsWith("reanimator-",StringComparison.Ordinal) ? profile[..^1]+"10" : profile;
        }
        if (mode == SubtypePilots.Mode) return SubtypePilots.Profile(deck);
        if (mode == AggroMode) return Profile(deck);
        if (mode == ControlMode) {
            var profile = Profile(deck);
            return profile == Aggro ? Default : profile;
        }
        return mode == Adaptive && string.Equals(deck.Subtype, "Reanimator", StringComparison.OrdinalIgnoreCase)
            ? Profile(deck) : Default;
    }
    public static bool ValidMode(string mode) => mode is CurrentSubtypeMode or CurrentAggroMode or CurrentControlMode or LegacySubtypeModeV10 or LegacyAggroModeV10 or SubtypePilots.Mode or AggroMode or ControlMode or Adaptive or Baseline;
}
