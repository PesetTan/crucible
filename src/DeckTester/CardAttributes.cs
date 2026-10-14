using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace DeckTester;

public sealed record AttributesAppend(JsonObject Attributes, bool ReplaceExisting = false);
public sealed record AttributesSnapshot(Dictionary<string,JsonElement> Cards,string Hash);

public sealed class CardAttributes
{
    private readonly string path;
    public CardAttributes(AppPaths paths):this(paths.Source) { }
    public CardAttributes(string path)
    {
        this.path=path;
        using var db=Open(false);
        using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA table_info(cards)";
        var found=false;using(var reader=cmd.ExecuteReader())while(reader.Read())found|=reader.GetString(1)=="attributes";
        if(!found){
            using var backup=new SqliteConnection($"Data Source={path}.before-attributes.sqlite");backup.Open();db.BackupDatabase(backup);
            cmd.CommandText="ALTER TABLE cards ADD COLUMN attributes TEXT NOT NULL DEFAULT '{}' CHECK(json_valid(attributes) AND json_type(attributes)='object')";cmd.ExecuteNonQuery();
        }
        cmd.CommandText="CREATE TABLE IF NOT EXISTS card_attributes_history(revision INTEGER PRIMARY KEY AUTOINCREMENT,arena_id INTEGER NOT NULL,changed_at TEXT NOT NULL,before_json TEXT NOT NULL,after_json TEXT NOT NULL)";cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open(bool readOnly)
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite,DefaultTimeout=30}.ToString());db.Open();return db;
    }
    public static JsonObject Merge(JsonObject existing,JsonObject additions,bool replace=false)
    {
        var result=(JsonObject)existing.DeepClone();
        foreach(var (key,value) in additions){
            if(!result.ContainsKey(key)){result[key]=value?.DeepClone();continue;}
            if(result[key] is JsonObject obj && value is JsonObject more){result[key]=Merge(obj,more,replace);continue;}
            if(result[key] is JsonArray array && value is JsonArray values){
                if(replace){result[key]=values.DeepClone();continue;}
                foreach(var item in values)if(!array.Any(old=>JsonNode.DeepEquals(old,item)))array.Add(item?.DeepClone());continue;
            }
            if(JsonNode.DeepEquals(result[key],value))continue;
            if(!replace)throw new ArgumentException($"Attribute '{key}' already has a different value. Set replaceExisting explicitly to replace it.");
            result[key]=value?.DeepClone();
        }
        return result;
    }
    public static JsonElement Parse(string? text)
    {
        var node=JsonNode.Parse(string.IsNullOrEmpty(text)?"{}":text);
        if(node is not JsonObject)throw new InvalidDataException("Card attributes must be a JSON object.");
        return JsonSerializer.SerializeToElement(Canonical(node),Json.Options);
    }
    private static JsonNode? Canonical(JsonNode? node)=>node switch {
        JsonObject obj=>new JsonObject(obj.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,Canonical(p.Value)))),
        JsonArray array=>new JsonArray(array.Select(Canonical).ToArray()),
        _=>node?.DeepClone()
    };
    public JsonElement Read(int arenaId)
    {
        using var db=Open(true);using var cmd=db.CreateCommand();cmd.CommandText="SELECT attributes FROM cards WHERE arena_id=$id";cmd.Parameters.AddWithValue("$id",arenaId);
        return Parse(cmd.ExecuteScalar() as string??throw new KeyNotFoundException("Card not found."));
    }
    public JsonElement Append(int arenaId,AttributesAppend request)
    {
        if(request.Attributes is null)throw new ArgumentException("Provide an attributes object.");
        using var db=Open(false);using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT attributes FROM cards WHERE arena_id=$id";cmd.Parameters.AddWithValue("$id",arenaId);
        var before=cmd.ExecuteScalar() as string??throw new KeyNotFoundException("Card not found.");
        var merged=Parse(Merge((JsonObject)JsonNode.Parse(before)!,request.Attributes,request.ReplaceExisting).ToJsonString()).GetRawText();
        if(Parse(before).GetRawText()==merged)return Parse(merged);
        cmd.CommandText="UPDATE cards SET attributes=$after WHERE arena_id=$id";cmd.Parameters.AddWithValue("$after",merged);cmd.ExecuteNonQuery();
        cmd.CommandText="INSERT INTO card_attributes_history(arena_id,changed_at,before_json,after_json) VALUES($id,$at,$before,$after)";
        cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$before",before);cmd.ExecuteNonQuery();tx.Commit();
        return Parse(merged);
    }
    public AttributesSnapshot Snapshot(IEnumerable<DeckManifest> decks)
    {
        var cards=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        using var db=Open(true);using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT attributes FROM cards WHERE arena_id=$id";var id=cmd.Parameters.Add("$id",SqliteType.Integer);
        foreach(var card in decks.SelectMany(d=>d.Cards).OrderBy(c=>c.Name,StringComparer.Ordinal).ThenBy(c=>c.ArenaId)){
            id.Value=card.ArenaId;var value=cmd.ExecuteScalar() as string;
            var attributes=value is null?card.Attributes??Parse("{}"):Parse(value);
            if(cards.TryGetValue(card.Name,out var old)&&old.GetRawText()!=attributes.GetRawText())throw new InvalidDataException($"Conflicting attributes for printings of {card.Name}.");
            cards[card.Name]=attributes;
        }
        return new(cards,CatalogHash(db,tx));
    }
    public string CatalogHash(){using var db=Open(true);return CatalogHash(db,null);}
    private static string CatalogHash(SqliteConnection db,SqliteTransaction? transaction)
    {
        using var cmd=db.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="SELECT arena_id,attributes FROM cards WHERE attributes<>'{}' ORDER BY arena_id";
        using var reader=cmd.ExecuteReader();var rows=new List<object>();while(reader.Read())rows.Add(new object[]{reader.GetInt32(0),Parse(reader.GetString(1))});
        return DeckImporter.Hash(Json.Encode(rows));
    }
}
