using System.Net.Http.Json;
using CENS15_V2.Models;
using CENS15_V2.Services.Interfaces;

namespace CENS15_V2.Services
{
    public class EmailJsService : IEmailService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public EmailJsService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task SendInitialAccessAsync(Auth auth, string temporaryPassword)
        {
            await SendTemplateAsync(auth, temporaryPassword: temporaryPassword, verificationCode: null);
        }

        public async Task SendPasswordResetCodeAsync(Auth auth, string verificationCode)
        {
            await SendTemplateAsync(auth, temporaryPassword: null, verificationCode: verificationCode);
        }

        private async Task SendTemplateAsync(Auth auth, string? temporaryPassword, string? verificationCode)
        {
            var emailJs = _configuration.GetSection("EmailJs");
            var serviceId = emailJs["ServiceId"];
            var templateId = emailJs["TemplateId"];
            var publicKey = emailJs["PublicKey"];
            var privateKey = emailJs["PrivateKey"];

            if (string.IsNullOrWhiteSpace(serviceId) ||
                string.IsNullOrWhiteSpace(templateId) ||
                string.IsNullOrWhiteSpace(publicKey))
            {
                throw new InvalidOperationException("EmailJS no está configurado.");
            }

            var userName = $"{auth.User.FirstName} {auth.User.LastName}".Trim();
            var accessCode = temporaryPassword ?? verificationCode ?? string.Empty;
            var payload = new
            {
                service_id = serviceId,
                template_id = templateId,
                user_id = publicKey,
                accessToken = privateKey,
                template_params = new
                {
                    to_email = auth.Email,
                    email = auth.Email,
                    user_email = auth.Email,
                    recipient_email = auth.Email,
                    to_name = string.IsNullOrWhiteSpace(userName) ? auth.Email : userName,
                    name = string.IsNullOrWhiteSpace(userName) ? auth.Email : userName,
                    temporary_password = temporaryPassword ?? string.Empty,
                    password = temporaryPassword ?? string.Empty,
                    verification_code = verificationCode ?? string.Empty,
                    code = verificationCode ?? string.Empty,
                    access_code = accessCode,
                    app_name = "CENS N°15"
                }
            };

            var response = await _httpClient.PostAsJsonAsync("https://api.emailjs.com/api/v1.0/email/send", payload);
            if (!response.IsSuccessStatusCode)
            {
                var details = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"No se pudo enviar el correo: {details}");
            }
        }
    }
}
