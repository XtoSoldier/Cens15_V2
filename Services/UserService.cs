using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Services.Interfaces;
using CENS15_V2.Models.DTOs.UsersDTOs;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class UserService: IUserService
    {
        private readonly AppDbContext _context;

        public UserService(AppDbContext context)
        {
            _context = context;
        }
        public async Task<IEnumerable<UserDTO>> GetAllAsync()
        {
            return await _context.Users
                .AsNoTracking()
                .Select(u => new UserDTO
                {
                    Id = u.Id,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Image = u.Image,
                    Status = u.Status,
                    RoleId = u.RoleId,
                    Role = u.Role != null ? u.Role.Name : null,
                    Email = u.Auth != null ? u.Auth.Email : null
                })
                .ToListAsync();
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            return await _context.Users.FindAsync(id);
        }

        public async Task<User> CreateAsync(User user)
        {
            user.Id = Guid.NewGuid();

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return user;
        }

        public async Task<bool> UpdateAsync(Guid id, User user)
        {
            var existing = await _context.Users.FindAsync(id);
            if (existing == null) return false;

            existing.FirstName = user.FirstName;
            existing.LastName = user.LastName;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return false;

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}

