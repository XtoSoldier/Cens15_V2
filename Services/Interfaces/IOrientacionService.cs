using CENS15_V2.Models.DTOs.OrientacionesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IOrientacionService
    {
        Task<IEnumerable<OrientacionDto>> GetAllAsync();
        Task<OrientacionDto?> GetByIdAsync(int id);
        Task<OrientacionDto> CreateAsync(CreateOrientacionRequest request);
        Task<bool> UpdateAsync(int id, UpdateOrientacionRequest request);
        Task<bool> DeleteAsync(int id);
    }
}
