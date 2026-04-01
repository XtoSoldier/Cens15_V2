namespace CENS15_V2.Models.DTOs.AuthDTOs
{
    public class RegisterRequest
    {
        public string Apellido { get; set; }
        public string Nombres { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string? Image {  get; set; }
    }
}
