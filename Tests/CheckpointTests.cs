using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Models;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class CheckpointTests
{
    private const string Passphrase = "checkpoint test passphrase";

    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CheckpointTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "opencode", "cens-checkpoints-" + Guid.NewGuid().ToString("N"));
        public AppDbContext Db { get; }
        public CheckpointStorage Storage { get; }
        public CheckpointGate Gate { get; } = new();
        public CheckpointModel Model { get; }
        public SessionEpoch Epoch { get; }
        public ClaimsPrincipal Caller { get; }
        public CheckpointService Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Storage = new CheckpointStorage(new ConfigurationBuilder().Build(), new EnvironmentStub(Root));
            Epoch = new SessionEpoch(Storage);
            Model = new CheckpointModel(Db);
            var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
            var user = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Admin", Status = true, Role = role };
            user.Auth = new Auth { Id = user.Id, User = user, Email = "admin@example.invalid", PasswordHash = BCrypt.Net.BCrypt.HashPassword("test password", 4) };
            Db.Add(user);
            Db.SaveChanges();
            Caller = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, "Admin"),
                new Claim(SessionEpoch.Claim, Epoch.Current)], "Test"));
            Service = new CheckpointService(Db, Storage, Gate, Model, new CheckpointRecovery(Db, Storage, Epoch, Gate), Epoch, new CheckpointPreviews());
        }
        public Task<ApplicationSnapshot> Capture() => Model.CaptureAsync(Storage, "test", null, Caller.FindFirstValue(ClaimTypes.NameIdentifier)!, false, default);
        public void Dispose()
        {
            Db.Dispose();
            Storage.Dispose();
            Directory.Delete(Root, true);
        }
    }

    [Fact]
    public async Task CaptureIncludesEveryEfScalarSoftDeletedRowsLegacyRelationshipsAndActualFiles()
    {
        using var f = new Fixture();
        var role = await f.Db.Roles.SingleAsync();
        f.Db.Add(new RolePermission { Role = role, Permission = new Permission { Id = Guid.NewGuid(), Name = "legacy" } });
        var alumno = new Alumno { Id = 72, Nombres = "Eliminado", Apellidos = "Historico", NumeroDocumento = "12345678", Eliminado = true,
            Contacto = new AlumnoContacto { Id = 81, Email = "student@example.invalid" } };
        f.Db.Add(alumno);
        var auth = await f.Db.Auths.SingleAsync();
        f.Db.Add(new Token { Id = auth.Id, Auth = auth, AccessToken = "historical-jwt-not-a-signing-key" });
        f.Db.Add(new PasswordResetCode { AuthId = auth.Id, CodeHash = "historical-reset-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        await f.Db.SaveChangesAsync();
        Directory.CreateDirectory(Path.Combine(f.Storage.Uploads, "alumno-documentos"));
        await File.WriteAllBytesAsync(Path.Combine(f.Storage.Uploads, "alumno-documentos", "document.bin"), [0, 1, 2, 255]);
        var snapshot = await f.Capture();
        Assert.Equal(f.Db.Model.GetEntityTypes().Count(), snapshot.Entidades.Count);
        foreach (var entity in f.Db.Model.GetEntityTypes())
            foreach (var row in snapshot.Entidades[entity.Name])
                Assert.Equal(entity.GetProperties().Select(p => p.Name).Order(), row.Keys.Order());
        Assert.Single(snapshot.Entidades[typeof(Alumno).FullName!]);
        Assert.True(snapshot.Entidades[typeof(Alumno).FullName!][0]["Eliminado"].GetBoolean());
        Assert.Equal(72, snapshot.Entidades[typeof(AlumnoContacto).FullName!][0]["AlumnoId"].GetInt32());
        Assert.Single(snapshot.Entidades[typeof(RolePermission).FullName!]);
        Assert.Single(snapshot.Entidades[typeof(PasswordResetCode).FullName!]);
        Assert.Single(snapshot.Entidades[typeof(Token).FullName!]);
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, snapshot.Archivos["alumno-documentos/document.bin"]);
        Assert.NotEmpty(f.Model.Validate(snapshot));
    }

    [Fact]
    public async Task PortableArchiveAuthenticatesHeaderPayloadAndPassphrase()
    {
        using var f = new Fixture();
        var snapshot = await f.Capture();
        var archive = CheckpointStorage.Export(snapshot, Passphrase);
        Assert.Equal("CENSBK01", Encoding.ASCII.GetString(archive, 0, 8));
        Assert.DoesNotContain("PasswordHash", Encoding.UTF8.GetString(archive));
        Assert.Equal(snapshot.Id, CheckpointStorage.Import(archive, Passphrase).Id);
        Assert.Contains("Clave incorrecta", Assert.Throws<CheckpointException>(() => CheckpointStorage.Import(archive, "incorrect passphrase")).Message);
        foreach (var position in new[] { 8, 24, 36, 52, archive.Length - 1 })
        {
            var modified = (byte[])archive.Clone(); modified[position] ^= 1;
            Assert.Throws<CheckpointException>(() => CheckpointStorage.Import(modified, Passphrase));
        }
        Assert.Throws<CheckpointException>(() => CheckpointStorage.Export(snapshot, "short"));
        Assert.Throws<CheckpointException>(() => CheckpointStorage.Import([1, 2, 3], Passphrase));
    }

    [Fact]
    public async Task ServerRegistryIsEncryptedAndImportOnlyRegistersWithoutChangingDomain()
    {
        using var f = new Fixture();
        var metadata = await f.Service.CreateAsync(new("Dated point", "Description"), f.Caller, default);
        var archive = await f.Service.ExportAsync(metadata.Id, Passphrase, f.Caller, default);
        var user = await f.Db.Users.SingleAsync(); user.FirstName = "Modified after capture";
        await f.Db.SaveChangesAsync();
        var imported = await f.Service.ImportAsync(archive, Passphrase, f.Caller, default);
        Assert.NotEqual(metadata.Id, imported.Id);
        Assert.Equal("Modified after capture", (await f.Db.Users.SingleAsync()).FirstName);
        var bytes = await File.ReadAllBytesAsync(f.Storage.PathFor(metadata.Id));
        Assert.DoesNotContain("admin@example.invalid", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("PasswordHash", JsonSerializer.Serialize(metadata));
        Assert.Equal(2, (await f.Service.ListAsync(f.Caller, default)).Length);
    }

    [Fact]
    public async Task PreviewReportsUnsupportedProviderAndRestoreNeverMutatesInMemory()
    {
        using var f = new Fixture();
        var point = await f.Service.CreateAsync(new("point", null), f.Caller, default);
        var preview = await f.Service.PreviewAsync(point.Id, f.Caller, default);
        Assert.False(preview.PuedeRestaurar);
        Assert.Equal("", preview.Huella);
        Assert.Contains(preview.Errores, e => e.Contains("PostgreSQL"));
        var exception = await Assert.ThrowsAsync<CheckpointException>(() => f.Service.RestoreAsync(point.Id,
            new("invalid", "RESTAURAR TODO"), f.Caller, default));
        Assert.Equal(409, exception.Status);
        Assert.Single(f.Db.Users);
        Assert.True(f.Gate.Healthy);
    }

    [Fact]
    public async Task RestoreRequiresExactConfirmationAndExplicitOptOutAcknowledgement()
    {
        using var f = new Fixture();
        foreach (var request in new[] { new CheckpointRestore("x", "yes"), new CheckpointRestore("x", "RESTAURAR TODO", false, false) })
            Assert.Equal(400, (await Assert.ThrowsAsync<CheckpointException>(() => f.Service.RestoreAsync(Guid.NewGuid(), request, f.Caller, default))).Status);
        Assert.True(new CheckpointRestore("x", "RESTAURAR TODO").CrearRespaldo);
    }

    [Fact]
    public async Task StoreAdminCheckAndEpochRejectDemotedOrPreRestoreSessions()
    {
        using var f = new Fixture();
        f.Epoch.Rotate();
        Assert.Equal(401, (await Assert.ThrowsAsync<CheckpointException>(() => f.Service.ListAsync(f.Caller, default))).Status);
        var fresh = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, f.Caller.FindFirstValue(ClaimTypes.NameIdentifier)!),
            new Claim(ClaimTypes.Role, "Admin"), new Claim(SessionEpoch.Claim, f.Epoch.Current)], "Test"));
        var role = await f.Db.Roles.SingleAsync(); role.Name = "NoLongerAdmin"; await f.Db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<CheckpointException>(() => f.Service.ListAsync(fresh, default))).Status);
        Assert.Equal(f.Epoch.Current, new SessionEpoch(f.Storage).Current);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("folder\\file")]
    [InlineData("C:/file")]
    [InlineData("folder/../file")]
    [InlineData("CON.txt")]
    [InlineData("folder/name.")]
    public void UnsafeUploadNamesAreRejected(string name) => Assert.Throws<CheckpointException>(() => CheckpointStorage.ValidateFileName(name));

    [Fact]
    public async Task ValidationRejectsMissingFieldsDanglingForeignKeysDuplicatesUnsafePathsAndNoAdmin()
    {
        using var f = new Fixture();
        var source = await f.Capture();
        ApplicationSnapshot Clone() => CheckpointStorage.Decode(CheckpointStorage.Encode(source));
        var invalid = Clone(); invalid.Entidades.Remove(typeof(Auth).FullName!);
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        invalid = Clone(); invalid.Entidades[typeof(User).FullName!][0].Remove("Status");
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        invalid = Clone(); invalid.Entidades[typeof(User).FullName!][0]["RoleId"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        invalid = Clone(); invalid.Entidades[typeof(Auth).FullName!].Add(invalid.Entidades[typeof(Auth).FullName!][0]);
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        invalid = Clone(); invalid.Archivos["file"] = []; invalid.Archivos["FILE"] = [];
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        invalid = Clone(); invalid.Archivos["folder"] = []; invalid.Archivos["folder/file"] = [];
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        invalid = Clone(); invalid.Entidades[typeof(User).FullName!][0]["Status"] = JsonSerializer.SerializeToElement(false);
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        invalid = Clone(); invalid.Entidades[typeof(Auth).FullName!][0]["PasswordHash"] = JsonSerializer.SerializeToElement("not a bcrypt hash");
        Assert.Throws<CheckpointException>(() => f.Model.Validate(invalid));
        Assert.Throws<CheckpointException>(() => CheckpointStorage.Decode(Encoding.UTF8.GetBytes("{\"Version\":1,\"Version\":1}")));
        Assert.Throws<CheckpointException>(() => CheckpointStorage.Decode(Encoding.UTF8.GetBytes("{\"Unrecognized\":1}")));
    }

    [Fact]
    public async Task GateBlocksRequestsDuringWriterAndWriterWaitsForOrdinaryRequests()
    {
        var gate = new CheckpointGate();
        var reader = await gate.ReadAsync();
        var writing = gate.WriteAsync();
        Assert.False(writing.IsCompleted);
        reader.Dispose();
        using (await writing.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.ReadAsync(timeout.Token));
        }
        using var successful = await gate.ReadAsync();
    }

    [Fact]
    public async Task MiddlewareGatesStaticFilesAndOrdinaryTransferRoutesButNotCheckpointRoutes()
    {
        var gate = new CheckpointGate();
        var called = 0;
        var middleware = new CheckpointGateMiddleware(_ => { called++; return Task.CompletedTask; });
        using (await gate.WriteAsync())
        {
            var checkpoint = new DefaultHttpContext(); checkpoint.Request.Path = "/api/checkpoints";
            await middleware.InvokeAsync(checkpoint, gate).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, called);
            foreach (var path in new[] { "/api/transferencias", "/uploads/document.pdf" })
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                var ordinary = new DefaultHttpContext(); ordinary.Request.Path = path; ordinary.RequestAborted = timeout.Token;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.InvokeAsync(ordinary, gate));
            }
        }
        gate.Healthy = false;
        var unavailable = new DefaultHttpContext(); unavailable.Request.Path = "/api/checkpoints";
        await middleware.InvokeAsync(unavailable, gate);
        Assert.Equal(503, unavailable.Response.StatusCode);
    }

    [Fact]
    public void PreviewBindingsAreOneUseAndBoundToCallerTargetSourceAndExpiry()
    {
        var bindings = new CheckpointPreviews();
        var expected = new CheckpointPreviews.Binding(Guid.NewGuid(), "caller", "epoch", "target", "source", DateTimeOffset.UtcNow.AddMinutes(5));
        var token = bindings.Add(expected);
        Assert.Equal(expected, bindings.Take(token));
        Assert.Null(bindings.Take(token));
        Assert.Null(bindings.Take("unrecognized"));
    }

    [Fact]
    public void StorageRejectsWebRootAndConcurrentInstance()
    {
        using var f = new Fixture();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Snapshots:Directory"] = Path.Combine(f.Root, "wwwroot", "private") }).Build();
        Assert.Throws<InvalidOperationException>(() => new CheckpointStorage(config, new EnvironmentStub(f.Root)));
        Assert.Throws<IOException>(() => new CheckpointStorage(new ConfigurationBuilder().Build(), new EnvironmentStub(f.Root)));
    }
}
