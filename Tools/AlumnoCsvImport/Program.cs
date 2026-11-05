using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AlumnoCsvImport;

public static class ImportCli
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try { return await Run(args); }
        catch (Exception ex)
        {
            // Provider, parser and filesystem exception messages may contain credentials or source PII.
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = "Import did not complete normally. Run a read-only preview to verify the database state before retrying.", category = ex.GetType().Name }));
            return 1;
        }
    }

    public static NpgsqlConnectionStringBuilder LocalTarget(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        if (target.Host is not ("localhost" or "127.0.0.1" or "::1") || string.IsNullOrWhiteSpace(target.Database))
            throw new InvalidOperationException("Only a named database on an explicit loopback host is supported.");
        // Do not allow provider diagnostics to include parameter values.
        target.IncludeErrorDetail = false;
        target.LogParameters = false;
        return target;
    }

    private static async Task<int> Run(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var apply = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply") { if (apply) throw new ArgumentException(); apply = true; continue; }
            if (args[i] is not ("--file" or "--api-root" or "--environment" or "--expected-sha256" or "--expected-database" or "--expected-receipt")
                || i + 1 >= args.Length || !options.TryAdd(args[i], args[++i])) throw new ArgumentException("Invalid options.");
        }
        if (!options.TryGetValue("--file", out var file) || options.GetValueOrDefault("--environment", "Development") != "Development")
            throw new ArgumentException("Require --file and Development environment.");
        if (apply && new[] { "--expected-sha256", "--expected-database", "--expected-receipt" }.Any(k => !options.ContainsKey(k)))
            throw new ArgumentException("Apply requires preview confirmation.");

        var csv = LegacyCsvReader.Read(await File.ReadAllBytesAsync(file));
        var root = Path.GetFullPath(options.GetValueOrDefault("--api-root", Directory.GetCurrentDirectory()));
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true).AddEnvironmentVariables().Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing connection configuration.");
        var target = LocalTarget(connectionString);
        if (apply && (!string.Equals(csv.Sha256, options["--expected-sha256"], StringComparison.OrdinalIgnoreCase)
            || target.Database != options["--expected-database"])) throw new InvalidOperationException("Preview confirmation mismatch.");

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(target.ConnectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (apply)
        {
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Alumnos\", \"AlumnoContactos\", \"AlumnoNacimientos\", \"AlumnoDocumentos\" IN SHARE ROW EXCLUSIVE MODE");
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Inscripciones\", \"CursadasMaterias\", \"Calificaciones\", \"TiposDocumentoAlumno\" IN SHARE MODE");
        }
        else await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");

        var catalogRows = await db.TiposDocumentoAlumno.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        var catalog = catalogRows.ToDictionary(t => t.Nombre, t => t.Id, StringComparer.OrdinalIgnoreCase);
        var existing = await db.Alumnos.IgnoreQueryFilters().AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.NumeroDocumento, a.Nombres, a.Apellidos, a.FechaNacimiento }).ToListAsync();
        var existingKeys = existing.Select(a => LegacyCsvReader.StudentKey(a.NumeroDocumento, a.Nombres, a.Apellidos, a.FechaNacimiento)).ToHashSet(StringComparer.Ordinal);
        var matched = csv.Rows.Where(r => existingKeys.Contains(r.DniKey)).ToList();
        var additions = csv.Rows.Where(r => !existingKeys.Contains(r.DniKey)).ToList();
        var before = await Counts(db);
        var sanitizedTarget = new { host = target.Host, port = target.Port, database = target.Database };
        var fingerprint = Hash(JsonSerializer.Serialize(sanitizedTarget));
        var docWarnings = Enumerable.Range(0, 4).Select(i => new
        {
            column = LegacyCsvReader.FlagColumns[i], catalogName = LegacyCsvReader.DocumentNames[i],
            catalogMissing = !catalog.ContainsKey(LegacyCsvReader.DocumentNames[i]),
            blankSourceCount = csv.FieldBlankCounts[LegacyCsvReader.FlagColumns[i]],
            omittedMissingCatalogCount = csv.Rows.Count(r => r.Data.Flags[i].HasValue && !catalog.ContainsKey(LegacyCsvReader.DocumentNames[i])),
            presentedNew = additions.Count(r => r.Data.Flags[i] == true && catalog.ContainsKey(LegacyCsvReader.DocumentNames[i])),
            notPresentedNew = additions.Count(r => r.Data.Flags[i] == false && catalog.ContainsKey(LegacyCsvReader.DocumentNames[i]))
        }).ToArray();
        var summary = new
        {
             sourceSha256 = csv.Sha256, totalRead = csv.TotalRead, validUnique = csv.Rows.Count,
             repairedQuotes = csv.RepairedQuotes, missingDocument = csv.Rows.Count(r => r.Dni == null),
             missingBirthDate = csv.Rows.Count(r => r.Data.FechaNacimiento == null), missingGender = csv.Rows.Count(r => r.Data.Genero == null),
            duplicates = csv.Duplicates, fieldBlankCounts = csv.FieldBlankCounts,
            ignoredColumns = LegacyCsvReader.IgnoredColumns, validationErrors = csv.ValidationErrors,
            matchedExisting = matched.Count, newCount = additions.Count, target = sanitizedTarget,
            targetFingerprint = fingerprint, documentMappingWarnings = docWarnings, currentCounts = before,
            canApply = csv.ValidationErrors.Count == 0,
            // Catalog IDs and existing matches bind the receipt without disclosing document numbers.
            stateFingerprint = Hash(JsonSerializer.Serialize(new { catalog = catalogRows.Select(t => new { t.Id, t.Nombre }), existing }))
        };
        var receipt = Hash(JsonSerializer.Serialize(summary));
        Console.WriteLine(JsonSerializer.Serialize(new { mode = apply ? "apply-validation" : "preview", summary, receipt }, JsonOptions));
        if (!apply || csv.ValidationErrors.Count > 0)
        {
            await transaction.RollbackAsync();
            return csv.ValidationErrors.Count > 0 ? 2 : 0;
        }
        if (receipt != options["--expected-receipt"]) throw new InvalidOperationException("Preview state changed; run preview again.");

        var matchedIds = existing.Where(a => matched.Any(r => r.DniKey == LegacyCsvReader.StudentKey(a.NumeroDocumento, a.Nombres, a.Apellidos, a.FechaNacimiento))).Select(a => a.Id).ToArray();
        var matchedBefore = await Snapshot(db, matchedIds);
        var inserted = additions.Select(row => (row.Row, Alumno: LegacyCsvReader.Map(row, catalog))).ToList();
        db.Alumnos.AddRange(inserted.Select(x => x.Alumno));
        if (db.ChangeTracker.Entries().Any(e => e.State != EntityState.Added ||
            e.Entity is not (Alumno or AlumnoContacto or AlumnoNacimiento or AlumnoDocumento)))
            throw new InvalidOperationException("Unexpected tracked entity.");
        await db.SaveChangesAsync();
        var ids = inserted.Select(x => x.Alumno.Id).ToArray();
        var after = await Counts(db);
        if (await db.Inscripciones.IgnoreQueryFilters().AnyAsync(i => ids.Contains(i.AlumnoId))
            || after.Inscripciones != before.Inscripciones || after.Cursadas != before.Cursadas
            || after.Calificaciones != before.Calificaciones || after.Alumnos != before.Alumnos + inserted.Count
            || matchedBefore != await Snapshot(db, matchedIds))
            throw new InvalidOperationException("Post-insert invariants failed.");
        await transaction.CommitAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "applied", insertedCount = inserted.Count,
            generatedIds = inserted.Select(x => new { sourceRow = x.Row, id = x.Alumno.Id }), counts = after }, JsonOptions));
        return 0;
    }

    private record DatabaseCounts(int Alumnos, int Inscripciones, int Cursadas, int Calificaciones);
    private static async Task<DatabaseCounts> Counts(AppDbContext db) => new(
        await db.Alumnos.IgnoreQueryFilters().CountAsync(), await db.Inscripciones.IgnoreQueryFilters().CountAsync(),
        await db.CursadasMaterias.IgnoreQueryFilters().CountAsync(), await db.Calificaciones.IgnoreQueryFilters().CountAsync());

    private static async Task<string> Snapshot(AppDbContext db, int[] ids)
    {
        var alumnos = await db.Alumnos.IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.Id)).OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Nombres, a.Apellidos, a.NumeroDocumento, a.FechaNacimiento, a.Genero,
                a.Domicilio, a.Eliminado, a.FechaEliminacion }).ToListAsync();
        var contacts = await db.AlumnoContactos.IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.AlumnoId)).OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.AlumnoId, a.TelefonoAlumno, a.Email, a.NombreEmergencia, a.TelefonoEmergencia }).ToListAsync();
        var births = await db.AlumnoNacimientos.IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.AlumnoId)).OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.AlumnoId, a.Localidad, a.Provincia, a.Pais }).ToListAsync();
        var docs = await db.AlumnoDocumentos.IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.AlumnoId)).OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.AlumnoId, a.TipoDocumentoAlumnoId, a.Presentado, a.ImagenUrl, a.ImagenesUrl }).ToListAsync();
        return Hash(JsonSerializer.Serialize(new { alumnos, contacts, births, docs }));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
