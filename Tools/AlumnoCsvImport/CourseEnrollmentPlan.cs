using CENS15_V2.Models;

namespace AlumnoCsvImport;

public record EnrollmentIssue(long Row, string Code, string? Field = null, int? ExistingId = null);
public record EnrollmentAction(ImportRow Source, int? AlumnoId, int? ExistingEnrollmentId);
public sealed class EnrollmentPlan
{
    public List<EnrollmentAction> Actions { get; } = [];
    public List<EnrollmentIssue> Conflicts { get; } = [];
    public List<EnrollmentIssue> Warnings { get; } = [];
}

public static class CourseEnrollmentPlan
{
    public static Inscripcion CreateEnrollment(EnrollmentAction action, IReadOnlyList<Materia> subjects,
        IReadOnlyDictionary<string, int> catalog, int courseId, int year)
    {
        if (action.ExistingEnrollmentId.HasValue) throw new InvalidOperationException();
        if (LegacyCsvReader.DocumentNames.Any(n => !catalog.ContainsKey(n)) || subjects.Count == 0 ||
            subjects.Any(m => m.CursoId != courseId) || subjects.Select(m => m.Id).Distinct().Count() != subjects.Count)
            throw new InvalidOperationException();
        var enrollment = new Inscripcion { CursoId = courseId, Anio = year, Estado = EstadoInscripcion.Activa, FechaInscripcion = DateTime.UtcNow };
        if (action.AlumnoId is int id) enrollment.AlumnoId = id;
        else enrollment.Alumno = LegacyCsvReader.Map(action.Source, catalog);
        foreach (var subject in subjects)
            enrollment.CursadasMaterias.Add(new CursadaMateria { MateriaId = subject.Id, MateriaNombre = subject.Nombre });
        return enrollment;
    }

