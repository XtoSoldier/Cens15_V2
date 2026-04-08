using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Models.DTOs.UsersDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _context;

        public UserService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<UserDTO>> GetAllAsync()
        {
            return await BuildUserQuery()
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<UserDTO?> GetByIdAsync(Guid id)
        {
            return await BuildUserQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<UserDTO> CreateAsync(CreateUserRequest request)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = request.FirstName,
                LastName = request.LastName,
                Image = request.Image,
                Status = request.Status,
                RoleId = request.RoleId
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return await BuildUserQuery()
                .AsNoTracking()
                .FirstAsync(u => u.Id == user.Id);
        }

        public async Task<bool> UpdateAsync(Guid id, UpdateUserRequest request)
        {
            var existing = await _context.Users.FindAsync(id);
            if (existing == null) return false;

            existing.FirstName = request.FirstName;
            existing.LastName = request.LastName;
            existing.Image = request.Image;
            existing.RoleId = request.RoleId;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateStatusAsync(Guid id, UpdateUserStatusRequest request)
        {
            var existing = await _context.Users.FindAsync(id);
            if (existing == null) return false;

            existing.Status = request.Status;
            await _context.SaveChangesAsync();
            return true;
        }


        public async Task<bool> UpdateEmailAsync(Guid id, UpdateUserEmailRequest request)
        {
            var auth = await _context.Auths.FindAsync(id);
            if (auth == null) return false;

            var normalizedEmail = request.Email.Trim();
            var emailExists = await _context.Auths.AnyAsync(a =>
                a.Id != id && a.Email.ToLower() == normalizedEmail.ToLower());

            if (emailExists)
            {
                throw new InvalidOperationException("El email ya existe");
            }

            auth.Email = normalizedEmail;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var user = await _context.Users
                .Include(u => u.Auth)
                    .ThenInclude(a => a.Token)
                .Include(u => u.Docente)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return false;

            if (user.Docente != null)
            {
                user.Docente.UserId = null;
            }

            if (user.Auth?.Token != null)
            {
                _context.Tokens.Remove(user.Auth.Token);
            }

            if (user.Auth != null)
            {
                _context.Auths.Remove(user.Auth);
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return true;
        }

        private IQueryable<UserDTO> BuildUserQuery()
        {
            return _context.Users
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
                });
        }
    }
}
