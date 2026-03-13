using CENS15_V2.Models.DTOs.AnexosDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IAnexoService
    {
        Task<IEnumerable<AnexoDto>> GetAllAsync();
        Task<AnexoDto?> GetByIdAsync(int id);
        Task<AnexoDto> CreateAsync(CreateAnexoRequest request);
        Task<bool> UpdateAsync(int id, UpdateAnexoRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
