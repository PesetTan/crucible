using System.Text.Json;
using System.Text.Json.Serialization;
using DeckTester;

static class SerializationCompatibilityChecks
{
    public static void Run(){
        var legacy=new JsonSerializerOptions(JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
        void Equal<T>(T current,object original,string label){
            var before=JsonSerializer.Serialize(original,legacy);var after=Json.Encode(current);
            if(before!=after)throw new Exception($"{label} changed its identity bytes. Before: {before}; after: {after}");
        }
        var cards=new List<CardEntry>{new(1,"Teferi’s <Study>",4,"MainDeck","TST","Creature","{1}{U}","Draw a card.",true,""){Attributes=CardAttributes.Parse("{\"b\":2,\"a\":{\"enabled\":true}}")}};
        Equal(new DeckFingerprint("Deck","personal","source","frozen",null,cards),new{name="Deck",type="personal",sourceId="source",snapshot="frozen",url=(string?)null,cards},"Deck manifest fingerprint");
        Equal(new WorkshopFingerprint("Copy","custom","parent",cards),new{name="Copy",sourceType="custom",parentDeckId="parent",cards},"Workshop fingerprint");
        Equal(new SeedFingerprint("sha256-v1",42,"A","B",1,7),new{scheme="sha256-v1",rootSeed=42L,a="A",b="B",starter=1,index=7},"Derived game seed");
        var config=new RunConfig(["A","B"],2,1,180,150,42,true,"engine","adapter");var baseline=new WorkshopBaseline("run","A","B");
        Equal(new WorkshopRunFingerprint(config,baseline,"A"),new{config,WorkshopBaseline=baseline,WorkshopDeckId="A"},"Workshop run idempotency");
        var deck=cards.Select(c=>new ForgeDeckCard(c.Name,c.Quantity,c.Attributes)).ToList();
        Equal(new ForgeGameRequest("attempt",42,1,150,false,null,null,"pilot","one","two",deck,deck),new{
            attemptId="attempt",seed=42L,starter=1,turnLimit=150,performanceMode=false,
            optimizerPolicyA=(OptimizerPolicy?)null,optimizerPolicyB=(OptimizerPolicy?)null,pilotVersion="pilot",pilotA="one",pilotB="two",
            deckA=cards.Select(c=>new{c.Name,c.Quantity,c.Attributes}),deckB=cards.Select(c=>new{c.Name,c.Quantity,c.Attributes})},"Forge worker protocol");
        var pairs=new List<object[]>{new object[]{"Teferi’s <Study>",4}};
        var reference=JsonSerializer.Serialize(pairs,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        if(Json.EncodeRelaxed(pairs)!=reference)throw new Exception("List hash escaping changed.");
        Console.WriteLine("Serialization compatibility checks passed: exact manifest, Workshop, seed, idempotency, Forge request and list-hash bytes.");
    }
}
