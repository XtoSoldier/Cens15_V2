using System.ComponentModel.DataAnnotations;

namespace CENS15_V2.Models.DTOs.AuthDTOs
{
    public class InitialAccessRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
