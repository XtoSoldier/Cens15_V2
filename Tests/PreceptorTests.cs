using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.PreceptoresDTOs;
using CENS15_V2.Models.DTOs.RolesDTOs;
using CENS15_V2.Models.DTOs.UsersDTOs;
using CENS15_V2.Models.DTOs.Transferencias;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class PreceptorTests : IDisposable
{
    private readonly DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly AppDbContext db;
    private readonly PreceptorService service;
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid preceptorRoleId = Guid.NewGuid();
    private readonly Guid otherRoleId = Guid.NewGuid();

    public PreceptorTests()
    {
        db = new AppDbContext(options);
        service = new PreceptorService(db, new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper());
        db.Roles.AddRange(new Role { Id = preceptorRoleId, Name = "  pReCePtOr  " },
            new Role { Id = otherRoleId, Name = "User" });
        db.Users.Add(new User { Id = userId, FirstName = "Ana", LastName = "Perez", RoleId = preceptorRoleId, Status = true });
        db.Auths.Add(new Auth { Id = userId, Email = "ana@example.com", PasswordHash = "test-only" });
        db.Anexos.Add(new Anexo { Id = 1, Nombre = "Centro" });
        db.Orientaciones.Add(new Orientacion { Id = 1, Nombre = "Sociales", NombreCorto = "SOC" });
        db.Cursos.AddRange(Enumerable.Range(1, 3).Select(id => new Curso
        {
            Id = id, CursoNombre = id.ToString(), Division = "A", AnexoId = 1, OrientacionId = 1,
            Semipresencial = id == 2
        }));
        db.SaveChanges();
    }

    private static GuardarPreceptorRequest Request(params int[] cursos) => new()
    {
        Nombres = " Ana ", Apellidos = " Perez ", Email = "   ", CursoIds = cursos.ToList()
    };

    [Fact]
    public async Task CreateAndReadReturnExistingCourseContractWithoutInternalVersion()
    {
        var request = Request(2, 1);
        request.UserId = userId;
        request.Email = " ana@example.com ";
        var created = await service.CreateAsync(request);
        Assert.Equal("Ana", created.Nombres);
        Assert.Equal("Perez", created.Apellidos);
        Assert.Equal("ana@example.com", created.Email);
        Assert.Equal(userId, created.UserId);
        Assert.Equal(new[] { 1, 2 }, created.Cursos.Select(c => c.Id));
        var item = (await service.GetAllAsync()).Single();
        var json = JsonSerializer.SerializeToElement(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(new[] { "id", "nombres", "apellidos", "email", "userId", "cursos" }, json.EnumerateObject().Select(p => p.Name));
        var course = json.GetProperty("cursos")[1];
        Assert.Equal(1, course.GetProperty("id_anexo").GetInt32());
        Assert.Equal(1, course.GetProperty("id_orientacion").GetInt32());
        Assert.Equal("SOC", course.GetProperty("orientacionNombreCorto").GetString());
        Assert.Equal("Centro", course.GetProperty("anexoNombre").GetString());
        Assert.True(course.GetProperty("semipresencial").GetBoolean());
        Assert.Equal("Ana Perez", course.GetProperty("preceptorNombre").GetString());
        Assert.NotNull(await service.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task CourseReadsIncludeOnlyTheirAssignedPreceptorWithoutTrackingOrWrites()
    {
        await service.CreateAsync(Request(1));
        var other = Request(2);
        other.Nombres = "Maria Elena";
        other.Apellidos = "Lopez Gomez";
        await service.CreateAsync(other);
        using var fresh = new AppDbContext(options);
        var cursos = new CursoService(fresh, new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper());
        var all = (await cursos.GetAllAsync()).ToDictionary(c => c.Id);
        Assert.Equal("Ana Perez", all[1].PreceptorNombre);
        Assert.Equal("Maria Elena Lopez Gomez", all[2].PreceptorNombre);
        Assert.Null(all[3].PreceptorNombre);
        Assert.Equal("Ana Perez", (await cursos.GetByIdAsync(1))!.PreceptorNombre);
        Assert.Null((await cursos.GetByIdAsync(3))!.PreceptorNombre);
        Assert.Null(await cursos.GetByIdAsync(99));
        Assert.Empty(fresh.ChangeTracker.Entries());
        var json = JsonSerializer.SerializeToElement(all[3], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("preceptorNombre").ValueKind);
    }

    [Fact]
    public async Task CourseReadsReflectRenamesAndUnlinkingWithoutBorrowingAnotherCoursesPreceptor()
    {
        var first = await service.CreateAsync(Request(1, 2));
        var other = Request(3);
        other.Nombres = "Otro";
        await service.CreateAsync(other);
        var renamed = Request(2);
        renamed.Nombres = "Nombre Actualizado";
        await service.UpdateAsync(first.Id, renamed);
        using var fresh = new AppDbContext(options);
        var cursos = new CursoService(fresh, new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper());
        Assert.Null((await cursos.GetByIdAsync(1))!.PreceptorNombre);
        Assert.Equal("Nombre Actualizado Perez", (await cursos.GetByIdAsync(2))!.PreceptorNombre);
        Assert.Equal("Otro Perez", (await cursos.GetByIdAsync(3))!.PreceptorNombre);
        await service.DeleteAsync(first.Id);
        Assert.Null((await cursos.GetByIdAsync(2))!.PreceptorNombre);
    }

    [Fact]
    public async Task CreatingAndEditingCoursesDoesNotAssignOrChangePreceptors()
    {
        var preceptor = await service.CreateAsync(Request(1));
        var cursos = new CursoService(db, new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper());
        var created = await cursos.CreateAsync(new CENS15_V2.Models.DTOs.CursosDTOs.CreateCursoRequest
        {
            Curso = "1\u00b0", Division = "B", IdOrientacion = 1, IdAnexo = 1
        });
        Assert.Null(created.PreceptorNombre);
        await cursos.UpdateAsync(1, new CENS15_V2.Models.DTOs.CursosDTOs.UpdateCursoRequest
        {
            Curso = "1\u00b0", Division = "A", IdOrientacion = 1, IdAnexo = 1
        });
        Assert.Equal(preceptor.Id, (await db.Cursos.FindAsync(1))!.PreceptorId);
        Assert.Equal("Ana Perez", (await cursos.GetByIdAsync(1))!.PreceptorNombre);
    }

    [Fact]
    public async Task OptionalUserEmailAndEmptyCoursesAreAccepted()
    {
        var item = await service.CreateAsync(Request());
        Assert.Null(item.Email);
        Assert.Null(item.UserId);
        Assert.Empty(item.Cursos);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("surname")]
    [InlineData("email")]
    [InlineData("duplicate")]
    [InlineData("missing-course")]
    [InlineData("null-courses")]
    [InlineData("missing-user")]
    [InlineData("wrong-role")]
    public async Task InvalidRequestsDoNotMutateTrackedEntities(string kind)
    {
        var created = await service.CreateAsync(Request(1));
        var request = Request(2);
        switch (kind)
        {
            case "name": request.Nombres = null!; break;
            case "surname": request.Apellidos = "  "; break;
            case "email": request.Email = "invalid"; break;
            case "duplicate": request.CursoIds = [2, 2]; break;
            case "missing-course": request.CursoIds = [99]; break;
            case "null-courses": request.CursoIds = null!; break;
            case "missing-user": request.UserId = Guid.NewGuid(); break;
            case "wrong-role":
                (await db.Users.FindAsync(userId))!.RoleId = otherRoleId;
                await db.SaveChangesAsync();
                request.UserId = userId;
                break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(created.Id, request));
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Equal("Ana", (await db.Preceptores.FindAsync(created.Id))!.Nombres);
        Assert.Equal(created.Id, (await db.Cursos.FindAsync(1))!.PreceptorId);
        Assert.Null((await db.Cursos.FindAsync(2))!.PreceptorId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request));
        Assert.Single(db.Preceptores);
    }

    [Fact]
    public async Task OccupiedCoursesAndUsersCannotBeStolen()
    {
        var request = Request(1);
        request.UserId = userId;
        var first = await service.CreateAsync(request);
        var second = await service.CreateAsync(Request(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Request(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(second.Id, Request(1)));
        request.CursoIds = [];
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(second.Id, request));
        Assert.Equal(first.Id, (await db.Cursos.FindAsync(1))!.PreceptorId);
        Assert.Equal(second.Id, (await db.Cursos.FindAsync(2))!.PreceptorId);
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
    }

    [Fact]
    public async Task PutReplacesCoursesAndCanUnlinkUserAndClearEverything()
    {
        var request = Request(1, 2);
        request.UserId = userId;
        var item = await service.CreateAsync(request);
        Assert.True(await service.UpdateAsync(item.Id, Request(2, 3)));
        Assert.Null((await db.Cursos.FindAsync(1))!.PreceptorId);
        Assert.Equal(new[] { 2, 3 }, (await service.GetByIdAsync(item.Id))!.Cursos.Select(c => c.Id));
        Assert.Null((await service.GetByIdAsync(item.Id))!.UserId);
        Assert.True(await service.UpdateAsync(item.Id, Request()));
        Assert.All(await db.Cursos.ToListAsync(), c => Assert.Null(c.PreceptorId));
        Assert.Empty((await service.GetByIdAsync(item.Id))!.Cursos);
    }

    [Fact]
    public async Task DeleteFreesCoursesAndPreservesUser()
    {
        var request = Request(1, 2);
        request.UserId = userId;
        var item = await service.CreateAsync(request);
        Assert.True(await service.DeleteAsync(item.Id));
        Assert.Null(await service.GetByIdAsync(item.Id));
        Assert.Equal(3, await db.Cursos.CountAsync());
        Assert.All(await db.Cursos.ToListAsync(), c => Assert.Null(c.PreceptorId));
        Assert.NotNull(await db.Users.FindAsync(userId));
        Assert.False(await service.DeleteAsync(item.Id));
        Assert.False(await service.UpdateAsync(item.Id, Request()));
    }

    [Fact]
    public async Task OptionsIncludeLinkedUsersAndOccupiedCoursesButExcludeOtherRoles()
    {
        var request = Request(1);
        request.UserId = userId;
        await service.CreateAsync(request);
        db.Users.Add(new User { Id = Guid.NewGuid(), FirstName = "Otro", LastName = "Rol", RoleId = otherRoleId });
        await db.SaveChangesAsync();
        var options = await service.GetOpcionesAsync();
        Assert.Equal(userId, Assert.Single(options.Usuarios).Id);
        Assert.Equal("ana@example.com", options.Usuarios[0].Email);
        Assert.Equal(3, options.Cursos.Count);
    }

    [Fact]
    public async Task UserAndRoleGuardsRejectBeforeAnyMutationAndUserDeletionUnlinks()
    {
        var request = Request(1);
        request.UserId = userId;
        var item = await service.CreateAsync(request);
        var users = new UserService(db, new PasswordHasher(new ConfigurationBuilder().Build()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => users.UpdateAsync(userId,
            new UpdateUserRequest { FirstName = "Changed", RoleId = otherRoleId }));
        Assert.Equal("Ana", (await db.Users.FindAsync(userId))!.FirstName);
        var roles = new RoleService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => roles.UpdateAsync(preceptorRoleId,
            new UpdateRoleRequest { Name = "Changed" }));
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.True(await roles.UpdateAsync(preceptorRoleId, new UpdateRoleRequest { Name = "Preceptor" }));
        Assert.True(await users.UpdateAsync(userId, new UpdateUserRequest
        {
            FirstName = "Ana", LastName = "Perez", RoleId = preceptorRoleId
        }));
        Assert.True(await users.DeleteAsync(userId));
        Assert.Null((await service.GetByIdAsync(item.Id))!.UserId);
        Assert.Equal(item.Id, (await db.Cursos.FindAsync(1))!.PreceptorId);
    }

    [Fact]
    public async Task InvalidResponsibilitiesDoNotRenameTrackedRole()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RoleService(db).UpdateAsync(otherRoleId,
            new UpdateRoleRequest { Name = "Changed", ResponsibilityIds = [Guid.NewGuid()] }));
        Assert.Equal("User", (await db.Roles.FindAsync(otherRoleId))!.Name);
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
    }

    [Fact]
    public async Task TransferCannotChangeLinkedUsersRole()
    {
        var request = Request();
        request.UserId = userId;
        await service.CreateAsync(request);
        var transfer = new TransferenciaService(db);
        var package = await transfer.ExportarAsync("usuarios");
        package = package with { Datos = package.Datos with
        {
            Roles = [new RolTransferencia("User", [])],
            Usuarios = [new UsuarioTransferencia("ana@example.com", "Changed", "Perez", true, "User")]
        } };
        var report = await transfer.ProcesarAsync(package, "actualizar");
        Assert.False(report.PuedeImportar);
        Assert.Contains(report.Errores, e => e.Contains("preceptor"));
        Assert.Equal(preceptorRoleId, (await db.Users.FindAsync(userId))!.RoleId);
    }

    [Fact]
    public async Task ConcurrentCourseAssignmentIsRejectedEvenWhenOriginalFkWasNull()
    {
        using var second = new AppDbContext(options);
        var firstCourse = await db.Cursos.FindAsync(1);
        var secondCourse = await second.Cursos.FindAsync(1);
        firstCourse!.PreceptorId = 10;
        await db.SaveChangesAsync();
        secondCourse!.PreceptorId = 20;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        using var fresh = new AppDbContext(options);
        Assert.Equal(10, (await fresh.Cursos.FindAsync(1))!.PreceptorId);
    }

    [Fact]
    public async Task ConcurrentCourseOccupancyReturnsControlledConflictWithoutStealing()
    {
        var first = await service.CreateAsync(Request());
        var other = await service.CreateAsync(Request());
        using var second = new AppDbContext(options);
        await second.Cursos.FindAsync(1);
        var secondService = new PreceptorService(second,
            new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper());
        await service.UpdateAsync(first.Id, Request(1));
        await Assert.ThrowsAsync<PreceptorConflictException>(() => secondService.UpdateAsync(other.Id, Request(1)));
        Assert.Empty(second.ChangeTracker.Entries());
        using var fresh = new AppDbContext(options);
        Assert.Equal(first.Id, (await fresh.Cursos.FindAsync(1))!.PreceptorId);
    }

    [Fact]
    public async Task ConcurrentPreceptorEditReturnsControlledConflictAndClearsRejectedWork()
    {
        var item = await service.CreateAsync(Request());
        using var second = new AppDbContext(options);
        var secondService = new PreceptorService(second,
            new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper());
        await second.Preceptores.FindAsync(item.Id);
        await service.UpdateAsync(item.Id, Request());
        var controller = new PreceptoresController(secondService);
        Assert.IsType<ConflictObjectResult>(await controller.Put(item.Id, Request()));
        Assert.Empty(second.ChangeTracker.Entries());
    }

    [Fact]
    public void RelationalMetadataEnforcesCardinalitySetNullAndOptimisticConcurrency()
    {
        using var relational = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only;Username=unused;Password=unused").Options);
        var preceptor = relational.Model.FindEntityType(typeof(Preceptor))!;
        Assert.Contains(preceptor.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == "UserId");
        Assert.True(preceptor.FindProperty("UserId")!.IsNullable);
        Assert.True(preceptor.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Equal(DeleteBehavior.SetNull, preceptor.GetForeignKeys().Single().DeleteBehavior);
        var course = relational.Model.FindEntityType(typeof(Curso))!;
        Assert.True(course.FindProperty("PreceptorId")!.IsNullable);
        Assert.True(course.FindProperty("PreceptorId")!.IsConcurrencyToken);
        var fk = course.GetForeignKeys().Single(k => k.PrincipalEntityType.ClrType == typeof(Preceptor));
        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);
        Assert.False(fk.IsUnique);
        Assert.Contains(new CheckpointModel(relational).Entities, e => e.ClrType == typeof(Preceptor));
    }

    [Fact]
    public async Task SeedMakesRoleAvailableWithoutGrantingItPermissions()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:BCryptWorkFactor"] = "4"
        }).Build();
        await DbSeeder.Seed(db, new PasswordHasher(config));
        Assert.Single(await db.Roles.Where(r => r.Name.Trim().ToLower() == "preceptor").ToListAsync());
        Assert.Empty(await db.Set<RoleResponsibility>().Where(r => r.RoleId == preceptorRoleId).ToListAsync());
        Assert.Contains(await db.Responsibilities.ToListAsync(), r => r.Name == ResponsibilityPolicies.PreceptoresConsultar);
    }

    [Fact]
    public void EveryEndpointIsAuthenticatedAndAdministrativeActionsUsePreceptorPermissions()
    {
        var type = typeof(PreceptoresController);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        var policies = new Dictionary<string, string>
        {
            ["Get"] = ResponsibilityPolicies.PreceptoresConsultar,
            ["GetById"] = ResponsibilityPolicies.PreceptoresConsultar,
            ["GetOpciones"] = ResponsibilityPolicies.PreceptoresConsultar,
            ["GetMisCursos"] = ResponsibilityPolicies.CalificacionesConsultar,
            ["Post"] = ResponsibilityPolicies.PreceptoresCrear,
            ["Put"] = ResponsibilityPolicies.PreceptoresEditar,
            ["Delete"] = ResponsibilityPolicies.PreceptoresEliminar
        };
        foreach (var (name, policy) in policies)
        {
            var method = type.GetMethod(name)!;
            Assert.Equal(policy, Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>()).Policy);
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Contains(policy, ResponsibilityPolicies.All);
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ResponsibilityPolicies.ClaimType,
            ResponsibilityPolicies.Preceptores)], "test"));
        Assert.True(ResponsibilityPolicies.HasResponsibility(principal, ResponsibilityPolicies.PreceptoresEditar));
        Assert.False(ResponsibilityPolicies.HasResponsibility(principal, ResponsibilityPolicies.UsuariosConsultar));
    }

    public void Dispose() => db.Dispose();
}
