using DeckTester;
using Microsoft.Data.Sqlite;

static CardEntry Card(string type,string text,string cost="")=>new(1,"Test",1,"MainDeck","TST",type,cost,text,true,"");
static void Check(CardEntry card,string? expected,string message){
    var actual=LandMana.Read(card)?.Symbols;
    if(actual!=expected)throw new Exception($"{message}: expected {expected??"null"}, got {actual??"null"}");
}
Check(Card("Basic Land — Mountain",""),"{R}","Basic lands have implicit mana abilities");
Check(Card("Land — Plains Swamp",""),"{W} / {B}","Basic land subtypes supply both colors");
Check(Card("Land","As this land enters, choose a basic land type. This land is the chosen type."),"{W} / {U} / {B} / {R} / {G}","Chosen basic land type supplies its implicit color");
Check(Card("Land","{oT}: Add {oCoC}."),"{C}","Multiple colorless mana uses one type symbol");
Check(Card("Land","{2}{G}, {T}: Add {R} or {U}."),"{U} / {R}","Activation costs are not production");
Check(Card("Land","{oT}: Add {oG}.\n{oT}: Add {oB}. Activate only if you control a Swamp or a Forest."),"{B} / {G}","Conditional colors remain possible");
Check(Card("Land","{oT}: Add {oC}.\n{oT}: Add one mana of any color. Spend this mana only to cast an instant or sorcery spell."),"{W} / {U} / {B} / {R} / {G} / {C}","Restricted rainbow land includes colorless");
Check(Card("Land","As this land enters, choose a color.\n{oT}: Add one mana of the chosen color."),"{W} / {U} / {B} / {R} / {G}","Chosen color is possible in the deck list");
Check(Card("Land","{oT}: Add {oC}.\n{oT}: Add one mana of any of the exiled cards' colors."),"{W} / {U} / {B} / {R} / {G} / {C}","Variable colors are possible");
Check(Card("Land","{oT}: Add {oC}.\nOther Caves you control have \"{oT}, Pay 1 life: Add one mana of any color.\""),"{C}","Granted mana abilities do not belong to this land");
Check(Card("Land","{oT}, Sacrifice this land: Search your library for a basic land card, put it onto the battlefield tapped, then shuffle."),"","Fetch lands do not directly produce mana");
Check(Card("Land","{oT}: Add {oC}.\n{o2}, {oT}: Create a Treasure token. (It's an artifact with \"{oT}, Sacrifice this artifact: Add one mana of any color.\")"),"{C}","Treasure reminder text is not land production");
Check(Card("Creature // Land","{T}: Add {G}.","{2}{G}"),null,"Spell face retains its mana cost");
var restricted=LandMana.Read(Card("Land","{oT}: Add {oR}. Spend this mana only to cast an instant spell."))!;
if(!restricted.Description.Contains("Spend this mana only")||!restricted.Description.Contains("{T}: Add {R}"))throw new Exception("Tooltip retains restrictions and readable mana notation.");

// Audit the actual local land catalog read-only to catch unhandled production wording.
var source=Path.GetFullPath("../../standard_cards.sqlite",Directory.GetCurrentDirectory());
if(File.Exists(source)){
    using var db=new SqliteConnection($"Data Source={source};Mode=ReadOnly");db.Open();
    using var cmd=db.CreateCommand();cmd.CommandText="SELECT name,type_line,rules_text FROM cards WHERE is_primary_card=1 AND type_line LIKE '%Land%'";
    using var reader=cmd.ExecuteReader();var count=0;
    while(reader.Read()){
        var card=Card(reader.GetString(1),reader.IsDBNull(2)?"":reader.GetString(2)) with{Name=reader.GetString(0)};
        if(LandMana.Read(card) is null)throw new Exception($"Unclassified catalog land: {card.Name}");
        count++;
    }
    Console.WriteLine($"Audited {count} catalog land printings.");
}
Console.WriteLine("Land mana checks passed.");
