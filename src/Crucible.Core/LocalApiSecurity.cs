namespace DeckTester;

public static class LocalApiSecurity
{
    public static void Use(WebApplication app,HashSet<string> allowedHosts,string instanceToken)
    {
        app.Use(async(context,next)=>{
            if(!allowedHosts.Contains(context.Request.Host.Host)){context.Response.StatusCode=403;return;}
            var origin=context.Request.Headers.Origin.ToString();
            if(origin.Length>0&&(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||!string.Equals(uri.Scheme,context.Request.Scheme,StringComparison.OrdinalIgnoreCase)||!string.Equals(uri.Host,context.Request.Host.Host,StringComparison.OrdinalIgnoreCase)||uri.Port!=context.Request.Host.Port)) {context.Response.StatusCode=403;return;}
            if(context.Request.Path.StartsWithSegments("/api")&&context.Request.Method is not ("GET" or "HEAD")&&context.Request.Headers["X-Instance-Token"]!=instanceToken){context.Response.StatusCode=403;return;}
            try{await next();}
            catch(Exception error)when(error is KeyNotFoundException or ArgumentException or InvalidOperationException or InvalidDataException){
                if(context.Response.HasStarted)throw;
                context.Response.StatusCode=error is KeyNotFoundException?404:error is ArgumentException?422:409;
                await context.Response.WriteAsJsonAsync(new ApiError(context.Response.StatusCode,error.Message),Json.Info<ApiError>());
    }
});
    }
}
