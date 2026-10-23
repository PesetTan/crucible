using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DeckTester;

// Coverage is versioned evidence, independent of card rules, ownership and execution selection.
public sealed record EngineSupportCard(string Name,string Kind,int? ArenaId);
public sealed record EngineSupportMatchup(string Id,string Label,string DeckA,string DeckB,string EligibilityAsOf);
public sealed record EngineSupportPack(int Version,string ProviderId,string EngineVersion,string SourceSha256,
    string RuntimeSha256,string EvidenceSha256,string Target,List<EngineSupportCard> Cards,
    List<EngineSupportMatchup> Matchups,List<DeckManifest> Decks)
{
    public string Id=>ProviderId+":"+EngineVersion+":"+SourceSha256;
    public static void Validate(EngineSupportPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
        Require(pack.Version==1&&Regex.IsMatch(pack.ProviderId??"","^[a-z][a-z0-9-]{0,63}$")
            &&!string.IsNullOrWhiteSpace(pack.EngineVersion)&&!string.IsNullOrWhiteSpace(pack.Target),"Invalid engine support identity.");
        foreach(var hash in new[]{pack.SourceSha256,pack.RuntimeSha256,pack.EvidenceSha256})
            Require(Regex.IsMatch(hash??"","^[a-f0-9]{64}$"),"Support requires pinned source, runtime and evidence hashes.");
        Require(pack.Cards is {Count:>0}&&pack.Decks is {Count:>0}&&pack.Matchups is {Count:>0},"Empty engine support pack.");
        Require(pack.Cards!.All(c=>c is not null&&!string.IsNullOrWhiteSpace(c.Name)&&c.Kind is "Card" or "Token"&&(c.Kind=="Token"?c.ArenaId is null:c.ArenaId is >0))
            &&pack.Cards.Select(c=>c.Name).Distinct(StringComparer.Ordinal).Count()==pack.Cards.Count,"Invalid or duplicate supported card.");
        var names=pack.Cards.Where(c=>c.Kind=="Card").Select(c=>c.Name).ToHashSet(StringComparer.Ordinal);
        var decks=new Dictionary<string,DeckManifest>(StringComparer.Ordinal);
        foreach(var deck in pack.Decks!){
            DeckImporter.Validate(deck);
            Require(deck.SideCount==0&&deck.Cards.All(c=>names.Contains(c.Name)),"Supported lists must contain supported main-deck cards only.");
            Require(decks.TryAdd(deck.Id,deck),"Duplicate supported deck identity.");
        }
        var ids=new HashSet<string>(StringComparer.Ordinal);var referenced=new HashSet<string>(StringComparer.Ordinal);
        foreach(var pair in pack.Matchups!){
            if(pair is null)throw new InvalidDataException("A supported matchup cannot be null.");
            Require(!string.IsNullOrWhiteSpace(pair.Id)&&ids.Add(pair.Id)&&!string.IsNullOrWhiteSpace(pair.Label)
                &&pair.DeckA!=pair.DeckB&&decks.ContainsKey(pair.DeckA)&&decks.ContainsKey(pair.DeckB)
                &&DateOnly.TryParseExact(pair.EligibilityAsOf,"yyyy-MM-dd",out _),"Invalid supported matchup reference or dated eligibility.");
            referenced.Add(pair.DeckA);referenced.Add(pair.DeckB);
        }
        Require(referenced.SetEquals(decks.Keys),"Each supported deck must belong to a validated matchup.");
    }
}
public sealed record EngineSupportReceipt(string CatalogId,int AddedDecks,int Cards,int Matchups,bool Duplicate);

public sealed partial class Store
{
    public List<EngineSupportPack> EngineSupport()=>Read<EngineSupportPack>("engine_support_catalogs");
    public async Task<EngineSupportReceipt> ImportEngineSupport(EngineSupportPack pack)
    {
        EngineSupportPack.Validate(pack); // Validate the whole batch before opening a write transaction.
        var payload=Json.Encode(pack);
        await writer.WaitAsync();try{
            using var db=Open();using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="SELECT payload FROM engine_support_catalogs WHERE id=$id";cmd.Parameters.AddWithValue("$id",pack.Id);
            if(cmd.ExecuteScalar() is string old){
                if(old!=payload)throw new InvalidDataException("An immutable engine support identity already has different content.");
                return new(pack.Id,0,pack.Cards.Count,pack.Matchups.Count,true);
            }
            var added=0;
            cmd.Parameters.Clear();cmd.CommandText="SELECT payload FROM decks ORDER BY rowid";
            var library=new List<DeckManifest>();
            using(var reader=cmd.ExecuteReader())while(reader.Read())library.Add(Json.Decode<DeckManifest>(reader.GetString(0)));
            static string MainList(DeckManifest deck)=>Json.EncodeRelaxed(deck.Cards.Where(c=>c.Zone=="MainDeck")
                .GroupBy(c=>c.Name).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new object[]{g.Key,g.Sum(c=>c.Quantity)}).ToList());
            foreach(var deck in pack.Decks){
                // Support belongs to the exact list, regardless of its source/version ID.
                // Keep acceptance provenance in the catalog; reuse existing library rows.
                if(library.Any(prior=>prior.SideCount==0&&prior.ListHash==deck.ListHash&&MainList(prior)==MainList(deck)))continue;
                cmd.Parameters.Clear();cmd.CommandText="SELECT payload FROM decks WHERE id=$id";cmd.Parameters.AddWithValue("$id",deck.Id);
                if(cmd.ExecuteScalar() is string existing){
                    var prior=Json.Decode<DeckManifest>(existing);DeckImporter.Validate(prior);
                    if(prior.ManifestHash!=deck.ManifestHash||prior.ListHash!=deck.ListHash)throw new InvalidDataException("Existing deck identity conflicts with support pack.");
                    continue;
                }
                cmd.CommandText="INSERT INTO decks(id,payload,friendly_name,strategy,subtype) VALUES($id,$payload,$name,$strategy,$subtype)";
                cmd.Parameters.AddWithValue("$payload",Json.Encode(deck));cmd.Parameters.AddWithValue("$name",NewFriendlyName(db,tx));
                cmd.Parameters.AddWithValue("$strategy",(object?)DeckStrategy.ForName(deck.Name)??DBNull.Value);
                cmd.Parameters.AddWithValue("$subtype",(object?)DeckStrategy.SubtypeForName(deck.Name)??DBNull.Value);added+=cmd.ExecuteNonQuery();
                library.Add(deck);
            }
            cmd.Parameters.Clear();cmd.CommandText="INSERT INTO engine_support_catalogs(id,payload) VALUES($id,$payload)";
            cmd.Parameters.AddWithValue("$id",pack.Id);cmd.Parameters.AddWithValue("$payload",payload);cmd.ExecuteNonQuery();
            foreach(var card in pack.Cards){
                cmd.Parameters.Clear();cmd.CommandText="INSERT INTO engine_support_cards(catalog_id,name,kind,arena_id) VALUES($catalog,$name,$kind,$arena)";
                cmd.Parameters.AddWithValue("$catalog",pack.Id);cmd.Parameters.AddWithValue("$name",card.Name);
                cmd.Parameters.AddWithValue("$kind",card.Kind);cmd.Parameters.AddWithValue("$arena",(object?)card.ArenaId??DBNull.Value);cmd.ExecuteNonQuery();
            }
            tx.Commit();return new(pack.Id,added,pack.Cards.Count,pack.Matchups.Count,false);
        }finally{writer.Release();}
    }
}
