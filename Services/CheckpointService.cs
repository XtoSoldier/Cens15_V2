using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Security;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services;

public sealed class CheckpointPreviews
{
    public sealed record Binding(Guid Id, string Caller, string Epoch, string Target, string Source, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, Binding> bindings = new(StringComparer.Ordinal);
    public string Add(Binding binding)
    {
        foreach (var pair in bindings.Where(p => p.Value.Expires < DateTimeOffset.UtcNow)) bindings.TryRemove(pair.Key, out _);
        if (bindings.Count >= 1000) throw new CheckpointException("Demasiadas vistas previas pendientes.", 409);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        bindings[token] = binding;
        return token;
    }
    public Binding? Take(string token) => token is not null && bindings.TryRemove(token, out var value) ? value : null;
}

public sealed class CheckpointService(AppDbContext db, CheckpointStorage storage, CheckpointGate gate,
    CheckpointModel model, CheckpointRecovery recovery, SessionEpoch epoch, CheckpointPreviews previews)
{
    private async Task<string> RequireAdmin(ClaimsPrincipal caller, CancellationToken ct)
    {
        if (!gate.Healthy) throw new CheckpointException("Recuperacion pendiente. Servicio bloqueado.", 503);
        if (caller.FindFirstValue(SessionEpoch.Claim) != epoch.Current) throw new CheckpointException("La sesion ha caducado. Ingrese nuevamente.", 401);
        var id = caller.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId) || !caller.IsInRole("Admin") ||
            !await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId && u.Status && u.Role.Name == "Admin" && u.Auth != null, ct))
            throw new CheckpointException("Solo un Admin activo puede administrar checkpoints.", 403);
        return userId.ToString("D");
    }

    private static string Hash(ApplicationSnapshot s)
    {
        // Metadata about when/by whom the live state was read is intentionally not part of its identity.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { s.HuellaEsquema, s.Migraciones, s.Entidades, s.Archivos }, CheckpointStorage.Json);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private static string SourceHash(ApplicationSnapshot s) => Convert.ToHexString(SHA256.HashData(CheckpointStorage.Encode(s)));

    private async Task Compatible(ApplicationSnapshot s, CancellationToken ct)
    {
        model.Validate(s);
        if (s.HuellaEsquema != await model.FingerprintAsync(ct) || !s.Migraciones.SequenceEqual(await model.MigrationsAsync(ct)))
            throw new CheckpointException("El modelo, esquema PostgreSQL o historial de migraciones no coincide. No se admite rollback de esquema.", 409);
    }

    public async Task<CheckpointMetadata[]> ListAsync(ClaimsPrincipal caller, CancellationToken ct)
    {
        using var lease = await gate.WriteAsync(ct);
        await RequireAdmin(caller, ct);
        return storage.Ids().Select(id => storage.Metadata(storage.Load(id))).OrderByDescending(x => x.CreadoEn).ToArray();
    }

    public async Task<CheckpointMetadata> CreateAsync(CheckpointCreate request, ClaimsPrincipal caller, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre) || request.Nombre.Length > 200 || request.Descripcion?.Length > 2000)
            throw new CheckpointException("Nombre obligatorio (maximo 200) y descripcion de hasta 2000 caracteres.");
        using var lease = await gate.WriteAsync(ct);
        var user = await RequireAdmin(caller, ct);
        await using var tx = await model.BeginLockedAsync(ct);
        var snapshot = await model.CaptureAsync(storage, request.Nombre.Trim(), request.Descripcion, user, false, ct);
        model.Validate(snapshot);
        var result = storage.Save(snapshot);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }

    public async Task<CheckpointMetadata> ImportAsync(byte[] archive, string passphrase, ClaimsPrincipal caller, CancellationToken ct)
    {
        using var lease = await gate.WriteAsync(ct);
        var user = await RequireAdmin(caller, ct);
        var snapshot = CheckpointStorage.Import(archive, passphrase);
        await using var tx = await model.BeginLockedAsync(ct);
        await Compatible(snapshot, ct);
        // A portable archive can be registered multiple times without overwriting a dated point.
        snapshot.Id = Guid.NewGuid();
        snapshot.CreadoPor = user;
        snapshot.Automatico = false;
        var result = storage.Save(snapshot);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }

    public async Task<byte[]> ExportAsync(Guid id, string passphrase, ClaimsPrincipal caller, CancellationToken ct)
    {
        using var lease = await gate.WriteAsync(ct);
        await RequireAdmin(caller, ct);
        return CheckpointStorage.Export(storage.Load(id), passphrase);
    }

    public async Task<CheckpointPreview> PreviewAsync(Guid id, ClaimsPrincipal caller, CancellationToken ct)
    {
        using var lease = await gate.WriteAsync(ct);
        var user = await RequireAdmin(caller, ct);
        var source = storage.Load(id);
        await using var tx = await model.BeginLockedAsync(ct);
        var errors = new List<string>();
        try { await Compatible(source, ct); }
        catch (CheckpointException ex) { errors.Add(ex.Message); }
        if (!model.PostgreSql) errors.Add("Restauracion disponible exclusivamente en PostgreSQL.");
        var current = await model.CaptureAsync(storage, "preview", null, user, false, ct);
        var token = errors.Count == 0 ? previews.Add(new(id, user, epoch.Current, Hash(current), SourceHash(source), DateTimeOffset.UtcNow.AddMinutes(5))) : "";
        if (tx is not null) await tx.CommitAsync(ct);
        return new(id, source.Nombre, source.CreadoEn, errors.Count == 0,
             current.Entidades.Select(x => new CheckpointCounts(x.Key, x.Value.Count,
                 x.Key == typeof(Token).FullName || x.Key == typeof(PasswordResetCode).FullName
                     ? 0 : source.Entidades.GetValueOrDefault(x.Key)?.Count ?? 0)).ToArray(),
            current.Archivos.Count, source.Archivos.Count,
            ["Se reemplazaran TODOS los datos y uploads, incluidos usuarios y credenciales. Se requiere reingreso con credenciales del snapshot.",
             "Los tokens y codigos de recuperacion historicos no se reactivaran. La vista previa vence en 5 minutos."], errors.ToArray(), token);
    }

    public async Task<CheckpointRestored> RestoreAsync(Guid id, CheckpointRestore request, ClaimsPrincipal caller, CancellationToken ct)
    {
        if (request.Confirmacion != "RESTAURAR TODO" || (!request.CrearRespaldo && !request.AceptarSinRespaldo))
            throw new CheckpointException("Confirme RESTAURAR TODO y acepte explicitamente si decide no crear respaldo previo.");
        using var lease = await gate.WriteAsync(ct);
        var user = await RequireAdmin(caller, ct);
        if (!model.PostgreSql) throw new CheckpointException("La restauracion completa requiere PostgreSQL.", 409);
        var binding = previews.Take(request.Huella);
        if (binding is null || binding.Id != id || binding.Caller != user || binding.Epoch != epoch.Current || binding.Expires < DateTimeOffset.UtcNow)
            throw new CheckpointException("Vista previa vencida o invalida. Solicite una nueva.", 409);
        var source = storage.Load(id);
        var operation = Guid.NewGuid();
        Guid? backupId = null;
        var tx = await model.BeginLockedAsync(ct);
        try
        {
            await Compatible(source, ct);
            var values = model.Validate(source);
            var current = await model.CaptureAsync(storage, "Respaldo previo " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
                "Creado automaticamente antes de restaurar un checkpoint completo.", user, true, ct);
            if (binding.Target != Hash(current) || binding.Source != SourceHash(source))
                throw new CheckpointException("Los datos, archivos o checkpoint cambiaron. Solicite una nueva vista previa.", 409);
            if (request.CrearRespaldo)
            {
                model.Validate(current);
                backupId = storage.Save(current).Id; // Any backup failure aborts before files or database are changed.
            }
            recovery.StageFiles(operation, source);
            gate.Healthy = false;
            recovery.Swap(operation);
            await model.ReplaceRowsAsync(values, operation, ct);
            // Do not cancel after the commit decision: recovery resolves an ambiguous commit from its DB marker.
            await tx!.CommitAsync(CancellationToken.None);
            await tx.DisposeAsync();
            tx = null;
            await recovery.RecoverAsync(CancellationToken.None);
            return new(true, backupId, true, "Restauracion completa. Ingrese nuevamente con credenciales del checkpoint.");
        }
        catch
        {
            if (tx is not null)
            {
                try { await tx.RollbackAsync(CancellationToken.None); }
                finally { await tx.DisposeAsync(); tx = null; }
            }
            // On any uncertainty leave Healthy=false and preserve the journal. Startup must recover before serving.
            if (!gate.Healthy) await recovery.RecoverAsync(CancellationToken.None);
            recovery.RemoveUnjournaledStage(operation);
            throw;
        }
        finally { if (tx is not null) await tx.DisposeAsync(); }
    }
}
