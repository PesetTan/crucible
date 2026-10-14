namespace DeckTester;

public static class DeckStrategy
{
    // These broad play styles are editorial labels, separate from exact-list fingerprints.
    private static readonly Dictionary<string, string> ByName = new(StringComparer.Ordinal)
    {
        ["4-Color Allies"] = "aggro", ["4-Color Control"] = "control",
        ["4-Color Dinosaurs"] = "combo", ["4-Color Reanimator"] = "control-combo",
        ["5-Color Control"] = "control", ["Abzan Lifegain"] = "aggro-combo",
        ["Azorius Control"] = "control", ["Azorius Flash"] = "aggro-control",
        ["Boros Dragons"] = "aggro-control", ["Boros Dwarf"] = "aggro-combo",
        ["Boros Tokens"] = "aggro-combo", ["Dimir Control"] = "control",
        ["Dimir Jace"] = "control-combo", ["Dimir Midrange"] = "aggro-control",
        ["Grixis Fling"] = "aggro-combo", ["Gruul Landfall"] = "aggro-combo",
        ["Izzet Spellementals"] = "aggro-control", ["Jeskai Artifacts"] = "control-combo",
        ["Jeskai Lessons"] = "control", ["Jeskai Superfriends"] = "control",
        ["Jund Sacrifice"] = "control-combo", ["Mardu Discard"] = "aggro-control",
        ["Mono-Black Demons"] = "aggro-control", ["Mono-Black Skeletons"] = "aggro-combo",
        ["Mono-Blue Elementals"] = "aggro-control", ["Mono-Blue Jace"] = "control-combo",
        ["Mono-Green Landfall"] = "aggro-combo", ["Mono-Red Burn"] = "aggro",
        ["Mono-White Angels"] = "aggro-combo", ["Mono-White Auras"] = "aggro-combo",
        ["Mono-White Auras — 21 Lands"] = "aggro-combo",
        ["Mono-White Lifegain"] = "aggro-combo", ["Naya World War Hulk"] = "combo",
        ["Orzhov Lifegain"] = "aggro-combo", ["Orzhov Sacrifice"] = "aggro-combo",
        ["Orzhov Skeletons"] = "aggro-combo", ["Rakdos Aggro"] = "aggro",
        ["Rakdos Fling"] = "aggro-combo", ["Reanimator"] = "control-combo",
        ["Selesnya Landfall"] = "aggro-combo", ["Selesnya Ouroboroid"] = "aggro-control",
        ["Selesnya Rabbits"] = "aggro-combo", ["Sultai Reanimator"] = "control-combo",
        ["Sultai Reanimator — Workshop"] = "control-combo", ["Temur Kona"] = "combo"
    };

    public static string? ForName(string name) => ByName.GetValueOrDefault(name);
    // One primary archetype subtype per named list. Keep this annotation outside the payload.
    private static readonly Dictionary<string, string> SubtypeByName = new(StringComparer.Ordinal)
    {
        ["4-Color Allies"] = "Allies", ["4-Color Control"] = "Spell Control",
        ["4-Color Dinosaurs"] = "Dinosaur Reanimator", ["4-Color Reanimator"] = "Reanimator",
        ["5-Color Control"] = "Spell Control", ["Abzan Lifegain"] = "Lifegain",
        ["Azorius Control"] = "Jace Control", ["Azorius Flash"] = "Flash",
        ["Boros Dragons"] = "Dragons", ["Boros Dwarf"] = "Dwarves",
        ["Boros Tokens"] = "Tokens", ["Dimir Control"] = "Planeswalkers",
        ["Dimir Jace"] = "Jace Exile", ["Dimir Midrange"] = "Midrange",
        ["Grixis Fling"] = "Fling", ["Gruul Landfall"] = "Landfall",
        ["Izzet Spellementals"] = "Spellementals", ["Jeskai Artifacts"] = "Artifacts",
        ["Jeskai Lessons"] = "Lessons", ["Jeskai Superfriends"] = "Superfriends",
        ["Jund Sacrifice"] = "Sacrifice", ["Mardu Discard"] = "Discard",
        ["Mono-Black Demons"] = "Demons", ["Mono-Black Skeletons"] = "Skeletons",
        ["Mono-Blue Elementals"] = "Elementals", ["Mono-Blue Jace"] = "Jace Exile",
        ["Mono-Green Landfall"] = "Landfall", ["Mono-Red Burn"] = "Burn",
        ["Mono-White Angels"] = "Angels", ["Mono-White Auras"] = "Auras",
        ["Mono-White Auras — 21 Lands"] = "Auras", ["Mono-White Lifegain"] = "Lifegain",
        ["Naya World War Hulk"] = "Big Creatures", ["Orzhov Lifegain"] = "Lifegain",
        ["Orzhov Sacrifice"] = "Sacrifice", ["Orzhov Skeletons"] = "Skeletons",
        ["Rakdos Aggro"] = "Creature Aggro", ["Rakdos Fling"] = "Fling",
        ["Reanimator"] = "Reanimator", ["Selesnya Landfall"] = "Landfall",
        ["Selesnya Ouroboroid"] = "Midrange", ["Selesnya Rabbits"] = "Rabbits",
        ["Sultai Reanimator"] = "Reanimator", ["Sultai Reanimator — Workshop"] = "Reanimator",
        ["Temur Kona"] = "Omniscience"
    };

    public static string? SubtypeForName(string name) => SubtypeByName.GetValueOrDefault(name);
    public static string Label(string? strategy) => strategy switch
    {
        "aggro" => "Aggro", "control" => "Control", "combo" => "Combo",
        "aggro-control" => "Aggro–Control", "aggro-combo" => "Aggro–Combo",
        "control-combo" => "Control–Combo", _ => "Unclassified"
    };
}
