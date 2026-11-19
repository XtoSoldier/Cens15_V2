using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CalificacionesDTOs;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Models.DTOs.CertificadoTemplatesDTOs;
using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Models.DTOs.DocentesDTOs;
using CENS15_V2.Models.DTOs.InscripcionesDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class AcademicAccessTests
{
    private static IMapper Mapper() => new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper();
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ClaimsPrincipal Principal(Guid id, string role, params string[] grants) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role) }
            .Concat(grants.Select(g => new Claim(ResponsibilityPolicies.ClaimType, g))), "test"));
    private static void SignIn(ControllerBase controller, ClaimsPrincipal user) => controller.ControllerContext =
        new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

    internal static async Task<IActionResult> Filter(AppDbContext db, ControllerBase controller, ClaimsPrincipal user,
        string method, Dictionary<string, object?> args, Func<Dictionary<string, object?>, Task<IActionResult>> action)
    {
        SignIn(controller, user);
        controller.HttpContext.Request.Method = method;
        var context = new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(context, filters, args, controller);
        ActionExecutedContext? executed = null;
        await new AcademicAccessFilter(db).OnActionExecutionAsync(executing, async () =>
        {
            executed = new ActionExecutedContext(context, filters, controller) { Result = await action(args) };
            return executed;
        });
        return executing.Result ?? executed!.Result!;
    }

    private static async Task<(CursadaMateria Own, CursadaMateria Foreign)> Seed(AppDbContext db, Guid preceptorId)
    {
        var alumno = new Alumno { Nombres = "Alumno", Apellidos = "Compartido" };
        CursadaMateria Attendance(string name, Guid userId) => new()
        {
            Inscripcion = new Inscripcion { Anio = 2026, Alumno = alumno, Curso = new Curso
            {
                CursoNombre = name, Division = "A", Anexo = new Anexo { Nombre = "Centro" },
                Orientacion = new Orientacion { Nombre = "Sociales" },
                Preceptor = new Preceptor { Nombres = name, Apellidos = "Preceptor", UserId = userId }
            } },
            Materia = new Materia { Nombre = name }, MateriaNombre = name,
            Calificacion = new Calificacion { MateriaNombre = name, C1Promedio = 8, CalificacionFinal = 8 }
        };
        var own = Attendance("Propia", preceptorId);
        var foreign = Attendance("Ajena", Guid.NewGuid());
        // Subjects need not determine the security scope: enrollment is authoritative.
        own.Materia.Curso = own.Inscripcion.Curso;
        foreign.Materia.Curso = foreign.Inscripcion.Curso;
        db.AddRange(own, foreign);
        db.CertificadoTemplates.Add(new CertificadoTemplate { Nombre = "Notas", ContenidoHtml = "{{BLOQUE_CALIFICACIONES}}" });
        await db.SaveChangesAsync();
        return (own, foreign);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData(" superADMIN ")]
    public async Task SuperAdminPassesEveryRegisteredAssertionWithoutGrants(string role)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            foreach (var name in ResponsibilityPolicies.All)
                options.AddPolicy(name, p => p.RequireAssertion(c => ResponsibilityPolicies.HasResponsibility(c.User, name)));
        });
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        foreach (var name in ResponsibilityPolicies.All)
            Assert.True((await authorization.AuthorizeAsync(Principal(Guid.NewGuid(), role), null, name)).Succeeded, name);
    }

    [Fact]
    public void PreceptorGetsOnlyMinimumReadPoliciesEvenWithEveryLegacyGrant()
    {
        var user = Principal(Guid.NewGuid(), " Preceptor ", ResponsibilityPolicies.All);
        string[] reads = [ResponsibilityPolicies.AlumnosConsultar, ResponsibilityPolicies.InscripcionesConsultar,
            ResponsibilityPolicies.CursosConsultar, ResponsibilityPolicies.MateriasConsultar,
            ResponsibilityPolicies.CursadasMateriasConsultar, ResponsibilityPolicies.AlumnoDocumentosConsultar,
            ResponsibilityPolicies.TiposDocumentoAlumnoConsultar, ResponsibilityPolicies.CalificacionesConsultar,
            ResponsibilityPolicies.CertificadosConsultar];
        foreach (var policy in ResponsibilityPolicies.All)
            Assert.Equal(reads.Contains(policy), ResponsibilityPolicies.HasResponsibility(user, policy));
        foreach (var read in reads)
            Assert.True(ResponsibilityPolicies.HasResponsibility(Principal(Guid.NewGuid(), "Preceptor"), read));
        Assert.False(ResponsibilityPolicies.HasResponsibility(Principal(Guid.NewGuid(), "NotSuperAdmin"), ResponsibilityPolicies.AlumnosConsultar));
        Assert.False(ResponsibilityPolicies.HasResponsibility(Principal(Guid.NewGuid(), "SuperAdminBackup"), ResponsibilityPolicies.AlumnosConsultar));
        var admin = Principal(Guid.NewGuid(), "Admin", ResponsibilityPolicies.Calificaciones);
        var docente = Principal(Guid.NewGuid(), "Docente", ResponsibilityPolicies.Calificaciones);
        Assert.True(ResponsibilityPolicies.HasResponsibility(admin, ResponsibilityPolicies.CalificacionesEditar));
        Assert.True(ResponsibilityPolicies.HasResponsibility(docente, ResponsibilityPolicies.CalificacionesEditar));
        Assert.False(ResponsibilityPolicies.HasResponsibility(docente, ResponsibilityPolicies.AlumnosConsultar));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidIdentityIsDeniedByPoliciesAndResourceFilter(string id)
    {
        using var db = Database();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id), new Claim(ClaimTypes.Role, "SuperAdmin")], "test"));
        Assert.All(ResponsibilityPolicies.All, p => Assert.False(ResponsibilityPolicies.HasResponsibility(user, p)));
        var controller = new CalificacionesController(new CalificacionService(db, Mapper()));
        Assert.IsType<ForbidResult>(await Filter(db, controller, user, "GET", new() { ["id"] = 1 },
            _ => throw new Exception("Must not execute action")));
        SignIn(controller, user);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Post(new CreateCalificacionRequest())).StatusCode);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "SuperAdmin")]));
        Assert.False(ResponsibilityPolicies.HasResponsibility(anonymous, ResponsibilityPolicies.AlumnosConsultar));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GradeReadsAreScopedByEnrollmentForBothRoutes(bool byCursada)
    {
        using var db = Database();
        var userId = Guid.NewGuid();
        var data = await Seed(db, userId);
        var controller = new CalificacionesController(new CalificacionService(db, Mapper()));
        foreach (var attendance in new[] { data.Own, data.Foreign })
        {
            var args = byCursada ? new Dictionary<string, object?> { ["cursadaMateriaId"] = attendance.Id }
                : new Dictionary<string, object?> { ["id"] = attendance.Calificacion!.Id };
            Task<IActionResult> Read(Dictionary<string, object?> _) => byCursada
                ? controller.GetByCursadaMateria(attendance.Id) : controller.GetById(attendance.Calificacion!.Id);
            var result = await Filter(db, controller, Principal(userId, "Preceptor", ResponsibilityPolicies.Calificaciones), "GET", args, Read);
            if (attendance == data.Own) Assert.IsType<OkObjectResult>(result);
            else Assert.IsType<ForbidResult>(result);
            Assert.IsType<ForbidResult>(await Filter(db, controller, Principal(Guid.NewGuid(), "Preceptor"), "GET", args, Read));
            Assert.IsType<OkObjectResult>(await Filter(db, controller, Principal(Guid.NewGuid(), "SuperAdmin"), "GET", args, Read));
        }
        // Reassignment in DB immediately changes scope, regardless of the unchanged JWT.
        data.Own.Inscripcion.Curso.Preceptor!.UserId = Guid.NewGuid();
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await Filter(db, controller, Principal(userId, "Preceptor"), "GET",
            new() { ["cursadaMateriaId"] = data.Own.Id }, _ => controller.GetByCursadaMateria(data.Own.Id)));
    }

    [Fact]
    public async Task PreceptorNeverWritesAndSuperAdminLinkedToTeacherWritesForeignSubject()
    {
        using var db = Database();
        var id = Guid.NewGuid();
        var data = await Seed(db, id);
        var teacher = new Docente { Nombres = "Teacher", Apellidos = "Linked", UserId = id };
        db.Users.Add(new User { Id = id, Status = true, FirstName = "Teacher", LastName = "Linked", Role = new Role { Name = "SuperAdmin" } });
        db.MateriaDocentes.Add(new MateriaDocente { Docente = teacher, Materia = data.Own.Materia, Activo = true });
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var controller = new CalificacionesController(service);
        var create = new CreateCalificacionRequest { CursadaMateriaId = data.Foreign.Id, C1Promedio = 9 };
        var update = new UpdateCalificacionRequest { CursadaMateriaId = data.Foreign.Id, C1Promedio = 9 };
        SignIn(controller, Principal(id, "Preceptor", ResponsibilityPolicies.CalificacionesCrear, ResponsibilityPolicies.CalificacionesEditar));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Post(create)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Put(data.Foreign.Calificacion!.Id, update)).StatusCode);
        SignIn(controller, Principal(id, "SuperAdmin"));
        Assert.IsType<NoContentResult>(await controller.Put(data.Foreign.Calificacion.Id, update));
        db.Calificaciones.Remove(data.Foreign.Calificacion);
        await db.SaveChangesAsync();
        Assert.IsType<CreatedAtActionResult>(await controller.Post(create));
        Assert.False(await service.CanUserCalificarCursadaAsync(Guid.NewGuid(), data.Own.Id));
        Assert.True(await service.CanUserCalificarCursadaAsync(id, data.Own.Id));
        Assert.False(await service.CanUserCalificarCursadaAsync(id, data.Foreign.Id));
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public async Task MisCursosUsesDatabaseLinkReturnsCursoDtosAndUnlinkedEmpty(string idClaim)
    {
        using var db = Database();
        var id = Guid.NewGuid();
        var data = await Seed(db, id);
        var controller = new PreceptoresController(new PreceptorService(db, Mapper()));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(idClaim, id.ToString()), new Claim(ClaimTypes.Role, "Preceptor")], "test"));
        SignIn(controller, user);
        var course = Assert.Single(Assert.IsType<CursoDto[]>(Assert.IsType<OkObjectResult>(await controller.GetMisCursos(db, Mapper())).Value));
        Assert.Equal(data.Own.Inscripcion.CursoId, course.Id);
        Assert.Equal("Propia", course.Curso);
        SignIn(controller, Principal(Guid.NewGuid(), "Preceptor"));
        Assert.Empty(Assert.IsType<CursoDto[]>(Assert.IsType<OkObjectResult>(await controller.GetMisCursos(db, Mapper())).Value));
        SignIn(controller, new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.IsType<ForbidResult>(await controller.GetMisCursos(db, Mapper()));
        Assert.Equal(ResponsibilityPolicies.CalificacionesConsultar, typeof(PreceptoresController)
            .GetMethod(nameof(PreceptoresController.GetMisCursos))!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Fact]
    public async Task StudentDataAndRelationshipsRemainReadableOutsideAssignedCoursesWithoutEmbeddedGrades()
    {
        using var db = Database();
        var id = Guid.NewGuid();
        var data = await Seed(db, id);
        var outsideStudent = new Alumno { Nombres = "Fuera", Apellidos = "Sin relacion" };
        db.Alumnos.Add(outsideStudent);
        await db.SaveChangesAsync();
        var user = Principal(id, "Preceptor");
        var alumnos = new AlumnosController(new AlumnoService(db, Mapper()));
        SignIn(alumnos, user);
        Assert.True(ResponsibilityPolicies.HasResponsibility(user, ResponsibilityPolicies.AlumnosConsultar));
        var all = Assert.IsAssignableFrom<IEnumerable<AlumnoDto>>(Assert.IsType<OkObjectResult>(await alumnos.Get()).Value);
        Assert.Equal(2, all.Count());
        Assert.IsType<OkObjectResult>(await alumnos.GetById(outsideStudent.Id));
        var inscripciones = new InscripcionesController(new InscripcionService(db, Mapper()));
        SignIn(inscripciones, user);
        var enrollments = Assert.IsAssignableFrom<IEnumerable<InscripcionDto>>(Assert.IsType<OkObjectResult>(
            await inscripciones.GetByAlumno(data.Own.Inscripcion.AlumnoId)).Value);
        Assert.Contains(enrollments, i => i.Id == data.Foreign.InscripcionId);
        var attendance = await new CursadaMateriaService(db, Mapper()).GetByInscripcionIdAsync(data.Foreign.InscripcionId);
        var json = JsonSerializer.Serialize(new { Alumnos = all, Inscripciones = enrollments, Cursadas = attendance });
        Assert.DoesNotContain("C1Promedio", json);
        Assert.DoesNotContain("CalificacionFinal", json);
        Assert.DoesNotContain("CalificacionId", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TeacherGradeProjectionsCannotLeakForeignEnrollments(bool ownRoute)
    {
        using var db = Database();
        var id = Guid.NewGuid();
        var data = await Seed(db, id);
        var teacher = new Docente { Nombres = "Docente", Apellidos = "Mixto", UserId = id };
        db.MateriaDocentes.AddRange(new MateriaDocente { Docente = teacher, Materia = data.Own.Materia, Activo = true },
            new MateriaDocente { Docente = teacher, Materia = data.Foreign.Materia, Activo = true });
        await db.SaveChangesAsync();
        var controller = new DocentesController(new DocenteService(db, Mapper()));
        Task<IActionResult> Read(Dictionary<string, object?> _) => ownRoute
            ? controller.GetMisAlumnosParaCalificar() : controller.GetAlumnosParaCalificar(teacher.Id);
        var result = Assert.IsType<OkObjectResult>(await Filter(db, controller, Principal(id, "Preceptor", ResponsibilityPolicies.Docentes), "GET", new(), Read));
        var items = Assert.IsAssignableFrom<IEnumerable<DocenteMateriaConAlumnosDto>>(result.Value);
        Assert.Equal(data.Own.Id, Assert.Single(Assert.Single(items).Alumnos).CursadaMateriaId);
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("own")]
    [InlineData("foreign")]
    [InlineData("mixed")]
    [InlineData("invalid")]
    public async Task CertificateRenderingNeverExportsForeignGrades(string selection)
    {
        using var db = Database();
        var id = Guid.NewGuid();
        var data = await Seed(db, id);
        var controller = new CertificadosController(new CertificadoTemplateService(db, Mapper()), null!);
        var selected = selection switch { "own" => data.Own.InscripcionId.ToString(), "foreign" => data.Foreign.InscripcionId.ToString(),
            "mixed" => $"{data.Own.InscripcionId},{data.Foreign.InscripcionId}", "invalid" => "not-an-id", _ => null };
        var args = new Dictionary<string, object?> { ["id"] = 1, ["alumnoId"] = data.Own.Inscripcion.AlumnoId, ["inscripcionIds"] = selected };
        Task<IActionResult> Render(Dictionary<string, object?> a) => controller.RenderForAlumno(1, data.Own.Inscripcion.AlumnoId, a["inscripcionIds"] as string);
        var result = await Filter(db, controller, Principal(id, "Preceptor"), "GET", args, Render);
        if (selection is "foreign" or "mixed" or "invalid") Assert.IsType<ForbidResult>(result);
        else
        {
            var rendered = Assert.IsType<RenderedCertificadoTemplateDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Contains("Propia", rendered.Html);
            Assert.DoesNotContain("Ajena", rendered.Html);
        }
        Assert.IsType<ForbidResult>(await Filter(db, controller, Principal(Guid.NewGuid(), "Preceptor"), "GET",
            new() { ["id"] = 1, ["alumnoId"] = data.Own.Inscripcion.AlumnoId, ["inscripcionIds"] = null }, Render));
    }

    [Theory]
    [InlineData(typeof(CheckpointsController))]
    [InlineData(typeof(TransferenciasController))]
    public void AdministrativeExportsAreNotOpenedToOvergrantedPreceptor(Type controller)
    {
        var policy = controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy!;
        Assert.Equal(ResponsibilityPolicies.AdminAccess, policy);
        Assert.False(ResponsibilityPolicies.HasResponsibility(Principal(Guid.NewGuid(), "Preceptor", ResponsibilityPolicies.All), policy));
        Assert.True(ResponsibilityPolicies.HasResponsibility(Principal(Guid.NewGuid(), "SuperAdmin"), policy));
        Assert.True(ResponsibilityPolicies.HasResponsibility(Principal(Guid.NewGuid(), "Admin"), policy));
    }

    [Theory]
    [InlineData(typeof(CalificacionesController))]
    [InlineData(typeof(DocentesController))]
    [InlineData(typeof(CertificadosController))]
    public void GradeBearingControllersRegisterTheResourceFilter(Type controller) => Assert.Equal(typeof(AcademicAccessFilter),
        controller.GetCustomAttribute<TypeFilterAttribute>()!.ImplementationType);

    [Theory]
    [InlineData("Admin", true)]
    [InlineData(" superADMIN ", true)]
    [InlineData("Preceptor", false)]
    [InlineData("NotSuperAdmin", false)]
    public async Task ExportServiceChecksCurrentDatabaseRole(string role, bool allowed)
    {
        using var db = Database();
        var actor = new User { Id = Guid.NewGuid(), FirstName = "Actor", LastName = "Test", Status = true,
            Role = new Role { Name = role } };
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        var service = new TransferenciaService(db);
        if (allowed)
            Assert.Equal("alumnos", (await service.ExportarAsync("alumnos", actorId: actor.Id)).Seccion);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExportarAsync("alumnos", actorId: actor.Id));
        actor = await db.Users.SingleAsync(u => u.Id == actor.Id);
        actor.Status = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExportarAsync("alumnos", actorId: actor.Id));
    }

    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AcademicAccessTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("SuperAdmin", true)]
    [InlineData("Preceptor", false)]
    public async Task CheckpointsRequireCurrentAdministrativeRoleEvenWithOvergrantedClaims(string role, bool allowed)
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "cens-academic-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var db = Database();
            using var storage = new CheckpointStorage(new ConfigurationBuilder().Build(), new EnvironmentStub(root));
            var epoch = new SessionEpoch(storage);
            var actor = new User { Id = Guid.NewGuid(), FirstName = "Actor", LastName = "Test", Status = true,
                Role = new Role { Name = role }, Auth = new Auth { Email = "test@example.invalid", PasswordHash = "unused" } };
            db.Users.Add(actor);
            await db.SaveChangesAsync();
            var user = Principal(actor.Id, role, ResponsibilityPolicies.All);
            ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(SessionEpoch.Claim, epoch.Current));
            var service = new CheckpointService(db, storage, new CheckpointGate(), null!, null!, epoch, new CheckpointPreviews());
            if (allowed) Assert.Empty(await service.ListAsync(user, default));
            else Assert.Equal(403, (await Assert.ThrowsAsync<CheckpointException>(() => service.ListAsync(user, default))).Status);
            actor.Role.Name = "Preceptor";
            await db.SaveChangesAsync();
            Assert.Equal(403, (await Assert.ThrowsAsync<CheckpointException>(() => service.ListAsync(user, default))).Status);
        }
        finally { Directory.Delete(root, true); }
    }
}
