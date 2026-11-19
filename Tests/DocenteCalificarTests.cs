using System.Reflection;
using System.Security.Claims;
using AutoMapper;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CalificacionesDTOs;
using CENS15_V2.Models.DTOs.DocentesDTOs;
using CENS15_V2.Models.DTOs.CursadasMateriasDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class DocenteCalificarTests
{
    private static IMapper Mapper() => new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper();
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ClaimsPrincipal Principal(Guid userId, string idClaim = ClaimTypes.NameIdentifier) => new(new ClaimsIdentity(
        [new Claim(idClaim, userId.ToString()), new Claim(ClaimTypes.Role, "Docente")], "test"));
    private static void SignIn(ControllerBase controller, ClaimsPrincipal user) =>
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

    [Theory]
    [InlineData(nameof(DocentesController.GetMiPerfil))]
    [InlineData(nameof(DocentesController.GetMisAlumnosParaCalificar))]
    public void OwnEndpointsUseGradingPermissionsWithoutOpeningAdministrativeLookups(string action)
    {
        var user = Principal(Guid.NewGuid());
        var policy = typeof(DocentesController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy!;
        Assert.Equal(ResponsibilityPolicies.CalificacionesConsultar, policy);
        Assert.True(ResponsibilityPolicies.HasResponsibility(user, policy));
        var adminPolicy = typeof(DocentesController).GetMethod(nameof(DocentesController.GetByUserId))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Policy!;
        Assert.Equal(ResponsibilityPolicies.DocentesConsultar, adminPolicy);
        Assert.False(ResponsibilityPolicies.HasResponsibility(user, adminPolicy));
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public async Task LinkedTeacherSeesOwnCoursesAndSubjectsAndCanCreateAndEditTheirGrades(string idClaim)
    {
        using var db = Database();
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Status = true, FirstName = "Ana", LastName = "Perez", Role = new Role { Name = "Docente" } });
        var own = new Docente { Nombres = "Ana", Apellidos = "Perez", UserId = userId };
        var other = new Docente { Nombres = "Otro", Apellidos = "Docente", UserId = Guid.NewGuid() };
        var course = new Curso { CursoNombre = "1", Division = "F", Anexo = new Anexo { Nombre = "Centro" },
            Orientacion = new Orientacion { Nombre = "Sociales" } };
        var subjects = new[] { "Historia", "Lengua", "Inactiva", "Otro docente" }
            .Select(name => new Materia { Nombre = name, Curso = course }).ToArray();
        var enrollment = new Inscripcion { Anio = 2026, Curso = course,
            Alumno = new Alumno { Nombres = "Alumno", Apellidos = "Prueba" } };
        var attendances = subjects.Select(subject => new CursadaMateria
            { Materia = subject, MateriaNombre = subject.Nombre, Inscripcion = enrollment }).ToArray();
        db.CursadasMaterias.AddRange(attendances);
        db.MateriaDocentes.AddRange(subjects.Select((subject, i) => new MateriaDocente
            { Materia = subject, Docente = i == 3 ? other : own, Activo = i != 2 }));
        await db.SaveChangesAsync();

        var controller = new DocentesController(new DocenteService(db, Mapper()));
        var principal = Principal(userId, idClaim);
        SignIn(controller, principal);
        var profile = Assert.IsType<DocenteDto>(Assert.IsType<OkObjectResult>(await controller.GetMiPerfil()).Value);
        Assert.Equal(own.Id, profile.Id);
        Assert.Equal(userId, profile.UserId);
        var courses = Assert.IsAssignableFrom<IEnumerable<DocenteMateriaConAlumnosDto>>(
            Assert.IsType<OkObjectResult>(await controller.GetMisAlumnosParaCalificar()).Value).ToArray();
        Assert.Equal(new[] { "Historia", "Lengua" }, courses.Select(m => m.MateriaNombre));
        Assert.All(courses, m => {
            Assert.Equal("1 F", m.CursoLabel);
            Assert.Equal(2026, m.Anio);
            Assert.Equal(enrollment.AlumnoId, Assert.Single(m.Alumnos).AlumnoId);
        });

        var grading = new CalificacionesController(new CalificacionService(db, Mapper()));
        SignIn(grading, principal);
        Assert.True(ResponsibilityPolicies.HasResponsibility(principal, ResponsibilityPolicies.CalificacionesCrear));
        Assert.True(ResponsibilityPolicies.HasResponsibility(principal, ResponsibilityPolicies.CalificacionesEditar));
        var created = Assert.IsType<CalificacionDto>(Assert.IsType<CreatedAtActionResult>(await grading.Post(
            new CreateCalificacionRequest { CursadaMateriaId = attendances[0].Id, C1Promedio = 6, C2Promedio = 8 })).Value);
        Assert.IsType<NoContentResult>(await grading.Put(created.Id,
            new UpdateCalificacionRequest { CursadaMateriaId = attendances[0].Id, C1Promedio = 8, C2Promedio = 8 }));
        Assert.Equal(8m, (await db.Calificaciones.FindAsync(created.Id))!.CalificacionFinal);
        foreach (var attendance in attendances.Skip(2))
        {
            var denied = Assert.IsType<ObjectResult>(await grading.Post(
                new CreateCalificacionRequest { CursadaMateriaId = attendance.Id, C1Promedio = 6, C2Promedio = 8 }));
            Assert.Equal(403, denied.StatusCode);
        }
        Assert.Single(db.Calificaciones);
    }

    [Fact]
    public async Task MissingLinkReturns404WithoutBorrowingAnotherTeachersProfile()
    {
        using var db = Database();
        db.Docentes.Add(new Docente { Nombres = "Otro", Apellidos = "Docente", UserId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var controller = new DocentesController(new DocenteService(db, Mapper()));
        SignIn(controller, Principal(Guid.NewGuid()));
        var result = Assert.IsType<NotFoundObjectResult>(await controller.GetMiPerfil());
        Assert.Contains("vinculado", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task MissingOrInvalidIdentityIsUnauthorized()
    {
        using var db = Database();
        var controller = new DocentesController(new DocenteService(db, Mapper()));
        foreach (var id in new[] { "", "not-a-guid" })
        {
            SignIn(controller, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id)], "test")));
            Assert.IsType<UnauthorizedResult>(await controller.GetMiPerfil());
        }
    }

    [Fact]
    public async Task TeacherWithoutGrantsHasOnlyMinimumGradingPoliciesAndStillHonorsOtherGrants()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options => {
            foreach (var name in ResponsibilityPolicies.All)
                options.AddPolicy(name, p => p.RequireAssertion(c => ResponsibilityPolicies.HasResponsibility(c.User, name)));
        });
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal = Principal(Guid.NewGuid());
        string[] minimum = [ResponsibilityPolicies.CalificacionesConsultar, ResponsibilityPolicies.CalificacionesCrear,
            ResponsibilityPolicies.CalificacionesEditar];
        foreach (var policy in ResponsibilityPolicies.All)
            Assert.Equal(minimum.Contains(policy), (await authorization.AuthorizeAsync(principal, null, policy)).Succeeded);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ResponsibilityPolicies.ClaimType, ResponsibilityPolicies.MateriasConsultar));
        Assert.True((await authorization.AuthorizeAsync(principal, null, ResponsibilityPolicies.MateriasConsultar)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, null, ResponsibilityPolicies.MateriasEditar)).Succeeded);
    }

    private static async Task<(User User, Docente Teacher, CursadaMateria Own, CursadaMateria Foreign)> Seed(AppDbContext db)
    {
        var user = new User { Id = Guid.NewGuid(), Status = true, FirstName = "Ana", LastName = "Perez", Role = new Role { Name = "Docente" } };
        var teacher = new Docente { Nombres = "Ana", Apellidos = "Perez", User = user };
        CursadaMateria Attendance(string division) {
            var course = new Curso { CursoNombre = "1", Division = division, Anexo = new Anexo { Nombre = "Centro" },
                Orientacion = new Orientacion { Nombre = "Sociales" } };
            return new CursadaMateria { Materia = new Materia { Nombre = "Historia", Curso = course }, MateriaNombre = "Historia",
                Inscripcion = new Inscripcion { Anio = 2026, Curso = course, Alumno = new Alumno { Nombres = division, Apellidos = "Alumno" } },
                Calificacion = new Calificacion { MateriaNombre = "Historia", C1Promedio = 8 } };
        }
        var own = Attendance("F");
        var foreign = Attendance("G");
        db.AddRange(own, foreign);
        db.MateriaDocentes.Add(new MateriaDocente { Docente = teacher, Materia = own.Materia, Activo = true });
        await db.SaveChangesAsync();
        return (user, teacher, own, foreign);
    }

    [Theory]
    [InlineData("own", true)]
    [InlineData("foreign", false)]
    [InlineData("inactive-assignment", false)]
    [InlineData("inactive-user", false)]
    [InlineData("unlinked", false)]
    [InlineData("wrong-course", false)]
    public async Task TeacherScopeIsRecheckedForEachIndividualWriteAndRead(string scenario, bool allowed)
    {
        using var db = Database();
        var data = await Seed(db);
        var principal = Principal(data.User.Id);
        var attendance = scenario == "foreign" ? data.Foreign : data.Own;
        if (scenario == "inactive-assignment") (await db.MateriaDocentes.SingleAsync()).Activo = false;
        if (scenario == "inactive-user") data.User.Status = false;
        if (scenario == "unlinked") data.Teacher.UserId = null;
        if (scenario == "wrong-course") data.Own.Inscripcion.Curso = data.Foreign.Inscripcion.Curso;
        await db.SaveChangesAsync();
        var grading = new CalificacionesController(new CalificacionService(db, Mapper()));
        SignIn(grading, principal);
        var gradeId = attendance.Calificacion!.Id;
        var update = await grading.Put(gradeId, new UpdateCalificacionRequest { CursadaMateriaId = attendance.Id, C1Nota1 = 9 });
        if (allowed) Assert.IsType<NoContentResult>(update);
        else Assert.Equal(403, Assert.IsType<ObjectResult>(update).StatusCode);
        foreach (var byAttendance in new[] { false, true })
        {
            var args = byAttendance ? new Dictionary<string, object?> { ["cursadaMateriaId"] = attendance.Id }
                : new Dictionary<string, object?> { ["id"] = gradeId };
            var read = await AcademicAccessTests.Filter(db, grading, principal, "GET", args,
                _ => byAttendance ? grading.GetByCursadaMateria(attendance.Id) : grading.GetById(gradeId));
            if (allowed) Assert.IsType<OkObjectResult>(read);
            else Assert.IsType<ForbidResult>(read);
        }
        db.Calificaciones.Remove(attendance.Calificacion);
        await db.SaveChangesAsync();
        var create = await grading.Post(new CreateCalificacionRequest { CursadaMateriaId = attendance.Id, C1Nota1 = 7 });
        if (allowed) Assert.IsType<CreatedAtActionResult>(create);
        else Assert.Equal(403, Assert.IsType<ObjectResult>(create).StatusCode);
    }

    [Fact]
    public async Task SheetsSeparateYearsIncludeMissingAttendancesAndEmptySubjectsWithoutWriting()
    {
        using var db = Database();
        var data = await Seed(db);
        var prior = new Inscripcion { Anio = 2025, Alumno = data.Own.Inscripcion.Alumno, Curso = data.Own.Inscripcion.Curso };
        var newSubject = new Materia { Nombre = "Nueva", Curso = data.Own.Materia.Curso };
        var empty = new Materia { Nombre = "Vacia", Curso = new Curso { CursoNombre = "3", Division = "A",
            Anexo = data.Own.Materia.Curso.Anexo, Orientacion = data.Own.Materia.Curso.Orientacion } };
        db.AddRange(prior, newSubject, empty);
        db.MateriaDocentes.AddRange(new MateriaDocente { Docente = data.Teacher, Materia = newSubject, Activo = true },
            new MateriaDocente { Docente = data.Teacher, Materia = empty, Activo = true });
        await db.SaveChangesAsync();
        var service = new DocenteService(db, Mapper());
        var sheets = (await service.GetMateriasActivasConAlumnosByUserIdAsync(data.User.Id)).ToArray();
        Assert.Equal(5, sheets.Length);
        var current = Assert.Single(sheets, s => s.MateriaId == data.Own.MateriaId && s.Anio == 2026);
        Assert.Equal(data.Own.Id, Assert.Single(current.Alumnos).CursadaMateriaId);
        var older = Assert.Single(sheets, s => s.MateriaId == data.Own.MateriaId && s.Anio == 2025);
        Assert.Equal(prior.Id, Assert.Single(older.Alumnos).InscripcionId);
        Assert.Equal(0, Assert.Single(older.Alumnos).CursadaMateriaId);
        Assert.All(sheets.Where(s => s.MateriaId == newSubject.Id), s => Assert.Equal(0, Assert.Single(s.Alumnos).CursadaMateriaId));
        var emptySheet = Assert.Single(sheets, s => s.MateriaId == empty.Id);
        Assert.Equal(DateTime.Today.Year, emptySheet.Anio);
        Assert.Empty(emptySheet.Alumnos);
        Assert.DoesNotContain(sheets, s => s.MateriaId == data.Foreign.MateriaId);
        Assert.Equal(2, await db.CursadasMaterias.CountAsync());
        Assert.False(db.ChangeTracker.HasChanges());

        var controller = new DocentesController(service);
        SignIn(controller, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, " superADMIN ")], "test")));
        var all = Assert.IsAssignableFrom<IEnumerable<DocenteMateriaConAlumnosDto>>(
            Assert.IsType<OkObjectResult>(await controller.GetMateriasParaCalificar()).Value).ToArray();
        Assert.Equal(6, all.Length);
        Assert.Contains(all, s => s.MateriaId == data.Foreign.MateriaId);
        SignIn(controller, Principal(data.User.Id));
        Assert.IsType<ForbidResult>(await controller.GetMateriasParaCalificar());
    }

    [Theory]
    [InlineData("own", true)]
    [InlineData("super", true)]
    [InlineData("foreign", false)]
    [InlineData("inactive", false)]
    [InlineData("unlinked", false)]
    [InlineData("preceptor", false)]
    [InlineData("other-course", false)]
    public async Task MissingAttendanceIsCreatedOnlyByExplicitAuthorizedOperation(string scenario, bool allowed)
    {
        using var db = Database();
        var data = await Seed(db);
        var enrollment = new Inscripcion { Anio = 2026, Curso = data.Own.Inscripcion.Curso,
            Alumno = new Alumno { Nombres = "Nuevo", Apellidos = "Alumno" } };
        db.Inscripciones.Add(enrollment);
        if (scenario == "inactive") (await db.MateriaDocentes.SingleAsync()).Activo = false;
        if (scenario == "unlinked") data.Teacher.UserId = null;
        await db.SaveChangesAsync();
        var principal = Principal(scenario == "foreign" ? Guid.NewGuid() : data.User.Id);
        if (scenario is "super" or "preceptor") principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", data.User.Id.ToString()), new Claim(ClaimTypes.Role, scenario == "super" ? "SuperAdmin" : "Preceptor")], "test"));
        var controller = new CursadasMateriasController(new CursadaMateriaService(db, Mapper()));
        SignIn(controller, principal);
        var request = new CreateCursadaMateriaRequest { InscripcionId = enrollment.Id,
            MateriaId = scenario == "other-course" ? data.Foreign.MateriaId : data.Own.MateriaId };
        var result = await controller.EnsureParaCalificar(request, db);
        if (allowed)
        {
            var created = Assert.IsType<CursadaMateriaDto>(Assert.IsType<OkObjectResult>(result).Value);
            var retry = Assert.IsType<CursadaMateriaDto>(Assert.IsType<OkObjectResult>(await controller.EnsureParaCalificar(request, db)).Value);
            Assert.Equal(created.Id, retry.Id);
            Assert.Equal(3, await db.CursadasMaterias.CountAsync());
            var grading = new CalificacionesController(new CalificacionService(db, Mapper()));
            SignIn(grading, principal);
            Assert.IsType<CreatedAtActionResult>(await grading.Post(new CreateCalificacionRequest { CursadaMateriaId = created.Id, C1Nota1 = 8 }));
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
            Assert.Equal(2, await db.CursadasMaterias.CountAsync());
        }
    }

    [Fact]
    public async Task GenericActiveAdministrativeGrantsRemainAllowedWithoutTeacherLink()
    {
        using var db = Database();
        var data = await Seed(db);
        var admin = new User { Id = Guid.NewGuid(), Status = true, FirstName = "Custom", LastName = "Admin", Role = new Role { Name = "CustomGrader" } };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", admin.Id.ToString()),
            new Claim(ClaimTypes.Role, "CustomGrader"), new Claim(ResponsibilityPolicies.ClaimType, ResponsibilityPolicies.Calificaciones)], "test"));
        Assert.True(ResponsibilityPolicies.HasResponsibility(principal, ResponsibilityPolicies.CalificacionesEditar));
        var controller = new CalificacionesController(new CalificacionService(db, Mapper()));
        SignIn(controller, principal);
        Assert.IsType<NoContentResult>(await controller.Put(data.Foreign.Calificacion!.Id,
            new UpdateCalificacionRequest { CursadaMateriaId = data.Foreign.Id, C1Nota1 = 9 }));
    }

    [Theory]
    [InlineData("own")]
    [InlineData("inactive-assignment")]
    [InlineData("inactive-user")]
    [InlineData("unlinked")]
    public async Task OwnSheetNeverReturnsOtherTeachersOrRevokedAssignments(string scenario)
    {
        using var db = Database();
        var data = await Seed(db);
        var principal = Principal(data.User.Id);
        if (scenario == "inactive-assignment") (await db.MateriaDocentes.SingleAsync()).Activo = false;
        if (scenario == "inactive-user") data.User.Status = false;
        if (scenario == "unlinked") data.Teacher.UserId = null;
        await db.SaveChangesAsync();
        var controller = new DocentesController(new DocenteService(db, Mapper()));
        var result = await AcademicAccessTests.Filter(db, controller, principal, "GET", new(), _ => controller.GetMisAlumnosParaCalificar());
        if (scenario == "inactive-user") Assert.IsType<ForbidResult>(result);
        else
        {
            var sheets = Assert.IsAssignableFrom<IEnumerable<DocenteMateriaConAlumnosDto>>(Assert.IsType<OkObjectResult>(result).Value).ToArray();
            if (scenario == "own") Assert.Equal(data.Own.MateriaId, Assert.Single(sheets).MateriaId);
            else Assert.Empty(sheets);
        }
        var other = new Docente { Nombres = "Otro", Apellidos = "Docente" };
        db.MateriaDocentes.Add(new MateriaDocente { Docente = other, Materia = data.Foreign.Materia, Activo = true });
        await db.SaveChangesAsync();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ResponsibilityPolicies.ClaimType, ResponsibilityPolicies.DocentesConsultar));
        var foreign = await AcademicAccessTests.Filter(db, controller, principal, "GET", new(), _ => controller.GetAlumnosParaCalificar(other.Id));
        if (scenario == "inactive-user") Assert.IsType<ForbidResult>(foreign);
        else Assert.Empty(Assert.IsAssignableFrom<IEnumerable<DocenteMateriaConAlumnosDto>>(Assert.IsType<OkObjectResult>(foreign).Value));
    }

    [Fact]
    public async Task MissingResourcesRemain404AndAdministrativeGrantsDoNotOpenSuperSheet()
    {
        using var db = Database();
        var data = await Seed(db);
        var principal = Principal(data.User.Id);
        var grading = new CalificacionesController(new CalificacionService(db, Mapper()));
        var result = await AcademicAccessTests.Filter(db, grading, principal, "GET", new() { ["id"] = int.MaxValue },
            _ => grading.GetById(int.MaxValue));
        Assert.IsType<NotFoundResult>(result);
        var attendance = new CursadasMateriasController(new CursadaMateriaService(db, Mapper()));
        SignIn(attendance, principal);
        Assert.IsType<NotFoundResult>(await attendance.EnsureParaCalificar(
            new CreateCursadaMateriaRequest { InscripcionId = int.MaxValue, MateriaId = data.Own.MateriaId }, db));
        var teachers = new DocentesController(new DocenteService(db, Mapper()));
        SignIn(teachers, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", data.User.Id.ToString()),
            new Claim(ClaimTypes.Role, "Admin"), new Claim(ResponsibilityPolicies.ClaimType, ResponsibilityPolicies.Calificaciones)], "test")));
        Assert.IsType<ForbidResult>(await teachers.GetMateriasParaCalificar());
    }
}
