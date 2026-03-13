using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.TiposDocumentoAlumnoDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class TipoDocumentoAlumnoService : ITipoDocumentoAlumnoService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public TipoDocumentoAlumnoService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<TipoDocumentoAlumnoDto>> GetAllAsync()
        {
            var items = await _context.TiposDocumentoAlumno
                .AsNoTracking()
                .OrderBy(t => t.Nombre)
                .ToListAsync();

            return _mapper.Map<IEnumerable<TipoDocumentoAlumnoDto>>(items);
        }

        public async Task<TipoDocumentoAlumnoDto?> GetByIdAsync(int id)
        {
            var item = await _context.TiposDocumentoAlumno
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            return item == null ? null : _mapper.Map<TipoDocumentoAlumnoDto>(item);
        }

        public async Task<TipoDocumentoAlumnoDto> CreateAsync(CreateTipoDocumentoAlumnoRequest request)
        {
            var nombre = request.Nombre.Trim();
            await ValidateNombreDisponibleAsync(nombre);

            var item = new TipoDocumentoAlumno
            {
                Nombre = nombre
            };

            _context.TiposDocumentoAlumno.Add(item);
            await _context.SaveChangesAsync();

            return _mapper.Map<TipoDocumentoAlumnoDto>(item);
        }

        public async Task<bool> UpdateAsync(int id, UpdateTipoDocumentoAlumnoRequest request)
        {
            var item = await _context.TiposDocumentoAlumno.FirstOrDefaultAsync(t => t.Id == id);
            if (item == null)
            {
                return false;
            }

            var nombre = request.Nombre.Trim();
            var exists = await _context.TiposDocumentoAlumno.AnyAsync(t => t.Id != id && t.Nombre.ToLower() == nombre.ToLower());
            if (exists)
            {
                throw new InvalidOperationException("Ya existe un tipo de documento con ese nombre.");
            }

            item.Nombre = nombre;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var item = await _context.TiposDocumentoAlumno.FirstOrDefaultAsync(t => t.Id == id);
            if (item == null)
            {
                return false;
            }

            var inUse = await _context.AlumnoDocumentos.AnyAsync(d => d.TipoDocumentoAlumnoId == id);
            if (inUse)
            {
                throw new InvalidOperationException("No se puede eliminar el tipo de documento porque está asociado a documentos de alumnos.");
            }

            _context.TiposDocumentoAlumno.Remove(item);
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task ValidateNombreDisponibleAsync(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new InvalidOperationException("El nombre es obligatorio.");
            }

            var exists = await _context.TiposDocumentoAlumno.AnyAsync(t => t.Nombre.ToLower() == nombre.ToLower());
            if (exists)
            {
                throw new InvalidOperationException("Ya existe un tipo de documento con ese nombre.");
            }
        }
    }
}
