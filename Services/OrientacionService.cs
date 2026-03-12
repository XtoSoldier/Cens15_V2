using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.OrientacionesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class OrientacionService : IOrientacionService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public OrientacionService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<OrientacionDto>> GetAllAsync()
        {
            var items = await _context.Orientaciones
                .AsNoTracking()
                .OrderBy(o => o.Nombre)
                .ToListAsync();

            return _mapper.Map<IEnumerable<OrientacionDto>>(items);
        }

        public async Task<OrientacionDto?> GetByIdAsync(int id)
        {
            var item = await _context.Orientaciones
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id);

            return item == null ? null : _mapper.Map<OrientacionDto>(item);
        }

        public async Task<OrientacionDto> CreateAsync(CreateOrientacionRequest request)
        {
            var nombre = request.Nombre.Trim();
            var nombreCorto = request.NombreCorto.Trim();

            await ValidateDisponibilidadAsync(nombre, nombreCorto);

            var item = new Orientacion
            {
                Nombre = nombre,
                NombreCorto = nombreCorto
            };

            _context.Orientaciones.Add(item);
            await _context.SaveChangesAsync();

            return _mapper.Map<OrientacionDto>(item);
        }

        public async Task<bool> UpdateAsync(int id, UpdateOrientacionRequest request)
        {
            var item = await _context.Orientaciones.FirstOrDefaultAsync(o => o.Id == id);
            if (item == null)
            {
                return false;
            }

            var nombre = request.Nombre.Trim();
            var nombreCorto = request.NombreCorto.Trim();

            await ValidateDisponibilidadAsync(nombre, nombreCorto, id);

            item.Nombre = nombre;
            item.NombreCorto = nombreCorto;
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var item = await _context.Orientaciones.FirstOrDefaultAsync(o => o.Id == id);
            if (item == null)
            {
                return false;
            }

            _context.Orientaciones.Remove(item);
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task ValidateDisponibilidadAsync(string nombre, string nombreCorto, int? id = null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new InvalidOperationException("El nombre de la orientación es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(nombreCorto))
            {
                throw new InvalidOperationException("El nombre corto de la orientación es obligatorio.");
            }

            var nombreExists = await _context.Orientaciones
                .AnyAsync(o => (!id.HasValue || o.Id != id.Value) && o.Nombre.ToLower() == nombre.ToLower());
            if (nombreExists)
            {
                throw new InvalidOperationException("Ya existe una orientación con ese nombre.");
            }

            var nombreCortoExists = await _context.Orientaciones
                .AnyAsync(o => (!id.HasValue || o.Id != id.Value) && o.NombreCorto.ToLower() == nombreCorto.ToLower());
            if (nombreCortoExists)
            {
                throw new InvalidOperationException("Ya existe una orientación con ese nombre corto.");
            }
        }
    }
}
