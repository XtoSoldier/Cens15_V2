using System.Reflection;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class DatabaseMaintenanceTests : IDisposable
{
    private const string Connection = "Host=production.example.invalid;Database=cens_operator;Username=operator;Password=not-a-real-secret";
    private readonly string root = Path.Combine(Path.GetTempPath(), "opencode", "maintenance-" + Guid.NewGuid().ToString("N"));
    private readonly CheckpointStorage storage;
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public DatabaseMaintenanceTests()
    {
        storage = new CheckpointStorage(Config(), new EnvironmentStub(root));
    }

    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = Connection,
            ["Security:BCryptWorkFactor"] = "4"
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private DatabaseMaintenance Service(IConfiguration? config = null, AppDbContext? context = null)
    {
        config ??= Config();
        return new DatabaseMaintenance(context ?? db, config, new PasswordHasher(config), storage,
            NullLogger<DatabaseMaintenance>.Instance);
    }

    // Exercise the seed unit without weakening RunAsync's PostgreSQL-only public boundary.
    private Task Seed(IConfiguration? config = null, CancellationToken ct = default) =>
        (Task)typeof(DatabaseMaintenance).GetMethod("SeedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Service(config), new object[] { ct })!;

    private async Task<Auth> ExistingAccount(string roleName = "Admin", bool active = true)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = roleName };
        var user = new User
        {
            Id = Guid.NewGuid(), FirstName = "Existing", LastName = "Profile", Image = "keep.png",
            Status = active, Role = role, RoleId = role.Id
        };
        var auth = new Auth
        {
            Id = user.Id, Email = "existing@example.com", PasswordHash = "unchanged-password-hash",
            MustChangePassword = true, InitialAccessSentAt = DateTime.UtcNow, User = user
        };
        db.AddRange(role, user, auth);
        await db.SaveChangesAsync();
        return auth;
    }

    [Theory]
    [InlineData("check")]
    [InlineData("Migrate")]
    [InlineData("seed ")]
    [InlineData("")]
    public async Task OnlyExactOperationsAreAccepted(string operation)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RunAsync(operation, "cens_operator"));
        Assert.Contains("Operacion", error.Message);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ConfirmationIsRequiredBeforeAnyDatabaseAccess(string confirmation)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RunAsync("seed", confirmation));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Password=never-print-this;invalid-key=bad")]
    [InlineData("Host=production.example.invalid")]
    [InlineData("Database=cens_operator")]
    public async Task ExplicitValidDefaultConnectionIsRequiredWithoutFallback(string? connection)
    {
        var config = Config(("ConnectionStrings:DefaultConnection", connection),
            ("ConnectionStrings:DefaultConnectionFallback", Connection));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(config).RunAsync("seed", "cens_operator"));
        Assert.Contains("DefaultConnection", error.Message);
        Assert.DoesNotContain("never-print-this", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task PublicMaintenanceRejectsNonPostgresProvider()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RunAsync("migrate", "cens_operator"));
        Assert.Contains("Npgsql", error.Message);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("CENS_OPERATOR")]
    [InlineData("cens_operator ")]
    [InlineData("other")]
    public async Task ConfirmationMustExactlyMatchActualDatabaseWithoutConnecting(string confirmation)
    {
        using var postgres = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context: postgres).RunAsync("migrate", confirmation));
        Assert.Contains("no coincide", error.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, postgres.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task ConfigurationMustMatchActualContextDestination()
    {
        using var postgres = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
        var config = Config(("ConnectionStrings:DefaultConnection", Connection.Replace("production.example.invalid", "other.example.invalid")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(config, postgres).RunAsync("seed", "cens_operator"));
        Assert.Equal(System.Data.ConnectionState.Closed, postgres.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("seed")]
    public async Task RemoteHostPassesPreflightWithoutNetworkWhenCancelled(string operation)
    {
        using var postgres = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(context: postgres)
            .RunAsync(operation, "cens_operator", cancellation.Token));
        Assert.Equal(System.Data.ConnectionState.Closed, postgres.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("seed")]
    public async Task PendingJournalIsNeverRecoveredOrRewritten(string operation)
    {
        var journal = Path.Combine(storage.Root, "restore.journal");
        var original = new byte[] { 1, 2, 3, 4 };
        CheckpointStorage.AtomicWrite(journal, original);
        using var postgres = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context: postgres).RunAsync(operation, "cens_operator"));
        Assert.Contains("restore.journal", error.Message);
        Assert.Equal(original, File.ReadAllBytes(journal));
        Assert.Equal(System.Data.ConnectionState.Closed, postgres.Database.GetDbConnection().State);
    }

    [Fact]
    public void SharedStorageHoldsExclusiveInstanceLock()
    {
        Assert.Throws<IOException>(() => new FileStream(Path.Combine(storage.Root, "instance.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    [Fact]
    public async Task CatalogsAreIdempotentAndNeverCreateAccountsOrAcademicDemoData()
    {
        await DbSeeder.SeedCatalogsAsync(db);
        await DbSeeder.SeedCatalogsAsync(db);
        Assert.Equal(new[] { "Admin", "Preceptor", "User" }, await db.Roles.OrderBy(r => r.Name).Select(r => r.Name).ToArrayAsync());
        Assert.Equal(4, await db.TiposDocumentoAlumno.CountAsync());
        Assert.Equal(ResponsibilityPolicies.All.Count(), await db.Responsibilities.CountAsync());
        var admin = await db.Roles.Include(r => r.Responsibilities).SingleAsync(r => r.Name == "Admin");
        Assert.Equal(ResponsibilityPolicies.All.Count(), admin.Responsibilities.Count);
        Assert.Empty(db.Users);
        Assert.Empty(db.Auths);
        AssertNoDemo();
    }

    [Fact]
    public async Task FreshDatabaseRequiresBootstrapBeforeTrackingAnyCatalogs()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seed());
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(db.Roles);
    }

    [Theory]
    [InlineData("Bootstrap:AdminEmail", "admin@example.com")]
    [InlineData("Bootstrap:AdminPassword", "custom-password")]
    public async Task HalfBootstrapIsRejectedEvenWithExistingAdmin(string key, string value)
    {
        await ExistingAccount();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(Config((key, value))));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(db.Responsibilities);
    }

    [Theory]
    [InlineData("bad-email", "abcdef", "4")]
    [InlineData("Display Name <admin@example.com>", "abcdef", "4")]
    [InlineData("admin@example.com", "short", "4")]
    [InlineData("admin@example.com", "abcdef", null)]
    [InlineData("admin@example.com", "abcdef", "0")]
    [InlineData("admin@example.com", "abcdef", "32")]
    [InlineData("admin@example.com", "abcdef", "invalid")]
    public async Task InvalidBootstrapNeverTracksOrWritesCatalogs(string email, string password, string? cost)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(Config(("Bootstrap:AdminEmail", email),
            ("Bootstrap:AdminPassword", password), ("Security:BCryptWorkFactor", cost))));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(db.Roles);
        Assert.Empty(db.Auths);
    }

    [Fact]
    public async Task PasswordLimitIsUtf8BytesNotCharacters()
    {
        var password = new string('\u00e9', 37);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(Config(("Bootstrap:AdminEmail", "admin@example.com"),
            ("Bootstrap:AdminPassword", password))));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("  aDmIn  ")]
    [InlineData("  SUPERadmin ")]
    public async Task ExistingActiveAdministratorAllowsCatalogsAndPreservesAuthAndProfile(string role)
    {
        var auth = await ExistingAccount(role);
        var originalDate = auth.InitialAccessSentAt;
        await Seed();
        Assert.Single(db.Users);
        Assert.Single(db.Auths);
        Assert.Equal("unchanged-password-hash", auth.PasswordHash);
        Assert.Equal("existing@example.com", auth.Email);
        Assert.True(auth.MustChangePassword);
        Assert.Equal(originalDate, auth.InitialAccessSentAt);
        Assert.Equal("Existing", auth.User.FirstName);
        Assert.Equal("Profile", auth.User.LastName);
        Assert.Equal("keep.png", auth.User.Image);
        Assert.Equal(role, auth.User.Role.Name);
        AssertNoDemo();
    }

    [Theory]
    [InlineData("Admin", false)]
    [InlineData("User", true)]
    public async Task InactiveOrNonAdminDoesNotAllowMissingBootstrap(string role, bool active)
    {
        await ExistingAccount(role, active);
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seed());
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(db.Responsibilities);
    }

    [Fact]
    public async Task CustomBootstrapCreatesOnlyRequestedAccountWithBcryptAndIsIdempotent()
    {
        var password = new string('a', 72);
        var config = Config(("Bootstrap:AdminEmail", "  custom@example.com  "), ("Bootstrap:AdminPassword", password));
        await Seed(config);
        var auth = await db.Auths.Include(a => a.User).SingleAsync();
        Assert.Equal("custom@example.com", auth.Email);
        Assert.True(new PasswordHasher(config).Verify(password, auth.PasswordHash));
        Assert.True(auth.User.Status);
        Assert.Equal("Admin", auth.User.Role.Name);
        var hash = auth.PasswordHash;
        await Seed(config);
        Assert.Single(db.Auths);
        Assert.Equal(hash, auth.PasswordHash);
        AssertNoDemo();
    }

    [Fact]
    public async Task ExistingEmailIsNotPromotedOrPasswordResetWhenNoActiveAdmin()
    {
        var auth = await ExistingAccount("User");
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(Config(("Bootstrap:AdminEmail", "EXISTING@example.com"),
            ("Bootstrap:AdminPassword", "replacement"))));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(db.Responsibilities);
        Assert.Equal("unchanged-password-hash", auth.PasswordHash);
    }

    [Fact]
    public async Task ExistingAdminBootstrapNeverRehashesOrChangesExistingAccount()
    {
        var auth = await ExistingAccount();
        await Seed(Config(("Bootstrap:AdminEmail", "EXISTING@example.com"), ("Bootstrap:AdminPassword", "replacement")));
        Assert.Single(db.Auths);
        Assert.Equal("unchanged-password-hash", auth.PasswordHash);
        Assert.True(auth.MustChangePassword);
    }

    [Fact]
    public void SchemaSqlPreservesLegacyPatchesAndUnmappedUuidCommitMetadataWithoutConnecting()
    {
        var sql = (string)typeof(DatabaseMaintenance).GetField("LegacySchemaSql", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        using var command = new NpgsqlCommand(sql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.\"__CensCheckpointCommits\" (\"Id\" uuid PRIMARY KEY);", command.CommandText);
        Assert.Contains("\"InitialAccessSentAt\" timestamp with time zone NULL", sql);
        Assert.Contains("\"Attempts\" integer NOT NULL", sql);
        Assert.Contains("ON DELETE SET NULL", sql);
        Assert.Contains("ON DELETE CASCADE", sql);
        Assert.Contains("CREATE INDEX IF NOT EXISTS \"IX_PasswordResetCodes_ExpiresAt\"", sql);
        Assert.DoesNotContain("DOB", sql);
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.GetTableName() == "__CensCheckpointCommits");
        Assert.Null(command.Connection);
    }

    [Fact]
    public async Task HistoricalSeedStillProvidesDemoTestCredentials()
    {
        var config = Config();
        await DbSeeder.Seed(db, new PasswordHasher(config));
        var auth = await db.Auths.SingleAsync();
        Assert.Equal("admin@admin.com", auth.Email);
        Assert.True(new PasswordHasher(config).Verify("Admin123*", auth.PasswordHash));
        AssertNoDemo();
    }

    [Fact]
    public async Task MigrationFailureIsSanitizedAndAbortsBeforePatchesOrSeeding()
    {
        using var postgres = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection)
            .ReplaceService<IMigrator, FailingMigrator>().Options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context: postgres)
            .RunAsync("migrate", "cens_operator"));
        Assert.DoesNotContain("not-a-real-secret", error.ToString());
        Assert.DoesNotContain("bootstrap-secret", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Empty(postgres.ChangeTracker.Entries());
        Assert.Equal(System.Data.ConnectionState.Closed, postgres.Database.GetDbConnection().State);
    }

    private void AssertNoDemo()
    {
        Assert.Empty(db.Alumnos);
        Assert.Empty(db.Docentes);
        Assert.Empty(db.Materias);
        Assert.Empty(db.Cursos);
        Assert.Empty(db.Inscripciones);
        Assert.Empty(db.Calificaciones);
    }

    public void Dispose()
    {
        db.Dispose();
        storage.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MaintenanceTests";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    public sealed class FailingMigrator : IMigrator
    {
        public void Migrate(string? targetMigration = null) => throw new InvalidOperationException(Connection + ";bootstrap-secret");
        public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Connection + ";bootstrap-secret");
        public string GenerateScript(string? fromMigration = null, string? toMigration = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) => throw new NotSupportedException();
    }
}
