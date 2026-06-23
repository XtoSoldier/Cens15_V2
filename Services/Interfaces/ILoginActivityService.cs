using CENS15_V2.Models.DTOs.LoginActivitiesDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface ILoginActivityService
    {
        Task<IEnumerable<LoginActivityDto>> GetAllAsync();
        Task RegisterAsync(CreateLoginActivityRequest request, string? ipAddress);
    }
}
