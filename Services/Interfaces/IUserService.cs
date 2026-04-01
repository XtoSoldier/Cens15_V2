using CENS15_V2.Models.DTOs.UsersDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IUserService
    {
        Task<IEnumerable<UserDTO>> GetAllAsync();
        Task<UserDTO?> GetByIdAsync(Guid id);
        Task<UserDTO> CreateAsync(CreateUserRequest request);
        Task<bool> UpdateAsync(Guid id, UpdateUserRequest request);
        Task<bool> UpdateStatusAsync(Guid id, UpdateUserStatusRequest request);
        Task<bool> DeleteAsync(Guid id);
    }
}
