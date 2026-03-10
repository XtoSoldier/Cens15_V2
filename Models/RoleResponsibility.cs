namespace CENS15_V2.Models
{
    public class RoleResponsibility
    {
        public Guid RoleId { get; set; }
        public Role Role { get; set; }

        public Guid ResponsibilityId { get; set; }
        public Responsibility Responsibility { get; set; }
    }
}
