using CENS15_V2.Data;
using CENS15_V2.Entities;
using CENS15_V2.Helper;
using CENS15_V2.Models.DTOs.UsersDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CENS15_V2.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _context;
        private readonly PasswordHasher _hasher;

        public UserService(AppDbContext context, PasswordHasher hasher)
        {
            _context = context;
            _hasher = hasher;
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

        public Task<bool> UpdateAsync(Guid id, UpdateUserRequest request) => PreceptorWriteGuard.ExecuteAsync(_context, async () =>
        {
            var existing = await _context.Users.FindAsync(id);
            if (existing == null) return false;

            CENS15_V2.Models.Auth? auth = null;
            string? passwordHash = null;
            if (!string.IsNullOrEmpty(request.Password))
            {
                PasswordHasher.ValidatePassword(request.Password);
                auth = await _context.Auths.FindAsync(id);
                if (auth == null)
                    throw new InvalidOperationException("El usuario no tiene credenciales de acceso. No se cambio la contrasena.");
                passwordHash = _hasher.Hash(request.Password);
            }

            if (await _context.Preceptores.AnyAsync(p => p.UserId == id))
            {
                var roleName = await _context.Roles.Where(r => r.Id == request.RoleId).Select(r => r.Name).FirstOrDefaultAsync();
                if (!PreceptorService.IsPreceptorRole(roleName))
                    throw new InvalidOperationException("No se puede cambiar el rol de un usuario vinculado a un preceptor. Desvinculalo primero.");
            }

            existing.FirstName = request.FirstName;
            existing.LastName = request.LastName;
            existing.Image = request.Image;
            existing.RoleId = request.RoleId;
            if (auth != null)
            {
                auth.PasswordHash = passwordHash!;
                auth.MustChangePassword = false;
            }

            await _context.SaveChangesAsync();
            return true;
        });

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
                .Include(u => u.Preceptor)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return false;

            if (user.Docente != null)
            {
                user.Docente.UserId = null;
            }
            if (user.Preceptor != null)
            {
                user.Preceptor.UserId = null;
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
