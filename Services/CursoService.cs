using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CursosDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class CursoService : ICursoService
    {
        private static readonly HashSet<string> CursosValidos = new() { "1°", "2°", "3°" };
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public CursoService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<CursoDto>> GetAllAsync()
        {
            var cursos = await QueryCursos().AsNoTracking().ToListAsync();
            return _mapper.Map<IEnumerable<CursoDto>>(cursos);
        }

        public async Task<CursoDto?> GetByIdAsync(int id)
        {
            var curso = await QueryCursos().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            return curso == null ? null : _mapper.Map<CursoDto>(curso);
        }

        public async Task<CursoDto> CreateAsync(CreateCursoRequest request)
        {
            var (curso, division) = NormalizeAndValidate(request.Curso, request.Division);
            await ValidateRulesAsync(curso, division, request.IdOrientacion, request.IdAnexo);

            var entity = new Curso
            {
                CursoNombre = curso,
                Division = division,
                OrientacionId = request.IdOrientacion,
                AnexoId = request.IdAnexo,
                Semipresencial = request.Semipresencial
            };

            _context.Cursos.Add(entity);
            await _context.SaveChangesAsync();

            var created = await QueryCursos().AsNoTracking().FirstAsync(c => c.Id == entity.Id);
            return _mapper.Map<CursoDto>(created);
        }

        public async Task<bool> UpdateAsync(int id, UpdateCursoRequest request)
        {
            var entity = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == id);
            if (entity == null)
            {
                return false;
            }

            var (curso, division) = NormalizeAndValidate(request.Curso, request.Division);
            await ValidateRulesAsync(curso, division, request.IdOrientacion, request.IdAnexo, id);

            entity.CursoNombre = curso;
            entity.Division = division;
            entity.OrientacionId = request.IdOrientacion;
            entity.AnexoId = request.IdAnexo;
            entity.Semipresencial = request.Semipresencial;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == id);
            if (entity == null)
            {
                return false;
            }

            _context.Cursos.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        private IQueryable<Curso> QueryCursos()
        {
            return _context.Cursos
                .Include(c => c.Orientacion)
                .Include(c => c.Anexo);
        }

        private static (string curso, string division) NormalizeAndValidate(string cursoInput, string divisionInput)
        {
            var curso = cursoInput.Trim();
            var division = divisionInput.Trim().ToUpperInvariant();

            if (!CursosValidos.Contains(curso))
            {
                throw new InvalidOperationException("El curso solo puede ser 1°, 2° o 3°.");
            }

            if (division.Length != 1 || division[0] < 'A' || division[0] > 'K')
            {
                throw new InvalidOperationException("La división solo puede ser una letra mayúscula entre A y K.");
            }

            return (curso, division);
        }

        private async Task ValidateRulesAsync(string curso, string division, int orientacionId, int anexoId, int? id = null)
        {
            var orientacionExists = await _context.Orientaciones.AnyAsync(o => o.Id == orientacionId);
            if (!orientacionExists)
            {
                throw new InvalidOperationException("La orientación indicada no existe.");
            }

            var anexoExists = await _context.Anexos.AnyAsync(a => a.Id == anexoId);
            if (!anexoExists)
            {
                throw new InvalidOperationException("El anexo indicado no existe.");
            }

            var duplicated = await _context.Cursos.AnyAsync(c =>
                (!id.HasValue || c.Id != id.Value) &&
                c.CursoNombre == curso &&
                c.Division == division);

            if (duplicated)
            {
                throw new InvalidOperationException("Ya existe un curso con el mismo año y división.");
            }
        }
    }
}
