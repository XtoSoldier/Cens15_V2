using System.Text;
using AlumnoCsvImport;
using CENS15_V2.Data;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CENS15_V2.Tests;

public class AlumnoCourseCsvImportTests
{
    private static readonly Dictionary<string, int> Catalog = LegacyCsvReader.DocumentNames.Select((name, i) => (name, i))
        .ToDictionary(x => x.name, x => x.i + 1);
    private static readonly Materia[] Subjects = [new() { Id = 90, CursoId = 6, Nombre = "Historia" }, new() { Id = 91, CursoId = 6, Nombre = "Lengua" }];
    private static string Row(params (string Key, string Value)[] changes)
    {
        var fields = LegacyCsvReader.Header.Split(',').ToDictionary(h => h, _ => "");
        fields["nombre"] = "Ana"; fields["apellido"] = "Perez"; fields["dni"] = "12.345.678";
        fields["fecha_de_nacimiento"] = "2001-02-03"; fields["inscripcion"] = "2026"; fields["curso_id"] = "28";
        foreach (var flag in LegacyCsvReader.FlagColumns) fields[flag] = "0";
        foreach (var (key, value) in changes) fields[key] = value;
        return string.Join(",", fields.Values.Select(v => "\"" + v.Replace("\"", "\"\"") + "\""));
    }
    private static byte[] Bytes(params string[] rows) => Encoding.UTF8.GetBytes(LegacyCsvReader.Header + "\n" + string.Join("\n", rows));
    private static CsvResult Source(params string[] rows) => CourseEnrollmentPlan.Read(Bytes(rows.Length == 0 ? [Row()] : rows), 2026);
    private static Alumno Student(CsvResult source, int id = 10)
    {
        var student = LegacyCsvReader.Map(source.Rows[0], Catalog); student.Id = id;
        student.NumeroDocumento = "12345678";
        return student;
    }
    private static EnrollmentPlan Plan(CsvResult source, Alumno[]? students = null, Inscripcion[]? enrollments = null, CursadaMateria[]? cursadas = null) =>
        CourseEnrollmentPlan.Build(source, students ?? [], enrollments ?? [], cursadas ?? [], Subjects, 6, 2026);

    [Theory]
    [InlineData("inscripcion", "1")]
    [InlineData("inscripcion", "2025")]
    [InlineData("curso_id", "6")]
    [InlineData("dni", "0")]
    [InlineData("dni", "")]
    [InlineData("dni", "ABC")]
    public void EnrollmentValidationDoesNotChangeLegacyMode(string field, string value)
    {
        var bytes = Bytes(Row((field, value)));
        Assert.Empty(LegacyCsvReader.Read(bytes).ValidationErrors);
        Assert.NotEmpty(CourseEnrollmentPlan.Read(bytes, 2026).ValidationErrors);
    }

    [Fact]
    public void DuplicateMetadataIsValidatedEvenForDiscardedEquivalentRow()
    {
        var source = Source(Row(), Row(("curso_id", "6")));
        Assert.Single(source.Rows);
        Assert.Single(source.Duplicates);
        Assert.Contains(source.ValidationErrors, e => e.Row == 3 && e.Field == "curso_id");
    }

    [Fact]
    public void EquivalentDniDeduplicatedButContradictorySourceBlocked()
    {
        Assert.Single(Source(Row(), Row(("dni", "12345678"))).Rows);
        Assert.Contains(Source(Row(), Row(("email", "different@example.invalid"))).ValidationErrors, e => e.Code == "contradictory_duplicate");
    }

    [Theory]
    [InlineData("1", "F", true)]
    [InlineData("1\u00b0", "f", true)]
    [InlineData("1\u00ba", " F ", true)]
    [InlineData("2", "F", false)]
    [InlineData("1", "A", false)]
    public void TargetIsResolvedByNameAndDivisionNotLegacyId(string name, string division, bool expected) =>
        Assert.Equal(expected, CourseEnrollmentPlan.IsTarget(new Curso { Id = 28, CursoNombre = name, Division = division }));

    [Fact]
    public void ThirdEValidatesItsOwnSourceAndPreservesMalformedEmailWithAWarning()
    {
        var source = CourseEnrollmentPlan.Read(Bytes(Row(("curso_id", "34"), ("email", "ezequiel@exampleinvalid"))), 2026, 34);
        Assert.Empty(source.ValidationErrors);
        var plan = Plan(source);
        Assert.Equal("ezequiel@exampleinvalid", plan.Actions[0].Source.Data.Email);
        Assert.Contains(plan.Warnings, w => w.Code == "SuspiciousEmailPreserved");
        var course = new Curso { Id = 11, CursoNombre = "3\u00b0", Division = "E" };
        Assert.True(CourseEnrollmentPlan.IsTarget(course, "E", 3));
        Assert.False(CourseEnrollmentPlan.IsTarget(course, "D", 3));
    }

