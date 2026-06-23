namespace CENS15_V2.Models
{
    public class PasswordResetCode
    {
        public int Id { get; set; }
        public Guid AuthId { get; set; }
        public string CodeHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public int Attempts { get; set; }

        public Auth Auth { get; set; } = null!;
    }
}
