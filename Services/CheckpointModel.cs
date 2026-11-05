using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace CENS15_V2.Services;

public sealed class CheckpointModel(AppDbContext db)
{
    public IEntityType[] Entities => db.Model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    public static string Table(IEntityType e) => Quote(e.GetSchema() ?? "public") + "." + Quote(e.GetTableName()!);
    public bool PostgreSql => db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    public async Task<IDbContextTransaction?> BeginLockedAsync(CancellationToken ct)
    {
        if (!PostgreSql) return null;
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // No CASCADE: external FK dependants must cause a safe refusal, not silent deletion.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE " + string.Join(",", Entities.Select(Table).Distinct().Order()) + " IN ACCESS EXCLUSIVE MODE", ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }

    public async Task<string[]> MigrationsAsync(CancellationToken ct) => db.Database.IsRelational()
        ? (await db.Database.GetAppliedMigrationsAsync(ct)).Order(StringComparer.Ordinal).ToArray() : [];

    public async Task<string> FingerprintAsync(CancellationToken ct)
    {
        var text = new StringBuilder();
        foreach (var e in Entities)
        {
            if (e.IsOwned() || e.HasSharedClrType || e.GetTableName() is null || e.FindPrimaryKey() is null)
                throw new CheckpointException("El modelo contiene una entidad no admitida para snapshots.", 409);
            var store = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
            text.AppendLine(e.Name + "|" + Table(e));
            foreach (var p in e.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
                text.AppendLine($"{p.Name}|{p.ClrType.FullName}|{p.IsNullable}|{p.GetColumnName(store)}|{(db.Database.IsRelational() ? p.GetColumnType() : "")}|{p.GetMaxLength()}|{p.GetPrecision()}|{p.GetScale()}|{p.ValueGenerated}|{p.GetDefaultValueSql()}|{p.GetComputedColumnSql()}|{p.GetTypeMapping().Converter?.ProviderClrType.FullName}");
            foreach (var k in e.GetKeys().OrderBy(k => string.Join(",", k.Properties.Select(p => p.Name))))
                text.AppendLine("KEY|" + string.Join(",", k.Properties.Select(p => p.Name)));
            foreach (var i in e.GetIndexes().OrderBy(i => string.Join(",", i.Properties.Select(p => p.Name))))
                text.AppendLine("INDEX|" + i.IsUnique + "|" + string.Join(",", i.Properties.Select(p => p.Name)) + "|" + i.GetFilter());
            foreach (var fk in e.GetForeignKeys().OrderBy(fk => string.Join(",", fk.Properties.Select(p => p.Name))))
                text.AppendLine("FK|" + string.Join(",", fk.Properties.Select(p => p.Name)) + "|" + fk.PrincipalEntityType.Name + "|" + string.Join(",", fk.PrincipalKey.Properties.Select(p => p.Name)) + "|" + fk.DeleteBehavior);
            if (PostgreSql)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = @"SELECT value FROM (
                    SELECT 'C|' || a.attname || '|' || pg_catalog.format_type(a.atttypid,a.atttypmod) || '|' || a.attnotnull || '|' || a.attidentity::text || '|' || COALESCE(pg_get_expr(d.adbin,d.adrelid),'') AS value
                    FROM pg_attribute a LEFT JOIN pg_attrdef d ON a.attrelid=d.adrelid AND a.attnum=d.adnum
                    WHERE a.attrelid=to_regclass(@t) AND a.attnum>0 AND NOT a.attisdropped
                    UNION ALL SELECT 'K|' || conname || '|' || pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid=to_regclass(@t)
                    UNION ALL SELECT 'I|' || indexname || '|' || indexdef FROM pg_indexes WHERE schemaname=@s AND tablename=@n
                    ) q ORDER BY value";
                AddParameter(command, "t", Table(e));
                AddParameter(command, "s", e.GetSchema() ?? "public");
                AddParameter(command, "n", e.GetTableName()!);
                if (command.Connection!.State != ConnectionState.Open) await command.Connection.OpenAsync(ct);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) text.AppendLine(reader.GetString(0));
            }
        }
        foreach (var m in await MigrationsAsync(ct)) text.AppendLine("MIGRATION|" + m);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static async Task<List<object>> RowsAsync<T>(AppDbContext context, CancellationToken ct) where T : class =>
        (await context.Set<T>().IgnoreQueryFilters().AsTracking().ToListAsync(ct)).Cast<object>().ToList();