    [Fact]
    public void ExplicitlyApprovedSentinelIsBlankedInMemoryWithoutChangingSourceBytes()
    {
        var bytes = Bytes(Row(("curso_id", "33"), ("fecha_de_nacimiento", "9999-09-09")),
            Row(("curso_id", "33"), ("dni", "34567890"), ("fecha_de_nacimiento", "2001-02-03")));
        var before = bytes.ToArray();
        var source = CourseEnrollmentPlan.Read(bytes, 2026, 33, "9999-09-09");
        Assert.Empty(source.ValidationErrors);
        Assert.Equal(new long[] { 2 }, source.BirthDateBlankedRows);
        Assert.Null(source.Rows[0].Data.FechaNacimiento);
        Assert.Equal(new DateOnly(2001, 2, 3), source.Rows[1].Data.FechaNacimiento);
        Assert.Equal(before, bytes);
        Assert.NotEmpty(CourseEnrollmentPlan.Read(bytes, 2026, 33).ValidationErrors);
        Assert.NotEmpty(LegacyCsvReader.Read(bytes).ValidationErrors);
    }

    [Fact]
    public void ThirdDResolvesTheThirdYearAndKeepsFutureBirthDatesBlocked()
    {
        var bytes = Bytes(Row(("curso_id", "33")));
        Assert.Empty(CourseEnrollmentPlan.Read(bytes, 2026, 33).ValidationErrors);
        var course = new Curso { Id = 10, CursoNombre = "3\u00b0", Division = "D" };
        Assert.True(CourseEnrollmentPlan.IsTarget(course, "D", 3));
        Assert.False(CourseEnrollmentPlan.IsTarget(course, "D", 2));
        Assert.False(CourseEnrollmentPlan.IsTarget(course, "F", 3));
        var invalid = CourseEnrollmentPlan.Read(Bytes(Row(("curso_id", "33"), ("fecha_de_nacimiento", "9999-09-09"))), 2026, 33);
        Assert.Contains(invalid.ValidationErrors, e => e.Field == "fecha_de_nacimiento" && e.Code == "invalid_date");
    }

    [Fact]
    public void SecondFUsesTheExplicitTargetRatherThanTheSourceFilenameOrLegacyId()
    {
        var bytes = Bytes(Row(("curso_id", "32")));
        Assert.Empty(CourseEnrollmentPlan.Read(bytes, 2026, 32).ValidationErrors);
        Assert.Contains(CourseEnrollmentPlan.Read(bytes, 2026, 25).ValidationErrors, e => e.Field == "curso_id");
        var course = new Curso { Id = 8, CursoNombre = "2\u00b0", Division = "F" };
        Assert.True(CourseEnrollmentPlan.IsTarget(course, "F", 2));
        Assert.False(CourseEnrollmentPlan.IsTarget(course, "F"));
        Assert.False(CourseEnrollmentPlan.IsTarget(course, "H", 2));
    }

    [Fact]
    public void FirstGUsesItsOwnLegacyCourseWithoutChangingFirstF()
    {
        var bytes = Bytes(Row(("curso_id", "25")));
        Assert.Empty(CourseEnrollmentPlan.Read(bytes, 2026, 25).ValidationErrors);
        Assert.Contains(CourseEnrollmentPlan.Read(bytes, 2026).ValidationErrors, e => e.Field == "curso_id");
        var course = new Curso { Id = 7, CursoNombre = "1\u00b0", Division = "G" };
        Assert.True(CourseEnrollmentPlan.IsTarget(course, "G"));
        Assert.False(CourseEnrollmentPlan.IsTarget(course));
        var plan = Plan(Source(Row(("email", "completar@cens15.com"))));
        Assert.Contains(plan.Warnings, w => w.Code == "SuspiciousEmailPreserved");
        Assert.Equal("completar@cens15.com", plan.Actions[0].Source.Data.Email);
    }

