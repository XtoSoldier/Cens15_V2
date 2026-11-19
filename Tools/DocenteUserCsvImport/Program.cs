using System.Data;
using System.Text;
using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DocenteUserCsvImport;

public static class ImportCli
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string Fingerprint(object value) => CsvImport.Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));

    public static async Task<int> Main(string[] args)
    {
        try { return args.FirstOrDefault() == "--link-docentes" ? await DocenteUserLinkCli.Run(args[1..]) : await Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = "Import not completed. Run a fresh read-only preview before retrying.", category = ex.GetType().Name }));
            return 1;
        }
    }

    public static NpgsqlConnectionStringBuilder LocalTarget(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        if (target.Host is not ("localhost" or "127.0.0.1" or "::1") || string.IsNullOrWhiteSpace(target.Database))
            throw new InvalidOperationException();
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
            if (args[i] is not ("--file" or "--api-root" or "--expected-sha256" or "--expected-database" or "--expected-receipt") ||
                i + 1 >= args.Length || !options.TryAdd(args[i], args[++i])) throw new ArgumentException();
        }
        if (!options.TryGetValue("--file", out var file) ||
            (apply && new[] { "--expected-sha256", "--expected-database", "--expected-receipt" }.Any(k => !options.ContainsKey(k))))
            throw new ArgumentException();
        var source = CsvImport.Read(await File.ReadAllBytesAsync(file));
        var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath(options.GetValueOrDefault("--api-root", Directory.GetCurrentDirectory())))
            .AddJsonFile("appsettings.json", optional: false).AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables().Build();
        var target = LocalTarget(config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException());
        if (apply && (!string.Equals(source.Sha256, options["--expected-sha256"], StringComparison.OrdinalIgnoreCase) ||
            target.Database != options["--expected-database"])) throw new InvalidOperationException();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(target.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (apply) await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Users\", \"Auths\", \"Roles\" IN SHARE ROW EXCLUSIVE MODE");
        else await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");

        var users = await db.Users.IgnoreQueryFilters().Select(u => new ExistingUser(u.Id, u.RoleId)).ToListAsync();
        var auths = await db.Auths.IgnoreQueryFilters().Select(a => new ExistingAuth(a.Id, a.Email)).ToListAsync();
        var roles = await db.Roles.Select(r => new ExistingRole(r.Id, r.Name)).ToListAsync();
        var plan = CsvImport.Plan(source, users, auths, roles);
        var beforeState = await Snapshot(db);
        var before = await Counts(db, apply);
        var summary = new
        {
            sourceSha256 = source.Sha256, totalRows = source.TotalRows, uniqueEmails = source.Rows.Count,
            repeatedRows = source.Duplicates.Sum(d => d.Lines.Length - 1), source.Errors, source.Duplicates, source.Warnings,
            target = new { host = target.Host, port = target.Port, database = target.Database },
            matchedExisting = plan.MatchedLines.Count, matchedLines = plan.MatchedLines, newCount = plan.Additions.Count,
            plan.WillCreateRole, plan.Conflicts, currentCounts = before, stateFingerprint = beforeState,
            canApply = source.Errors.Count == 0 && plan.Conflicts.Count == 0
        };
        var receipt = Fingerprint(new { version = 1, summary });
        Console.WriteLine(JsonSerializer.Serialize(new { mode = apply ? "apply-validation" : "preview", summary, receipt }, JsonOptions));
        if (!apply || !summary.canApply)
        {
            await tx.RollbackAsync();
            return summary.canApply ? 0 : 2;
        }
        if (!string.Equals(receipt, options["--expected-receipt"], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        var roleId = plan.RoleId ?? Guid.NewGuid();
        if (plan.WillCreateRole) db.Roles.Add(new Role { Id = roleId, Name = "Docente" });
        var hasher = new PasswordHasher(config);
        var additions = plan.Additions.Select(r => CsvImport.Map(r, roleId, hasher)).ToList();
        db.Users.AddRange(additions);
        if (db.ChangeTracker.Entries().Any(e => e.State != EntityState.Added ||
            e.Entity is not (CENS15_V2.Entities.User or Auth or Role))) throw new InvalidOperationException();
        await db.SaveChangesAsync();
        var ids = additions.Select(u => u.Id).ToArray();
        var after = await Counts(db, false);
        foreach (var (table, count) in before)
        {
            var expected = count + (table == "Users" || table == "Auths" ? additions.Count : table == "Roles" && plan.WillCreateRole ? 1 : 0);
            if (after[table] != expected) throw new InvalidOperationException();
        }
        if (beforeState != await Snapshot(db, ids, plan.WillCreateRole ? roleId : null)) throw new InvalidOperationException();
        var savedUsers = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Status, u.RoleId }).ToListAsync();
        var savedAuths = await db.Auths.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync();
        foreach (var addition in additions)
        {
            var u = savedUsers.Single(u => u.Id == addition.Id);
            var a = savedAuths.Single(a => a.Id == addition.Id);
            if (!u.Status || u.RoleId != roleId || u.FirstName != addition.FirstName || u.LastName != addition.LastName ||
                a.Email != addition.Auth.Email || a.MustChangePassword || a.InitialAccessSentAt != null ||
                !hasher.Verify(a.Email[..a.Email.IndexOf('@')], a.PasswordHash)) throw new InvalidOperationException();
        }
        var savedRole = await db.Roles.SingleAsync(r => r.Id == roleId);
        if (!string.Equals(savedRole.Name.Trim(), "Docente", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        await tx.CommitAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "applied", insertedCount = additions.Count,
            createdRole = plan.WillCreateRole, countsBefore = before, countsAfter = after,
            preexistingUnchanged = true, credentialsVerified = true }, JsonOptions));
        return 0;
    }

    private static async Task<string> Snapshot(AppDbContext db, Guid[]? excludedUsers = null, Guid? excludedRole = null)
    {
        excludedUsers ??= [];
        // Scalar projections deliberately avoid pending navigation/table changes (for example Preceptores).
        var users = await db.Users.IgnoreQueryFilters().Where(u => !excludedUsers.Contains(u.Id)).OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Image, u.Status, u.RoleId }).ToListAsync();
        var auths = await db.Auths.IgnoreQueryFilters().Where(a => !excludedUsers.Contains(a.Id)).OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Email, a.PasswordHash, a.MustChangePassword, a.InitialAccessSentAt }).ToListAsync();
        var roles = await db.Roles.Where(r => !excludedRole.HasValue || r.Id != excludedRole.Value).OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Name }).ToListAsync();
        return Fingerprint(new { users, auths, roles });
    }

    internal static async Task<SortedDictionary<string, long>> Counts(AppDbContext db, bool lockTables)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        await using var tablesCommand = connection.CreateCommand();
        tablesCommand.Transaction = transaction;
        tablesCommand.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name";
        var tables = new List<string>();
        await using (var reader = await tablesCommand.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var quoted = "public.\"" + table.Replace("\"", "\"\"") + "\"";
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (lockTables && table is not ("Users" or "Auths" or "Roles"))
            {
                command.CommandText = "LOCK TABLE " + quoted + " IN SHARE MODE";
                await command.ExecuteNonQueryAsync();
            }
            command.CommandText = "SELECT COUNT(*) FROM " + quoted;
            counts.Add(table, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        return counts;
    }
}
