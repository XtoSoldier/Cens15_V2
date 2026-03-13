namespace CENS15_V2.Models.DTOs.RolesDTOs
{
    public class AssignResponsibilitiesRequest
    {
        public ICollection<Guid> ResponsibilityIds { get; set; } = new List<Guid>();
    }
}
