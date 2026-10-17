using System.Net;
using System.Security.Cryptography;
using Crucible.UiHost;
using DeckTester;

HostControl.WaitForLauncher();
if (args.Contains("--check-runtime", StringComparer.Ordinal)) { Console.WriteLine(Json.Encode(new CreatedRun(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "jit" : "native-aot"))); return; }
Run(args).GetAwaiter().GetResult();
static async Task Run(string[] args)
{
    var port = int.TryParse(Environment.GetEnvironmentVariable("MTG_PORT"), out var value) ? value : 5077;
    if (port is < 1024 or > 65535) throw new ArgumentException("Invalid UI port.");
    var backend = new Uri(Environment.GetEnvironmentVariable("MTG_BACKEND_URL") ?? "http://127.0.0.1:5079/");
    if (backend.Scheme != "http" || !backend.IsLoopback || backend.AbsolutePath != "/" || backend.Query.Length > 0 || backend.UserInfo.Length > 0) throw new ArgumentException("Backend must be an HTTP loopback origin.");
    var options = new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") };
    var builder = WebApplication.CreateSlimBuilder(options);
    builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2_097_152);
    var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost" };
    var urls = new List<string> { $"http://127.0.0.1:{port}" };
    if (Environment.GetEnvironmentVariable("MTG_LAN_ADDRESS") is { } lan)
    {
        if (!IPAddress.TryParse(lan, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException("LAN address must be a private IPv4 address.");
        var b = ip.GetAddressBytes(); if (!(b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168)) throw new ArgumentException("LAN address must be private.");
        hosts.Add(lan); urls.Add($"http://{lan}:{port}");
    }
    builder.WebHost.UseUrls(urls.ToArray());
    builder.Configuration.AddJsonFile("branding.json", false, false);
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, UiJsonContext.Default));
    var paths = new AppPaths(builder.Environment);
    builder.Services.AddSingleton<BrandSettings>(); builder.Services.AddSingleton(paths);
    var state = Path.GetFullPath(Environment.GetEnvironmentVariable("MTG_UI_STATE_ROOT") ?? Path.Combine(paths.Root, "src/DeckTester/ui-state"));
    builder.Services.AddSingleton<PilotEvaluationStore>();
    builder.Services.AddSingleton(new BuildNames(Path.Combine(state, "build-names.json")));
    builder.Services.AddSingleton(new BackendProxy(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.All }) { BaseAddress = backend, Timeout = TimeSpan.FromSeconds(90) }));
    var app = builder.Build();
    var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    LocalApiSecurity.Use(app, hosts, token);
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
        await next();
    });
    HostHealth.Map(app, "ui");
    HostControl.Map(app);
    app.MapGet("/api/v1/session", () => Results.Json(new InstanceSession(token), Json.Info<InstanceSession>()));
    app.MapGet("/branding.css", (BrandSettings brand) => Results.Text(brand.StyleSheet(), "text/css"));
    app.MapGet("/api/v1/presentation/catalog", async (BackendProxy proxy, HttpContext c) => Results.Json(Presentation.Catalog(await proxy.Read<LibrarySnapshot>("/api/v1/library-state", c.RequestAborted)), UiJsonContext.Default.UiCatalog));
    app.MapGet("/api/v1/presentation/build-names", (string engine, string bridge, BuildNames names) =>
    {
        if (engine.Length > 128 || bridge.Length > 128 || engine.Any(c => !char.IsAsciiLetterOrDigit(c)) || bridge.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid build identity.");
        return Results.Json(new UiBuild(names.Engine(engine), names.Bridge(bridge)), UiJsonContext.Default.UiBuild);
    });
    app.MapGet("/api/v1/presentation/pilot-source", (string report, string file, PilotEvaluationStore store) =>
    {
        var snapshot = store.Read();
        if (snapshot.Report is not { } saved || saved.Id != report) throw new KeyNotFoundException("Evaluation changed. Refresh its scorecard first.");
        return Results.Json(store.ReadSource(saved, file), Json.Info<PilotSourceDocument>());
    });
    app.MapPost("/api/v1/presentation/estimate", async (HttpContext c, BackendProxy proxy) =>
    {
        var request = await System.Text.Json.JsonSerializer.DeserializeAsync(c.Request.Body, Json.Info<RunRequest>(), c.RequestAborted) ?? throw new ArgumentException("Missing run request.");
        var preflight = await proxy.Post<PreflightResponse>("/api/v1/runs/preflight", Json.Encode(request), c.RequestAborted);
        var snapshot = await proxy.Read<UiSnapshot>("/api/v1/ui-state", c.RequestAborted);
        // Exact cache eligibility comes from backend preflight. The estimate is advisory only.
        var range = request.ProviderId=="forge"?RuntimeEstimate.Calculate(request.DeckIds, request.GamesPerPair, request.Workers, request.TimeoutSeconds, request.TurnLimit, request.StaggerStarts, request.EstimatedGameSeconds, request.StartLeadSeconds, snapshot.TimingSamples, request.StartupSpacingSeconds, null, snapshot.SessionSamples, request.PerformanceMode):null;
        return Results.Json(new EstimateReply(preflight, range), UiJsonContext.Default.EstimateReply);
    });
    app.MapMethods("/api/v1/{**path}", ["GET", "HEAD", "POST", "DELETE"], (HttpContext c, BackendProxy proxy) => proxy.Forward(c));
    app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = c.File.Name == "index.html" ? "no-cache" : "public, max-age=3600" });
    app.MapFallbackToFile("index.html");
    await app.RunAsync();
}
