using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DeckTester;

public sealed record WorkshopCard(int ArenaId,int Quantity);
public sealed record CustomDeckRequest(string Name,List<WorkshopCard> Cards,string? ParentDeckId=null,List<WorkshopCard>? Sideboard=null);
public sealed record DeckResolution(List<CardEntry> Cards,List<string> Issues);
public sealed record DeckValidation(List<CardEntry> Cards,List<string> Errors,List<string> Blockers)
{
    public bool CanSave=>Errors.Count==0;
    public List<CardEntry> Sideboard { get; init; }=[];
}
public sealed record ArenaListRequest(string Text);
public sealed record WorkshopReplay(string GameId,string Outcome,int ActualTurns);
public sealed record WorkshopSnapshot(int Version,List<DeckManifest> Decks,List<SimulationRun> Runs,
    List<ActiveSessionProgress> ActiveSessions,SessionTurnStats? TurnStats,Dictionary<int,int> ManaValues,ResourceSnapshot Resources);
public sealed record WorkshopMatchup(SimulationRun Run,DeckManifest Deck,DeckManifest Opponent,MatchupSummary Summary,
    int Wins,int Losses,int Draws,int OnPlay,int WinsOnPlay,int OnDraw,int WinsOnDraw,double? WinRate,double[]? Interval,
    double? MedianRounds,double? MedianWinRounds,double? MedianLossRounds,DeckMulliganSummary? Mulligans,
    int ManaAnalyzed,int Starved,int Flooded,List<WorkshopReplay> Replays)
{
    public List<int> WinTurns { get; init; }=[];
    public List<int> LossTurns { get; init; }=[];
    public List<int> DrawTurns { get; init; }=[];
}

