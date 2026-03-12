using CENS15_V2.Models.DTOs.AlumnoDocumentosDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IAlumnoDocumentoService
    {
        Task<IEnumerable<AlumnoDocumentoItemDto>> GetAllAsync(int? alumnoId = null);
        Task<AlumnoDocumentoItemDto?> GetByIdAsync(int id);
        Task<AlumnoDocumentoItemDto> CreateAsync(CreateAlumnoDocumentoItemRequest request);
        Task<bool> UpdateAsync(int id, UpdateAlumnoDocumentoItemRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
