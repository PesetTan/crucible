namespace DeckTester;

public sealed record LibrarySnapshot(int Version,List<DeckSnapshot> Decks,Dictionary<int,int> ManaValues)
{
    public string ETag()=>"\""+DeckImporter.Hash(Json.Encode(this))+"\"";
}
