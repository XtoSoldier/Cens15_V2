namespace CENS15_V2.Models.DTOs.LoginActivitiesDTOs
{
    public class LoginActivityDto
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Email { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string? UserName { get; set; }
        public bool Success { get; set; }
        public string? Reason { get; set; }
        public string? Platform { get; set; }
        public string? DeviceInfo { get; set; }
        public string? AppVersion { get; set; }
        public string? IpAddress { get; set; }
    }
}
