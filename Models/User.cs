using System.Data;
using CENS15_V2.Models;

namespace CENS15_V2.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string?  Image { get; set; }
        public bool Status { get; set; }

        public Guid RoleId { get; set; }
        public Role Role { get; set; }

        public Auth Auth { get; set; }
    }
}
