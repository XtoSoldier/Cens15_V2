using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CENS15_V2.Data;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace AlumnoCsvImport;

public static class CourseEnrollmentCli
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] WritableTables = ["Alumnos", "AlumnoContactos", "AlumnoNacimientos", "AlumnoDocumentos", "Inscripciones", "CursadasMaterias"];
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    public sealed record TableState(long Count, string Fingerprint);

    public static async Task<int> Run(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var apply = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply") { if (apply) throw new ArgumentException(); apply = true; continue; }
            if (args[i] is not ("--file" or "--course" or "--year" or "--api-root" or "--environment" or
                "--expected-sha256" or "--expected-database" or "--expected-receipt" or "--expected-course-id" or "--unknown-birth-date") ||
                i + 1 >= args.Length || !options.TryAdd(args[i], args[++i])) throw new ArgumentException();
        }
        if (!options.TryGetValue("--file", out var file) || options.GetValueOrDefault("--course") is not ("1F" or "1G" or "2F" or "3D" or "3E") ||
            options.GetValueOrDefault("--year") != "2026" || options.GetValueOrDefault("--environment", "Development") != "Development")
            throw new ArgumentException();
        if (apply && new[] { "--expected-sha256", "--expected-database", "--expected-receipt", "--expected-course-id" }.Any(k => !options.ContainsKey(k)))
            throw new ArgumentException();
        const int year = 2026;
        var requestedCourse = options["--course"];
        var level = requestedCourse[0] - '0';
        var division = requestedCourse[1..];
        var sourceCourseId = requestedCourse switch { "1F" => 28, "1G" => 25, "2F" => 32, "3D" => 33, "3E" => 34, _ => throw new ArgumentException() };
        var unknownBirthDate = options.GetValueOrDefault("--unknown-birth-date");
        if (unknownBirthDate != null && (requestedCourse != "3D" || unknownBirthDate != "9999-09-09"))
            throw new ArgumentException("Unsupported birth date normalization.");
        var source = CourseEnrollmentPlan.Read(await File.ReadAllBytesAsync(file), year, sourceCourseId, unknownBirthDate);
        // Intentionally do not add environment providers or run the API host/seeder.
        var root = Path.GetFullPath(options.GetValueOrDefault("--api-root", Directory.GetCurrentDirectory()));
        var configuration = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json", false)
            .AddJsonFile("appsettings.Development.json", false).Build();
        var target = ImportCli.LocalTarget(configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException());
        target.Timeout = 15;
        target.CommandTimeout = 60;
        if (apply && (!source.Sha256.Equals(options["--expected-sha256"], StringComparison.OrdinalIgnoreCase) ||
            target.Database != options["--expected-database"])) throw new InvalidOperationException();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(target.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!apply) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '15s'");
        var before = await Snapshot(db, apply);

        // Project scalars: this CLI must work without applying pending unrelated model migrations.
        var courses = await db.Cursos.OrderBy(c => c.Id).Select(c => new Curso
        { Id = c.Id, CursoNombre = c.CursoNombre, Division = c.Division, AnexoId = c.AnexoId, OrientacionId = c.OrientacionId }).ToListAsync();
        var candidates = courses.Where(c => CourseEnrollmentPlan.IsTarget(c, division, level)).ToList();
        var course = candidates.Count == 1 ? candidates[0] : null;
        var catalogRows = await db.TiposDocumentoAlumno.OrderBy(t => t.Id).Select(t => new { t.Id, t.Nombre }).ToListAsync();
        var catalog = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var blockers = new List<string>();
        foreach (var item in catalogRows)
            if (!catalog.TryAdd(item.Nombre, item.Id)) blockers.Add("AmbiguousDocumentCatalog");
        foreach (var name in LegacyCsvReader.DocumentNames)
            if (!catalog.ContainsKey(name)) blockers.Add("MissingDocumentCatalog:" + name);
        if (course == null) blockers.Add("TargetCourseMustBeUnique");
        if (apply && course?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) != options["--expected-course-id"])
            throw new InvalidOperationException();
        var subjects = course == null ? new List<Materia>() : await db.Materias.Where(m => m.CursoId == course.Id)
            .OrderBy(m => m.Id).Select(m => new Materia { Id = m.Id, CursoId = m.CursoId, Nombre = m.Nombre }).ToListAsync();
        if (course != null && subjects.Count == 0) blockers.Add("TargetCourseHasNoSubjects");
        var students = await db.Alumnos.IgnoreQueryFilters().Include(a => a.Contacto).Include(a => a.DatosNacimiento)
            .Include(a => a.Documentos).OrderBy(a => a.Id).ToListAsync();
        var enrollments = await db.Inscripciones.IgnoreQueryFilters().OrderBy(i => i.Id).ToListAsync();
        var cursadas = await db.CursadasMaterias.IgnoreQueryFilters().OrderBy(c => c.Id).ToListAsync();
        var plan = CourseEnrollmentPlan.Build(source, students, enrollments, cursadas, subjects, course?.Id ?? -1, year);
        foreach (var action in plan.Actions.Where(a => a.AlumnoId.HasValue))
        {
            var student = students.Single(a => a.Id == action.AlumnoId);
            for (var i = 0; i < 4; i++)
            {
                if (!catalog.TryGetValue(LegacyCsvReader.DocumentNames[i], out var id)) continue;
                var docs = student.Documentos.Where(d => d.TipoDocumentoAlumnoId == id).ToList();
                if (action.Source.Data.Flags[i] is bool flag && (docs.Count != 1 || docs[0].Presentado != flag))
                    plan.Warnings.Add(new(action.Source.Row, "ExistingDocumentFlagDifferencePreserved", LegacyCsvReader.FlagColumns[i], student.Id));
            }
        }
        var newStudents = plan.Actions.Count(a => !a.AlumnoId.HasValue);
        var newEnrollments = plan.Actions.Count(a => !a.ExistingEnrollmentId.HasValue);
        var expectedChanges = WritableTables.ToDictionary(t => t, _ => 0L, StringComparer.Ordinal);
        expectedChanges["Alumnos"] = expectedChanges["AlumnoContactos"] = expectedChanges["AlumnoNacimientos"] = newStudents;
        expectedChanges["AlumnoDocumentos"] = plan.Actions.Where(a => !a.AlumnoId.HasValue).Sum(a => a.Source.Data.Flags.Count(f => f.HasValue));
        expectedChanges["Inscripciones"] = newEnrollments;
        expectedChanges["CursadasMaterias"] = newEnrollments * subjects.Count;
        var annexes = await db.Anexos.OrderBy(a => a.Id).Select(a => new { a.Id, a.Nombre }).ToListAsync();
        var orientations = await db.Orientaciones.OrderBy(o => o.Id).Select(o => new { o.Id, o.Nombre }).ToListAsync();
        var summary = new
        {
            sourceSha256 = source.Sha256, source.TotalRead, uniqueRows = source.Rows.Count, source.RepairedQuotes,
            source.ValidationErrors, source.Duplicates, source.BirthDateBlankedRows, unknownBirthDate, blockers, plan.Conflicts, plan.Warnings,
            target = new { target.Host, target.Port, target.Database }, courseRequested = requestedCourse, sourceCourseId, year,
            courseCandidates = candidates.Select(c => new { c.Id, c.CursoNombre, c.Division, c.AnexoId, c.OrientacionId,
                anexo = annexes.SingleOrDefault(a => a.Id == c.AnexoId)?.Nombre,
                orientacion = orientations.SingleOrDefault(o => o.Id == c.OrientacionId)?.Nombre }),
            subjects = subjects.Select(m => new { m.Id, m.Nombre }), subjectCount = subjects.Count,
            documentCatalog = catalogRows, newStudents, matchedExisting = plan.Actions.Count(a => a.AlumnoId.HasValue),
            newEnrollments, skippedActive = plan.Actions.Count(a => a.ExistingEnrollmentId.HasValue), expectedChanges,
            rowPlan = plan.Actions.Select(a => new { sourceRow = a.Source.Row, a.AlumnoId, a.ExistingEnrollmentId }),
            beforeCounts = before.ToDictionary(t => t.Key, t => t.Value.Count), stateFingerprint = Hash(before),
            canApply = source.ValidationErrors.Count == 0 && blockers.Count == 0 && plan.Conflicts.Count == 0
        };
        var receipt = Hash(new { version = 1, summary });
        Console.WriteLine(JsonSerializer.Serialize(new { mode = apply ? "enroll-apply-validation" : "enroll-preview", summary, receipt }, JsonOptions));
        if (!apply || !summary.canApply)
        {
            await transaction.RollbackAsync();
            return summary.canApply ? 0 : 2;
        }
        if (!receipt.Equals(options["--expected-receipt"], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();

        var generatedEnrollments = new List<Inscripcion>();
        foreach (var action in plan.Actions)
        {
            if (action.ExistingEnrollmentId.HasValue) continue;
            generatedEnrollments.Add(CourseEnrollmentPlan.CreateEnrollment(action, subjects, catalog, course!.Id, year));
        }
        db.Inscripciones.AddRange(generatedEnrollments);
        if (db.ChangeTracker.Entries().Any(e => e.State != EntityState.Added ||
            e.Entity is not (Alumno or AlumnoContacto or AlumnoNacimiento or AlumnoDocumento or Inscripcion or CursadaMateria)))
            throw new InvalidOperationException();
        await db.SaveChangesAsync();
        var exclusions = db.ChangeTracker.Entries().GroupBy(e => e.Metadata.GetTableName()!)
            .ToDictionary(g => g.Key, g => g.Select(e => (int)e.Property("Id").CurrentValue!).ToArray());
        var after = await Snapshot(db, false);
        var unchanged = await Snapshot(db, false, exclusions);
        foreach (var (table, state) in before)
            if (after[table].Count != state.Count + expectedChanges.GetValueOrDefault(table) || unchanged[table] != state)
                throw new InvalidOperationException();
        if (Hash(before.Keys.ToArray()) != Hash(after.Keys.ToArray())) throw new InvalidOperationException();
        var allEnrollments = await db.Inscripciones.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var allCursadas = await db.CursadasMaterias.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        foreach (var enrollment in generatedEnrollments)
        {
            var saved = allEnrollments.Single(i => i.AlumnoId == enrollment.AlumnoId && i.CursoId == course!.Id && i.Anio == year);
            if (saved.Estado != EstadoInscripcion.Activa || saved.FechaInscripcion != enrollment.FechaInscripcion)
            {
                // PostgreSQL stores timestamps at microsecond precision.
                if (saved.Estado != EstadoInscripcion.Activa || Math.Abs((saved.FechaInscripcion - enrollment.FechaInscripcion).Ticks) >= 10)
                    throw new InvalidOperationException();
            }
            var savedSubjects = allCursadas.Where(c => c.InscripcionId == saved.Id).ToList();
            if (savedSubjects.Count != subjects.Count || subjects.Any(m => savedSubjects.Count(c => c.MateriaId == m.Id && c.MateriaNombre == m.Nombre) != 1))
                throw new InvalidOperationException();
        }
        var rereadStudents = await db.Alumnos.IgnoreQueryFilters().AsNoTracking().Include(a => a.Contacto).Include(a => a.DatosNacimiento).Include(a => a.Documentos).ToListAsync();
        foreach (var action in plan.Actions.Where(a => !a.AlumnoId.HasValue))
        {
            var saved = rereadStudents.Single(a => LegacyCsvReader.NormalizeDni(a.NumeroDocumento) == LegacyCsvReader.NormalizeDni(action.Source.Dni));
            if (Profile(saved) != Profile(LegacyCsvReader.Map(action.Source, catalog))) throw new InvalidOperationException();
        }
        var rerun = CourseEnrollmentPlan.Build(source, rereadStudents, allEnrollments, allCursadas, subjects, course!.Id, year);
        if (rerun.Conflicts.Count != 0 || rerun.Actions.Count != source.Rows.Count || rerun.Actions.Any(a => !a.AlumnoId.HasValue || !a.ExistingEnrollmentId.HasValue))
            throw new InvalidOperationException();
        await transaction.CommitAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "enroll-applied", expectedChanges,
            beforeCounts = before.ToDictionary(t => t.Key, t => t.Value.Count), afterCounts = after.ToDictionary(t => t.Key, t => t.Value.Count),
            preexistingUnchanged = true, idempotentPlanVerified = true,
            generated = generatedEnrollments.Select(i => new { i.Id, i.AlumnoId, i.CursoId, i.Anio }) }, JsonOptions));
        return 0;
    }

    private static string Profile(Alumno a) => Hash(new
    {
        a.Nombres, a.Apellidos, a.NumeroDocumento, a.FechaNacimiento, a.Genero, a.Domicilio, a.Eliminado, a.FechaEliminacion,
        contact = new { a.Contacto.TelefonoAlumno, a.Contacto.Email, a.Contacto.NombreEmergencia, a.Contacto.TelefonoEmergencia },
        birth = new { a.DatosNacimiento.Localidad, a.DatosNacimiento.Provincia, a.DatosNacimiento.Pais },
        docs = a.Documentos.OrderBy(d => d.TipoDocumentoAlumnoId).Select(d => new { d.TipoDocumentoAlumnoId, d.Presentado, d.ImagenUrl, d.ImagenesUrl })
    });

    private static async Task<SortedDictionary<string, TableState>> Snapshot(AppDbContext db, bool lockTables,
        Dictionary<string, int[]>? excluded = null)
    {
        var connection = db.Database.GetDbConnection();
        var tx = db.Database.CurrentTransaction!.GetDbTransaction();
        await using var list = connection.CreateCommand();
        list.Transaction = tx;
        list.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name";
        var tables = new List<string>();
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var result = new SortedDictionary<string, TableState>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var quoted = "public.\"" + table.Replace("\"", "\"\"") + "\"";
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            if (lockTables)
            {
                command.CommandText = "LOCK TABLE " + quoted + (WritableTables.Contains(table) ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE");
                await command.ExecuteNonQueryAsync();
            }
            // Only generated integer PKs are interpolated, never source data or supplied identifiers.
            var filter = excluded != null && excluded.TryGetValue(table, out var ids) && ids.Length > 0
                ? " WHERE t.\"Id\" NOT IN (" + string.Join(",", ids) + ")" : "";
            command.CommandText = "SELECT row_to_json(t)::text FROM " + quoted + " t" + filter + " ORDER BY row_to_json(t)::text COLLATE \"C\"";
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long count = 0;
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(reader.GetString(0) + "\n"));
                    count++;
                }
            result.Add(table, new(count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
        }
        return result;
    }
}
