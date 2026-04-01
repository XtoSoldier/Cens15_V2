namespace CENS15_V2.Models.DTOs.UsersDTOs
{
    public class UserDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Image { get; set; }
        public bool Status { get; set; }
        public Guid RoleId { get; set; }
        public string? Role { get; set; }
        public string? Email { get; set; }
    }
}