    [Fact]
    public void RepairsClearEmergencySwapOnlyInMemoryAndPreservesSuspiciousEmail()
    {
        var source = Source(Row(("nombre_tutor", "+54 2901 123456"), ("tel_tutor", "Natalia"), ("email", "sin_correo@hotmial.com")));
        var plan = Plan(source);
        var row = Assert.Single(plan.Actions).Source;
        Assert.Equal("Natalia", row.Data.NombreEmergencia);
        Assert.Equal("+54 2901 123456", row.Data.TelefonoEmergencia);
        Assert.Equal("sin_correo@hotmial.com", row.Data.Email);
        Assert.Contains(plan.Warnings, w => w.Code == "SwappedEmergencyContactFields");
        Assert.Contains(plan.Warnings, w => w.Code == "SuspiciousEmailPreserved");
        Assert.Equal("+54 2901 123456", source.Rows[0].Data.NombreEmergencia);
    }

    [Fact]
    public void PhoneInNameWithEmptyPhoneDoesNotInventName()
    {
        var plan = Plan(Source(Row(("nombre_tutor", "2901123456"))));
        Assert.Equal("", plan.Actions[0].Source.Data.NombreEmergencia);
        Assert.Equal("2901123456", plan.Actions[0].Source.Data.TelefonoEmergencia);
        Assert.Contains(plan.Warnings, w => w.Code == "MovedEmergencyPhoneFromName");
    }

    [Fact]
    public void AmbiguousPhoneTransformationBlocks()
    {
        Assert.Contains(Plan(Source(Row(("nombre_tutor", "2901123456"), ("tel_tutor", "2901654321")))).Conflicts,
            c => c.Code == "AmbiguousEmergencyContact");
    }

    [Fact]
    public void ReusesNormalizedDniWithoutOverwritingProfileAndPreservesOtherEnrollment()
    {
        var source = Source(); var student = Student(source); student.Domicilio = "Existing address";
        var other = new Inscripcion { Id = 20, AlumnoId = student.Id, CursoId = 9, Anio = 2026, Estado = EstadoInscripcion.Activa };
        var plan = Plan(source, [student], [other]);
        Assert.Empty(plan.Conflicts);
        Assert.Equal(10, plan.Actions[0].AlumnoId);
        Assert.Null(plan.Actions[0].ExistingEnrollmentId);
        Assert.Equal("Existing address", student.Domicilio);
        Assert.Contains(plan.Warnings, w => w.Field == "domicilio");
        var enrollment = CourseEnrollmentPlan.CreateEnrollment(plan.Actions[0], Subjects, Catalog, 6, 2026);
        Assert.Equal(10, enrollment.AlumnoId); Assert.Null(enrollment.Alumno);
        Assert.Equal(9, other.CursoId);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("birth")]
    [InlineData("deleted")]
    [InlineData("duplicate")]
    public void ConflictingDatabaseIdentityBlocks(string kind)
    {
        var source = Source(); var student = Student(source);
        if (kind == "name") student.Nombres = "Other";
        if (kind == "birth") student.FechaNacimiento = null;
        if (kind == "deleted") student.Eliminado = true;
        Assert.NotEmpty(Plan(source, kind == "duplicate" ? [student, Student(source, 11)] : [student]).Conflicts);
    }

    [Fact]
    public void ActiveEnrollmentSkippedAndMissingSubjectsBlockedRatherThanRepaired()
    {
        var source = Source(); var student = Student(source);
        var enrollment = new Inscripcion { Id = 20, AlumnoId = 10, CursoId = 6, Anio = 2026, Estado = EstadoInscripcion.Activa };
        Assert.Contains(Plan(source, [student], [enrollment]).Conflicts, c => c.Code == "ExistingEnrollmentSubjectsRequireApproval");
        var cursadas = Subjects.Select(m => new CursadaMateria { InscripcionId = 20, MateriaId = m.Id }).ToArray();
        var plan = Plan(source, [student], [enrollment], cursadas);
        Assert.Empty(plan.Conflicts); Assert.Equal(20, plan.Actions[0].ExistingEnrollmentId);
        Assert.Throws<InvalidOperationException>(() => CourseEnrollmentPlan.CreateEnrollment(plan.Actions[0], Subjects, Catalog, 6, 2026));
        enrollment.Estado = EstadoInscripcion.Anulada;
        Assert.Contains(Plan(source, [student], [enrollment], cursadas).Conflicts, c => c.Code == "ExistingEnrollmentNotActive");
        enrollment.Estado = EstadoInscripcion.Activa;
        Assert.NotEmpty(Plan(source, [student], [enrollment, enrollment], cursadas).Conflicts);
        Assert.NotEmpty(Plan(source, [student], [enrollment], [.. cursadas, cursadas[0]]).Conflicts);
    }

