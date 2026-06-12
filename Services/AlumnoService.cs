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

        public async Task<IEnumerable<AlumnoDto>> GetAllAsync()
        {
            var alumnos = await QueryAlumno().AsNoTracking().ToListAsync();
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
            await ValidateTipoDocumentosAsync(request.Documentos.Select(d => d.TipoDocumentoAlumnoId));
            await ValidateNumeroDocumentoDisponibleAsync(request.NumeroDocumento);

            var alumno = _mapper.Map<Alumno>(request);
            alumno.Nombres = FormatNombreApellido(alumno.Nombres);
            alumno.Apellidos = FormatNombreApellido(alumno.Apellidos);
            alumno.NumeroDocumento = request.NumeroDocumento.Trim();
            NormalizeDocumentos(alumno.Documentos);
            _context.Alumnos.Add(alumno);
            await _context.SaveChangesAsync();

            var created = await QueryAlumno().AsNoTracking().FirstAsync(a => a.Id == alumno.Id);
            return _mapper.Map<AlumnoDto>(created);
        }

        public async Task<bool> UpdateAsync(int id, UpdateAlumnoRequest request)
        {
            ValidateGenero(request.Genero);
            await ValidateTipoDocumentosAsync(request.Documentos.Select(d => d.TipoDocumentoAlumnoId));
            await ValidateNumeroDocumentoDisponibleAsync(request.NumeroDocumento, id);

            var alumno = await QueryAlumno().FirstOrDefaultAsync(a => a.Id == id);
            if (alumno == null)
            {
                return false;
            }

            alumno.Nombres = FormatNombreApellido(request.Nombres);
            alumno.Apellidos = FormatNombreApellido(request.Apellidos);
            alumno.NumeroDocumento = request.NumeroDocumento.Trim();
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
            var alumno = await _context.Alumnos.FindAsync(id);
            if (alumno == null)
            {
                return false;
            }

            _context.Alumnos.Remove(alumno);
            await _context.SaveChangesAsync();
            return true;
        }

        private IQueryable<Alumno> QueryAlumno()
        {
            return _context.Alumnos
                .Include(a => a.DatosNacimiento)
                .Include(a => a.Contacto)
                .Include(a => a.Documentos)
                    .ThenInclude(d => d.TipoDocumentoAlumno);
        }


        private static void ValidateGenero(Genero genero)
        {
            if (!Enum.IsDefined(typeof(Genero), genero))
            {
                throw new InvalidOperationException("El género debe ser Varon o Mujer.");
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

        private async Task ValidateNumeroDocumentoDisponibleAsync(string numeroDocumento, int? alumnoId = null)
        {
            var normalizedNumeroDocumento = numeroDocumento.Trim();
            var exists = await _context.Alumnos.AnyAsync(a =>
                a.NumeroDocumento == normalizedNumeroDocumento &&
                (!alumnoId.HasValue || a.Id != alumnoId.Value));

            if (exists)
            {
                throw new InvalidOperationException($"Ya existe un alumno registrado con DNI {normalizedNumeroDocumento}.");
            }
        }
    }
}
