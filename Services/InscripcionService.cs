using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.InscripcionesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class InscripcionService : IInscripcionService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public InscripcionService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<InscripcionDto> CreateAsync(CreateInscripcionRequest request)
        {
            await ValidateCreateAsync(request);

            var inscripcion = new Inscripcion
            {
                AlumnoId = request.AlumnoId,
                CursoId = request.CursoId,
                Anio = request.Anio,
                FechaInscripcion = DateTime.UtcNow,
                Estado = EstadoInscripcion.Activa
            };

            _context.Inscripciones.Add(inscripcion);
            await _context.SaveChangesAsync();

            var created = await QueryInscripciones()
                .AsNoTracking()
                .FirstAsync(i => i.Id == inscripcion.Id);

            return _mapper.Map<InscripcionDto>(created);
        }

        public async Task<InscripcionDto?> GetByIdAsync(int id)
        {
            var inscripcion = await QueryInscripciones()
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id);

            return inscripcion == null ? null : _mapper.Map<InscripcionDto>(inscripcion);
        }

        public async Task<IEnumerable<InscripcionDto>> GetByAlumnoIdAsync(int alumnoId)
        {
            var inscripciones = await QueryInscripciones()
                .AsNoTracking()
                .Where(i => i.AlumnoId == alumnoId)
                .OrderByDescending(i => i.Anio)
                .ThenBy(i => i.Curso.CursoNombre)
                .ToListAsync();

            return _mapper.Map<IEnumerable<InscripcionDto>>(inscripciones);
        }

        public async Task<IEnumerable<InscripcionDto>> GetByCursoIdAsync(int cursoId, int? anio)
        {
            var query = QueryInscripciones()
                .AsNoTracking()
                .Where(i => i.CursoId == cursoId);

            if (anio.HasValue)
            {
                query = query.Where(i => i.Anio == anio.Value);
            }

            var inscripciones = await query
                .OrderBy(i => i.Anio)
                .ThenBy(i => i.Alumno.Apellidos)
                .ThenBy(i => i.Alumno.Nombres)
                .ToListAsync();

            return _mapper.Map<IEnumerable<InscripcionDto>>(inscripciones);
        }

        public async Task<bool> AnularAsync(int id)
        {
            var inscripcion = await _context.Inscripciones.FirstOrDefaultAsync(i => i.Id == id);
            if (inscripcion == null)
            {
                return false;
            }

            inscripcion.Estado = EstadoInscripcion.Anulada;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateEstadoAsync(int id, EstadoInscripcion estado)
        {
            if (!Enum.IsDefined(typeof(EstadoInscripcion), estado))
            {
                throw new InvalidOperationException("El estado de inscripción es inválido.");
            }

            var inscripcion = await _context.Inscripciones.FirstOrDefaultAsync(i => i.Id == id);
            if (inscripcion == null)
            {
                return false;
            }

            inscripcion.Estado = estado;
            await _context.SaveChangesAsync();
            return true;
        }

        private IQueryable<Inscripcion> QueryInscripciones()
        {
            return _context.Inscripciones
                .Include(i => i.Alumno)
                .Include(i => i.Curso);
        }

        private async Task ValidateCreateAsync(CreateInscripcionRequest request)
        {
            if (request.Anio < 1900 || request.Anio > 3000)
            {
                throw new InvalidOperationException("El año de inscripción es inválido.");
            }

            var alumnoExists = await _context.Alumnos.AnyAsync(a => a.Id == request.AlumnoId);
            if (!alumnoExists)
            {
                throw new InvalidOperationException("El alumno indicado no existe.");
            }

            var cursoExists = await _context.Cursos.AnyAsync(c => c.Id == request.CursoId);
            if (!cursoExists)
            {
                throw new InvalidOperationException("El curso indicado no existe.");
            }

            var duplicate = await _context.Inscripciones.AnyAsync(i =>
                i.AlumnoId == request.AlumnoId
                && i.CursoId == request.CursoId
                && i.Anio == request.Anio);

            if (duplicate)
            {
                throw new InvalidOperationException("Ya existe una inscripción para el mismo alumno, curso y año.");
            }
        }
    }
}