    public async Task<ApplicationSnapshot> CaptureAsync(CheckpointStorage storage, string name, string? description, string caller, bool automatic, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var result = new ApplicationSnapshot
        {
            Id = Guid.NewGuid(), Nombre = name, Descripcion = description, CreadoPor = caller,
            CreadoEn = DateTimeOffset.UtcNow, Automatico = automatic,
            HuellaEsquema = await FingerprintAsync(ct), Migraciones = await MigrationsAsync(ct)
        };
        foreach (var e in Entities)
        {
            var method = typeof(CheckpointModel).GetMethod(nameof(RowsAsync), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(e.ClrType);
            var rows = await (Task<List<object>>)method.Invoke(null, [db, ct])!;
            var scalars = new List<SortedDictionary<string, JsonElement>>();
            foreach (var row in rows)
            {
                var values = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var p in e.GetProperties())
                    values.Add(p.Name, JsonSerializer.SerializeToElement(db.Entry(row).Property(p.Name).CurrentValue, p.ClrType, CheckpointStorage.Json));
                scalars.Add(values);
            }
            var pk = e.FindPrimaryKey()!;
            scalars.Sort((a, b) => StringComparer.Ordinal.Compare(Key(pk.Properties, a), Key(pk.Properties, b)));
            result.Entidades.Add(e.Name, scalars);
        }
        result.Archivos = storage.CaptureFiles();
        // The same representation is used for preview bindings, including nulls, IDs and soft-deleted rows.
        _ = CheckpointStorage.Encode(result);
        return result;
    }

    private static string Key(IReadOnlyList<IProperty> props, SortedDictionary<string, JsonElement> row) =>
        JsonSerializer.Serialize(props.Select(p => row[p.Name]).ToArray());

