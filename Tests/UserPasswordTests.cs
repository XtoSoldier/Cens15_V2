using System.Text.Json;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Models.DTOs.UsersDTOs;
using CENS15_V2.Services;
using CENS15_V2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class UserPasswordTests : IDisposable
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly PasswordHasher hasher = new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Security:BCryptWorkFactor"] = "4" }).Build());
    private readonly User user;
    private readonly UserService service;
    private readonly UsersController controller;

    public UserPasswordTests()
    {
        user = new User { Id = Guid.NewGuid(), FirstName = "Original", LastName = "Test", Status = true,
            Role = new Role { Id = Guid.NewGuid(), Name = "User" } };
        user.Auth = new Auth { Id = user.Id, User = user, Email = "test@example.invalid",
            PasswordHash = hasher.Hash("old-password"), MustChangePassword = true };
        db.Add(user);
        db.SaveChanges();
        service = new UserService(db, hasher);
        controller = new UsersController(service);
    }

    private UpdateUserRequest Request(string? password) => new()
    {
        FirstName = "Changed", LastName = "Updated", RoleId = user.RoleId, Password = password
    };

    [Fact]
    public async Task PutBindsPasswordPersistsHashAndNeverReturnsCredentials()
    {
        var request = JsonSerializer.Deserialize<UpdateUserRequest>(
            JsonSerializer.Serialize(Request(" new-password ")))!;
        Assert.IsType<NoContentResult>(await controller.Put(user.Id, request));
        var id = user.Id;
        db.ChangeTracker.Clear();
        var auth = await db.Auths.SingleAsync(a => a.Id == id);
        Assert.StartsWith("$2", auth.PasswordHash);
        Assert.True(hasher.Verify(" new-password ", auth.PasswordHash));
        Assert.False(hasher.Verify("old-password", auth.PasswordHash));
        Assert.False(hasher.Verify("new-password", auth.PasswordHash));
        Assert.False(auth.MustChangePassword);
        var response = JsonSerializer.Serialize(await service.GetByIdAsync(id));
        Assert.DoesNotContain(auth.PasswordHash, response);
        Assert.DoesNotContain("Password", response);
        Assert.DoesNotContain("new-password", response);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task EmptyOrOmittedPasswordPreservesCredentials(string? password)
    {
        var hash = user.Auth.PasswordHash;
        Assert.IsType<NoContentResult>(await controller.Put(user.Id, Request(password)));
        Assert.Equal(hash, user.Auth.PasswordHash);
        Assert.True(user.Auth.MustChangePassword);
        Assert.Equal("Changed", user.FirstName);
    }

    [Theory]
    [InlineData("short")]
    [InlineData(" ")]
    public async Task InvalidPasswordRejectsBeforeAnyTrackedOrPersistedMutation(string password)
    {
        var hash = user.Auth.PasswordHash;
        var result = Assert.IsType<BadRequestObjectResult>(await controller.Put(user.Id, Request(password)));
        Assert.Contains("6 caracteres", Assert.IsType<string>(result.Value));
        Assert.Equal("Original", user.FirstName);
        Assert.Equal(hash, user.Auth.PasswordHash);
        Assert.True(user.Auth.MustChangePassword);
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        db.ChangeTracker.Clear();
        Assert.Equal("Original", (await db.Users.SingleAsync()).FirstName);
        Assert.Equal(hash, (await db.Auths.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task MissingAuthIsExplicitFailureWithoutMutatingUser()
    {
        db.Auths.Remove(user.Auth);
        await db.SaveChangesAsync();
        var result = Assert.IsType<BadRequestObjectResult>(await controller.Put(user.Id, Request("new-password")));
        Assert.Contains("no tiene credenciales", Assert.IsType<string>(result.Value));
        Assert.Equal("Original", user.FirstName);
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
    }

    [Fact]
    public async Task MissingUserDoesNotReportSuccess() =>
        Assert.IsType<NotFoundResult>(await controller.Put(Guid.NewGuid(), Request("new-password")));

    private sealed class TestTokens : ITokenService
    {
        public AuthResponse CreateToken(Auth auth) => new() { UserId = auth.Id.ToString() };
    }

    private sealed class NoEmails : IEmailService
    {
        public Task SendInitialAccessAsync(Auth auth, string temporaryPassword) => throw new InvalidOperationException();
        public Task SendPasswordResetCodeAsync(Auth auth, string verificationCode) => throw new InvalidOperationException();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" initial-password ")]
    public async Task RegistrationKeepsOptionalInitialPasswordAndNormalizesOnlyEmail(string? password)
    {
        var authService = new AuthService(db, new TestTokens(), hasher, new NoEmails());
        var response = await authService.Register(new RegisterRequest
        {
            Nombres = "New", Apellido = "User", Email = " NEW@EXAMPLE.INVALID ", Password = password
        });
        var auth = await db.Auths.SingleAsync(a => a.Id == Guid.Parse(response.UserId));
        Assert.Equal("NEW@EXAMPLE.INVALID", auth.Email);
        Assert.Equal(string.IsNullOrWhiteSpace(password), auth.MustChangePassword);
        if (!string.IsNullOrWhiteSpace(password))
        {
            Assert.True(hasher.Verify(password, auth.PasswordHash));
            Assert.False(hasher.Verify(password.Trim(), auth.PasswordHash));
            Assert.NotNull(await authService.Login(new LoginRequest { Email = " new@example.invalid ", Password = password }));
        }
        await Assert.ThrowsAsync<Exception>(() => authService.Register(new RegisterRequest
        {
            Nombres = "Duplicate", Apellido = "User", Email = " new@example.invalid "
        }));
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [Fact]
    public async Task ShortRegistrationPasswordRejectsBeforeTrackingOrSavingNewUser()
    {
        var authService = new AuthService(db, new TestTokens(), hasher, new NoEmails());
        await Assert.ThrowsAsync<InvalidOperationException>(() => authService.Register(new RegisterRequest
        {
            Nombres = "New", Apellido = "User", Email = "new@example.invalid", Password = "short"
        }));
        Assert.Single(db.Users);
        Assert.Single(db.Auths);
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
    }

    public void Dispose() => db.Dispose();
}
