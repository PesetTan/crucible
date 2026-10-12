using System.Text.RegularExpressions;

namespace MtgSimulator;

public sealed class BrandSettings(IConfiguration configuration)
{
    public string Logo => LocalAsset(configuration["Branding:Logo"]);
    public string Icon => LocalAsset(configuration["Branding:Icon"] ?? configuration["Branding:Logo"]);
    public string IconType => Icon.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png";
    public (string Name, string Color)[] Palette => Enumerable.Range(0, ChartPalette.MaximumDecks).Select(i =>
        (configuration[$"Branding:ChartColors:{i}:Name"] ?? $"Color {i + 1}",
         Color(configuration[$"Branding:ChartColors:{i}:Color"]))).ToArray();

    public string StyleSheet()
    {
        var declarations = new List<string>();
        foreach (var section in new[] { "UiColors", "DetailColors" })
            foreach (var entry in configuration.GetSection($"Branding:{section}").GetChildren())
                if (Regex.IsMatch(entry.Key, "^[a-z][a-z0-9-]*$"))
                    declarations.Add($"--theme-{entry.Key}:{Color(entry.Value)}");
        return ":root{" + string.Join(";", declarations) + "}";
    }

    private static string Color(string? value) => value is not null && Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$") ? value : "#77D1BB";
    private static string LocalAsset(string? value) => value is not null && Regex.IsMatch(value, "^assets/[a-zA-Z0-9/_-]+\\.(svg|png|webp|ico)$", RegexOptions.IgnoreCase) && !value.Contains("..", StringComparison.Ordinal) ? value : "assets/brand/crucible/master.svg";
}
