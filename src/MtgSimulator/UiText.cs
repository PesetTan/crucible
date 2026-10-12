using System.Globalization;

namespace MtgSimulator;

public static class UiText
{
    public static string Label(string value)=>value switch{
        "cancelled"=>"Paused","cancelling"=>"Pausing",
        _=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_',' '))};
}
