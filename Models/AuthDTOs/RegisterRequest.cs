using System.ComponentModel.DataAnnotations;

namespace CENS15_V2.Models.DTOs.AuthDTOs
{
    public class RegisterRequest
    {
        [Required]
        public string Apellido { get; set; } = string.Empty;

        [Required]
        public string Nombres { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string? Image { get; set; }
    }
}
