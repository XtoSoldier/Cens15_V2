using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CursadasMateriasDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class CursadaMateriaService : ICursadaMateriaService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public CursadaMateriaService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<CursadaMateriaDto> CreateAsync(CreateCursadaMateriaRequest request)
        {
            var materia = await ValidateAndGetMateriaAsync(request.InscripcionId, request.MateriaId);

            var entity = new CursadaMateria
            {
                InscripcionId = request.InscripcionId,
                MateriaId = request.MateriaId,
                MateriaNombre = materia.Nombre
            };

            _context.CursadasMaterias.Add(entity);
            await _context.SaveChangesAsync();

            var created = await _context.CursadasMaterias
                .AsNoTracking()
                .FirstAsync(cm => cm.Id == entity.Id);

            return _mapper.Map<CursadaMateriaDto>(created);
        }

        public async Task<CursadaMateriaDto?> GetByIdAsync(int id)
        {
            var entity = await _context.CursadasMaterias
                .AsNoTracking()
                .FirstOrDefaultAsync(cm => cm.Id == id);

            return entity == null ? null : _mapper.Map<CursadaMateriaDto>(entity);
        }

        public async Task<IEnumerable<CursadaMateriaDto>> GetByInscripcionIdAsync(int inscripcionId)
        {
            var items = await _context.CursadasMaterias
                .AsNoTracking()
                .Where(cm => cm.InscripcionId == inscripcionId)
                .OrderBy(cm => cm.MateriaNombre)
                .ToListAsync();

            return _mapper.Map<IEnumerable<CursadaMateriaDto>>(items);
        }

        public async Task<bool> UpdateAsync(int id, UpdateCursadaMateriaRequest request)
        {
            var entity = await _context.CursadasMaterias
                .Include(cm => cm.Calificacion)
                .FirstOrDefaultAsync(cm => cm.Id == id);

            if (entity == null)
            {
                return false;
            }

            var materia = await ValidateAndGetMateriaAsync(request.InscripcionId, request.MateriaId, id);

            entity.InscripcionId = request.InscripcionId;
            entity.MateriaId = request.MateriaId;
            entity.MateriaNombre = materia.Nombre;

            if (entity.Calificacion != null)
            {
                entity.Calificacion.MateriaNombre = materia.Nombre;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        private async Task<Materia> ValidateAndGetMateriaAsync(int inscripcionId, int materiaId, int? id = null)
        {
            var inscripcion = await _context.Inscripciones
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == inscripcionId);
            if (inscripcion == null)
            {
                throw new InvalidOperationException("La inscripción indicada no existe.");
            }

            var materia = await _context.Materias
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == materiaId);
            if (materia == null)
            {
                throw new InvalidOperationException("La materia indicada no existe.");
            }

            if (materia.CursoId != inscripcion.CursoId)
            {
                throw new InvalidOperationException("La materia indicada no pertenece al curso de la inscripción.");
            }

            var duplicate = await _context.CursadasMaterias.AnyAsync(cm =>
                (!id.HasValue || cm.Id != id.Value)
                && cm.InscripcionId == inscripcionId
                && cm.MateriaId == materiaId);

            if (duplicate)
            {
                throw new InvalidOperationException("Ya existe una cursada para la misma inscripción y materia.");
            }

            return materia;
        }
    }
}
