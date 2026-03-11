namespace CENS15_V2.Models
{
    public class Role
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
        public ICollection<RoleResponsibility> Responsibilities { get; set; } = new List<RoleResponsibility>();

    }
}
