using System.Data.Common;
using System.Security.Claims;
using AutoMapper;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class AlumnoSearchTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IMapper Mapper() => new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper();

    private static async Task Seed(AppDbContext db)
    {
        db.Alumnos.AddRange(
            new Alumno { Id = 1, Nombres = "Juan Carlos", Apellidos = "Perez", NumeroDocumento = "30123456" },
            new Alumno { Id = 2, Nombres = "Maria", Apellidos = "Lopez", NumeroDocumento = "40987654" },
            new Alumno { Id = 3, Nombres = "Juan", Apellidos = "Gomez" },
            new Alumno { Id = 4, Nombres = "Juan", Apellidos = "Perez", NumeroDocumento = "30123999", Eliminado = true },
            new Alumno { Id = 5, Nombres = "Ana%_", Apellidos = "Literal", NumeroDocumento = "ABC%_123" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("j")]
    [InlineData("ju")]
    [InlineData("  ju  ")]
    public async Task ShortSearchNeverReturnsAll(string search)
    {
        using var db = Db();
        await Seed(db);
        var service = new AlumnoService(db, Mapper());
        Assert.Empty(await service.GetAllAsync(search: search));
        Assert.Empty(await service.GetAllAsync(includeDeleted: true, search: search));
    }

    [Theory]
    [InlineData("jUa", new[] { 1, 3 })]
    [InlineData("pErEz", new[] { 1 })]
    [InlineData("  MARIA  ", new[] { 2 })]
    [InlineData("123", new[] { 1, 5 })]
    [InlineData("30123456", new[] { 1 })]
    [InlineData("JUAN   perez", new[] { 1 })]
    [InlineData("Perez Juan Carlos", new[] { 1 })]
    [InlineData("juan 30123", new[] { 1 })]
    [InlineData("Juan Lopez", new int[0])]
    [InlineData("inexistente", new int[0])]
    [InlineData("Ana%_", new[] { 5 })]
    [InlineData("abc%_", new[] { 5 })]
    [InlineData("%__", new int[0])]
    [InlineData("Jua%", new int[0])]
    [InlineData("Jua_", new int[0])]
    public async Task SearchMatchesEveryTermLiterallyAcrossFields(string search, int[] expected)
    {
        using var db = Db();
        await Seed(db);
        var results = await new AlumnoService(db, Mapper()).GetAllAsync(search: search);
        Assert.Equal(expected, results.Select(a => a.Id).OrderBy(id => id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task OmittedSearchPreservesAllAndIncludeDeletedBehavior()
    {
        using var db = Db();
        await Seed(db);
        var service = new AlumnoService(db, Mapper());
        Assert.Equal(new[] { 1, 2, 3, 5 }, (await service.GetAllAsync()).Select(a => a.Id).OrderBy(id => id));
        Assert.Equal(5, (await service.GetAllAsync(includeDeleted: true)).Count());
        Assert.Equal(new[] { 1, 4 }, (await service.GetAllAsync(true, "juan perez")).Select(a => a.Id).OrderBy(id => id));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData(" sUpEr AdMiN ", true)]
    [InlineData("Docente", false)]
    [InlineData("", false)]
    public async Task ControllerKeepsAdminGuardWithAndWithoutSearch(string role, bool allowed)
    {
        using var db = Db();
        await Seed(db);
        var controller = Controller(db, role);
        foreach (var search in new string?[] { null, "juan perez", "" })
        {
            var result = await controller.Get(includeDeleted: true, search: search);
            if (allowed)
            {
                var alumnos = Assert.IsAssignableFrom<IEnumerable<AlumnoDto>>(Assert.IsType<OkObjectResult>(result).Value);
                Assert.Equal(search == null ? 5 : search.Length == 0 ? 0 : 2, alumnos.Count());
            }
            else
            {
                Assert.IsType<ForbidResult>(result);
            }
        }
        var policy = Assert.Single(typeof(AlumnosController).GetMethod(nameof(AlumnosController.Get))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(ResponsibilityPolicies.AlumnosConsultar, policy.Policy);
    }

    [Fact]
    public async Task ControllerForwardsSearchAndOnlyOmittedSearchReturnsAll()
    {
        using var db = Db();
        await Seed(db);
        var controller = Controller(db, "Docente");
        var filtered = Assert.IsAssignableFrom<IEnumerable<AlumnoDto>>(
            Assert.IsType<OkObjectResult>(await controller.Get(search: "JUAN Perez")).Value);
        Assert.Equal(1, Assert.Single(filtered).Id);
        var all = Assert.IsAssignableFrom<IEnumerable<AlumnoDto>>(
            Assert.IsType<OkObjectResult>(await controller.Get()).Value);
        Assert.Equal(4, all.Count());
        foreach (var query in new[] { "?search=", "?search", "?search=%20%20" })
        {
            controller.Request.QueryString = new QueryString(query);
            var empty = Assert.IsAssignableFrom<IEnumerable<AlumnoDto>>(
                Assert.IsType<OkObjectResult>(await controller.Get(search: null)).Value);
            Assert.Empty(empty);
        }
    }

    [Fact]
    public async Task ActualServiceQueryTranslatesWithNpgsqlWithoutConnecting()
    {
        var capture = new CaptureQuery();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused;Password=unused")
            .AddInterceptors(new SuppressConnection(), capture).Options;
        using var db = new AppDbContext(options);
        await Assert.ThrowsAsync<QueryCapturedException>(() => new AlumnoService(db, Mapper()).GetAllAsync(search: "JUAN %_1", cursoId: 10, anio: 2026));

        var sql = capture.Sql!;
        Assert.Contains("WHERE", sql);
        Assert.Contains("lower(", sql);
        Assert.Contains("\"Nombres\"", sql);
        Assert.Contains("\"Apellidos\"", sql);
        Assert.Contains("\"NumeroDocumento\"", sql);
        Assert.Contains("\"Eliminado\"", sql);
        Assert.Contains("AND", sql);
        Assert.Contains("EXISTS", sql);
        Assert.Contains("\"CursoId\"", sql);
        Assert.Contains("\"Anio\"", sql);
        Assert.Contains("%juan%", capture.Parameters);
        Assert.Contains("%\\%\\_1%", capture.Parameters);
    }

    [Fact]
    public async Task CourseAndYearMatchTheSameEnrollmentWithoutDuplicatesOrDeletedStudents()
    {
        using var db = Db();
        await Seed(db);
        db.Inscripciones.AddRange(
            new Inscripcion { AlumnoId = 1, CursoId = 10, Anio = 2024 },
            new Inscripcion { AlumnoId = 1, CursoId = 20, Anio = 2026 },
            new Inscripcion { AlumnoId = 2, CursoId = 10, Anio = 2026 },
            new Inscripcion { AlumnoId = 2, CursoId = 10, Anio = 2026 },
            new Inscripcion { AlumnoId = 4, CursoId = 10, Anio = 2026 });
        await db.SaveChangesAsync();
        var service = new AlumnoService(db, Mapper());
        Assert.Equal(2, Assert.Single(await service.GetAllAsync(cursoId: 10, anio: 2026)).Id);
        Assert.Equal(1, Assert.Single(await service.GetAllAsync(cursoId: 10, anio: 2024)).Id);
        Assert.Equal(new[] { 1, 2 }, (await service.GetAllAsync(cursoId: 10)).Select(a => a.Id).OrderBy(id => id));
        Assert.Equal(new[] { 1, 2 }, (await service.GetAllAsync(anio: 2026)).Select(a => a.Id).OrderBy(id => id));
        Assert.Equal(new[] { 2, 4 }, (await service.GetAllAsync(includeDeleted: true, cursoId: 10, anio: 2026)).Select(a => a.Id).OrderBy(id => id));
        Assert.Empty(await service.GetAllAsync(search: "Juan", cursoId: 10, anio: 2026));
        Assert.Equal(2, Assert.Single(await service.GetAllAsync(search: "MARIA", cursoId: 10, anio: 2026)).Id);
        Assert.Empty(await service.GetAllAsync(cursoId: 999, anio: 2026));
        Assert.Empty(await service.GetAllAsync(cursoId: 10, anio: 2023));
        var controller = Controller(db, "Preceptor");
        var result = Assert.IsAssignableFrom<IEnumerable<AlumnoDto>>(Assert.IsType<OkObjectResult>(
            await controller.Get(cursoId: 10, anio: 2026)).Value);
        Assert.Equal(2, Assert.Single(result).Id);
    }

    private static AlumnosController Controller(AppDbContext db, string role) => new(new AlumnoService(db, Mapper()))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test")) } }
    };

    private sealed class QueryCapturedException : Exception { }

    private sealed class SuppressConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class CaptureQuery : DbCommandInterceptor
    {
        public string? Sql { get; private set; }
        public string[] Parameters { get; private set; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // Connection opening is suppressed and execution stops after Npgsql has generated SQL and parameters.
            Sql = command.CommandText;
            Parameters = command.Parameters.Cast<DbParameter>().Select(p => p.Value?.ToString() ?? "").ToArray();
            throw new QueryCapturedException();
        }
    }
}
