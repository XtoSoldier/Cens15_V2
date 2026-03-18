using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.CalificacionesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class CalificacionService : ICalificacionService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public CalificacionService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<CalificacionDto> CreateAsync(CreateCalificacionRequest request)
        {
            var materiaNombre = await ValidateAndGetMateriaNombreAsync(request.CursadaMateriaId);

            var entity = new Calificacion
            {
                CursadaMateriaId = request.CursadaMateriaId,
                MateriaNombre = materiaNombre,
                C1Nota1 = request.C1Nota1,
                C1Nota2 = request.C1Nota2,
                C1Nota3 = request.C1Nota3,
                C1Promedio = request.C1Promedio,
                C2Nota1 = request.C2Nota1,
                C2Nota2 = request.C2Nota2,
                C2Nota3 = request.C2Nota3,
                C2Promedio = request.C2Promedio,
                PromedioAnual = request.PromedioAnual,
                RecuperacionDiciembre = request.RecuperacionDiciembre,
                RecuperacionMarzo = request.RecuperacionMarzo,
                CalificacionFinal = request.CalificacionFinal,
                Estado = request.Estado
            };

            _context.Calificaciones.Add(entity);
            await _context.SaveChangesAsync();

            var created = await _context.Calificaciones
                .AsNoTracking()
                .FirstAsync(c => c.Id == entity.Id);

            return _mapper.Map<CalificacionDto>(created);
        }

        public async Task<CalificacionDto?> GetByIdAsync(int id)
        {
            var entity = await _context.Calificaciones
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            return entity == null ? null : _mapper.Map<CalificacionDto>(entity);
        }

        public async Task<CalificacionDto?> GetByCursadaMateriaIdAsync(int cursadaMateriaId)
        {
            var entity = await _context.Calificaciones
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CursadaMateriaId == cursadaMateriaId);

            return entity == null ? null : _mapper.Map<CalificacionDto>(entity);
        }

        public async Task<bool> UpdateAsync(int id, UpdateCalificacionRequest request)
        {
            var entity = await _context.Calificaciones.FirstOrDefaultAsync(c => c.Id == id);
            if (entity == null)
            {
                return false;
            }

            var materiaNombre = await ValidateAndGetMateriaNombreAsync(request.CursadaMateriaId, id);

            entity.CursadaMateriaId = request.CursadaMateriaId;
            entity.MateriaNombre = materiaNombre;
            entity.C1Nota1 = request.C1Nota1;
            entity.C1Nota2 = request.C1Nota2;
            entity.C1Nota3 = request.C1Nota3;
            entity.C1Promedio = request.C1Promedio;
            entity.C2Nota1 = request.C2Nota1;
            entity.C2Nota2 = request.C2Nota2;
            entity.C2Nota3 = request.C2Nota3;
            entity.C2Promedio = request.C2Promedio;
            entity.PromedioAnual = request.PromedioAnual;
            entity.RecuperacionDiciembre = request.RecuperacionDiciembre;
            entity.RecuperacionMarzo = request.RecuperacionMarzo;
            entity.CalificacionFinal = request.CalificacionFinal;
            entity.Estado = request.Estado;

            await _context.SaveChangesAsync();
            return true;
        }

        private async Task<string> ValidateAndGetMateriaNombreAsync(int cursadaMateriaId, int? id = null)
        {
            var cursadaMateria = await _context.CursadasMaterias
                .AsNoTracking()
                .FirstOrDefaultAsync(cm => cm.Id == cursadaMateriaId);
            if (cursadaMateria == null)
            {
                throw new InvalidOperationException("La cursada de materia indicada no existe.");
            }

            var duplicate = await _context.Calificaciones.AnyAsync(c =>
                (!id.HasValue || c.Id != id.Value)
                && c.CursadaMateriaId == cursadaMateriaId);

            if (duplicate)
            {
                throw new InvalidOperationException("Ya existe una calificación para la cursada de materia indicada.");
            }

            return string.IsNullOrWhiteSpace(cursadaMateria.MateriaNombre)
                ? await _context.Materias
                    .Where(m => m.Id == cursadaMateria.MateriaId)
                    .Select(m => m.Nombre)
                    .FirstAsync()
                : cursadaMateria.MateriaNombre;
        }
    }
}
