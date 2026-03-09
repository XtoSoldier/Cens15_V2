using CENS15_V2.Entities;

namespace CENS15_V2.Models
{
    public class Auth
    {
        public Guid Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public Token Token { get; set; }
        public User User { get; set; }
    }
}
