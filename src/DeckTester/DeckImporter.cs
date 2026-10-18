using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace DeckTester;

public sealed record DeckPack(int Version,List<DeckManifest> Decks);

// Source adapters and their data live outside the application. Packs retain exact manifests.
public sealed class DeckImporter(AppPaths paths, Store store)
{
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
        var packPath=paths.DeckPack;
        if(packPath is null)return [];
        var pack=Json.Decode<DeckPack>(await File.ReadAllTextAsync(packPath));
        if(pack.Version!=1||pack.Decks is null)throw new InvalidDataException("Unsupported deck pack. Expected version 1 and a decks array.");
        // Validate the entire input before writing; a broken later list cannot import a partial batch.
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var deck in pack.Decks){
            Validate(deck);
            if(!ids.Add(deck.Id))throw new InvalidDataException("Duplicate deck identity in pack.");
        }
        var existing=store.Decks();
        var imported=new List<DeckManifest>();
        foreach(var deck in pack.Decks){
            // Attributes have independent per-run snapshots. Keep older immutable library versions.
            var same=existing.FirstOrDefault(old=>old.Id==deck.Id||
                old.SourceType==deck.SourceType&&old.SourceDeckId==deck.SourceDeckId&&old.Snapshot==deck.Snapshot&&old.SourceUrl==deck.SourceUrl
                &&old.ListHash==deck.ListHash&&WithoutAttributes(old.Cards)==WithoutAttributes(deck.Cards));
            if(same is not null){imported.Add(same);continue;}
            await store.SaveDeck(deck);existing.Add(deck);imported.Add(deck);
        }
        return imported;
    }
    private static string WithoutAttributes(List<CardEntry> cards)
    {
        var nodes=JsonNode.Parse(Json.Encode(cards))!.AsArray();
        foreach(var node in nodes)node!.AsObject().Remove("attributes");return nodes.ToJsonString();
    }
    public static void Validate(DeckManifest deck)
    {
        if(deck is null||string.IsNullOrWhiteSpace(deck.Name)||string.IsNullOrWhiteSpace(deck.SourceDeckId)||string.IsNullOrWhiteSpace(deck.Snapshot)
            ||deck.SourceType is not ("public" or "personal")||deck.ImportedAt==default||deck.Cards is null||deck.Cards.Count==0)
            throw new InvalidDataException("A pack requires complete public or personal source manifests.");
        var cards=deck.Cards;
        if(cards.Any(c=>c is null||c.Quantity<=0||c.ArenaId<=0||string.IsNullOrWhiteSpace(c.Name)||string.IsNullOrWhiteSpace(c.Type)||c.Zone is not ("MainDeck" or "Sideboard"))
            ||cards.Where(c=>c.Zone=="MainDeck").Sum(c=>(long)c.Quantity)!=60||cards.Where(c=>c.Zone=="Sideboard").Sum(c=>(long)c.Quantity)>15)
            throw new InvalidDataException($"Invalid card identity, zone, or deck count for {deck.Name}.");
        foreach(var group in cards.GroupBy(c=>c.Name))
            if(group.Sum(c=>(long)c.Quantity)>4&&!group.First().Type.Contains("Basic Land",StringComparison.Ordinal))
                throw new InvalidDataException($"Copy limit exceeded for {group.Key}.");
        var pairs=cards.Where(c=>c.Zone=="MainDeck").GroupBy(c=>c.Name).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new object[]{g.Key,g.Sum(c=>c.Quantity)}).ToList();
        var listHash=Hash(Json.EncodeRelaxed(pairs));
        var manifestHash=Hash(Json.Encode(new DeckFingerprint(deck.Name,deck.SourceType,deck.SourceDeckId,deck.Snapshot,deck.SourceUrl,cards)));
        // Historical manifests predate the optional Attributes property. Preserve their
        // original hashes only when no attribute payload is present; never drop supplied data.
        if(deck.ManifestHash!=manifestHash&&cards.All(c=>c.Attributes is null)){
            var legacy=JsonNode.Parse(Json.Encode(new DeckFingerprint(deck.Name,deck.SourceType,deck.SourceDeckId,deck.Snapshot,deck.SourceUrl,cards)))!;
            foreach(var card in legacy["cards"]!.AsArray())card!.AsObject().Remove("attributes");
            manifestHash=Hash(legacy.ToJsonString());
        }
        if(deck.ListHash!=listHash||deck.ManifestHash!=manifestHash||deck.Id!=manifestHash)
            throw new InvalidDataException($"Deck pack fingerprint mismatch for {deck.Name}.");
    }
}
