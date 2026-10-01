using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.MateriasDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CENS15_V2.Services
{
    public class MateriaService : IMateriaService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public MateriaService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<MateriaDto>> GetAllAsync()
        {
            var materias = await QueryMaterias()
                .AsNoTracking()
                .OrderBy(m => m.Nombre)
                .ToListAsync();

            return _mapper.Map<IEnumerable<MateriaDto>>(materias);
        }

        public async Task<MateriaDto?> GetByIdAsync(int id)
        {
            var materia = await QueryMaterias()
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            return materia == null ? null : _mapper.Map<MateriaDto>(materia);
        }

        public async Task<MateriaDto> CreateAsync(CreateMateriaRequest request)
        {
            var nombre = request.Nombre.Trim();
            var docentes = request.Docentes ?? new List<CreateMateriaDocenteRequest>();
            await ValidateRulesAsync(nombre, request.CursoId, docentes);
            if (request.CargaHoraria <= 0)
                throw new InvalidOperationException("La carga horaria de una nueva materia debe ser positiva.");
            var horarioIds = request.HorarioIds ?? new List<int>();
            await ValidateScheduleAsync(request.CursoId, request.CargaHoraria, horarioIds);

            var materia = new Materia
            {
                Nombre = nombre,
                CursoId = request.CursoId,
                CargaHoraria = request.CargaHoraria,
                Horarios = horarioIds.Select(h => new HorarioMateria
                {
                    CursoId = request.CursoId,
                    HorarioAnexoId = h
                }).ToList(),
                Docentes = docentes.Select(d => new MateriaDocente
                {
                    DocenteId = d.DocenteId,
                    Rol = d.Rol.Trim(),
                    Activo = d.Activo
                }).ToList()
            };

            _context.Materias.Add(materia);
            await SaveMateriaAsync();

            var created = await QueryMaterias()
                .AsNoTracking()
                .FirstAsync(m => m.Id == materia.Id);

            return _mapper.Map<MateriaDto>(created);
        }

        public async Task<bool> UpdateAsync(int id, UpdateMateriaRequest request)
        {
            var materia = await _context.Materias
                .Include(m => m.Docentes)
                .Include(m => m.Horarios)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (materia == null)
            {
                return false;
            }

            var nombre = request.Nombre.Trim();
            var docentes = request.Docentes ?? new List<CreateMateriaDocenteRequest>();
            await ValidateRulesAsync(nombre, request.CursoId, docentes, id);
            // Omitted scheduling fields preserve assignments when editing other subject details.
            var cargaHoraria = request.CargaHoraria ?? materia.CargaHoraria;
            var horarioIds = request.HorarioIds ?? materia.Horarios.Select(h => h.HorarioAnexoId).ToList();
            await ValidateScheduleAsync(request.CursoId, cargaHoraria, horarioIds, id);

            materia.Nombre = nombre;
            materia.CursoId = request.CursoId;
            materia.CargaHoraria = cargaHoraria;
            var removed = materia.Horarios.Where(h => !horarioIds.Contains(h.HorarioAnexoId)).ToList();
            _context.HorariosMateria.RemoveRange(removed);
            foreach (var horario in removed) materia.Horarios.Remove(horario);
            foreach (var horario in materia.Horarios) horario.CursoId = request.CursoId;
            foreach (var horarioId in horarioIds.Where(h => !materia.Horarios.Any(current => current.HorarioAnexoId == h)))
            {
                materia.Horarios.Add(new HorarioMateria
                {
                    CursoId = request.CursoId,
                    HorarioAnexoId = horarioId
                });
            }

            _context.RemoveRange(materia.Docentes);
            materia.Docentes = docentes.Select(d => new MateriaDocente
            {
                MateriaId = materia.Id,
                DocenteId = d.DocenteId,
                Rol = d.Rol.Trim(),
                Activo = d.Activo
            }).ToList();

            await SaveMateriaAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var materia = await _context.Materias.FirstOrDefaultAsync(m => m.Id == id);
            if (materia == null)
            {
                return false;
            }

            _context.Materias.Remove(materia);
            await _context.SaveChangesAsync();
            return true;
        }

        private IQueryable<Materia> QueryMaterias()
        {
            return _context.Materias
                .Include(m => m.Curso)
                .Include(m => m.Horarios)
                    .ThenInclude(h => h.HorarioAnexo)
                .Include(m => m.Docentes)
                    .ThenInclude(md => md.Docente);
        }

        private async Task ValidateScheduleAsync(int cursoId, int cargaHoraria, List<int> horarioIds, int? materiaId = null)
        {
            if (cargaHoraria < 0 || (horarioIds.Count > 0 && cargaHoraria == 0))
                throw new InvalidOperationException("La carga horaria debe ser positiva para asignar horarios.");

            if (horarioIds.Distinct().Count() != horarioIds.Count)
                throw new InvalidOperationException("No se puede seleccionar la misma hora mas de una vez.");

            if (horarioIds.Count == 0) return;
            if (horarioIds.Count != cargaHoraria)
                throw new InvalidOperationException($"Debes seleccionar exactamente {cargaHoraria} horas catedra por semana.");

            var anexoId = await _context.Cursos.Where(c => c.Id == cursoId).Select(c => c.AnexoId).SingleAsync();
            var validCount = await _context.HorariosAnexo.CountAsync(h => horarioIds.Contains(h.Id)
                && h.AnexoId == anexoId && h.Tipo == TipoBloqueHorario.Clase);
            if (validCount != horarioIds.Count)
                throw new InvalidOperationException("Solo se pueden seleccionar horas de clase de la grilla del anexo del curso.");

            var occupied = await _context.HorariosMateria
                .Where(h => h.CursoId == cursoId && horarioIds.Contains(h.HorarioAnexoId)
                    && (!materiaId.HasValue || h.MateriaId != materiaId.Value))
                .Select(h => h.Materia.Nombre)
                .FirstOrDefaultAsync();
            if (occupied != null)
                throw new InvalidOperationException($"Una de las horas elegidas ya esta ocupada por {occupied} en este curso.");
        }

        private async Task SaveMateriaAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation
                && pg.ConstraintName == "IX_HorariosMateria_CursoId_HorarioAnexoId")
            {
                throw new InvalidOperationException("Una de las horas elegidas acaba de ser ocupada por otra materia del curso. Actualiza la grilla.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.ForeignKeyViolation
                && pg.ConstraintName == "FK_HorariosMateria_HorariosAnexo_HorarioAnexoId")
            {
                throw new InvalidOperationException("La grilla del anexo cambio mientras guardabas. Actualiza los horarios e intenta nuevamente.", ex);
            }
        }

        private async Task ValidateRulesAsync(string nombre, int cursoId, List<CreateMateriaDocenteRequest> docentes, int? id = null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new InvalidOperationException("El nombre de la materia es obligatorio.");
            }

            var cursoExists = await _context.Cursos.AnyAsync(c => c.Id == cursoId);
            if (!cursoExists)
            {
                throw new InvalidOperationException("El curso indicado no existe.");
            }

            if (docentes == null || docentes.Count == 0)
            {
                return;
            }

            if (docentes.Count > 2)
            {
                throw new InvalidOperationException("Solo se puede asignar un docente titular y un docente suplente por materia.");
            }

            var docentesIds = docentes.Select(d => d.DocenteId).ToList();
            if (docentesIds.Distinct().Count() != docentesIds.Count)
            {
                throw new InvalidOperationException("No se puede repetir un docente en la misma materia.");
            }

            var roles = docentes.Select(d => d.Rol.Trim()).ToList();
            if (roles.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException("El rol del docente en la materia es obligatorio.");
            }

            if (roles.Any(r => !string.Equals(r, "Titular", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r, "Suplente", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("El rol del docente debe ser Titular o Suplente.");
            }

            if (roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != roles.Count)
            {
                throw new InvalidOperationException("No se puede repetir el rol dentro de una misma materia.");
            }

            if (docentes.Count(d => d.Activo) != 1)
            {
                throw new InvalidOperationException("Debe quedar exactamente un docente activo por materia.");
            }

            var docentesExistentes = await _context.Docentes
                .CountAsync(d => docentesIds.Contains(d.Id));
            if (docentesExistentes != docentesIds.Count)
            {
                throw new InvalidOperationException("Uno o más docentes indicados no existen.");
            }

            var duplicada = await _context.Materias.AnyAsync(m =>
                (!id.HasValue || m.Id != id.Value)
                && m.CursoId == cursoId
                && m.Nombre.ToLower() == nombre.ToLower());

            if (duplicada)
            {
                throw new InvalidOperationException("Ya existe una materia con ese nombre para el curso indicado.");
            }
        }
    }
}
