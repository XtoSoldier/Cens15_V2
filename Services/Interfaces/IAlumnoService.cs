using CENS15_V2.Models.DTOs.AlumnosDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IAlumnoService
    {
        Task<IEnumerable<AlumnoDto>> GetAllAsync(bool includeDeleted = false);
        Task<AlumnoDto?> GetByIdAsync(int id);
        Task<AlumnoDto> CreateAsync(CreateAlumnoRequest request);
        Task<bool> UpdateAsync(int id, UpdateAlumnoRequest request);
        Task<AlumnoDeletionInfoDto?> GetDeletionInfoAsync(int id);
        Task<bool> DeleteAsync(int id);
        Task<bool> RestoreAsync(int id);
    }
}
