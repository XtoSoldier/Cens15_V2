namespace CENS15_V2.Services;

// Writer turnstile prevents an unending stream of requests from starving a checkpoint.
public sealed class CheckpointGate
{
    private readonly SemaphoreSlim turnstile = new(1);
    private readonly SemaphoreSlim room = new(1);
    private readonly SemaphoreSlim mutex = new(1);
    private int readers;
    public bool Healthy { get; set; } = true;

    public async Task<IDisposable> ReadAsync(CancellationToken ct = default)
    {
        await turnstile.WaitAsync(ct);
        turnstile.Release();
        await mutex.WaitAsync(ct);
        try
        {
            if (readers == 0) await room.WaitAsync(ct);
            readers++;
        }
        finally { mutex.Release(); }
        return new Lease(() =>
        {
            mutex.Wait();
            try { if (--readers == 0) room.Release(); }
            finally { mutex.Release(); }
        });
    }

    public async Task<IDisposable> WriteAsync(CancellationToken ct = default)
    {
        await turnstile.WaitAsync(ct);
        try { await room.WaitAsync(ct); }
        catch { turnstile.Release(); throw; }
        return new Lease(() => { room.Release(); turnstile.Release(); });
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}

public sealed class CheckpointGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CheckpointGate gate)
    {
        if (context.Request.Path.StartsWithSegments("/api/checkpoints", StringComparison.OrdinalIgnoreCase))
        {
            if (!gate.Healthy) { context.Response.StatusCode = 503; return; }
            await next(context);
            return;
        }
        // Acquire before authentication so no old principal is in flight while an import rotates the epoch.
        using var lease = string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/transferencias/importar", StringComparison.OrdinalIgnoreCase)
            ? await gate.WriteAsync(context.RequestAborted)
            : await gate.ReadAsync(context.RequestAborted);
        if (!gate.Healthy) { context.Response.StatusCode = 503; return; }
        await next(context);
    }
}
