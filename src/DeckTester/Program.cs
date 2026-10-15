using System.Security.Cryptography;
using System.Text;
using System.Net;
using DeckTester;
using DeckTester.Components;
using Microsoft.AspNetCore.DataProtection;

if(args.Contains("--setup",StringComparer.Ordinal))
{
    var directory=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT")??Directory.GetCurrentDirectory();
    while(!Directory.Exists(Path.Combine(directory,"docs","specs"))){directory=Path.GetDirectoryName(directory)??throw new ArgumentException("Run setup from the Crucible project directory.");}
    await EngineSetup.Install(directory);return;
}
if(args.Contains("--activate-engine",StringComparer.Ordinal))
{
    var directory=Environment.GetEnvironmentVariable("MTG_PROJECT_ROOT")??Directory.GetCurrentDirectory();
    while(!Directory.Exists(Path.Combine(directory,"docs","specs"))){directory=Path.GetDirectoryName(directory)??throw new ArgumentException("Run activation from the Crucible project directory.");}
    var bundle=await new EngineBundleManager(directory).Publish();
    Console.WriteLine($"Activated Forge bundle {bundle.Id}");
    return;
}

var mode=Environment.GetEnvironmentVariable("MTG_HOST_MODE")??"combined";
if(mode is not ("ui" or "service" or "combined"))throw new ArgumentException("MTG_HOST_MODE must be ui, service, or combined.");
var port=int.TryParse(Environment.GetEnvironmentVariable("MTG_PORT"),out var configuredPort)?configuredPort:mode=="service"?5079:5077;
if(port is <1024 or >65535)throw new ArgumentException("MTG_PORT must be between 1024 and 65535.");
var builder=WebApplication.CreateBuilder(args);
builder.Services.AddResponseCompression(options=>{options.EnableForHttps=true;options.MimeTypes=Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);});
builder.Configuration.AddJsonFile("branding.json", optional:false, reloadOnChange:true);
builder.Services.AddSingleton<BrandSettings>();
builder.Services.AddSingleton<PilotEvaluationStore>();
builder.Services.AddScoped<SourceRateCatalog>();
var uiStateRoot=Path.GetFullPath(Environment.GetEnvironmentVariable("MTG_UI_STATE_ROOT")??Path.Combine(builder.Environment.ContentRootPath,"ui-state"));
builder.Services.AddSingleton(services=>new BuildNames(Path.Combine(uiStateRoot,"build-names.json"),services.GetRequiredService<ILogger<BuildNames>>()));
var lanAddress=mode=="service"?null:Environment.GetEnvironmentVariable("MTG_LAN_ADDRESS");
if(lanAddress is not null && (!IPAddress.TryParse(lanAddress,out var lanIp)||lanIp.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||
    !(lanIp.GetAddressBytes()[0]==10 || (lanIp.GetAddressBytes()[0]==172 && lanIp.GetAddressBytes()[1] is >=16 and <=31) || (lanIp.GetAddressBytes()[0]==192 && lanIp.GetAddressBytes()[1]==168))))
    throw new ArgumentException("MTG_LAN_ADDRESS must be a private IPv4 address assigned to this computer.");
var allowedHosts=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"127.0.0.1","localhost"};
if(lanAddress is not null)allowedHosts.Add(lanAddress);
builder.WebHost.UseUrls(lanAddress is null ? [$"http://127.0.0.1:{port}"] : [$"http://127.0.0.1:{port}",$"http://{lanAddress}:{port}"]);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
FileStream? serviceOwnership=null;
if(mode=="ui"){
    builder.Services.Configure<HostOptions>(options=>options.ShutdownTimeout=TimeSpan.FromSeconds(5));
    var backendUri=new Uri(Environment.GetEnvironmentVariable("MTG_BACKEND_URL")??"http://127.0.0.1:5079/");
    if(backendUri.Scheme!="http"||!backendUri.IsLoopback||backendUri.AbsolutePath!="/"||backendUri.Query.Length>0||backendUri.UserInfo.Length>0)
        throw new ArgumentException("MTG_BACKEND_URL must be an HTTP loopback origin.");
    builder.Services.AddHttpClient("simulation",client=>{client.BaseAddress=backendUri;client.Timeout=TimeSpan.FromSeconds(90);})
        .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All});
    builder.Services.AddSingleton(new AppPaths(builder.Environment));
    builder.Services.AddScoped<IUiBackend>(services=>new RemoteUiBackend(services.GetRequiredService<IHttpClientFactory>().CreateClient("simulation"),services.GetRequiredService<AppPaths>()));
    var uiKeys=Path.Combine(uiStateRoot,"protection-keys");
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(uiKeys));
}else{
    var paths=new AppPaths(builder.Environment);
    try{serviceOwnership=new FileStream(Path.Combine(paths.Data,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    catch(IOException error){throw new InvalidOperationException("Another simulation host already owns this data directory. Stop that host before starting a replacement.",error);}
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Data,"protection-keys")));
    builder.Services.AddSingleton(paths);BackendHosting.Register(builder.Services);
}
builder.Services.ConfigureHttpJsonOptions(options=>{options.SerializerOptions.PropertyNamingPolicy=Json.Options.PropertyNamingPolicy;});
builder.Services.AddSingleton<AiNoteStore>();
var app=builder.Build();
app.Lifetime.ApplicationStopped.Register(()=>serviceOwnership?.Dispose());
app.UseResponseCompression();
app.MapGet("/branding.css", (BrandSettings branding, HttpContext context) => {
    context.Response.Headers.CacheControl="no-store";
    return Results.Text(branding.StyleSheet(), "text/css");
});
var instanceToken=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
LocalApiSecurity.Use(app,allowedHosts,instanceToken);
app.UseStaticFiles();app.UseAntiforgery();app.MapStaticAssets();
HostHealth.Map(app,mode);
BackendHosting.MapAnnotations(app);
if(mode=="ui"){
    app.MapGet("/api/v1/session",()=>new{instanceToken});
    app.MapGet("/api/{**path}",async(HttpContext context,IHttpClientFactory factory)=>{
        var path=context.Request.Path.Value!;
        if(!path.StartsWith("/api/v1/runs/",StringComparison.Ordinal)){context.Response.StatusCode=404;return;}
        if(path.EndsWith("/export",StringComparison.OrdinalIgnoreCase)||path.EndsWith("/replay-export",StringComparison.OrdinalIgnoreCase)){context.Response.StatusCode=404;return;}
        try{
            using var response=await factory.CreateClient("simulation").GetAsync(path.TrimStart('/')+context.Request.QueryString,HttpCompletionOption.ResponseHeadersRead,context.RequestAborted);
            context.Response.StatusCode=(int)response.StatusCode;
            context.Response.ContentType=response.Content.Headers.ContentType?.ToString();
            if(response.Content.Headers.ContentDisposition is {} disposition)context.Response.Headers.ContentDisposition=disposition.ToString();
            await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);
        }catch(HttpRequestException){context.Response.StatusCode=503;await context.Response.WriteAsync("Simulation service unavailable.");}
    });
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    await app.RunAsync();return;
}
BackendHosting.Map(app,instanceToken);
if(mode=="combined")app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await BackendHosting.Initialize(app);
await app.RunAsync();
