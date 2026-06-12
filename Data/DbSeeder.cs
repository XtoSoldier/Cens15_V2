using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Data
{
    public class DbSeeder
    {
        private const string SeedAdminEmail = "admin@admin.com";

        public static async Task Seed(AppDbContext context, PasswordHasher hasher)
        {
            var rolesToCreate = new[]
             {
                "Admin",
                "User"
            };

            foreach (var roleName in rolesToCreate)
            {
                if (!await context.Roles.AnyAsync(r => r.Name == roleName))
                {
                    context.Roles.Add(new Role
                    {
                        Id = Guid.NewGuid(),
                        Name = roleName
                    });
                }
            }

            var tiposDocumentoToCreate = new[]
            {
                "Fotocopia DNI",
                "Certificado de Estudio",
                "Pase",
                "Cuil"
            };

            foreach (var tipoDocumentoNombre in tiposDocumentoToCreate)
            {
                if (!await context.TiposDocumentoAlumno.AnyAsync(t => t.Nombre == tipoDocumentoNombre))
                {
                    context.TiposDocumentoAlumno.Add(new TipoDocumentoAlumno
                    {
                        Nombre = tipoDocumentoNombre
                    });
                }
            }

            await context.SaveChangesAsync();

            var adminExists = await context.Auths.AnyAsync(a => a.Email == SeedAdminEmail);
            if (!adminExists)
            {
                var adminRole = await context.Roles.FirstAsync(r => r.Name == "Admin");

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Super",
                    LastName = "Admin",
                    Image = "default.png",
                    RoleId = adminRole.Id,
                    Status = true
                };

                var auth = new Auth
                {
                    Id = user.Id,
                    Email = SeedAdminEmail,
                    PasswordHash = hasher.Hash("Admin123*"),
                    User = user
                };

                context.AddRange(user, auth);
                await context.SaveChangesAsync();
            }

            await SeedSampleMateriasYDocentes(context);
            await SeedCursadasMaterias(context);
            await VincularAdminConDocente(context);
            await SeedAlumnosInscriptosParaGarcia(context);
        }

        private static async Task VincularAdminConDocente(AppDbContext context)
        {
            var adminAuth = await context.Auths.FirstOrDefaultAsync(a => a.Email == SeedAdminEmail);
            if (adminAuth == null) return;

            var garcia = await context.Docentes.FirstOrDefaultAsync(d => d.Email == "juan.garcia@cens15.edu.ar");
            if (garcia == null) return;

            if (garcia.UserId == null)
            {
                garcia.UserId = adminAuth.Id;
                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedSampleMateriasYDocentes(AppDbContext context)
        {
            var docentes = new[]
            {
                ("Ana", "Gomez", "ana.gomez@cens15.edu.ar"),
                ("Carlos", "Martinez", "carlos.martinez@cens15.edu.ar"),
                ("Lucia", "Fernandez", "lucia.fernandez@cens15.edu.ar"),
                ("Diego", "Rodriguez", "diego.rodriguez@cens15.edu.ar"),
                ("Sofia", "Pereyra", "sofia.pereyra@cens15.edu.ar"),
                ("Martin", "Lopez", "martin.lopez@cens15.edu.ar"),
                ("Valeria", "Sosa", "valeria.sosa@cens15.edu.ar"),
                ("Pablo", "Benitez", "pablo.benitez@cens15.edu.ar"),
                ("Carolina", "Ramos", "carolina.ramos@cens15.edu.ar"),
                ("Jorge", "Acosta", "jorge.acosta@cens15.edu.ar"),
                ("Juan", "Garcia", "juan.garcia@cens15.edu.ar")
            };

            foreach (var docente in docentes)
            {
                if (!await context.Docentes.AnyAsync(d => d.Email == docente.Item3))
                {
                    context.Docentes.Add(new Docente
                    {
                        Nombres = docente.Item1,
                        Apellidos = docente.Item2,
                        Email = docente.Item3
                    });
                }
            }

            await context.SaveChangesAsync();

            if (!await context.Cursos.AnyAsync())
            {
                var orientacion = await context.Orientaciones.FirstOrDefaultAsync(o => o.NombreCorto == "Aduana");
                if (orientacion == null)
                {
                    orientacion = new Orientacion { Nombre = "Administracion de Aduana", NombreCorto = "Aduana" };
                    context.Orientaciones.Add(orientacion);
                }

                var anexo = await context.Anexos.FirstOrDefaultAsync(a => a.Nombre == "Esc. 41");
                if (anexo == null)
                {
                    anexo = new Anexo { Nombre = "Esc. 41" };
                    context.Anexos.Add(anexo);
                }

                await context.SaveChangesAsync();

                var cursos = new[]
                {
                    ("1", "A"), ("2", "B"), ("3", "D")
                };

                foreach (var curso in cursos)
                {
                    context.Cursos.Add(new Curso
                    {
                        CursoNombre = curso.Item1,
                        Division = curso.Item2,
                        OrientacionId = orientacion.Id,
                        AnexoId = anexo.Id,
                        Semipresencial = false
                    });
                }

                await context.SaveChangesAsync();
            }

            var docentesIds = await context.Docentes
                .OrderBy(d => d.Id)
                .Select(d => d.Id)
                .ToListAsync();

            if (docentesIds.Count == 0)
            {
                return;
            }

            var materiasBase = new[]
            {
                "Lengua y Literatura",
                "Matematica",
                "Ciencias Sociales",
                "Ciencias Naturales",
                "Ingles",
                "Educacion Civica",
                "Administracion",
                "Informatica"
            };

            var cursosExistentes = await context.Cursos
                .Include(c => c.Materias)
                .ThenInclude(m => m.Docentes)
                .OrderBy(c => c.Id)
                .ToListAsync();

            var docenteIndex = 0;
            foreach (var curso in cursosExistentes)
            {
                foreach (var nombreMateria in materiasBase)
                {
                    var materia = curso.Materias.FirstOrDefault(m => m.Nombre == nombreMateria);
                    if (materia == null)
                    {
                        materia = new Materia
                        {
                            Nombre = nombreMateria,
                            CursoId = curso.Id
                        };
                        context.Materias.Add(materia);
                        await context.SaveChangesAsync();
                    }

                    if (!await context.Set<MateriaDocente>().AnyAsync(md => md.MateriaId == materia.Id))
                    {
                        context.Set<MateriaDocente>().Add(new MateriaDocente
                        {
                            MateriaId = materia.Id,
                            DocenteId = docentesIds[docenteIndex % docentesIds.Count],
                            Rol = "Titular"
                        });
                        docenteIndex++;
                    }
                }
            }

            await context.SaveChangesAsync();
        }

        private static async Task SeedCursadasMaterias(AppDbContext context)
        {
            var inscripciones = await context.Inscripciones
                .AsNoTracking()
                .Select(i => new { i.Id, i.CursoId })
                .ToListAsync();

            if (inscripciones.Count == 0)
            {
                return;
            }

            var materias = await context.Materias
                .AsNoTracking()
                .Select(m => new { m.Id, m.Nombre, m.CursoId })
                .ToListAsync();

            var cursadasExistentes = await context.CursadasMaterias
                .AsNoTracking()
                .Select(cm => new { cm.InscripcionId, cm.MateriaId })
                .ToListAsync();

            var cursadasSet = cursadasExistentes
                .Select(cm => (cm.InscripcionId, cm.MateriaId))
                .ToHashSet();

            var cursadasNuevas = new List<CursadaMateria>();
            foreach (var inscripcion in inscripciones)
            {
                foreach (var materia in materias.Where(m => m.CursoId == inscripcion.CursoId))
                {
                    if (cursadasSet.Contains((inscripcion.Id, materia.Id)))
                    {
                        continue;
                    }

                    cursadasNuevas.Add(new CursadaMateria
                    {
                        InscripcionId = inscripcion.Id,
                        MateriaId = materia.Id,
                        MateriaNombre = materia.Nombre
                    });
                }
            }

            if (cursadasNuevas.Count == 0)
            {
                return;
            }

            context.CursadasMaterias.AddRange(cursadasNuevas);
            await context.SaveChangesAsync();
        }

        private static async Task SeedAlumnosInscriptosParaGarcia(AppDbContext context)
        {
            var garcia = await context.Docentes.FirstOrDefaultAsync(d => d.Email == "juan.garcia@cens15.edu.ar");
            if (garcia == null) return;

            var cursos = await context.Cursos
                .Include(c => c.Materias)
                .OrderBy(c => c.Id)
                .ToListAsync();

            if (cursos.Count == 0) return;

            var materiasParaGarcia = new[] { "Matematica", "Informatica" };

            foreach (var curso in cursos)
            {
                foreach (var materiaNombre in materiasParaGarcia)
                {
                    var materia = curso.Materias.FirstOrDefault(m => m.Nombre == materiaNombre);
                    if (materia == null) continue;

                    var yaAsignado = await context.Set<MateriaDocente>()
                        .AnyAsync(md => md.MateriaId == materia.Id && md.DocenteId == garcia.Id);

                    if (!yaAsignado)
                    {
                        context.Set<MateriaDocente>().Add(new MateriaDocente
                        {
                            MateriaId = materia.Id,
                            DocenteId = garcia.Id,
                            Rol = "Adjunto"
                        });
                    }
                }
            }

            await context.SaveChangesAsync();

            var alumnosExistentes = await context.Alumnos.CountAsync();
            if (alumnosExistentes > 0)
            {
                var inscripcionesExistentes = await context.Inscripciones.CountAsync();
                if (inscripcionesExistentes > 0)
                {
                    await SeedCursadasMaterias(context);
                    return;
                }
            }

            var nombresAlumnos = new[]
            {
                ("Lopez", "Juan"),
                ("Gonzalez", "Maria"),
                ("Martinez", "Carlos"),
                ("Rodriguez", "Ana"),
                ("Fernandez", "Pedro"),
                ("Diaz", "Laura"),
                ("Perez", "Santiago"),
                ("Garcia", "Valentina"),
            };

            var nuevosAlumnos = new List<Alumno>();
            foreach (var (apellido, nombre) in nombresAlumnos)
            {
                var dni = $"{20000000 + nuevosAlumnos.Count}";
                if (!await context.Alumnos.AnyAsync(a => a.NumeroDocumento == dni))
                {
                    var alumno = new Alumno
                    {
                        Nombres = nombre,
                        Apellidos = apellido,
                        NumeroDocumento = dni
                    };
                    context.Alumnos.Add(alumno);
                    nuevosAlumnos.Add(alumno);
                }
            }

            await context.SaveChangesAsync();

            var todosAlumnos = await context.Alumnos.OrderBy(a => a.Id).ToListAsync();
            if (todosAlumnos.Count == 0) return;

            var currentYear = DateTime.UtcNow.Year;
            var inscripcionesNuevas = new List<Inscripcion>();
            var alumnoIdx = 0;

            foreach (var curso in cursos)
            {
                for (int i = 0; i < 3 && alumnoIdx < todosAlumnos.Count; i++)
                {
                    var alumno = todosAlumnos[alumnoIdx];
                    alumnoIdx++;

                    if (!await context.Inscripciones.AnyAsync(ins =>
                        ins.AlumnoId == alumno.Id && ins.CursoId == curso.Id && ins.Anio == currentYear))
                    {
                        inscripcionesNuevas.Add(new Inscripcion
                        {
                            AlumnoId = alumno.Id,
                            CursoId = curso.Id,
                            Anio = currentYear,
                            FechaInscripcion = DateTime.UtcNow,
                            Estado = EstadoInscripcion.Activa
                        });
                    }
                }
            }

            if (inscripcionesNuevas.Count > 0)
            {
                context.Inscripciones.AddRange(inscripcionesNuevas);
                await context.SaveChangesAsync();
            }

            await SeedCursadasMaterias(context);
        }
    }
}
