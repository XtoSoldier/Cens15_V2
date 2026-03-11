namespace CENS15_V2.Models.DTOs.RolesDTOs
{
    public class RoleDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ICollection<Guid> ResponsibilityIds { get; set; }
    }
}