    [Fact]
    public async Task NewGraphContainsCompleteProfileAllSubjectsNoGradesAndRerunIsNoOpInDisposableMemoryOnly()
    {
        var source = Source(Row(("cert_estudio", "1"), ("nombre_tutor", "2901123456"), ("email", "literal@hotmial.com")));
        var plan = Plan(source);
        var before = DateTime.UtcNow;
        var graph = CourseEnrollmentPlan.CreateEnrollment(plan.Actions[0], Subjects, Catalog, 6, 2026);
        Assert.Equal(6, graph.CursoId); Assert.NotEqual(28, graph.CursoId);
        Assert.Equal(2026, graph.Anio); Assert.Equal(EstadoInscripcion.Activa, graph.Estado);
        Assert.InRange(graph.FechaInscripcion, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, graph.FechaInscripcion.Kind);
        Assert.Equal(4, graph.Alumno.Documentos.Count);
        Assert.Contains(graph.Alumno.Documentos, d => d.TipoDocumentoAlumnoId == Catalog["Certificado de Estudio"] && d.Presentado);
        Assert.Equal("literal@hotmial.com", graph.Alumno.Contacto.Email);
        Assert.Equal("2901123456", graph.Alumno.Contacto.TelefonoEmergencia);
        Assert.NotNull(graph.Alumno.DatosNacimiento);
        Assert.Equal(Subjects.Length, graph.CursadasMaterias.Count);
        Assert.All(graph.CursadasMaterias, c => Assert.Null(c.Calificacion));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Inscripciones.Add(graph); await db.SaveChangesAsync();
        Assert.Empty(await db.Calificaciones.ToListAsync());
        var rerun = CourseEnrollmentPlan.Build(source, await db.Alumnos.ToListAsync(), await db.Inscripciones.ToListAsync(),
            await db.CursadasMaterias.ToListAsync(), Subjects, 6, 2026);
        Assert.Empty(rerun.Conflicts);
        Assert.All(rerun.Actions, a => { Assert.NotNull(a.AlumnoId); Assert.NotNull(a.ExistingEnrollmentId); });
        Assert.DoesNotContain(rerun.Actions, a => !a.AlumnoId.HasValue || !a.ExistingEnrollmentId.HasValue);
    }

    [Fact]
    public async Task ApplyWithoutAllConfirmationsFailsBeforeOpeningDatabase()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CourseEnrollmentCli.Run(["--file", "not-read.csv", "--course", "1F", "--year", "2026", "--apply"]));
        await Assert.ThrowsAsync<ArgumentException>(() => CourseEnrollmentCli.Run(["--file", "not-read.csv", "--course", "1F", "--year", "2026", "--environment", "Production"]));
    }

    [Fact]
    public void MissingCatalogOrWrongSubjectCourseCannotSilentlyLoseDocuments()
    {
        var action = Plan(Source()).Actions[0];
        Assert.Throws<InvalidOperationException>(() => CourseEnrollmentPlan.CreateEnrollment(action, Subjects, new Dictionary<string, int>(), 6, 2026));
        Assert.Throws<InvalidOperationException>(() => CourseEnrollmentPlan.CreateEnrollment(action, Subjects, Catalog, 28, 2026));
        Assert.Throws<InvalidOperationException>(() => CourseEnrollmentPlan.CreateEnrollment(action, [], Catalog, 6, 2026));
    }

    [Fact]
    public void PossibleExistingPersonWithoutDniBlocksRatherThanDuplicatingOrRestoring()
    {
        var source = Source(); var student = Student(source); student.NumeroDocumento = null;
        Assert.Contains(Plan(source, [student]).Conflicts, c => c.Code == "PossibleExistingIdentityWithoutDni");
        student.Eliminado = true;
        Assert.NotEmpty(Plan(source, [student]).Conflicts);
    }

    [Fact]
    public void EmptyAndMalformedSourceAreNotApplicable()
    {
        Assert.Contains(CourseEnrollmentPlan.Read(Bytes(), 2026).ValidationErrors, e => e.Code == "empty_source");
        Assert.NotEmpty(CourseEnrollmentPlan.Read(Encoding.UTF8.GetBytes("wrong\na,b"), 2026).ValidationErrors);
        Assert.Contains(CourseEnrollmentPlan.Read(Bytes("a,b"), 2026).ValidationErrors, e => e.Code == "column_count");
    }
}
