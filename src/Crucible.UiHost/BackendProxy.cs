using System.Net;
using System.Text;
using DeckTester;

namespace Crucible.UiHost;
// One fixed loopback destination. Never accepts an upstream URL or credentials from browser input.
public sealed class BackendProxy(HttpClient client)
{
    public async Task<T> Read<T>(string path, CancellationToken ct) { using var r = await Send(HttpMethod.Get, path, null, null, ct); var raw = await r.Content.ReadAsStringAsync(ct); if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Simulation service: " + raw); return Json.Decode<T>(raw); }
    public async Task<T> Post<T>(string path, string body, CancellationToken ct) { using var r = await Send(HttpMethod.Post, path, body, null, ct); var raw = await r.Content.ReadAsStringAsync(ct); if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Simulation service: " + raw); return Json.Decode<T>(raw); }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body, HttpContext? context, CancellationToken ct)
    {
        if (!path.StartsWith("/api/v1/", StringComparison.Ordinal)) throw new ArgumentException("Unsupported API path.");
        // Fetch a fresh backend instance token per mutation, avoiding stale credentials across restarts.
        string? token = null;
        if (method != HttpMethod.Get && method != HttpMethod.Head)
        {
            using var session = await client.GetAsync("api/v1/session", ct); session.EnsureSuccessStatusCode();
            token = Json.Decode<InstanceSession>(await session.Content.ReadAsStringAsync(ct)).InstanceToken;
        }
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.TryAddWithoutValidation("X-Instance-Token", token);
        if (context is not null) foreach (var name in new[] { "If-None-Match", "Idempotency-Key", "Range" }) if (context.Request.Headers.TryGetValue(name, out var value)) request.Headers.TryAddWithoutValidation(name, value.ToArray());
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    public async Task Forward(HttpContext context)
    {
        try
        {
            string? body = null;
            if (context.Request.Method is not ("GET" or "HEAD"))
            {
                if (context.Request.ContentLength > 2_097_152) { context.Response.StatusCode = 413; return; }
                using var reader = new StreamReader(context.Request.Body); body = await reader.ReadToEndAsync(context.RequestAborted);
                if (Encoding.UTF8.GetByteCount(body) > 2_097_152) { context.Response.StatusCode = 413; return; }
            }
            using var upstream = await Send(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString, body, context, context.RequestAborted);
            context.Response.StatusCode = (int)upstream.StatusCode;
            foreach (var name in new[] { "ETag", "Cache-Control", "Content-Type", "Content-Disposition", "Content-Range", "Accept-Ranges" })
            {
                if (upstream.Headers.TryGetValues(name, out var values) || upstream.Content.Headers.TryGetValues(name, out values)) context.Response.Headers[name] = values.ToArray();
            }
            await upstream.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException && !context.RequestAborted.IsCancellationRequested) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new ApiError(503, "Simulation service unavailable. Your saved data remains local."), Json.Info<ApiError>(), cancellationToken: context.RequestAborted); }
    }
}
