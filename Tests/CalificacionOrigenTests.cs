using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Helper;
using CENS15_V2.Migrations;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CalificacionesDTOs;
using CENS15_V2.Security;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace CENS15_V2.Tests;

public class CalificacionOrigenTests
{
    private static IMapper Mapper() => new MapperConfiguration(c => c.AddProfile<AutoMapperProfile>()).CreateMapper();
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CursadaMateria Attendance(string name = "Historia") => new()
    {
        MateriaNombre = name,
        Materia = new Materia { Nombre = name, Curso = new Curso { CursoNombre = "1", Division = "A",
            Anexo = new Anexo { Nombre = "Centro" }, Orientacion = new Orientacion { Nombre = "Sociales" } } },
        Inscripcion = new Inscripcion { Anio = 2026, Alumno = new Alumno { Nombres = "Prueba", Apellidos = "Alumno" } }
    };

    private static CreateCalificacionRequest Equivalence(int id) => new()
    {
        CursadaMateriaId = id, Estado = EstadoMateria.AprobadoPorEquivalencia,
        DisposicionEquivalencia = "  Texto libre <script>alert('x')</script> & sin formato  ",
        C1Nota1 = 1.1m, C1Nota2 = 2.2m, C1Nota3 = 3.3m, C2Nota1 = 4.4m, C2Nota2 = 5.5m, C2Nota3 = 6.6m,
        C1Promedio = 5, C2Promedio = 8.25m, C2Condicion = CondicionNota.SinCalificar,
        RecuperacionDiciembre = 7.25m, DiciembreCondicion = CondicionNota.Ausente,
        RecuperacionMarzo = 9.25m, MarzoCondicion = CondicionNota.SinCalificar,
        PromedioAnual = 10, CalificacionFinal = 10
    };

    private static UpdateCalificacionRequest Update(CalificacionDto grade) => new()
    {
        CursadaMateriaId = grade.CursadaMateriaId, Estado = grade.Estado,
        C1Nota1 = grade.C1Nota1, C1Nota2 = grade.C1Nota2, C1Nota3 = grade.C1Nota3,
        C2Nota1 = grade.C2Nota1, C2Nota2 = grade.C2Nota2, C2Nota3 = grade.C2Nota3,
        C1Promedio = grade.C1Promedio, C2Promedio = grade.C2Promedio,
        C1Condicion = grade.C1Condicion, C2Condicion = grade.C2Condicion,
        RecuperacionDiciembre = grade.RecuperacionDiciembre, RecuperacionMarzo = grade.RecuperacionMarzo,
        DiciembreCondicion = grade.DiciembreCondicion, MarzoCondicion = grade.MarzoCondicion,
        RecuperacionExcepcional = grade.RecuperacionExcepcional
    };

