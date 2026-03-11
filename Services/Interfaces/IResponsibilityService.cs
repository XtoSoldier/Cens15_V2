using CENS15_V2.Models.DTOs.ResponsibilitiesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IResponsibilityService
    {
        Task<IEnumerable<ResponsibilityDto>> GetAllAsync();
        Task<ResponsibilityDto?> GetByIdAsync(Guid id);
        Task<ResponsibilityDto> CreateAsync(CreateResponsibilityRequest request);
        Task<bool> UpdateAsync(Guid id, UpdateResponsibilityRequest request);
        Task<bool> DeleteAsync(Guid id);
    }
}
