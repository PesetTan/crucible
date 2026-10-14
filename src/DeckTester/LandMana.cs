using System.Text.RegularExpressions;

namespace DeckTester;

public sealed record LandManaSummary(string Symbols, string Description);

public static class LandMana
{
    private static readonly (string Type, char Symbol)[] Basics = [("Plains",'W'),("Island",'U'),("Swamp",'B'),("Mountain",'R'),("Forest",'G')];
    private static readonly Dictionary<char,string> Names = new() { ['W']="white",['U']="blue",['B']="black",['R']="red",['G']="green",['C']="colorless" };

    // Match the displayed production symbols, with colorless and nonproducing lands last.
    public static string SortKey(CardEntry card)
    {
        var mana=Read(card);
        if(mana is null)return "8";
        if(mana.Symbols.Length==0)return "7";
        return string.Concat("WUBRGC".Where(c=>mana.Symbols.Contains(c)).Select(c=>(char)('1'+"WUBRGC".IndexOf(c))));
    }

    public static LandManaSummary? Read(CardEntry card)
    {
        // Only describe the displayed face, not a spell's reverse land face.
        var type=card.Type.Split("//",StringSplitOptions.None)[0];
        if(!Regex.IsMatch(type,@"\bLand\b"))return null;
        var colors=new HashSet<char>();
        foreach(var (basic,symbol) in Basics)
            if(Regex.IsMatch(type,$@"\b{basic}\b"))colors.Add(symbol);
        var text=card.Text.Split("//",StringSplitOptions.None)[0].Replace("\\n","\n");
        // Quoted abilities are granted to other objects, not this land's production.
        var ownText=Regex.Replace(text,"\"[^\"]*\"","");
        if(Regex.IsMatch(ownText,@"choose a basic land type",RegexOptions.IgnoreCase))
            foreach(var c in "WUBRG")colors.Add(c);
        foreach(Match addition in Regex.Matches(ownText,@"\badd\s+([^\.\n]+)",RegexOptions.IgnoreCase)){
            var output=addition.Groups[1].Value;
            foreach(Match token in Regex.Matches(output,@"\{([^}]+)\}")){
                var body=token.Groups[1].Value.Replace("o","");
                if(body.All(c=>"WUBRGC".Contains(c)))foreach(var c in body)colors.Add(c);
            }
            if(Regex.IsMatch(output,@"\b(any (?:one )?color|(?:combination of |chosen |that |exiled cards' )colors?)\b",RegexOptions.IgnoreCase))
                foreach(var c in "WUBRG")colors.Add(c);
        }
        if(colors.Count==0){
            if(Regex.IsMatch(ownText,@"search your library for [^.\n]*land",RegexOptions.IgnoreCase))
                return new("","Finds a land; does not directly produce mana.");
            return null;
        }
        var ordered="WUBRGC".Where(colors.Contains).ToArray();
        var label=$"Possible mana: {string.Join(", ",ordered.Select(c=>Names[c]))}.";
        var rules=Regex.Replace(text,@"\{([^}]+)\}",m=>Regex.Replace(m.Groups[1].Value,@"o([WUBRGCT]|\d+)","{$1}"));
        return new(string.Join(" / ",ordered.Select(c=>$"{{{c}}}")),string.IsNullOrWhiteSpace(rules)?label:$"{label}\n{rules}");
    }
}
