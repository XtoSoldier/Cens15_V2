namespace CENS15_V2.Models.DTOs.AuthDTOs
{
    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public bool MustChangePassword { get; set; }
    }
}
