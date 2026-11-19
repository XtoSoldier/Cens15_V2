using System.Data.Common;
using CENS15_V2.Data;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class ApplicationStartupTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "startup-test-" + Guid.NewGuid().ToString("N"));
    private sealed class Probe(bool compatible = true, bool fail = false) : IDatabaseSchemaProbe
    {
        public int Calls;
        public Task<bool> IsCompatibleAsync(CancellationToken ct = default)
        {
            Calls++;
            if (fail) throw new InvalidOperationException("Private database diagnostic must never be the public reason.");
            return Task.FromResult(compatible);
        }
    }
    private sealed class NoConnections : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken ct = default) => throw new InvalidOperationException("Unexpected database operation in fixture recovery.");
    }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "StartupTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private IConfiguration Configuration(string? remove = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=metadata_only;Username=unused;Password=unused",
            ["Jwt:Key"] = new string('a', 32), ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test",
            ["Jwt:DurationInMinutes"] = "60", ["Security:BCryptWorkFactor"] = "4"
        };
        if (remove != null) values.Remove(remove);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private ServiceProvider Services(IConfiguration configuration, Probe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IWebHostEnvironment>(new TestEnvironment(root));
        services.AddSingleton<CheckpointStorage>();
        services.AddSingleton<SessionEpoch>();
        services.AddSingleton<CheckpointGate>();
        services.AddSingleton<StartupReadiness>();
        services.AddSingleton<IDatabaseSchemaProbe>(probe);
        services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(configuration.GetConnectionString("DefaultConnection") ?? "Host=unused;Database=unused")
            .AddInterceptors(new NoConnections()).Options));
        services.AddScoped<CheckpointRecovery>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CompatibleStartupDoesNotConnectForRecoveryOrSeedAndRetainsKeysAcrossChecks()
    {
        var config = Configuration(); var probe = new Probe();
        await using var services = Services(config, probe);
        await ApplicationStartup.InitializeAsync(services, config, NullLogger.Instance);
        Assert.True(services.GetRequiredService<StartupReadiness>().IsReady);
        Assert.True(services.GetRequiredService<CheckpointGate>().Healthy);
        var storage = services.GetRequiredService<CheckpointStorage>();
        var key = await File.ReadAllBytesAsync(Path.Combine(storage.Root, "server.key"));
        var epoch = services.GetRequiredService<SessionEpoch>().Current;
        await ApplicationStartup.InitializeAsync(services, config, NullLogger.Instance);
        Assert.True(services.GetRequiredService<StartupReadiness>().IsReady);
        Assert.Equal(key, await File.ReadAllBytesAsync(Path.Combine(storage.Root, "server.key")));
        Assert.Equal(epoch, services.GetRequiredService<SessionEpoch>().Current);
        Assert.Equal(2, probe.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SchemaOrConnectionFailureDoesNotEscapeAndBlocksBusiness(bool connectionFailure)
    {
        var config = Configuration();
        await using var services = Services(config, new Probe(compatible: false, fail: connectionFailure));
        await ApplicationStartup.InitializeAsync(services, config, NullLogger.Instance);
        var status = services.GetRequiredService<StartupReadiness>();
        Assert.False(status.IsReady);
        Assert.False(services.GetRequiredService<CheckpointGate>().Healthy);
        Assert.Equal(connectionFailure ? "database_unavailable" : "schema_incompatible", status.Reason);
        Assert.DoesNotContain("Private", status.Reason);
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection")] [InlineData("Jwt:Key")]
    [InlineData("Jwt:Issuer")] [InlineData("Jwt:Audience")]
    [InlineData("Jwt:DurationInMinutes")] [InlineData("Security:BCryptWorkFactor")]
    public async Task MissingConfigurationFailsClosedWithoutProbingOrCreatingStorage(string field)
    {
        var config = Configuration(field); var probe = new Probe();
        await using var services = Services(config, probe);
        await ApplicationStartup.InitializeAsync(services, config, NullLogger.Instance);
        Assert.Equal("configuration_unavailable", services.GetRequiredService<StartupReadiness>().Reason);
        Assert.Equal(0, probe.Calls);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task InvalidJournalIsRetainedAndNeverMarksTheApplicationReady()
    {
        var config = Configuration();
        await using var services = Services(config, new Probe());
        var storage = services.GetRequiredService<CheckpointStorage>();
        var journal = Path.Combine(storage.Root, "restore.journal");
        await File.WriteAllTextAsync(journal, "invalid-journal");
        await ApplicationStartup.InitializeAsync(services, config, NullLogger.Instance);
        Assert.Equal("recovery_pending", services.GetRequiredService<StartupReadiness>().Reason);
        Assert.False(services.GetRequiredService<CheckpointGate>().Healthy);
        Assert.Equal("invalid-journal", await File.ReadAllTextAsync(journal));
    }

    [Fact]
    public async Task InvalidPersistentKeyReturnsStorageFailureAndReleasesItsFileHandle()
    {
        var config = Configuration(); var probe = new Probe();
        var checkpoints = Path.Combine(root, "App_Data", "checkpoints");
        Directory.CreateDirectory(checkpoints);
        await File.WriteAllBytesAsync(Path.Combine(checkpoints, "server.key"), [1]);
        await using var services = Services(config, probe);
        await ApplicationStartup.InitializeAsync(services, config, NullLogger.Instance);
        Assert.Equal("storage_unavailable", services.GetRequiredService<StartupReadiness>().Reason);
        Assert.Equal(0, probe.Calls);
        using var handle = new FileStream(Path.Combine(checkpoints, "instance.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(Path.Combine(checkpoints, "server.key")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
