using CENS15_V2.Entities;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;

namespace CENS15_V2.Services.Interfaces
{
    public interface ITokenService
    {
        AuthResponse CreateToken(Auth auth);
    }
}
