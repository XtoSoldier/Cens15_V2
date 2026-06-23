using CENS15_V2.Data;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.LoginActivitiesDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class LoginActivityService : ILoginActivityService
    {
        private readonly AppDbContext _context;

        public LoginActivityService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<LoginActivityDto>> GetAllAsync()
        {
            return await _context.LoginActivities
                .AsNoTracking()
                .Include(a => a.User)
                .OrderByDescending(a => a.CreatedAt)
                .Take(500)
                .Select(a => new LoginActivityDto
                {
                    Id = a.Id,
                    CreatedAt = a.CreatedAt,
                    Email = a.Email,
                    UserId = a.UserId,
                    UserName = a.User != null ? (a.User.LastName + ", " + a.User.FirstName) : null,
                    Success = a.Success,
                    Reason = a.Reason,
                    Platform = a.Platform,
                    DeviceInfo = a.DeviceInfo,
                    AppVersion = a.AppVersion,
                    IpAddress = a.IpAddress
                })
                .ToListAsync();
        }

        public async Task RegisterAsync(CreateLoginActivityRequest request, string? ipAddress)
        {
            var email = request.Email.Trim();
            Guid? userId = request.UserId;

            if (!userId.HasValue && !string.IsNullOrWhiteSpace(email))
            {
                userId = await _context.Auths
                    .Where(a => a.Email.ToLower() == email.ToLower())
                    .Select(a => (Guid?)a.Id)
                    .FirstOrDefaultAsync();
            }

            var activity = new LoginActivity
            {
                CreatedAt = DateTime.UtcNow,
                Email = email,
                UserId = userId,
                Success = request.Success,
                Reason = Truncate(request.Reason, 500),
                Platform = Truncate(request.Platform, 80),
                DeviceInfo = Truncate(request.DeviceInfo, 500),
                AppVersion = Truncate(request.AppVersion, 80),
                IpAddress = Truncate(ipAddress, 80)
            };

            _context.LoginActivities.Add(activity);
            await _context.SaveChangesAsync();
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
