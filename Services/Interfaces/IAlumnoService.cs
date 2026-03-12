using CENS15_V2.Models.DTOs.AlumnosDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IAlumnoService
    {
        Task<IEnumerable<AlumnoDto>> GetAllAsync();
        Task<AlumnoDto?> GetByIdAsync(int id);
        Task<AlumnoDto> CreateAsync(CreateAlumnoRequest request);
        Task<bool> UpdateAsync(int id, UpdateAlumnoRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
