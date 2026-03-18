using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.InscripcionesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IInscripcionService
    {
        Task<InscripcionDto> CreateAsync(CreateInscripcionRequest request);
        Task<InscripcionDto?> GetByIdAsync(int id);
        Task<IEnumerable<InscripcionDto>> GetByAlumnoIdAsync(int alumnoId);
        Task<IEnumerable<InscripcionDto>> GetByCursoIdAsync(int cursoId, int? anio);
        Task<bool> UpdateAsync(int id, UpdateInscripcionRequest request);
        Task<bool> AnularAsync(int id);
        Task<bool> UpdateEstadoAsync(int id, EstadoInscripcion estado);
    }
}
