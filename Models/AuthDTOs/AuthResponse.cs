namespace CENS15_V2.Models.DTOs.AuthDTOs
{
    public class AuthResponse
    {
        public string Token { get; set; }
        public DateTime Expiration { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }
}
