using System.Data;
using System.Text.RegularExpressions;
using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CENS15_V2.Services;

public interface IDatabaseSchemaProbe
{
    Task<bool> IsCompatibleAsync(CancellationToken ct = default);
}

public sealed class DatabaseSchemaProbe(AppDbContext db) : IDatabaseSchemaProbe
{
    // Only catalog metadata is read, never application rows or migration history.
    private const string MetadataSql = """
        SELECT n.nspname, c.relname, a.attname,
               pg_catalog.format_type(a.atttypid, a.atttypmod)
        FROM pg_catalog.pg_attribute AS a
        JOIN pg_catalog.pg_class AS c ON c.oid = a.attrelid
        JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
        WHERE a.attnum > 0 AND NOT a.attisdropped
          AND c.relkind IN ('r', 'p')
          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
          AND pg_catalog.has_table_privilege(c.oid, 'SELECT')
        """;

    public async Task<bool> IsCompatibleAsync(CancellationToken ct = default)
    {
        var logger = db.GetService<ILoggerFactory>().CreateLogger<DatabaseSchemaProbe>();
        if (!db.Database.IsRelational() || db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            logger.LogWarning("Schema probe requires the PostgreSQL relational provider.");
            return false;
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        try
        {
            if (openedHere) await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = MetadataSql;
            var columns = new Dictionary<(string Schema, string Table, string Column), string>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    columns[(reader.GetString(0), reader.GetString(1), reader.GetString(2))] = reader.GetString(3);
            }

            var compatible = MetadataMatches(db.Model, columns);
            if (!compatible)
                logger.LogWarning("Schema incompatible: required model/checkpoint columns or store types are missing, incompatible, or tables lack SELECT permission.");
            return compatible;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    internal static bool MetadataMatches(IModel model, IReadOnlyDictionary<(string Schema, string Table, string Column), string> columns)
    {
        foreach (var table in model.GetRelationalModel().Tables)
        {
            foreach (var column in table.Columns)
            {
                if (!columns.TryGetValue((table.Schema ?? "public", table.Name, column.Name), out var actual)
                    || NormalizeType(actual) != NormalizeType(column.StoreType))
                    return false;
            }
        }

        return columns.TryGetValue(("public", "__CensCheckpointCommits", "Id"), out var markerType)
            && NormalizeType(markerType) == "uuid";
    }

    private static string NormalizeType(string storeType)
    {
        var type = Regex.Replace(storeType.ToLowerInvariant().Trim(), @"\s*\([^)]*\)", "");
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return NormalizeType(type[..^2]) + "[]";
        return type switch
        {
            "varchar" => "character varying",
            "char" or "bpchar" => "character",
            "int" or "int4" => "integer",
            "int2" => "smallint",
            "int8" => "bigint",
            "bool" => "boolean",
            "decimal" => "numeric",
            "float4" => "real",
            "float8" => "double precision",
            "timestamptz" => "timestamp with time zone",
            "timestamp" => "timestamp without time zone",
            "timetz" => "time with time zone",
            "time" => "time without time zone",
            _ => type
        };
    }
}
