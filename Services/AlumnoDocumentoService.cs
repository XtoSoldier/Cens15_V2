using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class AlumnoDocumentoService : IAlumnoDocumentoService
    {
        private const int MaxArchivos = 2;

        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly IWebHostEnvironment _environment;

        public AlumnoDocumentoService(AppDbContext context, IMapper mapper, IWebHostEnvironment environment)
        {
            _context = context;
            _mapper = mapper;
            _environment = environment;
        }

        public async Task<IEnumerable<AlumnoDocumentoItemDto>> GetAllAsync(int? alumnoId = null)
        {
            var query = _context.AlumnoDocumentos
                .AsNoTracking()
                .Include(d => d.TipoDocumentoAlumno)
                .AsQueryable();

            if (alumnoId.HasValue)
            {
                query = query.Where(d => d.AlumnoId == alumnoId.Value);
            }

            var documentos = await query.ToListAsync();
            return _mapper.Map<IEnumerable<AlumnoDocumentoItemDto>>(documentos);
        }

        public async Task<AlumnoDocumentoItemDto?> GetByIdAsync(int id)
        {
            var documento = await _context.AlumnoDocumentos
                .AsNoTracking()
                .Include(d => d.TipoDocumentoAlumno)
                .FirstOrDefaultAsync(d => d.Id == id);

            return documento == null ? null : _mapper.Map<AlumnoDocumentoItemDto>(documento);
        }

        public async Task<AlumnoDocumentoItemDto> CreateAsync(CreateAlumnoDocumentoItemRequest request)
        {
            await ValidateReferencesAsync(request.AlumnoId, request.TipoDocumentoAlumnoId);

            var documento = _mapper.Map<AlumnoDocumento>(request);
            documento.ImagenesUrl = await BuildArchivosUrlAsync(request.ImagenUrl, request.ImagenesUrl, request.Imagenes);
            documento.ImagenUrl = documento.ImagenesUrl.FirstOrDefault();

            _context.AlumnoDocumentos.Add(documento);
            await _context.SaveChangesAsync();

            var created = await _context.AlumnoDocumentos
                .AsNoTracking()
                .Include(d => d.TipoDocumentoAlumno)
                .FirstAsync(d => d.Id == documento.Id);

            return _mapper.Map<AlumnoDocumentoItemDto>(created);
        }

        public async Task<bool> UpdateAsync(int id, UpdateAlumnoDocumentoItemRequest request)
        {
            var documento = await _context.AlumnoDocumentos.FirstOrDefaultAsync(d => d.Id == id);
            if (documento == null)
            {
                return false;
            }

            var tipoExists = await _context.TiposDocumentoAlumno.AnyAsync(t => t.Id == request.TipoDocumentoAlumnoId);
            if (!tipoExists)
            {
                throw new InvalidOperationException("El tipo de documento no existe.");
            }

            documento.TipoDocumentoAlumnoId = request.TipoDocumentoAlumnoId;
            documento.Presentado = request.Presentado;
            documento.ImagenesUrl = await BuildArchivosUrlAsync(request.ImagenUrl, request.ImagenesUrl, request.Imagenes);
            documento.ImagenUrl = documento.ImagenesUrl.FirstOrDefault();

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var documento = await _context.AlumnoDocumentos.FindAsync(id);
            if (documento == null)
            {
                return false;
            }

            _context.AlumnoDocumentos.Remove(documento);
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task ValidateReferencesAsync(int alumnoId, int tipoDocumentoAlumnoId)
        {
            var alumnoExists = await _context.Alumnos.AnyAsync(a => a.Id == alumnoId);
            if (!alumnoExists)
            {
                throw new InvalidOperationException("El alumno no existe.");
            }

            var tipoExists = await _context.TiposDocumentoAlumno.AnyAsync(t => t.Id == tipoDocumentoAlumnoId);
            if (!tipoExists)
            {
                throw new InvalidOperationException("El tipo de documento no existe.");
            }
        }

        private async Task<List<string>> BuildArchivosUrlAsync(string? imagenUrl, IEnumerable<string>? imagenesUrl, IEnumerable<IFormFile>? imagenes)
        {
            var urls = imagenesUrl?
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .ToList() ?? new List<string>();

            if (!string.IsNullOrWhiteSpace(imagenUrl) && urls.Count == 0)
            {
                urls.Add(imagenUrl.Trim());
            }

            var archivos = imagenes?.Where(file => file.Length > 0).ToList() ?? new List<IFormFile>();
            if (urls.Count + archivos.Count > MaxArchivos)
            {
                throw new InvalidOperationException($"AlumnoDocumento acepta como máximo {MaxArchivos} archivos.");
            }

            foreach (var archivo in archivos)
            {
                if (!IsArchivoPermitido(archivo))
                {
                    throw new InvalidOperationException("Solo se permiten imágenes o archivos PDF.");
                }

                urls.Add(await SaveArchivoAsync(archivo));
            }

            return urls;
        }

        private static bool IsArchivoPermitido(IFormFile archivo)
        {
            return archivo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || archivo.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> SaveArchivoAsync(IFormFile archivo)
        {
            var uploadsPath = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads", "alumno-documentos");
            Directory.CreateDirectory(uploadsPath);

            var extension = Path.GetExtension(archivo.FileName);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsPath, fileName);

            await using var stream = File.Create(filePath);
            await archivo.CopyToAsync(stream);

            return $"/uploads/alumno-documentos/{fileName}";
        }
    }
}
