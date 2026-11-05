using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Security;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services;

// The journal is independent of the restored EF domain. The commit row is written in the restore transaction.
public sealed class CheckpointRecovery(AppDbContext db, CheckpointStorage storage, SessionEpoch epoch, CheckpointGate gate)
{
    private string Journal => Path.Combine(storage.Root, "restore.journal");
    public string Stage(Guid operation) => Path.Combine(storage.Root, operation.ToString("D") + ".new");
    private string Old(Guid operation) => Path.Combine(storage.Root, operation.ToString("D") + ".old");

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        gate.Healthy = false;
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new InvalidOperationException("La recuperacion de checkpoints requiere PostgreSQL.");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS public.\"__CensCheckpointCommits\" (\"Id\" uuid PRIMARY KEY)", ct);
        await RecoverAsync(ct);
        gate.Healthy = true;
    }

    public async Task<bool> HasRestoredAsync(CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM public.\"__CensCheckpointCommits\")";
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public void StageFiles(Guid operation, ApplicationSnapshot snapshot)
    {
        var stage = Stage(operation);
        CheckpointStorage.EnsureNoLinks(stage);
        if (Directory.Exists(stage) || Directory.Exists(Old(operation)) || File.Exists(Journal))
            throw new CheckpointException("Existe una recuperacion pendiente.", 409);
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var (relative, content) in snapshot.Archivos)
            {
                var path = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                CheckpointStorage.AtomicWrite(path, content);
            }
        }
        catch { Directory.Delete(stage, true); throw; }
    }

    public void Swap(Guid operation)
    {
        CheckpointStorage.EnsureNoLinks(storage.Uploads);
        // Never perform the first rename unless the journal is durable.
        CheckpointStorage.AtomicWrite(Journal, JsonSerializer.SerializeToUtf8Bytes(operation));
        Directory.Move(storage.Uploads, Old(operation));
        Directory.Move(Stage(operation), storage.Uploads);
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        CheckpointStorage.EnsureNoLinks(Journal);
        if (!File.Exists(Journal)) return;
        gate.Healthy = false;
        Guid operation;
        try { operation = JsonSerializer.Deserialize<Guid>(File.ReadAllBytes(Journal)); }
        catch { throw new InvalidOperationException("Diario de restauracion invalido. Se requiere intervencion del operador."); }
        if (operation == Guid.Empty) throw new InvalidOperationException("Diario de restauracion invalido.");
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM public.\"__CensCheckpointCommits\" WHERE \"Id\"=@id)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = operation;
        command.Parameters.Add(parameter);
        var committed = (bool)(await command.ExecuteScalarAsync(ct))!;
        var old = Old(operation);
        var stage = Stage(operation);
        CheckpointStorage.EnsureNoLinks(old);
        CheckpointStorage.EnsureNoLinks(stage);
        CheckpointStorage.EnsureNoLinks(storage.Uploads);
        if (committed)
        {
            if (!Directory.Exists(storage.Uploads) || Directory.Exists(stage))
                throw new InvalidOperationException("Restauracion confirmada sin uploads definitivos. No se habilitara la aplicacion.");
            epoch.Rotate();
        }
        else if (Directory.Exists(old))
        {
            if (Directory.Exists(storage.Uploads)) Directory.Delete(storage.Uploads, true);
            Directory.Move(old, storage.Uploads);
        }
        else if (!Directory.Exists(storage.Uploads))
            throw new InvalidOperationException("No se pueden recuperar los uploads originales.");
        // Delete the journal before old-file cleanup. A crash during cleanup must not trigger a second rollback.
        File.Delete(Journal);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        if (Directory.Exists(old)) Directory.Delete(old, true);
        gate.Healthy = true;
    }

    public void RemoveUnjournaledStage(Guid operation)
    {
        if (!File.Exists(Journal) && Directory.Exists(Stage(operation))) Directory.Delete(Stage(operation), true);
    }
}
