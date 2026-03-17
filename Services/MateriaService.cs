using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.MateriasDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

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
            await ValidateRulesAsync(nombre, request.CursoId, request.DocenteId);

            var materia = new Materia
            {
                Nombre = nombre,
                CursoId = request.CursoId,
                DocenteId = request.DocenteId
            };

            _context.Materias.Add(materia);
            await _context.SaveChangesAsync();

            var created = await QueryMaterias()
                .AsNoTracking()
                .FirstAsync(m => m.Id == materia.Id);

            return _mapper.Map<MateriaDto>(created);
        }

        public async Task<bool> UpdateAsync(int id, UpdateMateriaRequest request)
        {
            var materia = await _context.Materias.FirstOrDefaultAsync(m => m.Id == id);
            if (materia == null)
            {
                return false;
            }

            var nombre = request.Nombre.Trim();
            await ValidateRulesAsync(nombre, request.CursoId, request.DocenteId, id);

            materia.Nombre = nombre;
            materia.CursoId = request.CursoId;
            materia.DocenteId = request.DocenteId;

            await _context.SaveChangesAsync();
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
                .Include(m => m.Docente);
        }

        private async Task ValidateRulesAsync(string nombre, int cursoId, int docenteId, int? id = null)
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

            var docenteExists = await _context.Docentes.AnyAsync(d => d.Id == docenteId);
            if (!docenteExists)
            {
                throw new InvalidOperationException("El docente indicado no existe.");
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
