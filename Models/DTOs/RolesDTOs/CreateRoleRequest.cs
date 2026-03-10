namespace CENS15_V2.Models.DTOs.RolesDTOs
{
    public class CreateRoleRequest
    {
        public string Name { get; set; }
        public ICollection<Guid>? ResponsibilityIds { get; set; }
    }
}
