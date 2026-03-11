using AutoMapper;
using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AlumnosDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class AlumnoService : IAlumnoService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;

        public AlumnoService(AppDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IEnumerable<AlumnoDto>> GetAllAsync()
        {
            var alumnos = await QueryAlumno().AsNoTracking().ToListAsync();
            return _mapper.Map<IEnumerable<AlumnoDto>>(alumnos);
        }

        public async Task<AlumnoDto?> GetByIdAsync(int id)
        {
            var alumno = await QueryAlumno().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
            return alumno == null ? null : _mapper.Map<AlumnoDto>(alumno);
        }

        public async Task<AlumnoDto> CreateAsync(CreateAlumnoRequest request)
        {
            await ValidateTipoDocumentosAsync(request.Documentos.Select(d => d.TipoDocumentoAlumnoId));

            var alumno = _mapper.Map<Alumno>(request);
            _context.Alumnos.Add(alumno);
            await _context.SaveChangesAsync();

            var created = await QueryAlumno().AsNoTracking().FirstAsync(a => a.Id == alumno.Id);
            return _mapper.Map<AlumnoDto>(created);
        }

        public async Task<bool> UpdateAsync(int id, UpdateAlumnoRequest request)
        {
            await ValidateTipoDocumentosAsync(request.Documentos.Select(d => d.TipoDocumentoAlumnoId));

            var alumno = await QueryAlumno().FirstOrDefaultAsync(a => a.Id == id);
            if (alumno == null)
            {
                return false;
            }

            alumno.Nombres = request.Nombres;
            alumno.Apellidos = request.Apellidos;
            alumno.NumeroDocumento = request.NumeroDocumento;
            alumno.FechaNacimiento = request.FechaNacimiento;
            alumno.Domicilio = request.Domicilio;

            alumno.DatosNacimiento ??= new AlumnoNacimiento();
            alumno.DatosNacimiento.Localidad = request.DatosNacimiento.Localidad;
            alumno.DatosNacimiento.Provincia = request.DatosNacimiento.Provincia;
            alumno.DatosNacimiento.Pais = request.DatosNacimiento.Pais;

            alumno.Contacto ??= new AlumnoContacto();
            alumno.Contacto.TelefonoAlumno = request.Contacto.TelefonoAlumno;
            alumno.Contacto.Email = request.Contacto.Email;
            alumno.Contacto.NombreEmergencia = request.Contacto.NombreEmergencia;
            alumno.Contacto.TelefonoEmergencia = request.Contacto.TelefonoEmergencia;

            _context.AlumnoDocumentos.RemoveRange(alumno.Documentos);
            alumno.Documentos = request.Documentos.Select(d => new AlumnoDocumento
            {
                TipoDocumentoAlumnoId = d.TipoDocumentoAlumnoId,
                Presentado = d.Presentado,
                ImagenUrl = d.ImagenUrl
            }).ToList();

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var alumno = await _context.Alumnos.FindAsync(id);
            if (alumno == null)
            {
                return false;
            }

            _context.Alumnos.Remove(alumno);
            await _context.SaveChangesAsync();
            return true;
        }

        private IQueryable<Alumno> QueryAlumno()
        {
            return _context.Alumnos
                .Include(a => a.DatosNacimiento)
                .Include(a => a.Contacto)
                .Include(a => a.Documentos)
                    .ThenInclude(d => d.TipoDocumentoAlumno);
        }

        private async Task ValidateTipoDocumentosAsync(IEnumerable<int> tipoDocumentoIds)
        {
            var ids = tipoDocumentoIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            var count = await _context.TiposDocumentoAlumno.CountAsync(t => ids.Contains(t.Id));
            if (count != ids.Count)
            {
                throw new InvalidOperationException("Uno o más tipos de documento no existen.");
            }
        }
    }
}
