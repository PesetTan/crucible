using DeckTester;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
var root=Path.Combine(Path.GetTempPath(),"card-attributes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var path=Path.Combine(root,"cards.sqlite");
try{
    using(var db=new SqliteConnection($"Data Source={path}")){
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TABLE cards(arena_id INTEGER PRIMARY KEY,name TEXT NOT NULL); INSERT INTO cards VALUES(1,'Generic Copier'); INSERT INTO cards VALUES(2,'Generic Payoff');";cmd.ExecuteNonQuery();
    }
    var attributes=new CardAttributes(path);
    Check(File.Exists(path+".before-attributes.sqlite"),"Schema migration preserves an online backup");
    var first=attributes.Append(1,new(JsonNode.Parse("""{"schemaVersion":1,"roles":["graveyard_copier"],"future":{"first":true}}""")!.AsObject()));
    var appended=attributes.Append(1,new(JsonNode.Parse("""{"roles":["graveyard_copier","value"],"future":{"second":[1,2]}}""")!.AsObject()));
    Check(appended.GetProperty("roles").GetArrayLength()==2,"Arrays append distinct values");
    Check(appended.GetProperty("future").GetProperty("first").GetBoolean(),"Unknown nested attributes survive append");
    Check(appended.GetProperty("future").GetProperty("second").GetArrayLength()==2,"New arbitrary nested attributes are allowed");
    var corrected=CardAttributes.Merge(JsonNode.Parse("""{"roles":["old","keep"]}""")!.AsObject(),JsonNode.Parse("""{"roles":["keep"]}""")!.AsObject(),true);
    Check(corrected["roles"]!.AsArray().Count==1,"Explicit replacement can remove an incorrect role");
    try{attributes.Append(1,new(JsonNode.Parse("""{"future":{"first":false}}""")!.AsObject()));throw new Exception("Conflict was accepted");}catch(ArgumentException){}
    Check(attributes.Read(1).GetRawText()==appended.GetRawText(),"Rejected updates leave prior attributes unchanged");
    var card=new CardEntry(1,"Generic Copier",4,"MainDeck","","Creature","","",true,"");
    var deck=new DeckManifest("d","Test","custom","","","",null,"l","m",DateTimeOffset.UtcNow,[card],"");
    var snapshot=attributes.Snapshot([deck]);
    attributes.Append(1,new(JsonNode.Parse("""{"future":{"third":"added later"}}""")!.AsObject()));
    var later=attributes.Snapshot([deck]);
    Check(snapshot.Hash!=later.Hash,"Attributes changes alter the compatibility hash");
    Check(!snapshot.Cards["Generic Copier"].GetProperty("future").TryGetProperty("third",out _),"A session snapshot is immutable after catalog append");
    var config=new RunConfig(["A","B"],2,1,180,100,42,true,"engine","adapter"){AttributesHash=snapshot.Hash,CardAttributes=snapshot.Cards};
    Check(!Store.CompatibleSession(config,config with{AttributesHash=later.Hash}),"Runs cannot reuse games with different attributes");
    Check(Json.Decode<RunConfig>(Json.Encode(config)).CardAttributes!["Generic Copier"].GetRawText()==snapshot.Cards["Generic Copier"].GetRawText(),"Run JSON preserves arbitrary attributes");
    var reordered=CardAttributes.Parse("""{"b":2,"a":{"z":3,"c":1}}""");
    Check(reordered.GetRawText()==CardAttributes.Parse("""{"a":{"c":1,"z":3},"b":2}""").GetRawText(),"Object key order does not alter attributes identity");
    Console.WriteLine("Card attributes checks passed.");
}finally{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
