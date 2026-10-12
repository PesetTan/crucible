using System.Globalization;

namespace DeckTester;

public static class UiText
{
    public static string Label(string value)=>value switch{
        "cancelled"=>"Paused","cancelling"=>"Pausing",
        _=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_',' '))};

    // Older saved incidents retain their original evidence text. Normalize its terminology only when displayed.
    public static string PlayerTurnWording(string value)=>value
        .Replace("own-turn","player-turn",StringComparison.OrdinalIgnoreCase)
        .Replace("own turn","player turn",StringComparison.OrdinalIgnoreCase);
}
