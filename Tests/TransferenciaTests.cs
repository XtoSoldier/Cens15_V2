using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CENS15_V2.Controllers;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.Transferencias;
using CENS15_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CENS15_V2.Tests;

public sealed class TransferenciaTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DatosTransferencia Vacio() => new()
    {
        Docentes = [], Anexos = [], Orientaciones = [], Cursos = [], Materias = [],
        Bloques = [], Asignaciones = [], Alumnos = []
    };
    private static PaqueteTransferencia Paquete(string seccion, DatosTransferencia? datos = null) => new()
    {
        Formato = "cens15-transferencia", Version = 1, Seccion = seccion, ExportadoEn = DateTimeOffset.UtcNow,
        Datos = datos ?? Vacio(), Alcance = [], Exclusiones = []
    };
    private static DocenteTransferencia Docente(string? email = "ana@example.com") => new("Ana", "Perez", email);
    private static AlumnoTransferencia Alumno(string documento = "123") => new(documento, "Maria", "Lopez",
        new DateOnly(1990, 1, 2), Genero.Mujer, "Calle 1", new("Ushuaia", "TDF", "Argentina"), new("111", "", "Familiar", "222"));
    private static PaqueteTransferencia Horarios()
    {
        var curso = new CursoClave("1\u00b0", "A", "Centro", "Sociales");
        var materia = new MateriaClave(curso, "Matematica");
        var bloque = new BloqueClave("Centro", DayOfWeek.Monday, 1);
        return Paquete("horarios", Vacio() with
        {
            Anexos = [new("Centro")], Orientaciones = [new("Sociales", "SOC")],
            Cursos = [new(curso, false)], Docentes = [Docente()],
            Materias = [new(materia, 1, [new(Docente(), "Titular", true)])],
            Bloques = [new(bloque, TipoBloqueHorario.Clase, new(19, 0), new(19, 40))],
            Asignaciones = [new(materia, bloque)]
        });
    }
    private static async Task<ReporteTransferencia> Aplicar(TransferenciaService service, PaqueteTransferencia p, string modo = "actualizar")
    {
        var preview = await service.ProcesarAsync(p, modo);
        Assert.True(preview.PuedeImportar, string.Join("; ", preview.Errores));
        var report = await service.ProcesarAsync(p, modo, true, preview.Huella, modo == "reemplazar" ? "REEMPLAZAR" : "");
        Assert.True(report.Importado, string.Join("; ", report.Errores));
        return report;
    }

    [Fact]
    public void EveryEndpointRequiresAdminAndHasSizeBound()
    {
        var type = typeof(TransferenciasController);
        Assert.Equal("Admin", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal(TransferenciaService.MaxBytes,
            ((IRequestSizeLimitMetadata)type.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize);
        Assert.DoesNotContain(type.GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
    }

    [Fact]
    public async Task StudentsWithoutDocumentsRoundTripWithoutMergingOrEnrollment()
    {
        using var source = Db();
        source.Alumnos.AddRange(new Alumno { Nombres = "Ana", Apellidos = "Perez" },
            new Alumno { Nombres = "Otra", Apellidos = "Persona" });
        await source.SaveChangesAsync();
        var exported = await new TransferenciaService(source).ExportarAsync("alumnos");
        var package = TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(exported, TransferenciaService.JsonOptions));
        using var target = Db();
        var service = new TransferenciaService(target);
        await Aplicar(service, package);
        await Aplicar(service, package, "reemplazar");
        Assert.Equal(2, await target.Alumnos.CountAsync());
        Assert.All(await target.Alumnos.ToListAsync(), a =>
        {
            Assert.Null(a.NumeroDocumento); Assert.Null(a.FechaNacimiento); Assert.Null(a.Genero);
        });
        Assert.Empty(target.Inscripciones);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("destination-with-dni")]
    [InlineData("source-with-dni")]
    public async Task AmbiguousStudentsWithoutDocumentsAreBlocked(string kind)
    {
        using var db = Db();
        var item = Alumno() with { NumeroDocumento = null, FechaNacimiento = null, Genero = null };
        var students = new List<AlumnoTransferencia> { item };
        if (kind.StartsWith("source"))
            students.Add(item with { NumeroDocumento = kind == "source" ? " " : "123" });
        else
        {
            db.Alumnos.AddRange(new Alumno { Nombres = item.Nombres, Apellidos = item.Apellidos },
                new Alumno { Nombres = item.Nombres, Apellidos = item.Apellidos,
                    NumeroDocumento = kind == "destination" ? null : "123" });
            await db.SaveChangesAsync();
        }
        var report = await new TransferenciaService(db).ProcesarAsync(Paquete("alumnos", Vacio() with { Alumnos = students }), "actualizar");
        Assert.False(report.PuedeImportar);
        Assert.False(report.Importado);
        Assert.Contains(report.Errores, e => e.Contains("ambigu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StudentWithDniCannotSilentlyMatchAnUndocumentedNamesake()
    {
        using var db = Db();
        var item = Alumno();
        db.Alumnos.Add(new() { Nombres = item.Nombres, Apellidos = item.Apellidos, FechaNacimiento = item.FechaNacimiento });
        await db.SaveChangesAsync();
        var report = await new TransferenciaService(db).ProcesarAsync(Paquete("alumnos", Vacio() with { Alumnos = [item] }), "actualizar");
        Assert.False(report.PuedeImportar);
        Assert.Contains("sin DNI", report.Errores.Single());
    }

    [Fact]
    public async Task NullableTransferStillRejectsProvidedInvalidBirthDatesAndGender()
    {
        using var db = Db();
        var invalidGender = JsonSerializer.SerializeToNode(Paquete("alumnos", Vacio() with { Alumnos = [Alumno()] }), TransferenciaService.JsonOptions)!;
        invalidGender["datos"]!["alumnos"]![0]!["genero"] = "Desconocido";
        Assert.Throws<ArgumentException>(() => TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(invalidGender)));
        foreach (var item in new[] { Alumno() with { FechaNacimiento = DateOnly.MinValue },
            Alumno() with { FechaNacimiento = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) } })
        {
            var report = await new TransferenciaService(db).ProcesarAsync(Paquete("alumnos", Vacio() with { Alumnos = [item] }), "actualizar");
            Assert.False(report.PuedeImportar);
        }
        Assert.Empty(db.Alumnos);
    }

    [Theory]
    [InlineData("docentes")]
    [InlineData("materias")]
    [InlineData("horarios")]
    [InlineData("alumnos")]
    [InlineData("usuarios")]
    [InlineData("roles")]
    [InlineData("responsabilidades")]
    [InlineData("cursos")]
    public async Task EmptyExportsAreValidVersionedPortablePackages(string seccion)
    {
        using var db = Db();
        var p = await new TransferenciaService(db).ExportarAsync(seccion);
        var json = JsonSerializer.SerializeToElement(p, TransferenciaService.JsonOptions);
        Assert.Equal(seccion, TransferenciaService.LeerPaquete(json).Seccion);
        Assert.DoesNotContain("userId", json.GetRawText());
        Assert.DoesNotContain("password", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(p.Alcance);
        Assert.NotEmpty(p.Exclusiones);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("record-null")]
    [InlineData("id")]
    [InlineData("identity")]
    [InlineData("nested-missing")]
    [InlineData("version")]
    [InlineData("section")]
    [InlineData("numeric-enum")]
    public void MalformedOrNonPortablePackagesAreRejected(string kind)
    {
        var node = JsonSerializer.SerializeToNode(Horarios(), TransferenciaService.JsonOptions)!;
        var data = node["datos"]!;
        switch (kind)
        {
            case "missing": data.AsObject().Remove("alumnos"); break;
            case "null": data["alumnos"] = null; break;
            case "record-null": data["docentes"]![0] = null; break;
            case "id": data["docentes"]![0]!["id"] = 44; break;
            case "identity": data["docentes"]![0]!["userId"] = Guid.NewGuid().ToString(); break;
            case "nested-missing": data["materias"]![0]!.AsObject().Remove("docentes"); break;
            case "version": node["version"] = 2; break;
            case "section": node["seccion"] = "desconocida"; break;
            case "numeric-enum": data["bloques"]![0]!["tipo"] = 0; break;
        }
        Assert.Throws<ArgumentException>(() => TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(node)));
    }

    [Fact]
    public void DuplicateJsonPropertiesAndOversizedFilesAreRejected()
    {
        using var doc = JsonDocument.Parse("{\"formato\":\"a\",\"Formato\":\"b\"}");
        Assert.Throws<ArgumentException>(() => TransferenciaService.LeerPaquete(doc.RootElement));
        var large = JsonSerializer.SerializeToElement(new { data = new string('x', TransferenciaService.MaxBytes) });
        Assert.Contains("20 MB", Assert.Throws<ArgumentException>(() => TransferenciaService.LeerPaquete(large)).Message);
    }

    [Fact]
    public async Task PreviewHasNoWritesAndFingerprintIgnoresExportTimestamp()
    {
        using var db = Db();
        var service = new TransferenciaService(db); var p = Horarios();
        var first = await service.ProcesarAsync(p, "actualizar");
        Assert.True(first.PuedeImportar);
        Assert.Empty(db.Anexos); Assert.Empty(db.Docentes); Assert.Empty(db.HorariosMateria);
        var second = await service.ProcesarAsync(p with { ExportadoEn = p.ExportadoEn.AddDays(1) }, "actualizar");
        Assert.Equal(first.Huella, second.Huella);
        Assert.Equal(first.Huella, (await service.ProcesarAsync(p, "actualizar")).Huella);
        Assert.NotEqual(first.Huella, (await service.ProcesarAsync(p, "reemplazar")).Huella);
    }

    [Fact]
    public async Task StalePreviewAndPayloadChangesCannotApply()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        var p = Paquete("docentes", Vacio() with { Docentes = [Docente()] });
        var preview = await service.ProcesarAsync(p, "actualizar");
        db.Docentes.Add(new() { Nombres = "Otro", Apellidos = "Docente", Email = "otro@example.com" });
        await db.SaveChangesAsync();
        var stale = await service.ProcesarAsync(p, "actualizar", true, preview.Huella);
        Assert.False(stale.Importado); Assert.False(stale.PuedeImportar); Assert.Single(db.Docentes);
        preview = await service.ProcesarAsync(p, "actualizar");
        var changed = p with { Datos = p.Datos with { Docentes = [Docente("distinto@example.com")] } };
        Assert.False((await service.ProcesarAsync(changed, "actualizar", true, preview.Huella)).Importado);
        Assert.Single(db.Docentes);
    }

    [Fact]
    public async Task ReplaceRequiresExtraConfirmationAndPreview()
    {
        using var db = Db(); var service = new TransferenciaService(db); var p = Paquete("docentes");
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProcesarAsync(p, "reemplazar", true));
        var preview = await service.ProcesarAsync(p, "reemplazar");
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProcesarAsync(p, "reemplazar", true, preview.Huella, "reemplazar"));
    }

    [Fact]
    public async Task TeacherUpdatesPreserveDestinationIdAndUserLink()
    {
        using var db = Db(); var userId = Guid.NewGuid();
        db.Docentes.Add(new() { Id = 91, Nombres = "Anterior", Apellidos = "Perez", Email = "ana@example.com", UserId = userId });
        await db.SaveChangesAsync();
        await Aplicar(new(db), Paquete("docentes", Vacio() with { Docentes = [Docente()] }), "reemplazar");
        var teacher = await db.Docentes.SingleAsync();
        Assert.Equal(91, teacher.Id); Assert.Equal(userId, teacher.UserId); Assert.Equal("Ana", teacher.Nombres);
        Assert.Empty(db.Users); Assert.Empty(db.Auths);
    }

    [Fact]
    public async Task TeacherReplaceBlocksLinkedUsersAndSubjectReferences()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        await Aplicar(service, Horarios());
        var report = await service.ProcesarAsync(Paquete("docentes"), "reemplazar");
        Assert.False(report.PuedeImportar); Assert.Contains("materias", report.Errores.Single());
        db.MateriaDocentes.RemoveRange(db.MateriaDocentes); await db.SaveChangesAsync();
        var teacher = await db.Docentes.SingleAsync(); teacher.UserId = Guid.NewGuid(); await db.SaveChangesAsync();
        Assert.False((await service.ProcesarAsync(Paquete("docentes"), "reemplazar")).PuedeImportar);
    }

    [Fact]
    public async Task UnreferencedTeachersCanBeReplacedWithoutDeletingPrerequisites()
    {
        using var db = Db(); db.Docentes.Add(new() { Nombres = "Viejo", Apellidos = "Libre" });
        db.Anexos.Add(new() { Nombre = "Conservar" }); await db.SaveChangesAsync();
        var report = await Aplicar(new(db), Paquete("docentes", Vacio() with { Docentes = [Docente()] }), "reemplazar");
        Assert.Equal(1, report.Resumen.Single(x => x.Entidad == "docentes").Eliminar);
        Assert.Equal("Ana", (await db.Docentes.SingleAsync()).Nombres); Assert.Single(db.Anexos);
    }

    [Fact]
    public async Task AmbiguousTeacherNamesAndDuplicateNaturalKeysAreBlocked()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        db.Docentes.AddRange(new Docente { Nombres = "Ana", Apellidos = "Perez" },
            new Docente { Nombres = " ANA ", Apellidos = "PEREZ", Email = "different@example.com" });
        await db.SaveChangesAsync();
        Assert.False((await service.ProcesarAsync(Paquete("docentes", Vacio() with { Docentes = [Docente(null)] }), "actualizar")).PuedeImportar);
        var duplicate = Paquete("docentes", Vacio() with { Docentes = [Docente(), Docente("ANA@example.com")] });
        Assert.False((await service.ProcesarAsync(duplicate, "actualizar")).PuedeImportar);
    }

    [Fact]
    public async Task CompleteScheduleCanRoundTripToAnEmptyEnvironmentWithDifferentIds()
    {
        using var source = Db(); var service = new TransferenciaService(source);
        await Aplicar(service, Horarios());
        var exported = await service.ExportarAsync("horarios");
        using var target = Db();
        target.Anexos.Add(new() { Id = 100, Nombre = "No relacionado" });
        target.Docentes.Add(new() { Id = 100, Nombres = "Otro", Apellidos = "Docente", Email = "otro@example.com" });
        await target.SaveChangesAsync();
        await Aplicar(new(target), TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(exported, TransferenciaService.JsonOptions)));
        var materia = await target.Materias.Include(m => m.Curso).ThenInclude(c => c.Anexo).Include(m => m.Horarios).SingleAsync();
        Assert.Equal("Centro", materia.Curso.Anexo.Nombre); Assert.NotEqual(1, materia.Curso.AnexoId);
        var join = await target.MateriaDocentes.Include(x => x.Docente).SingleAsync();
        Assert.Equal("ana@example.com", join.Docente.Email); Assert.NotEqual(1, join.DocenteId);
        Assert.Single(materia.Horarios); Assert.Equal(materia.CursoId, materia.Horarios.Single().CursoId);
        Assert.Equal(2, await target.Anexos.CountAsync());
        var blockId = (await target.HorariosAnexo.SingleAsync()).Id;
        var assignmentId = materia.Horarios.Single().Id;
        await Aplicar(new(target), exported, "reemplazar");
        Assert.Equal(blockId, (await target.HorariosAnexo.SingleAsync()).Id);
        Assert.Equal(assignmentId, (await target.HorariosMateria.SingleAsync()).Id);
        Assert.Equal(2, await target.Anexos.CountAsync());
    }

    [Fact]
    public async Task ExistingPrerequisiteMismatchesBlockRatherThanOverwrite()
    {
        using var db = Db();
        db.Orientaciones.Add(new() { Nombre = "Sociales", NombreCorto = "OTRO" }); await db.SaveChangesAsync();
        var report = await new TransferenciaService(db).ProcesarAsync(Horarios(), "actualizar");
        Assert.False(report.PuedeImportar); Assert.Empty(db.Anexos); Assert.Empty(db.Materias);
        Assert.Equal("OTRO", (await db.Orientaciones.SingleAsync()).NombreCorto);
    }

    [Fact]
    public async Task SubjectTransferPreservesSchedulesAndProtectsHistoryFromReplacement()
    {
        using var db = Db(); var service = new TransferenciaService(db); await Aplicar(service, Horarios());
        var exported = await service.ExportarAsync("materias"); var scheduleId = (await db.HorariosMateria.SingleAsync()).Id;
        await Aplicar(service, exported);
        Assert.Equal(scheduleId, (await db.HorariosMateria.SingleAsync()).Id);
        Assert.False((await service.ProcesarAsync(Paquete("materias"), "reemplazar")).PuedeImportar);
        var changed = exported with { Datos = exported.Datos with { Materias = [exported.Datos.Materias[0] with { CargaHoraria = 2 }] } };
        Assert.False((await service.ProcesarAsync(changed, "actualizar")).PuedeImportar);
        Assert.Equal(1, (await db.Materias.SingleAsync()).CargaHoraria);
    }

    [Fact]
    public async Task SubjectReplaceBlocksEvenSoftDeletedStudentHistory()
    {
        using var db = Db(); var service = new TransferenciaService(db); await Aplicar(service, Horarios());
        db.HorariosMateria.RemoveRange(db.HorariosMateria);
        var materia = await db.Materias.SingleAsync();
        var alumno = new Alumno { NumeroDocumento = "999", Eliminado = true };
        var inscripcion = new Inscripcion { Alumno = alumno, CursoId = materia.CursoId };
        db.CursadasMaterias.Add(new() { MateriaId = materia.Id, Inscripcion = inscripcion });
        await db.SaveChangesAsync();
        var report = await service.ProcesarAsync(Paquete("materias"), "reemplazar");
        Assert.False(report.PuedeImportar); Assert.Contains("historial", report.Errores.Single());
        Assert.Single(db.CursadasMaterias.IgnoreQueryFilters());
    }

    [Theory]
    [InlineData("weekend")]
    [InlineData("duration")]
    [InlineData("overlap")]
    [InlineData("break")]
    [InlineData("load")]
    [InlineData("active")]
    [InlineData("occupancy")]
    [InlineData("missing-context")]
    public async Task InvalidSchedulesAreBlockedWithoutWritingPrerequisites(string kind)
    {
        using var db = Db(); var p = Horarios(); var d = p.Datos;
        var b = d.Bloques[0]; var m = d.Materias[0];
        switch (kind)
        {
            case "weekend": d = d with { Bloques = [b with { Clave = b.Clave with { DiaSemana = DayOfWeek.Sunday } }] }; break;
            case "duration": d = d with { Bloques = [b with { HoraFin = b.HoraInicio }] }; break;
            case "overlap": d = d with { Bloques = [b, b with { Clave = b.Clave with { Orden = 2 } }] }; break;
            case "break": d = d with { Bloques = [b with { Tipo = TipoBloqueHorario.Receso }] }; break;
            case "load": d = d with { Materias = [m with { CargaHoraria = 2 }] }; break;
            case "active": d = d with { Materias = [m with { Docentes = [new(Docente(), "Titular", false)] }] }; break;
            case "occupancy":
                var second = m with { Clave = m.Clave with { Nombre = "Historia" } };
                d = d with { Materias = [m, second], Asignaciones = [d.Asignaciones[0], new(second.Clave, b.Clave)] }; break;
            case "missing-context": d = d with { Cursos = [] }; break;
        }
        var report = await new TransferenciaService(db).ProcesarAsync(p with { Datos = d }, "actualizar");
        Assert.False(report.PuedeImportar); Assert.Empty(db.Anexos); Assert.Empty(db.Materias); Assert.Empty(db.HorariosAnexo);
    }

    [Fact]
    public async Task AssignedGridCannotBeEditedOrRemovedAndUpdateDoesNotDeleteAssignments()
    {
        using var db = Db(); var service = new TransferenciaService(db); var p = Horarios(); await Aplicar(service, p);
        var changed = p with { Datos = p.Datos with { Bloques = [p.Datos.Bloques[0] with { HoraFin = new(20, 0) }] } };
        Assert.False((await service.ProcesarAsync(changed, "reemplazar")).PuedeImportar);
        Assert.False((await service.ProcesarAsync(Paquete("horarios"), "reemplazar")).PuedeImportar);
        var withoutAssignments = p with { Datos = p.Datos with { Asignaciones = [] } };
        await Aplicar(service, withoutAssignments);
        Assert.Single(db.HorariosMateria);
        await Aplicar(service, withoutAssignments, "reemplazar");
        Assert.Empty(db.HorariosMateria); Assert.Single(db.Materias); Assert.Single(db.HorariosAnexo);
    }

    [Fact]
    public async Task StudentProfilesRoundTripAndUpdatesPreserveContactIdsAndDocuments()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        var p = Paquete("alumnos", Vacio() with { Alumnos = [Alumno()] });
        await Aplicar(service, p); var alumno = await db.Alumnos.Include(x => x.Contacto).SingleAsync();
        var id = alumno.Id; var contactId = alumno.Contacto.Id;
        var exported = await service.ExportarAsync("alumnos");
        Assert.Contains(exported.Alcance, s => s.Contains("Contacto"));
        Assert.Contains(exported.Exclusiones, s => s.Contains("calificaciones"));
        Assert.DoesNotContain("inscripciones", JsonSerializer.Serialize(exported.Datos, TransferenciaService.JsonOptions));
        var tipo = new TipoDocumentoAlumno { Nombre = "DNI" };
        db.AlumnoDocumentos.Add(new() { AlumnoId = id, TipoDocumentoAlumno = tipo }); await db.SaveChangesAsync();
        var changed = p with { Datos = p.Datos with { Alumnos = [Alumno() with { Nombres = "Actualizada", Contacto = null }] } };
        await Aplicar(service, changed, "reemplazar");
        alumno = await db.Alumnos.Include(x => x.Contacto).Include(x => x.DatosNacimiento).SingleAsync();
        Assert.Equal(id, alumno.Id); Assert.Equal(contactId, alumno.Contacto.Id); Assert.Equal("111", alumno.Contacto.TelefonoAlumno);
        Assert.Equal("Ushuaia", alumno.DatosNacimiento.Localidad); Assert.Single(db.AlumnoDocumentos);
        using var target = Db(); await Aplicar(new(target), exported);
        Assert.Equal("123", (await target.Alumnos.SingleAsync()).NumeroDocumento);
        Assert.Single(target.AlumnoContactos); Assert.Single(target.AlumnoNacimientos);
    }

    [Fact]
    public async Task StudentReplaceSoftDeletesOnlySafeAbsencesAndBlocksDocumentsOrHistory()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        await Aplicar(service, Paquete("alumnos", Vacio() with { Alumnos = [Alumno()] }));
        var report = await Aplicar(service, Paquete("alumnos"), "reemplazar");
        Assert.Equal(1, report.Resumen.Single().Eliminar); Assert.Empty(db.Alumnos);
        Assert.True((await db.Alumnos.IgnoreQueryFilters().SingleAsync()).Eliminado);
        Assert.False((await service.ProcesarAsync(Paquete("alumnos", Vacio() with { Alumnos = [Alumno()] }), "actualizar")).PuedeImportar);
        var alumno = await db.Alumnos.IgnoreQueryFilters().SingleAsync(); alumno.Eliminado = false;
        db.AlumnoDocumentos.Add(new() { AlumnoId = alumno.Id, TipoDocumentoAlumno = new() { Nombre = "DNI" } }); await db.SaveChangesAsync();
        Assert.False((await service.ProcesarAsync(Paquete("alumnos"), "reemplazar")).PuedeImportar);
        Assert.Single(db.Alumnos); Assert.Single(db.AlumnoDocumentos);
    }

    [Fact]
    public async Task StudentDuplicateDocumentAndCrossSectionDataAreBlocked()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        var p = Paquete("alumnos", Vacio() with { Alumnos = [Alumno(), Alumno(" 123 ")] });
        Assert.False((await service.ProcesarAsync(p, "actualizar")).PuedeImportar);
        p = Paquete("docentes", Vacio() with { Alumnos = [Alumno()] });
        Assert.False((await service.ProcesarAsync(p, "actualizar")).PuedeImportar);
    }

    [Fact]
    public async Task ApiReturnsPreview200ImportConflictAndMalformed400()
    {
        using var db = Db(); var controller = new TransferenciasController(new(db));
        var actor = await CrearUsuario(db, "admin@example.com", "Admin");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString())], "test"))
        } };
        var p = Horarios();
        var invalid = p with { Datos = p.Datos with { Materias = [p.Datos.Materias[0] with { CargaHoraria = 3 }] } };
        var request = new SolicitudTransferencia
        {
            Paquete = JsonSerializer.SerializeToElement(invalid, TransferenciaService.JsonOptions), Modo = "actualizar"
        };
        var preview = Assert.IsType<OkObjectResult>(await controller.Preview(request, default));
        Assert.False(Assert.IsType<ReporteTransferencia>(preview.Value).PuedeImportar);
        Assert.IsType<ConflictObjectResult>(await controller.Importar(request with { Huella = "anterior" }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Preview(request with { Paquete = JsonSerializer.SerializeToElement(new { id = 1 }) }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Preview(request with { Modo = "borrar" }, default));
        var download = Assert.IsType<FileContentResult>(await controller.Exportar("alumnos", default));
        Assert.Equal("application/json", download.ContentType); Assert.EndsWith(".json", download.FileDownloadName);
        Assert.Empty(db.Anexos); Assert.Empty(db.Materias);
    }

    [Fact]
    public void ImportAcceptsOptionalMetadataButNeverOptionalDataLists()
    {
        var node = JsonSerializer.SerializeToNode(Paquete("docentes"), TransferenciaService.JsonOptions)!.AsObject();
        node.Remove("alcance"); node.Remove("exclusiones");
        Assert.Equal("docentes", TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(node)).Seccion);
    }

    [Fact]
    public async Task TeacherRolesCanSwapWithoutChangingSubjectOrTeacherIds()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        var p = Horarios(); var second = new DocenteTransferencia("Luis", "Diaz", "luis@example.com");
        p = p with { Datos = p.Datos with { Docentes = [Docente(), second], Materias = [p.Datos.Materias[0] with
        {
            Docentes = [new(Docente(), "Titular", true), new(second, "Suplente", false)]
        }] } };
        await Aplicar(service, p);
        var exported = await service.ExportarAsync("materias"); var id = (await db.Materias.SingleAsync()).Id;
        var ids = await db.Docentes.Select(x => x.Id).OrderBy(x => x).ToListAsync();
        var changed = exported with { Datos = exported.Datos with { Materias = [exported.Datos.Materias[0] with
        {
            Docentes = [new(Docente(), "Suplente", false), new(second, "Titular", true)]
        }] } };
        await Aplicar(service, changed);
        Assert.Equal(id, (await db.Materias.SingleAsync()).Id);
        Assert.Equal(ids, await db.Docentes.Select(x => x.Id).OrderBy(x => x).ToListAsync());
        Assert.Equal("luis@example.com", (await db.MateriaDocentes.Include(x => x.Docente).SingleAsync(x => x.Activo)).Docente.Email);
        Assert.Single(db.HorariosMateria);
    }

    [Fact]
    public async Task ReplaceCanMoveCourseOccupancyBetweenSubjectsButUpdateCannot()
    {
        using var db = Db(); var service = new TransferenciaService(db); var p = Horarios(); await Aplicar(service, p);
        var second = p.Datos.Materias[0] with { Clave = p.Datos.Materias[0].Clave with { Nombre = "Historia" } };
        var changed = p with { Datos = p.Datos with { Materias = [p.Datos.Materias[0], second],
            Asignaciones = [new(second.Clave, p.Datos.Bloques[0].Clave)] } };
        Assert.False((await service.ProcesarAsync(changed, "actualizar")).PuedeImportar);
        await Aplicar(service, changed, "reemplazar");
        Assert.Equal("Historia", (await db.HorariosMateria.Include(x => x.Materia).SingleAsync()).Materia.Nombre);
        Assert.Equal(2, await db.Materias.CountAsync());
    }

    [Fact]
    public async Task SameBlockCanBeAssignedToDifferentCourses()
    {
        using var db = Db(); var p = Horarios(); var first = p.Datos.Materias[0];
        var otherCourse = first.Clave.Curso with { Division = "B" };
        var second = first with { Clave = first.Clave with { Curso = otherCourse } };
        p = p with { Datos = p.Datos with { Cursos = [p.Datos.Cursos[0], new(otherCourse, false)],
            Materias = [first, second], Asignaciones = [p.Datos.Asignaciones[0], new(second.Clave, p.Datos.Bloques[0].Clave)] } };
        await Aplicar(new(db), p);
        Assert.Equal(2, await db.HorariosMateria.CountAsync());
        Assert.Equal(2, await db.HorariosMateria.Select(x => x.CursoId).Distinct().CountAsync());
    }

    [Fact]
    public async Task DestinationDuplicateCourseKeysAreBlocked()
    {
        using var db = Db(); var service = new TransferenciaService(db); await Aplicar(service, Horarios());
        var course = await db.Cursos.SingleAsync();
        db.Cursos.Add(new() { CursoNombre = course.CursoNombre, Division = " a ", AnexoId = course.AnexoId, OrientacionId = course.OrientacionId });
        await db.SaveChangesAsync();
        Assert.False((await service.ProcesarAsync(Horarios(), "actualizar")).PuedeImportar);
        Assert.Single(db.Materias);
    }

    private static async Task<User> CrearUsuario(AppDbContext db, string email = "ana@example.com", string rol = "Docente")
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == rol) ?? new Role { Id = Guid.NewGuid(), Name = rol };
        var user = new User { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Perez", Status = true, Role = role };
        user.Auth = new Auth { Id = user.Id, User = user, Email = email, PasswordHash = "hash-destino" };
        db.Users.Add(user); await db.SaveChangesAsync();
        return user;
    }

    private static PaqueteTransferencia Usuarios() => Paquete("usuarios", Vacio() with
    {
        Usuarios = [new("ana@example.com", "Ana", "Perez", true, "Docente")],
        Roles = [new("Docente", ["Alumnos.Leer"])],
        Responsabilidades = [new("Alumnos.Leer", null)]
    });

    [Fact]
    public void OldV1FilesDefaultSecurityListsButRejectUnknownSecretsAndNullLists()
    {
        var node = JsonSerializer.SerializeToNode(Paquete("docentes"), TransferenciaService.JsonOptions)!;
        foreach (var name in new[] { "usuarios", "roles", "responsabilidades" }) node["datos"]!.AsObject().Remove(name);
        Assert.Empty(TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(node)).Datos.Usuarios);
        node = JsonSerializer.SerializeToNode(Usuarios(), TransferenciaService.JsonOptions)!;
        Assert.Null(TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(node)).Datos.Responsabilidades[0].Descripcion);
        node["datos"]!["usuarios"]![0]!["passwordHash"] = "secret";
        Assert.Throws<ArgumentException>(() => TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(node)));
        node["datos"]!["usuarios"]![0]!.AsObject().Remove("passwordHash");
        node["datos"]!["roles"] = null;
        Assert.Throws<ArgumentException>(() => TransferenciaService.LeerPaquete(JsonSerializer.SerializeToElement(node)));
    }

    [Fact]
    public async Task UsersRoundTripWithOnlyProfilesAndNewUnexportedInitialAccessSecret()
    {
        using var db = Db(); var service = new TransferenciaService(db); var p = Usuarios();
        var preview = await service.ProcesarAsync(p, "actualizar");
        Assert.True(preview.PuedeImportar); Assert.Empty(db.Users); Assert.Empty(db.Auths); Assert.Empty(db.Roles);
        Assert.Contains(preview.Advertencias, x => x.Contains("RequestInitialAccess"));
        await Aplicar(service, p);
        var user = await db.Users.Include(u => u.Auth).Include(u => u.Role).ThenInclude(r => r.Responsibilities).SingleAsync();
        Assert.NotEqual(Guid.Empty, user.Id); Assert.Equal(user.Id, user.Auth.Id);
        Assert.True(user.Auth.MustChangePassword); Assert.Null(user.Auth.InitialAccessSentAt);
        Assert.StartsWith("$2", user.Auth.PasswordHash); Assert.Single(user.Role.Responsibilities);
        var exported = await service.ExportarAsync("usuarios");
        var json = JsonSerializer.Serialize(exported.Datos, TransferenciaService.JsonOptions);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.Id.ToString(), json);
        Assert.Single(exported.Datos.Roles); Assert.Single(exported.Datos.Responsabilidades);
        using var target = Db(); await Aplicar(new(target), exported);
        Assert.NotEqual(user.Id, (await target.Users.SingleAsync()).Id);
        Assert.NotEqual(user.Auth.PasswordHash, (await target.Auths.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task MatchingEmailPreservesAuthImageAndTeacherLinksAndSecurityStateInvalidatesPreview()
    {
        using var db = Db(); var user = await CrearUsuario(db); user.Image = "keep.jpg";
        db.Docentes.Add(new() { Nombres = "Ana", Apellidos = "Perez", UserId = user.Id }); await db.SaveChangesAsync();
        var service = new TransferenciaService(db);
        var p = Usuarios() with { Datos = Usuarios().Datos with { Roles = [new("Docente", [])], Responsabilidades = [] } };
        var preview = await service.ProcesarAsync(p, "actualizar");
        var auth = await db.Auths.SingleAsync(); auth.PasswordHash = "hash-nuevo"; await db.SaveChangesAsync();
        Assert.False((await service.ProcesarAsync(p, "actualizar", true, preview.Huella)).Importado);
        p = p with { Datos = p.Datos with { Usuarios = [p.Datos.Usuarios[0] with { Email = " ANA@example.com ", Nombres = "Actualizada" }] } };
        await Aplicar(service, p);
        user = await db.Users.Include(u => u.Auth).SingleAsync();
        Assert.Equal("Actualizada", user.FirstName); Assert.Equal("hash-nuevo", user.Auth.PasswordHash);
        Assert.Equal("ana@example.com", user.Auth.Email); Assert.Equal("keep.jpg", user.Image);
        Assert.Equal(user.Id, (await db.Docentes.SingleAsync()).UserId);
    }

    [Fact]
    public async Task SecurityPrerequisitesCannotOverwriteExistingGrantsOrDescriptions()
    {
        using var db = Db(); var service = new TransferenciaService(db); await Aplicar(service, Usuarios());
        var p = Usuarios() with { Datos = Usuarios().Datos with { Roles = [new("Docente", [])], Responsabilidades = [] } };
        var report = await service.ProcesarAsync(p, "actualizar");
        Assert.Contains("Importa roles primero", report.Errores.Single());
        p = Paquete("roles", Usuarios().Datos with { Usuarios = [], Responsabilidades = [new("Alumnos.Leer", "Diferente")] });
        report = await service.ProcesarAsync(p, "actualizar");
        Assert.Contains("Importa responsabilidades primero", report.Errores.Single());
        Assert.Single(await db.Set<RoleResponsibility>().ToListAsync());
        Assert.Null((await db.Responsibilities.SingleAsync()).Description);
    }

    [Fact]
    public async Task OwnerSectionsUpdateGrantsAndBlockReferencedGlobalReplacements()
    {
        using var db = Db(); var service = new TransferenciaService(db); await Aplicar(service, Usuarios());
        Assert.False((await service.ProcesarAsync(Paquete("roles"), "reemplazar")).PuedeImportar);
        Assert.False((await service.ProcesarAsync(Paquete("responsabilidades"), "reemplazar")).PuedeImportar);
        await Aplicar(service, Paquete("responsabilidades", Vacio() with { Responsabilidades = [new("Alumnos.Leer", "Leer alumnos")] }));
        Assert.Equal("Leer alumnos", (await db.Responsibilities.SingleAsync()).Description);
        await Aplicar(service, Paquete("roles", Vacio() with { Roles = [new("Docente", [])] }));
        Assert.Empty(db.Set<RoleResponsibility>());
        await Aplicar(service, Paquete("responsabilidades"), "reemplazar"); Assert.Empty(db.Responsibilities);
        await Aplicar(service, Paquete("roles", Vacio() with { Roles = [new("Sin usuarios", [])] }));
        var exported = await service.ExportarAsync("roles");
        Assert.Equal(2, exported.Datos.Roles.Count); Assert.Empty(exported.Datos.Usuarios);
    }

    [Fact]
    public async Task AdminRoleGrantsAndLastActiveAdminAndCurrentCallerAreProtected()
    {
        using var db = Db(); var admin = await CrearUsuario(db, "admin@example.com", "Admin");
        var responsibility = new Responsibility { Id = Guid.NewGuid(), Name = "Administracion.Escribir" };
        db.Responsibilities.Add(responsibility);
        admin.Role.Responsibilities.Add(new RoleResponsibility { Role = admin.Role, Responsibility = responsibility });
        await db.SaveChangesAsync(); var service = new TransferenciaService(db);
        Assert.False((await service.ProcesarAsync(Paquete("roles"), "reemplazar")).PuedeImportar);
        var rolePackage = Paquete("roles", Vacio() with { Roles = [new("Admin", [])] });
        Assert.Contains("Admin", (await service.ProcesarAsync(rolePackage, "actualizar")).Errores.Single());
        var p = Paquete("usuarios", Vacio() with
        {
            Usuarios = [new("admin@example.com", "Admin", "Actual", true, "Docente")], Roles = [new("Docente", [])]
        });
        Assert.Contains("ultimo Admin", (await service.ProcesarAsync(p, "actualizar")).Errores.Single());
        Assert.Contains("propio rol", (await service.ProcesarAsync(p, "actualizar", actorId: admin.Id)).Errores.Single());
        Assert.False((await service.ProcesarAsync(Paquete("usuarios"), "reemplazar", actorId: admin.Id)).PuedeImportar);
        Assert.Contains("ultimo Admin", (await service.ProcesarAsync(Paquete("usuarios"), "reemplazar")).Errores.Single());
        var nonAdmin = await CrearUsuario(db, "other@example.com");
        Assert.False((await service.ProcesarAsync(Paquete("roles"), "actualizar", actorId: nonAdmin.Id)).PuedeImportar);
    }

    [Fact]
    public async Task UserDeactivationAndInactiveCreationPreserveAuthAndHistoricalLinks()
    {
        using var db = Db(); var user = await CrearUsuario(db); var service = new TransferenciaService(db);
        db.Docentes.Add(new() { Nombres = "Ana", Apellidos = "Perez", UserId = user.Id }); await db.SaveChangesAsync();
        var preview = await service.ProcesarAsync(Paquete("usuarios"), "reemplazar");
        Assert.True(preview.PuedeImportar); Assert.True((await db.Users.SingleAsync()).Status);
        var report = await Aplicar(service, Paquete("usuarios"), "reemplazar");
        Assert.Equal(1, report.Resumen.Single(x => x.Entidad == "usuarios (baja logica)").Eliminar);
        Assert.Single(db.Users); Assert.Single(db.Auths); Assert.False((await db.Users.SingleAsync()).Status);
        Assert.Equal(user.Id, (await db.Docentes.SingleAsync()).UserId);
        Assert.Equal("hash-destino", (await db.Auths.SingleAsync()).PasswordHash);
        var p = Usuarios() with { Datos = Usuarios().Datos with
        {
            Usuarios = [Usuarios().Datos.Usuarios[0] with { Activo = false }], Roles = [new("Docente", [])], Responsabilidades = []
        } };
        await Aplicar(service, p);
        Assert.False((await db.Users.SingleAsync()).Status);
        p = p with { Datos = p.Datos with { Usuarios = [p.Datos.Usuarios[0] with { Activo = true }] } };
        await Aplicar(service, p); Assert.True((await db.Users.SingleAsync()).Status);
        using var empty = Db();
        await Aplicar(new(empty), p with { Datos = p.Datos with { Usuarios = [p.Datos.Usuarios[0] with { Activo = false }] } });
        Assert.False((await empty.Users.SingleAsync()).Status); Assert.True((await empty.Auths.SingleAsync()).MustChangePassword);
    }

    [Fact]
    public async Task SecurityRelationshipChangesInvalidatePreviewAndRoleReplacementRetainsIdentity()
    {
        using var db = Db(); var service = new TransferenciaService(db); await Aplicar(service, Usuarios());
        var p = await service.ExportarAsync("usuarios");
        var preview = await service.ProcesarAsync(p, "actualizar");
        db.Set<RoleResponsibility>().RemoveRange(db.Set<RoleResponsibility>()); await db.SaveChangesAsync();
        Assert.False((await service.ProcesarAsync(p, "actualizar", true, preview.Huella)).Importado);
        var roleId = (await db.Roles.SingleAsync()).Id;
        await Aplicar(service, Paquete("roles", p.Datos with { Usuarios = [] }));
        Assert.Equal(roleId, (await db.Roles.SingleAsync()).Id); Assert.Single(db.Set<RoleResponsibility>());
        await Aplicar(service, Paquete("roles", Vacio() with { Roles = [new("Docente", [])] }));
        Assert.Equal(roleId, (await db.Users.SingleAsync()).RoleId); Assert.Empty(db.Set<RoleResponsibility>());
    }

    [Fact]
    public async Task ControllerRejectsMissingActorAndStaleAdminCannotExportOrModify()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        var controller = new TransferenciasController(service);
        var request = new SolicitudTransferencia
        {
            Paquete = JsonSerializer.SerializeToElement(Paquete("roles"), TransferenciaService.JsonOptions), Modo = "actualizar"
        };
        Assert.IsType<ForbidResult>(await controller.Preview(request, default));
        var user = await CrearUsuario(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, "Admin")], "test"))
        } };
        Assert.IsType<ForbidResult>(await controller.Exportar("usuarios", default));
        var preview = Assert.IsType<OkObjectResult>(await controller.Preview(request, default));
        Assert.False(Assert.IsType<ReporteTransferencia>(preview.Value).PuedeImportar);
        Assert.IsType<ConflictObjectResult>(await controller.Importar(request with { Huella = "stale" }, default));
        Assert.Equal("Docente", (await db.Roles.SingleAsync()).Name);
    }

    [Fact]
    public async Task CoursesOwnSectionUpdatesModalityAndBlocksGlobalIdentityConflictsAndHistoricalDeletion()
    {
        using var db = Db(); var service = new TransferenciaService(db);
        var p = Paquete("cursos", Horarios().Datos with { Docentes = [], Materias = [], Bloques = [], Asignaciones = [] });
        await Aplicar(service, p); var id = (await db.Cursos.SingleAsync()).Id;
        p = p with { Datos = p.Datos with { Cursos = [p.Datos.Cursos[0] with { Semipresencial = true }] } };
        await Aplicar(service, p); Assert.True((await db.Cursos.SingleAsync()).Semipresencial);
        var exported = await service.ExportarAsync("cursos");
        Assert.Single(exported.Datos.Cursos); Assert.Single(exported.Datos.Anexos); Assert.Single(exported.Datos.Orientaciones);
        var conflict = p with { Datos = p.Datos with
        {
            Anexos = [new("Otro")], Cursos = [p.Datos.Cursos[0] with { Clave = p.Datos.Cursos[0].Clave with { Anexo = "Otro" } }]
        } };
        Assert.Contains("global", (await service.ProcesarAsync(conflict, "actualizar")).Errores.Single()); Assert.Single(db.Anexos);
        db.Inscripciones.Add(new() { CursoId = id, Alumno = new() { NumeroDocumento = "999", Eliminado = true } });
        await db.SaveChangesAsync();
        Assert.False((await service.ProcesarAsync(Paquete("cursos"), "reemplazar")).PuedeImportar);
        Assert.Equal(id, (await db.Cursos.SingleAsync()).Id);
    }

    [Theory]
    [InlineData("1", "A")]
    [InlineData("4\u00b0", "A")]
    [InlineData("1\u00b0", "L")]
    public async Task InvalidCourseBusinessLabelsAreRejected(string year, string division)
    {
        using var db = Db(); var d = Horarios().Datos;
        var p = Paquete("cursos", Vacio() with { Anexos = d.Anexos, Orientaciones = d.Orientaciones,
            Cursos = [new(d.Cursos[0].Clave with { CursoNombre = year, Division = division }, false)] });
        Assert.False((await new TransferenciaService(db).ProcesarAsync(p, "actualizar")).PuedeImportar);
        Assert.Empty(db.Cursos); Assert.Empty(db.Anexos);
    }

    [Fact]
    public async Task EmptyUnassignedGridCanBeRemovedWithoutRemovingAnnexes()
    {
        using var db = Db(); var service = new TransferenciaService(db); var p = Horarios();
        await Aplicar(service, p with { Datos = p.Datos with { Asignaciones = [] } });
        await Aplicar(service, Paquete("horarios"), "reemplazar");
        Assert.Empty(db.HorariosAnexo); Assert.Single(db.Anexos); Assert.Single(db.Materias); Assert.Single(db.Cursos);
    }

    [Fact]
    public async Task UnreferencedSubjectsCanBeRemovedWithoutDeletingTeachersOrCourses()
    {
        using var db = Db(); var service = new TransferenciaService(db); var p = Horarios();
        await Aplicar(service, p with { Datos = p.Datos with { Asignaciones = [] } });
        await Aplicar(service, Paquete("materias"), "reemplazar");
        Assert.Empty(db.Materias); Assert.Empty(db.MateriaDocentes);
        Assert.Single(db.Docentes); Assert.Single(db.Cursos); Assert.Single(db.HorariosAnexo);
    }
}
