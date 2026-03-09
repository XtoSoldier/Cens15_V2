using CENS15_V2.Models.DTOs.AuthDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse?> Login(LoginRequest request);
        Task<AuthResponse> Register(RegisterRequest request);
    }
}
