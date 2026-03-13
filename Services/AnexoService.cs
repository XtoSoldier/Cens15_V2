using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AnexosDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class AnexoService : IAnexoService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public AnexoService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<AnexoDto>> GetAllAsync()
        {
            var items = await _context.Anexos
                .AsNoTracking()
                .OrderBy(a => a.Nombre)
                .ToListAsync();

            return _mapper.Map<IEnumerable<AnexoDto>>(items);
        }

        public async Task<AnexoDto?> GetByIdAsync(int id)
        {
            var item = await _context.Anexos
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

            return item == null ? null : _mapper.Map<AnexoDto>(item);
        }

        public async Task<AnexoDto> CreateAsync(CreateAnexoRequest request)
        {
            var nombre = request.Nombre.Trim();
            await ValidateNombreDisponible(nombre);

            var entity = new Anexo { Nombre = nombre };
            _context.Anexos.Add(entity);
            await _context.SaveChangesAsync();

            return _mapper.Map<AnexoDto>(entity);
        }

        public async Task<bool> UpdateAsync(int id, UpdateAnexoRequest request)
        {
            var entity = await _context.Anexos.FirstOrDefaultAsync(a => a.Id == id);
            if (entity == null)
            {
                return false;
            }

            var nombre = request.Nombre.Trim();
            await ValidateNombreDisponible(nombre, id);

            entity.Nombre = nombre;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _context.Anexos.FirstOrDefaultAsync(a => a.Id == id);
            if (entity == null)
            {
                return false;
            }

            var inUse = await _context.Cursos.AnyAsync(c => c.AnexoId == id);
            if (inUse)
            {
                throw new InvalidOperationException("No se puede eliminar el anexo porque tiene cursos asociados.");
            }

            _context.Anexos.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task ValidateNombreDisponible(string nombre, int? id = null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new InvalidOperationException("El nombre del anexo es obligatorio.");
            }

            var exists = await _context.Anexos.AnyAsync(a =>
                (!id.HasValue || a.Id != id.Value) &&
                a.Nombre.ToLower() == nombre.ToLower());

            if (exists)
            {
                throw new InvalidOperationException("Ya existe un anexo con ese nombre.");
            }
        }
    }
}
