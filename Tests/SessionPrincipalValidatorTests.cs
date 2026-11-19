using System.Security.Claims;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Models.DTOs.Transferencias;
using CENS15_V2.Security;
using CENS15_V2.Services;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class SessionPrincipalValidatorTests
{
    private const string JwtKey = "session-tests-signing-key-not-for-production";

    [Theory]
    [InlineData("es-AR")]
    [InlineData("en-US")]
    public void TokenDurationUsesTheSameInvariantFormatAsStartupValidation(string culture)
    {
        using var f = new Fixture();
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey, ["Jwt:DurationInMinutes"] = "1.5", ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test"
            }).Build();
            var before = DateTime.UtcNow;
            var response = new CENS15.V2.Services.TokenService(config, f.Epoch).CreateToken(f.User.Auth);
            Assert.InRange(response.Expiration, before.AddSeconds(90), DateTime.UtcNow.AddSeconds(90));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RestartPreservesEpochAndExplicitRotationPersistsAcrossRestart()
    {
        using var f = new Fixture();
        var original = f.Epoch.Current;
        Assert.Equal(original, new SessionEpoch(f.Storage).Current);
        f.Epoch.Rotate();
        Assert.NotEqual(original, f.Epoch.Current);
        Assert.Equal(f.Epoch.Current, new SessionEpoch(f.Storage).Current);
    }
    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SessionTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class SaveProbe : SaveChangesInterceptor
    {
        public Action? BeforeSave { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            BeforeSave?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "opencode", "cens-sessions-" + Guid.NewGuid().ToString("N"));
        public SaveProbe Probe { get; } = new();
        public AppDbContext Db { get; }
        public CheckpointStorage Storage { get; }
        public SessionEpoch Epoch { get; }
        public User User { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Storage = new CheckpointStorage(new ConfigurationBuilder().Build(), new EnvironmentStub(Root));
            Epoch = new SessionEpoch(Storage);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(Probe).Options);
            var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
            role.Responsibilities.Add(new RoleResponsibility
            {
                Role = role, Responsibility = new Responsibility { Id = Guid.NewGuid(), Name = "Usuarios.Consultar" }
            });
            User = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Admin", Status = true, Role = role };
            User.Auth = new Auth { Id = User.Id, User = User, Email = "admin@example.invalid", PasswordHash = "keep" };
            Db.Add(User); Db.SaveChanges();
        }
        public ClaimsPrincipal Principal() => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, User.Id.ToString()), new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Email, "ADMIN@example.invalid"), new Claim(SessionEpoch.Claim, Epoch.Current),
            new Claim(CredentialVersion.Claim, CredentialVersion.Compute(User.Id, User.Auth.PasswordHash, JwtKey)),
            new Claim(ResponsibilityPolicies.ClaimType, "Usuarios.Consultar")], "Test"));
        public void Dispose()
        {
            Db.Dispose(); Storage.Dispose(); Directory.Delete(Root, true);
        }
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("inactive", false)]
    [InlineData("missing-user", false)]
    [InlineData("missing-auth", false)]
    [InlineData("role", false)]
    [InlineData("grants-removed", false)]
    [InlineData("grants-added", false)]
    [InlineData("email", false)]
    [InlineData("epoch", false)]
    [InlineData("missing-id", false)]
    [InlineData("extra-role", false)]
    [InlineData("password-changed", false)]
    [InlineData("missing-version", false)]
    [InlineData("extra-version", false)]
    public async Task ValidatesCurrentPersistedIdentityAndExactPrivileges(string change, bool expected)
    {
        using var f = new Fixture(); var principal = f.Principal();
        switch (change)
        {
            case "valid":
                ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, f.User.Id.ToString()));
                ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("email", "admin@example.invalid"));
                break;
            case "inactive": f.User.Status = false; break;
            case "missing-user": f.Db.Users.Remove(f.User); break;
            case "missing-auth": f.Db.Auths.Remove(f.User.Auth); break;
            case "role": f.User.Role.Name = "Docente"; break;
            case "grants-removed": f.Db.Set<RoleResponsibility>().RemoveRange(f.User.Role.Responsibilities); break;
            case "grants-added": f.Db.Add(new RoleResponsibility { Role = f.User.Role,
                Responsibility = new Responsibility { Id = Guid.NewGuid(), Name = "Roles.Editar" } }); break;
            case "email": f.User.Auth.Email = "other@example.invalid"; break;
            case "epoch": f.Epoch.Rotate(); break;
            case "missing-id": ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst(ClaimTypes.NameIdentifier)!); break;
            case "extra-role": ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Other")); break;
            case "password-changed": f.User.Auth.PasswordHash = "changed"; break;
            case "missing-version": ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst(CredentialVersion.Claim)!); break;
            case "extra-version": ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(CredentialVersion.Claim, "other")); break;
        }
        await f.Db.SaveChangesAsync();
        var rejections = new List<string>();
        Assert.Equal(expected, await SessionPrincipalValidator.ValidateAsync(principal, f.Db, f.Epoch.Current, JwtKey,
            onRejected: rejections.Add));
        if (expected) Assert.Empty(rejections);
        else Assert.Equal(change switch
        {
            "inactive" or "missing-user" or "missing-auth" => "account_unavailable",
            "role" or "email" => "identity_changed",
            "grants-removed" or "grants-added" => "responsibilities_changed",
            "epoch" => "session_epoch_changed",
            "password-changed" => "credential_changed",
            _ => "invalid_claims"
        }, Assert.Single(rejections));
        Assert.False(await SessionPrincipalValidator.ValidateAsync(null, f.Db, f.Epoch.Current, JwtKey));
    }

    private static PaqueteTransferencia Package(string section = "responsabilidades") => new()
    {
        Formato = "cens15-transferencia", Version = 1, Seccion = section, ExportadoEn = DateTimeOffset.UtcNow,
        Datos = new DatosTransferencia
        {
            Docentes = [], Anexos = [], Orientaciones = [], Cursos = [], Materias = [], Bloques = [], Asignaciones = [], Alumnos = [],
            Responsabilidades = section == "responsabilidades" ? [new("Nueva", null)] : []
        }
    };

    [Theory]
    [InlineData("usuarios")]
    [InlineData("roles")]
    [InlineData("responsabilidades")]
    public async Task CommittedSecurityImportRotatesBeforeSaveAndRequiresLoginButPreviewAndOtherSectionsDoNot(string section)
    {
        using var f = new Fixture(); var service = new TransferenciaService(f.Db, f.Epoch);
        var principal = f.Principal(); var original = f.Epoch.Current; var p = Package(section);
        var preview = await service.ProcesarAsync(p, "actualizar");
        Assert.Equal(original, f.Epoch.Current); Assert.False(preview.RequiereReingreso);
        var stale = await service.ProcesarAsync(p, "actualizar", true, "wrong");
        Assert.False(stale.Importado); Assert.Equal(original, f.Epoch.Current);
        f.Probe.BeforeSave = () => Assert.NotEqual(original, f.Epoch.Current);
        var applied = await service.ProcesarAsync(p, "actualizar", true, preview.Huella);
        Assert.True(applied.Importado); Assert.True(applied.RequiereReingreso);
        Assert.Equal(f.Epoch.Current, new SessionEpoch(f.Storage).Current);
        Assert.False(await SessionPrincipalValidator.ValidateAsync(principal, f.Db, f.Epoch.Current, JwtKey));
        f.Probe.BeforeSave = null;
        var epoch = f.Epoch.Current; p = Package("docentes");
        preview = await service.ProcesarAsync(p, "actualizar");
        applied = await service.ProcesarAsync(p, "actualizar", true, preview.Huella);
        Assert.True(applied.Importado); Assert.False(applied.RequiereReingreso); Assert.Equal(epoch, f.Epoch.Current);
    }

    [Fact]
    public async Task RotationFailurePreventsAllWritesAndSaveFailureLeavesOldSessionsRevoked()
    {
        using var f = new Fixture(); var service = new TransferenciaService(f.Db, f.Epoch); var p = Package();
        var preview = await service.ProcesarAsync(p, "actualizar"); var original = f.Epoch.Current;
        var path = Path.Combine(f.Storage.Root, "session.epoch");
        File.Delete(path); Directory.CreateDirectory(path);
        var writes = 0; f.Probe.BeforeSave = () => writes++;
        var report = await service.ProcesarAsync(p, "actualizar", true, preview.Huella);
        Assert.False(report.Importado); Assert.False(report.RequiereReingreso); Assert.Equal(0, writes);
        Assert.Equal(original, f.Epoch.Current); Assert.Single(f.Db.Responsibilities);
        Directory.Delete(path);
        f.Probe.BeforeSave = () => throw new DbUpdateException("test failure");
        report = await service.ProcesarAsync(p, "actualizar", true, preview.Huella);
        Assert.False(report.Importado); Assert.False(report.RequiereReingreso);
        Assert.NotEqual(original, f.Epoch.Current); Assert.Single(f.Db.Responsibilities);
    }

    private sealed class NoTokens : ITokenService
    {
        public AuthResponse CreateToken(Auth auth) => throw new InvalidOperationException("Inactive login must not issue tokens.");
    }
    private sealed class NoEmails : IEmailService
    {
        public Task SendInitialAccessAsync(Auth auth, string temporaryPassword) => throw new InvalidOperationException("No emails expected.");
        public Task SendPasswordResetCodeAsync(Auth auth, string verificationCode) => throw new InvalidOperationException("No emails expected.");
    }

    private sealed class CapturingEmail : IEmailService
    {
        public string Code { get; private set; } = string.Empty;
        public Task SendInitialAccessAsync(Auth auth, string temporaryPassword) => throw new InvalidOperationException("OTP required.");
        public Task SendPasswordResetCodeAsync(Auth auth, string verificationCode)
        {
            Code = verificationCode;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("change")]
    [InlineData("reset")]
    [InlineData("admin")]
    public async Task PasswordChangeRevokesOnlyThatUsersTokensAndPreservesGlobalEpoch(string flow)
    {
        using var f = new Fixture();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey, ["Jwt:DurationInMinutes"] = "15", ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test",
            ["Security:BCryptWorkFactor"] = "4"
        }).Build();
        var hasher = new PasswordHasher(config);
        f.User.Auth.PasswordHash = hasher.Hash("old-password");
        var other = new User { Id = Guid.NewGuid(), FirstName = "Other", LastName = "Test", Status = true, Role = f.User.Role };
        other.Auth = new Auth { Id = other.Id, User = other, Email = "other@example.invalid", PasswordHash = hasher.Hash("unrelated-password") };
        f.Db.Add(other);
        await f.Db.SaveChangesAsync();
        var tokens = new CENS15.V2.Services.TokenService(config, f.Epoch);
        ClaimsPrincipal Issue(Auth auth)
        {
            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.CreateToken(auth).Token);
            Assert.Equal(CredentialVersion.Compute(auth.Id, auth.PasswordHash, JwtKey), token.Claims.Single(c => c.Type == CredentialVersion.Claim).Value);
            Assert.DoesNotContain(auth.PasswordHash, token.Payload.SerializeToJson());
            return new ClaimsPrincipal(new ClaimsIdentity(token.Claims, "Test"));
        }
        var oldPrincipal = Issue(f.User.Auth);
        var unrelatedPrincipal = Issue(other.Auth);
        var epoch = f.Epoch.Current;
        Assert.True(await SessionPrincipalValidator.ValidateAsync(oldPrincipal, f.Db, epoch, JwtKey));
        Assert.True(await SessionPrincipalValidator.ValidateAsync(unrelatedPrincipal, f.Db, epoch, JwtKey));
        var email = new CapturingEmail();
        var service = new AuthService(f.Db, tokens, hasher, email);
        if (flow == "admin")
        {
            var users = new UserService(f.Db, hasher);
            Assert.True(await users.UpdateAsync(f.User.Id, new CENS15_V2.Models.DTOs.UsersDTOs.UpdateUserRequest
            {
                FirstName = f.User.FirstName, LastName = f.User.LastName, RoleId = f.User.RoleId,
                Password = " new-password "
            }));
        }
        else if (flow == "reset")
        {
            await service.RequestPasswordResetAsync(new PasswordResetRequest { Email = f.User.Auth.Email });
            Assert.True(await SessionPrincipalValidator.ValidateAsync(oldPrincipal, f.Db, epoch, JwtKey));
            await service.ResetPasswordAsync(new ResetPasswordRequest { Email = f.User.Auth.Email, Code = email.Code, NewPassword = "new-password" });
        }
        else
        {
            Assert.True(await service.ChangePasswordAsync(f.User.Id,
                new ChangePasswordRequest { CurrentPassword = "old-password", NewPassword = "new-password" }));
        }
        Assert.Equal(epoch, f.Epoch.Current);
        Assert.False(await SessionPrincipalValidator.ValidateAsync(oldPrincipal, f.Db, epoch, JwtKey));
        Assert.True(await SessionPrincipalValidator.ValidateAsync(unrelatedPrincipal, f.Db, epoch, JwtKey));
        Assert.True(await SessionPrincipalValidator.ValidateAsync(Issue(f.User.Auth), f.Db, epoch, JwtKey));
        Assert.False(await SessionPrincipalValidator.ValidateAsync(Issue(f.User.Auth), f.Db, epoch, "different-signing-key"));
        Assert.Null(await service.Login(new LoginRequest { Email = f.User.Auth.Email, Password = "old-password" }));
        var login = await service.Login(new LoginRequest
        {
            Email = " ADMIN@EXAMPLE.INVALID ", Password = flow == "admin" ? " new-password " : "new-password"
        });
        Assert.NotNull(login);
        if (flow == "admin")
            Assert.Null(await service.Login(new LoginRequest { Email = f.User.Auth.Email, Password = "new-password" }));
    }

    [Fact]
    public async Task InactiveLoginIsRejectedBeforePasswordVerificationOrTokenIssuance()
    {
        using var f = new Fixture(); f.User.Status = false; await f.Db.SaveChangesAsync();
        var service = new AuthService(f.Db, new NoTokens(), new PasswordHasher(new ConfigurationBuilder().Build()), new NoEmails());
        Assert.Null(await service.Login(new LoginRequest { Email = f.User.Auth.Email, Password = "anything" }));
        await service.RequestInitialAccessAsync(new InitialAccessRequest { Email = f.User.Auth.Email });
    }

    [Theory]
    [InlineData("/api/transferencias/importar", true)]
    [InlineData("/API/TRANSFERENCIAS/IMPORTAR/", true)]
    [InlineData("/api/transferencias/preview", false)]
    [InlineData("/api/transferencias/importar-extra", false)]
    public async Task ImportEndpointExclusivelyGatesBeforeDownstreamAuthentication(string path, bool writer)
    {
        var gate = new CheckpointGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new CheckpointGateMiddleware(async _ => { entered.SetResult(); await release.Task; });
        var context = new DefaultHttpContext(); context.Request.Path = path;
        var priorRequest = await gate.ReadAsync();
        var request = middleware.InvokeAsync(context, gate);
        Assert.Equal(!writer, entered.Task.IsCompleted);
        priorRequest.Dispose(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var followingRequest = gate.ReadAsync();
        Assert.Equal(!writer, followingRequest.IsCompleted);
        release.SetResult(); await request.WaitAsync(TimeSpan.FromSeconds(5));
        using var following = await followingRequest.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