    public static CsvResult Read(byte[] bytes, int year, int sourceCourseId = 28, string? unknownBirthDate = null)
    {
        var metadataErrors = new List<RowError>();
        var blankedBirthDates = new List<long>();
        var source = LegacyCsvReader.Read(bytes, (row, fields) =>
        {
            if (fields[9].Trim() != year.ToString(System.Globalization.CultureInfo.InvariantCulture))
                metadataErrors.Add(new(row, "inscripcion", "unexpected_source_year"));
            if (fields[11].Trim() != sourceCourseId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                metadataErrors.Add(new(row, "curso_id", "unexpected_legacy_course"));
            if (unknownBirthDate != null && fields[4].Trim() == unknownBirthDate)
            {
                fields[4] = "";
                blankedBirthDates.Add(row);
            }
        });
        source.ValidationErrors.AddRange(metadataErrors);
        source.BirthDateBlankedRows.AddRange(blankedBirthDates);
        if (source.TotalRead == 0) source.ValidationErrors.Add(new(0, "file", "empty_source"));
        foreach (var row in source.Rows)
            if (row.Dni == null || !LegacyCsvReader.NormalizeDni(row.Dni).All(char.IsAsciiDigit))
                source.ValidationErrors.Add(new(row.Row, "dni", "unambiguous_numeric_dni_required"));
        return source;
    }

    public static bool IsTarget(Curso course) => IsTarget(course, "F");

    public static bool IsTarget(Curso course, string division, int level = 1) =>
        course.CursoNombre.Trim().TrimEnd('\u00b0', '\u00ba') == level.ToString(System.Globalization.CultureInfo.InvariantCulture)
        && course.Division.Trim().Equals(division, StringComparison.OrdinalIgnoreCase);

    private static bool IsPhone(string value) => value.Count(char.IsAsciiDigit) >= 6 &&
        value.All(c => char.IsAsciiDigit(c) || char.IsWhiteSpace(c) || "+-().".Contains(c));

    public static ImportRow NormalizeContact(ImportRow row, List<EnrollmentIssue> warnings)
    {
        var d = row.Data;
        if (IsPhone(d.NombreEmergencia))
        {
            if (d.TelefonoEmergencia.Length == 0)
            {
                warnings.Add(new(row.Row, "MovedEmergencyPhoneFromName"));
                d = d with { NombreEmergencia = "", TelefonoEmergencia = d.NombreEmergencia };
            }
            else if (d.TelefonoEmergencia.Any(char.IsLetter) && !d.TelefonoEmergencia.Any(char.IsAsciiDigit))
            {
                warnings.Add(new(row.Row, "SwappedEmergencyContactFields"));
                d = d with { NombreEmergencia = d.TelefonoEmergencia, TelefonoEmergencia = d.NombreEmergencia };
            }
            else warnings.Add(new(row.Row, "AmbiguousEmergencyContact"));
        }
        if (d.Email.Length > 0 && (!System.Net.Mail.MailAddress.TryCreate(d.Email, out var address) ||
            address.Address != d.Email || !address.Host.Contains('.') ||
            d.Email.Contains("hotmial.com", StringComparison.OrdinalIgnoreCase) ||
            d.Email.Contains("sin_correo", StringComparison.OrdinalIgnoreCase) ||
            d.Email.StartsWith("completar@", StringComparison.OrdinalIgnoreCase)))
            warnings.Add(new(row.Row, "SuspiciousEmailPreserved", "email"));
        return row with { Data = d };
    }

    public static EnrollmentPlan Build(CsvResult source, IReadOnlyList<Alumno> students,
        IReadOnlyList<Inscripcion> enrollments, IReadOnlyList<CursadaMateria> cursadas,
        IReadOnlyList<Materia> subjects, int courseId, int year)
    {
        var plan = new EnrollmentPlan();
        foreach (var raw in source.Rows)
        {
            var row = NormalizeContact(raw, plan.Warnings);
            if (plan.Warnings.Any(w => w.Row == row.Row && w.Code == "AmbiguousEmergencyContact"))
                plan.Conflicts.Add(new(row.Row, "AmbiguousEmergencyContact"));
            if (row.Dni == null) { plan.Conflicts.Add(new(row.Row, "MissingDni")); continue; }
            var matches = students.Where(a => LegacyCsvReader.NormalizeDni(a.NumeroDocumento) == LegacyCsvReader.NormalizeDni(row.Dni)).ToList();
            if (matches.Count > 1) { plan.Conflicts.Add(new(row.Row, "DuplicateDatabaseDni")); continue; }
            var student = matches.SingleOrDefault();
            if (student == null && students.Any(a =>
                (LegacyCsvReader.NormalizeDni(a.NumeroDocumento).Length == 0 || LegacyCsvReader.NormalizeDni(a.NumeroDocumento).All(c => c == '0')) &&
                LegacyCsvReader.NormalizeWord(a.Nombres) == LegacyCsvReader.NormalizeWord(row.Data.Nombres) &&
                LegacyCsvReader.NormalizeWord(a.Apellidos) == LegacyCsvReader.NormalizeWord(row.Data.Apellidos) &&
                (a.FechaNacimiento == null || row.Data.FechaNacimiento == null || a.FechaNacimiento == row.Data.FechaNacimiento)))
            {
                plan.Conflicts.Add(new(row.Row, "PossibleExistingIdentityWithoutDni"));
                continue;
            }
            if (student?.Eliminado == true) { plan.Conflicts.Add(new(row.Row, "SoftDeletedStudent", ExistingId: student.Id)); continue; }
            if (student != null)
            {
                void Compare(string field, object? a, object? b, bool identity = false)
                {
                    if (Equals(a, b)) return;
                    var issue = new EnrollmentIssue(row.Row, identity ? "IdentityConflict" : "ExistingProfileDifferencePreserved", field, student.Id);
                    if (identity) plan.Conflicts.Add(issue); else plan.Warnings.Add(issue);
                }
                Compare("nombres", LegacyCsvReader.NormalizeWord(student.Nombres), LegacyCsvReader.NormalizeWord(row.Data.Nombres), true);
                Compare("apellidos", LegacyCsvReader.NormalizeWord(student.Apellidos), LegacyCsvReader.NormalizeWord(row.Data.Apellidos), true);
                Compare("fechaNacimiento", student.FechaNacimiento, row.Data.FechaNacimiento, true);
                Compare("genero", student.Genero, row.Data.Genero);
                Compare("domicilio", student.Domicilio, row.Data.Domicilio);
                Compare("telefono", student.Contacto?.TelefonoAlumno, row.Data.Telefono);
                Compare("email", student.Contacto?.Email, row.Data.Email);
                Compare("nombreEmergencia", student.Contacto?.NombreEmergencia, row.Data.NombreEmergencia);
                Compare("telefonoEmergencia", student.Contacto?.TelefonoEmergencia, row.Data.TelefonoEmergencia);
                Compare("localidad", student.DatosNacimiento?.Localidad, row.Data.Localidad);
                Compare("provincia", student.DatosNacimiento?.Provincia, row.Data.Provincia);
                Compare("pais", student.DatosNacimiento?.Pais, row.Data.Pais);
            }
            var existing = enrollments.Where(i => i.AlumnoId == student?.Id && i.CursoId == courseId && i.Anio == year).ToList();
            if (existing.Count > 1) { plan.Conflicts.Add(new(row.Row, "DuplicateEnrollment", ExistingId: student?.Id)); continue; }
            var enrollment = existing.SingleOrDefault();
            if (enrollment != null)
            {
                if (enrollment.Estado != EstadoInscripcion.Activa)
                    plan.Conflicts.Add(new(row.Row, "ExistingEnrollmentNotActive", ExistingId: enrollment.Id));
                var current = cursadas.Where(c => c.InscripcionId == enrollment.Id).ToList();
                if (current.Count != subjects.Count || subjects.Any(m => current.Count(c => c.MateriaId == m.Id) != 1))
                    plan.Conflicts.Add(new(row.Row, "ExistingEnrollmentSubjectsRequireApproval", ExistingId: enrollment.Id));
            }
            plan.Actions.Add(new(row, student?.Id, enrollment?.Id));
        }
        return plan;
    }
}
