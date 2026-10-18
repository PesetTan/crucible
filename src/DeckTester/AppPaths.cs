namespace DeckTester;

public sealed class AppPaths
{
    public string Root { get; }
    public string Assets { get; }
    public bool Installed { get; }
    public string Data => Path.Combine(Root, "data");
    public string Source => Path.GetFullPath(Environment.GetEnvironmentVariable("MTG_CARD_DB") ?? (Installed ? Path.Combine(Root,"catalog.sqlite") : Path.Combine(Root, "..", "..", "standard_cards.sqlite")));
    // Explicit opt-in only. The path may live outside the checkout and is never packaged.
    public string? DeckPack => Environment.GetEnvironmentVariable("CRUCIBLE_DECK_PACK") is {} pack ? Path.GetFullPath(pack) : null;
    public string Vendor => Path.Combine(Root, "vendor");
    public string Jar => Path.Combine(Vendor, "forge-gui-desktop-2.0.15-jar-with-dependencies.jar");
    public string Bridge => Path.Combine(Root, "engine", "build");
    public static string JavaTool(string name) => OperatingSystem.IsWindows() ? name+".exe" : name;
    public string JavaHome => OperatingSystem.IsMacOS()?Path.Combine(Vendor,"jdk","Contents","Home"):Path.Combine(Vendor,"jdk");
    public string Java => Environment.GetEnvironmentVariable("MTG_JAVA") ?? Path.Combine(JavaHome,"bin",JavaTool("java"));
    public string Replays => Path.Combine(Data, "replays");
    public string Decks => Path.Combine(Data, "decks");
    public AppPaths(IHostEnvironment environment)
        : this(Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT") ??
            (Environment.GetEnvironmentVariable("MTG_ASSET_ROOT") is not null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Crucible") : Path.Combine(environment.ContentRootPath,"..","..")),
            Environment.GetEnvironmentVariable("MTG_ASSET_ROOT")) { }

    public AppPaths(string root,string? assets=null)
    {
        Root = Path.GetFullPath(root);
        Assets = Path.GetFullPath(assets??Root);
        Installed = assets is not null;
        if (Installed ? !File.Exists(Path.Combine(Assets,"starter","manifest.json")) : !Directory.Exists(Path.Combine(Root,"docs","specs")))
            throw new InvalidOperationException("The Crucible installation assets or project directory are unavailable.");
        Directory.CreateDirectory(Data); Directory.CreateDirectory(Replays); Directory.CreateDirectory(Decks);
    }
}
