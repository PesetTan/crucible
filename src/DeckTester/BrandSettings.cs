using System.Text.RegularExpressions;

namespace DeckTester;

public sealed class BrandSettings(IConfiguration configuration)
{
    public string Logo => LocalAsset(configuration["Branding:Logo"]);
    public string LogoBowl => LocalAsset(configuration["Branding:LogoBowl"] ?? "assets/brand/crucible/bowl.svg");
    public string LogoFlame => LocalAsset(configuration["Branding:LogoFlame"] ?? "assets/brand/crucible/flame.svg");
    public string Icon => LocalAsset(configuration["Branding:Icon"] ?? configuration["Branding:Logo"]);
    public string IconType => Icon.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png";
    public (string Name, string Color)[] Palette => Enumerable.Range(0, SimulationLimits.MaximumDecks).Select(i =>
        (configuration[$"Branding:ChartColors:{i}:Name"] ?? $"Color {i + 1}",
         $"var(--theme-chart-{i + 1})")).ToArray();

    public string StyleSheet()
    {
        var shared = Declarations("SharedColors");
        var light = Declarations("Themes:Light");
        var dark = Declarations("Themes:Dark");
        for (var i = 0; i < SimulationLimits.MaximumDecks; i++)
        {
            var color = configuration[$"Branding:ChartColors:{i}:Color"];
            light.Add($"--theme-chart-{i + 1}:{Color(configuration[$"Branding:ChartColors:{i}:LightColor"] ?? color)}");
            dark.Add($"--theme-chart-{i + 1}:{Color(color)}");
        }
        return ":root{" + string.Join(";", shared.Concat(light)) + "}" +
            ":root[data-theme=\"dark\"]{" + string.Join(";", dark) + "}";
    }

    private List<string> Declarations(string section) => configuration.GetSection($"Branding:{section}")
        .GetChildren().Where(entry => Regex.IsMatch(entry.Key, "^[a-z][a-z0-9-]*$"))
        .Select(entry => $"--theme-{entry.Key}:{Color(entry.Value)}").ToList();

    private static string Color(string? value) => value is not null && Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$") ? value : "#77D1BB";
    private static string LocalAsset(string? value) => value is not null && Regex.IsMatch(value, "^assets/[a-zA-Z0-9/_-]+\\.(svg|png|webp|ico)$", RegexOptions.IgnoreCase) && !value.Contains("..", StringComparison.Ordinal) ? value : "assets/brand/crucible/master.svg";
}
