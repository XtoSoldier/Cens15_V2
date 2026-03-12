namespace CENS15_V2.Models
{
    public class Responsibility
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }

        public ICollection<RoleResponsibility> Roles { get; set; } = new List<RoleResponsibility>();
    }
}
