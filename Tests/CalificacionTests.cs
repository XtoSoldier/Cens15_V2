using System.Globalization;
using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CalificacionesDTOs;
using CENS15_V2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CENS15_V2.Tests;

public class CalificacionTests
{
    [Theory]
    [InlineData("5", "7", "6", EstadoMateria.Aprobado)]
    [InlineData("7", "5", "6", EstadoMateria.RecuperaSegundoCuatrimestre)]
    [InlineData("A", "8", "4", EstadoMateria.RecuperaPrimerCuatrimestre)]
    [InlineData("0", "8", "4", EstadoMateria.RecuperaPrimerCuatrimestre)]
    [InlineData("A", "A", "0", EstadoMateria.RecuperaAmbos)]
    [InlineData("5.35", "5.75", "6", EstadoMateria.RecuperaAmbos)]
    [InlineData("5.8", "6", "6", EstadoMateria.RecuperaPrimerCuatrimestre)]
    [InlineData("6", "S/C", "6", EstadoMateria.Aprobado)]
    [InlineData("S/C", "8", "8", EstadoMateria.Aprobado)]
    [InlineData("5.5", "S/C", "5.5", EstadoMateria.RecuperaAmbos)]
    [InlineData("S/C", "A", "0", EstadoMateria.RecuperaAmbos)]
    [InlineData("S/C", "S/C", null, EstadoMateria.EnCurso)]
    [InlineData("5", "", null, EstadoMateria.EnCurso)]
    [InlineData("", "A", null, EstadoMateria.EnCurso)]
    [InlineData("S/C", "", null, EstadoMateria.EnCurso)]
    public void CalculationMatrix(string first, string second, string? annual, EstadoMateria state)
    {
        var grade = Grade(first, second);
        grade.RecuperacionExcepcional = EstadoMateria.RecuperaAmbos;
        CalificacionRules.Apply(grade);
        Assert.Equal(Parse(annual), grade.PromedioAnual);
        Assert.Equal(state, grade.Estado);
        Assert.Equal(state == EstadoMateria.Aprobado ? Parse(annual) : null, grade.CalificacionFinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(EstadoMateria.EnCurso)]
    [InlineData(EstadoMateria.Aprobado)]
    [InlineData((EstadoMateria)99)]
    public void ExceptionalQuarterRequiresValidExplicitChoice(EstadoMateria? choice)
    {
        var grade = Grade("5", "S/C");
        grade.RecuperacionExcepcional = choice;
        Assert.Throws<InvalidOperationException>(() => CalificacionRules.Apply(grade));
    }

    [Theory]
    [InlineData(EstadoMateria.RecuperaPrimerCuatrimestre)]
    [InlineData(EstadoMateria.RecuperaSegundoCuatrimestre)]
    [InlineData(EstadoMateria.RecuperaAmbos)]
    public void ChoiceSurvivesRecoveryAndClearsWhenBothQuartersAreGraded(EstadoMateria choice)
    {
        var grade = Grade("5", "S/C");
        grade.RecuperacionExcepcional = choice;
        CalificacionRules.Apply(grade);
        Assert.Equal(choice, grade.Estado);
        grade.RecuperacionDiciembre = 4.25m;
        CalificacionRules.Apply(grade);
        Assert.Equal(EstadoMateria.Aprobado, grade.Estado);
        Assert.Equal(5m, grade.CalificacionFinal);
        Assert.Equal(choice, grade.RecuperacionExcepcional);
        grade.C2Condicion = CondicionNota.Numerica;
        grade.C2Promedio = 7m;
        CalificacionRules.Apply(grade);
        Assert.Null(grade.RecuperacionExcepcional);
        Assert.Equal(6m, grade.CalificacionFinal);
    }

    [Theory]
    [InlineData("3.9", "4.1", "5")]
    [InlineData("4", "9", "4")]
    [InlineData("A", "4", "4")]
    [InlineData("S/C", "4", "4")]
    [InlineData("A", "S/C", null)]
    public void RecoveryThresholdAndDecemberPriority(string december, string march, string? final)
    {
        var grade = Grade("5", "5");
        (grade.RecuperacionDiciembre, grade.DiciembreCondicion) = Note(december);
        (grade.RecuperacionMarzo, grade.MarzoCondicion) = Note(march);
        CalificacionRules.Apply(grade);
        Assert.Equal(Parse(final), grade.CalificacionFinal);
        Assert.Equal(final == null ? EstadoMateria.RecuperaAmbos : EstadoMateria.Aprobado, grade.Estado);
    }

    [Fact]
    public void NumericZeroIsNotConvertedToAbsentAndDecimalsAreUnchanged()
    {
        var grade = Grade("0", "8.25");
        grade.C1Nota1 = 5.35m;
        CalificacionRules.Apply(grade);
        Assert.Equal(CondicionNota.Numerica, grade.C1Condicion);
        Assert.Equal(0m, grade.C1Promedio);
        Assert.Equal(8.25m, grade.C2Promedio);
        Assert.Equal(5.35m, grade.C1Nota1);
    }

    [Fact]
    public void ConditionsNormalizeValuesAndPendingQuarterCannotBeApprovedByExam()
    {
        var grade = Grade("A", "S/C");
        grade.C1Promedio = 9;
        grade.C2Promedio = 8;
        grade.RecuperacionExcepcional = EstadoMateria.RecuperaPrimerCuatrimestre;
        CalificacionRules.Apply(grade);
        Assert.Equal(0m, grade.C1Promedio);
        Assert.Null(grade.C2Promedio);
        grade.C2Condicion = CondicionNota.Numerica;
        grade.RecuperacionDiciembre = 10;
        CalificacionRules.Apply(grade);
        Assert.Equal(EstadoMateria.EnCurso, grade.Estado);
        Assert.Null(grade.PromedioAnual);
        Assert.Null(grade.CalificacionFinal);
        Assert.Null(grade.RecuperacionExcepcional);
    }

    [Fact]
    public void ManualLegacyApprovalIsPreserved()
    {
        var grade = new Calificacion { PromedioAnual = 6.25m, CalificacionFinal = 7.5m, Estado = EstadoMateria.Aprobado };
        CalificacionRules.Apply(grade);
        Assert.Equal(6.25m, grade.PromedioAnual);
        Assert.Equal(7.5m, grade.CalificacionFinal);
        Assert.Equal(EstadoMateria.Aprobado, grade.Estado);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void RejectsOutOfRangeQuarterExamAndPartial(int value)
    {
        foreach (var field in new[] { nameof(Calificacion.C1Promedio), nameof(Calificacion.RecuperacionMarzo), nameof(Calificacion.C2Nota3) })
        {
            var grade = Grade("6", "6");
            typeof(Calificacion).GetProperty(field)!.SetValue(grade, (decimal?)value);
            Assert.Throws<InvalidOperationException>(() => CalificacionRules.Apply(grade));
        }
    }

    [Fact]
    public void RejectsUnknownCondition()
    {
        var grade = Grade("6", "6");
        grade.C1Condicion = (CondicionNota)99;
        Assert.Throws<InvalidOperationException>(() => CalificacionRules.Apply(grade));
    }

    [Fact]
    public async Task ServiceRoundTripsConditionsChoiceAndAllSixPartialsWithoutTrustingComputedFields()
    {
        using var db = Database();
        var service = new CalificacionService(db, Mapper());
        db.CursadasMaterias.Add(new CursadaMateria
        {
            Id = 1, MateriaNombre = "Historia",
            Inscripcion = new Inscripcion { Alumno = new Alumno { Nombres = "Prueba", Apellidos = "Alumno" } }
        });
        await db.SaveChangesAsync();
        var created = await service.CreateAsync(new CreateCalificacionRequest
        {
            CursadaMateriaId = 1, C1Condicion = CondicionNota.Ausente, C2Condicion = CondicionNota.SinCalificar,
            RecuperacionExcepcional = EstadoMateria.RecuperaAmbos,
            C1Nota1 = 1.1m, C1Nota2 = 2.2m, C1Nota3 = 3.3m, C2Nota1 = 4.4m, C2Nota2 = 5.5m, C2Nota3 = 6.6m,
            PromedioAnual = 10, CalificacionFinal = 10, Estado = EstadoMateria.Aprobado
        });
        db.ChangeTracker.Clear();
        var loaded = (await service.GetByCursadaMateriaIdAsync(1))!;
        Assert.Equal(0m, loaded.C1Promedio);
        Assert.Null(loaded.C2Promedio);
        Assert.Equal(CondicionNota.Ausente, loaded.C1Condicion);
        Assert.Equal(CondicionNota.SinCalificar, loaded.C2Condicion);
        Assert.Equal(EstadoMateria.RecuperaAmbos, loaded.Estado);
        Assert.Null(loaded.CalificacionFinal);
        Assert.True(await service.UpdateAsync(created.Id, new UpdateCalificacionRequest
        {
            CursadaMateriaId = 1, C1Condicion = loaded.C1Condicion, C2Condicion = loaded.C2Condicion,
            RecuperacionExcepcional = loaded.RecuperacionExcepcional,
            C1Nota1 = loaded.C1Nota1, C1Nota2 = loaded.C1Nota2, C1Nota3 = loaded.C1Nota3,
            C2Nota1 = loaded.C2Nota1, C2Nota2 = loaded.C2Nota2, C2Nota3 = loaded.C2Nota3,
            DiciembreCondicion = CondicionNota.Ausente, RecuperacionMarzo = 4.25m
        }));
        db.ChangeTracker.Clear();
        var updated = (await service.GetByIdAsync(created.Id))!;
        Assert.Equal(new decimal?[] { 1.1m, 2.2m, 3.3m, 4.4m, 5.5m, 6.6m },
            new[] { updated.C1Nota1, updated.C1Nota2, updated.C1Nota3, updated.C2Nota1, updated.C2Nota2, updated.C2Nota3 });
        Assert.Equal(EstadoMateria.RecuperaAmbos, updated.RecuperacionExcepcional);
        Assert.Equal(CondicionNota.Ausente, updated.DiciembreCondicion);
        Assert.Equal(0m, updated.RecuperacionDiciembre);
        Assert.Equal(EstadoMateria.Aprobado, updated.Estado);
        Assert.Equal(5m, updated.CalificacionFinal);
    }

    [Fact]
    public async Task RejectedWritesDoNotChangePersistedOrTrackedGrades()
    {
        using var db = Database();
        var service = new CalificacionService(db, Mapper());
        db.CursadasMaterias.Add(new CursadaMateria
        {
            Id = 1, MateriaNombre = "Historia",
            Inscripcion = new Inscripcion { Alumno = new Alumno { Nombres = "Prueba", Apellidos = "Alumno" } }
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CreateCalificacionRequest
        { CursadaMateriaId = 1, C1Promedio = 5, C2Condicion = CondicionNota.SinCalificar }));
        Assert.Empty(db.Calificaciones);
        var created = await service.CreateAsync(new CreateCalificacionRequest { CursadaMateriaId = 1, C1Promedio = 6, C2Promedio = 8 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(created.Id, new UpdateCalificacionRequest
        { CursadaMateriaId = 1, C1Promedio = 5, C2Condicion = CondicionNota.SinCalificar }));
        Assert.Equal(8m, db.Calificaciones.Local.Single().C2Promedio);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(7m, (await service.GetByIdAsync(created.Id))!.CalificacionFinal);
    }

    [Fact]
    public async Task TeacherProjectionAndCertificateDisplayPersistedConditionsWithoutRecalculatingHistory()
    {
        using var db = Database();
        var course = new Curso { CursoNombre = "1", Division = "A", Anexo = new Anexo { Nombre = "Centro" }, Orientacion = new Orientacion { Nombre = "Sociales" } };
        var subject = new Materia { Nombre = "Historia", Curso = course };
        var student = new Alumno { Nombres = "Prueba", Apellidos = "Alumno" };
        var enrollment = new Inscripcion { Alumno = student, Curso = course, Anio = 2026 };
        var grade = Grade("A", "S/C");
        grade.C1Nota1 = 5.35m;
        grade.RecuperacionExcepcional = EstadoMateria.RecuperaAmbos;
        grade.DiciembreCondicion = CondicionNota.Ausente;
        grade.MarzoCondicion = CondicionNota.SinCalificar;
        grade.PromedioAnual = 9.25m;
        var attendance = new CursadaMateria { Inscripcion = enrollment, Materia = subject, MateriaNombre = "Historia", Calificacion = grade };
        var teacher = new Docente { Nombres = "Prueba", Apellidos = "Docente" };
        db.CursadasMaterias.Add(attendance);
        db.MateriaDocentes.Add(new MateriaDocente { Docente = teacher, Materia = subject, Activo = true });
        var template = new CertificadoTemplate { Nombre = "Boletin", ContenidoHtml = "{{BLOQUE_CALIFICACIONES}}" };
        db.CertificadoTemplates.Add(template);
        await db.SaveChangesAsync();
        var projected = Assert.Single(Assert.Single(await new DocenteService(db, Mapper()).GetMateriasConAlumnosAsync(teacher.Id)).Alumnos);
        Assert.Equal(CondicionNota.Ausente, projected.C1Condicion);
        Assert.Equal(CondicionNota.SinCalificar, projected.C2Condicion);
        Assert.Equal(EstadoMateria.RecuperaAmbos, projected.RecuperacionExcepcional);
        Assert.Equal(5.35m, projected.C1Nota1);
        Assert.Equal(9.25m, projected.PromedioAnual);
        var rendered = await new CertificadoTemplateService(db, Mapper()).RenderForAlumnoAsync(template.Id, student.Id);
        Assert.Contains("A (Ausente)", rendered!.Html);
        Assert.Contains("S/C (Sin Calificar)", rendered.Html);
        Assert.Equal(9.25m, (await db.Calificaciones.AsNoTracking().SingleAsync()).PromedioAnual);
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IMapper Mapper() => new MapperConfiguration(config => config.AddProfile<AutoMapperProfile>()).CreateMapper();
    private static decimal? Parse(string? value) => string.IsNullOrEmpty(value) ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
    private static (decimal?, CondicionNota) Note(string value) => value switch
    {
        "A" => (0m, CondicionNota.Ausente),
        "S/C" => (null, CondicionNota.SinCalificar),
        _ => (Parse(value), CondicionNota.Numerica)
    };
    private static Calificacion Grade(string first, string second)
    {
        var grade = new Calificacion();
        (grade.C1Promedio, grade.C1Condicion) = Note(first);
        (grade.C2Promedio, grade.C2Condicion) = Note(second);
        return grade;
    }
}
