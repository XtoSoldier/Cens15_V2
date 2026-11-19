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
            var email = NormalizeEmail(request.Email);

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
            var email = NormalizeEmail(request.Email);

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
            return await GetMateriasConAlumnosAsync(docenteId, onlyActivas: true);
        }

        public async Task<IEnumerable<DocenteMateriaConAlumnosDto>> GetMateriasActivasConAlumnosByUserIdAsync(Guid userId)
        {
            var docenteId = await _context.Docentes
                .Where(d => d.UserId == userId)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync();

            if (!docenteId.HasValue)
            {
                return Enumerable.Empty<DocenteMateriaConAlumnosDto>();
            }

            return await GetMateriasConAlumnosAsync(docenteId.Value, onlyActivas: true);
        }

        public Task<IEnumerable<DocenteMateriaConAlumnosDto>> GetMateriasParaCalificarAsync() => BuildPlanillasAsync(null);

        private async Task<IEnumerable<DocenteMateriaConAlumnosDto>> GetMateriasConAlumnosAsync(int docenteId, bool onlyActivas)
        {
            var materiaIds = await _context.MateriaDocentes.Where(md => md.DocenteId == docenteId
                && (!onlyActivas || md.Activo)).Select(md => md.MateriaId).ToArrayAsync();
            return await BuildPlanillasAsync(materiaIds);
        }

        private async Task<IEnumerable<DocenteMateriaConAlumnosDto>> BuildPlanillasAsync(int[]? materiaIds)
        {
            var materias = await _context.Materias.Include(m => m.Curso).AsNoTracking()
                .Where(m => materiaIds == null || materiaIds.Contains(m.Id)).ToListAsync();
            var cursoIds = materias.Select(m => m.CursoId).Distinct().ToArray();
            var inscripciones = await _context.Inscripciones.AsNoTracking().Include(i => i.Alumno)
                .Include(i => i.CursadasMaterias).ThenInclude(cm => cm.Calificacion)
                .Where(i => cursoIds.Contains(i.CursoId)).ToListAsync();

            var result = new List<DocenteMateriaConAlumnosDto>();

            foreach (var materia in materias)
            {
                var inscripcionesDeCurso = inscripciones.Where(i => i.CursoId == materia.CursoId).ToList();
                var anios = inscripcionesDeCurso.Select(i => i.Anio).Distinct().DefaultIfEmpty(DateTime.Today.Year);
                foreach (var anio in anios)
                {
                    var alumnos = inscripcionesDeCurso.Where(i => i.Anio == anio).Select(i =>
                    {
                        var cm = i.CursadasMaterias.SingleOrDefault(cm => cm.MateriaId == materia.Id);
                        var cal = cm?.Calificacion;
                        return new AlumnoCalificacionSimpleDto
                        {
                            AlumnoId = i.AlumnoId,
                            InscripcionId = i.Id,
                            AlumnoNombre = $"{i.Alumno.Apellidos}, {i.Alumno.Nombres}",
                            CursadaMateriaId = cm?.Id ?? 0,
                            CalificacionId = cal?.Id,
                            C1Nota1 = cal?.C1Nota1,
                            C1Nota2 = cal?.C1Nota2,
                            C1Nota3 = cal?.C1Nota3,
                            C2Nota1 = cal?.C2Nota1,
                            C2Nota2 = cal?.C2Nota2,
                            C2Nota3 = cal?.C2Nota3,
                            C1Condicion = cal?.C1Condicion ?? CondicionNota.Numerica,
                            C2Condicion = cal?.C2Condicion ?? CondicionNota.Numerica,
                            DiciembreCondicion = cal?.DiciembreCondicion ?? CondicionNota.Numerica,
                            MarzoCondicion = cal?.MarzoCondicion ?? CondicionNota.Numerica,
                            RecuperacionExcepcional = cal?.RecuperacionExcepcional,
                            C1Promedio = cal?.C1Promedio,
                            C2Promedio = cal?.C2Promedio,
                            PromedioAnual = cal?.PromedioAnual,
                            RecuperacionDiciembre = cal?.RecuperacionDiciembre,
                            RecuperacionMarzo = cal?.RecuperacionMarzo,
                            CalificacionFinal = cal?.CalificacionFinal,
                            Estado = (int)(cal?.Estado ?? 0),
                            DisposicionEquivalencia = cal?.DisposicionEquivalencia,
                            DesdePase = cal?.DesdePase ?? false
                        };
                    }).OrderBy(a => a.AlumnoNombre).ToList();

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
            }

            return result.OrderBy(r => r.CursoNombre).ThenBy(r => r.Division).ThenBy(r => r.MateriaNombre).ThenByDescending(r => r.Anio);
        }

        private async Task ValidateRulesAsync(string nombres, string apellidos, string? email, Guid? userId, int? id = null)
        {
            if (string.IsNullOrWhiteSpace(nombres))
            {
                throw new InvalidOperationException("El nombre del docente es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(apellidos))
            {
                throw new InvalidOperationException("El apellido del docente es obligatorio.");
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailExists = await _context.Docentes.AnyAsync(d =>
                    (!id.HasValue || d.Id != id.Value) && d.Email != null && d.Email.ToLower() == email.ToLower());
                if (emailExists)
                {
                    throw new InvalidOperationException("Ya existe un docente con ese email.");
                }
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

        private static string? NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        }
    }
}
