using System.ComponentModel.DataAnnotations;

namespace CENS15_V2.Models.DTOs.AuthDTOs
{
    public class ChangePasswordRequest
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        public string NewPassword { get; set; } = string.Empty;
    }
}