    public Dictionary<string, List<Dictionary<IProperty, object?>>> Validate(ApplicationSnapshot snapshot)
    {
        if (snapshot.Version != 1 || snapshot.Id == Guid.Empty || snapshot.CreadoEn == default || string.IsNullOrWhiteSpace(snapshot.Nombre) ||
            snapshot.Nombre.Length > 200 || snapshot.Descripcion?.Length > 2000 || snapshot.CreadoPor is null || snapshot.CreadoPor.Length > 200 ||
            string.IsNullOrWhiteSpace(snapshot.HuellaEsquema) ||
            snapshot.Entidades is null || snapshot.Archivos is null || snapshot.Migraciones is null ||
            !Entities.Select(e => e.Name).SequenceEqual(snapshot.Entidades.Keys.Order(StringComparer.Ordinal)))
            throw new CheckpointException("Snapshot incompleto o incompatible con el modelo.");
        if (snapshot.Entidades.Values.Any(r => r is null) || snapshot.Migraciones.Any(m => string.IsNullOrWhiteSpace(m)))
            throw new CheckpointException("Listas de snapshot invalidas.");
        if (snapshot.Entidades.Values.Sum(r => (long)r.Count) > 1_000_000 || snapshot.Archivos.Count > 50_000)
            throw new CheckpointException("Snapshot excede los limites de filas/archivos.");

        var values = new Dictionary<string, List<Dictionary<IProperty, object?>>>();
        foreach (var e in Entities)
        {
            var props = e.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            var list = new List<Dictionary<IProperty, object?>>();
            foreach (var row in snapshot.Entidades[e.Name])
            {
                if (row is null || !props.Select(p => p.Name).SequenceEqual(row.Keys.Order(StringComparer.Ordinal)))
                    throw new CheckpointException("Campos de entidad incompletos o desconocidos: " + e.ClrType.Name);
                var converted = new Dictionary<IProperty, object?>();
                foreach (var p in props)
                {
                    object? value;
                    try
                    {
                        if (row[p.Name].ValueKind == JsonValueKind.Null)
                        {
                            if (!p.IsNullable) throw new JsonException();
                            value = null;
                        }
                        else value = row[p.Name].Deserialize(p.ClrType, CheckpointStorage.Json);
                        if (value is string s && p.GetMaxLength() is int max && s.Length > max) throw new JsonException();
                        if (value is DateTime dt && dt.Kind != DateTimeKind.Utc && db.Database.IsRelational() && p.GetColumnType()?.Contains("with time zone") == true) throw new JsonException();
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException or OverflowException or ArgumentException)
                    { throw new CheckpointException("Valor escalar invalido: " + e.ClrType.Name + "." + p.Name); }
                    converted.Add(p, value);
                }
                list.Add(converted);
            }
            values.Add(e.Name, list);
            foreach (var unique in e.GetKeys().Select(k => k.Properties).Concat(e.GetIndexes().Where(i => i.IsUnique).Select(i => i.Properties)))
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in snapshot.Entidades[e.Name])
                {
                    if (unique.Any(p => row[p.Name].ValueKind == JsonValueKind.Null)) continue;
                    if (!keys.Add(Key(unique, row))) throw new CheckpointException("Clave duplicada en " + e.ClrType.Name);
                }
            }
        }
        foreach (var e in Entities)
            foreach (var fk in e.GetForeignKeys())
            {
                var parentKeys = snapshot.Entidades[fk.PrincipalEntityType.Name].Select(r => Key(fk.PrincipalKey.Properties, r)).ToHashSet(StringComparer.Ordinal);
                foreach (var row in snapshot.Entidades[e.Name])
                    if (!fk.Properties.Any(p => row[p.Name].ValueKind == JsonValueKind.Null) && !parentKeys.Contains(Key(fk.Properties, row)))
                        throw new CheckpointException("Relacion inexistente en " + e.ClrType.Name);
            }
        ValidateAdmin(snapshot);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var (path, bytes) in snapshot.Archivos)
        {
            CheckpointStorage.ValidateFileName(path);
            if (bytes is null || !paths.Add(path)) throw new CheckpointException("Archivo duplicado o invalido.");
            size += bytes.Length;
        }
        foreach (var path in paths)
        {
            var parent = path;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (paths.Contains(parent)) throw new CheckpointException("Colision entre archivo y directorio.");
            }
        }
        if (size > CheckpointStorage.MaximumBytes * 3L / 4) throw new CheckpointException("Uploads excede el limite.");
        _ = InsertionOrder();
        return values;
    }

    private void ValidateAdmin(ApplicationSnapshot snapshot)
    {
        var roles = snapshot.Entidades[db.Model.FindEntityType(typeof(Role))!.Name];
        var users = snapshot.Entidades[db.Model.FindEntityType(typeof(CENS15_V2.Entities.User))!.Name];
        var auths = snapshot.Entidades[db.Model.FindEntityType(typeof(Auth))!.Name];
        var adminIds = roles.Where(r => r["Name"].GetString() == "Admin").Select(r => r["Id"].GetGuid()).ToHashSet();
        var userIds = users.Where(u => u["Status"].GetBoolean() && adminIds.Contains(u["RoleId"].GetGuid())).Select(u => u["Id"].GetGuid()).ToHashSet();
        foreach (var auth in auths.Where(a => userIds.Contains(a["Id"].GetGuid())))
        {
            if (string.IsNullOrWhiteSpace(auth["Email"].GetString())) continue;
            try
            {
                var hash = auth["PasswordHash"].GetString();
                if (hash is null || !System.Text.RegularExpressions.Regex.IsMatch(hash,
                        @"\A\$2[aby]\$(0[4-9]|1[0-6])\$[./A-Za-z0-9]{53}\z")) continue;
                // Parse without running attacker-controlled, potentially enormous bcrypt work factors.
                _ = BCrypt.Net.BCrypt.InterrogateHash(hash);
                return;
            }
            catch { /* An unusable hash must not strand the application without an administrator. */ }
        }
        throw new CheckpointException("El snapshot no contiene un Admin activo con credenciales utilizables.");
    }

    private IEntityType[] InsertionOrder()
    {
        var pending = Entities.ToList();
        var result = new List<IEntityType>();
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(e => e.GetForeignKeys().All(fk => result.Contains(fk.PrincipalEntityType)));
            if (next is null) throw new CheckpointException("El modelo contiene relaciones ciclicas no admitidas.", 409);
            result.Add(next);
            pending.Remove(next);
        }
        return result.ToArray();
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    public async Task ReplaceRowsAsync(Dictionary<string, List<Dictionary<IProperty, object?>>> values, Guid operation, CancellationToken ct)
    {
        if (!PostgreSql || db.Database.CurrentTransaction is null)
            throw new CheckpointException("La restauracion completa requiere PostgreSQL y una transaccion exclusiva.", 409);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE " + string.Join(",", Entities.Select(Table).Distinct()), ct);
        foreach (var e in InsertionOrder())
        {
            // Historical codes are captured for completeness but are never reactivated.
            if (e.ClrType == typeof(Token) || e.ClrType == typeof(PasswordResetCode)) continue;
            var store = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
            var props = e.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            if (props.Any(p => p.GetComputedColumnSql() is not null)) throw new CheckpointException("Columnas calculadas no admitidas.", 409);
            foreach (var row in values[e.Name])
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
                command.CommandText = "INSERT INTO " + Table(e) + " (" + string.Join(",", props.Select(p => Quote(p.GetColumnName(store)!))) +
                    ") OVERRIDING SYSTEM VALUE VALUES (" + string.Join(",", props.Select((_, i) => "@p" + i)) + ")";
                for (var i = 0; i < props.Length; i++)
                {
                    var value = row[props[i]];
                    var converter = props[i].GetTypeMapping().Converter;
                    AddParameter(command, "p" + i, converter is null ? value : converter.ConvertToProvider(value));
                }
                await command.ExecuteNonQueryAsync(ct);
            }
            foreach (var p in props.Where(p => p.ValueGenerated == ValueGenerated.OnAdd && (p.ClrType == typeof(int) || p.ClrType == typeof(long))))
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
                var col = p.GetColumnName(store)!;
                command.CommandText = "SELECT n.nspname, s.relname FROM pg_class s JOIN pg_namespace n ON n.oid=s.relnamespace WHERE s.oid=pg_get_serial_sequence(@t,@c)::regclass";
                AddParameter(command, "t", Table(e));
                AddParameter(command, "c", col);
                string? sequence = null;
                await using (var reader = await command.ExecuteReaderAsync(ct))
                    if (await reader.ReadAsync(ct)) sequence = Quote(reader.GetString(0)) + "." + Quote(reader.GetString(1));
                if (sequence is null) continue;
                command.Parameters.Clear();
                command.CommandText = "SELECT MAX(" + Quote(col) + ") FROM " + Table(e);
                var max = await command.ExecuteScalarAsync(ct);
                var next = max is null or DBNull ? 1L : checked(Convert.ToInt64(max) + 1L);
                // Unlike setval(), ALTER SEQUENCE RESTART is rolled back with a failed restore.
                command.CommandText = "ALTER SEQUENCE " + sequence + " RESTART WITH " + Math.Max(next, 1L).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.\"__CensCheckpointCommits\" (\"Id\") VALUES ({operation})", ct);
        db.ChangeTracker.Clear();
    }
}
