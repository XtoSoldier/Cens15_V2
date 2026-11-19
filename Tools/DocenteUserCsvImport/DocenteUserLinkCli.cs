using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CENS15_V2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace DocenteUserCsvImport;

public static class DocenteUserLinkCli
{
    private static string Fingerprint(object value) => CsvImport.Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));

    public static async Task<int> Run(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var apply = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply") { if (apply) throw new ArgumentException(); apply = true; continue; }
            if (args[i] is not ("--api-root" or "--expected-database" or "--expected-receipt") ||
                i + 1 >= args.Length || !options.TryAdd(args[i], args[++i])) throw new ArgumentException();
        }
        if (apply && new[] { "--expected-database", "--expected-receipt" }
            .Any(k => !options.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v))) throw new ArgumentException();
        var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath(options.GetValueOrDefault("--api-root", Directory.GetCurrentDirectory())))
            .AddJsonFile("appsettings.json", optional: false).AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables().Build();
        var target = ImportCli.LocalTarget(config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException());
        if (apply && target.Database != options["--expected-database"]) throw new InvalidOperationException();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(target.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (apply) await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Docentes\", \"Users\", \"Auths\", \"Roles\" IN SHARE ROW EXCLUSIVE MODE");
        else await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");

        var docentes = await db.Docentes.IgnoreQueryFilters().Select(d => new LinkDocente(d.Id, d.Nombres, d.Apellidos, d.Email, d.UserId)).ToListAsync();
        var users = await db.Users.IgnoreQueryFilters().Select(u => new LinkUser(u.Id, u.FirstName, u.LastName, u.Status, u.RoleId)).ToListAsync();
        var auths = await db.Auths.IgnoreQueryFilters().Select(a => new LinkAuth(a.Id, a.Email)).ToListAsync();
        var roles = await db.Roles.IgnoreQueryFilters().Select(r => new ExistingRole(r.Id, r.Name)).ToListAsync();
        var plan = DocenteUserLink.Plan(docentes, users, auths, roles);
        var beforeState = await Snapshot(db);
        var before = await ImportCli.Counts(db, apply);
        var summary = new
        {
            target = new { host = target.Host, port = target.Port, database = target.Database },
            plan.Proposals, plan.Skipped, currentCounts = before, stateFingerprint = StateFingerprint(beforeState),
            insertedCount = 0, updatedLinksOnly = plan.Proposals.Count, noApplyWritesNeeded = plan.Proposals.Count == 0
        };
        var receipt = Fingerprint(new { operation = "link-docentes", version = 1, summary });
        if (apply && !string.Equals(receipt, options["--expected-receipt"], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = apply ? "apply-validation" : "preview", summary, receipt },
            new JsonSerializerOptions { WriteIndented = true }));
        if (!apply || plan.Proposals.Count == 0)
        {
            await tx.RollbackAsync();
            return 0;
        }

        foreach (var p in plan.Proposals)
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Docentes\" SET \"UserId\" = {p.UserId} WHERE \"Id\" = {p.DocenteId} AND \"UserId\" IS NULL");
            if (changed != 1) throw new InvalidOperationException();
        }
        var after = await ImportCli.Counts(db, false);
        if (!before.SequenceEqual(after) || StateFingerprint(beforeState, plan.Proposals) != StateFingerprint(await Snapshot(db)))
            throw new InvalidOperationException();
        var saved = await db.Docentes.IgnoreQueryFilters().Select(d => new { d.Id, d.UserId }).ToListAsync();
        if (saved.Where(d => d.UserId.HasValue).GroupBy(d => d.UserId).Any(g => g.Count() > 1) ||
            plan.Proposals.Any(p => saved.Single(d => d.Id == p.DocenteId).UserId != p.UserId)) throw new InvalidOperationException();
        var freshDocentes = docentes.Select(d => plan.Proposals.SingleOrDefault(p => p.DocenteId == d.Id) is { } p
            ? d with { UserId = p.UserId } : d).ToArray();
        if (DocenteUserLink.Plan(freshDocentes, users, auths, roles).Proposals.Count != 0) throw new InvalidOperationException();
        await tx.CommitAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "applied", insertedCount = 0, updatedLinksOnly = plan.Proposals.Count,
            otherScalarStateUnchanged = true, countsUnchanged = true, idempotentFreshPreview = true }));
        return 0;
    }

    // Read every database scalar column, including unmapped columns and PasswordHash, without ever emitting rows.
    private static async Task<SortedDictionary<string, string[]>> Snapshot(AppDbContext db)
    {
        var state = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var table in new[] { "Docentes", "Users", "Auths", "Roles" })
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SELECT to_jsonb(t)::text FROM public.\"" + table + "\" AS t";
            var rows = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
            state.Add(table, rows.ToArray());
        }
        return state;
    }

    public static string StateFingerprint(SortedDictionary<string, string[]> state, IReadOnlyList<LinkProposal>? expectedLinks = null)
    {
        var links = (expectedLinks ?? []).ToDictionary(p => p.DocenteId, p => p.UserId);
        return Fingerprint(state.Select(table => new
        {
            table = table.Key,
            rows = table.Value.Select(row =>
            {
                var json = JsonNode.Parse(row)!.AsObject();
                if (table.Key == "Docentes" && links.TryGetValue(json["Id"]!.GetValue<int>(), out var userId))
                {
                    if (json["UserId"] is not null) throw new InvalidOperationException();
                    json["UserId"] = userId;
                }
                return JsonSerializer.Serialize(json.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => p.Value));
            }).OrderBy(row => row, StringComparer.Ordinal).ToArray()
        }).ToArray());
    }
}
