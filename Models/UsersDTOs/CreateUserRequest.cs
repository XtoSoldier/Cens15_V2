namespace CENS15_V2.Models.DTOs.UsersDTOs
{
    public class CreateUserRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Image { get; set; }
        public bool Status { get; set; } = true;
        public Guid RoleId { get; set; }
    }
}
