using CENS15_V2.Models.DTOs.CursosDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface ICursoService
    {
        Task<IEnumerable<CursoDto>> GetAllAsync();
        Task<CursoDto?> GetByIdAsync(int id);
        Task<CursoDto> CreateAsync(CreateCursoRequest request);
        Task<bool> UpdateAsync(int id, UpdateCursoRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
