namespace CENS15_V2.Models.DTOs.RolesDTOs
{
    public class UpdateRoleRequest
    {
        public string Name { get; set; }
        public ICollection<Guid>? ResponsibilityIds { get; set; }
    }
}
