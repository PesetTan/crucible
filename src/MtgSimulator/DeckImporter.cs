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
        ["Boros Dragons"]="5bfccf8bc7d5", ["Orzhov Lifegain"]="13e423adf8d4", ["Boros Dwarf"]="70e6f5fdb1be",
        ["Sultai Reanimator"]="6eaf31896999",
        ["Dimir Control"]="a3bb861adac0",
        ["Mardu Discard"]="f954ca85fd91",
        ["Selesnya Landfall"]="a542415a04a0",
        ["Selesnya Rabbits"]="e76bc13178f7",
        ["Temur Kona"]="06e22807cc03",
        ["Azorius Flash"]="4f255f1b5654",
        ["Mono-Black Skeletons"]="d30fa3f2b2b4",
        ["Grixis Fling"]="e9d278aefc12",
        ["Mono-White Angels"]="facd862880e3",
        ["Rakdos Fling"]="6d9428e05a44",
        ["4-Color Dinosaurs"]="45a35606bcaa",
        ["Mono-Red Burn"]="9d2c6de9735a",
        ["Dimir Midrange"]="cf55c5ebc24d",
        ["Boros Tokens"]="56fad517ee7a",
        ["Rakdos Aggro"]="ab5ac5345b8e",
        ["Mono-Green Landfall"]="889c4d91fbf7",
        ["4-Color Allies"]="a407f86dcd18",
        ["Gruul Landfall"]="12daa24147ec",
        ["Selesnya Ouroboroid"]="f357a74be842",
        ["4-Color Control"]="5bf798edba50",
        ["Izzet Spellementals"]="912d5fd2dc10"
    };
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public Dictionary<int, int> ReadManaValues()
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Source, Mode = SqliteOpenMode.ReadOnly }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT arena_id, mana_value FROM cards WHERE mana_value IS NOT NULL";
        using var reader = cmd.ExecuteReader();
        var values = new Dictionary<int, int>();
        while (reader.Read()) values.Add(reader.GetInt32(0), reader.GetInt32(1));
        return values;
    }
    public async Task<List<DeckManifest>> Import()
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Source, Mode = SqliteOpenMode.ReadOnly }.ToString()); db.Open();
        var imported = new List<DeckManifest>();
        foreach (var (name, fingerprint) in Expected)
        {
            using var select = db.CreateCommand(); select.CommandText = "SELECT deck_id,snapshot_id,source_url,archetype_tier,tier_source_url,tier_snapshot_date,win_rate_percent,matches_approx,matches_displayed FROM untapped_decks WHERE name=$name AND snapshot_id IN ('standard-bo1-bronze-platinum-2026-10-11','standard-bo1-tier-representatives-2026-10-11') ORDER BY matches_approx DESC, deck_id LIMIT 1";
            select.Parameters.AddWithValue("$name",name); using var reader = select.ExecuteReader(); if (!reader.Read()) throw new InvalidDataException($"Frozen source list missing: {name}.");
            var sourceId = reader.GetString(0); var snapshot = reader.GetString(1); var url = reader.GetString(2); var tier = reader.IsDBNull(3) ? null : reader.GetString(3); var tierUrl = reader.IsDBNull(4) ? null : reader.GetString(4); var tierDate = reader.IsDBNull(5) ? null : reader.GetString(5); var rate=reader.GetDouble(6); var matches=reader.GetInt32(7); var matchesLabel=reader.GetString(8); reader.Close();
            var cards = ReadCards(db,"SELECT dc.quantity,dc.zone,cc.arena_id,cc.name,cc.set_code,c.type_line,c.mana_cost,c.rules_text,c.standard_legal,c.linked_face_arena_ids FROM untapped_deck_cards dc JOIN card_catalog cc USING(arena_id) LEFT JOIN cards c USING(arena_id) WHERE dc.deck_id=$id AND dc.snapshot_id=$snapshot",sourceId,snapshot);
            var tierNote = snapshot == "standard-bo1-tier-representatives-2026-10-11" ? " Popular archetype-page representative with fewer than 200 matches (100+ filter)." : "";
            var deck = Manifest(name,"public",sourceId,snapshot,url,cards,"Frozen public snapshot, October 11, 2026; Bronze–Platinum, September 29–October 11. Legality and pilot behavior not independently validated." + tierNote);
            if (!deck.ListHash.StartsWith(fingerprint, StringComparison.Ordinal)) throw new InvalidDataException($"{name}: source composition differs from the plan. Import rejected.");
            imported.Add(deck with { ArchetypeTier = tier, TierSourceUrl = tierUrl, TierSnapshotDate = tierDate, PublishedWinRatePercent=rate, PublishedMatchesApprox=matches, PublishedMatchesDisplayed=matchesLabel });
        }
        // Include every exact A/C list, including lower-volume variants of an archetype.
        var extraSources = new List<(string Id,string Snapshot,string Name,string Url,string Tier,string? TierUrl,string? TierDate,double Rate,int Matches,string MatchesLabel)>();
        using (var select=db.CreateCommand()) {
            select.CommandText="SELECT deck_id,snapshot_id,name,source_url,archetype_tier,tier_source_url,tier_snapshot_date,win_rate_percent,matches_approx,matches_displayed FROM untapped_decks WHERE archetype_tier IN ('A','C') ORDER BY archetype_tier,name,matches_approx DESC,deck_id";
            using var reader=select.ExecuteReader();
            while(reader.Read())extraSources.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetDouble(7),reader.GetInt32(8),reader.GetString(9)));
        }
        foreach(var source in extraSources) {
            if(imported.Any(d=>d.SourceDeckId==source.Id&&d.Snapshot==source.Snapshot))continue;
            var cards=ReadCards(db,"SELECT dc.quantity,dc.zone,cc.arena_id,cc.name,cc.set_code,c.type_line,c.mana_cost,c.rules_text,c.standard_legal,c.linked_face_arena_ids FROM untapped_deck_cards dc JOIN card_catalog cc USING(arena_id) LEFT JOIN cards c USING(arena_id) WHERE dc.deck_id=$id AND dc.snapshot_id=$snapshot",source.Id,source.Snapshot);
            var deck=Manifest(source.Name,"public",source.Id,source.Snapshot,source.Url,cards,"Frozen public snapshot, October 11, 2026; Standard Bo1, Bronze–Platinum, September 29–October 11. Legality and pilot behavior not independently validated.");
            imported.Add(deck with {ArchetypeTier=source.Tier,TierSourceUrl=source.TierUrl,TierSnapshotDate=source.TierDate,PublishedWinRatePercent=source.Rate,PublishedMatchesApprox=source.Matches,PublishedMatchesDisplayed=source.MatchesLabel});
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
        foreach(var deck in imported) await store.SaveDeck(deck);
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
