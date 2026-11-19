using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class StartupReadinessTests
{
    [Fact]
    public void StateStartsUnavailableAndOnlyPublishesWhitelistedReasons()
    {
        var readiness = new StartupReadiness();
        Assert.False(readiness.IsReady);
        Assert.Equal("initializing", readiness.Reason);
        foreach (var reason in new[] { "initializing", "database_unavailable", "schema_incompatible", "storage_unavailable", "recovery_pending", "configuration_unavailable" })
        {
            readiness.MarkReady();
            Assert.True(readiness.IsReady);
            readiness.MarkUnavailable(reason);
            Assert.False(readiness.IsReady);
            Assert.Equal(reason, readiness.Reason);
        }
        readiness.MarkUnavailable("Host=secret;Password=private /data/private.sql");
        Assert.Equal("initializing", readiness.Reason);
    }

    [Fact]
    public void StateSupportsConcurrentUpdates()
    {
        var readiness = new StartupReadiness();
        Parallel.For(0, 10000, i =>
        {
            if (i % 2 == 0) readiness.MarkReady();
            else readiness.MarkUnavailable("database_unavailable");
            Assert.Contains(readiness.Reason, new[] { "initializing", "database_unavailable" });
        });
        readiness.MarkReady();
        Assert.True(readiness.IsReady);
    }

    public static IEnumerable<object[]> ProbeCases()
    {
        foreach (var method in new[] { "GET", "HEAD" })
        foreach (var path in new[] { "/api/health", "/API/HEALTH/", "/api/ready", "/API/READY/" })
        foreach (var ready in new[] { false, true })
        foreach (var healthy in new[] { false, true })
            yield return new object[] { method, path, ready, healthy };
    }

    [Theory]
    [MemberData(nameof(ProbeCases))]
    public async Task ProbesAreAnonymousReadOnlyAndNeverInvokeNext(string method, string path, bool ready, bool healthy)
    {
        var readiness = new StartupReadiness();
        if (ready) readiness.MarkReady();
        else readiness.MarkUnavailable("database_unavailable");
        var gate = new CheckpointGate { Healthy = healthy };
        // Even an exclusive restoration lease must not block diagnostics.
        using var lease = await gate.WriteAsync();
        var context = Context(method, path);
        var middleware = new StartupReadinessMiddleware(_ => throw new InvalidOperationException("Authentication/data pipeline must not run."));
        await middleware.InvokeAsync(context, readiness, gate).WaitAsync(TimeSpan.FromSeconds(5));
        var health = path.Contains("health", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(health || ready && healthy ? 200 : 503, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.StartsWith("application/json", context.Response.ContentType);
        var body = await Body(context);
        if (method == "HEAD") Assert.Empty(body);
        else
        {
            using var json = JsonDocument.Parse(body);
            Assert.Equal(health ? "ok" : ready && healthy ? "ready" : "not_ready", json.RootElement.GetProperty("status").GetString());
            if (!health && !(ready && healthy))
                Assert.Equal(ready ? "recovery_pending" : "database_unavailable", json.RootElement.GetProperty("reason").GetString());
        }
    }

    [Theory]
    [InlineData("GET", "/api/alumnos")]
    [InlineData("POST", "/api/alumnos")]
    [InlineData("DELETE", "/api/checkpoints/1")]
    [InlineData("POST", "/api/health")]
    [InlineData("GET", "/api/health/extra")]
    [InlineData("GET", "/api/ready//")]
    [InlineData("GET", "/swagger-not-really")]
    [InlineData("HEAD", "/api/alumnos")]
    public async Task BusinessNeverRunsWhenEitherStateIsUnavailable(string method, string path)
    {
        foreach (var ready in new[] { false, true })
        foreach (var healthy in new[] { false, true })
        {
            if (ready && healthy) continue;
            var readiness = new StartupReadiness();
            if (ready) readiness.MarkReady();
            else readiness.MarkUnavailable("Password=secret; SELECT * FROM alumnos; /private/file");
            var context = Context(method, path);
            await new StartupReadinessMiddleware(_ => throw new InvalidOperationException("Business/data pipeline must not run."))
                .InvokeAsync(context, readiness, new CheckpointGate { Healthy = healthy });
            Assert.Equal(503, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
            var body = await Body(context);
            if (method == "HEAD") Assert.Empty(body);
            else
            {
                using var json = JsonDocument.Parse(body);
                Assert.Equal("Service temporarily unavailable.", json.RootElement.GetProperty("message").GetString());
                Assert.Equal(ready ? "recovery_pending" : "initializing", json.RootElement.GetProperty("reason").GetString());
            }
        }
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/SWAGGER/")]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/swagger-ui.css")]
    [InlineData("/swagger/swagger-ui-bundle.js")]
    public async Task SwaggerRemainsAvailableWithoutReadiness(string path)
    {
        var calls = 0;
        await new StartupReadinessMiddleware(_ => { calls++; return Task.CompletedTask; })
            .InvokeAsync(Context("GET", path), new StartupReadiness(), new CheckpointGate { Healthy = false });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReadyBusinessPassesThroughAndDoesNotHideUnexpectedFailures()
    {
        var readiness = new StartupReadiness();
        readiness.MarkReady();
        var context = Context("GET", "/api/alumnos");
        var middleware = new StartupReadinessMiddleware(ctx =>
        {
            Assert.Same(context, ctx);
            throw new InvalidOperationException("genuine downstream error");
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, readiness, new CheckpointGate()));
        Assert.Equal("genuine downstream error", error.Message);
    }

    [Fact]
    public async Task InMemoryProviderCannotAccidentallyReportReady()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Assert.False(await new DatabaseSchemaProbe(db).IsCompatibleAsync());
    }

    [Fact]
    public async Task ProbeReadsOnlyCatalogMetadataAndAcceptsSchemaWithoutMigrationHistoryOrRows()
    {
        using var connection = new MetadataConnection();
        await using var db = Database(connection);
        PopulateMetadata(db, connection.Metadata);
        Assert.DoesNotContain(connection.Metadata.Rows.Cast<DataRow>(), row => (string)row[1] == "__EFMigrationsHistory");
        Assert.True(await new DatabaseSchemaProbe(db).IsCompatibleAsync());
        var sql = Assert.Single(connection.Commands);
        Assert.StartsWith("SELECT ", sql);
        Assert.Contains("pg_catalog.pg_attribute", sql);
        foreach (var forbidden in new[] { "CREATE", "ALTER", "INSERT", "UPDATE", "DELETE", "COUNT(", "__EFMigrationsHistory", "FROM \"Alumnos\"" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData("missing_table")]
    [InlineData("missing_column")]
    [InlineData("wrong_type")]
    [InlineData("missing_marker")]
    [InlineData("wrong_marker_type")]
    [InlineData("wrong_schema")]
    public async Task ProbeRejectsIncompatibleStructure(string change)
    {
        using var connection = new MetadataConnection();
        await using var db = Database(connection);
        PopulateMetadata(db, connection.Metadata);
        var rows = connection.Metadata.Rows;
        var marker = rows.Cast<DataRow>().Single(row => (string)row[1] == "__CensCheckpointCommits");
        switch (change)
        {
            case "missing_table":
                var table = (string)rows[0][1];
                foreach (var row in rows.Cast<DataRow>().Where(row => (string)row[1] == table).ToArray()) rows.Remove(row);
                break;
            case "missing_column": rows.RemoveAt(0); break;
            case "wrong_type": rows[0][3] = "incompatible_type"; break;
            case "missing_marker": rows.Remove(marker); break;
            case "wrong_marker_type": marker[3] = "text"; break;
            case "wrong_schema": marker[0] = "other"; break;
        }
        Assert.False(await new DatabaseSchemaProbe(db).IsCompatibleAsync());
    }

    [Theory]
    [InlineData("varchar(80)", "character varying(120)", true)]
    [InlineData("numeric(18,2)", "decimal(10,4)", true)]
    [InlineData("timestamp(6) with time zone", "timestamptz", true)]
    [InlineData("timestamp", "timestamp without time zone", true)]
    [InlineData("int4[]", "integer[]", true)]
    [InlineData("uuid", "text", false)]
    [InlineData("timestamp with time zone", "timestamp without time zone", false)]
    [InlineData("bigint", "integer", false)]
    public void StoreTypesCompareByBasicPostgresType(string expected, string actual, bool matches)
    {
        var normalize = typeof(DatabaseSchemaProbe).GetMethod("NormalizeType", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(matches, Equals(normalize.Invoke(null, new object[] { expected }), normalize.Invoke(null, new object[] { actual })));
    }

    [Fact]
    public async Task ProbeIgnoresLegacyNullabilityAndExtraMetadataAndPreservesOpenConnection()
    {
        using var connection = new MetadataConnection();
        await using var db = Database(connection);
        PopulateMetadata(db, connection.Metadata);
        // Catalog projection deliberately has no nullability or row values.
        Assert.Equal(4, connection.Metadata.Columns.Count);
        Assert.Contains(connection.Metadata.Rows.Cast<DataRow>(), row => (string)row[1] == "AlumnoContactos" && (string)row[2] == "Email");
        connection.Metadata.Rows.Add("public", "Unrelated", "Extra", "text");
        connection.Open();
        Assert.True(await new DatabaseSchemaProbe(db).IsCompatibleAsync());
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task ConnectionErrorsAndCancellationPropagateToStartupOwner()
    {
        using var connection = new MetadataConnection { OpenError = new InvalidOperationException("simulated connection failure") };
        await using var db = Database(connection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseSchemaProbe(db).IsCompatibleAsync());
        connection.OpenError = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DatabaseSchemaProbe(db).IsCompatibleAsync(cancellation.Token));
        Assert.Empty(connection.Commands);
    }

    private static AppDbContext Database(DbConnection connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);

    private static void PopulateMetadata(AppDbContext db, DataTable metadata)
    {
        foreach (var table in db.Model.GetRelationalModel().Tables)
        foreach (var column in table.Columns)
            metadata.Rows.Add(table.Schema ?? "public", table.Name, column.Name, column.StoreType);
        metadata.Rows.Add("public", "__CensCheckpointCommits", "Id", "uuid");
    }

    private static DefaultHttpContext Context(string method, string path)
    {
        // No RequestServices, authentication, storage, or database registration.
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body, leaveOpen: true).ReadToEndAsync();
    }

    private sealed class MetadataConnection : DbConnection
    {
        private ConnectionState state;
        public DataTable Metadata { get; } = new();
        public List<string> Commands { get; } = new();
        public Exception? OpenError { get; set; }
        public MetadataConnection()
        {
            foreach (var name in new[] { "schema", "table", "column", "type" }) Metadata.Columns.Add(name, typeof(string));
        }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "metadata_unit_test";
        public override string DataSource => "no_network";
        public override string ServerVersion => "16";
        public override ConnectionState State => state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open()
        {
            if (OpenError is not null) throw OpenError;
            state = ConnectionState.Open;
        }
        public override Task OpenAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Open();
            return Task.CompletedTask;
        }
        public override void Close() => state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new MetadataCommand(this);
    }

    private sealed class MetadataCommand(MetadataConnection connection) : DbCommand
    {
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new InvalidOperationException("Writes are forbidden.");
        public override object ExecuteScalar() => throw new InvalidOperationException("Row queries are forbidden.");
        public override void Prepare() => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            connection.Commands.Add(CommandText);
            return connection.Metadata.CreateDataReader();
        }
    }
}
