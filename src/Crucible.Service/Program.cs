using System.Security.Cryptography;
using DeckTester;

HostControl.WaitForLauncher();
if(Environment.GetEnvironmentVariable("MTG_STARTUP_DIAGNOSTICS")=="1")Console.Error.WriteLine("Crucible.Service entered Main.");
if(args.Contains("--check-runtime",StringComparer.Ordinal)){
    Console.WriteLine(Json.Encode(new CreatedRun(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported?"jit":"native-aot")));
    return;
}
Run(args).GetAwaiter().GetResult();

static async Task Run(string[] args)
{
    var mode=Environment.GetEnvironmentVariable("MTG_HOST_MODE");
    if(mode is not (null or "service"))throw new ArgumentException("Crucible.Service only supports MTG_HOST_MODE=service.");
    var port=int.TryParse(Environment.GetEnvironmentVariable("MTG_PORT"),out var configuredPort)?configuredPort:5079;
    if(port is <1024 or >65535)throw new ArgumentException("MTG_PORT must be between 1024 and 65535.");
    var builder=WebApplication.CreateSlimBuilder(args);
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    builder.Services.AddResponseCompression(options=>options.MimeTypes=Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]));
    builder.Services.ConfigureHttpJsonOptions(options=>options.SerializerOptions.TypeInfoResolverChain.Insert(0,Json.Options.TypeInfoResolver!));
    var paths=new AppPaths(builder.Environment);
    if(args.Contains("--setup",StringComparer.Ordinal)){await EngineSetup.Install(paths);return;}
    var ownership=HostControl.AcquireDataOwnership(paths);
    CatalogBootstrap.Ensure(paths);
    builder.Services.AddSingleton(paths);
    BackendHosting.Register(builder.Services);
    builder.Services.AddSingleton<AiNoteStore>();
    builder.Services.AddSingleton<PilotEvaluationStore>();
    var app=builder.Build();
    app.Lifetime.ApplicationStopped.Register(ownership.Dispose);
    app.UseResponseCompression();
    var instanceToken=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    LocalApiSecurity.Use(app,new(StringComparer.OrdinalIgnoreCase){"127.0.0.1","localhost"},instanceToken);
    HostHealth.Map(app,"service");
    HostControl.Map(app);
    BackendHosting.MapAnnotations(app);
    BackendHosting.Map(app,instanceToken);
    await BackendHosting.Initialize(app);
    await app.RunAsync();
}
