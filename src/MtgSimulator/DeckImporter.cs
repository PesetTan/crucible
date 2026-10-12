using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MtgSimulator;

public sealed class DeckImporter(AppPaths paths, Store store)
{
    private static readonly Dictionary<string,string> Expected = new()
    {
        ["Mono-White Auras"]="51a64417db83", ["4-Color Reanimator"]="7db2253158dc", ["Mono-White Lifegain"]="d79a6ecc8a28",
        ["Abzan Lifegain"]="0b27dc67ddd3", ["Jeskai Artifacts"]="85eb10944e9e",
        ["Orzhov Skeletons"]="b99bc553c31d", ["Azorius Control"]="4b4d0146aa2e", ["Orzhov Sacrifice"]="f7b0b02c30d3",
        ["Boros Dragons"]="5bfccf8bc7d5", ["Orzhov Lifegain"]="13e423adf8d4", ["Boros Dwarf"]="70e6f5fdb1be"
    };
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async Task<List<DeckManifest>> Import()
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Source, Mode = SqliteOpenMode.ReadOnly }.ToString()); db.Open();
        var imported = new List<DeckManifest>();
        foreach (var (name, fingerprint) in Expected)
        {
            using var select = db.CreateCommand(); select.CommandText = "SELECT deck_id,snapshot_id,source_url FROM untapped_decks WHERE name=$name AND snapshot_id='standard-bo1-bronze-platinum-2026-10-11' ORDER BY matches_approx DESC, deck_id LIMIT 1";
            select.Parameters.AddWithValue("$name",name); using var reader = select.ExecuteReader(); if (!reader.Read()) throw new InvalidDataException($"Frozen source list missing: {name}.");
            var sourceId = reader.GetString(0); var snapshot = reader.GetString(1); var url = reader.GetString(2); reader.Close();
            var cards = ReadCards(db,"SELECT dc.quantity,dc.zone,cc.arena_id,cc.name,cc.set_code,c.type_line,c.mana_cost,c.rules_text,c.standard_legal,c.linked_face_arena_ids FROM untapped_deck_cards dc JOIN card_catalog cc USING(arena_id) LEFT JOIN cards c USING(arena_id) WHERE dc.deck_id=$id AND dc.snapshot_id=$snapshot",sourceId,snapshot);
            var deck = Manifest(name,"public",sourceId,snapshot,url,cards,"Frozen public snapshot, October 11, 2026; Bronze–Platinum, September 29–October 11. Legality and pilot behavior not independently validated.");
            if (!deck.ListHash.StartsWith(fingerprint, StringComparison.Ordinal)) throw new InvalidDataException($"{name}: source composition differs from the plan. Import rejected.");
            imported.Add(deck);
        }
        using (var cmd=db.CreateCommand())
        {
            cmd.CommandText="SELECT name,snapshot_time FROM decks WHERE deck_id=$id"; cmd.Parameters.AddWithValue("$id","75c0271e-f980-47b3-bb01-a6b305f2430b");
            using var reader=cmd.ExecuteReader(); if(reader.Read()) {
                var name=reader.GetString(0); var snapshot=reader.GetString(1); reader.Close();
                var cards=ReadCards(db,"SELECT dc.quantity,dc.zone,cc.arena_id,cc.name,cc.set_code,c.type_line,c.mana_cost,c.rules_text,c.standard_legal,c.linked_face_arena_ids FROM deck_cards dc JOIN card_catalog cc USING(arena_id) LEFT JOIN cards c USING(arena_id) WHERE dc.deck_id=$id","75c0271e-f980-47b3-bb01-a6b305f2430b",null);
                imported.Add(Manifest(name,"personal","75c0271e-f980-47b3-bb01-a6b305f2430b",snapshot,null,cards,"Captured personal deck; source log changed after import. This is the saved snapshot, not a claim about your current deck. No ownership data is inferred."));
            }
        }
        foreach(var deck in imported) { if(!store.Decks().Any(d=>d.Id==deck.Id)) await store.SaveDeck(deck); }
        return imported;
    }
    private static List<CardEntry> ReadCards(SqliteConnection db,string sql,string id,string? snapshot)
    {
        using var cmd=db.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",id);if(snapshot is not null)cmd.Parameters.AddWithValue("$snapshot",snapshot);
        using var reader=cmd.ExecuteReader();var cards=new List<CardEntry>();
        while(reader.Read()) {
            if(reader.IsDBNull(5))throw new InvalidDataException($"Missing rules profile: {reader.GetString(3)}.");
            string S(int i)=>reader.IsDBNull(i)?"":reader.GetString(i);
            cards.Add(new(reader.GetInt32(2),S(3),reader.GetInt32(0),S(1),S(4),S(5),S(6),S(7),!reader.IsDBNull(8)&&reader.GetInt32(8)==1,S(9)));
        } return cards.OrderBy(c=>c.Zone,StringComparer.Ordinal).ThenBy(c=>c.Name,StringComparer.Ordinal).ToList();
    }
    private static DeckManifest Manifest(string name,string type,string sourceId,string snapshot,string? url,List<CardEntry> cards,string note)
    {
        if(cards.Any(c=>c.Quantity<=0)||cards.Where(c=>c.Zone=="MainDeck").Sum(c=>c.Quantity)!=60)throw new InvalidDataException($"Invalid main-deck count for {name}.");
        if(cards.Where(c=>c.Zone=="Sideboard").Sum(c=>c.Quantity)>15)throw new InvalidDataException("Sideboard exceeds 15 cards.");
        foreach(var group in cards.Where(c=>c.Zone is "MainDeck" or "Sideboard").GroupBy(c=>c.Name))
            if(group.Sum(c=>c.Quantity)>4&&!group.First().Type.Contains("Basic Land",StringComparison.Ordinal))throw new InvalidDataException($"Copy limit exceeded for {group.Key}.");
        var pairs=cards.Where(c=>c.Zone=="MainDeck").GroupBy(c=>c.Name).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new object[]{g.Key,g.Sum(c=>c.Quantity)}).ToList();
        var listHash=Hash(JsonSerializer.Serialize(pairs,new JsonSerializerOptions{Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        var manifestHash=Hash(Json.Encode(new{ name,type,sourceId,snapshot,url,cards }));
        return new(manifestHash,name,type,type=="personal"?"Saved snapshot":"Exact public list",sourceId,snapshot,url,listHash,manifestHash,DateTimeOffset.UtcNow,cards,note);
    }
}
