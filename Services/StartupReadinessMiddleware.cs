namespace CENS15_V2.Services;

public sealed class StartupReadinessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, StartupReadiness readiness, CheckpointGate gate)
    {
        var path = context.Request.Path.Value;
        var probeMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        var health = probeMethod && IsPath(path, "/api/health");
        var ready = probeMethod && IsPath(path, "/api/ready");
        if (health || ready)
        {
            context.Response.Headers.CacheControl = "no-store";
            var available = readiness.IsReady && gate.Healthy;
            context.Response.StatusCode = health || available ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json; charset=utf-8";
            if (!HttpMethods.IsHead(context.Request.Method))
            {
                if (health) await context.Response.WriteAsJsonAsync(new { status = "ok" }, context.RequestAborted);
                else if (available) await context.Response.WriteAsJsonAsync(new { status = "ready" }, context.RequestAborted);
                else await context.Response.WriteAsJsonAsync(new { status = "not_ready", reason = Reason(readiness, gate) }, context.RequestAborted);
            }
            return;
        }

        if (context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!readiness.IsReady || !gate.Healthy)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "application/json; charset=utf-8";
            if (!HttpMethods.IsHead(context.Request.Method))
                await context.Response.WriteAsJsonAsync(new { message = "Service temporarily unavailable.", reason = Reason(readiness, gate) }, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsPath(string? path, string expected) =>
        string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, expected + "/", StringComparison.OrdinalIgnoreCase);

    private static string Reason(StartupReadiness readiness, CheckpointGate gate) =>
        readiness.IsReady && !gate.Healthy ? "recovery_pending" : readiness.Reason;
}
