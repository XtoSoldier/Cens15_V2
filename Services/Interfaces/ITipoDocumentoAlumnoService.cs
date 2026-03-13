using CENS15_V2.Models.DTOs.TiposDocumentoAlumnoDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface ITipoDocumentoAlumnoService
    {
        Task<IEnumerable<TipoDocumentoAlumnoDto>> GetAllAsync();
        Task<TipoDocumentoAlumnoDto?> GetByIdAsync(int id);
        Task<TipoDocumentoAlumnoDto> CreateAsync(CreateTipoDocumentoAlumnoRequest request);
        Task<bool> UpdateAsync(int id, UpdateTipoDocumentoAlumnoRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
