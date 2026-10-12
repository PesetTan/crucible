using System.Globalization;

namespace MtgSimulator;

public static class UiText
{
    public static string Label(string value)=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_',' '));
}
