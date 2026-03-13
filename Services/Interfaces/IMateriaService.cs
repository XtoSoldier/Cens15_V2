using CENS15_V2.Models.DTOs.MateriasDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IMateriaService
    {
        Task<IEnumerable<MateriaDto>> GetAllAsync();
        Task<MateriaDto?> GetByIdAsync(int id);
        Task<MateriaDto> CreateAsync(CreateMateriaRequest request);
        Task<bool> UpdateAsync(int id, UpdateMateriaRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
