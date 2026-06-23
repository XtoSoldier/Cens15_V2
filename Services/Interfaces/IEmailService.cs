using CENS15_V2.Models;

namespace CENS15_V2.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendInitialAccessAsync(Auth auth, string temporaryPassword);
        Task SendPasswordResetCodeAsync(Auth auth, string verificationCode);
    }
}
