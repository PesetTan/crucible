namespace MtgSimulator;

public sealed class AppPaths
{
    public string Root { get; }
    public string Data => Path.Combine(Root, "data");
    public string Source => Path.GetFullPath(Path.Combine(Root, "..", "..", "standard_cards.sqlite"));
    public string Vendor => Path.Combine(Root, "vendor");
    public string Jar => Path.Combine(Vendor, "forge-gui-desktop-2.0.15-jar-with-dependencies.jar");
    public string Bridge => Path.Combine(Root, "engine", "build");
    public string Java => Environment.GetEnvironmentVariable("MTG_JAVA") ?? (OperatingSystem.IsMacOS()?Path.Combine(Vendor,"jdk","Contents","Home","bin","java"):Path.Combine(Vendor,"jdk","bin","java"));
    public string Replays => Path.Combine(Data, "replays");
    public string Decks => Path.Combine(Data, "decks");
    public AppPaths(IHostEnvironment environment)
    {
        var supplied = Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT");
        Root = supplied is not null ? Path.GetFullPath(supplied) : Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
        if (!Directory.Exists(Path.Combine(Root, "docs", "specs"))) throw new InvalidOperationException("Set MTG_PROJECT_ROOT to the mtg-simulator project directory.");
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Replays); Directory.CreateDirectory(Decks);
    }
}