    [Fact]
    public void EnumAndMigrationContractHasOnlyTwoColumnsAndNoDataChanges()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Enum.GetValues<EstadoMateria>().Select(e => (int)e));
        var migration = new AddCalificacionOrigen();
        Assert.Equal(2, migration.UpOperations.Count);
        var columns = migration.UpOperations.Cast<AddColumnOperation>().ToDictionary(c => c.Name);
        Assert.All(columns.Values, c => Assert.Equal("Calificaciones", c.Table));
        Assert.False(columns["DesdePase"].IsNullable);
        Assert.Equal(false, columns["DesdePase"].DefaultValue);
        Assert.Equal("boolean", columns["DesdePase"].ColumnType);
        Assert.True(columns["DisposicionEquivalencia"].IsNullable);
        Assert.Equal("text", columns["DisposicionEquivalencia"].ColumnType);
        Assert.Equal(2, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, op => Assert.IsType<DropColumnOperation>(op));
        var contact = migration.TargetModel.FindEntityType(typeof(AlumnoContacto).FullName!)!;
        Assert.True(contact.FindProperty("Email")!.IsNullable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public void EquivalenceRequiresNonblankDisposition(string? disposition)
    {
        var grade = new Calificacion { Estado = EstadoMateria.AprobadoPorEquivalencia, DisposicionEquivalencia = disposition };
        Assert.Throws<InvalidOperationException>(() => CalificacionRules.Apply(grade));
    }

    [Fact]
    public void EquivalenceAcceptsFreeLongTextWithoutInventingNumericGrade()
    {
        var text = new string('x', 20000) + " <>& \" '";
        var grade = new Calificacion { Estado = EstadoMateria.AprobadoPorEquivalencia,
            DisposicionEquivalencia = "  " + text + "  ", PromedioAnual = 9, CalificacionFinal = 10 };
        CalificacionRules.Apply(grade);
        Assert.Equal(text, grade.DisposicionEquivalencia);
        Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, grade.Estado);
        Assert.Null(grade.PromedioAnual);
        Assert.Null(grade.CalificacionFinal);
    }

    [Theory]
    [InlineData(nameof(Calificacion.C1Condicion))]
    [InlineData(nameof(Calificacion.C2Condicion))]
    [InlineData(nameof(Calificacion.DiciembreCondicion))]
    [InlineData(nameof(Calificacion.MarzoCondicion))]
    public void EquivalenceStillValidatesEveryCondition(string field)
    {
        var grade = new Calificacion { Estado = EstadoMateria.AprobadoPorEquivalencia, DisposicionEquivalencia = "Libre" };
        typeof(Calificacion).GetProperty(field)!.SetValue(grade, (CondicionNota)99);
        Assert.Throws<InvalidOperationException>(() => CalificacionRules.Apply(grade));
    }

    [Theory]
    [InlineData(nameof(Calificacion.C1Nota1))]
    [InlineData(nameof(Calificacion.C1Nota2))]
    [InlineData(nameof(Calificacion.C1Nota3))]
    [InlineData(nameof(Calificacion.C2Nota1))]
    [InlineData(nameof(Calificacion.C2Nota2))]
    [InlineData(nameof(Calificacion.C2Nota3))]
    [InlineData(nameof(Calificacion.C1Promedio))]
    [InlineData(nameof(Calificacion.C2Promedio))]
    [InlineData(nameof(Calificacion.RecuperacionDiciembre))]
    [InlineData(nameof(Calificacion.RecuperacionMarzo))]
    public void EquivalenceStillValidatesRawNotes(string field)
    {
        foreach (var invalid in new[] { -1m, 11m })
        {
            var grade = new Calificacion { Estado = EstadoMateria.AprobadoPorEquivalencia, DisposicionEquivalencia = "Libre" };
            typeof(Calificacion).GetProperty(field)!.SetValue(grade, (decimal?)invalid);
            Assert.Throws<InvalidOperationException>(() => CalificacionRules.Apply(grade));
        }
    }

    [Theory]
    [InlineData(EstadoMateria.EnCurso)]
    [InlineData(EstadoMateria.Aprobado)]
    [InlineData(EstadoMateria.AprobadoPorEquivalencia)]
    [InlineData((EstadoMateria)99)]
    public void EquivalenceRejectsInvalidExceptionalChoice(EstadoMateria choice)
    {
        var grade = new Calificacion { Estado = EstadoMateria.AprobadoPorEquivalencia,
            DisposicionEquivalencia = "Libre", RecuperacionExcepcional = choice };
        Assert.Throws<InvalidOperationException>(() => CalificacionRules.Apply(grade));
    }

    [Fact]
    public async Task EquivalenceRoundTripsRawValuesAndMetadataThroughBothGetsAndUpdate()
    {
        using var db = Database();
        var attendance = Attendance();
        db.Add(attendance);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var request = Equivalence(attendance.Id);
        var created = await service.CreateAsync(request);
        db.ChangeTracker.Clear();
        var loaded = (await service.GetByIdAsync(created.Id))!;
        Assert.Equal(request.DisposicionEquivalencia!.Trim(), loaded.DisposicionEquivalencia);
        Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, loaded.Estado);
        Assert.False(loaded.DesdePase);
        Assert.Null(loaded.PromedioAnual);
        Assert.Null(loaded.CalificacionFinal);
        Assert.Equal(8.25m, loaded.C2Promedio);
        Assert.Equal(7.25m, loaded.RecuperacionDiciembre);
        Assert.Equal(9.25m, loaded.RecuperacionMarzo);
        var update = Update(loaded); // Null/omitted metadata retains disposition while still A/E.
        Assert.True(await service.UpdateAsync(created.Id, update));
        db.ChangeTracker.Clear();
        var refreshed = (await service.GetByCursadaMateriaIdAsync(attendance.Id))!;
        Assert.Equal(JsonSerializer.Serialize(loaded), JsonSerializer.Serialize(refreshed));
        Assert.Equal(new decimal?[] { 1.1m, 2.2m, 3.3m, 4.4m, 5.5m, 6.6m },
            new[] { refreshed.C1Nota1, refreshed.C1Nota2, refreshed.C1Nota3, refreshed.C2Nota1, refreshed.C2Nota2, refreshed.C2Nota3 });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingEquivalenceRequiresChoiceAndRejectedWritesCannotFlush(bool pass)
    {
        using var db = Database();
        var attendance = Attendance();
        db.Add(attendance);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var created = await service.CreateAsync(Equivalence(attendance.Id));
        var update = Update(created);
        update.Estado = EstadoMateria.Aprobado;
        update.DesdePase = pass;
        update.PromedioAnual = 10;
        update.CalificacionFinal = 10;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(created.Id, update));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(8.25m, (await service.GetByIdAsync(created.Id))!.C2Promedio);
        Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, (await service.GetByIdAsync(created.Id))!.Estado);
        update.RecuperacionExcepcional = EstadoMateria.RecuperaPrimerCuatrimestre;
        Assert.True(await service.UpdateAsync(created.Id, update));
        db.ChangeTracker.Clear();
        var normal = (await service.GetByIdAsync(created.Id))!;
        Assert.Equal(EstadoMateria.RecuperaPrimerCuatrimestre, normal.Estado);
        Assert.Equal(5m, normal.PromedioAnual);
        Assert.Null(normal.CalificacionFinal);
        Assert.Null(normal.DisposicionEquivalencia);
        Assert.Equal(pass, normal.DesdePase);
        Assert.Null(normal.C2Promedio);
        Assert.Equal(0m, normal.RecuperacionDiciembre);
        Assert.Null(normal.RecuperacionMarzo);
        Assert.Equal(6.6m, normal.C2Nota3);
    }

    [Fact]
    public async Task LegacyClientCannotAccidentallyRemoveEquivalenceAndExplicitRemovalRecalculatesEmptyRaw()
    {
        using var db = Database();
        var attendance = Attendance();
        db.Add(attendance);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var created = await service.CreateAsync(new CreateCalificacionRequest { CursadaMateriaId = attendance.Id,
            Estado = EstadoMateria.AprobadoPorEquivalencia, DisposicionEquivalencia = "Libre" });
        var oldClient = new UpdateCalificacionRequest { CursadaMateriaId = attendance.Id,
            Estado = EstadoMateria.Aprobado, PromedioAnual = 10, CalificacionFinal = 10 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(created.Id, oldClient));
        await db.SaveChangesAsync();
        Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, (await service.GetByIdAsync(created.Id))!.Estado);
        oldClient.DesdePase = false;
        Assert.True(await service.UpdateAsync(created.Id, oldClient));
        var removed = (await service.GetByIdAsync(created.Id))!;
        Assert.Equal(EstadoMateria.EnCurso, removed.Estado);
        Assert.Null(removed.PromedioAnual);
        Assert.Null(removed.CalificacionFinal);
        Assert.Null(removed.DisposicionEquivalencia);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalOrPassCanBecomeEquivalenceOnlyWithCompatibleMetadata(bool originallyPass)
    {
        using var db = Database();
        var attendance = Attendance();
        db.Add(attendance);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var created = await service.CreateAsync(new CreateCalificacionRequest { CursadaMateriaId = attendance.Id,
            DesdePase = originallyPass, C1Promedio = 7.5m, C2Promedio = 8.5m, RecuperacionMarzo = 9.25m });
        var update = Update(created);
        update.Estado = EstadoMateria.AprobadoPorEquivalencia;
        update.DisposicionEquivalencia = "  Nueva disposicion libre  ";
        if (originallyPass)
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(created.Id, update));
        update.DesdePase = false;
        Assert.True(await service.UpdateAsync(created.Id, update));
        db.ChangeTracker.Clear();
        var loaded = (await service.GetByIdAsync(created.Id))!;
        Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, loaded.Estado);
        Assert.Equal("Nueva disposicion libre", loaded.DisposicionEquivalencia);
        Assert.Equal(7.5m, loaded.C1Promedio);
        Assert.Equal(8.5m, loaded.C2Promedio);
        Assert.Equal(9.25m, loaded.RecuperacionMarzo);
        Assert.Null(loaded.CalificacionFinal);
        Assert.Null(loaded.PromedioAnual);
        var controller = Controller(service, Guid.NewGuid());
        Assert.Equal(loaded.DisposicionEquivalencia, Assert.IsType<CalificacionDto>(
            Assert.IsType<OkObjectResult>(await controller.GetById(created.Id)).Value).DisposicionEquivalencia);
        Assert.False(Assert.IsType<CalificacionDto>(Assert.IsType<OkObjectResult>(
            await controller.GetByCursadaMateria(attendance.Id)).Value).DesdePase);
    }

    [Theory]
    [InlineData(EstadoMateria.RecuperaPrimerCuatrimestre)]
    [InlineData(EstadoMateria.RecuperaSegundoCuatrimestre)]
    [InlineData(EstadoMateria.RecuperaAmbos)]
    public void EquivalencePreservesValidExceptionalChoiceAndHistoricalAbsentValues(EstadoMateria choice)
    {
        var grade = new Calificacion { Estado = EstadoMateria.AprobadoPorEquivalencia,
            DisposicionEquivalencia = "Libre", C1Promedio = 8.25m, C1Condicion = CondicionNota.Ausente,
            C2Promedio = 5, RecuperacionExcepcional = choice };
        CalificacionRules.Apply(grade);
        Assert.Equal(8.25m, grade.C1Promedio);
        Assert.Equal(CondicionNota.Ausente, grade.C1Condicion);
        Assert.Equal(choice, grade.RecuperacionExcepcional);
        Assert.Equal(EstadoMateria.AprobadoPorEquivalencia, grade.Estado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PassIsOnlyMetadataAndNullablePutPreservesIt(bool? pass)
    {
        using var db = Database();
        var attendance = Attendance();
        db.Add(attendance);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var created = await service.CreateAsync(new CreateCalificacionRequest { CursadaMateriaId = attendance.Id,
            DesdePase = true, C1Promedio = 5, C2Promedio = 7, DisposicionEquivalencia = "Must clear" });
        Assert.Equal(EstadoMateria.Aprobado, created.Estado);
        Assert.Equal(6m, created.CalificacionFinal);
        Assert.Null(created.DisposicionEquivalencia);
        var update = Update(created);
        update.DesdePase = pass;
        await service.UpdateAsync(created.Id, update);
        db.ChangeTracker.Clear();
        var loaded = (await service.GetByIdAsync(created.Id))!;
        Assert.Equal(pass ?? true, loaded.DesdePase);
        Assert.Equal(EstadoMateria.Aprobado, loaded.Estado);
        Assert.Equal(6m, loaded.CalificacionFinal);
    }

    [Fact]
    public async Task HistoricApprovalIsNotReclassifiedAndOrphanDispositionCannotAuthorizeNewEquivalence()
    {
        using var db = Database();
        var attendance = Attendance();
        attendance.Calificacion = new Calificacion { Estado = EstadoMateria.Aprobado,
            PromedioAnual = 7.25m, CalificacionFinal = 8.5m, DisposicionEquivalencia = "Orphan historical text" };
        db.Add(attendance);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var loaded = (await service.GetByIdAsync(attendance.Calificacion.Id))!;
        Assert.Equal(EstadoMateria.Aprobado, loaded.Estado);
        Assert.Equal(8.5m, loaded.CalificacionFinal);
        var update = Update(loaded);
        update.Estado = EstadoMateria.AprobadoPorEquivalencia;
        update.DesdePase = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(loaded.Id, update));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(EstadoMateria.Aprobado, (await service.GetByIdAsync(loaded.Id))!.Estado);
    }

    [Fact]
    public async Task TeacherAndCertificateExposeMetadataWithEscapedDispositionAndPlainPassLegend()
    {
        using var db = Database();
        var attendance = Attendance("Historia <img src=x onerror=alert(1)>");
        attendance.Inscripcion.Curso = attendance.Materia.Curso;
        var teacher = new Docente { Nombres = "Prueba", Apellidos = "Docente" };
        db.Add(attendance);
        db.Add(new MateriaDocente { Docente = teacher, Materia = attendance.Materia, Activo = true });
        var template = new CertificadoTemplate { Nombre = "Boletin", ContenidoHtml = "{{BLOQUE_CALIFICACIONES}}" };
        db.Add(template);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var created = await service.CreateAsync(Equivalence(attendance.Id));
        db.ChangeTracker.Clear();
        var projected = Assert.Single(Assert.Single(await new DocenteService(db, Mapper()).GetMateriasConAlumnosAsync(teacher.Id)).Alumnos);
        Assert.Equal(5, projected.Estado);
        Assert.Equal(created.DisposicionEquivalencia, projected.DisposicionEquivalencia);
        Assert.False(projected.DesdePase);
        Assert.Equal(8.25m, projected.C2Promedio);
        var certificates = new CertificadoTemplateService(db, Mapper());
        var html = (await certificates.RenderForAlumnoAsync(template.Id, attendance.Inscripcion.AlumnoId))!.Html;
        Assert.Contains("A/E", html);
        Assert.Contains("Disposici\u00f3n: " + WebUtility.HtmlEncode(created.DisposicionEquivalencia), html);
        Assert.Contains(WebUtility.HtmlEncode(attendance.MateriaNombre), html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        var update = Update(created);
        update.Estado = EstadoMateria.EnCurso;
        update.DesdePase = true;
        update.RecuperacionExcepcional = EstadoMateria.RecuperaAmbos;
        await service.UpdateAsync(created.Id, update);
        db.ChangeTracker.Clear();
        projected = Assert.Single(Assert.Single(await new DocenteService(db, Mapper()).GetMateriasConAlumnosAsync(teacher.Id)).Alumnos);
        Assert.True(projected.DesdePase);
        Assert.Null(projected.DisposicionEquivalencia);
        html = (await certificates.RenderForAlumnoAsync(template.Id, attendance.Inscripcion.AlumnoId))!.Html;
        Assert.Contains("Viene de Pase", html);
        Assert.DoesNotContain("Disposici\u00f3n:", html);
        Assert.DoesNotContain("A/E", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ControllerRejectsIncompatibleOriginsWith400WithoutTrackedSideEffects(bool create)
    {
        using var db = Database();
        var attendance = Attendance();
        db.Add(attendance);
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var controller = Controller(service, Guid.NewGuid());
        ((ClaimsIdentity)controller.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));
        if (create)
        {
            var request = Equivalence(attendance.Id);
            request.DesdePase = true;
            Assert.IsType<BadRequestObjectResult>(await controller.Post(request));
            await db.SaveChangesAsync();
            Assert.Empty(db.Calificaciones);
        }
        else
        {
            var created = await service.CreateAsync(Equivalence(attendance.Id));
            var update = Update(created);
            update.DesdePase = true;
            Assert.IsType<BadRequestObjectResult>(await controller.Put(created.Id, update));
            await db.SaveChangesAsync();
            Assert.False((await service.GetByIdAsync(created.Id))!.DesdePase);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ControllerChecksOriginalAndDestinationForAuthenticatedTeacher(bool originalActive, bool destinationActive)
    {
        using var db = Database();
        var original = Attendance("Original");
        var destination = Attendance("Destino");
        var userId = Guid.NewGuid();
        db.Users.Add(new CENS15_V2.Entities.User { Id = userId, Status = true, FirstName = "Prueba", LastName = "Docente",
            Role = new Role { Name = "Docente" } });
        original.Inscripcion.Curso = original.Materia.Curso;
        destination.Inscripcion.Curso = destination.Materia.Curso;
        var teacher = new Docente { Nombres = "Prueba", Apellidos = "Docente", UserId = userId };
        db.AddRange(original, destination);
        db.Add(new MateriaDocente { Docente = teacher, Materia = original.Materia, Activo = originalActive });
        db.Add(new MateriaDocente { Docente = teacher, Materia = destination.Materia, Activo = destinationActive });
        await db.SaveChangesAsync();
        var service = new CalificacionService(db, Mapper());
        var created = await service.CreateAsync(Equivalence(original.Id));
        var update = Update(created);
        update.CursadaMateriaId = destination.Id;
        update.DesdePase = false;
        var result = Assert.IsType<ObjectResult>(await Controller(service, userId).Put(created.Id, update));
        Assert.Equal(403, result.StatusCode);
        await db.SaveChangesAsync();
        Assert.Equal(original.Id, (await service.GetByIdAsync(created.Id))!.CursadaMateriaId);
    }

    [Fact]
    public void ControllerRetainsExistingResponsibilityPolicies()
    {
        Assert.Equal(ResponsibilityPolicies.CalificacionesCrear, typeof(CalificacionesController).GetMethod("Post")!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Policy);
        Assert.Equal(ResponsibilityPolicies.CalificacionesEditar, typeof(CalificacionesController).GetMethod("Put")!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Policy);
    }

    private static CalificacionesController Controller(CalificacionService service, Guid userId) => new(service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test")) } }
    };
}