// The simulation service owns catalog access. Browser metadata is never trusted on save.
public sealed class DeckWorkshop(AppPaths paths,Store store,ForgeEngine engine)
{
    private List<(CardEntry Card,int Title,string Collector,int ManaValue)> Catalog(IEnumerable<int>? arenaIds=null)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=paths.Source,Mode=SqliteOpenMode.ReadOnly}.ToString());db.Open();
        using var cmd=db.CreateCommand();cmd.CommandText="SELECT arena_id,title_id,name,set_code,collector_number,type_line,mana_cost,rules_text,standard_legal,linked_face_arena_ids,mana_value,attributes FROM cards WHERE is_primary_card=1";
        if(arenaIds is not null){
            cmd.CommandText+=" AND arena_id IN (SELECT value FROM json_each($ids))";
            cmd.Parameters.AddWithValue("$ids",Json.Encode(arenaIds.Distinct().ToList()));
        }
        using var reader=cmd.ExecuteReader();var cards=new List<(CardEntry,int,string,int)>();
        while(reader.Read()){
            string S(int i)=>reader.IsDBNull(i)?"":reader.GetString(i);
            cards.Add((new(reader.GetInt32(0),S(2),1,"MainDeck",S(3),S(5),S(6),S(7),reader.GetInt32(8)==1,S(9)){Attributes=CardAttributes.Parse(S(11))},reader.GetInt32(1),S(4),reader.IsDBNull(10)?int.MaxValue:reader.GetInt32(10)));
        }
        return cards;
    }
    public List<CardEntry> Search(string query)
    {
        if(string.IsNullOrWhiteSpace(query)||query.Trim().Length<2)return [];
        if(query.Length>120)throw new ArgumentException("Search is limited to 120 characters.");
        return Catalog().Where(c=>c.Card.Name.Contains(query.Trim(),StringComparison.OrdinalIgnoreCase))
            .GroupBy(c=>c.Title).Select(g=>g.OrderByDescending(c=>c.Card.LocallyLegal).ThenBy(c=>c.Card.ArenaId).First())
            .OrderBy(c=>c.ManaValue).ThenBy(c=>c.Card.Name,StringComparer.OrdinalIgnoreCase).Take(30).Select(c=>c.Card).ToList();
    }
    public DeckResolution Resolve(string text)
    {
        if(text is null)throw new ArgumentException("Provide Arena deck text.");
        if(text.Length>30000)throw new ArgumentException("Deck list is limited to 30,000 characters.");
        var catalog=Catalog();var result=new List<CardEntry>();var issues=new List<string>();var sideboard=false;var lineNumber=0;
        foreach(var raw in text.Replace("\r","").Split('\n')){
            lineNumber++;var line=raw.Trim();if(line.Length==0)continue;
            if(line.Equals("Deck",StringComparison.OrdinalIgnoreCase)){sideboard=false;continue;}
            if(line.Equals("Sideboard",StringComparison.OrdinalIgnoreCase)){sideboard=true;continue;}
            if(line.Equals("Commander",StringComparison.OrdinalIgnoreCase)||line.Equals("Companion",StringComparison.OrdinalIgnoreCase)){
                issues.Add($"Line {lineNumber}: {line} is outside the main-deck editor.");continue;
            }
            var match=Regex.Match(line,@"^(\d+)\s+(.+?)(?:\s+\(([A-Za-z0-9]+)\)\s+(\S+))?$");
            if(!match.Success||!int.TryParse(match.Groups[1].Value,out var quantity)||quantity is <1 or >1000){issues.Add($"Line {lineNumber}: expected a positive quantity followed by a card name.");continue;}
            var name=match.Groups[2].Value;
            var candidates=catalog.Where(c=>c.Card.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToList();
            if(candidates.Count==0)candidates=catalog.Where(c=>c.Card.Name.Split(" // ")[0].Equals(name,StringComparison.OrdinalIgnoreCase)).ToList();
            if(match.Groups[3].Success)candidates=candidates.Where(c=>c.Card.Set.Equals(match.Groups[3].Value,StringComparison.OrdinalIgnoreCase)&&c.Collector.Equals(match.Groups[4].Value,StringComparison.OrdinalIgnoreCase)).ToList();
            if(candidates.Count==0){issues.Add($"Line {lineNumber}: could not resolve {name}{(match.Groups[3].Success?" with that printing":"")} in the local catalog.");continue;}
            if(candidates.Select(c=>c.Title).Distinct().Count()!=1){issues.Add($"Line {lineNumber}: {name} is ambiguous; specify its Arena set and collector number.");continue;}
            var card=candidates.OrderByDescending(c=>c.Card.LocallyLegal).ThenBy(c=>c.Card.ArenaId).First().Card;
            result.Add(card with{Quantity=quantity,Zone=sideboard?"Sideboard":"MainDeck"});
        }
        return new(result.GroupBy(c=>(c.ArenaId,c.Zone)).Select(g=>g.First() with{Quantity=g.Sum(c=>c.Quantity)}).ToList(),issues);
    }
    public DeckValidation Validate(CustomDeckRequest request)
    {
        if(request.Cards is null||request.Name is null)throw new ArgumentException("Provide a deck name and card entries.");
        var errors=new List<string>();var blockers=new List<string>();
        var catalog=Catalog(request.Cards.Take(201).Concat(request.Sideboard??[]).Select(c=>c.ArenaId)).ToDictionary(c=>c.Card.ArenaId,c=>c.Card);
        var cards=new List<CardEntry>();
        if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Trim().Length>100)errors.Add("Choose a deck name of 1–100 characters.");
        if(request.Cards.Count>200)errors.Add("A deck may contain at most 200 distinct entries.");
        foreach(var entry in request.Cards.Take(201)){
            if(entry.Quantity is <1 or >1000){errors.Add($"Invalid quantity for card {entry.ArenaId}.");continue;}
            if(!catalog.TryGetValue(entry.ArenaId,out var card)){errors.Add($"Card {entry.ArenaId} has no primary rules profile in the local catalog.");continue;}
            cards.Add(card with{Quantity=entry.Quantity});
        }
        cards=cards.GroupBy(c=>c.ArenaId).Select(g=>g.First() with{Quantity=g.Sum(c=>c.Quantity)}).OrderBy(c=>c.Name,StringComparer.Ordinal).ThenBy(c=>c.ArenaId).ToList();
        var count=cards.Sum(c=>c.Quantity);if(count!=60)errors.Add($"Main deck has {count} cards; it needs exactly 60.");
        foreach(var group in cards.GroupBy(c=>c.Name)){
            var card=group.First();var limit=CopyLimit(card);if(group.Sum(c=>c.Quantity)>limit)errors.Add($"{card.Name}: exceeds the {limit}-copy limit.");
        }
        DeckManifest? parent=null;
        if(request.ParentDeckId is {} parentId){try{parent=store.Deck(parentId);}catch(KeyNotFoundException){errors.Add("The parent deck version is unavailable.");}}
        var sideboard=parent?.Cards.Where(c=>c.Zone=="Sideboard").ToList()??[];
        if(parent is null)foreach(var entry in request.Sideboard??[]){
            if(entry.Quantity is <1 or >15||!catalog.TryGetValue(entry.ArenaId,out var side)){errors.Add($"Invalid sideboard provenance for card {entry.ArenaId}.");continue;}
            sideboard.Add(side with{Quantity=entry.Quantity,Zone="Sideboard"});
        }
        if(sideboard.Sum(c=>c.Quantity)>15)errors.Add("Sideboard provenance exceeds 15 cards.");
        var manifest=MakeManifest(request,cards,parent,sideboard);
        blockers.AddRange(cards.Where(c=>!c.LocallyLegal).Select(c=>$"{c.Name}: lacks local Standard legality evidence."));
        blockers.AddRange(engine.Coverage(manifest).Cards.Where(c=>c.Status=="missing").Select(c=>$"{c.Name}: unsupported by the active Forge build."));
        return new(cards,errors,blockers){Sideboard=sideboard};
    }
    public static int CopyLimit(CardEntry card)
    {
        if(Regex.IsMatch(card.Type,@"\bBasic\b")&&Regex.IsMatch(card.Type,@"\bLand\b")||Regex.IsMatch(card.Text,@"A deck can have any number",RegexOptions.IgnoreCase))return int.MaxValue;
        var match=Regex.Match(card.Text,@"A deck can have up to (\d+|five|six|seven|eight|nine|ten) cards",RegexOptions.IgnoreCase);
        if(!match.Success)return 4;
        return int.TryParse(match.Groups[1].Value,out var n)?n:match.Groups[1].Value.ToLowerInvariant() switch{"five"=>5,"six"=>6,"seven"=>7,"eight"=>8,"nine"=>9,_=>10};
    }
    public async Task<DeckManifest> Save(CustomDeckRequest request)
    {
        var validation=Validate(request);if(!validation.CanSave)throw new ArgumentException(string.Join("\n",validation.Errors));
        var parent=request.ParentDeckId is {} parentId?store.Deck(parentId):null;
        var manifest=MakeManifest(request,validation.Cards,parent,validation.Sideboard);
        await store.SaveCustomDeck(manifest);return store.Deck(manifest.Id);
    }
    private static DeckManifest MakeManifest(CustomDeckRequest request,List<CardEntry> main,DeckManifest? parent,List<CardEntry> sideboard)
    {
        var cards=main.Concat(sideboard).ToList();
        var pairs=main.GroupBy(c=>c.Name).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new object[]{g.Key,g.Sum(c=>c.Quantity)}).ToList();
        var listHash=DeckImporter.Hash(System.Text.Json.JsonSerializer.Serialize(pairs,new System.Text.Json.JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        var name=request.Name.Trim();var hash=DeckImporter.Hash(Json.Encode(new{name,sourceType="custom",parentDeckId=parent?.Id,cards}));
        return new(hash,name,"custom","Workshop version",parent?.SourceDeckId??"",parent?.Snapshot??"Local Arena catalog",parent?.SourceUrl,listHash,hash,DateTimeOffset.UtcNow,cards,
            parent is null?"Custom Arena list; legality uses the dated local catalog. No ownership is inferred.":$"Workshop copy of {parent.Name} ({parent.ListHash[..8]}); source captured {parent.Snapshot}. Public results belong to the original list.") {ParentDeckId=parent?.Id};
    }
    public WorkshopMatchup Matchup(string runId,string deckId,string opponentId)
    {
        var run=store.Run(runId);var deck=store.Deck(deckId);var opponent=store.Deck(opponentId);
        return Summarize(run,deck,opponent,store.MatchupSlots(runId,deckId,opponentId));
    }
    public static WorkshopMatchup Summarize(SimulationRun run,DeckManifest deck,DeckManifest opponent,List<GameSlot> all)
    {
        if(deck.Id==opponent.Id||!run.Config.DeckIds.Contains(deck.Id)||!run.Config.DeckIds.Contains(opponent.Id))throw new ArgumentException("Choose a matchup belonging to this session.");
        var slots=all.Where(s=>s.DeckA==deck.Id&&s.DeckB==opponent.Id||s.DeckB==deck.Id&&s.DeckA==opponent.Id).ToList();
        var summary=Metrics.Summarize(run with{Config=run.Config with{DeckIds=[deck.Id,opponent.Id]},Total=slots.Count},slots);
        var pair=summary.Matchups.SingleOrDefault()??throw new InvalidOperationException("No scheduled matchup found.");
        var valid=slots.Where(s=>s.Status=="completed"&&s.Result is not null).ToList();var wins=valid.Count(s=>s.Result!.Winner==deck.Id);var losses=valid.Count(s=>s.Result!.Winner==opponent.Id);
        var play=valid.Where(s=>s.Starter==deck.Id).ToList();var draw=valid.Where(s=>s.Starter!=deck.Id).ToList();
        var analyzed=valid.Where(s=>s.Result!.ManaDetectionVersion==ManaAnalyzer.Version).ToList();
        int Incidents(string kind)=>analyzed.Count(s=>s.Result!.ManaFlags?.Any(f=>f.Player==(s.DeckA==deck.Id?"A":"B")&&f.Kind==kind)==true);
        return new(run,deck,opponent,pair,wins,losses,valid.Count(s=>s.Result!.Draw),play.Count,play.Count(s=>s.Result!.Winner==deck.Id),draw.Count,draw.Count(s=>s.Result!.Winner==deck.Id),
            valid.Count==0?null:(double)wins/valid.Count,Metrics.Wilson(wins,valid.Count),Metrics.Median(valid.Select(s=>(s.Result!.ActualTurns+1)/2)),
            Metrics.Median(valid.Where(s=>s.Result!.Winner==deck.Id).Select(s=>(s.Result!.ActualTurns+1)/2)),Metrics.Median(valid.Where(s=>s.Result!.Winner==opponent.Id).Select(s=>(s.Result!.ActualTurns+1)/2)),
            summary.Mulligans.FirstOrDefault(m=>m.DeckId==deck.Id),analyzed.Count,Incidents("mana-starved"),Incidents("mana-flooded"),
            valid.Take(12).Select(s=>new WorkshopReplay(s.Id,s.Result!.Draw?"Draw":s.Result.Winner==deck.Id?"Win":"Loss",s.Result.ActualTurns)).ToList()) {
                WinTurns=valid.Where(s=>s.Result!.Winner==deck.Id).Select(s=>s.Result!.ActualTurns).ToList(),
                LossTurns=valid.Where(s=>s.Result!.Winner==opponent.Id).Select(s=>s.Result!.ActualTurns).ToList(),
                DrawTurns=valid.Where(s=>s.Result!.Draw).Select(s=>s.Result!.ActualTurns).ToList()
            };
    }
}
