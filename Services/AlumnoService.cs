using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CENS15_V2.Services
{
    public class AlumnoService : IAlumnoService
    {
        private const int MaxImagenesPorDocumento = 2;

        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public AlumnoService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<AlumnoDto>> GetAllAsync(bool includeDeleted = false)
        {
            var alumnos = await QueryAlumno(includeDeleted).AsNoTracking().ToListAsync();
            return _mapper.Map<IEnumerable<AlumnoDto>>(alumnos);
        }

        public async Task<AlumnoDto?> GetByIdAsync(int id)
        {
            var alumno = await QueryAlumno().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
            return alumno == null ? null : _mapper.Map<AlumnoDto>(alumno);
        }

        public async Task<AlumnoDto> CreateAsync(CreateAlumnoRequest request)
        {
            ValidateGenero(request.Genero);
            ValidateFechaNacimiento(request.FechaNacimiento);
            await ValidateTipoDocumentosAsync(request.Documentos.Select(d => d.TipoDocumentoAlumnoId));
            await ValidateNumeroDocumentoDisponibleAsync(request.NumeroDocumento);

            var alumno = _mapper.Map<Alumno>(request);
            alumno.Nombres = FormatNombreApellido(alumno.Nombres);
            alumno.Apellidos = FormatNombreApellido(alumno.Apellidos);
            alumno.NumeroDocumento = string.IsNullOrWhiteSpace(request.NumeroDocumento) ? null : request.NumeroDocumento.Trim();
            NormalizeDocumentos(alumno.Documentos);
            _context.Alumnos.Add(alumno);
            await _context.SaveChangesAsync();

            var created = await QueryAlumno().AsNoTracking().FirstAsync(a => a.Id == alumno.Id);
            return _mapper.Map<AlumnoDto>(created);
        }

        public async Task<bool> UpdateAsync(int id, UpdateAlumnoRequest request)
        {
            ValidateGenero(request.Genero);
            ValidateFechaNacimiento(request.FechaNacimiento);
            await ValidateTipoDocumentosAsync(request.Documentos.Select(d => d.TipoDocumentoAlumnoId));
            await ValidateNumeroDocumentoDisponibleAsync(request.NumeroDocumento, id);

            var alumno = await QueryAlumno().FirstOrDefaultAsync(a => a.Id == id);
            if (alumno == null)
            {
                return false;
            }

            alumno.Nombres = FormatNombreApellido(request.Nombres);
            alumno.Apellidos = FormatNombreApellido(request.Apellidos);
            alumno.NumeroDocumento = string.IsNullOrWhiteSpace(request.NumeroDocumento) ? null : request.NumeroDocumento.Trim();
            alumno.FechaNacimiento = request.FechaNacimiento;
            alumno.Genero = request.Genero;
            alumno.Domicilio = request.Domicilio;

            alumno.DatosNacimiento ??= new AlumnoNacimiento();
            alumno.DatosNacimiento.Localidad = request.DatosNacimiento.Localidad;
            alumno.DatosNacimiento.Provincia = request.DatosNacimiento.Provincia;
            alumno.DatosNacimiento.Pais = request.DatosNacimiento.Pais;

            alumno.Contacto ??= new AlumnoContacto();
            alumno.Contacto.TelefonoAlumno = request.Contacto.TelefonoAlumno;
            alumno.Contacto.Email = request.Contacto.Email;
            alumno.Contacto.NombreEmergencia = request.Contacto.NombreEmergencia;
            alumno.Contacto.TelefonoEmergencia = request.Contacto.TelefonoEmergencia;

            _context.AlumnoDocumentos.RemoveRange(alumno.Documentos);
            alumno.Documentos = request.Documentos.Select(d => new AlumnoDocumento
            {
                TipoDocumentoAlumnoId = d.TipoDocumentoAlumnoId,
                Presentado = d.Presentado,
                ImagenUrl = GetImagenesUrl(d.ImagenUrl, d.ImagenesUrl).FirstOrDefault(),
                ImagenesUrl = GetImagenesUrl(d.ImagenUrl, d.ImagenesUrl)
            }).ToList();

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var alumno = await _context.Alumnos.FirstOrDefaultAsync(a => a.Id == id);
            if (alumno == null)
            {
                return false;
            }

            alumno.Eliminado = true;
            alumno.FechaEliminacion = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<AlumnoDeletionInfoDto?> GetDeletionInfoAsync(int id)
        {
            var alumnoExists = await _context.Alumnos.AnyAsync(a => a.Id == id);
            if (!alumnoExists)
            {
                return null;
            }

            var inscripciones = _context.Inscripciones.Where(i => i.AlumnoId == id);
            var inscripcionesCount = await inscripciones.CountAsync();
            var inscripcionesActivasCount = await inscripciones.CountAsync(i => i.Estado == EstadoInscripcion.Activa);
            var calificacionesCount = await _context.Calificaciones.CountAsync(c =>
                c.CursadaMateria.Inscripcion.AlumnoId == id);

            var tieneDatosAcademicos = inscripcionesCount > 0 || calificacionesCount > 0;
            return new AlumnoDeletionInfoDto
            {
                AlumnoId = id,
                InscripcionesCount = inscripcionesCount,
                InscripcionesActivasCount = inscripcionesActivasCount,
                CalificacionesCount = calificacionesCount,
                Mensaje = tieneDatosAcademicos
                    ? $"El alumno tiene {inscripcionesCount} inscripción(es) y {calificacionesCount} calificación(es). Se ocultará, pero su historial académico se conservará."
                    : "El alumno se ocultará de la aplicación. Podrá restaurarse desde el panel de administración."
            };
        }

        public async Task<bool> RestoreAsync(int id)
        {
            var alumno = await _context.Alumnos
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == id);
            if (alumno == null)
            {
                return false;
            }

            alumno.Eliminado = false;
            alumno.FechaEliminacion = null;
            await _context.SaveChangesAsync();
            return true;
        }

        private IQueryable<Alumno> QueryAlumno(bool includeDeleted = false)
        {
            var alumnos = includeDeleted
                ? _context.Alumnos.IgnoreQueryFilters()
                : _context.Alumnos;

            return alumnos
                .Include(a => a.DatosNacimiento)
                .Include(a => a.Contacto)
                .Include(a => a.Documentos)
                    .ThenInclude(d => d.TipoDocumentoAlumno);
        }


        private static void ValidateGenero(Genero? genero)
        {
            if (genero.HasValue && !Enum.IsDefined(typeof(Genero), genero.Value))
            {
                throw new InvalidOperationException("El género debe ser Varon o Mujer.");
            }
        }

        private static void ValidateFechaNacimiento(DateOnly? fechaNacimiento)
        {
            if (fechaNacimiento.HasValue && (fechaNacimiento.Value == DateOnly.MinValue || fechaNacimiento.Value > DateOnly.FromDateTime(DateTime.UtcNow)))
            {
                throw new InvalidOperationException("La fecha de nacimiento debe ser valida y no futura, o quedar vacia.");
            }
        }

        private static string FormatNombreApellido(string value)
        {
            var normalized = value.Trim().ToLower(new CultureInfo("es-AR"));
            return CultureInfo.GetCultureInfo("es-AR").TextInfo.ToTitleCase(normalized);
        }

        private static void NormalizeDocumentos(IEnumerable<AlumnoDocumento> documentos)
        {
            foreach (var documento in documentos)
            {
                documento.ImagenesUrl = GetImagenesUrl(documento.ImagenUrl, documento.ImagenesUrl);
                documento.ImagenUrl = documento.ImagenesUrl.FirstOrDefault();
            }
        }

        private static List<string> GetImagenesUrl(string? imagenUrl, IEnumerable<string>? imagenesUrl)
        {
            var urls = imagenesUrl?
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .ToList() ?? new List<string>();

            if (!string.IsNullOrWhiteSpace(imagenUrl) && urls.Count == 0)
            {
                urls.Add(imagenUrl.Trim());
            }

            if (urls.Count > MaxImagenesPorDocumento)
            {
                throw new InvalidOperationException($"Cada documento acepta como máximo {MaxImagenesPorDocumento} imágenes.");
            }

            return urls;
        }

        private async Task ValidateTipoDocumentosAsync(IEnumerable<int> tipoDocumentoIds)
        {
            var ids = tipoDocumentoIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            var count = await _context.TiposDocumentoAlumno.CountAsync(t => ids.Contains(t.Id));
            if (count != ids.Count)
            {
                throw new InvalidOperationException("Uno o más tipos de documento no existen.");
            }
        }

        private async Task ValidateNumeroDocumentoDisponibleAsync(string? numeroDocumento, int? alumnoId = null)
        {
            if (string.IsNullOrWhiteSpace(numeroDocumento))
            {
                return;
            }

            var normalizedNumeroDocumento = numeroDocumento.Trim();
            var exists = await _context.Alumnos.IgnoreQueryFilters().AnyAsync(a =>
                a.NumeroDocumento == normalizedNumeroDocumento &&
                (!alumnoId.HasValue || a.Id != alumnoId.Value));

            if (exists)
            {
                throw new InvalidOperationException($"Ya existe un alumno registrado con DNI {normalizedNumeroDocumento}.");
            }
        }
    }
}
