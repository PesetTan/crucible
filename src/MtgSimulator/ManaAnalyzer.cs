namespace MtgSimulator;

public static class ManaAnalyzer
{
    public const string Version="land-availability-v1";
    public const string StarvedDefinition="At own turn 4 or later: fewer than 3 lands in play, no lands in hand, and at least 2 nonland cards in hand, at two consecutive completed own-turn ends.";
    public const string FloodedDefinition="At own turn 5 or later: at least 5 lands in play, at least 3 lands in hand, and at most 1 nonland card in hand, at two consecutive completed own-turn ends.";
    public static List<ManaFlag> Detect(IEnumerable<TurnSample> measurements)
    {
        var flags=new List<ManaFlag>();
        foreach(var player in measurements.GroupBy(s=>s.Player)){
            var samples=player.OrderBy(s=>s.PlayerTurn).ThenBy(s=>s.ActualTurn).ToList();
            Add("mana-starved",samples,s=>s.PlayerTurn>=4&&s.Lands<3&&s.HandLands==0&&s.HandSpells>=2,flags);
            Add("mana-flooded",samples,s=>s.PlayerTurn>=5&&s.Lands>=5&&s.HandLands>=3&&s.HandSpells<=1,flags);
        }return flags;
    }
    private static void Add(string kind,List<TurnSample> samples,Func<TurnSample,bool> matches,List<ManaFlag> flags)
    {
        var streak=new List<TurnSample>();
        void Flush(){if(streak.Count>=2){var first=streak[0];var last=streak[^1];var evidence=string.Join("; ",streak.Select(s=>$"own turn {s.PlayerTurn} (actual {s.ActualTurn}): {s.Lands} lands in play, {s.HandLands} lands and {s.HandSpells} nonlands in hand"));flags.Add(new(kind,first.Player,first.PlayerTurn,last.PlayerTurn,first.ActualTurn,last.ActualTurn,evidence,Version));}streak.Clear();}
        foreach(var sample in samples){
            if(sample.HandLands is null||sample.HandSpells is null||!matches(sample)){Flush();continue;}
            if(streak.Count>0&&sample.PlayerTurn!=streak[^1].PlayerTurn+1)Flush();streak.Add(sample);
        }Flush();
    }
}
