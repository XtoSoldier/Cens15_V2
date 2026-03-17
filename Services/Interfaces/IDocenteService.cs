using CENS15_V2.Models.DTOs.DocentesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IDocenteService
    {
        Task<IEnumerable<DocenteDto>> GetAllAsync();
        Task<DocenteDto?> GetByIdAsync(int id);
        Task<DocenteDto> CreateAsync(CreateDocenteRequest request);
        Task<bool> UpdateAsync(int id, UpdateDocenteRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
