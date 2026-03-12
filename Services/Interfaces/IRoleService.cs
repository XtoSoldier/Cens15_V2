using CENS15_V2.Models.DTOs.ResponsibilitiesDTOs;
using CENS15_V2.Models.DTOs.RolesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IRoleService
    {
        Task<IEnumerable<RoleDto>> GetAllAsync();
        Task<RoleDto?> GetByIdAsync(Guid id);
        Task<RoleDto> CreateAsync(CreateRoleRequest request);
        Task<bool> UpdateAsync(Guid id, UpdateRoleRequest request);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> AssignResponsibilitiesAsync(Guid roleId, AssignResponsibilitiesRequest request);
        Task<IEnumerable<ResponsibilityDto>> GetResponsibilitiesAsync(Guid roleId);
    }
}
