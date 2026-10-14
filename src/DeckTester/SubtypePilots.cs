namespace DeckTester;

// Assignment only. Behavior reads the run's frozen card attributes in ForgeBridge.
public static class SubtypePilots
{
    public const string Mode = "subtypes-v001+aggro-v001+control-v1+reanimator-v9";
    public static readonly IReadOnlyDictionary<string,string> Profiles = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Auras"] = "auras-v001",
        ["Lifegain"] = "lifegain-v001",
        ["Artifacts"] = "artifacts-v001",
        ["Skeletons"] = "skeletons-v001",
        ["Jace Control"] = "jace-control-v001",
        ["Sacrifice"] = "sacrifice-v001",
        ["Dragons"] = "dragons-v001",
        ["Dwarves"] = "dwarves-v001",
        ["Planeswalkers"] = "planeswalkers-v001",
        ["Discard"] = "discard-v001",
        ["Landfall"] = "landfall-v001",
        ["Rabbits"] = "rabbits-v001",
        ["Omniscience"] = "omniscience-v001",
        ["Flash"] = "flash-v001",
        ["Fling"] = "fling-v001",
        ["Angels"] = "angels-v001",
        ["Dinosaur Reanimator"] = "dinosaur-reanimator-v001",
        ["Burn"] = "burn-v001",
        ["Midrange"] = "midrange-v001",
        ["Tokens"] = "tokens-v001",
        ["Creature Aggro"] = "creature-aggro-v001",
        ["Allies"] = "allies-v001",
        ["Spell Control"] = "spell-control-v001",
        ["Spellementals"] = "spellementals-v001",
        ["Jace Exile"] = "jace-exile-v001",
        ["Lessons"] = "lessons-v001",
        ["Superfriends"] = "superfriends-v001",
        ["Demons"] = "demons-v001",
        ["Elementals"] = "elementals-v001",
        ["Big Creatures"] = "big-creatures-v001",
    };
    public static string Profile(DeckManifest deck) => string.Equals(deck.Subtype,"Reanimator",StringComparison.OrdinalIgnoreCase)
        ? ReanimatorPilots.Profile(deck)
        : deck.Subtype is {} subtype && Profiles.TryGetValue(subtype,out var profile) ? profile : ReanimatorPilots.Profile(deck);
}
