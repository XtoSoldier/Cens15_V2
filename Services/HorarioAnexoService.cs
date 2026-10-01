using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.HorariosAnexosDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CENS15_V2.Services
{
    public class HorarioAnexoService : IHorarioAnexoService
    {
        private readonly AppDbContext _context;

        public HorarioAnexoService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<HorarioAnexoDto>?> GetByAnexoAsync(int anexoId)
        {
            var exists = await _context.Anexos.AnyAsync(a => a.Id == anexoId);
            if (!exists) return null;

            return await _context.HorariosAnexo
                .Where(h => h.AnexoId == anexoId)
                .OrderBy(h => h.DiaSemana)
                .ThenBy(h => h.Orden)
                .Select(h => new HorarioAnexoDto
                {
                    Id = h.Id,
                    DiaSemana = h.DiaSemana,
                    Orden = h.Orden,
                    Tipo = h.Tipo,
                    HoraInicio = h.HoraInicio,
                    HoraFin = h.HoraFin
                })
                .ToListAsync();
        }

        public async Task<bool> ReplaceByAnexoAsync(int anexoId, GuardarHorariosAnexoRequest request)
        {
            var exists = await _context.Anexos.AnyAsync(a => a.Id == anexoId);
            if (!exists) return false;

            if (request.Bloques == null)
                throw new InvalidOperationException("Debes indicar los bloques de la grilla.");
            ValidateBlocks(request.Bloques);

            var current = await _context.HorariosAnexo
                .Where(h => h.AnexoId == anexoId)
                .ToListAsync();
            var assigned = await _context.HorariosMateria
                .Where(h => h.HorarioAnexo.AnexoId == anexoId)
                .Select(h => h.HorarioAnexoId)
                .Distinct()
                .ToListAsync();
            var incoming = request.Bloques.ToDictionary(b => (b.DiaSemana, b.Orden));
            foreach (var block in current.Where(h => assigned.Contains(h.Id)))
            {
                if (!incoming.TryGetValue((block.DiaSemana, block.Orden), out var next)
                    || next.Tipo != block.Tipo || next.HoraInicio != block.HoraInicio || next.HoraFin != block.HoraFin)
                {
                    throw new InvalidOperationException("No puedes modificar ni eliminar bloques asignados a materias. Quita primero sus asignaciones horarias.");
                }
            }

            // Keep block identities so saving the annex grid never erases subject assignments.
            foreach (var block in current)
            {
                if (!incoming.ContainsKey((block.DiaSemana, block.Orden)))
                    _context.HorariosAnexo.Remove(block);
            }
            var existing = current.ToDictionary(b => (b.DiaSemana, b.Orden));
            foreach (var block in request.Bloques)
            {
                if (!existing.TryGetValue((block.DiaSemana, block.Orden), out var entity))
                {
                    entity = new HorarioAnexo { AnexoId = anexoId, DiaSemana = block.DiaSemana, Orden = block.Orden };
                    _context.HorariosAnexo.Add(entity);
                }
                entity.Tipo = block.Tipo;
                entity.HoraInicio = block.HoraInicio;
                entity.HoraFin = block.HoraFin;
            }
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                throw new InvalidOperationException("Un bloque acaba de ser asignado a una materia. Recarga la grilla antes de modificarlo.", ex);
            }
            return true;
        }

        private static void ValidateBlocks(IEnumerable<HorarioAnexoRequest> blocks)
        {
            foreach (var day in blocks.GroupBy(block => block.DiaSemana))
            {
                if (day.Key < DayOfWeek.Monday || day.Key > DayOfWeek.Friday)
                {
                    throw new InvalidOperationException("La grilla sólo admite bloques de lunes a viernes.");
                }

                var ordered = day.OrderBy(block => block.Orden).ToList();
                if (ordered.Select(block => block.Orden).Distinct().Count() != ordered.Count)
                {
                    throw new InvalidOperationException("Cada bloque del día debe tener un orden único.");
                }

                TimeOnly? previousEnd = null;
                foreach (var block in ordered)
                {
                    if (block.Orden < 1 || !Enum.IsDefined(block.Tipo))
                    {
                        throw new InvalidOperationException("El orden del bloque debe ser positivo y su tipo debe ser Clase o Receso.");
                    }
                    if (block.HoraFin <= block.HoraInicio)
                    {
                        throw new InvalidOperationException("La hora de fin debe ser posterior a la hora de inicio.");
                    }

                    if (previousEnd.HasValue && block.HoraInicio < previousEnd.Value)
                    {
                        throw new InvalidOperationException("Los bloques de un día no pueden superponerse.");
                    }

                    previousEnd = block.HoraFin;
                }
            }
        }
    }
}
