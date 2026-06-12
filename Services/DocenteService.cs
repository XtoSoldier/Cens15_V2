using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.DocentesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class DocenteService : IDocenteService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public DocenteService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<DocenteDto>> GetAllAsync()
        {
            var items = await QueryDocentes()
                .AsNoTracking()
                .OrderBy(d => d.Apellidos)
                .ThenBy(d => d.Nombres)
                .ToListAsync();

            return _mapper.Map<IEnumerable<DocenteDto>>(items);
        }

        public async Task<DocenteDto?> GetByIdAsync(int id)
        {
            var item = await QueryDocentes()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            return item == null ? null : _mapper.Map<DocenteDto>(item);
        }

        private IQueryable<Docente> QueryDocentes()
        {
            return _context.Docentes
                .Include(d => d.Materias)
                    .ThenInclude(dm => dm.Materia)
                        .ThenInclude(m => m.Curso);
        }

        public async Task<DocenteDto> CreateAsync(CreateDocenteRequest request)
        {
            var nombres = request.Nombres.Trim();
            var apellidos = request.Apellidos.Trim();
            var email = request.Email.Trim();

            await ValidateRulesAsync(nombres, apellidos, email, request.UserId);

            var item = new Docente
            {
                Nombres = nombres,
                Apellidos = apellidos,
                Email = email,
                UserId = request.UserId
            };

            _context.Docentes.Add(item);
            await _context.SaveChangesAsync();

            return _mapper.Map<DocenteDto>(item);
        }

        public async Task<bool> UpdateAsync(int id, UpdateDocenteRequest request)
        {
            var item = await _context.Docentes.FirstOrDefaultAsync(d => d.Id == id);
            if (item == null)
            {
                return false;
            }

            var nombres = request.Nombres.Trim();
            var apellidos = request.Apellidos.Trim();
            var email = request.Email.Trim();

            await ValidateRulesAsync(nombres, apellidos, email, request.UserId, id);

            item.Nombres = nombres;
            item.Apellidos = apellidos;
            item.Email = email;
            item.UserId = request.UserId;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var item = await _context.Docentes.FirstOrDefaultAsync(d => d.Id == id);
            if (item == null)
            {
                return false;
            }

            _context.Docentes.Remove(item);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<DocenteDto?> GetByUserIdAsync(Guid userId)
        {
            var item = await QueryDocentes()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == userId);

            return item == null ? null : _mapper.Map<DocenteDto>(item);
        }

        public async Task<IEnumerable<DocenteMateriaConAlumnosDto>> GetMateriasConAlumnosAsync(int docenteId)
        {
            var docente = await _context.Docentes
                .Include(d => d.Materias)
                    .ThenInclude(dm => dm.Materia)
                        .ThenInclude(m => m.Curso)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == docenteId);

            if (docente == null) return Enumerable.Empty<DocenteMateriaConAlumnosDto>();

            var materiaIds = docente.Materias.Select(m => m.MateriaId).ToList();

            var cursadas = await _context.CursadasMaterias
                .Include(cm => cm.Inscripcion)
                    .ThenInclude(i => i.Alumno)
                .Include(cm => cm.Calificacion)
                .Where(cm => materiaIds.Contains(cm.MateriaId))
                .AsNoTracking()
                .ToListAsync();

            var result = new List<DocenteMateriaConAlumnosDto>();

            foreach (var dm in docente.Materias)
            {
                var materia = dm.Materia;
                var cursadasDeMateria = cursadas.Where(cm => cm.MateriaId == materia.Id).ToList();

                if (cursadasDeMateria.Count == 0) continue;

                var alumnos = cursadasDeMateria.Select(cm =>
                {
                    var cal = cm.Calificacion;
                    return new AlumnoCalificacionSimpleDto
                    {
                        AlumnoId = cm.Inscripcion.AlumnoId,
                        AlumnoNombre = $"{cm.Inscripcion.Alumno.Apellidos}, {cm.Inscripcion.Alumno.Nombres}",
                        CursadaMateriaId = cm.Id,
                        CalificacionId = cal?.Id,
                        C1Promedio = cal?.C1Promedio,
                        C2Promedio = cal?.C2Promedio,
                        PromedioAnual = cal?.PromedioAnual,
                        RecuperacionDiciembre = cal?.RecuperacionDiciembre,
                        RecuperacionMarzo = cal?.RecuperacionMarzo,
                        CalificacionFinal = cal?.CalificacionFinal,
                        Estado = (int)(cal?.Estado ?? 0)
                    };
                }).OrderBy(a => a.AlumnoNombre).ToList();

                var anio = cursadasDeMateria
                    .Select(cm => cm.Inscripcion.Anio)
                    .FirstOrDefault();

                result.Add(new DocenteMateriaConAlumnosDto
                {
                    MateriaId = materia.Id,
                    MateriaNombre = materia.Nombre,
                    CursoId = materia.CursoId,
                    CursoNombre = materia.Curso.CursoNombre,
                    Division = materia.Curso.Division,
                    CursoLabel = $"{materia.Curso.CursoNombre} {materia.Curso.Division}".Trim(),
                    Anio = anio,
                    Alumnos = alumnos
                });
            }

            return result.OrderBy(r => r.CursoNombre).ThenBy(r => r.MateriaNombre);
        }

        private async Task ValidateRulesAsync(string nombres, string apellidos, string email, Guid? userId, int? id = null)
        {
            if (string.IsNullOrWhiteSpace(nombres))
            {
                throw new InvalidOperationException("El nombre del docente es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(apellidos))
            {
                throw new InvalidOperationException("El apellido del docente es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("El email del docente es obligatorio.");
            }

            var emailExists = await _context.Docentes.AnyAsync(d =>
                (!id.HasValue || d.Id != id.Value) && d.Email.ToLower() == email.ToLower());
            if (emailExists)
            {
                throw new InvalidOperationException("Ya existe un docente con ese email.");
            }

            if (!userId.HasValue)
            {
                return;
            }

            var userExists = await _context.Users.AnyAsync(u => u.Id == userId.Value);
            if (!userExists)
            {
                throw new InvalidOperationException("El usuario indicado no existe.");
            }

            var userInUse = await _context.Docentes.AnyAsync(d =>
                (!id.HasValue || d.Id != id.Value) && d.UserId == userId.Value);
            if (userInUse)
            {
                throw new InvalidOperationException("El usuario indicado ya está vinculado a otro docente.");
            }
        }
    }
}
