using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.Transferencias;
using CENS15_V2.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CENS15_V2.Services;

public sealed class TransferenciaService(AppDbContext db, SessionEpoch? sessionEpoch = null)
{
    public const int MaxBytes = 20 * 1024 * 1024;
    private const int MaxPackageBytes = MaxBytes - 4096;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private static readonly string[] Secciones = ["docentes", "materias", "horarios", "alumnos", "usuarios", "roles", "responsabilidades", "cursos"];
    private static readonly string[] Exclusiones = ["Credenciales, secretos de acceso, tokens, sesiones y permisos legacy",
        "Documentos, archivos, imagenes y certificados",
        "Inscripciones, cursadas, calificaciones e historial academico"];

    public static PaqueteTransferencia LeerPaquete(JsonElement json)
    {
        try
        {
            if (json.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("El paquete debe ser un objeto JSON de transferencia.");
            if (Encoding.UTF8.GetByteCount(json.GetRawText()) > MaxPackageBytes)
                throw new ArgumentException("El paquete supera el limite de 20 MB.");
            ValidarJson(json);
            var paquete = json.Deserialize<PaqueteTransferencia>(JsonOptions)
                ?? throw new ArgumentException("El paquete esta vacio.");
            ValidarCabecera(paquete);
            return paquete;
        }
        catch (JsonException)
        {
            throw new ArgumentException("Archivo de transferencia invalido: faltan campos obligatorios, hay campos desconocidos o tipos incorrectos. Usa un archivo exportado por este sistema (version 1).");
        }
    }

    private static void ValidarJson(JsonElement value, string? name = null)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("El JSON contiene campos duplicados.");
                ValidarJson(property.Value, property.Name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 100000) throw new ArgumentException("Una lista supera los 100000 registros permitidos.");
            foreach (var item in value.EnumerateArray()) ValidarJson(item);
        }
        else if (value.ValueKind == JsonValueKind.Null && name is not ("email" or "datosNacimiento" or "contacto" or "descripcion" or "numeroDocumento" or "fechaNacimiento" or "genero"))
            throw new ArgumentException("No se permiten listas, registros ni campos obligatorios nulos.");
        else if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 2000)
            throw new ArgumentException("Un campo de texto supera los 2000 caracteres permitidos.");
    }

    private static void ValidarCabecera(PaqueteTransferencia p)
    {
        if (p.Formato != "cens15-transferencia" || p.Version != 1 || !Secciones.Contains(p.Seccion))
            throw new ArgumentException("Formato, version o seccion de transferencia no admitidos.");
        if (p.Datos == null || p.Alcance == null || p.Exclusiones == null
            || p.Datos.Docentes == null || p.Datos.Anexos == null || p.Datos.Orientaciones == null
            || p.Datos.Cursos == null || p.Datos.Materias == null || p.Datos.Bloques == null
            || p.Datos.Asignaciones == null || p.Datos.Alumnos == null || p.Datos.Usuarios == null
            || p.Datos.Roles == null || p.Datos.Responsabilidades == null)
            throw new ArgumentException("Todas las listas del paquete son obligatorias, incluso cuando estan vacias.");
    }

    public async Task<PaqueteTransferencia> ExportarAsync(string seccion, CancellationToken ct = default, Guid? actorId = null)
    {
        if (!Secciones.Contains(seccion)) throw new ArgumentException("Seccion desconocida. Usa " + string.Join(", ", Secciones) + ".");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        var s = await CargarAsync(ct, EsSeguridad(seccion));
        if (actorId.HasValue && !await db.Users.AnyAsync(u => u.Id == actorId.Value && u.Status && u.Role.Name == "Admin", ct))
            throw new UnauthorizedAccessException("El usuario actual ya no es un administrador activo en el destino.");
        var materias = seccion is "materias" or "horarios" ? s.Materias : [];
        var cursos = seccion == "cursos" ? s.Cursos : s.Cursos.Where(c => materias.Any(m => m.CursoId == c.Id)).ToList();
        var roles = seccion == "roles" ? s.Roles : seccion == "usuarios"
            ? s.Roles.Where(r => s.Usuarios.Any(u => u.RoleId == r.Id)).ToList() : [];
        var responsabilidades = seccion == "responsabilidades" ? s.Responsabilidades
            : s.Responsabilidades.Where(x => roles.Any(r => r.Responsibilities.Any(rr => rr.ResponsibilityId == x.Id))).ToList();
        var anexos = seccion == "horarios" ? s.Anexos : s.Anexos.Where(a => cursos.Any(c => c.AnexoId == a.Id)).ToList();
        var docentes = seccion == "docentes" ? s.Docentes
            : s.Docentes.Where(d => materias.Any(m => m.Docentes.Any(md => md.DocenteId == d.Id))).ToList();
        var paquete = new PaqueteTransferencia
        {
            Formato = "cens15-transferencia", Version = 1, Seccion = seccion, ExportadoEn = DateTimeOffset.UtcNow,
            Alcance = Alcance(seccion), Exclusiones = Exclusiones,
            Datos = new DatosTransferencia
            {
                Usuarios = seccion == "usuarios" ? s.Usuarios.OrderBy(u => u.Auth.Email, StringComparer.Ordinal)
                    .Select(u => new UsuarioTransferencia(u.Auth.Email, u.FirstName, u.LastName, u.Status, u.Role.Name)).ToList() : [],
                Roles = roles.OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => new RolTransferencia(r.Name,
                    r.Responsibilities.Select(rr => rr.Responsibility.Name).OrderBy(n => n, StringComparer.Ordinal).ToList())).ToList(),
                Responsabilidades = responsabilidades.OrderBy(r => r.Name, StringComparer.Ordinal)
                    .Select(r => new ResponsabilidadTransferencia(r.Name, r.Description)).ToList(),
                Docentes = docentes.OrderBy(d => d.Id).Select(DocenteDto).ToList(),
                Anexos = anexos.OrderBy(a => a.Id).Select(a => new AnexoTransferencia(a.Nombre)).ToList(),
                Orientaciones = s.Orientaciones.Where(o => cursos.Any(c => c.OrientacionId == o.Id))
                    .OrderBy(o => o.Id).Select(o => new OrientacionTransferencia(o.Nombre, o.NombreCorto)).ToList(),
                Cursos = cursos.OrderBy(c => c.Id).Select(c => new CursoTransferencia(CursoDto(c), c.Semipresencial)).ToList(),
                Materias = materias.OrderBy(m => m.Id).Select(m => new MateriaTransferencia(MateriaDto(m), m.CargaHoraria,
                    m.Docentes.OrderBy(d => d.Rol).Select(d => new DocenciaTransferencia(DocenteDto(d.Docente), d.Rol, d.Activo)).ToList())).ToList(),
                Bloques = seccion == "horarios" ? s.Bloques.OrderBy(b => b.Id)
                    .Select(b => new BloqueTransferencia(BloqueDto(b), b.Tipo, b.HoraInicio, b.HoraFin)).ToList() : [],
                Asignaciones = seccion == "horarios" ? s.Asignaciones.OrderBy(a => a.Id)
                    .Select(a => new AsignacionTransferencia(MateriaDto(a.Materia), BloqueDto(a.HorarioAnexo))).ToList() : [],
                Alumnos = seccion == "alumnos" ? s.Alumnos.Where(a => !a.Eliminado).OrderBy(a => a.Id)
                    .Select(a => new AlumnoTransferencia(a.NumeroDocumento, a.Nombres, a.Apellidos, a.FechaNacimiento,
                        a.Genero, a.Domicilio,
                        a.DatosNacimiento == null ? null : new(a.DatosNacimiento.Localidad, a.DatosNacimiento.Provincia, a.DatosNacimiento.Pais),
                        a.Contacto == null ? null : new(a.Contacto.TelefonoAlumno, a.Contacto.Email, a.Contacto.NombreEmergencia, a.Contacto.TelefonoEmergencia))).ToList() : []
            }
        };
        try
        {
            if (JsonSerializer.SerializeToUtf8Bytes(paquete, JsonOptions).Length > MaxPackageBytes)
                throw new ArgumentException("La exportacion supera los 20 MB permitidos. Reduce los datos antes de transferir.");
        }
        catch (JsonException)
        {
            throw new ArgumentException("No se puede exportar: existen valores de genero, dia o tipo de bloque invalidos en el origen. Corrige los datos antes de transferir.");
        }
        return paquete;
    }

    public async Task<ReporteTransferencia> ProcesarAsync(PaqueteTransferencia paquete, string modo,
        bool importar = false, string? huella = null, string? confirmacion = null, CancellationToken ct = default, Guid? actorId = null)
    {
        ValidarCabecera(paquete);
        if (modo is not ("actualizar" or "reemplazar")) throw new ArgumentException("El modo debe ser actualizar o reemplazar.");
        if (importar && (string.IsNullOrWhiteSpace(huella) || (modo == "reemplazar" && confirmacion != "REEMPLAZAR")))
            throw new ArgumentException("Debes obtener una vista previa y confirmar el reemplazo escribiendo REEMPLAZAR.");
        var report = new ReporteTransferencia { Seccion = paquete.Seccion, Modo = modo };
        report.Advertencias.Add("No se transfieren credenciales, tokens ni sesiones. Se conservan los vinculos de usuarios del destino.");
        if (EsSeguridad(paquete.Seccion))
        {
            report.Advertencias.Add("Los prerrequisitos existentes no sobrescriben privilegios: importa responsabilidades y luego roles si difieren. Permisos legacy no se transfieren.");
            report.Advertencias.Add(sessionEpoch != null
                ? "Importar esta seccion invalida todas las sesiones. Debes volver a ingresar despues de confirmar."
                : "Sin SessionEpoch configurado no se revocan sesiones; configuracion solo para pruebas aisladas.");
        }
        if (paquete.Seccion == "usuarios")
            report.Advertencias.Add("Los usuarios nuevos deben solicitar acceso inicial (RequestInitialAccess) despues de importar. No se envian emails ni se exportan secretos. Los usuarios inactivos no pueden iniciar sesion.");
        if (paquete.Seccion == "alumnos")
            report.Advertencias.Add("Solo ficha, contacto y nacimiento. NO incluye inscripciones, cursadas, notas, documentos ni certificados. Los datos excluidos existentes se conservan; no es una migracion del historial.");
        if (modo == "reemplazar") report.Advertencias.Add("Reemplazo global de la seccion seleccionada: los ausentes se eliminan solo si no hay referencias protegidas. Alumnos y usuarios: baja logica, nunca borrado fisico.");
        if (paquete.Seccion is "materias" or "horarios")
            report.Advertencias.Add("Los prerrequisitos solo se crean si faltan; no se sobrescriben. Las materias sin asignaciones horarias pueden quedar pendientes.");
        if (paquete.Seccion == "materias")
            report.Advertencias.Add("Las docencias de materias coincidentes se sustituyen por las del archivo, sin eliminar docentes ni horarios.");
        IDbContextTransaction? tx = null;
        try
        {
            // Preview never saves or rolls back writes. Serializable apply binds the plan to the state read here.
            if (db.Database.IsRelational())
                tx = await db.Database.BeginTransactionAsync(importar ? IsolationLevel.Serializable : IsolationLevel.RepeatableRead, ct);
            if (importar && db.Database.IsNpgsql())
            {
                // Ordinary API writes also take table write locks. Prevent new cascade targets and
                // natural-key phantoms between validation and apply, even from read-committed writers.
                await db.Database.ExecuteSqlRawAsync("""
                    LOCK TABLE "Anexos", "Orientaciones", "Cursos", "Docentes", "Materias",
                    "MateriaDocentes", "HorariosAnexo", "HorariosMateria", "Alumnos",
                    "AlumnoContactos", "AlumnoNacimientos", "AlumnoDocumentos", "Inscripciones",
                    "CursadasMaterias", "Calificaciones" IN SHARE ROW EXCLUSIVE MODE
                    """, ct);
                if (EsSeguridad(paquete.Seccion))
                    await db.Database.ExecuteSqlRawAsync("""
                        LOCK TABLE "Users", "Auths", "Roles", "Responsibilities", "RoleResponsibility", "RolePermission"
                        IN SHARE ROW EXCLUSIVE MODE
                        """, ct);
            }
            var state = await CargarAsync(ct, EsSeguridad(paquete.Seccion));
            if (actorId.HasValue)
            {
                var actor = await db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == actorId.Value, ct);
                if (actor == null || !actor.Status || actor.Role.Name != "Admin")
                    report.Errores.Add("El usuario actual ya no es un administrador activo en el destino.");
            }
            report.Huella = CalcularHuella(paquete, modo);
            var actions = new List<Action>();
            var removals = new List<Action>();
            try { Preparar(paquete, modo, state, report, actions, removals, actorId); }
            catch (InvalidOperationException ex) { report.Errores.Add(ex.Message); }
            if (!report.PuedeImportar || !importar) return report;
            if (!string.Equals(huella, report.Huella, StringComparison.Ordinal))
            {
                report.Errores.Add("El paquete, el modo o el destino cambiaron desde la vista previa. Genera una nueva vista previa y confirma nuevamente.");
                return report;
            }
            // The middleware holds the writer lease before authentication. Fail closed before any DB write.
            if (EsSeguridad(paquete.Seccion) && sessionEpoch != null)
            {
                try { sessionEpoch.Rotate(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    report.Errores.Add("No se importo ningun cambio: no se pudo invalidar las sesiones de forma segura.");
                    return report;
                }
            }
            // Free unique roles/occupancies before their replacements. Both saves share one transaction.
            foreach (var removal in removals) removal();
            if (removals.Count > 0) await db.SaveChangesAsync(ct);
            foreach (var action in actions) action();
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            report.Importado = true;
            report.RequiereReingreso = EsSeguridad(paquete.Seccion) && sessionEpoch != null;
            return report;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is PostgresException { SqlState: "40001" or "40P01" })
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            report.Errores.Add("No se importo ningun cambio: conflicto de concurrencia o integridad. Actualiza la vista previa y revisa las referencias y la ocupacion de horarios.");
            return report;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
            db.ChangeTracker.Clear();
        }
    }

    private void Preparar(PaqueteTransferencia p, string modo, Estado s, ReporteTransferencia r,
        List<Action> actions, List<Action> removals, Guid? actorId)
    {
        var d = p.Datos;
        var reemplazar = modo == "reemplazar";
        Exigir(d.Usuarios.Count == 0 || p.Seccion == "usuarios", "Solo usuarios puede transferir perfiles de usuarios.");
        Exigir(d.Roles.Count == 0 || p.Seccion is "usuarios" or "roles", "La lista roles no pertenece a esta seccion.");
        Exigir(d.Responsabilidades.Count == 0 || EsSeguridad(p.Seccion), "La lista responsabilidades no pertenece a esta seccion.");
        if (EsSeguridad(p.Seccion))
        {
            Exigir(d.Docentes.Count + d.Anexos.Count + d.Orientaciones.Count + d.Cursos.Count + d.Materias.Count
                + d.Bloques.Count + d.Asignaciones.Count + d.Alumnos.Count == 0, "La seccion de seguridad contiene datos ajenos.");
            PrepararSeguridad(p, reemplazar, s, r, actions, actorId);
            return;
        }
        Exigir(d.Alumnos.Count == 0 || p.Seccion == "alumnos", "La lista alumnos no pertenece a esta seccion.");
        Exigir((d.Bloques.Count == 0 && d.Asignaciones.Count == 0) || p.Seccion == "horarios", "Solo horarios puede transferir bloques y asignaciones.");
        Exigir(p.Seccion is "materias" or "horarios" or "cursos" || (d.Anexos.Count == 0 && d.Orientaciones.Count == 0 && d.Cursos.Count == 0 && d.Materias.Count == 0), "El paquete contiene prerrequisitos ajenos a la seccion.");
        Exigir(p.Seccion != "cursos" || d.Docentes.Count + d.Materias.Count == 0, "El paquete cursos no puede modificar docentes ni materias.");
        Exigir(p.Seccion != "alumnos" || d.Docentes.Count == 0, "El paquete alumnos no puede modificar docentes.");
        if (p.Seccion is "materias" or "horarios")
        {
            var usedDocentes = d.Materias.SelectMany(m => m.Docentes).Select(md => DocenteKey(md.Docente)).ToHashSet();
            var usedCursos = d.Materias.Select(m => CursoKey(m.Clave.Curso)).ToHashSet();
            var usedOrientaciones = d.Cursos.Select(c => N(c.Clave.Orientacion)).ToHashSet();
            var usedAnexos = d.Cursos.Select(c => N(c.Clave.Anexo)).ToHashSet();
            Exigir(d.Docentes.All(doc => usedDocentes.Contains(DocenteKey(doc))),
                "El paquete incluye docentes prerrequisito no usados por ninguna materia.");
            Exigir(d.Cursos.All(c => usedCursos.Contains(CursoKey(c.Clave))),
                "El paquete incluye cursos prerrequisito no usados por ninguna materia.");
            Exigir(d.Orientaciones.All(o => usedOrientaciones.Contains(N(o.Nombre))),
                "El paquete incluye orientaciones prerrequisito no usadas por ningun curso.");
            Exigir(p.Seccion == "horarios" || d.Anexos.All(a => usedAnexos.Contains(N(a.Nombre))),
                "El paquete incluye anexos prerrequisito no usados por ningun curso.");
        }

        if (p.Seccion == "alumnos")
        {
            PrepararAlumnos(d.Alumnos, reemplazar, s, r, actions);
            return;
        }

        var docentes = Unicos(s.Docentes, x => DocenteKey(DocenteDto(x)), "docentes del destino");
        var incomingDocentes = Unicos(d.Docentes, DocenteKey, "docentes del archivo");
        var destinationNames = s.Docentes.GroupBy(x => NombreDocente(DocenteDto(x))).ToDictionary(x => x.Key, x => x.Count());
        var incomingNames = d.Docentes.GroupBy(NombreDocente).ToDictionary(x => x.Key, x => x.Count());
        var namesWithoutEmail = s.Docentes.Where(x => string.IsNullOrWhiteSpace(x.Email)).Select(x => NombreDocente(DocenteDto(x))).ToHashSet();
        foreach (var (key, item) in incomingDocentes)
        {
            ValidarDocente(item);
            var exists = docentes.TryGetValue(key, out var entity);
            // An email-less identity must never accidentally merge with an email-bearing namesake.
            if (string.IsNullOrWhiteSpace(item.Email))
            {
                Exigir(destinationNames.GetValueOrDefault(NombreDocente(item)) <= (exists ? 1 : 0)
                    && incomingNames[NombreDocente(item)] == 1,
                    $"Docente ambiguo por nombre: {item.Apellidos}, {item.Nombres}.");
            }
            else if (!exists)
                Exigir(!namesWithoutEmail.Contains(NombreDocente(item)),
                    $"Docente ambiguo: {item.Apellidos}, {item.Nombres} existe sin email en el destino. Completa su email antes de transferir.");
            if (!exists)
            {
                entity = new Docente { Nombres = item.Nombres.Trim(), Apellidos = item.Apellidos.Trim(), Email = Email(item.Email) };
                docentes.Add(key, entity);
                var created = entity;
                actions.Add(() => db.Docentes.Add(created));
                Contar(r, "docentes", crear: 1);
            }
            else if (p.Seccion == "docentes")
            {
                var current = entity!;
                actions.Add(() => { current.Nombres = item.Nombres.Trim(); current.Apellidos = item.Apellidos.Trim(); current.Email = Email(item.Email); });
                Contar(r, "docentes", actualizar: 1);
            }
            else
                Exigir(NombreDocente(DocenteDto(entity!)) == NombreDocente(item), $"El docente prerrequisito {item.Email ?? item.Apellidos} difiere del destino. Transfiere docentes primero.");
        }
        if (p.Seccion == "docentes")
        {
            if (reemplazar)
                foreach (var (key, entity) in docentes.Where(x => x.Value.Id != 0 && !incomingDocentes.ContainsKey(x.Key)))
                {
                    Exigir(entity.UserId == null && !s.Docencias.Any(x => x.DocenteId == entity.Id), $"No se puede eliminar al docente {entity.Apellidos}, {entity.Nombres}: tiene usuario vinculado o materias asignadas.");
                    actions.Add(() => db.Docentes.Remove(entity));
                    Contar(r, "docentes", eliminar: 1);
                }
            return;
        }

        var anexos = Unicos(s.Anexos, x => N(x.Nombre), "anexos del destino");
        var incomingAnexos = Unicos(d.Anexos, x => N(x.Nombre), "anexos del archivo");
        foreach (var (key, item) in incomingAnexos)
        {
            Texto(item.Nombre, "nombre del anexo");
            if (anexos.ContainsKey(key)) continue;
            var entity = new Anexo { Nombre = item.Nombre.Trim() };
            anexos.Add(key, entity); actions.Add(() => db.Anexos.Add(entity)); Contar(r, "anexos (prerrequisito)", crear: 1);
        }
        var orientaciones = Unicos(s.Orientaciones, x => N(x.Nombre), "orientaciones del destino");
        var incomingOrientaciones = Unicos(d.Orientaciones, x => N(x.Nombre), "orientaciones del archivo");
        Unicos(d.Orientaciones, x => N(x.NombreCorto), "nombres cortos de orientacion del archivo");
        foreach (var (key, item) in incomingOrientaciones)
        {
            Texto(item.Nombre, "nombre de orientacion"); Texto(item.NombreCorto, "nombre corto de orientacion");
            if (orientaciones.TryGetValue(key, out var current))
            {
                Exigir(N(current.NombreCorto) == N(item.NombreCorto), $"La orientacion {item.Nombre} tiene otro nombre corto en el destino.");
                continue;
            }
            Exigir(!orientaciones.Values.Any(o => N(o.NombreCorto) == N(item.NombreCorto)), $"Nombre corto de orientacion en conflicto: {item.NombreCorto}.");
            var entity = new Orientacion { Nombre = item.Nombre.Trim(), NombreCorto = item.NombreCorto.Trim() };
            orientaciones.Add(key, entity); actions.Add(() => db.Orientaciones.Add(entity)); Contar(r, "orientaciones (prerrequisito)", crear: 1);
        }
        var cursos = Unicos(s.Cursos, x => CursoKey(CursoDto(x)), "cursos del destino");
        var incomingCursos = Unicos(d.Cursos, x => CursoKey(x.Clave), "cursos del archivo");
        Unicos(s.Cursos, x => Key(N(x.CursoNombre), N(x.Division)), "anio/division global de cursos del destino");
        Unicos(d.Cursos, x => Key(N(x.Clave.CursoNombre), N(x.Clave.Division)), "anio/division global de cursos del archivo");
        foreach (var (key, item) in incomingCursos)
        {
            ValidarCurso(item.Clave);
            Exigir(incomingAnexos.ContainsKey(N(item.Clave.Anexo)) && incomingOrientaciones.ContainsKey(N(item.Clave.Orientacion)), "Falta el contexto de anexo u orientacion de un curso.");
            if (cursos.TryGetValue(key, out var current))
            {
                if (p.Seccion == "cursos")
                {
                    actions.Add(() => current.Semipresencial = item.Semipresencial);
                    Contar(r, "cursos", actualizar: 1);
                }
                else Exigir(current.Semipresencial == item.Semipresencial, $"El curso {item.Clave.CursoNombre} {item.Clave.Division} difiere en modalidad del destino. Transfiere cursos primero.");
                continue;
            }
            Exigir(!cursos.Values.Any(c => N(c.CursoNombre) == N(item.Clave.CursoNombre) && N(c.Division) == N(item.Clave.Division)),
                $"El curso {item.Clave.CursoNombre} {item.Clave.Division} ya existe con otro anexo u orientacion. La clave anio/division es global; corrige el conflicto antes de importar.");
            var entity = new Curso { CursoNombre = item.Clave.CursoNombre.Trim(), Division = item.Clave.Division.Trim(),
                Anexo = anexos[N(item.Clave.Anexo)], Orientacion = orientaciones[N(item.Clave.Orientacion)], Semipresencial = item.Semipresencial };
            cursos.Add(key, entity); actions.Add(() => db.Cursos.Add(entity)); Contar(r, p.Seccion == "cursos" ? "cursos" : "cursos (prerrequisito)", crear: 1);
        }
        if (p.Seccion == "cursos")
        {
            Exigir(d.Anexos.All(a => d.Cursos.Any(c => N(c.Clave.Anexo) == N(a.Nombre)))
                && d.Orientaciones.All(o => d.Cursos.Any(c => N(c.Clave.Orientacion) == N(o.Nombre))), "El contexto de cursos contiene anexos u orientaciones no usados.");
            if (reemplazar)
                foreach (var entity in s.Cursos.Where(c => !incomingCursos.ContainsKey(CursoKey(CursoDto(c)))))
                {
                    Exigir(!s.Materias.Any(m => m.CursoId == entity.Id) && !s.Asignaciones.Any(h => h.CursoId == entity.Id)
                        && !db.ChangeTracker.Entries<Inscripcion>().Any(i => i.Entity.CursoId == entity.Id),
                        $"No se puede eliminar el curso {entity.CursoNombre} {entity.Division}: tiene materias, horarios o inscripciones/historial.");
                    actions.Add(() => db.Cursos.Remove(entity)); Contar(r, "cursos", eliminar: 1);
                }
            return;
        }
        var materias = Unicos(s.Materias, x => MateriaKey(MateriaDto(x)), "materias del destino");
        var incomingMaterias = Unicos(d.Materias, x => MateriaKey(x.Clave), "materias del archivo");
        foreach (var (key, item) in incomingMaterias)
        {
            Texto(item.Clave.Nombre, "nombre de materia");
            Exigir(incomingCursos.ContainsKey(CursoKey(item.Clave.Curso)), "Falta el contexto del curso de una materia.");
            Exigir(item.CargaHoraria >= 0 && item.CargaHoraria <= 200, $"Carga horaria invalida en {item.Clave.Nombre}.");
            ValidarDocencias(item.Docentes, incomingDocentes);
            var exists = materias.TryGetValue(key, out var entity);
            if (!exists)
            {
                entity = new Materia { Nombre = item.Clave.Nombre.Trim(), CargaHoraria = item.CargaHoraria, Curso = cursos[CursoKey(item.Clave.Curso)] };
                materias.Add(key, entity); var created = entity;
                actions.Add(() => db.Materias.Add(created));
                Contar(r, p.Seccion == "horarios" ? "materias (prerrequisito)" : "materias", crear: 1);
            }
            else if (p.Seccion == "horarios")
            {
                var current = entity!;
                Exigir(current.CargaHoraria == item.CargaHoraria && FirmaDocencias(current.Docentes.Select(x => new DocenciaTransferencia(DocenteDto(x.Docente), x.Rol, x.Activo))) == FirmaDocencias(item.Docentes),
                    $"La materia prerrequisito {item.Clave.Nombre} difiere en carga o docentes del destino. Transfiere materias primero.");
                continue;
            }
            else
            {
                var current = entity!;
                Exigir(current.Horarios.Count == 0 || current.Horarios.Count == item.CargaHoraria,
                    $"La carga de {item.Clave.Nombre} no coincide con sus horarios existentes. No se borraran horarios al transferir materias.");
                actions.Add(() => { current.Nombre = item.Clave.Nombre.Trim(); current.CargaHoraria = item.CargaHoraria; });
                Contar(r, "materias", actualizar: 1);
            }
            var target = entity!;
            PrepararDocencias(target, item.Docentes, docentes, actions, removals);
        }
        if (p.Seccion == "materias")
        {
            if (reemplazar)
                foreach (var entity in s.Materias.Where(m => !incomingMaterias.ContainsKey(MateriaKey(MateriaDto(m)))))
                {
                    Exigir(!s.Cursadas.Any(c => c.MateriaId == entity.Id) && !s.Asignaciones.Any(a => a.MateriaId == entity.Id),
                        $"No se puede eliminar la materia {entity.Nombre}: tiene cursadas/historial o asignaciones horarias.");
                    actions.Add(() => { db.MateriaDocentes.RemoveRange(entity.Docentes); db.Materias.Remove(entity); });
                    Contar(r, "materias", eliminar: 1);
                }
            return;
        }
        PrepararHorarios(d, reemplazar, s, r, actions, removals, anexos, materias, incomingMaterias);
    }

    private void PrepararSeguridad(PaqueteTransferencia p, bool reemplazar, Estado s,
        ReporteTransferencia r, List<Action> actions, Guid? actorId)
    {
        var d = p.Datos;
        var responsabilidades = Unicos(s.Responsabilidades, x => N(x.Name), "responsabilidades del destino");
        var incomingResponsabilidades = Unicos(d.Responsabilidades, x => N(x.Nombre), "responsabilidades del archivo");
        var incomingRoles = Unicos(d.Roles, x => N(x.Nombre), "roles del archivo");
        var incomingUsuarios = Unicos(d.Usuarios, x => N(x.Email), "emails del archivo");
        Exigir(p.Seccion == "responsabilidades" || d.Responsabilidades.All(x => d.Roles.Any(role => role.Responsabilidades.Any(n => N(n) == N(x.Nombre)))),
            "Hay responsabilidades de contexto no usadas por los roles del archivo.");
        Exigir(p.Seccion != "usuarios" || d.Roles.All(role => d.Usuarios.Any(u => N(u.Rol) == N(role.Nombre))),
            "Hay roles de contexto no usados por los usuarios del archivo.");
        foreach (var (key, item) in incomingResponsabilidades)
        {
            Texto(item.Nombre, "nombre de responsabilidad");
            Exigir(item.Descripcion == null || item.Descripcion.Length <= 2000, "La descripcion supera los 2000 caracteres.");
            if (responsabilidades.TryGetValue(key, out var current))
            {
                if (p.Seccion == "responsabilidades")
                {
                    actions.Add(() => current.Description = item.Descripcion);
                    Contar(r, "responsabilidades", actualizar: 1);
                }
                else Exigir(current.Description == item.Descripcion,
                    $"La responsabilidad {item.Nombre} difiere del destino. Importa responsabilidades primero.");
            }
            else
            {
                var entity = new Responsibility { Id = Guid.NewGuid(), Name = item.Nombre.Trim(), Description = item.Descripcion };
                responsabilidades.Add(key, entity);
                actions.Add(() => db.Responsibilities.Add(entity));
                Contar(r, p.Seccion == "responsabilidades" ? "responsabilidades" : "responsabilidades (prerrequisito)", crear: 1);
            }
        }
        if (p.Seccion == "responsabilidades")
        {
            if (reemplazar)
                foreach (var entity in s.Responsabilidades.Where(x => !incomingResponsabilidades.ContainsKey(N(x.Name))))
                {
                    Exigir(!s.Roles.Any(role => role.Responsibilities.Any(rr => rr.ResponsibilityId == entity.Id)),
                        $"No se puede eliminar la responsabilidad {entity.Name}: esta referenciada por roles.");
                    actions.Add(() => db.Responsibilities.Remove(entity)); Contar(r, "responsabilidades", eliminar: 1);
                }
            return;
        }
        var roles = Unicos(s.Roles, x => N(x.Name), "roles del destino");
        foreach (var (key, item) in incomingRoles)
        {
            Texto(item.Nombre, "nombre del rol");
            Exigir(N(item.Nombre) != "ADMIN" || item.Nombre == "Admin", "El nombre reservado del rol administrador debe ser exactamente Admin.");
            Exigir(item.Responsabilidades != null, "Cada rol debe incluir la lista responsabilidades.");
            var names = Unicos(item.Responsabilidades!, N, "responsabilidades de un rol");
            foreach (var name in names.Values)
            {
                Texto(name, "nombre de responsabilidad del rol");
                Exigir(incomingResponsabilidades.ContainsKey(N(name)), "Falta el contexto de una responsabilidad del rol.");
            }
            var exists = roles.TryGetValue(key, out var entity);
            if (!exists)
            {
                entity = new Role { Id = Guid.NewGuid(), Name = item.Nombre.Trim() };
                roles.Add(key, entity);
                var created = entity; actions.Add(() => db.Roles.Add(created));
                Contar(r, p.Seccion == "roles" ? "roles" : "roles (prerrequisito)", crear: 1);
            }
            else if (p.Seccion == "usuarios")
            {
                Exigir(entity!.Responsibilities.Select(rr => N(rr.Responsibility.Name)).ToHashSet().SetEquals(names.Keys),
                    $"El rol {item.Nombre} difiere del destino. Importa roles primero.");
                continue;
            }
            else
            {
                // Admin recovery endpoints are role-based. Preserve its name and every existing grant.
                Exigir(N(entity!.Name) != "ADMIN" || entity.Responsibilities.All(rr => names.ContainsKey(N(rr.Responsibility.Name))),
                    "No se pueden quitar responsabilidades del rol Admin: protege administracion, restauracion y recuperacion.");
                Contar(r, "roles", actualizar: 1);
            }
            var target = entity!;
            foreach (var old in target.Responsibilities.Where(rr => !names.ContainsKey(N(rr.Responsibility.Name))).ToList())
                actions.Add(() => { db.Set<RoleResponsibility>().Remove(old); target.Responsibilities.Remove(old); });
            foreach (var name in names.Keys.Where(n => !target.Responsibilities.Any(rr => N(rr.Responsibility.Name) == n)))
            {
                var responsibility = responsabilidades[name];
                actions.Add(() => target.Responsibilities.Add(new RoleResponsibility
                {
                    Role = target, RoleId = target.Id, Responsibility = responsibility, ResponsibilityId = responsibility.Id
                }));
            }
        }
        if (p.Seccion == "roles")
        {
            if (reemplazar)
                foreach (var entity in s.Roles.Where(x => !incomingRoles.ContainsKey(N(x.Name))))
                {
                    Exigir(N(entity.Name) != "ADMIN", "No se puede eliminar ni renombrar el rol Admin.");
                    Exigir(!s.Usuarios.Any(u => u.RoleId == entity.Id), $"No se puede eliminar el rol {entity.Name}: tiene usuarios vinculados.");
                    actions.Add(() =>
                    {
                        db.Set<RoleResponsibility>().RemoveRange(entity.Responsibilities);
                        db.Set<RolePermission>().RemoveRange(entity.Permissions);
                        db.Roles.Remove(entity);
                    });
                    Contar(r, "roles", eliminar: 1);
                }
            return;
        }
        Exigir(s.Usuarios.All(u => u.Auth != null && u.Role != null), "Hay usuarios sin Auth o rol en el destino. Corrige su integridad antes de importar.");
        var usuarios = Unicos(s.Usuarios, u => N(u.Auth.Email), "emails del destino");
        foreach (var (key, item) in incomingUsuarios)
        {
            Texto(item.Email, "email de usuario"); Texto(item.Nombres, "nombres de usuario");
            Texto(item.Apellidos, "apellidos de usuario"); Texto(item.Rol, "rol de usuario");
            Exigir(System.Net.Mail.MailAddress.TryCreate(item.Email.Trim(), out var email) && email.Address == item.Email.Trim(), "Email de usuario invalido.");
            Exigir(incomingRoles.ContainsKey(N(item.Rol)), "Falta el contexto del rol del usuario.");
            var role = roles[N(item.Rol)];
            var exists = usuarios.TryGetValue(key, out var entity);
            Exigir(!exists || entity!.Id != actorId || (item.Activo && entity.RoleId == role.Id),
                "El usuario actual no puede darse de baja ni cambiar su propio rol.");
            if (!exists)
            {
                entity = new User { Id = Guid.NewGuid(), Role = role, RoleId = role.Id };
                var created = entity;
                actions.Add(() =>
                {
                    created.Auth = new Auth
                    {
                        Id = created.Id, User = created, Email = Email(item.Email)!, MustChangePassword = true,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)))
                    };
                    db.Users.Add(created);
                });
            }
            var target = entity!;
            actions.Add(() =>
            {
                target.FirstName = item.Nombres.Trim(); target.LastName = item.Apellidos.Trim();
                target.Status = item.Activo; target.Role = role; target.RoleId = role.Id;
            });
            Contar(r, "usuarios", crear: exists ? 0 : 1, actualizar: exists ? 1 : 0);
        }
        if (reemplazar)
            foreach (var entity in s.Usuarios.Where(u => u.Status && !incomingUsuarios.ContainsKey(N(u.Auth.Email))))
            {
                Exigir(entity.Id != actorId, "El usuario actual no puede darse de baja por ausencia en el archivo.");
                actions.Add(() => entity.Status = false);
                Contar(r, "usuarios (baja logica)", eliminar: 1);
            }
        var adminsBefore = s.Usuarios.Count(u => u.Status && N(u.Role.Name) == "ADMIN");
        var adminsAfter = s.Usuarios.Count(u => incomingUsuarios.TryGetValue(N(u.Auth.Email), out var next)
            ? next.Activo && N(next.Rol) == "ADMIN" : !reemplazar && u.Status && N(u.Role.Name) == "ADMIN")
            + incomingUsuarios.Count(x => !usuarios.ContainsKey(x.Key) && x.Value.Activo && N(x.Value.Rol) == "ADMIN");
        Exigir(adminsBefore == 0 || adminsAfter > 0, "No se puede dejar el destino sin su ultimo Admin activo.");
    }

    private void PrepararDocencias(Materia target, List<DocenciaTransferencia> source,
        Dictionary<string, Docente> docentes, List<Action> actions, List<Action> removals)
    {
        var retained = new HashSet<string>();
        foreach (var old in target.Docentes.ToList())
        {
            var next = source.SingleOrDefault(x => DocenteKey(x.Docente) == DocenteKey(DocenteDto(old.Docente)));
            if (next == null || old.Rol != Rol(next.Rol))
                removals.Add(() => { db.MateriaDocentes.Remove(old); target.Docentes.Remove(old); });
            else
            {
                retained.Add(DocenteKey(next.Docente));
                actions.Add(() => old.Activo = next.Activo);
            }
        }
        foreach (var next in source.Where(x => !retained.Contains(DocenteKey(x.Docente))))
        {
            var docente = docentes[DocenteKey(next.Docente)];
            actions.Add(() => target.Docentes.Add(new MateriaDocente { Materia = target, Docente = docente, Rol = Rol(next.Rol), Activo = next.Activo }));
        }
    }

    private void PrepararAlumnos(List<AlumnoTransferencia> source, bool reemplazar, Estado s,
        ReporteTransferencia r, List<Action> actions)
    {
        var current = Unicos(s.Alumnos, a => AlumnoKey(a.NumeroDocumento, a.Nombres, a.Apellidos, a.FechaNacimiento), "alumnos del destino (incluidas bajas; sin DNI: nombre, apellido y nacimiento)");
        var incoming = Unicos(source, a => AlumnoKey(a.NumeroDocumento, a.Nombres, a.Apellidos, a.FechaNacimiento), "alumnos del archivo (sin DNI: nombre, apellido y nacimiento)");
        var destinationNames = s.Alumnos.GroupBy(a => NombreAlumno(a.Nombres, a.Apellidos, a.FechaNacimiento)).ToDictionary(g => g.Key, g => g.Count());
        var incomingNames = source.GroupBy(a => NombreAlumno(a.Nombres, a.Apellidos, a.FechaNacimiento)).ToDictionary(g => g.Key, g => g.Count());
        var namesWithoutDocument = s.Alumnos.Where(a => string.IsNullOrWhiteSpace(a.NumeroDocumento))
            .Select(a => NombreAlumno(a.Nombres, a.Apellidos, a.FechaNacimiento)).ToHashSet();
        foreach (var (key, item) in incoming)
        {
            if (!string.IsNullOrWhiteSpace(item.NumeroDocumento)) Texto(item.NumeroDocumento, "numero de documento");
            Texto(item.Nombres, "nombres del alumno"); Texto(item.Apellidos, "apellidos del alumno");
            Exigir((!item.FechaNacimiento.HasValue || (item.FechaNacimiento.Value != DateOnly.MinValue && item.FechaNacimiento.Value <= DateOnly.FromDateTime(DateTime.UtcNow)))
                && (!item.Genero.HasValue || Enum.IsDefined(item.Genero.Value)), $"Fecha de nacimiento o genero invalido para {item.Apellidos}, {item.Nombres}.");
            Exigir(item.Contacto == null || item.Contacto.Email != null, "El email de contacto del alumno no puede ser nulo (puede estar vacio).");
            var exists = current.TryGetValue(key, out var entity);
            var name = NombreAlumno(item.Nombres, item.Apellidos, item.FechaNacimiento);
            if (string.IsNullOrWhiteSpace(item.NumeroDocumento))
                Exigir(destinationNames.GetValueOrDefault(name) <= (exists ? 1 : 0) && incomingNames[name] == 1,
                    $"Alumno ambiguo sin DNI: {item.Apellidos}, {item.Nombres}, nacimiento {item.FechaNacimiento?.ToString("yyyy-MM-dd") ?? "sin fecha"}. Completa el DNI antes de transferir.");
            else if (!exists)
                Exigir(!namesWithoutDocument.Contains(name),
                    $"Alumno ambiguo: {item.Apellidos}, {item.Nombres} existe sin DNI en el destino. Completa su DNI antes de transferir.");
            Exigir(!exists || !entity!.Eliminado, $"El alumno {item.NumeroDocumento} esta dado de baja en el destino. Debe restaurarse explicitamente antes de importar.");
            if (!exists)
            {
                entity = new Alumno();
                var created = entity; actions.Add(() => db.Alumnos.Add(created));
            }
            var target = entity!;
            actions.Add(() =>
            {
                target.Nombres = item.Nombres.Trim(); target.Apellidos = item.Apellidos.Trim();
                target.NumeroDocumento = string.IsNullOrWhiteSpace(item.NumeroDocumento) ? null : item.NumeroDocumento.Trim();
                target.FechaNacimiento = item.FechaNacimiento; target.Genero = item.Genero; target.Domicilio = item.Domicilio;
                // Null optional details preserve destination data instead of silently deleting it.
                if (item.DatosNacimiento != null)
                {
                    target.DatosNacimiento ??= new AlumnoNacimiento { Alumno = target };
                    target.DatosNacimiento.Localidad = item.DatosNacimiento.Localidad;
                    target.DatosNacimiento.Provincia = item.DatosNacimiento.Provincia;
                    target.DatosNacimiento.Pais = item.DatosNacimiento.Pais;
                }
                if (item.Contacto != null)
                {
                    target.Contacto ??= new AlumnoContacto { Alumno = target };
                    target.Contacto.TelefonoAlumno = item.Contacto.TelefonoAlumno; target.Contacto.Email = item.Contacto.Email!;
                    target.Contacto.NombreEmergencia = item.Contacto.NombreEmergencia; target.Contacto.TelefonoEmergencia = item.Contacto.TelefonoEmergencia;
                }
            });
            Contar(r, "alumnos", crear: exists ? 0 : 1, actualizar: exists ? 1 : 0);
        }
        if (reemplazar)
            foreach (var entity in s.Alumnos.Where(a => !a.Eliminado && !incoming.ContainsKey(AlumnoKey(a.NumeroDocumento, a.Nombres, a.Apellidos, a.FechaNacimiento))))
            {
                Exigir(entity.Inscripciones.Count == 0 && entity.Documentos.Count == 0,
                    $"No se puede dar de baja al alumno {entity.NumeroDocumento}: tiene inscripciones/historial o documentos que quedarian ocultos.");
                actions.Add(() => { entity.Eliminado = true; entity.FechaEliminacion = DateTime.UtcNow; });
                Contar(r, "alumnos (baja logica)", eliminar: 1);
            }
        r.Advertencias.Add("Contacto o nacimiento nulos conservan los datos existentes del destino.");
        r.Advertencias.Add("Sin DNI se identifica por nombre, apellido y fecha de nacimiento (puede estar vacia). Coincidencias ambiguas se bloquean; completa el DNI para resolverlas.");
    }

    private static string NombreAlumno(string nombres, string apellidos, DateOnly? nacimiento) => Key(N(nombres), N(apellidos), nacimiento?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "");
    private static string AlumnoKey(string? documento, string nombres, string apellidos, DateOnly? nacimiento) =>
        string.IsNullOrWhiteSpace(documento) ? Key("SIN-DNI", NombreAlumno(nombres, apellidos, nacimiento)) : Key("DNI", N(documento));

    private void PrepararHorarios(DatosTransferencia d, bool reemplazar, Estado s, ReporteTransferencia r,
        List<Action> actions, List<Action> removals, Dictionary<string, Anexo> anexos, Dictionary<string, Materia> materias,
        Dictionary<string, MateriaTransferencia> incomingMaterias)
    {
        var bloques = Unicos(s.Bloques, b => BloqueKey(BloqueDto(b)), "bloques del destino");
        var incoming = Unicos(d.Bloques, b => BloqueKey(b.Clave), "bloques del archivo");
        foreach (var (key, item) in incoming)
        {
            Exigir(d.Anexos.Any(a => N(a.Nombre) == N(item.Clave.Anexo)), "Falta el contexto del anexo de un bloque.");
            ValidarBloque(item);
            if (bloques.TryGetValue(key, out var entity))
            {
                Exigir(!s.Asignaciones.Any(a => a.HorarioAnexoId == entity.Id) || MismoBloque(entity, item),
                    $"No se puede modificar el bloque asignado de {item.Clave.Anexo}, {item.Clave.DiaSemana}, orden {item.Clave.Orden}. Quita primero sus asignaciones.");
                actions.Add(() => { entity.Tipo = item.Tipo; entity.HoraInicio = item.HoraInicio; entity.HoraFin = item.HoraFin; });
                Contar(r, "bloques horarios", actualizar: 1);
            }
            else
            {
                entity = new HorarioAnexo { Anexo = anexos[N(item.Clave.Anexo)], DiaSemana = item.Clave.DiaSemana,
                    Orden = item.Clave.Orden, Tipo = item.Tipo, HoraInicio = item.HoraInicio, HoraFin = item.HoraFin };
                bloques.Add(key, entity); var created = entity;
                actions.Add(() => db.HorariosAnexo.Add(created)); Contar(r, "bloques horarios", crear: 1);
            }
        }
        if (reemplazar)
            foreach (var entity in s.Bloques.Where(b => !incoming.ContainsKey(BloqueKey(BloqueDto(b)))))
            {
                Exigir(!s.Asignaciones.Any(a => a.HorarioAnexoId == entity.Id),
                    $"No se puede eliminar el bloque asignado de {entity.Anexo.Nombre}, {entity.DiaSemana}, orden {entity.Orden}. Quita primero sus asignaciones.");
                actions.Add(() => db.HorariosAnexo.Remove(entity)); Contar(r, "bloques horarios", eliminar: 1);
            }
        var finalBlocks = incoming.Values.Concat(reemplazar ? [] : s.Bloques
            .Where(b => !incoming.ContainsKey(BloqueKey(BloqueDto(b))))
            .Select(b => new BloqueTransferencia(BloqueDto(b), b.Tipo, b.HoraInicio, b.HoraFin))).ToList();
        foreach (var group in finalBlocks.GroupBy(b => Key(N(b.Clave.Anexo), b.Clave.DiaSemana)))
        {
            TimeOnly? end = null;
            foreach (var item in group.OrderBy(b => b.Clave.Orden))
            {
                ValidarBloque(item);
                Exigir(end == null || item.HoraInicio >= end, $"Los bloques de {item.Clave.Anexo}, {item.Clave.DiaSemana} se superponen o tienen un orden temporal invalido.");
                end = item.HoraFin;
            }
        }
        var assignments = Unicos(d.Asignaciones, a => Key(MateriaKey(a.Materia), BloqueKey(a.Bloque)), "asignaciones del archivo");
        foreach (var item in assignments.Values)
        {
            var mk = MateriaKey(item.Materia); var bk = BloqueKey(item.Bloque);
            Exigir(incomingMaterias.ContainsKey(mk) && incoming.ContainsKey(bk), "Falta la materia o el bloque de una asignacion en el archivo.");
            Exigir(incoming[bk].Tipo == TipoBloqueHorario.Clase && N(item.Materia.Curso.Anexo) == N(item.Bloque.Anexo), "Solo se pueden asignar bloques de clase del anexo del curso.");
        }
        // Update mode merges assignments; replace mode explicitly owns all assignments, not their subjects.
        var finalAssignments = d.Asignaciones.Concat(reemplazar ? [] : s.Asignaciones
            .Select(a => new AsignacionTransferencia(MateriaDto(a.Materia), BloqueDto(a.HorarioAnexo)))
            .Where(a => !assignments.ContainsKey(Key(MateriaKey(a.Materia), BloqueKey(a.Bloque))))).ToList();
        Unicos(finalAssignments, a => Key(CursoKey(a.Materia.Curso), BloqueKey(a.Bloque)), "ocupaciones del curso (dos materias usan el mismo bloque)");
        foreach (var group in finalAssignments.GroupBy(a => MateriaKey(a.Materia)))
        {
            var carga = incomingMaterias.TryGetValue(group.Key, out var item) ? item.CargaHoraria : materias[group.Key].CargaHoraria;
            Exigir(group.Count() == carga, $"La cantidad de horarios de {group.First().Materia.Nombre} debe ser igual a su carga horaria ({carga}). Actualizar agrega sin quitar; usa reemplazar para cambiar asignaciones.");
        }
        foreach (var entity in s.Asignaciones)
        {
            var key = Key(MateriaKey(MateriaDto(entity.Materia)), BloqueKey(BloqueDto(entity.HorarioAnexo)));
            if (reemplazar && !assignments.ContainsKey(key))
            {
                removals.Add(() => db.HorariosMateria.Remove(entity)); Contar(r, "asignaciones horarias", eliminar: 1);
            }
            else if (assignments.ContainsKey(key)) Contar(r, "asignaciones horarias", actualizar: 1);
        }
        var existing = s.Asignaciones.Select(a => Key(MateriaKey(MateriaDto(a.Materia)), BloqueKey(BloqueDto(a.HorarioAnexo)))).ToHashSet();
        foreach (var (key, item) in assignments.Where(a => !existing.Contains(a.Key)))
        {
            var materia = materias[MateriaKey(item.Materia)]; var bloque = bloques[BloqueKey(item.Bloque)];
            actions.Add(() => db.HorariosMateria.Add(new HorarioMateria { Materia = materia, Curso = materia.Curso, HorarioAnexo = bloque }));
            Contar(r, "asignaciones horarias", crear: 1);
        }
    }

    private static void ValidarDocencias(List<DocenciaTransferencia> source, Dictionary<string, DocenteTransferencia> docentes)
    {
        Exigir(source != null, "La lista docentes de cada materia es obligatoria.");
        Exigir(source!.Count <= 2 && (source.Count == 0 || source.Count(d => d.Activo) == 1), "Debe existir exactamente un docente activo cuando la materia tiene docentes; maximo titular y suplente.");
        Unicos(source, d => DocenteKey(d.Docente), "docentes de una materia");
        Unicos(source, d => N(d.Rol), "roles de una materia");
        foreach (var item in source)
        {
            Exigir(N(item.Rol) is "TITULAR" or "SUPLENTE", "El rol docente debe ser Titular o Suplente.");
            Exigir(docentes.ContainsKey(DocenteKey(item.Docente)), "Falta el contexto de un docente de materia.");
        }
    }

    private static void ValidarDocente(DocenteTransferencia item)
    {
        Texto(item.Nombres, "nombres del docente"); Texto(item.Apellidos, "apellidos del docente");
        Exigir(string.IsNullOrWhiteSpace(item.Email) || System.Net.Mail.MailAddress.TryCreate(item.Email.Trim(), out var email) && email.Address == item.Email.Trim(), "Email de docente invalido.");
    }
    private static void ValidarCurso(CursoClave c)
    {
        Texto(c.CursoNombre, "anio/curso"); Texto(c.Division, "division"); Texto(c.Anexo, "anexo"); Texto(c.Orientacion, "orientacion");
        Exigir(c.CursoNombre is "1\u00b0" or "2\u00b0" or "3\u00b0" && c.Division.Length == 1 && c.Division[0] is >= 'A' and <= 'K',
            "El curso debe ser 1°, 2° o 3° y la division de A a K.");
    }
    private static void ValidarBloque(BloqueTransferencia b)
    {
        Exigir(b.Clave.DiaSemana is >= DayOfWeek.Monday and <= DayOfWeek.Friday && b.Clave.Orden > 0
            && Enum.IsDefined(b.Tipo) && b.HoraFin > b.HoraInicio, "Los bloques deben ser de lunes a viernes, con orden positivo, tipo Clase/Receso y duracion positiva.");
    }
    private static bool MismoBloque(HorarioAnexo a, BloqueTransferencia b) => a.Tipo == b.Tipo && a.HoraInicio == b.HoraInicio && a.HoraFin == b.HoraFin;
    private static string FirmaDocencias(IEnumerable<DocenciaTransferencia> source) => Key(source
        .Select(d => Key(DocenteKey(d.Docente), N(d.Rol), d.Activo)).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    private static string Rol(string s) => N(s) == "TITULAR" ? "Titular" : "Suplente";
    private static string? Email(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToLowerInvariant();
    private static string N(string? s) => string.Join(" ", (s ?? "").Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string Key(params object[] values) => JsonSerializer.Serialize(values, JsonOptions);
    private static string NombreDocente(DocenteTransferencia d) => Key(N(d.Nombres), N(d.Apellidos));
    private static string DocenteKey(DocenteTransferencia d) => string.IsNullOrWhiteSpace(d.Email) ? Key("nombre", NombreDocente(d)) : Key("email", N(d.Email));
    private static string CursoKey(CursoClave c) => Key(N(c.CursoNombre), N(c.Division), N(c.Anexo), N(c.Orientacion));
    private static string MateriaKey(MateriaClave m) => Key(CursoKey(m.Curso), N(m.Nombre));
    private static string BloqueKey(BloqueClave b) => Key(N(b.Anexo), b.DiaSemana, b.Orden);
    private static DocenteTransferencia DocenteDto(Docente d) => new(d.Nombres, d.Apellidos, d.Email);
    private static CursoClave CursoDto(Curso c) => new(c.CursoNombre, c.Division, c.Anexo.Nombre, c.Orientacion.Nombre);
    private static MateriaClave MateriaDto(Materia m) => new(CursoDto(m.Curso), m.Nombre);
    private static BloqueClave BloqueDto(HorarioAnexo b) => new(b.Anexo.Nombre, b.DiaSemana, b.Orden);
    private static void Texto(string? s, string label) => Exigir(!string.IsNullOrWhiteSpace(s) && s.Length <= 200, $"Campo obligatorio o demasiado largo: {label} (maximo 200 caracteres).");
    private static void Exigir(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Dictionary<string, T> Unicos<T>(IEnumerable<T> values, Func<T, string> key, string label)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
            Exigir(result.TryAdd(key(value), value), $"Hay claves naturales duplicadas o ambiguas en {label}. Corrige los duplicados antes de transferir.");
        return result;
    }
    private static void Contar(ReporteTransferencia r, string entidad, int crear = 0, int actualizar = 0, int eliminar = 0)
    {
        var row = r.Resumen.SingleOrDefault(x => x.Entidad == entidad);
        if (row == null) { row = new ResumenTransferencia { Entidad = entidad }; r.Resumen.Add(row); }
        row.Crear += crear; row.Actualizar += actualizar; row.Eliminar += eliminar;
    }
    private static string[] Alcance(string seccion) => seccion switch
    {
        "docentes" => ["Nombres, apellidos y email de docentes"],
        "materias" => ["Materias, carga horaria y docentes con rol/activo", "Anexos, orientaciones, cursos y docentes como prerrequisitos nuevos solamente", "Horarios existentes del destino conservados"],
        "horarios" => ["Grillas base y asignaciones horarias por materia", "Anexos, orientaciones, cursos, docentes y materias como prerrequisitos nuevos solamente"],
        "cursos" => ["Cursos y modalidad semipresencial", "Anexos y orientaciones como prerrequisitos nuevos solamente"],
        "usuarios" => ["Perfiles de usuarios sin credenciales ni archivos", "Roles y responsabilidades como prerrequisitos nuevos solamente"],
        "roles" => ["Roles y sus responsabilidades por nombre", "Responsabilidades como prerrequisitos nuevos solamente"],
        "responsabilidades" => ["Nombres y descripciones de responsabilidades"],
        _ => ["Ficha personal de alumnos activos", "Contacto y datos de nacimiento"]
    };

    private string CalcularHuella(PaqueteTransferencia p, string modo)
    {
        // Security sections include authentication scalars only inside this one-way digest, never in DTOs.
        var rows = db.ChangeTracker.Entries().Select(e => Key(e.Metadata.Name,
            e.Properties.OrderBy(x => x.Metadata.Name, StringComparer.Ordinal)
                .Select(x => Key(x.Metadata.Name, x.CurrentValue is Enum value ? Convert.ToInt64(value) : x.CurrentValue!)).ToArray()))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var input = Key(p.Formato, p.Version, p.Seccion, modo, p.Datos, rows);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static bool EsSeguridad(string seccion) => seccion is "usuarios" or "roles" or "responsabilidades";

    private async Task<Estado> CargarAsync(CancellationToken ct, bool seguridad = false)
    {
        db.ChangeTracker.Clear();
        var s = new Estado
        {
            Anexos = await db.Anexos.ToListAsync(ct),
            Orientaciones = await db.Orientaciones.ToListAsync(ct),
            Cursos = await db.Cursos.ToListAsync(ct),
            Docentes = await db.Docentes.ToListAsync(ct),
            Materias = await db.Materias.ToListAsync(ct),
            Docencias = await db.MateriaDocentes.ToListAsync(ct),
            Bloques = await db.HorariosAnexo.ToListAsync(ct),
            Asignaciones = await db.HorariosMateria.ToListAsync(ct),
            Alumnos = await db.Alumnos.IgnoreQueryFilters().ToListAsync(ct),
            Cursadas = await db.CursadasMaterias.IgnoreQueryFilters().ToListAsync(ct)
        };
        await db.AlumnoContactos.IgnoreQueryFilters().LoadAsync(ct);
        await db.AlumnoNacimientos.IgnoreQueryFilters().LoadAsync(ct);
        await db.AlumnoDocumentos.IgnoreQueryFilters().LoadAsync(ct);
        await db.Inscripciones.IgnoreQueryFilters().LoadAsync(ct);
        await db.Calificaciones.IgnoreQueryFilters().LoadAsync(ct);
        if (seguridad)
        {
            s.Responsabilidades = await db.Responsibilities.IgnoreQueryFilters().ToListAsync(ct);
            s.Roles = await db.Roles.IgnoreQueryFilters().ToListAsync(ct);
            await db.Set<RoleResponsibility>().IgnoreQueryFilters().LoadAsync(ct);
            await db.Set<RolePermission>().IgnoreQueryFilters().LoadAsync(ct);
            s.Usuarios = await db.Users.IgnoreQueryFilters().ToListAsync(ct);
            await db.Auths.IgnoreQueryFilters().LoadAsync(ct);
        }
        return s;
    }

    private sealed class Estado
    {
        public List<User> Usuarios { get; set; } = [];
        public List<Role> Roles { get; set; } = [];
        public List<Responsibility> Responsabilidades { get; set; } = [];
        public required List<Anexo> Anexos { get; init; }
        public required List<Orientacion> Orientaciones { get; init; }
        public required List<Curso> Cursos { get; init; }
        public required List<Docente> Docentes { get; init; }
        public required List<Materia> Materias { get; init; }
        public required List<MateriaDocente> Docencias { get; init; }
        public required List<HorarioAnexo> Bloques { get; init; }
        public required List<HorarioMateria> Asignaciones { get; init; }
        public required List<Alumno> Alumnos { get; init; }
        public required List<CursadaMateria> Cursadas { get; init; }
    }
}
