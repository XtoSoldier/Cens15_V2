using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Services;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class AuthOtpTests
{
    private sealed class NoTokens : ITokenService
    {
        public AuthResponse CreateToken(Auth auth) => throw new InvalidOperationException("No token expected.");
    }

    private sealed class FakeEmail : IEmailService
    {
        public List<string> Codes { get; } = [];
        public bool Fail { get; set; }
        public Action? BeforeSend { get; set; }
        public Task SendInitialAccessAsync(Auth auth, string temporaryPassword) =>
            throw new InvalidOperationException("Initial access must use the OTP email method.");
        public Task SendPasswordResetCodeAsync(Auth auth, string verificationCode)
        {
            BeforeSend?.Invoke();
            if (Fail) throw new HttpRequestException("Sensitive provider details: private@example.invalid");
            Codes.Add(verificationCode);
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public DbContextOptions<AppDbContext> Options { get; } = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public AppDbContext Db { get; }
        public PasswordHasher Hasher { get; } = new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Security:BCryptWorkFactor"] = "4" }).Build());
        public FakeEmail Email { get; } = new();
        public Auth Auth { get; }
        public AuthService Service { get; }
        public Fixture()
        {
            Db = new AppDbContext(Options);
            var user = new User { Id = Guid.NewGuid(), FirstName = "OTP", LastName = "Test", Status = true,
                Role = new Role { Id = Guid.NewGuid(), Name = "User" } };
            Auth = new Auth { Id = user.Id, User = user, Email = "otp@example.invalid",
                PasswordHash = Hasher.Hash("old-password"), MustChangePassword = true };
            Db.Auths.Add(Auth);
            Db.SaveChanges();
            Service = new AuthService(Db, new NoTokens(), Hasher, Email);
        }
        public Task Issue(bool initial = false) => initial
            ? Service.RequestInitialAccessAsync(new InitialAccessRequest { Email = " OTP@EXAMPLE.INVALID " })
            : Service.RequestPasswordResetAsync(new PasswordResetRequest { Email = " OTP@EXAMPLE.INVALID " });
        public Task Validate(string code) => Service.ValidatePasswordResetCodeAsync(
            new ValidatePasswordResetCodeRequest { Email = Auth.Email, Code = code });
        public Task Reset(string code, string password = "new-password") => Service.ResetPasswordAsync(
            new ResetPasswordRequest { Email = Auth.Email, Code = code, NewPassword = password });
        public void Dispose() => Db.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothEntryPointsSendOtpWithoutChangingCredentialsAndValidationDoesNotConsume(bool initial)
    {
        using var f = new Fixture();
        var oldHash = f.Auth.PasswordHash;
        var before = DateTime.UtcNow;
        await f.Issue(initial);
        var value = Assert.Single(f.Email.Codes);
        Assert.Matches("^[0-9]{6}$", value);
        var code = Assert.Single(f.Db.PasswordResetCodes);
        Assert.NotEqual(value, code.CodeHash);
        Assert.True(f.Hasher.Verify(value, code.CodeHash));
        Assert.InRange(code.CreatedAt, before, DateTime.UtcNow);
        Assert.Equal(TimeSpan.FromMinutes(15), code.ExpiresAt - code.CreatedAt);
        Assert.Equal(oldHash, f.Auth.PasswordHash);
        Assert.True(f.Auth.MustChangePassword);
        Assert.Null(f.Auth.InitialAccessSentAt);
        await f.Validate(value);
        await f.Validate(value);
        Assert.Null(code.UsedAt);
        Assert.Equal(0, code.Attempts);
        Assert.Equal(oldHash, f.Auth.PasswordHash);
        await f.Reset(value);
        Assert.NotNull(code.UsedAt);
        Assert.True(f.Hasher.Verify("new-password", f.Auth.PasswordHash));
        Assert.False(f.Auth.MustChangePassword);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reset(value, "another-password"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Validate(value));
        Assert.True(f.Hasher.Verify("new-password", f.Auth.PasswordHash));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedSendPreservesPreviousCodeAndCredentialsAndHidesProviderDetails(bool initial)
    {
        using var f = new Fixture();
        await f.Issue();
        var value = f.Email.Codes.Single();
        var previous = f.Db.PasswordResetCodes.Single();
        var hash = f.Auth.PasswordHash;
        f.Email.Fail = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Issue(initial));
        Assert.DoesNotContain("private@example.invalid", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Null(previous.UsedAt);
        Assert.Single(f.Db.PasswordResetCodes);
        Assert.Equal(hash, f.Auth.PasswordHash);
        Assert.True(f.Auth.MustChangePassword);
        // Persisted state and a fresh service must still accept the previous code.
        using var db = new AppDbContext(f.Options);
        await new AuthService(db, new NoTokens(), f.Hasher, f.Email).ValidatePasswordResetCodeAsync(
            new ValidatePasswordResetCodeRequest { Email = f.Auth.Email, Code = value });
    }

    [Fact]
    public async Task ResendRevokesEveryUnusedCodeOnlyAfterSuccessfulEmail()
    {
        using var f = new Fixture();
        await f.Issue(true);
        var previous = f.Db.PasswordResetCodes.Single();
        previous.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        f.Email.BeforeSend = () => Assert.Null(previous.UsedAt);
        await f.Issue();
        Assert.NotNull(previous.UsedAt);
        var latest = f.Db.PasswordResetCodes.OrderByDescending(c => c.Id).First();
        Assert.Null(latest.UsedAt);
        Assert.True(f.Hasher.Verify(f.Email.Codes.Last(), latest.CodeHash));
        await f.Validate(f.Email.Codes.Last());
    }

    [Theory]
    [InlineData("used")]
    [InlineData("expired")]
    [InlineData("attempts")]
    public async Task LatestCodeIncludingUsedRowsNeverFallsBackToAnOlderUnusedCode(string state)
    {
        using var f = new Fixture();
        var now = DateTime.UtcNow;
        f.Db.PasswordResetCodes.AddRange(
            new PasswordResetCode { AuthId = f.Auth.Id, CodeHash = f.Hasher.Hash("123456"),
                CreatedAt = now, ExpiresAt = now.AddMinutes(15) },
            new PasswordResetCode { AuthId = f.Auth.Id, CodeHash = f.Hasher.Hash("654321"),
                CreatedAt = now, ExpiresAt = state == "expired" ? now.AddSeconds(-1) : now.AddMinutes(15),
                UsedAt = state == "used" ? now : null, Attempts = state == "attempts" ? 5 : 0 });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Validate("123456"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reset("123456"));
        Assert.True(f.Hasher.Verify("old-password", f.Auth.PasswordHash));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiveWrongAttemptsPersistAcrossRequestsAndBlockCorrectCode(bool reset)
    {
        using var f = new Fixture();
        await f.Issue();
        var correct = f.Email.Codes.Single();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var db = new AppDbContext(f.Options);
            var service = new AuthService(db, new NoTokens(), f.Hasher, f.Email);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reset
                ? service.ResetPasswordAsync(new ResetPasswordRequest { Email = f.Auth.Email, Code = "000000", NewPassword = "new-password" })
                : service.ValidatePasswordResetCodeAsync(new ValidatePasswordResetCodeRequest { Email = f.Auth.Email, Code = "000000" }));
            using var check = new AppDbContext(f.Options);
            Assert.Equal(attempt, check.PasswordResetCodes.Single().Attempts);
        }
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Validate(correct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reset(correct));
    }

    [Fact]
    public async Task ResetInvalidatesAllOtherCodesButNotOtherUsersCodes()
    {
        using var f = new Fixture();
        await f.Issue();
        var other = new Auth { Id = Guid.NewGuid(), Email = "other@example.invalid", PasswordHash = "other-hash",
            User = new User { FirstName = "Other", LastName = "Test", Status = true, Role = f.Auth.User.Role } };
        other.User.Id = other.Id;
        f.Db.Auths.Add(other);
        var older = new PasswordResetCode { AuthId = f.Auth.Id, CodeHash = f.Hasher.Hash("123456"),
            CreatedAt = DateTime.UtcNow.AddHours(-1), ExpiresAt = DateTime.UtcNow.AddMinutes(1) };
        var unrelated = new PasswordResetCode { AuthId = other.Id, CodeHash = "unrelated", ExpiresAt = DateTime.UtcNow.AddMinutes(15) };
        f.Db.PasswordResetCodes.AddRange(older, unrelated);
        await f.Db.SaveChangesAsync();
        await f.Reset(f.Email.Codes.Single());
        Assert.NotNull(older.UsedAt);
        Assert.Null(unrelated.UsedAt);
        Assert.Equal("other-hash", other.PasswordHash);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted-auth")]
    [InlineData("unknown")]
    public async Task UnknownInactiveAndDeletedAccountsDoNotSendAndCannotValidateOrReset(string state)
    {
        using var f = new Fixture();
        await f.Issue();
        var value = f.Email.Codes.Single();
        if (state == "inactive") f.Auth.User.Status = false;
        if (state == "deleted-auth") f.Db.Auths.Remove(f.Auth);
        if (state == "unknown") f.Auth.Email = "different@example.invalid";
        await f.Db.SaveChangesAsync();
        var email = "otp@example.invalid";
        await f.Service.RequestInitialAccessAsync(new InitialAccessRequest { Email = email });
        await f.Service.RequestPasswordResetAsync(new PasswordResetRequest { Email = email });
        Assert.Single(f.Email.Codes);
        var validation = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ValidatePasswordResetCodeAsync(
            new ValidatePasswordResetCodeRequest { Email = email, Code = value }));
        var reset = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResetPasswordAsync(
            new ResetPasswordRequest { Email = email, Code = value, NewPassword = "new-password" }));
        Assert.Equal("No hay un c\u00f3digo vigente para ese correo.", validation.Message);
        Assert.Equal(validation.Message, reset.Message);
    }

    [Fact]
    public async Task ShortPasswordDoesNotConsumeCodeOrChangeCredentials()
    {
        using var f = new Fixture();
        await f.Issue();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reset(f.Email.Codes.Single(), "short"));
        Assert.Null(f.Db.PasswordResetCodes.Single().UsedAt);
        Assert.True(f.Hasher.Verify("old-password", f.Auth.PasswordHash));
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("012345", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("ABCDEF", false)]
    [InlineData(" 123456", false)]
    [InlineData("\u0661\u0662\u0663\u0664\u0665\u0666", false)]
    public void BothCodeDtosRequireExactlySixAsciiDigits(string code, bool expected)
    {
        object[] requests = [new ValidatePasswordResetCodeRequest { Email = "otp@example.invalid", Code = code },
            new ResetPasswordRequest { Email = "otp@example.invalid", Code = code, NewPassword = "new-password" }];
        foreach (var request in requests)
            Assert.Equal(expected, Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Fact]
    public async Task InitialAccessControllerKeepsEndpointAndRespondsWithCodeNotPassword()
    {
        using var f = new Fixture();
        var result = Assert.IsType<OkObjectResult>(await new AuthController(f.Service)
            .RequestInitialAccess(new InitialAccessRequest { Email = f.Auth.Email }));
        var message = JsonSerializer.SerializeToElement(result.Value).GetProperty("message").GetString()!;
        Assert.Contains("c\u00f3digo de acceso \u00fanico", message);
        Assert.DoesNotContain("clave", message);
    }

    private sealed class EmailHandler : HttpMessageHandler
    {
        public JsonElement Payload { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Payload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task EmailJsOtpTemplateUsesAllThreeCodeVariablesWithoutAnyNetworkCall()
    {
        using var f = new Fixture();
        using var handler = new EmailHandler();
        using var client = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmailJs:ServiceId"] = "fake-service", ["EmailJs:TemplateId"] = "fake-template", ["EmailJs:PublicKey"] = "fake-key"
        }).Build();
        await new EmailJsService(client, config).SendPasswordResetCodeAsync(f.Auth, "123456");
        var parameters = handler.Payload.GetProperty("template_params");
        foreach (var key in new[] { "verification_code", "code", "access_code" })
            Assert.Equal("123456", parameters.GetProperty(key).GetString());
        Assert.Equal(string.Empty, parameters.GetProperty("password").GetString());
        Assert.Equal(string.Empty, parameters.GetProperty("temporary_password").GetString());
        Assert.Equal("fake-template", handler.Payload.GetProperty("template_id").GetString());
    }
}
