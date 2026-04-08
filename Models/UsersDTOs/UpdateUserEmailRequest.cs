using System.ComponentModel.DataAnnotations;

namespace CENS15_V2.Models.DTOs.UsersDTOs
{
    public class UpdateUserEmailRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
