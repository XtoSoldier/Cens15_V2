using CENS15_V2.Entities;

namespace CENS15_V2.Models
{
    public class LoginActivity
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Email { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public bool Success { get; set; }
        public string? Reason { get; set; }
        public string? Platform { get; set; }
        public string? DeviceInfo { get; set; }
        public string? AppVersion { get; set; }
        public string? IpAddress { get; set; }

        public User? User { get; set; }
    }
}
